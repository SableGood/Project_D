using System.Collections.Generic;
using UnityEngine;

// 플레이어 상·하체 분리 애니메이션 (탑뷰)
//  ■ 상체: 조준 방향(마우스)에 따라 8방향 그림 + 가만히 있으면 대기(Idle) 그림
//  ■ 하체: 움직이면 걷기 프레임, 멈추면 서 있는 프레임. 이동 방향에 따라 좌우 반전
//  ■ 무기: 상체 그림의 '손' 위치에 무기 손잡이가 오도록 무기 장착점을 매 프레임 옮김
//  ■ 이음새: 상체 아래쪽·하체 위쪽 가장자리에는 검은 외곽선을 그리지 않음 (셰이더 _OutlineSkip)
//  ■ 1인칭: 실제 무기는 카메라 앞(뷰모델)에 있으므로, 손 위치에 '그림자 전용' 무기를 두어 바닥에 무기 그림자가 보이게 함
//
// 구조:  플레이어 ─ Sprite (하체, SpriteRenderer + Billboard)
//                     └─ Upper (상체, SpriteRenderer, 피벗 = 엉덩이)
//  * [Tools/플레이어/3. 상·하체 애니메이션 적용]이 Resources/Image/Players/{이름}/ 의 그림과 rig.json으로 자동 구성합니다.
//  * 깊이: Project_D/Sprite Shadow 셰이더의 '발밑 세운 판' 깊이에 작은 앞당김(상체 0.03m, 무기 0.06m)을 줘서
//          하체 < 상체 < 무기 순서로 깨끗하게 겹침
[DefaultExecutionOrder(50)] // Billboard(0) 다음, WeaponVisual(100) 전에
public class PlayerBodyAnimator : MonoBehaviour
{
    public enum Dir { Idle, Up, UpRight, Right, DownRight, Down, DownLeft, Left, UpLeft }

    [Header("연결")]
    public SpriteRenderer lowerRenderer;
    public SpriteRenderer upperRenderer;

    [Header("상체 (Dir 순서: Idle, Up, UpRight, Right, DownRight, Down, DownLeft, Left, UpLeft)")]
    public Sprite[] upperSprites = new Sprite[9];
    [Tooltip("상체 그림 기준 손 위치(미터, 상체 피벗=엉덩이 기준, x 오른쪽 / y 위)")]
    public Vector2[] handOffsets = new Vector2[9];

    [Header("하체")]
    public Sprite[] walkFrames;
    public int standFrame = 1;
    public float walkFps = 10f;
    [Tooltip("하체 그림이 오른쪽을 보고 그려졌으면 체크 (왼쪽 이동 시 반전)")]
    public bool legsFaceRight = true;

    [Header("대기")]
    [Tooltip("멈춘 채로 쏘지 않고 이 시간(초)이 지나면 대기 자세")]
    public float idleDelay = 1.0f;

    [Header("초상화 (HUD·캐릭터 선택용)")]
    public Sprite portrait;

    [Header("겹침 깊이 앞당김 (미터)")]
    public float upperDepthBias = 0.03f;
    public float weaponDepthBias = 0.06f;

    [Header("상·하체 이음새 (외곽선 생략)")]
    [Tooltip("상체 그림에서 엉덩이(이음새) 선의 높이 (0 = 그림 맨 아래, 1 = 맨 위). 메뉴가 자동 설정")]
    [Range(0f, 1f)] public float upperJointV = 0f;
    [Tooltip("하체 그림에서 허리 윗선의 높이 (0 = 맨 아래, 1 = 맨 위). 메뉴가 자동 설정")]
    [Range(0f, 1f)] public float lowerJointV = 1f;
    [Tooltip("상체 이음새 선 아래로 이 폭(텍셀)만큼은 외곽선을 그리지 않음")]
    public float upperJoinTexels = 3f;
    [Tooltip("하체 허리 윗선 위로 이 폭(텍셀)만큼은 외곽선을 그리지 않음")]
    public float lowerJoinTexels = 2f;

    [Header("1인칭 무기 그림자")]
    public bool fpsWeaponShadow = true;

