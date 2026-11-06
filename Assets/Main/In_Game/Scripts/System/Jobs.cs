using UnityEngine;

// 직업 효과 (JobData의 직업을 실제 수치로). 고른 직업 = GameManager.SelectedJob
//  J01 공돌이     : 포탑 설치·강화 비용 -20%, 최대 체력 -30%
//  J02 헬스 중독자 : 최대 체력 +50%, 포탑 1개만 설치 (판매는 가능)
//  J03 돈 많은 백수: 웨이브가 끝날 때마다 +40 골드 (전용 재능은 아직 없음)
//  J04 공무원     : 아직 효과 없음 (관련 재능 기획 예정)
//  J05 사업가     : 보유 골드 50당 피해 +1% (최대 +40% = 2,000 골드). 플레이어·포탑 모두
//  J06 학생       : 포탑·강화·엘리트 상점 비용 -10%, 얻는 골드 -10%
//  J07 군인       : 무기 칸 3개 (기본 2개), 이동속도 +5%
//  J08 대통령     : 무기로 직접 공격 불가 (상점에 무기 안 나옴), 경호실장 둘이 따라다니며 자동 사격. 스킬은 사용 가능
public static class Jobs
{
    public const string Engineer = "J01", Gym = "J02", Rich = "J03", Officer = "J04",
                        Business = "J05", Student = "J06", Soldier = "J07", President = "J08";

    public static string Code
    {
        get
        {
            JobData.Job j = GameManager.Instance != null ? JobData.Get(GameManager.Instance.SelectedJob) : null;
            return j != null ? j.code : "";
        }
    }
    public static bool Is(string code) => Code == code;

    public static float TurretCostMul => Is(Engineer) ? 0.8f : Is(Student) ? 0.9f : 1f;
    public static float ShopPriceMul => Is(Student) ? 0.9f : 1f;
    public static float GoldGainMul => Is(Student) ? 0.9f : 1f;
    public static float MaxHpMul => Is(Engineer) ? 0.7f : Is(Gym) ? 1.5f : 1f;
    public static int TurretLimit => Is(Gym) ? 1 : int.MaxValue;
    public static int WaveBonusGold => Is(Rich) ? 40 : 0;
    public static float MoveSpeedMul => Is(Soldier) ? 1.05f : 1f;
    public static int WeaponSlots => Is(Soldier) ? 3 : 2;
    public static bool CanUseWeapons => !Is(President);

    // 사업가: 보유 골드 50당 +1%, 최대 +40%
    public const float BusinessGoldPerPct = 50f, BusinessMaxPct = 40f;
    public static float DamageMul
    {
        get
        {
            if (!Is(Business) || GoldManager.Instance == null) return 1f;
            return 1f + Mathf.Min(BusinessMaxPct, GoldManager.Instance.Gold / BusinessGoldPerPct) / 100f;
        }
    }

    // 직업을 반영한 가격 (올림)
    public static int TurretPrice(int baseCost) => Mathf.CeilToInt(baseCost * TurretCostMul);
    public static int ShopPrice(int baseCost) => Mathf.CeilToInt(baseCost * ShopPriceMul);
}
