using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using System.IO;

// 몹 프리팹 정리 도구
//  [Tools/몹/1. 몹 프리팹 정리 (2D 이미지 적용)]
//   1) 루트 크기를 1로 되돌리고, 기존 큐브 모양은 자식 "Body"로 옮김
//      (루트가 늘어나 있으면 그 아래의 2D 일러스트가 회전할 때 찌그러지기 때문)
//   2) 콜라이더/NavMeshAgent 크기를 원래 몸집에 맞게 보정
//   3) 2D 이미지가 있으면 자식 "Sprite"(SpriteRenderer + Billboard)를 만들고 큐브는 숨김
//        이미지 위치: Assets/Resources/Image/Mobs/Pic_{코드}.png   (예: Pic_MM001.png, Mob_MM001.png도 인식)
//   4) 몹 코드에 맞춰 행동(공성/저격/가속 등)과 넉백 저항을 설정
//   5) 모든 몹 일러스트를 기준 몹(MM001)과 같은 배율로 맞춤 → 원본 이미지의 픽셀 비율 그대로 크기가 정해짐
//      (콜라이더/에이전트 높이는 일러스트 높이에 맞춰 자동 보정, 발이 지면에 닿도록)
//   7) 프레임 애니메이션: Assets/Resources/Image/Mobs/Anim/{코드}/{클립}_{번호}.png 가 있으면
//      SpriteFrameAnimator를 붙이고 클립을 채움 (Walk로 시작하는 클립 = 걷기)
//   6) 그림자용 머티리얼(Mat_2D_Mob, 셰이더 Project_D/Sprite Shadow)을 적용 → 바닥에 그림자가 생김
//  여러 번 실행해도 안전합니다. (이미지만 바꾼 뒤 다시 실행하면 이미지가 갱신됨)
public static class MobPrefabTools
{
    public const string MobPrefabFolder = "Assets/Main/In_Game/Main_Prefabs/Mobs";
    public const string MobImageFolder = "Assets/Resources/Image/Mobs";

    // 일러스트 배율 기준: MM001 일러스트의 높이를 1.5m로 맞추는 배율을 모든 몹에 똑같이 적용
    public const string ReferenceMobCode = "MM001";
    public const float ReferenceMobHeight = 1.5f;

    // 그림자 머티리얼 (양면 + 그림자 + flipX 지원 스프라이트 셰이더)
    public const string MobSpriteShaderName = "Project_D/Sprite Shadow"; // 일러스트는 조명 영향 없음 + 지면 그림자만
    public const string MobSpriteMaterialPath = "Assets/Main/In_Game/Main_Material/Mat_2D_Mob.mat";

