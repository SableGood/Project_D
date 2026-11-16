using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// v168 일시정지 창 도감: 옵션 아래 '직업 목록 · 무기 목록 · 재능 목록' 버튼
//  ■ 누르면 오른쪽 조작키 안내 자리에 목록이 대신 나옴 (같은 버튼을 다시 누르거나 '조작키'를 누르면 안내로)
//  ■ 직업: 아이콘·장점·단점, 지금 직업 표시 / 재능: 희귀도 필터 + 보유 수 (테스트 재능 목록과 같은 모양, 얻기 없음)
//  ■ 무기: 분류별 성장 줄기 (기본 → 모던 → 어썰트·레인저·발리스타, 해금 무기). 테크트리 화면은 시안 확정 후
//  * 일시정지 창을 다시 열면 항상 조작키 안내부터
public class PauseCodex : MonoBehaviour
{
    public enum Tab { None, Jobs, Weapons, Talents }
    private static readonly (string label, Tab tab)[] Buttons = { ("직업 목록", Tab.Jobs), ("무기 목록", Tab.Weapons), ("재능 목록", Tab.Talents) };
    private static PauseCodex current;

    private static readonly Color Row = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color RowMine = new Color(0.78f, 0.91f, 0.65f, 0.16f);
    private static readonly Color Accent = new Color(0.78f, 0.91f, 0.65f, 1f);   // #C6E7A7
    private static readonly Color TextMain = new Color(0.93f, 0.93f, 0.9f, 1f);
    private static readonly Color TextSub = new Color(0.66f, 0.66f, 0.63f, 1f);
    private static readonly string[] FilterNames = { "전체", "일반", "고급", "희귀", "영웅", "전설", "신화", "보유" };

    private GameObject guide;
    private CanvasGroup group;
    private Text title, count;
    private RectTransform filterRow, scrollRt, content;
    private readonly List<Image> filterBg = new List<Image>();
    private readonly List<Text> filterTx = new List<Text>();
    private Tab tab = Tab.None;
    private int filter;

    // ===== 일시정지 창 꾸미기 전: 옵션 버튼을 복사해 그 아래에 셋 =====
    public static void AddButtons(Transform pausePanel)
    {
        Button opt = null;
        foreach (Transform c in pausePanel)
        {
            Button b = c.GetComponent<Button>();
            if (b == null) continue;
            Text tx = b.GetComponentInChildren<Text>(true);
            if (c.name == "Pause_Option" || c.name == "OptionsButton" || (tx != null && tx.text.Trim() == "옵션")) { opt = b; break; }
        }
        if (opt == null) return;
        int idx = opt.transform.GetSiblingIndex();
        for (int i = 0; i < Buttons.Length; i++)
        {
            Tab t = Buttons[i].tab;
            Button nb = Instantiate(opt, pausePanel);
            nb.name = "Codex_" + t;
            nb.onClick = new Button.ButtonClickedEvent();
            nb.onClick.AddListener(() => { if (current != null) current.Show(t); });
            foreach (Text tx in nb.GetComponentsInChildren<Text>(true)) tx.text = Buttons[i].label;
            nb.transform.SetSiblingIndex(idx + 1 + i);
        }
    }

    // ===== 조작키 안내(KeyGuide.AttachToPause) 다음: 같은 자리에 목록 칸 =====
    public static void Build(Transform pausePanel)
    {
        KeyGuide g = pausePanel.GetComponentInChildren<KeyGuide>(true);
        if (g == null) return;
        RectTransform grt = (RectTransform)g.transform;
        RectTransform rt = NewRect("PauseCodex", pausePanel);
        rt.anchorMin = grt.anchorMin; rt.anchorMax = grt.anchorMax; rt.pivot = grt.pivot;
        rt.anchoredPosition = grt.anchoredPosition; rt.sizeDelta = grt.sizeDelta;
        rt.SetSiblingIndex(grt.GetSiblingIndex() + 1);   // 옵션 창보다 아래
        PauseCodex c = rt.gameObject.AddComponent<PauseCodex>();
        c.guide = g.gameObject;
        c.Make(rt);
        current = c;
    }

