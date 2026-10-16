using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 배경(스카이박스)·바닥(필드) 이미지 적용
//  [Tools/환경/스카이박스 적용]
//   - 이미지: Assets/Main/In_Game/Main_Material/Textures/Tex_Skybox_Field.png (풍경 한 장)
//   - 머티리얼: Assets/Main/In_Game/Main_Material/Mat_Skybox_Field.mat (Skybox/Panoramic, 360도 한 장 — 좌우가 이어지는 파노라마, 가로:세로 = 2:1)
//   - 열린 씬의 Lighting 스카이박스로 지정하고, 메인 카메라 배경을 Skybox로
//   - 풍경의 가운데(골짜기)가 보이는 방향은 머티리얼의 Rotation 값으로 돌릴 수 있음
//  [Tools/바닥/필드 이미지 적용]
//   - 이미지: Assets/Main/In_Game/Main_Material/Textures/Tex_Floor_Field.png (필드 한 장, 가장자리가 어두운 풀밭)
//   - 머티리얼: Assets/Main/In_Game/Main_Material/Mat_Floor_Field.mat (Toon Lit) → 'Floor'에 2 x 2장으로 반복 (이음새 없는 그림)
//   - Floor 크기를 16:9(가로 = X)로 맞춤 (넓이는 그대로). 이후 NavMeshSurface Bake 다시 필요
//   - 바깥 바닥(PlayArea)은 같은 머티리얼을 이어서 반복
//  실행 후 씬을 저장(Ctrl+S)하세요.
public static class EnvironmentTools
{
    const string TexFolder = "Assets/Main/In_Game/Main_Material/Textures";
    const string MatFolder = "Assets/Main/In_Game/Main_Material";
    public const string SkyTexPath = TexFolder + "/Tex_Skybox_Field.png";
    public const string SkyMatPath = MatFolder + "/Mat_Skybox_Field.mat";
    public const string FieldTexPath = TexFolder + "/Tex_Floor_Field.png";
    public const string FieldMatPath = MatFolder + "/Mat_Floor_Field.mat";
    public const string FloorName = "Floor";       // 씬의 바닥 오브젝트 이름 (예전 FloorTools에서 옮겨 옴)
    public const float FieldRepeat = 2f;   // 바닥 한 변에 필드 그림을 몇 번 반복할지

