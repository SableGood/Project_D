using UnityEngine;
using UnityEngine.AI;

// 캐릭터 고유 이동기 (Space)
//  - 침팬지·원숭이: 점프. 높이는 지금 설치된 포탑 중 가장 높은 것을 넘을 만큼 (포탑이 없으면 기본 높이)
//  - 오랑우탄: 구르기. 잠깐 이동속도 2배로 한 방향으로 굴러감
//      탑뷰: 몸 그림과 무기가 한 바퀴 회전 / 1인칭: 카메라가 잠깐 낮아졌다가 돌아옴
//  PlayerController가 자동으로 붙이고, 이동 계산에 사용합니다.
//  실행 순서 1000: 다른 스크립트가 그림 위치를 정한 뒤에 구르기 회전을 덧입힘
[DefaultExecutionOrder(1000)]
public class PlayerMovementAbility : MonoBehaviour
{
    public enum Kind { Auto, Jump, Roll }

    [Tooltip("Auto = 캐릭터 코드로 결정 (PC003 오랑우탄 = 구르기, 나머지 = 점프)")]
    public Kind kind = Kind.Auto;

    [Header("점프")]
    [Tooltip("점프 중 중력 (클수록 빨리 떨어짐)")]
    public float jumpGravity = 22f;
    [Tooltip("가장 높은 포탑 꼭대기보다 이만큼 더 높이 뜀 (m)")]
    public float jumpClearance = 0.6f;
    [Tooltip("설치된 포탑이 없을 때 점프 높이 (m)")]
    public float defaultJumpHeight = 2.2f;
    public float minJumpHeight = 1.2f, maxJumpHeight = 4.5f;

    [Header("구르기")]
    public float rollDuration = 0.45f;
    [Tooltip("구르는 동안 이동속도 배율")]
    public float rollSpeedMultiplier = 2f;
    [Tooltip("구르기 재사용 대기시간 (초, 구르기가 끝난 뒤부터)")]
    public float rollCooldown = 0.6f;
    [Tooltip("1인칭에서 구를 때 카메라가 내려가는 깊이 (m)")]
    public float fpsRollDip = 0.55f;
    [Tooltip("탑뷰 구르기 회전 중심: 캐릭터 그림 높이의 이 비율 (0 = 발, 1 = 머리 꼭대기)")]
    [Range(0f, 1f)] public float rollPivotHeight = 0.3f;

    public Kind ActiveKind { get; private set; }
    public bool IsJumping { get; private set; }
    public bool IsRolling { get; private set; }
    public bool IsActing => IsJumping || IsRolling;      // 이동기 사용 중 (재능 '점핑킹' 등에서 사용 예정)
    public float CurrentJumpHeight { get; private set; }
    public float EyeOffset { get; private set; }           // 1인칭 카메라 높이 보정 (구르기)
    public float RollCooldownLeft => Mathf.Max(0f, rollReadyTime - Time.time);

    private PlayerController pc;
    private CharacterController cc;
    private float pendingJumpVelocity;
    private float groundY;

    private Vector3 rollDir;
    private float rollStart, rollReadyTime, rollSign;

    // 구르기 회전 대상 (그림과 무기 장착점)
    private Transform[] spinTargets;
    private Vector3[] savedLocalPos;
    private Quaternion[] savedLocalRot;
    private bool spinApplied;

    void Awake()
    {
        pc = GetComponent<PlayerController>();
        cc = GetComponent<CharacterController>();
    }

    // 화면을 그린 뒤 회전을 되돌림 → 다른 스크립트는 항상 원래 값을 봄
    void OnEnable() { Camera.onPostRender += OnCameraPostRender; }
    private void OnCameraPostRender(Camera c) { if (c == Camera.main) RestoreSpin(); }

    void Start()
    {
        ActiveKind = kind;
        if (ActiveKind == Kind.Auto)
        {
            string code = pc != null && pc.myData != null ? pc.myData.entityCode : "";
            ActiveKind = code == "PC003" ? Kind.Roll : Kind.Jump;
        }
    }

