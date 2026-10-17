using UnityEngine;
using UnityEngine.AI;
using System.Collections;

// 몹 인공지능: "무엇을 노리고, 언제 어떻게 공격할지"를 결정합니다.
// 실제 공격(피해 계산·투사체 발사)은 같은 오브젝트(또는 자식)의 Weapon 컴포넌트가 실행합니다.
//
//  ■ 근거리(Melee/Multi): 접근 → 예비동작(빨갛게 번쩍) → 타격 → 쿨타임
//      플레이어를 노릴 때는 플레이어 주변 슬롯(MeleeSlotRing)에 자리를 배정받아 그 자리에서만 공격
//      (플레이어 안쪽으로 파고들거나 몹끼리 겹치지 않음, 자리가 없으면 바깥에서 대기)
//  ■ 원거리(Ranged)     : 사거리까지 접근 → 멈춰서 조준 → 발사 / 너무 가까우면 뒤로 물러남(옵션)
//  ■ 저격(aimLockTime>0): 제자리에서 붉은 조준선을 일정 시간 겨눈 뒤 확정 타격
//  ■ 공성(TowerFirst)   : 주변에 타워가 있으면 플레이어보다 타워를 먼저 공격
//
// 수치(체력·방어·이동속도·공격력·공격속도·사거리)는 Mob_Data.csv → EntityData/WeaponData 에서 옵니다.
// 행동 옵션(아래 '행동' 항목)은 [Tools/몹/1. 몹 프리팹 정리] 메뉴가 몹 코드에 맞춰 자동 설정합니다.
[RequireComponent(typeof(NavMeshAgent))]
public class MobAI : MonoBehaviour
{
    public enum MobType { Melee, Ranged, Multi }
    public enum TargetPriority { Player, TowerFirst }

    [Header("몹 설정")]
    public MobType type;
    [Tooltip("원거리 몹이 멈춰 설 거리. 0이면 사거리의 85%")]
    public float maintainDistance = 0f;
    public EntityData myData;

    [Header("행동")]
    public TargetPriority targetPriority = TargetPriority.Player;
    [Tooltip("TowerFirst일 때 이 반경 안의 타워를 먼저 노림")]
    public float towerSearchRadius = 12f;
    [Tooltip("플레이어를 쫓다가 포탑에 길이 막히면(제자리에서 못 나아가면) 그 포탑을 공격")]
    public bool attackBlockingTowers = true;
    [Tooltip("이 시간(초) 이상 막혀 있으면 길막 포탑을 공격 대상으로 전환")]
    public float blockedDelay = 0.8f;
    [Tooltip("근거리 공격 전 예비동작 시간(초). 이 시간 안에 피하면 맞지 않음")]
    public float meleeWindup = 0.35f;
    [Tooltip("0보다 크면 저격형: 이 시간(초) 동안 제자리 조준 후 확정 타격")]
    public float aimLockTime = 0f;
    [Tooltip("원거리 몹이 너무 가까워지면 뒤로 물러남")]
    public bool retreatWhenTooClose = false;
    [Tooltip("움직일수록 점점 빨라짐 (방향을 크게 틀면 다시 느려짐)")]
    public bool accelerateWhileMoving = false;
    public float accelerateDuration = 2.5f;
    [Range(0.1f, 1f)] public float startSpeedRatio = 0.35f;
    [Tooltip("멈춰서 공격할 때 목표를 향해 도는 속도")]
    public float turnSpeed = 8f;

    [Header("연출")]
    [Tooltip("몹 일러스트가 오른쪽을 보고 그려졌으면 체크 (왼쪽으로 이동 시 좌우 반전)")]
    public bool spriteFacesRight = true;
    public Color windupColor = new Color(1f, 0.35f, 0.35f, 1f);
    [Tooltip("피격 시 잠깐 바뀌는 색 (스프라이트는 색을 곱하는 방식이라 흰색은 변화가 없음)")]
    public Color hitFlashColor = new Color(1f, 0.6f, 0.6f, 1f);
    [Tooltip("피격 시 일러스트가 이 색으로 번쩍임 (몹 전용 셰이더 사용 시). 공격 예비동작(붉은색)과 구분되도록 흰색 기본")]
    public Color hitFlashOverlay = Color.white;
    [Tooltip("피격 번쩍임 진하기 (0.5 = 반투명하게 덮음)")]
    [Range(0f, 1f)] public float hitFlashStrength = 0.5f;
    public float hitFlashDuration = 0.08f;
    [Tooltip("피격 시 그림이 흔들리는 폭(미터). 0이면 흔들림 없음 (몸은 밀려나지 않고 그림만 흔들림)")]
    public float hitShakeAmount = 0.07f;
    public float hitShakeDuration = 0.15f;
    public Color aimLineColor = new Color(1f, 0.1f, 0.1f, 0.9f);

