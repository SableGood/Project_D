using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

// 씬의 메뉴 창(Canvas > Center_Panels: 일시정지·게임오버·난이도·캐릭터·재능 선택)을
// 하단 HUD(GameHUD)와 같은 디자인으로 실행 중에 다시 꾸밉니다.
//  - 씬 파일은 수정하지 않습니다. (버튼 연결·GameManager 동작은 그대로)
//  - 씬이 열릴 때마다 자동 적용 (Center_Panels가 없는 씬은 무시)
//  - 배치: 창마다 가운데 카드 + 뒤 화면 어둡게, 버튼은 HUD 칸과 같은 테두리/배경
//  - 캐릭터 선택: 캐릭터 일러스트 + 체력/방어/이동속도 + 고유능력(Mob_Data.csv) 카드
public class MenuSkin : MonoBehaviour
{
    public const string RootName = "Center_Panels";

    // ── 색 (GameHUD와 통일) ──
    private static readonly Color Dim = new Color(0f, 0f, 0f, 0.5f);
    private static readonly Color CardBg = new Color(0f, 0f, 0f, 0.5f);
    private static readonly Color ButtonBg = new Color(0.12f, 0.12f, 0.14f, 0.85f);
    private static readonly Color FrameNormal = new Color(1f, 1f, 1f, 0.18f);
    private static readonly Color Accent = new Color(1f, 0.85f, 0.25f, 1f);
    private static readonly Color SubText = new Color(1f, 1f, 1f, 0.65f);

