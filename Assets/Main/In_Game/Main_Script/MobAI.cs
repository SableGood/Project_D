using UnityEngine;
using UnityEngine.AI;

public class MobAI : MonoBehaviour
{
    public enum MobType { Melee, Ranged }

    [Header("몬스터 설정")]
    public MobType type;
    public float maintainDistance = 5.0f; // 원거리 몬스터가 유지할 거리

    private NavMeshAgent agent;
    private Transform player;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        // Start()에서의 1회성 탐색은 제거하고, Update()의 실시간 탐색으로 통합했습니다.
    }

    void Update()
    {
        // ★ 추가된 방어 코드: 플레이어가 없으면 생성될 때까지 매 프레임 태그로 찾습니다.
        if (player == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }
            return; // 타겟을 찾는 동안은 아래 이동 로직을 건너뜁니다.
        }

        // --- 여기서부터 기존의 몬스터 행동 로직 ---

        float distance = Vector3.Distance(transform.position, player.position);

        if (type == MobType.Melee)
        {
            // [근거리] 무조건 플레이어를 향해 최단거리 돌진
            agent.SetDestination(player.position);
        }
        else if (type == MobType.Ranged)
        {
            // [원거리] 거리가 멀면 다가가고, 가까우면 멈춰서 거리 유지
            if (distance > maintainDistance)
            {
                agent.SetDestination(player.position);
            }
            else
            {
                agent.ResetPath(); // 제자리에 멈춤

                // 멈춘 상태에서도 플레이어를 쳐다보게 회전
                Vector3 lookPos = player.position - transform.position;
                lookPos.y = 0;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookPos), Time.deltaTime * 5f);
            }
        }
    }
}