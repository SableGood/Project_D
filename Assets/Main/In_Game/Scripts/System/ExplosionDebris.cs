using UnityEngine;

// v176 폭발 파편: 돌·흙 조각이 사방으로 튀어 바닥에 몇 번 튕기고 멈춤 (탄피와 같은 물리, HitEffects.DropDebris)
//  - 조각 그림은 코드로 만든 작은 도트 (채도 낮은 회색·갈색). 가로 1유닛
//  - v177 조각 크기는 일정(ShardSize), 개수 2배. 불똥(빠르게 날다 사그라짐) + 사방으로 밀려나는 연기 덩어리 추가
//  - 개수·속도는 폭발 반지름에 비례
public static class ExplosionDebris
{
    private static Sprite[] shards;
    private static readonly Color32[] Cols =
    {
        new Color32(74, 68, 62, 255), new Color32(98, 90, 80, 255), new Color32(122, 114, 102, 255), new Color32(84, 72, 60, 255),
    };
    private static readonly Color32 Edge = new Color32(33, 20, 26, 255);

    private static void Ensure()
    {
        if (shards != null) return;
        var rng = new System.Random(11);
        shards = new Sprite[8];
        for (int k = 0; k < shards.Length; k++)
        {
            int n = 4 + (k % 3);                       // 4~6 픽셀 조각
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            Color32 body = Cols[k % Cols.Length];
            Color32 light = new Color32((byte)Mathf.Min(255, body.r + 28), (byte)Mathf.Min(255, body.g + 26), (byte)Mathf.Min(255, body.b + 22), 255);
            float cx = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x - cx, dy = y - cx;
                    float r = cx + 0.4f - (float)rng.NextDouble() * 0.9f;
                    bool inside = Mathf.Abs(dx) + Mathf.Abs(dy) * (k % 2 == 0 ? 0.8f : 1.2f) <= r;
                    px[y * n + x] = inside ? (dy > 0.3f && dx < 0.3f ? light : body) : new Color32(0, 0, 0, 0);
                }
            // 바깥 테두리
            var o = (Color32[])px.Clone();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    if (px[y * n + x].a == 0) continue;
                    bool edge = x == 0 || y == 0 || x == n - 1 || y == n - 1 || px[y * n + x - 1].a == 0 || px[y * n + x + 1].a == 0 || px[(y - 1) * n + x].a == 0 || px[(y + 1) * n + x].a == 0;
                    if (edge && (x + y + k) % 3 != 0) o[y * n + x] = Edge;
                }
            tex.SetPixels32(o); tex.Apply(false, true);
            shards[k] = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n, 0, SpriteMeshType.FullRect);
            shards[k].name = "ExplosionShard_" + k;
        }
    }

    public static float ShardSize = 0.15f;      // 파편 크기 (m, 일정)
    public static float ShardCount = 18f;       // 반지름 2.5m 기준 파편 수
    public static float SparkCount = 14f;       // 불똥 수
    public static float SmokeCount = 6f;        // 연기 덩어리 수
    private static readonly Color SparkA = new Color(1f, 0.86f, 0.55f), SparkB = new Color(0.92f, 0.6f, 0.32f), SparkC = new Color(0.85f, 0.8f, 0.72f);

    private static int groundMask = -1;
    public static float FloorBelow(Vector3 p)
    {
        if (groundMask == -1) groundMask = ~LayerMask.GetMask("Mob", "Player", "Ignore Raycast", "UI");
        if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 30f, groundMask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return p.y - 0.5f;
    }

    // radius: 폭발 반지름(m)
    public static void Burst(Vector3 center, float radius)
    {
        if (!HitEffects.Enabled) return;
        Ensure();
        float floorY = FloorBelow(center);
        Vector3 start = new Vector3(center.x, Mathf.Max(center.y, floorY + 0.15f), center.z);
        float k = Mathf.Clamp(radius / 2.5f, 0.6f, 2.2f);
        int count = Mathf.RoundToInt(ShardCount * k);
        for (int i = 0; i < count; i++)
        {
            float a = (i + Random.value * 0.8f) / count * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float out_ = Random.Range(2.5f, 6.5f) * Mathf.Sqrt(k);
            float up = Random.Range(3.5f, 8f) * Mathf.Sqrt(k);
            Vector3 vel = dir * out_ + Vector3.up * up;
            Sprite sp = shards[Random.Range(0, shards.Length)];
            HitEffects.DropDebris(sp, start + dir * Random.Range(0f, 0.3f), vel, Vector3.one * ShardSize, Random.Range(0f, 360f), floorY, Random.Range(1.6f, 2.6f),
                Random.Range(300f, 720f) * (Random.value < 0.5f ? -1f : 1f));
        }

        // 불똥: 빠르게 위·바깥으로 튀고 공기 저항으로 금방 느려지며 사그라짐 (일부는 하얀 재)
        int sparks = Mathf.RoundToInt(SparkCount * k);
        for (int i = 0; i < sparks; i++)
        {
            Vector3 d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) * 1.3f + 0.15f;
            float r = Random.value;
            Color c = r < 0.45f ? SparkA : r < 0.85f ? SparkB : SparkC;
            HitEffects.Spark(start + Vector3.up * 0.2f, d.normalized * Random.Range(9f, 18f) * Mathf.Sqrt(k), c,
                Random.Range(0.06f, 0.09f), Random.Range(0.35f, 0.7f), 9f);
        }

        // 연기 덩어리: 가운데에서 사방으로 밀려나며 떠오르고 퍼짐
        int smokes = Mathf.RoundToInt(SmokeCount * k);
        for (int i = 0; i < smokes; i++)
        {
            float a = (i + Random.value * 0.7f) / smokes * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 vel = dir * Random.Range(2.5f, 4.5f) * Mathf.Sqrt(k) + Vector3.up * Random.Range(0.6f, 1.6f);
            HitEffects.Smoke(start + Vector3.up * 0.3f + dir * radius * 0.2f, vel, Random.Range(0.45f, 0.7f) * Mathf.Sqrt(k),
                Random.Range(1.1f, 1.8f), Random.Range(0.55f, 0.75f), 1.2f);
        }
    }
}
