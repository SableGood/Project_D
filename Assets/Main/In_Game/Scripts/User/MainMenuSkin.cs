using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// v129 메인 메뉴 꾸미기 (씬에 놓을 것 없음: MainMenu 씬이 열리면 스스로 생김)
//  - 배경 (v130): 바깥 풍경·구름·새떼 + 폐허(덩굴·전깃줄·스파크). 화면을 꽉 채움, 아주 천천히 숨 쉬듯 확대
//      그림: Menu_Landscape · Menu_Clouds · Menu_Ruins · Menu_Vine_1/2 (없으면 예전 MainMenu_BG 한 장)
//      게임 시작 때는 폐허만 빠르게 확대되며 바깥으로 나가는 연출 → 검은 화면 → In_Game
//  - 씬에 있는 버튼을 이름으로 찾아 모양을 바꿈 (버튼 기능은 그대로)
//      Main_Status(플레이어 정보) · Main_Option(설정) · Main_Quit(게임 종료): 초록 패널, 마우스를 올리면 주황 패널
//      Main_GameStart(게임 시작): 빨간 버저. 누르면 주먹으로 내리침 → 눌린 버저 + Buzzers 소리 → 화면이 어두워지며 게임 시작
//  - 마우스 커서 대신 침팬지 손 (손가락 끝이 커서 위치)
//  위치·크기는 아래 Layout 값 (기준 해상도 1920×1080, 화면 가운데가 0,0)
public class MainMenuSkin : MonoBehaviour
{
    public const string MenuSceneName = "MainMenu";
    private const string Dir = "Image/UI/MainMenu/";

    // ===== 배치 =====
    // v132: 실제 조작판처럼 아래쪽 한 줄 — 왼쪽 아래 버저, 그 오른쪽으로 패널 3개를 같은 간격으로
    //  v133: 버저는 아래쪽이 화면 밖으로 살짝 잘리게 (책상 위에 놓인 느낌), 패널은 화면 아래에 붙임
    public static Vector2 BuzzerPos = new Vector2(-700f, -433f), BuzzerSize = new Vector2(440f, 373f);
    public static Vector2 PanelSize = new Vector2(360f, 95f);
    public static Vector2 PanelStart = new Vector2(-110f, -469f);   // 첫 기계·패널 위치 (v136: 오른쪽으로 붙임, x만 씀)
    public static float PanelStep = 405f;                           // 기계 사이 간격 (오른쪽으로)
    public static readonly string[] PanelOrder = { "Main_Status", "Main_Option", "Main_Quit" };
    // v133: 팔이 오른쪽 아래에서 왼쪽 위로 45° (원래 팔 길이 그대로, 681×973)
    //  팔이 잘린 끝이 화면 안으로 들어오지 않게, 손이 올라갈 수 있는 높이를 제한 (그 위로는 보통 커서가 보임)
    public static Vector2 HandSize = new Vector2(306f, 438f);
    public const float ArmCutTop = 0.276f;  // 팔 잘린 끝 중 가장 높은 곳 (그림 아래에서 비율)
    // 손 그림 안의 기준점 (0~1, 왼쪽 아래가 0,0): 손가락 끝 / 주먹이 닿는 곳
    public static readonly Vector2 PointTip = new Vector2(0.1165f, 0.996f), FistHit = new Vector2(0.194f, 0.705f);

