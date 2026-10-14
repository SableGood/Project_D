using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

// 도트(픽셀아트) 비주얼 스타일 통일
//  [Tools/비주얼/1. 도트 스타일 통일 적용]
//   ① 플레이어: 몹과 같은 스프라이트 머티리얼(Mat_2D_Mob, 조명 영향 없음 + 지면 그림자) + 발밑 기준 빌보드
//   ② 조명: 하늘색 환경광 -> 중립 회색(Flat), 하늘 반사 끔, 그림자 진하기 통일
//   ③ 포탑·바닥·씬의 3D 오브젝트: Standard -> 'Project_D/Toon Lit' (계단식 명암, 반사·하이라이트 없음, 그림자 색 보정)
//      + 해당 텍스처를 Point 필터로 (도트가 번지지 않게)
//   * 실행 후 씬 저장(Ctrl+S), 포탑 아이콘 다시 찍기(Tools/포탑/2. 전부 다시 생성)
//  [Tools/비주얼/2. 원래대로 되돌리기]
//   - 위 변경을 모두 원래 상태로 되돌림 (백업: Assets/Editor/VisualStyle_Backup.json)
//  * 미리보기용(*_Preview, PreviewTower)과 반투명 머티리얼, Packages 안의 머티리얼은 건드리지 않음
public static class VisualStyleTools
{
    public const string ToonShaderName = "Project_D/Toon Lit";
    public const string OriginalShaderTag = "ProjectD_OriginalShader";
    public const string PlayerPrefabFolder = "Assets/Main/In_Game/Main_Prefabs/Players";
    public const string TurretPrefabFolder = "Assets/Main/In_Game/Main_Prefabs/Turrets";
    public const string BackupPath = "Assets/Editor/VisualStyle_Backup.json";

    // ② 조명 값
    public static readonly Color AmbientColor = new Color(0.55f, 0.55f, 0.55f); // 중립 회색 (Standard로 남은 오브젝트용)
    public const float ShadowStrength = 0.7f;                                   // 그림자 진하기 (몹·플레이어·포탑 공통)

    // Toon으로 바꿀 셰이더 목록 (이 외의 셰이더는 건드리지 않음)
    static readonly HashSet<string> ConvertibleShaders = new HashSet<string>
    {
        "Standard", "Standard (Specular setup)",
        "Legacy Shaders/Diffuse", "Legacy Shaders/Bumped Diffuse",
        "Legacy Shaders/Specular", "Legacy Shaders/Bumped Specular",
        "Mobile/Diffuse", "Mobile/Bumped Diffuse",
    };

    // ---------------- 백업 데이터 ----------------
    [System.Serializable] class PlayerEntry { public string prefabPath; public string childPath; public string materialPath; public bool anchorAtFeet; }
    [System.Serializable] class TextureEntry { public string path; public int filterMode; }
    [System.Serializable] class LightEntry { public string objectPath; public float shadowStrength; }
    [System.Serializable]
    class Backup
    {
        public List<PlayerEntry> players = new List<PlayerEntry>();
        public List<TextureEntry> textures = new List<TextureEntry>();
        public bool hasLighting;
        public string scenePath;
        public int ambientMode;
        public Color ambientLight, ambientSky, ambientEquator, ambientGround;
        public float ambientIntensity;
        public int reflectionMode;
        public float reflectionIntensity;
        public List<LightEntry> lights = new List<LightEntry>();
    }

