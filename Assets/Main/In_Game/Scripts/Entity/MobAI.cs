using UnityEngine;
using UnityEngine.AI;

public class MobAI : MonoBehaviour
{
    public enum MobType { Melee, Ranged, Multi }

    [Header("몬스터 설정")]
    public MobType type;
    public float maintainDistance = 0.0f;
    public EntityData myData;

    // [최적화] 컴포넌트 참조를 캐싱할 변수들
    private NavMeshAgent agent;
    private Health health;
    private Weapon weapon;
    private Transform player;

    // 오브젝트가 생성될 때 최초 1회만 호출되어 컴포넌트 탐색 부하를 최소화합니다.
    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        health = GetComponent<Health>();
        weapon = GetComponentInChildren<Weapon>();
    }

    // ★ [치명적 버그 픽스] Start() 대신 풀에서 꺼내져 활성화될 때마다 호출되는 OnEnable() 사용
    void OnEnable()
    {
        // 캐싱된 데이터가 있을 경우 몹의 스탯과 무기를 새것처럼 온전히 초기화합니다.
        if (myData != null)
        {
            if (agent != null) agent.speed = myData.moveSpeed;
            if (health != null) health.InitStats(myData);
            if (weapon != null) weapon.InitWeapon(myData.defaultWeapon);
        }
    }

    void Update()
    {
        // 캐싱된 플레이어가 없거나 파괴되었다면, GameManager의 변수를 참조합니다.
        if (player == null)
        {
            if (GameManager.Instance != null && GameManager.Instance.playerTransform != null)
            {
                player = GameManager.Instance.playerTransform;
            }
            return; // 플레이어가 아직 세팅되지 않았다면 대기
        }

        // [이후 기존 AI 이동 로직 유지...]
        float distance = Vector3.Distance(transform.position, player.position);

        if (type == MobType.Melee)
        {
            agent.SetDestination(player.position);
        }
        else if (type == MobType.Ranged)
        {
            if (distance > maintainDistance)
            {
                agent.SetDestination(player.position);
            }
            else
            {
                agent.ResetPath();
                Vector3 lookPos = player.position - transform.position;
                lookPos.y = 0;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookPos), Time.deltaTime * 5f);
            }
        }
    }
}