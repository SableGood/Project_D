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
    private PlayerController playerController; // 시점 상태를 읽어오기 위한 참조 추가

    void Start()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            playerHealth = player.GetComponent<Health>();
            playerController = player.GetComponent<PlayerController>(); // PlayerController 할당
        }

        if (playerHealth == null)
        {
            Debug.LogWarning("PlayerHUD: 플레이어의 Health 컴포넌트를 찾지 못했습니다!");
        }
        UpdateCrosshair();
    }

    void Update()
    {
        UpdatePlayerHUD();
        SyncCrosshairVisibility(); // 매 프레임 시점(TPS/탑뷰)에 맞춰 크로스헤어 표시 여부 동기화
        
        // ★ 추가: 플레이어를 아직 못 찾았다면 계속 찾도록 유도
        if (playerController == null || playerHealth == null)
        {
            GameObject player = GameObject.FindWithTag("Player");
            if (player != null)
            {
                playerHealth = player.GetComponent<Health>();
                playerController = player.GetComponent<PlayerController>();
                Debug.Log("PlayerHUD: 플레이어를 성공적으로 찾았습니다.");
            }
            return; // 플레이어를 찾는 동안은 아래 UI 갱신 로직을 건너뜀
        }

        // 실시간으로 플레이어 체력 상태를 갱신
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

        // 초기화 시점에서는 상태 갱신만 진행 (활성화는 SyncCrosshairVisibility에서 제어)
    }

    // TPS 상태에 따라 크로스헤어 온/오프를 결정하는 핵심 함수
    private void SyncCrosshairVisibility()
    {
        if (playerController == null || crosshairs.Length == 0) return;

        bool shouldBeVisible = playerController.isTPS;

        for (int i = 0; i < crosshairs.Length; i++)
        {
            if (crosshairs[i] != null)
            {
                // 현재 선택된 인덱스이고, TPS 모드일 때만 활성화
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

        int currentHP = playerHealth.GetCurrentHealth();
        int maxHP = playerHealth.maxHealth;

        if (hpNumericText != null)
        {
            hpNumericText.text = currentHP.ToString();
        }

        if (hpBarImage != null && maxHP > 0)
        {
            hpBarImage.fillAmount = (float)currentHP / maxHP;
        }
    }
}