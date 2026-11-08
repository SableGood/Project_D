using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 재능 목록 (테스트용, v101)
//  시작 화면에서 '테스트 모드'를 켜면 게임 중 F3으로 열림. 모든 재능이 목록으로 나오고, 검색·희귀도로 찾아서 누르면 바로 얻음
//  - 검색: 이름·코드·효과·설명에 들어간 글자 (띄어쓰기로 여러 단어 → 모두 포함)
//  - 희귀도 버튼: 전체 / 일반 ~ 신화 / 보유 (지금 가진 재능만)
//  - Esc 또는 F3: 닫기. 열려 있는 동안 게임은 멈춤
public class TalentBrowser : MonoBehaviour
{
    public static TalentBrowser Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;

    private bool open;
    private float prevTimeScale = 1f;
    private CursorLockMode prevLock;
    private bool prevCursor;

    private Canvas canvas;
    private GameObject root;
    private InputField search;
    private RectTransform content;
    private Text countText;
    private readonly List<Button> filterButtons = new List<Button>();
    private int filter; // 0 = 전체, 1~6 = 희귀도, 7 = 보유
    private static readonly string[] FilterNames = { "전체", "일반", "고급", "희귀", "영웅", "전설", "신화", "보유" };

    private static readonly Color Panel = new Color(0.09f, 0.09f, 0.09f, 0.97f);
    private static readonly Color Row = new Color(0.17f, 0.17f, 0.16f, 1f);
    private static readonly Color RowHover = new Color(0.25f, 0.27f, 0.22f, 1f);
    private static readonly Color Accent = new Color(0.91f, 0.76f, 0.48f, 1f);
    private static readonly Color TextMain = new Color(0.93f, 0.93f, 0.9f, 1f);
    private static readonly Color TextSub = new Color(0.66f, 0.66f, 0.63f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("TalentBrowser");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<TalentBrowser>();
    }

