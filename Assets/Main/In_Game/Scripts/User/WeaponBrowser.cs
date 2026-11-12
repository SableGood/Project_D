using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// v156 무기 목록 (테스트용) — 재능 목록(F3)과 같은 모양
//  시작 화면에서 '테스트 모드'를 켜면 게임 중 F6으로 열림. 모든 무기의 능력치를 한눈에 보고, 누르면 바로 들기
//  - 검색: 이름·별칭·코드·분류·역할·설명 (띄어쓰기로 여러 단어 → 모두 포함)
//  - 분류 버튼: 전체 / 권총 / 기관단총 / 돌격 / 전투 / 기관총 / 저격 / 산탄 / 근접 / 로켓 / 보유
//  - Esc 또는 F6: 닫기. 열려 있는 동안 게임은 멈춤
public class WeaponBrowser : MonoBehaviour
{
    public static WeaponBrowser Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;
    public static KeyCode Key = KeyCode.F6;

    private bool open;
    private float prevTimeScale = 1f;
    private CursorLockMode prevLock;
    private bool prevCursor;

    private GameObject root;
    private InputField search;
    private RectTransform content;
    private Text countText;
    private readonly List<Button> filterButtons = new List<Button>();
    private int filter;
    private static readonly string[] FilterNames = { "전체", "권총", "기관단총", "돌격", "전투", "기관총", "저격", "산탄", "근접", "로켓", "보유" };
    private static readonly string[] FilterTags = { "", "HG", "SMG", "AR", "BR", "MG", "RF", "SG", "MELEE", "RL", "" };

    private static readonly Color Panel = new Color(0.09f, 0.09f, 0.09f, 0.97f);
    private static readonly Color Row = new Color(0.17f, 0.17f, 0.16f, 1f);
    private static readonly Color RowHeld = new Color(0.24f, 0.28f, 0.2f, 1f);
    private static readonly Color Accent = new Color(0.91f, 0.76f, 0.48f, 1f);
    private static readonly Color TextMain = new Color(0.93f, 0.93f, 0.9f, 1f);
    private static readonly Color TextSub = new Color(0.66f, 0.66f, 0.63f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("WeaponBrowser");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<WeaponBrowser>();
    }

