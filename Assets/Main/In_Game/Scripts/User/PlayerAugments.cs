using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 플레이어가 얻은 재능과 그 효과 (AugmentManager가 플레이어에 자동으로 붙임)
//  - 능력치 효과는 정적 배율(DamageMul 등)로 Weapon / PlayerController 가 읽어 감
//  - 특수 효과(흡혈, 피해 반사, 부활, 폭발탄 …)는 Health.AnyDamagedBy 이벤트로 처리
//  ※ 코드 안의 Augment(증강) = 게임 용어 '재능'
public class PlayerAugments : MonoBehaviour
{
    public static PlayerAugments Instance { get; private set; }

    // ===== 효과 종류 (구글 시트 '효과' 열에 쓰는 이름) =====
    public const string MaxHp = "최대체력", DefenseFx = "방어력", DamagePct = "공격력%", FireRatePct = "공격속도%",
        ReloadPct = "재장전%", MoveSpeedPct = "이동속도%", Regen = "체력재생", Lifesteal = "흡혈%", Reflect = "피해반사",
        StandStill = "정지보너스", MoveHeal = "이동회복", NoHitBonus = "무피격보너스", AutoReloadFx = "자동재장전",
        Revive = "부활", Explosive = "폭발탄", Normalize = "정상화", RandomFx = "무작위",
        BlockTrap = "블럭함정", SemiBoost = "단발강화", PowerUp = "위력증가", Rooted = "뿌리박기",
        BoomUp = "폭발강화";
    // 스킬을 주는 효과(은신 / 고폭수류탄 / 파편수류탄)는 PlayerSkills 참고

    private static readonly HashSet<string> Known = new HashSet<string>
    {
        MaxHp, DefenseFx, DamagePct, FireRatePct, ReloadPct, MoveSpeedPct, Regen, Lifesteal, Reflect,
        StandStill, MoveHeal, NoHitBonus, AutoReloadFx, Revive, Explosive, Normalize, RandomFx,
        BlockTrap, SemiBoost, PowerUp, Rooted, BoomUp,
        PlayerSkills.Cloak, PlayerSkills.HeGrenade, PlayerSkills.FragGrenade
    };
    public static bool IsKnownEffect(string effect) => !string.IsNullOrEmpty(effect) && Known.Contains(effect.Trim());

    // ===== 다른 스크립트가 읽는 값 (재능이 없으면 기본값) =====
    public static float DamageMul => Instance != null ? 1f + (Instance.dmgPct + Instance.bonusDmgPct + Instance.NoHitPct) / 100f : 1f;
    public static float FireRateMul => Instance != null ? Mathf.Max(0.1f, 1f + (Instance.ratePct + Instance.NoHitPct) / 100f) : 1f;
    public static float ReloadTimeMul => Instance != null ? Mathf.Clamp(1f - Instance.reloadPct / 100f, 0.2f, 3f) : 1f;
    public static float MoveSpeedMul => Instance == null ? 1f : Instance.rooted ? 0f : Mathf.Max(0.2f, 1f + (Instance.movePct + Instance.NoHitPct) / 100f);
    public static bool AutoReload => Instance != null && Instance.autoReload;
    // 포탑 (뿌리 박기)
    public static float TurretDamageMul => Instance != null ? 1f + Instance.turretDmgPct / 100f : 1f;
    public static float TurretFireRateMul => Instance != null ? 1f + Instance.turretRatePct / 100f : 1f;
    // 위력증가 배율 (스킬 피해에도 곱함)
    public static float Power => Instance != null ? Instance.power : 1f;
    // 예술은 폭발이다: 플레이어 폭발(폭발탄·수류탄·파편)의 범위와 피해, 투척 스킬 재사용 대기시간
    public static float ExplosionMul => Instance != null ? 1f + Instance.boomPct / 100f : 1f;
    public static float ThrowCooldownMul => Instance != null ? Mathf.Clamp(1f - Instance.throwCdPct / 100f, 0.2f, 1f) : 1f;

