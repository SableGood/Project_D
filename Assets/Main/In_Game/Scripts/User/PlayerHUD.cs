using UnityEngine;
using UnityEngine.UI;

public class PlayerHUD : MonoBehaviour
{
    [Header("HP UI 연결 (하이라키 구조 반영)")]
    public Text hpNumericText;
    public Image hpBarImage;
    public GameObject[] crosshairs;
    public int currentCrosshairIndex = 0;

    private Health playerHealth;
    private PlayerController playerController;

    void Start()
    {
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

        bool shouldBeVisible = playerController.isFirstPerson; // 크로스헤어는 1인칭에서만

        for (int i = 0; i < crosshairs.Length; i++)
        {
            if (crosshairs[i] != null)
            {
                bool isActive = (i == currentCrosshairIndex) && shouldBeVisible;
                if (crosshairs[i].activeSelf != isActive)
                {
                    crosshairs[i].SetActive(isActive);
                }
            }
        }
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