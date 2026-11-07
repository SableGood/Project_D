using System.Collections.Generic;
using UnityEngine;

// v125 재능 (Augment_Data A0xx 보류 → 구현). PlayerAugments가 플레이어에 자동으로 붙임
//  효과 이름(Augment_Data '효과' 열)과 수치:
//   스왑패줌(수치1 교체 후 첫 발 추가 피해%)   : 무기 교체 딜레이(1초)가 없어지고, 교체 후 첫 발이 강해짐
//   구르기치명                                 : 구르기(점프 캐릭터는 점프)가 끝나면 다음 한 발이 반드시 치명타
//   시체회복(수치1 시체가 남는 시간, 수치2 회복) : 처치한 적의 시체가 오래 남고, 밟으면 체력 회복
//   소이탄(수치1 화상 초당 피해, 수치2 화상 시간, 수치3 확률%) : 총에 맞은 적이 불탐. 총구 화염·피격 이펙트가 주황색
public class TalentExtra : MonoBehaviour
{
    public static TalentExtra Instance { get; private set; }

    public const string SwapZoom = "스왑패줌", RollCrit = "구르기치명", CorpseHealFx = "시체회복", Incendiary = "소이탄";

    private static readonly HashSet<string> known = new HashSet<string> { SwapZoom, RollCrit, CorpseHealFx, Incendiary };
    public static bool IsKnown(string effect) => known.Contains(effect);

    // 무기 교체 딜레이 (모든 무기 공통, 초)
    public const float BaseSwapDelay = 1f;

    // ---- 합산 값 ----
    private float swapBonus; private bool hasSwapZoom, hasRollCrit;
    private float corpseTime, corpseHeal;
    private float fireDps, fireTime, fireChance;

    // ---- 상태 ----
    private bool pendingRollCrit, pendingSwapBonus, lastActing;
    private int seenCount = -1;
    private PlayerAugments pa;
    private PlayerMovementAbility ability;
    private Health health;

    private static TalentExtra I => Instance;

    // ===== 다른 스크립트가 읽는 값 =====
    public static float SwapDelay => I != null && I.hasSwapZoom ? 0f : BaseSwapDelay;
    public static float CorpseTime => I != null ? I.corpseTime : 0f;
    public static float CorpseHeal => I != null ? I.corpseHeal : 0f;
    public static bool IncendiaryOn => I != null && I.fireDps > 0f;
    public static readonly Color IncendiaryTint = new Color(1f, 0.45f, 0.12f, 1f);
    public static readonly Color IncendiaryFlash = new Color(1f, 0.6f, 0.3f, 1f);
    public static bool RollCritReady => I != null && I.pendingRollCrit;

    // 무기를 바꿨을 때 (PlayerWeaponManager)
    public static void OnWeaponSwapped()
    {
        if (I != null && I.hasSwapZoom && I.swapBonus > 0f) I.pendingSwapBonus = true;
    }

    // 플레이어 무기가 한 번 쏠 때 (Weapon.PerformAttack). forceCrit: 이번 발 반드시 치명타, 반환값: 이번 발 피해 배율
    public static float ConsumeShot(out bool forceCrit)
    {
        forceCrit = false;
        if (I == null) return 1f;
        float m = 1f;
        if (I.pendingRollCrit) { forceCrit = true; I.pendingRollCrit = false; }
        if (I.pendingSwapBonus) { m *= 1f + I.swapBonus / 100f; I.pendingSwapBonus = false; }
        return m;
    }

    // ===== 수명 =====
    void Awake()
    {
        Instance = this;
        pa = GetComponent<PlayerAugments>();
        ability = GetComponent<PlayerMovementAbility>();
        health = GetComponent<Health>();
    }

    void OnEnable() { Health.AnyDamagedBy += OnAnyDamaged; }

    void OnDisable()
    {
        Health.AnyDamagedBy -= OnAnyDamaged;
        if (Instance == this) Instance = null;
    }

    private void Recalculate()
    {
        swapBonus = 0f; hasSwapZoom = hasRollCrit = false;
        corpseTime = corpseHeal = 0f;
        fireDps = fireTime = fireChance = 0f;
        if (pa == null) return;
        float p = PlayerAugments.Power;
        foreach (AugmentData a in pa.Acquired)
        {
            float v1 = a.value1, v2 = a.value2, v3 = a.value3;
            switch (a.effect)
            {
                case SwapZoom: hasSwapZoom = true; swapBonus += (v1 > 0f ? v1 : 20f) * p; break;
                case RollCrit: hasRollCrit = true; break;
                case CorpseHealFx: corpseTime = Mathf.Max(corpseTime, v1 > 0f ? v1 : 20f); corpseHeal += (v2 > 0f ? v2 : 10f) * p; break;
                case Incendiary:
                    fireDps += (v1 > 0f ? v1 : 3f) * p;
                    fireTime = Mathf.Max(fireTime, v2 > 0f ? v2 : 2f);
                    fireChance = Mathf.Max(fireChance, v3 > 0f ? v3 : 100f);
                    break;
            }
        }
        if (!hasRollCrit) pendingRollCrit = false;
        if (!hasSwapZoom) pendingSwapBonus = false;
    }

    void Update()
    {
        if (pa != null && pa.Acquired.Count != seenCount) { seenCount = pa.Acquired.Count; Recalculate(); }

        // 구른닷: 이동기가 끝나는 순간 다음 한 발 치명타 준비
        bool acting = ability != null && ability.IsActing;
        if (hasRollCrit && lastActing && !acting) pendingRollCrit = true;
        lastActing = acting;
    }

    // 소이탄: 플레이어 총에 맞은 몹에 화상
    private void OnAnyDamaged(Health victim, float damage, Transform attacker)
    {
        if (fireDps <= 0f || victim == null || damage <= 0f || Health.DotTick) return;
        if (attacker != transform || !Weapon.PlayerGunHit) return;
        if (victim.GetComponent<MobAI>() == null) return;
        if (fireChance < 100f && Random.value * 100f >= fireChance) return;
        StatusEffects.Apply(victim, StatusType.Burn, fireDps, fireTime);
    }

    // 알뜰살뜰: 시체를 밟으면 회복 (MobDeathFx). 회복했으면 true (시체가 사라짐)
    public static bool TryPickCorpse(Vector3 feet)
    {
        if (I == null || I.corpseHeal <= 0f || I.health == null || I.health.IsDead) return false;
        if (I.health.CurrentHealth >= I.health.maxHealth - 0.01f) return false; // 체력이 가득이면 그대로 둠
        Vector3 d = I.transform.position - feet; d.y = 0f;
        if (d.sqrMagnitude > 0.8f * 0.8f) return false;
        I.health.Heal(I.corpseHeal);
        Sfx.Play("UI/UI_Select_1", 0.5f);
        return true;
    }
}
