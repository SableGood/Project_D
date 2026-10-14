using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 보스 페이즈 (체력에 따라 단계가 바뀌며 점점 위협적으로)
//  - 기본: 1페이즈(100%) → 2페이즈(60% 이하) → 3페이즈(30% 이하)
//  - 페이즈마다: 그림(기본 이미지 + 걷기 프레임), 이동속도·공격속도·공격력 배율, 넉백 저항
//  - 전환 연출: 잠깐 경직 + 무적 + 크게 흔들림 + 붉은 충격파 + 화면 상단 보스 체력바에 "PHASE n" 표시
//  - 나중에 기믹/능력은 OnPhaseChanged 이벤트에 연결하면 됩니다. (예: 3페이즈 진입 시 졸개 소환)
//  * [Tools/몹/1. 몹 프리팹 정리]가 MB001에 자동으로 붙이고 그림을 채웁니다.
//      그림: Resources/Image/Mobs/Pic_MB001.png(1), Pic_MB001_P2.png(2), Pic_MB001_P3.png(3)
//      걷기: Resources/Image/Mobs/Anim/MB001_P{n}/Walk6_01.png ...  (없는 페이즈는 그림이 들썩이며 걸음)
public class BossPhases : MonoBehaviour
{
    [System.Serializable]
    public class Phase
    {
        public string name = "Phase";
        [Tooltip("체력 비율이 이 값 이하가 되면 이 페이즈로 전환 (1페이즈는 1)")]
        [Range(0f, 1f)] public float hpThreshold = 1f;
        public Sprite idleSprite;
        public List<SpriteClip> walkClips = new List<SpriteClip>();
        [Header("능력치 배율 (1 = 기본)")]
        public float moveSpeedMul = 1f;
        public float attackSpeedMul = 1f;
        public float damageMul = 1f;
        [Range(0f, 1f)] public float knockbackResistance = 0.85f;
    }

    // 화면 상단 체력바에 표시할 이름
    public string displayName = "BOSS";
    public List<Phase> phases = new List<Phase>();

    [Header("페이즈 전환 연출")]
    public float transitionStun = 1.2f;       // 경직 시간
    public float transitionInvulnerable = 1.2f;
    public Color shockwaveColor = new Color(1f, 0.25f, 0.15f, 0.9f);
    public float shockwaveRadius = 6f;

    public int CurrentPhase { get; private set; }   // 0부터
    public Health Health => health;
    public event System.Action<int> OnPhaseChanged; // 인자 = 새 페이즈 번호(0부터)

    // 살아있는 보스 목록 (보스 체력바가 사용)
    public static readonly List<BossPhases> Active = new List<BossPhases>();
    public static event System.Action<BossPhases, int> AnyPhaseChanged;

    private Health health;
    private MobAI ai;
    private KnockbackReceiver knockback;
    private SpriteFrameAnimator anim;
    private Billboard board;
    private Coroutine transitionRoutine;
    private LineRenderer ring;
    private static Material ringMaterial;

    void Awake()
    {
        health = GetComponent<Health>();
        ai = GetComponent<MobAI>();
        knockback = GetComponent<KnockbackReceiver>();
        anim = GetComponentInChildren<SpriteFrameAnimator>(true);
        board = GetComponentInChildren<Billboard>(true);
    }

    void OnEnable()
    {
        if (health != null) health.OnDamaged += HandleDamaged;
        if (!Active.Contains(this)) Active.Add(this);
        CurrentPhase = 0;
        if (health != null) health.invincible = false;
        if (ring != null) ring.enabled = false;
        // MobAI가 OnEnable에서 배율을 1로 초기화하므로 한 프레임 뒤에 1페이즈 적용
        StartCoroutine(ApplyNextFrame());
    }

    void OnDisable()
    {
        if (health != null) health.OnDamaged -= HandleDamaged;
        Active.Remove(this);
        transitionRoutine = null;
    }

    private IEnumerator ApplyNextFrame()
    {
        yield return null;
        ApplyPhase(0);
    }

