using UnityEngine;
using System.Collections; // 코루틴 사용을 위해 추가

public class Health : MonoBehaviour
{
    [Header("체력 스펙 설정")]
    public int maxHealth = 100; // 최대 체력
    private int currentHealth;  // 현재 체력
    private bool isDead = false; // 중복 사망 판정 방지 플래그

    void Start()
    {
        currentHealth = maxHealth; // 게임 시작 시 체력을 최대치로 초기화
        isDead = false;
    }

    // 외부 공격 스크립트에서 데미지를 줄 때 호출하는 함수
    public void TakeDamage(int damage)
    {
        // 이미 사망 처리 중이라면 중복 데미지 무시
        if (isDead) return;

        currentHealth -= damage;
        // 체력이 0 이하로 내려가지 않도록 최솟값 보정 (음수 방지)
        currentHealth = Mathf.Max(currentHealth, 0);

        Debug.Log($"{gameObject.name}이(가) {damage}의 데미지를 입었습니다! (남은 체력: {currentHealth}/{maxHealth})");

        // 체력이 정확히 0 이하가 되었고 아직 사망하지 않았다면
        if (currentHealth <= 0 && !isDead)
        {
            isDead = true;
            StartCoroutine(ProcessDeathRoutine());
        }
    }

    // 사망 처리 및 약간의 인지 딜레이를 주는 코루틴
    private IEnumerator ProcessDeathRoutine()
    {
        Debug.Log($"{gameObject.name} 사망 프로세스 진입.");

        // 플레이어일 경우 피격을 인지할 수 있도록 0.5초 대기 후 게임 오버 호출
        if (CompareTag("Player") || GetComponent<PlayerController>() != null)
        {
            yield return new WaitForSeconds(0.5f); // 0.5초 동안 피격 순간 인지 가능

            if (GameManager.Instance != null)
            {
                GameManager.Instance.TriggerGameOver();
            }
        }

        // 오브젝트 파괴
        Destroy(gameObject);
    }

    // HUD 등에서 현재 체력을 안전하게 가져오기 위한 Getter 메서드
    public int GetCurrentHealth()
    {
        return currentHealth;
    }
}