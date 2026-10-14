using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// 플레이 구역 (씬의 'Floor' 크기 = 이동 가능한 범위)
//  - 플레이어·몹·포탑이 Floor 밖으로 나가지 못하게 막음
//      플레이어: 보이지 않는 벽(충돌체) + 위치 보정 / 몹: 0.2초마다 밖에 있으면 안으로 되돌림 / 포탑: 밖에는 설치 불가
//  - 경계: 노란 선
//  - 바깥: Floor와 같은 텍스처의 넓은 바닥(자연스럽게 이어짐) + 빨간 빗금(투명도 60%)
//  - 씬에 'Floor'가 있으면 실행 시 자동으로 생깁니다. (씬 파일은 수정하지 않음, 바깥 바닥도 실행 중에만 생성)
//  * Floor 크기를 바꾸면 내비메쉬(몹 길찾기)도 다시 구워야 몹이 새 범위를 모두 다닙니다.
public class PlayArea : MonoBehaviour
{
    public static PlayArea Instance { get; private set; }

    public const string FloorName = "Floor";

    [Header("바깥 바닥")]
    [Tooltip("바깥 바닥 한 변의 길이(미터). Unity 기본 Plane 기준 스케일 16 = 160m")]
    public float outerSize = 160f;

    [Header("경계선")]
    public Color borderColor = new Color(1f, 0.85f, 0.1f, 1f);
    public float borderWidth = 0.15f;

    [Header("바깥 빗금")]
    public Color hatchColor = new Color(0.9f, 0.1f, 0.1f, 0.6f);
    [Tooltip("빗금 한 칸의 크기(미터)")]
    public float hatchPeriod = 1.2f;

    [Header("이동 제한")]
    [Tooltip("경계에서 이만큼 안쪽까지만 이동 (몸 두께)")]
    public float edgeMargin = 0.4f;

    public Bounds Area { get; private set; }
    private float groundY;
    private float nextAgentCheck;
    private Transform player;
    private CharacterController playerCC;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryCreate();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => TryCreate();

