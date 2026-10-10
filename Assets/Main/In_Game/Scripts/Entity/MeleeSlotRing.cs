using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

// 근접 슬롯 시스템 (플레이어 주변 원형 자리 배정)
//  - 플레이어 주변을 slotCount(기본 8)개의 자리로 나누고, 다가온 근접 몹에게 자리를 하나씩 배정합니다.
//  - 몹은 배정된 자리까지만 다가가서 공격하므로, 플레이어 몸 안쪽으로 파고들거나 서로 겹치지 않습니다.
//    (몹이 플레이어를 밀어 튕겨내던 문제 방지)
//  - 몸집이 큰 몹은 옆 자리까지 여러 칸을 차지합니다.
//  - 자리가 가득 차면 바깥쪽에서 대기하다가, 자리가 비면(몹 처치 등) 들어옵니다.
//  - 자리는 월드 기준 방향으로 고정되어 있어, 플레이어가 회전해도 몹들이 같이 돌지 않습니다.
//
// MobAI가 목표(플레이어)를 정하면 자동으로 플레이어에 붙습니다. (수동으로 붙여서 값 조절도 가능)
public class MeleeSlotRing : MonoBehaviour
{
    // 테스트 패널용 전역 스위치
    public static bool SystemEnabled = true;
    public static bool ShowMarkers = false;

    [Header("슬롯")]
    [Range(4, 16)] public int slotCount = 8;
    [Tooltip("슬롯에 선 몹의 몸 표면 ~ 플레이어 몸 표면 사이 거리 (몹 무기 사거리가 더 짧으면 사거리에 맞춰 줄어듦)")]
    public float gap = 0.35f;
    [Tooltip("플레이어와 몸 표면 거리가 이 안으로 들어오면 슬롯을 요청")]
    public float engageRange = 5f;

    [Header("플레이어 충돌 보조")]
    [Tooltip("플레이어에 NavMeshObstacle(길막 없음)을 붙여, 이동 중인 몹들이 플레이어를 피해 돌아가도록 함")]
    public bool addNavObstacle = true;

    private struct Claim { public int start; public int count; }
    private MobAI[] owners;
    private readonly Dictionary<MobAI, Claim> claims = new Dictionary<MobAI, Claim>();
    private float bodyRadius = 0.5f;
    private float feetOffset = -1f;
    private LineRenderer[] markers;
    private static Material markerMat;

    public int OccupiedSlots { get { int n = 0; if (owners != null) foreach (var o in owners) if (o != null) n++; return n; } }
    public int ClaimCount => claims.Count;
    public float SlotAngle => 360f / slotCount;