    // 칼로리 소비: 이 무기만 연사력 증가 + 단발 고정
    public static bool ForcedSemi(PlayerWeaponData d) => Instance != null && d != null && Instance.semiBoost.ContainsKey(d.weaponID);
    public static float WeaponFireRateMul(PlayerWeaponData d)
    {
        if (Instance == null || d == null || !Instance.semiBoost.TryGetValue(d.weaponID, out float pct)) return 1f;
        return 1f + pct * Instance.power / 100f;
    }
    // 칼로리 소비를 받을 수 있는 무기: 권총·기관단총·돌격소총·기관총·전투소총 (근접·저격·산탄 제외)
    public static bool IsSemiBoostTarget(PlayerWeaponData d)
    {
        if (d == null || !d.UsesAmmo) return false;
        string role = d.role ?? "";
        return !role.Contains("저격") && !role.Contains("산탄") && !role.Contains("근접") && d.projectileCount <= 1;
    }

    // 얻은 재능 (얻은 순서대로, 중복은 여러 번)
    private readonly List<AugmentData> acquired = new List<AugmentData>();
    public IReadOnlyList<AugmentData> Acquired => acquired;
    public int StackOf(string code) { int n = 0; foreach (AugmentData a in acquired) if (a.code == code) n++; return n; }

    // 합산 값 (위력증가 배율이 곱해진 값)
    private float flatMaxHp, flatDef, dmgPct, ratePct, reloadPct, movePct, regen, lifesteal;
    private float reflectChance, reflectPct, standDef, standRegen, moveHeal, stopDamage;
    private float explodeChance, explodeRadius, explodeDmgPct, revivePct;
    private float noHitBasePct, noHitStepPct;
    private float blockRate, blockLife, blockDamage;
    private float turretDmgPct, turretRatePct;
    private float boomPct, throwCdPct;
    private float power = 1f; // 위력증가 (이 정도의 위력을?)
    private int reviveStacks;
    private bool autoReload, rooted;
    private readonly Dictionary<string, float> semiBoost = new Dictionary<string, float>(); // 무기 코드 → 연사력 %
    // 사라지지 않는 보너스 (정상화)
    private float bonusDmgPct, bonusMaxHp;

    private int reviveUsed;
    private int noHitWaves;           // 안 맞으면 되는걸: 맞지 않고 클리어한 웨이브 수
    private bool hitThisWave;
    private float appliedMaxHp;
    private float stillTime, stopDamageTimer, blockTimer;

    // 안 맞으면 되는걸: 기본 % + 무피격 웨이브마다 % (공격력·공격속도·이동속도)
    private float NoHitPct => noHitBasePct > 0f || noHitStepPct > 0f ? noHitBasePct + noHitStepPct * noHitWaves : 0f;

    private Health health;
    private PlayerController controller;
    private PlayerSkills skills;
    public PlayerSkills Skills => skills;

    private const float StandStillDelay = 0.5f;   // 이만큼 멈춰 있으면 정지 보너스
    private const float StopDamageGrace = 1.5f;   // 이동회복: 웨이브 중 이만큼 멈춰 있으면 피해 시작
    private const float ExplosionStun = 0.5f;
    private const float BlockStun = 1f, BlockRadius = 0.45f, BlockRehit = 1f;

    void Awake()
    {
        Instance = this;
        health = GetComponent<Health>();
        controller = GetComponent<PlayerController>();
        skills = GetComponent<PlayerSkills>();
        if (skills == null) skills = gameObject.AddComponent<PlayerSkills>();
    }

    void OnEnable() { Health.AnyDamagedBy += OnAnyDamaged; if (health != null) health.BeforeDeath = TryRevive; }
    void OnDisable()
    {
        Health.AnyDamagedBy -= OnAnyDamaged;
        if (health != null && health.BeforeDeath == (System.Func<Health, bool>)TryRevive) health.BeforeDeath = null;
        if (Instance == this) Instance = null;
        foreach (Block b in blocks) if (b.go != null) Destroy(b.go);
        blocks.Clear();
    }

