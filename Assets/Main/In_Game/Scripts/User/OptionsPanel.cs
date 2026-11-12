using UnityEngine;
using UnityEngine.UI;

// v154 음량 설정 (PlayerPrefs 저장): BGM = MusicPlayer.Volume, 전투 효과음, UI 효과음
public static class GameVolume
{
    private static float sfx = -1f, ui = -1f;
    public static float Sfx
    {
        get { if (sfx < 0f) sfx = PlayerPrefs.GetFloat("SfxVolume", 1f); return sfx; }
        set { sfx = Mathf.Clamp01(value); PlayerPrefs.SetFloat("SfxVolume", sfx); }
    }
    public static float Ui
    {
        get { if (ui < 0f) ui = PlayerPrefs.GetFloat("UiVolume", 1f); return ui; }
        set { ui = Mathf.Clamp01(value); PlayerPrefs.SetFloat("UiVolume", ui); }
    }
    public static float Bgm
    {
        get => MusicPlayer.Volume;
        set => MusicPlayer.Volume = value;
    }
    // v157 1인칭 마우스 감도 배율 (PlayerController.mouseSensitivity × 이 값)
    private static float sens = -1f;
    public static float MouseSens
    {
        get { if (sens < 0f) sens = PlayerPrefs.GetFloat("MouseSens", 1f); return sens; }
        set { sens = Mathf.Clamp(value, 0.2f, 3f); PlayerPrefs.SetFloat("MouseSens", sens); }
    }
}

// v154 일시정지 창의 '옵션' 버튼 + 음량 슬라이더 창 (MenuSkin이 일시정지 창을 꾸밀 때 만듦)
public class OptionsPanel : MonoBehaviour
{
    private static readonly Color CardBg = new Color(0.05f, 0.05f, 0.06f, 0.92f);
    private static readonly Color Frame = new Color(1f, 1f, 1f, 0.18f);
    private static readonly Color Track = new Color(1f, 1f, 1f, 0.12f);
    private static readonly Color Fill = new Color(1f, 0.85f, 0.25f, 1f);
    private static OptionsPanel current;

    // 일시정지 창의 '옵션' 버튼에 이 창을 연결. v157: 씬에 이미 있는 '옵션'(Pause_Option, 기능 없던 것)을 씀 → 버튼 하나만
    //  씬에 없으면 새로 만들어 마지막 버튼(나가기) 앞에 넣음. 꾸미기 전에 불러야 같은 모양이 됨
    public static void AddButton(Transform pausePanel)
    {
        Button template = null; int last = -1;
        foreach (Transform c in pausePanel)
        {
            Button b = c.GetComponent<Button>();
            if (b == null) continue;
            if (template == null) template = b;
            last = c.GetSiblingIndex();
            Text tx = b.GetComponentInChildren<Text>(true);
            if (c.name == "Pause_Option" || (tx != null && tx.text.Trim() == "옵션"))
            {
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(() => { if (current != null) current.Open(); });
                return;
            }
        }
        if (template == null) return;
        Button nb = Instantiate(template, pausePanel);
        nb.name = "OptionsButton";
        nb.onClick = new Button.ButtonClickedEvent();   // 복사된 씬 연결(계속하기 등) 지우기
        nb.onClick.AddListener(() => { if (current != null) current.Open(); });
        foreach (Text t in nb.GetComponentsInChildren<Text>(true)) t.text = "옵션";
        nb.transform.SetSiblingIndex(Mathf.Max(0, last));
    }

