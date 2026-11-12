using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// v163 빌드 용량 줄이기 (에디터 전용 · 빌드에는 안 들어감)
//  메뉴: Project_D → 빌드 용량
//   1) 텍스처 압축: Resources·Main 폴더의 '압축 안 함' 텍스처를 고품질 압축(BC7)으로 → 텍스처 용량 약 1/4
//      바꾼 목록은 Assets/Editor/BuildSize_Compressed.txt 에 남김 → '텍스처 압축 되돌리기'로 원래대로
//   2) 안 쓰는 파일 빼기: 코드가 이름으로 부르지 않는 파일을 Resources 밖(Assets/_NotInBuild)으로 옮김
//      (Resources 안의 파일은 안 써도 전부 빌드에 들어감. 옮겨도 씬·프리팹이 직접 쓰는 것은 그대로 빌드에 들어감)
// v164 유니티를 열거나 스크립트를 다시 불러오면 자동으로 한 번: 안 쓰는 파일 옮기기 → 텍스처 압축 (+ UI 그림 밉맵 끄기)
[InitializeOnLoad]
public static class BuildSizeAuto
{
    private const string Marker = "Library/BuildSize_v164.done";
    static BuildSizeAuto() { EditorApplication.delayCall += Run; }
    private static void Run()
    {
        if (File.Exists(Marker) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.WriteAllText(Marker, System.DateTime.Now.ToString());
        BuildSizeTool.MoveUnusedNow();
        BuildSizeTool.CompressNow();
    }
}

// v166 v165(무기 그림 크기 줄이기) 롤백: 용량이 오히려 늘고 무기 정보창 그림 크기가 어긋남
//  v165가 바꾼 무기 그림 최대 크기를 원래 값(2048)으로 자동 한 번 되돌림
[InitializeOnLoad]
public static class WeaponPartsRevertAuto
{
    private const string Marker = "Library/BuildSize_v166.done";
    static WeaponPartsRevertAuto() { EditorApplication.delayCall += Run; }
    private static void Run()
    {
        if (File.Exists(Marker) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.WriteAllText(Marker, System.DateTime.Now.ToString());
        int n = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Image/Weapons/Parts" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null || ti.maxTextureSize == 2048) continue;
                ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
                n++;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        Debug.Log($"[빌드 용량] v165 롤백: 무기 그림 {n}개 최대 크기를 2048로 되돌림");
    }
}

public static class BuildSizeTool
{
    // 화면에 고정해서 보이는 그림 (멀리서 작아질 일이 없음) → 밉맵 필요 없음
    private static readonly string[] NoMipFolders = { "Assets/Resources/Image/UI", "Assets/Resources/Image/Intro", "Assets/Resources/Image/TurretIcons", "Assets/Resources/Image/Talents" };

    private static readonly string[] Roots = { "Assets/Resources", "Assets/Main" };
    private const string ListPath = "Assets/Editor/BuildSize_Compressed.txt";
    private const string UnusedRoot = "Assets/_NotInBuild";

    // 코드에서 이름으로 부르지 않는 것 (v163에 확인): 예전 섬 맵 · 예전 캐릭터 선택 그림 · 예전 메인 메뉴 그림 · 테스트 기록
    private static readonly string[] Unused =
    {
        "Assets/Resources/Image/Map/Island_Field.png",
        "Assets/Resources/Map/Island_Height.bytes",
        "Assets/Resources/Image/Pic_Monk_New.png",
        "Assets/Resources/Image/Pic_Monk_Back.png",
        "Assets/Resources/Image/Pic_Oran_Back.png",
        "Assets/Resources/Image/Pick_Chim_Back.png",
        "Assets/Resources/Image/UI/MainMenu/Start_Machine.png",
        "Assets/Resources/Image/UI/MainMenu/Start_Pressed.png",
        "Assets/Resources/Image/UI/MainMenu/Start_Open.png",
        "Assets/Resources/Image/UI/MainMenu/Start_Covered.png",
        "Assets/Resources/Image/UI/MainMenu/Holo_Green_9.png",
        "Assets/Resources/Image/UI/MainMenu/Holo_Yellow_9.png",
        "Assets/Resources/Image/UI/MainMenu/Holo_Orange_9.png",
        "Assets/Resources/PerformanceTestRunInfo.json",
        "Assets/Resources/PerformanceTestRunSettings.json",
    };

