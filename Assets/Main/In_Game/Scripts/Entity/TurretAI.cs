using UnityEngine;

// 공격형 포탑 AI: 사거리 안의 몹을 찾아 포탑 머리(turretHead)를 돌리고, 정조준되면 사격합니다.
//  - 수치(사거리·공격력·공격속도·체력·방어력)는 Mob_Data.csv → EntityData/WeaponData 에서 옵니다.
//  - 자식에 Weapon이 여러 개면(쌍열 기관총 등) 번갈아 쏩니다. 발사 간격 = 1 / 공격속도
//  - 타겟 선정: 가까운 몹 우선(기본) / 먼 몹 우선(저격 포탑: "멀리 있는 몹 부터 공격")
//  - 제압 사격: 명중한 몹을 잠깐 느리게 함 (hitSlowPercent > 0)
//  * [Tools/포탑/3. 포탑 기능 자동 설정] 메뉴가 포탑 코드에 맞춰 이 값들을 채웁니다.
public class TurretAI : MonoBehaviour
{
    public enum TargetPriority { Nearest, Farthest }

    [Header("타워 설정")]
    public EntityData myData;             // 포탑의 스탯 데이터 (Mob_Data.csv에서 임포트한 데이터)
    public Transform turretHead;          // 좌우로 회전할 '포탑' 자식 오브젝트
    public float rotationSpeed = 10f;     // 회전 속도 (클수록 빠르게 따라감)
    public float fireAngleThreshold = 5f; // 몇 도 이내로 조준되었을 때 사격할지 (오차 허용 범위)

    [Header("타겟 선정")]
    public TargetPriority targetPriority = TargetPriority.Nearest;

    [Header("데이터 보완")]
    [Tooltip("CSV의 공격력이 비어 있을 때(0) 대신 쓰는 공격력. CSV에 값을 넣으면 그 값이 우선합니다.")]
    public float fallbackDamage = 10f;

    [Header("제압 사격 (명중 시 이동 둔화)")]
    [Tooltip("0.3 = 명중한 몹이 30% 느려짐. 0이면 없음")]
    [Range(0f, 0.9f)] public float hitSlowPercent = 0f;
    public float hitSlowDuration = 1f;

    private Weapon[] weapons;
    private int nextWeaponIndex;
    private float nextFireTime;
    private Collider currentTarget;
    private Health currentTargetHealth;
    private Vector3 localAimAxis = Vector3.forward; // 포탑 머리 기준 총구 방향
    private float checkRate = 0.2f;                 // 0.2초마다 타겟 갱신 (성능 최적화)
    private float nextCheckTime;
    private static readonly Collider[] hits = new Collider[64];

    public Transform CurrentTarget => currentTarget != null ? currentTarget.transform : null;

    void Awake()
    {
        // 3중 구조(몸통 -> 포탑 -> 무기) 중 하위에 있는 Weapon 컴포넌트를 모두 찾아옵니다.
        weapons = GetComponentsInChildren<Weapon>(true);
    }

    void OnEnable()
    {
        int mobMask = LayerMask.GetMask("Mob");
        foreach (Weapon w in weapons)
        {
            if (w == null) continue;
            // 데이터 주입 (데이터가 없으면 무기 프리팹의 기본값 사용)
            if (myData != null) w.InitWeapon(myData.defaultWeapon);
            if (w.attackDamage <= 0)
            {
                w.attackDamage = Mathf.RoundToInt(fallbackDamage);
                Debug.LogWarning($"[포탑] {name}: CSV 공격력이 비어 있어 임시 공격력 {w.attackDamage}을 사용합니다. (Mob_Data.csv에 값을 넣어주세요)");
            }

            // ★ 포탑이 목표를 바라본 후 직접 쏘게 만들기 위해 강제로 Manual(수동) 모드로 변경합니다.
            //   (데이터가 없어도 적용 → 무기가 회전 없이 혼자 자동 사격하는 일 방지)
            w.aimType = Weapon.AimType.Manual;
            if (w.targetLayer.value == 0 && mobMask != 0) w.targetLayer = mobMask;
            w.hitSlowPercent = hitSlowPercent;
            w.hitSlowDuration = hitSlowDuration;
        }

        // ★ 포탑 체력 초기화 (없으면 체력 0 상태라 첫 피격에 바로 파괴됨)
        if (myData != null && TryGetComponent(out Health health))
        {
            health.InitStats(myData);
        }

        localAimAxis = ComputeAimAxis();
        currentTarget = null;
        currentTargetHealth = null;
        nextWeaponIndex = 0;
        nextFireTime = 0f;
    }

