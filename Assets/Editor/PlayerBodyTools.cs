using System.IO;
using UnityEngine;
using UnityEditor;

// 플레이어 상·하체 애니메이션 구성
//  [Tools/플레이어/3. 상·하체 애니메이션 적용]
//   플레이어 프리팹 PC_{이름} 마다 Assets/Resources/Image/Players/{이름}/ 폴더가 있으면 적용
//     rig.json          : 픽셀 배율(ppu), 엉덩이 위치, 방향별 상체 그림 이름과 손 위치, 걷기 속도
//     Upper/Upper_*.png : 상체 9장 (Idle, Up, UpRight, Right, DownRight, Down, DownLeft, Left, UpLeft)
//     Lower/Walk6_*.png : 하체 걷기 프레임 (오른쪽을 보는 그림)
//     (상체 피벗 = 엉덩이: rig.json의 upperPivotY. 손이 엉덩이 아래로 내려와도 잘리지 않음)
//   → 'Sprite'(하체) 아래에 'Upper'(상체)를 만들고, 루트에 PlayerBodyAnimator를 붙여 그림·손 위치를 채움
//   → 기존 일러스트는 HUD/캐릭터 선택 초상화로 보관
//  여러 번 실행해도 안전합니다. (그림/rig.json만 바꾸고 다시 실행)
public static class PlayerBodyTools
{
    const string PlayerPrefabFolder = "Assets/Main/In_Game/Main_Prefabs/Players";
    const string PlayerImageFolder = "Assets/Resources/Image/Players";

    [System.Serializable] class DirEntry { public string name; public string sprite; public float[] hand; }
    [System.Serializable] class RigData
    {
        public float ppu = 100f; public float[] hipLocal; public float walkFps = 10f; public int standFrame = 1; public DirEntry[] dirs;
        public int upperPivotY = 0;       // 상체 그림 맨 아래에서 엉덩이(이음새)까지 픽셀 (손이 엉덩이 아래로 내려오는 그림용)
        public int[] upperSize;           // 상체 그림 크기 [가로, 세로]
        public int lowerWaistTop = 0;     // 하체 그림 맨 위에서 허리 윗선까지 픽셀
        public int[] lowerSize;           // 하체 그림 크기 [가로, 세로]
    }

    [MenuItem("Tools/플레이어/3. 상·하체 애니메이션 적용")]
    public static void ApplyAll()
    {
        int applied = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PlayerPrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = Path.GetFileNameWithoutExtension(path).Replace("PC_", "");
            string folder = $"{PlayerImageFolder}/{name}";
            if (!File.Exists($"{folder}/rig.json")) continue;
            if (Apply(path, folder)) applied++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log(applied > 0
            ? $"[플레이어] 상·하체 애니메이션 적용: {applied}개 캐릭터"
            : $"[플레이어] 적용할 캐릭터가 없습니다. ({PlayerImageFolder}/{{이름}}/rig.json 확인)");
    }

