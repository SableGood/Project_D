using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 프로젝트 정리 (깃헙 업로드 전)
//  [Tools/정리/1. 안 쓰는 파일 휴지통으로]
//    씬·프리팹·머티리얼 어디에서도 쓰지 않는 파일을 Windows 휴지통으로 보냄 (휴지통에서 복구 가능)
//    - 빌드에 없는 옛 씬(Assets/Main/In_Game.unity), "지워도 됩니다" 표시된 빈 스크립트 2개
//    - 필드 바닥으로 바뀌기 전의 잔디 바닥 세트 (텍스처 2장 + 머티리얼 + FloorTools)
//    - 초기 프로토타입 포탑·무기 프리팹, 안 쓰는 단색 머티리얼, 그 머티리얼만 쓰던 포탑 그림
//  [Tools/정리/2. 안 쓰는 그림을 Resources 밖으로]
//    Resources 안의 그림은 안 써도 빌드에 전부 들어감 → 코드·씬에서 안 쓰는 그림을 Assets/_Unused/ 로 옮김 (삭제 아님)
//  * 끝나면 ProjectStructure.txt 를 새로 만들고, 이 스크립트(ProjectCleanup.cs)는 지워도 됩니다.
public static class ProjectCleanup
{
    static readonly string[] Unused =
    {
        "Assets/Main/In_Game.unity",
        "Assets/Main/In_Game/Scripts/User/TowerBarHUD.cs",
        "Assets/Main/In_Game/Scripts/User/WeaponHUD.cs",
        "Assets/Editor/FloorTools.cs",
        "Assets/Main/In_Game/Main_Material/Mat_Floor_Grass.mat",
        "Assets/Main/In_Game/Main_Material/Textures/Tex_Floor_Grass.png",
        "Assets/Main/In_Game/Main_Material/Textures/Tex_Floor_Grass_Normal.png",
        "Assets/Main/In_Game/Main_Prefabs/Turrets/Tower.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Turrets/Tower_Buff.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Turrets/Tower_Debuff.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Turrets/Tower_Mortar.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Turrets/Tower_Railgun.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Turrets/Tower_Rifle.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Turrets/HostileTower 1.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Turrets/PreviewTower.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Weapons/P_Weapon_Cannon.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Weapons/P_Weapon_Missile 1.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Weapons/P_Weapon_Shotgun.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Weapons/P_Weapon_Snipe.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Weapons/T_Weapon_Cannon.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Weapons/T_Weapon_Missile.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Weapons/T_Weapon_Rapid.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Weapons/T_Weapon_Shotgun.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Weapons/T_Weapon_Snipe.prefab",
        "Assets/Main/In_Game/Main_Prefabs/Projectile_Missile.prefab",
        "Assets/Main/In_Game/Main_Material/Mat_2D_Chim.mat",
        "Assets/Main/In_Game/Main_Material/Mat_2D_Monk.mat",
        "Assets/Main/In_Game/Main_Material/Mat_2D_Oran.mat",
        "Assets/Main/In_Game/Main_Material/Mat_Blue.mat",
        "Assets/Main/In_Game/Main_Material/Mat_DarkGray.mat",
        "Assets/Main/In_Game/Main_Material/Mat_Gray.mat",
        "Assets/Main/In_Game/Main_Material/Mat_Green.mat",
        "Assets/Main/In_Game/Main_Material/Mat_LightGray.mat",
        "Assets/Main/In_Game/Main_Material/Mat_Lime.mat",
        "Assets/Main/In_Game/Main_Material/Mat_Magenta.mat",
        "Assets/Main/In_Game/Main_Material/Mat_Orange.mat",
        "Assets/Main/In_Game/Main_Material/Mat_Pink.mat",
        "Assets/Main/In_Game/Main_Material/Mat_Red.mat",
        "Assets/Main/In_Game/Main_Material/Mat_Sand.mat",
        "Assets/Main/In_Game/Main_Material/Mat_Yellow.mat",
        "Assets/Main/In_Game/Main_Material/Mob_Magenta.mat",
        "Assets/Main/In_Game/Main_Material/Turrets/Mat_TR010_Bottom.mat",
        "Assets/Resources/Image/Turrets/TR010_Bottom_1.png"
    };

