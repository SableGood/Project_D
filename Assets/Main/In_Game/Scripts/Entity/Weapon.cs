using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Weapon : MonoBehaviour
{
    public enum FireMode { StraightFast, RapidBullet, ParabolicShell, HomingMissile, Hitscan }
    public enum AimType { Auto, Manual }

    [Header("발사 방식 및 속성")]
    public AimType aimType = AimType.Auto;
    public FireMode fireMode = FireMode.Hitscan;
    public GameObject projectilePrefab;
    public LayerMask targetLayer;

    [Header("발사 위치 (총구)")]
    [Tooltip("탄이 나가는 위치. 비어 있으면 무기 위치 기준 기본 오프셋에서 발사합니다. (몹/타워 무기 호환용)")]
    public Transform muzzle;

    [Header("타겟팅 최적화 설정")]
    public int maxTargetCapacity = 20;

    [Header("무기 스펙 (기본/런타임 가변)")]
    public float attackRange = 10f;
    public float attackCooldown = 0.5f;
    public int attackDamage = 10;

    [Header("플레이어 전용 스펙")]
    public PlayerWeaponData playerWepData; // 플레이어 무기 프리팹에 미리 연결됨 (임포터가 자동 지정)
    public int currentAmmo;
    public bool isReloading = false;

    [Header("히트스캔 궤적 표시 (플레이어 무기)")]
    public bool showTracer = true;
    public float tracerDuration = 0.05f;
    public float tracerWidth = 0.03f;
    public Color tracerColor = new Color(1f, 0.9f, 0.5f, 1f);

    private float lastAttackTime = -999f;
    private Collider[] hitColliders;

    // 플레이어 데이터에서 읽어오는 값 (몹/타워는 기본값 = 기존 동작과 동일)
    private int pelletCount = 1;
    private float spreadAngle = 0f;
    private bool canPierce = false;
    private float projectileSpeedOverride = 0f;
    private float moveSpreadAngle = 0f;     // 이동 중 추가 탄퍼짐
    private float knockbackDistance = 0f;   // 명중 시 넉백 거리
    private float armorPenetration = 0f;    // 방어구 무시 (방어력에서 이만큼 빼고 계산)
    private bool isOwnerMoving = false;     // PlayerWeaponManager가 매 프레임 알려줌

    // 한 번 발사(산탄 포함)에 같은 대상이 여러 번 넉백되지 않도록 기록
    private readonly HashSet<Health> knockedThisShot = new HashSet<Health>();
    // 관통 레이 하나에 같은 대상이 여러 콜라이더로 중복 피격되지 않도록 기록
    private readonly HashSet<Health> hitThisRay = new HashSet<Health>();

    // 궤적(트레이서) 표시용
    private static Material tracerMaterial;
    private readonly List<LineRenderer> tracers = new List<LineRenderer>();
    private int tracerIndex;
    private Coroutine tracerRoutine;

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

    // 2. 플레이어 전용 초기화 (CSV 값이 전부 여기서 적용됩니다)
    public void InitPlayerWeapon(PlayerWeaponData data)
    {
        if (data == null) return;
        this.playerWepData = data;

        this.attackDamage = Mathf.RoundToInt(data.damage);
        this.attackRange = data.range > 0 ? data.range : this.attackRange;
        this.attackCooldown = data.fireRate > 0 ? (1f / data.fireRate) : 0.5f;
        this.currentAmmo = data.maxAmmo;
        this.isReloading = false;

        this.pelletCount = Mathf.Max(1, data.projectileCount);
        this.spreadAngle = Mathf.Max(0f, data.spread);
        this.canPierce = data.canPierce;
        this.projectileSpeedOverride = data.projectileSpeed;
        this.moveSpreadAngle = Mathf.Max(0f, data.moveSpread);
        this.knockbackDistance = Mathf.Max(0f, data.knockback);
        this.armorPenetration = Mathf.Max(0f, data.armorPenetration);

        // Fire Mode(CSV)가 실제 발사 방식을 결정합니다.
        if (data.IsProjectile)
        {
            this.fireMode = FireMode.StraightFast; // 플레이어 투사체는 직선 비행
            if (data.projectilePrefab != null) this.projectilePrefab = data.projectilePrefab;
            if (this.projectilePrefab == null)
                Debug.LogWarning($"[{data.weaponID}] Fire Mode가 Projectile인데 투사체 프리팹이 없습니다.");
        }
        else
        {
            this.fireMode = FireMode.Hitscan;
        }
    }

    // 소유자(플레이어)가 이동 중인지 → 이동 탄퍼짐 적용 여부
    public void SetOwnerMoving(bool moving)
    {
        isOwnerMoving = moving;
    }

    // 현재 실제로 적용되는 탄퍼짐 (HUD 크로스헤어 등에서 활용 가능)
    public float CurrentSpread => spreadAngle + (isOwnerMoving ? moveSpreadAngle : 0f);

    void Update()
    {
        if (aimType == AimType.Auto && Time.time >= lastAttackTime + attackCooldown)
        {
            AutoAttackUpdate();
        }
    }

    // 수동 사격 (플레이어/포탑용)
    public void ManualAttackCommand(Vector3 targetPosition)
    {
        if (isReloading) return; // 장전 중 사격 불가

        if (Time.time >= lastAttackTime + attackCooldown)
        {
            // 탄창 시스템 적용 (장탄수가 0보다 큰 무기만 적용, 0 = 무한)
            if (playerWepData != null && playerWepData.maxAmmo > 0)
            {
                if (currentAmmo <= 0)
                {
                    StartReload();
                    return;
                }
                currentAmmo--; // 총알 소모
            }

            PerformAttack(null, targetPosition);
            lastAttackTime = Time.time;
        }
    }

    // 재장전 시작 (코루틴을 무기 자신에서 돌려, 무기 교체 시 함께 정리되도록 함)
    public void StartReload()
    {
        if (isReloading || playerWepData == null || playerWepData.maxAmmo <= 0) return;
        if (currentAmmo >= playerWepData.maxAmmo) return;
        StartCoroutine(ReloadCoroutine());
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
    // 아래부터는 몹 타겟팅 및 실제 데미지 적용 로직입니다.
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
        tracerIndex = 0;
        knockedThisShot.Clear();

        // 투사체 갯수만큼 발사 (산탄총 등). 몹/타워는 1발.
        int count = Mathf.Max(1, pelletCount);
        for (int i = 0; i < count; i++)
        {
            if (fireMode == FireMode.Hitscan) FireHitscan(targetTransform, manualTargetPosition);
            else FireProjectile(targetTransform, manualTargetPosition);
        }

        if (tracerIndex > 0)
        {
            if (tracerRoutine != null) StopCoroutine(tracerRoutine);
            tracerRoutine = StartCoroutine(HideTracersAfterDelay());
        }
    }

    // 발사 위치: 총구(muzzle)가 있으면 그곳, 없으면 기존 방식의 고정 오프셋
    private Vector3 GetFireOrigin(bool isHitscan)
    {
        if (muzzle != null) return muzzle.position;
        return isHitscan
            ? transform.position + Vector3.up * 1.0f
            : transform.position + transform.forward * 1.0f + Vector3.up * 0.5f;
    }

    // 탄퍼짐: 수평(Y축 기준)으로 ±spread/2 범위에서 무작위 회전
    private Vector3 ApplySpread(Vector3 direction)
    {
        float spread = CurrentSpread;
        if (spread <= 0f) return direction;
        float half = spread * 0.5f;
        return Quaternion.AngleAxis(Random.Range(-half, half), Vector3.up) * direction;
    }

    private void FireHitscan(Transform targetTransform, Vector3 manualTargetPosition)
    {
        Vector3 aimPos = targetTransform != null ? targetTransform.position + Vector3.up * 0.5f : manualTargetPosition;
        Vector3 fireOrigin = GetFireOrigin(true);
        Vector3 direction = ApplySpread((aimPos - fireOrigin).normalized);
        Vector3 endPoint = fireOrigin + direction * attackRange;

        if (canPierce)
        {
            // 관통: 사거리 안의 모든 대상에게 피해
            hitThisRay.Clear();
            RaycastHit[] hits = Physics.RaycastAll(fireOrigin, direction, attackRange, targetLayer, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                Health hp = h.transform.GetComponentInParent<Health>();
                if (hp == null || !hitThisRay.Add(hp)) continue; // 같은 대상 중복 피격 방지
                ApplyDamage(hp, direction);
            }
        }
        else if (Physics.Raycast(fireOrigin, direction, out RaycastHit hit, attackRange, targetLayer, QueryTriggerInteraction.Ignore))
        {
            ApplyDamage(hit.transform, direction);
            endPoint = hit.point;
        }

        Debug.DrawLine(fireOrigin, endPoint, Color.green, 0.5f);
        if (showTracer && playerWepData != null) ShowTracer(fireOrigin, endPoint);
    }

    private void FireProjectile(Transform targetTransform, Vector3 manualTargetPosition)
    {
        if (projectilePrefab == null) return;

        if (PoolManager.Instance == null)
        {
            Debug.LogError("PoolManager 인스턴스가 씬에 존재하지 않습니다!");
            return;
        }

        Vector3 spawnPos = GetFireOrigin(false);

        Vector3 aimPos = manualTargetPosition;
        if (targetTransform == null && spreadAngle > 0f)
        {
            aimPos = spawnPos + ApplySpread(manualTargetPosition - spawnPos);
        }

        GameObject projObj = PoolManager.Instance.Spawn(projectilePrefab, spawnPos, Quaternion.identity);
        if (projObj == null) return;

        Projectile projectile = projObj.GetComponent<Projectile>();
        if (projectile != null)
        {
            ConfigureProjectile(projectile);
            if (projectileSpeedOverride > 0f) projectile.speed = projectileSpeedOverride;
            projectile.armorPenetration = armorPenetration;
            projectile.knockbackDistance = knockbackDistance;

            if (targetTransform != null) projectile.InitializeAuto(targetTransform);
            else projectile.InitializeManual(aimPos);
        }
    }

    // 기존 호출부 호환용 (넉백 방향 없음)
    public void ApplyDamage(Transform target)
    {
        ApplyDamage(target, Vector3.zero);
    }

    public void ApplyDamage(Transform target, Vector3 hitDirection)
    {
        if (target == null) return;
        ApplyDamage(target.GetComponentInParent<Health>(), hitDirection);
    }

    // 피해(방어력/방어구 무시 반영) + 넉백(한 번 발사에 대상당 1회)
    private void ApplyDamage(Health targetHealth, Vector3 hitDirection)
    {
        if (targetHealth == null || targetHealth.IsDead) return;

        targetHealth.TakeDamage(attackDamage, armorPenetration);

        if (knockbackDistance > 0f && hitDirection != Vector3.zero && knockedThisShot.Add(targetHealth))
        {
            KnockbackReceiver.TryApply(targetHealth.transform, hitDirection, knockbackDistance);
        }
    }

    private void ConfigureProjectile(Projectile projectile)
    {
        projectile.attackDamage = this.attackDamage;
        projectile.targetLayer = this.targetLayer;
        projectile.armorPenetration = 0f;
        projectile.knockbackDistance = 0f;

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

    // =========================================================
    // 히트스캔 궤적 (짧게 번쩍이는 선)
    // =========================================================

    private void ShowTracer(Vector3 from, Vector3 to)
    {
        LineRenderer lr = GetTracer(tracerIndex++);
        lr.SetPosition(0, from);
        lr.SetPosition(1, to);
        lr.enabled = true;
    }

    private LineRenderer GetTracer(int index)
    {
        while (tracers.Count <= index)
        {
            if (tracerMaterial == null) tracerMaterial = new Material(Shader.Find("Sprites/Default"));

            GameObject go = new GameObject("Tracer");
            go.transform.SetParent(transform, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.sharedMaterial = tracerMaterial;
            lr.widthMultiplier = tracerWidth;
            lr.startColor = tracerColor;
            lr.endColor = new Color(tracerColor.r, tracerColor.g, tracerColor.b, 0.2f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            tracers.Add(lr);
        }
        return tracers[index];
    }

    private IEnumerator HideTracersAfterDelay()
    {
        yield return new WaitForSeconds(tracerDuration);
        foreach (LineRenderer lr in tracers) if (lr != null) lr.enabled = false;
        tracerRoutine = null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        if (muzzle != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(muzzle.position, 0.05f);
        }
    }
}
