using UnityEngine;
using System.Collections;

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

    [Header("무기 스펙 (기본/런타임 가변)")]
    public float attackRange = 10f;
    public float attackCooldown = 0.5f;
    public int attackDamage = 10;

    [Header("플레이어 전용 스펙")]
    public PlayerWeaponData playerWepData; // 플레이어가 들었을 때만 할당됨
    public int currentAmmo;
    public bool isReloading = false;

    private float lastAttackTime = 0f;
    private Collider[] hitColliders;

    void Awake()
    {
        hitColliders = new Collider[maxTargetCapacity];
    }

    // 1. 몹/타워 전용 초기화 (기존 데이터)
    public void InitWeapon(WeaponData data)
    {
        if (data == null) return;
        this.attackDamage = (int)data.attackPower;
        this.attackRange = data.attackRange > 0 ? data.attackRange : this.attackRange;
        this.attackCooldown = data.attackSpeed > 0 ? (1f / data.attackSpeed) : this.attackCooldown;

        if (data.projectilePrefab != null)
        {
            this.projectilePrefab = data.projectilePrefab;
        }
    }

    // 2. 플레이어 전용 초기화 (새로운 탄창 시스템 등 적용)
    public void InitPlayerWeapon(PlayerWeaponData data)
    {
        if (data == null) return;
        this.playerWepData = data;

        this.attackDamage = (int)data.damage;
        this.attackRange = data.range;
        this.attackCooldown = data.fireRate > 0 ? (1f / data.fireRate) : 0.5f;
        this.currentAmmo = data.maxAmmo;
        this.isReloading = false;

        // 투사체 발사형 무기일 경우 Resources 폴더에서 동적 로드
        if (data.fireMode == "Projectile" && !string.IsNullOrEmpty(data.projectilePrefabName))
        {
            GameObject loadedPrefab = Resources.Load<GameObject>($"Prefabs/Projectiles/{data.projectilePrefabName}");
            if (loadedPrefab != null) this.projectilePrefab = loadedPrefab;
        }
    }

    void Update()
    {
        if (aimType == AimType.Auto && Time.time >= lastAttackTime + attackCooldown)
        {
            AutoAttackUpdate();
        }
    }

    // 수동 사격 (플레이어용)
    public void ManualAttackCommand(Vector3 targetPosition)
    {
        if (isReloading) return; // 장전 중 사격 불가

        if (Time.time >= lastAttackTime + attackCooldown)
        {
            // 탄창 시스템 적용 (탄창이 0보다 큰 무기만 적용)
            if (playerWepData != null && playerWepData.maxAmmo > 0)
            {
                if (currentAmmo <= 0)
                {
                    StartCoroutine(ReloadCoroutine());
                    return;
                }
                currentAmmo--; // 총알 소모
            }

            PerformAttack(null, targetPosition);
            lastAttackTime = Time.time;
        }
    }

    public IEnumerator ReloadCoroutine()
    {
        if (isReloading || playerWepData == null || playerWepData.maxAmmo <= 0) yield break;

        isReloading = true;
        Debug.Log("재장전 시작!");

        yield return new WaitForSeconds(playerWepData.reloadTime);

        currentAmmo = playerWepData.maxAmmo;
        isReloading = false;
        Debug.Log("재장전 완료!");
    }

    // =========================================================
    // 아래부터는 기존에 작성하셨던 몹 타겟팅 및 실제 데미지 적용 로직입니다.
    // =========================================================

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
            if (hitColliders[i] == null) continue;

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