    private void HandleDamaged(float amount)
    {
        if (health == null || health.IsDead || phases.Count == 0) return;
        float ratio = health.maxHealth > 0f ? health.CurrentHealth / health.maxHealth : 1f;

        // 한 번에 큰 피해로 두 단계를 건너뛰어도 가장 깊은 페이즈로
        int target = CurrentPhase;
        for (int i = CurrentPhase + 1; i < phases.Count; i++)
            if (ratio <= phases[i].hpThreshold) target = i;
        if (target != CurrentPhase) EnterPhase(target);
    }

    public void EnterPhase(int index)
    {
        index = Mathf.Clamp(index, 0, phases.Count - 1);
        CurrentPhase = index;
        ApplyPhase(index);
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(TransitionRoutine());
        OnPhaseChanged?.Invoke(index);
        AnyPhaseChanged?.Invoke(this, index);
    }

    private void ApplyPhase(int index)
    {
        if (index < 0 || index >= phases.Count) return;
        Phase p = phases[index];
        if (anim != null && p.idleSprite != null) anim.SetSprites(p.idleSprite, new List<SpriteClip>(p.walkClips));
        else if (board != null && p.idleSprite != null)
        {
            SpriteRenderer sr = board.GetComponent<SpriteRenderer>();
            if (sr != null) sr.sprite = p.idleSprite;
        }
        if (ai != null) ai.SetPhaseModifiers(p.moveSpeedMul, p.attackSpeedMul, p.damageMul);
        if (knockback != null) knockback.resistance = p.knockbackResistance;
    }

    private IEnumerator TransitionRoutine()
    {
        if (ai != null) ai.Stun(transitionStun);
        if (health != null) health.invincible = true;
        if (board != null) board.Shake(0.18f, 0.7f);
        StartCoroutine(Shockwave());

        yield return new WaitForSeconds(transitionInvulnerable);
        if (health != null) health.invincible = false;
        transitionRoutine = null;
    }

    // 발밑에서 붉은 원이 퍼져나감
    private IEnumerator Shockwave()
    {
        if (ring == null)
        {
            if (ringMaterial == null) ringMaterial = new Material(Shader.Find("Sprites/Default"));
            GameObject go = new GameObject("PhaseShockwave");
            go.transform.SetParent(transform, false);
            go.layer = 2;
            ring = go.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.positionCount = 56;
            ring.widthMultiplier = 0.25f;
            ring.sharedMaterial = ringMaterial;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
        }
        Vector3 c = transform.position;
        Collider col = GetComponent<Collider>();
        if (col != null) c.y = col.bounds.min.y + 0.08f;
        ring.enabled = true;
        const float dur = 0.7f;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            float k = t / dur;
            float r = Mathf.Lerp(0.5f, shockwaveRadius, 1f - (1f - k) * (1f - k));
            for (int i = 0; i < ring.positionCount; i++)
            {
                float a = i * Mathf.PI * 2f / ring.positionCount;
                ring.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
            Color col2 = shockwaveColor; col2.a *= 1f - k;
            ring.startColor = ring.endColor = col2;
            ring.widthMultiplier = Mathf.Lerp(0.35f, 0.08f, k);
            yield return null;
        }
        ring.enabled = false;
    }

    // 기본 3페이즈 구성 (도구가 처음 붙일 때 사용, 값은 인스펙터에서 조절)
    public void SetupDefaultPhases()
    {
        phases.Clear();
        phases.Add(new Phase { name = "1페이즈", hpThreshold = 1f, moveSpeedMul = 1f, attackSpeedMul = 1f, damageMul = 1f, knockbackResistance = 0.85f });
        phases.Add(new Phase { name = "2페이즈", hpThreshold = 0.6f, moveSpeedMul = 1.15f, attackSpeedMul = 1.25f, damageMul = 1.2f, knockbackResistance = 0.9f });
        phases.Add(new Phase { name = "3페이즈", hpThreshold = 0.3f, moveSpeedMul = 1.3f, attackSpeedMul = 1.5f, damageMul = 1.4f, knockbackResistance = 0.95f });
    }
}
