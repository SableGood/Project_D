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
    [HideInInspector] public Transform attacker; // 쏜 개체 (피격 알림용, 없으면 null)
    [HideInInspector] public bool isCrit;        // 치명타 탄 (플레이어 무기, Weapon이 정함)
    [HideInInspector] public bool blockByWall;   // v154 적 탄: 요새 벽에 막힘
    [HideInInspector] public bool fromPlayerGun; // 플레이어 총에서 나간 탄 (재능 효과)
    [HideInInspector] public LayerMask targetLayer;
    [HideInInspector] public float armorPenetration;   // 방어구 무시
    [HideInInspector] public float knockbackDistance;  // 명중 시 넉백 거리
    [HideInInspector] public float hitSlowPercent;     // 명중 시 이동 둔화 (제압 사격)
    [HideInInspector] public float hitSlowDuration;
    [HideInInspector] public float stopDistance;
    // v109: 그림 탄 (몹 총탄·포탄). null이면 프리팹 원래 모양(육면체 등)
    [HideInInspector] public Sprite visualSprite;
    [HideInInspector] public float visualLength = 0.4f;   // 그림 길이 (m)
    [HideInInspector] public string explosionSfx;         // v121: 폭발 소리 (Sound 폴더 기준). 비우면 Gun/Frag_Explode
    [HideInInspector] public string explosionFx;          // 범위 폭발 때 재생할 도트 이펙트 (PixelFx 이름)
    private SpriteRenderer visualSr;
    private Renderer[] meshRenderers;
    private Vector3 lastPos;

    // 그림 탄 켜기/끄기 (풀에서 재사용되므로 쏠 때마다 호출)
    public void SetVisual(Sprite s, float length)
    {
        if (meshRenderers == null) meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        visualSprite = s;
        visualLength = length;
        foreach (Renderer r in meshRenderers) if (r != null) r.enabled = s == null;
        if (s != null && visualSr == null)
        {
            GameObject go = new GameObject("Visual");
            go.transform.SetParent(transform, false);
            visualSr = go.AddComponent<SpriteRenderer>();
            visualSr.sortingOrder = 15;
            visualSr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        if (visualSr != null)
        {
            visualSr.enabled = s != null;
            visualSr.sprite = s;
        }
        lastPos = transform.position;
    }

    // 그림 탄: 카메라를 보게 하고, 화면에서 날아가는 방향으로 머리를 돌림 (그림은 위쪽이 머리)
    void LateUpdate()
    {
        if (visualSr == null || !visualSr.enabled || visualSprite == null) return;
        Camera cam = Camera.main;
        Vector3 v = transform.position - lastPos;
        lastPos = transform.position;
        if (cam == null) return;
        Transform vt = visualSr.transform;
        // v115: 그림의 PPU와 상관없이 visualLength(m)가 되게 (예전: 픽셀 수 기준 → PPU 100 그림은 100배 작아 안 보였음)
        float px = visualLength / Mathf.Max(1e-4f, visualSprite.bounds.size.y);
        // 부모(프리팹) 크기를 상쇄
        Vector3 ps = transform.lossyScale;
        vt.localScale = new Vector3(px / Mathf.Max(1e-4f, ps.x), px / Mathf.Max(1e-4f, ps.y), px / Mathf.Max(1e-4f, ps.z));
        float ang = 0f;
        if (v.sqrMagnitude > 1e-8f)
        {
            float x = Vector3.Dot(v, cam.transform.right), y = Vector3.Dot(v, cam.transform.up);
            ang = Mathf.Atan2(y, x) * Mathf.Rad2Deg - 90f;
            lastAng = ang;
        }
        else ang = lastAng;
        vt.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, ang);

        // v115 로켓 연기 꼬리: 꽁무니에서 뒤쪽으로 퍼지는 연기를 짧은 간격으로
        if (!string.IsNullOrEmpty(trailFx) && v.sqrMagnitude > 1e-8f && Time.time >= nextTrail && PixelFx.Has(trailFx))
        {
            nextTrail = Time.time + trailInterval;
            Vector3 back = -v.normalized;
            PixelFx.Play(trailFx, transform.position + back * visualLength * 0.5f, back, trailMetersPerPixel, null, 1.6f, 0.7f);
        }
    }
    [HideInInspector] public string trailFx;              // 날아가며 남기는 연기 (PixelFx 이름). null = 없음
    [HideInInspector] public float trailInterval = 0.035f;
    [HideInInspector] public float trailMetersPerPixel = 0.012f;   // v116: 2배 (잘 보이게)
    // v116 로켓 가속: 처음 launchDelay초는 launchMul배, 그 뒤로 boostMul배 속도
    [HideInInspector] public float launchMul = 1f, boostMul = 1f, launchDelay = 0f;
    private float flyTime;
    private float nextTrail;
    private float lastAng;

    // 범위 폭발 설정: radius > 0 이면 그 반경으로 폭발, 아니면 프리팹 원래 설정으로 되돌림 (풀 재사용 대비)
    private bool aoeDefaultsSaved, defaultAoe;
    private float defaultRadius;
    public void ApplyAoe(float radius)
    {
        if (!aoeDefaultsSaved) { aoeDefaultsSaved = true; defaultAoe = isAreaOfEffect; defaultRadius = explosionRadius; }
        if (radius > 0f) { isAreaOfEffect = true; explosionRadius = radius; }
        else { isAreaOfEffect = defaultAoe; explosionRadius = defaultRadius; }
    }       // >0 이면 이 거리(조준점)에서 멈추고 착탄 이펙트 (플레이어 총탄)

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
        journeyTime = 0f; flyTime = 0f;
    }

    // 수동 에임(좌표 발사) 초기화
    public void InitializeManual(Vector3 aimPosition)
    {
        target = null; // 고정된 타겟이 없음
        targetPos = aimPosition;
        straightDir = (targetPos - transform.position).normalized;
        startPos = transform.position;
        journeyTime = 0f; flyTime = 0f;
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

        if (blockByWall && FortWall.Blocks(transform.position))
        {
            HitEffects.Impact(transform.position, true);
            ReturnToPool();
            return;
        }

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
        float mul = launchDelay > 0f ? (flyTime < launchDelay ? launchMul : boostMul) : 1f;
        flyTime += Time.deltaTime;
        transform.position += straightDir * speed * mul * Time.deltaTime;

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

        if (stopDistance > 0f && target == null && Vector3.Distance(startPos, transform.position) >= stopDistance)
        {
            transform.position = startPos + straightDir * stopDistance;
            if (isAreaOfEffect) { HitTarget(null); return; } // v113 로켓: 조준점에서 폭발
            HitEffects.Impact(transform.position, false);
            if (fromPlayerGun) Sfx.BulletImpact(transform.position, null);
            ReturnToPool();
            return;
        }

        if (Vector3.Distance(startPos, transform.position) > 30f)
        {
            if (isAreaOfEffect) { HitTarget(null); return; }
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
                Health.NextHitCrit = isCrit;
                Weapon.MarkPlayerGunHit(fromPlayerGun);
                directHealth.TakeDamage(attackDamage, armorPenetration, attacker);
                Weapon.MarkPlayerGunHit(false);
                KnockbackReceiver.TryApply(directHealth.transform, hitDir, knockbackDistance);
                if (hitSlowPercent > 0f) MobAI.TryApplySlow(directHealth, hitSlowPercent, hitSlowDuration);
            }
        }

        if (isAreaOfEffect)
        {
            // v109: 폭발 이펙트 (도트). 연기 고리가 폭발 범위만큼 퍼지게
            if (!string.IsNullOrEmpty(explosionFx) && PixelFx.Has(explosionFx))
            {
                Camera cam = Camera.main;
                Vector3 p = transform.position + (cam != null ? -cam.transform.forward * 0.3f : Vector3.zero);
                PixelFx.Play(explosionFx, p, Vector3.zero, explosionRadius * 2f / 56f);
                Sfx.Play(string.IsNullOrEmpty(explosionSfx) ? "Gun/Frag_Explode" : explosionSfx, 0.55f);
            }
            Collider[] colliders = Physics.OverlapSphere(transform.position, explosionRadius, targetLayer);
            HashSet<Health> damaged = new HashSet<Health>();
            if (directHealth != null) damaged.Add(directHealth); // ★ 직격 대상은 폭발 피해 중복 제외

            foreach (Collider hit in colliders)
            {
                Health areaHealth = hit.GetComponentInParent<Health>();
                if (areaHealth == null || areaHealth.IsDead || !damaged.Add(areaHealth)) continue;

                areaHealth.TakeDamage(attackDamage, armorPenetration, attacker);
                if (hitSlowPercent > 0f) MobAI.TryApplySlow(areaHealth, hitSlowPercent, hitSlowDuration);
                // 폭발은 중심에서 바깥쪽으로 밀어냄
                KnockbackReceiver.TryApply(areaHealth.transform, areaHealth.transform.position - transform.position, knockbackDistance);
            }
        }

        ReturnToPool();
    }

    private void ReturnToPool()
    {
        explosionSfx = null;
        trailFx = null;   // 풀에서 다른 무기(포탑·몹)가 재사용할 때 연기가 따라가지 않게
        launchMul = boostMul = 1f; launchDelay = 0f;
        isCrit = false;
        fromPlayerGun = false;
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