    private enum State { Chase, Attacking }

    // ★ 테스트 패널용
    public static bool AiPaused = false;            // 모든 몹의 AI 일시 정지
    public string StateLabel { get; private set; } = "대기";
    public Transform CurrentTarget => target;
    public string LastAction { get; private set; } = "-";   // 마지막 공격 결과 (타격/빗나감 등)
    public int HitCount { get; private set; }               // 이번 스폰 이후 명중 횟수
    public int MissCount { get; private set; }              // 이번 스폰 이후 빗나간 횟수
    public string SlotInfo => slotRing == null || !MeleeSlotRing.SystemEnabled || type == MobType.Ranged ? "-"
        : (slotRing.HasClaim(this) ? $"{slotRing.SlotLabel(this)} (사용 중 {slotRing.OccupiedSlots}/{slotRing.slotCount}칸)" : "배정 없음");

    // 캐싱
    private NavMeshAgent agent;
    private Health health;
    private Weapon weapon;
    private KnockbackReceiver knockback;
    private SpriteRenderer bodySprite;
    private Billboard bodyBoard;
    private Renderer bodyRenderer;       // 스프라이트가 없을 때(큐브 등) 색 변경용
    private Color bodyBaseColor = Color.white;
    private Camera cam;
    private LineRenderer aimLine;
    private MaterialPropertyBlock flashBlock;
    private bool spriteSupportsFlash;
    private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
    private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

    // 상태
    private State state = State.Chase;
    private Transform target;
    private Transform blockingTower;     // 길을 막아서 공격 중인 포탑
    private float stuckTime;
    private float lastMoveCommandTime;
    public bool IsAttackingBlocker => blockingTower != null && target == blockingTower;
    private Health targetHealth;
    private Collider[] targetColliders;
    private float nextRetargetTime;
    private float nextPathTime;
    private float lastAttackTime = -999f;
    private float baseSpeed;
    private float initialAgentSpeed;     // 프리팹에 설정된 원래 이동속도 (둔화가 누적되지 않게 보관)

    // 이동 둔화 (충격파 포탑, 제압 사격 등)
    private float slowPercent;
    private float slowUntil;
    public float SlowPercent => Time.time < slowUntil ? slowPercent : 0f;
    public float SpeedMultiplier => (1f - SlowPercent) * phaseMoveMul;

    // 보스 페이즈 등에서 주는 능력치 배율 (BossPhases가 SetPhaseModifiers로 설정)
    private float phaseMoveMul = 1f;
    private float baseAttackCooldown = -1f;
    private int baseAttackDamage = -1;
    private float stunUntil;

    // 이동속도·공격속도·공격력 배율 (1 = 기본). 풀에서 다시 나오면 1로 초기화됨
    public void SetPhaseModifiers(float moveMul, float attackSpeedMul, float damageMul)
    {
        phaseMoveMul = Mathf.Max(0.05f, moveMul);
        if (weapon != null && baseAttackCooldown > 0f)
        {
            weapon.attackCooldown = baseAttackCooldown / Mathf.Max(0.05f, attackSpeedMul);
            weapon.attackDamage = Mathf.Max(0, Mathf.RoundToInt(baseAttackDamage * damageMul));
        }
    }

    // 잠깐 행동 정지 (보스 페이즈 전환 연출 등)
    public void Stun(float seconds)
    {
        stunUntil = Mathf.Max(stunUntil, Time.time + seconds);
    }
    public bool IsStunned => Time.time < stunUntil;
    private float movingTime;
    private Vector3 lastMoveDir;
    private Coroutine attackRoutine;
    private Coroutine flashRoutine;
    private int towerMask;
    private static readonly Collider[] towerHits = new Collider[16];

