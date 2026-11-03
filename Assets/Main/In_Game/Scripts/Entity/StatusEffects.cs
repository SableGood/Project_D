using UnityEngine;

// 상태이상 (몹)
//  화상 Burn   : 초당 피해, 시간 제한 있음. 붉은빛
//  둔화 Slow   : 이동속도 감소 (기본 30%), 시간 제한 있음. 푸른빛
//  기절 Stun   : 이동·공격 불가 (기본 1초). 채도 낮아짐
//  중독 Poison : 초당 피해, 죽을 때까지 지속. 보랏빛
//  치명타 Crit : 상태이상은 아니고, 치명타를 맞았을 때 체력바 옆에 아이콘을 잠깐 띄움
//
//  사용: StatusEffects.Apply(맞은 콜라이더나 몹, StatusType.Burn, 초당피해, 지속초)
//        StatusEffects.MarkCrit(맞은 콜라이더나 몹)
//  같은 상태이상이 다시 걸리면: 수치는 더 큰 쪽, 지속시간은 더 긴 쪽 (중독은 새 수치로 갱신)
//  지속 피해는 방어력을 무시하고 0.5초마다 들어가며, 피격 번쩍임·흔들림은 없음
//  MobAI가 시작할 때 자동으로 붙음. 풀에서 다시 나오면 모두 풀림
public enum StatusType { Burn, Slow, Stun, Poison }

public class StatusEffects : MonoBehaviour
{
    // 값을 안 넣었을 때의 기본값
    public const float DefaultBurnDps = 5f, DefaultBurnTime = 3f;
    public const float DefaultSlowPercent = 0.3f, DefaultSlowTime = 2f;
    public const float DefaultStunTime = 1f;
    public const float DefaultPoisonDps = 3f;
    public const float CritShowTime = 0.8f;

    private const float TickInterval = 0.5f;
    private const float TintAmount = 0.22f;   // 기존 색에 덧씌우는 정도 (0 = 없음, 1 = 단색)
    private const float StunDesaturate = 0.55f;

    private static readonly Color BurnTint = new Color(1f, 0.22f, 0.08f);
    private static readonly Color SlowTint = new Color(0.3f, 0.62f, 1f);   // 아이콘(파란 눈꽃)에 맞춤
    private static readonly Color PoisonTint = new Color(0.62f, 0.2f, 0.95f);

    private static readonly int StatusColorId = Shader.PropertyToID("_StatusColor");
    private static readonly int StatusDesatId = Shader.PropertyToID("_StatusDesat");

    private Health health;
    private MobAI mob;
    private SpriteRenderer body;
    private MaterialPropertyBlock block;
    private bool shaderSupports;

    private float burnDps, burnUntil;
    private float slowPercent, slowUntil;
    private float stunUntil;
    private float poisonDps;
    private float critUntil;
    private float nextTick;
    private Color shownColor = new Color(0, 0, 0, -1f);
    private float shownDesat = -1f;

    public bool Burning => Time.time < burnUntil && burnDps > 0f;
    // 둔화는 포탑(충격파 등)이 건 것도 함께 표시
    public bool Slowed => mob != null ? mob.SlowPercent > 0f : (Time.time < slowUntil && slowPercent > 0f);
    public bool Stunned => Time.time < stunUntil;
    public bool Poisoned => poisonDps > 0f;
    public bool CritShown => Time.time < critUntil;
    public bool AnyShown => Burning || Slowed || Stunned || Poisoned || CritShown;

    // ===== 외부에서 호출 =====
    public static StatusEffects Of(Component target)
    {
        if (target == null) return null;
        MobAI m = target.GetComponentInParent<MobAI>();
        if (m == null || !m.isActiveAndEnabled) return null;
        StatusEffects s = m.GetComponent<StatusEffects>();
        if (s == null) s = m.gameObject.AddComponent<StatusEffects>();
        return s;
    }

