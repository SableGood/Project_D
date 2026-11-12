using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

// 웨이브 진행 (v91: 시간제 웨이브)
//  v151 무한 생존: 웨이브 목록 끝까지 가면 처음부터 다시 (LoopCount +1 → Difficulty가 몹 체력·공격력·수를 올림)
//    · 점수 = 생존 시간 (SurvivalTime, v157: 화면 오른쪽 위). 게임 오버 때 최고 기록(PlayerPrefs "BestSurvival")과 함께 보여 줌
//  - 정비 시간: 첫 웨이브 전 30초, 이후 난이도별(쉬움 20 / 보통 15 / 어려움 10초). 시간이 다 되면 자동 시작
//    · 일찍 시작하면 남은 1초당 3골드
//  - 웨이브 길이: 스테이지 안 순서대로 20·25·30·35·40·45·65초
//    · 처음 80% 동안 스폰 포인트마다 2~3초 간격으로 3~6마리씩 나옴 (동시에 살아 있는 몹은 최대 60)
//    · 시간이 끝나면 남은 몹은 골드 없이 사라지고 웨이브 종료. 다 잡으면 바로 종료
//    · 보스 웨이브는 보스를 잡을 때까지 계속
//  - 몹 수 × 난이도 배율. 어려움이면 엘리트 웨이브에 엘리트 +1
//  - 클리어 보너스 = (50 + 15 × (웨이브 번호-1)) × 직업 골드 배율 + 직업 웨이브 보너스(백수)
public class WaveManager : MonoBehaviour
{
    public static WaveManager Instance { get; private set; }

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

    [Header("시간제 웨이브")]
    [Tooltip("스테이지 안 웨이브 순서별 길이(초). 모자라면 마지막 값")]
    public float[] waveDurations = { 20f, 25f, 30f, 35f, 40f, 45f, 65f };
    [Tooltip("웨이브 길이 중 몹이 나오는 구간 비율")]
    [Range(0.3f, 1f)] public float spawnWindowRatio = 0.8f;
    public Vector2 groupInterval = new Vector2(2f, 3f);
    public Vector2Int groupSize = new Vector2Int(3, 6);
    [Tooltip("동시에 살아 있을 수 있는 몹 수")]
    public int maxAlive = 60;
    [Tooltip("일찍 시작할 때 남은 1초당 골드")]
    public int earlyStartGoldPerSec = 3;
    [Tooltip("웨이브마다 늘어나는 클리어 보너스")]
    public int waveClearBonusStep = 15;

    private int currentWaveIndex = 0;     // 실제 리스트 인덱스
    private bool isWaveActive = false;
    public static bool IsWaveActive { get; private set; } // 웨이브 진행 중
    private readonly HashSet<GameObject> alive = new HashSet<GameObject>();
    private readonly HashSet<GameObject> bosses = new HashSet<GameObject>();
    private bool despawning;
    private bool spawningDone;
    private bool bossWave;
    private float waveEndTime;
    private float waveDuration;
    private Coroutine waveRoutine;

    // 정비 시간
    private bool prepActive;
    private float prepRemaining;
    private Text buttonLabel;
    private Text timerText;
    private int lastShownSec = -1;
    // v157 생존 시간(오른쪽 위) + 버전 + 웨이브 시작 카운트다운(버튼 오른쪽)
    private Text clockText, versionText, countText;
    private RectTransform countRt;
    private float countPulse = -99f;
    public static int CountdownAlertSec = 10;   // 이 초부터 카운트다운 강조

    public int AliveCount => alive.Count;

    // v151 무한 생존
    public static int LoopCount { get; private set; }      // 웨이브 목록을 몇 바퀴 돌았는지
    public static int TotalWave { get; private set; }      // 지금까지 시작한 웨이브 수 (화면 표시)
    public static float SurvivalTime { get; private set; } // 생존 시간 (초)
    public const string BestKey = "BestSurvival";

