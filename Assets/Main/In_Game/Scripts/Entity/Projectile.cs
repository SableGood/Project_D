using UnityEngine;
using System.Collections.Generic;

public class Projectile : MonoBehaviour
{
    public enum ProjectileType { Homing, Straight, Parabolic }

    [Header("투사체 모드 및 스펙 설정")]
    public ProjectileType projectileType = ProjectileType.Homing;
    public float speed = 15.0f;

    // 외부(Weapon)에서 주입받는 동적 스펙
    [HideInInspector] public int attackDamage;
    [HideInInspector] public LayerMask targetLayer;
    [HideInInspector] public float armorPenetration;   // 방어구 무시
    [HideInInspector] public float knockbackDistance;  // 명중 시 넉백 거리

    private Transform target;
    private Vector3 straightDir;
    private Vector3 startPos;
    private Vector3 targetPos;
    private float journeyTime = 0f;

    // 자동 에임(타겟 존재) 초기화
    public void InitializeAuto(Transform targetTransform)
    {
        target = targetTransform;
        if (target != null)
        {
            targetPos = target.position + Vector3.up * 0.5f;
            straightDir = (targetPos - transform.position).normalized;
        }
        startPos = transform.position;
        journeyTime = 0f;
    }

    // 수동 에임(좌표 발사) 초기화
    public void InitializeManual(Vector3 aimPosition)
    {
        target = null; // 고정된 타겟이 없음
        targetPos = aimPosition;
        straightDir = (targetPos - transform.position).normalized;
        startPos = transform.position;
        journeyTime = 0f;
    }

    void Update()
    {
        // ★ 타겟이 풀로 반환(비활성화)되면 파괴된 것과 같이 취급 (재활용된 몹을 쫓아가는 버그 방지)
        if (target != null && !target.gameObject.activeInHierarchy) target = null;

        switch (projectileType)
        {
            case ProjectileType.Homing:
                FlyHoming();
                break;
            case ProjectileType.Straight:
                FlyStraight();
                break;
            case ProjectileType.Parabolic:
                FlyParabolic();
                break;
        }

        // ★ 위에서 이미 명중/소멸해 풀로 돌아갔다면 여기서 종료 (같은 프레임 이중 반환 → 풀에 중복 등록되는 버그 방지)
        if (!gameObject.activeInHierarchy) return;

        // ★ [핵심] 수동 조준 등 락온 타겟이 없을 경우 비행 도중 충돌(명중) 검사를 별도로 수행
        if (target == null)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, 0.5f, targetLayer);
            if (hits.Length > 0)
            {
                HitTarget(hits[0].transform);
            }
        }
    }

    private void FlyHoming()
    {
        if (target == null)
        {
            ReturnToPool();
            return;
        }

        Vector3 currentTargetPos = target.position + Vector3.up * 0.5f;
        Vector3 dir = (currentTargetPos - transform.position).normalized;

        transform.position += dir * speed * Time.deltaTime;

        if (dir != Vector3.zero) transform.rotation = Quaternion.LookRotation(dir);

        if (Vector3.Distance(transform.position, currentTargetPos) < 0.5f)
        {
            HitTarget(target);
        }
    }

    private void FlyStraight()
    {
        transform.position += straightDir * speed * Time.deltaTime;

        if (straightDir != Vector3.zero) transform.rotation = Quaternion.LookRotation(straightDir);

        if (target != null)
        {
            float distanceToTarget = Vector3.Distance(transform.position, target.position + Vector3.up * 0.5f);
            if (distanceToTarget < 1.0f)
            {
                HitTarget(target);
                return;
            }
        }

        if (Vector3.Distance(startPos, transform.position) > 30f)
        {
            ReturnToPool();
        }
    }

    private void FlyParabolic()
    {
        journeyTime += Time.deltaTime * (speed * 0.1f);
        Vector3 currentPos = Vector3.Lerp(startPos, targetPos, journeyTime);

        float height = Mathf.Sin(journeyTime * Mathf.PI) * 3.0f;
        currentPos.y += height;

        Vector3 moveDir = (currentPos - transform.position).normalized;
        transform.position = currentPos;

        if (moveDir != Vector3.zero) transform.rotation = Quaternion.LookRotation(moveDir);

        if (journeyTime >= 1.0f)
        {
            // 도달 시점 타격. 타겟이 없으면 도착 지점에서 범위 폭발
            HitTarget(target);
        }
    }

    [Header("폭발 범위 설정 (Shell/포격 전용)")]
    public bool isAreaOfEffect = false;
    public float explosionRadius = 3.0f;

    // 특정 명중 대상(hitTransform)이 있을 경우 우선 데미지 처리
    private void HitTarget(Transform hitTransform = null)
    {
        Vector3 hitDir = transform.forward;
        Health directHealth = null;

        if (hitTransform != null)
        {
            directHealth = hitTransform.GetComponentInParent<Health>();
            if (directHealth != null && !directHealth.IsDead)
            {
                directHealth.TakeDamage(attackDamage, armorPenetration);
                KnockbackReceiver.TryApply(directHealth.transform, hitDir, knockbackDistance);
            }
        }

        if (isAreaOfEffect)
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, explosionRadius, targetLayer);
            HashSet<Health> damaged = new HashSet<Health>();
            if (directHealth != null) damaged.Add(directHealth); // ★ 직격 대상은 폭발 피해 중복 제외

            foreach (Collider hit in colliders)
            {
                Health areaHealth = hit.GetComponentInParent<Health>();
                if (areaHealth == null || areaHealth.IsDead || !damaged.Add(areaHealth)) continue;

                areaHealth.TakeDamage(attackDamage, armorPenetration);
                // 폭발은 중심에서 바깥쪽으로 밀어냄
                KnockbackReceiver.TryApply(areaHealth.transform, areaHealth.transform.position - transform.position, knockbackDistance);
            }
        }

        ReturnToPool();
    }

    private void ReturnToPool()
    {
        if (TryGetComponent(out PooledObject pooledObj))
        {
            pooledObj.ReleaseToPool();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDrawGizmos()
    {
        if (isAreaOfEffect)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, explosionRadius);
        }
    }
}