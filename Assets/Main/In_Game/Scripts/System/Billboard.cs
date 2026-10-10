using UnityEngine;

public class Billboard : MonoBehaviour
{
    [Tooltip("켜면 이미지 아래 끝(발밑)을 기준으로 카메라 쪽으로 기울입니다.\n" +
             "끄면 이미지 중앙 기준으로 기울어져, 탑뷰처럼 카메라가 내려다볼 때 발이 바닥에서 떠 보입니다.\n" +
             "(몹은 MobAI가 자동으로 켭니다)")]
    public bool anchorAtFeet = false;

    private Camera mainCam;
    private SpriteRenderer sr;
    private Vector3 baseLocalPos; // 똑바로 서 있을 때의 위치 (부모 기준)

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        baseLocalPos = transform.localPosition;
    }

    void Start()
    {
        mainCam = Camera.main;

        // SpriteRenderer의 숨겨진 그림자 옵션을 코드로 강제 활성화
        if (sr != null)
        {
            // TwoSided로 설정해야 빌보드 회전 시 그림자가 얇아지거나 사라지는 현상을 막을 수 있습니다.
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            sr.receiveShadows = true;
        }
    }

    void LateUpdate()
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        // 스프라이트가 항상 카메라와 동일한 각도를 유지하도록 회전값을 고정합니다.
        // 캐릭터가 3D 공간에서 회전하더라도 일러스트는 항상 유저를 바라봅니다.
        transform.rotation = mainCam.transform.rotation;

        if (anchorAtFeet && sr != null && sr.sprite != null && transform.parent != null)
        {
            // 똑바로 섰을 때 발밑이 있을 자리(부모 기준)를 고정점으로 삼고, 그 점을 중심으로 기울임
            float minY = sr.sprite.bounds.min.y;                        // 스프라이트 기준 발밑 높이 (중앙 피벗이면 음수)
            float scaleY = Mathf.Abs(transform.localScale.y);
            Vector3 feetLocal = baseLocalPos + Vector3.up * (minY * scaleY);
            Vector3 feetWorld = transform.parent.TransformPoint(feetLocal);
            float worldScaleY = Mathf.Abs(transform.lossyScale.y);
            transform.position = feetWorld - transform.rotation * (Vector3.up * (minY * worldScaleY));
        }
    }
}
