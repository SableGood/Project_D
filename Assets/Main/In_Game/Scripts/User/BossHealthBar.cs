using UnityEngine;
using UnityEngine.UI;

// 화면 위쪽 가운데 보스 체력바 (보스가 있을 때만 보임)
//   BOSS 이름                                  Ⅰ Ⅱ Ⅲ
//   [==================|=========|=====]   ← 60% / 30% 지점에 페이즈 경계 표시
//   페이즈가 바뀌면 바 아래에 "PHASE 2" 가 크게 떴다 사라짐
//  - 하단 HUD와 같은 디자인(반투명 배경, 얇은 테두리, 맑은 고딕). 자동 생성되므로 씬에 놓을 필요 없음
public class BossHealthBar : MonoBehaviour
{
    private const float Width = 640f, BarH = 16f;
    private static readonly Color PanelBg = new Color(0f, 0f, 0f, 0.5f);
    private static readonly Color FrameColor = new Color(1f, 1f, 1f, 0.18f);
    private static readonly Color TrackColor = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color FillColor = new Color(0.85f, 0.18f, 0.15f, 1f);
    private static readonly Color TrailColor = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color PipOn = new Color(1f, 0.85f, 0.25f, 1f);
    private static readonly Color PipOff = new Color(1f, 1f, 1f, 0.25f);

