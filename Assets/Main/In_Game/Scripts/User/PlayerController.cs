using UnityEngine;
using UnityEngine.Serialization;

public class PlayerController : MonoBehaviour
{
    [Header("이동 및 회전 속도")]
    public float moveSpeed = 5.0f;
    public float mouseSensitivity = 2.0f;

    [Header("현재 시점 상태 (Tab: 탑뷰 ↔ 1인칭)")]
    [FormerlySerializedAs("isTPS")]
    public bool isFirstPerson = false;
    public float cameraPitch = 0f;
    [Tooltip("1인칭에서 위/아래로 볼 수 있는 최대 각도")]
    public float maxPitch = 80f;

    [Header("충돌 크기")]
    [Tooltip("켜면 시작할 때 충돌 반경을 그림의 발 폭에 맞춤 (기존 0.8은 그림보다 훨씬 넓어 포탑에 닿기 전에 막혔음)")]
    public bool fitColliderToSprite = true;
    [Tooltip("몸 반경(미터). 캐릭터 크기(스케일)와 상관없이 이 값이 실제 반경")]
    public float bodyRadius = 0.35f;

    [Header("플레이어 데이터 설정")]
    public EntityData myData; // 인스펙터에서 연결할 플레이어 전용 ScriptableObject

    private CharacterController controller;

    // ★ [추가] 스폰 시점의 정상적인 바닥 높이를 기억할 변수
    private float defaultYPos;
    private float defaultFeetY; // 그때의 발 높이 (바닥)

    // ★ 이동 입력 중인지 (무기 이동 탄퍼짐 등에서 사용)
    public bool IsMoving { get; private set; }

    // 이동기 (Space: 점프 / 구르기) — 없으면 Start에서 자동으로 붙임
    private PlayerMovementAbility ability;
    public PlayerMovementAbility Ability => ability;
    private float verticalVelocity;
    [Tooltip("점프가 아닐 때 떨어지는 중력 (포탑 위에서 내려올 때 등)")]
    public float fallGravity = 22f;
    // 포탑·몹 위에 올라섰을 때 옆으로 미끄러져 내려오기
    private Vector3 slideOffDir;
    private float slideOffUntil;
    private const float SlideOffSpeed = 4f;

    // 탑뷰 카메라가 따라갈 바닥 높이 (점프해도 카메라는 그대로 → 캐릭터가 뜨는 게 보임)
    private bool groundRecorded;
    public float GroundY => groundRecorded ? defaultYPos : transform.position.y;
    // 바닥(발) 높이: 점프 중에도 실제 땅 높이 (탄피·탄창이 떨어질 바닥 등)
    public float FloorY => groundRecorded ? defaultFeetY : (controller != null ? controller.bounds.min.y : transform.position.y);
    // 1인칭 카메라 높이 보정 (구르기)
    public float EyeOffset => ability != null ? ability.EyeOffset : 0f;