    // ===== 재능 얻기 =====
    // 돌려주는 값: 화면에 보여줄 추가 안내 (없으면 null)
    // skillSlot: 스킬 재능일 때 고른 칸 (PlayerSkills 칸 번호, -1 = 빈 칸에 자동)
    public string Acquire(AugmentData a, int skillSlot = -1)
    {
        if (a == null) return null;
        string note = null;
        switch (a.effect)
        {
            case Normalize:
            {
                int lost = acquired.Count;
                acquired.Clear();
                semiBoost.Clear();
                if (skills != null) skills.ClearAll();
                noHitWaves = 0;
                bonusDmgPct += a.value1 * lost;
                bonusMaxHp += a.value2 * lost;
                note = $"재능 {lost}개를 잃고 공격력 +{a.value1 * lost:0.#}%, 최대 체력 +{a.value2 * lost:0.#}";
                break;
            }
            case RandomFx:
            {
                AugmentData pick = AugmentManager.PickRandomFor(this, a);
                if (pick != null)
                {
                    acquired.Add(a);
                    string inner = Acquire(pick);
                    Recalculate();
                    return $"'{pick.name}' 획득!" + (inner != null ? "\n" + inner : "");
                }
                note = "더 얻을 수 있는 재능이 없습니다";
                break;
            }
            case SemiBoost:
            {
                PlayerWeaponData d = CurrentWeaponData();
                if (d != null && IsSemiBoostTarget(d))
                {
                    semiBoost[d.weaponID] = (semiBoost.TryGetValue(d.weaponID, out float v) ? v : 0f) + a.value1;
                    note = $"'{d.inGameName}' 연사력 +{a.value1:0.#}%, 단발 사격만 가능";
                }
                break;
            }
            case PlayerSkills.Cloak:
            case PlayerSkills.HeGrenade:
            case PlayerSkills.FragGrenade:
            {
                int slot = skills != null ? skills.Grant(a, skillSlot) : -1;
                note = slot >= 0 ? $"{PlayerSkills.SlotKeyNames[slot]} 키로 사용" : "빈 스킬 칸이 없어 사용할 수 없습니다";
                break;
            }
            case NoHitBonus:
                hitThisWave = false;
                note = "맞지 않고 웨이브를 클리어할 때마다 더 강해집니다";
                break;
        }
        acquired.Add(a);
        Recalculate();
        return note;
    }

    // 칼로리 소비를 지금 고를 수 있는지 (들고 있는 무기가 대상이고 아직 안 받았을 때)
    public bool CanTakeSemiBoost()
    {
        PlayerWeaponData d = CurrentWeaponData();
        return d != null && IsSemiBoostTarget(d) && !semiBoost.ContainsKey(d.weaponID);
    }

    private PlayerWeaponData CurrentWeaponData()
    {
        PlayerWeaponManager pwm = GetComponent<PlayerWeaponManager>();
        return pwm != null && pwm.CurrentWeapon != null ? pwm.CurrentWeapon.playerWepData : null;
    }

    // 웨이브를 클리어했을 때 (AugmentManager가 호출). 돌려주는 값: 화면 안내
    public string OnWaveCleared()
    {
        string note = null;
        if (noHitBasePct > 0f || noHitStepPct > 0f)
        {
            if (!hitThisWave)
            {
                noHitWaves++;
                note = $"무피격 클리어! 능력치 +{NoHitPct:0.#}%";
            }
        }
        hitThisWave = false;
        return note;
    }