    void Update()
    {
        if (open)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || (Input.GetKeyDown(Key) && !search.isFocused)) Close();
            return;
        }
        if (!Input.GetKeyDown(Key)) return;
        if (!PlayerWeaponManager.TestModeAllWeapons) { AugmentManager.Toast("무기 목록(F6)은 시작 화면에서 테스트 모드를 켜야 열려요"); return; }
        if (AugmentManager.IsOpen || TalentBrowser.IsOpen) return;
        if (Manager() == null) { AugmentManager.Toast("플레이어를 찾지 못해 무기 목록을 열 수 없어요"); return; }
        Open();
    }

    private static PlayerWeaponManager Manager() => FindFirstObjectByType<PlayerWeaponManager>();

    private void Open()
    {
        if (root == null) Build();
        open = true;
        root.SetActive(true);
        Sfx.UIOpen();
        prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        prevLock = Cursor.lockState; prevCursor = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        Rebuild();
        search.Select();
        search.ActivateInputField();
    }

    private void Close()
    {
        open = false;
        root.SetActive(false);
        Time.timeScale = prevTimeScale;
        Cursor.lockState = prevLock; Cursor.visible = prevCursor;
        AugmentManager.MarkClosed();
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null) es.SetSelectedGameObject(null);
    }

    // ===== 목록 =====
    private void Rebuild()
    {
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        PlayerWeaponManager pm = Manager();
        if (pm == null) return;
        string[] words = (search.text ?? "").Trim().ToLower().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        int shown = 0;
        for (int i = 0; i < pm.CatalogCount; i++)
        {
            PlayerWeaponData d = pm.DataAt(i);
            if (d == null) continue;
            bool owned = false;
            foreach (int o in pm.Owned) if (o == i) owned = true;
            string tag = d.Category.ToUpper();
            if (filter >= 1 && filter <= 9 && tag != FilterTags[filter]) continue;
            if (filter == 10 && !owned && pm.CurrentCatalogIndex != i) continue;
            string hay = (d.weaponID + " " + d.weaponName + " " + d.inGameName + " " + d.category + " " + d.role + " " + d.description + " " + d.acquire).ToLower();
            bool ok = true;
            foreach (string w in words) if (!hay.Contains(w)) { ok = false; break; }
            if (!ok) continue;
            MakeRow(pm, i, d, owned);
            shown++;
        }
        countText.text = $"{shown}개";
        for (int i = 0; i < filterButtons.Count; i++)
        {
            filterButtons[i].GetComponent<Image>().color = i == filter ? Accent : Row;
            filterButtons[i].GetComponentInChildren<Text>().color = i == filter ? Color.black : TextMain;
        }
    }

    private static string F(float v) => Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.##");

    private void MakeRow(PlayerWeaponManager pm, int index, PlayerWeaponData d, bool owned)
    {
        bool held = pm.CurrentCatalogIndex == index;
        GameObject go = new GameObject(d.weaponID, typeof(RectTransform));
        go.transform.SetParent(content, false);
        Image bg = go.AddComponent<Image>();
        bg.color = held ? RowHeld : Row;
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 92f;
        Button b = go.AddComponent<Button>();
        ColorBlock cb = b.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(1.35f, 1.35f, 1.2f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); b.colors = cb;

        // 무기 그림
        Image icon = NewImage(go.transform, "Icon", Color.white);
        RectTransform ir = icon.rectTransform; ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f); ir.pivot = new Vector2(0f, 0.5f);
        ir.sizeDelta = new Vector2(150f, 76f); ir.anchoredPosition = new Vector2(12f, 0f);
        icon.sprite = d.weaponSprite; icon.preserveAspect = true; icon.raycastTarget = false; icon.enabled = d.weaponSprite != null;

        // 이름 줄 + 능력치 줄 + 설명
        float shots = Mathf.Max(1, d.projectileCount);
        float dps = d.damage * shots * Mathf.Max(0f, d.fireRate);
        string name = string.IsNullOrEmpty(d.inGameName) ? d.weaponName : $"{d.inGameName} <color=#8A8A84>({d.weaponName})</color>";
        string tags = $"<color=#8A8A84>{d.weaponID}</color>  <color=#E8C27A>{d.category}</color>  <color=#8A8A84>{d.role}</color>";
        string own = held ? "  <color=#C6E7A7>들고 있음</color>" : owned ? "  <color=#E8C27A>보유</color>" : "";
        string acq = d.TalentOnly ? "재능" : d.ShopUnlock ? "해금" : "상점";
        string mode = (d.IsAuto ? "연사" : "단발") + " · " + (d.IsProjectile ? "투사체" : "즉발");
        string ammo = d.UsesAmmo ? $"{d.maxAmmo}발 · 재장전 {F(d.reloadTime)}초" : "탄 무한";
        string extra = "";
        if (d.canPierce) extra += " · 관통";
        if (d.armorPenetration > 0f) extra += $" · 방어무시 {F(d.armorPenetration)}";
        if (d.knockback > 0f) extra += $" · 넉백 {F(d.knockback)}";
        if (d.aoeRadius > 0f) extra += $" · 폭발 {F(d.aoeRadius)}m";
        string stats = $"공격력 <color=#FFFFFF>{F(d.damage)}{(shots > 1 ? " ×" + shots : "")}</color> · 초당 {F(d.fireRate)}발 · <color=#E8C27A>DPS {Mathf.RoundToInt(dps)}</color> · {ammo} · 사거리 {F(d.range)} · 퍼짐 {F(d.spread)}°(+이동 {F(d.moveSpread)}°) · {mode}{extra} · 획득 {acq}";

        Text t = NewText(go.transform, "Text", 19, TextAnchor.MiddleLeft);
        t.supportRichText = true;
        t.text = $"{tags}   <b>{name}</b>{own}\n<size=16><color=#C8C8C0>{stats}</color></size>\n<size=15><color=#8A8A84>{d.description}</color></size>";
        RectTransform tr = t.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(176f, 4f); tr.offsetMax = new Vector2(-100f, -4f);
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;

        Text act = NewText(go.transform, "Act", 17, TextAnchor.MiddleCenter);
        act.text = held ? "사용 중" : "들기";
        act.color = held ? TextSub : Accent;
        RectTransform ar = act.rectTransform; ar.anchorMin = new Vector2(1, 0); ar.anchorMax = new Vector2(1, 1); ar.pivot = new Vector2(1, 0.5f);
        ar.sizeDelta = new Vector2(90f, 0f); ar.anchoredPosition = new Vector2(-6f, 0f);

        b.interactable = !held;
        b.onClick.AddListener(() => Take(index, d));
    }

    private void Take(int index, PlayerWeaponData d)
    {
        PlayerWeaponManager pm = Manager();
        if (pm == null) return;
        pm.EquipWeapon(index);
        AugmentManager.Toast($"'{(string.IsNullOrEmpty(d.inGameName) ? d.weaponName : d.inGameName)}' 들기");
        Rebuild();
    }

    // ===== UI 만들기 =====
    private void Build()
    {
        root = new GameObject("WeaponBrowserUI", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 490;
        CanvasScaler cs = root.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920f, 1080f);
        cs.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            GameObject es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
            DontDestroyOnLoad(es);
        }

        Image dim = NewImage(root.transform, "Dim", new Color(0, 0, 0, 0.55f));
        Stretch(dim.rectTransform);

        Image panel = NewImage(root.transform, "Panel", Panel);
        RectTransform pr = panel.rectTransform; pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f); pr.sizeDelta = new Vector2(1500f, 900f);

        Text title = NewText(panel.transform, "Title", 30, TextAnchor.MiddleLeft);
        title.text = "무기 목록 <size=18><color=#8A8A84>테스트 모드 · 누르면 바로 들기 · Esc / F6 닫기</color></size>";
        title.supportRichText = true;
        Place(title.rectTransform, 24f, -18f, 1100f, 44f);

        countText = NewText(panel.transform, "Count", 18, TextAnchor.MiddleRight);
        countText.color = TextSub;
        RectTransform cr = countText.rectTransform; cr.anchorMin = cr.anchorMax = new Vector2(1, 1); cr.pivot = new Vector2(1, 1); cr.sizeDelta = new Vector2(200f, 44f); cr.anchoredPosition = new Vector2(-24f, -18f);

        Image sb = NewImage(panel.transform, "Search", new Color(0.2f, 0.2f, 0.19f, 1f));
        Place(sb.rectTransform, 24f, -72f, 1452f, 48f);
        search = sb.gameObject.AddComponent<InputField>();
        Text st = NewText(sb.transform, "Text", 22, TextAnchor.MiddleLeft);
        st.supportRichText = false;
        Stretch(st.rectTransform, 14f);
        Text ph = NewText(sb.transform, "Placeholder", 22, TextAnchor.MiddleLeft);
        ph.text = "검색: 이름 · 별칭 · 코드 · 분류 · 설명 (예: 듀티, W004, 저격)";
        ph.color = new Color(0.55f, 0.55f, 0.52f, 1f);
        Stretch(ph.rectTransform, 14f);
        search.textComponent = st;
        search.placeholder = ph;
        search.targetGraphic = sb;
        search.onValueChanged.AddListener(_ => Rebuild());

        for (int i = 0; i < FilterNames.Length; i++)
        {
            int idx = i;
            Image fb = NewImage(panel.transform, "Filter_" + FilterNames[i], Row);
            Place(fb.rectTransform, 24f + i * 130f, -130f, 122f, 38f);
            Button btn = fb.gameObject.AddComponent<Button>();
            Text ft = NewText(fb.transform, "Text", 18, TextAnchor.MiddleCenter);
            ft.text = FilterNames[i];
            Stretch(ft.rectTransform);
            btn.onClick.AddListener(() => { filter = idx; Rebuild(); });
            filterButtons.Add(btn);
        }

        GameObject sv = new GameObject("Scroll", typeof(RectTransform));
        sv.transform.SetParent(panel.transform, false);
        RectTransform svr = (RectTransform)sv.transform;
        svr.anchorMin = new Vector2(0, 0); svr.anchorMax = new Vector2(1, 1); svr.offsetMin = new Vector2(24f, 24f); svr.offsetMax = new Vector2(-24f, -182f);
        Image svBg = sv.AddComponent<Image>(); svBg.color = new Color(0, 0, 0, 0.2f);
        ScrollRect scroll = sv.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.scrollSensitivity = 40f; scroll.movementType = ScrollRect.MovementType.Clamped;

        GameObject vp = new GameObject("Viewport", typeof(RectTransform));
        vp.transform.SetParent(sv.transform, false);
        RectTransform vpr = (RectTransform)vp.transform; Stretch(vpr);
        vp.AddComponent<RectMask2D>();
        GameObject ct = new GameObject("Content", typeof(RectTransform));
        ct.transform.SetParent(vp.transform, false);
        content = (RectTransform)ct.transform;
        content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup vl = ct.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 6f; vl.padding = new RectOffset(6, 6, 6, 6);
        vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false; vl.childForceExpandWidth = true;
        ContentSizeFitter fit = ct.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vpr;
        scroll.content = content;

        root.SetActive(false);
    }

    private static Image NewImage(Transform parent, string name, Color c)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image i = go.AddComponent<Image>();
        i.color = c;
        return i;
    }

    private static Text NewText(Transform parent, string name, int size, TextAnchor anchor)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        UIFont.Apply(t, FontStyle.Normal);
        t.fontSize = size;
        t.alignment = anchor;
        t.color = TextMain;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private static void Stretch(RectTransform r, float pad = 0f)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(pad, 0f); r.offsetMax = new Vector2(-pad, 0f);
    }

    private static void Place(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
    }
}