    static Backup LoadBackup()
    {
        if (!File.Exists(BackupPath)) return new Backup();
        try { return JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath)) ?? new Backup(); }
        catch { return new Backup(); }
    }

    static void SaveBackup(Backup b)
    {
        File.WriteAllText(BackupPath, JsonUtility.ToJson(b, true));
        AssetDatabase.ImportAsset(BackupPath);
    }

    // ================= 1. 적용 =================
    [MenuItem("Tools/비주얼/1. 도트 스타일 통일 적용")]
    public static void ApplyAll()
    {
        Shader toon = Shader.Find(ToonShaderName);
        if (toon == null)
        {
            Debug.LogError($"[비주얼] 셰이더 '{ToonShaderName}'를 찾지 못했습니다. (Assets/Main/In_Game/Shaders/ToonLit.shader 확인)");
            return;
        }

        Backup backup = LoadBackup(); // 두 번 실행해도 '맨 처음 상태'가 백업에 남도록 기존 백업에 이어서 기록
        int players = ApplyPlayers(backup);
        int mats = 0, texs = 0;
        ApplyToonMaterials(toon, backup, ref mats, ref texs);
        bool lit = ApplyLighting(backup);
        SaveBackup(backup);
        AssetDatabase.SaveAssets();

        Debug.Log($"[비주얼] 도트 스타일 통일 완료: 플레이어 {players}개, Toon 머티리얼 {mats}개, Point 텍스처 {texs}개, 조명 {(lit ? "적용" : "건너뜀(씬 없음)")}.\n" +
                  "→ 씬을 저장(Ctrl+S)하고, Tools/포탑/2. 전부 다시 생성 으로 아이콘을 다시 찍으세요.");
    }

    // ① 플레이어 스프라이트 = 몹과 같은 머티리얼 + 발밑 기준 빌보드
    static int ApplyPlayers(Backup backup)
    {
        Material spriteMat = MobPrefabTools.GetOrCreateMobSpriteMaterial();
        if (spriteMat == null) return 0;

        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PlayerPrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            foreach (Billboard bb in root.GetComponentsInChildren<Billboard>(true))
            {
                SpriteRenderer sr = bb.GetComponent<SpriteRenderer>();
                if (sr == null) continue;
                string child = GetPath(bb.transform, root.transform);

                if (!backup.players.Exists(p => p.prefabPath == path && p.childPath == child))
                {
                    backup.players.Add(new PlayerEntry
                    {
                        prefabPath = path,
                        childPath = child,
                        materialPath = sr.sharedMaterial != null ? AssetDatabase.GetAssetPath(sr.sharedMaterial) : "",
                        anchorAtFeet = bb.anchorAtFeet,
                    });
                }
                sr.sharedMaterial = spriteMat;
                sr.shadowCastingMode = ShadowCastingMode.TwoSided;
                sr.receiveShadows = false; // 일러스트 위에 그림자 얼룩이 생기지 않게 (몹과 동일)
                bb.anchorAtFeet = true;
                changed = true;
            }
            if (changed) { PrefabUtility.SaveAsPrefabAsset(root, path); count++; }
            PrefabUtility.UnloadPrefabContents(root);
        }
        return count;
    }

    // ③ 포탑 프리팹 + 열린 씬의 3D 오브젝트 머티리얼 -> Toon Lit
    static void ApplyToonMaterials(Shader toon, Backup backup, ref int matCount, ref int texCount)
    {
        var targets = new HashSet<Material>();

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { TurretPrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = Path.GetFileNameWithoutExtension(path);
            if (file.EndsWith("_Preview") || file.StartsWith("PreviewTower")) continue;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                CollectMaterials(r, targets);
        }

        // 열린 씬의 바닥·지형·벽 등 (스프라이트/UI/파티클 제외)
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r is SpriteRenderer || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            if (EditorUtility.IsPersistent(r)) continue;
            CollectMaterials(r, targets);
        }

        int skippedPackage = 0;
        foreach (Material m in targets)
        {
            string mPath = AssetDatabase.GetAssetPath(m);
            if (!mPath.StartsWith("Assets/")) { skippedPackage++; continue; } // 패키지(ProBuilder 기본 등)는 수정 불가
            if (m.shader == toon || !ConvertibleShaders.Contains(m.shader.name)) continue;
            if (m.HasProperty("_Mode") && m.GetFloat("_Mode") > 0.5f) continue; // 반투명(Fade/Transparent/Cutout)은 제외

            ConvertToToon(m, toon);
            matCount++;

            foreach (string prop in new[] { "_MainTex", "_BumpMap" })
                if (SetPointFilter(m.HasProperty(prop) ? m.GetTexture(prop) : null, backup)) texCount++;
        }
        if (skippedPackage > 0)
            Debug.Log($"[비주얼] 패키지 기본 머티리얼 {skippedPackage}개는 건너뜀 (ProBuilder 기본 머티리얼 등). 바꾸려면 Assets 안에 머티리얼을 만들어 적용하세요.");
    }

    static void CollectMaterials(Renderer r, HashSet<Material> set)
    {
        foreach (Material m in r.sharedMaterials)
            if (m != null && m.shader != null) set.Add(m);
    }

    // 다른 곳(FloorTools 등)에서도 쓰는 변환: 원래 셰이더 이름을 태그로 남기고 교체
    // (_Color, _MainTex, _BumpMap, _BumpScale 은 이름이 같아 그대로 유지됨)
    public static void ConvertToToon(Material m, Shader toon)
    {
        if (m.shader == toon) return;
        Undo.RecordObject(m, "Toon 변환");
        string original = m.shader.name;
        m.shader = toon;
        m.SetOverrideTag(OriginalShaderTag, original);
        EditorUtility.SetDirty(m);
    }

    static bool SetPointFilter(Texture tex, Backup backup)
    {
        if (tex == null) return false;
        string path = AssetDatabase.GetAssetPath(tex);
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null || ti.filterMode == FilterMode.Point) return false;
        if (!backup.textures.Exists(t => t.path == path))
            backup.textures.Add(new TextureEntry { path = path, filterMode = (int)ti.filterMode });
        ti.filterMode = FilterMode.Point;
        ti.anisoLevel = 0;
        ti.SaveAndReimport();
        return true;
    }

    // ② 조명 정리
    static bool ApplyLighting(Backup backup)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid()) return false;

        if (!backup.hasLighting || backup.scenePath != scene.path)
        {
            backup.hasLighting = true;
            backup.scenePath = scene.path;
            backup.ambientMode = (int)RenderSettings.ambientMode;
            backup.ambientLight = RenderSettings.ambientLight;
            backup.ambientSky = RenderSettings.ambientSkyColor;
            backup.ambientEquator = RenderSettings.ambientEquatorColor;
            backup.ambientGround = RenderSettings.ambientGroundColor;
            backup.ambientIntensity = RenderSettings.ambientIntensity;
            backup.reflectionMode = (int)RenderSettings.defaultReflectionMode;
            backup.reflectionIntensity = RenderSettings.reflectionIntensity;
            backup.lights.Clear();
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                backup.lights.Add(new LightEntry { objectPath = GetPath(l.transform, null), shadowStrength = l.shadowStrength });
        }

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = AmbientColor;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null; // 하늘 반사 없음
        RenderSettings.reflectionIntensity = 0f;

        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (l.shadows == LightShadows.None) continue;
            Undo.RecordObject(l, "그림자 진하기");
            l.shadowStrength = ShadowStrength;
        }

        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(scene);
        return true;
    }

    // ================= 2. 되돌리기 =================
    [MenuItem("Tools/비주얼/2. 원래대로 되돌리기")]
    public static void RevertAll()
    {
        Backup backup = LoadBackup();
        int mats = 0;

        // ③ 머티리얼: 태그에 적힌 원래 셰이더로
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m == null) continue;
            string original = m.GetTag(OriginalShaderTag, false, "");
            if (string.IsNullOrEmpty(original)) continue;
            Shader s = Shader.Find(original);
            if (s == null) { Debug.LogWarning($"[비주얼] '{m.name}'의 원래 셰이더 '{original}'를 찾지 못했습니다."); continue; }
            m.shader = s;
            m.SetOverrideTag(OriginalShaderTag, "");
            EditorUtility.SetDirty(m);
            mats++;
        }

        // 텍스처 필터
        foreach (TextureEntry t in backup.textures)
        {
            TextureImporter ti = AssetImporter.GetAtPath(t.path) as TextureImporter;
            if (ti == null) continue;
            ti.filterMode = (FilterMode)t.filterMode;
            ti.SaveAndReimport();
        }

        // ① 플레이어
        foreach (PlayerEntry p in backup.players)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(p.prefabPath) == null) continue;
            GameObject root = PrefabUtility.LoadPrefabContents(p.prefabPath);
            Transform child = string.IsNullOrEmpty(p.childPath) ? root.transform : root.transform.Find(p.childPath);
            if (child != null)
            {
                SpriteRenderer sr = child.GetComponent<SpriteRenderer>();
                Billboard bb = child.GetComponent<Billboard>();
                if (sr != null)
                {
                    sr.sharedMaterial = string.IsNullOrEmpty(p.materialPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(p.materialPath);
                    sr.receiveShadows = true;
                }
                if (bb != null) bb.anchorAtFeet = p.anchorAtFeet;
                PrefabUtility.SaveAsPrefabAsset(root, p.prefabPath);
            }
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ② 조명 (백업한 씬이 열려 있을 때만)
        bool lit = false;
        var scene = EditorSceneManager.GetActiveScene();
        if (backup.hasLighting && scene.IsValid() && scene.path == backup.scenePath)
        {
            RenderSettings.ambientMode = (AmbientMode)backup.ambientMode;
            RenderSettings.ambientLight = backup.ambientLight;
            RenderSettings.ambientSkyColor = backup.ambientSky;
            RenderSettings.ambientEquatorColor = backup.ambientEquator;
            RenderSettings.ambientGroundColor = backup.ambientGround;
            RenderSettings.ambientIntensity = backup.ambientIntensity;
            RenderSettings.defaultReflectionMode = (DefaultReflectionMode)backup.reflectionMode;
            RenderSettings.reflectionIntensity = backup.reflectionIntensity;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                LightEntry e = backup.lights.Find(x => x.objectPath == GetPath(l.transform, null));
                if (e == null) continue;
                Undo.RecordObject(l, "그림자 진하기 복원");
                l.shadowStrength = e.shadowStrength;
            }
            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(scene);
            lit = true;
        }
        else if (backup.hasLighting)
        {
            Debug.LogWarning($"[비주얼] 조명은 '{backup.scenePath}' 씬을 연 상태에서 다시 실행해야 복원됩니다.");
        }

        AssetDatabase.SaveAssets();
        if (lit || !backup.hasLighting) AssetDatabase.DeleteAsset(BackupPath); // 다 되돌렸으면 백업 삭제
        Debug.Log($"[비주얼] 되돌리기 완료: 머티리얼 {mats}개, 텍스처 {backup.textures.Count}개, 플레이어 {backup.players.Count}개, 조명 {(lit ? "복원" : "건너뜀")}. 씬을 저장하세요.");
    }

    static string GetPath(Transform t, Transform root)
    {
        if (t == root) return "";
        string path = t.name;
        while (t.parent != null && t.parent != root)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
