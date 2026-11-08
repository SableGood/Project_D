using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// v149 In_Game 시작 연출 + 출동 준비 (예전 전체 화면 SetupScreen 대신)
//  1) 카메라가 하늘에서 바닥(시작 지점)을 향해 빙글 돌며 내려옴 (클릭/Space/Enter로 건너뛰기)
//  2) 바닥을 가까이 보여 주는 동안, 화면 위쪽을 덮는 출동 준비 판이 덜컹거리며 내려옴 (v150: 재능·상점 창과 같은 양식)
//       난이도 · 테스트 모드 · 직업 8개(아이콘+이름 한 줄) · 고른 직업 설명. 아래 가운데에 [출동] 버튼
//       위쪽 HUD(웨이브 타이머 등)는 이 판에 가려짐
//  3) 출동 → 판이 위로 들어가고, 카메라가 게임 화면 높이로 줌 아웃 → 캐릭터가 공중에서 떨어져 착지 (먼지 + 쿵)
//  캐릭터는 고르지 않음 (항상 0번 = 침팬지)
//  키보드: ← → 또는 A D = 직업, 1 2 3 = 난이도, T = 테스트 모드, Enter = 출동
public class InGameIntro : MonoBehaviour
{
    private static readonly string[] DifficultyNames = { "쉬움", "보통", "어려움" };

    // ===== 카메라 =====
    public static float SwirlTime = 3.4f;          // 빙글 돌며 내려오는 시간
    public static float SwirlTurns = 0.85f;        // 몇 바퀴
    public static float StartHeight = 48f, StartRadius = 34f;
    public static Vector3 CloseOffset = new Vector3(0f, 4.2f, -3.0f);   // 고르는 동안 바닥을 가까이 보는 자리 (시작 지점 기준)
    public static float ZoomOutTime = 1.1f;
    public static float DropHeight = 14f, DropTime = 0.45f;

