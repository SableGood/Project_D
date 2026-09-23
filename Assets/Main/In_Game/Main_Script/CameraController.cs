using UnityEngine;

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

    [Header("TPS 세팅 (수정본 적용)")]
    public Vector3 tpsOffset = new Vector3(0.5f, 1.5f, -3f); // Y축 1.5f 반영
    public float tpsFOV = 90f; // FOV 90 반영

    private Camera cam;
    private float viewBlend = 0f;

    void Start()
    {
        cam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        // ★ 수정된 방어 코드: player 변수(PlayerController)가 비어있다면 스스로 찾아 연결합니다.
        if (player == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                // 핵심: 오브젝트의 transform이 아니라 PlayerController 컴포넌트를 가져와야 합니다.
                player = playerObj.GetComponent<PlayerController>();
                Debug.Log("CameraController: 플레이어(PlayerController)를 자동으로 찾았습니다.");
            }
            return; // 대상을 찾는 동안은 아래 이동 로직을 건너뜁니다.
        }

        // --- 여기서부터 정상적인 카메라 추적 및 시점 전환 로직 ---

        float targetBlend = player.isTPS ? 1f : 0f;
        viewBlend = Mathf.Lerp(viewBlend, targetBlend, Time.deltaTime * transitionSpeed);

        // 탑뷰 위치/회전 계산
        Vector3 tdPos = player.transform.position + topDownOffset;
        Quaternion tdRot = Quaternion.Euler(topDownRotation);

        // TPS 위치/회전 계산
        Quaternion tpsRot = Quaternion.Euler(player.cameraPitch, player.transform.eulerAngles.y, 0);
        Vector3 tpsPos = player.transform.position + tpsRot * tpsOffset;

        // 카메라 이동, 회전, FOV 실시간 적용
        transform.position = Vector3.Lerp(tdPos, tpsPos, viewBlend);
        transform.rotation = Quaternion.Slerp(tdRot, tpsRot, viewBlend);
        cam.fieldOfView = Mathf.Lerp(topDownFOV, tpsFOV, viewBlend);
    }
}