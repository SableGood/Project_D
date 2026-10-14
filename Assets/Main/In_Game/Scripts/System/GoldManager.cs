using UnityEngine;

// 골드(재화) 관리
//  - 시작 골드 지급, 몹 처치 보상, 웨이브 클리어 보너스, 포탑 건설 비용 차감
//  - 씬에 없으면 처음 쓰일 때 자동으로 만들어집니다. (값을 바꾸고 싶으면 씬에 빈 오브젝트를 만들고 이 컴포넌트를 붙이세요)
//
// 몹 처치 보상: Mob_Data.csv의 '비용' 칸 (몹 줄에 숫자를 넣으면 그 값, 비어 있으면 아래 기본값)
// 포탑 건설 비용: Mob_Data.csv의 '비용' 칸 (TR 줄)
public class GoldManager : MonoBehaviour
{
    private static GoldManager instance;
    private static bool quitting;

    public static GoldManager Instance
    {
        get
        {
            if (instance == null && !quitting)
            {
                instance = FindFirstObjectByType<GoldManager>();
                if (instance == null) instance = new GameObject("GoldManager").AddComponent<GoldManager>();
                instance.Init();
            }
            return instance;
        }
    }

    // 플레이 시작마다 초기화 (에디터 설정에 따라 static 값이 남아 있을 수 있음)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { instance = null; quitting = false; }

    [Header("골드")]
    public int startGold = 300;
    [Tooltip("웨이브를 클리어할 때마다 받는 보너스")]
    public int waveClearBonus = 50;

    [Header("몹 처치 보상 기본값 (Mob_Data '비용' 칸이 비어 있을 때)")]
    public int defaultMobReward = 10;     // MM, MR
    public int defaultEliteReward = 30;   // ME
    public int defaultBossReward = 100;   // MB

    public int Gold { get; private set; }

    // 골드가 바뀔 때 (현재 골드, 변화량)
    public event System.Action<int, int> OnGoldChanged;
    // 골드가 부족해 실패했을 때 (필요한 골드)
    public event System.Action<int> OnGoldShort;

    private bool initialized;

    void Awake()
    {
        if (instance == null) instance = this;
        else if (instance != this) { Destroy(this); return; }
        Init();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void OnApplicationQuit() { quitting = true; }

    private void Init()
    {
        if (initialized) return;
        initialized = true;
        Gold = startGold;
    }

    public bool CanAfford(int amount) => amount <= 0 || Gold >= amount;

    // 골드 사용. 부족하면 false (골드는 그대로)
    public bool TrySpend(int amount)
    {
        if (amount <= 0) return true;
        if (Gold < amount)
        {
            OnGoldShort?.Invoke(amount);
            return false;
        }
        Gold -= amount;
        OnGoldChanged?.Invoke(Gold, -amount);
        return true;
    }

    public void Add(int amount)
    {
        if (amount == 0) return;
        Gold = Mathf.Max(0, Gold + amount);
        OnGoldChanged?.Invoke(Gold, amount);
    }

    // 골드 부족 알림만 보냄 (HUD 깜빡임용)
    public void NotifyShort(int needed) => OnGoldShort?.Invoke(needed);

    // 몹 처치 보상
    public int GetKillReward(EntityData data)
    {
        if (data != null && data.cost > 0) return data.cost;
        string code = data != null ? data.entityCode : "";
        if (!string.IsNullOrEmpty(code))
        {
            if (code.StartsWith("MB")) return defaultBossReward;
            if (code.StartsWith("ME")) return defaultEliteReward;
        }
        return defaultMobReward;
    }
}
