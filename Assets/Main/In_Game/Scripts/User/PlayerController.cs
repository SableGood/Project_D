using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("이동 및 회전 속도")]
    public float moveSpeed = 5.0f;
    public float mouseSensitivity = 2.0f;

    [Header("현재 시점 상태")]
    public bool isTPS = false;
    public float cameraPitch = 15f;

    [Header("플레이어 데이터 설정")]
    public EntityData myData; // 인스펙터에서 연결할 플레이어 전용 ScriptableObject

    private CharacterController controller;

    // ★ [추가] 스폰 시점의 정상적인 바닥 높이를 기억할 변수
    private float defaultYPos;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (controller == null)
        {
            controller = gameObject.AddComponent<CharacterController>();
        }
        UpdateCursorState();

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
            isTPS = !isTPS;
            UpdateCursorState();
        }

        if (isTPS)
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            transform.Rotate(Vector3.up * mouseX);

            cameraPitch -= mouseY;
            cameraPitch = Mathf.Clamp(cameraPitch, -45f, 60f);
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
        Vector3 moveDirection;

        if (isTPS)
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

    private void UpdateCursorState()
    {
        // UI 조작은 PlayerHUD로 이관하고, 여기서는 마우스 커서 상태만 제어합니다.
        if (isTPS)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}