    private void Recalculate()
    {
        flatMaxHp = flatDef = dmgPct = ratePct = reloadPct = movePct = regen = lifesteal = 0f;
        reflectChance = reflectPct = standDef = standRegen = moveHeal = stopDamage = 0f;
        explodeChance = explodeRadius = explodeDmgPct = revivePct = 0f;
        noHitBasePct = noHitStepPct = 0f;
        blockRate = blockLife = blockDamage = 0f;
        turretDmgPct = turretRatePct = 0f;
        boomPct = throwCdPct = 0f;
        reviveStacks = 0;
        autoReload = rooted = false;

        // 위력증가는 다른 재능의 수치에 곱해짐
        float powerPct = 0f;
        foreach (AugmentData a in acquired) if (a.effect == PowerUp) powerPct += a.value1;
        power = 1f + powerPct / 100f;
        float p = power;

        foreach (AugmentData a in acquired)
        {
            switch (a.effect)
            {
                case MaxHp: flatMaxHp += a.value1 * p; break;
                case DefenseFx: flatDef += a.value1 * p; break;
                case DamagePct: dmgPct += a.value1 * p; break;
                case FireRatePct: ratePct += a.value1 * p; break;
                case ReloadPct: reloadPct += a.value1 * p; break;
                case MoveSpeedPct: movePct += a.value1 * p; break;
                case Regen: regen += a.value1 * p; break;
                case Lifesteal: lifesteal += a.value1 * p; break;
                case Reflect: reflectChance += a.value1 * p; reflectPct = Mathf.Max(reflectPct, a.value2 * p); break;
                case StandStill: standDef += a.value1 * p; standRegen += a.value2 * p; break;
                case MoveHeal: moveHeal += a.value1 * p; stopDamage = Mathf.Max(stopDamage, a.value2); break;
                case NoHitBonus: noHitBasePct += a.value1 * p; noHitStepPct += a.value2 * p; break;
                case AutoReloadFx: autoReload = true; break;
                case Revive: reviveStacks++; revivePct = Mathf.Max(revivePct, a.value1 * p); break;
                case Explosive:
                    explodeChance += a.value1 * p;
                    explodeRadius = Mathf.Max(explodeRadius, a.value2);
                    explodeDmgPct = Mathf.Max(explodeDmgPct, a.value3 * p);
                    break;
                case BlockTrap:
                    blockRate += a.value1;
                    blockLife = Mathf.Max(blockLife, a.value2);
                    blockDamage = Mathf.Max(blockDamage, a.value3 * p);
                    break;
                case Rooted: rooted = true; turretDmgPct += a.value1 * p; turretRatePct += a.value2 * p; break;
                case BoomUp: boomPct += a.value1 * p; throwCdPct += a.value2 * p; break;
            }
        }

        if (health != null)
        {
            float targetMaxHp = flatMaxHp + bonusMaxHp;
            float delta = targetMaxHp - appliedMaxHp;
            if (Mathf.Abs(delta) > 0.001f) health.AddMaxHealth(delta);
            appliedMaxHp = targetMaxHp;
            health.bonusDefense = flatDef + (stillTime >= StandStillDelay ? standDef : 0f);
        }
    }

    void Update()
    {
        UpdateBlocks();
        if (health == null || health.IsDead || Time.timeScale <= 0f) return;
        float dt = Time.deltaTime;
        bool moving = controller != null && controller.IsMoving;
        stillTime = moving ? 0f : stillTime + dt;
        bool standing = stillTime >= StandStillDelay;

        health.bonusDefense = flatDef + (standing ? standDef : 0f);

        float heal = regen + (standing ? standRegen : 0f) + (moving ? moveHeal : 0f);
        if (heal > 0f) health.Heal(heal * dt);

        // 이동회복의 대가: 웨이브 중 오래 멈춰 있으면 1초마다 피해 (방어력 무시)
        if (stopDamage > 0f && WaveManager.IsWaveActive && stillTime >= StopDamageGrace)
        {
            stopDamageTimer += dt;
            if (stopDamageTimer >= 1f) { stopDamageTimer -= 1f; health.TakeDamage(stopDamage, 9999f, null); }
        }
        else stopDamageTimer = 0f;

        // 침묵의 암살자: 이동 중 블럭 떨어뜨리기
        if (blockRate > 0f && moving)
        {
            blockTimer += dt;
            float interval = 1f / Mathf.Max(0.1f, blockRate);
            if (blockTimer >= interval) { blockTimer -= interval; DropBlock(); }
        }
    }

    // ===== 피격 이벤트 =====
    private void OnAnyDamaged(Health victim, float damage, Transform attacker)
    {
        if (victim == null || damage <= 0f) return;

        if (victim == health)
        {
            // 안 맞으면 되는걸: 맞으면 쌓인 보너스 초기화
            if (attacker != null) { hitThisWave = true; noHitWaves = 0; }
            // 피해 반사: 확률적으로 받은 피해의 일부를 공격자에게 (방어력 무시, 연쇄 방지를 위해 공격자 없음으로)
            if (reflectChance > 0f && attacker != null && attacker != transform && Random.value * 100f < reflectChance)
            {
                Health enemy = attacker.GetComponent<Health>();
                if (enemy != null && !enemy.IsDead) enemy.TakeDamage(damage * reflectPct / 100f, 9999f, null);
            }
            return;
        }

        if (attacker != transform) return; // 아래는 플레이어가 준 피해만

        if (lifesteal > 0f) health.Heal(damage * lifesteal / 100f);

        if (explodeChance > 0f && Random.value * 100f < explodeChance)
            Explode(victim.transform.position, damage * Mathf.Max(0f, explodeDmgPct) / 100f * ExplosionMul, Mathf.Max(0.5f, explodeRadius) * ExplosionMul);
    }

