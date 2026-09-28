using UnityEngine;
using UnityEngine.AI;

public class MobAI : MonoBehaviour
{
    public enum MobType { Melee, Ranged, Multi }

    [Header("몬스터 설정")]
    public MobType type;
    public float maintainDistance = 0.0f;
    public EntityData myData;

    private NavMeshAgent agent;
    private Transform player;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        if (myData != null)
        {
            if (agent != null) agent.speed = myData.moveSpeed;

            Health health = GetComponent<Health>();
            if (health != null) health.InitStats(myData);

            Weapon weapon = GetComponentInChildren<Weapon>();
            if (weapon != null) weapon.InitWeapon(myData.defaultWeapon);
        }
    }

    void Update()
    {
        // 캐싱된 플레이어가 없거나 파괴되었다면, FindWithTag 대신 GameManager의 변수를 참조합니다.
        if (player == null)
        {
            if (GameManager.Instance != null && GameManager.Instance.playerTransform != null)
            {
                player = GameManager.Instance.playerTransform;
            }
            return; // 플레이어가 아직 세팅되지 않았다면 대기
        }

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