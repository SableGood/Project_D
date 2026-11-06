using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// v98 새 재능 (재능 분류 탭 C01~C24). PlayerAugments가 플레이어에 자동으로 붙임
//  효과 이름(Augment_Data '효과' 열)과 수치:
//   관통(수치1 관통 수, 수치2 관통마다 피해 감소%) · 도탄(수치1 피해%, 수치2 거리) · 산탄화(수치1 발 수, 수치2 한 발 피해%, 수치3 추가 탄퍼짐)
//   과열(수치1 식는 시간) · 마지막탄(수치1 피해%) · 재장전폭발(수치1 피해, 수치2 반경) · 탄창투척(수치1 피해, 수치2 경직)
//   치명타%(수치1) · 아드레날린(수치1 이동%, 수치2 연사%, 수치3 시간) · 치명처치폭발(수치1 대상 최대 체력%, 수치2 반경)
//   처치폭발(수치1 확률%, 수치2 반경, 수치3 피해) · 볼링핀(수치1 피해) · 처치회복(수치1 회복, 수치2 초당 최대)
//   직접줍기(수치1 골드%) · 현상금(수치1 골드 배수) · 냉각탄(수치1 둔화%, 수치2 시간) · 연쇄번개(수치1 확률%, 수치2 피해%, 수치3 대상 수)
//   포탑명당(수치1 피해%, 수치2 거리) · 오버클럭(수치1 공격속도%, 수치2 시간) · 자폭포탑(수치1 피해, 수치2 반경, 수치3 환급%)
//   잔상(수치1 시간) · 돌진(수치1 피해, 수치2 쿨타임 감소%) · 유리대포(수치1 피해%, 수치2 최대 체력 감소%) · 홈런(수치1 피해, 수치2 거리 배수)
public class TalentEffects : MonoBehaviour
{
    public static TalentEffects Instance { get; private set; }

    public const string Pierce = "관통", Ricochet = "도탄", SpreadShotFx = "산탄화", OverheatFx = "과열", LastShot = "마지막탄",
        ReloadBlast = "재장전폭발", MagThrow = "탄창투척", CritPct = "치명타%", Adrenaline = "아드레날린", CritKillBlast = "치명처치폭발",
        KillBlast = "처치폭발", Bowling = "볼링핀", KillHeal = "처치회복", ManualPickupFx = "직접줍기", Bounty = "현상금",
        FrostShot = "냉각탄", Chain = "연쇄번개", TurretBuddy = "포탑명당", Overclock = "오버클럭", TurretBomb = "자폭포탑",
        Decoy = "잔상", Dash = "돌진", GlassCannon = "유리대포", Homerun = "홈런";

    private static readonly HashSet<string> known = new HashSet<string>
    {
        Pierce, Ricochet, SpreadShotFx, OverheatFx, LastShot, ReloadBlast, MagThrow, CritPct, Adrenaline, CritKillBlast,
        KillBlast, Bowling, KillHeal, ManualPickupFx, Bounty, FrostShot, Chain, TurretBuddy, Overclock, TurretBomb,
        Decoy, Dash, GlassCannon, Homerun
    };
    public static bool IsKnown(string effect) => known.Contains(effect);

    // ---- 합산 값 ----
    private int pierce; private float pierceFall;
    private float ricochetPct, ricochetRange;
    private int pellets; private float pelletPct, pelletSpread;
    private float overheat, lastShotPct, reloadBlastDmg, reloadBlastR, magDmg, magStun;
    private float critBonus, adrMove, adrRate, adrTime, critKillPct, critKillR;
    private float killChance, killR, killDmg, bowlingDmg, killHeal, killHealCap;
    private float pickupPct, bountyMul, frostPct, frostTime, chainChance, chainPct; private int chainCount;
    private float buddyPct, buddyR, overclockPct, overclockTime, bombDmg, bombR, bombRefund;
    private float decoyTime, dashDmg, dashCd, glassDmg, glassHp, homerunDmg, homerunDist;

