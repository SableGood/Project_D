using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// 바닥(Floor) 잔디 텍스처 적용
//  [Tools/바닥/잔디 텍스처 적용]
//   - 텍스처: Assets/Main/In_Game/Main_Material/Textures/Tex_Floor_Grass.png (반복 타일)
//   - 머티리얼: Assets/Main/In_Game/Main_Material/Mat_Floor_Grass.mat 을 만들고, 열린 씬의 'Floor'에 연결
//   - 바닥 크기에 맞춰 반복 횟수 자동 계산 (타일 1장 = TileMeters 미터, 캐릭터 그림의 픽셀 크기와 비슷하게)
//   - 노멀맵: Tex_Floor_Grass_Normal.png 가 있으면 함께 적용 (빛 방향에 따라 풀·돌이 살짝 입체적으로 보임)
//   - 셰이더: 'Project_D/Toon Lit' (도트 스타일 계단식 명암). 없으면 Standard
//   - 필터: Point (도트가 번지지 않게 또렷하게)
//   - 실행 후 씬을 저장(Ctrl+S)하세요.
public static class FloorTools
{
    public const string TexturePath = "Assets/Main/In_Game/Main_Material/Textures/Tex_Floor_Grass.png";
    public const string NormalPath = "Assets/Main/In_Game/Main_Material/Textures/Tex_Floor_Grass_Normal.png";
    // 노멀맵 세기 (0 = 끔, 1 = 원본). 바닥이 너무 울퉁불퉁해 보이면 낮추세요
    public const float NormalStrength = 0.6f;
    public const string MaterialPath = "Assets/Main/In_Game/Main_Material/Mat_Floor_Grass.mat";
    public const string FloorName = "Floor";

    // 타일 한 장이 덮는 바닥 크기(미터). 크게 하면 무늬가 커지고 반복이 줄어듦
    public const float TileMeters = 5f;

    [MenuItem("Tools/바닥/잔디 텍스처 적용")]
    public static void ApplyGrass()
    {
        // 1) 텍스처 설정: 반복, 밉맵(멀리서 반짝임 방지), 압축
        TextureImporter ti = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if (ti == null)
        {
            Debug.LogError($"[바닥] 텍스처가 없습니다: {TexturePath}");
            return;
        }
        ti.textureType = TextureImporterType.Default;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.mipmapEnabled = true;
        ti.filterMode = FilterMode.Point;
        ti.anisoLevel = 0;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.maxTextureSize = 1024;
        ti.SaveAndReimport();
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);

        // 노멀맵 (있을 때만)
        Texture2D normal = null;
        TextureImporter ni = AssetImporter.GetAtPath(NormalPath) as TextureImporter;
        if (ni != null)
        {
            ni.textureType = TextureImporterType.NormalMap;
            ni.wrapMode = TextureWrapMode.Repeat;
            ni.mipmapEnabled = true;
            ni.filterMode = FilterMode.Point;
            ni.anisoLevel = 0;
            ni.textureCompression = TextureImporterCompression.Compressed;
            ni.maxTextureSize = 1024;
            ni.SaveAndReimport();
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        }

        // 2) 머티리얼 (도트 스타일 Toon Lit, 없으면 Standard)
        Shader toon = Shader.Find(VisualStyleTools.ToonShaderName);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(toon != null ? toon : Shader.Find("Standard")) { name = "Mat_Floor_Grass" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        else if (toon != null && mat.shader != toon)
        {
            VisualStyleTools.ConvertToToon(mat, toon);
        }
        mat.mainTexture = tex;
        mat.color = Color.white;
        mat.SetFloat("_Glossiness", 0f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_SpecularHighlights", 0f);
        mat.SetFloat("_GlossyReflections", 0f);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
        if (normal != null && NormalStrength > 0f)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", NormalStrength);
            mat.EnableKeyword("_NORMALMAP");
        }
        else
        {
            mat.SetTexture("_BumpMap", null);
            mat.DisableKeyword("_NORMALMAP");
        }

        // 3) 열린 씬의 Floor에 연결 + 크기에 맞춘 반복 횟수
        GameObject floor = GameObject.Find(FloorName);
        if (floor == null)
        {
            Debug.LogWarning($"[바닥] 열린 씬에서 '{FloorName}' 오브젝트를 찾지 못했습니다. 머티리얼만 만들었습니다: {MaterialPath}");
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return;
        }

        Renderer r = floor.GetComponent<Renderer>();
        Vector3 size = r != null ? r.bounds.size : new Vector3(10f, 0f, 10f);
        mat.mainTextureScale = new Vector2(Mathf.Max(1f, Mathf.Round(size.x / TileMeters)), Mathf.Max(1f, Mathf.Round(size.z / TileMeters)));
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        if (r != null)
        {
            Undo.RecordObject(r, "바닥 잔디 적용");
            r.sharedMaterial = mat;
            EditorSceneManager.MarkSceneDirty(floor.scene);
        }
        Debug.Log($"[바닥] '{FloorName}'에 잔디 적용 완료 (바닥 {size.x:0}x{size.z:0}m, 반복 {mat.mainTextureScale.x:0}x{mat.mainTextureScale.y:0}, 노멀맵 {(normal != null ? "적용" : "없음")}). 씬을 저장하세요 (Ctrl+S).");
    }
}
