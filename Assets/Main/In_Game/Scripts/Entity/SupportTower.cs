using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// 지원형 포탑 (공격하지 않는 포탑)
//  ■ SlowAura (충격파 포탑 TR040) : 사거리 안의 몹 이동속도를 계속 낮춤 ("근처 몹 이동 둔화 15%")
//  ■ Repair   (수리 포탑 TR050)   : 주기적으로 사거리 안의 다른 포탑 체력을 회복 ("주기적으로 주변 포탑 회복")
//
// 수치는 Mob_Data.csv에서 옵니다.
//  - 범위   = '사거리' 칸
//  - 둔화율 = '고유능력' 칸의 첫 번째 % 숫자 (예: "근처 몹 이동 둔화 15%" → 15%)
//  - 회복량 = '공격력' 칸 (비어 있으면 healAmount), 회복 주기 = 1 / '공격속도' 칸 (비어 있으면 healInterval)
//  * CSV를 고친 뒤에는 [Tools/CSV 데이터 생성/1. 개체 및 무기 (Mob_Data)]를 다시 실행하세요.
public class SupportTower : MonoBehaviour
{
    public enum Mode { SlowAura, Repair }

    [Header("포탑 설정")]
    public EntityData myData;
    public Mode mode = Mode.SlowAura;
    [Tooltip("효과 범위. CSV 사거리가 있으면 그 값을 사용")]
    public float range = 7f;

    [Header("둔화 (SlowAura)")]
    [Tooltip("CSV 고유능력에 %가 없을 때 쓰는 둔화율 (0.15 = 15%)")]
    [Range(0f, 0.9f)] public float slowPercent = 0.15f;
    public float slowTickInterval = 0.25f;   // 이 간격마다 범위 안 몹에게 둔화를 다시 걸어줌
    public float slowPulseVisualInterval = 1.2f;

    [Header("수리 (Repair)")]
    [Tooltip("CSV 공격력이 비어 있을 때 쓰는 1회 회복량")]
    public float healAmount = 10f;
    [Tooltip("CSV 공격속도가 비어 있을 때 쓰는 회복 주기(초)")]
    public float healInterval = 2f;
    [Tooltip("켜면 자기 자신도 회복")]
    public bool healSelf = false;

    [Header("연출")]
    [Tooltip("천천히 회전할 포탑 머리 (비우면 회전 없음)")]
    public Transform spinner;
    public float spinSpeed = 60f;
    public Color pulseColor = new Color(0.4f, 0.8f, 1f, 0.9f);
    public float pulseDuration = 0.6f;

    // 테스트/HUD용
    public float EffectivePercent { get; private set; }
    public float EffectiveHeal { get; private set; }
    public float EffectiveInterval { get; private set; }

    private float nextTickTime;
    private float nextPulseTime;
    private int mobMask, towerMask;
    private Health selfHealth;
    private LineRenderer pulseRing;
    private Coroutine pulseRoutine;
    private Renderer baseRenderer;
    private static Material ringMaterial;
    private static readonly Collider[] hits = new Collider[64];
    private readonly HashSet<Health> healedThisPulse = new HashSet<Health>();
    private const int RingSegments = 48;

    void Awake()
    {
        selfHealth = GetComponent<Health>();
        baseRenderer = GetComponent<Renderer>();
    }

    void OnEnable()
    {
        mobMask = LayerMask.GetMask("Mob");
        towerMask = LayerMask.GetMask("Tower");
        ApplyData();

        if (selfHealth == null) selfHealth = GetComponent<Health>();
        if (myData != null && selfHealth != null) selfHealth.InitStats(myData);

        nextTickTime = Time.time + 0.2f;
        nextPulseTime = Time.time + 0.5f;
        if (pulseRing != null) pulseRing.enabled = false;
    }

    void OnDisable()
    {
        if (pulseRoutine != null) { StopCoroutine(pulseRoutine); pulseRoutine = null; }
        if (pulseRing != null) pulseRing.enabled = false;
    }

