using UnityEngine;

// 섬 지형 (강으로 둘러싸인 맵)
//  - 바닥 그림: Resources/Image/Map/Island_Field.png  (그림 전체 = 맵 전체, 가로 60m. 섬 그림의 물가 + 예전 들판 그림을 이어 붙여 넓히고, 채도를 낮춰 연하게)
//  - 깊이 지도: Resources/Map/Island_Height.bytes  (그림에서 물 부분을 찾아 미리 만든 깊이 격자. 강둑은 1.2m에 걸쳐 완만하게 파임)
//      머리: int 가로칸, int 세로칸, float 맵 가로(m), float 맵 세로(m), float 최대 깊이(m) / 이어서 칸마다 byte 깊이(0~255)
//      칸 순서: 아래(남쪽, -z)에서 위로, 왼쪽(-x)에서 오른쪽으로
//  - 두 파일이 있으면 PlayArea가 시작할 때 씬의 Floor를 이 지형으로 바꿈 (씬 파일은 그대로, 실행 중에만)
//      땅(평평한 부분) = Floor 메쉬, 강(파인 부분) = RiverBed (Ignore Raycast 레이어) + 반투명 물 면
//      몹 길찾기(내비메쉬)는 땅만으로 실행 중에 다시 구움 → 몹은 물에 들어가지 않음
//  - 그림을 바꾸려면 두 파일을 함께 바꿔야 함 (깊이 지도는 그림에서 자동으로 만듦)
public static class IslandMap
{
    public const string TexturePath = "Image/Map/Island_Field";
    public const string HeightPath = "Map/Island_Height";

    private const float WaterSurfaceDepth = 0.22f;   // 물 면 높이 (땅 아래)
    private static readonly Color WaterColor = new Color(0.36f, 0.52f, 0.64f, 0.32f);   // 바닥 그림처럼 연하게

    public static bool Active { get; private set; }
    // 맵 전체 범위 (물 포함)
    public static Bounds MapBounds => new Bounds(new Vector3(minCorner.x + worldW * 0.5f, baseY, minCorner.z + worldH * 0.5f), new Vector3(worldW, 0.1f, worldH));
    public static float MaxDepth => maxDepth;

    private static int gw, gh;
    private static float worldW, worldH, maxDepth, baseY;
    private static byte[] depth;
    private static Vector3 minCorner;   // 맵 왼쪽 아래 (x, z)

