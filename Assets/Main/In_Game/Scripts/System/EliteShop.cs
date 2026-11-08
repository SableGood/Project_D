using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

// 엘리트 상점: 엘리트 웨이브를 클리어하면 재능 선택 대신 열림 (AugmentManager.OfferAfterWave가 부름)
//  - 진열: 새 포탑 해금 / 무기 강화 / 새 무기 를 섞어서 무작위 4칸 (구글 시트 '엘리트 상점' 탭의 결정)
//    · 새 포탑: 아직 잠긴 포탑 (처음에는 TR010·TR020만 열려 있음, RunProgress)
//    · 무기 강화: 보유 무기의 다음 단계. 기본 → 모던, 모던 → 3티어(어썰트·레인저·발리스타 중 하나를 사면서 고름)
//    · 새 무기: 아직 없는 계열의 기본형. 무기는 최대 2정 → 꽉 차 있으면 버릴 무기를 고르는 창 (취소 가능)
//    · 포탑 다음 단계(TR011 등)는 프리팹과 업그레이드 기능이 생기면 추가
//  - 가격 (v91): 새 포탑 150 / 새 무기 150 (배틀 200) / 무기 강화 → 모던 150, → 3티어 250. 직업(학생) 할인 반영
//    골드가 되는 만큼 여러 개 살 수 있음. 다시 뽑기: 무료 1회, 이후 30 G부터 할 때마다 +20
//  - 화면: 아래쪽 진열대 (위에 노란 차양 줄무늬, 골드색 강조). 재능 선택창(왼쪽 목록, 연두색)과 일부러 다르게
//  - 1~4 키 / 마우스 올리기 = 고르기, Enter / 버튼 = 사기, Esc = 나가기. 여는 동안 일시정지. F4 = 테스트로 열기
public class EliteShop : MonoBehaviour
{
    public static EliteShop Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;

    [Header("상점")]
    [Tooltip("진열 칸 수")]
    public int slotCount = 4;
    [Header("가격 (골드)")]
    public int priceNewTurret = 150;
    public int priceNewWeapon = 150;
    public int priceNewWeaponPremium = 200;   // 고급 무기 (배틀)
    public int priceUpgradeModern = 150;
    public int priceUpgradeTier3 = 250;
    public int rerollBaseCost = 30, rerollStep = 20;
    private int paidRerolls;
    [Tooltip("상점을 열 때마다 공짜로 다시 뽑을 수 있는 횟수")]
    public int freeRerolls = 1;

    [Header("테스트")]
    public bool debugKey = true; // F4

