using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// v156 조작키 안내: 키보드 모양 + 마우스 + 분류별 설명 (출동 준비 화면 아래쪽)
//  쓰는 키만 분류 색으로 칠함. 테스트 키(F2·F3·F4·F6·F9)는 테스트 모드를 켰을 때만 칠함
//  키를 바꾸면 여기 Bindings · Legend 도 같이 고칠 것
public class KeyGuide : MonoBehaviour
{
    private static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out Color c); return c; }
    private static readonly Color PanelBg = Hex("#3A3A3A"), RowBg = Hex("#333331"), Line = Hex("#4C4C4A"), Dark = Hex("#232323"), TextSub = Hex("#A8A8A0"), Accent = Hex("#C6E7A7");

    public enum Cat { Move, Combat, Skill, Build, System, Test }
    private static readonly Color[] CatColor = { Hex("#C6E7A7"), Hex("#F2B66D"), Hex("#BFA8F2"), Hex("#8EC5E8"), Hex("#E4E4DC"), Hex("#F08A7A") };
    private static readonly string[] CatName = { "이동", "전투", "스킬", "건설", "시스템", "테스트" };

    // 키 이름 → 분류
    private static readonly Dictionary<string, Cat> Bindings = new Dictionary<string, Cat>
    {
        { "W", Cat.Move }, { "A", Cat.Move }, { "S", Cat.Move }, { "D", Cat.Move }, { "Space", Cat.Move },
        { "R", Cat.Combat }, { "X", Cat.Combat }, { "LMB", Cat.Combat },
        { "LShift", Cat.Skill }, { "Q", Cat.Skill }, { "E", Cat.Skill }, { "F", Cat.Skill },
        { "1", Cat.Build }, { "2", Cat.Build }, { "3", Cat.Build }, { "4", Cat.Build }, { "5", Cat.Build }, { "RMB", Cat.Build },
        { "Esc", Cat.System }, { "Tab", Cat.System }, { "F1", Cat.System }, { "Wheel", Cat.System },
        { "F2", Cat.Test }, { "F3", Cat.Test }, { "F4", Cat.Test }, { "F6", Cat.Test }, { "F9", Cat.Test },
    };

    private static readonly string[] Legend =
    {
        "W A S D 이동 · Space 점프",
        "좌클릭 사격 · R 재장전 · X 무기 교체 (1인칭은 휠도)",
        "Shift · Q · E · F 직업 스킬",
        "1~5 포탑 고르기 → 좌클릭 설치 · 우클릭 포탑·벽 선택 → Q 판매·수리 / E 업그레이드·강화",
        "Tab 탑뷰 ↔ 1인칭 · 휠 카메라 줌 (탑뷰) · F1 웨이브 바로 시작 · Esc 일시정지·옵션",
        "F2 몹 테스트 · F3 재능 목록 · F4 상점 · F6 무기 목록 · F9 3D 총 비교",
    };

    // 키보드 줄: 이름 (빈 이름 = 빈칸), 폭(칸)
    private static readonly (string, float)[][] Rows =
    {
        new[] { ("Esc", 1f), ("", 1f), ("F1", 1f), ("F2", 1f), ("F3", 1f), ("F4", 1f), ("", 0.5f), ("F5", 1f), ("F6", 1f), ("F7", 1f), ("F8", 1f), ("", 0.5f), ("F9", 1f), ("F10", 1f), ("F11", 1f), ("F12", 1f) },
        new[] { ("`", 1f), ("1", 1f), ("2", 1f), ("3", 1f), ("4", 1f), ("5", 1f), ("6", 1f), ("7", 1f), ("8", 1f), ("9", 1f), ("0", 1f), ("-", 1f), ("=", 1f), ("Back", 2f) },
        new[] { ("Tab", 1.5f), ("Q", 1f), ("W", 1f), ("E", 1f), ("R", 1f), ("T", 1f), ("Y", 1f), ("U", 1f), ("I", 1f), ("O", 1f), ("P", 1f), ("[", 1f), ("]", 1f), ("\\", 1.5f) },
        new[] { ("Caps", 1.75f), ("A", 1f), ("S", 1f), ("D", 1f), ("F", 1f), ("G", 1f), ("H", 1f), ("J", 1f), ("K", 1f), ("L", 1f), (";", 1f), ("'", 1f), ("Enter", 2.25f) },
        new[] { ("LShift", 2.25f), ("Z", 1f), ("X", 1f), ("C", 1f), ("V", 1f), ("B", 1f), ("N", 1f), ("M", 1f), (",", 1f), (".", 1f), ("/", 1f), ("RShift", 2.75f) },
        new[] { ("Ctrl", 1.25f), ("Win", 1.25f), ("Alt", 1.25f), ("Space", 6.25f), ("Alt ", 1.25f), ("Win ", 1.25f), ("Menu", 1.25f), ("Ctrl ", 1.25f) },
    };

    private class Cap { public Image inner; public Text label; public Cat cat; public bool used; }
    private readonly List<Cap> caps = new List<Cap>();
    private Text legend;
    private bool test;

    public static KeyGuide Build(RectTransform parent, Vector2 size, bool stacked = false)
    {
        RectTransform rt = NewRect("KeyGuide", parent);
        rt.sizeDelta = size;
        rt.gameObject.AddComponent<Image>().color = Line;
        Fill(NewRect("Inner", rt), 3f).gameObject.AddComponent<Image>().color = new Color(PanelBg.r, PanelBg.g, PanelBg.b, 0.96f);
        KeyGuide g = rt.gameObject.AddComponent<KeyGuide>();
        g.stacked = stacked;
        g.Make(rt, size);
        return g;
    }

    private bool stacked;   // true = 설명을 키보드 아래에 (일시정지 창처럼 폭이 좁을 때)
    public static Vector2 StackedSize = new Vector2(830f, 590f);

    // v157 일시정지 창: 카드를 넓혀 왼쪽 = 버튼, 오른쪽 남는 자리 = 조작키 안내 (MenuSkin이 꾸민 뒤에 부름)
    public static void AttachToPause(Transform pausePanel)
    {
        RectTransform card = pausePanel.Find("Card") as RectTransform;
        if (card == null) return;
        float oldW = card.sizeDelta.x, oldH = card.sizeDelta.y;
        const float pad = 40f, colW = 300f, gap = 40f;
        float w = pad + colW + gap + StackedSize.x + 30f;
        float h = Mathf.Max(oldH, StackedSize.y + 70f);
        card.sizeDelta = new Vector2(w, h);
        float colX = -w * 0.5f + pad + colW * 0.5f;
        float dy = (h - oldH) * 0.5f;   // 위쪽 기준 그대로 (제목·버튼을 카드 위쪽에 맞춤)
        foreach (Transform c in pausePanel)
        {
            if (c == card || c.name == "OptionsPanel") continue;
            RectTransform r = c as RectTransform;
            if (r == null) continue;
            if (c.GetComponent<Button>() != null || c.GetComponent<Text>() != null)
            {
                Vector2 p = r.anchoredPosition;
                r.anchoredPosition = new Vector2(colX, p.y + dy);
                if (c.GetComponent<Text>() != null) r.sizeDelta = new Vector2(colW + 40f, r.sizeDelta.y);
            }
        }
        // 버튼 열 아래 남는 칸: 세로 구분선
        RectTransform line = NewRect("GuideDivider", card);
        line.anchorMin = line.anchorMax = line.pivot = new Vector2(0.5f, 0.5f);
        line.sizeDelta = new Vector2(2f, h - 60f);
        line.anchoredPosition = new Vector2(-w * 0.5f + pad + colW + gap * 0.5f, 0f);
        line.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

        KeyGuide g = Build((RectTransform)pausePanel, StackedSize, true);
        RectTransform grt = (RectTransform)g.transform;
        grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
        grt.anchoredPosition = new Vector2(w * 0.5f - 30f - StackedSize.x * 0.5f, 0f);
        int optIdx = -1;
        foreach (Transform c in pausePanel) if (c.name == "OptionsPanel") optIdx = c.GetSiblingIndex();
        if (optIdx >= 0) grt.SetSiblingIndex(optIdx);   // 옵션 창은 계속 맨 위
        foreach (Graphic gr in g.GetComponentsInChildren<Graphic>(true)) gr.raycastTarget = false;
    }

    void OnEnable()
    {
        if (caps.Count > 0) SetTest(PlayerWeaponManager.TestModeAllWeapons);   // 일시정지할 때마다 테스트 모드 반영
    }

    private void Make(RectTransform rt, Vector2 size)
    {
        const float pad = 22f, u = 44f, gap = 4f, keyH = 40f, fnH = 32f;
        Text title = NewText(rt, "Title", "조작키", 22, Accent, TextAnchor.MiddleLeft);
        TL(title.rectTransform, pad, 12f, 300f, 30f);

        // ── 키보드 ──
        float y0 = 52f;
        for (int r = 0; r < Rows.Length; r++)
        {
            float x = pad, h = r == 0 ? fnH : keyH;
            float y = r == 0 ? y0 : y0 + fnH + 10f + (r - 1) * (keyH + gap);
            foreach (var (name, w) in Rows[r])
            {
                float kw = w * u - gap;
                if (!string.IsNullOrEmpty(name)) MakeCap(rt, name.Trim(), x, y, kw, h);
                x += w * u;
            }
        }
        float kbW = 15f * u, kbBottom = y0 + fnH + 10f + 5f * (keyH + gap);

        // ── 마우스 ──
        float mx = pad + kbW + 26f, my = y0 + 40f, mw = 96f, mh = 150f;
        RectTransform mouse = NewRect("Mouse", rt);
        TL(mouse, mx, my, mw, mh);
        mouse.gameObject.AddComponent<Image>().color = Line;
        Fill(NewRect("Body", mouse), 3f).gameObject.AddComponent<Image>().color = RowBg;
        MakeCap(mouse, "LMB", 6f, 6f, mw / 2f - 9f, 62f, "좌");
        MakeCap(mouse, "RMB", mw / 2f + 3f, 6f, mw / 2f - 9f, 62f, "우");
        MakeCap(mouse, "Wheel", mw / 2f - 8f, 16f, 16f, 34f, "");
        Text ml = NewText(rt, "MouseLabel", "마우스", 16, TextSub, TextAnchor.MiddleCenter);
        TL(ml.rectTransform, mx - 10f, my + mh + 6f, mw + 20f, 24f);

        // ── 설명 ── (기본: 마우스 오른쪽 / stacked: 키보드 아래)
        legend = NewText(rt, "Legend", "", 18, TextSub, TextAnchor.UpperLeft);
        legend.lineSpacing = 1.3f;
        if (stacked) TL(legend.rectTransform, pad, kbBottom + 14f, size.x - pad * 2f, size.y - kbBottom - 24f);
        else
        {
            float lx = mx + mw + 30f;
            TL(legend.rectTransform, lx, y0 - 4f, size.x - lx - pad, kbBottom - y0 + 10f);
        }
        Refresh();
    }

    private void MakeCap(RectTransform parent, string key, float x, float y, float w, float h, string text = null)
    {
        RectTransform k = NewRect("Key_" + key, parent);
        TL(k, x, y, w, h);
        k.gameObject.AddComponent<Image>().color = Line;
        Cap c = new Cap();
        c.inner = Fill(NewRect("Inner", k), 2f).gameObject.AddComponent<Image>();
        string shown = text ?? (key == "LShift" || key == "RShift" ? "Shift" : key);
        int fs = shown.Length >= 4 ? 13 : shown.Length >= 3 ? 14 : 17;
        c.label = NewText(k, "Label", shown, fs, TextSub, TextAnchor.MiddleCenter);
        Fill(c.label.rectTransform, 0f);
        c.used = Bindings.TryGetValue(key, out c.cat);
        caps.Add(c);
    }

    public void SetTest(bool on) { test = on; Refresh(); }

    private void Refresh()
    {
        foreach (Cap c in caps)
        {
            bool lit = c.used && (c.cat != Cat.Test || test);
            c.inner.color = lit ? CatColor[(int)c.cat] : RowBg;
            c.label.color = lit ? Dark : new Color(TextSub.r, TextSub.g, TextSub.b, 0.45f);
        }
        if (legend == null) return;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Legend.Length; i++)
        {
            bool off = (Cat)i == Cat.Test && !test;
            string col = ColorUtility.ToHtmlStringRGB(CatColor[i]);
            string body = off ? "<color=#6A6A66>테스트 모드를 켜면 사용</color>" : $"<color=#EDEDE6>{Legend[i]}</color>";
            sb.Append($"<color=#{col}>■ {CatName[i]}</color>  {body}");
            if (i < Legend.Length - 1) sb.Append('\n');
        }
        legend.text = sb.ToString();
    }

    // ===== 도우미 =====
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

    private static RectTransform Fill(RectTransform r, float d)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(d, d); r.offsetMax = new Vector2(-d, -d);
        return r;
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
}
