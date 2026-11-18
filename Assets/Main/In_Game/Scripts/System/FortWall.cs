using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// v151 요새 입구 벽 (요새 맵 전용, FortLayout이 만듦)
//  - 처음부터 나무 벽이 서 있음. v156: 포탑처럼 우클릭으로 선택 → 위에 안내 (TowerSelection)
//      E = 다음 단계로 강화 (강화하면 체력이 가득 참) / 무너졌으면 잔해를 골라 E = 다시 짓기
//      Q = 수리 (잃은 체력 비율 × 지금 단계 값 × RepairCostRatio)
//  - 단계: 나무 → 돌 → 철 → 강철 (Levels). 값은 직업 포탑 할인(공돌이·학생)이 적용됨
//  - 벽은 포탑과 같은 Tower 레이어 + 길막(NavMeshObstacle 깎기) → 몹은 돌아갈 길이 없어 벽을 때림
//      기지 밖의 몹은 벽이 서 있는 동안 벽만 노림 (MobAI.UpdateTarget). 플레이어가 기지 밖에 나와 있으면 플레이어를 쫓음
//  - 플레이어는 자기 벽을 그냥 통과함
//  - 부서지면 쿵 + 먼지 + 낮은 잔해. 잔해를 골라 E로 나무 벽부터 다시 짓기
public class FortWall : MonoBehaviour
{
    public class Level
    {
        public string name;
        public float hp, defense;
        public int cost;
    }

    public static readonly Level[] Levels =
    {
        new Level { name = "나무 벽", hp = 800f,  defense = 2f,  cost = 80 },
        new Level { name = "돌 벽",   hp = 2000f, defense = 6f,  cost = 220 },
        new Level { name = "철 벽",   hp = 4500f, defense = 12f, cost = 480 },
        new Level { name = "강철 벽", hp = 9000f, defense = 20f, cost = 900 },
    };

    public static float Height = 1.15f, Thickness = 0.9f;   // v159 높이 반 (2.3 → 1.15)
    public static float RepairCostRatio = 0.5f;
    public static float DropHeight = 6f, DropTime = 0.18f;

    // v172 벽 여러 개: 처음 요새 입구 벽 + 플레이어가 X로 지은 벽 (전부 합쳐 MaxWalls개까지)
    public static readonly System.Collections.Generic.List<FortWall> All = new System.Collections.Generic.List<FortWall>();
    public static int MaxWalls = 2;
    public static float DemolishRefund = 0.5f;
    public static FortWall Instance => All.Count > 0 ? All[0] : null;   // 예전 코드 호환 (첫 벽)

    private Vector3 pos;
    private Quaternion rot;
    private float length;
    private int level = -1;            // -1 = 없음
    private int paid;                  // 지금까지 낸 골드 (철거 환급)
    private GameObject body;
    private Health health;
    private Material bodyMat;
    private static Texture2D[] textures;
    private readonly System.Collections.Generic.List<Vector2Int> tiles = new System.Collections.Generic.List<Vector2Int>();

    public bool IsUp => body != null && health != null && !health.IsDead;
    public Vector3 Position => pos;
    public static bool AnyStanding { get { foreach (FortWall w in All) if (w != null && w.IsUp) return true; return false; } }

    // v154 아군 탄은 벽을 통과, 적 탄은 벽에 막힘
    public static bool IsWall(Collider c)
    {
        if (c == null) return false;
        foreach (FortWall w in All) if (w != null && w.body != null && c.transform.IsChildOf(w.body.transform)) return true;
        return false;
    }
    public static bool IsWallRoot(Transform t)
    {
        if (t == null) return false;
        foreach (FortWall w in All) if (w != null && w.IsUp && t == w.body.transform) return true;
        return false;
    }
    public static bool Blocks(Vector3 p)
    {
        foreach (FortWall w in All)
        {
            if (w == null || !w.IsUp) continue;
            Collider c = w.body.GetComponent<Collider>();
            if (c != null && c.bounds.Contains(p) && (c.ClosestPoint(p) - p).sqrMagnitude < 0.0001f) return true;
        }
        return false;
    }
    public static bool BlocksSegment(Vector3 a, Vector3 b, out Vector3 hitPoint)
    {
        hitPoint = b;
        Vector3 d = b - a; float len = d.magnitude;
        if (len < 0.0001f) return false;
        bool any = false; float best = len;
        foreach (FortWall w in All)
        {
            if (w == null || !w.IsUp) continue;
            Collider c = w.body.GetComponent<Collider>();
            if (c != null && c.Raycast(new Ray(a, d / len), out RaycastHit h, best)) { best = h.distance; hitPoint = h.point; any = true; }
        }
        return any;
    }
    public static bool TileTaken(Vector2Int t)
    {
        foreach (FortWall w in All) if (w != null && w.tiles.Contains(t)) return true;
        return false;
    }