    [MenuItem("Tools/환경/스카이박스 적용")]
    public static void ApplySkybox()
    {
        Texture2D tex = ImportPixelTexture(SkyTexPath, TextureWrapMode.Clamp, false);
        if (tex == null) return;

        Shader sh = Shader.Find("Skybox/Panoramic");
        if (sh == null) { Debug.LogError("[환경] Skybox/Panoramic 셰이더를 찾지 못했습니다."); return; }
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(SkyMatPath);
        if (mat == null)
        {
            mat = new Material(sh) { name = "Mat_Skybox_Field" };
            AssetDatabase.CreateAsset(mat, SkyMatPath);
        }
        else mat.shader = sh;

        mat.SetTexture("_MainTex", tex);
        mat.SetFloat("_Mapping", 1f);            // Latitude Longitude Layout
        mat.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
        mat.DisableKeyword("_MAPPING_6_FRAMES_LAYOUT");
        mat.SetFloat("_ImageType", 0f);          // 360 Degrees (한 바퀴 이어지는 파노라마)
        mat.SetFloat("_MirrorOnBack", 0f);       // 거울 반복 없음
        mat.SetFloat("_Layout", 0f);
        mat.SetFloat("_Exposure", 1f);
        if (mat.HasProperty("_Tint")) mat.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 1f)); // 기본값(원본 색 그대로)
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        RenderSettings.skybox = mat;
        foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (!c.CompareTag("MainCamera")) continue;
            Undo.RecordObject(c, "스카이박스 적용");
            c.clearFlags = CameraClearFlags.Skybox;
        }
        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[환경] 스카이박스 적용 완료. 씬을 저장하세요 (Ctrl+S). 풍경 방향은 Mat_Skybox_Field의 Rotation으로 조절");
    }

    [MenuItem("Tools/바닥/필드 이미지 적용")]
    public static void ApplyField()
    {
        Texture2D tex = ImportPixelTexture(FieldTexPath, TextureWrapMode.Repeat, true);
        if (tex == null) return;

        Shader toon = Shader.Find(VisualStyleTools.ToonShaderName);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(FieldMatPath);
        if (mat == null)
        {
            mat = new Material(toon != null ? toon : Shader.Find("Standard")) { name = "Mat_Floor_Field" };
            AssetDatabase.CreateAsset(mat, FieldMatPath);
        }
        else if (toon != null && mat.shader != toon) VisualStyleTools.ConvertToToon(mat, toon);

        mat.mainTexture = tex;
        mat.mainTextureScale = new Vector2(FieldRepeat, FieldRepeat);   // 바닥에 2 x 2장 (한 장이면 픽셀이 너무 커 보임)
        mat.mainTextureOffset = Vector2.zero;
        mat.color = Color.white;
        mat.SetFloat("_Glossiness", 0f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_SpecularHighlights", 0f);
        mat.SetFloat("_GlossyReflections", 0f);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        mat.SetTexture("_BumpMap", null);
        mat.DisableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        GameObject floor = GameObject.Find(FloorName);
        if (floor == null)
        {
            Debug.LogWarning($"[바닥] 열린 씬에서 '{FloorName}' 오브젝트를 찾지 못했습니다. 머티리얼만 만들었습니다: {FieldMatPath}");
            return;
        }
        FitFloor16x9(floor);
        Renderer r = floor.GetComponent<Renderer>();
        if (r != null)
        {
            Undo.RecordObject(r, "바닥 필드 적용");
            r.sharedMaterial = mat;
            EditorSceneManager.MarkSceneDirty(floor.scene);
        }
        Debug.Log("[바닥] 'Floor'에 필드 이미지 적용 완료 (2 x 2장 반복, 16:9). 씬을 저장(Ctrl+S)하고, Floor의 NavMeshSurface에서 Bake를 다시 눌러 주세요.");
    }

    // 바닥을 화면 비율(16:9, 가로 = X축)로: 넓이는 그대로 두고 가로·세로 비율만 바꿈 (예: 3 x 3 → 4 x 2.25)
    [MenuItem("Tools/바닥/바닥 16:9 비율로 맞추기")]
    public static void FitFloor16x9Menu()
    {
        GameObject floor = GameObject.Find(FloorName);
        if (floor == null) { Debug.LogWarning($"[바닥] 열린 씬에서 '{FloorName}'를 찾지 못했습니다."); return; }
        FitFloor16x9(floor);
        Debug.Log("[바닥] 16:9로 맞춤. 씬을 저장(Ctrl+S)하고, Floor의 NavMeshSurface에서 Bake를 다시 눌러 주세요.");
    }

    private static void FitFloor16x9(GameObject floor)
    {
        Transform t = floor.transform;
        Vector3 s = t.localScale;
        float area = Mathf.Abs(s.x * s.z);
        float sx = Mathf.Sqrt(area * 16f / 9f), sz = Mathf.Sqrt(area * 9f / 16f);
        if (Mathf.Abs(Mathf.Abs(s.x) - sx) < 0.001f && Mathf.Abs(Mathf.Abs(s.z) - sz) < 0.001f) return;
        Undo.RecordObject(t, "바닥 16:9");
        t.localScale = new Vector3(sx, s.y, sz);
        EditorSceneManager.MarkSceneDirty(floor.scene);
        Debug.Log($"[바닥] Floor 크기 {s.x:0.##} x {s.z:0.##} → {sx:0.##} x {sz:0.##} (16:9, 넓이 동일)");
    }

    // 도트 그림용 텍스처 설정: 크게(4096), 무압축, 점 필터
    private static Texture2D ImportPixelTexture(string path, TextureWrapMode wrap, bool mipmaps)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogError($"[환경] 이미지가 없습니다: {path}"); return null; }
        ti.textureType = TextureImporterType.Default;
        ti.wrapMode = wrap;
        ti.mipmapEnabled = mipmaps;
        ti.filterMode = FilterMode.Point;
        ti.anisoLevel = 0;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 4096;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