    public static EliteShop Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("EliteShop").AddComponent<EliteShop>();
    }

    private enum Kind { NewTurret, NewWeapon, WeaponUpgrade }
    private class Item
    {
        public Kind kind;
        public string turretCode;
        public int weaponFrom = -1;                         // 강화: 지금 무기 (무기 목록 번호)
        public readonly List<int> weapons = new List<int>(); // 새 무기 1개 / 강화 결과 1~3개
        public string category, title, desc, verb;
        public Sprite icon;
        public bool sold;
        public int price;
        // 화면
        public RectTransform crate;
        public Image frame, inner, tagBg, buyBg;
        public Text tag, cat, name, info, buyText;
        public GameObject buy;
    }

    private bool open, closing;
    private float openedAt, closeAt;
    private int selected, rerollsLeft;
    private readonly List<Item> items = new List<Item>();
    private float prevTimeScale = 1f;
    private CursorLockMode prevLock;
    private bool prevCursorVisible;

    // 화면 (기준 해상도 1920×1080)
    private const float TrayH = 712f, TrayBottom = 48f, DimAlpha = 0.35f;
    private Image dim;
    private RectTransform root, tray, shelf, dialog;
    private Text eyebrowText, goldText, emptyText, rerollText, noticeText;
    private RectTransform coin;
    private Image rerollFrame;
    private float noticeUntil;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (debugKey && PlayerWeaponManager.TestModeAllWeapons && !open && !AugmentManager.IsOpen && Input.GetKeyDown(KeyCode.F4)) Open("F4 테스트 상점");
        if (!open) return;
        HandleKeys();
        Animate();
        if (goldText != null) RefreshGold();
        if (noticeText.gameObject.activeSelf && Time.unscaledTime > noticeUntil) noticeText.gameObject.SetActive(false);
    }

    public IEnumerator OpenAndWait(string eyebrow)
    {
        Open(eyebrow);
        while (open) yield return null;
    }

    // ===== 열기·닫기 =====
    public void Open(string eyebrow)
    {
        if (open) return;
        open = true;
        closing = false;
        Sfx.UIOpen(); // v129 팝업 열림 소리
        prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        prevLock = Cursor.lockState; prevCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        EnsureEventSystem();

        eyebrowText.text = eyebrow;
        rerollsLeft = Mathf.Max(0, freeRerolls);
        paidRerolls = 0;
        CloseDialog();
        Restock();
        openedAt = Time.unscaledTime;
        root.gameObject.SetActive(true);
        Animate();
    }

    private void BeginClose()
    {
        if (closing) return;
        CloseDialog();
        closing = true;
        closeAt = Time.unscaledTime;
    }

    private void Close()
    {
        open = false;
        closing = false;
        root.gameObject.SetActive(false);
        AugmentManager.MarkClosed();
        Time.timeScale = prevTimeScale;
        Cursor.lockState = prevLock; Cursor.visible = prevCursorVisible;
    }

    private static float EaseOutCubic(float t) { t = 1f - t; return 1f - t * t * t; }
    private static float EaseInCubic(float t) => t * t * t;

    private void Animate()
    {
        float now = Time.unscaledTime;
        float hidden = -TrayH - 40f;
        if (!closing)
        {
            float u = EaseOutCubic(Mathf.Clamp01((now - openedAt) / 0.3f));
            tray.anchoredPosition = new Vector2(0f, Mathf.Lerp(hidden, TrayBottom, u));
            SetDim(DimAlpha * u);
            return;
        }
        float c = Mathf.Clamp01((now - closeAt) / 0.25f);
        tray.anchoredPosition = new Vector2(0f, Mathf.Lerp(TrayBottom, hidden, EaseInCubic(c)));
        SetDim(DimAlpha * (1f - c));
        if (c >= 1f) Close();
    }

    private void SetDim(float a) { Color c = dim.color; c.a = a; dim.color = c; }

    private void HandleKeys()
    {
        if (closing) return;
        if (dialog != null)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) CloseDialog();
            return;
        }
        for (int i = 0; i < items.Count && i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
            {
                if (selected == i) Buy(i); else Select(i);
                return;
            }
        if (Input.GetKeyDown(KeyCode.LeftArrow)) Select(Mathf.Max(0, selected - 1));
        else if (Input.GetKeyDown(KeyCode.RightArrow)) Select(Mathf.Min(items.Count - 1, selected + 1));
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Buy(selected);
        else if (Input.GetKeyDown(KeyCode.Escape)) BeginClose();
    }

    // ===== 진열 =====
    private static PlayerWeaponManager Weapons() => FindFirstObjectByType<PlayerWeaponManager>();
    private static TowerBuilder Builder() => FindFirstObjectByType<TowerBuilder>();

    private static string WName(PlayerWeaponData d) => d != null ? (d.weaponName ?? "").Trim() : "";
    private static string Family(string n) { int i = n.IndexOf('_'); return i < 0 ? n : n.Substring(0, i); }
    private static int Tier(string n) => n.IndexOf('_') < 0 ? 1 : (n.EndsWith("_Modern") || n.EndsWith("_Upgrade") ? 2 : 3);   // v121: Duty_Upgrade = 2단계
    private static string Display(PlayerWeaponData d)
    {
        if (d == null) return "?";
        return !string.IsNullOrEmpty(d.inGameName) ? d.inGameName.Trim() : WName(d);
    }

    private List<Item> BuildPool()
    {
        var pool = new List<Item>();

        // 새 포탑: 잠긴 포탑
        TowerBuilder tb = Builder();
        if (tb != null)
            for (int i = 0; i < tb.SlotCount; i++)
            {
                GameObject p = tb.GetTowerPrefab(i);
                if (p == null || RunProgress.IsTurretUnlocked(p.name)) continue;
                EntityData d = tb.GetTowerData(i);
                var it = new Item { kind = Kind.NewTurret, turretCode = p.name, category = "새 포탑", verb = "해금" };
                it.title = d != null && !string.IsNullOrEmpty(d.entityName) ? d.entityName : p.name;
                it.desc = d != null && !string.IsNullOrEmpty(d.abilityText) ? d.abilityText : $"{i + 1}번 칸 · 짓는 비용 {tb.GetTowerCost(i)} G";
                it.icon = Resources.Load<Sprite>($"Image/TurretIcons/{p.name}");
                it.price = Jobs.ShopPrice(priceNewTurret);
                pool.Add(it);
            }

        PlayerWeaponManager pw = Weapons();
        if (pw != null && pw.CatalogCount > 0 && Jobs.CanUseWeapons) // 대통령: 무기 상품 없음
        {
            var ownedFamilies = new HashSet<string>();
            foreach (int o in pw.Owned) ownedFamilies.Add(Family(WName(pw.DataAt(o))));

            // 무기 강화: 보유 무기마다 다음 단계
            foreach (int o in pw.Owned)
            {
                PlayerWeaponData d = pw.DataAt(o);
                string n = WName(d);
                if (n.Length == 0 || Tier(n) >= 3) continue;
                var it = new Item { kind = Kind.WeaponUpgrade, weaponFrom = o, verb = "강화" };
                for (int i = 0; i < pw.CatalogCount; i++)
                {
                    string ni = WName(pw.DataAt(i));
                    if (ni.Length > 0 && Family(ni) == Family(n) && Tier(ni) == Tier(n) + 1) it.weapons.Add(i);
                }
                if (it.weapons.Count == 0) continue;
                it.price = Jobs.ShopPrice(Tier(n) + 1 >= 3 ? priceUpgradeTier3 : priceUpgradeModern);
                if (it.weapons.Count == 1)
                {
                    PlayerWeaponData to = pw.DataAt(it.weapons[0]);
                    it.category = "무기 강화";
                    it.title = Display(to);
                    it.desc = $"보유한 {Display(d)}의 다음 단계";
                    it.icon = to != null ? to.weaponSprite : null;
                }
                else
                {
                    it.category = "무기 강화 · 3티어";
                    it.title = $"{Display(d)} 3티어";
                    it.desc = $"사면 {it.weapons.Count}갈래 중 하나를 골라요";
                    it.icon = d != null ? d.weaponSprite : null;
                }
                pool.Add(it);
            }

            // 새 무기: 아직 없는 계열의 기본형
            for (int i = 0; i < pw.CatalogCount; i++)
            {
                PlayerWeaponData d = pw.DataAt(i);
                string n = WName(d);
                if (n.Length == 0 || Tier(n) != 1 || ownedFamilies.Contains(Family(n))) continue;
                if (d.TalentOnly) continue;                                            // v113: 재능으로만 얻는 무기 (로켓포)
                if (d.ShopUnlock && !RunProgress.HasShopCategory(d.Category)) continue; // v113: 같은 분류 무기를 상점에서 얻어야 등장
                var it = new Item { kind = Kind.NewWeapon, category = d.ShopUnlock ? "새 무기 · 해금" : "새 무기", verb = "사기", title = Display(d), icon = d.weaponSprite };
                it.desc = !string.IsNullOrEmpty(d.role) ? d.role.Trim() : n;
                it.price = Jobs.ShopPrice(Family(n) == "Battle" || d.ShopUnlock ? priceNewWeaponPremium : priceNewWeapon);
                it.weapons.Add(i);
                pool.Add(it);
            }
        }
        return pool;
    }

    private void Restock()
    {
        List<Item> pool = BuildPool();
        for (int i = pool.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
        foreach (Item it in items) if (it.crate != null) Destroy(it.crate.gameObject);
        items.Clear();
        int n = Mathf.Min(Mathf.Max(1, slotCount), pool.Count);
        for (int i = 0; i < n; i++) { items.Add(pool[i]); MakeCrate(pool[i], i, n); }
        emptyText.gameObject.SetActive(items.Count == 0);
        RefreshReroll();
        Select(0);
    }

    private int RerollCost => rerollBaseCost + rerollStep * paidRerolls;

    private void Reroll()
    {
        if (closing || dialog != null) return;
        if (rerollsLeft > 0) rerollsLeft--;
        else
        {
            GoldManager gm = GoldManager.Instance;
            int cost = RerollCost;
            if (gm != null && !gm.TrySpend(cost)) { Notice($"골드가 모자라요 ({cost} G 필요)"); return; }
            paidRerolls++;
        }
        Restock();
    }

    private bool StillValid(Item it)
    {
        PlayerWeaponManager pw = Weapons();
        switch (it.kind)
        {
            case Kind.NewTurret: return !RunProgress.IsTurretUnlocked(it.turretCode);
            case Kind.WeaponUpgrade: return pw != null && pw.Owns(it.weaponFrom);
            default:
                if (pw == null || it.weapons.Count == 0) return false;
                string fam = Family(WName(pw.DataAt(it.weapons[0])));
                foreach (int o in pw.Owned) if (Family(WName(pw.DataAt(o))) == fam) return false;
                return true;
        }
    }

    // ===== 사기 =====
    private void Buy(int index)
    {
        if (closing || dialog != null || index < 0 || index >= items.Count) return;
        Select(index);
        Item it = items[index];
        if (it.sold || !StillValid(it)) return;
        GoldManager gm = GoldManager.Instance;
        if (gm != null && !gm.CanAfford(it.price)) { Notice($"골드가 모자라요 ({it.price} G 필요)"); gm.NotifyShort(it.price); return; }

        PlayerWeaponManager pw = Weapons();
        switch (it.kind)
        {
            case Kind.NewTurret:
                if (!Pay(it)) return;
                RunProgress.UnlockTurret(it.turretCode);
                Sold(it, $"'{it.title}' 해금 · 이제 지을 수 있어요");
                break;

            case Kind.NewWeapon:
                if (pw == null) return;
                if (pw.HasFreeSlot)
                {
                    if (!Pay(it)) return;
                    pw.GiveWeapon(it.weapons[0]);
                    Sold(it, $"'{it.title}' 획득");
                }
                else
                {
                    var names = new List<string>();
                    foreach (int o in pw.Owned) names.Add(Display(pw.DataAt(o)) + " 버리기");
                    ShowDialog("무기 교체", $"무기는 {PlayerWeaponManager.MaxOwned}정까지 들 수 있어요. '{it.title}' 대신 버릴 무기를 고르세요.", names, k =>
                    {
                        if (!Pay(it)) return;
                        pw.GiveWeapon(it.weapons[0], k);
                        Sold(it, $"'{it.title}' 획득");
                    });
                }
                break;

            case Kind.WeaponUpgrade:
                if (pw == null) return;
                int slot = -1;
                for (int i = 0; i < pw.Owned.Count; i++) if (pw.Owned[i] == it.weaponFrom) slot = i;
                if (slot < 0) return;
                if (it.weapons.Count == 1)
                {
                    if (!Pay(it)) return;
                    pw.UpgradeOwned(slot, it.weapons[0]);
                    Sold(it, $"'{it.title}'(으)로 강화");
                }
                else
                {
                    var opts = new List<PlayerWeaponData>();
                    foreach (int w in it.weapons) opts.Add(pw.DataAt(w));
                    int s = slot;
                    ShowWeaponChoice("3티어 업그레이드", $"{Display(pw.DataAt(it.weaponFrom))}을(를) 어느 쪽으로 강화할까요?  <size=20><color=#A8A8A0>숫자 키 · 클릭으로 고르기</color></size>", pw.DataAt(it.weaponFrom), opts, k =>
                    {
                        if (!Pay(it)) return;
                        pw.UpgradeOwned(s, it.weapons[k]);
                        Sold(it, $"'{Display(pw.DataAt(it.weapons[k]))}'(으)로 강화");
                    });
                }
                break;
        }
    }

    private bool Pay(Item it)
    {
        GoldManager gm = GoldManager.Instance;
        if (gm == null) return true;
        if (gm.TrySpend(it.price)) return true;
        Notice($"골드가 모자라요 ({it.price} G 필요)");
        return false;
    }

    private void Sold(Item it, string message)
    {
        it.sold = true;
        // v113: 상점에서 얻은 무기의 분류를 기록 → 같은 분류의 해금 무기가 이후 상점에 나옴
        if (it.kind != Kind.NewTurret && it.weapons.Count > 0)
        {
            PlayerWeaponManager pw = Weapons();
            PlayerWeaponData got = pw != null ? pw.DataAt(it.weapons[0]) : null;
            if (got != null) RunProgress.AddShopCategory(got.Category);
        }
        Notice(message);
        for (int i = 0; i < items.Count; i++) StyleCrate(items[i], i == selected);
    }

    private void Notice(string message)
    {
        noticeText.text = message;
        noticeText.gameObject.SetActive(true);
        noticeUntil = Time.unscaledTime + 2.5f;
    }

    // ===== 화면 =====
    private static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out Color c); return c; }
    private static readonly Color PanelBg = Hex("#3A3A3A");
    private static readonly Color Line = Hex("#4C4C4A");
    private static readonly Color LineStrong = Hex("#6A6A66");
    private static readonly Color Gold = Hex("#E8C27A");
    private static readonly Color GoldDark = Hex("#B08A3E");
    private static readonly Color Pick = Hex("#42403A");
    private static readonly Color Dark = Hex("#232323");
    private static readonly Color TextMain = Hex("#EDEDE6");
    private static readonly Color TextSub = Hex("#A8A8A0");
    private static readonly Color Muted = Hex("#6A6A66");
    private static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

    private void Select(int i)
    {
        if (items.Count == 0) return;
        selected = Mathf.Clamp(i, 0, items.Count - 1);
        for (int k = 0; k < items.Count; k++) StyleCrate(items[k], k == selected);
    }

    private void StyleCrate(Item it, bool on)
    {
        bool can = !it.sold && StillValid(it);
        it.frame.color = on && can ? Gold : Clear;
        it.inner.color = on && can ? Pick : Clear;
        it.cat.color = !can ? Muted : on ? Gold : TextSub;
        it.name.color = can ? TextMain : Muted;
        it.info.color = can ? TextSub : Muted;
        it.tagBg.color = can ? Gold : Hex("#555552");
        it.tag.text = it.sold ? "구매함" : can ? $"{it.price} G" : "살 수 없음";
        it.buy.SetActive(on && can);
        it.buyText.text = $"{it.price} G 내고 {it.verb}";
    }

    private void RefreshGold()
    {
        int g = GoldManager.Instance != null ? GoldManager.Instance.Gold : 0;
        string s = $"{g} G";
        if (goldText.text != s)
        {
            goldText.text = s;
            coin.anchoredPosition = new Vector2(-48f - goldText.preferredWidth - 16f, coin.anchoredPosition.y);
        }
    }

    private void RefreshReroll()
    {
        bool free = rerollsLeft > 0;
        int g = GoldManager.Instance != null ? GoldManager.Instance.Gold : int.MaxValue;
        bool can = free || g >= RerollCost;
        rerollText.text = free ? $"다시 뽑기 (무료 {rerollsLeft}회)" : $"다시 뽑기 ({RerollCost} G)";
        rerollText.color = can ? TextMain : Muted;
        rerollFrame.color = can ? LineStrong : Line;
    }

    private void BuildUI()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 490; // 재능 알림(500)보다 아래
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        root = NewRect("EliteShop", transform);
        Stretch(root);
        dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0.08f, 0.08f, 0.08f, DimAlpha);

        // 진열대 판 (화면 아래, 좌우 여백 96)
        tray = NewRect("Tray", root);
        tray.anchorMin = new Vector2(0f, 0f); tray.anchorMax = new Vector2(1f, 0f);
        tray.pivot = new Vector2(0.5f, 0f);
        tray.sizeDelta = new Vector2(-192f, TrayH);
        tray.anchoredPosition = new Vector2(0f, TrayBottom);
        RectTransform drop = NewRect("Shadow", tray);
        Stretch(drop); drop.offsetMin = new Vector2(0f, -16f); drop.offsetMax = new Vector2(0f, -16f);
        drop.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
        RectTransform body = NewRect("Body", tray);
        Stretch(body);
        body.gameObject.AddComponent<Image>().color = PanelBg;

        // 차양 줄무늬 (노랑·어두운 칸 번갈아)
        const int stripes = 36;
        for (int k = 0; k < stripes; k++)
        {
            RectTransform s = NewRect("Awning", tray);
            s.anchorMin = new Vector2((float)k / stripes, 1f); s.anchorMax = new Vector2((float)(k + 1) / stripes, 1f);
            s.pivot = new Vector2(0.5f, 1f);
            s.sizeDelta = new Vector2(0f, 24f); s.anchoredPosition = Vector2.zero;
            s.gameObject.AddComponent<Image>().color = k % 2 == 0 ? Gold : PanelBg;
        }
        RectTransform awningLine = NewRect("AwningLine", tray);
        awningLine.anchorMin = new Vector2(0f, 1f); awningLine.anchorMax = new Vector2(1f, 1f);
        awningLine.pivot = new Vector2(0.5f, 1f);
        awningLine.sizeDelta = new Vector2(0f, 4f); awningLine.anchoredPosition = new Vector2(0f, -24f);
        awningLine.gameObject.AddComponent<Image>().color = GoldDark;

        // 제목·골드
        eyebrowText = NewText(tray, "Eyebrow", 24, FontStyle.Normal, TextAnchor.MiddleLeft, Gold);
        TL(eyebrowText.rectTransform, 48f, 60f, 1200f, 36f);
        Text title = NewText(tray, "Title", 48, FontStyle.Normal, TextAnchor.MiddleLeft, TextMain);
        TL(title.rectTransform, 48f, 96f, 800f, 64f);
        title.text = "엘리트 상점";

        goldText = NewText(tray, "Gold", 48, FontStyle.Bold, TextAnchor.MiddleRight, Gold);
        goldText.rectTransform.anchorMin = goldText.rectTransform.anchorMax = new Vector2(1f, 1f);
        goldText.rectTransform.pivot = new Vector2(1f, 1f);
        goldText.rectTransform.sizeDelta = new Vector2(480f, 64f);
        goldText.rectTransform.anchoredPosition = new Vector2(-48f, -96f);
        goldText.horizontalOverflow = HorizontalWrapMode.Overflow;
        coin = NewRect("Coin", tray);
        coin.anchorMin = coin.anchorMax = new Vector2(1f, 1f);
        coin.pivot = new Vector2(1f, 0.5f);
        coin.sizeDelta = new Vector2(36f, 36f);
        coin.anchoredPosition = new Vector2(-200f, -128f);
        coin.gameObject.AddComponent<Image>().color = Gold;
        RectTransform coinShade = NewRect("Shade", coin);
        coinShade.anchorMin = new Vector2(0f, 0f); coinShade.anchorMax = new Vector2(1f, 0f);
        coinShade.pivot = new Vector2(0.5f, 0f); coinShade.sizeDelta = new Vector2(0f, 6f); coinShade.anchoredPosition = Vector2.zero;
        coinShade.gameObject.AddComponent<Image>().color = GoldDark;

        // 진열 칸 자리
        shelf = NewRect("Shelf", tray);
        shelf.anchorMin = new Vector2(0f, 1f); shelf.anchorMax = new Vector2(1f, 1f);
        shelf.pivot = new Vector2(0.5f, 1f);
        shelf.sizeDelta = new Vector2(-96f, 404f);
        shelf.anchoredPosition = new Vector2(0f, -188f);
        emptyText = NewText(shelf, "Empty", 24, FontStyle.Normal, TextAnchor.MiddleCenter, TextSub);
        Stretch(emptyText.rectTransform);
        emptyText.text = "지금 진열할 상품이 없어요";
        emptyText.gameObject.SetActive(false);

        // 아래 줄: 안내 · 다시 뽑기 · 나가기
        Text foot = NewText(tray, "Foot", 24, FontStyle.Normal, TextAnchor.MiddleLeft, TextSub);
        BL(foot.rectTransform, 48f, 40f, 900f, 40f);
        foot.text = "남은 골드는 그대로 남아요. 포탑을 짓는 데도 써요.";

        RectTransform exit = MakeButton(tray, "Exit", "나가기  Esc", 200f, out _, out _);
        exit.anchorMin = exit.anchorMax = new Vector2(1f, 0f); exit.pivot = new Vector2(1f, 0f);
        exit.anchoredPosition = new Vector2(-48f, 36f);
        exit.GetComponent<Button>().onClick.AddListener(BeginClose);
        RectTransform reroll = MakeButton(tray, "Reroll", "다시 뽑기", 320f, out rerollFrame, out rerollText);
        reroll.anchorMin = reroll.anchorMax = new Vector2(1f, 0f); reroll.pivot = new Vector2(1f, 0f);
        reroll.anchoredPosition = new Vector2(-272f, 36f);
        reroll.GetComponent<Button>().onClick.AddListener(Reroll);

        // 알림 (구매·골드 부족)
        noticeText = NewText(root, "Notice", 30, FontStyle.Bold, TextAnchor.MiddleCenter, Gold);
        noticeText.rectTransform.anchorMin = noticeText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        noticeText.rectTransform.pivot = new Vector2(0.5f, 1f);
        noticeText.rectTransform.sizeDelta = new Vector2(1400f, 60f);
        noticeText.rectTransform.anchoredPosition = new Vector2(0f, -150f);
        var outline = noticeText.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(2f, -2f);
        noticeText.gameObject.SetActive(false);

        root.gameObject.SetActive(false);
    }

    // 테두리 버튼 (폭 w, 높이 48)
    private RectTransform MakeButton(Transform parent, string name, string label, float w, out Image frame, out Text text)
    {
        RectTransform r = NewRect(name, parent);
        r.sizeDelta = new Vector2(w, 48f);
        frame = r.gameObject.AddComponent<Image>();
        frame.color = LineStrong;
        Inset(NewRect("Inner", r), 4f).gameObject.AddComponent<Image>().color = PanelBg;
        var b = r.gameObject.AddComponent<Button>();
        b.targetGraphic = frame;
        text = NewText(r, "Label", 24, FontStyle.Normal, TextAnchor.MiddleCenter, TextMain);
        Stretch(text.rectTransform);
        text.text = label;
        return r;
    }

    // 진열 칸 하나: 종류 / 그림(+가격표) / 이름 / 설명 / (고른 칸만) 사기 버튼
    private void MakeCrate(Item it, int index, int count)
    {
        float gap = 32f;
        RectTransform c = NewRect("Crate_" + index, shelf);
        c.anchorMin = new Vector2((float)index / count, 0f); c.anchorMax = new Vector2((float)(index + 1) / count, 1f);
        c.offsetMin = new Vector2(index == 0 ? 0f : gap * 0.5f, 0f);
        c.offsetMax = new Vector2(index == count - 1 ? 0f : -gap * 0.5f, 0f);
        it.crate = c;
        it.frame = c.gameObject.AddComponent<Image>();
        it.inner = Inset(NewRect("Inner", c), 4f).gameObject.AddComponent<Image>();
        it.inner.raycastTarget = false;
        var hover = c.gameObject.AddComponent<GameHUDHoverRelay>();
        hover.onEnter = () => { if (!closing && dialog == null) Select(index); };
        var click = c.gameObject.AddComponent<Button>();
        click.transition = Selectable.Transition.None;
        click.onClick.AddListener(() => Select(index));

        it.cat = NewText(c, "Category", 24, FontStyle.Normal, TextAnchor.MiddleLeft, TextSub);
        TopRow(it.cat.rectTransform, 12f, 36f);
        it.cat.text = it.category;

        RectTransform box = NewRect("Picture", c);
        TopRow(box, 56f, 168f);
        box.gameObject.AddComponent<Image>().color = Line;
        Inset(NewRect("Inner", box), 4f).gameObject.AddComponent<Image>().color = Hex("#2C2C2C");
        Image icon = Inset(NewRect("Icon", box), 16f).gameObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        if (it.icon != null) icon.sprite = it.icon;
        else
        {
            icon.color = Clear;
            Text ph = NewText(box, "NoPicture", 12, FontStyle.Normal, TextAnchor.MiddleCenter, Muted);
            Stretch(ph.rectTransform);
            ph.text = "그림 없음";
        }

        // 가격표 (그림 오른쪽 위에 걸림)
        RectTransform tag = NewRect("PriceTag", c);
        tag.anchorMin = tag.anchorMax = new Vector2(1f, 1f);
        tag.pivot = new Vector2(1f, 1f);
        tag.sizeDelta = new Vector2(132f, 40f);
        tag.anchoredPosition = new Vector2(4f, -68f);
        it.tagBg = tag.gameObject.AddComponent<Image>();
        it.tagBg.raycastTarget = false;
        it.tag = NewText(tag, "Text", 24, FontStyle.Bold, TextAnchor.MiddleCenter, Dark);
        Stretch(it.tag.rectTransform);

        it.name = NewText(c, "Name", 36, FontStyle.Bold, TextAnchor.MiddleLeft, TextMain);
        TopRow(it.name.rectTransform, 236f, 48f);
        it.name.horizontalOverflow = HorizontalWrapMode.Overflow;
        it.name.text = it.title;
        it.info = NewText(c, "Desc", 24, FontStyle.Normal, TextAnchor.UpperLeft, TextSub);
        TopRow(it.info.rectTransform, 292f, 64f);
        it.info.verticalOverflow = VerticalWrapMode.Truncate;
        it.info.text = it.desc;

        RectTransform buy = NewRect("Buy", c);
        buy.anchorMin = new Vector2(0f, 0f); buy.anchorMax = new Vector2(1f, 0f);
        buy.pivot = new Vector2(0.5f, 0f);
        buy.sizeDelta = new Vector2(-24f, 52f);
        buy.anchoredPosition = new Vector2(0f, 12f);
        RectTransform buyShade = NewRect("Shade", buy);
        Stretch(buyShade); buyShade.offsetMin = new Vector2(0f, -4f); buyShade.offsetMax = new Vector2(0f, -4f);
        buyShade.gameObject.AddComponent<Image>().color = GoldDark;
        it.buyBg = NewRect("Face", buy).gameObject.AddComponent<Image>();
        Stretch((RectTransform)it.buyBg.transform);
        it.buyBg.color = Gold;
        var buyButton = buy.gameObject.AddComponent<Button>();
        buyButton.targetGraphic = it.buyBg;
        buyButton.onClick.AddListener(() => Buy(index));
        it.buyText = NewText(buy, "Label", 24, FontStyle.Bold, TextAnchor.MiddleCenter, Dark);
        Stretch(it.buyText.rectTransform);
        it.buy = buy.gameObject;
    }

    // ===== 고르는 창 (무기 교체 / 3티어) =====
    private void ShowDialog(string title, string message, List<string> options, System.Action<int> onPick)
    {
        CloseDialog();
        Sfx.UIOpen();
        dialog = NewRect("Dialog", root);
        Stretch(dialog);
        dialog.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 0.6f);

        float h = 200f + options.Count * 76f + 76f;
        RectTransform box = NewRect("Box", dialog);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(760f, h);
        box.gameObject.AddComponent<Image>().color = Gold;
        Inset(NewRect("Inner", box), 4f).gameObject.AddComponent<Image>().color = PanelBg;

        Text t = NewText(box, "Title", 36, FontStyle.Bold, TextAnchor.MiddleLeft, TextMain);
        TL(t.rectTransform, 48f, 40f, 664f, 48f);
        t.text = title;
        Text m = NewText(box, "Message", 24, FontStyle.Normal, TextAnchor.UpperLeft, TextSub);
        TL(m.rectTransform, 48f, 100f, 664f, 72f);
        m.text = message;

        for (int i = 0; i < options.Count; i++)
        {
            int k = i;
            RectTransform b = MakeButton(box, "Option_" + i, $"{i + 1}  {options[i]}", 664f, out Image f, out Text label);
            b.anchorMin = b.anchorMax = new Vector2(0f, 1f); b.pivot = new Vector2(0f, 1f);
            b.sizeDelta = new Vector2(664f, 64f);
            b.anchoredPosition = new Vector2(48f, -(196f + i * 76f));
            f.color = Gold;
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.offsetMin = new Vector2(24f, 0f);
            b.GetComponent<Button>().onClick.AddListener(() => { CloseDialog(); onPick(k); });
        }
        RectTransform cancel = MakeButton(box, "Cancel", "취소  Esc", 200f, out _, out _);
        cancel.anchorMin = cancel.anchorMax = new Vector2(1f, 0f); cancel.pivot = new Vector2(1f, 0f);
        cancel.anchoredPosition = new Vector2(-48f, 32f);
        cancel.GetComponent<Button>().onClick.AddListener(CloseDialog);
        dialogPick = onPick;
        dialogCount = options.Count;
        dialogFrame = Time.frameCount;
    }

    private System.Action<int> dialogPick;
    private int dialogCount, dialogFrame;

    // ===== 3티어 고르는 창 (v106): 갈래마다 그림 · 역할 · 설명 · 지금 무기와 비교한 능력치 =====
    private void ShowWeaponChoice(string title, string message, PlayerWeaponData from, List<PlayerWeaponData> options, System.Action<int> onPick)
    {
        CloseDialog();
        Sfx.UIOpen();
        dialog = NewRect("Dialog", root);
        Stretch(dialog);
        dialog.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.05f, 0.7f);

        const float cardW = 400f, cardH = 560f, gap = 28f, pad = 48f;
        float boxW = pad * 2f + options.Count * cardW + (options.Count - 1) * gap;
        float boxH = 150f + cardH + 110f;
        RectTransform box = NewRect("Box", dialog);
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.sizeDelta = new Vector2(boxW, boxH);
        box.gameObject.AddComponent<Image>().color = Gold;
        Inset(NewRect("Inner", box), 4f).gameObject.AddComponent<Image>().color = PanelBg;

        Text t = NewText(box, "Title", 36, FontStyle.Bold, TextAnchor.MiddleLeft, TextMain);
        TL(t.rectTransform, pad, 36f, boxW - pad * 2f, 48f);
        t.text = title;
        Text m = NewText(box, "Message", 24, FontStyle.Normal, TextAnchor.UpperLeft, TextSub);
        m.supportRichText = true;
        TL(m.rectTransform, pad, 94f, boxW - pad * 2f, 40f);
        m.text = message;

        for (int i = 0; i < options.Count; i++)
        {
            int k = i;
            PlayerWeaponData d = options[i];
            RectTransform c = NewRect("Card_" + i, box);
            TL(c, pad + i * (cardW + gap), 150f, cardW, cardH);
            Image frame = c.gameObject.AddComponent<Image>();
            frame.color = LineStrong;
            Inset(NewRect("Inner", c), 4f).gameObject.AddComponent<Image>().color = Pick;
            Button btn = c.gameObject.AddComponent<Button>();
            btn.targetGraphic = frame;
            ColorBlock cb = btn.colors; cb.highlightedColor = Gold; cb.pressedColor = GoldDark; cb.selectedColor = LineStrong; btn.colors = cb;
            btn.onClick.AddListener(() => { CloseDialog(); onPick(k); });

            Text num = NewText(c, "Key", 22, FontStyle.Bold, TextAnchor.MiddleLeft, Gold);
            TL(num.rectTransform, 20f, 14f, 60f, 30f);
            num.text = (i + 1).ToString();

            // 그림
            RectTransform pic = NewRect("Picture", c);
            TL(pic, 30f, 46f, cardW - 60f, 120f);
            if (d != null && d.weaponSprite != null)
            {
                Image img = pic.gameObject.AddComponent<Image>();
                img.sprite = d.weaponSprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            Text name = NewText(c, "Name", 28, FontStyle.Bold, TextAnchor.MiddleCenter, TextMain);
            TL(name.rectTransform, 16f, 176f, cardW - 32f, 36f);
            name.text = Display(d);
            Text role = NewText(c, "Role", 20, FontStyle.Normal, TextAnchor.MiddleCenter, Gold);
            TL(role.rectTransform, 16f, 212f, cardW - 32f, 28f);
            role.text = d != null && !string.IsNullOrEmpty(d.role) ? d.role.Trim() : "";

            Text desc = NewText(c, "Desc", 19, FontStyle.Normal, TextAnchor.UpperLeft, TextSub);
            TL(desc.rectTransform, 24f, 248f, cardW - 48f, 80f);
            desc.text = d != null && !string.IsNullOrEmpty(d.description) ? d.description.Trim() : "";

            Text stats = NewText(c, "Stats", 20, FontStyle.Normal, TextAnchor.UpperLeft, TextMain);
            stats.supportRichText = true;
            stats.lineSpacing = 1.1f;
            TL(stats.rectTransform, 24f, 336f, cardW - 48f, cardH - 352f);
            stats.text = StatLines(from, d);
        }

        RectTransform cancel = MakeButton(box, "Cancel", "취소  Esc", 200f, out _, out _);
        cancel.anchorMin = cancel.anchorMax = new Vector2(1f, 0f); cancel.pivot = new Vector2(1f, 0f);
        cancel.anchoredPosition = new Vector2(-pad, 32f);
        cancel.GetComponent<Button>().onClick.AddListener(CloseDialog);
        dialogPick = onPick;
        dialogCount = options.Count;
        dialogFrame = Time.frameCount;
    }

    // 능력치 줄: 값 + 지금 무기와의 차이 (좋아지면 초록, 나빠지면 빨강)
    private static string StatLines(PlayerWeaponData a, PlayerWeaponData b)
    {
        if (b == null) return "";
        var sb = new System.Text.StringBuilder();
        void Row(string label, float va, float vb, string fmt, bool higherBetter, string unit = "")
        {
            string v = vb.ToString(fmt) + unit;
            string diff = "";
            if (a != null && Mathf.Abs(vb - va) > 0.0001f)
            {
                bool better = higherBetter ? vb > va : vb < va;
                string sign = vb > va ? "+" : "−";
                diff = $"  <color={(better ? "#9BE08A" : "#F08A7A")}>{sign}{Mathf.Abs(vb - va).ToString(fmt)}{unit}</color>";
            }
            sb.Append($"<color=#A8A8A0>{label}</color>  {v}{diff}\n");
        }
        int pa = a != null ? Mathf.Max(1, a.projectileCount) : 1, pb = Mathf.Max(1, b.projectileCount);
        Row("공격력", a != null ? a.damage : 0f, b.damage, "0.#", true);
        if (pa > 1 || pb > 1) Row("한 번에", pa, pb, "0", true, "발");
        Row("공격속도", a != null ? a.fireRate : 0f, b.fireRate, "0.##", true, "/초");
        if (b.UsesAmmo) Row("장탄수", a != null ? a.maxAmmo : 0f, b.maxAmmo, "0", true);
        if (b.UsesAmmo) Row("재장전", a != null ? a.reloadTime : 0f, b.reloadTime, "0.##", false, "초");
        Row("사거리", a != null ? a.range : 0f, b.range, "0.#", true);
        Row("탄퍼짐", a != null ? a.spread : 0f, b.spread, "0.#", false, "°");
        if (b.armorPenetration > 0f || (a != null && a.armorPenetration > 0f)) Row("방어 무시", a != null ? a.armorPenetration : 0f, b.armorPenetration, "0.#", true);
        if (b.knockback > 0f || (a != null && a.knockback > 0f)) Row("넉백", a != null ? a.knockback : 0f, b.knockback, "0.##", true);
        if (b.canPierce) sb.Append("<color=#E8C27A>관통</color>\n");
        if (!string.IsNullOrEmpty(b.fireMechanism)) sb.Append($"<color=#A8A8A0>{(b.IsAuto ? "연사" : "단발")}</color>");
        return sb.ToString();
    }

    void LateUpdate()
    {
        // 고르는 창: 숫자 키로도 고르기
        if (!open || dialog == null || dialogPick == null || Time.frameCount == dialogFrame) return;
        for (int i = 0; i < dialogCount && i < 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
            {
                System.Action<int> pick = dialogPick;
                CloseDialog();
                pick(i);
                return;
            }
    }

    private void CloseDialog()
    {
        if (dialog != null) Destroy(dialog.gameObject);
        dialog = null;
        dialogPick = null;
        dialogCount = 0;
    }

    // ===== 배치 도우미 =====
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    private static RectTransform Inset(RectTransform r, float d)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(d, d); r.offsetMax = new Vector2(-d, -d);
        return r;
    }

    private static void TL(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.sizeDelta = new Vector2(w, h);
        r.anchoredPosition = new Vector2(x, -y);
    }

    private static void BL(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 0f);
        r.pivot = new Vector2(0f, 0f);
        r.sizeDelta = new Vector2(w, h);
        r.anchoredPosition = new Vector2(x, y);
    }

    // 칸 안에서 위쪽 top만큼 내려온 가로 한 줄 (좌우 여백 16)
    private static void TopRow(RectTransform r, float top, float height)
    {
        r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.sizeDelta = new Vector2(-32f, height);
        r.anchoredPosition = new Vector2(0f, -top);
    }

    private static Text NewText(Transform parent, string name, int size, FontStyle style, TextAnchor anchor, Color color)
    {
        RectTransform r = NewRect(name, parent);
        var t = r.gameObject.AddComponent<Text>();
        t.fontSize = size;
        UIFont.Apply(t, style);
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }
}