    [Header("새 몸 그림 + 손만 따로 (F5로 예전 그림과 비교)")]
    [Tooltip("켜면 Resources/{armRigFolder}의 새 그림(팔 없는 상체 + 새 하체)을 쓰고, 무기는 작은 손 두 개가 잡음")]
    public bool armRigTest = false; // v112: 기본은 팔이 그려진 원래 통짜 그림 (테스트 모드 F5로 팔 분리 그림과 비교)
    public KeyCode armRigToggleKey = KeyCode.F5;
    public string armRigFolder = "Image/Players/Chimp_Arm";
    [Tooltip("무기 손잡이가 도는 중심 (상체 피벗=엉덩이 기준, 미터) — 가슴 앞")]
    public Vector2 gripCenter = new Vector2(-0.02f, 0.31f);
    [Tooltip("조준 방향으로 손잡이가 중심에서 떨어지는 거리 (가로, 세로 반지름)")]
    public Vector2 gripRadius = new Vector2(0.25f, 0.17f);
    [Tooltip("대기 자세에서 무기를 아래로 내리는 각도")]
    public float idleArmAngle = -35f;
    [Tooltip("앞손 위치 = 손잡이에서 무기 방향으로 '무기 길이 × 이 비율'")]
    public float frontGripRatio = 0.33f;
    [Tooltip("이 각도보다 위를 겨누면 무기·손이 몸 뒤로 (sin 값, 0.6 ≒ 37도). 1보다 크면 항상 몸 앞")]
    public float gunBehindAboveSin = 2f;
    public float handDepthBias = 0.02f; // 무기보다 앞
    [Header("팔 (어깨 → 팔꿈치 → 손, 길이가 정해진 두 마디)")]
    [Tooltip("어깨 높이 (상체 피벗=엉덩이 기준, 미터)")]
    public float shoulderY = 0.40f;
    [Tooltip("뒷손(손잡이) 팔의 어깨: 조준하는 쪽으로 이만큼")]
    public float rearShoulderX = 0.125f;
    [Tooltip("앞손 팔의 어깨: 조준하는 쪽으로 이만큼 (0 = 가슴 가운데)")]
    public float frontShoulderX = 0f;
    public float upperArmLength = 0.2167f;
    public float forearmLength = 0.2167f;
    [Tooltip("손이 너무 멀 때 팔을 최대 몇 배까지 늘릴지")]
    public float maxArmStretch = 1.15f;
    [Tooltip("한 손 무기: 어깨에서 손잡이까지 거리 (팔을 거의 다 뻗은 길이)")]
    public float oneHandReach = 0.38f;
    public float armDepthBias = 0.04f;

    public Dir CurrentDir { get; private set; }

    // ---- 새 몸 + 손 ----
    private Sprite handRearSprite, handFrontSprite;
    private SpriteRenderer handRear, handFront;
    private Sprite upperArmSprite, forearmSprite;
    private SpriteRenderer rearUpper, rearFore, frontUpper, frontFore;
    private Vector3 lastFeet;
    private bool lastFeetValid, gunBehind, aimLeft;
    private bool armRigLoaded, armRigOn;
    private Sprite[] oldUpper, oldWalk, rigUpper, rigWalk;
    private float oldUpperJointV, oldLowerJointV;
    private const float RigUpperJointV = 14f / 240f, RigLowerJointV = 1f - 5f / 240f;

    private PlayerController pc;
    private PlayerWeaponManager weapons;
    private Billboard board;
    private Camera cam;
    private float walkPhase;
    private bool facingLeft;
    private float lastActiveTime;
    private MaterialPropertyBlock block;
    private readonly List<SpriteRenderer> weaponRenderers = new List<SpriteRenderer>();
    private int weaponChildSignature = -1;
    private static readonly int FeetPosId = Shader.PropertyToID("_FeetPos");
    private static readonly int OutlineSkipId = Shader.PropertyToID("_OutlineSkip");
    private static readonly int OutlineSkipSpanId = Shader.PropertyToID("_OutlineSkipSpan");
    private readonly Dictionary<Sprite, Vector2> waistCache = new Dictionary<Sprite, Vector2>();

