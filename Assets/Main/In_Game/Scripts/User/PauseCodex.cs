using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// v168 일시정지 창 도감: 옵션 아래 '직업 목록 · 무기 목록 · 재능 목록' 버튼
//  ■ 누르면 오른쪽 조작키 안내 자리에 목록이 대신 나옴 (같은 버튼을 다시 누르거나 '조작키'를 누르면 안내로)
//  ■ 직업: 아이콘·장점·단점, 지금 직업 표시 / 재능: 희귀도 필터 + 보유 수 (테스트 재능 목록과 같은 모양, 얻기 없음)
//  ■ 무기: v170 분류 탭 + 세로 테크트리 (기본 ↓ 모던 ↓ 어썰트·레인저·발리스타), 노드마다 무기 그림·보유 표시
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
    private int catSel = 0;   // v170 무기 분류 탭 (일시정지를 다시 열어도 유지)
    private RectTransform catRow;
    private readonly List<Image> catBg = new List<Image>();
    private readonly List<Text> catTx = new List<Text>();

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
        if (!on) { HideTip(); return; }
        Sfx.UIOpen();
        HideTip();
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

        // v170 무기 분류 탭
        catRow = NewRect("Categories", rt);
        TL(catRow, 22f, 52f, size.x - 44f, 30f);
        float cw = (size.x - 44f - (CatTabs.Length - 1) * 6f) / CatTabs.Length, cx = 0f;
        for (int i = 0; i < CatTabs.Length; i++)
        {
            int k = i;
            Button b = MakeButton(catRow, CatTabs[i].label, 14);
            TL((RectTransform)b.transform, cx, 0f, cw, 30f);
            cx += cw + 6f;
            b.onClick.AddListener(() => { catSel = k; Rebuild(); });
            catBg.Add(b.GetComponent<Image>());
            catTx.Add(b.GetComponentInChildren<Text>());
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
        catRow.gameObject.SetActive(tab == Tab.Weapons);
        float top = filters || tab == Tab.Weapons ? 92f : 54f;
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
        for (int i = 0; i < catBg.Count; i++)
        {
            catBg[i].color = i == catSel ? Accent : Row;
            catTx[i].color = i == catSel ? new Color(0.13f, 0.08f, 0.1f) : TextMain;
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

    // ----- 무기: v170 세로 테크트리 (분류 탭 → 이름별 줄기: 기본 ↓ 모던 ↓ 갈래), 노드마다 무기 그림 -----
    private static readonly (string label, string[] tags)[] CatTabs =
    {
        ("권총", new[] { "HG" }), ("기관단총", new[] { "SMG" }), ("돌격소총", new[] { "AR" }), ("전투소총", new[] { "BR" }),
        ("기관총", new[] { "MG" }), ("저격총", new[] { "RF" }), ("산탄총", new[] { "SG" }), ("근접", new[] { "MELEE" }), ("특수", new[] { "RL", "GL" }),
    };
    // v171 작게 접힌 노드 (그림 일부 + 이름), 마우스를 올리면 강조 + 옆에 자세한 정보
    private const float NodeW = 112f, NodeH = 58f, RowGap = 18f, ColGap = 8f, FamGap = 44f;
    private RectTransform tip; private Image tipPic; private Text tipText;

    private int FillWeapons()
    {
        title.text = "무기 목록  <size=14><color=#8A8A84>위에서 아래로 강화 · 마우스를 올리면 자세히</color></size>";
        HideTip();
        PlayerWeaponManager pm = FindFirstObjectByType<PlayerWeaponManager>();
        if (pm == null) return 0;
        var owned = new HashSet<string>();
        foreach (int o in pm.Owned) { PlayerWeaponData d = pm.DataAt(o); if (d != null) owned.Add(d.weaponID); }
        var tags = new HashSet<string>(CatTabs[Mathf.Clamp(catSel, 0, CatTabs.Length - 1)].tags);

        // 이름 앞부분(“유진 / 모던”의 ‘유진’)이 같은 무기끼리 한 줄기. 상점 줄기 먼저, 해금·재능 줄기는 오른쪽
        var fams = new List<string>();
        var parts = new Dictionary<string, List<(string part, PlayerWeaponData d)>>();
        for (int i = 0; i < pm.CatalogCount; i++)
        {
            PlayerWeaponData d = pm.DataAt(i);
            if (d == null || !tags.Contains(d.Category.ToUpper())) continue;
            string nm = string.IsNullOrEmpty(d.inGameName) ? d.weaponName : d.inGameName;
            int slash = nm.IndexOf(" / ");
            string fam = slash >= 0 ? nm.Substring(0, slash) : nm;
            if (!parts.TryGetValue(fam, out var lst)) { parts[fam] = lst = new List<(string, PlayerWeaponData)>(); fams.Add(fam); }
            lst.Add((slash >= 0 ? nm.Substring(slash + 3) : "", d));
        }
        if (fams.Count == 0) { RectTransform er = MakeRow(44f, Row); Text et = NewText(er, "Empty", "이 분류에는 무기가 없어요", 15, TextSub, TextAnchor.MiddleCenter); Fill(et.rectTransform, 0f); return 0; }

        var famRows = new Dictionary<string, List<List<(string part, PlayerWeaponData d)>>>();
        var famWidth = new Dictionary<string, float>();
        var famKind = new Dictionary<string, int>();   // 0 상점, 1 해금, 2 재능
        int maxRows = 1;
        foreach (string fam in fams)
        {
            var list = parts[fam];
            list.Sort((x, y) => string.CompareOrdinal(x.d.weaponID, y.d.weaponID));
            var rows = new List<List<(string part, PlayerWeaponData d)>> { new List<(string, PlayerWeaponData)>(), new List<(string, PlayerWeaponData)>(), new List<(string, PlayerWeaponData)>() };
            foreach (var e in list) rows[e.part == "" ? 0 : e.part == "모던" ? 1 : 2].Add(e);
            rows.RemoveAll(rw => rw.Count == 0);
            famRows[fam] = rows;
            int wide = 1; foreach (var rw in rows) wide = Mathf.Max(wide, rw.Count);
            famWidth[fam] = wide * NodeW + (wide - 1) * ColGap;
            PlayerWeaponData first = rows[0][0].d;
            famKind[fam] = first.ShopUnlock ? 1 : first.TalentOnly ? 2 : 0;
            int offset = famKind[fam] == 1 ? 1 : 0;   // 해금 줄기는 모던과 같은 높이에서 시작 (같은 티어)
            maxRows = Mathf.Max(maxRows, rows.Count + offset);
        }
        fams.Sort((x, y) => famKind[x].CompareTo(famKind[y]));

        float W = Mathf.Max(400f, scrollRt.sizeDelta.x - 8f);
        float total = -FamGap; foreach (string fam in fams) total += famWidth[fam] + FamGap;
        const float head = 10f, legendH = 26f;
        float h = head + maxRows * NodeH + (maxRows - 1) * RowGap + 12f + legendH;
        RectTransform block = MakeRow(h, Row);
        float fx = Mathf.Max(8f, (W - total) * 0.5f);
        int nodes = 0;
        float mainCx = -1f; bool mainHasModern = false;
        foreach (string fam in fams)
        {
            var rows = famRows[fam];
            float fw = famWidth[fam], cxFam = fx + fw * 0.5f;
            int kind = famKind[fam];
            int offset = kind == 1 ? 1 : 0;

            for (int r = 0; r < rows.Count; r++)
            {
                int n = rows[r].Count;
                float rw = n * NodeW + (n - 1) * ColGap;
                float x0 = cxFam - rw * 0.5f;
                float y = head + (r + offset) * (NodeH + RowGap);
                if (r > 0)
                {
                    float prevBottom = y - RowGap, mid = prevBottom + RowGap * 0.5f;
                    Line(block, cxFam - 1f, prevBottom, 2f, mid - prevBottom + 1f);
                    float c0 = x0 + NodeW * 0.5f, c1 = x0 + rw - NodeW * 0.5f;
                    if (n > 1) Line(block, c0, mid - 1f, c1 - c0, 2f);
                    for (int k = 0; k < n; k++) { float c = x0 + k * (NodeW + ColGap) + NodeW * 0.5f; Line(block, c - 1f, mid, 2f, y - mid); }
                }
                for (int k = 0; k < n; k++)
                {
                    var (part, d) = rows[r][k];
                    float nx = x0 + k * (NodeW + ColGap);
                    string how = r == 0 ? (kind == 1 ? "해금 · 같은 분류 1티어를 상점에서 얻으면 상점에 등장" : kind == 2 ? "재능으로만 얻음" : "상점에서 구매")
                                        : "윗단계 무기를 강화";
                    WeaponNode(block, nx, y, part == "" ? fam : $"{fam} / {part}", d, owned.Contains(d.weaponID), how, TierColor(part, kind));
                    nodes++;
                }
            }
            if (kind == 0 && mainCx < 0f) { mainCx = cxFam; mainHasModern = rows.Count > 1; }
            // 해금 갈래: 1티어 → 모던으로 내려가는 가지 중간에서 옆으로 뻗어 모던과 같은 높이에
            if (kind == 1 && mainCx > 0f)
            {
                float stemTop = head + NodeH, mid = stemTop + RowGap * 0.5f, top = head + NodeH + RowGap;
                if (!mainHasModern) Line(block, mainCx - 1f, stemTop, 2f, mid - stemTop + 1f);
                float lx = Mathf.Min(mainCx, cxFam), rx = Mathf.Max(mainCx, cxFam);
                Line(block, lx, mid - 1f, rx - lx, 2f);
                Line(block, cxFam - 1f, mid, 2f, top - mid);
            }
            fx += fw + FamGap;
        }

        // 색 리본 범례
        RectTransform legend = NewRect("Legend", block);
        TL(legend, 0f, h - legendH, W, legendH - 4f);
        HorizontalLayoutGroup hl = legend.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.childAlignment = TextAnchor.MiddleCenter; hl.spacing = 14f; hl.childControlWidth = hl.childControlHeight = true; hl.childForceExpandWidth = hl.childForceExpandHeight = false;
        foreach (var (lbl, col) in Legend)
        {
            RectTransform item = NewRect("L_" + lbl, legend);
            HorizontalLayoutGroup il = item.gameObject.AddComponent<HorizontalLayoutGroup>();
            il.spacing = 4f; il.childAlignment = TextAnchor.MiddleLeft; il.childControlWidth = il.childControlHeight = true; il.childForceExpandWidth = il.childForceExpandHeight = false;
            Image tri = NewRect("Tri", item).gameObject.AddComponent<Image>();
            tri.sprite = Corner(); tri.color = col; tri.raycastTarget = false;
            LayoutElement le = tri.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = le.preferredHeight = 12f;
            Text lt = NewText(item, "T", lbl, 12, TextSub, TextAnchor.MiddleLeft);
            lt.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
        return nodes;
    }

    // v172 티어·특성 색 (노드 오른쪽 아래 직각삼각형 리본)
    private static readonly Color Wood = new Color(0.62f, 0.42f, 0.24f), Modern = new Color(0.33f, 0.34f, 0.36f),
        Assault = new Color(0.9f, 0.36f, 0.24f), Ranger = new Color(0.42f, 0.74f, 0.36f), Ballista = new Color(0.32f, 0.56f, 0.9f),
        Unlock = new Color(0.92f, 0.72f, 0.2f), TalentCol = new Color(0.66f, 0.46f, 0.9f), UpgradeCol = new Color(0.96f, 0.86f, 0.5f);
    private static readonly (string, Color)[] Legend =
    {
        ("1티어", Wood), ("모던", Modern), ("어썰트", Assault), ("레인저", Ranger), ("발리스타", Ballista), ("해금", Unlock), ("재능", TalentCol),
    };
    private static Color TierColor(string part, int kind)
    {
        if (kind == 2) return TalentCol;
        if (kind == 1) return part == "" ? Unlock : UpgradeCol;
        switch (part)
        {
            case "": return Wood;
            case "모던": return Modern;
            case "어썰트": return Assault;
            case "레인저": return Ranger;
            case "발리스타": return Ballista;
            default: return UpgradeCol;
        }
    }
    private static Sprite cornerSprite;
    private static Sprite Corner()
    {
        if (cornerSprite != null) return cornerSprite;
        const int N = 32;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                px[y * N + x] = x >= N - 1 - y ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);   // 오른쪽 아래 직각삼각형
        t.SetPixels32(px); t.Apply();
        cornerSprite = Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
        return cornerSprite;
    }

    private void WeaponNode(RectTransform parent, float x, float y, string fullName, PlayerWeaponData d, bool mine, string how, Color ribbon)
    {
        RectTransform n = NewRect("Node_" + d.weaponID, parent);
        TL(n, x, y, NodeW, NodeH);
        Image bg = n.gameObject.AddComponent<Image>();
        Color baseBg = mine ? RowMine : new Color(0f, 0f, 0f, 0.28f);
        bg.color = baseBg; bg.raycastTarget = true;   // 마우스 올림 판정 (스크롤은 그대로 통과)
        Outline o = n.gameObject.AddComponent<Outline>();
        Color baseLine = mine ? Accent : new Color(1f, 1f, 1f, 0.22f);
        o.effectColor = baseLine;
        o.effectDistance = mine ? new Vector2(2f, -2f) : new Vector2(1f, -1f);

        // 무기 그림: 칸에 맞춰 전체 (자르지 않음)
        Image pic = NewRect("Pic", n).gameObject.AddComponent<Image>();
        pic.sprite = d.weaponSprite; pic.preserveAspect = true; pic.raycastTarget = false; pic.enabled = d.weaponSprite != null;
        TL(pic.rectTransform, 5f, 5f, NodeW - 10f, NodeH - 10f);

        // 티어·특성 색 리본 (오른쪽 아래, 테두리에 맞춘 직각삼각형)
        Image tri = NewRect("Ribbon", n).gameObject.AddComponent<Image>();
        tri.sprite = Corner(); tri.color = ribbon; tri.raycastTarget = false;
        RectTransform tr = tri.rectTransform;
        tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(1f, 0f);
        tr.sizeDelta = new Vector2(20f, 20f); tr.anchoredPosition = Vector2.zero;

        CodexHover hv = n.gameObject.AddComponent<CodexHover>();
        hv.enter = () =>
        {
            bg.color = mine ? new Color(0.78f, 0.91f, 0.65f, 0.28f) : new Color(1f, 1f, 1f, 0.14f);
            o.effectColor = Color.white; o.effectDistance = new Vector2(2f, -2f);
            ShowTip(n, d, fullName, mine, false, how);
        };
        hv.exit = () =>
        {
            bg.color = baseBg; o.effectColor = baseLine; o.effectDistance = mine ? new Vector2(2f, -2f) : new Vector2(1f, -1f);
            HideTip();
        };
    }

    private void ShowTip(RectTransform node, PlayerWeaponData d, string name, bool mine, bool locked, string how)
    {
        RectTransform root = (RectTransform)transform;
        if (tip == null)
        {
            tip = NewRect("WeaponTip", root);
            Image bg = tip.gameObject.AddComponent<Image>();
            bg.color = new Color(0.09f, 0.09f, 0.085f, 0.97f); bg.raycastTarget = false;
            Outline ol = tip.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(1f, 1f, 1f, 0.3f);
            tipPic = NewRect("Pic", tip).gameObject.AddComponent<Image>();
            tipPic.preserveAspect = true; tipPic.raycastTarget = false;
            TL(tipPic.rectTransform, 12f, 10f, 236f, 70f);
            tipText = NewText(tip, "Text", "", 14, TextMain, TextAnchor.UpperLeft);
            TL(tipText.rectTransform, 14f, 86f, 232f, 140f);
        }
        tip.SetAsLastSibling();
        tip.gameObject.SetActive(true);
        tipPic.sprite = d.weaponSprite; tipPic.enabled = d.weaponSprite != null;
        string state = mine ? "<color=#C6E7A7>보유</color>" : locked ? "<color=#F0A08C>잠김</color>" : "<color=#B5B5AE>미보유</color>";
        string ammo = d.UsesAmmo ? $"{d.maxAmmo}발 · 재장전 {d.reloadTime:0.#}초" : "탄 무한";
        string role = string.IsNullOrEmpty(d.role) ? "" : $"<size=12><color=#8A8A84>{d.role}</color></size>\n";
        tipText.text = $"{role}<b><size=17>{name}</size></b>  <size=12>{state}</size>\n" +
                       $"<size=13><color=#D8D8D0>피해 {d.damage:0.#} · 연사 {d.fireRate:0.#}/초 · 사거리 {d.range:0.#}\n{ammo}</color>\n<color=#8A8A84>{how}</color></size>";
        float th = 86f + tipText.preferredHeight + 12f;
        tip.anchorMin = tip.anchorMax = tip.pivot = new Vector2(0f, 1f);
        tip.sizeDelta = new Vector2(260f, th);
        // 노드 오른쪽에, 넘치면 왼쪽에
        Vector3[] c = new Vector3[4]; node.GetWorldCorners(c);
        Vector2 tr = root.InverseTransformPoint(c[2]), tl = root.InverseTransformPoint(c[1]);
        Vector2 size = root.rect.size;
        float px = tr.x - root.rect.xMin + 8f;
        if (px + 260f > size.x) px = tl.x - root.rect.xMin - 8f - 260f;
        float py = -(root.rect.yMax - tr.y);
        py = Mathf.Clamp(py, -(size.y - th), 0f);
        tip.anchoredPosition = new Vector2(Mathf.Max(0f, px), py);
    }

    private void HideTip() { if (tip != null) tip.gameObject.SetActive(false); }

    private static void Line(RectTransform parent, float x, float y, float w, float h)
    {
        Image l = NewRect("Line", parent).gameObject.AddComponent<Image>();
        l.color = new Color(1f, 1f, 1f, 0.35f); l.raycastTarget = false;
        TL(l.rectTransform, x, y, Mathf.Max(1f, w), Mathf.Max(1f, h));
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

// v171 무기 노드 마우스 올림 (휠·드래그는 막지 않음 → 목록 스크롤 그대로)
public class CodexHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    public System.Action enter, exit;
    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { enter?.Invoke(); }
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { exit?.Invoke(); }
    void OnDisable() { exit?.Invoke(); }
}
