using UnityEngine;
using UnityEngine.UI;

// 게임 시작 화면 (출격 준비): 난이도 · 캐릭터 · 직업을 한 화면에서 고르고 시작
//  - 예전 난이도 → 캐릭터 → 직업(씬의 Center_Panels) 세 단계를 대신함 (GameManager.useCombinedSetup)
//  - 키보드: Enter = 시작
//  - 색: 배경 #3A3A3A 계열, 강조 #C6E7A7, 글꼴 Galmuri
public class SetupScreen : MonoBehaviour
{
    private static readonly Color Back = Hex("#2E2E2E", 0.97f);
    private static readonly Color Panel = Hex("#3A3A3A");
    private static readonly Color Line = Hex("#555555");
    private static readonly Color Accent = Hex("#C6E7A7");
    private static readonly Color Dark = Hex("#1F241B");
    private static readonly Color TextMain = Hex("#EDEDE6");
    private static readonly Color Muted = Hex("#A8A8A0");
    private static readonly Color ConsColor = Hex("#F08A7A");

    private static readonly string[] DifficultyNames = { "쉬움", "보통", "어려움" };
    private static readonly string[] CharacterNames = { "침팬지", "원숭이", "오랑우탄" };
    private static readonly string[] CharacterCodes = { "PC001", "PC002", "PC003" };
    // 아직 쓸 수 없는 캐릭터 (회색 + 빗금, 고를 수 없음)
    private static readonly bool[] CharacterLocked = { false, true, true };

    private GameManager gm;
    private int difficulty = 1, character = 0, job = 0;

    private Image[] diffFrames, charFrames, jobFrames;
    private Text[] diffTexts, charNames, jobNames;
    private Text detailName, detailPros, detailCons;
    private Image testFrame;
    private Text testText;
    private Image detailIcon;

    public static SetupScreen Open(GameManager manager)
    {
        GameObject go = new GameObject("SetupScreen", typeof(RectTransform));
        SetupScreen s = go.AddComponent<SetupScreen>();
        s.gm = manager;
        s.Build();
        return s;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Confirm();
    }

    private void Confirm()
    {
        if (gm != null) gm.BeginRun(difficulty, character, job);
        Destroy(gameObject);
    }

