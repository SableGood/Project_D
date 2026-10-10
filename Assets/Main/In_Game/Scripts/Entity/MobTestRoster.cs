using UnityEngine;
using System.Collections.Generic;

// 몹 테스트 패널에 버튼으로 표시할 몹 프리팹 목록.
// [Tools/몹/1. 몹 프리팹 정리] 또는 [Tools/몹/2. 테스트 패널 몹 목록 갱신] 실행 시 자동으로 채워집니다.
// 위치: Assets/Resources/Data/MobTestRoster.asset  (Resources에서 실행 중에 불러옴)
[CreateAssetMenu(fileName = "MobTestRoster", menuName = "Data/Mob Test Roster")]
public class MobTestRoster : ScriptableObject
{
    public List<GameObject> mobs = new List<GameObject>();
}
