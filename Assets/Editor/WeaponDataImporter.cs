using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Weapon_Data.csv → PlayerWeaponData 에셋 + 무기 프리팹을 자동으로 만들어 주는 에디터 도구입니다.
//
// ■ 열(Column)은 "헤더 이름"으로 찾습니다. 순서를 바꾸거나 새 열을 추가해도 동작합니다.
// ■ 한 행 = 한 무기 (강화 단계도 각각 별도의 행)
// ■ 생성 위치
//    - 데이터 : Assets/Resources/Data/Weapons/PlayerWeapons/{코드명}.asset
//    - 프리팹 : Assets/Main/In_Game/Main_Prefabs/Weapons/Player/{코드명}.prefab
// ■ 이미 있는 프리팹은 이미지/크기/데이터만 갱신하고, 직접 옮겨 둔 총구(Muzzle) 위치는 유지합니다.
//   (총구 위치까지 처음 상태로 되돌리려면 "프리팹 다시 만들기" 메뉴 사용)
public class WeaponDataImporter
{
    public const string CsvResourceName = "Weapon_Data";
    public const string DataFolder = "Assets/Resources/Data/Weapons/PlayerWeapons";
    public const string WeaponPrefabFolder = "Assets/Main/In_Game/Main_Prefabs/Weapons/Player";
    public const string WeaponImageFolder = "Assets/Resources/Image/Weapons";
    public const string ProjectilePrefabFolder = "Assets/Main/In_Game/Main_Prefabs";

    // 무기 프리팹 기본 배치값 (무기 길이 대비 비율)
    private const float GripOffsetRatio = 0.2f;   // 손잡이(회전축)에서 이미지 중심까지 → 이미지가 앞으로 나오도록
    private const float MuzzleHeightRatio = 0.15f; // 총구 높이 (이미지 높이 대비, 위쪽)

    [MenuItem("Tools/CSV 데이터 생성/3. 무기 (Weapon_Data)")]
    public static void ImportWeaponData()
    {
        Import(false);
    }

    [MenuItem("Tools/CSV 데이터 생성/3-1. 무기 프리팹 다시 만들기 (총구 위치 초기화)")]
    public static void ImportWeaponDataOverwrite()
    {
        if (!EditorUtility.DisplayDialog("무기 프리팹 다시 만들기",
            "모든 플레이어 무기 프리팹을 처음 상태로 다시 만듭니다.\n직접 조정한 총구(Muzzle) 위치가 초기화됩니다.\n계속할까요?",
            "다시 만들기", "취소"))
            return;
        Import(true);
    }

