using UnityEngine;

// 직업 (게임 시작 시 캐릭터와 함께 고름). 시트 '직업' 탭 기준
//  효과는 Jobs.cs (고른 직업 = GameManager.SelectedJob)
public static class JobData
{
    public class Job
    {
        public string code, name, pros, cons, iconFile;
        private Sprite icon; private bool loaded;
        public Sprite Icon
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    icon = Resources.Load<Sprite>("Image/UI/Jobs/" + iconFile);
                    if (icon != null) icon.texture.filterMode = FilterMode.Point;
                }
                return icon;
            }
        }
    }

    public static readonly Job[] All =
    {
        new Job { code = "J01", name = "공돌이",     iconFile = "J01_Engineer",  pros = "포탑 구매·업그레이드 비용 -20%",              cons = "최대 체력 -30%" },
        new Job { code = "J02", name = "헬스 중독자", iconFile = "J02_Gym",       pros = "최대 체력 +50%",                              cons = "포탑을 1개만 설치 (판매는 가능)" },
        new Job { code = "J03", name = "돈 많은 백수", iconFile = "J03_Rich",      pros = "웨이브가 끝날 때마다 실업급여 40 골드",       cons = "전용 재능과 능력이 없음" },
        new Job { code = "J04", name = "공무원",     iconFile = "J04_Officer",   pros = "어쩌면 좋은 재능을 얻을지도..? (아직 효과 없음)", cons = "" },
        new Job { code = "J05", name = "사업가",     iconFile = "J05_Business",  pros = "보유 골드 50당 피해 +1% (최대 +40%). 포탑 피해에도 적용", cons = "" },
        new Job { code = "J06", name = "학생",       iconFile = "J06_Student",   pros = "포탑·업그레이드·엘리트 상점 비용 -10%",       cons = "획득 골드 -10%" },
        new Job { code = "J07", name = "군인",       iconFile = "J07_Soldier",   pros = "무기 슬롯 +1, 이동속도 +5%",                  cons = "" },
        new Job { code = "J08", name = "대통령",     iconFile = "J08_President", pros = "자동으로 공격하는 경호실장 둘을 데리고 다님", cons = "무기로 직접 공격할 수 없음 (무기 구매 불가). 스킬은 사용 가능" },
    };

    public static Job Get(int index) => index >= 0 && index < All.Length ? All[index] : null;
}
