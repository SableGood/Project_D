using UnityEngine;

// 무기 이미지 표시 담당.
//  ■ 탑뷰: 무기 이미지가 항상 카메라를 향하면서(빌보드) 화면 위에서 조준점 방향으로 회전합니다.
//          왼쪽을 볼 때는 상하 반전해 무기가 뒤집혀 보이지 않게 합니다.
//  ■ 1인칭: 무기를 카메라 오른쪽 아래에 고정해 "손에 들고 있는" 것처럼 보여줍니다. (FPS 뷰모델)
//
// 구조:  Visual (이 스크립트) ─┬─ Sprite (SpriteRenderer)
//                              └─ Muzzle (총구)  ← 함께 움직여 항상 총구 끝에 위치
//
// ※ 카메라가 이동한 뒤에 위치를 잡아야 흔들림이 없으므로 CameraController(기본 0)보다 늦게 실행합니다.
[DefaultExecutionOrder(100)]
public class WeaponVisual : MonoBehaviour
{
    [Header("탑뷰")]
    [Tooltip("조준 방향이 화면 왼쪽일 때 무기 이미지를 상하 반전합니다.")]
    public bool flipWhenAimingLeft = true;

    // 1인칭 배치값은 PlayerWeaponManager(플레이어)에서 모든 무기에 공통으로 넘겨줍니다.
    private Vector3 fpsOffset = new Vector3(0.3f, -0.25f, 0.6f);
    private Vector3 fpsRotation = new Vector3(0f, -125f, 0f);
    private float fpsScale = 0.45f;

    private Camera cam;
    private Vector3 aimPoint;
    private bool hasAimPoint = false;
    private bool isFirstPerson = false;

    private Vector3 baseLocalPosition;
    private Vector3 baseLocalScale;

    void Awake()
    {
        baseLocalPosition = transform.localPosition;
        baseLocalScale = transform.localScale;
    }

    // PlayerWeaponManager가 매 프레임 조준점을 넘겨줍니다.
    public void SetAimPoint(Vector3 worldPoint)
    {
        aimPoint = worldPoint;
        hasAimPoint = true;
    }

    public void SetFirstPersonPose(Vector3 offset, Vector3 rotation, float scale)
    {
        fpsOffset = offset;
        fpsRotation = rotation;
        fpsScale = scale;
    }

    public void SetFirstPerson(bool firstPerson)
    {
        if (isFirstPerson == firstPerson) return;
        isFirstPerson = firstPerson;

        if (!isFirstPerson)
        {
            // 탑뷰로 돌아오면 원래 자리(플레이어 손 위치)로 복귀
            transform.localPosition = baseLocalPosition;
            transform.localScale = baseLocalScale;
        }
    }

    // 사격 직전에 호출하면 이번 프레임 기준으로 즉시 갱신 (총구 위치 정확도↑)
    public void Refresh()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        if (isFirstPerson) RefreshFirstPerson();
        else RefreshTopDown();
    }

    private void RefreshTopDown()
    {
        Vector3 dir = hasAimPoint
            ? aimPoint - transform.position
            : (transform.parent != null ? transform.parent.forward : Vector3.forward);

        // 조준 방향을 화면(카메라) 평면으로 투영해 화면상 각도를 구합니다.
        Transform camT = cam.transform;
        float x = Vector3.Dot(dir, camT.right);
        float y = Vector3.Dot(dir, camT.up);

        float angle = (Mathf.Abs(x) + Mathf.Abs(y) < 0.0001f) ? 0f : Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        transform.rotation = camT.rotation * Quaternion.Euler(0f, 0f, angle);

        Vector3 scale = transform.localScale;
        float sy = Mathf.Abs(scale.y);
        scale.y = (flipWhenAimingLeft && x < 0f) ? -sy : sy;
        transform.localScale = scale;
    }

    private void RefreshFirstPerson()
    {
        Transform camT = cam.transform;
        transform.position = camT.TransformPoint(fpsOffset);
        transform.rotation = camT.rotation * Quaternion.Euler(fpsRotation);

        // 부모(플레이어) 크기와 상관없이 일정한 크기가 되도록 월드 기준으로 맞춤
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(
            Mathf.Abs(baseLocalScale.x) * fpsScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            Mathf.Abs(baseLocalScale.y) * fpsScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            Mathf.Abs(baseLocalScale.z) / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
    }

    void LateUpdate()
    {
        Refresh();
    }
}
