using System.Collections.Generic;
using UnityEngine;

// v172 건설 타일: 1칸 = 1m. 맵 가운데 기준 정수 좌표가 칸 가운데 (fort_map_gen.py의 절벽·해자도 이 칸에 맞춰 그림)
//  - 포탑 2×2칸, 벽 3×1칸. 칸이 비어 있으려면: 땅(물·절벽 아님) + 플레이 구역 안 + 포탑·벽이 차지하지 않음
//  - BuildGridView: 건설 중 커서 주변 칸을 바닥에 그림 (빈 칸 흐린 흰색, 막힌 칸 빨강, 놓을 자리 초록/빨강)
public static class BuildGrid
{
    public const float Tile = 1f;
    public static System.Func<Vector2Int, bool> TowerTaken;   // TowerBuilder가 채움

    private static readonly Dictionary<Vector2Int, bool> terrainCache = new Dictionary<Vector2Int, bool>();
    private static int cacheScene = -1;

    private static Vector3 Origin
    {
        get
        {
            if (!IslandMap.Active) return Vector3.zero;
            Vector3 c = IslandMap.MapBounds.center;
            return new Vector3(c.x, 0f, c.z);
        }
    }

    public static Vector2Int TileOf(Vector3 p)
    {
        Vector3 o = Origin;
        return new Vector2Int(Mathf.RoundToInt((p.x - o.x) / Tile), Mathf.RoundToInt((p.z - o.z) / Tile));
    }

    public static Vector3 CenterOf(Vector2Int t)
    {
        Vector3 o = Origin;
        return new Vector3(o.x + t.x * Tile, 0f, o.z + t.y * Tile);
    }

    // w×h칸 자리: p에 가장 가까운 자리의 칸 목록과 가운데 (짝수 칸이면 가운데가 칸 경계)
    public static Vector3 Footprint(Vector3 p, int w, int h, List<Vector2Int> tiles)
    {
        Vector3 o = Origin;
        int x0 = Mathf.RoundToInt((p.x - o.x) / Tile - (w - 1) * 0.5f);
        int z0 = Mathf.RoundToInt((p.z - o.z) / Tile - (h - 1) * 0.5f);
        tiles.Clear();
        for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) tiles.Add(new Vector2Int(x0 + i, z0 + j));
        return new Vector3(o.x + (x0 + (w - 1) * 0.5f) * Tile, p.y, o.z + (z0 + (h - 1) * 0.5f) * Tile);
    }

    public static bool TerrainOk(Vector2Int t)
    {
        int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (scene != cacheScene) { terrainCache.Clear(); cacheScene = scene; }
        if (terrainCache.TryGetValue(t, out bool ok)) return ok;
        Vector3 c = CenterOf(t);
        ok = IslandMap.IsLandArea(c, Tile * 0.42f) && PlayArea.IsInside(c, Tile * 0.42f);
        terrainCache[t] = ok;
        return ok;
    }

    public static bool Taken(Vector2Int t) => (TowerTaken != null && TowerTaken(t)) || FortWall.TileTaken(t);
    public static bool Free(Vector2Int t) => TerrainOk(t) && !Taken(t);
    public static bool AllFree(List<Vector2Int> tiles) { foreach (Vector2Int t in tiles) if (!Free(t)) return false; return true; }
}

public class BuildGridView : MonoBehaviour
{
    public const int Radius = 7;
    public static Color FreeColor = new Color(1f, 1f, 1f, 0.10f), BlockedColor = new Color(1f, 0.3f, 0.25f, 0.22f),
                        OkColor = new Color(0.45f, 1f, 0.4f, 0.55f), BadColor = new Color(1f, 0.3f, 0.25f, 0.6f);
    private static BuildGridView inst;
    private SpriteRenderer[] cells;
    private Vector2Int lastCenter = new Vector2Int(int.MinValue, 0);
    private float nextRefresh;
    private readonly HashSet<Vector2Int> foot = new HashSet<Vector2Int>();
    private bool footOk;
    private int groundMask;

    public static void Show(Vector3 at, List<Vector2Int> footprint, bool ok)
    {
        if (inst == null) inst = new GameObject("BuildGridView").AddComponent<BuildGridView>();
        inst.gameObject.SetActive(true);
        inst.Draw(at, footprint, ok);
    }

    public static void Hide() { if (inst != null) inst.gameObject.SetActive(false); }

    void Awake()
    {
        int n = (Radius * 2 + 1) * (Radius * 2 + 1);
        cells = new SpriteRenderer[n];
        Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(px); tex.Apply();
        Sprite sq = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        for (int i = 0; i < n; i++)
        {
            GameObject g = new GameObject("Tile");
            g.layer = 2;
            g.transform.SetParent(transform, false);
            g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            g.transform.localScale = Vector3.one * (BuildGrid.Tile * 0.9f);
            SpriteRenderer r = g.AddComponent<SpriteRenderer>();
            r.sprite = sq; r.sortingOrder = -20;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            cells[i] = r;
        }
        groundMask = ~((1 << 2) | LayerMask.GetMask("Tower", "Mob", "Player"));
    }

    private void Draw(Vector3 at, List<Vector2Int> footprint, bool ok)
    {
        bool footChanged = footOk != ok || footprint.Count != foot.Count;
        if (!footChanged) foreach (Vector2Int t in footprint) if (!foot.Contains(t)) { footChanged = true; break; }
        foot.Clear(); foreach (Vector2Int t in footprint) foot.Add(t);
        footOk = ok;
        Vector2Int c = BuildGrid.TileOf(at);
        bool moved = c != lastCenter;
        if (!moved && !footChanged && Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.25f;
        lastCenter = c;
        int k = 0;
        for (int dx = -Radius; dx <= Radius; dx++)
            for (int dz = -Radius; dz <= Radius; dz++, k++)
            {
                Vector2Int t = new Vector2Int(c.x + dx, c.y + dz);
                SpriteRenderer r = cells[k];
                float fade = 1f - Mathf.Clamp01((new Vector2(dx, dz).magnitude - Radius + 2.5f) / 2.5f);   // 가장자리는 흐리게
                Color col = foot.Contains(t) ? (ok ? OkColor : BadColor) : (BuildGrid.Free(t) ? FreeColor : BlockedColor);
                col.a *= foot.Contains(t) ? 1f : fade;
                r.color = col;
                if (moved)
                {
                    Vector3 p = BuildGrid.CenterOf(t);
                    float y = 0f;
                    if (Physics.Raycast(new Vector3(p.x, 50f, p.z), Vector3.down, out RaycastHit hit, 100f, groundMask, QueryTriggerInteraction.Ignore)) y = hit.point.y;
                    r.transform.position = new Vector3(p.x, y + 0.04f, p.z);
                }
            }
    }
}
