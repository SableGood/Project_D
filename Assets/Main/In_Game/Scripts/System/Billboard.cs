using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Camera mainCam;

    void Start()
    {
        mainCam = Camera.main;

        // SpriteRenderer의 숨겨진 그림자 옵션을 코드로 강제 활성화
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            // TwoSided로 설정해야 빌보드 회전 시 그림자가 얇아지거나 사라지는 현상을 막을 수 있습니다.
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            sr.receiveShadows = true;
        }
    }

    void LateUpdate()
    {
        if (mainCam == null) return;

        // 스프라이트가 항상 카메라와 동일한 각도를 유지하도록 회전값을 고정합니다.
        // 캐릭터가 3D 공간에서 회전하더라도 일러스트는 항상 유저를 바라봅니다.
        transform.rotation = mainCam.transform.rotation;
    }
}