    // 1인칭 무기 그림자용 (탑뷰에서 본 무기 그림의 손잡이 기준 위치·크기를 기억해 둠)
    private SpriteRenderer shadowProxy;
    private Vector3 weaponSpriteOffset = new Vector3(0.2f, 0f, 0f); // 손(장착점) → 무기 그림 중심 (미터, 오른쪽 조준 기준)
    private Vector3 weaponSpriteScale = Vector3.zero;
    private bool lastFps;

    // HUD/메뉴용 초상화: 이 컴포넌트가 있으면 지정 초상화, 없으면 빌보드 그림
    public static Sprite GetPortrait(GameObject player)
    {
        if (player == null) return null;
        PlayerBodyAnimator a = player.GetComponentInChildren<PlayerBodyAnimator>(true);
        if (a != null && a.portrait != null) return a.portrait;
        Billboard b = player.GetComponentInChildren<Billboard>(true);
        SpriteRenderer sr = b != null ? b.GetComponent<SpriteRenderer>() : null;
        return sr != null ? sr.sprite : null;
    }

    void Awake()
    {
        pc = GetComponent<PlayerController>();
        weapons = GetComponent<PlayerWeaponManager>();
        if (lowerRenderer != null) board = lowerRenderer.GetComponent<Billboard>();
        lastActiveTime = -999f;
    }

    void Start()
    {
        if (armRigTest) SetArmRig(true);
    }

    // 새 그림 불러오기 (한 번만)
    private bool LoadArmRig()
    {
        if (armRigLoaded) return rigUpper != null;
        armRigLoaded = true;
        string[] names = { "Idle", "Up", "UpRight", "Right", "DownRight", "Down", "DownLeft", "Left", "UpLeft" };
        var up = new Sprite[9];
        for (int i = 0; i < 9; i++)
        {
            up[i] = Resources.Load<Sprite>($"{armRigFolder}/Upper/Upper_{names[i]}");
            if (up[i] == null) { Debug.LogWarning($"[새 몸 그림] {armRigFolder}/Upper/Upper_{names[i]} 그림이 없어 끕니다."); return false; }
        }
        var walk = new List<Sprite>();
        for (int i = 1; i <= 12; i++)
        {
            Sprite s = Resources.Load<Sprite>($"{armRigFolder}/Lower/Walk6_{i:00}");
            if (s == null) break;
            walk.Add(s);
        }
        handRearSprite = Resources.Load<Sprite>($"{armRigFolder}/Hands/Hand_Rear");
        handFrontSprite = Resources.Load<Sprite>($"{armRigFolder}/Hands/Hand_Front");
        upperArmSprite = Resources.Load<Sprite>($"{armRigFolder}/Hands/UpperArm");
        forearmSprite = Resources.Load<Sprite>($"{armRigFolder}/Hands/Forearm");
        if (walk.Count == 0 || handRearSprite == null) { Debug.LogWarning("[새 몸 그림] 하체 또는 손 그림이 없어 끕니다."); return false; }
        rigUpper = up;
        rigWalk = walk.ToArray();
        return true;
    }

    private void SetArmRig(bool on)
    {
        if (on && !LoadArmRig()) on = false;
        if (on == armRigOn) return;
        armRigOn = on;
        if (on)
        {
            oldUpper = upperSprites; oldWalk = walkFrames;
            oldUpperJointV = upperJointV; oldLowerJointV = lowerJointV;
            upperSprites = rigUpper; walkFrames = rigWalk;
            upperJointV = RigUpperJointV; lowerJointV = RigLowerJointV;
            if (handRear == null && upperRenderer != null)
            {
                handRear = MakeHand("Hand_Rear", handRearSprite);
                handFront = MakeHand("Hand_Front", handFrontSprite != null ? handFrontSprite : handRearSprite);
                if (upperArmSprite != null && forearmSprite != null)
                {
                    rearUpper = MakeHand("Arm_Rear_Upper", upperArmSprite);
                    rearFore = MakeHand("Arm_Rear_Fore", forearmSprite);
                    frontUpper = MakeHand("Arm_Front_Upper", upperArmSprite);
                    frontFore = MakeHand("Arm_Front_Fore", forearmSprite);
                }
                PlayerHands ph = gameObject.GetComponent<PlayerHands>();
                if (ph == null) ph = gameObject.AddComponent<PlayerHands>();
                ph.body = this;
            }
            SetRigPartsActive(true);
        }
        else
        {
            if (oldUpper != null) upperSprites = oldUpper;
            if (oldWalk != null) walkFrames = oldWalk;
            upperJointV = oldUpperJointV; lowerJointV = oldLowerJointV;
            SetRigPartsActive(false);
        }
        waistCache.Clear();
        Debug.Log(on ? "[새 몸 그림] 켜짐 (F5로 예전 그림)" : "[새 몸 그림] 꺼짐 (F5로 새 그림)");
    }