    void Awake()
    {
        Instance = this;
        LoopCount = 0; TotalWave = 0; SurvivalTime = 0f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        // v106: 인스펙터 Waves 목록의 빈 칸(None)은 건너뜀 (8번째 칸이 비어 있어 8웨이브 시작 시 오류가 났음)
        if (waves == null) waves = new List<WaveData>();
        int removed = waves.RemoveAll(w => w == null);
        if (removed > 0) Debug.LogWarning($"[WaveManager] Waves 목록에 비어 있는 칸 {removed}개를 건너뜁니다. 인스펙터에서 정리해 주세요.");
        IsWaveActive = false;
        if (waveText != null) waveText.gameObject.SetActive(false);
        StyleWaveText();
        if (waveStartButton != null) waveStartButton.onClick.AddListener(StartWave);
        MenuSkin.SkinWaveStartButton(waveStartButton); // 왼쪽 위 고정 + UI 디자인 통일
        if (waveStartButton != null) buttonLabel = waveStartButton.GetComponentInChildren<Text>(true);
        CreateTimerText();

        // 시작 스테이지 지정 로직 실행
        SetStartStage(startStage);

        // 재능 선택 (엘리트·보스 웨이브 클리어 후). 씬에 없으면 자동으로 만듦
        AugmentManager.EnsureExists();

        // 몹 테스트 패널 (Wave Start 버튼 아래)
        SyncTestPanel(); // v105: 몹 테스트 패널은 테스트 모드에서만

        BeginPrep(Difficulty.FirstPrepTime);
    }

