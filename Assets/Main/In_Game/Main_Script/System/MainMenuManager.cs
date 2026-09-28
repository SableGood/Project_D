using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuManager : MonoBehaviour
{
    [Header("서브 패널 연결")]
    public GameObject optionsPanel;    // 설정 창 UI 패널
    public GameObject playerInfoPanel; // 플레이어 정보 창 UI 패널

    void Start()
    {
        // 메인 메뉴 진입 시 서브 패널들은 보이지 않게 숨김 처리
        if (optionsPanel != null) optionsPanel.SetActive(false);
        if (playerInfoPanel != null) playerInfoPanel.SetActive(false);
    }

    // 1. 게임 시작 버튼 기능
    public void StartGame()
    {
        // ★ 추가: 플레이어가 캐릭터를 누르지 않고 바로 시작했을 경우 기본값(0: 침팬지) 강제 지정
        if (!PlayerPrefs.HasKey("SelectedCharacter"))
        {
            PlayerPrefs.SetInt("SelectedCharacter", 0);
            PlayerPrefs.Save();
        }

        SceneManager.LoadScene("In_Game"); // 실제 게임 플레이 씬 이름으로 변경하세요
    }

    // 2. 설정 버튼 기능 (열기/닫기)
    public void OpenOptionsPanel(bool isOpen)
    {
        if (optionsPanel != null) optionsPanel.SetActive(isOpen);
    }

    // 3. 플레이어 정보 버튼 기능 (열기/닫기)
    public void OpenPlayerInfoPanel(bool isOpen)
    {
        if (playerInfoPanel != null) playerInfoPanel.SetActive(isOpen);
    }

    // 4. 게임 종료 버튼 기능
    public void QuitGame()
    {
        Debug.Log("게임이 종료됩니다.");
        Application.Quit(); // 빌드된 게임 앱 종료

        // 유니티 에디터에서 테스트 중일 때 플레이 모드를 꺼주는 안전장치
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // 5. 캐릭터 선택 기능 (0: 침팬지, 1: 원숭이, 2: 오랑우탄)
    public void SelectCharacter(int characterIndex)
    {
        PlayerPrefs.SetInt("SelectedCharacter", characterIndex);
        PlayerPrefs.Save(); // ★ 추가: 선택한 값을 즉시 디스크에 저장하여 씬 전환 시 유실 방지
        Debug.Log("캐릭터 선택 완료. 인덱스: " + characterIndex);
    }
}