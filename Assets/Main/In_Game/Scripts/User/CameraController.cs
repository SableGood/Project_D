using UnityEngine;
using UnityEngine.UI;

// 탑뷰 ↔ 1인칭(FPS) 시점 카메라. Tab으로 전환
//  - 기본(Blink): 카메라가 날아가지 않고, 화면이 순간 어두워졌다가 새 시점으로 밝아짐 → 멀미 없음
//  - Smooth: 두 시점 사이를 짧게 이동 (예전 방식보다 짧고, 시작·끝이 부드럽고, 방향은 바라보는 쪽으로 먼저 맞춤)
public class CameraController : MonoBehaviour
{
    public enum TransitionMode { Blink, Smooth }

    [Header("연결할 대상")]
    public PlayerController player;

    [Header("시점 전환")]
    public TransitionMode transitionMode = TransitionMode.Blink;
    [Tooltip("Blink: 어두운 화면에서 다시 밝아지는 시간 (초)")]
    public float blinkDuration = 0.22f;
    [Tooltip("Blink: 전환 순간 화면 어둡기 (1 = 완전히 검게)")]
    [Range(0f, 1f)] public float blinkDarkness = 0.85f;
    [Tooltip("Smooth: 이동 시간 (초)")]
    public float smoothDuration = 0.35f;
    [HideInInspector] public float transitionSpeed = 8.0f; // (예전 값, 사용 안 함)

    [Header("탑뷰 세팅")]
    public Vector3 topDownOffset = new Vector3(0, 10, -7);
    public Vector3 topDownRotation = new Vector3(55, 0, 0);
    public float topDownFOV = 60f;
    [Tooltip("v156 탑뷰 마우스 휠 줌: 카메라 거리 배율 (작을수록 가까이)")]
    public float zoomMin = 0.6f, zoomMax = 1.4f, zoomStep = 0.1f, zoomSmooth = 14f;
    private float zoom = 1f, zoomTarget = 1f;

    [Header("1인칭(FPS) 세팅")]
    [Tooltip("플레이어 중심에서 눈 높이까지 (위쪽)")]
    public float fpsEyeHeight = 0.5f;
    [Tooltip("눈 위치를 앞쪽으로 살짝 빼는 거리 (내 몸이 화면에 걸리지 않게)")]
    public float fpsForwardOffset = 0.1f;
    public float fpsFOV = 75f;
    [Tooltip("1인칭 근거리 클리핑 (무기가 카메라 앞에서 잘리지 않도록 작게)")]
    public float fpsNearClip = 0.05f;

    // 다른 스크립트용: 지금 1인칭 화면인지, 탑뷰 카메라 각도 (1인칭에서 그림자 방향을 탑뷰와 같게 고정할 때 사용)
    public static bool FirstPersonView { get; private set; }
    public static Quaternion TopDownRotation { get; private set; } = Quaternion.Euler(55f, 0f, 0f);

    private Camera cam;
    private float viewBlend = 0f;
    private float topDownNearClip;
    private bool lastFirstPerson;
    private bool initialized;
    private float blinkStart = -999f;
    private Image blinkImage;

    // ===== 화면 흔들림 · 번쩍임 (폭발 등에서 호출) =====
    private static CameraController instance;
    private static float shakeAmp, shakeDur = 1f, shakeUntil;
    private static Color flashColor;
    private static float flashAlpha, flashDur = 1f, flashStart = -999f;
    private Image flashImage;

    // amplitude: 미터 단위 최대 흔들림, duration: 초 (점점 약해짐). 더 약한 흔들림은 진행 중인 것을 덮지 않음
    public static void Shake(float amplitude, float duration)
    {
        if (amplitude <= 0.001f || duration <= 0f) return;
        float now = Time.time;
        float current = now < shakeUntil ? shakeAmp * (shakeUntil - now) / shakeDur : 0f;
        if (amplitude < current) return;
        shakeAmp = amplitude; shakeDur = duration; shakeUntil = now + duration;
    }

    // 화면 전체를 color로 잠깐 덮었다가 사라짐 (alpha: 시작 진하기)
    public static void Flash(Color color, float alpha, float duration)
    {
        if (alpha <= 0.01f || duration <= 0f) return;
        flashColor = color; flashAlpha = Mathf.Clamp01(alpha); flashDur = duration; flashStart = Time.unscaledTime;
    }

    private void ApplyShake()
    {
        float now = Time.time;
        if (now >= shakeUntil) return;
        float k = (shakeUntil - now) / Mathf.Max(0.01f, shakeDur);
        transform.position += Random.insideUnitSphere * shakeAmp * k * k;
    }

    private void UpdateFlash()
    {
        float t = (Time.unscaledTime - flashStart) / Mathf.Max(0.01f, flashDur);
        float a = t >= 1f || t < 0f ? 0f : flashAlpha * (1f - t);
        if (a <= 0f && flashImage == null) return;
        if (flashImage == null)
        {
            GameObject go = new GameObject("ExplosionFlashOverlay", typeof(RectTransform));
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 450;   // HUD 위, 메뉴(재능·상점) 아래
            GameObject img = new GameObject("Flash", typeof(RectTransform));
            img.transform.SetParent(go.transform, false);
            RectTransform rt = (RectTransform)img.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            flashImage = img.AddComponent<Image>();
            flashImage.raycastTarget = false;
        }
        Color c = flashColor; c.a = a; flashImage.color = c;
        if (flashImage.enabled != a > 0f) flashImage.enabled = a > 0f;
    }

    void Start()
    {
        instance = this;
        cam = GetComponent<Camera>();
        topDownNearClip = cam.nearClipPlane;
    }

