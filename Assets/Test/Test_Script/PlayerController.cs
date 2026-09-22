using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("이동 및 회전 속도")]
    public float moveSpeed = 5.0f;
    public float mouseSensitivity = 2.0f;

    [Header("UI 연결")]
    public GameObject crosshairUI; // 에디터에서 방금 만든 Crosshair를 넣어줄 곳입니다.

    [Header("현재 시점 상태 (인스펙터 확인용)")]
    public bool isTPS = false;

    void Start()
    {
        // 게임이 딱 켜졌을 때 커서와 크로스헤어 상태를 초기화합니다.
        UpdateCursorState();
    }

    void Update()
    {
        // 1. 시점 전환 입력
        if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.LeftShift))
        {
            isTPS = !isTPS;
            UpdateCursorState(); // 시점이 바뀔 때마다 상태 업데이트 함수 호출
        }

        // 2. 캐릭터 회전
        if (isTPS)
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            transform.Rotate(Vector3.up * mouseX);
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

        // 3. 캐릭터 이동
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector3 moveDirection = (transform.forward * v + transform.right * h).normalized;
        transform.position += moveDirection * moveSpeed * Time.deltaTime;
    }

    // [핵심 로직] 시점에 따라 마우스 커서와 크로스헤어 UI를 제어하는 함수
    private void UpdateCursorState()
    {
        if (isTPS)
        {
            // TPS 뷰: 진짜 마우스 커서를 화면 중앙에 가두고 투명하게 만듦 + 크로스헤어 UI 켜기
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (crosshairUI != null) crosshairUI.SetActive(true);
        }
        else
        {
            // 탑뷰: 마우스 커서를 화면에 다시 풀어주고 보이게 만듦 + 크로스헤어 UI 끄기
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (crosshairUI != null) crosshairUI.SetActive(false);
        }
    }
}