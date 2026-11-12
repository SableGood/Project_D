using System.Collections.Generic;
using UnityEngine;

// v152 1인칭 3D 무기 (시험판: 콜트 W002)
//  - Resources/Model3D/Weapons/<모델>.fbx 를 카메라 오른쪽 아래에 띄우고, 2D 1인칭 그림은 숨김
//  - 움직이는 부품은 Blockbench 그룹 이름으로 찾음: mv_slide, mv_hammer, mv_trigger, mv_mag, mv_barrel
//    표시점: pt_muzzle(총구 → 총구 화염·탄 위치), pt_hand_r(오른손), pt_mag_axis(탄창이 빠지는 방향)
//  - 사격: 슬라이드 후퇴·복귀, 방아쇠 당김, 해머, 반동(총구 들림 + 뒤로 밀림). 탄이 떨어지면 슬라이드 후퇴 고정
//  - 재장전 (진행도 0~1, 소리 Colt_magout 0.02 / magin 0.55 / Cock 0.74 에 맞춤)
//      0.00~0.25 총을 안쪽으로 눕히고 탄창이 아래로 빠짐 · 왼손이 아래로 내려감
//      0.30~0.50 왼손이 새 탄창을 들고 올라옴 → 0.50~0.58 밀어 넣고 손바닥으로 '탁'
//      0.62~0.74 왼손이 슬라이드 뒤를 잡고 당겼다 놓음(0.74 '철컥') → 왼손 제자리
//  - 손: 임시 유인원 손 (상자). 털 #3A3132, 손 #A8704E. 유인원 캐릭터 작업 때 모델로 바꿀 예정
//  WeaponVisual이 1인칭일 때 Show, 탑뷰일 때 Hide를 부름
public class Viewmodel3D : MonoBehaviour
{
    // 무기 코드 → 모델 이름 (Resources/Model3D/Weapons/)
    private static readonly Dictionary<string, string> Models = new Dictionary<string, string>
    {
        { "W002", "Pistol_M1911" },
    };
    // v154 무기 '이름'에 들어 있으면 이 모델 (돌격소총 유진 계열 → M4)
    private static readonly Dictionary<string, string> NameModels = new Dictionary<string, string>
    {
        { "Duty", "Rifle_M4" },   // v155 6방향 그림은 듀티(Duty)였음
    };
    // v155 비교용: F9 = 같은 총의 Blockbench(FBX) 모델 <-> 메쉬 JSON 모델 전환
    private static readonly Dictionary<string, string> AltModels = new Dictionary<string, string> { { "Rifle_M4", "Rifle_Duty_BB" } };
    private static readonly Dictionary<string, string> TextureOf = new Dictionary<string, string> { { "Rifle_Duty_BB", "Rifle_M4" } };
    public static bool UseBlockbench;
    private static int toggleFrame = -1;
    // v154 모델별 화면 배치·손 크기. 메쉬 JSON 모델(Resources/Model3D/Weapons/<이름>_mesh)은 1칸 = 도트 1픽셀
    private class Profile
    {
        public float distance = 0.55f, screenHeight = 0.40f, yawInward = -7f, pitch = 7f, handScale = 1f, magWait = 18f, rackTravel = -1f, magDrop = 70f;
        public float gripPx = 81.5f, magLen = 0f;   // gripPx = 총구~오른손 표시점 거리(원본 칸). FBX 모델의 크기 환산용
        public Vector2 screenPos = new Vector2(0.56f, -0.74f);
    }
    private static readonly Dictionary<string, Profile> Profiles = new Dictionary<string, Profile>
    {
        { "Rifle_M4", RifleDuty }, { "Rifle_Duty_BB", RifleDuty },
    };
    // v155 소총은 권총(높이 62칸 = 화면 0.40)과 같은 실제 축척이 되도록 크게 + 몸 쪽으로 당김 (개머리판은 화면 밖)
    private static Profile RifleDuty => new Profile { distance = 0.5f, screenHeight = 0.64f, screenPos = new Vector2(0.36f, -0.80f), yawInward = -4f, pitch = 3f,
                                    handScale = 0.5f, magWait = 7f, rackTravel = 8f, magDrop = 34f, gripPx = 95.2f, magLen = 21f };

