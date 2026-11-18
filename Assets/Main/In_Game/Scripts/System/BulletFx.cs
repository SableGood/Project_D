using System.Collections.Generic;
using UnityEngine;

// 빠르게 날아가는 총탄 (v105)
//  예전의 '총구에서 끝까지 한 번에 그어지는 빛줄기(예광탄)' 대신, 짧은 탄 하나가 총구에서 맞은 곳까지 아주 빠르게 날아감
//  - 판정은 예전처럼 쏘는 순간 바로 (그림만 따라감)
//  - 머리(앞)는 밝은 노란빛 흰색, 꼬리로 갈수록 투명
public class BulletFx : MonoBehaviour
{
    public static float Speed = 140f;      // 초당 미터
    public static float Length = 0.55f;    // 탄 그림 길이 (꼬리 포함)
    public static float Width = 0.035f;
    public static Color Head = new Color(1f, 0.96f, 0.78f, 1f);
    public static Color Tail = new Color(1f, 0.72f, 0.3f, 0f);

    // v158 플레이어 총탄은 더 잘 보이게: 더 길고 굵고 조금 느리게 + 바깥 빛(넓고 옅은 줄) 한 겹
    public static float PlayerLength = 1.1f, PlayerWidthMul = 1.8f, PlayerWidthMulFps = 1.2f, PlayerSpeed = 115f;
    public static float GlowWidthMul = 3.2f, GlowAlpha = 0.35f;
    public static Color PlayerHead = new Color(1f, 1f, 0.9f, 1f);
    public static Color PlayerTail = new Color(1f, 0.8f, 0.35f, 0f);
    public static Color GlowColor = new Color(1f, 0.78f, 0.35f, 1f);

    private class Shot { public LineRenderer lr, glow; public Vector3 from, dir; public float dist, travel, speed, length, trail; public Color trailColor; }
    private class Linger { public LineRenderer lr; public float life, max; public Color col; }
    private readonly List<Linger> lingers = new List<Linger>();

