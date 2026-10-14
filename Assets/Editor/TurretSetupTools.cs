using System.IO;
using UnityEngine;
using UnityEditor;

// 포탑 기능 자동 설정 + 건설 슬롯 정리
//  [Tools/포탑/3. 포탑 기능 자동 설정 + 슬롯 정리]
//   ① 플레이어 프리팹(TowerBuilder)의 1~5번 슬롯을 SlotCodes 순서(TR010~TR050)로 다시 연결
//      (프리팹 이름을 바꾸면 슬롯은 '파일'을 따라가므로, 이름 기준으로 다시 맞춰줌)
//   ② 포탑 프리팹에 기능 부착 (Mob_Data.csv 데이터 연결)
//      TR020 중화기(기관총) : 좌우 총열 번갈아 연사 + 제압 사격(명중한 몹 둔화)
//      TR030 저격          : 먼 몹부터 노림, 히트스캔 + 궤적
//      TR040 충격파        : 범위 안 몹 이동 둔화 (고유능력의 %)
//      TR050 수리          : 주기적으로 주변 포탑 회복
//   ③ 없어진 포탑의 썸네일(Resources/Image/TurretIcons) 정리
//  * 실행 전에 [Tools/CSV 데이터 생성/1. 개체 및 무기 (Mob_Data)]를 한 번 실행해 고유능력 칸을 읽어오세요.
public static class TurretSetupTools
{
    public static readonly string[] SlotCodes = { "TR010", "TR020", "TR030", "TR040", "TR050" };

    const string TurretFolder = "Assets/Main/In_Game/Main_Prefabs/Turrets";
    const string PlayerFolder = "Assets/Main/In_Game/Main_Prefabs/Players";
    const string ProjectileFolder = "Assets/Main/In_Game/Main_Prefabs";
    const string IconFolder = "Assets/Resources/Image/TurretIcons";
    const string EntityFolder = "Assets/Resources/Data/Entities/Turrets";

    [MenuItem("Tools/포탑/3. 포탑 기능 자동 설정 + 슬롯 정리")]
    public static void SetupAll()
    {
        int slots = FixSlots();

        int towers = 0;
        if (Edit("TR020", SetupHeavyMG)) towers++;
        if (Edit("TR030", SetupSniper)) towers++;
        if (Edit("TR040", root => SetupSupport(root, "TR040", SupportTower.Mode.SlowAura))) towers++;
        if (Edit("TR050", root => SetupSupport(root, "TR050", SupportTower.Mode.Repair))) towers++;

        int icons = CleanIcons();
        AssetDatabase.SaveAssets();
        Debug.Log($"[포탑] 설정 완료: 슬롯 정리 {slots}개 플레이어, 기능 설정 {towers}개 포탑, 남은 썸네일 정리 {icons}개.\n" +
                  "→ 플레이해서 2~5번 포탑을 설치해 보세요. (수치는 Mob_Data.csv에서 수정 후 CSV 임포트)");
    }

