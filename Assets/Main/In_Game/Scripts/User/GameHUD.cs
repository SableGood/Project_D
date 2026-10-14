using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// 인게임 하단 통합 HUD (플레이어 상태창)
//
//   (배경 판은 반투명 50%, panelAlpha로 조절)
//   [체력 숫자] [========= 체력바 =========]                [골드]
//   Shift  Q   E   R  Passive   [ 초상화 ]   1   2   3   4   5          [ 무기 카드 ]
//   [ ] [ ] [ ] [ ] [ ]                       [포탑 슬롯 x5]           (오른쪽 아래)
//
//  - 플레이어가 생기면 자동으로 연결됩니다. (TowerBuilder / PlayerWeaponManager가 Ensure() 호출)
//  - 씬에 직접 만든 상태창(PanelDesign)은 실행 중에만 숨깁니다. (씬 파일은 그대로, hideScenePanel 끄면 다시 보임)
//  - 스킬 칸(Shift/Q/E/R/Passive)은 아직 기능이 없는 자리입니다. SetSkillIcon / SetSkillCooldown 으로 나중에 연결
//  - 포탑 썸네일: Resources/Image/TurretIcons/{포탑 프리팹 이름}.png  ([Tools/포탑/1. 포탑 썸네일 생성])
public class GameHUD : MonoBehaviour
{
    public static GameHUD Instance { get; private set; }

    [Header("기존 씬 UI")]
    [Tooltip("씬의 기존 하단 상태창을 실행 중에 숨김 (씬 파일은 수정하지 않음)")]
    public bool hideScenePanel = true;
    public string scenePanelName = "PanelDesign";

    [Header("배경 판")]
    [Tooltip("하단 패널·무기 카드 뒤 배경의 진하기 (0 = 투명, 1 = 검정)")]
    [Range(0f, 1f)] public float panelAlpha = 0.5f;

    // ── 색상 (포탑 슬롯 디자인 기준으로 통일) ──
    private static readonly Color SlotBg = new Color(0.12f, 0.12f, 0.14f, 0.95f);
    private static readonly Color FrameNormal = new Color(1f, 1f, 1f, 0.18f);
    private static readonly Color Accent = new Color(1f, 0.85f, 0.25f, 1f);
    private static readonly Color CostOk = new Color(1f, 0.88f, 0.4f, 1f);
    private static readonly Color Warn = new Color(1f, 0.4f, 0.4f, 1f);
    private static readonly Color SubText = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color HpColor = new Color(0.55f, 0.85f, 0.35f, 1f);
    private static readonly Color HpTrailColor = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color TrackColor = new Color(0f, 0f, 0f, 0.6f);

    // ── 크기 (1920x1080 기준) ──
    private const float Slot = 80f, SlotGap = 10f, Portrait = 112f, GroupGap = 24f, Pad = 16f;
    private static readonly string[] SkillKeys = { "Shift", "Q", "E", "R", "Passive" };

    private Font font;
    private RectTransform root;
    private RectTransform towerGroup;
    private RectTransform weaponCard;

    // 연결된 플레이어
    private Transform boundPlayer;
    private Health playerHealth;
    private TowerBuilder builder;
    private PlayerWeaponManager weaponManager;

    // 체력
    private Text hpText;
    private RectTransform hpFill, hpTrail;
    private Image hpFillImage;
    private float trailRatio = 1f;

    // 골드 / 알림
    private Text goldText;
    private RectTransform goldRect;
    private Vector2 goldBasePos;
    private float shakeUntil;
    private Text infoText;
    private float noticeUntil;
    private string hoverInfo;

    // 초상화
    private Image portraitImage;

    // 스킬
    private Image[] skillIcons = new Image[5];
    private RectTransform[] skillCooldowns = new RectTransform[5];

    // 포탑
    private Image[] towerFrames;
    private Image[] towerIcons;
    private Text[] towerCosts;
    private Text[] towerKeys;
    private bool[] towerHasIcon;
    private int lastGold = int.MinValue, lastSelected = int.MinValue;

    // 무기
    private Text weaponNameText, weaponIndexText, ammoText, weaponHintText;
    private Image weaponIcon;
    private GameObject reloadBarRoot;
    private RectTransform reloadFill;

