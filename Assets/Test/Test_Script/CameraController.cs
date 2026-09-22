using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("연결할 대상")]
    public PlayerController player; // [주의!] 연결 타입이 Transform에서 PlayerController로 변경됨

    [Header("전환 속도")]
    public float transitionSpeed = 5.0f;

    [Header("탑뷰 세팅")]
    public Vector3 topDownOffset = new Vector3(0, 10, -7);
    public Vector3 topDownRotation = new Vector3(55, 0, 0);
    public float topDownFOV = 60f;

    [Header("TPS 세팅")]
    public Vector3 tpsOffset = new Vector3(0.5f, 2f, -3f); // 캐릭터 중심에서 우측 0.5, 위로 2, 뒤로 3만큼 떨어진 위치
    public Vector3 tpsRotation = new Vector3(15, 0, 0); // 기본적으로 아래로 살짝 15도 내려다보는 각도
    public float tpsFOV = 40f;

    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (player == null) return;

        bool isTPS = player.isTPS; // 플레이어의 현재 시점 상태를 읽어옴

        Vector3 targetPosition;
        Quaternion targetRotation;
        float targetFOV;

        if (isTPS)
        {
            // [TPS 모드] 카메라는 플레이어의 회전(등 뒤)에 맞춰 같이 돌아가야 함
            targetPosition = player.transform.position + player.transform.TransformDirection(tpsOffset);
            // 카메라 회전은 플레이어의 좌우 회전값(Y)에 카메라 자체의 상하 각도(X)를 더함
            targetRotation = Quaternion.Euler(tpsRotation.x, player.transform.eulerAngles.y, 0);
            targetFOV = tpsFOV;
        }
        else
        {
            // [탑뷰 모드] 플레이어의 회전과 무관하게 절대적인 공중 위치와 각도를 유지
            targetPosition = player.transform.position + topDownOffset;
            targetRotation = Quaternion.Euler(topDownRotation);
            targetFOV = topDownFOV;
        }

        // 카메라 이동 및 회전을 부드럽게(Lerp/Slerp) 처리
        transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * transitionSpeed);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * transitionSpeed);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, Time.deltaTime * transitionSpeed);
    }
}