    // 1인칭일 때 숨길 내 캐릭터 일러스트 (그림자는 남김)
    private SpriteRenderer[] bodySprites;
    private UnityEngine.Rendering.ShadowCastingMode[] bodyShadowModes;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (controller == null)
        {
            controller = gameObject.AddComponent<CharacterController>();
        }
        if (fitColliderToSprite)
        {
            float s = Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.x));
            controller.radius = bodyRadius / s;
        }
        CacheBodySprites();
        ApplyViewMode();
        ability = GetComponent<PlayerMovementAbility>();
        if (ability == null) ability = gameObject.AddComponent<PlayerMovementAbility>();

        // 게임 시작 시, GameManager에 자신의 Transform(위치)을 등록합니다.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RegisterPlayer(transform);
        }

        // 플레이어 스폰 시 EntityData를 기반으로 체력 스탯을 초기화합니다.
        Health health = GetComponent<Health>();
        if (health != null && myData != null)
        {
            health.InitStats(myData);
            Debug.Log($"플레이어 스탯 초기화 완료! 최대 체력: {health.maxHealth}");
        }
        else if (myData == null)
        {
            Debug.LogWarning("PlayerController: myData(EntityData)가 할당되지 않아 체력을 초기화할 수 없습니다.");
        }

        // ★ [핵심 추가] GameManager가 동적으로 계산해준 완벽한 스폰 높이를 기록해둡니다.
        defaultYPos = transform.position.y;
        defaultFeetY = controller.bounds.min.y;
        groundRecorded = true;
    }

    void Update()
    {
        if (AugmentManager.IsOpen) return; // 재능 선택 중에는 시점 전환·마우스 회전·이동 안 함

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            isFirstPerson = !isFirstPerson;
            ApplyViewMode();
        }

        if (isFirstPerson)
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            transform.Rotate(Vector3.up * mouseX);

            cameraPitch -= mouseY;
            cameraPitch = Mathf.Clamp(cameraPitch, -maxPitch, maxPitch);
        }
        else
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float rayDistance))
            {
                Vector3 point = ray.GetPoint(rayDistance);
                Vector3 lookAtPoint = new Vector3(point.x, transform.position.y, point.z);
                transform.LookAt(lookAtPoint);
            }
        }

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        IsMoving = Mathf.Abs(h) > 0.01f || Mathf.Abs(v) > 0.01f;
        Vector3 moveDirection;

        if (isFirstPerson)
        {
            moveDirection = (transform.forward * v + transform.right * h).normalized;
        }
        else
        {
            moveDirection = new Vector3(h, 0, v).normalized;
        }

        // ★ [핵심 방어 코드 1] 이동 벡터에 속도를 곱한 뒤, Y축(위아래)은 따로 계산합니다.
        float baseSpeed = moveSpeed * PlayerAugments.MoveSpeedMul; // 재능 이동속도 반영 (뿌리 박기 = 0)
        Vector3 horizontal = moveDirection * baseSpeed;
        bool grounded = controller.isGrounded;

        // 이동기 (Space). 이동할 수 없는 상태(뿌리 박기)에서는 사용 불가
        if (ability != null)
        {
            if (Input.GetKeyDown(KeyCode.Space) && baseSpeed > 0f) ability.TryStart(moveDirection, grounded);
            horizontal = ability.ModifyHorizontal(horizontal, baseSpeed);
            float jumpV = ability.ConsumeJumpVelocity();
            if (jumpV > 0f) { verticalVelocity = jumpV; grounded = false; }
        }

        if (grounded && verticalVelocity <= 0f)
        {
            // 땅에 닿아있을 때는 몹의 머리를 타고 올라가지 못하도록 바닥으로 계속 누릅니다.
            verticalVelocity = -1f;
        }
        else
        {
            // 공중: 중력이 쌓임 (점프 중엔 점프 중력)
            float g = ability != null && ability.IsJumping ? ability.Gravity : fallGravity;
            verticalVelocity -= g * Time.deltaTime;
        }

        // 포탑·몹 위에 올라섰으면 옆으로 밀어서 내려오게
        if (Time.time < slideOffUntil) horizontal += slideOffDir * SlideOffSpeed;

        controller.Move((horizontal + Vector3.up * verticalVelocity) * Time.deltaTime);
        if (controller.isGrounded && ability != null) ability.OnGrounded(controller.bounds.min.y);
        if ((controller.collisionFlags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f; // 머리를 부딪히면 상승 멈춤

        // ★ [핵심 방어 코드 2] 몹과 비정상적으로 겹쳐 유니티 물리 엔진이 캐릭터를 하늘로 발사했을 경우의 최후 안전장치
        //    점프 중에는 점프 높이까지는 정상
        float allowedRise = ability != null && ability.IsJumping ? ability.CurrentJumpHeight + 1.5f : 1.0f;
        if (transform.position.y > defaultYPos + allowedRise) // 정상 높이보다 높이 솟구치면 감지
        {
            verticalVelocity = 0f;
            // CharacterController가 켜져 있으면 transform 위치 강제 변경이 무시되므로 잠시 끕니다.
            controller.enabled = false;

            Vector3 correctedPos = transform.position;
            correctedPos.y = defaultYPos; // 스폰되었던 정상적인 바닥 높이로 즉시 끌어내림
            transform.position = correctedPos;

            controller.enabled = true; // 보정 완료 후 다시 켬
        }
    }

    // 클릭한 UI 버튼이 '선택됨'으로 남아 있으면 Space(=확인 키)가 그 버튼을 다시 누름 → 매 프레임 선택 해제
    void LateUpdate()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);
    }

    // 무엇인가의 위(포탑 꼭대기, 몹 머리)에 올라서면 그 물체 바깥쪽으로 미끄러져 내려옴
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.normal.y < 0.6f) return;                 // 옆면·벽
        if (hit.point.y < defaultFeetY + 0.3f) return;   // 원래 바닥
        Vector3 away = transform.position - hit.collider.bounds.center;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = transform.forward;
        slideOffDir = away.normalized;
        slideOffUntil = Time.time + 0.1f;
    }

    // 시점 전환 시: 커서 상태 + 내 일러스트 표시 여부
    private void ApplyViewMode()
    {
        if (isFirstPerson)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // 1인칭에서는 내 일러스트가 카메라를 가리므로 숨기고, 바닥 그림자만 남깁니다.
        if (bodySprites == null) return;
        for (int i = 0; i < bodySprites.Length; i++)
        {
            if (bodySprites[i] == null) continue;
            bodySprites[i].shadowCastingMode = isFirstPerson
                ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                : bodyShadowModes[i];
        }
    }

    // 캐릭터 일러스트 = Billboard가 붙은 SpriteRenderer (무기 이미지는 제외됨)
    private void CacheBodySprites()
    {
        Billboard[] boards = GetComponentsInChildren<Billboard>(true);
        bodySprites = new SpriteRenderer[boards.Length];
        bodyShadowModes = new UnityEngine.Rendering.ShadowCastingMode[boards.Length];
        for (int i = 0; i < boards.Length; i++)
        {
            bodySprites[i] = boards[i].GetComponent<SpriteRenderer>();
            // Billboard.Start에서 TwoSided로 바꾸므로 기본값을 TwoSided로 기억
            bodyShadowModes[i] = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        }
    }
}