    // v175 무기 분류별 탄 (A안 탄종 차이 + B안 분류 색을 채도 절반으로)
    //  len = 탄 길이(m), w = 굵기 배율(기준 돌격소총 1), speed = m/s, glow = 바깥 빛 진하기, trail = 맞은 뒤 남는 잔상 줄(초)
    public class Style { public float len, w, speed, glow, trail; public Color head, tail; public int tracerEvery; public Color tracerHead, tracerTail; }
    public static float ColorSaturation = 0.5f;   // 분류 색 채도 (1 = B안 그대로)
    private static Dictionary<string, Style> styles;
    private static readonly Dictionary<string, int> shotCount = new Dictionary<string, int>();
    private static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out Color c); return c; }
    private static Color Sat(Color c)
    {
        float l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
        Color g = new Color(l, l, l, c.a);
        return Color.Lerp(g, c, ColorSaturation);
    }
    private static Style S(float len, float w, float speed, float glow, string head, string tail, float trail = 0f)
        => new Style { len = len, w = w, speed = speed, glow = glow, trail = trail, head = Sat(H(head)), tail = Sat(H(tail)) };
    public static Style StyleFor(string category)
    {
        if (string.IsNullOrEmpty(category)) return null;
        if (styles == null)
        {
            styles = new Dictionary<string, Style>
            {
                { "HG",  S(0.7f, 0.8f, 100f, 0.30f, "#fff4cf", "#ffc24a") },          // 권총: 짧고 통통, 노랑
                { "SMG", S(0.5f, 0.6f, 125f, 0.20f, "#f2ffd6", "#a8e85c") },          // 기관단총: 가늘고 짧게, 연두
                { "AR",  S(1.1f, 1.0f, 130f, 0.35f, "#fffff0", "#ffcc59") },          // 돌격소총: 기준, 호박색
                { "BR",  S(1.4f, 1.25f, 145f, 0.45f, "#ffe8d6", "#ff7a2e") },         // 전투소총: 굵게, 주황
                { "MG",  S(1.3f, 1.1f, 125f, 0.35f, "#ffd6cc", "#ff4a3a") },          // 기관총: 빨강 + 3발마다 진한 예광탄
                { "RF",  S(2.6f, 1.45f, 230f, 0.55f, "#ffffff", "#8fd2ff", 0.35f) },  // 저격총: 길고 빠르게, 얼음 파랑 + 잔상 줄
                { "SG",  S(0.34f, 0.55f, 110f, 0.20f, "#fff6d0", "#ffd23f") },        // 산탄총: 짧은 알갱이, 금색
            };
            Style mg = styles["MG"];
            mg.tracerEvery = 3; mg.tracerHead = H("#ffd0c0"); mg.tracerTail = H("#ff3a22");
        }
        return styles.TryGetValue(category.Trim().ToUpper(), out Style st) ? st : null;
    }

    private static BulletFx instance;
    private static Material mat;
    private readonly List<Shot> active = new List<Shot>();
    private readonly Stack<LineRenderer> pool = new Stack<LineRenderer>();

    public static void Spawn(Vector3 from, Vector3 to, float widthMul = 1f, bool player = false, string category = null)
    {
        Vector3 d = to - from;
        float dist = d.magnitude;
        if (dist < 0.3f) return; // 바로 앞에서 맞음 → 탄 그림 없이 타격만
        if (instance == null)
        {
            GameObject go = new GameObject("BulletFx");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BulletFx>();
        }
        instance.Add(from, d / dist, dist, widthMul, player, player ? StyleFor(category) : null, category);
    }

    private void Add(Vector3 from, Vector3 dir, float dist, float widthMul, bool player, Style st, string category)
    {
        if (active.Count > 120) return;
        float len = st != null ? st.len : player ? PlayerLength : Length;
        float w = Width * Mathf.Clamp(widthMul, 0.3f, 2.5f) * (st != null ? st.w : 1f);
        if (player) w *= CameraController.FirstPersonView ? PlayerWidthMulFps : PlayerWidthMul;
        Color headC = player ? PlayerHead : Head, tailC = player ? PlayerTail : Tail, glowC = GlowColor;
        float glowA = GlowAlpha;
        if (st != null)
        {
            headC = st.head; tailC = st.tail; tailC.a = 0f; glowC = st.tail; glowA = st.glow;
            if (st.tracerEvery > 0)
            {
                shotCount.TryGetValue(category, out int n); n++; shotCount[category] = n;
                if (n % st.tracerEvery == 0) { headC = st.tracerHead; tailC = st.tracerTail; tailC.a = 0f; glowC = st.tracerTail; glowA = Mathf.Min(1f, st.glow * 1.7f); }
            }
        }
        LineRenderer lr = Take(tailC, headC, w, 1);
        LineRenderer glow = null;
        if (player)
        {
            Color g0 = glowC; g0.a = 0f;
            Color g1 = glowC; g1.a = glowA;
            glow = Take(g0, g1, w * GlowWidthMul, 0);
        }
        float first = Mathf.Min(dist, len * 0.5f);   // 첫 프레임: 총구 화염 바로 앞에 보임
        foreach (LineRenderer l in new[] { lr, glow })
        {
            if (l == null) continue;
            l.SetPosition(0, from);
            l.SetPosition(1, from + dir * first);
            l.enabled = true;
        }
        float spd = st != null ? st.speed : player ? PlayerSpeed : Speed;
        active.Add(new Shot { lr = lr, glow = glow, from = from, dir = dir, dist = dist, travel = first, speed = spd, length = len,
                              trail = st != null ? st.trail : 0f, trailColor = st != null ? st.tail : Color.clear });
    }

    private LineRenderer Take(Color tail, Color head, float width, int order)
    {
        LineRenderer lr = pool.Count > 0 ? pool.Pop() : Create();
        lr.sortingOrder = order;   // 바깥 빛(0) 위에 탄 심(1)
        lr.startColor = tail;   // 0 = 꼬리
        lr.endColor = head;     // 1 = 머리
        lr.widthMultiplier = width;
        return lr;
    }

    private LineRenderer Create()
    {
        if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
        GameObject go = new GameObject("Bullet");
        go.layer = 2; // Ignore Raycast
        go.transform.SetParent(transform, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.sharedMaterial = mat;
        lr.numCapVertices = 2;
        lr.startColor = Tail; // 0 = 꼬리
        lr.endColor = Head;   // 1 = 머리
        lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 1f));
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        for (int i = lingers.Count - 1; i >= 0; i--)
        {
            Linger g = lingers[i];
            g.life -= dt;
            if (g.life <= 0f) { g.lr.enabled = false; pool.Push(g.lr); lingers.RemoveAt(i); continue; }
            Color c = g.col; c.a = 0.4f * g.life / g.max;
            g.lr.startColor = g.lr.endColor = c;
        }
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Shot s = active[i];
            s.travel += s.speed * dt;
            float head = Mathf.Min(s.travel, s.dist);
            float tail = Mathf.Max(0f, s.travel - s.length);
            if (tail >= s.dist)
            {
                if (s.trail > 0f)
                {
                    // 저격총 잔상 줄: 총구~맞은 곳 가는 줄이 잠깐 남았다 사라짐
                    LineRenderer ln = Take(s.trailColor, s.trailColor, s.lr.widthMultiplier * 0.35f, 0);
                    ln.SetPosition(0, s.from); ln.SetPosition(1, s.from + s.dir * s.dist); ln.enabled = true;
                    lingers.Add(new Linger { lr = ln, life = s.trail, max = s.trail, col = s.trailColor });
                }
                s.lr.enabled = false;
                pool.Push(s.lr);
                if (s.glow != null) { s.glow.enabled = false; pool.Push(s.glow); }
                active.RemoveAt(i);
                continue;
            }
            s.lr.SetPosition(0, s.from + s.dir * tail);
            s.lr.SetPosition(1, s.from + s.dir * head);
            if (s.glow != null)
            {
                s.glow.SetPosition(0, s.from + s.dir * tail);
                s.glow.SetPosition(1, s.from + s.dir * head);
            }
        }
    }
}
