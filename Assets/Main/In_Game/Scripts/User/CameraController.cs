using UnityEngine;

// 탑뷰 ↔ 1인칭(FPS) 시점 카메라. Tab으로 전환하면 두 시점 사이를 부드럽게 이동합니다.
public class CameraController : MonoBehaviour
{
    [Header("연결할 대상")]
    public PlayerController player;

    [Header("시점 전환 속도")]
    public float transitionSpeed = 8.0f;

    [Header("탑뷰 세팅")]
    public Vector3 topDownOffset = new Vector3(0, 10, -7);
    public Vector3 topDownRotation = new Vector3(55, 0, 0);
    public float topDownFOV = 60f;

    [Header("1인칭(FPS) 세팅")]
    [Tooltip("플레이어 중심에서 눈 높이까지 (위쪽)")]
    public float fpsEyeHeight = 0.5f;
    [Tooltip("눈 위치를 앞쪽으로 살짝 빼는 거리 (내 몸이 화면에 걸리지 않게)")]
    public float fpsForwardOffset = 0.1f;
    public float fpsFOV = 75f;
    [Tooltip("1인칭 근거리 클리핑 (무기가 카메라 앞에서 잘리지 않도록 작게)")]
    public float fpsNearClip = 0.05f;

    private Camera cam;
    private float viewBlend = 0f;
    private float topDownNearClip;

    void Start()
    {
        cam = GetComponent<Camera>();
        topDownNearClip = cam.nearClipPlane;
    }

    void LateUpdate()
    {
        // player 변수(PlayerController)가 비어있다면 스스로 찾아 연결합니다.
        if (player == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.GetComponent<PlayerController>();
                Debug.Log("CameraController: 플레이어(PlayerController)를 자동으로 찾았습니다.");
            }
            return; // 대상을 찾는 동안은 아래 이동 로직을 건너뜁니다.
        }

        float targetBlend = player.isFirstPerson ? 1f : 0f;
        viewBlend = Mathf.Lerp(viewBlend, targetBlend, Time.deltaTime * transitionSpeed);
        if (Mathf.Abs(viewBlend - targetBlend) < 0.001f) viewBlend = targetBlend;

        Transform p = player.transform;

        // 탑뷰 위치/회전
        Vector3 tdPos = p.position + topDownOffset;
        Quaternion tdRot = Quaternion.Euler(topDownRotation);

        // 1인칭 위치/회전 (눈 높이, 플레이어가 바라보는 방향 + 상하 시선)
        Quaternion fpsRot = Quaternion.Euler(player.cameraPitch, p.eulerAngles.y, 0);
        Vector3 fpsPos = p.position + Vector3.up * fpsEyeHeight + p.forward * fpsForwardOffset;

        // 카메라 이동, 회전, FOV, 근거리 클리핑 적용
        transform.position = Vector3.Lerp(tdPos, fpsPos, viewBlend);
        transform.rotation = Quaternion.Slerp(tdRot, fpsRot, viewBlend);
        cam.fieldOfView = Mathf.Lerp(topDownFOV, fpsFOV, viewBlend);
        cam.nearClipPlane = Mathf.Lerp(topDownNearClip, fpsNearClip, viewBlend);
    }
}