    private void SetRigPartsActive(bool on)
    {
        foreach (SpriteRenderer r in new[] { handRear, handFront, rearUpper, rearFore, frontUpper, frontFore })
            if (r != null) r.gameObject.SetActive(on);
    }

    // 두 마디 팔: 어깨(상체 로컬)에서 손(월드)까지. 팔꿈치는 아래쪽으로 굽힘
    private void PoseArm(SpriteRenderer up, SpriteRenderer fore, Vector2 shoulder, Vector3 handWorld, bool show, float bias)
    {
        if (up == null || fore == null) return;
        up.enabled = fore.enabled = show;
        if (!show) return;
        Transform ut = upperRenderer.transform;
        Vector2 hand = ut.InverseTransformPoint(handWorld);
        Vector2 d = hand - shoulder;
        float dist = Mathf.Max(0.001f, d.magnitude);
        float k = Mathf.Clamp(dist / (upperArmLength + forearmLength), 1f, Mathf.Max(1f, maxArmStretch));
        float lu = upperArmLength * k, lf = forearmLength * k;
        float dd = Mathf.Min(dist, lu + lf - 0.0001f);
        float cosA = Mathf.Clamp((lu * lu + dd * dd - lf * lf) / (2f * lu * dd), -1f, 1f);
        float a = Mathf.Acos(cosA) * Mathf.Rad2Deg;
        float baseAng = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        float upAng = aimLeft ? baseAng + a : baseAng - a; // 팔꿈치가 아래로
        Vector2 elbow = shoulder + new Vector2(Mathf.Cos(upAng * Mathf.Deg2Rad), Mathf.Sin(upAng * Mathf.Deg2Rad)) * lu;
        Vector2 fd = hand - elbow;
        float foreAng = Mathf.Atan2(fd.y, fd.x) * Mathf.Rad2Deg;
        float foreK = fd.magnitude / Mathf.Max(0.001f, forearmLength);
        float sy = aimLeft ? -1f : 1f; // 밝은 쪽(위)이 항상 위로
        up.transform.localPosition = new Vector3(shoulder.x, shoulder.y, 0f);
        up.transform.localRotation = Quaternion.Euler(0f, 0f, upAng);
        up.transform.localScale = new Vector3(k, sy, 1f);
        fore.transform.localPosition = new Vector3(elbow.x, elbow.y, 0f);
        fore.transform.localRotation = Quaternion.Euler(0f, 0f, foreAng);
        fore.transform.localScale = new Vector3(Mathf.Min(foreK, maxArmStretch), sy, 1f);
        var shadow = lowerRenderer.shadowCastingMode;
        up.shadowCastingMode = fore.shadowCastingMode = shadow;
        SetFeet(up, lastFeet, lastFeetValid ? 1f + bias : 0f);
        SetFeet(fore, lastFeet, lastFeetValid ? 1f + bias + 0.005f : 0f);
    }

    private SpriteRenderer MakeHand(string n, Sprite sp)
    {
        GameObject go = new GameObject(n);
        go.layer = upperRenderer.gameObject.layer;
        go.transform.SetParent(upperRenderer.transform, false);
        SpriteRenderer r = go.AddComponent<SpriteRenderer>();
        r.sprite = sp;
        r.sharedMaterial = upperRenderer.sharedMaterial;
        r.sortingLayerID = upperRenderer.sortingLayerID;
        r.sortingOrder = upperRenderer.sortingOrder + 2;
        r.receiveShadows = upperRenderer.receiveShadows;
        return r;
    }

    // 한 손으로 드는 무기: 권총·근접 (앞손 없음)
    private bool OneHanded()
    {
        Weapon w = weapons != null ? weapons.CurrentWeapon : null;
        PlayerWeaponData d = w != null ? w.playerWepData : null;
        if (d == null) return false;
        string role = d.role ?? "";
        string n = d.weaponName ?? "";
        return role.Contains("권총") || role.Contains("근접") || n.StartsWith("Colt") || !d.UsesAmmo;
    }

