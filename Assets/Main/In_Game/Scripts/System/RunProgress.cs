using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 한 판 동안 쌓이는 진행 상태 (씬을 새로 불러오면 처음으로 돌아감)
//  - 해금한 포탑 (엘리트 상점). 처음에는 StartTurrets만
//  - 두 갈래 중 하나를 사서 잠긴 포탑 단계
//  - 선택창에 나왔지만 고르지 않은 재능 (그 판에서 다시 나오지 않음)
public static class RunProgress
{
    public static readonly string[] StartTurrets = { "TR010", "TR020" };

    private static readonly HashSet<string> turrets = new HashSet<string>();
    private static readonly HashSet<string> lockedBranches = new HashSet<string>();
    private static readonly HashSet<string> passedTalents = new HashSet<string>();
    private static readonly HashSet<string> shopCategories = new HashSet<string>(); // v113: 엘리트 상점에서 얻은 무기 분류 (SMG, AR …)

    // 바뀔 때마다 올라감 (HUD가 다시 그릴지 판단)
    public static int Version { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        Reset();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) Reset();
    }

    public static void Reset()
    {
        turrets.Clear();
        foreach (string c in StartTurrets) turrets.Add(c);
        lockedBranches.Clear();
        passedTalents.Clear();
        shopCategories.Clear();
        Version++;
    }

    // ── 포탑 ──
    public static bool IsTurretUnlocked(string code) => !string.IsNullOrEmpty(code) && turrets.Contains(code);
    public static void UnlockTurret(string code) { if (!string.IsNullOrEmpty(code) && turrets.Add(code)) Version++; }
    public static bool IsBranchLocked(string code) => lockedBranches.Contains(code);
    public static void LockBranch(string code) { if (!string.IsNullOrEmpty(code) && lockedBranches.Add(code)) Version++; }

    // ── 재능 ──
    public static bool IsTalentPassed(string code) => passedTalents.Contains(code);
    public static void PassTalent(string code) { if (!string.IsNullOrEmpty(code)) passedTalents.Add(code); }

    // ── 엘리트 상점 무기 분류 (v113) ── 이 분류의 무기를 상점에서 사거나 강화하면, 같은 분류의 '해금' 무기가 상점에 나옴
    public static bool HasShopCategory(string cat) => !string.IsNullOrEmpty(cat) && shopCategories.Contains(cat.Trim());
    public static void AddShopCategory(string cat) { if (!string.IsNullOrEmpty(cat) && shopCategories.Add(cat.Trim())) Version++; }
}