    static bool Apply(string prefabPath, string folder)
    {
        RigData rig = JsonUtility.FromJson<RigData>(File.ReadAllText($"{folder}/rig.json"));
        if (rig == null || rig.dirs == null) { Debug.LogWarning($"[플레이어] rig.json을 읽지 못했습니다: {folder}"); return false; }

        // 그림 불러오기 (픽셀 설정 + 피벗: 상체=아래 가운데(엉덩이), 하체=가운데)
        var upper = new Sprite[9];
        var hands = new Vector2[9];
        float pivotV = (rig.upperSize != null && rig.upperSize.Length >= 2 && rig.upperSize[1] > 0) ? (float)rig.upperPivotY / rig.upperSize[1] : 0f;
        foreach (DirEntry d in rig.dirs)
        {
            if (!System.Enum.TryParse(d.name, out PlayerBodyAnimator.Dir dir)) continue;
            upper[(int)dir] = LoadSprite($"{folder}/Upper/{d.sprite}.png", rig.ppu, SpriteAlignment.Custom, new Vector2(0.5f, pivotV));
            if (d.hand != null && d.hand.Length >= 2) hands[(int)dir] = new Vector2(d.hand[0], d.hand[1]);
        }
        var walk = new System.Collections.Generic.List<Sprite>();
        string lowerDir = $"{folder}/Lower";
        if (Directory.Exists(lowerDir))
        {
            string[] files = Directory.GetFiles(lowerDir, "*.png");
            System.Array.Sort(files);
            foreach (string f in files)
            {
                Sprite s = LoadSprite(f.Replace('\\', '/'), rig.ppu, SpriteAlignment.Center);
                if (s != null) walk.Add(s);
            }
        }
        if (walk.Count == 0 || upper[(int)PlayerBodyAnimator.Dir.Right] == null)
        {
            Debug.LogWarning($"[플레이어] {folder}: 상체/하체 그림이 부족합니다.");
            return false;
        }

        Material mat = MobPrefabTools.GetOrCreateMobSpriteMaterial();
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform spriteT = root.transform.Find("Sprite");
            if (spriteT == null) { Debug.LogWarning($"[플레이어] {prefabPath}: 'Sprite' 자식이 없습니다."); return false; }
            SpriteRenderer lower = spriteT.GetComponent<SpriteRenderer>();
            if (lower == null) lower = spriteT.gameObject.AddComponent<SpriteRenderer>();
            if (spriteT.GetComponent<Billboard>() == null) spriteT.gameObject.AddComponent<Billboard>();

            PlayerBodyAnimator anim = root.GetComponent<PlayerBodyAnimator>();
            if (anim == null) anim = root.AddComponent<PlayerBodyAnimator>();
            // 기존 일러스트는 초상화로 보관 (처음 한 번)
            if (anim.portrait == null && lower.sprite != null && !walk.Contains(lower.sprite)) anim.portrait = lower.sprite;

            int stand = Mathf.Clamp(rig.standFrame, 0, walk.Count - 1);
            spriteT.localScale = Vector3.one;
            spriteT.localPosition = new Vector3(0f, spriteT.localPosition.y, 0f);
            lower.sprite = walk[stand];
            lower.flipX = false;
            lower.color = Color.white;
            if (mat != null) lower.sharedMaterial = mat;
            lower.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;

            // 상체: 하체 그림의 허리 윗부분에 엉덩이(피벗)를 맞춤
            Transform upperT = spriteT.Find("Upper");
            if (upperT == null)
            {
                upperT = new GameObject("Upper").transform;
                upperT.SetParent(spriteT, false);
                upperT.gameObject.layer = spriteT.gameObject.layer;
            }
            Vector2 hip = (rig.hipLocal != null && rig.hipLocal.Length >= 2) ? new Vector2(rig.hipLocal[0], rig.hipLocal[1]) : Vector2.zero;
            // 엉덩이·손 위치는 그림과 같은 좌표계(하체 그림 기준)라서 캐릭터 크기(루트 스케일)에 맞춰 함께 커지고 작아짐
            upperT.localPosition = new Vector3(hip.x, hip.y, 0f);
            upperT.localRotation = Quaternion.identity;
            upperT.localScale = Vector3.one;
            SpriteRenderer up = upperT.GetComponent<SpriteRenderer>();
            if (up == null) up = upperT.gameObject.AddComponent<SpriteRenderer>();
            up.sprite = upper[(int)PlayerBodyAnimator.Dir.Idle] ?? upper[(int)PlayerBodyAnimator.Dir.Right];
            if (mat != null) up.sharedMaterial = mat;
            up.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            up.receiveShadows = false;

            anim.lowerRenderer = lower;
            anim.upperRenderer = up;
            anim.upperSprites = upper;
            anim.handOffsets = hands;
            anim.walkFrames = walk.ToArray();
            anim.standFrame = stand;
            anim.walkFps = rig.walkFps > 0f ? rig.walkFps : 10f;
            // 이음새 외곽선 생략 위치
            anim.upperJointV = pivotV;
            anim.lowerJointV = (rig.lowerSize != null && rig.lowerSize.Length >= 2 && rig.lowerSize[1] > 0)
                ? 1f - (float)rig.lowerWaistTop / rig.lowerSize[1] : 1f;

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[플레이어] {Path.GetFileNameWithoutExtension(prefabPath)}: 상체 {System.Array.FindAll(upper, s => s != null).Length}방향, 하체 {walk.Count}프레임 적용");
            return true;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static Sprite LoadSprite(string path, float ppu, SpriteAlignment alignment) => LoadSprite(path, ppu, alignment, new Vector2(0.5f, 0.5f));

    static Sprite LoadSprite(string path, float ppu, SpriteAlignment alignment, Vector2 customPivot)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return null;
        TextureImporterSettings set = new TextureImporterSettings();
        ti.ReadTextureSettings(set);
        bool changed = false;
        if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
        if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
        bool pivotDiff = alignment == SpriteAlignment.Custom && (set.spritePivot - customPivot).sqrMagnitude > 1e-8f;
        if (set.spriteAlignment != (int)alignment || !Mathf.Approximately(set.spritePixelsPerUnit, ppu) || pivotDiff)
        {
            ti.ReadTextureSettings(set);
            set.spriteAlignment = (int)alignment;
            if (alignment == SpriteAlignment.Custom) set.spritePivot = customPivot;
            set.spritePixelsPerUnit = ppu;
            ti.SetTextureSettings(set);
            changed = true;
        }
        if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
        if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
        if (ti.textureCompression != TextureImporterCompression.Compressed) { ti.textureCompression = TextureImporterCompression.Compressed; changed = true; }
        if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
        if (changed) ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