    private int MobMask()
    {
        PlayerWeaponManager pwm = GetComponent<PlayerWeaponManager>();
        int mask = pwm != null && pwm.CurrentWeapon != null ? pwm.CurrentWeapon.targetLayer.value : 0;
        return mask != 0 ? mask : LayerMask.GetMask("Mob");
    }

    private readonly Collider[] hits = new Collider[32];
    private void Explode(Vector3 center, float damage, float radius)
    {
        var done = new HashSet<Health>();
        int n = Physics.OverlapSphereNonAlloc(center, radius, hits, MobMask(), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Health h = hits[i].GetComponentInParent<Health>();
            if (h == null || h == health || h.IsDead || !done.Add(h)) continue;
            if (damage > 0f) h.TakeDamage(damage, 0f, null); // 공격자 없음: 폭발이 또 폭발을 부르지 않게
            MobAI ai = h.GetComponent<MobAI>();
            if (ai != null) ai.Stun(ExplosionStun);
        }

        // 연출: 둥글게 퍼지는 연기
        for (int i = 0; i < 10; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized;
            Vector3 v = new Vector3(d.x, 0.4f, d.y) * radius * 2.2f;
            HitEffects.Smoke(center + Vector3.up * 0.3f, v, radius * 0.45f, 0.45f, 0.85f, 0.2f);
        }
    }

    // ===== 침묵의 암살자: 바닥 블럭 =====
    private class Block { public GameObject go; public Vector3 pos; public float until; public Dictionary<Health, float> lastHit = new Dictionary<Health, float>(); }
    private readonly List<Block> blocks = new List<Block>();
    private static Material blockMat;

    private void DropBlock()
    {
        Vector3 pos = transform.position;
        if (Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 5f, ~((1 << 2) | MobMask() | LayerMask.GetMask("Player")), QueryTriggerInteraction.Ignore))
            pos = hit.point;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "TalentBlock";
        Destroy(go.GetComponent<Collider>());
        go.transform.position = pos + Vector3.up * 0.08f;
        go.transform.localScale = new Vector3(0.28f, 0.16f, 0.28f);
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 90f), 0f);
        if (blockMat == null)
        {
            Shader s = Shader.Find("Unlit/Color");
            blockMat = new Material(s != null ? s : Shader.Find("Standard"));
            blockMat.color = new Color(0.55f, 0.42f, 0.28f);
        }
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = blockMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        blocks.Add(new Block { go = go, pos = pos, until = Time.time + Mathf.Max(0.5f, blockLife) });
    }

    private void UpdateBlocks()
    {
        if (blocks.Count == 0) return;
        int mask = MobMask();
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            Block b = blocks[i];
            if (Time.time >= b.until || b.go == null)
            {
                if (b.go != null) Destroy(b.go);
                blocks.RemoveAt(i);
                continue;
            }
            int n = Physics.OverlapSphereNonAlloc(b.pos, BlockRadius, hits, mask, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++)
            {
                Health h = hits[k].GetComponentInParent<Health>();
                if (h == null || h == health || h.IsDead) continue;
                if (b.lastHit.TryGetValue(h, out float t) && Time.time < t + BlockRehit) continue;
                b.lastHit[h] = Time.time;
                if (blockDamage > 0f) h.TakeDamage(blockDamage, 0f, null);
                MobAI ai = h.GetComponent<MobAI>();
                if (ai != null) ai.Stun(BlockStun);
            }
        }
    }

    // ===== 부활 =====
    private bool TryRevive(Health h)
    {
        if (reviveStacks - reviveUsed <= 0) return false;
        reviveUsed++;
        h.SetCurrentHealth(h.maxHealth * Mathf.Clamp(revivePct, 1f, 100f) / 100f);
        h.ignoreBaseDefense = true; // 부활의 대가: 기본 방어력 0
        StartCoroutine(ReviveInvincible(h));
        AugmentManager.Toast("영웅은 죽지 않아요! 부활 (기본 방어력 0)");
        return true;
    }

    private IEnumerator ReviveInvincible(Health h)
    {
        bool prev = h.invincible;
        h.invincible = true;
        yield return new WaitForSeconds(1.5f);
        h.invincible = prev;
    }
}
