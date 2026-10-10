using UnityEngine;

public class TurretAI : MonoBehaviour
{
    [Header("타워 설정")]
    public EntityData myData;             // 포탑의 스탯 데이터 (Mob_Data.csv에서 임포트한 데이터)
    public Transform turretHead;          // 좌우로 회전할 '포탑' 자식 오브젝트
    public float rotationSpeed = 10f;     // 회전 속도
    public float fireAngleThreshold = 5f; // 몇 도 이내로 조준되었을 때 사격할지 (오차 허용 범위)

    private Weapon weapon;
    private Transform currentTarget;
    private float checkRate = 0.2f;       // 0.2초마다 타겟 갱신 (성능 최적화)
    private float nextCheckTime;

    void Awake()
    {
        // 3중 구조(몸통 -> 포탑 -> 무기) 중 하위에 있는 Weapon 컴포넌트를 찾아옵니다.
        weapon = GetComponentInChildren<Weapon>();
    }

    void OnEnable()
    {
        if (weapon != null)
        {
            // 데이터 주입 (데이터가 없으면 무기 프리팹의 기본값 사용)
            if (myData != null) weapon.InitWeapon(myData.defaultWeapon);

            // ★ 핵심: 포탑이 목표를 바라본 후 직접 쏘게 만들기 위해 강제로 Manual(수동) 모드로 변경합니다.
            //   (데이터가 없어도 적용 → 무기가 회전 없이 혼자 자동 사격하는 일 방지)
            weapon.aimType = Weapon.AimType.Manual;
        }

        // ★ 포탑 체력 초기화 (없으면 체력 0 상태라 첫 피격에 바로 파괴됨)
        if (myData != null && TryGetComponent(out Health health))
        {
            health.InitStats(myData);
        }
    }

    // 회전 축: turretHead 미지정 시 자기 자신 (NullReference 방지)
    private Transform Head => turretHead != null ? turretHead : transform;

    void Update()
    {
        FindTarget();
        AimAndShoot();
    }

    // 사거리 내 가장 가까운 적 탐색
    void FindTarget()
    {
        if (Time.time < nextCheckTime) return;
        nextCheckTime = Time.time + checkRate;

        if (weapon == null) return;

        // Weapon에 세팅된 사거리와 타겟 레이어를 활용하여 겹치는 적(Collider)들을 찾습니다.
        Collider[] hits = Physics.OverlapSphere(transform.position, weapon.attackRange, weapon.targetLayer);

        float shortestDistance = Mathf.Infinity;
        Transform nearestEnemy = null;

        foreach (Collider hit in hits)
        {
            float distance = (transform.position - hit.transform.position).sqrMagnitude; // sqrMagnitude로 연산 속도 최적화
            if (distance < shortestDistance)
            {
                shortestDistance = distance;
                nearestEnemy = hit.transform;
            }
        }
        currentTarget = nearestEnemy;
    }

    // 목표를 향해 회전하고 정조준 시 사격명령 하달
    void AimAndShoot()
    {
        // 타겟이 풀로 반환(비활성화)되었으면 해제
        if (currentTarget != null && !currentTarget.gameObject.activeInHierarchy) currentTarget = null;
        if (currentTarget == null || weapon == null) return;

        // 1. 타겟 방향 벡터 계산 (포탑이 위아래로 까딱거리지 않게 Y축 회전만 적용)
        Transform head = Head;
        Vector3 dirToTarget = currentTarget.position - head.position;
        dirToTarget.y = 0;

        if (dirToTarget.sqrMagnitude < 0.01f) return;

        // 2. 포탑 머리(TurretHead)를 부드럽게 회전 (Slerp)
        Quaternion targetRotation = Quaternion.LookRotation(dirToTarget);
        head.rotation = Quaternion.Slerp(head.rotation, targetRotation, Time.deltaTime * rotationSpeed);

        // 3. 조준 각도 계산 및 사격
        // 현재 포탑이 바라보는 방향(forward)과 타겟 방향 간의 각도 차이를 잽니다.
        float angleToTarget = Vector3.Angle(head.forward, dirToTarget);

        // 오차 범위(5도) 안으로 들어오면 Weapon의 수동 사격 로직을 호출!
        if (angleToTarget <= fireAngleThreshold)
        {
            weapon.ManualAttackCommand(currentTarget.position);
        }
    }
}