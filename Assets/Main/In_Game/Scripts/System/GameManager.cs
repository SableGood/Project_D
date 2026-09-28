using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    // 전역에서 쉽게 접근할 수 있도록 싱글톤 인스턴스 선언
    public static GameManager Instance { get; private set; }

    [Header("플레이어 스폰 설정")]
    public GameObject[] playerPrefabs; // 0:침팬지, 1:원숭이, 2:오랑우탄 프리팹 할당
    public Transform spawnPoint;       // 씬 내에 배치한 시작 위치(빈 오브젝트)

    private GameObject currentPlayer;

    [Header("게임 코어 UI 패널")]
    public GameObject pauseMenuUI;   // ESC 키를 눌렀을 때 등장할 일시정지 메뉴 UI 캔버스/패널
    public GameObject gameOverUI;    // 플레이어 사망 시 등장할 게임 오버 UI 캔버스/패널

    [Header("게임 시작 셋업 UI 패널")]
    public GameObject difficultyPanel; // 난이도 선택 창
    public GameObject characterPanel;  // 캐릭터 선택 창
    public GameObject talentPanel;     // 재능 선택 창

    [Header("글로벌 참조 데이터")]
    public Transform playerTransform; // 몬스터들이 참조할 플레이어의 위치

    // 상태 관리 변수들
    private bool isPaused = false;
    private bool isGameOver = false;
    private bool isSetupComplete = false; // 시작 셋업이 끝났는지 확인하는 변수

    // 플레이어의 선택 데이터를 임시 저장할 변수들
    private int selectedDifficulty = 1; // 0: Easy, 1: Normal, 2: Hard
    private int selectedCharacter = 0;
    private int selectedTalent = 0;     // 재능 인덱스

    void Awake()
    {
        // 싱글톤 패턴 초기화 (중복 방지)
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // 1. 게임 코어 UI 숨김
        if (pauseMenuUI != null) pauseMenuUI.SetActive(false);
        if (gameOverUI != null) gameOverUI.SetActive(false);

        // 2. 셋업 UI 초기화 (난이도 창만 켜고 나머지는 숨김)
        if (difficultyPanel != null) difficultyPanel.SetActive(true);
        if (characterPanel != null) characterPanel.SetActive(false);
        if (talentPanel != null) talentPanel.SetActive(false);

        // 3. 셋업을 위해 게임 시간을 0으로 정지하고 커서 활성화
        isSetupComplete = false;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Update()
    {
        // 게임 셋업이 완료되었고, 게임 오버 상태가 아닐 때만 ESC 일시정지 가능
        if (Input.GetKeyDown(KeyCode.Escape) && !isGameOver && isSetupComplete)
        {
            if (isPaused)
            {
                ResumeGame(); // 이미 정지되어 있다면 다시 게임 재개
            }
            else
            {
                PauseGame();  // 진행 중이라면 게임 일시정지
            }
        }
    }

    // --- [플레이어 위치 등록 기능 (최적화 핵심)] ---
    public void RegisterPlayer(Transform player)
    {
        playerTransform = player;
        Debug.Log("GameManager: 플레이어 위치 등록 완료!");
    }

    // --- [시작 셋업 1단계: 난이도 선택] ---
    public void SelectDifficulty(int difficultyIndex)
    {
        selectedDifficulty = difficultyIndex;
        Debug.Log("난이도 선택 완료: " + difficultyIndex);

        if (difficultyPanel != null) difficultyPanel.SetActive(false);
        if (characterPanel != null) characterPanel.SetActive(true);
    }

    // --- [시작 셋업 2단계: 캐릭터 선택] ---
    public void SelectCharacter(int charIndex)
    {
        selectedCharacter = charIndex;
        Debug.Log("캐릭터 선택 완료: " + charIndex);

        if (characterPanel != null) characterPanel.SetActive(false);
        if (talentPanel != null) talentPanel.SetActive(true);
    }

    // --- [시작 셋업 3단계: 재능 선택 및 게임 본격 시작] ---
    public void SelectTalent(int talentIndex)
    {
        selectedTalent = talentIndex;
        Debug.Log("재능 선택 완료: " + talentIndex);

        // 셋업 창을 모두 끄고 게임 시간 및 상태 정상화
        if (talentPanel != null) talentPanel.SetActive(false);

        isSetupComplete = true; // 셋업 완료 선언
        Time.timeScale = 1f;    // 시간 흐름 재개

        // 모든 설정이 끝났으므로 저장된 인덱스를 기반으로 플레이어 스폰
        SpawnPlayer(selectedCharacter);
        ApplyGameSettings();
    }

    // --- [플레이어 스폰 기능] ---
    private void SpawnPlayer(int index)
    {
        if (playerPrefabs != null && playerPrefabs.Length > index && playerPrefabs[index] != null)
        {
            currentPlayer = Instantiate(playerPrefabs[index], spawnPoint.position, spawnPoint.rotation);
            currentPlayer.tag = "Player";

            // 스폰 직후 안전하게 트랜스폼 동기화 등록
            RegisterPlayer(currentPlayer.transform);

            Debug.Log($"GameManager: {currentPlayer.name} 스폰 완료!");
        }
        else
        {
            Debug.LogError("GameManager: 플레이어 프리팹 배열이 비어있거나 인덱스 오류입니다.");
        }
    }

    private void ApplyGameSettings()
    {
        // 추후 선택된 난이도나 재능을 게임 내 스탯에 반영하는 로직
    }

    // --- [기존 코어 시스템 (일시정지, 종료, 재시작)] ---

    public void PauseGame()
    {
        isPaused = true;
        Time.timeScale = 0f;
        if (pauseMenuUI != null) pauseMenuUI.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ResumeGame()
    {
        isPaused = false;
        Time.timeScale = 1f;
        if (pauseMenuUI != null) pauseMenuUI.SetActive(false);
    }

    public void TriggerGameOver()
    {
        if (isGameOver) return;

        isGameOver = true;
        Time.timeScale = 0f;
        if (gameOverUI != null) gameOverUI.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Debug.Log("게임 오버! 플레이어가 사망했습니다.");
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void LoadMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene("MainMenu");
    }

    public void QuitGame()
    {
        Debug.Log("게임이 종료됩니다.");
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}