    private static Font font;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            Transform root = FindDeep(go.transform, RootName);
            if (root == null) continue;
            if (root.GetComponent<MenuSkin>() == null) root.gameObject.AddComponent<MenuSkin>().Apply();
            return;
        }
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            Transform r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    // ==================================================================
    private void Apply()
    {
        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Arial" }, 32);
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 씬 캔버스: 해상도가 달라도 같은 비율로 보이게 (1920x1080 기준) + 글자 선명하게
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas.pixelPerfect = true;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null)
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }
        }

        foreach (Transform panel in transform)
        {
            string n = panel.name;
            if (n.Contains("Character")) SkinPanel(panel, Layout.Cards);
            else if (n.Contains("Talent")) SkinPanel(panel, Layout.Grid);
            else if (n.Contains("Difficulty")) SkinPanel(panel, Layout.Row);
            else SkinPanel(panel, Layout.Column); // 일시정지, 게임오버 등
        }
    }

    private enum Layout { Column, Row, Grid, Cards }

    private void SkinPanel(Transform panel, Layout layout)
    {
        RectTransform prt = panel as RectTransform;
        if (prt == null) return;

        // 창 전체: 뒤 화면을 반투명하게 어둡게
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(4000f, 4000f);
        Image dim = panel.GetComponent<Image>();
        if (dim != null) { dim.sprite = null; dim.color = Dim; }

        // 버튼 / 제목 찾기 (제목 = 버튼 밖에 있는 첫 글자)
        List<Button> buttons = new List<Button>();
        Text title = null;
        foreach (Transform c in panel)
        {
            Button b = c.GetComponent<Button>();
            if (b != null) { buttons.Add(b); continue; }
            Text t = c.GetComponent<Text>();
            if (t != null && title == null) title = t;
        }

        // 카드 크기
        Vector2 btnSize; int cols; float gap = 14f;
        switch (layout)
        {
            case Layout.Row: btnSize = new Vector2(200f, 72f); cols = Mathf.Max(1, buttons.Count); break;
            case Layout.Grid: btnSize = new Vector2(170f, 64f); cols = 4; break;
            case Layout.Cards: btnSize = new Vector2(220f, 300f); cols = Mathf.Max(1, buttons.Count); gap = 20f; break;
            default: btnSize = new Vector2(300f, 56f); cols = 1; break;
        }
        int rows = Mathf.CeilToInt(buttons.Count / (float)cols);
        float gridW = cols * btnSize.x + (cols - 1) * gap;
        float gridH = rows * btnSize.y + (rows - 1) * gap;
        float cardW = Mathf.Max(520f, gridW + 80f);
        float cardH = gridH + 160f;

        // 가운데 카드 (버튼들 뒤)
        RectTransform card = NewRect("Card", panel);
        card.SetAsFirstSibling();
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(Mathf.Round(cardW), Mathf.Round(cardH));
        card.anchoredPosition = Vector2.zero;
        Image cardImg = card.gameObject.AddComponent<Image>();
        cardImg.color = CardBg;
        cardImg.raycastTarget = true; // 카드 뒤 클릭 막기
        AddFrame(card, FrameNormal, 2f);

        // 제목
        if (title != null)
        {
            RectTransform trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.sizeDelta = new Vector2(cardW - 40f, 50f);
            trt.anchoredPosition = new Vector2(0f, Mathf.Round(cardH * 0.5f - 46f));
            StyleText(title, 32, Color.white, true);
        }

        // 버튼 배치
        float startY = cardH * 0.5f - 96f; // 제목 아래
        for (int i = 0; i < buttons.Count; i++)
        {
            int r = i / cols, c = i % cols;
            int inRow = Mathf.Min(cols, buttons.Count - r * cols);
            float rowW = inRow * btnSize.x + (inRow - 1) * gap;
            float x = -rowW * 0.5f + btnSize.x * 0.5f + c * (btnSize.x + gap);
            float y = startY - btnSize.y * 0.5f - r * (btnSize.y + gap);

            RectTransform brt = (RectTransform)buttons[i].transform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = btnSize;
            brt.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
            StyleButton(buttons[i]);

            if (layout == Layout.Cards) BuildCharacterCard(buttons[i], btnSize);
        }
    }

    // ── 버튼: HUD 칸과 같은 테두리 + 어두운 배경, 마우스를 올리면 노란 테두리 ──
    private void StyleButton(Button b)
    {
        Image img = b.GetComponent<Image>();
        if (img != null) { img.sprite = null; img.type = Image.Type.Simple; img.color = ButtonBg; }

        ColorBlock cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.selectedColor = Color.white;
        cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        cb.colorMultiplier = 1f;
        b.colors = cb;

        Image[] frame = AddFrame((RectTransform)b.transform, FrameNormal, 2f);
        GameHUDHoverRelay hover = b.gameObject.AddComponent<GameHUDHoverRelay>();
        hover.onEnter = () => SetFrameColor(frame, Accent);
        hover.onExit = () => SetFrameColor(frame, FrameNormal);

        foreach (Text t in b.GetComponentsInChildren<Text>(true)) StyleText(t, 22, Color.white, true);
    }

    // ── 캐릭터 카드: 일러스트 + 이름 + 능력치 + 고유능력 ──
    private void BuildCharacterCard(Button b, Vector2 size)
    {
        int index; string code;
        string n = b.name;
        if (n.Contains("Chim")) { index = 0; code = "PC001"; }
        else if (n.Contains("Monk")) { index = 1; code = "PC002"; }
        else if (n.Contains("Oran")) { index = 2; code = "PC003"; }
        else return;

        // 이름 글자는 카드 아래쪽으로
        Text label = b.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            RectTransform lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.offsetMin = new Vector2(0f, 74f); lrt.offsetMax = new Vector2(0f, 104f);
            StyleText(label, 24, Color.white, true);
        }

        // 일러스트 (플레이어 프리팹의 그림 → 없으면 Resources/Image/Pic_*)
        Sprite portrait = null;
        GameManager gm = GameManager.Instance != null ? GameManager.Instance : FindFirstObjectByType<GameManager>();
        if (gm != null && gm.playerPrefabs != null && index < gm.playerPrefabs.Length && gm.playerPrefabs[index] != null)
        {
            Billboard board = gm.playerPrefabs[index].GetComponentInChildren<Billboard>(true);
            SpriteRenderer sr = board != null ? board.GetComponent<SpriteRenderer>() : null;
            if (sr != null) portrait = sr.sprite;
        }
        if (portrait == null)
        {
            string[] pics = { "Pic_Chim", "Pic_Monk", "Pic_Oran" };
            Sprite[] all = Resources.LoadAll<Sprite>("Image/" + pics[index]);
            if (all != null && all.Length > 0) portrait = all[0];
        }
        if (portrait != null)
        {
            RectTransform pic = NewRect("Portrait", b.transform);
            pic.anchorMin = new Vector2(0f, 1f); pic.anchorMax = new Vector2(1f, 1f);
            pic.pivot = new Vector2(0.5f, 1f);
            pic.offsetMin = new Vector2(16f, -186f); pic.offsetMax = new Vector2(-16f, -14f);
            Image img = pic.gameObject.AddComponent<Image>();
            img.sprite = portrait;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        // 능력치 + 고유능력
        EntityData data = Resources.Load<EntityData>($"Data/Entities/Player/Ent_{code}");
        string stats = data != null ? $"체력 {data.maxHp:0}  ·  방어 {data.defense:0}  ·  속도 {data.moveSpeed:0.#}" : "";
        string ability = MobDataText(code, 13);
        string trait = MobDataText(code, 14);
        string desc = stats;
        if (!string.IsNullOrEmpty(ability)) desc += $"\n<color=#FFE066>{ability}</color>";
        if (!string.IsNullOrEmpty(trait)) desc += $"\n{trait}";

        RectTransform drt = NewRect("Desc", b.transform);
        drt.anchorMin = new Vector2(0f, 0f); drt.anchorMax = new Vector2(1f, 0f);
        drt.pivot = new Vector2(0.5f, 0f);
        drt.offsetMin = new Vector2(10f, 8f); drt.offsetMax = new Vector2(-10f, 72f);
        Text dt = drt.gameObject.AddComponent<Text>();
        dt.text = desc;
        dt.alignment = TextAnchor.UpperCenter;
        dt.horizontalOverflow = HorizontalWrapMode.Wrap;
        dt.verticalOverflow = VerticalWrapMode.Overflow;
        dt.supportRichText = true;
        dt.raycastTarget = false;
        StyleText(dt, 14, SubText, false);
        dt.horizontalOverflow = HorizontalWrapMode.Wrap; // 긴 능력 설명은 줄바꿈
    }

    // Mob_Data.csv에서 코드 줄의 특정 칸 (13 = 고유능력, 14 = 특징)
    private static Dictionary<string, string[]> mobData;
    private static string MobDataText(string code, int column)
    {
        if (mobData == null)
        {
            mobData = new Dictionary<string, string[]>();
            TextAsset csv = Resources.Load<TextAsset>("Mob_Data");
            if (csv != null)
            {
                foreach (string line in csv.text.Split('\n'))
                {
                    string[] cols = SplitCsv(line.TrimEnd('\r'));
                    if (cols.Length > 0 && !string.IsNullOrEmpty(cols[0])) mobData[cols[0].Trim()] = cols;
                }
            }
        }
        if (!mobData.TryGetValue(code, out string[] row) || column >= row.Length) return "";
        string v = row[column].Trim();
        return v == "-" || v == "??" ? "" : v;
    }

    private static string[] SplitCsv(string line)
    {
        List<string> result = new List<string>();
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else quoted = !quoted; }
            else if (ch == ',' && !quoted) { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }

    // ── 공통 ──
    private static void StyleText(Text t, int size, Color color, bool bold)
    {
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.alignment = t.alignment == TextAnchor.UpperCenter ? TextAnchor.UpperCenter : TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        if (t.GetComponent<Shadow>() == null)
        {
            Shadow s = t.gameObject.AddComponent<Shadow>();
            s.effectColor = new Color(0f, 0f, 0f, 0.9f);
            s.effectDistance = new Vector2(1.5f, -1.5f);
        }
    }

    // 테두리: 안쪽 가장자리 4줄 (배경이 반투명이어도 테두리만 깔끔하게)
    private static Image[] AddFrame(RectTransform rt, Color color, float width)
    {
        RectTransform root = NewRect("Frame", rt);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        Image[] edges = new Image[4];
        Vector2[,] a = { { new Vector2(0, 1), new Vector2(1, 1) }, { new Vector2(0, 0), new Vector2(1, 0) }, { new Vector2(0, 0), new Vector2(0, 1) }, { new Vector2(1, 0), new Vector2(1, 1) } };
        for (int i = 0; i < 4; i++)
        {
            RectTransform e = NewRect("Edge", root);
            e.anchorMin = a[i, 0]; e.anchorMax = a[i, 1];
            e.pivot = new Vector2(i == 3 ? 1f : 0f, i == 0 ? 1f : 0f);
            e.anchoredPosition = Vector2.zero;
            e.sizeDelta = i < 2 ? new Vector2(0f, width) : new Vector2(width, 0f);
            edges[i] = e.gameObject.AddComponent<Image>();
            edges[i].color = color;
            edges[i].raycastTarget = false;
        }
        return edges;
    }

    private static void SetFrameColor(Image[] edges, Color color)
    {
        foreach (Image e in edges) if (e != null) e.color = color;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
