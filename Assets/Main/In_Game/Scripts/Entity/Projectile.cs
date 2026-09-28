using UnityEngine;

public class Projectile : MonoBehaviour
{
    // 투사체 비행 방식을 정의하는 열거형 (Enum)
    public enum ProjectileType { Homing, Straight, Parabolic }

    [Header("투사체 모드 및 스펙 설정")]
    public ProjectileType projectileType = ProjectileType.Homing; // 투사체 비행 모드 (기본값: 유도형)
    public float speed = 15.0f;                                   // 투사체 이동 속도

    // 내부 연산에 사용되는 프라이빗 변수들
    private Transform target;      // 유도형일 때 추적할 대상의 트랜스포메이션
    private Vector3 straightDir;   // 직선형일 때 발사 순간 고정되는 이동 방향
    private Vector3 startPos;      // 포물선형 또는 거리 계산을 위한 최초 시작 위치
    private Vector3 targetPos;     // 포물선형이 도달해야 할 목표 지점 좌표
    private float journeyTime = 0f;// 포물선 이동 시 진행률을 나타내는 시간 변수

    // 무기(Tower/Player)에서 발사될 때 호출되는 초기화 함수
    public void Initialize(Transform targetTransform)
    {
        target = targetTransform; // 전달받은 타겟 정보 저장

        if (target != null)
        {
            // 대상의 중심점(Y축 +0.5 오프셋)을 계산하여 발밑으로 파묻히거나 솟구치는 현상 방지
            targetPos = target.position + Vector3.up * 0.5f;
            // 직선형으로 날아갈 방향 벡터 미리 계산
            straightDir = (targetPos - transform.position).normalized;
        }
        // 투사체가 생성된 최초 위치 저장
        startPos = transform.position;
    }

    // 유니티 생명주기 함수: 매 프레임마다 호출됨
    void Update()
    {
        // 설정된 투사체 타입에 따라 알맞은 비행 함수 실행
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
    }

    // 1. 완전 유도 방식 (타겟의 실시간 위치를 끝까지 추적)
    private void FlyHoming()
    {
        // 추적 중인 타겟이 게임상에서 사라졌다면 투사체도 즉시 파괴
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }

        // 타겟의 실시간 위치에 오프셋을 더해 목표 좌표 갱신
        Vector3 currentTargetPos = target.position + Vector3.up * 0.5f;
        // 현재 위치에서 목표 좌표까지의 방향 벡터 계산
        Vector3 dir = (currentTargetPos - transform.position).normalized;

        // 속도와 시간에 맞춰 투사체 위치 이동
        transform.position += dir * speed * Time.deltaTime;

        // 투사체의 앞부분이 실제 이동 방향을 바라보도록 회전값 적용
        if (dir != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(dir);
        }

        // 투사체와 타겟 사이의 거리가 0.5미터 미만으로 좁혀지면 명중으로 간주
        if (Vector3.Distance(transform.position, currentTargetPos) < 0.5f)
        {
            HitTarget();
        }
    }

    // 2. 직선형 방식 (발사 순간의 방향으로 곧장 날아감)
    private void FlyStraight()
    {
        // 사전에 계산된 고정 방향으로 직진 이동
        transform.position += straightDir * speed * Time.deltaTime;

        // 투사체가 진행 방향을 바라보도록 회전
        if (straightDir != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(straightDir);
        }

        // 직선형 탄환도 비행 도중 타겟과의 거리를 검사하여 명중 판정 수행
        if (target != null)
        {
            float distanceToTarget = Vector3.Distance(transform.position, target.position + Vector3.up * 0.5f);
            if (distanceToTarget < 1.0f) // 탄환과 적의 거리가 1미터 이내로 가까워지면 명중
            {
                HitTarget();
                return;
            }
        }

        // 타겟이 없거나 최대 사거리(30미터)를 초과하여 날아가면 메모리 관리를 위해 자동 소멸
        if (Vector3.Distance(startPos, transform.position) > 30f)
        {
            Destroy(gameObject);
        }
    }

    // 3. 느린 포물선 방식 (박격포 곡사포 형태)
    private void FlyParabolic()
    {
        // 시간에 따라 포물선 진행률 증가 (속도 계수 반영)
        journeyTime += Time.deltaTime * (speed * 0.1f);

        // 시작점과 목표점 사이를 선형 보간(Lerp)하여 기본 이동 위치 산출
        Vector3 currentPos = Vector3.Lerp(startPos, targetPos, journeyTime);

        // 삼각함수 Sin을 이용해 포물선의 정중앙에서 Y축 높이가 솟아오르는 곡선 효과 추가
        float height = Mathf.Sin(journeyTime * Mathf.PI) * 3.0f;
        currentPos.y += height;

        // 이전 위치와 현재 위치를 비교해 이동 방향 벡터 산출
        Vector3 moveDir = (currentPos - transform.position).normalized;
        transform.position = currentPos;

        // 투사체가 이동 방향을 자연스럽게 바라보도록 회전
        if (moveDir != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(moveDir);
        }

        // 포물선 이동 완료 시점(진행률 1.0 이상) 도달 시 명중 처리
        if (journeyTime >= 1.0f)
        {
            HitTarget();
        }
    }

    [Header("폭발 범위 설정 (Shell/포격 전용)")]
    public bool isAreaOfEffect = false;    // 범위 폭발 데미지를 줄 것인지 여부
    public float explosionRadius = 3.0f;   // 폭발 반경 범위

    // 투사체가 적중했을 때 실행되는 공통 명중 및 데미지 전달 함수
    private void HitTarget()
    {
        // 1. 단일 대상 타격 처리
        if (target != null)
        {
            // 타겟 오브젝트에 부착된 Health 컴포넌트 탐색
            Health targetHealth = target.GetComponent<Health>();
            if (targetHealth != null)
            {
                // 타겟에게 데미지 부여 (기본 데미지 10 적용)
                targetHealth.TakeDamage(10);
            }
        }

        // 2. 범위 공격(Shell 등) 옵션이 켜져 있는 경우 주변 적들까지 동시 타격
        if (isAreaOfEffect)
        {
            // 투사체 폭발 위치 주변의 모든 콜라이더 탐색
            Collider[] colliders = Physics.OverlapSphere(transform.position, explosionRadius);
            foreach (Collider hit in colliders)
            {
                Health areaHealth = hit.GetComponent<Health>();
                if (areaHealth != null)
                {
                    // 폭발 범위 내 대상들에게 광역 데미지 부여 (15 데미지)
                    areaHealth.TakeDamage(15);
                }
            }
        }

        // 임무를 완수한 투사체 오브젝트 파괴
        Destroy(gameObject);
    }

    // 에디터 뷰(Scene View)에서 포격 계열 무기의 폭발 범위를 시각적으로 확인하기 위한 기즈모 함수
    private void OnDrawGizmos()
    {
        if (isAreaOfEffect)
        {
            Gizmos.color = Color.red; // 빨간색 와이어 사스피어 표시
            Gizmos.DrawWireSphere(transform.position, explosionRadius);
        }
    }
}