    public static void Ensure()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("GameHUD");
        Instance = go.AddComponent<GameHUD>();
        Instance.Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        GoldManager gm = GoldManager.Instance;
        if (gm != null) { gm.OnGoldShort -= HandleGoldShort; gm.OnGoldChanged -= HandleGoldChanged; }
    }

    // ==================================================================
    // 갱신
    // ==================================================================
    void Update()
    {
        Transform player = GameManager.Instance != null ? GameManager.Instance.playerTransform : null;
        if (player != boundPlayer) Bind(player);

        bool show = boundPlayer != null;
        if (root.gameObject.activeSelf != show) { root.gameObject.SetActive(show); weaponCard.gameObject.SetActive(show); }
        if (!show) return;

        UpdateHealth();
        UpdateTowers();
        UpdateWeapon();
        UpdateGoldEffects();
    }

    private void Bind(Transform player)
    {
        boundPlayer = player;
        playerHealth = player != null ? player.GetComponent<Health>() : null;
        builder = player != null ? player.GetComponent<TowerBuilder>() : null;
        weaponManager = player != null ? player.GetComponent<PlayerWeaponManager>() : null;
        trailRatio = 1f;
        lastGold = lastSelected = int.MinValue;

        // 초상화: 플레이어 일러스트 (Billboard가 붙은 스프라이트)
        Sprite portrait = null;
        if (player != null)
        {
            Billboard board = player.GetComponentInChildren<Billboard>(true);
            SpriteRenderer sr = board != null ? board.GetComponent<SpriteRenderer>() : null;
            if (sr != null) portrait = sr.sprite;
        }
        portraitImage.sprite = portrait;
        portraitImage.color = portrait != null ? Color.white : new Color(1f, 1f, 1f, 0f);

        BuildTowerSlots();

        // 기존 씬 상태창 숨김
        if (hideScenePanel && !string.IsNullOrEmpty(scenePanelName))
        {
            GameObject legacy = GameObject.Find(scenePanelName);
            if (legacy != null) legacy.SetActive(false);
        }
    }

    private void UpdateHealth()
    {
        if (playerHealth == null) return;
        float max = Mathf.Max(1f, playerHealth.maxHealth);
        float cur = Mathf.Max(0f, playerHealth.CurrentHealth);
        float ratio = Mathf.Clamp01(cur / max);

        hpText.text = Mathf.CeilToInt(cur).ToString();
        hpFill.anchorMax = new Vector2(ratio, 1f);
        hpFillImage.color = ratio <= 0.3f ? Warn : HpColor;

        // 깎인 만큼 흰 막대가 천천히 따라 줄어듦
        trailRatio = ratio > trailRatio ? ratio : Mathf.MoveTowards(trailRatio, ratio, Time.deltaTime * 0.6f);
        hpTrail.anchorMax = new Vector2(trailRatio, 1f);
    }

    private void UpdateTowers()
    {
        if (builder == null || towerFrames == null) { infoText.text = NoticeActive() ? infoText.text : ""; return; }

        GoldManager gm = GoldManager.Instance;
        int gold = gm != null ? gm.Gold : 0;
        int selected = builder.SelectedSlot;

        if (gold != lastGold || selected != lastSelected)
        {
            lastGold = gold;
            lastSelected = selected;
            goldText.text = $"골드  <color=#FFE066>{gold}</color>";
            for (int i = 0; i < towerFrames.Length; i++)
            {
                bool affordable = gold >= builder.GetTowerCost(i);
                towerFrames[i].color = i == selected ? Accent : FrameNormal;
                towerKeys[i].color = i == selected ? Accent : Color.white;
                if (towerHasIcon[i]) towerIcons[i].color = affordable ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
                towerCosts[i].color = affordable ? CostOk : Warn;
            }
        }

        // 안내 문구: 골드 부족 알림 > 마우스를 올린 슬롯 > 선택한 포탑
        if (NoticeActive()) return;
        infoText.color = Color.white;
        if (!string.IsNullOrEmpty(hoverInfo)) infoText.text = hoverInfo;
        else if (selected >= 0) infoText.text = $"{TowerLabel(selected)}   <color=#AAAAAA>좌클릭 설치 · 같은 번호 다시 누르면 취소</color>";
        else infoText.text = "";
    }

    private void UpdateWeapon()
    {
        Weapon weapon = weaponManager != null ? weaponManager.CurrentWeapon : null;
        PlayerWeaponData data = weapon != null ? weapon.playerWepData : null;
        if (data == null)
        {
            weaponNameText.text = "무기 없음";
            weaponIndexText.text = "";
            ammoText.text = "";
            weaponHintText.text = "";
            weaponIcon.color = new Color(1f, 1f, 1f, 0f);
            reloadBarRoot.SetActive(false);
            return;
        }

        weaponNameText.text = string.IsNullOrEmpty(data.inGameName) ? data.weaponID : data.inGameName;
        int count = weaponManager.WeaponCount;
        weaponIndexText.text = count > 1 ? $"휠  {weaponManager.CurrentIndex + 1} / {count}" : "";

        if (weaponIcon.sprite != data.weaponSprite) weaponIcon.sprite = data.weaponSprite;
        weaponIcon.color = data.weaponSprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);

        if (!data.UsesAmmo)
        {
            ammoText.text = "<size=40><b>∞</b></size>";
            weaponHintText.text = "";
            reloadBarRoot.SetActive(false);
            return;
        }

        int cur = weapon.currentAmmo, max = data.maxAmmo;
        bool low = cur <= Mathf.CeilToInt(max * 0.25f);
        string curHex = low ? "FF6666" : "FFFFFF";
        ammoText.text = $"<size=40><b><color=#{curHex}>{cur}</color></b></size><size=22><color=#FFFFFF99> / {max}</color></size>";

        if (weapon.isReloading)
        {
            reloadBarRoot.SetActive(true);
            reloadFill.anchorMax = new Vector2(weapon.ReloadProgress, 1f);
            weaponHintText.text = "재장전 중...";
            weaponHintText.color = CostOk;
        }
        else
        {
            reloadBarRoot.SetActive(false);
            weaponHintText.text = cur <= 0 ? "R 키로 재장전" : "";
            weaponHintText.color = Warn;
        }
    }

    private void UpdateGoldEffects()
    {
        if (Time.unscaledTime < shakeUntil)
        {
            float k = (shakeUntil - Time.unscaledTime) / 0.4f;
            goldRect.anchoredPosition = goldBasePos + new Vector2(Mathf.Round(Mathf.Sin(Time.unscaledTime * 60f) * 6f * k), 0f);
            goldText.color = Warn;
        }
        else if (goldRect.anchoredPosition != goldBasePos)
        {
            goldRect.anchoredPosition = goldBasePos;
            goldText.color = Color.white;
        }
    }

    private bool NoticeActive() => Time.unscaledTime < noticeUntil;

    private void HandleGoldChanged(int gold, int delta) { lastGold = int.MinValue; }

    private void HandleGoldShort(int needed)
    {
        int have = GoldManager.Instance != null ? GoldManager.Instance.Gold : 0;
        infoText.text = $"골드가 부족합니다 (필요 {needed} / 보유 {have})";
        infoText.color = Warn;
        noticeUntil = Time.unscaledTime + 1.5f;
        shakeUntil = Time.unscaledTime + 0.4f;
    }

    private string TowerLabel(int i)
    {
        EntityData d = builder.GetTowerData(i);
        GameObject p = builder.GetTowerPrefab(i);
        string name = d != null && !string.IsNullOrEmpty(d.entityName) ? d.entityName : (p != null ? p.name : "");
        return $"<b>{name}</b>  <color=#FFE066>{builder.GetTowerCost(i)} G</color>";
    }

    // ── 스킬 칸 (나중에 스킬 시스템에서 호출) ──
    public void SetSkillIcon(int index, Sprite icon)
    {
        if (index < 0 || index >= skillIcons.Length) return;
        skillIcons[index].sprite = icon;
        skillIcons[index].color = icon != null ? Color.white : new Color(1f, 1f, 1f, 0f);
    }

    // 0 = 사용 가능, 1 = 막 사용함 (아래에서부터 어두운 막이 줄어듦)
    public void SetSkillCooldown(int index, float remainingRatio)
    {
        if (index < 0 || index >= skillCooldowns.Length) return;
        skillCooldowns[index].anchorMax = new Vector2(1f, Mathf.Clamp01(remainingRatio));
    }

    // ==================================================================
    // UI 생성
    // ==================================================================
    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1;    // 일시정지·게임오버 화면보다 아래
        canvas.pixelPerfect = true;  // 글자 번짐 방지
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Arial" }, 32);
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        float groupW = 5 * Slot + 4 * SlotGap;
        float width = Pad * 2 + groupW * 2 + GroupGap * 2 + Portrait;
        float slotY = 14f, labelY = slotY + Slot + 2f, hpY = labelY + 30f, height = hpY + 40f;

        // 하단 패널
        root = NewRect("BottomPanel", transform);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
        root.pivot = new Vector2(0.5f, 0f);
        root.sizeDelta = new Vector2(width, height);
        root.anchoredPosition = new Vector2(0f, 12f);
        AddImage(root, new Color(0f, 0f, 0f, panelAlpha), false);

        // ── 체력 줄 ──
        hpText = AddText(Place(NewRect("HP", root), Pad, hpY, 90f, 30f), "100", 26, TextAnchor.MiddleLeft, Color.white);
        hpText.fontStyle = FontStyle.Bold;
        AddShadow(hpText);

        float barX = Pad + 90f, barW = width - Pad - barX - 170f;
        RectTransform track = Place(NewRect("HpBar", root), barX, hpY + 9f, barW, 12f);
        AddImage(track, TrackColor, false);
        hpTrail = Stretch(NewRect("Trail", track));
        AddImage(hpTrail, HpTrailColor, false);
        hpFill = Stretch(NewRect("Fill", track));
        hpFillImage = AddImage(hpFill, HpColor, false);

        // 골드 (체력 줄 오른쪽 끝)
        goldRect = Place(NewRect("Gold", root), width - Pad - 160f, hpY, 160f, 30f);
        goldBasePos = goldRect.anchoredPosition;
        goldText = AddText(goldRect, "골드  0", 22, TextAnchor.MiddleRight, Color.white);
        goldText.fontStyle = FontStyle.Bold;
        AddShadow(goldText);

        // 안내 문구 (패널 바로 위)
        RectTransform info = Place(NewRect("Info", root), 0f, height + 6f, width, 26f);
        infoText = AddText(info, "", 18, TextAnchor.LowerCenter, Color.white);
        AddShadow(infoText);

        // ── 스킬 칸 (왼쪽) ──
        for (int i = 0; i < 5; i++)
        {
            float x = Pad + i * (Slot + SlotGap);
            RectTransform box = SlotBox(root, $"Skill_{SkillKeys[i]}", x, slotY, out Image frame);
            RectTransform iconRt = Inset(NewRect("Icon", box), 8f);
            skillIcons[i] = AddImage(iconRt, new Color(1f, 1f, 1f, 0f), false);
            skillIcons[i].preserveAspect = true;
            skillCooldowns[i] = NewRect("Cooldown", box);
            skillCooldowns[i].anchorMin = Vector2.zero;
            skillCooldowns[i].anchorMax = new Vector2(1f, 0f);
            skillCooldowns[i].offsetMin = new Vector2(2f, 2f); skillCooldowns[i].offsetMax = new Vector2(-2f, 0f);
            AddImage(skillCooldowns[i], new Color(0f, 0f, 0f, 0.6f), false);
            KeyLabel(root, SkillKeys[i], x, labelY, Color.white);
        }

        // ── 초상화 (가운데) ──
        float px = Pad + groupW + GroupGap;
        RectTransform portraitBox = Place(NewRect("Portrait", root), px, slotY, Portrait, Portrait);
        AddImage(portraitBox, FrameNormal, false);
        RectTransform portraitInner = Inset(NewRect("Inner", portraitBox), 2f);
        AddImage(portraitInner, SlotBg, false);
        portraitImage = AddImage(Inset(NewRect("Image", portraitInner), 6f), new Color(1f, 1f, 1f, 0f), false);
        portraitImage.preserveAspect = true;

        // ── 포탑 칸 (오른쪽) ── 플레이어가 연결되면 채움
        towerGroup = Place(NewRect("Towers", root), px + Portrait + GroupGap, 0f, groupW, height);

        BuildWeaponCard(width, height);

        GoldManager gm = GoldManager.Instance;
        if (gm != null) { gm.OnGoldShort += HandleGoldShort; gm.OnGoldChanged += HandleGoldChanged; }

        root.gameObject.SetActive(false);
        weaponCard.gameObject.SetActive(false);
    }

    private void BuildTowerSlots()
    {
        for (int i = towerGroup.childCount - 1; i >= 0; i--) Destroy(towerGroup.GetChild(i).gameObject);
        towerFrames = null;
        if (builder == null || builder.SlotCount == 0) return;

        int n = Mathf.Min(5, builder.SlotCount);
        towerFrames = new Image[n]; towerIcons = new Image[n]; towerCosts = new Text[n]; towerKeys = new Text[n]; towerHasIcon = new bool[n];
        float slotY = 14f, labelY = slotY + Slot + 2f;

        for (int i = 0; i < n; i++)
        {
            float x = i * (Slot + SlotGap);
            RectTransform box = SlotBox(towerGroup, $"Tower_{i + 1}", x, slotY, out towerFrames[i]);

            // 클릭으로 선택 + 마우스를 올리면 이름/비용 안내
            Button button = box.gameObject.AddComponent<Button>();
            button.targetGraphic = towerFrames[i];
            int captured = i;
            button.onClick.AddListener(() => { if (builder != null) builder.SelectSlot(captured); });
            GameHUDHoverRelay hover = box.gameObject.AddComponent<GameHUDHoverRelay>();
            hover.onEnter = () => hoverInfo = TowerLabel(captured);
            hover.onExit = () => hoverInfo = null;

            // 썸네일
            RectTransform iconRt = NewRect("Icon", box);
            iconRt.anchorMin = Vector2.zero; iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = new Vector2(6f, 20f); iconRt.offsetMax = new Vector2(-6f, -4f);
            towerIcons[i] = AddImage(iconRt, Color.white, false);
            towerIcons[i].preserveAspect = true;
            GameObject prefab = builder.GetTowerPrefab(i);
            Sprite sprite = prefab != null ? Resources.Load<Sprite>($"Image/TurretIcons/{prefab.name}") : null;
            towerHasIcon[i] = sprite != null;
            if (sprite != null) towerIcons[i].sprite = sprite;
            else
            {
                towerIcons[i].color = new Color(1f, 1f, 1f, 0.06f);
                AddText(iconRt, prefab != null ? prefab.name : "-", 14, TextAnchor.MiddleCenter, SubText);
            }

            // 비용 (칸 아래쪽 띠)
            RectTransform costRt = NewRect("Cost", box);
            costRt.anchorMin = Vector2.zero; costRt.anchorMax = new Vector2(1f, 0f);
            costRt.pivot = new Vector2(0.5f, 0f);
            costRt.offsetMin = new Vector2(2f, 2f); costRt.offsetMax = new Vector2(-2f, 20f);
            AddImage(costRt, new Color(0f, 0f, 0f, 0.45f), false);
            towerCosts[i] = AddText(costRt, $"{builder.GetTowerCost(i)} G", 15, TextAnchor.MiddleCenter, CostOk);
            towerCosts[i].fontStyle = FontStyle.Bold;

            towerKeys[i] = KeyLabel(towerGroup, (i + 1).ToString(), x, labelY, Color.white);
        }
    }

    // 무기 카드: 하단 패널 위, 포탑 칸 쪽(오른쪽)에 붙여서 배치 (화면 비율이 달라도 포탑 칸과 겹치지 않음)
    private void BuildWeaponCard(float panelWidth, float panelHeight)
    {
        const float w = 340f, h = 120f;
        weaponCard = Place(NewRect("WeaponCard", root), panelWidth - w, panelHeight + 36f, w, h);
        AddImage(weaponCard, new Color(0f, 0f, 0f, panelAlpha), false);

        weaponNameText = AddText(Place(NewRect("Name", weaponCard), 14f, h - 34f, w - 120f, 26f), "", 20, TextAnchor.MiddleLeft, Color.white);
        weaponNameText.fontStyle = FontStyle.Bold;
        AddShadow(weaponNameText);
        weaponIndexText = AddText(Place(NewRect("Index", weaponCard), w - 120f, h - 34f, 106f, 26f), "", 15, TextAnchor.MiddleRight, SubText);
        AddShadow(weaponIndexText);

        // 무기 그림 칸
        RectTransform iconBox = Place(NewRect("IconBox", weaponCard), 14f, 14f, 150f, 64f);
        AddImage(iconBox, FrameNormal, false);
        RectTransform iconInner = Inset(NewRect("Inner", iconBox), 2f);
        AddImage(iconInner, SlotBg, false);
        weaponIcon = AddImage(Inset(NewRect("Icon", iconInner), 6f), new Color(1f, 1f, 1f, 0f), false);
        weaponIcon.preserveAspect = true;

        ammoText = AddText(Place(NewRect("Ammo", weaponCard), 176f, 34f, w - 190f, 46f), "", 22, TextAnchor.MiddleRight, Color.white);
        AddShadow(ammoText);
        weaponHintText = AddText(Place(NewRect("Hint", weaponCard), 176f, 6f, w - 190f, 20f), "", 15, TextAnchor.MiddleRight, Warn);
        AddShadow(weaponHintText);

        RectTransform bar = Place(NewRect("ReloadBar", weaponCard), 176f, 28f, w - 190f, 5f);
        AddImage(bar, TrackColor, false);
        reloadFill = Stretch(NewRect("Fill", bar));
        reloadFill.anchorMax = new Vector2(0f, 1f);
        AddImage(reloadFill, CostOk, false);
        reloadBarRoot = bar.gameObject;
        reloadBarRoot.SetActive(false);
    }

    // ==================================================================
    // 도우미
    // ==================================================================
    // 테두리 + 안쪽 배경으로 된 정사각형 칸
    private RectTransform SlotBox(RectTransform parent, string name, float x, float y, out Image frame)
    {
        RectTransform box = Place(NewRect(name, parent), x, y, Slot, Slot);
        frame = AddImage(box, FrameNormal, true);
        RectTransform inner = Inset(NewRect("Inner", box), 2f);
        AddImage(inner, SlotBg, false);
        return box;
    }

    private Text KeyLabel(RectTransform parent, string label, float x, float y, Color color)
    {
        Text t = AddText(Place(NewRect("Key_" + label, parent), x - 10f, y, Slot + 20f, 26f), label, 17, TextAnchor.MiddleCenter, color);
        t.fontStyle = FontStyle.Bold;
        AddShadow(t);
        return t;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // 부모의 왼쪽 아래 기준 (x, y)에 (w, h) 크기로 배치
    private static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
        rt.sizeDelta = new Vector2(Mathf.Round(w), Mathf.Round(h));
        return rt;
    }

    private static RectTransform Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    private static RectTransform Inset(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        return rt;
    }

    private static Image AddImage(RectTransform rt, Color color, bool raycast)
    {
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = raycast;
        return img;
    }

    private Text AddText(RectTransform rt, string content, int size, TextAnchor align, Color color)
    {
        GameObject go = rt.gameObject;
        if (go.GetComponent<Graphic>() != null)
        {
            // 이미 그림(Image)이 있는 칸이면 자식에 글자를 붙임
            go = Stretch(NewRect("Text", rt)).gameObject;
        }
        Text t = go.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = content;
        return t;
    }

    private static void AddShadow(Text t)
    {
        Shadow s = t.gameObject.AddComponent<Shadow>();
        s.effectColor = new Color(0f, 0f, 0f, 0.9f);
        s.effectDistance = new Vector2(1.5f, -1.5f);
    }
}

// 마우스 올림/내림 알림 (GameHUD 포탑 칸 안내 문구용, 실행 중에만 붙임)
public class GameHUDHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public System.Action onEnter, onExit;
    public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
    public void OnPointerExit(PointerEventData e) => onExit?.Invoke();
}
