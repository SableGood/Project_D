using UnityEngine;

public class Weapon : MonoBehaviour
{
    public enum FireMode { StraightFast, RapidBullet, ParabolicShell, HomingMissile, Hitscan }
    public enum AimType { Auto, Manual }

    [Header("발사 방식 및 속성")]
    public AimType aimType = AimType.Auto;
    public FireMode fireMode = FireMode.Hitscan;
    public GameObject projectilePrefab;
    public LayerMask targetLayer;

    [Header("타겟팅 최적화 설정")]
    public int maxTargetCapacity = 20;

    [Header("무기 스펙 (기본값)")]
    public float attackRange = 10f;
    public float attackCooldown = 0.5f;
    public int attackDamage = 10;

    private float lastAttackTime = 0f;
    private Collider[] hitColliders;

    void Awake()
    {
        hitColliders = new Collider[maxTargetCapacity];
    }

    public void InitWeapon(WeaponData data)
    {
        if (data == null) return;

        // 데이터가 유효한 경우에만 덮어씌우거나, 필요한 항목만 가져옵니다.
        this.attackDamage = (int)data.attackPower;
        this.attackRange = data.attackRange > 0 ? data.attackRange : this.attackRange;
        this.attackCooldown = data.attackSpeed > 0 ? (1f / data.attackSpeed) : this.attackCooldown;
    }

    void Update()
    {
        if (aimType == AimType.Auto && Time.time >= lastAttackTime + attackCooldown)
        {
            AutoAttackUpdate();
        }
    }

    public void ManualAttackCommand(Vector3 targetPosition)
    {
        if (Time.time >= lastAttackTime + attackCooldown)
        {
            PerformAttack(null, targetPosition);
            lastAttackTime = Time.time;
        }
    }

    private void AutoAttackUpdate()
    {
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, attackRange, hitColliders, targetLayer);
        if (hitCount > 0)
        {
            Transform bestTarget = GetClosestTarget(hitCount);
            if (bestTarget != null)
            {
                PerformAttack(bestTarget, Vector3.zero);
                lastAttackTime = Time.time;
            }
        }
    }

    private Transform GetClosestTarget(int hitCount)
    {
        Transform bestTarget = null;
        float closestSqrDistance = Mathf.Infinity;
        Vector3 currentPos = transform.position;

        for (int i = 0; i < hitCount; i++)
        {
            if (hitColliders[i] == null) continue; // 널 체크 추가

            float sqrDist = (hitColliders[i].transform.position - currentPos).sqrMagnitude;
            if (sqrDist < closestSqrDistance)
            {
                closestSqrDistance = sqrDist;
                bestTarget = hitColliders[i].transform;
            }
        }
        return bestTarget;
    }

    private void PerformAttack(Transform targetTransform, Vector3 manualTargetPosition)
    {
        if (fireMode == FireMode.Hitscan)
        {
            Vector3 aimPos = targetTransform != null ? targetTransform.position + Vector3.up * 0.5f : manualTargetPosition;
            // ★ 수정: 무기(총구)의 월드 좌표 기준점 명시
            Vector3 fireOrigin = transform.position + Vector3.up * 1.0f;
            Vector3 direction = (aimPos - fireOrigin).normalized;

            if (Physics.Raycast(fireOrigin, direction, out RaycastHit hit, attackRange, targetLayer))
            {
                ApplyDamage(hit.transform);
                Debug.DrawLine(fireOrigin, hit.point, Color.green, 0.5f);
            }
            return;
        }

        if (projectilePrefab != null)
        {
            // ★ [핵심 해결] 무기가 플레이어 자식으로 잘 붙어있다면, transform.position은 플레이어를 따라 움직이는 현재 무기의 월드 좌표입니다.
            // 이 위치를 기준으로 정확히 총구 앞쪽에서 투사체를 스폰합니다.
            Vector3 spawnPos = transform.position + transform.forward * 1.0f + Vector3.up * 0.5f;

            if (PoolManager.Instance == null)
            {
                Debug.LogError("PoolManager 인스턴스가 씬에 존재하지 않습니다!");
                return;
            }

            GameObject projObj = PoolManager.Instance.Spawn(projectilePrefab, spawnPos, Quaternion.identity);
            if (projObj == null) return;

            Projectile projectile = projObj.GetComponent<Projectile>();
            if (projectile != null)
            {
                ConfigureProjectile(projectile);

                if (targetTransform != null) projectile.InitializeAuto(targetTransform);
                else projectile.InitializeManual(manualTargetPosition);
            }
        }
    }

    public void ApplyDamage(Transform target)
    {
        Health targetHealth = target.GetComponent<Health>();
        if (targetHealth != null) targetHealth.TakeDamage(attackDamage);
    }

    private void ConfigureProjectile(Projectile projectile)
    {
        projectile.attackDamage = this.attackDamage;
        projectile.targetLayer = this.targetLayer;

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
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}