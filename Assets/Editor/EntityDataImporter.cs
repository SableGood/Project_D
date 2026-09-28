using UnityEngine;
using UnityEditor;
using System.IO;

public class EntityDataImporter : MonoBehaviour
{
    [MenuItem("Tools/CSV 데이터 생성/1. 개체 및 무기 (Mob_Data)")]
    public static void ImportCSVData()
    {
        TextAsset csvData = Resources.Load<TextAsset>("Mob_Data");
        if (csvData == null)
        {
            Debug.LogError("Resources 폴더에 Mob_Data.csv 파일을 찾을 수 없습니다!");
            return;
        }

        // 1. 기본 폴더 및 하위 폴더 생성 (없을 경우에만)
        CreateFolderIfNotExists("Assets", "Resources");
        CreateFolderIfNotExists("Assets/Resources", "Data");
        CreateFolderIfNotExists("Assets/Resources/Data", "Weapons");
        CreateFolderIfNotExists("Assets/Resources/Data", "Entities");

        // ★ 추가: Weapons 하위 분류 폴더 생성
        CreateFolderIfNotExists("Assets/Resources/Data/Weapons", "Mobs");
        CreateFolderIfNotExists("Assets/Resources/Data/Weapons", "Player");
        CreateFolderIfNotExists("Assets/Resources/Data/Weapons", "Turrets");

        // Entities 하위 분류 폴더 생성
        CreateFolderIfNotExists("Assets/Resources/Data/Entities", "Mobs");
        CreateFolderIfNotExists("Assets/Resources/Data/Entities", "Player");
        CreateFolderIfNotExists("Assets/Resources/Data/Entities", "Turrets");

        string[] rows = csvData.text.Split(new char[] { '\n' });

        for (int i = 2; i < rows.Length; i++)
        {
            string row = rows[i].Trim();
            if (string.IsNullOrEmpty(row)) continue;

            string[] cols = row.Split(',');

            if (cols.Length < 13 || string.IsNullOrEmpty(cols[0]) || cols[0].ToLower().Contains("nan")) continue;

            string code = cols[0].Trim();
            string name = cols[1].Trim();
            string tag = cols[3].Trim();
            string attackTypeStr = cols[5].Trim();

            float range = ParseFloat(cols[6]);
            float hp = ParseFloat(cols[7]);
            float def = ParseFloat(cols[8]);
            float atk = ParseFloat(cols[9]);
            float atkSpeed = ParseFloat(cols[10]);
            float speed = ParseFloat(cols[11]);
            int cost = (int)ParseFloat(cols[12]);

            // ★ 추가: 태그에 따른 서브 폴더 이름 결정 (무기와 개체 모두 적용하기 위해 위로 끌어올림)
            string subFolder = "Mobs"; // 기본값
            if (tag == "Player") subFolder = "Player";
            else if (tag == "Turret") subFolder = "Turrets";

            // ===== 2. 무기(Weapon) 데이터 덮어쓰기 or 생성 및 폴더 분류 =====
            string weaponAssetPath = $"Assets/Resources/Data/Weapons/{subFolder}/Wep_{code}.asset";
            WeaponData weaponData = AssetDatabase.LoadAssetAtPath<WeaponData>(weaponAssetPath);
            bool isNewWeapon = false;

            // 파일이 없으면 새로 생성
            if (weaponData == null)
            {
                weaponData = ScriptableObject.CreateInstance<WeaponData>();
                isNewWeapon = true;
            }

            // 값 갱신
            weaponData.weaponName = code + "_Weapon";
            weaponData.attackPower = atk;
            weaponData.attackSpeed = atkSpeed;
            weaponData.attackRange = range;

            if (attackTypeStr.Contains("Melee")) weaponData.attackType = AttackType.Melee;
            else if (attackTypeStr.Contains("Ranged")) weaponData.attackType = AttackType.Ranged;
            else if (attackTypeStr.Contains("Multi")) weaponData.attackType = AttackType.Multi;
            else weaponData.attackType = AttackType.None;

            if (isNewWeapon) AssetDatabase.CreateAsset(weaponData, weaponAssetPath);
            else EditorUtility.SetDirty(weaponData); // 기존 파일이면 변경되었다고 유니티에 알림

            // ===== 3. 개체(Entity) 데이터 덮어쓰기 or 생성 및 폴더 분류 =====
            string entityAssetPath = $"Assets/Resources/Data/Entities/{subFolder}/Ent_{code}.asset";
            EntityData entityData = AssetDatabase.LoadAssetAtPath<EntityData>(entityAssetPath);
            bool isNewEntity = false;

            if (entityData == null)
            {
                entityData = ScriptableObject.CreateInstance<EntityData>();
                isNewEntity = true;
            }

            entityData.entityCode = code;
            entityData.entityName = name;
            entityData.maxHp = hp;
            entityData.defense = def;
            entityData.moveSpeed = speed;
            entityData.cost = cost;
            entityData.defaultWeapon = weaponData;

            if (tag == "Player") entityData.entityType = EntityType.Player;
            else if (tag == "Turret") entityData.entityType = EntityType.Turret;
            else entityData.entityType = EntityType.Mob;

            if (isNewEntity) AssetDatabase.CreateAsset(entityData, entityAssetPath);
            else EditorUtility.SetDirty(entityData);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("CSV 데이터 갱신 및 전체 자동 분류 완료!");
    }

    // 폴더가 없으면 생성해주는 헬퍼 함수
    private static void CreateFolderIfNotExists(string parentFolder, string newFolderName)
    {
        string fullPath = parentFolder + "/" + newFolderName;
        if (!AssetDatabase.IsValidFolder(fullPath))
        {
            AssetDatabase.CreateFolder(parentFolder, newFolderName);
        }
    }

    private static float ParseFloat(string value)
    {
        if (string.IsNullOrEmpty(value) || value.ToLower().Contains("nan") || value == "-") return 0f;
        float result = 0f;
        float.TryParse(value, out result);
        return result;
    }
}