    // 무기 손잡이 위치: 가슴 앞에서 조준 방향으로 타원을 그리며 돎
    private Vector3 GripPoint(Vector2 aim, bool idle, bool fps)
    {
        float th = fps ? 0f : Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
        if (idle) th = aim.x < 0f ? 180f - idleArmAngle : idleArmAngle;
        float c = Mathf.Cos(th * Mathf.Deg2Rad), s = Mathf.Sin(th * Mathf.Deg2Rad);
        gunBehind = !fps && !idle && s > gunBehindAboveSin;
        aimLeft = c < 0f;
        // 한 손 무기(권총·근접): 팔을 뻗어 어깨에서 조준 방향으로 (몸 위에 접히면 털색이 겹쳐 팔이 안 보임)
        if (OneHanded())
        {
            float side = aimLeft ? -1f : 1f;
            Vector2 sh = new Vector2(side * rearShoulderX, shoulderY);
            return upperRenderer.transform.TransformPoint(new Vector3(sh.x + oneHandReach * c, sh.y + oneHandReach * s, 0f));
        }
        return upperRenderer.transform.TransformPoint(new Vector3(gripCenter.x + gripRadius.x * c, gripCenter.y + gripRadius.y * s, 0f));
    }

    // 무기가 자리를 잡은 뒤(WeaponVisual 다음) PlayerHands가 호출: 뒷손 = 손잡이, 앞손 = 무기 앞쪽
    public void UpdateHands()
    {
        if (!armRigOn || handRear == null) return;
        bool fps = pc != null && pc.isFirstPerson;
        var shadow = lowerRenderer.shadowCastingMode;
        handRear.shadowCastingMode = shadow;
        handFront.shadowCastingMode = shadow;
        if (weapons == null || weapons.weaponMountPoint == null || weapons.CurrentWeapon == null)
        {
            handRear.enabled = handFront.enabled = false;
            PoseArm(rearUpper, rearFore, Vector2.zero, Vector3.zero, false, 0f);
            PoseArm(frontUpper, frontFore, Vector2.zero, Vector3.zero, false, 0f);
            return;
        }
        Transform mount = weapons.weaponMountPoint;
        handRear.enabled = !fps;
        handRear.transform.position = mount.position;

        SpriteRenderer w = MainWeaponRenderer();
        bool two = !OneHanded() && w != null && w.sprite != null;
        handFront.enabled = two && !fps;
        if (two)
        {
            Transform visual = w.transform.parent != null ? w.transform.parent : w.transform;
            float len = w.sprite.bounds.size.x * Mathf.Abs(w.transform.lossyScale.x);
            handFront.transform.position = mount.position + visual.right * len * frontGripRatio;
        }
        handRear.flipX = handFront.flipX = aimLeft;

        float bias = gunBehind ? upperDepthBias - 0.015f : weaponDepthBias + handDepthBias;
        SetFeet(handRear, lastFeet, lastFeetValid ? 1f + bias : 0f);
        SetFeet(handFront, lastFeet, lastFeetValid ? 1f + bias : 0f);

        // 팔: 어깨에서 손까지 (무기 뒤, 상체 앞)
        float side = aimLeft ? -1f : 1f;
        float armBias = gunBehind ? upperDepthBias - 0.02f : armDepthBias;
        PoseArm(rearUpper, rearFore, new Vector2(side * rearShoulderX, shoulderY), handRear.transform.position, !fps, armBias);
        PoseArm(frontUpper, frontFore, new Vector2(side * frontShoulderX, shoulderY), handFront.transform.position, handFront.enabled, armBias - 0.004f);
    }