    // 근접 슬롯
    private MeleeSlotRing slotRing;
    private float nextSlotTryTime;
    private bool atSlot;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        initialAgentSpeed = agent != null ? agent.speed : 3.5f;
        health = GetComponent<Health>();
        weapon = GetComponentInChildren<Weapon>();
        knockback = GetComponent<KnockbackReceiver>();

        // 몹 일러스트 = Billboard가 붙은 SpriteRenderer, 없으면 일반 렌더러(큐브)
        Billboard board = GetComponentInChildren<Billboard>(true);
        if (board != null)
        {
            bodySprite = board.GetComponent<SpriteRenderer>();
            bodyBoard = board;
            board.anchorAtFeet = true; // 탑뷰에서 발이 바닥에서 떠 보이지 않도록 발밑 기준으로 기울임
        }
        if (bodySprite != null && bodySprite.sharedMaterial != null)
            spriteSupportsFlash = bodySprite.sharedMaterial.HasProperty(FlashAmountId);
        if (bodySprite == null)
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is LineRenderer || r is ParticleSystemRenderer) continue;
                bodyRenderer = r;
                break;
            }
        }
        // 몹 일러스트는 그림자용 셰이더(Project_D/Sprite Shadow)를 쓰며, flipX와 색(color)을 그대로 지원합니다.
        if (bodySprite != null) bodyBaseColor = bodySprite.color;
        else if (bodyRenderer != null && bodyRenderer.sharedMaterial != null && bodyRenderer.sharedMaterial.HasProperty("_Color"))
            bodyBaseColor = bodyRenderer.sharedMaterial.color;

        int towerLayer = LayerMask.NameToLayer("Tower");
        towerMask = towerLayer >= 0 ? (1 << towerLayer) : 0;
    }

    // 풀에서 꺼내질 때마다 호출 → 스탯/상태 초기화
    void OnEnable()
    {
        if (myData != null)
        {
            if (health != null) health.InitStats(myData);
            if (weapon != null) weapon.InitWeapon(myData.defaultWeapon);
            baseSpeed = myData.moveSpeed > 0 ? myData.moveSpeed : initialAgentSpeed;
        }
        else
        {
            baseSpeed = initialAgentSpeed;
        }
        slowPercent = 0f;
        slowUntil = 0f;
        phaseMoveMul = 1f;
        stunUntil = 0f;

        if (weapon != null)
        {
            // 공격 시점은 MobAI가 정하므로 무기의 자동 사격은 끔
            weapon.aimType = Weapon.AimType.Manual;
            baseAttackCooldown = weapon.attackCooldown;   // 페이즈 배율의 기준값
            baseAttackDamage = weapon.attackDamage;
            // 몹 무기는 플레이어와 타워만 공격 (프리팹 설정과 무관하게 보장)
            int mask = LayerMask.GetMask("Player", "Tower");
            if (mask != 0) weapon.targetLayer = mask;
        }

        if (agent != null)
        {
            agent.speed = accelerateWhileMoving ? baseSpeed * startSpeedRatio : baseSpeed;
            agent.stoppingDistance = 0f;
            if (accelerateWhileMoving) agent.angularSpeed = 90f; // 방향 전환이 느림
        }

        if (health != null) { health.OnDamaged += HandleDamaged; health.OnDied += HandleDied; }

        state = State.Chase;
        target = null;
        blockingTower = null;
        stuckTime = 0f;
        targetHealth = null;
        movingTime = 0f;
        LastAction = "-"; HitCount = 0; MissCount = 0; StateLabel = "대기";
        lastAttackTime = Time.time - Random.Range(0f, 0.5f); // 동시에 스폰된 몹들의 공격 타이밍을 조금씩 어긋나게
        SetBodyColor(bodyBaseColor);
        if (spriteSupportsFlash) SetFlash(0f); // 풀에서 재사용될 때 번쩍임 상태 초기화
        if (aimLine != null) aimLine.enabled = false;
    }

    void OnDisable()
    {
        ReleaseSlot();
        if (health != null) { health.OnDamaged -= HandleDamaged; health.OnDied -= HandleDied; }
        attackRoutine = null;
        flashRoutine = null;
    }

    void Update()
    {
        if (agent == null || !agent.isOnNavMesh) return;
        if (health != null && health.IsDead) { ReleaseSlot(); StopMoving(); return; }
        if (AiPaused) { StopMoving(); StateLabel = "AI 정지"; return; }
        if (IsStunned) { StopMoving(); StateLabel = "경직"; return; }
        if (!accelerateWhileMoving) agent.speed = baseSpeed * SpeedMultiplier; // 둔화 반영

        UpdateTarget();
        UpdateSpriteFacing();

        if (target == null) { StopMoving(); StateLabel = "대기 (목표 없음)"; return; }
        if (state == State.Attacking) return; // 공격 동작 중에는 코루틴이 제어

        float dist = SurfaceDistance();
        float reach = weapon != null ? weapon.attackRange : 1f;

        CheckBlockedByTower(dist, reach);

        if (type == MobType.Ranged)
            UpdateRanged(dist, reach);
        else
            UpdateMelee(dist, reach);
    }

    // ------------------------------------------------------------------
    // 타겟 선정
    // ------------------------------------------------------------------
    private void UpdateTarget()
    {
        bool invalid = target == null || !target.gameObject.activeInHierarchy || (targetHealth != null && targetHealth.IsDead)
                       || PlayerSkills.IsCloaked(target); // 은신한 플레이어는 바로 놓침
        if (!invalid && Time.time < nextRetargetTime) return;
        nextRetargetTime = Time.time + 0.5f;

        Transform newTarget = null;

        // 길을 막은 포탑: 부서지거나 멀어질 때까지 우선 공격
        if (blockingTower != null)
        {
            Health bh = blockingTower.GetComponent<Health>();
            bool gone = !blockingTower.gameObject.activeInHierarchy || (bh != null && bh.IsDead)
                        || FlatDistance(transform.position, blockingTower.position) > towerSearchRadius * 0.5f;
            if (gone) blockingTower = null;
            else newTarget = blockingTower;
        }

        if (newTarget == null && targetPriority == TargetPriority.TowerFirst && towerMask != 0)
            newTarget = FindNearestTower();

        if (newTarget == null && GameManager.Instance != null && GameManager.Instance.playerTransform != null)
        {
            Transform p = GameManager.Instance.playerTransform;
            if (!PlayerSkills.IsCloaked(p)) newTarget = p;
            else if (towerMask != 0) newTarget = FindNearestTower(); // 은신 중: 가까운 포탑을 노림 (없으면 멈춤)
        }

        if (newTarget != target)
        {
            ReleaseSlot();
            target = newTarget;
            // 플레이어가 목표면 플레이어 주변 슬롯 사용 (근접형만)
            slotRing = null;
            if (target != null && type != MobType.Ranged && target.GetComponent<PlayerController>() != null)
            {
                slotRing = target.GetComponent<MeleeSlotRing>();
                if (slotRing == null) slotRing = target.gameObject.AddComponent<MeleeSlotRing>();
            }
            targetHealth = target != null ? target.GetComponentInParent<Health>() : null;
            targetColliders = target != null ? target.GetComponentsInChildren<Collider>() : null;
        }
    }

    // 플레이어를 쫓는 중인데 제자리에서 못 나아가고, 바로 앞에 포탑이 있으면 → 그 포탑을 공격
    private void CheckBlockedByTower(float dist, float reach)
    {
        if (!attackBlockingTowers || towerMask == 0 || blockingTower != null || target == null) { stuckTime = 0f; return; }
        if (target.GetComponent<PlayerController>() == null) { stuckTime = 0f; return; } // 플레이어 추격 중일 때만

        bool tryingToMove = Time.time - lastMoveCommandTime < 0.3f && !agent.pathPending && dist > reach + 0.3f;
        float speedNow = new Vector2(agent.velocity.x, agent.velocity.z).magnitude;
        bool slow = speedNow < Mathf.Max(0.3f, agent.speed * 0.35f);
        // 경로가 끊긴 경우(포탑이 길을 완전히 막아 내비메쉬가 갈라짐)도 막힌 것으로 봄
        bool partial = agent.hasPath && agent.pathStatus != UnityEngine.AI.NavMeshPathStatus.PathComplete;

        if (tryingToMove && (slow || partial)) stuckTime += Time.deltaTime;
        else stuckTime = Mathf.Max(0f, stuckTime - Time.deltaTime * 2f);
        if (stuckTime < blockedDelay) return;
        stuckTime = 0f;

        Transform tower = FindTowerAhead(target.position);
        if (tower == null) return;
        blockingTower = tower;
        nextRetargetTime = 0f; // 바로 대상 교체
        LastAction = $"길막 포탑 공격 → {tower.name.Replace("(Clone)", "")}";
    }

    // 플레이어 방향 앞쪽(±75도), 가까운 거리 안의 포탑
    private Transform FindTowerAhead(Vector3 goal)
    {
        float radius = agent.radius + 2f;
        int count = Physics.OverlapSphereNonAlloc(transform.position, radius, towerHits, towerMask, QueryTriggerInteraction.Ignore);
        Vector3 toGoal = goal - transform.position; toGoal.y = 0f;
        Transform best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Transform root = towerHits[i].transform.root;
            if (!root.gameObject.activeInHierarchy) continue;
            Health h = root.GetComponent<Health>();
            if (h == null || h.IsDead) continue;
            Vector3 to = towerHits[i].ClosestPoint(transform.position) - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.0001f && toGoal.sqrMagnitude > 0.0001f && Vector3.Angle(toGoal, to) > 75f) continue;
            float sqr = to.sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = root; }
        }
        return best;
    }

    private Transform FindNearestTower()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, towerSearchRadius, towerHits, towerMask, QueryTriggerInteraction.Ignore);
        Transform best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Transform root = towerHits[i].transform.root; // 타워 본체 (부품 콜라이더의 최상위)
            if (!root.gameObject.activeInHierarchy) continue;
            float sqr = (root.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = root; }
        }
        return best;
    }

    // 내 몸 표면 ~ 대상 몸 표면 사이의 수평 거리 (몸집이 큰 몹/타워도 사거리 판정이 자연스럽도록)
    private float SurfaceDistance()
    {
        Vector3 myPos = transform.position;
        Vector3 closest = target.position;

        if (targetColliders != null && targetColliders.Length > 0)
        {
            float best = float.MaxValue;
            foreach (Collider c in targetColliders)
            {
                if (c == null || !c.enabled) continue;
                Vector3 p = c.bounds.ClosestPoint(myPos);
                float d = (new Vector2(p.x - myPos.x, p.z - myPos.z)).sqrMagnitude;
                if (d < best) { best = d; closest = p; }
            }
        }

        float flat = new Vector2(closest.x - myPos.x, closest.z - myPos.z).magnitude;
        return Mathf.Max(0f, flat - agent.radius);
    }

    private Vector3 TargetCenter()
    {
        if (targetColliders != null)
            foreach (Collider c in targetColliders)
                if (c != null && c.enabled) return c.bounds.center;
        return target.position + Vector3.up * 0.5f;
    }

    // ------------------------------------------------------------------
    // 근거리
    // ------------------------------------------------------------------
    private void UpdateMelee(float dist, float reach)
    {
        if (slotRing != null && MeleeSlotRing.SystemEnabled && slotRing.isActiveAndEnabled)
        {
            UpdateMeleeWithSlot(dist, reach);
            return;
        }
        ReleaseSlot();

        if (dist > reach)
        {
            MoveTo(target.position);
            StateLabel = "추적";
            return;
        }

        StopMoving();
        FaceTarget();
        StateLabel = "공격 대기 (쿨타임)";

        if (Time.time >= lastAttackTime + AttackCooldown())
            attackRoutine = StartCoroutine(MeleeAttackRoutine(reach));
    }

    // 슬롯 방식 근접: 가까워지면 자리를 배정받고 → 자리로 이동 → 자리에서 공격
    private void UpdateMeleeWithSlot(float dist, float reach)
    {
        float standoff = Mathf.Min(slotRing.gap, reach * 0.8f); // 자리에서 반드시 공격이 닿도록
        float ringR = slotRing.RingRadius(agent.radius, standoff);

        if (!slotRing.HasClaim(this))
        {
            if (dist > slotRing.engageRange)
            {
                MoveTo(target.position);
                StateLabel = "추적";
                return;
            }
            if (Time.time >= nextSlotTryTime)
            {
                nextSlotTryTime = Time.time + 0.25f;
                slotRing.TryClaim(this, transform.position, agent.radius, ringR);
            }
            if (!slotRing.HasClaim(this))
            {
                // 자리가 가득 참 → 바깥쪽에서 대기 (자리가 비면 들어감)
                Vector3 wait = slotRing.WaitPosition(transform.position, ringR + agent.radius * 2f + 0.6f);
                if (FlatDistance(transform.position, wait) > 0.5f) MoveTo(wait);
                else { StopMoving(); FaceTarget(); }
                StateLabel = "자리 대기 (슬롯 가득)";
                return;
            }
            atSlot = false;
        }
        else if (dist > slotRing.engageRange + 3f)
        {
            // 플레이어가 멀리 도망감 → 자리 반납하고 추적
            ReleaseSlot();
            MoveTo(target.position);
            StateLabel = "추적";
            return;
        }
        else if (Time.time >= nextSlotTryTime)
        {
            nextSlotTryTime = Time.time + 0.5f;
            slotRing.ReclaimIfFar(this, transform.position, agent.radius, ringR);
        }

        Vector3 slotPos = slotRing.GetSlotPosition(this, ringR);
        float toSlot = FlatDistance(transform.position, slotPos);
        // 도착/출발 판정에 여유를 둬서 제자리에서 움찔거리지 않도록
        if (atSlot && toSlot > 0.5f) atSlot = false;
        else if (!atSlot && toSlot < 0.25f) atSlot = true;

        string label = slotRing.SlotLabel(this);
        if (!atSlot)
        {
            MoveTo(slotPos);
            StateLabel = $"슬롯 {label}으로 이동";
        }
        else
        {
            StopMoving();
            FaceTarget();
            StateLabel = $"슬롯 {label} · 공격 대기 (쿨타임)";
        }

        // 자리에 (거의) 도착했고 사거리 안이면 공격
        if (toSlot < 0.6f && dist <= reach && Time.time >= lastAttackTime + AttackCooldown())
        {
            StopMoving();
            attackRoutine = StartCoroutine(MeleeAttackRoutine(reach));
        }
    }

    private void ReleaseSlot()
    {
        if (slotRing != null) slotRing.Release(this);
        atSlot = false;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        return new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }

    private IEnumerator MeleeAttackRoutine(float reach)
    {
        state = State.Attacking;
        lastAttackTime = Time.time;

        // 예비동작: 빨갛게 변함 (플레이어가 보고 피할 수 있는 시간)
        StateLabel = "예비동작";
        SetBodyColor(windupColor);
        float t = 0f;
        while (t < meleeWindup)
        {
            t += Time.deltaTime;
            if (target != null) FaceTarget();
            yield return null;
        }
        SetBodyColor(bodyBaseColor);

        // 아직 사거리 안(약간의 여유 포함)에 있으면 타격
        if (target != null && target.gameObject.activeInHierarchy && SurfaceDistance() <= reach + 0.3f && weapon != null && !PlayerSkills.IsCloaked(target))
        {
            Vector3 dir = target.position - transform.position;
            weapon.ApplyDamage(targetHealth != null ? targetHealth.transform : target, dir);
            LastAction = "근접 타격 명중"; HitCount++;
        }
        else { LastAction = "근접 공격 빗나감 (회피됨)"; MissCount++; }

        lastAttackTime = Time.time;
        state = State.Chase;
        attackRoutine = null;
    }

    // ------------------------------------------------------------------
    // 원거리
    // ------------------------------------------------------------------
    private void UpdateRanged(float dist, float reach)
    {
        float keep = maintainDistance > 0f ? Mathf.Min(maintainDistance, reach) : reach * 0.85f;

        if (dist > reach)
        {
            MoveTo(target.position);
            StateLabel = "추적 (사거리 밖)";
            return;
        }

        if (retreatWhenTooClose && dist < reach * 0.4f)
        {
            StateLabel = "후퇴 (너무 가까움)";
            Vector3 away = transform.position - target.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            MoveTo(transform.position + away.normalized * 3f);
            return;
        }

        if (dist > keep)
        {
            // 사거리 안이지만 원하는 거리보다 멀면 조금 더 접근하면서도 사격 가능 (회전은 이동 방향이 담당)
            MoveTo(target.position);
            StateLabel = "접근하며 사격";
        }
        else
        {
            StopMoving();
            FaceTarget();
            StateLabel = "거리 유지 · 사격";
        }

        if (Time.time < lastAttackTime + AttackCooldown()) return;

        if (aimLockTime > 0f)
            attackRoutine = StartCoroutine(SniperRoutine(reach));
        else
            FireAt(TargetCenter());
    }

    private void FireAt(Vector3 aimPoint)
    {
        if (weapon == null) return;
        lastAttackTime = Time.time;
        weapon.ManualAttackCommand(aimPoint);
        LastAction = "원거리 발사";
    }

    // 저격: 제자리에서 조준선을 겨누다가 확정 타격
    private IEnumerator SniperRoutine(float reach)
    {
        state = State.Attacking;
        lastAttackTime = Time.time;
        StopMoving();

        LineRenderer line = GetAimLine();
        line.enabled = true;
        StateLabel = "저격 조준 중";

        float t = 0f;
        while (t < aimLockTime)
        {
            if (target == null || !target.gameObject.activeInHierarchy) break;
            t += Time.deltaTime;
            FaceTarget();

            Vector3 from = transform.position + Vector3.up * 0.3f;
            line.SetPosition(0, from);
            line.SetPosition(1, TargetCenter());
            // 발사 직전일수록 굵고 진하게
            float k = t / aimLockTime;
            line.widthMultiplier = Mathf.Lerp(0.01f, 0.05f, k);
            Color c = aimLineColor; c.a = Mathf.Lerp(0.25f, 1f, k);
            line.startColor = line.endColor = c;
            yield return null;
        }
        line.enabled = false;

        if (target != null && target.gameObject.activeInHierarchy && weapon != null && t >= aimLockTime && !PlayerSkills.IsCloaked(target))
        {
            Vector3 dir = target.position - transform.position;
            weapon.ApplyDamage(targetHealth != null ? targetHealth.transform : target, dir); // 확정 타격
            LastAction = "저격 명중"; HitCount++;
        }

        lastAttackTime = Time.time;
        state = State.Chase;
        attackRoutine = null;
    }

    private LineRenderer GetAimLine()
    {
        if (aimLine != null) return aimLine;
        GameObject go = new GameObject("AimLine");
        go.transform.SetParent(transform, false);
        aimLine = go.AddComponent<LineRenderer>();
        aimLine.useWorldSpace = true;
        aimLine.positionCount = 2;
        aimLine.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        aimLine.receiveShadows = false;
        aimLine.enabled = false;
        return aimLine;
    }

    // ------------------------------------------------------------------
    // 이동 둔화 (포탑 효과)
    // ------------------------------------------------------------------
    // percent: 0.15 = 15% 느려짐. 여러 둔화가 겹치면 가장 강한 것만 적용되고, 지속시간은 늘어남
    public void ApplySlow(float percent, float duration)
    {
        percent = Mathf.Clamp(percent, 0f, 0.9f);
        if (percent <= 0f || duration <= 0f) return;
        float now = Time.time;
        if (now >= slowUntil) slowPercent = percent;          // 둔화가 끝난 상태면 새로 시작
        else slowPercent = Mathf.Max(slowPercent, percent);    // 진행 중이면 더 강한 쪽
        slowUntil = Mathf.Max(slowUntil, now + duration);
    }

    // 콜라이더(자식 포함)에서 몹을 찾아 둔화 적용
    public static bool TryApplySlow(Component hit, float percent, float duration)
    {
        if (hit == null || percent <= 0f) return false;
        MobAI mob = hit.GetComponentInParent<MobAI>();
        if (mob == null || !mob.isActiveAndEnabled) return false;
        mob.ApplySlow(percent, duration);
        return true;
    }

    // ------------------------------------------------------------------
    // 이동 / 회전 / 연출
    // ------------------------------------------------------------------
    private float AttackCooldown()
    {
        return weapon != null ? Mathf.Max(0.1f, weapon.attackCooldown) : 1f;
    }

    private void MoveTo(Vector3 destination)
    {
        agent.isStopped = false;
        lastMoveCommandTime = Time.time;
        if (Time.time >= nextPathTime) // 경로 계산은 0.2초마다 (매 프레임 계산 방지)
        {
            agent.SetDestination(destination);
            nextPathTime = Time.time + 0.2f;
        }

        if (accelerateWhileMoving)
        {
            Vector3 v = agent.velocity; v.y = 0f;
            if (v.sqrMagnitude > 0.01f)
            {
                // 방향을 크게 틀면(60도 이상) 가속이 초기화됨
                if (lastMoveDir != Vector3.zero && Vector3.Angle(lastMoveDir, v) > 60f) movingTime = 0f;
                lastMoveDir = v.normalized;
            }
            movingTime += Time.deltaTime;
            float k = Mathf.Clamp01(movingTime / Mathf.Max(0.1f, accelerateDuration));
            agent.speed = baseSpeed * Mathf.Lerp(startSpeedRatio, 1f, k) * SpeedMultiplier;
        }
    }

    private void StopMoving()
    {
        if (agent != null && agent.isOnNavMesh && !agent.isStopped)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
        movingTime = 0f;
    }

    private void FaceTarget()
    {
        if (target == null) return;
        Vector3 look = target.position - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), Time.deltaTime * turnSpeed);
    }

    // 화면 기준으로 왼쪽을 향하면 일러스트 좌우 반전
    private void UpdateSpriteFacing()
    {
        if (bodySprite == null) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 dir = agent.velocity.sqrMagnitude > 0.05f
            ? agent.velocity
            : (target != null ? target.position - transform.position : transform.forward);
        float side = Vector3.Dot(dir, cam.transform.right);
        if (Mathf.Abs(side) < 0.05f) return; // 정면/후면 이동 중엔 그대로 유지

        bool facingLeft = side < 0f;
        bodySprite.flipX = spriteFacesRight ? facingLeft : !facingLeft;
    }

    // 처치 보상 골드 (체력이 0이 되어 죽었을 때만. 웨이브 정리·테스트 제거로 사라질 때는 없음)
    private void HandleDied()
    {
        ReleaseSlot();
        GoldManager gm = GoldManager.Instance;
        if (gm == null) return;
        int reward = gm.GetKillReward(myData);
        gm.Add(reward);
        LastAction = $"처치됨 (+{reward} 골드)";
    }

    private void HandleDamaged(float amount)
    {
        if (!isActiveAndEnabled) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(HitFlashRoutine());
        if (bodyBoard != null) bodyBoard.Shake(hitShakeAmount, hitShakeDuration); // 타격감: 그림만 흔들림
    }

    // 피격: 흰색으로 번쩍 (공격 예비동작의 붉은색과 구분)
    private IEnumerator HitFlashRoutine()
    {
        if (spriteSupportsFlash)
        {
            SetFlash(hitFlashStrength);
            yield return new WaitForSeconds(hitFlashDuration);
            SetFlash(0f);
        }
        else
        {
            // 이미지가 없는 몹(큐브)은 기존처럼 색으로 표시
            SetBodyColor(hitFlashColor);
            yield return new WaitForSeconds(hitFlashDuration);
            SetBodyColor(state == State.Attacking && aimLockTime <= 0f ? windupColor : bodyBaseColor);
        }
        flashRoutine = null;
    }

    private void SetFlash(float amount)
    {
        if (bodySprite == null) return;
        if (flashBlock == null) flashBlock = new MaterialPropertyBlock();
        // 기존 값(Billboard가 넣는 발 위치 등)은 유지하고 번쩍임 값만 바꿈
        bodySprite.GetPropertyBlock(flashBlock);
        flashBlock.SetFloat(FlashAmountId, Mathf.Max(0f, amount));
        flashBlock.SetColor(FlashColorId, hitFlashOverlay);
        bodySprite.SetPropertyBlock(flashBlock);
    }

    private void SetBodyColor(Color c)
    {
        if (bodySprite != null) bodySprite.color = c;
        else if (bodyRenderer != null && bodyRenderer.material.HasProperty("_Color")) bodyRenderer.material.color = c;
    }

    private void OnDrawGizmosSelected()
    {
        Weapon w = weapon != null ? weapon : GetComponentInChildren<Weapon>();
        if (w != null)
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, w.attackRange);
        }
        if (targetPriority == TargetPriority.TowerFirst)
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, towerSearchRadius);
        }
    }
}
