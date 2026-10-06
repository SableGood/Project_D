using UnityEngine;
using System.Collections;

public class Health : MonoBehaviour
{
    [Header("체력 상태 (데이터 자동 연동)")]
    public float maxHealth;     // 데이터에서 받아올 최대 체력
    private float currentHealth; // 현재 체력
    private float defense;       // 방어력 (추후 데미지 공식에 활용)
    private bool isDead = false;

    // MobAI가 스폰될 때 이 함수를 불러서 스탯을 꽂아줍니다.
    public void InitStats(EntityData data)
    {
        this.maxHealth = data.maxHp;
        this.currentHealth = data.maxHp;
        this.defense = data.defense;
        this.isDead = false;
    }

    public void TakeDamage(int damage)
    {
        if (isDead) return;

        // 추후 여기에 방어력(defense)을 적용한 데미지 감소 공식을 넣을 수 있습니다.
        currentHealth -= damage;
        currentHealth = Mathf.Max(currentHealth, 0);

        Debug.Log($"{gameObject.name} 피격! (남은 체력: {currentHealth}/{maxHealth})");

        if (currentHealth <= 0 && !isDead)
        {
            isDead = true;
            StartCoroutine(ProcessDeathRoutine());
        }
    }

    private IEnumerator ProcessDeathRoutine()
    {
        Debug.Log($"{gameObject.name} 사망.");

        if (CompareTag("Player") || GetComponent<PlayerController>() != null)
        {
            yield return new WaitForSeconds(0.5f);
            if (GameManager.Instance != null) GameManager.Instance.TriggerGameOver();
        }

        // [최적화 코드] Die() 함수 내부
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