    private bool testPanelTried;
    private void SyncTestPanel()
    {
        bool want = showMobTestPanel && PlayerWeaponManager.TestModeAllWeapons;
        if (want && MobTestPanel.Instance == null && !testPanelTried) { testPanelTried = true; MobTestPanel.Create(waveStartButton); }
        else if (!want && MobTestPanel.Instance != null) { Destroy(MobTestPanel.Instance.gameObject); testPanelTried = false; }
    }

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.IsRunning) SurvivalTime += Time.deltaTime;
        SyncTestPanel();
        if (Input.GetKeyDown(KeyCode.F1) && waveStartButton != null && waveStartButton.gameObject.activeSelf)
        {
            StartWave();
        }

        // v105: 테스트 모드에서는 정비 시간이 줄지 않음 → 웨이브는 Wave Start(F1)를 눌러야 시작
        if (prepActive && !AugmentManager.IsOpen && !PlayerWeaponManager.TestModeAllWeapons)
        {
            prepRemaining -= Time.deltaTime;
            if (prepRemaining <= 0f) { prepRemaining = 0f; StartWave(); }
        }
        UpdateTimerUI();
    }

    // 인스펙터에서 입력한 스테이지 번호에 맞춰 시작 인덱스를 찾아주는 함수
    private void SetStartStage(int targetStage)
    {
        for (int i = 0; i < waves.Count; i++)
        {
            if (waves[i].stageNumber == targetStage)
            {
                currentWaveIndex = i;
                currentStage = targetStage;
                currentWaveNumber = i + 1;
                Debug.Log($"[스테이지 지정] Stage {targetStage} (웨이브 {currentWaveNumber}) 부터 게임을 시작합니다.");
                return;
            }
        }

        currentWaveIndex = 0;
        if (waves.Count > 0)
        {
            currentStage = waves[0].stageNumber;
            currentWaveNumber = 1;
        }
    }

    // ===== 정비 시간 =====
    private void BeginPrep(float seconds)
    {
        if (currentWaveIndex >= waves.Count) { prepActive = false; return; }
        prepActive = true;
        prepRemaining = Mathf.Max(0f, seconds);
        lastShownSec = -1;
        if (waveStartButton != null) waveStartButton.gameObject.SetActive(true);
    }

    private int EarlyStartBonus => prepActive ? Mathf.FloorToInt(prepRemaining) * earlyStartGoldPerSec : 0;

    public void StartWave()
    {
        if (isWaveActive) return;

        if (currentWaveIndex >= waves.Count)
        {
            Debug.Log("모든 웨이브를 클리어했습니다!");
            return;
        }

        int early = EarlyStartBonus;
        prepActive = false;
        if (early > 0 && GoldManager.Instance != null)
        {
            GoldManager.Instance.Add(early);
            Debug.Log($"[웨이브] 조기 시작 보너스 +{early} 골드");
        }

        waveRoutine = StartCoroutine(WaveRoutine(early));
    }

    // 스테이지 안 웨이브 번호에 맞는 길이
    private float DurationFor(int waveInStage)
    {
        if (waveDurations == null || waveDurations.Length == 0) return 30f;
        return waveDurations[Mathf.Clamp(waveInStage - 1, 0, waveDurations.Length - 1)];
    }

    private static bool IsType(WaveData w, string type) => w != null && string.Equals((w.waveType ?? "").Trim(), type, System.StringComparison.OrdinalIgnoreCase);
    private static bool PrefabIs(GameObject prefab, string prefix) => prefab != null && prefab.name.Replace("Ent_", "").StartsWith(prefix);

    private IEnumerator WaveRoutine(int earlyBonus)
    {
        isWaveActive = true;
        IsWaveActive = true;
        TotalWave++;
        spawningDone = false;
        despawning = false;
        alive.Clear();
        bosses.Clear();

        if (waveStartButton != null) waveStartButton.gameObject.SetActive(false);

        WaveData wave = waves[currentWaveIndex];
        currentStage = wave.stageNumber;
        currentWaveNumber = currentWaveIndex + 1;
        int waveInStage = WaveNumberInStage(currentWaveIndex);
        bossWave = IsType(wave, "Boss");
        bool eliteWave = IsType(wave, "Elite");
        waveDuration = DurationFor(waveInStage);

        // 웨이브 시작 문구는 표시하지 않음 (v92). 위쪽 타이머가 웨이브 번호·남은 시간을 보여줌
        if (waveText != null) waveText.gameObject.SetActive(false);

        // v151 요새 맵: 웨이브별 스폰 포인트는 섬 맵 기준이라 무시하고 요새 스폰 위치(기본 스폰 포인트)만 씀
        Transform[] points = (!FortLayout.Active && wave.waveSpawnPoints != null && wave.waveSpawnPoints.Length > 0) ? wave.waveSpawnPoints : defaultSpawnPoints;
        if (points == null || points.Length == 0)
        {
            Debug.LogError("지정된 스폰 포인트가 없습니다! WaveManager의 Default Spawn Points를 확인하세요.");
            if (waveText != null) waveText.gameObject.SetActive(false);
            isWaveActive = false;
            IsWaveActive = false;
            BeginPrep(Difficulty.PrepTime);
            yield break;
        }

        // 스폰 포인트별 대기열 (몹 수 × 난이도, 어려움이면 엘리트 +1). 보스·엘리트는 맨 앞
        var queues = new Dictionary<int, List<GameObject>>();
        bool addedHardElite = false;
        int total = 0;
        foreach (WaveMobInfo info in wave.mobList)
        {
            if (info == null || info.mobPrefab == null || info.spawnCount <= 0) continue;
            bool boss = PrefabIs(info.mobPrefab, "MB");
            bool elite = PrefabIs(info.mobPrefab, "ME");
            int count = (boss || elite) ? info.spawnCount : Mathf.Max(1, Mathf.RoundToInt(info.spawnCount * Difficulty.MobCountMul));
            if (elite && eliteWave && Difficulty.Hard && !addedHardElite) { count++; addedHardElite = true; }
            int key = Mathf.Max(0, info.spawnPointIndex);
            if (!queues.TryGetValue(key, out var q)) queues[key] = q = new List<GameObject>();
            for (int i = 0; i < count; i++)
            {
                if (boss || elite) q.Insert(0, info.mobPrefab);
                else q.Insert(Random.Range(0, q.Count + 1), info.mobPrefab);
            }
            total += count;
        }
        // 보스·엘리트가 맨 앞에 오도록 (무작위로 섞인 일반 몹 뒤로 밀리지 않게)
        foreach (var q in queues.Values)
            q.Sort((a, b) => Rank(a).CompareTo(Rank(b)));

        Debug.Log($"[Stage {currentStage} - {waveInStage} 웨이브] {waveDuration:0}초 · 총 {total}마리" + (bossWave ? " · 보스 웨이브" : ""));

        waveEndTime = Time.time + waveDuration;
        float spawnEnd = Time.time + waveDuration * spawnWindowRatio;

        // 무리 지어 스폰
        while (true)
        {
            int left = 0;
            foreach (var q in queues.Values) left += q.Count;
            if (left == 0) break;

            float interval = Random.Range(groupInterval.x, groupInterval.y);
            int ticksLeft = Mathf.Max(1, Mathf.FloorToInt((spawnEnd - Time.time) / interval) + 1);

            foreach (var kv in queues)
            {
                List<GameObject> q = kv.Value;
                if (q.Count == 0) continue;
                int size = Mathf.Max(Random.Range(groupSize.x, groupSize.y + 1), Mathf.CeilToInt(q.Count / (float)ticksLeft));
                size = Mathf.Min(size, q.Count);
                Transform sp = GetSpawnPoint(points, kv.Key);
                for (int i = 0; i < size; i++)
                {
                    if (alive.Count >= maxAlive) break;
                    GameObject prefab = q[0];
                    q.RemoveAt(0);
                    SpawnMob(prefab, sp.position, sp.rotation, PrefabIs(prefab, "MB"));
                }
            }

            yield return new WaitForSeconds(interval);
            if (!isWaveActive) yield break;
            if (Time.time >= waveEndTime && !(bossWave && bosses.Count > 0)) { waveRoutine = null; FinishWave(true); yield break; }
        }
        spawningDone = true;
        CheckWaveEnd();

        // 시간 끝날 때까지 대기 (다 잡으면 CheckWaveEnd가 먼저 끝냄)
        while (isWaveActive)
        {
            if (Time.time >= waveEndTime && !(bossWave && bosses.Count > 0))
            {
                waveRoutine = null;
                FinishWave(true);
                yield break;
            }
            yield return null;
        }
    }

    private static int Rank(GameObject p) => PrefabIs(p, "MB") ? 0 : PrefabIs(p, "ME") ? 1 : 2;

    private void SpawnMob(GameObject prefab, Vector3 center, Quaternion rot, bool boss)
    {
        GameObject mob = PoolManager.Instance.Spawn(prefab, GetScatteredPosition(center), rot);
        if (mob == null) return;
        MobTracker tracker = mob.GetComponent<MobTracker>();
        if (tracker == null) tracker = mob.AddComponent<MobTracker>();
        tracker.Initialize(this);
        alive.Add(mob);
        if (boss) bosses.Add(mob);
    }

    // 웨이브 도중 추가 소환 (보스 졸개 등). 웨이브에 포함되어 시간이 끝나면 함께 사라짐
    public void SpawnExtra(string mobCode, int count, Vector3 center, float radius = 3f)
    {
        GameObject prefab = FindMobPrefab(mobCode);
        if (prefab == null) { Debug.LogWarning($"[웨이브] 추가 소환할 몹 {mobCode}을(를) 웨이브 데이터에서 찾지 못했습니다."); return; }
        for (int i = 0; i < count; i++)
        {
            if (alive.Count >= maxAlive + 10) break;
            Vector2 r = Random.insideUnitCircle.normalized * radius;
            SpawnMob(prefab, center + new Vector3(r.x, 0f, r.y), Quaternion.identity, false);
        }
    }

    public GameObject FindMobPrefab(string mobCode)
    {
        foreach (WaveData w in waves)
        {
            if (w == null) continue;
            foreach (WaveMobInfo info in w.mobList)
                if (info != null && info.mobPrefab != null && info.mobPrefab.name.Replace("Ent_", "") == mobCode) return info.mobPrefab;
        }
        return null;
    }

    [Header("테스트")]
    [Tooltip("Wave Start 아래에 몹 테스트 패널 표시 (F2로 숨기기). 출시 빌드에서는 끄세요.")]
    public bool showMobTestPanel = true;

    [Header("스폰 흩뿌리기")]
    [Tooltip("같은 스폰 포인트에서 나오는 몹들이 겹치지 않도록 퍼뜨리는 반경")]
    public float spawnScatterRadius = 1.5f;

    // 스폰 포인트 번호(1부터)에 해당하는 지점. 이름이 "..._번호"로 끝나는 오브젝트를 우선 찾고, 없으면 목록 순서로
    private Transform GetSpawnPoint(Transform[] points, int index)
    {
        if (index > 0)
        {
            foreach (Transform p in points)
                if (p != null && p.name.EndsWith("_" + index)) return p;
            if (index - 1 < points.Length && points[index - 1] != null) return points[index - 1];
            Debug.LogWarning($"스폰 포인트 {index}번을 찾을 수 없어 무작위 지점에서 스폰합니다.");
        }
        return points[Random.Range(0, points.Length)];
    }

    // 스폰 지점 주변의 무작위 위치 (네비메시 위로 보정)
    private Vector3 GetScatteredPosition(Vector3 center)
    {
        Vector2 r = Random.insideUnitCircle * spawnScatterRadius;
        Vector3 pos = center + new Vector3(r.x, 0f, r.y);
        if (UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit hit, 3f, UnityEngine.AI.NavMesh.AllAreas))
            return hit.position;
        return center;
    }

    // 스테이지 안에서 몇 번째 웨이브인지 (1부터)
    private int WaveNumberInStage(int index)
    {
        int stage = waves[index] != null ? waves[index].stageNumber : 1;
        int n = 0;
        for (int i = 0; i <= index; i++) if (waves[i] != null && waves[i].stageNumber == stage) n++;
        return n;
    }

    public void UnregisterMob(GameObject mob)
    {
        alive.Remove(mob);
        bool wasBoss = bosses.Remove(mob);
        if (!isWaveActive || despawning) return;
        if (bossWave && wasBoss && bosses.Count == 0) { FinishWave(false); return; } // 보스 처치 = 웨이브 종료
        CheckWaveEnd();
    }

    private void CheckWaveEnd()
    {
        if (isWaveActive && spawningDone && alive.Count == 0) FinishWave(false);
    }

    // 웨이브 종료. timeUp: 시간이 다 되어 끝남 (남은 몹은 골드 없이 사라짐)
    private void FinishWave(bool timeUp)
    {
        if (!isWaveActive) return;
        isWaveActive = false;
        IsWaveActive = false;
        if (waveRoutine != null) { StopCoroutine(waveRoutine); waveRoutine = null; }

        int leftover = alive.Count;
        DespawnAll();

        int clearedIndex = currentWaveIndex;
        currentWaveIndex++;
        if (currentWaveIndex >= waves.Count && waves.Count > 0)
        {
            // v151 무한: 처음부터 다시, 한 바퀴마다 강해짐
            currentWaveIndex = 0;
            LoopCount++;
            Debug.Log($"[무한 생존] 웨이브 {waves.Count}개를 모두 넘김 → {LoopCount + 1}바퀴째 (몹 체력 x{Difficulty.MobHpMul:0.##})");
        }
        StartCoroutine(EndWaveRoutine(clearedIndex, timeUp && leftover > 0 ? leftover : 0));
    }

    // 남은 몹을 골드 없이 제거 (풀로 돌려보냄)
    private void DespawnAll()
    {
        despawning = true;
        var list = new List<GameObject>(alive);
        foreach (GameObject m in list)
        {
            if (m == null || !m.activeInHierarchy) continue;
            if (m.TryGetComponent(out PooledObject po)) po.ReleaseToPool();
            else m.SetActive(false);
        }
        alive.Clear();
        bosses.Clear();
        despawning = false;
    }

    private int ClearBonus(int waveInStage)
    {
        int baseBonus = GoldManager.Instance != null ? GoldManager.Instance.waveClearBonus : 50;
        float b = (baseBonus + waveClearBonusStep * (waveInStage - 1)) * Jobs.GoldGainMul;
        return Mathf.RoundToInt(b) + Jobs.WaveBonusGold;
    }

    private IEnumerator EndWaveRoutine(int cleared, int leftover)
    {
        int waveInStage = cleared >= 0 && cleared < waves.Count ? WaveNumberInStage(cleared) : 1;
        int bonus = ClearBonus(waveInStage);
        if (bonus > 0 && GoldManager.Instance != null) GoldManager.Instance.Add(bonus);

        if (waveText != null)
        {
            string head = leftover > 0 ? $"시간 종료 (남은 몹 {leftover}마리 퇴각)" : "웨이브 종료";
            waveText.text = bonus > 0 ? $"{head}\n<size=28><color=#FFD54A>+{bonus} 골드</color>  ·  정비 시간</size>" : $"{head}\n<size=28>정비 시간</size>";
            waveText.gameObject.SetActive(true);
        }

        bool offer = cleared >= 0 && cleared < waves.Count && AugmentManager.Instance != null
                     && AugmentManager.Instance.WillOffer(waves[cleared], waveInStage);
        yield return new WaitForSeconds(offer ? 1.2f : 3.0f);
        if (waveText != null) waveText.gameObject.SetActive(false);

        if (cleared >= 0 && cleared < waves.Count && AugmentManager.Instance != null)
            yield return AugmentManager.Instance.OfferAfterWave(waves[cleared], waveInStage);

        if (currentWaveIndex < waves.Count)
        {
            BeginPrep(Difficulty.PrepTime);
        }
        else if (waveText != null)
        {
            waveText.text = "모든 스테이지 클리어!";
            waveText.gameObject.SetActive(true);
        }
    }

    // 화면 가운데 안내 문구 (웨이브 종료 등): 씬의 기본 Text가 얇은 검은 글씨라 게임 UI와 같은 글꼴·흰 글씨·외곽선으로
    private void StyleWaveText()
    {
        if (waveText == null) return;
        UIFont.Apply(waveText, FontStyle.Bold);
        waveText.color = Color.white;
        waveText.fontSize = Mathf.Max(waveText.fontSize, 40);
        waveText.alignment = TextAnchor.MiddleCenter;
        waveText.supportRichText = true;
        waveText.lineSpacing = 1.05f;
        waveText.raycastTarget = false;
        waveText.horizontalOverflow = HorizontalWrapMode.Overflow;
        waveText.verticalOverflow = VerticalWrapMode.Overflow;
        if (waveText.GetComponent<Outline>() == null)
        {
            Outline o = waveText.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.9f);
            o.effectDistance = new Vector2(2.5f, -2.5f);
        }
        if (waveText.GetComponent<Shadow>() == null || waveText.GetComponents<Shadow>().Length < 2)
        {
            Shadow sh = waveText.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.5f);
            sh.effectDistance = new Vector2(0f, -4f);
        }
    }

    // ===== 타이머 UI =====
    private void CreateTimerText()
    {
        Canvas canvas = waveText != null ? waveText.canvas : (waveStartButton != null ? waveStartButton.GetComponentInParent<Canvas>() : null);
        if (canvas == null) return;
        GameObject go = new GameObject("WaveTimer", typeof(RectTransform));
        go.transform.SetParent(canvas.rootCanvas.transform, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -14f);
        rt.sizeDelta = new Vector2(1000f, 40f);
        timerText = go.AddComponent<Text>();
        UIFont.Apply(timerText, FontStyle.Bold);
        timerText.fontSize = 26;
        timerText.alignment = TextAnchor.MiddleCenter;
        timerText.supportRichText = true;
        timerText.raycastTarget = false;
        timerText.color = Color.white;
        Outline o = go.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.85f);
        o.effectDistance = new Vector2(2f, -2f);
        timerText.text = "";

        // v157 생존 시간: 오른쪽 위, 작게
        clockText = MakeHudText(canvas, "SurvivalClock", new Vector2(1f, 1f), new Vector2(-22f, -14f), new Vector2(360f, 34f), 22, TextAnchor.MiddleRight);
        versionText = MakeHudText(canvas, "Version", new Vector2(1f, 1f), new Vector2(-22f, -46f), new Vector2(360f, 20f), 14, TextAnchor.MiddleRight);
        versionText.text = $"<color=#FFFFFF80>Project_D 프로토타입 {BuildInfo.Version}</color>";
        versionText.fontStyle = FontStyle.Normal; UIFont.Apply(versionText, FontStyle.Normal);

        // v157 웨이브 시작 카운트다운: WAVE START 버튼 오른쪽에 크게
        countText = MakeHudText(canvas, "WaveCountdown", new Vector2(0f, 1f),
            new Vector2(MenuSkin.WaveButtonPos.x + MenuSkin.WaveButtonSize.x + 18f, MenuSkin.WaveButtonPos.y), new Vector2(220f, MenuSkin.WaveButtonSize.y), 44, TextAnchor.MiddleLeft);
        countRt = countText.rectTransform;
        countRt.pivot = new Vector2(0f, 0.5f);
        countRt.anchoredPosition = new Vector2(countRt.anchoredPosition.x, MenuSkin.WaveButtonPos.y - MenuSkin.WaveButtonSize.y * 0.5f);
        countText.enabled = false;
    }

    private static Text MakeHudText(Canvas canvas, string name, Vector2 anchor, Vector2 pos, Vector2 size, int fontSize, TextAnchor align)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(canvas.rootCanvas.transform, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Text t = go.AddComponent<Text>();
        UIFont.Apply(t, FontStyle.Bold);
        t.fontSize = fontSize;
        t.alignment = align;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.color = Color.white;
        Outline o = go.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.85f);
        o.effectDistance = new Vector2(1.5f, -1.5f);
        t.text = "";
        return t;
    }

    private void UpdateTimerUI()
    {
        if (timerText != null)
        {
            if (isWaveActive && waves.Count > 0)
            {
                float left = Mathf.Max(0f, waveEndTime - Time.time);
                string t = bossWave && left <= 0f && bosses.Count > 0 ? "보스 처치까지" : $"{Mathf.CeilToInt(left)}초";
                timerText.text = $"웨이브 {TotalWave}  <color=#FFD54A>{t}</color>  <size=18><color=#FFFFFFB0>남은 몹 {alive.Count}</color></size>";
            }
            else if (prepActive)
            {
                timerText.text = PlayerWeaponManager.TestModeAllWeapons
                    ? "정비 시간  <color=#7FE0FF>테스트 모드</color>  <size=18><color=#FFFFFFB0>F1로 웨이브 시작</color></size>"
                    : "정비 시간";
            }
            else timerText.text = "";
        }
        bool running = GameManager.Instance != null && GameManager.Instance.IsRunning;
        if (clockText != null) clockText.text = running ? $"<color=#C6E7A7>생존 {FormatTime(SurvivalTime)}</color>" : "";
        if (versionText != null) versionText.enabled = running;
        UpdateCountdown();

        if (buttonLabel != null && prepActive)
        {
            int sec = Mathf.CeilToInt(prepRemaining);
            if (sec != lastShownSec)
            {
                lastShownSec = sec;
                int early = EarlyStartBonus;
                buttonLabel.text = $"WAVE START  <size=15><color=#FFFFFFA6>F1</color></size>\n<size=14><color=#FFD54A>지금 시작 +{early}G</color></size>";
                buttonLabel.lineSpacing = 0.9f;
            }
        }
    }

    // ===== v151 생존 시간 =====
    public static string FormatTime(float sec)
    {
        int s = Mathf.FloorToInt(Mathf.Max(0f, sec));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
    }

    // v157 웨이브 시작까지 남은 초 (버튼 오른쪽). 10초부터 주황→빨강 + 초마다 '쿵' 커졌다 작아짐 + 째깍 소리
    private int lastCountSec = -1;
    private void UpdateCountdown()
    {
        if (countText == null) return;
        bool show = prepActive && !PlayerWeaponManager.TestModeAllWeapons && waveStartButton != null && waveStartButton.gameObject.activeInHierarchy
                    && GameManager.Instance != null && GameManager.Instance.IsRunning;
        countText.enabled = show;
        if (!show) { lastCountSec = -1; return; }
        int sec = Mathf.CeilToInt(prepRemaining);
        bool alert = sec <= CountdownAlertSec;
        if (sec != lastCountSec)
        {
            lastCountSec = sec;
            if (alert)
            {
                countPulse = Time.unscaledTime;
                if (sec <= 5 && sec > 0) Sfx.Play(Sfx.UIHoverSound, 0.5f, 0f);
            }
        }
        float k = Mathf.Clamp01((Time.unscaledTime - countPulse) / 0.35f);
        float punch = alert ? 1f + 0.35f * (1f - k) * (1f - k) : 1f;
        countRt.localScale = Vector3.one * punch;
        Color c = !alert ? Color.white : Color.Lerp(new Color(1f, 0.32f, 0.25f), new Color(1f, 0.72f, 0.25f), sec / (float)Mathf.Max(1, CountdownAlertSec));
        if (alert) c = Color.Lerp(Color.white, c, 0.4f + 0.6f * k);   // 바뀌는 순간 하얗게 번쩍
        string hex = ColorUtility.ToHtmlStringRGB(c);
        countText.text = $"<color=#{hex}>{sec}</color><size=22><color=#FFFFFFB0> 초</color></size>";
    }

    // 게임 오버 때 화면 위쪽에 생존 시간 + 최고 기록 (GameManager.TriggerGameOver가 부름)
    public static void ShowSurvivalRecord()
    {
        float t = SurvivalTime;
        float best = PlayerPrefs.GetFloat(BestKey, 0f);
        bool record = t > best;
        if (record) { best = t; PlayerPrefs.SetFloat(BestKey, t); PlayerPrefs.Save(); }

        GameObject go = new GameObject("SurvivalRecord", typeof(RectTransform));
        Canvas c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 600;
        CanvasScaler sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        Text tx = new GameObject("Text", typeof(RectTransform)).AddComponent<Text>();
        RectTransform rt = tx.rectTransform;
        rt.SetParent(go.transform, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -60f);
        rt.sizeDelta = new Vector2(1200f, 160f);
        UIFont.Apply(tx, FontStyle.Bold);
        tx.fontSize = 48;
        tx.alignment = TextAnchor.UpperCenter;
        tx.supportRichText = true;
        tx.raycastTarget = false;
        tx.horizontalOverflow = HorizontalWrapMode.Overflow;
        tx.verticalOverflow = VerticalWrapMode.Overflow;
        tx.color = Color.white;
        tx.text = $"생존 시간  <color=#C6E7A7>{FormatTime(t)}</color>   <size=30>웨이브 {TotalWave}</size>\n"
                + (record ? "<size=30><color=#FFD54A>새 최고 기록!</color></size>" : $"<size=30><color=#FFFFFFB0>최고 기록 {FormatTime(best)}</color></size>");
        Outline o = tx.gameObject.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.9f);
        o.effectDistance = new Vector2(2.5f, -2.5f);
        Debug.Log($"[무한 생존] 생존 {FormatTime(t)}, 웨이브 {TotalWave}, 최고 {FormatTime(best)}" + (record ? " (새 기록)" : ""));
    }
}
