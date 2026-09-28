using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class WaveManager : MonoBehaviour
{
    [Header("기본 설정 (공용 스폰 포인트)")]
    public Transform[] defaultSpawnPoints;

    [Header("웨이브 전체 데이터 설정")]
    public List<WaveData> waves;

    [Header("스테이지 지정 (테스트 및 시작 설정)")]
    [Tooltip("게임을 시작할 스테이지 번호를 입력하세요.")]
    public int startStage = 1;

    [Header("현재 진행 상태 (실시간 확인용)")]
    public int currentStage = 1;
    public int currentWaveNumber = 1;

    [Header("UI 연결")]
    public Text waveText;
    public Button waveStartButton;

    private int currentWaveIndex = 0;     // 실제 리스트 인덱스
    private bool isWaveActive = false;
    private int remainingMobCount = 0;

    void Start()
    {
        if (waveText != null) waveText.gameObject.SetActive(false);
        if (waveStartButton != null) waveStartButton.onClick.AddListener(StartWave);

        // 시작 스테이지 지정 로직 실행
        SetStartStage(startStage);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1) && waveStartButton != null && waveStartButton.gameObject.activeSelf)
        {
            StartWave();
        }
    }

    // 인스펙터에서 입력한 스테이지 번호에 맞춰 시작 인덱스를 찾아주는 함수
    private void SetStartStage(int targetStage)
    {
        for (int i = 0; i < waves.Count; i++)
        {
            // 리스트를 순회하며 목표 스테이지의 첫 번째 웨이브를 찾음
            if (waves[i].stageNumber == targetStage)
            {
                currentWaveIndex = i;
                currentStage = targetStage;
                currentWaveNumber = i + 1;
                Debug.Log($"[스테이지 지정] Stage {targetStage} (웨이브 {currentWaveNumber}) 부터 게임을 시작합니다.");
                return;
            }
        }

        // 입력한 스테이지를 찾을 수 없으면 처음부터 시작
        currentWaveIndex = 0;
        if (waves.Count > 0)
        {
            currentStage = waves[0].stageNumber;
            currentWaveNumber = 1;
        }
    }

    public void StartWave()
    {
        if (isWaveActive) return;

        if (currentWaveIndex >= waves.Count)
        {
            Debug.Log("모든 웨이브를 클리어했습니다!");
            return;
        }

        StartCoroutine(WaveRoutine());
    }

    private IEnumerator WaveRoutine()
    {
        isWaveActive = true;
        remainingMobCount = 0;

        if (waveStartButton != null) waveStartButton.gameObject.SetActive(false);

        WaveData currentWaveData = waves[currentWaveIndex];

        // 현재 진행 상태 인스펙터 변수 갱신
        currentStage = currentWaveData.stageNumber;
        currentWaveNumber = currentWaveIndex + 1;

        if (waveText != null)
        {
            waveText.text = $"Stage {currentStage}\n웨이브 {currentWaveNumber} 시작";
            waveText.gameObject.SetActive(true);
        }

        yield return new WaitForSeconds(2.0f);

        if (waveText != null) waveText.gameObject.SetActive(false);

        Transform[] targetSpawnPoints = (currentWaveData.waveSpawnPoints != null && currentWaveData.waveSpawnPoints.Length > 0)
            ? currentWaveData.waveSpawnPoints
            : defaultSpawnPoints;

        foreach (WaveMobInfo info in currentWaveData.mobList)
        {
            remainingMobCount += info.spawnCount;
        }

        Debug.Log($"[Stage {currentStage} - {currentWaveNumber} 웨이브] 총 소환될 몹 마릿수: {remainingMobCount}");

        foreach (WaveMobInfo info in currentWaveData.mobList)
        {
            for (int i = 0; i < info.spawnCount; i++)
            {
                if (targetSpawnPoints == null || targetSpawnPoints.Length == 0)
                {
                    Debug.LogWarning("지정된 스폰 포인트가 없습니다!");
                    break;
                }

                Transform sp = targetSpawnPoints[Random.Range(0, targetSpawnPoints.Length)];

                GameObject spawnedMob = Instantiate(info.mobPrefab, sp.position, sp.rotation);

                MobTracker tracker = spawnedMob.GetComponent<MobTracker>();
                if (tracker == null)
                {
                    tracker = spawnedMob.AddComponent<MobTracker>();
                }
                tracker.Initialize(this);

                yield return new WaitForSeconds(0.5f);
            }
        }
    }

    public void UnregisterMob(GameObject mob)
    {
        if (isWaveActive)
        {
            remainingMobCount--;

            if (remainingMobCount <= 0)
            {
                EndWave();
            }
        }
    }

    private void EndWave()
    {
        isWaveActive = false;
        currentWaveIndex++;

        StartCoroutine(EndWaveRoutine());
    }

    private IEnumerator EndWaveRoutine()
    {
        if (waveText != null)
        {
            waveText.text = "웨이브 종료\n- 정비 시간 -";
            waveText.gameObject.SetActive(true);
        }

        yield return new WaitForSeconds(3.0f);
        if (waveText != null) waveText.gameObject.SetActive(false);

        if (currentWaveIndex < waves.Count)
        {
            if (waveStartButton != null) waveStartButton.gameObject.SetActive(true);
        }
        else
        {
            if (waveText != null)
            {
                waveText.text = "모든 스테이지 클리어!";
                waveText.gameObject.SetActive(true);
            }
        }
    }
}