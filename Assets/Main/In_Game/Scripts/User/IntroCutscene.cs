using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// v128 인트로 컷씬: 게임을 켜고 메인 메뉴(Out_Game/MainMenu 씬)에 처음 들어갈 때 한 번 재생
//  그림 5장(Resources/Image/Intro/Intro_1~5) + 아래쪽 자막(한 글자씩)
//  - 그림은 천천히 확대·이동 (켄 번즈), 장면 사이는 검은 화면으로 페이드
//  - 클릭 / Space / Enter: 자막이 나오는 중이면 바로 다 보여 주고, 다 나왔으면 다음 장면
//  - Esc: 전체 건너뛰기
//  - 게임을 켤 때마다 한 번 (게임 중 메인 메뉴로 돌아와도 다시 안 나옴)
//  씬에 따로 놓을 필요 없음: 메인 메뉴 씬이 열리면 스스로 생김
public class IntroCutscene : MonoBehaviour
{
    public const string MenuSceneName = "MainMenu";
    public static bool Played { get; private set; }

    private static readonly string[] Lines =
    {
        "먼 미래, 이미 멸종한 인류가 남긴 최후의 무인 연구 시설.",
        "정체불명의 돌연변이 유인원 군단이 이곳을 습격했습니다.",
        "실험실의 경고음이 울리고 통제력을 잃은 실험체들이 쏟아져 나옵니다.",
        "뛰어난 지능의 실험체가 남겨진 무기와 자동화 포탑의 통제 권한을 획득했습니다.",
        "진화의 끝에서 살아남는 것이 누구인지, 증명할 시간입니다.",
    };
    // 장면마다 확대 방향 (시작 위치 → 끝 위치, 화면 비율 기준)
    //  (확대 1.04배 → 가장자리에 2% 여유. 이동은 그 안에서만)
    private static readonly Vector2[] PanFrom = { new Vector2(0f, -0.012f), new Vector2(0.012f, 0f), new Vector2(-0.012f, 0f), new Vector2(0.012f, 0.008f), new Vector2(-0.01f, 0f) };
    private static readonly Vector2[] PanTo = { new Vector2(0f, 0.012f), new Vector2(-0.012f, 0f), new Vector2(0.012f, 0f), new Vector2(-0.008f, -0.008f), new Vector2(0.01f, 0f) };

    public static float FadeTime = 0.7f;
    public static float CharsPerSecond = 20f;
    public static float HoldAfterText = 2.2f;   // 자막이 다 나온 뒤 다음 장면까지
    public static float ZoomFrom = 1.04f, ZoomTo = 1.12f;