    void Update()
    {
        if (open)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || (Input.GetKeyDown(KeyCode.F3) && !search.isFocused)) Close();
            return;
        }
        if (!Input.GetKeyDown(KeyCode.F3)) return;
        if (!PlayerWeaponManager.TestModeAllWeapons) { AugmentManager.Toast("재능 목록(F3)은 시작 화면에서 테스트 모드를 켜야 열려요"); return; }
        if (AugmentManager.IsOpen) return; // 다른 창(재능 선택·상점)이 열려 있음
        if (Player() == null) { AugmentManager.Toast("플레이어를 찾지 못해 재능 목록을 열 수 없어요"); return; }
        Open();
    }

    private static PlayerAugments Player()
    {
        // v103: 재능을 하나도 안 얻었으면 PlayerAugments가 아직 없음 → 플레이어에 붙여 줌 (재능 선택 창과 같은 방식)
        if (PlayerAugments.Instance != null) return PlayerAugments.Instance;
        PlayerController pc = FindFirstObjectByType<PlayerController>();
        if (pc == null) return null;
        PlayerAugments pa = pc.GetComponent<PlayerAugments>();
        if (pa == null) pa = pc.gameObject.AddComponent<PlayerAugments>();
        return pa;
    }

    private void Open()
    {
        if (root == null) Build();
        open = true;
        root.SetActive(true);
        Sfx.UIOpen(); // v129
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
        AugmentManager.MarkClosed(); // 같은 프레임의 Esc로 일시정지 창이 열리지 않게
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null) es.SetSelectedGameObject(null);
    }

    // ===== 목록 =====
    private void Rebuild()
    {
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        PlayerAugments pa = Player();
        string[] words = (search.text ?? "").Trim().ToLower().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        int shown = 0;
        foreach (AugmentData a in AugmentDatabase.All)
        {
            if (!PlayerAugments.IsKnownEffect(a.effect)) continue;
            int stack = pa != null ? pa.StackOf(a.code) : 0;
            if (filter >= 1 && filter <= 6 && AugmentManager.RarityIndex(a.rarity) != filter - 1) continue;
            if (filter == 7 && stack == 0) continue;
            string hay = (a.code + " " + a.name + " " + a.effect + " " + a.description + " " + a.rarity).ToLower();
            bool ok = true;
            foreach (string w in words) if (!hay.Contains(w)) { ok = false; break; }
            if (!ok) continue;
            MakeRow(a, stack);
            shown++;
        }
        countText.text = $"{shown}개";
        for (int i = 0; i < filterButtons.Count; i++)
            filterButtons[i].GetComponent<Image>().color = i == filter ? Accent : Row;
        for (int i = 0; i < filterButtons.Count; i++)
            filterButtons[i].GetComponentInChildren<Text>().color = i == filter ? Color.black : TextMain;
    }

    private void MakeRow(AugmentData a, int stack)
    {
        GameObject go = new GameObject(a.code, typeof(RectTransform));
        go.transform.SetParent(content, false);
        Image bg = go.AddComponent<Image>();
        bg.color = Row;
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 62f;
        Button b = go.AddComponent<Button>();
        ColorBlock cb = b.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(1.35f, 1.35f, 1.2f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); b.colors = cb;

        // 희귀도 띠
        Image band = NewImage(go.transform, "Band", AugmentManager.RarityColor(a.rarity));
        RectTransform br = band.rectTransform; br.anchorMin = new Vector2(0, 0); br.anchorMax = new Vector2(0, 1); br.pivot = new Vector2(0, 0.5f);
        br.sizeDelta = new Vector2(6f, 0f); br.anchoredPosition = Vector2.zero;

        string rc = ColorUtility.ToHtmlStringRGB(AugmentManager.RarityColor(a.rarity));
        bool maxed = stack >= Mathf.Max(1, a.maxStacks);
        string own = stack > 0 ? $"  <color=#E8C27A>보유 {stack}/{Mathf.Max(1, a.maxStacks)}</color>" : (a.maxStacks > 1 ? $"  <color=#8A8A84>최대 {a.maxStacks}</color>" : "");
        string off = a.enabled ? "" : "  <color=#F08A7A>(사용 X)</color>";
        Text t = NewText(go.transform, "Text", 19, TextAnchor.MiddleLeft);
        t.supportRichText = true;
        t.text = $"<color=#8A8A84>{a.code}</color>  <b>{a.name}</b>  <size=15><color=#{rc}>{a.rarity}</color>  <color=#8A8A84>{a.effect}</color></size>{own}{off}\n<size=15><color=#A8A8A0>{a.description}</color></size>";
        RectTransform tr = t.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(18f, 4f); tr.offsetMax = new Vector2(-110f, -4f);
        t.horizontalOverflow = HorizontalWrapMode.Wrap;

        Text act = NewText(go.transform, "Act", 17, TextAnchor.MiddleCenter);
        act.text = maxed ? "최대" : "얻기";
        act.color = maxed ? TextSub : Accent;
        RectTransform ar = act.rectTransform; ar.anchorMin = new Vector2(1, 0); ar.anchorMax = new Vector2(1, 1); ar.pivot = new Vector2(1, 0.5f);
        ar.sizeDelta = new Vector2(100f, 0f); ar.anchoredPosition = new Vector2(-6f, 0f);

        b.interactable = !maxed;
        b.onClick.AddListener(() => Take(a));
    }

    private void Take(AugmentData a)
    {
        PlayerAugments pa = Player();
        if (pa == null) return;
        string note = pa.Acquire(a);
        AugmentManager.Toast($"'{a.name}' 획득" + (string.IsNullOrEmpty(note) ? "" : "\n" + note));
        Rebuild();
    }

    // ===== UI 만들기 =====
    private void Build()
    {
        root = new GameObject("TalentBrowserUI", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 490; // 알림(Toast, 500)이 위에 보이게
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

        // 어두운 배경
        Image dim = NewImage(root.transform, "Dim", new Color(0, 0, 0, 0.55f));
        Stretch(dim.rectTransform);

        Image panel = NewImage(root.transform, "Panel", Panel);
        RectTransform pr = panel.rectTransform; pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f); pr.sizeDelta = new Vector2(1180f, 860f);

        Text title = NewText(panel.transform, "Title", 30, TextAnchor.MiddleLeft);
        title.text = "재능 목록 <size=18><color=#8A8A84>테스트 모드 · 누르면 바로 얻음 · Esc / F3 닫기</color></size>";
        title.supportRichText = true;
        Place(title.rectTransform, 24f, -18f, 900f, 44f);

        countText = NewText(panel.transform, "Count", 18, TextAnchor.MiddleRight);
        countText.color = TextSub;
        RectTransform cr = countText.rectTransform; cr.anchorMin = cr.anchorMax = new Vector2(1, 1); cr.pivot = new Vector2(1, 1); cr.sizeDelta = new Vector2(200f, 44f); cr.anchoredPosition = new Vector2(-24f, -18f);

        // 검색 칸
        Image sb = NewImage(panel.transform, "Search", new Color(0.2f, 0.2f, 0.19f, 1f));
        Place(sb.rectTransform, 24f, -72f, 1132f, 48f);
        search = sb.gameObject.AddComponent<InputField>();
        Text st = NewText(sb.transform, "Text", 22, TextAnchor.MiddleLeft);
        st.supportRichText = false;
        Stretch(st.rectTransform, 14f);
        Text ph = NewText(sb.transform, "Placeholder", 22, TextAnchor.MiddleLeft);
        ph.text = "검색: 이름 · 코드 · 효과 · 설명 (예: 폭발, 치명타, C1)";
        ph.color = new Color(0.55f, 0.55f, 0.52f, 1f);
        Stretch(ph.rectTransform, 14f);
        search.textComponent = st;
        search.placeholder = ph;
        search.targetGraphic = sb;
        search.onValueChanged.AddListener(_ => Rebuild());

        // 희귀도 버튼
        for (int i = 0; i < FilterNames.Length; i++)
        {
            int idx = i;
            Image fb = NewImage(panel.transform, "Filter_" + FilterNames[i], Row);
            Place(fb.rectTransform, 24f + i * 108f, -130f, 100f, 38f);
            Button btn = fb.gameObject.AddComponent<Button>();
            Text ft = NewText(fb.transform, "Text", 18, TextAnchor.MiddleCenter);
            ft.text = FilterNames[i];
            Stretch(ft.rectTransform);
            btn.onClick.AddListener(() => { filter = idx; Rebuild(); });
            filterButtons.Add(btn);
        }

        // 목록 (스크롤)
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

    // 왼쪽 위 기준 배치
    private static void Place(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
    }
}
