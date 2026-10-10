using UnityEngine;
using System.Collections.Generic;

// 몹 생성 정보를 담는 구조체 (여기서 WaveMobInfo가 정의됩니다)
[System.Serializable]
public class WaveMobInfo
{
    public GameObject mobPrefab; // 생성할 몹 프리팹
    public int spawnCount;       // 마릿수
    [Tooltip("Wave_Data.csv의 Spawn Point 번호 (1부터). 0이면 기본 스폰 포인트 중 무작위")]
    public int spawnPointIndex;  // 스폰 포인트 번호
}

// 웨이브 하나의 전체 정보를 담는 ScriptableObject 데이터 파일 (에러 해결의 핵심)
[CreateAssetMenu(fileName = "NewWaveData", menuName = "Data/WaveData")]
public class WaveData : ScriptableObject
{
    [Header("스테이지 정보")]
    public int stageNumber = 1; // ★ 추가됨: 이 웨이브가 속한 스테이지 번호

    [Header("해당 웨이브 전용 스폰 포인트 (비워두면 기본값)")]
    public Transform[] waveSpawnPoints;

    [Header("스폰 목록 (자동 생성됨)")]
    public List<WaveMobInfo> mobList = new List<WaveMobInfo>();
}