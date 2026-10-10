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

    [Header("플레이어 데이터 설정")]
    public EntityData myData; // 인스펙터에서 연결할 플레이어 전용 ScriptableObject

    private CharacterController controller;

    // ★ [추가] 스폰 시점의 정상적인 바닥 높이를 기억할 변수
    private float defaultYPos;

    // ★ 이동 입력 중인지 (무기 이동 탄퍼짐 등에서 사용)
    public bool IsMoving { get; private set; }

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
        CacheBodySprites();
        ApplyViewMode();

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
    }

    void Update()
    {
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

        // ★ [핵심 방어 코드 1] 이동 벡터에 속도를 곱한 뒤, Y축(위아래) 물리 상태를 강제 제어합니다.
        moveDirection *= moveSpeed;

        if (controller.isGrounded)
        {
            // 땅에 닿아있을 때는 몹의 머리를 타고 올라가지 못하도록 미세하게 바닥으로 계속 누릅니다.
            moveDirection.y = -0.1f;
        }
        else
        {
            // 공중에 있을 때만 중력을 적용합니다.
            moveDirection.y += Physics.gravity.y * Time.deltaTime;
        }

        controller.Move(moveDirection * Time.deltaTime);

        // ★ [핵심 방어 코드 2] 몹과 비정상적으로 겹쳐 유니티 물리 엔진이 캐릭터를 하늘로 발사했을 경우의 최후 안전장치
        if (transform.position.y > defaultYPos + 1.0f) // 정상 높이보다 1 이상 솟구치면 감지
        {
            // CharacterController가 켜져 있으면 transform 위치 강제 변경이 무시되므로 잠시 끕니다.
            controller.enabled = false;

            Vector3 correctedPos = transform.position;
            correctedPos.y = defaultYPos; // 스폰되었던 정상적인 바닥 높이로 즉시 끌어내림
            transform.position = correctedPos;

            controller.enabled = true; // 보정 완료 후 다시 켬
        }
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