    // ---- 상태 ----
    private float adrUntil, waveStartTime = -999f, healWindowStart, healInWindow;
    private bool lastWave, lastActing;
    private float glassApplied;
    private float baseRollCd = -1f;
    private int seenCount = -1;
    private readonly HashSet<Health> dashHit = new HashSet<Health>();
    private PlayerAugments pa;
    private Health health;
    private PlayerMovementAbility ability;
    private PlayerWeaponManager weapons;
    private static readonly Collider[] buf = new Collider[32];
    private static int mobMask = -1;
    private static int MobMask => mobMask >= 0 ? mobMask : (mobMask = LayerMask.GetMask("Mob"));

    // 재능이 준 피해 (공격자 없음) 도중인지 → 처치를 플레이어 처치로 셈
    public static bool InTalentDamage { get; private set; }

    // ===== 다른 스크립트가 읽는 값 =====
    private static TalentEffects I => Instance;
    public static int PierceCount => I != null ? I.pierce : 0;
    public static float PierceFalloff => I != null ? Mathf.Clamp01(I.pierceFall / 100f) : 0f;
    public static bool Overheat => I != null && I.overheat > 0f;
    public static float OverheatTime => I != null ? Mathf.Max(0.5f, I.overheat) : 3f;
    public static float LastShotMul => I != null && I.lastShotPct > 0f ? I.lastShotPct / 100f : 1f;
    public static float CritBonus => I != null ? I.critBonus / 100f : 0f;
    public static bool ManualPickup => I != null && I.pickupPct > 0f;
    public static float PickupRadius => 1.6f;
    public static float CoinLife => 20f;
    private static bool AdrActive => I != null && Time.time < I.adrUntil;
    public static float MoveMul => AdrActive ? 1f + I.adrMove / 100f : 1f;
    public static float FireRateMul => AdrActive ? 1f + I.adrRate / 100f : 1f;
    public static float DamageMul
    {
        get
        {
            if (I == null) return 1f;
            float m = 1f + I.glassDmg / 100f;
            if (I.buddyPct > 0f && I.NearTower(I.transform.position)) m *= 1f + I.buddyPct / 100f;
            return m;
        }
    }
    public static float TurretRateMul => I != null && I.overclockPct > 0f && WaveManager.IsWaveActive && Time.time - I.waveStartTime < I.overclockTime ? 1f + I.overclockPct / 100f : 1f;
    public static float TurretNearMul(Transform turret)
    {
        if (I == null || I.buddyPct <= 0f || turret == null) return 1f;
        Vector3 d = turret.position - I.transform.position; d.y = 0f;
        return d.sqrMagnitude <= I.buddyR * I.buddyR ? 1f + I.buddyPct / 100f : 1f;
    }
    public static float KillGoldMul(bool heavy)
    {
        if (I == null) return 1f;
        float m = 1f + I.pickupPct / 100f;
        if (heavy && I.bountyMul > 1f) m *= I.bountyMul;
        return m;
    }
    public static bool SpreadShot(PlayerWeaponData d, out int count, out float dmgMul, out float spread)
    {
        count = 1; dmgMul = 1f; spread = 0f;
        if (I == null || I.pellets <= 1 || !PlayerAugments.IsSemiBoostTarget(d)) return false;
        count = I.pellets; dmgMul = Mathf.Max(0.05f, I.pelletPct / 100f); spread = I.pelletSpread;
        return true;
    }
    // 날아가는 시체가 부딪힌 적에게 줄 피해 (0 = 없음)
    public static float CorpseHitDamage(Transform attacker, bool bigHit, out bool forceFly, out float distMul)
    {
        forceFly = false; distMul = 1f;
        if (I == null) return 0f;
        float dmg = 0f;
        bool byPlayer = attacker != null && attacker == I.transform;
        if (I.homerunDmg > 0f && byPlayer && I.HoldingMelee())
        {
            forceFly = true; distMul = Mathf.Max(1f, I.homerunDist); dmg = I.homerunDmg;
        }
        if (I.bowlingDmg > 0f)
        {
            dmg = Mathf.Max(dmg, I.bowlingDmg);
            if (bigHit) forceFly = true;
        }
        return dmg;
    }