    private static void TryCreate()
    {
        if (Instance != null) return;
        GameObject floor = GameObject.Find(FloorName);
        if (floor == null) return;
        Renderer r = floor.GetComponent<Renderer>();
        if (r == null) return;
        GameObject go = new GameObject("PlayArea");
        Instance = go.AddComponent<PlayArea>();
        Instance.Build(floor, r);
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ------------------------------------------------------------------
    // 외부에서 쓰는 검사
    // ------------------------------------------------------------------
    // 위치가 구역 안인지 (margin만큼 안쪽 기준). 구역이 없으면 항상 true
    public static bool IsInside(Vector3 p, float margin = 0f)
    {
        if (Instance == null) return true;
        Bounds b = Instance.Area;
        return p.x >= b.min.x + margin && p.x <= b.max.x - margin && p.z >= b.min.z + margin && p.z <= b.max.z - margin;
    }

    public static Vector3 Clamp(Vector3 p, float margin = 0f)
    {
        if (Instance == null) return p;
        Bounds b = Instance.Area;
        p.x = Mathf.Clamp(p.x, b.min.x + margin, b.max.x - margin);
        p.z = Mathf.Clamp(p.z, b.min.z + margin, b.max.z - margin);
        return p;
    }

    // ------------------------------------------------------------------
    void LateUpdate()
    {
        // 플레이어: 벽을 뚫고 나간 경우(넉백·순간이동 등) 안으로 되돌림
        if (player == null && GameManager.Instance != null && GameManager.Instance.playerTransform != null)
        {
            player = GameManager.Instance.playerTransform;
            playerCC = player.GetComponent<CharacterController>();
        }
        if (player != null && !IsInside(player.position, edgeMargin * 0.5f))
        {
            bool cc = playerCC != null && playerCC.enabled;
            if (cc) playerCC.enabled = false;
            player.position = Clamp(player.position, edgeMargin);
            if (cc) playerCC.enabled = true;
        }

        // 몹: 길찾기는 내비메쉬 안에서만 하지만, 넉백 등으로 밀려난 경우 되돌림
        if (Time.time >= nextAgentCheck)
        {
            nextAgentCheck = Time.time + 0.2f;
            foreach (NavMeshAgent a in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
            {
                if (a == null || !a.isActiveAndEnabled) continue;
                Vector3 p = a.transform.position;
                if (IsInside(p, 0f)) continue;
                Vector3 c = Clamp(p, edgeMargin);
                if (a.isOnNavMesh) a.Warp(c); else a.transform.position = c;
            }
        }
    }

    // ------------------------------------------------------------------
    // 생성
    // ------------------------------------------------------------------
    private void Build(GameObject floor, Renderer floorRenderer)
    {
        Area = floorRenderer.bounds;
        groundY = Area.max.y;
        Vector3 c = Area.center;
        float sx = Area.size.x, sz = Area.size.z;

        BuildWalls(c, sx, sz);
        BuildOuterFloor(floor, floorRenderer, c, sx, sz);
        BuildHatch(c, sx, sz);
        BuildBorder(c, sx, sz);
        Debug.Log($"[구역] 플레이 구역 {sx:0.#} x {sz:0.#} m (Floor 기준), 바깥 바닥 {outerSize:0}m");
    }

    // 보이지 않는 벽 4개 (플레이어 이동 차단). Ignore Raycast 레이어라 조준·설치 레이에는 걸리지 않음
    private void BuildWalls(Vector3 c, float sx, float sz)
    {
        const float thick = 2f, height = 12f;
        Vector3[] pos =
        {
            new Vector3(c.x, groundY + height * 0.5f, Area.max.z + thick * 0.5f),
            new Vector3(c.x, groundY + height * 0.5f, Area.min.z - thick * 0.5f),
            new Vector3(Area.max.x + thick * 0.5f, groundY + height * 0.5f, c.z),
            new Vector3(Area.min.x - thick * 0.5f, groundY + height * 0.5f, c.z),
        };
        Vector3[] size =
        {
            new Vector3(sx + thick * 2f, height, thick), new Vector3(sx + thick * 2f, height, thick),
            new Vector3(thick, height, sz + thick * 2f), new Vector3(thick, height, sz + thick * 2f),
        };
        for (int i = 0; i < 4; i++)
        {
            GameObject w = new GameObject("Wall_" + i);
            w.layer = 2; // Ignore Raycast
            w.transform.SetParent(transform, false);
            w.transform.position = pos[i];
            BoxCollider b = w.AddComponent<BoxCollider>();
            b.size = size[i];
        }
    }

    // 바깥 바닥: Floor와 같은 메쉬·머티리얼을 크게 깔고, 무늬가 Floor와 이어지도록 반복 횟수/위치를 맞춤
    private void BuildOuterFloor(GameObject floor, Renderer floorRenderer, Vector3 c, float sx, float sz)
    {
        MeshFilter mf = floor.GetComponent<MeshFilter>();
        if (mf == null || floorRenderer.sharedMaterial == null) return;

        GameObject outer = new GameObject("OuterFloor");
        outer.layer = floor.layer;
        outer.transform.SetParent(transform, false);
        outer.transform.position = new Vector3(c.x, groundY - 0.01f, c.z); // Floor보다 살짝 아래 (겹침 깜빡임 방지)
        outer.transform.rotation = floor.transform.rotation;
        float fx = outerSize / Mathf.Max(0.01f, sx), fz = outerSize / Mathf.Max(0.01f, sz);
        Vector3 fs = floor.transform.lossyScale;
        outer.transform.localScale = new Vector3(fs.x * fx, fs.y, fs.z * fz);
        outer.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;

        MeshRenderer mr = outer.AddComponent<MeshRenderer>();
        Material m = new Material(floorRenderer.sharedMaterial) { name = "OuterFloor (runtime)" };
        Vector2 t = m.mainTextureScale, o = m.mainTextureOffset;
        Vector2 tOut = new Vector2(t.x * fx, t.y * fz);
        m.mainTextureScale = tOut;
        m.mainTextureOffset = new Vector2(o.x + 0.5f * (t.x - tOut.x), o.y + 0.5f * (t.y - tOut.y));
        if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 0f);
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
    }

    // 바깥 빨간 빗금: 구역 둘레를 감싸는 4개의 띠, 빗금은 월드 좌표 기준이라 띠끼리 이어짐
    private void BuildHatch(Vector3 c, float sx, float sz)
    {
        float half = outerSize * 0.5f;
        float x0 = c.x - half, x1 = c.x + half, z0 = c.z - half, z1 = c.z + half;
        float ix0 = Area.min.x, ix1 = Area.max.x, iz0 = Area.min.z, iz1 = Area.max.z;
        float y = groundY + 0.02f;

        Vector2[][] rects =
        {
            new[] { new Vector2(x0, iz1), new Vector2(x1, z1) },  // 위
            new[] { new Vector2(x0, z0), new Vector2(x1, iz0) },  // 아래
            new[] { new Vector2(x0, iz0), new Vector2(ix0, iz1) }, // 왼쪽
            new[] { new Vector2(ix1, iz0), new Vector2(x1, iz1) }, // 오른쪽
        };
        Vector3[] v = new Vector3[16]; Vector2[] uv = new Vector2[16]; int[] tri = new int[24];
        for (int i = 0; i < 4; i++)
        {
            Vector2 a = rects[i][0], b = rects[i][1];
            int k = i * 4;
            v[k] = new Vector3(a.x, y, a.y); v[k + 1] = new Vector3(a.x, y, b.y); v[k + 2] = new Vector3(b.x, y, b.y); v[k + 3] = new Vector3(b.x, y, a.y);
            for (int j = 0; j < 4; j++) uv[k + j] = new Vector2(v[k + j].x, v[k + j].z) / hatchPeriod;
            int ti = i * 6;
            tri[ti] = k; tri[ti + 1] = k + 1; tri[ti + 2] = k + 2; tri[ti + 3] = k; tri[ti + 4] = k + 2; tri[ti + 5] = k + 3;
        }
        Mesh mesh = new Mesh { name = "OutsideHatch", vertices = v, uv = uv, triangles = tri };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();

        GameObject go = new GameObject("OutsideHatch");
        go.layer = 2;
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        Shader sh = Shader.Find("Unlit/Transparent");
        Material mat = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { name = "Hatch (runtime)" };
        mat.mainTexture = MakeHatchTexture(hatchColor);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // 16x16 도트 빗금 (대각선 줄무늬, 줄 = hatchColor, 사이 = 투명)
    private static Texture2D MakeHatchTexture(Color stripe)
    {
        const int N = 16;
        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "Hatch" };
        Color clear = new Color(stripe.r, stripe.g, stripe.b, 0f);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                tex.SetPixel(x, y, ((x + y) % N) < 5 ? stripe : clear);
        tex.Apply();
        return tex;
    }

    // 노란 경계선
    private void BuildBorder(Vector3 c, float sx, float sz)
    {
        GameObject go = new GameObject("Border");
        go.layer = 2;
        go.transform.SetParent(transform, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = 4;
        float y = groundY + 0.04f;
        lr.SetPosition(0, new Vector3(Area.min.x, y, Area.min.z));
        lr.SetPosition(1, new Vector3(Area.min.x, y, Area.max.z));
        lr.SetPosition(2, new Vector3(Area.max.x, y, Area.max.z));
        lr.SetPosition(3, new Vector3(Area.max.x, y, Area.min.z));
        lr.widthMultiplier = borderWidth;
        lr.alignment = LineAlignment.TransformZ;              // 바닥에 눕힘
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        lr.numCornerVertices = 0;
        lr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Border (runtime)" };
        lr.startColor = lr.endColor = borderColor;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }
}