    // ===== 출동 준비 판 (재능 선택·상점 창과 같은 색) =====
    public static float BandHeight = 500f;         // 화면 위를 덮는 판 높이 (1920x1080 기준)
    public static float GoBottom = 64f;            // 출동 버튼 아랫변 높이
    private static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out Color c); return c; }
    private static readonly Color PanelBg = Hex("#3A3A3A"), RowBg = Hex("#333331"), Line = Hex("#4C4C4A"), LineStrong = Hex("#6A6A66");
    private static readonly Color Accent = Hex("#C6E7A7"), Dark = Hex("#232323"), TextMain = Hex("#EDEDE6"), TextSub = Hex("#A8A8A0");
    private static readonly Color Cons = Hex("#F08A7A");

    private GameManager gm;
    private int difficulty = 1, job = 0;
    private Vector3 center;
    private bool skip, confirmed, panelsReady;

    private RectTransform root, band, goBtn;
    private CanvasGroup bandGroup, goGroup;
    private class Choice { public Image frame, inner; public Text label; public bool hover; }
    private Choice[] diffs, jobs;
    private Choice test;
    private Text detailName, detailPros, detailCons;
    private Image detailIcon;

    public static InGameIntro Begin(GameManager manager)
    {
        GameObject go = new GameObject("InGameIntro", typeof(RectTransform));
        InGameIntro s = go.AddComponent<InGameIntro>();
        s.gm = manager;
        return s;
    }

    void Start()
    {
        difficulty = Mathf.Clamp(gm != null ? gm.SelectedDifficulty : 1, 0, 2);
        job = Mathf.Clamp(gm != null ? gm.SelectedJob : 0, 0, JobData.All.Length - 1);
        center = gm != null && gm.spawnPoint != null ? gm.spawnPoint.position : Vector3.zero;
        BuildCanvas();
        StartCoroutine(Run());
    }

    void OnDestroy() { CameraController.OverrideActive = false; }

    void Update()
    {
        if (!panelsReady)
        {
            if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) skip = true;
            return;
        }
        if (confirmed) return;
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) ChangeJob(-1);
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) ChangeJob(1);
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetDifficulty(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetDifficulty(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SetDifficulty(2);
        if (Input.GetKeyDown(KeyCode.T)) ToggleTest();
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) confirmed = true;
    }

    // ================= 흐름 =================
    private IEnumerator Run()
    {
        CameraController.OverrideActive = true;
        Vector3 look = center + Vector3.up * 0.3f;
        Vector3 closePos = center + CloseOffset;
        Quaternion closeRot = Quaternion.LookRotation(look - closePos);
        float fov = 60f;
        if (CameraController.Main != null) { CameraController.Main.GetTopDownPose(center, out _, out _, out fov); }

        // 1) 빙글 돌며 내려옴
        float t0 = Time.unscaledTime;
        float startAng = Random.Range(0f, 360f);
        while (!skip)
        {
            float u = Mathf.Clamp01((Time.unscaledTime - t0) / SwirlTime);
            float e = 1f - Mathf.Pow(1f - u, 3f);                 // 빠르게 시작해서 천천히 멈춤
            float ang = (startAng + SwirlTurns * 360f * e) * Mathf.Deg2Rad;
            float r = Mathf.Lerp(StartRadius, 0f, e);
            Vector3 orbit = center + new Vector3(Mathf.Sin(ang) * r, Mathf.Lerp(StartHeight, CloseOffset.y, e), Mathf.Cos(ang) * r);
            Vector3 pos = Vector3.Lerp(orbit, closePos, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, u)));
            Quaternion rot = Quaternion.LookRotation(look - pos);
            SetCam(pos, rot, Mathf.Lerp(fov + 15f, fov, e));
            if (u >= 1f) break;
            yield return null;
        }
        SetCam(closePos, closeRot, fov);

        // 2) 출동 준비 판이 위에서 내려옴
        yield return StartCoroutine(PanelsIn());
        panelsReady = true;
        float idle0 = Time.unscaledTime;
        while (!confirmed)
        {
            // 고르는 동안 카메라가 아주 천천히 맴돔
            float a = (Time.unscaledTime - idle0) * 3f * Mathf.Deg2Rad;
            Vector3 p = center + Quaternion.Euler(0f, Mathf.Sin(a) * 6f, 0f) * CloseOffset;
            SetCam(p, Quaternion.LookRotation(look - p), fov);
            yield return null;
        }

        // 3) 출동: 판 들어감 → 게임 시작(플레이어 생성) → 줌 아웃 → 착지
        Sfx.Play("UI/UI_Click", 0.6f, 0f);
        yield return StartCoroutine(PanelsOut());
        Vector3 fromPos = CameraController.OverridePos; Quaternion fromRot = CameraController.OverrideRot;
        if (gm != null) gm.BeginRun(difficulty, 0, job);
        Transform pl = gm != null ? gm.playerTransform : null;
        CharacterController cc = pl != null ? pl.GetComponent<CharacterController>() : null;
        PlayerController pc = pl != null ? pl.GetComponent<PlayerController>() : null;
        Vector3 rest = pl != null ? pl.position : center;
        Vector3 ground = new Vector3(rest.x, center.y, rest.z);
        if (pl != null)
        {
            if (cc != null) cc.enabled = false;
            if (pc != null) pc.enabled = false;
            pl.position = rest + Vector3.up * DropHeight;
        }
        Vector3 endPos = ground + new Vector3(0f, 10f, -7f); Quaternion endRot = Quaternion.Euler(55f, 0f, 0f);
        if (CameraController.Main != null) CameraController.Main.GetTopDownPose(ground, out endPos, out endRot, out fov);

        t0 = Time.unscaledTime;
        bool dropped = false;
        float dropStart = ZoomOutTime * 0.55f;
        while (true)
        {
            float t = Time.unscaledTime - t0;
            float u = Mathf.Clamp01(t / ZoomOutTime);
            float e = Mathf.SmoothStep(0f, 1f, u);
            SetCam(Vector3.Lerp(fromPos, endPos, e), Quaternion.Slerp(fromRot, endRot, e), fov);
            if (pl != null && !dropped)
            {
                float d = Mathf.Clamp01((t - dropStart) / DropTime);
                pl.position = rest + Vector3.up * DropHeight * (1f - d * d);   // 점점 빨라지며 떨어짐
                if (d >= 1f) { dropped = true; Land(pl, rest, cc, pc); }
            }
            if (u >= 1f && (dropped || pl == null)) break;
            yield return null;
        }
        CameraController.OverrideActive = false;
        Destroy(gameObject);
    }

    private void Land(Transform pl, Vector3 rest, CharacterController cc, PlayerController pc)
    {
        pl.position = rest;
        Physics.SyncTransforms();
        if (cc != null) cc.enabled = true;
        if (pc != null) pc.enabled = true;
        Sfx.Play(TowerDrop.DeploySound, 0.8f, 0.04f);
        CameraController.Shake(0.18f, 0.25f);
        Vector3 c = new Vector3(rest.x, center.y + 0.1f, rest.z);
        for (int i = 0; i < 14; i++)
        {
            float a = (i + Random.value * 0.5f) / 14f * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            HitEffects.Smoke(c + dir * 0.4f, dir * Random.Range(1.6f, 2.6f) + Vector3.up * 0.4f, Random.Range(0.22f, 0.34f), Random.Range(0.6f, 0.9f), 0.6f, 1f);
        }
    }

    private static void SetCam(Vector3 pos, Quaternion rot, float fov)
    {
        CameraController.OverridePos = pos;
        CameraController.OverrideRot = rot;
        CameraController.OverrideFov = fov;
    }

    // ================= 출동 준비 판 =================
    private void BuildCanvas()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 480;   // HUD(-1) 위 → 위쪽 웨이브 타이머 등을 덮음
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();
        root = (RectTransform)transform;

        // ── 위쪽을 덮는 판 (재능 창의 왼쪽 판을 가로로 눕힌 모양: 아랫변에 강조선 + 그림자 띠) ──
        band = NewRect("Band", root);
        band.anchorMin = new Vector2(0f, 1f); band.anchorMax = new Vector2(1f, 1f);
        band.pivot = new Vector2(0.5f, 1f);
        band.sizeDelta = new Vector2(0f, BandHeight);
        band.gameObject.AddComponent<Image>().color = PanelBg;
        RectTransform edge = NewRect("Edge", band);
        edge.anchorMin = new Vector2(0f, 0f); edge.anchorMax = new Vector2(1f, 0f);
        edge.pivot = new Vector2(0.5f, 1f); edge.sizeDelta = new Vector2(0f, 4f); edge.anchoredPosition = Vector2.zero;
        edge.gameObject.AddComponent<Image>().color = Accent;
        RectTransform shade = NewRect("Shade", band);
        shade.anchorMin = new Vector2(0f, 0f); shade.anchorMax = new Vector2(1f, 0f);
        shade.pivot = new Vector2(0.5f, 1f); shade.sizeDelta = new Vector2(0f, 8f); shade.anchoredPosition = new Vector2(0f, -4f);
        shade.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
        bandGroup = band.gameObject.AddComponent<CanvasGroup>();

        // 가운데 1776 폭 안에 배치 (화면이 넓어도 가운데 정렬)
        RectTransform c = NewRect("Content", band);
        c.anchorMin = c.anchorMax = new Vector2(0.5f, 1f); c.pivot = new Vector2(0.5f, 1f);
        c.sizeDelta = new Vector2(1776f, BandHeight); c.anchoredPosition = Vector2.zero;

        TL(NewText(c, "Eyebrow", "출동 준비", 24, Accent, TextAnchor.MiddleLeft).rectTransform, 0f, 36f, 600f, 36f);

        // ── 왼쪽: 난이도 · 테스트 모드 ──
        const float leftW = 456f;
        TL(NewText(c, "DiffLabel", "난이도  <size=18>1 · 2 · 3</size>", 24, TextSub, TextAnchor.MiddleLeft).rectTransform, 0f, 92f, leftW, 32f);
        diffs = new Choice[3];
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            diffs[i] = MakeChoice(c, "Diff" + i, i * 156f, 132f, 144f, 64f, DifficultyNames[i], 24, () => SetDifficulty(idx));
        }
        TL(NewText(c, "TestLabel", "테스트 모드  <size=18>T</size>", 24, TextSub, TextAnchor.MiddleLeft).rectTransform, 0f, 224f, leftW, 32f);
        test = MakeChoice(c, "Test", 0f, 264f, leftW, 64f, "", 24, ToggleTest);
        Text testNote = NewText(c, "TestNote", "모든 무기: 마우스 휠 · 재능 목록: F3", 18, TextSub, TextAnchor.MiddleLeft);
        TL(testNote.rectTransform, 0f, 336f, leftW, 28f);

        // ── 오른쪽: 직업 8개 한 줄 + 설명 ──
        const float jx = 520f, jw = 1776f - 520f, gap = 12f;
        float cw = (jw - gap * (JobData.All.Length - 1)) / JobData.All.Length, ch = 164f;
        TL(NewText(c, "JobLabel", "직업  <size=18>← →</size>", 24, TextSub, TextAnchor.MiddleLeft).rectTransform, jx, 92f, 400f, 32f);
        jobs = new Choice[JobData.All.Length];
        for (int i = 0; i < JobData.All.Length; i++)
        {
            int idx = i;
            JobData.Job j = JobData.All[i];
            Choice ch1 = MakeChoice(c, "Job_" + j.code, jx + i * (cw + gap), 132f, cw, ch, j.name, 20, () => SetJob(idx));
            RectTransform lr = ch1.label.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(1f, 0f); lr.pivot = new Vector2(0.5f, 0f);
            lr.sizeDelta = new Vector2(-12f, 52f); lr.anchoredPosition = new Vector2(0f, 8f);
            ch1.label.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (j.Icon != null)
            {
                Image ic = NewRect("Icon", ch1.frame.transform).gameObject.AddComponent<Image>();
                RectTransform ir = ic.rectTransform;
                ir.anchorMin = ir.anchorMax = new Vector2(0.5f, 1f); ir.pivot = new Vector2(0.5f, 1f);
                ir.sizeDelta = new Vector2(88f, 88f); ir.anchoredPosition = new Vector2(0f, -16f);
                ic.sprite = j.Icon; ic.preserveAspect = true; ic.raycastTarget = false;
            }
            jobs[i] = ch1;
        }

        // 설명 (고른 직업)
        RectTransform d = NewRect("Detail", c);
        TL(d, jx, 312f, jw, 156f);
        d.gameObject.AddComponent<Image>().color = LineStrong;
        Inset(NewRect("Inner", d), 4f).gameObject.AddComponent<Image>().color = new Color(0.165f, 0.165f, 0.157f, 0.95f);
        RectTransform iconBox = NewRect("IconBox", d);
        TL(iconBox, 20f, 18f, 120f, 120f);
        iconBox.gameObject.AddComponent<Image>().color = Line;
        Inset(NewRect("Inner", iconBox), 4f).gameObject.AddComponent<Image>().color = Hex("#2C2C2C");
        detailIcon = Inset(NewRect("Picture", iconBox), 12f).gameObject.AddComponent<Image>();
        detailIcon.preserveAspect = true; detailIcon.raycastTarget = false;
        detailName = NewText(d, "Name", "", 36, TextMain, TextAnchor.MiddleLeft);
        TL(detailName.rectTransform, 168f, 16f, jw - 200f, 48f);
        detailPros = NewText(d, "Pros", "", 24, Accent, TextAnchor.UpperLeft);
        TL(detailPros.rectTransform, 168f, 70f, jw - 200f, 36f);
        detailCons = NewText(d, "Cons", "", 24, Cons, TextAnchor.UpperLeft);
        TL(detailCons.rectTransform, 168f, 108f, jw - 200f, 36f);

        // ── 아래 가운데: 출동 버튼 (상점·재능 창의 고른 줄과 같은 강조색 상자) ──
        goBtn = NewRect("Go", root);
        goBtn.anchorMin = goBtn.anchorMax = new Vector2(0.5f, 0f); goBtn.pivot = new Vector2(0.5f, 0f);
        goBtn.sizeDelta = new Vector2(360f, 88f); goBtn.anchoredPosition = new Vector2(0f, GoBottom);
        Image gf = goBtn.gameObject.AddComponent<Image>(); gf.color = Dark;
        Image gi = Inset(NewRect("Inner", goBtn), 4f).gameObject.AddComponent<Image>(); gi.color = Accent; gi.raycastTarget = false;
        Text gt = NewText(goBtn, "Text", "출동  <size=20>Enter</size>", 40, Dark, TextAnchor.MiddleCenter);
        Stretch(gt.rectTransform);
        Button gb = goBtn.gameObject.AddComponent<Button>();
        gb.transition = Selectable.Transition.None; gb.targetGraphic = gf;
        gb.onClick.AddListener(() => confirmed = true);
        goBtn.gameObject.AddComponent<UIHoverScale>().hoverScale = 1.06f;
        goGroup = goBtn.gameObject.AddComponent<CanvasGroup>();

        UIFont.FixBold(this);
        Refresh();
        SetShown(0f);
        bandGroup.alpha = goGroup.alpha = 0f;
        bandGroup.blocksRaycasts = goGroup.blocksRaycasts = false;
    }

    // 테두리 + 안쪽 판 상자 (고르면 강조색으로 뒤집힘)
    private Choice MakeChoice(RectTransform parent, string name, float x, float y, float w, float h, string text, int size, System.Action onClick)
    {
        Choice ch = new Choice();
        RectTransform rt = NewRect(name, parent);
        TL(rt, x, y, w, h);
        ch.frame = rt.gameObject.AddComponent<Image>();
        ch.inner = Inset(NewRect("Inner", rt), 4f).gameObject.AddComponent<Image>();
        ch.inner.raycastTarget = false;
        ch.label = NewText(rt, "Text", text, size, TextMain, TextAnchor.MiddleCenter);
        Stretch(ch.label.rectTransform);
        Button b = rt.gameObject.AddComponent<Button>();
        b.transition = Selectable.Transition.None; b.targetGraphic = ch.frame;
        b.onClick.AddListener(() => onClick());
        GameHUDHoverRelay hv = rt.gameObject.AddComponent<GameHUDHoverRelay>();
        hv.onEnter = () => { ch.hover = true; Refresh(); };
        hv.onExit = () => { ch.hover = false; Refresh(); };
        return ch;
    }

    private static void Style(Choice ch, bool on)
    {
        ch.frame.color = on ? Accent : (ch.hover ? LineStrong : Line);
        ch.inner.color = on ? Accent : RowBg;
        ch.label.color = on ? Dark : TextMain;
    }

    // 0 = 숨김 (판은 화면 위, 버튼은 화면 아래), 1 = 제자리
    private void SetShown(float k)
    {
        band.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(BandHeight + 20f, 0f, k));
        goBtn.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(-120f, GoBottom, k));
    }

    // 위에서 덜컹거리며 내려옴 (한 번 걸렸다가 쿵)
    private IEnumerator PanelsIn()
    {
        Sfx.UIOpen();
        bandGroup.alpha = goGroup.alpha = 1f;
        float t0 = Time.unscaledTime; const float dur = 0.6f;
        bool clunk = false;
        while (true)
        {
            float u = Mathf.Clamp01((Time.unscaledTime - t0) / dur);
            float k;
            if (u < 0.45f) { float a = u / 0.45f; k = 0.6f * (1f - (1f - a) * (1f - a)); }
            else if (u < 0.58f) k = 0.6f;
            else { float a = (u - 0.58f) / 0.42f; k = Mathf.LerpUnclamped(0.6f, 1f, 1f + 2.2f * Mathf.Pow(a - 1f, 3) + 1.2f * Mathf.Pow(a - 1f, 2)); }
            if (!clunk && u >= 0.45f) { clunk = true; Sfx.Play(TowerDrop.DeploySound, 0.3f, 0.08f); }
            SetShown(k);
            float shake = (1f - u) * 3f;
            band.anchoredPosition += new Vector2(Random.Range(-shake, shake), 0f);
            if (u >= 1f) break;
            yield return null;
        }
        SetShown(1f);
        Sfx.Play(TowerDrop.DeploySound, 0.4f, 0.06f);
        bandGroup.blocksRaycasts = goGroup.blocksRaycasts = true;
    }

    private IEnumerator PanelsOut()
    {
        bandGroup.blocksRaycasts = goGroup.blocksRaycasts = false;
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 0.3f)
        {
            float u = (Time.unscaledTime - t0) / 0.3f;
            SetShown(1f - u * u);
            bandGroup.alpha = goGroup.alpha = 1f - u;
            yield return null;
        }
        bandGroup.alpha = goGroup.alpha = 0f;
    }

    // ================= 선택 =================
    private void SetDifficulty(int i) { if (confirmed) return; difficulty = i; Refresh(); }
    private void SetJob(int i) { if (confirmed) return; job = i; Refresh(); }
    private void ChangeJob(int d) { SetJob((job + d + JobData.All.Length) % JobData.All.Length); }
    private void ToggleTest() { if (confirmed) return; PlayerWeaponManager.TestModeAllWeapons = !PlayerWeaponManager.TestModeAllWeapons; Refresh(); }

    private void Refresh()
    {
        if (diffs == null) return;
        for (int i = 0; i < diffs.Length; i++) Style(diffs[i], i == difficulty);
        for (int i = 0; i < jobs.Length; i++) Style(jobs[i], i == job);
        bool on = PlayerWeaponManager.TestModeAllWeapons;
        Style(test, on);
        test.label.text = on ? "켜짐" : "꺼짐";

        JobData.Job j = JobData.Get(job);
        if (j == null) return;
        detailName.text = j.name;
        detailIcon.sprite = j.Icon; detailIcon.enabled = j.Icon != null;
        detailPros.text = "+ " + j.pros;
        detailCons.text = string.IsNullOrEmpty(j.cons) ? "" : "− " + j.cons;
    }

    // ================= 도우미 =================
    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void TL(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.sizeDelta = new Vector2(w, h);
        r.anchoredPosition = new Vector2(x, -y);
    }

    private static RectTransform Inset(RectTransform r, float d)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(d, d); r.offsetMax = new Vector2(-d, -d);
        return r;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    private static Text NewText(Transform parent, string name, string text, int size, Color color, TextAnchor anchor)
    {
        Text t = NewRect(name, parent).gameObject.AddComponent<Text>();
        UIFont.Apply(t, FontStyle.Normal);
        t.fontSize = size; t.color = color; t.alignment = anchor; t.text = text;
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    private static void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null) return;
        GameObject es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }
}
