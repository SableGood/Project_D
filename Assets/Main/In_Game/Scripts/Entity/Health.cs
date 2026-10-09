using UnityEngine;
using System.Collections;

public class Health : MonoBehaviour
{
    // 방어력이 아무리 높아도 최소한 이만큼은 들어갑니다. (0 데미지 방지)
    public const float MinDamage = 1f;

    [Header("체력 상태 (데이터 자동 연동)")]
    public float maxHealth;     // 데이터에서 받아올 최대 체력
    private float currentHealth; // 현재 체력
    private float defense;       // 방어력 (절대수치: 받는 피해 = 공격력 - 방어력)
    private bool isDead = false;

    [Header("디버그")]
    [Tooltip("켜면 피격할 때마다 콘솔에 피해량을 출력합니다. (연사 무기 테스트 시 콘솔이 많이 쌓이므로 기본은 꺼둠)")]
    public bool logDamage = false;

    public float Defense => defense;
    public bool IsDead => isDead;

    // MobAI/PlayerController/TurretAI 가 스폰될 때 이 함수를 불러서 스탯을 꽂아줍니다.
    public void InitStats(EntityData data)
    {
        this.maxHealth = data.maxHp;
        this.currentHealth = data.maxHp;
        this.defense = data.defense;
        this.isDead = false;
    }

    // 기존 호출부 호환용 (방어구 무시 없음)
    public void TakeDamage(int damage)
    {
        TakeDamage(damage, 0f);
    }

    // ★ 피해 공식 (Mob_Data.csv 규칙)
    //    실제 피해 = 공격력 - max(0, 방어력 - 방어구 무시)   (최소 MinDamage)
    public void TakeDamage(float damage, float armorPenetration)
    {
        if (isDead) return;

        float finalDamage = CalculateDamage(damage, defense, armorPenetration);
        currentHealth -= finalDamage;
        currentHealth = Mathf.Max(currentHealth, 0);

        if (logDamage)
            Debug.Log($"{gameObject.name} 피격! 공격 {damage} → 실제 {finalDamage} (방어 {defense}, 관통 {armorPenetration}) / 남은 체력 {currentHealth}/{maxHealth}");

        if (currentHealth <= 0 && !isDead)
        {
            isDead = true;
            StartCoroutine(ProcessDeathRoutine());
        }
    }

    public static float CalculateDamage(float damage, float defense, float armorPenetration)
    {
        if (damage <= 0f) return 0f; // 공격력 0 (버프/디버프 포탑 등)은 피해 없음
        float effectiveDefense = Mathf.Max(0f, defense - Mathf.Max(0f, armorPenetration));
        return Mathf.Max(MinDamage, damage - effectiveDefense);
    }

    private IEnumerator ProcessDeathRoutine()
    {
        Debug.Log($"{gameObject.name} 사망.");

        if (CompareTag("Player") || GetComponent<PlayerController>() != null)
        {
            yield return new WaitForSeconds(0.5f);
            if (GameManager.Instance != null) GameManager.Instance.TriggerGameOver();
        }

        // 풀링된 오브젝트는 풀로 반환, 아니면 파괴
        if (TryGetComponent(out PooledObject pooledObj))
        {
            pooledObj.ReleaseToPool();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public float GetCurrentHealth()
    {
        return currentHealth;
    }
}