    // ===== 수명 =====
    void Awake()
    {
        Instance = this;
        pa = GetComponent<PlayerAugments>();
        health = GetComponent<Health>();
        ability = GetComponent<PlayerMovementAbility>();
        weapons = GetComponent<PlayerWeaponManager>();
    }

    void OnEnable()
    {
        Health.AnyDamagedBy += OnAnyDamaged;
        Weapon.PlayerReloadStarted += OnReloadStarted;
    }

    void OnDisable()
    {
        Health.AnyDamagedBy -= OnAnyDamaged;
        Weapon.PlayerReloadStarted -= OnReloadStarted;
        if (Instance == this) Instance = null;
    }

    private void Recalculate()
    {
        pierce = 0; pierceFall = 0f; ricochetPct = ricochetRange = 0f; pellets = 0; pelletPct = pelletSpread = 0f;
        overheat = lastShotPct = reloadBlastDmg = reloadBlastR = magDmg = magStun = 0f;
        critBonus = adrMove = adrRate = adrTime = critKillPct = critKillR = 0f;
        killChance = killR = killDmg = bowlingDmg = killHeal = killHealCap = 0f;
        pickupPct = 0f; bountyMul = 1f; frostPct = frostTime = chainChance = chainPct = 0f; chainCount = 0;
        buddyPct = buddyR = overclockPct = overclockTime = bombDmg = bombR = bombRefund = 0f;
        decoyTime = dashDmg = dashCd = glassDmg = glassHp = homerunDmg = homerunDist = 0f;
        if (pa == null) return;
        float p = PlayerAugments.Power;
        foreach (AugmentData a in pa.Acquired)
        {
            float v1 = a.value1, v2 = a.value2, v3 = a.value3;
            switch (a.effect)
            {
                case Pierce: pierce += Mathf.Max(1, Mathf.RoundToInt(v1)); pierceFall = v2; break;
                case Ricochet: ricochetPct += v1 * p; ricochetRange = Mathf.Max(ricochetRange, v2 > 0 ? v2 : 6f); break;
                case SpreadShotFx: pellets = Mathf.Max(pellets, Mathf.RoundToInt(v1)); pelletPct = v2 * p; pelletSpread = v3; break;
                case OverheatFx: overheat = v1 > 0 ? v1 : 3f; break;
                case LastShot: lastShotPct = Mathf.Max(lastShotPct, v1 * p); break;
                case ReloadBlast: reloadBlastDmg += v1 * p; reloadBlastR = Mathf.Max(reloadBlastR, v2 > 0 ? v2 : 2.5f); break;
                case MagThrow: magDmg += v1 * p; magStun = Mathf.Max(magStun, v2); break;
                case CritPct: critBonus += v1 * p; break;
                case Adrenaline: adrMove += v1 * p; adrRate += v2 * p; adrTime = Mathf.Max(adrTime, v3 > 0 ? v3 : 1f); break;
                case CritKillBlast: critKillPct += v1 * p; critKillR = Mathf.Max(critKillR, v2 > 0 ? v2 : 2f); break;
                case KillBlast: killChance += v1 * p; killR = Mathf.Max(killR, v2 > 0 ? v2 : 2f); killDmg = Mathf.Max(killDmg, v3 * p); break;
                case Bowling: bowlingDmg += v1 * p; break;
                case KillHeal: killHeal += v1 * p; killHealCap = Mathf.Max(killHealCap, v2 > 0 ? v2 * p : 5f); break;
                case ManualPickupFx: pickupPct += v1 * p; break;
                case Bounty: bountyMul = Mathf.Max(bountyMul, v1 > 1 ? v1 : 3f); break;
                case FrostShot: frostPct = Mathf.Max(frostPct, v1 * p); frostTime = Mathf.Max(frostTime, v2 > 0 ? v2 : 1f); break;
                case Chain: chainChance += v1 * p; chainPct = Mathf.Max(chainPct, v2 * p); chainCount = Mathf.Max(chainCount, Mathf.RoundToInt(v3 > 0 ? v3 : 3f)); break;
                case TurretBuddy: buddyPct += v1 * p; buddyR = Mathf.Max(buddyR, v2 > 0 ? v2 : 4f); break;
                case Overclock: overclockPct += v1 * p; overclockTime = Mathf.Max(overclockTime, v2 > 0 ? v2 : 10f); break;
                case TurretBomb: bombDmg += v1 * p; bombR = Mathf.Max(bombR, v2 > 0 ? v2 : 3f); bombRefund = Mathf.Max(bombRefund, v3); break;
                case Decoy: decoyTime = Mathf.Max(decoyTime, v1 > 0 ? v1 : 1.5f); break;
                case Dash: dashDmg += v1 * p; dashCd = Mathf.Max(dashCd, v2); break;
                case GlassCannon: glassDmg += v1 * p; glassHp += v2; break;
                case Homerun: homerunDmg += v1 * p; homerunDist = Mathf.Max(homerunDist, v2 > 0 ? v2 : 2.2f); break;
            }
        }

        // 돌진: 구르기 재사용 대기시간 감소
        if (ability != null)
        {
            if (baseRollCd < 0f) baseRollCd = ability.rollCooldown;
            ability.rollCooldown = baseRollCd * Mathf.Clamp01(1f - dashCd / 100f);
        }
        // 유리 대포: 최대 체력 감소 (얻은 만큼만 한 번씩)
        if (health != null)
        {
            float targetCut = Mathf.Clamp(glassHp, 0f, 90f) / 100f;
            if (targetCut > glassApplied + 0.0001f)
            {
                float remain = 1f - glassApplied;
                float mul = (1f - targetCut) / Mathf.Max(0.01f, remain);
                float newMax = Mathf.Max(20f, health.maxHealth * mul);
                health.AddMaxHealth(newMax - health.maxHealth);
                if (health.CurrentHealth > health.maxHealth) health.SetCurrentHealth(health.maxHealth);
                glassApplied = targetCut;
            }
        }
    }