    [Header("화면 배치")]
    public float distance = 0.55f;        // 카메라에서 총까지 (m)
    public float screenHeight = 0.40f;    // 총 높이 / 화면 높이
    public Vector2 screenPos = new Vector2(0.56f, -0.74f);  // 손잡이 위치 (화면 가운데 0, 가장자리 ±1). v153: 잘린 팔이 안 보이게 낮춤
    public float yawInward = -7f;         // 총구를 화면 안쪽으로 살짝 돌림 (도)
    public float pitch = 7f;              // v153: 낮춘 만큼 총구를 들어 조준선 쪽을 향하게
    [Header("사격")]
    public float slideTravel = 15f;       // 슬라이드 후퇴 거리 (모델 칸)
    public float recoilPitch = 4f;         // v155 총구 들림은 줄이고 뒤로 밀림 위주
    public float pushPerShot = 0.4f;      // v155 쏠 때마다 쌓이는 밀림 (반동 1당)
    public float pushMax = 3.2f;
    public float pushBack = 0.02f;        // 쌓인 밀림 1당 뒤로 (m)
    public float pushRecover = 4.5f;      // 쌓인 밀림이 풀리는 속도        // 반동 1당 총구 들림 (도)
    public float recoilBack = 0.035f;     // 반동 1당 뒤로 밀림 (m)
    public float recoilRecover = 20f;     // v154 빠르게 원위치 (찰진 반동)
    public float triggerTravel = 1.6f;
    [Header("재장전")]
    public float reloadRoll = 17f;        // v154 옆으로 '까딱' 재끼는 각도
    public float reloadLift = 3f;         // 총구를 드는 각도
    public float ejectCasingScale = 0.35f; // v154 1인칭 탄피 크기 (화면 가까이라 작게)
    public float flashScale = 0.75f;      // v154 1인칭 총구 화염 크기 (v158 0.4 → 0.75)
    public float magDropDistance = 70f;   // 빠진 탄창이 떨어지는 거리 (모델 칸)
    [Header("손")]
    public Color furColor = new Color32(0x3A, 0x31, 0x32, 0xFF);
    public Color skinColor = new Color32(0xA8, 0x70, 0x4E, 0xFF);
    public Color outlineColor = new Color32(0x21, 0x14, 0x1A, 0xFF);
    public float outlineWidth = 1.2f;

    private Weapon weapon;
    private Transform root;         // 카메라의 자식. 화면 배치
    private Transform gunSpace;     // +z = 총구 방향, +y = 위, +x = 오른쪽. 1 = 모델 1칸
    private Transform fix;          // 모델을 gunSpace 축에 맞춤
    private Transform slide, hammer, trigger, mag, muzzlePt, ejectPt;
    private Vector3 slideRest, triggerRest, magRest; private Quaternion hammerRest;
    private Vector3 slideBackDir, triggerBackDir, magOutDir, hammerAxis;   // fix 로컬
    private Vector3 gripG, magAxisG;    // gunSpace 기준
    private float unitWorld;            // 모델 1칸의 월드 크기
    private Transform handR, handL;     // 손 (gunSpace 자식)
    private Vector3 handLRest; private Quaternion handLRestRot;
    private Vector3 slideRearTopG, magTopG, handLG;
    private Transform handLPt;
    private float modelHeight;

    private bool shown;
    private readonly List<SpriteRenderer> hiddenSprites = new List<SpriteRenderer>();
    private SpriteRenderer[] allSprites;
    private float shotStart = -9f, shotCycle = 0.08f, triggerStart = -9f;
    private float kick;
    private float drawStart = -9f, drawDur = 1f;
    private Material mat, furMat, skinMat;

    public static Viewmodel3D TryCreate(GameObject visual, Weapon w)
    {
        if (w == null || w.playerWepData == null) return null;
        if (!Models.TryGetValue(w.playerWepData.weaponID ?? "", out string model))
        {
            string nm = (w.playerWepData.weaponName ?? "") + " " + (w.playerWepData.spriteName ?? "");
            foreach (var kv in NameModels) if (nm.Contains(kv.Key)) { model = kv.Value; break; }
            if (model == null) return null;
        }
        Viewmodel3D vm = visual.AddComponent<Viewmodel3D>();
        vm.weapon = w;
        vm.baseModel = model;
        if (!vm.Setup()) { Destroy(vm); return null; }
        return vm;
    }