    [MenuItem("Tools/몹/1. 몹 프리팹 정리 (2D 이미지 적용)")]
    public static void SetupMobPrefabs()
    {
        if (!AssetDatabase.IsValidFolder(MobImageFolder))
        {
            AssetDatabase.CreateFolder("Assets/Resources/Image", "Mobs");
            Debug.Log($"[몹] 이미지 폴더를 만들었습니다: {MobImageFolder}  (여기에 Pic_MM001.png 처럼 넣으세요)");
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { MobPrefabFolder });
        int withImage = 0, withoutImage = 0;

        // 공통 일러스트 배율 (MM001 기준)
        float sharedScale = -1f;
        Sprite refSprite = LoadMobSprite(ReferenceMobCode);
        if (refSprite != null && refSprite.bounds.size.y > 0.0001f)
            sharedScale = ReferenceMobHeight / refSprite.bounds.size.y;
        else
            Debug.LogWarning($"[몹] 기준 이미지(Pic_{ReferenceMobCode})가 없어 몹마다 원래 몸 높이에 맞춥니다.");

        Material shadowMat = GetOrCreateMobSpriteMaterial();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string code = Path.GetFileNameWithoutExtension(path).Replace("Ent_", "");
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                float bodyHeight = NormalizeRootScale(root);
                ApplyBehaviorPreset(root, code);

                Sprite sprite = LoadMobSprite(code);
                if (sprite != null)
                {
                    float scale = sharedScale > 0f ? sharedScale
                        : (sprite.bounds.size.y > 0.0001f ? bodyHeight / sprite.bounds.size.y : 1f);
                    ApplySprite(root, sprite, scale, shadowMat);
                    ApplyAnimations(root, code, sprite);
                    withImage++;
                }
                else
                {
                    withoutImage++;
                    Debug.Log($"[몹] {code}: 이미지가 없어 큐브 모양을 유지합니다. ({MobImageFolder}/Pic_{code}.png)");
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        UpdateTestRoster();
        AssetDatabase.SaveAssets();
        Debug.Log($"몹 프리팹 정리 완료: 이미지 적용 {withImage}개 / 큐브 유지 {withoutImage}개");
    }

    // ------------------------------------------------------------------
    // 몹 테스트 패널에 표시할 몹 목록 갱신 (Resources/Data/MobTestRoster.asset)
    // ------------------------------------------------------------------
    [MenuItem("Tools/몹/2. 테스트 패널 몹 목록 갱신")]
    public static void UpdateTestRoster()
    {
        const string rosterPath = "Assets/Resources/Data/MobTestRoster.asset";
        MobTestRoster roster = AssetDatabase.LoadAssetAtPath<MobTestRoster>(rosterPath);
        if (roster == null)
        {
            roster = ScriptableObject.CreateInstance<MobTestRoster>();
            AssetDatabase.CreateAsset(roster, rosterPath);
        }

        roster.mobs.Clear();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { MobPrefabFolder });
        System.Collections.Generic.List<GameObject> list = new System.Collections.Generic.List<GameObject>();
        foreach (string guid in guids)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab != null && prefab.GetComponent<MobAI>() != null) list.Add(prefab);
        }
        // 정렬: 근거리(MM) → 원거리(MR) → 엘리트(ME) → 보스(MB), 같은 계열은 번호순
        list.Sort((a, b) => SortKey(a.name).CompareTo(SortKey(b.name)));
        roster.mobs.AddRange(list);

        EditorUtility.SetDirty(roster);
        AssetDatabase.SaveAssets();
        Debug.Log($"[몹] 테스트 패널 몹 목록 갱신: {roster.mobs.Count}종");
    }

    private static string SortKey(string prefabName)
    {
        string code = prefabName.Replace("Ent_", "");
        string group = code.StartsWith("MM") ? "0" : code.StartsWith("MR") ? "1" : code.StartsWith("ME") ? "2" : code.StartsWith("MB") ? "3" : "4";
        return group + code;
    }

    // ------------------------------------------------------------------
    // 1~2) 루트 크기 정리 + 콜라이더/에이전트 보정. 반환값 = 몸 높이
    // ------------------------------------------------------------------
    private static float NormalizeRootScale(GameObject root)
    {
        Vector3 s = root.transform.localScale;
        BoxCollider box = root.GetComponent<BoxCollider>();
        NavMeshAgent agent = root.GetComponent<NavMeshAgent>();

        if (Vector3.Distance(s, Vector3.one) < 0.0001f)
        {
            // 이미 정리된 프리팹: 몸 높이는 콜라이더 기준
            return box != null ? box.size.y : 1f;
        }

        // 큐브 메쉬를 자식 Body로 이동 (원래 크기 유지)
        MeshFilter mf = root.GetComponent<MeshFilter>();
        MeshRenderer mr = root.GetComponent<MeshRenderer>();
        if (mf != null && mr != null)
        {
            GameObject body = new GameObject("Body");
            body.layer = root.layer;
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = s;

            MeshFilter newMf = body.AddComponent<MeshFilter>();
            newMf.sharedMesh = mf.sharedMesh;
            MeshRenderer newMr = body.AddComponent<MeshRenderer>();
            EditorUtility.CopySerialized(mr, newMr);

            Object.DestroyImmediate(mr);
            Object.DestroyImmediate(mf);
        }

        // 다른 자식(무기 등)은 루트 크기 변화에 영향받지 않도록 위치만 보정
        root.transform.localScale = Vector3.one;

        if (box != null)
        {
            box.size = Vector3.Scale(box.size, s);
            box.center = Vector3.Scale(box.center, s);
        }

        if (agent != null)
        {
            agent.radius = Mathf.Clamp(0.5f * Mathf.Max(s.x, s.z), 0.25f, 2f);
            agent.height = s.y;
            agent.baseOffset = s.y * 0.5f; // 피벗(몸 중앙)이 지면에서 몸 높이의 절반 위에 오도록
        }

        return s.y;
    }

    // ------------------------------------------------------------------
    // 3) 2D 일러스트 적용
    // ------------------------------------------------------------------
    private static void ApplySprite(GameObject root, Sprite sprite, float scale, Material shadowMat)
    {
        Transform spriteT = root.transform.Find("Sprite");
        if (spriteT == null)
        {
            GameObject go = new GameObject("Sprite");
            go.layer = root.layer;
            go.transform.SetParent(root.transform, false);
            go.transform.SetSiblingIndex(0);
            spriteT = go.transform;
        }

        SpriteRenderer sr = spriteT.GetComponent<SpriteRenderer>();
        if (sr == null) sr = spriteT.gameObject.AddComponent<SpriteRenderer>();
        if (spriteT.GetComponent<Billboard>() == null) spriteT.gameObject.AddComponent<Billboard>();

        sr.sprite = sprite;
        sr.flipX = false;
        sr.color = Color.white;
        spriteT.localScale = new Vector3(scale, scale, 1f);
        // 이미지 중심이 루트(몸 중앙)에 오도록 (피벗이 Center가 아니어도 안전)
        spriteT.localPosition = new Vector3(0f, -sprite.bounds.center.y * scale, 0f);

        // 그림자: 전용 스프라이트 셰이더 + 양면 그림자 (Sprites-Default는 그림자를 만들지 않음)
        if (shadowMat != null) sr.sharedMaterial = shadowMat;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        sr.receiveShadows = true;

        // 실제 보이는 높이에 맞춰 콜라이더/에이전트 높이 보정 (가로 크기·반경은 기존 값 유지)
        float h = sprite.bounds.size.y * scale;
        BoxCollider box = root.GetComponent<BoxCollider>();
        if (box != null)
        {
            box.size = new Vector3(box.size.x, h, box.size.z);
            box.center = new Vector3(box.center.x, 0f, box.center.z);
        }
        NavMeshAgent agent = root.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.height = h;
            agent.baseOffset = h * 0.5f; // 루트(몸 중앙)가 지면에서 h/2 위 → 발이 지면에 닿음
        }

        // 큐브는 숨김 (지우지 않고 남겨서, 나중에 비교/복구 가능)
        Transform body = root.transform.Find("Body");
        if (body != null) body.gameObject.SetActive(false);
    }

    // 몹 일러스트용 그림자 머티리얼
    //  - 셰이더: Project_D/Sprite Shadow (양면 표시 + 그림자 + flipX/색 지원)
    //    ※ Standard 셰이더는 뒷면을 그리지 않아, 좌우 반전된 몹이 그림자만 남고 안 보이는 문제가 있었음
    //  - 텍스처는 SpriteRenderer가 각 몹 이미지로 자동으로 넣어주므로 머티리얼 1개를 모든 몹이 같이 씀
    // ------------------------------------------------------------------
    // 7) 프레임 애니메이션
    // ------------------------------------------------------------------
    private static void ApplyAnimations(GameObject root, string code, Sprite idleSprite)
    {
        Transform spriteT = root.transform.Find("Sprite");
        if (spriteT == null) return;
        SpriteFrameAnimator anim = spriteT.GetComponent<SpriteFrameAnimator>();

        string folder = $"{MobImageFolder}/Anim/{code}";
        if (!Directory.Exists(folder))
        {
            if (anim != null) Object.DestroyImmediate(anim, true); // 애니메이션 폴더를 지웠으면 정지 이미지로 복귀
            return;
        }

        // 파일 이름 "{클립}_{번호}.png" → 클립별로 묶기
        var groups = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.List<string>>();
        foreach (string file in Directory.GetFiles(folder))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;
            string fname = Path.GetFileNameWithoutExtension(file);
            int us = fname.LastIndexOf('_');
            if (us <= 0) continue;
            string clipName = fname.Substring(0, us);
            if (!groups.TryGetValue(clipName, out var list)) groups[clipName] = list = new System.Collections.Generic.List<string>();
            list.Add(file.Replace('\\', '/'));
        }

        var clips = new System.Collections.Generic.List<SpriteClip>();
        foreach (var kv in groups)
        {
            if (!kv.Key.StartsWith("Walk"))
            {
                Debug.Log($"[몹] {code}: '{kv.Key}' 클립은 아직 쓰는 곳이 없어 건너뜁니다. (현재는 Walk로 시작하는 걷기 클립만 사용)");
                continue;
            }
            kv.Value.Sort((a, b) => FrameNumber(a).CompareTo(FrameNumber(b)));
            var frames = new System.Collections.Generic.List<Sprite>();
            foreach (string f in kv.Value)
            {
                Sprite sp = LoadSpriteWithPixelSettings(f);
                if (sp != null) frames.Add(sp);
            }
            if (frames.Count == 0) continue;

            // 기본 재생 속도: 한 바퀴(두 걸음) = 0.6초
            clips.Add(new SpriteClip { name = kv.Key, frames = frames.ToArray(), pingPong = false, fps = Mathf.Round(frames.Count / WalkCycleSeconds) });
            // 프레임이 3~4장이면 왕복 재생 버전도 추가 (비교 테스트용)
            if (frames.Count >= 3 && frames.Count <= 4)
                clips.Add(new SpriteClip { name = kv.Key + " 왕복", frames = frames.ToArray(), pingPong = true, fps = Mathf.Round((frames.Count * 2 - 2) / WalkCycleSeconds) });
        }

        if (clips.Count == 0)
        {
            if (anim != null) Object.DestroyImmediate(anim, true);
            return;
        }

        // 프레임이 많은 클립을 기본으로
        clips.Sort((a, b) => b.frames.Length.CompareTo(a.frames.Length));

        if (anim == null) anim = spriteT.gameObject.AddComponent<SpriteFrameAnimator>();
        anim.idleSprite = idleSprite;
        anim.walkClips = clips;
        anim.walkClipIndex = Mathf.Clamp(anim.walkClipIndex, 0, clips.Count - 1);

        var names = new System.Collections.Generic.List<string>();
        foreach (var c in clips) names.Add($"{c.name}({c.frames.Length}장, {c.fps}fps)");
        Debug.Log($"[몹] {code}: 걷기 애니메이션 적용 - {string.Join(", ", names)}");
    }

    public const float WalkCycleSeconds = 0.6f;

    private static int FrameNumber(string path)
    {
        string fname = Path.GetFileNameWithoutExtension(path);
        int us = fname.LastIndexOf('_');
        return (us >= 0 && int.TryParse(fname.Substring(us + 1), out int n)) ? n : 0;
    }

    private static Material GetOrCreateMobSpriteMaterial()
    {
        Shader shader = Shader.Find(MobSpriteShaderName);
        if (shader == null)
        {
            Debug.LogWarning($"[몹] 셰이더 '{MobSpriteShaderName}'를 찾지 못했습니다. (Assets/Main/In_Game/Shaders/SpriteLitShadow.shader 확인)");
            return null;
        }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MobSpriteMaterialPath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "Mat_2D_Mob" };
            AssetDatabase.CreateAsset(mat, MobSpriteMaterialPath);
            Debug.Log($"[몹] 그림자용 머티리얼 생성: {MobSpriteMaterialPath}");
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader; // 이전 버전(Standard)으로 만들어진 머티리얼 교체
        }

        mat.mainTexture = null;
        mat.color = Color.white;
        mat.SetFloat("_Cutoff", 0.5f);
        mat.renderQueue = -1; // 셰이더 기본값(AlphaTest) 사용
        mat.shaderKeywords = new string[0];
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Sprite LoadMobSprite(string code)
    {
        // 이름 규칙: Pic_{코드} (무기 이미지와 같은 규칙) 또는 Mob_{코드}
        string texPath = null;
        foreach (string prefix in new[] { "Pic_", "Mob_" })
        {
            foreach (string ext in new[] { ".png", ".PNG", ".jpg", ".jpeg" })
            {
                string p = $"{MobImageFolder}/{prefix}{code}{ext}";
                if (File.Exists(p)) { texPath = p; break; }
            }
            if (texPath != null) break;
        }
        if (texPath == null) return null;
        return LoadSpriteWithPixelSettings(texPath);
    }

    // 픽셀 아트용 스프라이트 임포트 설정(Point, 무압축, 밉맵 없음)을 보장하고 스프라이트를 반환
    private static Sprite LoadSpriteWithPixelSettings(string texPath)
    {
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

    // ------------------------------------------------------------------
    // 4) 몹 코드별 행동 프리셋 (Mob_Data.csv의 '고유능력' 설명 기준)
    //    값을 바꾸고 싶으면 여기 숫자를 고치거나, 실행 후 프리팹 인스펙터에서 직접 수정
    // ------------------------------------------------------------------
    private static void ApplyBehaviorPreset(GameObject root, string code)
    {
        MobAI ai = root.GetComponent<MobAI>();
        if (ai == null) return;

        KnockbackReceiver kb = root.GetComponent<KnockbackReceiver>();
        if (kb == null) kb = root.AddComponent<KnockbackReceiver>();

        // 기본값
        ai.targetPriority = MobAI.TargetPriority.Player;
        ai.aimLockTime = 0f;
        ai.retreatWhenTooClose = false;
        ai.accelerateWhileMoving = false;
        ai.meleeWindup = 0.35f;
        kb.resistance = 0f;

        switch (code)
        {
            case "MM001": // 근거리 기본
                ai.type = MobAI.MobType.Melee;
                break;
            case "MM002": // 근거리 탱커: 이동 시 점차 가속, 방향 전환이 느림
                ai.type = MobAI.MobType.Melee;
                ai.accelerateWhileMoving = true;
                ai.meleeWindup = 0.5f;
                kb.resistance = 0.6f;
                break;
            case "MM003": // 근거리 견제: 빠르고 날카로운 공격
                ai.type = MobAI.MobType.Melee;
                ai.meleeWindup = 0.2f;
                break;
            case "MR001": // 원거리 기본
                ai.type = MobAI.MobType.Ranged;
                ai.retreatWhenTooClose = true;
                break;
            case "MR002": // 원거리 공성: 방어기물(타워) 우선 타격
                ai.type = MobAI.MobType.Ranged;
                ai.targetPriority = MobAI.TargetPriority.TowerFirst;
                kb.resistance = 0.3f;
                break;
            case "MR003": // 원거리 저격: 3초 부동조준 후 확정 타격
                ai.type = MobAI.MobType.Ranged;
                ai.aimLockTime = 3f;
                ai.retreatWhenTooClose = true;
                break;
            case "ME001": // 엘리트 근거리
                ai.type = MobAI.MobType.Melee;
                ai.meleeWindup = 0.45f;
                kb.resistance = 0.5f;
                break;
            case "ME002": // 엘리트 원거리
                ai.type = MobAI.MobType.Ranged;
                kb.resistance = 0.5f;
                break;
            case "MB001": // 보스
                ai.type = MobAI.MobType.Multi;
                ai.meleeWindup = 0.6f;
                kb.resistance = 0.85f;
                break;
        }
    }
}