    void OnEnable() { if (group != null) Show(Tab.None); }   // 일시정지 창이 열릴 때
    void OnDestroy() { if (current == this) current = null; }

    public void Show(Tab t)
    {
        if (t != Tab.None && t == tab) t = Tab.None;   // 같은 버튼 다시 = 닫기
        tab = t;
        bool on = t != Tab.None;
        if (guide != null) guide.SetActive(!on);
        group.alpha = on ? 1f : 0f;
        group.blocksRaycasts = group.interactable = on;
        if (!on) return;
        Sfx.UIOpen();
        filter = 0;
        Rebuild();
    }

    // ===== 틀 =====
    private void Make(RectTransform rt)
    {
        Vector2 size = rt.sizeDelta;
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = group.interactable = false;

        title = NewText(rt, "Title", "", 22, Accent, TextAnchor.MiddleLeft);
        TL(title.rectTransform, 22f, 12f, 400f, 30f);
        count = NewText(rt, "Count", "", 16, TextSub, TextAnchor.MiddleRight);
        TL(count.rectTransform, size.x - 22f - 300f - 130f, 12f, 290f, 30f);
        Button back = MakeButton(rt, "조작키", 15);
        TL((RectTransform)back.transform, size.x - 22f - 120f, 10f, 120f, 34f);
        back.onClick.AddListener(() => Show(Tab.None));

        filterRow = NewRect("Filters", rt);
        TL(filterRow, 22f, 52f, size.x - 44f, 30f);
        float fx = 0f;
        for (int i = 0; i < FilterNames.Length; i++)
        {
            int k = i;
            Button b = MakeButton(filterRow, FilterNames[i], 14);
            TL((RectTransform)b.transform, fx, 0f, 78f, 30f);
            fx += 84f;
            b.onClick.AddListener(() => { filter = k; Rebuild(); });
            filterBg.Add(b.GetComponent<Image>());
            filterTx.Add(b.GetComponentInChildren<Text>());
        }

        // 스크롤 목록
        scrollRt = NewRect("Scroll", rt);
        ScrollRect sr = scrollRt.gameObject.AddComponent<ScrollRect>();
        sr.horizontal = false; sr.scrollSensitivity = 36f; sr.movementType = ScrollRect.MovementType.Clamped;
        RectTransform vp = NewRect("Viewport", scrollRt);
        Fill(vp, 0f);
        vp.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);   // 휠 받기
        vp.gameObject.AddComponent<RectMask2D>();
        content = NewRect("Content", vp);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f); content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero; content.anchoredPosition = Vector2.zero;
        VerticalLayoutGroup vl = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 6f; vl.childControlHeight = true; vl.childControlWidth = true;
        vl.childForceExpandHeight = false; vl.childForceExpandWidth = true;
        vl.padding = new RectOffset(0, 8, 0, 0);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.viewport = vp; sr.content = content;
    }

    private void Rebuild()
    {
        Vector2 size = ((RectTransform)transform).sizeDelta;
        bool filters = tab == Tab.Talents;
        filterRow.gameObject.SetActive(filters);
        float top = filters ? 92f : 54f;
        TL(scrollRt, 22f, top, size.x - 44f, size.y - top - 16f);
        for (int i = content.childCount - 1; i >= 0; i--) Destroy(content.GetChild(i).gameObject);
        content.anchoredPosition = Vector2.zero;
        int n = tab == Tab.Jobs ? FillJobs() : tab == Tab.Talents ? FillTalents() : FillWeapons();
        count.text = $"{n}개";
        for (int i = 0; i < filterBg.Count; i++)
        {
            filterBg[i].color = i == filter ? Accent : Row;
            filterTx[i].color = i == filter ? new Color(0.13f, 0.08f, 0.1f) : TextMain;
        }
    }

    // ----- 직업 -----
    private int FillJobs()
    {
        title.text = "직업 목록";
        int mine = GameManager.Instance != null ? GameManager.Instance.SelectedJob : -1;
        for (int i = 0; i < JobData.All.Length; i++)
        {
            JobData.Job j = JobData.All[i];
            RectTransform r = MakeRow(78f, i == mine ? RowMine : Row);
            if (j.Icon != null)
            {
                Image ic = NewRect("Icon", r).gameObject.AddComponent<Image>();
                ic.sprite = j.Icon; ic.preserveAspect = true; ic.raycastTarget = false;
                TL(ic.rectTransform, 10f, 7f, 64f, 64f);
            }
            string me = i == mine ? "  <size=14><color=#C6E7A7>지금 직업</color></size>" : "";
            string cons = string.IsNullOrEmpty(j.cons) ? "" : $"\n<color=#F0A08C>－ {j.cons}</color>";
            Text t = NewText(r, "Text", $"<b>{j.name}</b>{me}\n<size=15><color=#B8E0A0>＋ {j.pros}</color>{cons}</size>", 19, TextMain, TextAnchor.MiddleLeft);
            Stretch(t.rectTransform, 86f, 4f, 10f, 4f);
        }
        return JobData.All.Length;
    }

    // ----- 재능 -----
    private int FillTalents()
    {
        title.text = "재능 목록";
        PlayerAugments pa = PlayerAugments.Instance;
        int shown = 0;
        foreach (AugmentData a in AugmentDatabase.All)
        {
            if (!a.enabled || !PlayerAugments.IsKnownEffect(a.effect)) continue;
            int stack = pa != null ? pa.StackOf(a.code) : 0;
            if (filter >= 1 && filter <= 6 && AugmentManager.RarityIndex(a.rarity) != filter - 1) continue;
            if (filter == 7 && stack == 0) continue;
            RectTransform r = MakeRow(60f, stack > 0 ? RowMine : Row);
            Image band = NewRect("Band", r).gameObject.AddComponent<Image>();
            band.color = AugmentManager.RarityColor(a.rarity); band.raycastTarget = false;
            RectTransform br = band.rectTransform;
            br.anchorMin = Vector2.zero; br.anchorMax = new Vector2(0f, 1f); br.pivot = new Vector2(0f, 0.5f);
            br.sizeDelta = new Vector2(5f, 0f); br.anchoredPosition = Vector2.zero;
            string rc = ColorUtility.ToHtmlStringRGB(AugmentManager.RarityColor(a.rarity));
            int max = Mathf.Max(1, a.maxStacks);
            string own = stack > 0 ? $"  <color=#C6E7A7>보유 {stack}/{max}</color>" : (max > 1 ? $"  <color=#8A8A84>최대 {max}</color>" : "");
            Text t = NewText(r, "Text", $"<b>{a.name}</b>  <size=14><color=#{rc}>{a.rarity}</color></size>{own}\n<size=15><color=#A8A8A0>{a.description}</color></size>", 18, TextMain, TextAnchor.MiddleLeft);
            Stretch(t.rectTransform, 16f, 4f, 10f, 4f);
            shown++;
        }
        return shown;
    }

    // ----- 무기 (분류별 성장 줄기) -----
    private static readonly (string tag, string name)[] Cats =
    {
        ("HG", "권총"), ("SMG", "기관단총"), ("AR", "돌격소총"), ("BR", "전투소총"), ("MG", "기관총"),
        ("RF", "저격총"), ("SG", "산탄총"), ("MELEE", "근접"), ("RL", "로켓"), ("GL", "유탄"),
    };
    private int FillWeapons()
    {
        title.text = "무기 목록  <size=14><color=#8A8A84>기본 → 모던 → 어썰트 · 레인저 · 발리스타</color></size>";
        PlayerWeaponManager pm = FindFirstObjectByType<PlayerWeaponManager>();
        if (pm == null) return 0;
        var owned = new HashSet<string>();
        foreach (int o in pm.Owned) { PlayerWeaponData d = pm.DataAt(o); if (d != null) owned.Add(d.weaponID); }
        foreach (var (tag, cname) in Cats)
        {
            // 같은 이름(“콜트 / 모던”의 ‘콜트’)끼리 한 줄기: 기본 → 모던 → 3티어 갈래
            var fams = new List<string>();
            var famParts = new Dictionary<string, List<(string part, PlayerWeaponData d)>>();
            var extra = new System.Text.StringBuilder();
            for (int i = 0; i < pm.CatalogCount; i++)
            {
                PlayerWeaponData d = pm.DataAt(i);
                if (d == null || d.Category.ToUpper() != tag) continue;
                string nm = string.IsNullOrEmpty(d.inGameName) ? d.weaponName : d.inGameName;
                string acq = (d.acquire ?? "").Trim();
                string col = owned.Contains(d.weaponID) ? "C6E7A7" : "EDEDE6";
                if (acq == "해금" || acq == "재능")
                {
                    extra.Append(extra.Length > 0 ? " · " : "").Append($"<color=#{col}>{nm}</color><size=13><color=#8A8A84>({acq})</color></size>");
                    continue;
                }
                int slash = nm.IndexOf(" / ");
                string fam = slash >= 0 ? nm.Substring(0, slash) : nm;
                string part = slash >= 0 ? nm.Substring(slash + 3) : "";
                if (!famParts.TryGetValue(fam, out var lst)) { famParts[fam] = lst = new List<(string, PlayerWeaponData)>(); fams.Add(fam); }
                lst.Add((part, d));
            }
            if (fams.Count == 0 && extra.Length == 0) continue;
            var sb = new System.Text.StringBuilder();
            foreach (string fam in fams)
            {
                string Chip(string label, PlayerWeaponData w) => $"<color=#{(owned.Contains(w.weaponID) ? "C6E7A7" : "EDEDE6")}>{label}</color>";
                string head = $"<b>{fam}</b>", mid = "";
                var t3 = new List<string>();
                foreach (var (part, d) in famParts[fam])
                {
                    if (part == "") head = "<b>" + Chip(fam, d) + "</b>";
                    else if (part == "모던") mid = Chip("모던", d);
                    else t3.Add(Chip(part, d));
                }
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(head);
                if (mid != "") sb.Append("  →  ").Append(mid);
                if (t3.Count > 0) sb.Append("  →  ").Append(string.Join(" · ", t3));
            }
            if (extra.Length > 0) { if (sb.Length > 0) sb.Append('\n'); sb.Append($"<size=14><color=#8A8A84>그 밖에</color></size>  {extra}"); }
            int lines = sb.ToString().Split('\n').Length;
            RectTransform r = MakeRow(16f + lines * 24f, Row);
            Text h = NewText(r, "Cat", cname, 15, Accent, TextAnchor.UpperLeft);
            TL(h.rectTransform, 12f, 10f, 80f, 24f);
            Text t = NewText(r, "Tree", sb.ToString(), 16, TextMain, TextAnchor.MiddleLeft);
            Stretch(t.rectTransform, 100f, 6f, 10f, 6f);
        }
        return pm.CatalogCount;
    }

    // ===== 도우미 =====
    private RectTransform MakeRow(float h, Color bg)
    {
        RectTransform r = NewRect("Row", content);
        Image img = r.gameObject.AddComponent<Image>();
        img.color = bg; img.raycastTarget = true;   // 행 위에서도 휠 스크롤
        r.gameObject.AddComponent<LayoutElement>().preferredHeight = h;
        return r;
    }

    private static Button MakeButton(Transform parent, string label, int fs)
    {
        RectTransform r = NewRect("Btn_" + label, parent);
        Image img = r.gameObject.AddComponent<Image>();
        img.color = Row;
        Button b = r.gameObject.AddComponent<Button>();
        ColorBlock cb = b.colors; cb.highlightedColor = new Color(1.3f, 1.3f, 1.3f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); b.colors = cb;
        Text t = NewText(r, "Label", label, fs, TextMain, TextAnchor.MiddleCenter);
        Fill(t.rectTransform, 0f);
        return b;
    }

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

    private static void Fill(RectTransform r, float d)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(d, d); r.offsetMax = new Vector2(-d, -d);
    }

    private static void Stretch(RectTransform r, float l, float t, float right, float b)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(-right, -t);
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