    private static List<TextureImporter> Uncompressed(out long bytes)
    {
        var list = new List<TextureImporter>();
        bytes = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", Roots))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null || ti.textureCompression != TextureImporterCompression.Uncompressed) continue;
            TextureImporterPlatformSettings ps = ti.GetPlatformTextureSettings("Standalone");
            if (ps.overridden && ps.format != TextureImporterFormat.Automatic) continue;   // 따로 정해 둔 것은 건드리지 않음
            list.Add(ti);
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (t != null) bytes += (long)t.width * t.height * 4 * (ti.mipmapEnabled ? 4 : 3) / 3;
        }
        return list;
    }

    [MenuItem("Project_D/빌드 용량/1. 텍스처 압축 (고품질 BC7)")]
    private static void Compress()
    {
        var list = Uncompressed(out long bytes);
        if (list.Count == 0) { Debug.Log("[빌드 용량] 압축할 텍스처가 없어요"); return; }
        if (!EditorUtility.DisplayDialog("텍스처 압축", $"압축 안 된 텍스처 {list.Count}개(약 {bytes / 1048576f:0} MB)를 고품질 압축으로 바꿀까요?\n다시 불러오는 데 몇 분 걸릴 수 있어요.\n(되돌리기 메뉴로 원래대로 돌릴 수 있음)", "압축", "취소")) return;
        CompressNow();
    }

    public static void CompressNow()
    {
        var list = Uncompressed(out long bytes);
        if (list.Count == 0) { Debug.Log("[빌드 용량] 압축할 텍스처가 없어요"); return; }
        Debug.Log($"[빌드 용량] 텍스처 {list.Count}개 압축 시작 (약 {bytes / 1048576f:0} MB → 약 {bytes / 4 / 1048576f:0} MB)");
        var done = new List<string>();
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < list.Count; i++)
            {
                TextureImporter ti = list[i];
                if (EditorUtility.DisplayCancelableProgressBar("텍스처 압축", ti.assetPath, i / (float)list.Count)) break;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.crunchedCompression = false;
                foreach (string f in NoMipFolders) if (ti.assetPath.StartsWith(f)) ti.mipmapEnabled = false;
                ti.SaveAndReimport();
                done.Add(ti.assetPath);
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ListPath));
        File.AppendAllLines(ListPath, done);
        AssetDatabase.Refresh();
        Debug.Log($"[빌드 용량] 텍스처 {done.Count}개 압축 (목록: {ListPath})");
    }

    [MenuItem("Project_D/빌드 용량/텍스처 압축 되돌리기")]
    private static void Revert()
    {
        if (!File.Exists(ListPath)) { Debug.Log("[빌드 용량] 되돌릴 목록이 없어요"); return; }
        string[] paths = File.ReadAllLines(ListPath);
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string p in paths)
            {
                TextureImporter ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        File.Delete(ListPath);
        AssetDatabase.Refresh();
        Debug.Log($"[빌드 용량] 텍스처 {paths.Length}개를 압축 안 함으로 되돌림");
    }

    [MenuItem("Project_D/빌드 용량/2. 안 쓰는 파일 Resources 밖으로")]
    private static void MoveUnused() => MoveUnusedNow();

    public static void MoveUnusedNow()
    {
        int moved = 0;
        foreach (string p in Unused)
        {
            if (!File.Exists(p)) continue;
            string rel = p.Substring("Assets/Resources/".Length);
            string dst = UnusedRoot + "/" + rel;
            string dir = Path.GetDirectoryName(dst).Replace('\\', '/');
            EnsureFolder(dir);
            string err = AssetDatabase.MoveAsset(p, dst);
            if (string.IsNullOrEmpty(err)) moved++;
            else Debug.LogWarning($"[빌드 용량] 못 옮김 {p}: {err}");
        }
        AssetDatabase.Refresh();
        Debug.Log($"[빌드 용량] {moved}개를 {UnusedRoot}로 옮김 (빌드에서 빠짐. 다시 쓰려면 Resources로 되돌리면 됨)");
    }

    private static void EnsureFolder(string dir)
    {
        if (AssetDatabase.IsValidFolder(dir)) return;
        string parent = Path.GetDirectoryName(dir).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(dir));
    }
}
