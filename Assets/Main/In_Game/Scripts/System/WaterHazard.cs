using UnityEngine;

// 물에 빠진 플레이어 (섬 지형에서만, PlayArea가 붙임)
//  - 물에 들어가면 점점 느려지고 (slowTime초에 걸쳐 minSpeed까지)
//  - 들어간 뒤 graceTime초부터 drownTime초 동안 최대 체력만큼 피해를 받아 죽음 (방어력 무시)
//  - 물 밖으로 나오면 곧바로 원래대로
//  - 몹은 물에 들어가지 않음 (내비메쉬가 땅에만 있음)
public class WaterHazard : MonoBehaviour
{
    public float slowTime = 1.5f;
    [Range(0.05f, 1f)] public float minSpeed = 0.3f;
    public float graceTime = 0.3f;
    public float drownTime = 3f;
    private const float TickInterval = 0.25f;

    private PlayerController pc;
    private Health health;
    private SpriteRenderer lowerBody;   // 상·하체가 나뉜 캐릭터의 하체 (물속에서는 숨김)
    private bool lowerHidden;
    private float inWaterTime;
    private float nextTick;

    public static bool PlayerInWater { get; private set; }

    void Update()
    {
        if (!IslandMap.Active) return;
        if (pc == null)
        {
            Transform p = GameManager.Instance != null ? GameManager.Instance.playerTransform : null;
            if (p == null) { PlayerInWater = false; return; }
            pc = p.GetComponent<PlayerController>();
            health = p.GetComponent<Health>();
            if (pc == null) return;
            Transform upper = p.Find("Upper"), lower = p.Find("Sprite");
            lowerBody = upper != null && lower != null ? lower.GetComponent<SpriteRenderer>() : null;
        }

        bool wet = health != null && !health.IsDead && IslandMap.IsWater(pc.transform.position);
        PlayerInWater = wet;
        // 물속: 하체를 숨겨 허리까지 잠긴 것처럼 (강바닥이 파여 있어 상체도 함께 내려감)
        bool hide = wet;
        if (lowerBody != null && hide != lowerHidden) { lowerBody.enabled = !hide; lowerHidden = hide; }
        if (!wet)
        {
            inWaterTime = 0f;
            pc.WaterSpeedMul = 1f;
            return;
        }

        inWaterTime += Time.deltaTime;
        pc.WaterSpeedMul = Mathf.Lerp(1f, minSpeed, Mathf.Clamp01(inWaterTime / Mathf.Max(0.01f, slowTime)));

        if (inWaterTime >= graceTime && Time.time >= nextTick)
        {
            nextTick = Time.time + TickInterval;
            float dmg = health.maxHealth / Mathf.Max(0.1f, drownTime) * TickInterval;
            health.TakeDamage(dmg, 99999f, null);
        }
    }

    void OnDisable()
    {
        PlayerInWater = false;
        if (lowerBody != null) lowerBody.enabled = true;
        if (pc != null) pc.WaterSpeedMul = 1f;
    }
}