    // value: 화상·중독 = 초당 피해, 둔화 = 비율(0.3 = 30%), 기절 = 무시. 0 이하면 기본값
    // duration: 화상·둔화·기절 지속초 (0 이하면 기본값), 중독은 무시 (죽을 때까지)
    public static bool Apply(Component target, StatusType type, float value = 0f, float duration = 0f)
    {
        StatusEffects s = Of(target);
        if (s == null) return false;
        s.Add(type, value, duration);
        return true;
    }

    public static void MarkCrit(Component target)
    {
        StatusEffects s = Of(target);
        if (s != null) s.critUntil = Time.time + CritShowTime;
    }

    public void Add(StatusType type, float value, float duration)
    {
        if (health != null && health.IsDead) return;
        float now = Time.time;
        switch (type)
        {
            case StatusType.Burn:
                if (value <= 0f) value = DefaultBurnDps;
                if (duration <= 0f) duration = DefaultBurnTime;
                burnDps = now < burnUntil ? Mathf.Max(burnDps, value) : value;
                burnUntil = Mathf.Max(burnUntil, now + duration);
                StartTicking();
                break;
            case StatusType.Slow:
                if (value <= 0f) value = DefaultSlowPercent;
                if (duration <= 0f) duration = DefaultSlowTime;
                slowPercent = now < slowUntil ? Mathf.Max(slowPercent, value) : value;
                slowUntil = Mathf.Max(slowUntil, now + duration);
                if (mob != null) mob.ApplySlow(value, duration);
                break;
            case StatusType.Stun:
                if (duration <= 0f) duration = DefaultStunTime;
                stunUntil = Mathf.Max(stunUntil, now + duration);
                if (mob != null) mob.Stun(duration);
                break;
            case StatusType.Poison:
                if (value <= 0f) value = DefaultPoisonDps;
                poisonDps = value;   // 다시 걸리면 새 수치로 갱신
                StartTicking();
                break;
        }
    }

    public void ClearAll()
    {
        burnDps = burnUntil = 0f;
        slowPercent = slowUntil = 0f;
        stunUntil = 0f;
        poisonDps = 0f;
        critUntil = 0f;
        ApplyTint(true);
    }

    // ===== 내부 =====
    void Awake()
    {
        health = GetComponent<Health>();
        mob = GetComponent<MobAI>();
        if (mob != null) body = mob.BodySprite;
        shaderSupports = body != null && body.sharedMaterial != null && body.sharedMaterial.HasProperty(StatusColorId);
    }

    void OnEnable() { ClearAll(); }
    void OnDisable() { ClearAll(); }

    private void StartTicking()
    {
        if (nextTick < Time.time) nextTick = Time.time + TickInterval;
    }

    void Update()
    {
        if (health != null && health.IsDead) return;
        float now = Time.time;
        if ((Burning || Poisoned) && now >= nextTick)
        {
            nextTick = now + TickInterval;
            float dps = (Burning ? burnDps : 0f) + poisonDps;
            if (dps > 0f && health != null)
            {
                Health.DotTick = true;
                try { health.TakeDamage(dps * TickInterval, 99999f, null); }
                finally { Health.DotTick = false; }
            }
        }
        ApplyTint(false);
    }

    private void ApplyTint(bool force)
    {
        if (!shaderSupports) return;
        Color sum = Color.black; int n = 0;
        if (Burning) { sum += BurnTint; n++; }
        if (Slowed) { sum += SlowTint; n++; }
        if (Poisoned) { sum += PoisonTint; n++; }
        Color c = n > 0 ? sum / n : Color.black;
        c.a = n > 0 ? TintAmount : 0f;
        float desat = Stunned ? StunDesaturate : 0f;
        if (!force && c == shownColor && Mathf.Approximately(desat, shownDesat)) return;
        shownColor = c; shownDesat = desat;
        if (block == null) block = new MaterialPropertyBlock();
        body.GetPropertyBlock(block);   // 번쩍임·발 위치 등 다른 값은 유지
        block.SetColor(StatusColorId, c);
        block.SetFloat(StatusDesatId, desat);
        body.SetPropertyBlock(block);
    }
}