    // 회전 축: turretHead 미지정 시 자기 자신 (NullReference 방지)
    private Transform Head => turretHead != null ? turretHead : transform;

    private Weapon MainWeapon => (weapons != null && weapons.Length > 0) ? weapons[0] : null;

    void Update()
    {
        FindTarget();
        AimAndShoot();
    }

    // 총구들의 평균 위치로 '포탑이 바라보는 방향'을 계산 (모델의 앞이 +Z가 아니어도 정확히 조준)
    private Vector3 ComputeAimAxis()
    {
        Transform head = Head;
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (Weapon w in weapons)
        {
            if (w == null || w.muzzle == null) continue;
            sum += head.InverseTransformPoint(w.muzzle.position);
            count++;
        }
        if (count > 0)
        {
            Vector3 avg = sum / count;
            avg.y = 0f;
            if (avg.sqrMagnitude > 0.01f) return avg.normalized;
        }
        return Vector3.forward;
    }

    // 사거리 내 몹 탐색 (가까운 순 / 먼 순)
    void FindTarget()
    {
        if (Time.time < nextCheckTime && IsTargetValid()) return;
        nextCheckTime = Time.time + checkRate;

        Weapon weapon = MainWeapon;
        if (weapon == null) return;

        // Weapon에 세팅된 사거리와 타겟 레이어를 활용하여 겹치는 적(Collider)들을 찾습니다.
        int count = Physics.OverlapSphereNonAlloc(transform.position, weapon.attackRange, hits, weapon.targetLayer, QueryTriggerInteraction.Ignore);

        bool farthest = targetPriority == TargetPriority.Farthest;
        float best = farthest ? -1f : Mathf.Infinity;
        Collider bestCol = null;

        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i];
            if (c == null) continue;
            Health h = c.GetComponentInParent<Health>();
            if (h != null && h.IsDead) continue;

            float d = (transform.position - c.transform.position).sqrMagnitude; // sqrMagnitude로 연산 속도 최적화
            if (farthest ? d > best : d < best)
            {
                best = d;
                bestCol = c;
            }
        }
        currentTarget = bestCol;
        currentTargetHealth = bestCol != null ? bestCol.GetComponentInParent<Health>() : null;
    }

    private bool IsTargetValid()
    {
        if (currentTarget == null || !currentTarget.enabled || !currentTarget.gameObject.activeInHierarchy) return false;
        if (currentTargetHealth != null && currentTargetHealth.IsDead) return false;
        return true;
    }

    // 목표를 향해 회전하고 정조준 시 사격
    void AimAndShoot()
    {
        // 타겟이 풀로 반환(비활성화)되었거나 죽었으면 해제
        if (!IsTargetValid()) { currentTarget = null; currentTargetHealth = null; return; }
        if (weapons == null || weapons.Length == 0) return;

        Vector3 aimPoint = currentTarget.bounds.center;

        // 1. 수평 방향만 계산 (포탑이 위아래로 까딱거리지 않게 Y축 회전만 적용)
        Transform head = Head;
        Vector3 dirToTarget = aimPoint - head.position;
        dirToTarget.y = 0;
        if (dirToTarget.sqrMagnitude < 0.01f) return;

        Vector3 barrelDir = head.TransformDirection(localAimAxis);
        barrelDir.y = 0;

        // 2. 포탑 머리를 부드럽게 회전 (현재 방향 → 목표 방향의 각도 차이만큼 Y축으로 돌림)
        float delta = Vector3.SignedAngle(barrelDir, dirToTarget, Vector3.up);
        float step = delta * Mathf.Clamp01(Time.deltaTime * rotationSpeed);
        head.Rotate(Vector3.up, step, Space.World);

        // 3. 남은 각도가 오차 범위 안이면 사격 (여러 총열이면 번갈아)
        if (Mathf.Abs(delta - step) > fireAngleThreshold) return;
        if (Time.time < nextFireTime) return;

        Weapon w = weapons[nextWeaponIndex % weapons.Length];
        nextWeaponIndex = (nextWeaponIndex + 1) % weapons.Length;
        if (w == null) return;

        w.FireAt(aimPoint);
        nextFireTime = Time.time + Mathf.Max(0.05f, MainWeapon.attackCooldown);
    }

    private void OnDrawGizmosSelected()
    {
        Weapon w = MainWeapon != null ? MainWeapon : GetComponentInChildren<Weapon>();
        if (w == null) return;
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, w.attackRange);
    }
}
