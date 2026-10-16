using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 무기 그림(WeaponVisual, 100)이 자리를 잡은 뒤에 총구 화염·궤적을 총구에 다시 붙이도록 더 늦게 실행
[DefaultExecutionOrder(110)]
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
    [Tooltip("켜면 플레이어 무기가 아니어도 궤적 표시 (저격 포탑 등)")]
    public bool alwaysShowTracer = false;

    [Header("총구 화염 (발사 순간 번쩍)")]
    [Tooltip("켜면 쏠 때마다 총구에 짧게 화염이 보임 (포탑은 TurretAI가 켬)")]
    public bool muzzleFlash = false;
    [Tooltip("비워두면 코드로 만든 도트 화염 그림을 사용")]
    public Sprite muzzleFlashSprite;
    public float muzzleFlashSize = 0.7f;     // 월드 크기(미터)
    public float muzzleFlashDuration = 0.05f;
    public Color muzzleFlashColor = Color.white;

    [Header("총구 섬광 빛 + 재장전 연기 (플레이어 총기만)")]
    [Tooltip("쏠 때 총구 주변과 발밑 바닥이 잠깐 밝아짐 (실제 조명 대신 가벼운 그림이라 성능 부담 거의 없음)")]
    public bool muzzleGlow = true;
    public Color muzzleGlowColor = new Color(1f, 0.84f, 0.5f, 1f);
    [Range(0f, 1f)] public float muzzleGlowAlpha = 0.4f;   // 총구 주변 빛무리 진하기
    [Range(0f, 1f)] public float groundGlowAlpha = 0.28f;  // 바닥 빛 진하기
    public float groundGlowSize = 2.4f;                    // 바닥 빛 지름 (미터)
    public float muzzleGlowDuration = 0.08f;
    [Tooltip("사격 후 재장전할 때 총구에서 연기가 가늘게 피어오름")]
    public bool muzzleSmoke = true;
    public float smokeDuration = 1.4f;

    [Header("명중 시 이동 둔화 (포탑 제압 사격 등)")]
    [Tooltip("0.2 = 명중한 몹이 20% 느려짐. 0이면 없음 (TurretAI가 자동 설정)")]
    [Range(0f, 0.9f)] public float hitSlowPercent = 0f;
    public float hitSlowDuration = 1f;

    [Header("플레이어 총기: 조준점(커서)에서 탄이 멈춤")]
    [Tooltip("켜면 플레이어 총탄이 커서(조준점)까지만 날아가고, 그 자리나 도중의 포탑·바닥에 작은 회색 착탄 이펙트")]
    public bool stopAtAimPoint = true;

    // 플레이어 무기가 한 번 발사할 때마다 (반동·탄피 연출용, PlayerWeaponManager가 구독)
    public event System.Action<Weapon> Fired;
    // 마지막 발사 방향 (수평 기준이 아닌 실제 방향)
    public Vector3 LastFireDirection { get; private set; } = Vector3.forward;
    // 총기인지 (탄창이 있는 무기 = 총. 근접무기는 반동·탄피·착탄 없음)
    public bool IsGun => playerWepData != null && playerWepData.UsesAmmo;

    private float lastAttackTime = -999f;
    private int shotsSinceReload;      // 연기 양 (많이 쏠수록 진하고 오래)
    private float lastShotTime = -999f;
    private float reloadStartTime;
    private Coroutine reloadRoutine;
    private float shellStartTime, shellTime = 1f;

    // 한 발씩 장전하는 무기 (산탄총): 재장전 = 한 발 넣을 때마다 장탄수 +1, 장전 중에도 남은 탄이 있으면 바로 사격
    public bool ShellReload => playerWepData != null && playerWepData.maxAmmo > 0 &&
                               ((playerWepData.role ?? "").Contains("산탄") || playerWepData.projectileCount > 1);
    // 한 발 넣는 데 걸리는 시간 = CSV 재장전속도 ÷ 장탄수 (빈 총을 끝까지 채우는 시간이 CSV 값과 같음)
    public float ShellLoadTime => playerWepData != null ? Mathf.Max(0.1f, playerWepData.reloadTime / Mathf.Max(1, playerWepData.maxAmmo)) : 0.5f;

    // 재장전 진행도 0~1 (HUD 표시용). 한 발씩 장전하는 무기는 '지금 넣는 한 발'의 진행도
    public float ReloadProgress
    {
        get
        {
            if (!isReloading || playerWepData == null || playerWepData.reloadTime <= 0f) return 0f;
            if (ShellReload) return Mathf.Clamp01((Time.time - shellStartTime) / Mathf.Max(0.01f, shellTime));
            return Mathf.Clamp01((Time.time - reloadStartTime) / playerWepData.reloadTime);
        }
    }
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
    private readonly List<Vector3> tracerStartLocal = new List<Vector3>(); // 총구 기준 궤적 시작점 (움직여도 총구를 따라감)
    private Vector3 flashLocal;                                            // 총구 기준 화염 위치
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

    // 이동하면서 쏴도 화염·궤적이 쏜 자리에 남지 않고 총구를 따라가게 (매 프레임 마지막에 다시 붙임)
    void LateUpdate()
    {
        UpdateMuzzleGlow();
        if (muzzle == null) return;
        if (flashRenderer != null && flashRenderer.enabled)
        {
            flashRenderer.transform.position = muzzle.TransformPoint(flashLocal);
            Camera c = Camera.main;
            if (c != null)
            {
                float roll = Vector3.SignedAngle(c.transform.up, Vector3.ProjectOnPlane(flashRenderer.transform.up, c.transform.forward), c.transform.forward);
                flashRenderer.transform.rotation = c.transform.rotation * Quaternion.Euler(0f, 0f, roll);
            }
        }
        for (int i = 0; i < tracers.Count && i < tracerStartLocal.Count; i++)
        {
            LineRenderer lr = tracers[i];
            if (lr != null && lr.enabled) lr.SetPosition(0, muzzle.TransformPoint(tracerStartLocal[i]));
        }
    }

    void Update()
    {
        if (flashRenderer != null && flashRenderer.enabled && Time.time >= flashHideTime) flashRenderer.enabled = false;

        if (aimType == AimType.Auto && Time.time >= lastAttackTime + attackCooldown)
        {
            AutoAttackUpdate();
        }
    }

    // 수동 사격 (플레이어/포탑용)
    public void ManualAttackCommand(Vector3 targetPosition)
    {
        if (isReloading)
        {
            // 산탄총: 한 발씩 넣는 중이라도 남은 탄이 있으면 장전을 멈추고 바로 사격
            if (ShellReload && currentAmmo > 0 && Time.time >= lastAttackTime + attackCooldown) CancelReload();
            else return; // 장전 중 사격 불가
        }

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

    // 포탑 전용: 쿨타임·탄창 검사 없이 즉시 1회 발사 (발사 간격은 TurretAI가 관리 → 여러 총열 번갈아 사격)
    public void FireAt(Vector3 targetPosition)
    {
        PerformAttack(null, targetPosition);
        lastAttackTime = Time.time;
    }

    // 재장전 시작 (코루틴을 무기 자신에서 돌려, 무기 교체 시 함께 정리되도록 함)
    public void StartReload()
    {
        if (isReloading || playerWepData == null || playerWepData.maxAmmo <= 0) return;
        if (currentAmmo >= playerWepData.maxAmmo) return;
        reloadRoutine = StartCoroutine(ShellReload ? ShellReloadCoroutine() : ReloadCoroutine());
    }

    // 장전 중단 (산탄총: 장전 중 사격). 이미 넣은 탄은 그대로
    public void CancelReload()
    {
        if (!isReloading) return;
        if (reloadRoutine != null) StopCoroutine(reloadRoutine);
        reloadRoutine = null;
        isReloading = false;
    }

    // 한 발씩 장전: 한 발 넣을 때마다 장탄수 +1 → 가득 차면 끝
    private IEnumerator ShellReloadCoroutine()
    {
        if (isReloading || playerWepData == null || playerWepData.maxAmmo <= 0) yield break;
        isReloading = true;
        reloadStartTime = Time.time;
        if (muzzleSmoke && IsGun && shotsSinceReload > 0 && Time.time - lastShotTime < 4f) StartCoroutine(SmokeRoutine());
        shotsSinceReload = 0;

        while (currentAmmo < playerWepData.maxAmmo)
        {
            shellTime = ShellLoadTime;
            shellStartTime = Time.time;
            yield return new WaitForSeconds(shellTime);
            currentAmmo = Mathf.Min(playerWepData.maxAmmo, currentAmmo + 1);
        }
        isReloading = false;
        reloadRoutine = null;
    }

    public IEnumerator ReloadCoroutine()
    {
        if (isReloading || playerWepData == null || playerWepData.maxAmmo <= 0) yield break;

        isReloading = true;
        reloadStartTime = Time.time;
        if (muzzleSmoke && IsGun && shotsSinceReload > 0 && Time.time - lastShotTime < 4f) StartCoroutine(SmokeRoutine());
        shotsSinceReload = 0;

        yield return new WaitForSeconds(playerWepData.reloadTime);

        currentAmmo = playerWepData.maxAmmo;
        isReloading = false;
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
        Vector3 aimForFx = targetTransform != null ? targetTransform.position : manualTargetPosition;
        if (muzzleFlash) ShowMuzzleFlash(aimForFx);
        Vector3 fd = aimForFx - GetFireOrigin(fireMode == FireMode.Hitscan);
        if (fd.sqrMagnitude > 0.0001f) LastFireDirection = fd.normalized;
        if (playerWepData != null) { Fired?.Invoke(this); shotsSinceReload++; lastShotTime = Time.time; }
        if (muzzleFlash && muzzleGlow && IsGun) ShowMuzzleGlow();

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

        // 판정 레이의 시작점: 플레이어 무기는 '몸 중심(총구 높이)'에서 시작.
        // 총구에서 시작하면, 바짝 붙은 몹 안으로 총구가 들어갔을 때 레이가 몹 내부에서 출발해 맞지 않음.
        // 몸 중심에서 쏘면 몸과 총구 사이에 있는 몹도 정상적으로 맞음 (총알 궤적 그림은 그대로 총구에서 나감)
        Vector3 rayStart = fireOrigin;
        Transform owner = playerWepData != null ? OwnerBody() : null;
        if (owner != null) rayStart = new Vector3(owner.position.x, fireOrigin.y, owner.position.z);
        Vector3 toAim = aimPos - rayStart;
        if (toAim.sqrMagnitude < 0.0001f) toAim = aimPos - fireOrigin;
        Vector3 direction = ApplySpread(toAim.normalized);
        float muzzleDist = Vector3.Distance(rayStart, fireOrigin); // 이보다 가까운 지점 = 총구 뒤쪽 (궤적을 그리지 않음)

        // 플레이어 총기: 커서(조준점)까지만 날아감. 도중에 포탑·바닥·벽에 막히면 그 자리에서 멈춤
        bool stopAtAim = stopAtAimPoint && IsGun && targetTransform == null;
        float maxDist = attackRange + muzzleDist;
        bool blocked = false;
        if (stopAtAim)
        {
            maxDist = Mathf.Min(attackRange + muzzleDist, Vector3.Distance(rayStart, aimPos));
            // 가장 가까운 장애물 (플레이어 자신과 든 무기의 콜라이더는 제외)
            RaycastHit[] obstacles = Physics.RaycastAll(rayStart, direction, maxDist, ObstacleMask(), QueryTriggerInteraction.Ignore);
            foreach (RaycastHit o in obstacles)
            {
                if (owner != null && o.collider.transform.IsChildOf(owner)) continue;
                if (o.distance >= maxDist) continue;
                maxDist = o.distance;
                blocked = true;
            }
        }
        Vector3 endPoint = rayStart + direction * maxDist;
        bool hitTarget = false;

        // 시작점이 이미 몹 몸 안이면(몸끼리 겹침) 레이로는 잡히지 않으므로 따로 확인
        int insideCount = Physics.OverlapSphereNonAlloc(rayStart, 0.05f, insideHits, targetLayer, QueryTriggerInteraction.Ignore);

        if (canPierce)
        {
            // 관통: 사거리 안의 모든 대상에게 피해
            hitThisRay.Clear();
            for (int i = 0; i < insideCount; i++)
            {
                Health hp = insideHits[i].GetComponentInParent<Health>();
                if (hp != null && hitThisRay.Add(hp)) ApplyDamage(hp, direction);
            }
            RaycastHit[] hits = Physics.RaycastAll(rayStart, direction, maxDist, targetLayer, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                Health hp = h.transform.GetComponentInParent<Health>();
                if (hp == null || !hitThisRay.Add(hp)) continue; // 같은 대상 중복 피격 방지
                ApplyDamage(hp, direction);
            }
        }
        else if (insideCount > 0)
        {
            ApplyDamage(insideHits[0].transform, direction);
            endPoint = fireOrigin;
            hitTarget = true;
        }
        else if (Physics.Raycast(rayStart, direction, out RaycastHit hit, maxDist, targetLayer, QueryTriggerInteraction.Ignore))
        {
            ApplyDamage(hit.transform, direction);
            endPoint = hit.distance > muzzleDist ? hit.point : fireOrigin; // 총구 뒤에서 맞았으면 궤적 없이 그 자리 타격
            hitTarget = true;
        }
        if (!hitTarget && maxDist <= muzzleDist) endPoint = fireOrigin; // 총구에 닿기 전(몸과 총구 사이)에 막힘 → 궤적 없음

        // 몹에 맞지 않은 탄: 멈춘 자리(커서·포탑·바닥)에 작은 회색 착탄
        if (stopAtAim && !hitTarget) HitEffects.Impact(endPoint, blocked);

        Debug.DrawLine(fireOrigin, endPoint, Color.green, 0.5f);
        if (showTracer && (IsGun || alwaysShowTracer)) ShowTracer(fireOrigin, endPoint); // 근접무기는 탄 궤적 없음 (휘두르기 연출)
    }

    // 이 무기를 든 플레이어의 몸 (밀착 사격 판정의 기준점)
    private Transform ownerBody;
    private readonly Collider[] insideHits = new Collider[8];
    private Transform OwnerBody()
    {
        if (ownerBody == null)
        {
            PlayerController pc = GetComponentInParent<PlayerController>();
            ownerBody = pc != null ? pc.transform : null;
        }
        return ownerBody;
    }

    // 착탄 판정에 쓰는 '막는 물체' 레이어: 대상(몹)·플레이어·Ignore Raycast를 뺀 모든 것 (포탑, 바닥, 지형)
    private int obstacleMask = 0;
    private int ObstacleMask()
    {
        if (obstacleMask == 0)
        {
            int ignore = targetLayer.value | (1 << 2);
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) ignore |= 1 << playerLayer;
            obstacleMask = ~ignore;
        }
        return obstacleMask;
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

            // 플레이어 총탄: 커서(조준점)에서 멈추고 착탄 이펙트
            if (stopAtAimPoint && IsGun && targetTransform == null)
                projectile.stopDistance = Mathf.Min(attackRange, Vector3.Distance(spawnPos, manualTargetPosition));
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
        if (hitSlowPercent > 0f) MobAI.TryApplySlow(targetHealth, hitSlowPercent, hitSlowDuration);

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
        projectile.hitSlowPercent = hitSlowPercent;
        projectile.hitSlowDuration = hitSlowDuration;
        projectile.stopDistance = 0f;

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
    // 총구 화염 (도트 스타일, 카메라를 바라보는 작은 그림)
    // =========================================================
    private SpriteRenderer flashRenderer;
    private float flashHideTime;
    private static Sprite generatedFlash;
    private static Material flashMaterial;

    private void ShowMuzzleFlash(Vector3 aimPos)
    {
        Vector3 origin = GetFireOrigin(fireMode == FireMode.Hitscan);
        Vector3 dir = aimPos - origin; dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) origin += dir.normalized * 0.08f; // 총구 바로 앞

        if (flashRenderer == null)
        {
            // 부모 없이 생성 (포탑 부품의 비균일 크기에 찌그러지지 않게). 무기가 사라지면 함께 삭제
            GameObject go = new GameObject("MuzzleFlash");
            go.layer = 2; // Ignore Raycast
            flashRenderer = go.AddComponent<SpriteRenderer>();
            if (flashMaterial == null) flashMaterial = new Material(Shader.Find("Sprites/Default"));
            flashRenderer.sharedMaterial = flashMaterial;
            flashRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            flashRenderer.receiveShadows = false;
            flashRenderer.sortingOrder = 10;
        }

        Sprite sprite = muzzleFlashSprite != null ? muzzleFlashSprite : GetGeneratedFlash();
        flashRenderer.sprite = sprite;
        flashRenderer.color = muzzleFlashColor;

        Transform t = flashRenderer.transform;
        Camera cam = Camera.main;
        t.position = origin;
        if (muzzle != null) flashLocal = muzzle.InverseTransformPoint(origin);
        float roll = Random.Range(0, 4) * 90f + (Random.value < 0.5f ? 0f : 45f); // 도트가 깨지지 않게 45도 단위
        t.rotation = (cam != null ? cam.transform.rotation : Quaternion.identity) * Quaternion.Euler(0f, 0f, roll);
        float spriteSize = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
        t.localScale = Vector3.one * (muzzleFlashSize * Random.Range(0.85f, 1.15f) / Mathf.Max(0.0001f, spriteSize));

        flashRenderer.enabled = true;
        flashHideTime = Time.time + muzzleFlashDuration;
    }

    // 16x16 도트 화염 (흰 중심 → 노랑 → 주황 테두리, 십자 + 대각선 불꽃)
    void OnDisable()
    {
        if (flashRenderer != null) flashRenderer.enabled = false; // 포탑이 풀로 돌아갈 때 화염이 남지 않게
        if (haloRenderer != null) haloRenderer.enabled = false;
        if (groundRenderer != null) groundRenderer.enabled = false;
    }

    void OnDestroy()
    {
        if (flashRenderer != null) Destroy(flashRenderer.gameObject);
        if (haloRenderer != null) Destroy(haloRenderer.gameObject);
        if (groundRenderer != null) Destroy(groundRenderer.gameObject);
    }

    // =========================================================
    // 총구 섬광 빛 (가짜 조명): 총구 주변 빛무리 + 발밑 바닥에 둥근 빛
    //  - 진짜 Light를 쓰면 바닥·포탑 셰이더가 빛 하나당 한 번 더 그려야 해서, 반투명 그림 2장으로 흉내냄
    //  - 도트 느낌이 나도록 빛이 3단으로 끊긴 원
    // =========================================================
    private SpriteRenderer haloRenderer, groundRenderer;
    private float glowStartTime = -999f;
    private float glowFloorY;
    private static Sprite glowSprite;
    private static int glowIgnoreMask;
    private static bool glowMaskReady;

    // 0~1: 방금 쏜 섬광의 남은 밝기 (1인칭 무기 그림을 밝히는 데 사용)
    public float MuzzleGlow01
    {
        get
        {
            if (!muzzleGlow || muzzleGlowDuration <= 0f) return 0f;
            float t = (Time.time - glowStartTime) / muzzleGlowDuration;
            return t >= 1f || t < 0f ? 0f : (1f - t) * (1f - t);
        }
    }

    private void ShowMuzzleGlow()
    {
        if (flashRenderer == null) return;
        if (glowSprite == null) glowSprite = MakeGlowSprite();
        if (haloRenderer == null) haloRenderer = CreateGlowRenderer("MuzzleGlow");
        if (groundRenderer == null) groundRenderer = CreateGlowRenderer("MuzzleGroundGlow");

        Vector3 p = flashRenderer.transform.position;
        if (!glowMaskReady)
        {
            glowMaskReady = true;
            int pl = LayerMask.NameToLayer("Player");
            glowIgnoreMask = ~((1 << 2) | (pl >= 0 ? 1 << pl : 0));
        }
        glowFloorY = Physics.Raycast(p + Vector3.up * 0.05f, Vector3.down, out RaycastHit hit, 6f, glowIgnoreMask, QueryTriggerInteraction.Ignore)
            ? hit.point.y : p.y - 1.2f;
        glowStartTime = Time.time;
        haloRenderer.enabled = groundRenderer.enabled = true;
        UpdateMuzzleGlow();
    }

    private SpriteRenderer CreateGlowRenderer(string name)
    {
        GameObject go = new GameObject(name);
        go.layer = 2; // Ignore Raycast
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        if (flashMaterial == null) flashMaterial = new Material(Shader.Find("Sprites/Default"));
        sr.sharedMaterial = flashMaterial;
        sr.sprite = glowSprite;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        sr.sortingOrder = 9; // 화염(10) 바로 뒤
        sr.enabled = false;
        return sr;
    }

    private void UpdateMuzzleGlow()
    {
        if (haloRenderer == null || !haloRenderer.enabled) return;
        float k = MuzzleGlow01;
        if (k <= 0f || flashRenderer == null)
        {
            haloRenderer.enabled = false;
            if (groundRenderer != null) groundRenderer.enabled = false;
            return;
        }
        Camera c = Camera.main;
        Vector3 p = muzzle != null ? muzzle.TransformPoint(flashLocal) : flashRenderer.transform.position;
        float spriteSize = Mathf.Max(0.0001f, glowSprite.bounds.size.x);

        // 총구 빛무리: 화염보다 2.6배 크고 옅게, 화염 바로 뒤
        Transform ht = haloRenderer.transform;
        ht.position = p + (c != null ? c.transform.forward * 0.02f : Vector3.zero);
        ht.rotation = c != null ? c.transform.rotation : Quaternion.identity;
        ht.localScale = Vector3.one * (muzzleFlashSize * 2.6f / spriteSize);
        Color hc = muzzleGlowColor; hc.a = muzzleGlowAlpha * k; haloRenderer.color = hc;

        // 바닥 빛: 총구 바로 아래 바닥에 눕힘 (높이가 높을수록 옅게)
        Transform gt = groundRenderer.transform;
        float height = Mathf.Max(0f, p.y - glowFloorY);
        gt.position = new Vector3(p.x, glowFloorY + 0.02f, p.z);
        gt.rotation = Quaternion.Euler(90f, 0f, 0f);
        gt.localScale = Vector3.one * (groundGlowSize / spriteSize);
        Color gc = muzzleGlowColor; gc.a = groundGlowAlpha * k * Mathf.Clamp01(1.6f - height * 0.5f); groundRenderer.color = gc;
    }

    // 32x32 둥근 빛 (흰색, 바깥으로 갈수록 3단으로 옅어짐)
    private static Sprite MakeGlowSprite()
    {
        const int N = 32;
        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (N * 0.5f);
            float a = r < 0.32f ? 1f : r < 0.62f ? 0.55f : r < 0.95f ? 0.22f : 0f;
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        Sprite s = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        s.name = "GeneratedMuzzleGlow";
        return s;
    }

    // 재장전: 총구에서 가는 연기가 잠시 피어오름 (많이 쏠수록 진하고 오래)
    private IEnumerator SmokeRoutine()
    {
        float heat = Mathf.Clamp01(shotsSinceReload / 15f);
        float dur = smokeDuration * Mathf.Lerp(0.6f, 1f, heat);
        float end = Time.time + dur;
        while (Time.time < end)
        {
            if (muzzle != null)
            {
                float scale = muzzleFlashSize / 0.32f; // 1인칭에서는 무기 크기에 맞춰 작게
                float k = (end - Time.time) / dur;     // 1 → 0 (점점 가늘어짐)
                Vector3 p = muzzle.TransformPoint(flashLocal);
                Vector3 v = new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(0.22f, 0.36f), Random.Range(-0.06f, 0.06f)) * scale;
                float alpha = 0.38f * Mathf.Lerp(0.35f, 1f, k) * Mathf.Lerp(0.6f, 1f, heat);
                HitEffects.Smoke(p, v, Random.Range(0.09f, 0.14f) * scale, Random.Range(0.9f, 1.3f), alpha, scale);
            }
            yield return new WaitForSeconds(Random.Range(0.06f, 0.1f));
        }
    }

    private static Sprite GetGeneratedFlash()
    {
        if (generatedFlash != null) return generatedFlash;
        const int N = 16;
        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        Color clear = new Color(0, 0, 0, 0);
        Color white = new Color(1f, 1f, 0.92f, 1f), yellow = new Color(1f, 0.85f, 0.25f, 1f), orange = new Color(1f, 0.5f, 0.1f, 1f);
        float c = (N - 1) * 0.5f;
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = Mathf.Abs(x - c), dy = Mathf.Abs(y - c);
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            bool cross = (dx < 1f && dy < 7.6f) || (dy < 1f && dx < 7.6f);           // 십자 불꽃
            bool diag = Mathf.Abs(dx - dy) < 0.8f && r < 6.2f;                       // 대각선 짧은 불꽃
            Color col = clear;
            if (r < 2.2f) col = white;
            else if (r < 3.6f || (cross && r < 5f)) col = yellow;
            else if (cross || diag) col = orange;
            tex.SetPixel(x, y, col);
        }
        tex.Apply();
        generatedFlash = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        generatedFlash.name = "GeneratedMuzzleFlash";
        return generatedFlash;
    }

    // =========================================================
    // 히트스캔 궤적 (짧게 번쩍이는 선)
    // =========================================================

    private void ShowTracer(Vector3 from, Vector3 to)
    {
        LineRenderer lr = GetTracer(tracerIndex++);
        lr.SetPosition(0, from);
        while (tracerStartLocal.Count < tracers.Count) tracerStartLocal.Add(Vector3.zero);
        if (muzzle != null) tracerStartLocal[tracerIndex - 1] = muzzle.InverseTransformPoint(from);
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
