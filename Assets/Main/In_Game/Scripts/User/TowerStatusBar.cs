using UnityEngine;
using UnityEngine.UI;

// 설치된 포탑 머리 위 상태 표시 (이름 + 체력 바 + 체력 숫자)
//  - TowerBuilder가 포탑을 지을 때 자동으로 붙입니다. (TowerStatusBar.Attach)
//  - 항상 카메라를 바라봄 (빌보드)
//  - 체력 비율에 따라 바 색이 초록 → 노랑 → 빨강
//  * 나중에 표시할 상태(버프/디버프, 탄약, 레벨 등)는 BuildUI()에 줄을 추가하면 됩니다.
public class TowerStatusBar : MonoBehaviour
{
    // 전체 켜기/끄기 (테스트 패널 등에서 사용)
    public static bool Visible = true;

    [Tooltip("포탑 맨 위에서 이만큼 위에 표시 (미터)")]
    public float heightOffset = 0.6f;
    [Tooltip("체력 바 색의 채도를 이만큼 낮춤 (0 = 원색, 1 = 회색)")]
    [Range(0f, 1f)] public float barDesaturate = 0.3f;

    // 같은 밝기의 회색 쪽으로 섞어 채도만 낮춤
    private static Color Desaturate(Color c, float amount)
    {
        float l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
        return Color.Lerp(c, new Color(l, l, l, c.a), amount);
    }
    [Tooltip("월드 크기 배율 (0.01 = 100픽셀이 1미터)")]
    public float worldScale = 0.012f;

    const float Width = 150f;
    const float BarHeight = 7f;   // 체력 바 두께 (예전 14)

    private Health health;
    private Canvas canvas;
    private Text nameText;
    private Text hpText;
    private RectTransform hpFill;
    private Image hpFillImage;
    private float shownHp = -1f, shownMax = -1f;
    private Camera cam;

    private static Font font;

    // 포탑에 상태 표시를 붙이거나(이미 있으면 다시 연결) 이름을 갱신
    public static TowerStatusBar Attach(GameObject tower, EntityData data)
    {
        if (tower == null) return null;
        TowerStatusBar bar = tower.GetComponent<TowerStatusBar>();
        if (bar == null) bar = tower.AddComponent<TowerStatusBar>();
        string label = data != null && !string.IsNullOrEmpty(data.entityName) ? data.entityName : tower.name.Replace("(Clone)", "").Trim();
        bar.Bind(tower.GetComponent<Health>(), label);
        return bar;
    }

    private void Bind(Health h, string label)
    {
        health = h;
        if (canvas == null) BuildUI();
        nameText.text = label;
        shownHp = shownMax = -1f;
        PlaceAboveModel();
        Refresh();
    }

    void OnEnable()
    {
        if (canvas != null) canvas.gameObject.SetActive(Visible);
    }

    void LateUpdate()
    {
        if (canvas == null) return;
        bool show = Visible && (health == null || !health.IsDead);
        if (canvas.gameObject.activeSelf != show) canvas.gameObject.SetActive(show);
        if (!show) return;

        if (cam == null) cam = Camera.main;
        if (cam != null) canvas.transform.rotation = cam.transform.rotation;
        Refresh();
    }

    private void Refresh()
    {
        if (health == null)
        {
            hpText.text = "-";
            return;
        }
        float hp = health.CurrentHealth, max = Mathf.Max(1f, health.maxHealth);
        if (Mathf.Approximately(hp, shownHp) && Mathf.Approximately(max, shownMax)) return;
        shownHp = hp; shownMax = max;

        float k = Mathf.Clamp01(hp / max);
        hpFill.anchorMax = new Vector2(k, 1f);
        Color c = k > 0.5f ? Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(0.35f, 0.9f, 0.35f), (k - 0.5f) * 2f)
                           : Color.Lerp(new Color(0.95f, 0.25f, 0.2f), new Color(1f, 0.85f, 0.2f), k * 2f);
        hpFillImage.color = Desaturate(c, barDesaturate);
        hpText.text = $"{Mathf.CeilToInt(hp)} / {Mathf.CeilToInt(max)}";
    }

    // 모델의 가장 높은 지점 위에 배치 (렌더러 기준)
    private void PlaceAboveModel()
    {
        float top = transform.position.y + 1.5f;
        bool found = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is LineRenderer || r is ParticleSystemRenderer || r is TrailRenderer) continue;
            if (r.GetComponentInParent<Canvas>() != null) continue;
            top = found ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y;
            found = true;
        }
        canvas.transform.position = new Vector3(transform.position.x, top + heightOffset, transform.position.z);
    }

    private void BuildUI()
    {
        if (font == null)
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Arial" }, 32);
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        GameObject root = new GameObject("StatusBar", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.layer = 5; // UI
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;
        root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f; // 글자를 또렷하게
        RectTransform rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(Width, 52f);
        rt.localScale = Vector3.one * worldScale;

        // 이름
        nameText = MakeText(rt, "Name", 20, FontStyle.Bold, new Vector2(0f, 34f), new Vector2(Width + 60f, 24f));

        // 체력 바 (테두리 → 배경 → 채움)
        RectTransform frame = MakeRect(rt, "HpFrame", new Vector2(0f, 12f), new Vector2(Width + 4f, BarHeight + 4f), new Color(0f, 0f, 0f, 0.85f));
        RectTransform back = MakeRect(frame, "HpBack", Vector2.zero, new Vector2(Width, BarHeight), new Color(0.18f, 0.18f, 0.2f, 0.95f));
        hpFill = MakeRect(back, "HpFill", Vector2.zero, Vector2.zero, Color.green);
        hpFill.anchorMin = Vector2.zero;
        hpFill.anchorMax = Vector2.one;
        hpFill.pivot = new Vector2(0f, 0.5f);
        hpFill.offsetMin = Vector2.zero;
        hpFill.offsetMax = Vector2.zero;
        hpFillImage = hpFill.GetComponent<Image>();

        // 체력 숫자 (바 아래)
        hpText = MakeText(rt, "HpText", 16, FontStyle.Bold, new Vector2(0f, -8f), new Vector2(Width, 20f));
    }

    private static RectTransform MakeRect(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = 5;
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    private static Text MakeText(RectTransform parent, string name, int size, FontStyle style, Vector2 pos, Vector2 box)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
        go.layer = 5;
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = box;
        Text t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = Color.white;
        t.raycastTarget = false;
        Shadow sh = go.GetComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.9f);
        sh.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }
}
