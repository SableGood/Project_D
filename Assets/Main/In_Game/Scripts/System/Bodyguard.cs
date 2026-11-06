using UnityEngine;

// 대통령 직업의 경호실장 (2명). 플레이어 양옆 뒤를 따라다니며 가까운 몹을 자동으로 쏨
//  - 그림: 플레이어 캐릭터 그림(서 있는 하체 + 대기 상체)을 어둡게(정장 색) 칠해서 작게
//  - 사격: 사거리 9m 안에서 가장 가까운 몹, 0.4초마다 12 피해 (플레이어 재능의 공격력 배율 적용, 처치·흡혈은 플레이어 몫)
//  - 맞지 않음 (몹의 목표가 되지 않음). 1인칭에서도 그대로 따라다님
public class Bodyguard : MonoBehaviour
{
    public const float Range = 9f, Interval = 0.4f, Damage = 12f, FollowSpeed = 7f;
    private static readonly Color SuitTint = new Color(0.55f, 0.6f, 0.78f, 1f);

    private Transform owner;
    private Vector3 offset;
    private float nextShot;
    private LineRenderer tracer;
    private float tracerUntil;
    private SpriteRenderer body;
    private static readonly Collider[] hits = new Collider[64];

    public static void SpawnFor(PlayerWeaponManager pwm)
    {
        if (pwm == null) return;
        for (int i = 0; i < 2; i++)
        {
            GameObject go = new GameObject($"Bodyguard_{i + 1}");
            Bodyguard g = go.AddComponent<Bodyguard>();
            g.owner = pwm.transform;
            g.offset = new Vector3(i == 0 ? -1.1f : 1.1f, 0f, -0.6f);
            g.nextShot = Time.time + i * Interval * 0.5f;
            go.transform.position = pwm.transform.position + g.offset;
            g.BuildVisual(pwm.GetComponentInChildren<PlayerBodyAnimator>(true));
        }
    }

    private void BuildVisual(PlayerBodyAnimator pba)
    {
        GameObject s = new GameObject("Sprite");
        s.transform.SetParent(transform, false);
        body = s.AddComponent<SpriteRenderer>();
        if (pba != null && pba.lowerRenderer != null)
        {
            body.sharedMaterial = pba.lowerRenderer.sharedMaterial;
            if (pba.walkFrames != null && pba.walkFrames.Length > 0) body.sprite = pba.walkFrames[Mathf.Clamp(pba.standFrame, 0, pba.walkFrames.Length - 1)];
            s.transform.localScale = pba.lowerRenderer.transform.lossyScale * 0.8f;
            if (pba.upperRenderer != null && pba.upperSprites != null && pba.upperSprites.Length > 0 && pba.upperSprites[0] != null)
            {
                GameObject u = new GameObject("Upper");
                u.transform.SetParent(s.transform, false);
                u.transform.localPosition = pba.upperRenderer.transform.localPosition;
                SpriteRenderer ur = u.AddComponent<SpriteRenderer>();
                ur.sharedMaterial = pba.upperRenderer.sharedMaterial;
                ur.sprite = pba.upperSprites[0];
                ur.sortingOrder = body.sortingOrder + 1;
                ur.color = SuitTint;
            }
        }
        body.color = SuitTint;
        s.AddComponent<Billboard>();

        tracer = new GameObject("Tracer").AddComponent<LineRenderer>();
        tracer.transform.SetParent(transform, false);
        tracer.positionCount = 2;
        tracer.widthMultiplier = 0.04f;
        tracer.material = new Material(Shader.Find("Sprites/Default"));
        tracer.startColor = new Color(1f, 0.9f, 0.55f, 0.9f);
        tracer.endColor = new Color(1f, 0.9f, 0.55f, 0.2f);
        tracer.enabled = false;
    }

    void LateUpdate()
    {
        if (owner == null) { Destroy(gameObject); return; }
        // 플레이어 뒤쪽 양옆 (플레이어 회전과 무관하게 화면 기준)
        Vector3 goal = owner.position + offset;
        transform.position = Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-FollowSpeed * Time.deltaTime));
        if (tracer != null && tracer.enabled && Time.time >= tracerUntil) tracer.enabled = false;

        if (Time.timeScale <= 0f || AugmentManager.IsOpen || Time.time < nextShot) return;
        Health target = FindTarget();
        if (target == null) return;
        nextShot = Time.time + Interval;
        Vector3 from = transform.position + Vector3.up * 0.6f;
        Vector3 to = target.transform.position + Vector3.up * 0.5f;
        target.TakeDamage(Damage * PlayerAugments.DamageMul, 0f, owner);
        HitEffects.Impact(to, false);
        if (body != null) body.flipX = to.x < transform.position.x;
        if (tracer != null)
        {
            tracer.SetPosition(0, from);
            tracer.SetPosition(1, to);
            tracer.enabled = true;
            tracerUntil = Time.time + 0.05f;
        }
    }

    private Health FindTarget()
    {
        int mask = LayerMask.GetMask("Mob");
        int n = Physics.OverlapSphereNonAlloc(transform.position, Range, hits, mask, QueryTriggerInteraction.Ignore);
        Health best = null; float bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Health h = hits[i] != null ? hits[i].GetComponentInParent<Health>() : null;
            if (h == null || h.IsDead || !h.gameObject.activeInHierarchy) continue;
            float d = (h.transform.position - transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = h; }
        }
        return best;
    }
}