    private Image picture, blackout, alarm;
    private RectTransform pictureRt, rootRt;
    private Text subtitle, hint;
    private AspectRatioFitter fitter;
    private bool skipAll, advance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Played = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryStart(SceneManager.GetActiveScene()); // 게임을 켜자마자 메뉴 씬인 경우
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode mode) { TryStart(s); }

    private static void TryStart(Scene s)
    {
        if (Played || s.name != MenuSceneName) return;
        if (Resources.Load<Sprite>("Image/Intro/Intro_1") == null) return; // 그림이 없으면 생략
        if (FindFirstObjectByType<IntroCutscene>() != null) return;
        Played = true;
        new GameObject("IntroCutscene").AddComponent<IntroCutscene>();
    }

    void Start()
    {
        BuildUI();
        StartCoroutine(Run());
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) skipAll = true;
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            advance = true;
    }

    private IEnumerator Run()
    {
        blackout.color = Color.black;
        for (int i = 0; i < Lines.Length && !skipAll; i++)
        {
            Sprite sp = Resources.Load<Sprite>($"Image/Intro/Intro_{i + 1}");
            if (sp == null) continue;
            picture.sprite = sp;
            fitter.aspectRatio = sp.rect.width / Mathf.Max(1f, sp.rect.height);
            subtitle.text = "";
            advance = false;
            float t0 = Time.unscaledTime;

            // 장면 진행: 페이드 인 → 자막 한 글자씩 → 잠깐 머묾 (클릭으로 넘김) → 페이드 아웃
            string line = Lines[i];
            float textStart = t0 + FadeTime * 0.6f;
            float textDone = -1f;
            float sceneLen = FadeTime * 0.6f + line.Length / CharsPerSecond + HoldAfterText + FadeTime; // 켄 번즈 기준 길이
            while (!skipAll)
            {
                float now = Time.unscaledTime;
                float age = now - t0;
                blackout.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(age / FadeTime));
                Animate(i, Mathf.Clamp01(age / sceneLen), age);

                int shown = Mathf.Clamp(Mathf.FloorToInt((now - textStart) * CharsPerSecond), 0, line.Length);
                if (advance && textDone < 0f) { shown = line.Length; advance = false; textStart = now - line.Length / CharsPerSecond; } // 첫 클릭: 자막 바로 다 보이기
                subtitle.text = line.Substring(0, shown);
                if (shown >= line.Length && textDone < 0f) textDone = now;
                if (textDone >= 0f && (advance || now - textDone >= HoldAfterText)) break;
                yield return null;
            }
            // 페이드 아웃 (마지막 장면은 조금 길게)
            float fade = i == Lines.Length - 1 ? FadeTime * 1.6f : FadeTime;
            float f0 = Time.unscaledTime;
            while (!skipAll)
            {
                float k = (Time.unscaledTime - f0) / fade;
                if (k >= 1f) break;
                blackout.color = new Color(0f, 0f, 0f, k);
                subtitle.color = new Color(1f, 1f, 1f, 1f - k);
                Animate(i, Mathf.Clamp01((Time.unscaledTime - t0) / sceneLen), Time.unscaledTime - t0);
                yield return null;
            }
            subtitle.color = Color.white;
            blackout.color = Color.black;
        }

        // 메뉴 위의 검은 화면을 걷어냄
        subtitle.text = ""; hint.text = ""; picture.enabled = false; alarm.enabled = false;
        float e0 = Time.unscaledTime;
        while (Time.unscaledTime - e0 < 0.6f)
        {
            blackout.color = new Color(0f, 0f, 0f, 1f - (Time.unscaledTime - e0) / 0.6f);
            yield return null;
        }
        Destroy(gameObject);
    }

    // 켄 번즈 + 장면별 연출
    private void Animate(int i, float u, float age)
    {
        float e = u * u * (3f - 2f * u);
        float s = Mathf.Lerp(ZoomFrom, ZoomTo, e);
        pictureRt.localScale = new Vector3(s, s, 1f);
        Vector2 size = rootRt.rect.size;
        Vector2 pan = Vector2.Lerp(PanFrom[i % PanFrom.Length], PanTo[i % PanTo.Length], e);
        pictureRt.anchoredPosition = new Vector2(pan.x * size.x, pan.y * size.y);

        // 3번 장면(실험실 경고): 붉은 경고등이 깜빡임
        bool siren = i == 2;
        alarm.enabled = siren;
        if (siren) alarm.color = new Color(1f, 0.05f, 0.02f, 0.10f + 0.10f * Mathf.Max(0f, Mathf.Sin(age * Mathf.PI * 2.2f)));
        // 5번 장면(전투): 살짝 흔들림
        if (i == 4 && age < 1.2f)
        {
            float k = (1.2f - age) / 1.2f * 6f;
            pictureRt.anchoredPosition += new Vector2(Random.Range(-k, k), Random.Range(-k, k));
        }
    }

    // ===== UI =====
    private void BuildUI()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>(); // 뒤의 메뉴 버튼이 눌리지 않게

        // 배경 (검정, 클릭 막기)
        RectTransform root = rootRt = Full(new GameObject("Root", typeof(RectTransform)), transform);
        Image bg = root.gameObject.AddComponent<Image>();
        bg.color = Color.black;
        root.gameObject.AddComponent<RectMask2D>();

        // 그림: 화면을 꽉 채움 (비율 유지, 넘치는 부분은 잘림)
        GameObject pic = new GameObject("Picture", typeof(RectTransform));
        pictureRt = (RectTransform)pic.transform;
        pictureRt.SetParent(root, false);
        pictureRt.anchorMin = pictureRt.anchorMax = new Vector2(0.5f, 0.5f);
        picture = pic.AddComponent<Image>();
        picture.raycastTarget = false;
        fitter = pic.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = 16f / 9f;

        // 경고등 (붉은 막)
        alarm = Full(new GameObject("Alarm", typeof(RectTransform)), root).gameObject.AddComponent<Image>();
        alarm.raycastTarget = false; alarm.enabled = false;

        // 자막 띠: 아래쪽이 어두워지는 그라데이션
        RectTransform band = new GameObject("SubtitleBand", typeof(RectTransform)).GetComponent<RectTransform>();
        band.SetParent(root, false);
        band.anchorMin = new Vector2(0f, 0f); band.anchorMax = new Vector2(1f, 0f); band.pivot = new Vector2(0.5f, 0f);
        band.sizeDelta = new Vector2(0f, 260f);
        RawImage grad = band.gameObject.AddComponent<RawImage>();
        grad.texture = Gradient();
        grad.raycastTarget = false;

        subtitle = new GameObject("Subtitle", typeof(RectTransform)).AddComponent<Text>();
        RectTransform st = subtitle.rectTransform;
        st.SetParent(band, false);
        st.anchorMin = new Vector2(0.08f, 0f); st.anchorMax = new Vector2(0.92f, 1f);
        st.offsetMin = new Vector2(0f, 70f); st.offsetMax = new Vector2(0f, -60f);
        UIFont.Apply(subtitle, FontStyle.Normal);
        subtitle.fontSize = 36;
        subtitle.alignment = TextAnchor.MiddleCenter;
        subtitle.horizontalOverflow = HorizontalWrapMode.Wrap;
        subtitle.verticalOverflow = VerticalWrapMode.Overflow;
        subtitle.color = Color.white;
        subtitle.raycastTarget = false;
        Shadow sh = subtitle.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.9f); sh.effectDistance = new Vector2(3f, -3f);

        hint = new GameObject("Hint", typeof(RectTransform)).AddComponent<Text>();
        RectTransform ht = hint.rectTransform;
        ht.SetParent(root, false);
        ht.anchorMin = ht.anchorMax = ht.pivot = new Vector2(1f, 0f);
        ht.anchoredPosition = new Vector2(-28f, 22f);
        ht.sizeDelta = new Vector2(600f, 30f);
        UIFont.Apply(hint, FontStyle.Normal);
        hint.fontSize = 24;
        hint.alignment = TextAnchor.LowerRight;
        hint.color = new Color(1f, 1f, 1f, 0.45f);
        hint.text = "클릭: 다음   ESC: 건너뛰기";
        hint.raycastTarget = false;

        // 맨 위: 장면 전환용 검은 막
        blackout = Full(new GameObject("Blackout", typeof(RectTransform)), transform).gameObject.AddComponent<Image>();
        blackout.color = Color.black;
        blackout.raycastTarget = false;
    }

    private static RectTransform Full(GameObject go, Transform parent)
    {
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    // 아래(검정 85%) → 위(투명) 그라데이션
    private static Texture2D Gradient()
    {
        Texture2D t = new Texture2D(1, 64, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < 64; y++)
        {
            float a = 1f - y / 63f;
            t.SetPixel(0, y, new Color(0f, 0f, 0f, 0.85f * a * a * (3f - 2f * a)));
        }
        t.Apply();
        return t;
    }
}