    void Awake()
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            bodyRadius = cc.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            feetOffset = cc.center.y - cc.height * 0.5f;
            if (addNavObstacle && GetComponent<NavMeshObstacle>() == null)
            {
                NavMeshObstacle ob = gameObject.AddComponent<NavMeshObstacle>();
                ob.shape = NavMeshObstacleShape.Capsule;
                ob.center = cc.center;
                ob.radius = cc.radius;
                ob.height = cc.height;
                ob.carving = false; // 길을 막지는 않고, 이동 중인 몹의 회피 대상만 됨
            }
        }
        owners = new MobAI[slotCount];
    }

    void OnDisable() { ReleaseAll(); }

    // ------------------------------------------------------------------
    // 슬롯 배정
    // ------------------------------------------------------------------
    public bool HasClaim(MobAI mob) => mob != null && claims.ContainsKey(mob);

    // 이 몹이 서야 할 원의 반지름 (몸 중심 기준)
    public float RingRadius(float mobRadius, float standoff) => bodyRadius + mobRadius + Mathf.Max(0f, standoff);

    // 몸집에 따라 차지하는 칸 수
    public int SlotsNeeded(float mobRadius, float ringRadius)
    {
        float chord = 2f * ringRadius * Mathf.Sin(Mathf.PI / slotCount); // 이웃 슬롯 중심 사이 거리
        return Mathf.Clamp(Mathf.CeilToInt(mobRadius * 2f * 0.9f / Mathf.Max(0.01f, chord)), 1, slotCount);
    }

    // 몹의 현재 위치에서 가장 가까운 빈 자리 배정 (실패하면 false)
    public bool TryClaim(MobAI mob, Vector3 mobPos, float mobRadius, float ringRadius)
    {
        if (mob == null) return false;
        if (claims.ContainsKey(mob)) return true;
        EnsureArrays();

        int need = SlotsNeeded(mobRadius, ringRadius);
        float prefer = AngleOf(mobPos);

        // 선호 방향에서 가까운 순서로 시작 칸 후보 검사
        int bestStart = -1;
        float bestDiff = float.MaxValue;
        for (int s = 0; s < slotCount; s++)
        {
            if (!RangeFree(s, need, mob)) continue;
            float center = (s + (need - 1) * 0.5f) * SlotAngle;
            float diff = Mathf.Abs(Mathf.DeltaAngle(center, prefer));
            if (diff >= bestDiff) continue;
            if (!IsReachable(CenterPosition(s, need, ringRadius))) continue; // 벽/타워 안쪽 자리는 제외
            bestDiff = diff; bestStart = s;
        }
        if (bestStart < 0) return false;

        for (int i = 0; i < need; i++) owners[(bestStart + i) % slotCount] = mob;
        claims[mob] = new Claim { start = bestStart, count = need };
        return true;
    }

    // 플레이어가 몹 반대편으로 지나가 버리는 등 자리가 너무 먼 쪽이면 가까운 자리로 다시 배정
    public void ReclaimIfFar(MobAI mob, Vector3 mobPos, float mobRadius, float ringRadius, float maxAngle = 110f)
    {
        if (!claims.TryGetValue(mob, out Claim c)) return;
        float center = (c.start + (c.count - 1) * 0.5f) * SlotAngle;
        if (Mathf.Abs(Mathf.DeltaAngle(center, AngleOf(mobPos))) <= maxAngle) return;
        Release(mob);
        if (!TryClaim(mob, mobPos, mobRadius, ringRadius))
        {
            // 새 자리가 없으면 원래 자리 유지
            for (int i = 0; i < c.count; i++) owners[(c.start + i) % slotCount] = mob;
            claims[mob] = c;
        }
    }

    public void Release(MobAI mob)
    {
        if (mob == null || !claims.TryGetValue(mob, out Claim c)) return;
        for (int i = 0; i < c.count; i++)
        {
            int idx = (c.start + i) % slotCount;
            if (owners[idx] == mob) owners[idx] = null;
        }
        claims.Remove(mob);
    }

    public void ReleaseAll()
    {
        claims.Clear();
        if (owners != null) for (int i = 0; i < owners.Length; i++) owners[i] = null;
    }

    public Vector3 GetSlotPosition(MobAI mob, float ringRadius)
    {
        if (!claims.TryGetValue(mob, out Claim c)) return transform.position;
        return CenterPosition(c.start, c.count, ringRadius);
    }

    // 자리가 없을 때 대기할 위치 (몹이 있는 방향의 바깥쪽)
    public Vector3 WaitPosition(Vector3 mobPos, float waitRadius)
    {
        Vector3 dir = mobPos - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        Vector3 p = transform.position + dir.normalized * waitRadius;
        p.y = mobPos.y;
        return p;
    }

    public string SlotLabel(MobAI mob)
    {
        if (!claims.TryGetValue(mob, out Claim c)) return "없음";
        return c.count == 1 ? $"{c.start + 1}번" : $"{c.start + 1}~{(c.start + c.count - 1) % slotCount + 1}번({c.count}칸)";
    }

    // ------------------------------------------------------------------
    // 내부 계산
    // ------------------------------------------------------------------
    private void EnsureArrays()
    {
        if (owners == null || owners.Length != slotCount)
        {
            owners = new MobAI[slotCount];
            claims.Clear();
        }
    }

    private bool RangeFree(int start, int count, MobAI self)
    {
        for (int i = 0; i < count; i++)
        {
            MobAI o = owners[(start + i) % slotCount];
            if (o != null && o != self)
            {
                // 이미 죽었거나 꺼진 몹이 자리를 잡고 있으면 정리
                if (!o.isActiveAndEnabled) { Release(o); continue; }
                return false;
            }
        }
        return true;
    }

    // 0도 = +X(월드 오른쪽), 반시계 방향
    private float AngleOf(Vector3 worldPos)
    {
        Vector3 d = worldPos - transform.position;
        float a = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
        return a < 0f ? a + 360f : a;
    }

    private Vector3 CenterPosition(int start, int count, float ringRadius)
    {
        float a = (start + (count - 1) * 0.5f) * SlotAngle * Mathf.Deg2Rad;
        return transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ringRadius;
    }

    private static bool IsReachable(Vector3 pos)
    {
        if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) return false;
        Vector2 flat = new Vector2(hit.position.x - pos.x, hit.position.z - pos.z);
        return flat.sqrMagnitude < 0.5f * 0.5f;
    }

    // ------------------------------------------------------------------
    // 디버그 표시 (테스트 패널 '슬롯 표시')  초록 = 빈 자리, 빨강 = 사용 중
    // ------------------------------------------------------------------
    void LateUpdate()
    {
        bool show = ShowMarkers && SystemEnabled;
        if (!show)
        {
            if (markers != null) foreach (var m in markers) if (m != null) m.enabled = false;
            return;
        }
        EnsureArrays();
        if (markers == null || markers.Length != slotCount) BuildMarkers();

        float r = RingRadius(0.5f, gap); // 기준: 반지름 0.5 몹 (MM001 크기)
        float y = transform.position.y + feetOffset + 0.05f; // 발밑 바닥
        for (int i = 0; i < slotCount; i++)
        {
            LineRenderer lr = markers[i];
            lr.enabled = true;
            Vector3 c = CenterPosition(i, 1, r); c.y = y;
            const int seg = 12;
            for (int k = 0; k < seg; k++)
            {
                float t = k / (float)seg * Mathf.PI * 2f;
                lr.SetPosition(k, c + new Vector3(Mathf.Cos(t), 0f, Mathf.Sin(t)) * 0.22f);
            }
            Color col = owners[i] != null ? new Color(1f, 0.3f, 0.3f, 0.9f) : new Color(0.3f, 1f, 0.4f, 0.9f);
            lr.startColor = lr.endColor = col;
        }
    }

    private void BuildMarkers()
    {
        if (markers != null) foreach (var m in markers) if (m != null) Destroy(m.gameObject);
        if (markerMat == null) markerMat = new Material(Shader.Find("Sprites/Default"));
        markers = new LineRenderer[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            GameObject go = new GameObject($"SlotMarker_{i + 1}");
            go.transform.SetParent(transform, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = 12;
            lr.widthMultiplier = 0.04f;
            lr.sharedMaterial = markerMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            markers[i] = lr;
        }
    }

    private void OnDrawGizmosSelected()
    {
        float r = RingRadius(0.5f, gap);
        for (int i = 0; i < slotCount; i++)
        {
            Gizmos.color = (owners != null && i < owners.Length && owners[i] != null) ? Color.red : Color.green;
            Gizmos.DrawWireSphere(CenterPosition(i, 1, r), 0.25f);
        }
    }
}