    private static void Import(bool overwritePrefabs)
    {
        TextAsset csvData = Resources.Load<TextAsset>(CsvResourceName);
        if (csvData == null)
        {
            Debug.LogError($"Resources 폴더에서 '{CsvResourceName}.csv' 파일을 찾을 수 없습니다!");
            return;
        }

        EnsureFolder(DataFolder);
        EnsureFolder(WeaponPrefabFolder);

        List<List<string>> rows = CsvUtil.Parse(csvData.text);
        if (rows.Count < 2)
        {
            Debug.LogError("Weapon_Data.csv 에 데이터 행이 없습니다.");
            return;
        }

        CsvUtil.Header header = new CsvUtil.Header(rows[0]);
        int created = 0, updated = 0;
        List<string> warnings = new List<string>();

        for (int i = 1; i < rows.Count; i++)
        {
            List<string> row = rows[i];
            string weaponID = header.Get(row, "코드명");
            if (string.IsNullOrEmpty(weaponID)) continue;

            // ===== 1. 데이터 에셋 =====
            string assetPath = $"{DataFolder}/{weaponID}.asset";
            PlayerWeaponData data = AssetDatabase.LoadAssetAtPath<PlayerWeaponData>(assetPath);
            bool isNew = data == null;
            if (isNew) data = ScriptableObject.CreateInstance<PlayerWeaponData>();

            data.weaponID = weaponID;
            data.weaponName = header.Get(row, "이름");
            string alias = header.Get(row, "인게임 별칭 (임시)", "인게임 별칭");
            data.inGameName = string.IsNullOrEmpty(alias) ? data.weaponName : alias;
            data.role = header.Get(row, "역할");

            data.aimType = header.Get(row, "Aim Type");
            data.fireMode = header.Get(row, "Fire Mode");
            data.fireMechanism = header.Get(row, "발사방식");

            data.range = header.GetFloat(row, "유효거리");
            data.damage = header.GetFloat(row, "공격력");
            data.reloadTime = header.GetFloat(row, "재장전속도");
            data.maxAmmo = (int)header.GetFloat(row, "장탄수");
            data.fireRate = header.GetFloat(row, "공격속도");
            data.projectileCount = Mathf.Max(1, (int)header.GetFloat(row, "투사체 갯수"));
            data.spread = header.GetFloat(row, "탄퍼짐");
            data.canPierce = header.Get(row, "타겟 관통 여부").ToUpper() == "TRUE";
            data.armorPenetration = header.GetFloat(row, "방어구 무시");
            data.projectileSpeed = header.GetFloat(row, "투사체 속도");
            data.projectilePrefabName = CleanName(header.Get(row, "Projectile Prefab"));
            data.description = header.Get(row, "장비 설명");

            data.spriteName = CleanName(header.Get(row, "Sprite"));
            float length = header.GetFloat(row, "무기 크기");
            data.spriteLength = length > 0 ? length : 1f;

            // 이미지 연결 (자동으로 Sprite 형식 + 픽셀아트 설정 적용)
            data.weaponSprite = null;
            if (!string.IsNullOrEmpty(data.spriteName))
            {
                data.weaponSprite = LoadWeaponSprite(data.spriteName);
                if (data.weaponSprite == null) warnings.Add($"{weaponID}: 이미지 '{data.spriteName}' 를 {WeaponImageFolder} 에서 찾을 수 없습니다.");
            }
            else
            {
                warnings.Add($"{weaponID}: Sprite 열이 비어 있어 무기 이미지 없이 만듭니다.");
            }

            // 투사체 프리팹 연결 (Fire Mode 가 Projectile 일 때만 사용됨)
            data.projectilePrefab = null;
            if (!string.IsNullOrEmpty(data.projectilePrefabName))
            {
                data.projectilePrefab = FindPrefab(data.projectilePrefabName, ProjectilePrefabFolder);
                if (data.projectilePrefab == null && data.IsProjectile)
                    warnings.Add($"{weaponID}: 투사체 프리팹 '{data.projectilePrefabName}' 을 찾을 수 없습니다.");
            }
            if (data.IsProjectile && data.projectilePrefab == null)
                warnings.Add($"{weaponID}: Fire Mode 가 Projectile 인데 투사체 프리팹이 없어 발사되지 않습니다.");

            if (isNew) AssetDatabase.CreateAsset(data, assetPath);
            else EditorUtility.SetDirty(data);

            // ===== 2. 무기 프리팹 =====
            string prefabPath = $"{WeaponPrefabFolder}/{weaponID}.prefab";
            bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            if (prefabExists && !overwritePrefabs)
            {
                UpdateWeaponPrefab(prefabPath, data);
                updated++;
            }
            else
            {
                CreateWeaponPrefab(prefabPath, data);
                created++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        foreach (string w in warnings) Debug.LogWarning("[무기 임포트] " + w);
        Debug.Log($"무기 데이터 임포트 완료! 프리팹 새로 생성 {created}개 / 갱신 {updated}개 → {WeaponPrefabFolder}");
    }

    // ------------------------------------------------------------------
    // 무기 프리팹 구조
    //   W002 (Weapon)                ← 플레이어 WeaponMountPoint 에 붙는 루트
    //   └ Visual (WeaponVisual)      ← 조준 방향으로 회전 + 왼쪽일 때 상하 반전
    //       ├ Sprite (SpriteRenderer)← 무기 이미지
    //       └ Muzzle                 ← 총구. 여기서 탄이 나갑니다 (씬에서 직접 옮겨도 됨)
    // ------------------------------------------------------------------
    private static void CreateWeaponPrefab(string prefabPath, PlayerWeaponData data)
    {
        GameObject root = new GameObject(data.weaponID);
        try
        {
            Weapon weapon = root.AddComponent<Weapon>();
            weapon.aimType = Weapon.AimType.Manual;
            weapon.playerWepData = data;
            weapon.targetLayer = LayerMask.GetMask("Mob");

            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<WeaponVisual>();

            GameObject spriteObj = new GameObject("Sprite");
            spriteObj.transform.SetParent(visual.transform, false);
            SpriteRenderer sr = spriteObj.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 1; // 캐릭터 일러스트보다 앞에 그림

            GameObject muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(visual.transform, false);
            weapon.muzzle = muzzle.transform;

            ApplySpriteAndSize(sr, data);

            float height = GetSpriteHeight(sr);
            muzzle.transform.localPosition = new Vector3(
                data.spriteLength * (GripOffsetRatio + 0.5f),
                height * MuzzleHeightRatio,
                0f);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void UpdateWeaponPrefab(string prefabPath, PlayerWeaponData data)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Weapon weapon = root.GetComponent<Weapon>();
            if (weapon != null)
            {
                weapon.playerWepData = data;
                weapon.aimType = Weapon.AimType.Manual;
            }

            SpriteRenderer sr = root.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null) ApplySpriteAndSize(sr, data);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 이미지 적용 + 무기 길이(spriteLength)에 맞게 크기 조절
    private static void ApplySpriteAndSize(SpriteRenderer sr, PlayerWeaponData data)
    {
        sr.sprite = data.weaponSprite;
        Transform t = sr.transform;

        if (data.weaponSprite != null && data.weaponSprite.bounds.size.x > 0.0001f)
        {
            float scale = data.spriteLength / data.weaponSprite.bounds.size.x;
            t.localScale = new Vector3(scale, scale, 1f);
        }
        else
        {
            t.localScale = Vector3.one;
        }
        t.localPosition = new Vector3(data.spriteLength * GripOffsetRatio, 0f, 0f);
    }

    private static float GetSpriteHeight(SpriteRenderer sr)
    {
        if (sr.sprite == null) return 0f;
        return sr.sprite.bounds.size.y * sr.transform.localScale.y;
    }

    // ------------------------------------------------------------------
    // 이미지 / 프리팹 찾기
    // ------------------------------------------------------------------
    private static Sprite LoadWeaponSprite(string spriteName)
    {
        string texPath = FindAssetPath(spriteName, "t:Texture2D", WeaponImageFolder);
        if (texPath == null) return null;

        // 픽셀아트 무기 이미지용 임포트 설정 (Sprite / Point / 무압축)
        TextureImporter ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (ti != null)
        {
            bool changed = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
            if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
            if (changed) ti.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(texPath);
    }

    private static GameObject FindPrefab(string prefabName, string folder)
    {
        string path = FindAssetPath(prefabName, "t:Prefab", folder);
        return path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    // 이름이 정확히 일치하는 에셋 경로 (확장자 제외 비교)
    private static string FindAssetPath(string assetName, string typeFilter, string folder)
    {
        string[] guids = AssetDatabase.FindAssets($"{assetName} {typeFilter}", new[] { folder });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == assetName) return path;
        }
        return null;
    }

    private static string CleanName(string value)
    {
        value = (value ?? "").Trim();
        return value == "-" ? "" : value;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;
        string parent = Path.GetDirectoryName(folderPath).Replace("\\", "/");
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folderPath));
    }
}

// ------------------------------------------------------------------
// CSV 파서 (따옴표 안의 쉼표/줄바꿈 지원) + 헤더 이름으로 값 찾기
// ------------------------------------------------------------------
public static class CsvUtil
{
    public static List<List<string>> Parse(string text)
    {
        List<List<string>> rows = new List<List<string>>();
        List<string> row = new List<string>();
        StringBuilder cell = new StringBuilder();
        bool inQuotes = false;

        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1); // BOM 제거

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cell.Append(c);
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (c == '\r') { }
                else if (c == '\n')
                {
                    row.Add(cell.ToString()); cell.Clear();
                    rows.Add(row); row = new List<string>();
                }
                else cell.Append(c);
            }
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
        return rows;
    }

    public class Header
    {
        private readonly Dictionary<string, int> index = new Dictionary<string, int>();

        public Header(List<string> headerRow)
        {
            for (int i = 0; i < headerRow.Count; i++)
            {
                string key = Normalize(headerRow[i]);
                if (!string.IsNullOrEmpty(key) && !index.ContainsKey(key)) index[key] = i;
            }
        }

        // 여러 후보 이름 중 처음 찾은 열의 값을 반환 (없으면 "")
        public string Get(List<string> row, params string[] names)
        {
            foreach (string name in names)
            {
                if (index.TryGetValue(Normalize(name), out int col))
                    return col < row.Count ? row[col].Trim() : "";
            }
            return "";
        }

        public float GetFloat(List<string> row, params string[] names)
        {
            string v = Get(row, names);
            if (string.IsNullOrEmpty(v) || v == "-" || v.ToUpper() == "NAN") return 0f;
            return float.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result) ? result : 0f;
        }

        private static string Normalize(string s)
        {
            return (s ?? "").Replace(" ", "").Trim().ToLower();
        }
    }
}
