using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;
using System.Text.RegularExpressions;

// 포탑 썸네일 자동 생성
//  [Tools/포탑/1. 포탑 썸네일 생성 (없는 것만)]  : 아직 썸네일이 없는 포탑만 만듦 (직접 그린 PNG는 건드리지 않음)
//  [Tools/포탑/2. 포탑 썸네일 전부 다시 생성]    : 모델을 고친 뒤 전부 새로 찍을 때
//
//  - 대상: Main_Prefabs/Turrets 안의 TR010, TR020 … 처럼 'TR + 숫자 3자리' 이름의 프리팹 (미리보기 프리팹 제외)
//  - 저장: Assets/Resources/Image/TurretIcons/{프리팹 이름}.png (256x256, 배경 투명)
//  - 직접 그린 아이콘을 쓰고 싶으면 같은 이름의 PNG로 덮어쓰면 됩니다. (1번 메뉴는 이미 있는 파일을 건너뜀)
public static class TurretIconTools
{
    public const string TurretPrefabFolder = "Assets/Main/In_Game/Main_Prefabs/Turrets";
    public const string IconFolder = "Assets/Resources/Image/TurretIcons";
    private const int Size = 256;
    private const int RenderSize = Size * 2; // 2배로 찍어서 줄임 (계단 현상 완화)

    // 촬영 각도: 위에서 내려다보는 각도 / 좌우 방향 (프리팹의 정면 = +Z 기준)
    private const float CameraPitch = 28f;
    private const float CameraYaw = 215f;

    [MenuItem("Tools/포탑/1. 포탑 썸네일 생성 (없는 것만)")]
    public static void GenerateMissing() => Generate(false);

    [MenuItem("Tools/포탑/2. 포탑 썸네일 전부 다시 생성")]
    public static void GenerateAll()
    {
        if (!EditorUtility.DisplayDialog("포탑 썸네일", "모든 포탑 썸네일을 다시 찍습니다.\n직접 그려 넣은 PNG도 덮어씁니다. 계속할까요?", "다시 생성", "취소")) return;
        Generate(true);
    }

    private static void Generate(bool overwrite)
    {
        EnsureFolder(IconFolder);
        Regex towerName = new Regex(@"^TR\d{3}$");
        int made = 0, skipped = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { TurretPrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path);
            if (!towerName.IsMatch(name)) continue;

            string pngPath = $"{IconFolder}/{name}.png";
            if (!overwrite && File.Exists(pngPath)) { skipped++; continue; }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Texture2D tex = Render(prefab);
            if (tex == null) { Debug.LogWarning($"[포탑 썸네일] {name}: 보이는 모델이 없어 건너뜁니다."); continue; }

            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(pngPath);
            ApplyIconImportSettings(pngPath);
            made++;
        }

        AssetDatabase.Refresh();
        Debug.Log($"[포탑 썸네일] 생성 {made}개 / 이미 있어서 건너뜀 {skipped}개  →  {IconFolder}");
    }

    // 프리팹을 빈 미리보기 장면에 놓고 찍음. 배경 투명을 위해 검은 배경/흰 배경으로 두 번 찍어 투명도를 계산
    private static Texture2D Render(GameObject prefab)
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        RenderTexture rt = null;
        try
        {
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;

            // 보이는 모델의 범위
            bool any = false;
            Bounds b = new Bounds();
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (!any) return null;

            // 조명 (미리보기 장면 안에서만 비춤)
            AddLight(scene, Quaternion.Euler(45f, CameraYaw - 30f, 0f), 1.15f);
            AddLight(scene, Quaternion.Euler(20f, CameraYaw + 120f, 0f), 0.45f);

            // 카메라: 정사영, 모델이 꽉 차도록
            GameObject camGo = new GameObject("IconCamera");
            SceneManager.MoveGameObjectToScene(camGo, scene);
            Camera cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.scene = scene;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.01f;
            Quaternion rot = Quaternion.Euler(CameraPitch, CameraYaw, 0f);
            float dist = b.extents.magnitude * 4f + 1f;
            cam.farClipPlane = dist * 3f;
            cam.transform.rotation = rot;
            cam.transform.position = b.center - rot * Vector3.forward * dist;

            float half = 0f;
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 local = cam.transform.InverseTransformPoint(corner);
                half = Mathf.Max(half, Mathf.Abs(local.x), Mathf.Abs(local.y));
            }
            cam.orthographicSize = half * 1.08f;

            rt = RenderTexture.GetTemporary(RenderSize, RenderSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 1);
            cam.targetTexture = rt;

            Color[] onBlack = Shoot(cam, rt, Color.black);
            Color[] onWhite = Shoot(cam, rt, Color.white);
            cam.targetTexture = null;

            // 투명도 = 1 - (흰 배경 - 검은 배경) / 색 = 검은 배경 결과 / 투명도
            // 2x2 픽셀을 평균내며 절반 크기로 (검은 배경 결과는 이미 '색 x 투명도' 상태라 그대로 평균 가능)
            Color[] result = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float kr = 0f, kg = 0f, kb = 0f, a = 0f;
                    for (int dy = 0; dy < 2; dy++)
                    {
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int i = (y * 2 + dy) * RenderSize + (x * 2 + dx);
                            Color k = onBlack[i], w = onWhite[i];
                            a += Mathf.Clamp01(1f - ((w.r - k.r) + (w.g - k.g) + (w.b - k.b)) / 3f);
                            kr += k.r; kg += k.g; kb += k.b;
                        }
                    }
                    a *= 0.25f; kr *= 0.25f; kg *= 0.25f; kb *= 0.25f;
                    result[y * Size + x] = a > 0.004f ? new Color(Mathf.Clamp01(kr / a), Mathf.Clamp01(kg / a), Mathf.Clamp01(kb / a), a) : Color.clear;
                }
            }
            Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels(result);
            tex.Apply();
            return tex;
        }
        finally
        {
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static Color[] Shoot(Camera cam, RenderTexture rt, Color background)
    {
        cam.backgroundColor = background;
        cam.Render();
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D read = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, RenderSize, RenderSize), 0, 0);
        read.Apply();
        RenderTexture.active = prev;
        Color[] px = read.GetPixels();
        Object.DestroyImmediate(read);
        return px;
    }

    private static void AddLight(Scene scene, Quaternion rotation, float intensity)
    {
        GameObject go = new GameObject("IconLight");
        SceneManager.MoveGameObjectToScene(go, scene);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        go.transform.rotation = rotation;
    }

    private static void ApplyIconImportSettings(string path)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 256;
        ti.SaveAndReimport();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