    private static BossHealthBar instance;
    private Font font;
    private RectTransform root;
    private Text nameText, hpText, phaseBanner;
    private RectTransform fill, trail, markerRoot;
    private Text[] pips = new Text[0];
    private BossPhases boss;
    private float trailRatio = 1f;
    private float bannerUntil;
    private int markersFor = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (instance != null) return;
        GameObject go = new GameObject("BossHealthBar");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<BossHealthBar>();
        instance.Build();
    }

    void OnEnable() { BossPhases.AnyPhaseChanged += OnPhaseChanged; }
    void OnDisable() { BossPhases.AnyPhaseChanged -= OnPhaseChanged; }

    void Update()
    {
        // 살아있는 보스 중 첫 번째를 표시
        BossPhases b = null;
        foreach (BossPhases x in BossPhases.Active)
            if (x != null && x.isActiveAndEnabled && x.Health != null && !x.Health.IsDead) { b = x; break; }

        bool show = b != null;
        if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (!show) { boss = null; return; }

        if (b != boss)
        {
            boss = b;
            trailRatio = 1f;
            nameText.text = b.displayName;
            BuildMarkers(b);
        }

        float max = Mathf.Max(1f, b.Health.maxHealth);
        float ratio = Mathf.Clamp01(b.Health.CurrentHealth / max);
        fill.anchorMax = new Vector2(ratio, 1f);
        trailRatio = trailRatio > ratio ? Mathf.MoveTowards(trailRatio, ratio, Time.deltaTime * 0.35f) : ratio;
        trail.anchorMax = new Vector2(trailRatio, 1f);
        hpText.text = $"{Mathf.CeilToInt(b.Health.CurrentHealth)} / {Mathf.CeilToInt(max)}";

        for (int i = 0; i < pips.Length; i++) pips[i].color = i <= b.CurrentPhase ? PipOn : PipOff;

        if (phaseBanner.gameObject.activeSelf)
        {
            float left = bannerUntil - Time.unscaledTime;
            if (left <= 0f) phaseBanner.gameObject.SetActive(false);
            else
            {
                Color c = phaseBanner.color; c.a = Mathf.Clamp01(left / 0.5f); phaseBanner.color = c;
                float pop = 1f + Mathf.Max(0f, (left - 1.3f)) * 0.8f; // 처음에 살짝 크게 튀어나옴
                phaseBanner.rectTransform.localScale = Vector3.one * pop;
            }
        }
    }

    private void OnPhaseChanged(BossPhases b, int phase)
    {
        if (b != boss && boss != null) return;
        phaseBanner.text = $"PHASE {phase + 1}";
        phaseBanner.color = phase >= 2 ? new Color(1f, 0.3f, 0.2f, 1f) : PipOn;
        phaseBanner.gameObject.SetActive(true);
        bannerUntil = Time.unscaledTime + 1.8f;
    }

    // 페이즈 경계 눈금 + 로마숫자 표시
    private void BuildMarkers(BossPhases b)
    {
        if (markersFor == b.GetInstanceID()) return;
        markersFor = b.GetInstanceID();
        for (int i = markerRoot.childCount - 1; i >= 0; i--) Destroy(markerRoot.GetChild(i).gameObject);
        foreach (Text p in pips) if (p != null) Destroy(p.gameObject);

        for (int i = 1; i < b.phases.Count; i++)
        {
            RectTransform m = NewRect("Marker", markerRoot);
            float x = b.phases[i].hpThreshold;
            m.anchorMin = new Vector2(x, 0f); m.anchorMax = new Vector2(x, 1f);
            m.pivot = new Vector2(0.5f, 0.5f);
            m.sizeDelta = new Vector2(2f, 6f);
            m.anchoredPosition = Vector2.zero;
            Image img = m.gameObject.AddComponent<Image>(); img.color = new Color(1f, 1f, 1f, 0.85f); img.raycastTarget = false;
        }

        string[] roman = { "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ" };
        pips = new Text[b.phases.Count];
        for (int i = 0; i < pips.Length; i++)
        {
            RectTransform r = Place(NewRect("Pip", root), Width - 16f - (pips.Length - i) * 26f, 34f, 24f, 24f);
            pips[i] = MakeText(r, i < roman.Length ? roman[i] : (i + 1).ToString(), 18, TextAnchor.MiddleCenter);
        }
    }

    // ------------------------------------------------------------------
    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1;
        canvas.pixelPerfect = true;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Arial" }, 32);
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        root = NewRect("BossPanel", transform);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.sizeDelta = new Vector2(Width, 68f);
        root.anchoredPosition = new Vector2(0f, -16f);
        Image bg = root.gameObject.AddComponent<Image>(); bg.color = PanelBg; bg.raycastTarget = false;
        AddFrame(root);

        nameText = MakeText(Place(NewRect("Name", root), 16f, 34f, Width * 0.6f, 26f), "BOSS", 20, TextAnchor.MiddleLeft);
        nameText.fontStyle = FontStyle.Bold;

        RectTransform track = Place(NewRect("Track", root), 16f, 12f, Width - 32f, BarH);
        Image t = track.gameObject.AddComponent<Image>(); t.color = TrackColor; t.raycastTarget = false;
        trail = Stretch(NewRect("Trail", track)); Image ti = trail.gameObject.AddComponent<Image>(); ti.color = TrailColor; ti.raycastTarget = false;
        fill = Stretch(NewRect("Fill", track)); Image fi = fill.gameObject.AddComponent<Image>(); fi.color = FillColor; fi.raycastTarget = false;
        markerRoot = Stretch(NewRect("Markers", track));
        hpText = MakeText(Stretch(NewRect("HP", track)), "", 13, TextAnchor.MiddleCenter);

        // 페이즈 전환 배너 (체력바 아래)
        RectTransform banner = NewRect("PhaseBanner", root);
        banner.anchorMin = banner.anchorMax = new Vector2(0.5f, 0f);
        banner.pivot = new Vector2(0.5f, 1f);
        banner.sizeDelta = new Vector2(Width, 60f);
        banner.anchoredPosition = new Vector2(0f, -10f);
        phaseBanner = MakeText(banner, "", 44, TextAnchor.MiddleCenter);
        phaseBanner.fontStyle = FontStyle.Bold;
        phaseBanner.gameObject.SetActive(false);

        root.gameObject.SetActive(false);
    }

    private Text MakeText(RectTransform rt, string content, int size, TextAnchor align)
    {
        Text t = rt.gameObject.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.alignment = align; t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false; t.text = content;
        Shadow s = rt.gameObject.AddComponent<Shadow>();
        s.effectColor = new Color(0f, 0f, 0f, 0.9f); s.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    private static void AddFrame(RectTransform rt)
    {
        Vector2[,] a = { { new Vector2(0, 1), new Vector2(1, 1) }, { new Vector2(0, 0), new Vector2(1, 0) }, { new Vector2(0, 0), new Vector2(0, 1) }, { new Vector2(1, 0), new Vector2(1, 1) } };
        for (int i = 0; i < 4; i++)
        {
            RectTransform e = NewRect("Edge", rt);
            e.anchorMin = a[i, 0]; e.anchorMax = a[i, 1];
            e.pivot = new Vector2(i == 3 ? 1f : 0f, i == 0 ? 1f : 0f);
            e.anchoredPosition = Vector2.zero;
            e.sizeDelta = i < 2 ? new Vector2(0f, 2f) : new Vector2(2f, 0f);
            Image img = e.gameObject.AddComponent<Image>(); img.color = FrameColor; img.raycastTarget = false;
        }
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = Vector2.zero; rt.pivot = Vector2.zero;
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
}