    // ===== PlayerController에서 호출 =====
    // Space를 눌렀을 때. inputDir: 이동 입력 방향(정규화, 없으면 0), baseSpeed: 지금의 이동속도
    public void TryStart(Vector3 inputDir, bool grounded)
    {
        if (ActiveKind == Kind.Jump)
        {
            if (!grounded || IsJumping) return;
            groundY = cc != null ? cc.bounds.min.y : transform.position.y;
            CurrentJumpHeight = ComputeJumpHeight(groundY);
            pendingJumpVelocity = Mathf.Sqrt(2f * jumpGravity * CurrentJumpHeight);
            IsJumping = true;
        }
        else if (ActiveKind == Kind.Roll)
        {
            if (IsRolling || Time.time < rollReadyTime) return;
            Vector3 dir = inputDir;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) { dir = transform.forward; dir.y = 0f; } // 입력이 없으면 바라보는 쪽
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            rollDir = dir.normalized;
            rollStart = Time.time;
            IsRolling = true;

            // 탑뷰 회전 방향: 화면에서 오른쪽으로 구르면 시계 방향
            Camera cam = Camera.main;
            float screenX = cam != null ? Vector3.Dot(rollDir, cam.transform.right) : 1f;
            rollSign = screenX >= 0f ? -1f : 1f;
            CacheSpinTargets();
        }
    }

    // 이번 프레임에 줄 점프 속도 (한 번만 돌려줌)
    public float ConsumeJumpVelocity()
    {
        float v = pendingJumpVelocity;
        pendingJumpVelocity = 0f;
        return v;
    }

    // 구르는 동안에는 구르는 방향으로 빠르게
    public Vector3 ModifyHorizontal(Vector3 horizontal, float baseSpeed)
    {
        if (!IsRolling) return horizontal;
        return rollDir * baseSpeed * rollSpeedMultiplier;
    }

    // 착지 판정: 원래 바닥 높이 근처에 닿으면 점프 끝 (포탑 위에 잠깐 닿은 건 끝이 아님)
    public void OnGrounded(float feetY)
    {
        if (IsJumping && pendingJumpVelocity <= 0f && feetY <= groundY + 0.3f) IsJumping = false;
    }

    public float Gravity => jumpGravity;

    // ===== 점프 높이: 설치된 포탑 중 가장 높은 꼭대기 + 여유 =====
    private float ComputeJumpHeight(float floorY)
    {
        float top = float.NegativeInfinity;
        NavMeshObstacle[] obstacles = FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.None);
        foreach (NavMeshObstacle o in obstacles)
        {
            if (o == null || !o.isActiveAndEnabled) continue;
            Transform root = o.transform.root;
            foreach (Collider c in root.GetComponentsInChildren<Collider>())
                if (c.enabled && !c.isTrigger) top = Mathf.Max(top, c.bounds.max.y);
        }
        float h = float.IsNegativeInfinity(top) ? defaultJumpHeight : (top - floorY) + jumpClearance;
        return Mathf.Clamp(h, minJumpHeight, maxJumpHeight);
    }

    // ===== 구르기 진행 + 연출 =====
    void Update()
    {
        RestoreSpin(); // 화면을 그리지 않은 프레임 대비 (보통은 이미 되돌려져 있음)

        if (IsRolling && Time.time >= rollStart + rollDuration)
        {
            IsRolling = false;
            rollReadyTime = Time.time + rollCooldown;
            EyeOffset = 0f;
        }
        if (IsRolling)
        {
            float t = Mathf.Clamp01((Time.time - rollStart) / rollDuration);
            EyeOffset = -fpsRollDip * Mathf.Sin(t * Mathf.PI); // 1인칭: 내려갔다가 다시 올라옴
        }
    }

    void LateUpdate()
    {
        if (!IsRolling || pc == null || pc.isFirstPerson || spinTargets == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        float t = Mathf.Clamp01((Time.time - rollStart) / rollDuration);
        float angle = rollSign * 360f * Mathf.SmoothStep(0f, 1f, t);
        Vector3 pivot = SpinPivot();
        Vector3 axis = cam.transform.forward;

        for (int i = 0; i < spinTargets.Length; i++)
        {
            Transform s = spinTargets[i];
            if (s == null) continue;
            savedLocalPos[i] = s.localPosition;
            savedLocalRot[i] = s.localRotation;
            s.RotateAround(pivot, axis, angle);
        }
        spinApplied = true;
    }

    // 회전 중심: 몸 그림 전체(상·하체)의 아래에서 rollPivotHeight 높이 (그림이 없으면 캐릭터 충돌체 기준)
    private Vector3 SpinPivot()
    {
        bool any = false;
        Bounds b = default;
        foreach (Transform t in spinTargets)
        {
            if (t == null || t.GetComponent<Billboard>() == null) continue;
            SpriteRenderer sr = t.GetComponent<SpriteRenderer>();
            if (sr == null || !sr.enabled || sr.sprite == null) continue;
            if (!any) { b = sr.bounds; any = true; } else b.Encapsulate(sr.bounds);
        }
        if (!any)
        {
            if (cc == null) return transform.position + Vector3.up * 0.5f;
            b = cc.bounds;
        }
        return new Vector3(b.center.x, b.min.y + b.size.y * rollPivotHeight, b.center.z);
    }

    private void RestoreSpin()
    {
        if (!spinApplied || spinTargets == null) return;
        for (int i = 0; i < spinTargets.Length; i++)
        {
            Transform s = spinTargets[i];
            if (s == null) continue;
            s.localPosition = savedLocalPos[i];
            s.localRotation = savedLocalRot[i];
        }
        spinApplied = false;
    }

    // 몸 그림(빌보드) + 무기 장착점
    private void CacheSpinTargets()
    {
        RestoreSpin();
        var list = new System.Collections.Generic.List<Transform>();
        foreach (Billboard b in GetComponentsInChildren<Billboard>()) list.Add(b.transform);
        PlayerWeaponManager pwm = GetComponent<PlayerWeaponManager>();
        if (pwm != null && pwm.weaponMountPoint != null && pwm.weaponMountPoint != transform) list.Add(pwm.weaponMountPoint);
        spinTargets = list.ToArray();
        savedLocalPos = new Vector3[spinTargets.Length];
        savedLocalRot = new Quaternion[spinTargets.Length];
    }

    void OnDisable()
    {
        Camera.onPostRender -= OnCameraPostRender;
        RestoreSpin();
        IsRolling = false;
        IsJumping = false;
        EyeOffset = 0f;
    }
}