    // ── 검사 ──
    // 그 위치의 파인 깊이 (땅 = 0, 맵 밖 = 최대 깊이)
    public static float DepthAt(Vector3 p)
    {
        if (!Active) return 0f;
        float fx = (p.x - minCorner.x) / worldW * (gw - 1);
        float fz = (p.z - minCorner.z) / worldH * (gh - 1);
        if (fx < 0f || fz < 0f || fx > gw - 1 || fz > gh - 1) return maxDepth;
        int x0 = Mathf.Min((int)fx, gw - 2), z0 = Mathf.Min((int)fz, gh - 2);
        float tx = fx - x0, tz = fz - z0;
        float a = D(x0, z0), b = D(x0 + 1, z0), c = D(x0, z0 + 1), d = D(x0 + 1, z0 + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    private static float D(int x, int z) => depth[z * gw + x] / 255f * maxDepth;

    // 물 속인지 (강둑 비탈 아래쪽부터)
    public static bool IsWater(Vector3 p, float threshold = 0.12f) => Active && DepthAt(p) > threshold;

    // 사각형(가운데 c, 반쪽 크기 half) 전체가 땅인지 (포탑 설치용)
    public static bool IsLandArea(Vector3 c, float half)
    {
        if (!Active) return true;
        for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
                if (DepthAt(c + new Vector3(ix * half, 0f, iz * half)) > 0.02f) return false;
        return true;
    }

    // ── 생성 ──
    private static bool Load()
    {
        TextAsset h = Resources.Load<TextAsset>(HeightPath);
        if (h == null || h.bytes == null || h.bytes.Length < 20) return false;
        byte[] b = h.bytes;
        gw = System.BitConverter.ToInt32(b, 0);
        gh = System.BitConverter.ToInt32(b, 4);
        worldW = System.BitConverter.ToSingle(b, 8);
        worldH = System.BitConverter.ToSingle(b, 12);
        maxDepth = System.BitConverter.ToSingle(b, 16);
        if (gw < 2 || gh < 2 || b.Length < 20 + gw * gh) return false;
        depth = new byte[gw * gh];
        System.Array.Copy(b, 20, depth, 0, gw * gh);
        return true;
    }

    // Floor를 섬 지형으로 바꿈. 파일이 없으면 false (기존 평평한 바닥 그대로)
    public static bool Apply(GameObject floor)
    {
        Active = false;
        if (floor == null || !Load()) return false;
        Texture2D tex = Resources.Load<Texture2D>(TexturePath);
        if (tex == null) { Debug.LogWarning("IslandMap: 바닥 그림이 없음 (" + TexturePath + ")"); return false; }
        tex.filterMode = FilterMode.Bilinear;   // 배경은 부드럽게 (도트가 쨍하면 눈이 피로함)
        tex.wrapMode = TextureWrapMode.Clamp;

        Transform ft = floor.transform;
        baseY = ft.position.y;
        ft.rotation = Quaternion.identity;
        ft.localScale = Vector3.one;
        minCorner = new Vector3(ft.position.x - worldW * 0.5f, baseY, ft.position.z - worldH * 0.5f);
        Active = true;

        // 꼭짓점 (Floor 기준 좌표)
        Vector3[] v = new Vector3[gw * gh];
        Vector2[] uv = new Vector2[gw * gh];
        for (int z = 0; z < gh; z++)
            for (int x = 0; x < gw; x++)
            {
                int i = z * gw + x;
                v[i] = new Vector3(-worldW * 0.5f + worldW * x / (gw - 1), -D(x, z), -worldH * 0.5f + worldH * z / (gh - 1));
                uv[i] = new Vector2(x / (float)(gw - 1), z / (float)(gh - 1));
            }
        var land = new System.Collections.Generic.List<int>();
        var river = new System.Collections.Generic.List<int>();
        for (int z = 0; z < gh - 1; z++)
            for (int x = 0; x < gw - 1; x++)
            {
                int a = z * gw + x, b = a + 1, c = a + gw, d = c + 1;
                bool flat = depth[a] <= 3 && depth[b] <= 3 && depth[c] <= 3 && depth[d] <= 3;
                var list = flat ? land : river;
                list.Add(a); list.Add(c); list.Add(d);
                list.Add(a); list.Add(d); list.Add(b);
            }
        Mesh landMesh = MakeMesh("IslandLand", v, uv, land);
        Mesh riverMesh = MakeMesh("IslandRiverBed", v, uv, river);

        // 재질: Floor 재질을 복사해서 그림만 바꿈 (외곽선 없음)
        Renderer fr = floor.GetComponent<Renderer>();
        Material mat = fr != null && fr.sharedMaterial != null ? new Material(fr.sharedMaterial) : new Material(Shader.Find("Standard"));
        mat.name = "IslandField (runtime)";
        mat.mainTexture = tex;
        mat.mainTextureScale = Vector2.one;
        mat.mainTextureOffset = Vector2.zero;
        if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0f);
        if (fr != null) fr.sharedMaterial = mat;

        // 땅 = Floor 자신
        MeshFilter mf = floor.GetComponent<MeshFilter>();
        if (mf != null) mf.sharedMesh = landMesh;
        foreach (Collider col in floor.GetComponents<Collider>()) if (!(col is MeshCollider)) col.enabled = false;
        MeshCollider mc = floor.GetComponent<MeshCollider>();
        if (mc == null) mc = floor.AddComponent<MeshCollider>();
        mc.sharedMesh = landMesh;

        // 강바닥 (걸어 들어갈 수 있지만 내비메쉬·조준·설치에서는 제외되는 레이어)
        GameObject bed = new GameObject("RiverBed");
        bed.layer = 2;   // Ignore Raycast
        bed.transform.SetParent(ft, false);
        bed.AddComponent<MeshFilter>().sharedMesh = riverMesh;
        MeshRenderer br = bed.AddComponent<MeshRenderer>();
        br.sharedMaterial = mat;
        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        bed.AddComponent<MeshCollider>().sharedMesh = riverMesh;

        // 맵 바깥의 깊은 물 바닥: 강바닥과 같은 재질(같은 조명)로, 그림 가장자리 색을 바깥으로 늘려 씀 → 경계가 안 보임
        OuterBed(ft, mat, new Vector3(0f, -maxDepth - 0.002f, 0f), 400f);
        Quad(ft, "WaterSurface", new Vector3(0f, -WaterSurfaceDepth, 0f), 400f, WaterColor, true, 0f, 0f);

        RebuildNavMesh(floor);
        Debug.Log($"[섬 지형] {worldW:0.#} x {worldH:0.#} m, 최대 깊이 {maxDepth:0.##} m");
        return true;
    }

    // 맵 둘레를 감싸는 큰 판 (가운데는 비움). UV를 맵 크기 기준으로 늘려서, 그림의 가장자리 픽셀(Clamp)이 그대로 이어지게 함
    private static void OuterBed(Transform parent, Material mat, Vector3 localPos, float size)
    {
        GameObject go = new GameObject("OuterWaterBed");
        go.layer = 2;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        float h = size * 0.5f, hx = worldW * 0.5f, hz = worldH * 0.5f;
        Vector3[] v = {
            new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h),
            new Vector3(-hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz), new Vector3(hx, 0, -hz) };
        Vector2[] uv = new Vector2[v.Length];
        Vector3[] n = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) { uv[i] = new Vector2(v[i].x / worldW + 0.5f, v[i].z / worldH + 0.5f); n[i] = Vector3.up; }
        Mesh m = new Mesh { name = "OuterWaterBed" };
        m.vertices = v;
        m.uv = uv;
        m.normals = n;
        m.triangles = new[] { 0,1,5, 0,5,4,  1,2,6, 1,6,5,  2,3,7, 2,7,6,  3,0,4, 3,4,7 };   // 위에서 보이는 면
        m.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static Mesh MakeMesh(string name, Vector3[] v, Vector2[] uv, System.Collections.Generic.List<int> tris)
    {
        Mesh m = new Mesh { name = name };
        if (v.Length > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = v;
        m.uv = uv;
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // 큰 정사각형 판. hole이 0보다 크면 가운데 (holeW x holeH) 구멍을 비움 (맵 위로 겹치지 않게)
    private static void Quad(Transform parent, string name, Vector3 localPos, float size, Color color, bool transparent, float holeW, float holeH)
    {
        GameObject go = new GameObject(name);
        go.layer = 2;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        float h = size * 0.5f;
        Mesh m = new Mesh { name = name };
        if (holeW > 0f)
        {
            float hx = holeW * 0.5f, hz = holeH * 0.5f;
            // 바깥 사각형 - 안쪽 사각형 (띠 4개)
            m.vertices = new[] {
                new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h),
                new Vector3(-hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz), new Vector3(hx, 0, -hz) };
            m.triangles = new[] { 0,1,5, 0,5,4,  1,2,6, 1,6,5,  2,3,7, 2,7,6,  3,0,4, 3,4,7 };
        }
        else
        {
            m.vertices = new[] { new Vector3(-h, 0, -h), new Vector3(-h, 0, h), new Vector3(h, 0, h), new Vector3(h, 0, -h) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        }
        // 양면 (어느 쪽에서 봐도 보이게)
        int[] tr = m.triangles; int[] both = new int[tr.Length * 2];
        for (int i = 0; i < tr.Length; i += 3)
        {
            both[i] = tr[i]; both[i + 1] = tr[i + 1]; both[i + 2] = tr[i + 2];
            both[tr.Length + i] = tr[i]; both[tr.Length + i + 1] = tr[i + 2]; both[tr.Length + i + 2] = tr[i + 1];
        }
        m.triangles = both;
        m.RecalculateNormals();
        m.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        // 불투명 바닥은 깊이를 쓰는 셰이더(Unlit/Color)여야 스카이박스가 그 위에 그려지지 않음
        Shader sh = transparent ? Shader.Find("Sprites/Default") : Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        Material mat = new Material(sh) { name = name + " (runtime)" };
        mat.color = color;
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    // 씬 Floor의 NavMeshSurface(AI Navigation 패키지)로 땅만 다시 구움. 강(Ignore Raycast 레이어)은 제외
    private static void RebuildNavMesh(GameObject floor)
    {
        Component surface = null;
        foreach (MonoBehaviour mb in floor.GetComponents<MonoBehaviour>())
            if (mb != null && mb.GetType().Name == "NavMeshSurface") { surface = mb; break; }
        if (surface == null) { Debug.LogWarning("IslandMap: Floor에 NavMeshSurface가 없어 내비메쉬를 다시 굽지 못함"); return; }
        System.Type t = surface.GetType();
        LayerMask mask = ~(1 << 2);
        var layerProp = t.GetProperty("layerMask");
        if (layerProp != null && layerProp.CanWrite) layerProp.SetValue(surface, mask);
        else
        {
            var layerField = t.GetField("layerMask") ?? t.GetField("m_LayerMask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (layerField != null) layerField.SetValue(surface, mask);
        }
        var build = t.GetMethod("BuildNavMesh", System.Type.EmptyTypes);
        if (build != null) build.Invoke(surface, null);
        else Debug.LogWarning("IslandMap: NavMeshSurface.BuildNavMesh를 찾지 못함");
    }
}
