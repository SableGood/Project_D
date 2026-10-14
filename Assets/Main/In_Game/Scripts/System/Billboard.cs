using UnityEngine;

public class Billboard : MonoBehaviour
{
    [Tooltip("켜면 이미지 아래 끝(발밑)을 기준으로 카메라 쪽으로 기울입니다.\n" +
             "끄면 이미지 중앙 기준으로 기울어져, 탑뷰처럼 카메라가 내려다볼 때 발이 바닥에서 떠 보입니다.\n" +
             "(몹은 MobAI가 자동으로 켭니다)")]
    public bool anchorAtFeet = false;

    [Tooltip("발밑을 바닥에 맞춤: 부모에 CharacterController(플레이어)가 있으면 그 바닥 높이에 발을 둡니다.\n" +
             "(그림이 크거나 캐릭터 크기가 작으면 발이 바닥 아래로 들어가 아랫부분이 잘려 보이던 문제 방지)")]
    public bool snapFeetToGround = true;

    private Camera mainCam;
    private SpriteRenderer sr;
    private Vector3 baseLocalPos; // 똑바로 서 있을 때의 위치 (부모 기준)
    private CharacterController groundBody;

    // 걷기 그림이 없을 때 위아래로 들썩이는 높이(미터). SpriteFrameAnimator가 매 프레임 설정
    [HideInInspector] public float bobOffset;

    // 셰이더에 발 위치 전달 (Project_D/Sprite Shadow: 깊이를 발밑에 세운 판 기준으로 → 포탑에 파묻히지 않음)
    private static readonly int FeetPosId = Shader.PropertyToID("_FeetPos");
    private MaterialPropertyBlock feetBlock;

    // 이번 프레임의 발 위치 (상체·무기 등 다른 그림이 같은 기준으로 깊이를 맞출 때 사용)
    public Vector3 FeetWorld { get; private set; }
    public bool HasFeet { get; private set; }

    // 피격 흔들림
    private float shakeAmount;
    private float shakeStart, shakeEnd;
    private bool wasShaking;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        baseLocalPos = transform.localPosition;
        groundBody = GetComponentInParent<CharacterController>();
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

    void OnDisable()
    {
        shakeEnd = 0f;
        if (wasShaking) { transform.localPosition = baseLocalPos; wasShaking = false; }
    }

    // 피격 시 그림을 잠깐 흔듦 (amount: 최대 흔들림 거리(미터), duration: 초)
    public void Shake(float amount, float duration)
    {
        if (amount <= 0f || duration <= 0f) return;
        shakeAmount = amount;
        shakeStart = Time.time;
        shakeEnd = Time.time + duration;
    }

    void LateUpdate()
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        // 스프라이트가 항상 카메라와 동일한 각도를 유지하도록 회전값을 고정합니다.
        // 캐릭터가 3D 공간에서 회전하더라도 일러스트는 항상 유저를 바라봅니다.
        transform.rotation = mainCam.transform.rotation;

        Vector3 shake = ShakeOffset();
        if (bobOffset != 0f) shake += mainCam.transform.up * bobOffset;
        bool shaking = shake != Vector3.zero;

        if (anchorAtFeet && sr != null && sr.sprite != null && transform.parent != null)
        {
            // 똑바로 섰을 때 발밑이 있을 자리(부모 기준)를 고정점으로 삼고, 그 점을 중심으로 기울임
            float minY = sr.sprite.bounds.min.y;                        // 스프라이트 기준 발밑 높이 (중앙 피벗이면 음수)
            float scaleY = Mathf.Abs(transform.localScale.y);
            Vector3 feetLocal = baseLocalPos + Vector3.up * (minY * scaleY);
            Vector3 feetWorld = transform.parent.TransformPoint(feetLocal);
            if (snapFeetToGround && groundBody != null && groundBody.enabled)
                feetWorld.y = groundBody.bounds.min.y;                  // 발을 실제 바닥(캐릭터 충돌체 아래끝)에
            float worldScaleY = Mathf.Abs(transform.lossyScale.y);
            transform.position = feetWorld - transform.rotation * (Vector3.up * (minY * worldScaleY)) + shake;
            SendFeet(feetWorld);
        }
        else
        {
            if (shaking || wasShaking)
            {
                transform.localPosition = baseLocalPos;
                transform.position += shake;
            }
            if (sr != null && sr.sprite != null)
            {
                Bounds b = sr.bounds;
                SendFeet(new Vector3(transform.position.x, b.min.y, transform.position.z));
            }
        }
        wasShaking = shaking;
    }

    // 다른 값(피격 번쩍임 등)은 유지한 채 발 위치만 갱신
    private void SendFeet(Vector3 feet)
    {
        if (sr == null) return;
        FeetWorld = feet;
        HasFeet = true;
        if (feetBlock == null) feetBlock = new MaterialPropertyBlock();
        sr.GetPropertyBlock(feetBlock);
        feetBlock.SetVector(FeetPosId, new Vector4(feet.x, feet.y, feet.z, 1f));
        sr.SetPropertyBlock(feetBlock);
    }

    // 화면 기준 좌우(+약간 위아래)로 빠르게 떨리다가 점점 잦아듦
    private Vector3 ShakeOffset()
    {
        if (Time.time >= shakeEnd) return Vector3.zero;
        float k = 1f - Mathf.InverseLerp(shakeStart, shakeEnd, Time.time); // 1 → 0
        float t = (Time.time - shakeStart) * 60f;
        float x = Mathf.Sin(t * 1.7f) * shakeAmount * k;
        float y = Mathf.Cos(t * 2.3f) * shakeAmount * 0.35f * k;
        return mainCam.transform.right * x + mainCam.transform.up * y;
    }
}
