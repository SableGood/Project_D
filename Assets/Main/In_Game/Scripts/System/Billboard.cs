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

    // 1인칭 그림자: 그림은 카메라를 보지만, 그림자는 탑뷰 때와 같은 방향의 판에서 드리움
    //  (카메라·플레이어가 돌 때 그림자가 같이 돌거나 얇아지지 않게 → 광원 기준으로 고정)
    private SpriteRenderer shadowProxy;
    private bool proxyActive;
    private UnityEngine.Rendering.ShadowCastingMode savedShadowMode;

    // v167 발밑 그림자: 그림 모양 그림자(윤곽 뒤로 길게 늘어짐) 대신 발밑에 둥근 그림자 하나 (탑뷰·1인칭 모두)
    public static bool BlobShadows = true;
    public static float BlobSize = 1f;          // 전체 크기 배율
    public static float BlobAlpha = 0.42f;      // 진하기
    [Tooltip("이 캐릭터의 발밑 그림자 크기 배율")]
    public float blobScale = 1f;
    private SpriteRenderer blob;
    private bool blobOwner;
    private static Sprite blobSprite;
    private static Transform blobRoot;
    private static int blobMask = 0;

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
            sr.shadowCastingMode = BlobShadows ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            sr.receiveShadows = true;
        }
        // 한 캐릭터에 Billboard가 여럿이어도 그림자는 하나 (같은 부모 아래 첫 번째만)
        blobOwner = BlobShadows && sr != null &&
            (transform.parent == null || transform.parent.GetComponentInChildren<Billboard>(true) == this);
    }

    void OnDisable()
    {
        if (blob != null) blob.enabled = false;
        if (shadowProxy != null) shadowProxy.enabled = false;
        if (proxyActive) { proxyActive = false; if (sr != null) sr.shadowCastingMode = savedShadowMode; }
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
        //  * 1인칭에서 그림자 전용(내 몸)은 탑뷰 방향 그대로 → 그림자가 시점에 따라 돌지 않음
        bool fpsView = CameraController.FirstPersonView;
        bool shadowOnly = sr != null && sr.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        transform.rotation = (fpsView && shadowOnly) ? CameraController.TopDownRotation : mainCam.transform.rotation;

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
        UpdateShadowProxy(fpsView && !shadowOnly);
        UpdateBlob();
    }

    // ===== v167 발밑 그림자 =====
    private void UpdateBlob()
    {
        if (!blobOwner) return;
        bool show = sr.enabled && sr.sprite != null;
        if (!show) { if (blob != null && blob.enabled) blob.enabled = false; return; }
        if (blob == null)
        {
            if (blobRoot == null) blobRoot = new GameObject("BlobShadows").transform;
            GameObject go = new GameObject(name + "_Blob");
            go.layer = 2; // Ignore Raycast
            go.transform.SetParent(blobRoot, false);
            blob = go.AddComponent<SpriteRenderer>();
            blob.sprite = BlobSprite();
            blob.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            blob.receiveShadows = false;
            blob.sortingLayerID = sr.sortingLayerID;
            blob.sortingOrder = sr.sortingOrder - 5; // 몸 그림보다 먼저(아래에) 그림
        }
        if (!blob.enabled) blob.enabled = true;

        Vector3 feet = HasFeet ? FeetWorld : transform.position;
        float groundY = feet.y;
        if (blobMask == 0) blobMask = ~((1 << 2) | (1 << 5) | LayerMask.GetMask("Mob", "Player"));
        if (Physics.Raycast(feet + Vector3.up * 0.4f, Vector3.down, out RaycastHit hit, 4f, blobMask, QueryTriggerInteraction.Ignore))
            groundY = Mathf.Min(feet.y, hit.point.y);
        // 공중에 뜨면 작고 옅게
        float lift = Mathf.Clamp01((feet.y - groundY) / 2.5f);

        float w = sr.sprite.bounds.size.x * Mathf.Abs(transform.lossyScale.x);
        float h = sr.sprite.bounds.size.y * Mathf.Abs(transform.lossyScale.y);
        float size = Mathf.Clamp(Mathf.Min(w * 0.6f, h * 0.45f), 0.3f, 7f) * blobScale * BlobSize * (1f - lift * 0.4f);
        Color c = new Color(0.13f, 0.08f, 0.10f, BlobAlpha * (1f - lift * 0.6f) * sr.color.a);
        if (blob.color != c) blob.color = c;

        Transform t = blob.transform;
        t.position = new Vector3(feet.x, groundY + 0.035f, feet.z);
        t.rotation = Quaternion.Euler(90f, mainCam.transform.eulerAngles.y, 0f);
        // 그림 1칸 = 1m (64px / PPU 64) → 가로 size, 세로(깊이) size × 0.5
        t.localScale = new Vector3(size, size * 0.5f, 1f);
    }

    // 가운데가 진하고 가장자리가 부드러운 원 (64×64, 1m)
    private static Sprite BlobSprite()
    {
        if (blobSprite != null) return blobSprite;
        const int N = 64;
        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float t = Mathf.Clamp01((r - 0.55f) / 0.45f);
                float a = 1f - t * t * (3f - 2f * t);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        blobSprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        return blobSprite;
    }

    // 1인칭: 보이는 그림의 그림자는 끄고, 탑뷰 방향으로 세운 '그림자 전용' 복사본이 대신 드리움
    private void UpdateShadowProxy(bool want)
    {
        if (sr == null) return;
        if (want && !proxyActive)
        {
            savedShadowMode = sr.shadowCastingMode;
            if (savedShadowMode == UnityEngine.Rendering.ShadowCastingMode.Off) return; // 원래 그림자가 없는 그림
            proxyActive = true;
        }
        else if (!want && proxyActive)
        {
            proxyActive = false;
            sr.shadowCastingMode = savedShadowMode;
            if (shadowProxy != null) shadowProxy.enabled = false;
            return;
        }
        if (!proxyActive) return;

        if (shadowProxy == null)
        {
            GameObject go = new GameObject(name + "_Shadow");
            go.layer = gameObject.layer;
            if (transform.parent != null) go.transform.SetParent(transform.parent, false);
            shadowProxy = go.AddComponent<SpriteRenderer>();
            shadowProxy.receiveShadows = false;
        }
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shadowProxy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        shadowProxy.sharedMaterial = sr.sharedMaterial;
        shadowProxy.sprite = sr.sprite;
        shadowProxy.flipX = sr.flipX;
        shadowProxy.flipY = sr.flipY;
        shadowProxy.enabled = sr.enabled && sr.sprite != null;

        Transform t = shadowProxy.transform;
        Quaternion rot = CameraController.TopDownRotation;
        t.localScale = transform.localScale;
        t.rotation = rot;
        if (anchorAtFeet && sr.sprite != null && transform.parent != null)
        {
            float minY = sr.sprite.bounds.min.y;
            Vector3 feetLocal = baseLocalPos + Vector3.up * (minY * Mathf.Abs(transform.localScale.y));
            Vector3 feetWorld = transform.parent.TransformPoint(feetLocal);
            if (snapFeetToGround && groundBody != null && groundBody.enabled) feetWorld.y = groundBody.bounds.min.y;
            t.position = feetWorld - rot * (Vector3.up * (minY * Mathf.Abs(transform.lossyScale.y)));
        }
        else t.position = transform.position;
    }

    void OnDestroy()
    {
        if (blob != null) Destroy(blob.gameObject);
        if (shadowProxy != null) Destroy(shadowProxy.gameObject);
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
