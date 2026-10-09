using UnityEngine;
using UnityEngine.AI;
using System.Collections;

// 넉백(밀려남)을 받는 쪽 컴포넌트.
// 몹 프리팹에 직접 붙여 저항값을 정할 수 있고, 안 붙어 있으면 처음 넉백을 받을 때 자동으로 추가됩니다.
// NavMeshAgent(몹) → agent.Move, CharacterController(플레이어) → controller.Move 로 밀어서
// 길찾기/충돌이 깨지지 않게 합니다.
[DisallowMultipleComponent]
public class KnockbackReceiver : MonoBehaviour
{
    [Tooltip("넉백 저항 (0 = 그대로 밀림, 0.5 = 절반, 1 = 밀리지 않음). 보스/탱커는 높게.")]
    [Range(0f, 1f)] public float resistance = 0f;

    [Tooltip("밀려나는 데 걸리는 시간(초)")]
    public float duration = 0.12f;

    private NavMeshAgent agent;
    private CharacterController characterController;
    private Coroutine pushRoutine;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        characterController = GetComponent<CharacterController>();
    }

    void OnDisable()
    {
        // 풀로 반환될 때 진행 중인 넉백 정리
        pushRoutine = null;
    }

    // direction: 밀리는 방향 (수평으로 보정됨), distance: 밀리는 거리(유닛)
    public void Apply(Vector3 direction, float distance)
    {
        distance *= (1f - resistance);
        if (distance <= 0.001f || !isActiveAndEnabled) return;

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        direction.Normalize();

        if (pushRoutine != null) StopCoroutine(pushRoutine);
        pushRoutine = StartCoroutine(PushRoutine(direction * distance));
    }

    private IEnumerator PushRoutine(Vector3 totalOffset)
    {
        float elapsed = 0f;
        Vector3 moved = Vector3.zero;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - (1f - t) * (1f - t); // 처음에 빠르고 끝에 느려지는 곡선
            Vector3 target = totalOffset * eased;
            Vector3 step = target - moved;
            moved = target;

            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(step);
            else if (characterController != null && characterController.enabled) characterController.Move(step);
            else transform.position += step;

            yield return null;
        }
        pushRoutine = null;
    }

    // 맞은 대상(콜라이더의 Transform)에게 넉백을 시도하는 도우미
    public static void TryApply(Transform target, Vector3 direction, float distance)
    {
        if (target == null || distance <= 0f) return;

        KnockbackReceiver receiver = target.GetComponentInParent<KnockbackReceiver>();
        if (receiver == null)
        {
            Health health = target.GetComponentInParent<Health>();
            if (health == null) return; // 체력이 없는 물체(벽 등)는 밀지 않음
            receiver = health.gameObject.AddComponent<KnockbackReceiver>();
        }
        receiver.Apply(direction, distance);
    }
}
