using UnityEngine;

public class Weapon : MonoBehaviour
{
    public enum FireMode { StraightFast, RapidBullet, ParabolicShell, HomingMissile, Hitscan }

    [Header("무기 분류 및 스펙")]
    public FireMode fireMode = FireMode.Hitscan;
    public float attackRange = 10.0f;
    public float attackCooldown = 0.5f;
    public int attackDamage = 15;

    [Header("발사 모드 설정 (수동/자동)")]
    public bool isManualFire = false; // true: 클릭 시 발사, false: 자동 탐지 발사

    [Header("투사체 프리팹 연결 (Hitscan 외 사용)")]
    public GameObject projectilePrefab;

    [Header("타겟팅 설정")]
    public LayerMask targetLayer;

    private float lastAttackTime = 0f;

    void Update()
    {
        // 탭(V) 키를 눌러 수동/자동 모드를 게임 중 실시간으로 전환할 수 있습니다.
        if (Input.GetKeyDown(KeyCode.V))
        {
            isManualFire = !isManualFire;
            Debug.Log(gameObject.name + " 수동 발사 모드: " + isManualFire);
        }

        // 쿨타임이 찼을 때 발사 로직 처리
        if (Time.time >= lastAttackTime + attackCooldown)
        {
            if (isManualFire)
            {
                // 수동 모드: 마우스 좌클릭(0)을 누르고 있을 때만 공격 시도
                if (Input.GetMouseButton(0))
                {
                    TryAttack();
                }
            }
            else
            {
                // 자동 모드: 사거리 내에 적이 들어오면 알아서 공격 시도
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

    // Weapon.cs 내부의 PerformAttack 및 ExecuteHitscan 함수를 아래와 같이 수정합니다.

    private void PerformAttack(Transform target)
    {
        // 히트스캔 모드일 경우 즉시 데미지 전달
        if (fireMode == FireMode.Hitscan)
        {
            ApplyDamage(target);
            Debug.DrawLine(transform.position + Vector3.up * 1.0f, target.position + Vector3.up * 0.5f, Color.green, 0.5f);
            return;
        }

        // 투사체 생성 방식
        if (projectilePrefab != null)
        {
            Vector3 spawnPos = transform.position + Vector3.up * 1.0f;
            GameObject projObj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);

            Projectile projectile = projObj.GetComponent<Projectile>();
            if (projectile != null)
            {
                ConfigureProjectile(projectile, target);
            }
        }
    }

    // 대상에게 데미지를 전달하는 공통 함수
    public void ApplyDamage(Transform target)
    {
        Health targetHealth = target.GetComponent<Health>();
        if (targetHealth != null)
        {
            targetHealth.TakeDamage(attackDamage);
        }
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