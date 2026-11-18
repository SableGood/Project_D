using System.Collections.Generic;
using UnityEngine;

// v181 바닥에 눕는 표시 두 가지
//  1) 착탄 구역 (노란 원): 폭발 무기(알라·엔비)를 쏘면 터질 자리에 폭발 범위만큼 표시 → 터지거나 사라지면 지움
//  2) 폭발 흔적 (그을음): 터진 자리에 5~7초 남았다가 서서히 사라짐. 그림: Resources/Image/FX/Scorch/Scorch_S1·S2 (작은 폭발), L1·L2 (큰 폭발)
//  - 바닥에 평평하게 눕힌 스프라이트. 도트 크기는 폭발 범위에 맞춰 늘어남
//  - 멀티 대비: 화면 연출만 (게임 상태 없음)
public class GroundMarks : MonoBehaviour
{
    public static bool Enabled = true;
    public static Color MarkerColor = new Color(1f, 0.85f, 0.25f, 1f);
    public static float ScorchLifeMin = 5f, ScorchLifeMax = 7f, ScorchFade = 1.5f;
    public static float ScorchCover = 0.4f;      // 그을음 그림(내용)의 폭 = 폭발 지름 × 이 값
    public static float ScorchAlpha = 0.7f;      // v182 그을음 불투명도 (너무 눈에 띄지 않게)
    public static float LargeRadius = 3f;         // 이 반지름 이상이면 큰 그을음
    public static int MaxScorches = 24;

    private class Mark { public SpriteRenderer sr; public float age, life; public bool scorch; public Color col; }
    private static GroundMarks instance;
    private static Material mat;
    private static Sprite ring;
    private static Sprite[] small, large;
    private readonly List<Mark> marks = new List<Mark>();
    private readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
    private static readonly Dictionary<GameObject, Mark> byGo = new Dictionary<GameObject, Mark>();

    private static GroundMarks Inst()
    {
        if (instance == null)
        {
            GameObject go = new GameObject("GroundMarks");
            instance = go.AddComponent<GroundMarks>();
        }
        return instance;
    }

    private static void EnsureAssets()
    {
        if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
        if (ring == null) ring = MakeRing(96);
        if (small == null) small = LoadSet("Scorch_S1", "Scorch_S2");
        if (large == null) large = LoadSet("Scorch_L1", "Scorch_L2");
    }

    private static Sprite[] LoadSet(params string[] names)
    {
        var list = new List<Sprite>();
        foreach (string n in names)
        {
            Texture2D t = Resources.Load<Texture2D>("Image/FX/Scorch/" + n);
            if (t == null) continue;
            t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp;
            list.Add(Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect));
        }
        return list.ToArray();
    }

    // 도트 원: 2칸 테두리 + 옅은 속 (가로 1유닛 = 지름)
    private static Sprite MakeRing(int n)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        float c = (n - 1) * 0.5f, R = n * 0.5f - 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                byte a = 0;
                if (d <= R && d > R - 2.2f) a = 235;                 // 테두리
                else if (d <= R - 2.2f && d > R - 3.6f) a = 90;      // 테두리 안쪽 그림자
                else if (d <= R - 3.6f) a = 38;                       // 속
                px[y * n + x] = new Color32(255, 255, 255, a);
            }
        tex.SetPixels32(px); tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n, 0, SpriteMeshType.FullRect);
    }

    private SpriteRenderer Take()
    {
        SpriteRenderer sr = pool.Count > 0 ? pool.Pop() : null;
        if (sr == null)
        {
            GameObject go = new GameObject("GroundMark");
            go.layer = 2; // Ignore Raycast
            go.transform.SetParent(transform, false);
            sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = mat;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
        }
        sr.gameObject.SetActive(true);
        return sr;
    }

    private void Give(Mark m)
    {
        if (m.sr == null) return;
        byGo.Remove(m.sr.gameObject);
        m.sr.gameObject.SetActive(false);
        pool.Push(m.sr);
    }

    // 착탄 구역 표시. 돌려받은 것을 HideMarker에 넘겨 지움 (안 지우면 maxLife 뒤 자동으로 사라짐)
    public static GameObject ShowMarker(Vector3 point, float radius, float maxLife = 4f)
    {
        if (!Enabled || radius <= 0f) return null;
        GroundMarks g = Inst(); EnsureAssets();
        SpriteRenderer sr = g.Take();
        sr.sprite = ring;
        sr.sortingOrder = 6;
        float y = ExplosionDebris.FloorBelow(point) + 0.04f;
        sr.transform.SetPositionAndRotation(new Vector3(point.x, y, point.z), Quaternion.Euler(90f, 0f, 0f));
        sr.transform.localScale = Vector3.one * radius * 2f;
        var m = new Mark { sr = sr, life = maxLife, col = MarkerColor };
        sr.color = MarkerColor;
        g.marks.Add(m); byGo[sr.gameObject] = m;
        return sr.gameObject;
    }

    public static void HideMarker(GameObject marker)
    {
        if (marker == null || instance == null || !byGo.TryGetValue(marker, out Mark m)) return;
        m.age = m.life; // 다음 갱신에서 치움
    }

    // 폭발 흔적
    public static void Scorch(Vector3 point, float radius)
    {
        if (!Enabled || radius <= 0f) return;
        GroundMarks g = Inst(); EnsureAssets();
        bool big = radius >= LargeRadius;
        Sprite[] set = big && large.Length > 0 ? large : small.Length > 0 ? small : large;
        if (set == null || set.Length == 0) return;
        // 가장 오래된 그을음부터 정리
        int count = 0; foreach (Mark k in g.marks) if (k.scorch) count++;
        if (count >= MaxScorches) foreach (Mark k in g.marks) if (k.scorch) { k.age = Mathf.Max(k.age, k.life - 0.3f); break; }

        Sprite sp = set[Random.Range(0, set.Length)];
        float contentPx = sp.rect.width * (big ? 0.84f : 0.68f);   // 그림 안 실제 그을음 폭 (여백 제외)
        float mpp = radius * 2f * ScorchCover / contentPx;
        SpriteRenderer sr = g.Take();
        sr.sprite = sp;
        sr.sortingOrder = -4;
        float y = ExplosionDebris.FloorBelow(point) + 0.02f + Random.Range(0f, 0.01f);
        sr.transform.SetPositionAndRotation(new Vector3(point.x, y, point.z), Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
        float flip = Random.value < 0.5f ? -1f : 1f;
        sr.transform.localScale = new Vector3(mpp * flip, mpp, mpp);
        var m = new Mark { sr = sr, life = Random.Range(ScorchLifeMin, ScorchLifeMax), scorch = true, col = new Color(1f, 1f, 1f, ScorchAlpha) };
        sr.color = m.col;
        g.marks.Add(m);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            Mark m = marks[i];
            m.age += dt;
            if (m.sr == null || m.age >= m.life) { Give(m); marks.RemoveAt(i); continue; }
            Color c = m.col;
            if (m.scorch)
            {
                float left = m.life - m.age;
                c.a = m.col.a * (left < ScorchFade ? left / ScorchFade : 1f);
            }
            else
            {
                // 착탄 표시: 나타날 때 살짝 커지며 진해지고, 은은하게 깜빡
                float k = Mathf.Min(1f, m.age / 0.12f);
                c.a = Mathf.Lerp(0.3f, 1f, k) * (0.8f + 0.2f * Mathf.Sin(m.age * 14f));
            }
            m.sr.color = c;
        }
    }

    void OnDestroy() { if (instance == this) { instance = null; byGo.Clear(); } }
}