    void LateUpdate()
    {
        if (lowerRenderer == null || upperRenderer == null) return;
        if (cam == null) cam = Camera.main;
        bool fps = pc != null && pc.isFirstPerson;
        if (Input.GetKeyDown(armRigToggleKey) && PlayerWeaponManager.TestModeAllWeapons && !AugmentManager.IsOpen) SetArmRig(!armRigOn);

        // 1인칭에서는 내 몸이 안 보이므로(그림자만) 하체 설정을 상체에도 그대로
        upperRenderer.shadowCastingMode = lowerRenderer.shadowCastingMode;

        bool moving = pc != null && pc.IsMoving;
        if (moving || Input.GetMouseButton(0)) lastActiveTime = Time.time;

        // ---- 조준 방향 (화면 기준) ----
        Vector2 aim = Vector2.right;
        if (cam != null)
        {
            Vector3 sp = cam.WorldToScreenPoint(transform.position);
            aim = (Vector2)Input.mousePosition - new Vector2(sp.x, sp.y);
        }

        // ---- 상체 ----
        Dir dir;
        if (fps) dir = Dir.Right; // 1인칭: 몸은 그림자로만 보이므로 무기를 든 옆모습으로 고정
        else if (!moving && Time.time - lastActiveTime > idleDelay) dir = Dir.Idle;
        else dir = DirFromVector(aim);
        CurrentDir = dir;
        Sprite us = Get(upperSprites, (int)dir);
        if (us != null && upperRenderer.sprite != us) upperRenderer.sprite = us;

        // ---- 하체 ----
        float h = Input.GetAxisRaw("Horizontal");
        if (moving && Mathf.Abs(h) > 0.01f) facingLeft = h < 0f;          // 좌우로 움직이면 그쪽을 봄
        else if (!moving && Mathf.Abs(aim.x) > 4f) facingLeft = aim.x < 0f; // 서 있으면 조준 쪽을 봄
        if (fps) facingLeft = false; // 1인칭: 상체(오른쪽 고정)와 그림자 방향을 맞춤
        if (walkFrames != null && walkFrames.Length > 0)
        {
            Sprite ls;
            if (moving)
            {
                walkPhase = (walkPhase + walkFps * Time.deltaTime) % walkFrames.Length;
                ls = walkFrames[Mathf.Clamp((int)walkPhase, 0, walkFrames.Length - 1)];
            }
            else
            {
                walkPhase = 0f;
                ls = walkFrames[Mathf.Clamp(standFrame, 0, walkFrames.Length - 1)];
            }
            if (ls != null && lowerRenderer.sprite != ls) lowerRenderer.sprite = ls;
        }
        lowerRenderer.flipX = legsFaceRight ? facingLeft : !facingLeft;

        // ---- 무기 손잡이 → 손 ----
        RefreshWeaponRenderers();
        if (!fps && weapons != null && weapons.weaponMountPoint != null)
        {
            if (armRigOn)
                weapons.weaponMountPoint.position = GripPoint(aim, dir == Dir.Idle, fps);
            else
            {
                Vector2 hand = Get(handOffsets, (int)dir);
                weapons.weaponMountPoint.position = upperRenderer.transform.TransformPoint(hand);
            }
            CacheWeaponPose();
        }
        UpdateWeaponShadow(fps, dir);

        // ---- 겹침 깊이 (발 위치 + 앞당김) ----
        bool valid = !fps && board != null && board.HasFeet;
        Vector3 feet = board != null ? board.FeetWorld : transform.position;
        SetFeet(upperRenderer, feet, valid ? 1f + upperDepthBias : 0f);
        lastFeet = feet; lastFeetValid = valid;
        if (fps) gunBehind = false;
        // 새 몸 그림: 위를 겨누면 무기가 몸 뒤로
        float wb = armRigOn && gunBehind ? upperDepthBias - 0.025f : weaponDepthBias;
        foreach (SpriteRenderer r in weaponRenderers)
            if (r != null)
            {
                // 탄창(Magazine)은 무기 그림 바로 뒤에 (같은 깊이면 깜빡거림)
                float bias = r.name == "Magazine" ? wb - 0.01f                                   // 무기 뒤
                           : (r.name == "MagazineFront" || r.name == "Cover") ? wb + 0.01f   // 무기 앞 (기관총 탄통·덮개)
                           : wb;
                SetFeet(r, feet, valid ? 1f + bias : 0f);
            }

        // ---- 이음새: 상체 아래 / 하체 위 가장자리 외곽선 생략 ----
        // 상체: 허리 아래로 늘어진 털(최대 11텍셀, v86에서 그림을 아래로 늘림)까지 외곽선 생략, 단 하체가 실제로 있는 허리 폭 안에서만 (옆의 손은 외곽선 유지)
        SetVector(upperRenderer, OutlineSkipId, new Vector4(upperJointV, Mathf.Max(upperJoinTexels, 12f), 0f, 0f));
        SetVector(upperRenderer, OutlineSkipSpanId, WaistSpan());
        SetVector(lowerRenderer, OutlineSkipId, new Vector4(0f, 0f, lowerJointV, lowerJoinTexels));
    }

