using UnityEngine;
using UnityEngine.UI;

// v167 연속 킬: 플레이어가 몹을 처치할 때마다 +1, 5초 안에 다음 처치가 없으면 초기화
//  ■ 5킬부터 오른쪽 위(생존 시간·버전 아래)에 "N 연속 킬" 표시, 처치할 때마다 숫자가 튀며 떨리고 외곽선이 번쩍임
//  ■ 글자 밑 아주 얇은 하늘색 막대 = 남은 시간(5초)이 줄어듦. 다 줄면 콤보가 끝나고 글자가 사라짐
//  ■ 처치 판정: 마지막 공격자가 플레이어(총·수류탄·경호원) 또는 재능 피해. 포탑 처치는 세지 않음
//  * 시간은 Time.time → 일시정지 중에는 멈춤
public class KillStreak : MonoBehaviour
{
    public const float Window = 5f;   // 콤보 유지 시간
    public const int ShowFrom = 5;    // 이 킬 수부터 표시

    public static KillStreak Instance { get; private set; }
    public int Count { get; private set; }
    public int Best { get; private set; }

    private static readonly Color Sky = new Color(0.50f, 0.83f, 1f, 1f);       // 하늘색 (막대·번쩍임)
    private static readonly Color Dark = new Color(0.13f, 0.08f, 0.10f, 1f);  // #21141A

    private RectTransform box, textRt, barRt;
    private Text text;
    private Outline outline, outline2;
    private Image bar;
    private CanvasGroup group;
    private float expire, lastKill = -99f, alpha;

    public static KillStreak Create(Canvas canvas, Vector2 pos)
    {
        if (Instance != null || canvas == null) return Instance;
        GameObject go = new GameObject("KillStreak", typeof(RectTransform));
        go.transform.SetParent(canvas.rootCanvas.transform, false);
        KillStreak k = go.AddComponent<KillStreak>();
        k.Build(pos);
        return k;
    }

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }
    void OnEnable() { Health.AnyDied += OnAnyDied; }
    void OnDisable() { Health.AnyDied -= OnAnyDied; }

    private void Build(Vector2 pos)
    {
        box = (RectTransform)transform;
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(1f, 1f);
        box.anchoredPosition = pos;
        box.sizeDelta = new Vector2(320f, 56f);
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false;

        GameObject t = new GameObject("Text", typeof(RectTransform));
        t.transform.SetParent(box, false);
        textRt = (RectTransform)t.transform;
        textRt.anchorMin = textRt.anchorMax = textRt.pivot = new Vector2(1f, 1f);
        textRt.anchoredPosition = Vector2.zero;
        textRt.sizeDelta = new Vector2(320f, 46f);
        text = t.AddComponent<Text>();
        UIFont.Apply(text, FontStyle.Bold);
        text.fontSize = 22;
        text.alignment = TextAnchor.LowerRight;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = Color.white;
        outline = t.AddComponent<Outline>();
        outline.effectColor = Dark;
        outline.effectDistance = new Vector2(2f, -2f);
        outline2 = t.AddComponent<Outline>(); // 두 겹: 굵은 외곽선
        outline2.effectColor = Dark;
        outline2.effectDistance = new Vector2(1f, -1f);

        MakeBar("BarBack", new Color(0f, 0f, 0f, 0.35f));
        bar = MakeBar("Bar", Sky);
        barRt = bar.rectTransform;
    }

    private Image MakeBar(string n, Color c)
    {
        GameObject g = new GameObject(n, typeof(RectTransform));
        g.transform.SetParent(box, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-2f, -50f);
        rt.sizeDelta = new Vector2(150f, 3f);
        Image img = g.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    private void OnAnyDied(Health h)
    {
        if (h == null || h.GetComponent<MobAI>() == null) return;
        Transform a = h.LastAttacker;
        bool mine = (a != null && a.GetComponentInParent<PlayerController>() != null) || TalentEffects.InTalentDamage;
        if (!mine) return;
        if (Time.time > expire) Count = 0;
        Count++;
        if (Count > Best) Best = Count;
        expire = Time.time + Window;
        lastKill = Time.time;
        if (Count >= ShowFrom) text.text = $"<size=40>{Count}</size> 연속 킬";
    }

    void Update()
    {
        if (text == null) return;
        float left = expire - Time.time;
        if (Count > 0 && left <= 0f) Count = 0;
        bool show = Count >= ShowFrom && left > 0f;
        alpha = Mathf.MoveTowards(alpha, show ? 1f : 0f, Time.unscaledDeltaTime / (show ? 0.08f : 0.3f));
        group.alpha = alpha;
        if (alpha <= 0f) return;

        // 남은 시간 막대 (오른쪽 끝 고정, 왼쪽부터 줄어듦)
        float f = show ? Mathf.Clamp01(left / Window) : 0f;
        barRt.localScale = new Vector3(f, 1f, 1f);

        // 처치 직후: 튀어 오르고 떨림 + 외곽선 하늘색으로 번쩍
        float k = 1f - Mathf.Clamp01((Time.time - lastKill) / 0.35f);
        k *= k;
        textRt.localScale = Vector3.one * (1f + 0.4f * k);
        textRt.anchoredPosition = k > 0f ? Random.insideUnitCircle * 7f * k : Vector2.zero;
        outline.effectColor = Color.Lerp(Dark, Sky, k);
        outline.effectDistance = Vector2.Lerp(new Vector2(2f, -2f), new Vector2(3.5f, -3.5f), k);
        text.color = Color.Lerp(Color.white, new Color(1f, 0.95f, 0.6f, 1f), k);
    }
}