    public static void Build(Transform pausePanel)
    {
        RectTransform root = MenuSkin.NewRect("OptionsPanel", pausePanel);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
        Image block = root.gameObject.AddComponent<Image>();
        block.color = new Color(0f, 0f, 0f, 0.35f);   // 뒤 버튼 클릭 막기
        current = root.gameObject.AddComponent<OptionsPanel>();

        RectTransform card = MenuSkin.NewRect("Card", root);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(640f, 600f);
        card.gameObject.AddComponent<Image>().color = CardBg;
        MenuSkin.AddFrame(card, Frame, 2f);

        Text title = NewText(card, "옵션", 32, new Vector2(0f, 248f), new Vector2(560f, 50f), TextAnchor.MiddleCenter);
        title.color = Color.white;

        AddSlider(card, "배경 음악", 168f, GameVolume.Bgm, v => GameVolume.Bgm = v);
        AddSlider(card, "전투 효과음", 108f, GameVolume.Sfx, v => GameVolume.Sfx = v);
        AddSlider(card, "UI 효과음", 48f, GameVolume.Ui, v => { GameVolume.Ui = v; });
        // v157 마우스 감도 (1인칭): 0.2 ~ 3배
        AddSlider(card, "마우스 감도", -12f, Mathf.InverseLerp(0.2f, 3f, GameVolume.MouseSens), v => GameVolume.MouseSens = Mathf.Lerp(0.2f, 3f, v),
                  v => Mathf.Lerp(0.2f, 3f, v).ToString("0.0") + "배");

        // v157 화면: 전체 화면 켜기/끄기 · 해상도 (누를 때마다 다음 해상도)
        NewText(card, "화면", 22, new Vector2(-170f, -82f), new Vector2(200f, 40f), TextAnchor.MiddleLeft);
        Button full = MakeButton(card, "Fullscreen", new Vector2(-10f, -82f), new Vector2(170f, 44f), out Text fullText);
        Button res = MakeButton(card, "Resolution", new Vector2(180f, -82f), new Vector2(190f, 44f), out Text resText);
        fullText.text = Screen.fullScreen ? "전체 화면" : "창 모드";
        resText.text = $"{Screen.width} × {Screen.height}";
        full.onClick.AddListener(() =>
        {
            bool on = !Screen.fullScreen;
            Screen.SetResolution(Screen.width, Screen.height, on ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            fullText.text = on ? "전체 화면" : "창 모드";
        });
        res.onClick.AddListener(() =>
        {
            Vector2Int r = NextResolution(Screen.width, Screen.height);
            Screen.SetResolution(r.x, r.y, Screen.fullScreenMode);
            resText.text = $"{r.x} × {r.y}";
        });

        Button close = MakeButton(card, "CloseButton", new Vector2(0f, -230f), new Vector2(300f, 56f), out Text closeText);
        closeText.text = "닫기";
        close.onClick.AddListener(() => current.gameObject.SetActive(false));

        root.gameObject.SetActive(false);
    }

    private void Open() { gameObject.SetActive(true); transform.SetAsLastSibling(); }

    public static bool IsOpen => current != null && current.gameObject.activeInHierarchy;
    public static void Close() { if (current != null) current.gameObject.SetActive(false); }

    private static Text NewText(RectTransform parent, string s, int size, Vector2 pos, Vector2 sz, TextAnchor a, bool fill = false)
    {
        RectTransform rt = MenuSkin.NewRect("Text", parent);
        if (fill) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
        else { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = sz; rt.anchoredPosition = pos; }
        Text t = rt.gameObject.AddComponent<Text>();
        t.text = s; t.raycastTarget = false;
        MenuSkin.StyleText(t, size, Color.white, true);
        t.alignment = a;
        return t;
    }

    private static Button MakeButton(RectTransform card, string name, Vector2 pos, Vector2 size, out Text label)
    {
        RectTransform brt = MenuSkin.NewRect(name, card);
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = size;
        brt.anchoredPosition = pos;
        brt.gameObject.AddComponent<Image>();
        Button b = brt.gameObject.AddComponent<Button>();
        label = NewText(brt, "", 20, Vector2.zero, Vector2.zero, TextAnchor.MiddleCenter, true);
        MenuSkin.StyleButton(b);
        return b;
    }

    // 모니터가 지원하는 해상도(같은 크기는 하나로) 중 지금 다음 것. 끝이면 처음으로
    private static Vector2Int NextResolution(int w, int h)
    {
        var list = new System.Collections.Generic.List<Vector2Int>();
        foreach (Resolution r in Screen.resolutions)
        {
            var v = new Vector2Int(r.width, r.height);
            if (r.width >= 1024 && !list.Contains(v)) list.Add(v);
        }
        if (list.Count == 0) return new Vector2Int(w, h);
        int i = list.IndexOf(new Vector2Int(w, h));
        return list[(i + 1) % list.Count];
    }

    private static void AddSlider(RectTransform card, string label, float y, float value, System.Action<float> set, System.Func<float, string> fmt = null)
    {
        if (fmt == null) fmt = v => Mathf.RoundToInt(v * 100f) + "%";
        NewText(card, label, 22, new Vector2(-170f, y), new Vector2(200f, 40f), TextAnchor.MiddleLeft);
        Text pct = NewText(card, "", 20, new Vector2(240f, y), new Vector2(80f, 40f), TextAnchor.MiddleRight);

        RectTransform srt = MenuSkin.NewRect("Slider", card);
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = new Vector2(300f, 24f);
        srt.anchoredPosition = new Vector2(55f, y);

        RectTransform bg = MenuSkin.NewRect("Track", srt);
        bg.anchorMin = new Vector2(0f, 0.3f); bg.anchorMax = new Vector2(1f, 0.7f); bg.offsetMin = bg.offsetMax = Vector2.zero;
        bg.gameObject.AddComponent<Image>().color = Track;

        RectTransform area = MenuSkin.NewRect("FillArea", srt);
        area.anchorMin = new Vector2(0f, 0.3f); area.anchorMax = new Vector2(1f, 0.7f); area.offsetMin = area.offsetMax = Vector2.zero;
        RectTransform fill = MenuSkin.NewRect("Fill", area);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        Image fi = fill.gameObject.AddComponent<Image>(); fi.color = Fill; fi.raycastTarget = false;

        RectTransform harea = MenuSkin.NewRect("HandleArea", srt);
        harea.anchorMin = Vector2.zero; harea.anchorMax = Vector2.one; harea.offsetMin = new Vector2(8f, 0f); harea.offsetMax = new Vector2(-8f, 0f);
        RectTransform handle = MenuSkin.NewRect("Handle", harea);
        handle.sizeDelta = new Vector2(16f, 0f);
        Image hi = handle.gameObject.AddComponent<Image>(); hi.color = Color.white;
        MenuSkin.AddFrame(handle, new Color(0.13f, 0.08f, 0.1f, 1f), 2f);

        Slider s = srt.gameObject.AddComponent<Slider>();
        s.fillRect = fill; s.handleRect = handle; s.targetGraphic = hi;
        s.direction = Slider.Direction.LeftToRight; s.minValue = 0f; s.maxValue = 1f;
        s.value = value;
        pct.text = fmt(value);
        s.onValueChanged.AddListener(v => { set(v); pct.text = fmt(v); });
    }
}
