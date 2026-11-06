using UnityEngine;

// 난이도 배율 (출격 준비 화면의 쉬움·보통·어려움). 보통 = 시트 수치 그대로
//  - 몹 체력·공격력: 몹이 나올 때(MobAI.OnEnable) 곱함
//  - 몹 수: 웨이브 시작 때(WaveManager) 곱함
//  - 처치 골드: 몹이 죽을 때 곱함 (어려움은 몹이 많아서 낮춰 수입을 비슷하게)
//  - 정비 시간: 웨이브 사이 자동 시작까지의 시간 (첫 정비는 FirstPrepTime)
//  - 어려움 추가 규칙: 엘리트 웨이브에 엘리트 +1, 보스 2페이즈에 MM003 4마리 소환
public static class Difficulty
{
    public static int Index => GameManager.Instance != null ? Mathf.Clamp(GameManager.Instance.SelectedDifficulty, 0, 2) : 1;
    public static bool Hard => Index == 2;

    private static readonly float[] HpMul = { 0.75f, 1f, 1.25f };
    private static readonly float[] DmgMul = { 0.7f, 1f, 1.3f };
    private static readonly float[] CountMul = { 0.85f, 1f, 1.15f };
    private static readonly float[] GoldMul = { 1f, 1f, 0.85f };
    private static readonly float[] Prep = { 20f, 15f, 10f };

    public static float MobHpMul => HpMul[Index];
    public static float MobDamageMul => DmgMul[Index];
    public static float MobCountMul => CountMul[Index];
    public static float KillGoldMul => GoldMul[Index];
    public static float PrepTime => Prep[Index];
    public const float FirstPrepTime = 30f;
}