    static readonly string[] UnusedArt =
    {
        "Assets/Resources/Image/Pic_Monk_New.png",
        "Assets/Resources/Image/Pic_Monk_Back.png",
        "Assets/Resources/Image/Pick_Chim_Back.png",
        "Assets/Resources/Image/Pic_Oran_Back.png",
        "Assets/Resources/Image/ETC/Pic_Bullet_1.png",
        "Assets/Resources/Image/ETC/Pic_Casing_1.png",
        "Assets/Resources/Image/ETC/Pic_Casing_2.png",
        "Assets/Resources/Image/ETC/Pic_Casing_3.png",
        "Assets/Resources/Image/ETC/Pic_Casing_4.png",
        "Assets/Resources/Image/ETC/Pic_Shell_1.png",
        "Assets/Resources/Image/ETC/Pic_Wing.png"
    };

    const string UnusedFolder = "Assets/_Unused";

    [MenuItem("Tools/정리/1. 안 쓰는 파일 휴지통으로")]
    static void TrashUnused()
    {
        List<string> exist = Existing(Unused);
        if (exist.Count == 0) { EditorUtility.DisplayDialog("정리", "정리할 파일이 없습니다. (이미 정리됨)", "확인"); return; }
        string list = string.Join("\n", exist.ConvertAll(Path.GetFileName));
        if (!EditorUtility.DisplayDialog("안 쓰는 파일 정리",
                $"{exist.Count}개 파일을 Windows 휴지통으로 보냅니다. (휴지통에서 복구 가능)\n\n{list}", "휴지통으로", "취소"))
            return;
        int ok = 0;
        foreach (string p in exist)
        {
            if (AssetDatabase.MoveAssetToTrash(p)) ok++;
            else Debug.LogWarning("[정리] 휴지통으로 보내지 못함: " + p);
        }
        AssetDatabase.Refresh();
        ExportStructure();
        Debug.Log($"[정리] {ok}/{exist.Count}개 파일을 휴지통으로 보냈습니다.");
    }

    [MenuItem("Tools/정리/2. 안 쓰는 그림을 Resources 밖으로")]
    static void MoveUnusedArt()
    {
        List<string> exist = Existing(UnusedArt);
        if (exist.Count == 0) { EditorUtility.DisplayDialog("정리", "옮길 그림이 없습니다. (이미 정리됨)", "확인"); return; }
        if (!EditorUtility.DisplayDialog("안 쓰는 그림 옮기기",
                $"{exist.Count}개 그림을 {UnusedFolder}/ 로 옮깁니다. (삭제 아님, 빌드에서만 빠짐)\n\n{string.Join("\n", exist.ConvertAll(Path.GetFileName))}", "옮기기", "취소"))
            return;
        if (!AssetDatabase.IsValidFolder(UnusedFolder)) AssetDatabase.CreateFolder("Assets", "_Unused");
        foreach (string p in exist)
        {
            string sub = Path.GetFileName(Path.GetDirectoryName(p));          // Image 또는 ETC
            string dir = UnusedFolder + "/" + sub;
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(UnusedFolder, sub);
            string err = AssetDatabase.MoveAsset(p, dir + "/" + Path.GetFileName(p));
            if (!string.IsNullOrEmpty(err)) Debug.LogWarning("[정리] 옮기지 못함: " + p + " / " + err);
        }
        AssetDatabase.Refresh();
        ExportStructure();
        Debug.Log($"[정리] 그림 {exist.Count}개를 {UnusedFolder}/ 로 옮겼습니다.");
    }

    static List<string> Existing(string[] paths)
    {
        List<string> list = new List<string>();
        foreach (string p in paths)
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p)) && File.Exists(p)) list.Add(p);
        return list;
    }

    // 프로젝트 최상위의 ProjectStructure.txt 갱신 (Tools/프로젝트 구조 내보내기 와 같은 내용)
    static void ExportStructure()
    {
        try
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Project_D Assets Structure ===");
            foreach (string f in Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories))
                if (!f.EndsWith(".meta")) sb.AppendLine(f.Replace("\\", "/"));
            File.WriteAllText(Application.dataPath + "/../ProjectStructure.txt", sb.ToString());
        }
        catch (System.Exception e) { Debug.LogWarning("[정리] ProjectStructure.txt 갱신 실패: " + e.Message); }
    }
}