    // ---------------- ① 슬롯 ----------------
    static int FixSlots()
    {
        var towers = new GameObject[SlotCodes.Length];
        var previews = new GameObject[SlotCodes.Length];
        for (int i = 0; i < SlotCodes.Length; i++)
        {
            towers[i] = AssetDatabase.LoadAssetAtPath<GameObject>($"{TurretFolder}/{SlotCodes[i]}.prefab");
            previews[i] = AssetDatabase.LoadAssetAtPath<GameObject>($"{TurretFolder}/{SlotCodes[i]}_Preview.prefab");
            if (towers[i] == null) Debug.LogWarning($"[포탑] {SlotCodes[i]}.prefab 이 없습니다.");
            if (previews[i] == null) Debug.LogWarning($"[포탑] {SlotCodes[i]}_Preview.prefab 이 없습니다.");
        }

        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PlayerFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            TowerBuilder tb = root.GetComponentInChildren<TowerBuilder>(true);
            if (tb != null)
            {
                tb.towerPrefabs = (GameObject[])towers.Clone();
                tb.previewPrefabs = (GameObject[])previews.Clone();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                count++;
            }
            PrefabUtility.UnloadPrefabContents(root);
        }
        return count;
    }

    // ---------------- ② 포탑별 설정 ----------------
    static bool Edit(string code, System.Action<GameObject> setup)
    {
        string path = $"{TurretFolder}/{code}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            Debug.LogWarning($"[포탑] {path} 이 없어 건너뜁니다.");
            return false;
        }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        setup(root);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        return true;
    }

    // TR020 중화기 포탑: 쌍열 기관총, 제압 사격
    static void SetupHeavyMG(GameObject root)
    {
        TurretAI ai = GetOrAdd<TurretAI>(root);
        ai.myData = LoadData("TR020");
        ai.turretHead = FindChild(root.transform, "TR020_Top");
        ai.targetPriority = TurretAI.TargetPriority.Nearest;
        ai.rotationSpeed = 12f;
        ai.fireAngleThreshold = 6f;
        ai.fallbackDamage = 10f;
        ai.hitSlowPercent = 0.3f;   // 제압 사격: 맞은 몹 30% 둔화
        ai.hitSlowDuration = 0.8f;

        GameObject bullet = AssetDatabase.LoadAssetAtPath<GameObject>($"{ProjectileFolder}/Projectile_Bullet.prefab");
        foreach (string point in new[] { "WeaponPoint_L", "WeaponPoint_R" })
        {
            Transform t = FindChild(root.transform, point);
            if (t == null) { Debug.LogWarning($"[포탑] TR020: '{point}'를 찾지 못했습니다."); continue; }
            Weapon w = GetOrAdd<Weapon>(t.gameObject);
            SetupWeapon(w, Weapon.FireMode.RapidBullet, bullet);
        }
        // 가운데 WeaponPoint에 예전 무기가 붙어 있으면 제거 (좌우 번갈아 사격만 사용)
        Transform center = FindChild(root.transform, "WeaponPoint");
        if (center != null && center.TryGetComponent(out Weapon old)) Object.DestroyImmediate(old, true);
    }

    // TR030 저격 포탑: 먼 몹부터, 히트스캔
    static void SetupSniper(GameObject root)
    {
        TurretAI ai = GetOrAdd<TurretAI>(root);
        ai.myData = LoadData("TR030");
        Transform head = FindChild(root.transform, "TR030_Top");
        ai.turretHead = head;
        ai.targetPriority = TurretAI.TargetPriority.Farthest;
        ai.rotationSpeed = 8f;
        ai.fireAngleThreshold = 3f;
        ai.fallbackDamage = 40f;     // CSV 공격력이 비어 있어 임시값
        ai.hitSlowPercent = 0f;

        // 총구: 포신(TR030_Top/GameObject/Cube) 끝에 WeaponPoint를 만듦
        Transform barrel = head != null ? head.Find("GameObject") : null;
        Transform parent = barrel != null ? barrel : head != null ? head : root.transform;
        Transform muzzle = parent.Find("WeaponPoint");
        if (muzzle == null)
        {
            muzzle = new GameObject("WeaponPoint").transform;
            muzzle.SetParent(parent, false);
            muzzle.localPosition = barrel != null ? new Vector3(0f, -0.033f, 1.85f) : new Vector3(0f, 0f, 1f);
            muzzle.localRotation = Quaternion.identity;
        }
        Weapon w = GetOrAdd<Weapon>(muzzle.gameObject);
        SetupWeapon(w, Weapon.FireMode.Hitscan, null);
        w.alwaysShowTracer = true;
        w.tracerWidth = 0.06f;
        w.tracerDuration = 0.08f;
        w.tracerColor = new Color(1f, 0.95f, 0.7f, 1f);
    }

    // TR040 충격파 / TR050 수리
    static void SetupSupport(GameObject root, string code, SupportTower.Mode mode)
    {
        // 지원 포탑은 공격하지 않음
        foreach (TurretAI t in root.GetComponentsInChildren<TurretAI>(true)) Object.DestroyImmediate(t, true);

        SupportTower s = GetOrAdd<SupportTower>(root);
        s.myData = LoadData(code);
        s.mode = mode;
        s.spinner = FindChild(root.transform, code + "_Top");
        if (mode == SupportTower.Mode.SlowAura)
        {
            s.slowPercent = 0.15f;
            s.spinSpeed = 90f;
            s.pulseColor = new Color(0.45f, 0.8f, 1f, 0.9f);
        }
        else
        {
            s.healAmount = 10f;
            s.healInterval = 2f;
            s.spinSpeed = 40f;
            s.pulseColor = new Color(0.4f, 1f, 0.5f, 0.9f);
        }
    }

    static void SetupWeapon(Weapon w, Weapon.FireMode mode, GameObject projectile)
    {
        w.aimType = Weapon.AimType.Manual;
        w.fireMode = mode;
        if (projectile != null) w.projectilePrefab = projectile;
        w.muzzle = w.transform;
        int mob = LayerMask.GetMask("Mob");
        w.targetLayer = mob != 0 ? mob : 256;
    }

    // ---------------- ③ 썸네일 정리 ----------------
    static int CleanIcons()
    {
        if (!AssetDatabase.IsValidFolder(IconFolder)) return 0;
        int removed = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { IconFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{TurretFolder}/{name}.prefab") != null) continue;
            if (AssetDatabase.DeleteAsset(path)) { removed++; Debug.Log($"[포탑] 프리팹이 없는 썸네일 삭제: {path}"); }
        }
        return removed;
    }

    // ---------------- 공통 ----------------
    static EntityData LoadData(string code)
    {
        EntityData d = AssetDatabase.LoadAssetAtPath<EntityData>($"{EntityFolder}/Ent_{code}.asset");
        if (d == null) Debug.LogWarning($"[포탑] Ent_{code} 데이터가 없습니다. CSV 임포트를 먼저 실행하세요.");
        return d;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    static Transform FindChild(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
