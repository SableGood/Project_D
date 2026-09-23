using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("이동 및 회전 속도")]
    public float moveSpeed = 5.0f;
    public float mouseSensitivity = 2.0f;

    [Header("현재 시점 상태")]
    public bool isTPS = false;
    public float cameraPitch = 15f;

    private CharacterController controller;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (controller == null)
        {
            controller = gameObject.AddComponent<CharacterController>();
        }
        UpdateCursorState();
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

        controller.Move(moveDirection * moveSpeed * Time.deltaTime);
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