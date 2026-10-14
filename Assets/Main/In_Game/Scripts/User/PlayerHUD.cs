using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class PlayerHUD : MonoBehaviour
{
    [Header("HP UI 연결 (하이라키 구조 반영)")]
    public Text hpNumericText;
    public Image hpBarImage;
    public GameObject[] crosshairs;
    public int currentCrosshairIndex = 0;

    [Header("탑뷰 크로스헤어")]
    [Tooltip("탑뷰에서도 마우스 위치에 크로스헤어를 표시 (마우스 커서는 숨김, UI 위에서는 커서로 돌아옴)")]
    public bool showInTopDown = true;

    [Header("크로스헤어 반동 (쏠 때 요동)")]
    [Tooltip("반동 세기 1일 때 크로스헤어가 튀는 거리 (1080p 기준 픽셀)")]
    public float recoilJitter = 9f;
    [Tooltip("반동 세기 1일 때 크로스헤어가 커지는 비율")]
    public float recoilPunch = 0.22f;
    [Tooltip("제자리로 돌아오는 빠르기")]
    public float recoilRecover = 14f;
    private Vector2 recoilOffset;   // 1080p 기준 픽셀
    private float recoilScale;      // 0 = 원래 크기
    private Vector3[] baseScales;
    private static PlayerHUD instance;

    // 플레이어가 총을 쏠 때 (PlayerWeaponManager). 세기 1 = 보통 권총
    public static void Recoil(float strength)
    {
        if (instance == null) return;
        // 주로 위쪽으로, 좌우로 조금씩 흔들림
        Vector2 j = new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(0.5f, 1f)) * instance.recoilJitter * strength;
        instance.recoilOffset = Vector2.ClampMagnitude(instance.recoilOffset + j, instance.recoilJitter * strength * 2.2f);
        instance.recoilScale = Mathf.Min(instance.recoilScale + instance.recoilPunch * strength, instance.recoilPunch * strength * 2f);
    }

    void Awake() { instance = this; }
    void OnDestroy() { if (instance == this) instance = null; }

    private Health playerHealth;
    private PlayerController playerController;
    private Vector2[] homePositions;   // 1인칭(화면 가운데) 위치
    private bool hidingCursor;

    public int CrosshairCount => crosshairs != null ? crosshairs.Length : 0;
    public string CrosshairName(int i) => (crosshairs != null && i >= 0 && i < crosshairs.Length && crosshairs[i] != null)
        ? crosshairs[i].name.Replace("Crosshair_", "") : "-";

    // 다음 크로스헤어로 (몹 테스트 창 버튼)
    public void NextCrosshair()
    {
        if (CrosshairCount == 0) return;
        ChangeCrosshair((currentCrosshairIndex + 1) % CrosshairCount);
    }

    void Start()
    {
        homePositions = new Vector2[CrosshairCount];
        baseScales = new Vector3[CrosshairCount];
        for (int i = 0; i < CrosshairCount; i++)
            if (crosshairs[i] != null)
            {
                homePositions[i] = ((RectTransform)crosshairs[i].transform).anchoredPosition;
                baseScales[i] = crosshairs[i].transform.localScale;
            }
        // 크로스헤어 그림이 마우스 클릭/UI 판정을 가로채지 않게 (마우스 바로 아래에 있으므로)
        for (int i = 0; i < CrosshairCount; i++)
            if (crosshairs[i] != null)
                foreach (Graphic g in crosshairs[i].GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;

        // 씬 시작 시 크로스헤어를 모두 숨기는 초기화 작업만 수행합니다.
        UpdateCrosshair();
    }

    void Update()
    {
        // 1. 플레이어 지연 할당 (GameManager 연동 최적화)
        if (playerController == null || playerHealth == null)
        {
            if (GameManager.Instance != null && GameManager.Instance.playerTransform != null)
            {
                GameObject player = GameManager.Instance.playerTransform.gameObject;
                playerHealth = player.GetComponent<Health>();
                playerController = player.GetComponent<PlayerController>();
                Debug.Log("PlayerHUD: GameManager를 통해 플레이어를 성공적으로 찾았습니다.");
            }
            return; // 플레이어를 찾는 동안은 아래 UI 갱신 로직을 건너뜀
        }

        // 2. 실시간으로 플레이어 체력 및 크로스헤어 상태 갱신
        float decay = Mathf.Exp(-recoilRecover * Time.unscaledDeltaTime);
        recoilOffset *= decay;
        recoilScale *= decay;
        UpdatePlayerHUD();
        SyncCrosshairVisibility();
    }

    public void ChangeCrosshair(int index)
    {
        currentCrosshairIndex = index;
        UpdateCrosshair();
    }

    private void UpdateCrosshair()
    {
        for (int i = 0; i < crosshairs.Length; i++)
        {
            if (crosshairs[i] != null)
                crosshairs[i].SetActive(false);
        }
    }

    private void SyncCrosshairVisibility()
    {
        if (playerController == null || crosshairs.Length == 0) return;

        bool fps = playerController.isFirstPerson;
        // 탑뷰: 마우스를 따라다님. 단, UI(버튼·창) 위에 있거나 일시정지 중이면 일반 커서로
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool paused = Time.timeScale <= 0f;
        bool topDownShow = !fps && showInTopDown && !overUI && !paused && Application.isFocused;
        bool shouldBeVisible = fps || topDownShow;

        for (int i = 0; i < crosshairs.Length; i++)
        {
            if (crosshairs[i] != null)
            {
                bool isActive = (i == currentCrosshairIndex) && shouldBeVisible;
                if (crosshairs[i].activeSelf != isActive)
                {
                    crosshairs[i].SetActive(isActive);
                }
                if (isActive) PlaceCrosshair(i, fps);
            }
        }

        // 탑뷰에서 크로스헤어가 보일 때는 마우스 커서를 숨김 (1인칭은 PlayerController가 커서를 잠금)
        if (!fps)
        {
            bool hide = topDownShow;
            if (hide != hidingCursor || Cursor.visible == hide) { Cursor.visible = !hide; hidingCursor = hide; }
        }
        else hidingCursor = false;
    }

    // 1인칭: 화면 가운데(원래 자리) / 탑뷰: 마우스 위치
    private void PlaceCrosshair(int i, bool fps)
    {
        RectTransform rt = (RectTransform)crosshairs[i].transform;
        if (baseScales != null && i < baseScales.Length) rt.localScale = baseScales[i] * (1f + recoilScale);
        if (fps)
        {
            Canvas c = rt.GetComponentInParent<Canvas>();
            float sf = c != null ? c.scaleFactor : 1f;
            Vector2 off = recoilOffset * (Screen.height / 1080f) / Mathf.Max(0.01f, sf);
            if (homePositions != null && i < homePositions.Length) rt.anchoredPosition = homePositions[i] + off;
            return;
        }
        RectTransform parent = rt.parent as RectTransform;
        if (parent == null) return;
        Canvas canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
        Vector2 screen = (Vector2)Input.mousePosition + recoilOffset * (Screen.height / 1080f);
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, screen, cam, out Vector3 world))
            rt.position = world;
    }

    void OnDisable()
    {
        if (hidingCursor) { Cursor.visible = true; hidingCursor = false; }
    }

    private void UpdatePlayerHUD()
    {
        if (playerHealth == null) return;

        int currentHp = (int)playerHealth.GetCurrentHealth();
        int maxHp = (int)playerHealth.maxHealth;

        if (hpNumericText != null)
        {
            hpNumericText.text = currentHp.ToString();
        }

        if (hpBarImage != null && maxHp > 0)
        {
            hpBarImage.fillAmount = (float)currentHp / maxHp;
        }
    }
}