    private Canvas canvas;
    private RectTransform canvasRt, bgPic, hand, buzzerRt, fadeRt;
    private Image handImg, buzzerImg, fade, flash;
    private Sprite handPoint, handFist, buzzer, buzzerPressed, buzzerOpen, startMachine;
    private Vector2 buzzerBasePos;
    private float buzzerHitY = 0.6f;
    // v137 시작 버튼 기계 (세로 캐비닛). 버튼은 캐비닛 가운데 빈 판에
    // v141: 캐비닛·버튼 2배. 버튼이 손(주먹) 닿는 높이에 오도록 캐비닛을 더 아래로 (윗부분만 보임)
    // v143: 새 캐비닛 그림(493×912, 덮개 닫힘·열림·눌림 3장). 버튼이 주먹 닿는 높이에 오도록 아래로
    public static float StartMachineX = -700f, StartMachineWidth = 560f, StartMachineBottom = -958f;
    public static Vector2 StartButtonAt = new Vector2(0.625f, 0.583f); // 빨간 버튼 가운데 (캐비닛 그림 안, 왼쪽 아래 0,0)
    public static Vector2 StartButtonHit = new Vector2(0.32f, 0.2f);   // 누름 영역 크기 (캐비닛 너비·높이 비율)
    public static Vector2 StartLabelAt = new Vector2(0.55f, 0.80f);    // 노란 '게임 시작' 패널 (위쪽 계기판)
    public static float StartLabelWidth = 330f;
    public static Vector2 StartButtonSize = new Vector2(300f, 332f);   // (예전 버저용)
    // v140: 버튼만 밝고 선명해 혼자 튀어 보여서 → 캐비닛 조명에 맞게 살짝 어둡게 + 뒤에 그림자
    public static Color StartButtonTint = new Color(0.78f, 0.76f, 0.74f, 1f);
    public static float StartButtonShadow = 0.55f;
    public const string MachineSound = "UI/Machine_1";
    private Button startButton;
    private Vector2 handPos, handVel;
    private bool slamming;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TryStart(SceneManager.GetActiveScene());
    }
    private static void OnSceneLoaded(Scene s, LoadSceneMode m) { TryStart(s); }
    private static void TryStart(Scene s)
    {
        if (s.name != MenuSceneName || FindFirstObjectByType<MainMenuSkin>() != null) return;
        if (Resources.Load<Sprite>(Dir + "Panel_Normal") == null) return;
        new GameObject("MainMenuSkin").AddComponent<MainMenuSkin>();
    }

    void Start()
    {
        handPoint = Resources.Load<Sprite>(Dir + "Hand_Point");
        handFist = Resources.Load<Sprite>(Dir + "Hand_Fist");
        // v143 시작 버튼 캐비닛: 덮개 닫힘(평소) / 덮개 열림(마우스 올림) / 눌림(클릭) 그림이 통째로 바뀜. 없으면 예전 버저
        buzzer = Resources.Load<Sprite>(Dir + "Start_Cab_Covered");
        buzzerOpen = Resources.Load<Sprite>(Dir + "Start_Cab_Open");
        buzzerPressed = Resources.Load<Sprite>(Dir + "Start_Cab_Pressed");
        startMachine = buzzer;
        if (buzzer == null || startMachine == null)
        {
            startMachine = null; buzzerOpen = null;
            buzzer = Resources.Load<Sprite>(Dir + "Buzzer");
            buzzerPressed = Resources.Load<Sprite>(Dir + "Buzzer_Pressed");
        }

        Button anyButton = null;
        foreach (Button b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b.name.StartsWith("Main_")) { anyButton = b; break; }
        Canvas parentCanvas = anyButton != null ? anyButton.GetComponentInParent<Canvas>() : null;
        canvas = parentCanvas != null ? parentCanvas.rootCanvas : FindFirstObjectByType<Canvas>();
        if (canvas == null) { Destroy(gameObject); return; }
        canvasRt = (RectTransform)canvas.transform;

        // 해상도에 맞춰 크기 조절 (기준 1920×1080)
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        BuildBackground();
        StyleButtons();
        BuildOverlay();
        StartCoroutine(MenuIntro());
        Sfx.HoverOverride = "UI/UI_Main_On"; // v136 메인 메뉴 전용 마우스 올림 소리
    }

    // ===== v134 메뉴 등장 연출 =====
    //  검은 화면에서 서서히 밝아지며 살짝 다가감(클로즈업) → 아래에서 기계 3대가 연기와 함께 덜컹거리며 올라옴
    //  → 기계 위로 주황 홀로그램 패널이 튀어나옴 (빛줄기 + 기계에 비치는 빛). 다 나오기 전에는 버튼을 누를 수 없음
    public static readonly string[] MachineSprites = { "Menu_Machine_3", "Menu_Machine_2", "Menu_Machine_1" }; // 플레이어 정보 · 설정 · 게임 종료 순
    public static Vector2 MachineSize = new Vector2(420f, 212f);    // v136 도트 그림 210×106의 2배 (3번은 210×123이라 더 높음)
    public static float MachineBottom = -600f;                      // 기계 그림 아래 끝 (화면 아래 -540보다 밑 → 아랫부분이 살짝 잘림)
    public static float MachineTopFrac = 0.86f;                     // 홀로그램이 나오는 곳 (기계 그림 아래에서 비율)
    public static float HoloLift = 95f;                             // 나오는 곳에서 패널 가운데까지 높이
    public static float HoloTilt = 0f;                              // v138: 화면 안에서 기울이는 각도(Z) 없앰
    // v144: Y축 각도를 가짜 원근(UIFakePerspective)으로 — 먼 쪽이 작아짐. 기계마다 앞면 방향이 달라 부호가 다름
    public static float[] HoloYaws = { -18f, -18f, 18f };          // 플레이어 정보 · 설정 · 게임 종료 (-: 왼쪽이 멂, +: 오른쪽이 멂)
    public static float StartLabelYaw = 16f;
    public static Color HoloColor = new Color(1f, 0.58f, 0.18f, 1f);
    public static float StageCloseUp = 1.06f;                       // 등장 클로즈업이 끝나는 배율

    private class Rig
    {
        public RectTransform root, body, holo;
        public CanvasGroup group, holoGroup;
        public Image machine, glow, beam;
        public bool ready;
        public float phase;
        public float hidden, width;   // 숨어 있을 때 높이(아래로), 기계 너비 (연기 퍼지는 폭)
    }
    private readonly System.Collections.Generic.List<Rig> rigs = new System.Collections.Generic.List<Rig>();
    private CanvasGroup buzzerGroup;
    private float stageBase = 1.03f;
    public bool IntroDone { get; private set; }   // v146 메뉴 등장 연출이 끝났는지 (경고 CS0414 해결)
    private Sprite beamSprite;

    private void EnsureFxSprites()
    {
        if (pixel == null) pixel = MakeSprite(Tex(1, 1, (x, y) => Color.white));
        if (glow == null) glow = MakeSprite(Tex(32, 32, (x, y) => { float d = new Vector2(x - 15.5f, y - 15.5f).magnitude / 16f; float a = Mathf.Clamp01(1f - d); return new Color(1f, 1f, 1f, a * a); }), FilterMode.Bilinear);
        if (beamSprite == null)
            beamSprite = MakeSprite(Tex(32, 64, (x, y) =>
            {
                float v = y / 63f;                                  // 0 = 아래(기계), 1 = 위(패널)
                float half = Mathf.Lerp(0.12f, 0.5f, v);            // 위로 갈수록 넓어짐
                float d = Mathf.Abs(x - 15.5f) / 16f;
                float edge = Mathf.Clamp01((half - d) / 0.12f);
                float a = edge * Mathf.Lerp(0.9f, 0.25f, v);
                return new Color(1f, 1f, 1f, a);
            }), FilterMode.Bilinear);
    }

    private Rig BuildRig(int i, float x)
    {
        Rig r = new Rig { phase = Random.value * 10f, hidden = -(MachineSize.y + 120f), width = MachineSize.x };
        r.root = NewRect("PanelRig" + (i + 1), canvasRt);
        r.root.anchorMin = r.root.anchorMax = new Vector2(0.5f, 0.5f);
        r.root.sizeDelta = Vector2.zero;
        r.root.anchoredPosition = new Vector2(x, 0f);
        r.group = r.root.gameObject.AddComponent<CanvasGroup>();
        r.group.interactable = false; r.group.blocksRaycasts = false;

        float topY = MachineBottom + MachineSize.y * MachineTopFrac;
        // 빛줄기 (기계 → 패널), 기계 뒤
        r.beam = Img(NewRect("HoloBeam", r.root), beamSprite);
        RectTransform bt = r.beam.rectTransform;
        bt.anchorMin = bt.anchorMax = new Vector2(0.5f, 0.5f); bt.pivot = new Vector2(0.5f, 0f);
        bt.sizeDelta = new Vector2(PanelSize.x * 0.95f, HoloLift);
        bt.anchoredPosition = new Vector2(0f, topY - 6f);
        bt.localRotation = Quaternion.Euler(0f, 0f, HoloTilt * 0.5f);
        r.beam.color = new Color(HoloColor.r, HoloColor.g, HoloColor.b, 0f);

        // 기계 (올라오는 부분)
        r.body = NewRect("Body", r.root);
        r.body.anchorMin = r.body.anchorMax = new Vector2(0.5f, 0.5f);
        r.body.sizeDelta = Vector2.zero;
        Sprite ms = Resources.Load<Sprite>(Dir + MachineSprites[i % MachineSprites.Length]);
        r.machine = Img(NewRect("Machine", r.body), ms);
        RectTransform mt = r.machine.rectTransform;
        mt.anchorMin = mt.anchorMax = new Vector2(0.5f, 0.5f); mt.pivot = new Vector2(0.5f, 0f);
        mt.sizeDelta = ms != null ? new Vector2(MachineSize.x, MachineSize.x * ms.rect.height / ms.rect.width) : MachineSize;
        mt.anchoredPosition = new Vector2(0f, MachineBottom);
        if (ms == null) r.machine.enabled = false;
        // 홀로그램 빛이 기계 윗부분에 비침
        r.glow = Img(NewRect("HoloLight", r.body), glow);
        RectTransform gt = r.glow.rectTransform;
        gt.anchorMin = gt.anchorMax = new Vector2(0.5f, 0.5f);
        gt.sizeDelta = new Vector2(MachineSize.x * 0.95f, MachineSize.y * 0.55f);
        gt.anchoredPosition = new Vector2(0f, topY - MachineSize.y * 0.08f);
        r.glow.color = new Color(HoloColor.r, HoloColor.g, HoloColor.b, 0f);

        // 홀로그램 패널 (버튼이 여기로 들어감)
        r.holo = NewRect("Holo", r.root);
        r.holo.anchorMin = r.holo.anchorMax = new Vector2(0.5f, 0.5f);
        r.holo.sizeDelta = PanelSize;
        r.holo.anchoredPosition = new Vector2(0f, topY + HoloLift);
        r.holo.localRotation = Quaternion.Euler(0f, 0f, HoloTilt);
        r.holo.localScale = new Vector3(0.3f, 0f, 1f);
        r.holoGroup = r.holo.gameObject.AddComponent<CanvasGroup>();
        r.holoGroup.alpha = 0f;

        r.body.anchoredPosition = new Vector2(0f, -(MachineSize.y + 120f)); // 처음엔 화면 아래에 숨어 있음
        return r;
    }

    private IEnumerator MenuIntro()
    {
        if (fade != null) fade.color = Color.black;
        if (buzzerRt != null && startMachine == null) // 예전 버저만 (새 시작 기계는 다른 기계처럼 올라옴)
        {
            buzzerGroup = buzzerRt.GetComponent<CanvasGroup>();
            if (buzzerGroup == null) buzzerGroup = buzzerRt.gameObject.AddComponent<CanvasGroup>();
            buzzerGroup.alpha = 0f; buzzerGroup.interactable = false; buzzerGroup.blocksRaycasts = false;
        }
        // 인트로 컷씬이 끝날 때까지 기다림
        yield return null;
        while (FindFirstObjectByType<IntroCutscene>() != null) yield return null;
        yield return null;

        float t0 = Time.unscaledTime;
        bool[] started = new bool[rigs.Count];
        while (true)
        {
            float t = Time.unscaledTime - t0;
            float f = Mathf.Clamp01(t / 1.4f);
            if (fade != null && !slamming) fade.color = new Color(0f, 0f, 0f, 1f - f * f * (3f - 2f * f));
            float z = Mathf.Clamp01(t / 2.6f);
            stageBase = Mathf.Lerp(1f, StageCloseUp, 1f - (1f - z) * (1f - z) * (1f - z)); // 클로즈업 (빠르게 시작해서 천천히 멈춤)
            if (buzzerGroup != null) buzzerGroup.alpha = Mathf.Clamp01((t - 0.9f) / 0.5f);
            for (int i = 0; i < rigs.Count; i++)
                if (!started[i] && t >= 1.0f + i * 0.32f) { started[i] = true; StartCoroutine(RigRise(rigs[i])); }
            bool all = true; foreach (Rig r in rigs) all &= r.ready;
            if (t > 2.6f && all) break;
            if (t > 6f) break; // 안전장치
            yield return null;
        }
        if (buzzerGroup != null) { buzzerGroup.alpha = 1f; buzzerGroup.interactable = true; buzzerGroup.blocksRaycasts = true; }
        IntroDone = true;
    }

    // 기계가 덜컹거리며 올라옴 → 연기 → 홀로그램 튀어나옴
    private IEnumerator RigRise(Rig r)
    {
        float hidden = r.hidden;
        if (r.holo == null) Sfx.Play(MachineSound, 0.5f, 0.06f);   // v139: 기계 소리는 시작 버튼 기계가 올라올 때 한 번만
        float t0 = Time.unscaledTime;
        const float dur = 0.75f;
        while (true)
        {
            float u = Mathf.Clamp01((Time.unscaledTime - t0) / dur);
            // 덜컹: 올라가다 걸림 → 다시 올라가며 살짝 넘침
            float p;
            if (u < 0.42f) { float k = u / 0.42f; p = 0.58f * (1f - (1f - k) * (1f - k)); }
            else if (u < 0.55f) p = 0.58f - 0.03f * Mathf.Sin((u - 0.42f) / 0.13f * Mathf.PI);
            else { float k = (u - 0.55f) / 0.45f; p = Mathf.LerpUnclamped(0.58f, 1f, 1f + 2.2f * Mathf.Pow(k - 1f, 3) + 1.2f * Mathf.Pow(k - 1f, 2)); }
            float shake = (1f - u) * 3f;
            r.body.anchoredPosition = new Vector2(Random.Range(-shake, shake), Mathf.LerpUnclamped(hidden, 0f, p));
            r.body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(u * 38f) * 1.6f * (1f - u));
            if (u >= 1f) break;
            if (u > 0.42f && u < 0.44f) Puff(r, 3, 0.6f);   // 걸릴 때 연기 조금
            yield return null;
        }
        // 착지: 쿵 + 연기 + 살짝 튐
        r.body.localRotation = Quaternion.identity;
        Sfx.Play(TowerDrop.DeploySound, 0.55f, 0.08f);
        Puff(r, 9, 1f);
        float l0 = Time.unscaledTime;
        while (Time.unscaledTime - l0 < 0.14f)
        {
            float k = (Time.unscaledTime - l0) / 0.14f;
            r.body.anchoredPosition = new Vector2(0f, -7f * Mathf.Sin(k * Mathf.PI));
            yield return null;
        }
        r.body.anchoredPosition = Vector2.zero;
        if (r.holo == null) // 시작 버튼 기계: 홀로그램 없음
        {
            r.group.interactable = true; r.group.blocksRaycasts = true;
            r.ready = true;
            yield break;
        }
        yield return new WaitForSecondsRealtime(0.12f);

        // 홀로그램: 아래에서 펼쳐지며 지지직
        Sfx.UIOpen();
        float h0 = Time.unscaledTime;
        const float hd = 0.38f;
        while (true)
        {
            float k = Mathf.Clamp01((Time.unscaledTime - h0) / hd);
            float sy = k < 0.7f ? Mathf.Lerp(0f, 1.12f, k / 0.7f) : Mathf.Lerp(1.12f, 1f, (k - 0.7f) / 0.3f);
            float sx = Mathf.Lerp(0.3f, 1f, Mathf.Min(1f, k * 1.6f));
            r.holo.localScale = new Vector3(sx, sy, 1f);
            r.holoGroup.alpha = k < 0.85f ? (Random.value < 0.35f ? 0.35f : 0.95f) : 1f; // 깜빡깜빡
            SetHoloLight(r, k);
            if (k >= 1f) break;
            yield return null;
        }
        r.holo.localScale = Vector3.one;
        r.holoGroup.alpha = 1f;
        r.group.interactable = true; r.group.blocksRaycasts = true;
        r.ready = true;
    }

    private IEnumerator PlayLater(string clip, float delay, float vol)
    {
        yield return new WaitForSecondsRealtime(delay);
        Sfx.Play(clip, vol, 0.04f);
    }

    // 홀로그램 빛 세기 (0~1) + 은은하게 일렁임
    private void SetHoloLight(Rig r, float k)
    {
        if (r.glow == null || r.beam == null) return;
        float t = Time.unscaledTime + r.phase;
        float flick = 0.9f + 0.07f * Mathf.Sin(t * 7.3f) + 0.05f * Mathf.Sin(t * 17.1f) + (Random.value < 0.01f ? -0.25f : 0f);
        Color c = HoloColor;
        r.glow.color = new Color(c.r, c.g, c.b, 0.32f * k * flick);
        r.beam.color = new Color(c.r, c.g, c.b, 0.38f * k * flick);
    }

    private void UpdateRigs()
    {
        foreach (Rig r in rigs)
        {
            if (!r.ready || r.holoGroup == null) continue;
            SetHoloLight(r, 1f);
            float t = Time.unscaledTime + r.phase;
            r.holoGroup.alpha = 0.93f + 0.07f * Mathf.Sin(t * 11f) + (Random.value < 0.006f ? -0.35f : 0f); // 가끔 지직
        }
    }

    // 기계 아래쪽에서 연기 (회색 둥근 연기가 퍼지며 올라가 사라짐)
    private void Puff(Rig r, int n, float strength)
    {
        for (int i = 0; i < n; i++) StartCoroutine(PuffOne(r, strength));
    }
    private IEnumerator PuffOne(Rig r, float strength)
    {
        Image im = Img(NewRect("Smoke", r.root), glow);
        RectTransform rt = im.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        float side = Random.Range(-1f, 1f);
        Vector2 pos = new Vector2(side * r.width * 0.5f, -540f + Random.Range(0f, 40f));
        Vector2 vel = new Vector2(side * Random.Range(40f, 120f), Random.Range(30f, 90f)) * strength;
        float s0 = Random.Range(60f, 110f) * strength, life = Random.Range(0.7f, 1.2f);
        float g = Random.Range(0.45f, 0.62f);
        float t0 = Time.unscaledTime;
        while (true)
        {
            float u = (Time.unscaledTime - t0) / life;
            if (u >= 1f) break;
            pos += vel * Time.unscaledDeltaTime; vel *= 1f - 1.5f * Time.unscaledDeltaTime;
            rt.anchoredPosition = pos;
            float s = s0 * (1f + 1.4f * u);
            rt.sizeDelta = new Vector2(s * 1.3f, s);
            im.color = new Color(g, g, g * 1.02f, 0.55f * (1f - u) * Mathf.Min(1f, u * 6f));
            yield return null;
        }
        Destroy(im.gameObject);
    }

    // ===== 배경 (v130: 층으로 나눔) =====
    //  뒤에서부터: 바깥 풍경 → 구름(천천히 옆으로) → 새떼(가끔) → [폐허 묶음: 덩굴·전깃줄(구멍 뒤) → 폐허 그림 → 스파크]
    //  게임 시작 때 폐허 묶음만 빠르게 확대 → 바깥으로 나가는 느낌
    //  좌표는 그림 원본(2752×1536) 픽셀 기준, 왼쪽 위가 0,0
    public const float ArtW = 2752f, ArtH = 1536f;
    public static Vector2 HoleCenter = new Vector2(1445f, 860f);      // 확대 중심 (폐허 구멍 가운데)
    private static readonly Vector2 CloudArtSize = new Vector2(1699f, 472f), Vine1ArtSize = new Vector2(496f, 1341f), Vine2ArtSize = new Vector2(324f, 1331f);
    public static readonly Vector2[] SparkPoints =
    {
        new Vector2(426f, 600f),   // 왼쪽 벽 주황 표시등
        new Vector2(590f, 975f),   // 왼쪽 켜진 단말기
        new Vector2(862f, 645f),   // 천장에서 늘어진 끊어진 전선 끝
        new Vector2(2480f, 620f),  // 오른쪽 초록 표시등 패널
        new Vector2(2585f, 1075f), // 오른쪽 단말기 화면
    };
    private RectTransform stage, ruinsGroup, outside, sparkLayer, birdLayer;
    private RectTransform[] clouds;
    private float[] cloudSpeed;
    private RectTransform[] vines;
    private float[] vinePhase;
    private float[] nextSpark;
    private float nextBirds;
    private Sprite pixel, glow, birdA, birdB;
    private bool zooming;

    private void BuildBackground()
    {
        Sprite land = Resources.Load<Sprite>(Dir + "Menu_Landscape"), ruins = Resources.Load<Sprite>(Dir + "Menu_Ruins");
        RectTransform holder = NewRect("MenuBG", canvasRt);
        Stretch(holder);
        holder.SetAsFirstSibling();
        holder.gameObject.AddComponent<RectMask2D>();
        Image back = holder.gameObject.AddComponent<Image>(); back.color = Color.black; back.raycastTarget = false; // 가장자리 여백용

        stage = NewRect("Stage", holder);
        stage.anchorMin = stage.anchorMax = new Vector2(0.5f, 0.5f);
        AspectRatioFitter f = stage.gameObject.AddComponent<AspectRatioFitter>();
        f.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        f.aspectRatio = ArtW / ArtH;
        bgPic = stage;

        if (land == null || ruins == null)
        {
            // 예전 한 장짜리 배경
            Sprite sp = Resources.Load<Sprite>(Dir + "MainMenu_BG");
            if (sp != null) Img(Stretch(NewRect("Picture", stage)), sp);
            return;
        }

        pixel = MakeSprite(Tex(1, 1, (x, y) => Color.white));
        glow = MakeSprite(Tex(32, 32, (x, y) => { float d = new Vector2(x - 15.5f, y - 15.5f).magnitude / 16f; float a = Mathf.Clamp01(1f - d); return new Color(1f, 1f, 1f, a * a); }), FilterMode.Bilinear);
        MakeBirdSprites();

        // 바깥 (풍경·구름·새) — 확대 때 그대로
        outside = Stretch(NewRect("Outside", stage));
        outside.pivot = ArtNorm(HoleCenter);
        outside.offsetMin = outside.offsetMax = Vector2.zero;
        Img(Stretch(NewRect("Landscape", outside)), land);
        Sprite cloud = Resources.Load<Sprite>(Dir + "Menu_Clouds");
        if (cloud != null)
        {
            clouds = new RectTransform[2]; cloudSpeed = new[] { 14f, 8f };
            // 뒤쪽: 작고 옅게, 뒤집어서 / 앞쪽: 원래 크기
            clouds[1] = ArtRect("CloudFar", outside, new Vector2(1900f, 360f), CloudArtSize * 0.62f, new Vector2(0.5f, 0.5f));
            Image far = Img(clouds[1], cloud); far.color = new Color(1f, 1f, 1f, 0.75f);
            clouds[1].localScale = new Vector3(-1f, 1f, 1f);
            clouds[0] = ArtRect("Cloud", outside, new Vector2(1300f, 480f), CloudArtSize, new Vector2(0.5f, 0.5f));
            Img(clouds[0], cloud);
        }
        birdLayer = Stretch(NewRect("Birds", outside));
        nextBirds = Time.unscaledTime + Random.Range(2f, 5f);

        // 폐허 묶음 (확대 대상). 중심 = 구멍 가운데
        ruinsGroup = Stretch(NewRect("Ruins", stage));
        ruinsGroup.pivot = ArtNorm(HoleCenter);   // 확대 중심
        ruinsGroup.offsetMin = ruinsGroup.offsetMax = Vector2.zero;

        // 구멍 뒤에 매달린 덩굴 (윗부분은 천장에 가려짐)
        vines = new RectTransform[2]; vinePhase = new[] { 0f, 2.1f };
        Sprite v1 = Resources.Load<Sprite>(Dir + "Menu_Vine_1"), v2 = Resources.Load<Sprite>(Dir + "Menu_Vine_2");
        if (v1 != null) { vines[0] = ArtRect("Vine1", ruinsGroup, new Vector2(1010f, 215f), Vine1ArtSize * 0.5f, new Vector2(0.5f, 1f)); Img(vines[0], v1); }
        if (v2 != null) { vines[1] = ArtRect("Vine2", ruinsGroup, new Vector2(1890f, 330f), Vine2ArtSize * 0.55f, new Vector2(0.5f, 1f)); Img(vines[1], v2); }

        // 전깃줄: 천장에서 늘어진 줄 1 + 두 점 사이에 처진 줄 1 (구멍 뒤)
        Color wire = new Color(0.07f, 0.07f, 0.08f, 1f);
        UICable c1 = AddCable("CableLoose");
        c1.loose = true; c1.a = ArtNorm(new Vector2(1520f, 255f)); c1.length = 0.15f; c1.thickness = 0.0028f; c1.swayAmp = 7f; c1.swaySpeed = 1.3f; c1.color = wire; c1.raycastTarget = false;
        UICable c2 = AddCable("CableSag");
        c2.a = ArtNorm(new Vector2(1180f, 262f)); c2.b = ArtNorm(new Vector2(1700f, 270f)); c2.sag = 0.075f; c2.thickness = 0.0024f; c2.swayAmp = 9f; c2.swaySpeed = 0.9f; c2.phase = 1.3f; c2.color = wire; c2.raycastTarget = false;

        Img(Stretch(NewRect("RuinsPicture", ruinsGroup)), ruins);
        sparkLayer = Stretch(NewRect("Sparks", ruinsGroup));
        nextSpark = new float[SparkPoints.Length];
        for (int i = 0; i < nextSpark.Length; i++) nextSpark[i] = Time.unscaledTime + Random.Range(0.5f, 4f);
    }

    // v131: UI 그림에는 CanvasRenderer가 먼저 있어야 함 (없으면 RectMask2D가 오류)
    private UICable AddCable(string name)
    {
        RectTransform rt = Stretch(NewRect(name, ruinsGroup));
        if (rt.GetComponent<CanvasRenderer>() == null) rt.gameObject.AddComponent<CanvasRenderer>();
        return rt.gameObject.AddComponent<UICable>();
    }

    // ----- 배경 움직임 -----
    private void AnimateBackground()
    {
        if (stage == null) return;
        float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        float unit = stage.rect.width / ArtW; // 그림 1px = 화면 몇 단위

        // 구름: 왼쪽으로 천천히, 화면 밖으로 나가면 오른쪽에서 다시
        if (clouds != null)
            for (int i = 0; i < clouds.Length; i++)
            {
                RectTransform c = clouds[i];
                if (c == null) continue;
                Vector2 p = c.anchoredPosition;
                p.x -= cloudSpeed[i] * unit * dt;
                float w = c.rect.width;
                if (c.anchorMin.x * stage.rect.width + p.x + w < 0f) p.x += stage.rect.width + w; // 왼쪽 밖 → 오른쪽 밖
                c.anchoredPosition = p;
            }

        // 덩굴: 매달린 곳을 중심으로 살랑살랑
        if (vines != null)
            for (int i = 0; i < vines.Length; i++)
                if (vines[i] != null)
                {
                    float a = 1.6f * Mathf.Sin(t * 0.9f + vinePhase[i]) + 0.7f * Mathf.Sin(t * 2.1f + vinePhase[i] * 1.7f);
                    vines[i].localRotation = Quaternion.Euler(0f, 0f, a);
                }

        // 스파크: 기계마다 가끔 튐
        if (nextSpark != null && !zooming)
            for (int i = 0; i < nextSpark.Length; i++)
                if (t >= nextSpark[i])
                {
                    nextSpark[i] = t + Random.Range(2.5f, 7f);
                    StartCoroutine(SparkBurst(SparkPoints[i], unit));
                }

        // 새떼: 가끔 하늘을 가로질러 날아감
        if (birdLayer != null && t >= nextBirds)
        {
            nextBirds = t + Random.Range(9f, 18f);
            StartCoroutine(Flock(unit));
        }
    }

    private IEnumerator SparkBurst(Vector2 art, float unit)
    {
        Vector2 at = ArtLocal(art);
        int n = Random.Range(7, 15);
        var parts = new RectTransform[n]; var vel = new Vector2[n]; var life = new float[n]; var imgs = new Image[n];
        Image g = Img(NewRect("SparkGlow", sparkLayer), glow);
        g.rectTransform.anchorMin = g.rectTransform.anchorMax = Vector2.zero;
        g.rectTransform.sizeDelta = Vector2.one * 90f * unit;
        g.rectTransform.anchoredPosition = at;
        for (int i = 0; i < n; i++)
        {
            parts[i] = NewRect("Spark", sparkLayer);
            parts[i].anchorMin = parts[i].anchorMax = Vector2.zero;
            float s = Random.Range(4f, 8f) * unit;
            parts[i].sizeDelta = new Vector2(s, s);
            parts[i].anchoredPosition = at;
            imgs[i] = Img(parts[i], pixel);
            float ang = Random.Range(20f, 160f) * Mathf.Deg2Rad; // 위쪽으로 튀었다가 떨어짐
            vel[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(140f, 380f) * unit;
            life[i] = Random.Range(0.25f, 0.6f);
        }
        float t0 = Time.unscaledTime;
        while (true)
        {
            float age = Time.unscaledTime - t0, dt = Time.unscaledDeltaTime;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (parts[i] == null) continue;
                float u = age / life[i];
                if (u >= 1f) { Destroy(parts[i].gameObject); parts[i] = null; continue; }
                any = true;
                vel[i] += Vector2.down * 900f * unit * dt;
                parts[i].anchoredPosition += vel[i] * dt;
                imgs[i].color = Color.Lerp(new Color(1f, 0.97f, 0.75f, 1f), new Color(1f, 0.45f, 0.1f, 0f), u);
            }
            if (g != null) g.color = new Color(1f, 0.8f, 0.45f, 0.55f * Mathf.Clamp01(1f - age / 0.18f));
            if (!any) break;
            yield return null;
        }
        if (g != null) Destroy(g.gameObject);
    }

    private IEnumerator Flock(float unit)
    {
        int n = Random.Range(5, 10);
        bool toLeft = Random.value < 0.5f;
        float y = Random.Range(330f, 620f);              // 하늘 높이 (그림 기준)
        float speed = Random.Range(90f, 140f) * unit;
        float size = Random.Range(9f, 14f) * unit;
        var birds = new RectTransform[n]; var imgs = new Image[n]; var offs = new Vector2[n]; var ph = new float[n];
        Vector2 start = ArtLocal(new Vector2(toLeft ? 2350f : 500f, y));
        for (int i = 0; i < n; i++)
        {
            birds[i] = NewRect("Bird", birdLayer);
            birds[i].anchorMin = birds[i].anchorMax = Vector2.zero;
            birds[i].sizeDelta = new Vector2(size * 1.75f, size);
            imgs[i] = Img(birds[i], birdA);
            imgs[i].color = new Color(0.12f, 0.14f, 0.18f, 0.9f);
            int row = (i + 1) / 2; int sideSign = i % 2 == 0 ? 1 : -1;  // V자
            offs[i] = new Vector2((toLeft ? 1f : -1f) * row * size * 2.2f, sideSign * row * size * 1.3f) + Random.insideUnitCircle * size * 0.6f;
            ph[i] = Random.value * 6f;
        }
        float dist = stage.rect.width * 0.75f;
        float t0 = Time.unscaledTime;
        while ((Time.unscaledTime - t0) * speed < dist && !zooming)
        {
            float age = Time.unscaledTime - t0;
            Vector2 head = start + new Vector2((toLeft ? -1f : 1f) * age * speed, Mathf.Sin(age * 0.6f) * size * 2f);
            float fadeK = Mathf.Clamp01(Mathf.Min(age * 1.5f, (dist / speed - age) * 1.5f));
            for (int i = 0; i < n; i++)
            {
                birds[i].anchoredPosition = head + offs[i] + new Vector2(0f, Mathf.Sin(age * 3f + ph[i]) * size * 0.3f);
                imgs[i].sprite = Mathf.Repeat(age * 7f + ph[i], 1f) < 0.5f ? birdA : birdB;   // 날갯짓
                birds[i].localScale = new Vector3(toLeft ? -1f : 1f, 1f, 1f);
                Color c = imgs[i].color; c.a = 0.9f * fadeK; imgs[i].color = c;
            }
            yield return null;
        }
        foreach (RectTransform b in birds) if (b != null) Destroy(b.gameObject);
    }

    // 작은 도트 새 (날개 위/아래 2장, 7×4)
    private void MakeBirdSprites()
    {
        string[] up = { "X.....X", ".X...X.", "..XXX..", "...X..." };
        string[] dn = { ".......", "...X...", ".XXXXX.", "X.....X" };
        birdA = MakeSprite(Pattern(up)); birdB = MakeSprite(Pattern(dn));
    }
    private static Texture2D Pattern(string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        return Tex(w, h, (x, y) => rows[h - 1 - y][x] == 'X' ? Color.white : new Color(1f, 1f, 1f, 0f));
    }
    private static Texture2D Tex(int w, int h, System.Func<int, int, Color> f)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.SetPixel(x, y, f(x, y));
        t.Apply();
        return t;
    }
    private static Sprite MakeSprite(Texture2D t, FilterMode mode = FilterMode.Point)
    {
        t.filterMode = mode;
        return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
    }

    // 그림 좌표 → 0~1 비율 (왼쪽 아래 0,0)
    private static Vector2 ArtNorm(Vector2 art) => new Vector2(art.x / ArtW, 1f - art.y / ArtH);
    // 그림 좌표 → stage 안 위치 (왼쪽 아래 기준 단위)
    private Vector2 ArtLocal(Vector2 art) { Vector2 n = ArtNorm(art); return new Vector2(n.x * stage.rect.width, n.y * stage.rect.height); }
    // 그림 좌표·크기로 놓기 (anchor로 붙여서 화면 크기가 바뀌어도 그림과 같이 커짐)
    private RectTransform ArtRect(string name, RectTransform parent, Vector2 artPos, Vector2 artSize, Vector2 pivot)
    {
        RectTransform rt = NewRect(name, parent);
        Vector2 n = ArtNorm(artPos);
        Vector2 sz = new Vector2(artSize.x / ArtW, artSize.y / ArtH);
        rt.pivot = pivot;
        rt.anchorMin = n - Vector2.Scale(sz, pivot);
        rt.anchorMax = rt.anchorMin + sz;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }
    private static Image Img(RectTransform rt, Sprite sp)
    {
        Image i = rt.gameObject.AddComponent<Image>();
        i.sprite = sp; i.raycastTarget = false;
        return i;
    }

    // ===== 버튼 =====
    private void StyleButtons()
    {
        Sprite green = Resources.Load<Sprite>(Dir + "Panel_Normal"), orange = Resources.Load<Sprite>(Dir + "Panel_Hover");
        EnsureFxSprites();
        // v134: 패널마다 기계(아래) + 홀로그램 패널(주황, 기계에서 튀어나옴). 마우스를 올리면 초록
        Sprite holo = orange != null ? orange : green, holoHover = green != null ? green : orange;
        int row = 0;
        foreach (string n in PanelOrder)
        {
            Button b = Find(n);
            if (b == null) continue;
            int i = row++;
            Rig rig = BuildRig(i, PanelStart.x + PanelStep * i);
            RectTransform rt = (RectTransform)b.transform;
            rt.SetParent(rig.holo, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = PanelSize;
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one;
            Image img = b.GetComponent<Image>();
            if (img == null) img = b.gameObject.AddComponent<Image>();
            img.sprite = holo; img.type = Image.Type.Simple; img.preserveAspect = false; img.color = Color.white;
            b.targetGraphic = img;
            b.transition = Selectable.Transition.SpriteSwap;
            b.spriteState = new SpriteState { highlightedSprite = holoHover, pressedSprite = holoHover, selectedSprite = holo, disabledSprite = holo };
            ResetTint(img);
            Text label = b.GetComponentInChildren<Text>(true);
            StyleLabel(label, 36, Vector2.zero);
            float yaw = HoloYaws[Mathf.Min(i, HoloYaws.Length - 1)];
            AddPerspective(img, yaw, rt);
            if (label != null) AddPerspective(label, yaw, rt);
            if (b.GetComponent<UIHoverScale>() == null) b.gameObject.AddComponent<UIHoverScale>().hoverScale = 1.04f;
            rigs.Add(rig);
        }

        startButton = Find("Main_GameStart");
        if (startButton != null && buzzer != null && startMachine != null)
        {
            BuildStartRig();
        }
        else if (startButton != null && buzzer != null)
        {
            buzzerRt = (RectTransform)startButton.transform;
            buzzerRt.anchorMin = buzzerRt.anchorMax = buzzerRt.pivot = new Vector2(0.5f, 0.5f);
            buzzerRt.sizeDelta = BuzzerSize;
            buzzerRt.anchoredPosition = BuzzerPos;
            buzzerRt.localScale = Vector3.one;
            buzzerImg = startButton.GetComponent<Image>();
            if (buzzerImg == null) buzzerImg = startButton.gameObject.AddComponent<Image>();
            buzzerImg.sprite = buzzer; buzzerImg.color = Color.white; buzzerImg.preserveAspect = true;
            startButton.transition = Selectable.Transition.None;
            // 원래 연결된 기능(StartGame)은 내리치는 연출이 끝난 뒤에 부름
            startButton.onClick = new Button.ButtonClickedEvent();
            startButton.onClick.AddListener(OnBuzzer);
            if (startButton.GetComponent<UISoundMute>() == null) startButton.gameObject.AddComponent<UISoundMute>();
            if (startButton.GetComponent<UIHoverScale>() == null) startButton.gameObject.AddComponent<UIHoverScale>().hoverScale = 1.03f;
            buzzerBasePos = buzzerRt.anchoredPosition;
            // 글자는 버저 아래 명판처럼
            Text t = startButton.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                RectTransform tr = t.rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f); tr.pivot = new Vector2(0.5f, 0f); // 버저 위에
                tr.sizeDelta = new Vector2(420f, 50f);
                tr.anchoredPosition = new Vector2(0f, 2f);
                StyleLabel(t, 36, Vector2.zero);
                t.color = new Color(1f, 0.85f, 0.3f);
            }
        }
    }

    // v137 시작 버튼 기계: 세로 캐비닛이 다른 기계처럼 아래에서 올라오고, 가운데 빈 판에 시작 버튼
    // v143 시작 버튼 기계: 캐비닛 그림 3장(덮개 닫힘·열림·눌림)이 통째로 바뀜. 버튼 자리에 보이지 않는 누름 영역
    //  캐비닛이 다른 기계처럼 아래에서 올라오고, 위쪽 계기판에 노란 '게임 시작' 패널
    private Image startCab, startLabelPanel;
    private RectTransform startHitPoint;

    private static void AddPerspective(Graphic g, float yaw, RectTransform reference)
    {
        if (g == null) return;
        UIFakePerspective p = g.GetComponent<UIFakePerspective>();
        if (p == null) p = g.gameObject.AddComponent<UIFakePerspective>();
        p.Set(yaw, reference);
    }
    private void BuildStartRig()
    {
        float h = StartMachineWidth * startMachine.rect.height / startMachine.rect.width;
        Rig r = new Rig { phase = Random.value * 10f, hidden = -(h + 120f), width = StartMachineWidth };
        r.root = NewRect("StartRig", canvasRt);
        r.root.anchorMin = r.root.anchorMax = new Vector2(0.5f, 0.5f);
        r.root.sizeDelta = Vector2.zero;
        r.root.anchoredPosition = new Vector2(StartMachineX, 0f);
        r.group = r.root.gameObject.AddComponent<CanvasGroup>();
        r.group.interactable = false; r.group.blocksRaycasts = false;
        r.body = NewRect("Body", r.root);
        r.body.anchorMin = r.body.anchorMax = new Vector2(0.5f, 0.5f);
        r.body.sizeDelta = Vector2.zero;
        r.machine = Img(NewRect("Machine", r.body), startMachine);
        RectTransform mt = r.machine.rectTransform;
        mt.anchorMin = mt.anchorMax = new Vector2(0.5f, 0.5f); mt.pivot = new Vector2(0.5f, 0f);
        mt.sizeDelta = new Vector2(StartMachineWidth, h);
        mt.anchoredPosition = new Vector2(0f, StartMachineBottom);
        startCab = r.machine;

        // v144 누름 영역 (보이지 않음) = 캐비닛 전체 (계기판의 '게임 시작' 패널 포함). 주먹은 빨간 버튼 자리로
        startHitPoint = NewRect("ButtonPoint", r.body);
        startHitPoint.anchorMin = startHitPoint.anchorMax = startHitPoint.pivot = new Vector2(0.5f, 0.5f);
        startHitPoint.sizeDelta = new Vector2(StartButtonHit.x * StartMachineWidth, StartButtonHit.y * h);
        startHitPoint.anchoredPosition = new Vector2((StartButtonAt.x - 0.5f) * StartMachineWidth, StartMachineBottom + StartButtonAt.y * h);
        buzzerRt = (RectTransform)startButton.transform;
        buzzerRt.SetParent(r.body, false);
        buzzerRt.anchorMin = buzzerRt.anchorMax = new Vector2(0.5f, 0.5f); buzzerRt.pivot = new Vector2(0.5f, 0f);
        buzzerRt.sizeDelta = new Vector2(StartMachineWidth * 0.9f, h);
        buzzerRt.anchoredPosition = new Vector2(0f, StartMachineBottom);
        buzzerRt.localScale = Vector3.one; buzzerRt.localRotation = Quaternion.identity;
        buzzerBasePos = buzzerRt.anchoredPosition;
        buzzerHitY = 0.5f;
        Image hit = startButton.GetComponent<Image>();
        if (hit == null) hit = startButton.gameObject.AddComponent<Image>();
        hit.sprite = null; hit.color = new Color(1f, 1f, 1f, 0f); hit.raycastTarget = true;
        ResetTint(hit); hit.canvasRenderer.SetAlpha(0f);
        startButton.targetGraphic = hit;
        startButton.transition = Selectable.Transition.None;
        buzzerImg = startCab;   // 눌림 연출(Slam)은 캐비닛 그림을 바꿈
        startButton.onClick = new Button.ButtonClickedEvent();
        startButton.onClick.AddListener(OnBuzzer);
        if (startButton.GetComponent<UISoundMute>() == null) startButton.gameObject.AddComponent<UISoundMute>();
        if (startButton.GetComponent<UIHoverScale>() == null) startButton.gameObject.AddComponent<UIHoverScale>().hoverScale = 1f; // 마우스 올림 감지용

        // 위쪽 계기판에 노란 '게임 시작' 패널
        Sprite yellow = Resources.Load<Sprite>(Dir + "Panel_Yellow");
        startLabelPanel = Img(NewRect("StartLabel", r.body), yellow != null ? yellow : Resources.Load<Sprite>(Dir + "Panel_Hover"));
        RectTransform lp = startLabelPanel.rectTransform;
        lp.anchorMin = lp.anchorMax = new Vector2(0.5f, 0.5f);
        lp.sizeDelta = new Vector2(StartLabelWidth, StartLabelWidth * 95f / 360f);
        lp.anchoredPosition = new Vector2((StartLabelAt.x - 0.5f) * StartMachineWidth, StartMachineBottom + StartLabelAt.y * h);
        AddPerspective(startLabelPanel, StartLabelYaw, lp);
        Text t = startButton.GetComponentInChildren<Text>(true);
        if (t != null)
        {
            t.transform.SetParent(lp, false);
            RectTransform tr = t.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.pivot = new Vector2(0.5f, 0.5f);
            tr.offsetMin = tr.offsetMax = Vector2.zero; tr.localScale = Vector3.one; tr.localRotation = Quaternion.identity;
            StyleLabel(t, 36, Vector2.zero);
            t.color = Color.white;
            AddPerspective(t, StartLabelYaw, lp);
        }
        r.body.anchoredPosition = new Vector2(0f, r.hidden);
        rigs.Insert(0, r); // 가장 먼저 올라옴
    }

    // 캐비닛 그림: 마우스 올림 = 덮개 열림, 누르는 중 = 눌림, 평소 = 덮개 닫힘
    private void UpdateStartCab()
    {
        if (startCab == null || slamming || startButton == null) return;
        UIHoverScale hs = startButton.GetComponent<UIHoverScale>();
        bool ready = rigs.Count > 0 && rigs[0].ready;
        bool hover = ready && hs != null && hs.Hovered;
        Sprite want = !hover ? buzzer : (Input.GetMouseButton(0) && buzzerPressed != null ? buzzerPressed : (buzzerOpen != null ? buzzerOpen : buzzer));
        if (startCab.sprite != want) startCab.sprite = want;
    }

    // 버튼이 처음에 '색 바꾸기' 방식이었으면, 누를 수 없는 상태로 옮겨질 때 반투명 회색이 입혀진 채 남음 → 흰색으로 되돌림
    private static void ResetTint(Graphic g)
    {
        if (g == null) return;
        g.CrossFadeColor(Color.white, 0f, true, true);
        g.canvasRenderer.SetColor(Color.white);
    }

    private static void StyleLabel(Text t, int size, Vector2 offset)
    {
        if (t == null) return;
        UIFont.Apply(t, FontStyle.Normal);
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        if (t.GetComponent<Outline>() == null)
        {
            Outline o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.85f); o.effectDistance = new Vector2(3f, -3f);
        }
    }

    private static Button Find(string name)
    {
        foreach (Button b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b.name == name) return b;
        return null;
    }

    // ===== 손 커서 · 화면 효과 (메뉴 위, 인트로 컷씬 아래) =====
    private void BuildOverlay()
    {
        GameObject go = new GameObject("MenuOverlay", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        Canvas c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 3000;
        CanvasScaler s = go.AddComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920f, 1080f);
        s.matchWidthOrHeight = 0.5f;
        RectTransform root = (RectTransform)go.transform;

        flash = Stretch(NewRect("AlarmFlash", root)).gameObject.AddComponent<Image>();
        flash.color = new Color(1f, 0.05f, 0.02f, 0f); flash.raycastTarget = false;

        if (handPoint != null)
        {
            hand = NewRect("Hand", root);
            hand.anchorMin = hand.anchorMax = Vector2.zero;
            hand.sizeDelta = HandSize;
            hand.pivot = PointTip;
            handImg = hand.gameObject.AddComponent<Image>();
            handImg.sprite = handPoint; handImg.raycastTarget = false;
            Cursor.visible = false;
            handPos = MouseCanvas(root);
            hand.anchoredPosition = handPos;
        }

        fade = Stretch(NewRect("Fade", root)).gameObject.AddComponent<Image>();
        fade.color = new Color(0f, 0f, 0f, 0f); fade.raycastTarget = false;
        fadeRt = fade.rectTransform;
    }

    private Vector2 MouseCanvas(RectTransform root)
    {
        Vector2 size = root.rect.size;
        return new Vector2(Input.mousePosition.x / Mathf.Max(1, Screen.width) * size.x, Input.mousePosition.y / Mathf.Max(1, Screen.height) * size.y);
    }

    void Update()
    {
        if (hand == null || slamming) return;
        RectTransform root = (RectTransform)hand.parent;
        Vector2 target = MouseCanvas(root);
        // 팔 길이만큼만: 잘린 팔 끝이 화면 아래 밖에 있도록 손 높이 제한. 손이 닿지 않는 곳에서는 보통 커서를 보여 줌
        float maxY = (PointTip.y - ArmCutTop) * HandSize.y - 24f;
        Cursor.visible = target.y > maxY + 4f;
        target.y = Mathf.Min(target.y, maxY);
        Vector2 prev = handPos;
        handPos = Vector2.Lerp(handPos, target, 1f - Mathf.Exp(-30f * Time.unscaledDeltaTime));
        handVel = Vector2.Lerp(handVel, (handPos - prev) / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.2f);
        hand.anchoredPosition = handPos;
        // 움직이는 방향으로 살짝 기울고, 누르는 순간 살짝 눌림
        float tilt = Mathf.Clamp(-handVel.x * 0.003f, -3f, 3f);
        hand.localRotation = Quaternion.Lerp(hand.localRotation, Quaternion.Euler(0f, 0f, tilt), 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        float press = Input.GetMouseButton(0) ? 0.93f : 1f;
        hand.localScale = Vector3.Lerp(hand.localScale, Vector3.one * press, 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime));
    }

    // ===== 게임 시작 버저 =====
    private void OnBuzzer()
    {
        if (slamming) return;
        StartCoroutine(Slam());
    }

    private IEnumerator Slam()
    {
        slamming = true;
        Cursor.visible = false;
        if (startButton != null && startMachine != null)
        {   // 덮개 열린 그림으로 고정 (버튼을 끄면 덮개 닫힌 그림으로 돌아가는 것 방지)
            startButton.transition = Selectable.Transition.None;
            if (buzzerImg != null) { buzzerImg.overrideSprite = null; buzzerImg.sprite = buzzerOpen != null ? buzzerOpen : buzzer; }
        }
        foreach (Button b in FindObjectsByType<Button>(FindObjectsSortMode.None)) b.interactable = false;

        // 버저 빨간 버튼 위 (화면 좌표 → 손 캔버스 좌표)
        RectTransform root = hand != null ? (RectTransform)hand.parent : null;
        Vector2 hit = Vector2.zero;
        if (root != null && buzzerRt != null)
        {
            RectTransform tgt = startHitPoint != null ? startHitPoint : buzzerRt; // v144 시작 캐비닛: 빨간 버튼 자리
            Vector3 w = tgt.TransformPoint(new Vector3((0.5f - tgt.pivot.x) * tgt.rect.width, (buzzerHitY - tgt.pivot.y) * tgt.rect.height, 0f));
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, w);
            hit = new Vector2(sp.x / Screen.width * root.rect.width, sp.y / Screen.height * root.rect.height);
        }

        if (hand != null)
        {
            // 주먹으로 바꿈 (기준점: 주먹이 닿는 곳) → 아래로 당기며 크게(화면 쪽으로 끌어옴) → 힘껏 내리침
            Vector2 start = handPos;
            handImg.sprite = handFist != null ? handFist : handPoint;
            hand.pivot = handFist != null ? FistHit : PointTip;
            Vector2 up = hit + new Vector2(90f, -110f);
            yield return Move(start, up, Quaternion.Euler(0f, 0f, -5f), 1.18f, 0.16f, false);
            yield return Move(up, hit, Quaternion.identity, 0.95f, 0.06f, true);
        }

        // 쾅: 눌린 버저 + 소리 + 화면 흔들림 + 경고등
        if (startButton != null) startButton.transition = Selectable.Transition.None; // 눌린 그림 그대로 유지
        if (buzzerImg != null) buzzerImg.overrideSprite = null;
        if (buzzerImg != null && buzzerPressed != null) buzzerImg.sprite = buzzerPressed;
        Sfx.Play("UI/Buzzers", 0.9f, 0f);
        StartCoroutine(Shake(0.28f, 14f));
        if (buzzerRt != null) StartCoroutine(Squash(buzzerRt));

        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 0.7f)
        {
            float t = Time.unscaledTime - t0;
            flash.color = new Color(1f, 0.05f, 0.02f, 0.22f * Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 2.6f)));
            if (hand != null && t > 0.25f) hand.anchoredPosition = Vector2.Lerp(hand.anchoredPosition, hit + new Vector2(30f, 40f), 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            yield return null;
        }

        // v130 출발: 버튼·손이 사라지고, 폐허만 빠르게 확대 (바깥 풍경은 그대로) → 검은 화면 → 게임
        zooming = true;
        MusicPlayer.FadeOut(2.0f); // v142 메뉴 음악이 확대와 함께 사라지고, 게임에서 Survival Pulse가 서서히 시작
        var groups = new System.Collections.Generic.List<CanvasGroup>();
        foreach (Button b in FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            CanvasGroup cg = b.GetComponent<CanvasGroup>();
            if (cg == null) cg = b.gameObject.AddComponent<CanvasGroup>();
            groups.Add(cg);
        }
        foreach (Rig r in rigs) if (r.group != null) groups.Add(r.group); // v134 기계·홀로그램도 함께 사라짐
        float s0 = bgPic != null ? bgPic.localScale.x : 1f;
        t0 = Time.unscaledTime;
        const float hideT = 0.2f, zoomT = 1.9f;   // v133: 2배 길게, 더 천천히
        while (Time.unscaledTime - t0 < hideT + zoomT)
        {
            float t = Time.unscaledTime - t0;
            float h = Mathf.Clamp01(t / hideT);
            foreach (CanvasGroup cg in groups) if (cg != null) cg.alpha = 1f - h;
            if (handImg != null) handImg.color = new Color(1f, 1f, 1f, 1f - h);
            flash.color = new Color(1f, 0.05f, 0.02f, 0.22f * Mathf.Max(0f, Mathf.Sin((t + 0.7f) * Mathf.PI * 2.6f)) * (1f - h));

            float u = Mathf.Clamp01((t - hideT * 0.5f) / zoomT);
            float e = u * u;                           // 점점 빨라짐 (예전보다 완만하게)
            if (ruinsGroup != null) ruinsGroup.localScale = Vector3.one * Mathf.Lerp(1f, 3.6f, e);
            if (outside != null) outside.localScale = Vector3.one * Mathf.Lerp(1f, 1.12f, u);
            if (bgPic != null) bgPic.localScale = Vector3.one * s0;
            fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01((u - 0.45f) / 0.55f));
            yield return null;
        }
        fade.color = Color.black;

        Cursor.visible = true;
        MainMenuManager mm = FindFirstObjectByType<MainMenuManager>();
        if (mm != null) mm.StartGame();
        else SceneManager.LoadScene("In_Game");
    }

    private IEnumerator Move(Vector2 a, Vector2 b, Quaternion rot, float scale, float dur, bool accel)
    {
        Quaternion r0 = hand.localRotation; Vector3 s0 = hand.localScale;
        float t0 = Time.unscaledTime;
        while (true)
        {
            float u = Mathf.Clamp01((Time.unscaledTime - t0) / dur);
            float e = accel ? u * u : 1f - (1f - u) * (1f - u);
            hand.anchoredPosition = Vector2.LerpUnclamped(a, b, e);
            hand.localRotation = Quaternion.Slerp(r0, rot, e);
            hand.localScale = Vector3.Lerp(s0, Vector3.one * scale, e);
            if (u >= 1f) break;
            yield return null;
        }
        handPos = b;
    }

    private IEnumerator Shake(float dur, float amp)
    {
        RectTransform target = bgPic != null ? (RectTransform)bgPic.parent : null;
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < dur)
        {
            float k = 1f - (Time.unscaledTime - t0) / dur;
            Vector2 o = new Vector2(Random.Range(-amp, amp), Random.Range(-amp, amp)) * k;
            if (target != null) target.anchoredPosition = o;
            if (buzzerRt != null) buzzerRt.anchoredPosition = buzzerBasePos + o * 0.5f;
            yield return null;
        }
        if (target != null) target.anchoredPosition = Vector2.zero;
        if (buzzerRt != null) buzzerRt.anchoredPosition = buzzerBasePos;
    }

    private IEnumerator Squash(RectTransform rt)
    {
        UIHoverScale hs = rt.GetComponent<UIHoverScale>();
        if (hs != null) hs.enabled = false;
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 0.22f)
        {
            float u = (Time.unscaledTime - t0) / 0.22f;
            float sq = 1f - 0.07f * Mathf.Sin(u * Mathf.PI);
            rt.localScale = new Vector3(2f - sq, sq, 1f);
            yield return null;
        }
        rt.localScale = Vector3.one;
    }

    // 배경이 아주 천천히 숨 쉬듯 확대·축소
    void LateUpdate()
    {
        AnimateBackground();
        UpdateRigs();
        UpdateStartCab();
        if (bgPic == null || zooming) return;
        float s = stageBase + 0.012f * Mathf.Sin(Time.unscaledTime * 0.25f);
        bgPic.localScale = new Vector3(s, s, 1f);
    }

    void OnDestroy()
    {
        Sfx.HoverOverride = null;
        if (hand != null) Cursor.visible = true;
    }

    // ===== 도우미 =====
    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
    private static RectTransform Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }
}