    // ==================================================================
    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 480;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)transform;

        Stretch(Img(root, "Back", Back));

        // 제목
        Txt(root, "Title", "출격 준비", 48, true, TextMain, TextAnchor.UpperLeft, 96f, 64f, 900f, 60f);
        Txt(root, "Sub", "난이도 · 캐릭터 · 직업을 고르고 시작하세요", 24, false, Muted, TextAnchor.UpperLeft, 96f, 130f, 900f, 30f);

        // ── 난이도 ──
        Txt(root, "DiffLabel", "난이도", 24, false, Accent, TextAnchor.UpperLeft, 96f, 210f, 300f, 30f);
        diffFrames = new Image[3]; diffTexts = new Text[3];
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            RectTransform b = Box(root, "Diff_" + i, 96f + i * 196f, 252f, 180f, 60f, out diffFrames[i]);
            diffTexts[i] = Txt(b, "Text", DifficultyNames[i], 24, false, TextMain, TextAnchor.MiddleCenter, 0f, 0f, 180f, 60f);
            AddClick(b, () => { difficulty = idx; Refresh(); });
        }

        // ── 캐릭터 ──
        Txt(root, "CharLabel", "캐릭터", 24, false, Accent, TextAnchor.UpperLeft, 96f, 360f, 300f, 30f);
        charFrames = new Image[3]; charNames = new Text[3];
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            float x = 96f + i * 252f;
            RectTransform b = Box(root, "Char_" + i, x, 402f, 236f, 420f, out charFrames[i]);
            Sprite portrait = Portrait(i);
            if (portrait != null)
            {
                Image p = Img(b, "Portrait", Color.white);
                TL(p.rectTransform, 18f, 18f, 200f, 220f);
                p.sprite = portrait; p.preserveAspect = true;
            }
            charNames[i] = Txt(b, "Name", CharacterNames[i], 36, false, TextMain, TextAnchor.MiddleCenter, 0f, 248f, 236f, 44f);
            Txt(b, "Desc", CharacterDesc(i), 18, false, Muted, TextAnchor.UpperCenter, 14f, 300f, 208f, 110f).horizontalOverflow = HorizontalWrapMode.Wrap;
            if (CharacterLocked[i]) { LockCard(b, 236f, 420f); continue; }
            AddClick(b, () => { character = idx; Refresh(); });
        }

        // ── 직업 ──
        float jx = 980f, jy = 210f;
        Txt(root, "JobLabel", "직업", 24, false, Accent, TextAnchor.UpperLeft, jx, jy, 300f, 30f);
        jobFrames = new Image[JobData.All.Length]; jobNames = new Text[JobData.All.Length];
        const float cw = 196f, ch = 176f, gap = 14f;
        for (int i = 0; i < JobData.All.Length; i++)
        {
            int idx = i;
            JobData.Job j = JobData.All[i];
            RectTransform b = Box(root, "Job_" + j.code, jx + (i % 4) * (cw + gap), jy + 42f + (i / 4) * (ch + gap), cw, ch, out jobFrames[i]);
            if (j.Icon != null)
            {
                Image ic = Img(b, "Icon", Color.white);
                TL(ic.rectTransform, (cw - 96f) * 0.5f, 18f, 96f, 96f);
                ic.sprite = j.Icon; ic.preserveAspect = true;
            }
            jobNames[i] = Txt(b, "Name", j.name, 24, false, TextMain, TextAnchor.MiddleCenter, 0f, 124f, cw, 36f);
            AddClick(b, () => { job = idx; Refresh(); });
        }

        // 직업 설명
        float dy = jy + 42f + 2f * (ch + gap) + 6f;
        RectTransform detail = Box(root, "JobDetail", jx, dy, 4f * cw + 3f * gap, 236f, out _);
        detailIcon = Img(detail, "Icon", Color.white);
        TL(detailIcon.rectTransform, 24f, 24f, 120f, 120f);
        detailIcon.preserveAspect = true;
        detailName = Txt(detail, "Name", "", 36, false, TextMain, TextAnchor.UpperLeft, 168f, 24f, 640f, 44f);
        detailPros = Txt(detail, "Pros", "", 24, false, Accent, TextAnchor.UpperLeft, 168f, 80f, 640f, 64f);
        detailPros.horizontalOverflow = HorizontalWrapMode.Wrap;
        detailCons = Txt(detail, "Cons", "", 24, false, ConsColor, TextAnchor.UpperLeft, 168f, 146f, 640f, 64f);
        detailCons.horizontalOverflow = HorizontalWrapMode.Wrap;
        Txt(detail, "Note", "※ 공무원은 아직 효과 없음 (관련 재능 기획 예정)", 12, false, Muted, TextAnchor.LowerRight, 0f, 200f, 4f * cw + 3f * gap - 20f, 24f);

        // ── 테스트 모드 (모든 무기를 휠로 바꿔 가며 사용) ──
        RectTransform test = Box(root, "TestMode", 96f, 1080f - 72f - 64f, 520f, 64f, out testFrame);
        testText = Txt(test, "Text", "", 24, false, TextMain, TextAnchor.MiddleCenter, 0f, 0f, 520f, 64f);
        testText.supportRichText = true;
        AddClick(test, () => { PlayerWeaponManager.TestModeAllWeapons = !PlayerWeaponManager.TestModeAllWeapons; Refresh(); });

        // ── 시작 ──
        RectTransform start = Box(root, "Start", 1920f - 96f - 340f, 1080f - 72f - 84f, 340f, 84f, out Image startFrame);
        start.GetChild(0).GetComponent<Image>().color = Accent;   // 안쪽 배경을 강조색으로
        startFrame.color = Accent;
        Txt(start, "Text", "시작  <size=18>Enter</size>", 36, true, Dark, TextAnchor.MiddleCenter, 0f, 0f, 340f, 84f).supportRichText = true;
        AddClick(start, Confirm);

        UIFont.FixBold(this);
        Refresh();
    }

    private void Refresh()
    {
        for (int i = 0; i < diffFrames.Length; i++) SetSelected(diffFrames[i], diffTexts[i], i == difficulty);
        for (int i = 0; i < charFrames.Length; i++)
        {
            if (CharacterLocked[i]) { charFrames[i].color = Line; charNames[i].color = Muted; continue; }
            SetSelected(charFrames[i], charNames[i], i == character);
        }
        for (int i = 0; i < jobFrames.Length; i++) SetSelected(jobFrames[i], jobNames[i], i == job);

        bool test = PlayerWeaponManager.TestModeAllWeapons;
        testFrame.color = test ? Accent : Line;
        testText.color = test ? Accent : Muted;
        testText.text = "테스트 모드 " + (test ? "켜짐" : "꺼짐") + "  <size=18>(모든 무기: 마우스 휠 · 재능 목록: F3)</size>";

        JobData.Job j = JobData.Get(job);
        if (j == null) return;
        detailName.text = j.name;
        detailIcon.sprite = j.Icon;
        detailIcon.enabled = j.Icon != null;
        detailPros.text = "+ " + j.pros;
        detailCons.text = string.IsNullOrEmpty(j.cons) ? "" : "− " + j.cons;
    }

    private static void SetSelected(Image frame, Text label, bool on)
    {
        frame.color = on ? Accent : Line;
        if (label != null) label.color = on ? Accent : TextMain;
    }

    // 잠긴 카드: 어둡게 덮고 빗금 + '잠김'
    private static Texture2D hatchTex;
    private static void LockCard(RectTransform card, float w, float h)
    {
        foreach (Image img in card.GetComponentsInChildren<Image>(true))
            if (img.gameObject.name == "Portrait") img.color = new Color(0.45f, 0.45f, 0.45f, 0.6f);   // 그림은 회색으로
        RectTransform cover = NewRect("Locked", card);
        cover.anchorMin = Vector2.zero; cover.anchorMax = Vector2.one;
        cover.offsetMin = new Vector2(3f, 3f); cover.offsetMax = new Vector2(-3f, -3f);
        Image dim = cover.gameObject.AddComponent<Image>();
        dim.color = new Color(0.12f, 0.12f, 0.12f, 0.55f);
        dim.raycastTarget = true;   // 클릭 막기

        if (hatchTex == null)
        {
            const int N = 16;
            hatchTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "LockHatch" };
            Color on = new Color(1f, 1f, 1f, 0.10f), off = new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) hatchTex.SetPixel(x, y, ((x + y) % N) < 3 ? on : off);
            hatchTex.Apply();
        }
        RectTransform hr = NewRect("Hatch", cover);
        hr.anchorMin = Vector2.zero; hr.anchorMax = Vector2.one;
        hr.offsetMin = hr.offsetMax = Vector2.zero;
        RawImage hatch = hr.gameObject.AddComponent<RawImage>();
        hatch.texture = hatchTex;
        hatch.uvRect = new Rect(0f, 0f, w / 16f, h / 16f);
        hatch.raycastTarget = false;

        Text t = Txt(cover, "Text", "잠김", 24, false, TextMain, TextAnchor.MiddleCenter, 0f, 0f, w - 6f, h - 6f);
        t.color = new Color(1f, 1f, 1f, 0.8f);
    }

    // ── 캐릭터 정보 ──
    private Sprite Portrait(int i)
    {
        if (gm != null && gm.playerPrefabs != null && i < gm.playerPrefabs.Length && gm.playerPrefabs[i] != null)
        {
            Sprite s = PlayerBodyAnimator.GetPortrait(gm.playerPrefabs[i]);
            if (s != null) return s;
        }
        string[] pics = { "Pic_Chim", "Pic_Monk", "Pic_Oran" };
        Sprite[] all = Resources.LoadAll<Sprite>("Image/" + pics[i]);
        return all != null && all.Length > 0 ? all[0] : null;
    }

    private static string CharacterDesc(int i)
    {
        EntityData data = Resources.Load<EntityData>($"Data/Entities/Player/Ent_{CharacterCodes[i]}");
        string s = data != null ? $"체력 {data.maxHp:0} · 방어 {data.defense:0} · 속도 {data.moveSpeed:0.#}" : "";
        string ability = MenuSkin.MobDataText(CharacterCodes[i], 13);
        if (!string.IsNullOrEmpty(ability)) s += "\n" + ability;
        return s;
    }

    // ==================================================================
    // 도우미 (좌표는 1920x1080 기준, 왼쪽 위에서부터)
    private static Color Hex(string h, float a = 1f)
    {
        ColorUtility.TryParseHtmlString(h, out Color c);
        c.a = a;
        return c;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void TL(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void Stretch(Image img)
    {
        RectTransform rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static Image Img(Transform parent, string name, Color color)
    {
        RectTransform rt = NewRect(name, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static Text Txt(Transform parent, string name, string text, int size, bool bold, Color color, TextAnchor anchor,
        float x, float y, float w, float h)
    {
        RectTransform rt = NewRect(name, parent);
        TL(rt, x, y, w, h);
        Text t = rt.gameObject.AddComponent<Text>();
        t.text = text;
        t.fontSize = size;
        UIFont.Apply(t, bold ? FontStyle.Bold : FontStyle.Normal);
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    // 테두리(frame) + 안쪽 판으로 된 상자. frame = 선택 표시용 테두리 색
    private static RectTransform Box(Transform parent, string name, float x, float y, float w, float h, out Image frame)
    {
        RectTransform rt = NewRect(name, parent);
        TL(rt, x, y, w, h);
        frame = rt.gameObject.AddComponent<Image>();
        frame.color = Line;
        RectTransform inner = NewRect("Inner", rt);
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(3f, 3f); inner.offsetMax = new Vector2(-3f, -3f);
        Image bg = inner.gameObject.AddComponent<Image>();
        bg.color = Panel;
        bg.raycastTarget = false;
        inner.SetAsFirstSibling();
        return rt;
    }

    private static void AddClick(RectTransform rt, UnityEngine.Events.UnityAction action)
    {
        Image target = rt.GetComponent<Image>();
        if (target != null) target.raycastTarget = true;
        Button b = rt.gameObject.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.targetGraphic = target;
        b.onClick.AddListener(action);
    }
}