    void OnDisable()
    {
        if (shadowProxy != null) shadowProxy.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (shadowProxy != null) Destroy(shadowProxy.gameObject);
    }

    // 탑뷰일 때 무기 그림이 손에서 얼마나 떨어져 있고 얼마나 큰지 기억 (1인칭 그림자에 사용)
    private void CacheWeaponPose()
    {
        SpriteRenderer w = MainWeaponRenderer();
        if (w == null) return;
        Transform mount = weapons.weaponMountPoint;
        Transform visual = w.transform.parent != null ? w.transform.parent : w.transform;
        // 손(장착점) → 무기 그림 중심을 '무기가 겨누는 방향' 기준으로 (조준 각도·좌우 반전과 무관하게)
        Vector3 d = w.transform.position - mount.position;
        float flip = visual.lossyScale.y < 0f ? -1f : 1f;
        weaponSpriteOffset = new Vector3(Vector3.Dot(d, visual.right), Vector3.Dot(d, visual.up) * flip, 0f);
        Vector3 ls = w.transform.lossyScale;
        weaponSpriteScale = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), 1f);
    }

    // 1인칭: 손 위치에 그림자만 드리우는 무기 그림을 둠 / 탑뷰: 숨김
    private void UpdateWeaponShadow(bool fps, Dir dir)
    {
        bool want = fps && fpsWeaponShadow;
        SpriteRenderer w = MainWeaponRenderer();

        // 실제 무기 그림: 1인칭에선 카메라 앞이라 그림자를 끔 (엉뚱한 곳에 그림자가 생기지 않게)
        if (fps != lastFps || want)
        {
            var mode = fps ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (SpriteRenderer r in weaponRenderers)
                if (r != null && r.shadowCastingMode != mode) r.shadowCastingMode = mode;
            lastFps = fps;
        }

        if (!want || w == null || w.sprite == null)
        {
            if (shadowProxy != null && shadowProxy.gameObject.activeSelf) shadowProxy.gameObject.SetActive(false);
            return;
        }

        if (shadowProxy == null)
        {
            var go = new GameObject($"{name}_WeaponShadow");
            go.layer = upperRenderer.gameObject.layer;
            shadowProxy = go.AddComponent<SpriteRenderer>();
            shadowProxy.receiveShadows = false;
        }
        if (!shadowProxy.gameObject.activeSelf) shadowProxy.gameObject.SetActive(true);
        shadowProxy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        if (upperRenderer.sharedMaterial != null && shadowProxy.sharedMaterial != upperRenderer.sharedMaterial)
            shadowProxy.sharedMaterial = upperRenderer.sharedMaterial;
        // 그림자는 탑뷰(옆모습) 그림으로 (1인칭 전용 그림은 원근이 들어가 있어 그림자로 부적합)
        WeaponVisual wv = w.GetComponentInParent<WeaponVisual>();
        Sprite shadowSprite = (wv != null && wv.TopDownSprite != null) ? wv.TopDownSprite : w.sprite;
        if (shadowProxy.sprite != shadowSprite) shadowProxy.sprite = shadowSprite;

        // 상체(빌보드) 평면 위, 손 위치에서 오른쪽을 겨눈 모습
        Transform ut = upperRenderer.transform;
        Vector3 hand = ut.TransformPoint(Get(handOffsets, (int)dir));
        Transform pt = shadowProxy.transform;
        pt.rotation = ut.rotation;
        pt.position = hand + ut.right * weaponSpriteOffset.x + ut.up * weaponSpriteOffset.y;
        Vector3 sc = weaponSpriteScale.sqrMagnitude > 0f ? weaponSpriteScale : w.transform.lossyScale;
        pt.localScale = new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), 1f);
    }

    // 무기 그림 중 가장 큰 것(본체)
    private SpriteRenderer MainWeaponRenderer()
    {
        SpriteRenderer best = null;
        float bestArea = -1f;
        foreach (SpriteRenderer r in weaponRenderers)
        {
            if (r == null || r.sprite == null || !r.gameObject.activeInHierarchy) continue;
            Vector2 sz = r.sprite.bounds.size;
            float area = sz.x * sz.y;
            if (area > bestArea) { bestArea = area; best = r; }
        }
        return best;
    }

    // 하체 그림 맨 윗부분(허리)의 가로 범위 → 상체 그림의 u 좌표 (상체 외곽선 생략 범위)
    private Vector4 WaistSpan()
    {
        Sprite ls = lowerRenderer.sprite, us = upperRenderer.sprite;
        if (ls == null || us == null) return Vector4.zero;
        if (!waistCache.TryGetValue(ls, out Vector2 span))
        {
            span = new Vector2(float.MaxValue, float.MinValue);
            Vector2[] v = ls.vertices;
            float top = ls.bounds.max.y, band = ls.bounds.size.y * 0.15f;
            foreach (Vector2 q in v)
                if (q.y >= top - band) { span.x = Mathf.Min(span.x, q.x); span.y = Mathf.Max(span.y, q.x); }
            if (span.x > span.y) span = new Vector2(ls.bounds.min.x, ls.bounds.max.x);
            waistCache[ls] = span;
        }
        float a = span.x, b = span.y;
        if (lowerRenderer.flipX) { float t = -b; b = -a; a = t; }
        float w = us.rect.width / Mathf.Max(1f, us.pixelsPerUnit);
        float pu = us.pivot.x / Mathf.Max(1f, us.rect.width);
        float ox = upperRenderer.transform.localPosition.x;
        return new Vector4(pu + (a - ox) / w, pu + (b - ox) / w, 0f, 0f);
    }

    // 화면 기준 각도 → 8방향
    private static Dir DirFromVector(Vector2 v)
    {
        if (v.sqrMagnitude < 1f) return Dir.Down;
        float a = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg; // 0 = 오른쪽, 90 = 위
        int s = Mathf.RoundToInt(a / 45f);
        s = ((s % 8) + 8) % 8;
        switch (s)
        {
            case 0: return Dir.Right;
            case 1: return Dir.UpRight;
            case 2: return Dir.Up;
            case 3: return Dir.UpLeft;
            case 4: return Dir.Left;
            case 5: return Dir.DownLeft;
            case 6: return Dir.Down;
            default: return Dir.DownRight;
        }
    }

    // 무기를 바꾸면 무기 그림도 같은 셰이더(그림자·외곽선·깊이)로
    private void RefreshWeaponRenderers()
    {
        if (weapons == null || weapons.weaponMountPoint == null) return;
        Transform mount = weapons.weaponMountPoint;
        int sig = mount.childCount;
        for (int i = 0; i < mount.childCount; i++) sig = sig * 31 + mount.GetChild(i).GetInstanceID();
        if (sig == weaponChildSignature) return;
        weaponChildSignature = sig;

        weaponRenderers.Clear();
        foreach (SpriteRenderer r in mount.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (r == lowerRenderer || r == upperRenderer) continue;
            if (upperRenderer.sharedMaterial != null) r.sharedMaterial = upperRenderer.sharedMaterial;
            r.shadowCastingMode = (pc != null && pc.isFirstPerson)
                ? UnityEngine.Rendering.ShadowCastingMode.Off
                : UnityEngine.Rendering.ShadowCastingMode.On;
            weaponRenderers.Add(r);
        }
    }

    private void SetFeet(SpriteRenderer r, Vector3 feet, float w)
    {
        SetVector(r, FeetPosId, new Vector4(feet.x, feet.y, feet.z, w));
    }

    // 다른 값(피격 번쩍임 등)은 그대로 두고 한 값만 바꿈
    private void SetVector(SpriteRenderer r, int id, Vector4 v)
    {
        if (block == null) block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetVector(id, v);
        r.SetPropertyBlock(block);
    }

    private static T Get<T>(T[] arr, int i) => (arr != null && i >= 0 && i < arr.Length) ? arr[i] : default;
}
