using UnityEngine;
using UnityEditor;

public class WeaponDataImporter : MonoBehaviour
{
    [MenuItem("Tools/CSV 데이터 생성/3. 무기 세팅 (Weapon_Data)")]
    public static void ImportWeaponData()
    {
        TextAsset csvData = Resources.Load<TextAsset>("Weapon_Data - 시트1");
        if (csvData == null)
        {
            Debug.LogError("Resources 폴더에 'Weapon_Data - 시트1.csv' 파일을 찾을 수 없습니다!");
            return;
        }

        string savePath = "Assets/Resources/Data/Weapons";
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Data")) 
            AssetDatabase.CreateFolder("Assets/Resources", "Data");
        if (!AssetDatabase.IsValidFolder(savePath)) 
            AssetDatabase.CreateFolder("Assets/Resources/Data", "Weapons");

        string[] rows = csvData.text.Split(new char[] { '\n' });

        for (int i = 1; i < rows.Length; i++)
        {
            string row = rows[i].Trim();
            if (string.IsNullOrEmpty(row)) continue;

            string[] cols = row.Split(',');
            if (cols.Length < 19) continue;

            string weaponID = cols[0].Trim();
            if (string.IsNullOrEmpty(weaponID)) continue;

            string assetPath = $"{savePath}/{weaponID}.asset";
            
            // ★ WeaponData -> PlayerWeaponData 로 변경
            PlayerWeaponData weaponAsset = AssetDatabase.LoadAssetAtPath<PlayerWeaponData>(assetPath);
            bool isNew = false;

            if (weaponAsset == null)
            {
                weaponAsset = ScriptableObject.CreateInstance<PlayerWeaponData>();
                isNew = true;
            }

            weaponAsset.weaponID = weaponID;
            weaponAsset.inGameName = cols[1].Trim();
            weaponAsset.role = cols[3].Trim();
            
            weaponAsset.aimType = cols[6].Trim();
            weaponAsset.fireMode = cols[7].Trim();
            weaponAsset.fireMechanism = cols[8].Trim();

            weaponAsset.range = ParseFloat(cols[9]);
            weaponAsset.damage = ParseFloat(cols[10]);
            weaponAsset.reloadTime = ParseFloat(cols[11]);
            weaponAsset.maxAmmo = (int)ParseFloat(cols[12]);
            weaponAsset.fireRate = ParseFloat(cols[13]);
            weaponAsset.projectileCount = (int)ParseFloat(cols[14]);

            weaponAsset.canPierce = cols[15].Trim().ToUpper() == "TRUE";
            weaponAsset.armorPenetration = ParseFloat(cols[16]);
            weaponAsset.projectileSpeed = ParseFloat(cols[17]);
            
            string prefabStr = cols[18].Trim();
            weaponAsset.projectilePrefabName = prefabStr == "-" ? "" : prefabStr;
            
            if (cols.Length > 19)
            {
                weaponAsset.description = cols[19].Trim();
            }

            if (isNew) 
                AssetDatabase.CreateAsset(weaponAsset, assetPath);
            else 
                EditorUtility.SetDirty(weaponAsset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("CSV 무기 데이터 자동 생성 완료! (PlayerWeaponData 적용)");
    }

    private static float ParseFloat(string value)
    {
        value = value.Trim();
        if (string.IsNullOrEmpty(value) || value == "-" || value.ToUpper() == "NAN") 
            return 0f;
            
        if (float.TryParse(value, out float result)) 
            return result;
            
        return 0f;
    }
}