    // CSV 데이터 → 실제 수치
    private void ApplyData()
    {
        float pct = slowPercent;
        float heal = healAmount;
        float interval = healInterval;

        if (myData != null)
        {
            WeaponData wd = myData.defaultWeapon;
            if (wd != null)
            {
                if (wd.attackRange > 0f) range = wd.attackRange;
                if (wd.attackPower > 0f) heal = wd.attackPower;
                if (wd.attackSpeed > 0f) interval = 1f / wd.attackSpeed;
            }
            float parsed = ParsePercent(myData.abilityText);
            if (parsed > 0f) pct = parsed;
        }

        EffectivePercent = Mathf.Clamp(pct, 0f, 0.9f);
        EffectiveHeal = heal;
        EffectiveInterval = Mathf.Max(0.2f, interval);
    }

    // "근처 몹 이동 둔화 15%" → 0.15 (첫 번째 % 숫자)
    public static float ParsePercent(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        Match m = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*%");
        if (!m.Success) return 0f;
        return float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float v) ? v / 100f : 0f;
    }

    void Update()
    {
        if (spinner != null && spinSpeed != 0f) spinner.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        if (selfHealth == null) selfHealth = GetComponent<Health>(); // 건설 시 TowerBuilder가 Health를 나중에 붙임
        if (selfHealth != null && selfHealth.IsDead) return;

        if (mode == Mode.SlowAura) UpdateSlowAura();
        else UpdateRepair();
    }

    private void UpdateSlowAura()
    {
        if (Time.time >= nextTickTime)
        {
            nextTickTime = Time.time + slowTickInterval;
            if (mobMask != 0 && EffectivePercent > 0f)
            {
                int count = Physics.OverlapSphereNonAlloc(transform.position, range, hits, mobMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                    MobAI.TryApplySlow(hits[i], EffectivePercent, slowTickInterval + 0.2f); // 범위를 벗어나면 곧 풀림
            }
        }

        if (Time.time >= nextPulseTime)
        {
            nextPulseTime = Time.time + slowPulseVisualInterval;
            PlayPulse();
        }
    }

    private void UpdateRepair()
    {
        if (Time.time < nextTickTime) return;
        nextTickTime = Time.time + EffectiveInterval;
        if (towerMask == 0 || EffectiveHeal <= 0f) return;

        healedThisPulse.Clear();
        bool any = false;
        int count = Physics.OverlapSphereNonAlloc(transform.position, range, hits, towerMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Health h = hits[i] != null ? hits[i].GetComponentInParent<Health>() : null;
            if (h == null || h.IsDead || !healedThisPulse.Add(h)) continue;
            if (h == selfHealth && !healSelf) continue;
            if (h.GetComponent<PlayerController>() != null) continue; // 포탑만 (플레이어 제외)
            if (h.Heal(EffectiveHeal) > 0f) any = true;
        }
        PlayPulse(any ? 1f : 0.35f); // 실제로 회복했으면 진하게
    }

    // ---------------- 연출: 바닥에 퍼지는 원 ----------------
    private void PlayPulse(float alpha = 1f)
    {
        if (!isActiveAndEnabled) return;
        if (pulseRing == null) pulseRing = CreateRing();
        if (pulseRoutine != null) StopCoroutine(pulseRoutine);
        pulseRoutine = StartCoroutine(PulseRoutine(alpha));
    }

    private IEnumerator PulseRoutine(float alpha)
    {
        float groundY = baseRenderer != null ? baseRenderer.bounds.min.y + 0.05f : transform.position.y;
        Vector3 center = new Vector3(transform.position.x, groundY, transform.position.z);
        pulseRing.enabled = true;

        float t = 0f;
        while (t < pulseDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / pulseDuration);
            float radius = Mathf.Lerp(0.5f, range, 1f - (1f - k) * (1f - k)); // 빠르게 퍼졌다가 느려짐
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * Mathf.PI * 2f / RingSegments;
                pulseRing.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
            Color c = pulseColor;
            c.a *= alpha * (1f - k);
            pulseRing.startColor = c;
            pulseRing.endColor = c;
            yield return null;
        }
        pulseRing.enabled = false;
        pulseRoutine = null;
    }

    private LineRenderer CreateRing()
    {
        if (ringMaterial == null) ringMaterial = new Material(Shader.Find("Sprites/Default"));
        GameObject go = new GameObject("PulseRing");
        go.transform.SetParent(transform, false);
        go.layer = 2; // Ignore Raycast
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = RingSegments;
        lr.widthMultiplier = 0.12f;
        lr.sharedMaterial = ringMaterial;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = mode == Mode.Repair ? new Color(0.3f, 1f, 0.4f, 0.8f) : new Color(0.4f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
