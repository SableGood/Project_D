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
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            playerHealth = player.GetComponent<Health>();
            playerController = player.GetComponent<PlayerController>();
        }

        if (playerHealth == null)
        {
            Debug.LogWarning("PlayerHUD: 플레이어의 Health 컴포넌트를 찾지 못했습니다!");
        }
        UpdateCrosshair();
    }

    void Update()
    {
        // 플레이어를 아직 못 찾았다면 계속 찾도록 유도
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
    }

    private void SyncCrosshairVisibility()
    {
        if (playerController == null || crosshairs.Length == 0) return;

        bool shouldBeVisible = playerController.isTPS;

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

        // 대소문자 철자(currentHp, maxHp)를 모두 동일하게 통일했습니다.
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