using UnityEngine;

public class Weapon : MonoBehaviour
{
    public enum FireMode { StraightFast, RapidBullet, ParabolicShell, HomingMissile, Hitscan }

    [Header("발사 방식 및 타겟팅 (인스펙터 유지)")]
    public FireMode fireMode = FireMode.Hitscan;
    public bool isManualFire = false;
    public GameObject projectilePrefab;
    public LayerMask targetLayer;

    [Header("무기 스펙 (데이터 자동 연동)")]
    public float attackRange;
    public float attackCooldown;
    public int attackDamage;

    private float lastAttackTime = 0f;

    // MobAI가 무기 데이터를 꽂아주는 함수
    public void InitWeapon(WeaponData data)
    {
        if (data == null) return;

        this.attackDamage = (int)data.attackPower;
        this.attackRange = data.attackRange;

        // CSV의 공격 속도 수치가 '초당 공격 횟수'라면 1f / data.attackSpeed 로 쿨타임을 계산합니다.
        // 만약 수치 자체가 '공격 간격(초)'라면 data.attackSpeed 를 그대로 넣으시면 됩니다.
        this.attackCooldown = data.attackSpeed > 0 ? (1f / data.attackSpeed) : 1f;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.V))
        {
            isManualFire = !isManualFire;
            Debug.Log(gameObject.name + " 수동 발사 모드: " + isManualFire);
        }

        if (Time.time >= lastAttackTime + attackCooldown)
        {
            if (isManualFire)
            {
                if (Input.GetMouseButton(0)) TryAttack();
            }
            else
            {
                TryAttack();
            }
        }
    }

    private void TryAttack()
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, attackRange, targetLayer);

        if (hitColliders.Length > 0)
        {
            Transform target = hitColliders[0].transform;
            PerformAttack(target);
            lastAttackTime = Time.time;
        }
    }

    private void PerformAttack(Transform target)
    {
        if (fireMode == FireMode.Hitscan)
        {
            ApplyDamage(target);
            Debug.DrawLine(transform.position + Vector3.up * 1.0f, target.position + Vector3.up * 0.5f, Color.green, 0.5f);
            return;
        }

        if (projectilePrefab != null)
        {
            Vector3 spawnPos = transform.position + Vector3.up * 1.0f;
            GameObject projObj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

            Projectile projectile = projObj.GetComponent<Projectile>();
            if (projectile != null) ConfigureProjectile(projectile, target);
        }
    }

    public void ApplyDamage(Transform target)
    {
        Health targetHealth = target.GetComponent<Health>();
        if (targetHealth != null) targetHealth.TakeDamage(attackDamage);
    }

    private void ConfigureProjectile(Projectile projectile, Transform target)
    {
        switch (fireMode)
        {
            case FireMode.StraightFast:
                projectile.projectileType = Projectile.ProjectileType.Straight;
                projectile.speed = 25.0f;
                break;
            case FireMode.RapidBullet:
                projectile.projectileType = Projectile.ProjectileType.Straight;
                projectile.speed = 18.0f;
                break;
            case FireMode.ParabolicShell:
                projectile.projectileType = Projectile.ProjectileType.Parabolic;
                projectile.speed = 8.0f;
                break;
            case FireMode.HomingMissile:
                projectile.projectileType = Projectile.ProjectileType.Homing;
                projectile.speed = 12.0f;
                break;
        }
        projectile.Initialize(target);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}