    private string baseModel;
    private bool builtFlag;
    private float gripPx = 81.5f;

    // 모델 불러오기 + 배치값 적용 + 조립 (F9 전환 때 다시 부름)
    private bool Setup()
    {
        string model = baseModel;
        builtFlag = UseBlockbench;
        if (UseBlockbench && AltModels.TryGetValue(baseModel, out string alt) && Resources.Load<GameObject>("Model3D/Weapons/" + alt) != null) { model = alt; }
        GameObject prefab = Resources.Load<GameObject>("Model3D/Weapons/" + model);
        GameObject temp = null;
        if (prefab == null)
        {
            TextAsset json = Resources.Load<TextAsset>("Model3D/Weapons/" + model + "_mesh");
            if (json != null) { temp = prefab = BuildFromJson(json.text, model); }
        }
        if (prefab == null) { Debug.LogWarning("[3D 무기] 모델 없음: Model3D/Weapons/" + model); return false; }
        jsonModel = temp != null;
        magLen = 0f;
        if (Profiles.TryGetValue(model, out Profile p))
        {
            distance = p.distance; screenHeight = p.screenHeight; screenPos = p.screenPos; yawInward = p.yawInward; pitch = p.pitch;
            handScale = p.handScale; magWait = p.magWait; magDropDistance = p.magDrop; if (p.rackTravel > 0f) rackTravel = p.rackTravel;
            gripPx = p.gripPx; magLen = p.magLen;
        }
        string texName = TextureOf.TryGetValue(model, out string tn) ? tn : model;
        Build(prefab, Resources.Load<Texture2D>("Model3D/Weapons/" + texName));
        if (temp != null) Destroy(temp);
        return true;
    }

    void Update()
    {
        if (!AltModels.ContainsKey(baseModel ?? "")) return;
        if (Input.GetKeyDown(KeyCode.F9) && toggleFrame != Time.frameCount)
        {
            toggleFrame = Time.frameCount;
            UseBlockbench = !UseBlockbench;
            AugmentManager.Toast(UseBlockbench ? "3D 총: Blockbench(FBX) 모델" : "3D 총: 메쉬 JSON 모델");
        }
        if (builtFlag != UseBlockbench)
        {
            Hide();
            if (root != null) Destroy(root.gameObject);
            root = null;
            Setup();
        }
    }

