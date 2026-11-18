using UnityEngine;

// v183 자동 조준 (사이보그 직업 · 대통령 경호실장이 함께 씀)
//  - 사거리 안에서 가장 가까운 몹. 지금 노리던 몹이 아직 쏠 수 있으면 계속 노림 (조준이 이리저리 튀지 않게)
//  - 지형·포탑에 가려진 몹은 건너뜀 (아군 탄은 성벽을 지나가므로 성벽은 가림으로 치지 않음)
//  - cone: 1인칭처럼 바라보는 방향 기준 각도 제한 (0 이하 = 제한 없음)
//  - 멀티 대비: 상태 없음 (호출하는 쪽이 대상을 들고 있음)
public static class AutoAim
{
    private static readonly Collider[] hits = new Collider[128];
    private static int mobMask = -1, blockMask = -1;

    private static void Masks()
    {
        if (mobMask != -1) return;
        mobMask = LayerMask.GetMask("Mob");
        blockMask = ~LayerMask.GetMask("Mob", "Player", "Ignore Raycast", "UI");
    }

    public static Health Find(Vector3 from, float range, Health keep, Vector3 coneDir = default, float coneDeg = 0f)
    {
        Masks();
        if (keep != null && Usable(keep, from, range * 1.1f, coneDir, coneDeg)) return keep;
        int n = Physics.OverlapSphereNonAlloc(from, range, hits, mobMask, QueryTriggerInteraction.Ignore);
        Health best = null; float bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Health h = hits[i] != null ? hits[i].GetComponentInParent<Health>() : null;
            if (h == null || h == best) continue;
            float d = (h.transform.position - from).sqrMagnitude;
            if (d >= bestD || !Usable(h, from, range, coneDir, coneDeg)) continue;
            bestD = d; best = h;
        }
        return best;
    }

    private static bool Usable(Health h, Vector3 from, float range, Vector3 coneDir, float coneDeg)
    {
        if (h == null || h.IsDead || !h.gameObject.activeInHierarchy) return false;
        Vector3 p = AimPointOf(h);
        Vector3 to = p - from;
        if (to.sqrMagnitude > range * range) return false;
        if (coneDeg > 0f && coneDir.sqrMagnitude > 0.001f && Vector3.Angle(coneDir, to) > coneDeg) return false;
        if (Physics.Raycast(from, to.normalized, out RaycastHit hit, to.magnitude, blockMask, QueryTriggerInteraction.Ignore)
            && !FortWall.IsWall(hit.collider)) return false;
        return true;
    }

    // 몹의 몸 가운데 (콜라이더 중심). 없으면 발 위 0.5m
    public static Vector3 AimPointOf(Health h)
    {
        if (h == null) return Vector3.zero;
        Collider c = h.GetComponent<Collider>();
        if (c == null) c = h.GetComponentInChildren<Collider>();
        return c != null ? c.bounds.center : h.transform.position + Vector3.up * 0.5f;
    }
}
