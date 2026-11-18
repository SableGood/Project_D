using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// v172 벽 건설 (X): 1m 타일 위에 3×1칸 벽 (나무 벽부터, FortWall)
//  - X = 켜기/끄기 · 우클릭 = 90도 돌리기 · 좌클릭 = 짓기 · Esc·1~5(포탑) = 취소
//  - 벽은 처음 요새 입구 벽을 포함해 FortWall.MaxWalls(2)개까지. 더 지으려면 벽을 골라 G로 철거
//  - 비용 = 나무 벽 값 (직업 포탑 할인 적용). 짓는 자리 위에 몹이 있으면 못 지음
public class WallBuilder : MonoBehaviour
{
    public static WallBuilder Instance { get; private set; }
    public static KeyCode Key = KeyCode.X;
    public const int WallTiles = 3;
    public bool Active { get; private set; }
    public static bool BlocksFiring => Instance != null && (Instance.Active || Instance.waitRelease);
    public static int Cost => Jobs.TurretPrice(FortWall.Levels[0].cost);
    public static int Count { get { int n = 0; foreach (FortWall w in FortWall.All) if (w != null) n++; return n; } }

    private float yaw;
    private GameObject ghost;
    private Material ghostMat;
    private bool waitRelease;
    private readonly List<Vector2Int> tiles = new List<Vector2Int>();
    private TowerBuilder builder;
    private PlayerController player;

    void Awake() { Instance = this; builder = GetComponent<TowerBuilder>(); }
    void OnDestroy() { if (Instance == this) Instance = null; if (ghost != null) Destroy(ghost); }

    public void Toggle() { if (Active) Cancel(); else Begin(); }

    public void Begin()
    {
        if (!FortLayout.Active) { AugmentManager.Toast("이 맵에서는 벽을 지을 수 없어요"); return; }
        if (builder != null) builder.CancelBuild();
        Active = true;
        if (ghost == null)
        {
            ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghost.name = "WallGhost";
            ghost.layer = 2;
            Destroy(ghost.GetComponent<Collider>());
            ghostMat = new Material(Shader.Find("Sprites/Default"));
            ghost.GetComponent<Renderer>().sharedMaterial = ghostMat;
            ghost.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        ghost.SetActive(true);
        Sfx.Play(Sfx.UIClickSound, 0.5f);
    }

    public void Cancel()
    {
        if (!Active) return;
        Active = false;
        if (ghost != null) ghost.SetActive(false);
        BuildGridView.Hide();
    }

    void Update()
    {
        if (waitRelease && !Input.GetMouseButton(0)) waitRelease = false;
        bool inputOk = Time.timeScale > 0f && !AugmentManager.IsOpen && !WeaponBrowser.IsOpen;
        if (inputOk && Input.GetKeyDown(Key)) Toggle();
        if (!Active) return;
        if (!inputOk) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
        if (builder != null && builder.SelectedSlot >= 0) { Cancel(); return; }
        if (Input.GetMouseButtonDown(1)) yaw = yaw == 0f ? 90f : 0f;

        Camera cam = Camera.main;
        if (cam == null) return;
        if (player == null) { player = GetComponent<PlayerController>(); if (player == null) player = FindFirstObjectByType<PlayerController>(); }
        bool fps = player != null && player.isFirstPerson;
        Ray ray = fps ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : cam.ScreenPointToRay(Input.mousePosition);
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float dist)) return;
        Vector3 hit = ray.GetPoint(dist);

        bool alongX = yaw == 0f;
        Vector3 c = BuildGrid.Footprint(hit, alongX ? WallTiles : 1, alongX ? 1 : WallTiles, tiles);
        float gy = GroundY(c);
        c.y = gy;

        float far = builder != null ? builder.maxBuildDistance : 10f;
        bool inRange = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(c.x, c.z)) <= far;
        bool spaceOk = BuildGrid.AllFree(tiles) && !MobInside(c, alongX);
        bool countOk = Count < FortWall.MaxWalls;
        bool gold = GoldManager.Instance == null || GoldManager.Instance.CanAfford(Cost);
        bool ok = inRange && spaceOk && countOk && gold;

        ghost.transform.SetPositionAndRotation(c + Vector3.up * FortWall.Height * 0.5f, Quaternion.Euler(0f, yaw, 0f));
        ghost.transform.localScale = new Vector3(WallTiles * BuildGrid.Tile, FortWall.Height, FortWall.Thickness);
        ghostMat.color = ok ? new Color(0.45f, 1f, 0.4f, 0.45f) : new Color(1f, 0.3f, 0.25f, 0.45f);
        BuildGridView.Show(c, tiles, ok);

        bool onUI = !fps && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (Input.GetMouseButtonDown(0) && !onUI)
        {
            waitRelease = true;
            if (!countOk) { AugmentManager.Toast($"벽은 {FortWall.MaxWalls}개까지 지을 수 있어요 · 벽 우클릭 → G로 철거"); return; }
            if (!inRange) { AugmentManager.Toast("너무 멀어요 · 가까이 가서 지으세요"); return; }
            if (!spaceOk) { AugmentManager.Toast("여기는 지을 수 없어요"); return; }
            if (GoldManager.Instance != null && !GoldManager.Instance.TrySpend(Cost)) { GoldManager.Instance.NotifyShort(Cost); return; }
            FortWall.Add(c, yaw, WallTiles * BuildGrid.Tile, true, Cost);
            Cancel();
        }
    }

    private static bool MobInside(Vector3 c, bool alongX)
    {
        float l = WallTiles * BuildGrid.Tile * 0.5f, t = BuildGrid.Tile * 0.5f;   // v175 벽 자리(3×1칸)만 검사
        Vector3 half = new Vector3(alongX ? l : t, FortWall.Height * 0.5f, alongX ? t : l);
        return Physics.CheckBox(c + Vector3.up * FortWall.Height * 0.5f, half, Quaternion.identity, LayerMask.GetMask("Mob"), QueryTriggerInteraction.Ignore);
    }

    private static float GroundY(Vector3 p)
    {
        int mask = ~((1 << 2) | LayerMask.GetMask("Tower", "Mob", "Player"));
        return Physics.Raycast(new Vector3(p.x, 50f, p.z), Vector3.down, out RaycastHit h, 100f, mask, QueryTriggerInteraction.Ignore) ? h.point.y : 0f;
    }
}