    private static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { Transform r = Find(c, name); if (r != null) return r; }
        return null;
    }

    // ================= v154 메쉬 JSON 모델 (6방향 도트 → Tools/sprite3d 로 만든 것) =================
    [System.Serializable] private class JGroup { public string name; public float[] v; public float[] uv; public int[] t; }
    [System.Serializable] private class JPoint { public string name; public float[] p; }
    [System.Serializable] private class JModel { public JGroup[] groups; public JPoint[] points; public float[] magUp; public float magLen; }
    private bool jsonModel;
    private float handScale = 1f, magWait = 18f, rackTravel = -1f, magLen = 0f;
    private Transform magTopPt, chargePt;

    private static GameObject BuildFromJson(string text, string name)
    {
        JModel jm = JsonUtility.FromJson<JModel>(text);
        GameObject go = new GameObject(name);
        go.SetActive(false);
        foreach (JGroup g in jm.groups)
        {
            GameObject part = new GameObject(g.name);
            part.transform.SetParent(go.transform, false);
            int n = g.v.Length / 3;
            var verts = new Vector3[n]; var uvs = new Vector2[n];
            for (int i = 0; i < n; i++) { verts[i] = new Vector3(g.v[i * 3], g.v[i * 3 + 1], g.v[i * 3 + 2]); uvs[i] = new Vector2(g.uv[i * 2], g.uv[i * 2 + 1]); }
            Mesh mesh = new Mesh { name = name + "_" + g.name };
            mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = g.t;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>();
        }
        Vector3 magUp = jm.magUp != null && jm.magUp.Length == 3 ? new Vector3(jm.magUp[0], jm.magUp[1], jm.magUp[2]).normalized : Vector3.up;
        foreach (JPoint p in jm.points)
        {
            Transform t = new GameObject(p.name).transform;
            t.SetParent(go.transform, false);
            t.localPosition = new Vector3(p.p[0], p.p[1], p.p[2]);
            if (p.name == "pt_mag_axis") t.localRotation = Quaternion.FromToRotation(Vector3.up, magUp);
        }
        return go;
    }

    private Material MakeMat(Texture tex, Color color)
    {
        Shader sh = Shader.Find("Project_D/Toon Lit");
        Material m = new Material(sh != null ? sh : Shader.Find("Standard"));
        if (tex != null) { tex.filterMode = FilterMode.Point; m.mainTexture = tex; }
        m.color = color;
        if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", outlineColor);
        if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", outlineWidth);
        if (m.HasProperty("_ShadowColor")) m.SetColor("_ShadowColor", new Color(0.55f, 0.5f, 0.62f, 1f));
        return m;
    }

    private void Build(GameObject prefab, Texture2D tex)
    {
        root = new GameObject("Viewmodel3D_" + prefab.name).transform;
        gunSpace = new GameObject("GunSpace").transform; gunSpace.SetParent(root, false);
        fix = new GameObject("Fix").transform; fix.SetParent(gunSpace, false);
        GameObject model = Instantiate(prefab, fix, false);
        model.name = prefab.name;
        model.SetActive(true);   // v154 JSON 모델 원본은 꺼진 상태로 만듦
        model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one;

        mat = MakeMat(tex, Color.white);
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] ms = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < ms.Length; i++) ms[i] = mat;
            r.sharedMaterials = ms;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        Transform mt = model.transform;
        slide = Find(mt, "mv_slide"); hammer = Find(mt, "mv_hammer"); trigger = Find(mt, "mv_trigger");
        mag = Find(mt, "mv_mag"); muzzlePt = Find(mt, "pt_muzzle");
        Transform handPt = Find(mt, "pt_hand_r"), magAxis = Find(mt, "pt_mag_axis"), eject = Find(mt, "pt_eject");
        ejectPt = eject;
        handLPt = Find(mt, "pt_hand_l"); magTopPt = Find(mt, "pt_mag_top"); chargePt = Find(mt, "pt_charge");


        // ---- 모델 축 (fix 로컬): Blockbench +x = 총구 방향. FBX 변환으로 부호가 바뀔 수 있어 표시점으로 판단 ----
        Vector3 L(Transform t) => t != null ? fix.InverseTransformPoint(t.position) : Vector3.zero;
        Vector3 mz = L(muzzlePt), hr = L(handPt);
        Vector3 fwdM = Mathf.Abs(mz.x - hr.x) >= Mathf.Abs(mz.z - hr.z) ? new Vector3(Mathf.Sign(mz.x - hr.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(mz.z - hr.z));
        Vector3 upM = Vector3.up;
        fix.localRotation = Quaternion.Inverse(Quaternion.LookRotation(fwdM, upM));
        // 탄피 배출구(총의 오른쪽)가 화면 오른쪽에 오지 않으면 모델이 거울상 → 좌우 뒤집기
        Vector3 ej = gunSpace.InverseTransformPoint(eject != null ? eject.position : Vector3.zero), ctr = gunSpace.InverseTransformPoint(handPt != null ? handPt.position : Vector3.zero);
        if (eject != null && ej.x - ctr.x < 0f) fix.localScale = Mathf.Abs(fwdM.x) > 0f ? new Vector3(1f, 1f, -1f) : new Vector3(-1f, 1f, 1f);

        // 모델 크기 (gunSpace 단위): 높이 기준으로 화면 크기를 맞춤
        Bounds b = new Bounds(); bool first = true;
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            Bounds rb = r.bounds;
            foreach (Vector3 c in Corners(rb))
            {
                Vector3 p = gunSpace.InverseTransformPoint(c);
                if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
        }
        // 모델 1칸 크기: 총구~손잡이 사이 거리로 추정 (Blockbench 원본에서 약 78칸)
        float muzzleToGrip = Vector3.Distance(gunSpace.InverseTransformPoint(muzzlePt.position), gunSpace.InverseTransformPoint(handPt.position));
        float unitsPerBB = jsonModel ? 1f : muzzleToGrip / gripPx;   // pt_muzzle(92,54) ~ pt_hand_r(14,24). JSON 모델은 1칸 = 1픽셀
        fix.localScale = fix.localScale / unitsPerBB;
        modelHeight = b.size.y / unitsPerBB;

        // ---- 움직이는 부품의 방향 (부품의 부모 기준 로컬) ----
        if (slide != null) { slideRest = slide.localPosition; slideBackDir = slide.parent.InverseTransformDirection(-gunSpace.forward).normalized; }
        if (trigger != null) { triggerRest = trigger.localPosition; triggerBackDir = trigger.parent.InverseTransformDirection(-gunSpace.forward).normalized; }
        if (hammer != null) { hammerRest = hammer.localRotation; hammerAxis = hammer.parent.InverseTransformDirection(gunSpace.right).normalized; }
        if (mag != null)
        {
            magRest = mag.localPosition;
            Vector3 down = magAxis != null ? -magAxis.up : -gunSpace.up;
            magOutDir = mag.parent.InverseTransformDirection(down).normalized;
        }
        gripG = gunSpace.InverseTransformPoint(handPt.position);
        magAxisG = gunSpace.InverseTransformDirection(magAxis != null ? magAxis.up : gunSpace.up).normalized;
        slideRearTopG = chargePt != null ? gunSpace.InverseTransformPoint(chargePt.position) : gripG + new Vector3(0f, 33f, 2f);   // 슬라이드 뒤쪽 윗부분 / 소총 장전손잡이
        if (magTopPt != null) magTopG = gunSpace.InverseTransformPoint(magTopPt.position);
        if (handLPt != null) handLG = gunSpace.InverseTransformPoint(handLPt.position);

        BuildHands();
        root.gameObject.SetActive(false);
    }

    private static IEnumerable<Vector3> Corners(Bounds b)
    {
        Vector3 c = b.center, e = b.extents;
        for (int i = 0; i < 8; i++) yield return c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
    }

    // ================= 임시 유인원 손 =================
    private Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Material m, Vector3 euler = default)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(g.GetComponent<Collider>());
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localRotation = Quaternion.Euler(euler); g.transform.localScale = size;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return g.transform;
    }

    private void BuildHands()
    {
        furMat = MakeMat(null, furColor);
        skinMat = MakeMat(null, skinColor);
        Vector3 gripAxis = handLPt != null ? new Vector3(0f, 1f, 0.35f).normalized : magAxisG;   // v154 소총 손잡이는 아래가 뒤로 기울어짐
        Quaternion gripRot = Quaternion.LookRotation(Vector3.Cross(Vector3.right, gripAxis).normalized, gripAxis); // +y = 손잡이 축, +z = 앞
        const float gw = 7.5f;   // 손잡이 반쪽 두께 + 여유

        // 오른손: 손잡이를 감싸 쥠 (손바닥 = 오른쪽, 손가락 = 앞, 엄지 = 왼쪽 위)
        handR = new GameObject("HandR").transform; handR.SetParent(gunSpace, false);
        handR.localPosition = gripG; handR.localRotation = gripRot; handR.localScale = Vector3.one * handScale;
        Box(handR, "Palm", new Vector3(gw + 2.5f, -1f, -2f), new Vector3(5f, 28f, 24f), skinMat);
        Box(handR, "Fingers", new Vector3(0.5f, -3f, 14f), new Vector3(2f * gw + 6f, 24f, 7f), skinMat);
        Box(handR, "Thumb", new Vector3(-gw - 1.5f, 9f, 5f), new Vector3(4f, 5f, 16f), skinMat, new Vector3(-12f, 0f, 0f));
        Box(handR, "Wrist", new Vector3(gw + 3f, -8f, -16f), new Vector3(15f, 20f, 16f), furMat, new Vector3(20f, 0f, 0f));
        Box(handR, "Forearm", new Vector3(gw + 10f, -30f, -58f), new Vector3(22f, 22f, 80f), furMat, new Vector3(28f, -12f, 0f));

        // 왼손: 오른손 손가락을 아래·앞에서 감싸 받침 (손바닥 = 왼쪽)
        handL = new GameObject("HandL").transform; handL.SetParent(gunSpace, false);
        handLRest = gripG + new Vector3(-2f, -5f, 3f); handLRestRot = gripRot;
        if (handLPt != null) { handLRest = handLG + new Vector3(0f, 2f, 0f); handLRestRot = Quaternion.identity; }   // v154 소총: 앞손잡이
        handL.localPosition = handLRest; handL.localRotation = handLRestRot; handL.localScale = Vector3.one * handScale;
        Box(handL, "Palm", new Vector3(-gw - 7f, -2f, 2f), new Vector3(5f, 26f, 24f), skinMat);
        Box(handL, "Fingers", new Vector3(-1f, -4f, 20f), new Vector3(2f * gw + 12f, 22f, 6f), skinMat);
        Box(handL, "Thumb", new Vector3(-gw - 4f, 10f, 10f), new Vector3(4f, 5f, 15f), skinMat, new Vector3(-8f, 0f, 0f));
        Box(handL, "Wrist", new Vector3(-gw - 8f, -9f, -14f), new Vector3(15f, 20f, 16f), furMat, new Vector3(20f, 0f, 0f));
        Box(handL, "Forearm", new Vector3(-gw - 16f, -30f, -56f), new Vector3(22f, 22f, 80f), furMat, new Vector3(28f, 14f, 0f));
    }

    // ================= 표시 =================
    public void Show(Camera cam)
    {
        if (root == null || cam == null) return;
        if (!shown)
        {
            shown = true;
            hiddenSprites.Clear();
            allSprites = GetComponentsInChildren<SpriteRenderer>(true);
            foreach (SpriteRenderer sr in allSprites) if (sr.enabled) hiddenSprites.Add(sr);
            root.gameObject.SetActive(true);
        }
        if (allSprites != null) foreach (SpriteRenderer sr in allSprites) if (sr != null) sr.enabled = false;
        if (root.parent != cam.transform) root.SetParent(cam.transform, false);
        Pose(cam);
    }

    public void Hide()
    {
        if (!shown) return;
        shown = false;
        if (root != null) root.gameObject.SetActive(false);
        if (weapon != null) weapon.fxMuzzle = null;
        foreach (SpriteRenderer sr in hiddenSprites) if (sr != null) sr.enabled = true;
        hiddenSprites.Clear();
    }

    void OnDisable() { Hide(); }
    void OnDestroy() { if (root != null) Destroy(root.gameObject); }

    // ================= 이벤트 (WeaponVisual) =================
    public void OnShot(float cooldown)
    {
        shotStart = Time.time; triggerStart = Time.time;
        shotRollSign = Random.value < 0.5f ? -1f : 1f;
        shotCycle = Mathf.Clamp(cooldown * 0.75f, 0.05f, 0.1f);
    }
    public void OnKick(float strength) { kick = Mathf.Min(kick + strength, Mathf.Max(strength * 1.8f, 1.6f)); push = Mathf.Min(push + strength * pushPerShot, pushMax); }
    private float push;
    public void OnDraw(float duration) { drawStart = Time.time; drawDur = Mathf.Max(0.2f, duration); }

    // ================= 매 프레임 =================
    private static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    private static float Seg(float r, float a, float b) => Mathf.Clamp01((r - a) / Mathf.Max(0.0001f, b - a));

    private void Pose(Camera cam)
    {
        if (decayFrame != Time.frameCount) { decayFrame = Time.frameCount; if (kick > 0f) kick = kick < 0.002f ? 0f : kick * Mathf.Exp(-recoilRecover * Time.deltaTime); if (push > 0f) push = push < 0.002f ? 0f : push * Mathf.Exp(-pushRecover * Time.deltaTime); }
        bool reloading = weapon != null && weapon.isReloading;
        float r = reloading ? weapon.ReloadProgress : 0f;
        bool empty = weapon != null && weapon.IsGun && weapon.currentAmmo <= 0;

        // ---- 화면 배치 ----
        float halfH = distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * cam.aspect;
        unitWorld = 2f * halfH * screenHeight / Mathf.Max(1f, modelHeight);
        gunSpace.localScale = Vector3.one * unitWorld;

        float tilt = reloading ? ReloadTilt(r) : 0f;
        float shotRoll = 0f;
        float sk = (Time.time - shotStart) / 0.07f;
        if (sk >= 0f && sk < 1f) shotRoll = Mathf.Sin(Mathf.PI * sk) * shotRollSign * 1.6f;   // v154 쏠 때 살짝 '탁' 비틀림
        float bob = Mathf.Sin(Time.time * 1.6f) * 0.004f;
        Vector3 pos = new Vector3(screenPos.x * halfW, screenPos.y * halfH + bob, distance);
        pos += new Vector3(-0.06f * tilt, 0.03f * tilt, -kick * recoilBack - push * pushBack);
        pos.y -= push * pushBack * 0.25f;   // 밀리면서 살짝 내려앉음
        // 꺼내기: 아래에서 올라옴
        float dt = (Time.time - drawStart) / drawDur;
        if (dt >= 0f && dt < 1f) pos.y -= (1f - Ease(dt / 0.45f)) * halfH * 0.7f;

        // 손잡이(오른손)가 pos에 오도록 gunSpace를 옮김
        Quaternion rot = Quaternion.Euler(-pitch - kick * recoilPitch - tilt * reloadLift, yawInward, -tilt * reloadRoll + shotRoll);
        root.localPosition = pos;
        root.localRotation = rot;
        gunSpace.localRotation = Quaternion.identity;
        gunSpace.localPosition = -(gripG * unitWorld);

        // ---- 슬라이드 ----
        float slideOff = 0f;
        float st = (Time.time - shotStart) / shotCycle;
        if (st >= 0f && st < 1f) slideOff = st < 0.35f ? Ease(st / 0.35f) : 1f - Ease((st - 0.35f) / 0.65f);
        bool locked = empty && !reloading;
        if (locked && (st >= 0.35f || st < 0f)) slideOff = 1f;
        if (reloading)
        {
            bool wasEmpty = slide != null && slideHeldOpen;
            // 0.62~0.70 당김, 0.74 놓음 ('철컥')
            float pull = Seg(r, 0.64f, 0.71f);
            float release = Seg(r, 0.74f, 0.77f);
            slideOff = Mathf.Max(wasEmpty && r < 0.74f ? 1f : 0f, pull * (1f - release));
            if (r >= 0.74f && !releasedThisReload) { releasedThisReload = true; OnKick(0.55f); }
        }
        else { releasedThisReload = false; insertedThisReload = false; slideHeldOpen = locked; }
        if (slide != null) slide.localPosition = slideRest + slideBackDir * (slideOff * slideTravel * SlideScale());

        // ---- 방아쇠 · 해머 ----
        float tt = (Time.time - triggerStart) / 0.09f;
        float pullT = tt >= 0f && tt < 1f ? Mathf.Sin(Mathf.PI * tt) : 0f;
        if (trigger != null) trigger.localPosition = triggerRest + triggerBackDir * (pullT * triggerTravel * SlideScale());
        if (hammer != null) hammer.localRotation = Quaternion.AngleAxis(slideOff * 18f, hammerAxis) * hammerRest;

        // ---- 탄창 · 왼손 ----
        Vector3 magOff = Vector3.zero; bool magVisible = true;
        Vector3 lPos = handLRest; Quaternion lRot = handLRestRot;
        if (reloading)
        {
            Vector3 down = -magAxisG;
            // 탄창 빠짐 0.03~0.22 (가속), 0.22~0.32 안 보임
            float drop = Seg(r, 0.03f, 0.22f);
            float outD = drop * drop * magDropDistance;
            // 새 탄창: 0.32~0.50 아래에서 올라와 입구 밑에 대기, 0.50~0.56 밀어 넣음
            float bring = Ease(Seg(r, 0.30f, 0.50f));
            float insert = Ease(Seg(r, 0.50f, 0.56f));
            if (r < 0.22f) magOff = down * outD;
            else if (r < 0.30f) magVisible = false;
            else magOff = down * Mathf.Lerp(Mathf.Lerp(magDropDistance, magWait, bring), 0f, insert);
            if (r >= 0.56f && !insertedThisReload) { insertedThisReload = true; OnKick(0.45f); }   // v154 탄창 '탁' 끼움

            // 왼손 경로
            float hs = handScale;
            Vector3 away = handLRest + new Vector3(-30f, -110f, -30f) * hs;          // 화면 밖 (허리춤)
            bool rifle = magTopPt != null && magLen > 0f;
            Vector3 underMag = rifle ? magTopG + down * (magLen + magWait + 2f) : gripG + down * 46f + new Vector3(-3f, 0f, 0f);    // 새 탄창 바닥을 쥔 손
            Vector3 slap = rifle ? magTopG + down * (magLen + 1f) : gripG + down * 30f + new Vector3(-3f, 0f, 0f);
            Vector3 rack = slideRearTopG + new Vector3(-4f, 10f, -18f) * hs;          // 슬라이드 뒤 윗부분 / 장전손잡이를 쥠
            Quaternion rackRot = Quaternion.Euler(0f, 0f, -70f) * handLRestRot;
            if (r < 0.10f) lPos = handLRest;
            else if (r < 0.28f) lPos = Vector3.Lerp(handLRest, away, Ease(Seg(r, 0.10f, 0.28f)));
            else if (r < 0.32f) lPos = away;
            else if (r < 0.56f)
            {
                Vector3 hold = Vector3.Lerp(away, underMag, bring);
                lPos = Vector3.Lerp(hold, slap, insert);
            }
            else if (r < 0.62f) lPos = slap;
            else if (r < 0.66f) { lPos = Vector3.Lerp(slap, rack, Ease(Seg(r, 0.62f, 0.66f))); lRot = Quaternion.Slerp(handLRestRot, rackRot, Ease(Seg(r, 0.62f, 0.66f))); }
            else if (r < 0.76f) { lPos = rack - new Vector3(0f, 0f, slideOff * (rackTravel > 0f ? rackTravel : slideTravel)); lRot = rackRot; }
            else { float back = Ease(Seg(r, 0.76f, 0.92f)); lPos = Vector3.Lerp(rack, handLRest, back); lRot = Quaternion.Slerp(rackRot, handLRestRot, back); }
        }
        if (mag != null)
        {
            mag.gameObject.SetActive(magVisible);
            mag.localPosition = magRest + mag.parent.InverseTransformVector(gunSpace.TransformVector(magOff));
        }
        handL.localPosition = lPos; handL.localRotation = lRot;

        // 총구 위치 → 무기의 총구 (탄·총구 화염)
        if (weapon != null && weapon.muzzle != null && muzzlePt != null) weapon.muzzle.position = muzzlePt.position;
        if (weapon != null) { weapon.fxMuzzle = muzzlePt; weapon.fxForward = gunSpace.forward; weapon.fxScale = flashScale; }
    }

    // v154 재장전 기울기: 0.06초 안에 빠르게 옆으로 재낌 → 유지 → 끝에서 빠르게 원위치 (살짝 반대로 튕김)
    private static float ReloadTilt(float r)
    {
        const float inEnd = 0.07f, outStart = 0.86f, outEnd = 0.93f;
        if (r < inEnd) { float k = r / inEnd; return 1f - (1f - k) * (1f - k) * (1f - k); }
        if (r < outStart) return 1f;
        if (r < outEnd) { float k = (r - outStart) / (outEnd - outStart); return 1f - k * k; }
        float b = Seg(r, outEnd, 1f);
        return -0.12f * Mathf.Sin(Mathf.PI * b);
    }

    // v154 1인칭 탄피: 모델의 배출구 위치 + 화면 오른쪽/위 방향
    public bool EjectInfo(out Vector3 pos, out Vector3 right, out Vector3 up, out float scale)
    {
        pos = right = up = Vector3.zero; scale = ejectCasingScale;
        if (!shown || ejectPt == null || root == null || !root.gameObject.activeInHierarchy) return false;
        pos = ejectPt.position;
        right = root.right; up = root.up;
        return true;
    }
    public bool Shown => shown;

    private float shotRollSign = 1f;
    private bool slideHeldOpen, releasedThisReload, insertedThisReload;
    private int decayFrame = -1;

    // 부품 부모의 로컬 단위 → 모델 1칸 (fix 스케일 반영)
    private float SlideScale()
    {
        if (slide == null) return 1f;
        // gunSpace 1 = 모델 1칸. 부품 부모 공간에서 1칸의 길이
        Vector3 a = slide.parent.InverseTransformVector(gunSpace.TransformVector(Vector3.forward));
        return a.magnitude;
    }
}