    void Update()
    {
        if (pa != null && pa.Acquired.Count != seenCount) { seenCount = pa.Acquired.Count; Recalculate(); }

        bool wave = WaveManager.IsWaveActive;
        if (wave && !lastWave) waveStartTime = Time.time;
        lastWave = wave;

        // 이동기 시작/진행: 잔상, 돌진
        bool acting = ability != null && ability.IsActing;
        if (acting && !lastActing)
        {
            dashHit.Clear();
            if (decoyTime > 0f) SpawnDecoy(decoyTime);
        }
        if (acting && dashDmg > 0f)
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, 0.9f, buf, MobMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Health h = buf[i].GetComponentInParent<Health>();
                if (h == null || h.IsDead || !dashHit.Add(h)) continue;
                DealTalentDamage(h, dashDmg);
                Vector3 d = h.transform.position - transform.position; d.y = 0f;
                KnockbackReceiver.TryApply(h.transform, d, 1.5f);
            }
        }
        lastActing = acting;
    }

    // ===== 피해 이벤트 =====
    private void OnAnyDamaged(Health victim, float damage, Transform attacker)
    {
        if (victim == null || damage <= 0f || Health.DotTick) return;
        bool byPlayer = attacker != null && attacker == transform;
        bool mine = byPlayer || InTalentDamage;

        // 자폭 포탑
        if (bombDmg > 0f && victim.CurrentHealth <= 0f && !victim.IsDead && IsTower(victim))
        {
            Vector3 c = victim.transform.position;
            StartCoroutine(Blast(c, bombR, bombDmg, 0f));
            if (bombRefund > 0f && GoldManager.Instance != null)
            {
                TowerBuilder tb = FindFirstObjectByType<TowerBuilder>();
                int paid = tb != null ? tb.PaidCostOf(victim.gameObject) : 0;
                int refund = Mathf.RoundToInt(paid * bombRefund / 100f);
                if (refund > 0) { GoldManager.Instance.Add(refund); AugmentManager.Toast($"자폭 포탑! +{refund} 골드"); }
            }
            return;
        }

        if (victim.GetComponent<MobAI>() == null) return;
        bool crit = Health.LastHitCrit;
        bool gun = Weapon.PlayerGunHit;
        bool killing = victim.CurrentHealth <= 0f && !victim.IsDead;

        if (byPlayer && crit && adrTime > 0f) adrUntil = Time.time + adrTime;

        if (byPlayer && gun && !killing)
        {
            if (frostPct > 0f) MobAI.TryApplySlow(victim, frostPct / 100f, frostTime);
        }
        if (byPlayer && gun)
        {
            if (ricochetPct > 0f) DoRicochet(victim, damage * ricochetPct / 100f);
            if (chainChance > 0f && Random.value * 100f < chainChance) ChainLightning(victim, damage * chainPct / 100f);
        }

        if (killing && mine)
        {
            Vector3 c = victim.transform.position;
            if (byPlayer && crit && critKillPct > 0f) StartCoroutine(Blast(c, critKillR, victim.maxHealth * critKillPct / 100f, 0.02f));
            else if (killChance > 0f && Random.value * 100f < killChance) StartCoroutine(Blast(c, killR, killDmg, 0.06f));
            if (killHeal > 0f && health != null)
            {
                if (Time.time - healWindowStart >= 1f) { healWindowStart = Time.time; healInWindow = 0f; }
                float h = Mathf.Min(killHeal, killHealCap - healInWindow);
                if (h > 0f) { health.Heal(h); healInWindow += h; }
            }
        }
    }

    private static bool IsTower(Health h) => h.GetComponent<TurretAI>() != null || h.GetComponent<SupportTower>() != null;

    private bool HoldingMelee()
    {
        Weapon w = weapons != null ? weapons.CurrentWeapon : null;
        return w != null && w.playerWepData != null && !w.playerWepData.UsesAmmo;
    }

    private float nearCacheTime = -999f; private bool nearCache;
    private bool NearTower(Vector3 pos)
    {
        if (Time.time - nearCacheTime < 0.25f) return nearCache;
        nearCacheTime = Time.time;
        nearCache = false;
        int mask = LayerMask.GetMask("Tower");
        if (mask == 0) return false;
        int n = Physics.OverlapSphereNonAlloc(pos, buddyR, buf, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (buf[i].GetComponentInParent<TurretAI>() != null || buf[i].GetComponentInParent<SupportTower>() != null) { nearCache = true; break; }
        return nearCache;
    }

    // ===== 재능 피해 =====
    public static void DealTalentDamage(Health h, float dmg)
    {
        if (h == null || h.IsDead || dmg <= 0f) return;
        bool prev = InTalentDamage;
        InTalentDamage = true;
        h.TakeDamage(dmg, 0f, null);
        InTalentDamage = prev;
    }

    // 폭발 (연쇄 폭발·피의 축제·재장전 폭탄·자폭 포탑). delay: 연쇄가 한 프레임에 다 터지지 않게
    private int blastsThisSecond; private float blastWindow;
    private IEnumerator Blast(Vector3 c, float radius, float dmg, float delay)
    {
        if (Time.time - blastWindow > 1f) { blastWindow = Time.time; blastsThisSecond = 0; }
        if (++blastsThisSecond > 25) yield break; // 연쇄가 끝없이 이어지지 않게
        if (delay > 0f) yield return new WaitForSeconds(delay);
        radius *= PlayerAugments.ExplosionMul;
        dmg *= PlayerAugments.ExplosionMul;
        SkillGrenade.ExplosionFx(c, radius);
        Sfx.Play("Gun/Frag_Explode", 0.5f);
        CameraController.Shake(0.08f, 0.15f);
        int n = Physics.OverlapSphereNonAlloc(c, radius, buf, MobMask, QueryTriggerInteraction.Ignore);
        var list = new List<Health>();
        for (int i = 0; i < n; i++)
        {
            Health h = buf[i].GetComponentInParent<Health>();
            if (h != null && !h.IsDead && !list.Contains(h)) list.Add(h);
        }
        foreach (Health h in list)
        {
            DealTalentDamage(h, dmg);
            Vector3 d = h.transform.position - c; d.y = 0f;
            KnockbackReceiver.TryApply(h.transform, d, 1f);
        }
    }

    // ===== 도탄 =====
    private void DoRicochet(Health from, float dmg)
    {
        Health next = Nearest(from.transform.position, ricochetRange, from, null);
        if (next == null) return;
        Line(from.transform.position + Vector3.up * 0.8f, next.transform.position + Vector3.up * 0.8f, new Color(1f, 0.95f, 0.7f), 0.08f, false);
        DealTalentDamage(next, dmg);
    }

    // ===== 번개 =====
    private void ChainLightning(Health from, float dmg)
    {
        var done = new HashSet<Health> { from };
        Health cur = from;
        for (int k = 0; k < chainCount; k++)
        {
            Health next = Nearest(cur.transform.position, 4.5f, null, done);
            if (next == null) break;
            Line(cur.transform.position + Vector3.up * 0.9f, next.transform.position + Vector3.up * 0.9f, new Color(0.6f, 0.85f, 1f), 0.14f, true);
            done.Add(next);
            DealTalentDamage(next, dmg);
            StatusEffects.Apply(next, StatusType.Stun, 0f, 0.3f);
            cur = next;
        }
    }

    private static Health Nearest(Vector3 p, float r, Health except, HashSet<Health> skip)
    {
        int n = Physics.OverlapSphereNonAlloc(p, r, buf, MobMask, QueryTriggerInteraction.Ignore);
        Health best = null; float bd = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Health h = buf[i].GetComponentInParent<Health>();
            if (h == null || h.IsDead || h == except || (skip != null && skip.Contains(h))) continue;
            float d = (h.transform.position - p).sqrMagnitude;
            if (d < bd) { bd = d; best = h; }
        }
        return best;
    }

    // 선 이펙트 (도탄: 곧은 선, 번개: 지그재그)
    private static Material lineMat;
    private void Line(Vector3 a, Vector3 b, Color col, float life, bool zigzag)
    {
        GameObject go = new GameObject("TalentLine");
        LineRenderer lr = go.AddComponent<LineRenderer>();
        if (lineMat == null) lineMat = new Material(Shader.Find("Sprites/Default"));
        lr.sharedMaterial = lineMat;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.widthMultiplier = zigzag ? 0.09f : 0.05f;
        lr.startColor = lr.endColor = col;
        int pts = zigzag ? 7 : 2;
        lr.positionCount = pts;
        for (int i = 0; i < pts; i++)
        {
            float t = i / (float)(pts - 1);
            Vector3 p = Vector3.Lerp(a, b, t);
            if (zigzag && i > 0 && i < pts - 1) p += Random.insideUnitSphere * 0.25f;
            lr.SetPosition(i, p);
        }
        Destroy(go, life);
    }

    // ===== 재장전 =====
    private void OnReloadStarted(Weapon w)
    {
        if (weapons == null || w != weapons.CurrentWeapon) return;
        if (reloadBlastDmg > 0f) StartCoroutine(Blast(transform.position, reloadBlastR, reloadBlastDmg, 0f));
        if (magDmg > 0f) StartCoroutine(ThrowMag(w));
    }

    private IEnumerator ThrowMag(Weapon w)
    {
        Camera cam = Camera.main;
        Vector3 dir = w.LastFireDirection; dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
        dir.Normalize();
        GameObject go = new GameObject("ThrownMag");
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        SpriteRenderer magSrc = null;
        foreach (SpriteRenderer r in w.GetComponentsInChildren<SpriteRenderer>(true))
            if (r.name.StartsWith("Magazine") && r.sprite != null) { magSrc = r; break; }
        if (magSrc != null) { sr.sprite = magSrc.sprite; go.transform.localScale = magSrc.transform.lossyScale; }
        else { sr.sprite = HitEffects.CasingSprite(HitEffects.CasingType.Rifle); go.transform.localScale = Vector3.one * 2.5f; }
        Vector3 pos = transform.position + Vector3.up * 0.9f;
        float dist = 0f, spin = 0f;
        var hit = new HashSet<Health>();
        while (dist < 9f)
        {
            float step = 16f * Time.deltaTime;
            pos += dir * step; dist += step; spin += 900f * Time.deltaTime;
            go.transform.position = pos;
            if (cam != null) go.transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, spin);
            int n = Physics.OverlapSphereNonAlloc(pos, 0.45f, buf, MobMask, QueryTriggerInteraction.Ignore);
            bool stop = false;
            for (int i = 0; i < n; i++)
            {
                Health h = buf[i].GetComponentInParent<Health>();
                if (h == null || h.IsDead || !hit.Add(h)) continue;
                DealTalentDamage(h, magDmg);
                MobAI ai = h.GetComponent<MobAI>();
                if (ai != null && magStun > 0f) ai.Stun(magStun);
                Sfx.Play("Mob/Mob_HitSound_1", 0.6f);
                stop = true; break;
            }
            if (stop) break;
            yield return null;
        }
        Destroy(go);
    }

    // ===== 잔상 =====
    private void SpawnDecoy(float life)
    {
        PlayerBodyAnimator body = GetComponentInChildren<PlayerBodyAnimator>();
        GameObject root = new GameObject("Decoy");
        root.transform.position = transform.position;
        int towerLayer = LayerMask.NameToLayer("Tower");
        if (towerLayer >= 0) root.layer = towerLayer;
        CapsuleCollider col = root.AddComponent<CapsuleCollider>();
        col.radius = 0.35f; col.height = 1.6f; col.center = Vector3.up * 0.8f;
        Health dh = root.AddComponent<Health>();
        dh.invincible = true;
        if (body != null && body.lowerRenderer != null)
        {
            Transform src = body.lowerRenderer.transform;
            GameObject low = new GameObject("Lower");
            low.transform.SetParent(root.transform, false);
            low.transform.localPosition = src.localPosition;
            low.transform.localScale = src.localScale;
            SpriteRenderer lr = low.AddComponent<SpriteRenderer>();
            lr.sprite = body.lowerRenderer.sprite; lr.flipX = body.lowerRenderer.flipX;
            lr.sharedMaterial = body.lowerRenderer.sharedMaterial;
            lr.color = new Color(0.55f, 0.75f, 1f, 1f);
            Billboard bb = low.AddComponent<Billboard>(); bb.anchorAtFeet = body.lowerRenderer.GetComponent<Billboard>() != null && body.lowerRenderer.GetComponent<Billboard>().anchorAtFeet;
            if (body.upperRenderer != null)
            {
                GameObject up = new GameObject("Upper");
                up.transform.SetParent(low.transform, false);
                up.transform.localPosition = body.upperRenderer.transform.localPosition;
                up.transform.localScale = body.upperRenderer.transform.localScale;
                SpriteRenderer ur = up.AddComponent<SpriteRenderer>();
                ur.sprite = body.upperRenderer.sprite;
                ur.sharedMaterial = body.upperRenderer.sharedMaterial;
                ur.color = lr.color;
            }
        }
        PlayerSkills.DecoyUntil = Time.time + life;
        StartCoroutine(DecoyLife(root, life));
    }

    private IEnumerator DecoyLife(GameObject root, float life)
    {
        float end = Time.time + life;
        var rs = root.GetComponentsInChildren<SpriteRenderer>();
        while (Time.time < end)
        {
            float left = end - Time.time;
            if (left < 0.5f) foreach (var r in rs) if (r != null) r.enabled = ((int)(left / 0.06f)) % 2 == 0;
            yield return null;
        }
        Destroy(root);
    }

    // ===== 현상금 사냥꾼 (MobAI가 죽을 때 호출) =====
    public static void OnMobDied(MobAI mob, bool heavy)
    {
        if (I == null || !heavy || I.bountyMul <= 1f || I.pa == null) return;
        Health h = mob != null ? mob.GetComponent<Health>() : null;
        Transform killer = h != null ? h.LastAttacker : null;
        if (killer != I.transform && !InTalentDamage) return;
        AugmentData self = null;
        foreach (AugmentData a in I.pa.Acquired) if (a.effect == Bounty) { self = a; break; }
        AugmentData pick = AugmentManager.PickRandomFor(I.pa, self);
        if (pick == null) return;
        string note = I.pa.Acquire(pick);
        AugmentManager.Toast($"현상금! '{pick.name}' 획득" + (string.IsNullOrEmpty(note) ? "" : "\n" + note));
    }
}
