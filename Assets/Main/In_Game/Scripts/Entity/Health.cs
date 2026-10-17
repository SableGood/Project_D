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
    [Tooltip("켜면 피해를 받아도 체력이 줄지 않음 (피격 이벤트는 그대로 발생 → 테스트 패널에서 받은 피해량 확인 가능)")]
    public bool invincible = false;

    // 피격 시 실제 받은 피해량을 알림 (피격 번쩍임, 데미지 숫자 등 연출용)
    public event System.Action<float> OnDamaged;
    // 체력이 0이 되어 죽는 순간 1번 알림 (처치 보상 등). 풀 반환/웨이브 정리로 꺼질 때는 호출되지 않음
    public event System.Action OnDied;
    // 누가 맞든 피격될 때마다 알림 (피격 이펙트 등 전역 연출용: HitEffects)
    public static event System.Action<Health, float> AnyDamaged;
    // 위와 같지만 공격한 쪽(몹·플레이어 루트)도 함께 알림. 공격자를 모르면 null (재능: 흡혈·피해 반사 등)
    public static event System.Action<Health, float, Transform> AnyDamagedBy;

    // 재능 등으로 붙는 추가 방어력 / 기본 방어력 무시(부활 페널티)
    [HideInInspector] public float bonusDefense;
    [HideInInspector] public bool ignoreBaseDefense;
    // 체력이 0이 되는 순간 호출. true를 돌려주면 죽지 않음 (재능 '부활'). 체력 회복은 호출한 쪽에서 처리
    public System.Func<Health, bool> BeforeDeath;

    public float Defense => (ignoreBaseDefense ? 0f : defense) + bonusDefense;
    public float CurrentHealth => currentHealth;
    public bool IsDead => isDead;

    // MobAI/PlayerController/TurretAI 가 스폰될 때 이 함수를 불러서 스탯을 꽂아줍니다.
    public void InitStats(EntityData data)
    {
        this.maxHealth = data.maxHp;
        this.currentHealth = data.maxHp;
        this.defense = data.defense;
        this.isDead = false;
    }

    // 데이터 없이 직접 지정 (임시 타워 등)
    public void InitStats(float hp, float def)
    {
        this.maxHealth = hp;
        this.currentHealth = hp;
        this.defense = def;
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
        TakeDamage(damage, armorPenetration, null);
    }

    // attacker: 공격한 개체 (모르면 null)
    public void TakeDamage(float damage, float armorPenetration, Transform attacker)
    {
        if (isDead) return;

        float finalDamage = CalculateDamage(damage, Defense, armorPenetration);
        if (invincible)
        {
            OnDamaged?.Invoke(finalDamage);
            AnyDamaged?.Invoke(this, finalDamage);
            AnyDamagedBy?.Invoke(this, finalDamage, attacker);
            return;
        }
        currentHealth -= finalDamage;
        currentHealth = Mathf.Max(currentHealth, 0);
        if (currentHealth <= 0 && BeforeDeath != null && BeforeDeath(this)) currentHealth = Mathf.Max(currentHealth, 1f);
        OnDamaged?.Invoke(finalDamage);
        AnyDamaged?.Invoke(this, finalDamage);
        AnyDamagedBy?.Invoke(this, finalDamage, attacker);

        if (logDamage)
            Debug.Log($"{gameObject.name} 피격! 공격 {damage} → 실제 {finalDamage} (방어 {defense}, 관통 {armorPenetration}) / 남은 체력 {currentHealth}/{maxHealth}");

        if (currentHealth <= 0 && !isDead)
        {
            isDead = true;
            OnDied?.Invoke();
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

    // 체력 회복 (수리 포탑 등). 실제로 회복된 양을 돌려줌
    public event System.Action<float> OnHealed;
    public float Heal(float amount)
    {
        if (isDead || amount <= 0f || maxHealth <= 0f) return 0f;
        float healed = Mathf.Min(amount, maxHealth - currentHealth);
        if (healed <= 0f) return 0f;
        currentHealth += healed;
        OnHealed?.Invoke(healed);
        return healed;
    }

    public bool IsFullHealth => currentHealth >= maxHealth;

    // 최대 체력 증감 (재능). 늘어난 만큼 현재 체력도 함께 늘어남
    public void AddMaxHealth(float delta)
    {
        maxHealth = Mathf.Max(1f, maxHealth + delta);
        if (delta > 0f) currentHealth += delta;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }

    // 현재 체력을 직접 지정 (부활 등)
    public void SetCurrentHealth(float value)
    {
        currentHealth = Mathf.Clamp(value, 0f, maxHealth);
    }

    public float GetCurrentHealth()
    {
        return currentHealth;
    }
}