    // 맵을 새로 깔 때: 예전 벽을 모두 지우고 입구 벽 하나 (공짜 나무 벽)
    public static FortWall Create(Vector3 position, float yaw, float length)
    {
        for (int i = All.Count - 1; i >= 0; i--) if (All[i] != null) Destroy(All[i].gameObject);
        All.Clear();
        return Add(position, yaw, length, false, 0);
    }

    // 플레이어가 지은 벽 (WallBuilder) — 값은 부른 쪽에서 냄
    public static FortWall Add(Vector3 position, float yaw, float length, bool animate, int cost)
    {
        GameObject go = new GameObject("FortWallSlot");
        FortWall w = go.AddComponent<FortWall>();
        w.pos = position;
        w.rot = Quaternion.Euler(0f, yaw, 0f);
        w.length = length;
        w.paid = cost;
        go.transform.SetPositionAndRotation(position, w.rot);
        bool alongX = Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) < 45f || Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)) < 45f;
        int n = Mathf.Max(1, Mathf.RoundToInt(length / BuildGrid.Tile));
        BuildGrid.Footprint(position, alongX ? n : 1, alongX ? 1 : n, w.tiles);
        All.Add(w);
        w.Build(0, animate);
        return w;
    }

    void OnDestroy()
    {
        All.Remove(this);
        if (rubble != null) Destroy(rubble);
        if (body != null) Destroy(body);
    }

    // 몹이 노릴 벽: 플레이어가 거점(요새 포함) 안에 있고 몹은 그 밖이면, 그 거점을 둘러싼 벽 중 몹에게 가장 가까운 것
    //  플레이어가 거점 밖에 나와 있으면 null (몹은 플레이어를 쫓음)
    public static Transform TargetFor(Vector3 mobPos)
    {
        if (!FortLayout.Active || All.Count == 0) return null;
        Transform p = GameManager.Instance != null ? GameManager.Instance.playerTransform : null;
        int zone = p != null ? FortLayout.ZoneIndexOf(p.position, 0.5f) : -1;
        if (zone < 0 || FortLayout.ZoneIndexOf(mobPos, 0.5f) == zone) return null;
        Transform best = null; float bd = float.MaxValue;
        foreach (FortWall w in All)
        {
            if (w == null || !w.IsUp || !FortLayout.InZone(zone, w.pos, 2.5f)) continue;
            float d = (w.pos - mobPos).sqrMagnitude;
            if (d < bd) { bd = d; best = w.body.transform; }
        }
        return best;
    }

    // v172 철거 (G): 낸 골드의 절반 돌려받음. 입구 벽(처음부터 있던 것)은 돌려받을 골드 없음
    public void Demolish()
    {
        int refund = Mathf.FloorToInt(paid * DemolishRefund);
        if (refund > 0 && GoldManager.Instance != null) GoldManager.Instance.Add(refund);
        Puff(10, 1f);
        Sfx.Play(TowerDrop.DeploySound, 0.7f, 0.05f);
        AugmentManager.Toast(refund > 0 ? $"벽 철거 +{refund} G" : "벽 철거");
        Destroy(gameObject);
    }

    // ================= 짓기 · 강화 · 수리 =================
    private int CostOf(int lv) => Jobs.TurretPrice(Levels[lv].cost);

    private int RepairCost()
    {
        if (!IsUp) return 0;
        float missing = 1f - health.CurrentHealth / Mathf.Max(1f, health.maxHealth);
        if (missing <= 0.001f) return 0;
        return Mathf.Max(1, Mathf.CeilToInt(missing * CostOf(level) * RepairCostRatio));
    }

    private Collider ignoredPlayer;

    void Update()
    {
        // 플레이어는 자기 벽을 통과할 수 있음 (몹은 길막 때문에 못 지나감)
        Transform pl = GameManager.Instance != null ? GameManager.Instance.playerTransform : null;
        if (pl != null && body != null)
        {
            Collider pc = pl.GetComponent<CharacterController>();
            Collider bc = body.GetComponent<Collider>();
            if (pc != null && bc != null && ignoredPlayer != pc) { Physics.IgnoreCollision(pc, bc, true); ignoredPlayer = pc; }
        }
    }

    public void TryBuildOrUpgrade()
    {
        int next = IsUp ? level + 1 : 0;
        if (next >= Levels.Length) { AugmentManager.Toast("벽이 이미 최고 단계예요"); return; }
        if (!IsUp && Blocked()) { AugmentManager.Toast("입구에 무언가 있어서 지을 수 없어요"); return; }
        int cost = CostOf(next);
        if (GoldManager.Instance != null && !GoldManager.Instance.TrySpend(cost)) return;
        paid += cost;
        Build(next, true);
    }

    public void TryRepair()
    {
        int cost = RepairCost();
        if (!IsUp) { AugmentManager.Toast("무너진 벽은 E로 다시 지어야 해요"); return; }
        if (cost <= 0) { AugmentManager.Toast("벽 체력이 가득 차 있어요"); return; }
        if (GoldManager.Instance != null && !GoldManager.Instance.TrySpend(cost)) return;
        health.SetCurrentHealth(health.maxHealth);
        Sfx.Play("UI/UI_Click", 0.6f, 0f);
        Puff(6, 0.6f);
    }

    // 입구에 몹이 서 있으면 못 지음 (벽 안에 갇히지 않게. 플레이어는 벽을 통과하므로 상관없음)
    private bool Blocked()
    {
        Vector3 half = new Vector3(length * 0.5f, Height * 0.5f, Thickness * 0.5f + 0.2f);
        return Physics.CheckBox(pos + Vector3.up * Height * 0.5f, half, rot, LayerMask.GetMask("Mob"), QueryTriggerInteraction.Ignore);
    }

    private void Build(int lv, bool animate)
    {
        level = lv;
        Level L = Levels[lv];
        if (body == null || health == null || health.IsDead)
        {
            body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            int towerLayer = LayerMask.NameToLayer("Tower");
            if (towerLayer >= 0) body.layer = towerLayer;
            body.transform.SetPositionAndRotation(pos + Vector3.up * Height * 0.5f, rot);
            body.transform.localScale = new Vector3(length, Height, Thickness);
            bodyMat = MakeMaterial();
            body.GetComponent<Renderer>().sharedMaterial = bodyMat;
            NavMeshObstacle ob = body.AddComponent<NavMeshObstacle>();
            ob.shape = NavMeshObstacleShape.Box;
            ob.center = Vector3.zero;
            ob.size = Vector3.one;
            ob.carving = true;
            ob.carveOnlyStationary = false;
            health = body.AddComponent<Health>();
            health.OnDied += OnBodyDied;
            ignoredPlayer = null;   // 새 몸체 → 플레이어 통과를 다시 설정
        }
        body.name = L.name;
        ShowRubble(false);
        health.InitStats(L.hp, L.defense);
        if (bodyMat != null) bodyMat.mainTexture = WallTexture(lv);
        TowerStatusBar.Attach(body, null);
        if (animate) StartCoroutine(DropIn());
    }

    private IEnumerator DropIn()
    {
        if (body == null) yield break;
        Transform t = body.transform;
        Vector3 rest = pos + Vector3.up * Height * 0.5f;
        float t0 = Time.time;
        while (body != null)
        {
            float u = Mathf.Clamp01((Time.time - t0) / DropTime);
            t.position = rest + Vector3.up * DropHeight * (1f - u * u);
            if (u >= 1f) break;
            yield return null;
        }
        if (body == null) yield break;
        t.position = rest;
        Sfx.Play(TowerDrop.DeploySound, 0.9f, 0.05f);
        Sfx.Play(TowerDrop.AssembleSound, 0.6f, 0.05f);
        CameraController.Shake(0.12f, 0.15f);
        Puff(12, 1f);
    }

    private void OnBodyDied()
    {
        level = -1;
        Sfx.Play(TowerDrop.DeploySound, 1f, 0.05f);
        CameraController.Shake(0.2f, 0.3f);
        Puff(18, 1.3f);
        AugmentManager.Toast("벽이 무너졌어요!  잔해를 우클릭 → [E] 다시 짓기 · [G] 철거");
        body = null; health = null; bodyMat = null;   // 몸체는 Health가 지움
        ShowRubble(true);
    }

    private void Puff(int n, float scale)
    {
        Vector3 right = rot * Vector3.right, fwd = rot * Vector3.forward;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = pos + right * Random.Range(-length * 0.5f, length * 0.5f) + fwd * Random.Range(-0.6f, 0.6f) + Vector3.up * 0.15f;
            Vector3 dir = (fwd * Mathf.Sign(Random.value - 0.5f) + Vector3.up * 0.3f) * Random.Range(1f, 2f);
            HitEffects.Smoke(p, dir, Random.Range(0.2f, 0.32f) * scale, Random.Range(0.6f, 0.9f), 0.6f, 1f);
        }
    }

    // ================= 그림 =================
    private static Material MakeMaterial()
    {
        GameObject floor = GameObject.Find(PlayArea.FloorName);
        Renderer fr = floor != null ? floor.GetComponent<Renderer>() : null;
        Material m = fr != null && fr.sharedMaterial != null ? new Material(fr.sharedMaterial) : new Material(Shader.Find("Standard"));
        m.name = "FortWall (runtime)";
        m.mainTextureScale = new Vector2(3f, 2f);   // 큐브 면마다 0~1이라 긴 면에 맞춰 반복
        m.mainTextureOffset = Vector2.zero;
        if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 0f);
        return m;
    }

    private static Texture2D WallTexture(int lv)
    {
        if (textures == null) textures = new Texture2D[Levels.Length];
        if (textures[lv] != null) return textures[lv];
        const int N = 32;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "FortWall_" + lv };
        System.Random r = new System.Random(17 + lv);
        float J() => (float)r.NextDouble() * 0.08f - 0.04f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                Color c;
                switch (lv)
                {
                    case 0: // 세로 판자 + 가로 대
                    {
                        bool seam = x % 8 == 0;
                        bool bar = y >= 12 && y <= 15;
                        c = new Color(0.55f, 0.38f, 0.22f);
                        if ((x / 8) % 2 == 1) c *= 0.92f;
                        if (bar) c = new Color(0.45f, 0.3f, 0.17f);
                        if (seam && !bar) c *= 0.6f;
                        if (bar && (y == 12 || y == 15)) c *= 0.75f;
                        if (bar && (x % 8 == 4) && (y == 13 || y == 14)) c = new Color(0.25f, 0.25f, 0.27f);   // 못
                        break;
                    }
                    case 1: // 돌 벽돌
                    {
                        int row = y / 8, off = row % 2 == 0 ? 0 : 8;
                        bool mortar = y % 8 == 0 || (x + off) % 16 == 0;
                        float shade = 0.5f + 0.06f * (((x + off) / 16 + row) % 3);
                        c = mortar ? new Color(0.33f, 0.32f, 0.3f) : new Color(shade, shade * 0.97f, shade * 0.92f);
                        break;
                    }
                    case 2: // 철판 + 리벳
                    {
                        bool seam = x % 16 == 0 || y % 16 == 0;
                        bool rivet = (x % 16 == 3 || x % 16 == 13) && (y % 16 == 3 || y % 16 == 13);
                        c = new Color(0.42f, 0.44f, 0.47f);
                        if (seam) c = new Color(0.25f, 0.26f, 0.28f);
                        if (rivet) c = new Color(0.68f, 0.7f, 0.72f);
                        break;
                    }
                    default: // 강철 + 아래쪽 경고 줄무늬
                    {
                        bool seam = x % 16 == 0 || y % 16 == 0;
                        c = new Color(0.32f, 0.37f, 0.44f);
                        if (seam) c = new Color(0.18f, 0.2f, 0.24f);
                        if (y < 6) c = ((x + y) / 4) % 2 == 0 ? new Color(0.9f, 0.72f, 0.15f) : new Color(0.12f, 0.12f, 0.12f);
                        if (y == 6) c = new Color(0.15f, 0.16f, 0.18f);
                        break;
                    }
                }
                float j = J();
                t.SetPixel(x, y, new Color(Mathf.Clamp01(c.r + j), Mathf.Clamp01(c.g + j), Mathf.Clamp01(c.b + j), 1f));
            }
        t.Apply();
        textures[lv] = t;
        return t;
    }

    // ================= v156 선택 (TowerSelection: 우클릭 → Q 수리 / E 강화·다시 짓기) =================
    // 벽 자리(서 있든 무너졌든)의 상자와 광선이 만나는지. 무너졌을 때도 잔해를 눌러 고를 수 있게 콜라이더 없이 계산
    public bool Pick(Ray ray, out float dist)
    {
        dist = 0f;
        float h = IsUp ? Height : RubbleHeight;
        Vector3 center = pos + Vector3.up * h * 0.5f;
        Quaternion inv = Quaternion.Inverse(rot);
        Vector3 o = inv * (ray.origin - center), d = inv * ray.direction;
        Vector3 half = new Vector3(length * 0.5f, h * 0.5f, Thickness * 0.5f + 0.1f);
        float tMin = 0f, tMax = 500f;
        for (int i = 0; i < 3; i++)
        {
            if (Mathf.Abs(d[i]) < 1e-6f) { if (o[i] < -half[i] || o[i] > half[i]) return false; continue; }
            float t1 = (-half[i] - o[i]) / d[i], t2 = (half[i] - o[i]) / d[i];
            if (t1 > t2) { float tt = t1; t1 = t2; t2 = tt; }
            tMin = Mathf.Max(tMin, t1); tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        dist = tMin;
        return true;
    }

    // 외곽선을 씌울 그림 (서 있으면 벽, 무너졌으면 잔해)
    public GameObject Visual => IsUp ? body : rubble;
    public int CurrentLevel => IsUp ? level : -1;
    public Vector3 PromptAnchor => pos + Vector3.up * ((IsUp ? Height : RubbleHeight) + 0.5f);

    // 선택했을 때 위에 뜨는 안내 (흰색 = 가능, 회색 = 불가)
    public string PromptText()
    {
        const string ok = "#FFFFFF", no = "#7A7A7A", gold = "#FFD54A";
        int g = GoldManager.Instance != null ? GoldManager.Instance.Gold : int.MaxValue;
        string name = IsUp ? Levels[level].name : "무너진 벽";
        string hp = IsUp ? $"  <color=#A8A8A0>{Mathf.CeilToInt(health.CurrentHealth)}/{Mathf.CeilToInt(health.maxHealth)}</color>" : "";
        string line1;
        if (!IsUp) { int c = CostOf(0); line1 = $"<color={(g >= c ? ok : no)}>[E] {Levels[0].name} 다시 짓기 <color={gold}>{c} G</color></color>"; }
        else if (level + 1 < Levels.Length) { int c = CostOf(level + 1); line1 = $"<color={(g >= c ? ok : no)}>[E] {Levels[level + 1].name}으로 강화 <color={gold}>{c} G</color></color>"; }
        else line1 = $"<color={no}>[E] 강화 (최고 단계)</color>";
        int rc = RepairCost();
        string line2 = !IsUp ? $"<color={no}>[Q] 수리 (무너짐)</color>"
                     : rc > 0 ? $"<color={(g >= rc ? ok : no)}>[Q] 수리 <color={gold}>{rc} G</color></color>"
                     : $"<color={no}>[Q] 수리 (체력 가득)</color>";
        return $"<color=#C6E7A7>{name}</color>{hp}\n{line1}\n{line2}\n<color={ok}>[G] 철거</color>{(paid > 0 ? $" <color={gold}>+{Mathf.FloorToInt(paid * DemolishRefund)} G</color>" : "")}";
    }

    // ================= 무너진 자리 잔해 (고를 수 있게 낮은 돌무더기) =================
    public static float RubbleHeight = 0.35f;
    private GameObject rubble;

    private void ShowRubble(bool on)
    {
        if (on && rubble == null)
        {
            rubble = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rubble.name = "FortWall_Rubble";
            Destroy(rubble.GetComponent<Collider>());   // 길·탄을 막지 않음
            rubble.transform.SetPositionAndRotation(pos + Vector3.up * RubbleHeight * 0.5f, rot);
            rubble.transform.localScale = new Vector3(length, RubbleHeight, Thickness);
            Material m = MakeMaterial();
            m.mainTexture = WallTexture(1);
            m.color = new Color(0.55f, 0.52f, 0.5f, 1f);
            rubble.GetComponent<Renderer>().sharedMaterial = m;
        }
        if (rubble != null) rubble.SetActive(on);
    }
}
