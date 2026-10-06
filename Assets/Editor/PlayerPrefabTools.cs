using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

// 플레이어 프리팹 관련 일회성/반복 작업을 메뉴로 모아둔 에디터 도구입니다.
public static class PlayerPrefabTools
{
    private const string PlayerPrefabFolder = "Assets/Main/In_Game/Main_Prefabs/Players";

    // ------------------------------------------------------------------
    // 1) 무장 발사 위치 버그 수정용: 루트에 붙은 일러스트(SpriteRenderer + Billboard)를
    //    자식 오브젝트 "Sprite"로 분리합니다.
    //    → 일러스트만 카메라를 바라보고, 플레이어 본체(무기 포함)는 조준 방향으로 회전합니다.
    // ------------------------------------------------------------------
    [MenuItem("Tools/플레이어/1. 일러스트 분리 (Billboard 버그 수정)")]
    public static void SplitSpriteFromRoot()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PlayerPrefabFolder });
        int fixedCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                SpriteRenderer rootRenderer = root.GetComponent<SpriteRenderer>();
                if (rootRenderer == null)
                {
                    Debug.Log($"[건너뜀] {path} : 루트에 SpriteRenderer가 없습니다. (이미 분리됨)");
                    continue;
                }

                // 자식 "Sprite" 생성 (위치/회전/크기는 루트와 동일하게 시작)
                GameObject spriteObj = new GameObject("Sprite");
                spriteObj.layer = root.layer;
                spriteObj.transform.SetParent(root.transform, false);
                spriteObj.transform.SetSiblingIndex(0);

                // SpriteRenderer 설정(스프라이트, 머티리얼, 정렬 등)을 그대로 복사
                SpriteRenderer childRenderer = spriteObj.AddComponent<SpriteRenderer>();
                EditorUtility.CopySerialized(rootRenderer, childRenderer);
                spriteObj.AddComponent<Billboard>();

                // 루트에서 제거
                Billboard rootBillboard = root.GetComponent<Billboard>();
                if (rootBillboard != null) Object.DestroyImmediate(rootBillboard);
                Object.DestroyImmediate(rootRenderer);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                fixedCount++;
                Debug.Log($"[완료] {path} : 일러스트를 자식 'Sprite'로 분리했습니다.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Debug.Log($"플레이어 일러스트 분리 완료: {fixedCount}개 프리팹 수정");
    }

    // ------------------------------------------------------------------
    // 2) 테스트용: 생성된 모든 플레이어 무기 프리팹(W***)을 모든 캐릭터의 무기 목록에 넣습니다.
    //    (휠로 순서대로 교체하며 테스트)
    // ------------------------------------------------------------------
    [MenuItem("Tools/플레이어/2. 테스트: 모든 플레이어 무기 장착")]
    public static void AssignAllWeaponsForTest()
    {
        string[] weaponGuids = AssetDatabase.FindAssets("t:Prefab", new[] { WeaponDataImporter.WeaponPrefabFolder });
        List<GameObject> weapons = weaponGuids
            .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(go => go != null && go.GetComponent<Weapon>() != null)
            .OrderBy(go => go.name)
            .ToList();

        if (weapons.Count == 0)
        {
            Debug.LogWarning("장착할 무기 프리팹이 없습니다. 먼저 'Tools/CSV 데이터 생성/3. 무기'를 실행하세요.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PlayerPrefabFolder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                PlayerWeaponManager manager = root.GetComponent<PlayerWeaponManager>();
                if (manager == null) continue;

                manager.weaponPrefabs = weapons.ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[완료] {path} : 무기 {weapons.Count}개 장착 ({string.Join(", ", weapons.Select(w => w.name))})");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
