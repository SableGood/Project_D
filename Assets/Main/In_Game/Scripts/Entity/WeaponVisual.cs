using UnityEngine;

// 2.5D 무기 표시: 무기 이미지가 항상 카메라를 향하면서(빌보드),
// 화면 위에서 조준점 방향으로 회전합니다. 왼쪽을 볼 때는 상하 반전해 무기가 뒤집혀 보이지 않게 합니다.
//
// 구조:  Visual (이 스크립트) ─┬─ Sprite (SpriteRenderer)
//                              └─ Muzzle (총구)  ← 함께 회전/반전되어 항상 총구 끝에 위치
public class WeaponVisual : MonoBehaviour
{
    [Tooltip("조준 방향이 화면 왼쪽일 때 무기 이미지를 상하 반전합니다.")]
    public bool flipWhenAimingLeft = true;

    private Camera cam;
    private Vector3 aimPoint;
    private bool hasAimPoint = false;

    // PlayerWeaponManager가 매 프레임 조준점을 넘겨줍니다.
    public void SetAimPoint(Vector3 worldPoint)
    {
        aimPoint = worldPoint;
        hasAimPoint = true;
    }

    // 사격 직전에 호출하면 이번 프레임의 조준 방향으로 즉시 갱신 (총구 위치 정확도↑)
    public void Refresh()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

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

    void LateUpdate()
    {
        Refresh();
    }
}