    // v149 시작 연출(InGameIntro)이 카메라를 잠시 직접 움직임
    public static bool OverrideActive;
    public static Vector3 OverridePos;
    public static Quaternion OverrideRot = Quaternion.identity;
    public static float OverrideFov = 60f;
    public static CameraController Main => instance;

    // 탑뷰 게임 화면에서 이 바닥 위치를 볼 때의 카메라 자리 (시작 연출의 마지막 자리)
    public void GetTopDownPose(Vector3 groundPos, out Vector3 pos, out Quaternion rot, out float fov)
    {
        pos = groundPos + topDownOffset;
        rot = Quaternion.Euler(topDownRotation);
        fov = topDownFOV;
    }

    void LateUpdate()
    {
        if (OverrideActive)
        {
            transform.position = OverridePos;
            transform.rotation = OverrideRot;
            if (cam != null) { cam.fieldOfView = OverrideFov; cam.nearClipPlane = topDownNearClip; }
            ApplyShake();
            UpdateFlash();
            return;
        }
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
        if (!initialized) { initialized = true; lastFirstPerson = player.isFirstPerson; viewBlend = targetBlend; }
        bool switched = player.isFirstPerson != lastFirstPerson;
        lastFirstPerson = player.isFirstPerson;

        if (transitionMode == TransitionMode.Blink)
        {
            // 바로 새 시점으로 (카메라가 움직이는 장면이 없음) + 화면 깜빡임으로 전환을 가림
            viewBlend = targetBlend;
            if (switched) blinkStart = Time.unscaledTime;
        }
        else
        {
            // 일정한 시간 안에 이동, 시작과 끝을 천천히 (SmoothStep)
            float step = Time.deltaTime / Mathf.Max(0.05f, smoothDuration);
            viewBlend = Mathf.MoveTowards(viewBlend, targetBlend, step);
        }
        UpdateBlink();
        float e = Mathf.SmoothStep(0f, 1f, viewBlend);
        FirstPersonView = e > 0.5f;
        TopDownRotation = Quaternion.Euler(topDownRotation);

        Transform p = player.transform;

        // 탑뷰 위치/회전
        // 높이는 바닥 기준 (점프해도 카메라가 따라 올라가지 않아야 캐릭터가 뜨는 게 보임)
        UpdateZoom();
        Vector3 tdPos = new Vector3(p.position.x, player.GroundY, p.position.z) + topDownOffset * zoom;
        Quaternion tdRot = Quaternion.Euler(topDownRotation);

        // 1인칭 위치/회전 (눈 높이, 플레이어가 바라보는 방향 + 상하 시선)
        Quaternion fpsRot = Quaternion.Euler(player.cameraPitch, p.eulerAngles.y, 0);
        Vector3 fpsPos = p.position + Vector3.up * (fpsEyeHeight + player.EyeOffset) + p.forward * fpsForwardOffset; // EyeOffset: 구르기 때 낮아짐

        // Smooth: 탑뷰 쪽 방향을 플레이어가 바라보는 쪽으로 맞춰서 화면이 크게 빙글 돌지 않게
        if (transitionMode == TransitionMode.Smooth && viewBlend > 0f && viewBlend < 1f)
            tdRot = Quaternion.Euler(topDownRotation.x, p.eulerAngles.y, topDownRotation.z);

        // 카메라 이동, 회전, FOV, 근거리 클리핑 적용
        transform.position = Vector3.Lerp(tdPos, fpsPos, e);
        transform.rotation = Quaternion.Slerp(tdRot, fpsRot, e);
        cam.fieldOfView = Mathf.Lerp(topDownFOV, fpsFOV, e);
        cam.nearClipPlane = Mathf.Lerp(topDownNearClip, fpsNearClip, e);
        ApplyShake();
        UpdateFlash();
    }

    // v156 탑뷰에서 마우스 휠 = 줌 (1인칭에서는 휠 = 무기 교체)
    private void UpdateZoom()
    {
        bool can = !player.isFirstPerson && Time.timeScale > 0f && !AugmentManager.IsOpen && !TalentBrowser.IsOpen && !WeaponBrowser.IsOpen;
        float wheel = can ? Input.mouseScrollDelta.y : 0f;
        if (wheel != 0f) zoomTarget = Mathf.Clamp(zoomTarget - Mathf.Sign(wheel) * zoomStep, zoomMin, zoomMax);
        zoom = Mathf.Lerp(zoom, zoomTarget, 1f - Mathf.Exp(-zoomSmooth * Time.unscaledDeltaTime));
    }

    // 전환 순간 화면을 어둡게 했다가 빠르게 밝힘
    private void UpdateBlink()
    {
        float t = (Time.unscaledTime - blinkStart) / Mathf.Max(0.01f, blinkDuration);
        float a = t >= 1f || t < 0f ? 0f : blinkDarkness * (1f - t) * (1f - t);
        if (a <= 0f && blinkImage == null) return;
        if (blinkImage == null) CreateBlinkOverlay();
        Color c = blinkImage.color; c.a = a; blinkImage.color = c;
        if (blinkImage.enabled != a > 0f) blinkImage.enabled = a > 0f;
    }

    private void CreateBlinkOverlay()
    {
        GameObject go = new GameObject("ViewSwitchBlink", typeof(RectTransform));
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;   // 모든 UI 위
        GameObject img = new GameObject("Fade", typeof(RectTransform));
        img.transform.SetParent(go.transform, false);
        RectTransform rt = (RectTransform)img.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        blinkImage = img.AddComponent<Image>();
        blinkImage.color = new Color(0f, 0f, 0f, 0f);
        blinkImage.raycastTarget = false;
        blinkImage.enabled = false;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (blinkImage != null) Destroy(blinkImage.transform.parent.gameObject);
        if (flashImage != null) Destroy(flashImage.transform.parent.gameObject);
    }
}
