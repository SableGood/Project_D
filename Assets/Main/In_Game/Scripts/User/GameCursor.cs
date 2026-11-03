using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// 마우스 커서 4종 (Resources/Image/UI/Cursors)
//  UI     : 메뉴·재능 선택·엘리트 상점·일시정지·HUD 위
//  Aim    : 탑뷰 기본 조준점
//  Skill  : 스킬(수류탄) 조준 중
//  Tower  : 포탑 건설 칸을 고른 상태
//  1인칭은 커서를 숨기고 잠그므로 건드리지 않음. 씬에 따로 붙일 필요 없이 게임 시작 시 자동 생성
public class GameCursor : MonoBehaviour
{
    public enum Kind { None = -1, UI, Aim, Skill, Tower }

    private const string Folder = "Image/UI/Cursors/";
    private static readonly string[] Files = { "Cursor_UI", "Cursor_Aim", "Cursor_Skill", "Cursor_Tower" };
    // 32x32 그림 안의 클릭 지점 (왼쪽 위 기준)
    private static readonly Vector2[] Hotspots = { new Vector2(2, 2), new Vector2(16, 15), new Vector2(16, 15), new Vector2(16, 15) };

    private static GameCursor instance;
    private readonly Texture2D[] textures = new Texture2D[4];
    private Kind current = Kind.None;

    private PlayerController player;
    private TowerBuilder builder;
    private float nextFind;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("GameCursor");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GameCursor>();
    }

    void Awake()
    {
        for (int i = 0; i < Files.Length; i++)
        {
            Texture2D t = Resources.Load<Texture2D>(Folder + Files[i]);
            if (t == null) { Debug.LogWarning($"GameCursor: {Folder}{Files[i]} 없음"); continue; }
            textures[i] = t;
        }
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        player = null; builder = null; nextFind = 0f;
        current = Kind.None;   // 씬이 바뀌면 다시 지정
    }

    void LateUpdate()
    {
        if (Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 1f;
            if (player == null) player = FindFirstObjectByType<PlayerController>();
            if (builder == null) builder = FindFirstObjectByType<TowerBuilder>();
        }
        Apply(Decide());
    }

    private Kind Decide()
    {
        if (player == null) return Kind.UI;                                   // 메인 메뉴 등
        if (AugmentManager.IsOpen || Time.timeScale == 0f) return Kind.UI;  // 재능·상점·일시정지·게임 오버
        if (player.isFirstPerson) return Kind.UI;                           // 숨겨져 있음 (일시정지 등으로 풀리면 UI)
        if (PlayerSkills.Instance != null && PlayerSkills.Instance.IsAiming) return Kind.Skill;
        if (builder != null && builder.SelectedSlot >= 0) return Kind.Tower;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return Kind.UI;
        return Kind.Aim;
    }

    private void Apply(Kind k)
    {
        if (k == current) return;
        current = k;
        Texture2D t = textures[(int)k];
        if (t == null) { Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); return; }
        Cursor.SetCursor(t, Hotspots[(int)k], CursorMode.Auto);
    }
}
