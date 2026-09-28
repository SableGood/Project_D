using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

// 인스펙터 창에서 몹 종류와 마릿수를 설정하기 위한 데이터 구조체
[System.Serializable]
public class MobSpawnInfo
{
    public GameObject mobPrefab; // 생성할 몹 프리팹
    public int spawnCount;       // 생성할 마릿수
}

// 각 웨이브의 상세 데이터를 담는 구조체
[System.Serializable]
public class WaveData
{
    [Header("해당 웨이브 전용 스폰 포인트 (비워두면 기본값 사용)")]
    public Transform[] waveSpawnPoints;

    [Header("이 웨이브에 등장할 몹 구성 리스트")]
    public List<MobSpawnInfo> mobList;
}

public class WaveManager : MonoBehaviour
{
    [Header("기본 설정 (공용 스폰 포인트)")]
    public Transform[] defaultSpawnPoints;

    [Header("웨이브 전체 데이터 설정")]
    public List<WaveData> waves;

    [Header("UI 연결")]
    public Text waveText;
    public Button waveStartButton;

    private int currentWaveIndex = 0;     // 현재 진행 중인 웨이브 번호 (인덱스)
    private bool isWaveActive = false;    // 웨이브가 진행 중인지 여부
    private int remainingMobCount = 0;    // 현재 웨이브에서 살아남은 몹의 총 마릿수
    private bool isSpawningFinished = false; // 모든 몹의 소환이 끝났는지 여부

    void Start()
    {
        // 게임 시작 시 웨이브 안내 텍스트 비활성화
        if (waveText != null) waveText.gameObject.SetActive(false);

        // 버튼 클릭 시 웨이브 시작 함수가 실행되도록 이벤트 리스너 등록
        if (waveStartButton != null) waveStartButton.onClick.AddListener(StartWave);
    }

    void Update()
    {
        // F1 키를 누르고, 시작 버튼이 화면에 활성화되어 있을 때만 웨이브 시작 가능
        if (Input.GetKeyDown(KeyCode.F1) && waveStartButton != null && waveStartButton.gameObject.activeSelf)
        {
            StartWave();
        }
    }

    // 웨이브를 수동으로 시작하는 함수
    public void StartWave()
    {
        // 이미 웨이브가 진행 중이면 중복 실행 방지
        if (isWaveActive) return;

        // 모든 웨이브를 다 깼다면 더 이상 진행하지 않음
        if (currentWaveIndex >= waves.Count)
        {
            Debug.Log("모든 웨이브를 클리어했습니다!");
            return;
        }

        // 코루틴을 통해 웨이브 루프 시작
        StartCoroutine(WaveRoutine());
    }

    // 웨이브의 전체 흐름을 제어하는 코루틴 (시간차 소환 및 연출 담당)
    private IEnumerator WaveRoutine()
    {
        isWaveActive = true;
        isSpawningFinished = false;
        remainingMobCount = 0;

        // 웨이브가 시작되었으므로 시작 버튼을 화면에서 숨김
        if (waveStartButton != null) waveStartButton.gameObject.SetActive(false);

        // 웨이브 시작 안내 텍스트 출력
        if (waveText != null)
        {
            waveText.text = (currentWaveIndex + 1) + " 웨이브 시작";
            waveText.gameObject.SetActive(true);
        }

        // 2초 동안 대기
        yield return new WaitForSeconds(2.0f);

        // 텍스트 숨기기
        if (waveText != null) waveText.gameObject.SetActive(false);

        // 현재 웨이브의 데이터 가져오기
        WaveData currentWaveData = waves[currentWaveIndex];

        // 전용 스폰 포인트가 등록되어 있다면 그것을 쓰고, 없으면 기본 스폰 포인트 사용
        Transform[] targetSpawnPoints = (currentWaveData.waveSpawnPoints != null && currentWaveData.waveSpawnPoints.Length > 0)
            ? currentWaveData.waveSpawnPoints
            : defaultSpawnPoints;

        // 소환 전 총 몹 마릿수 미리 계산 (종료 조건 판별용)
        foreach (MobSpawnInfo info in currentWaveData.mobList)
        {
            remainingMobCount += info.spawnCount;
        }

        Debug.Log($"[{currentWaveIndex + 1} 웨이브] 총 소환될 몹 마릿수: {remainingMobCount}");

        // 몹 구성 리스트를 순회하며 순차적으로 소환
        foreach (MobSpawnInfo info in currentWaveData.mobList)
        {
            for (int i = 0; i < info.spawnCount; i++)
            {
                // 스폰 포인트가 아예 지정되지 않았다면 에러 방지를 위해 중단
                if (targetSpawnPoints == null || targetSpawnPoints.Length == 0)
                {
                    Debug.LogWarning("지정된 스폰 포인트가 없습니다!");
                    break;
                }

                // 무작위 스폰 포인트 선택
                Transform sp = targetSpawnPoints[Random.Range(0, targetSpawnPoints.Length)];

                // 몹 생성
                GameObject spawnedMob = Instantiate(info.mobPrefab, sp.position, sp.rotation);

                // 생성된 몹에 트래커 컴포넌트를 붙여서 자신(WaveManager)을 인식시킴
                MobTracker tracker = spawnedMob.GetComponent<MobTracker>();
                if (tracker == null)
                {
                    tracker = spawnedMob.AddComponent<MobTracker>();
                }
                tracker.Initialize(this);

                // 몹들이 겹쳐서 스폰되지 않도록 0.5초 간격 대기
                yield return new WaitForSeconds(0.5f);
            }
        }

        // 모든 소환 과정이 완료되었음을 마킹
        isSpawningFinished = true;
    }

    // 몹이 파괴될 때(MobTracker를 통해) 호출되는 최적화된 콜백 함수
    public void UnregisterMob(GameObject mob)
    {
        // 웨이브 진행 중일 때만 카운트 감소 (소환 중 파괴되는 예외 상황 방어)
        if (isWaveActive)
        {
            remainingMobCount--;
            Debug.Log($"몹 처치됨. 남은 몹 마릿수: {remainingMobCount}");

            // 남은 몹이 0마리 이하가 되면 웨이브 종료
            if (remainingMobCount <= 0)
            {
                EndWave();
            }
        }
    }

    // 웨이브 종료 처리 함수
    private void EndWave()
    {
        isWaveActive = false;
        currentWaveIndex++; // 다음 웨이브 인덱스로 이동

        StartCoroutine(EndWaveRoutine());
    }

    // 정비 시간 연출 및 다음 웨이브 준비 코루틴
    private IEnumerator EndWaveRoutine()
    {
        // 정비 시간 안내 텍스트 출력
        if (waveText != null)
        {
            waveText.text = "웨이브 종료\n- 정비 시간 -";
            waveText.gameObject.SetActive(true);
        }

        // 3초간 정비 시간 유지
        yield return new WaitForSeconds(3.0f);
        if (waveText != null) waveText.gameObject.SetActive(false);

        // 모든 웨이브를 클리어한 것이 아니라면 시작 버튼을 다시 활성화
        if (currentWaveIndex < waves.Count)
        {
            if (waveStartButton != null) waveStartButton.gameObject.SetActive(true);
        }
        else
        {
            // 모든 웨이브 클리어 시 연출
            if (waveText != null)
            {
                waveText.text = "모든 웨이브 클리어!";
                waveText.gameObject.SetActive(true);
            }
        }
    }
}