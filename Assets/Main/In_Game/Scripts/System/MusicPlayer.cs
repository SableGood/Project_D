using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// v142 배경 음악. 씬에 놓을 것 없음 (게임이 켜지자마자 스스로 생기고, 씬이 바뀌어도 계속 남음)
//  - 메인 메뉴·인트로 컷씬 등 게임 밖: Resources/Sound/Music/Calm_Before_the_Storm
//  - In_Game 씬: Survival_Pulse (계속 반복)
//  - 곡이 바뀔 때는 서서히 줄였다가 새 곡을 서서히 키움. 같은 곡이면 씬이 바뀌어도 끊기지 않고 이어짐
//  - 음량: MusicPlayer.Volume (0~1, PlayerPrefs "MusicVolume"에 저장)
public class MusicPlayer : MonoBehaviour
{
    // v145: OGG가 Unity에서 불러와지지 않아 WAV로 바꾸고 이름도 새로 (예전 .ogg 두 개는 지워도 됨)
    public const string MenuTrack = "Music/BGM_Menu_Calm";
    public const string GameTrack = "Music/BGM_Game_Survival";
    public const string GameSceneName = "In_Game";
    public static float DefaultVolume = 0.5f;
    public static float FadeInTime = 1.5f, FadeOutTime = 1.2f;

    public static MusicPlayer Instance { get; private set; }

    private AudioSource src;
    private string current;
    private float fadeMul = 1f;
    private Coroutine fading;

    public static float Volume
    {
        get => PlayerPrefs.GetFloat("MusicVolume", DefaultVolume);
        set { PlayerPrefs.SetFloat("MusicVolume", Mathf.Clamp01(value)); if (Instance != null) Instance.Apply(); }
    }

    // 게임이 켜지자마자 (첫 씬이 열리기 전) 시작 → 스튜디오 로고·인트로 컷씬 동안에도 메뉴 음악
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("Music");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<MusicPlayer>();
        Instance.src = go.AddComponent<AudioSource>();
        Instance.src.loop = true;
        Instance.src.playOnAwake = false;
        Instance.src.spatialBlend = 0f;
        Instance.src.priority = 0;   // 효과음이 많아도 음악은 끊기지 않게
        Instance.src.ignoreListenerPause = true;
        Scene first = SceneManager.GetActiveScene();
        if (first.IsValid() && !string.IsNullOrEmpty(first.name)) Instance.PlayTrack(IsGameScene(first) ? GameTrack : MenuTrack, 0.6f);
        SceneManager.sceneLoaded += (s, m) => { if (Instance != null && m == LoadSceneMode.Single) Instance.OnScene(s); };
    }

    // 게임 씬: 이름이 In_Game이거나, 씬에 GameManager가 있으면
    private static bool IsGameScene(Scene s)
    {
        if (s.name == GameSceneName) return true;
        foreach (GameObject root in s.GetRootGameObjects())
            if (root.GetComponentInChildren<GameManager>(true) != null) return true;
        return false;
    }

    private void OnScene(Scene s)
    {
        bool game = IsGameScene(s);
        Debug.Log($"[음악] 씬 '{s.name}' → {(game ? "게임" : "메뉴")} 음악");
        PlayTrack(game ? GameTrack : MenuTrack, FadeInTime);
    }

    // 다른 곡이면 바꿈 (같은 곡이면 그대로 이어서). fadeIn 초 동안 서서히 커짐
    public void PlayTrack(string path, float fadeIn)
    {
        if (current == path && src.isPlaying) { FadeTo(1f, fadeIn); return; }
        AudioClip clip = Resources.Load<AudioClip>("Sound/" + path);
        if (clip == null) { Debug.LogWarning("[음악] 없음: Sound/" + path); return; }
        current = path;
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        src.clip = clip;
        fadeMul = 0f; Apply();
        src.Play();
        FadeTo(1f, fadeIn);
        Debug.Log($"[음악] 재생: {clip.name} (음량 {Volume:0.00}, 불러오기 {clip.loadState})");
    }

    // v144 안전장치: 곡이 정해져 있는데 멈춰 있으면 다시 재생 (불러오기가 늦거나 무언가 멈춘 경우)
    private float nextCheck;
    void Update()
    {
        if (Time.unscaledTime < nextCheck || src == null || src.clip == null) return;
        nextCheck = Time.unscaledTime + 0.5f;
        if (!src.isPlaying && src.clip.loadState == AudioDataLoadState.Loaded)
        {
            src.Play();
            Debug.Log("[음악] 멈춰 있어서 다시 재생: " + src.clip.name);
        }
        if (src.clip.loadState == AudioDataLoadState.Failed) Debug.LogWarning("[음악] 불러오기 실패: " + src.clip.name);
    }

    // 게임 시작 연출 등에서: 지금 곡을 서서히 줄임 (다음 씬에서 새 곡이 서서히 커짐)
    public static void FadeOut(float time = -1f)
    {
        if (Instance != null) Instance.FadeTo(0f, time < 0f ? FadeOutTime : time);
    }

    private void FadeTo(float target, float time)
    {
        if (fading != null) StopCoroutine(fading);
        fading = StartCoroutine(Fade(target, Mathf.Max(0.01f, time)));
    }

    private IEnumerator Fade(float target, float time)
    {
        float from = fadeMul, t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < time)
        {
            float k = (Time.unscaledTime - t0) / time;
            fadeMul = Mathf.Lerp(from, target, k * k * (3f - 2f * k));
            Apply();
            yield return null;
        }
        fadeMul = target; Apply();
        fading = null;
    }

    private void Apply() { if (src != null) src.volume = Volume * fadeMul; }
}
