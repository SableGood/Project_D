using UnityEngine;

// 직업 효과 (JobData의 직업을 실제 수치로). 고른 직업 = GameManager.SelectedJob
//  (v135 시트 '직업' 탭 수정 반영)
//  J01 공돌이     : 포탑 설치·강화 비용 -30% (포탑 관련만, 엘리트 상점은 할인 없음). 무기로 직접 공격 불가 (스킬은 가능)
//  J02 헬스 중독자 : 최대 체력 +50%, 포탑 1개만 설치 (판매는 가능, 70% 환불)
//  J03 돈 많은 백수: 웨이브가 끝날 때마다 0~200 골드 무작위 (전용 재능은 아직 없음)
//  J04 사이보그   : 모든 공격 자동 (자동 조준·사격), 수동 조준 불가 — 자동 조준은 재능 A046과 함께 만들 예정 (지금은 효과 없음)
//  J05 사업가     : 보유 골드 1당 피해 +0.01% (최대 +100% = 10,000 골드). 플레이어·포탑 모두
//  J06 학생       : 포탑·강화·엘리트 상점 비용 -10%, 얻는 골드 -10%
//  J07 졷 윅      : 무기 칸 3개 (기본 2개), 재장전 시간 -20%
//  J08 대통령     : 무기로 직접 공격 불가 (상점에 무기 안 나옴), 경호실장 둘이 따라다니며 자동 사격. 스킬은 사용 가능
public static class Jobs
{
    public const string Engineer = "J01", Gym = "J02", Rich = "J03", Cyborg = "J04",
                        Business = "J05", Student = "J06", Wick = "J07", President = "J08";

    public static string Code
    {
        get
        {
            JobData.Job j = GameManager.Instance != null ? JobData.Get(GameManager.Instance.SelectedJob) : null;
            return j != null ? j.code : "";
        }
    }
    public static bool Is(string code) => Code == code;

    public static float TurretCostMul => Is(Engineer) ? 0.7f : Is(Student) ? 0.9f : 1f;
    public static float ShopPriceMul => Is(Student) ? 0.9f : 1f;
    public static float GoldGainMul => Is(Student) ? 0.9f : 1f;
    public static float MaxHpMul => Is(Gym) ? 1.5f : 1f;
    public static int TurretLimit => Is(Gym) ? 1 : int.MaxValue;
    public const int RichBonusMax = 200;
    public static int WaveBonusGold => Is(Rich) ? Random.Range(0, RichBonusMax + 1) : 0; // 부를 때마다 새로 뽑음
    public static float MoveSpeedMul => 1f;
    public static int WeaponSlots => Is(Wick) ? 3 : 2;
    public static float ReloadTimeMul => Is(Wick) ? 0.8f : 1f;
    // v147: 테스트 모드에서는 직업과 상관없이 무기 사용 가능 (공돌이·대통령으로 무기 테스트할 때 막히지 않게)
    public static bool CanUseWeapons => PlayerWeaponManager.TestModeAllWeapons || (!Is(President) && !Is(Engineer));
    public static bool AutoAim => Is(Cyborg);

    // 사업가: 보유 골드 1당 +0.01% (100골드당 +1%), 최대 +100%
    public const float BusinessGoldPerPct = 100f, BusinessMaxPct = 100f;
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
