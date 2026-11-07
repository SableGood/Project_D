using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using System.Text;

// 재능 선택 (웨이브 클리어 후 3개 중 1개 고르기)  ※ 코드 안의 Augment(증강) = 게임 용어 '재능'
//  - 재능 목록: Resources/Augment_Data.csv (구글 시트 Augment_Data 탭 → Tools/CSV 데이터 생성/0 으로 가져오기)
//  - 언제: 웨이브를 클리어할 때마다. 단 엘리트 웨이브 뒤에는 재능 대신 엘리트 상점(EliteShop)이 열림
//          보스 웨이브 뒤에는 높은 등급(희귀 이상)만 등장
//          Wave_Data의 Info가 비어 있으면 스테이지 안에서 fallbackWaveNumbers 번째(기본 3, 6) 웨이브를 엘리트로 봄
//  - 선택창에 나왔지만 고르지 않은 재능은 그 판에서 다시 나오지 않음 (RunProgress)
//  - 선택하는 동안 게임 일시정지. F3 = 테스트로 바로 열기
//  - 화면 (B안 '작전 기록'): 왼쪽에서 목록 판이 밀려 들어오고, 오른쪽에 고른 재능의 상세가 나옴
//    1~3 키 / 마우스 올리기 = 고르기, 같은 숫자 다시 / Enter / 클릭 = 확정, ↑↓ = 이동, Esc = 건너뛰기
//    (일시정지 중이라 모든 연출은 Time.unscaledTime 기준)
//  - 스킬 재능(은신·수류탄 등)을 고르면 "사용할 키를 눌러주세요" 단계로 넘어감 (Q / E / Shift / F 중 빈 칸, Esc = 목록으로)
//    빈 스킬 칸이 없으면 스킬 재능은 선택지에 나오지 않음
public class AugmentManager : MonoBehaviour
{
    public static AugmentManager Instance { get; private set; }
    // 재능 선택창 또는 엘리트 상점이 열려 있으면 true (사격·이동·포탑 선택 등을 막는 데 씀)
    public static bool IsOpen => (Instance != null && Instance.open) || EliteShop.IsOpen || TalentBrowser.IsOpen;
    // 선택 창이 닫힌 프레임 (창에서 누른 키가 바로 스킬·포탑 선택으로 쓰이지 않게)
    public static int ClosedFrame { get; private set; } = -10;
    public static void MarkClosed() => ClosedFrame = Time.frameCount;

    [Header("선택지")]
    public int choiceCount = 3;
    // 희귀도 (낮은 등급 → 높은 등급)
    public static readonly string[] Rarities = { "일반", "고급", "희귀", "영웅", "전설", "신화" };
    [Tooltip("희귀도별 등장 가중치 (일반 / 고급 / 희귀 / 영웅 / 전설 / 신화). 0이면 등장하지 않음")]
    public float[] rarityWeights = { 55f, 30f, 12f, 3f, 0f, 0f };
    [Tooltip("웨이브마다 일반 → 희귀 / 영웅으로 옮기는 확률 (%p)")]
    public float rarePerWave = 1.5f, epicPerWave = 0.5f;
    [Tooltip("보스 뒤 높은 등급 선택창의 희귀도별 확률 (일반 / 고급 / 희귀 / 영웅 / 전설 / 신화)")]
    public float[] highTierWeights = { 0f, 0f, 75f, 25f, 0f, 0f };
    private int rollWave = 1;   // 지금 선택창을 연 웨이브 (스테이지 안 번호)
    // 침팬지(무능력도 능력이다): 선택지 +1장
    private int ChoiceCount => choiceCount + (GameManager.Instance != null && GameManager.Instance.SelectedCharacter == 0 ? 1 : 0);

    [Header("등장 시점")]
    [Tooltip("이 종류(Wave_Data의 Info)의 웨이브 뒤에는 재능 대신 엘리트 상점")]
    public string[] shopWaveTypes = { "Elite" };
    [Tooltip("이 종류의 웨이브 뒤에는 높은 등급만 등장")]
    public string highTierWaveType = "Boss";
    [Tooltip("높은 등급 선택창의 최소 희귀도")]
    public string highTierMinRarity = "희귀";
    [Tooltip("웨이브 종류 정보가 없을 때: 스테이지 안에서 이 번째 웨이브를 엘리트로 봄 (상점)")]
    public int[] fallbackWaveNumbers = { 3, 6 };

    [Header("테스트")]
    public bool debugKey = true; // F3

    [Header("연출")]
    [Tooltip("왼쪽 판이 밀려 들어오는 시간 (초)")]
    public float slideDuration = 0.32f;
    [Tooltip("목록 줄이 차례로 나타나는 간격 (초)")]
    public float rowStagger = 0.06f;

    // 화면 배치 (기준 해상도 1920×1080)
    private const float DimAlpha = 0.45f, CloseDuration = 0.32f;
    private const float PanelW = 840f, PadX = 96f, ListW = 696f, RowH = 132f, RowGap = 12f;
    private const float DetailX = 960f, DetailY = 216f, DetailW = 816f, DetailH = 560f;

    private class Row
    {
        public AugmentData data;
        public RectTransform rt;
        public CanvasGroup group;
        public Image frame, inner, keyFrame, keyInner, tagBg;
        public Text key, title, desc, tag;
    }
    private readonly List<Row> rows = new List<Row>();
    private int selected;
    private float openedAt, closeAt, keyShownAt;
    private bool closing;
    private Row chosenRow;
    private string pendingToast, listTitle, listEyebrow;

    private bool open;
    private PlayerAugments player;
    private Canvas canvas;
    private Image dim;
    private RectTransform root, panel, listRoot, keyGrid, detail;
    private CanvasGroup detailGroup;
    private Text eyebrowText, titleText, noteText, ownedText, hintText, toastText;
    private Image detailTagBg;
    private Text detailTag, detailName, detailDesc, detailLabels, detailValues;
    private Text detailIconLabel;
    private Image detailIcon;
    // 스킬 키 선택 단계
    private AugmentData pendingSkill;
    private readonly List<GameObject> keyBoxes = new List<GameObject>();
    private float toastUntil;
    private float prevTimeScale = 1f;
    private CursorLockMode prevLock;
    private bool prevCursorVisible;

    private const string HintList = "숫자 키로 고르기 · 다시 누르거나 Enter 확정 · Esc 건너뛰기";
    private const string HintKeys = "Q · E · Shift · F 중 빈 키를 누르세요 · Esc 목록으로";

    public static AugmentManager EnsureExists()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("AugmentManager");
        return go.AddComponent<AugmentManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        AugmentDatabase.Reload();
        BuildUI();
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        // F3: 테스트 모드면 재능 목록(TalentBrowser)이 열림 (v101). 예전 무작위 3개 테스트 창은 없앰
        if (open)
        {
            HandleKeys();
            Animate();
        }
        if (toastText != null && toastText.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastText.gameObject.SetActive(false);
    }

    private void HandleKeys()
    {
        if (closing) return;
        if (pendingSkill != null)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { BackToList(); return; }
            int slot = PlayerSkills.SlotFromKeyDown();
            if (slot >= 0) PickSkillKey(slot);
            return;
        }
        for (int i = 0; i < rows.Count && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
            {
                if (selected == i) Choose(i); else Select(i);
                return;
            }
        }
        if (Input.GetKeyDown(KeyCode.UpArrow)) Select(Mathf.Max(0, selected - 1));
        else if (Input.GetKeyDown(KeyCode.DownArrow)) Select(Mathf.Min(rows.Count - 1, selected + 1));
        else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Choose(selected);
        else if (Input.GetKeyDown(KeyCode.Escape)) { PassOver(null); BeginClose(null, "재능 선택을 건너뛰었어요"); }
    }

    // ===== 웨이브 클리어 후 (WaveManager가 호출) =====
    public IEnumerator OfferAfterWave(WaveData wave, int waveNumberInStage)
    {
        PlayerAugments pa = FindPlayer();
        rollWave = Mathf.Max(1, waveNumberInStage);
        string waveNote = pa != null ? pa.OnWaveCleared() : null;
        if (IsShopWave(wave, waveNumberInStage))
        {
            if (!string.IsNullOrEmpty(waveNote)) Toast(waveNote);
            yield return EliteShop.Ensure().OpenAndWait($"엘리트 웨이브 {waveNumberInStage} 클리어 · 다음 엘리트 웨이브 뒤에 다시 열려요");
            yield break;
        }
        bool high = wave != null && string.Equals((wave.waveType ?? "").Trim(), highTierWaveType, System.StringComparison.OrdinalIgnoreCase);
        int minTier = high ? Mathf.Max(0, RarityIndex(highTierMinRarity)) : 0;
        string sub = high ? $"{waveNumberInStage}웨이브(보스) 클리어 · {highTierMinRarity} 이상"
                          : $"{waveNumberInStage}웨이브 클리어";
        Open("재능 선택", sub, minTier, waveNote);
        while (open) yield return null;
    }

    public static int RarityIndex(string rarity)
    {
        string r = (rarity ?? "").Trim();
        for (int i = 0; i < Rarities.Length; i++) if (Rarities[i] == r) return i;
        return 0; // 비어 있거나 모르는 값 = 일반
    }

    // 이 웨이브를 클리어하면 선택창(재능 또는 상점)이 뜨는지 (WaveManager가 '웨이브 종료' 문구를 얼마나 보여줄지 정할 때)
    //  지금은 모든 웨이브 뒤에 무언가 열림
    public bool WillOffer(WaveData wave, int waveNumberInStage) => true;

    // 엘리트 웨이브인지 (Info가 비어 있으면 fallbackWaveNumbers 번째 웨이브)
    private bool IsShopWave(WaveData wave, int waveNumberInStage)
    {
        string type = wave != null ? (wave.waveType ?? "").Trim() : "";
        if (!string.IsNullOrEmpty(type))
        {
            foreach (string t in shopWaveTypes) if (string.Equals(t, type, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        foreach (int n in fallbackWaveNumbers) if (n == waveNumberInStage) return true;
        return false;
    }

    // 고른 재능 말고 선택지에 있던 재능은 그 판에서 다시 나오지 않음 (chosen = null: 건너뛰기, 모두)
    private void PassOver(AugmentData chosen)
    {
        foreach (Row r in rows) if (r.data != null && r.data != chosen) RunProgress.PassTalent(r.data.code);
    }

    // ===== 선택 창 =====
    // title = 큰 제목, eyebrow = 제목 위의 짧은 줄 (예: '2웨이브 클리어'), note = 안내 (웨이브 보너스 등)
    public void Open(string title, string eyebrow, int minTier, string note = null)
    {
        if (open) return;
        player = FindPlayer();
        if (player == null) { Debug.LogWarning("[재능] 플레이어를 찾을 수 없어 재능 선택을 건너뜁니다."); return; }

        List<AugmentData> choices = Roll(player, ChoiceCount, null, minTier);
        if (choices.Count == 0)
        {
            Toast(note ?? "더 얻을 수 있는 재능이 없습니다");
            return;
        }

        open = true;
        prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        prevLock = Cursor.lockState; prevCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        EnsureEventSystem();

        listTitle = title;
        listEyebrow = eyebrow;
        eyebrowText.text = eyebrow;
        titleText.text = title;
        noteText.text = note ?? "";
        noteText.gameObject.SetActive(!string.IsNullOrEmpty(note));
        ownedText.text = OwnedSummary(player);
        hintText.text = HintList;

        foreach (Row r in rows) if (r.rt != null) Destroy(r.rt.gameObject);
        rows.Clear();
        for (int i = 0; i < choices.Count; i++) rows.Add(MakeRow(choices[i], i));
        pendingSkill = null;
        listRoot.gameObject.SetActive(true);
        keyGrid.gameObject.SetActive(false);

        closing = false;
        chosenRow = null;
        pendingToast = null;
        openedAt = Time.unscaledTime;
        Select(0);
        root.gameObject.SetActive(true);
        Animate();
    }

    private void Select(int i)
    {
        if (rows.Count == 0) return;
        selected = Mathf.Clamp(i, 0, rows.Count - 1);
        for (int k = 0; k < rows.Count; k++) StyleRow(rows[k], k == selected);
        ShowDetail(rows[selected].data);
    }

    private void Choose(int i)
    {
        if (!open || closing || player == null || pendingSkill != null || i < 0 || i >= rows.Count) return;
        Select(i);
        AugmentData a = rows[i].data;
        if (PlayerSkills.IsSkillEffect(a.effect) && player.Skills != null && player.Skills.HasFreeSlot)
        {
            ShowKeySelect(a);
            return;
        }
        string got = player.Acquire(a);
        PassOver(a);
        BeginClose(rows[i], string.IsNullOrEmpty(got) ? $"'{a.name}' 획득" : $"'{a.name}' 획득\n{got}");
    }

    // ===== 연출 =====
    private static float EaseOutCubic(float t) { t = 1f - t; return 1f - t * t * t; }
    private static float EaseInCubic(float t) => t * t * t;
    private static float EaseOutBack(float t) { const float c1 = 1.4f, c3 = c1 + 1f; t -= 1f; return 1f + c3 * t * t * t + c1 * t * t; }

    private void SetDim(float a) { if (dim != null) { Color c = dim.color; c.a = a; dim.color = c; } }

    private static float RowY(int i) => -i * (RowH + RowGap);

    private void Animate()
    {
        float now = Time.unscaledTime;
        if (!closing)
        {
            float t = now - openedAt;
            // 왼쪽 판이 밀려 들어오고, 줄이 위에서부터 차례로 나타남, 상세는 오른쪽에서 살짝 밀려 나타남
            float u = EaseOutCubic(Mathf.Clamp01(t / Mathf.Max(0.05f, slideDuration)));
            panel.anchoredPosition = new Vector2(Mathf.Lerp(-PanelW - 16f, 0f, u), 0f);
            SetDim(DimAlpha * Mathf.Clamp01(t / 0.2f));
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                if (r.rt == null) continue;
                float ru = EaseOutCubic(Mathf.Clamp01((t - 0.12f - i * rowStagger) / 0.25f));
                r.rt.anchoredPosition = new Vector2(Mathf.Lerp(-48f, 0f, ru), RowY(i));
                r.group.alpha = ru;
            }
            float du = EaseOutCubic(Mathf.Clamp01((t - 0.15f) / 0.3f));
            detail.anchoredPosition = new Vector2(DetailX + Mathf.Lerp(40f, 0f, du), -DetailY);
            detailGroup.alpha = du;
            if (keyGrid.gameObject.activeSelf)
            {
                float k = Mathf.Clamp01((now - keyShownAt) / 0.25f);
                keyGrid.localScale = Vector3.one * Mathf.LerpUnclamped(0.92f, 1f, EaseOutBack(k));
            }
            return;
        }

        // 닫기: 고른 줄이 한 번 툭 튀고 → 상세가 사라지며 → 판이 왼쪽으로 빠짐
        float c = Mathf.Clamp01((now - closeAt) / CloseDuration);
        if (chosenRow != null && chosenRow.rt != null)
        {
            float pop = Mathf.Sin(Mathf.Clamp01(c / 0.5f) * Mathf.PI) * 0.04f;
            chosenRow.rt.localScale = Vector3.one * (1f + pop);
        }
        float o = EaseInCubic(Mathf.Clamp01((c - 0.2f) / 0.8f));
        panel.anchoredPosition = new Vector2(Mathf.Lerp(0f, -PanelW - 16f, o), 0f);
        detailGroup.alpha = 1f - Mathf.Clamp01(c / 0.5f);
        SetDim(DimAlpha * (1f - c));
        if (c >= 1f)
        {
            string msg = pendingToast;
            Close();
            if (!string.IsNullOrEmpty(msg)) Toast(msg);
        }
    }

    private void BeginClose(Row chosen, string toast)
    {
        closing = true;
        closeAt = Time.unscaledTime;
        chosenRow = chosen;
        pendingToast = toast;
    }

    // ===== 스킬 키 선택 =====
    private void ShowKeySelect(AugmentData a)
    {
        pendingSkill = a;
        listRoot.gameObject.SetActive(false);
        eyebrowText.text = $"'{a.name}'을(를) 쓸 키";
        titleText.text = "스킬 키 선택";
        noteText.text = "";
        noteText.gameObject.SetActive(false);
        hintText.text = HintKeys;

        foreach (GameObject k in keyBoxes) Destroy(k);
        keyBoxes.Clear();
        // 키보드 배치 순서: Q  E / Shift  F
        int[] order = { 1, 2, 0, 3 };
        for (int i = 0; i < order.Length; i++) keyBoxes.Add(MakeKeyBox(order[i], i % 2, i / 2));
        keyGrid.gameObject.SetActive(true);
        keyShownAt = Time.unscaledTime;
        keyGrid.localScale = Vector3.one * 0.92f;
    }

    private void BackToList()
    {
        pendingSkill = null;
        keyGrid.gameObject.SetActive(false);
        listRoot.gameObject.SetActive(true);
        titleText.text = listTitle;
        eyebrowText.text = listEyebrow;
        noteText.gameObject.SetActive(false);
        hintText.text = HintList;
    }

    private void PickSkillKey(int slot)
    {
        if (pendingSkill == null || player == null) return;
        if (player.Skills == null || !player.Skills.IsSlotFree(slot))
        {
            PlayerSkills.Skill s = player.Skills != null ? player.Skills.GetSlot(slot) : null;
            noteText.text = s != null ? $"{PlayerSkills.SlotKeyNames[slot]} 키에는 이미 '{s.data.name}'이(가) 있어요" : "빈 키를 골라주세요";
            noteText.gameObject.SetActive(true);
            return;
        }
        AugmentData a = pendingSkill;
        pendingSkill = null;
        player.Acquire(a, slot);
        PassOver(a);
        BeginClose(rows.Count > selected ? rows[selected] : null, $"'{a.name}' 획득\n{PlayerSkills.SlotKeyNames[slot]} 키로 사용");
    }

    private GameObject MakeKeyBox(int slot, int col, int row)
    {
        PlayerSkills.Skill owned = player != null && player.Skills != null ? player.Skills.GetSlot(slot) : null;
        bool free = owned == null;
        const float w = 336f, h = 156f, gap = 24f;
        RectTransform box = NewRect("Key_" + PlayerSkills.SlotKeyNames[slot], keyGrid);
        TL(box, col * (w + gap), row * (h + gap), w, h);
        Image frame = box.gameObject.AddComponent<Image>();
        frame.color = free ? Accent : Line;
        RectTransform inRt = Inset(NewRect("Inner", box), 4f);
        Image inner = inRt.gameObject.AddComponent<Image>();
        inner.color = free ? RowBg : DisabledBg;
        inner.raycastTarget = false;
        if (free)
        {
            var button = box.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            button.onClick.AddListener(() => PickSkillKey(slot));
        }
        Text key = NewText(box, "Key", 48, FontStyle.Bold, TextAnchor.MiddleCenter, free ? TextMain : Muted);
        TopPlace(key.rectTransform, 24f, 60f);
        key.text = PlayerSkills.SlotKeyNames[slot];
        Text sub = NewText(box, "Owned", 24, FontStyle.Normal, TextAnchor.MiddleCenter, free ? TextSub : Muted);
        TopPlace(sub.rectTransform, 96f, 36f);
        sub.text = free ? "비어 있음" : owned.data.name;
        return box.gameObject;
    }

    private void Close()
    {
        open = false;
        closing = false;
        pendingSkill = null;
        ClosedFrame = Time.frameCount;
        root.gameObject.SetActive(false);
        Time.timeScale = prevTimeScale;
        Cursor.lockState = prevLock; Cursor.visible = prevCursorVisible;
    }

    // ===== 뽑기 =====
    // v91: 희귀도를 먼저 뽑고, 그 희귀도 안에서 재능을 고르게 뽑음 (재능 수가 많은 등급이 확률을 독차지하지 않게)
    //  일반 55 / 고급 30 / 희귀 12 / 영웅 3 에서 시작해 웨이브마다 일반 → 희귀 +1.5%p, 영웅 +0.5%p
    //  보스 뒤(minTier > 0): 희귀 75 / 영웅 25. 뽑힌 희귀도에 남은 재능이 없으면 다른 희귀도에서
    private List<AugmentData> Roll(PlayerAugments pa, int count, AugmentData exclude, int minTier)
    {
        var byTier = new List<AugmentData>[Rarities.Length];
        for (int t = 0; t < byTier.Length; t++) byTier[t] = new List<AugmentData>();
        foreach (AugmentData a in AugmentDatabase.All)
        {
            if (a == exclude || !IsEligible(pa, a) || Weight(a) <= 0f) continue;
            byTier[Mathf.Clamp(RarityIndex(a.rarity), 0, byTier.Length - 1)].Add(a);
        }
        float[] w = TierWeights(minTier);
        var result = new List<AugmentData>();
        while (result.Count < count)
        {
            float total = 0f;
            for (int t = 0; t < w.Length; t++) if (byTier[t].Count > 0) total += w[t];
            int tier = -1;
            if (total > 0f)
            {
                float r = Random.value * total;
                for (int t = 0; t < w.Length; t++)
                {
                    if (byTier[t].Count == 0 || w[t] <= 0f) continue;
                    r -= w[t];
                    if (r <= 0f) { tier = t; break; }
                }
                if (tier < 0) for (int t = w.Length - 1; t >= 0; t--) if (byTier[t].Count > 0 && w[t] > 0f) { tier = t; break; }
            }
            if (tier < 0)
            {
                // 확률이 있는 등급이 다 떨어짐 → 높은 등급부터 남은 것으로 채움
                for (int t = byTier.Length - 1; t >= 0; t--) if (byTier[t].Count > 0) { tier = t; break; }
                if (tier < 0) break;
            }
            List<AugmentData> pool = byTier[tier];
            AugmentData pick = pool[Random.Range(0, pool.Count)];
            result.Add(pick);
            pool.Remove(pick);
        }
        return result;
    }

    // 지금 웨이브의 희귀도별 확률
    private float[] TierWeights(int minTier)
    {
        float[] w = new float[Rarities.Length];
        if (minTier > 0 && highTierWeights != null)
        {
            for (int t = 0; t < w.Length && t < highTierWeights.Length; t++) w[t] = t >= minTier ? Mathf.Max(0f, highTierWeights[t]) : 0f;
            return w;
        }
        for (int t = 0; t < w.Length && rarityWeights != null && t < rarityWeights.Length; t++) w[t] = Mathf.Max(0f, rarityWeights[t]);
        float steps = Mathf.Max(0, rollWave - 1);
        float rare = rarePerWave * steps, epic = epicPerWave * steps;
        float moved = Mathf.Min(w[0], rare + epic);
        float k = rare + epic > 0f ? moved / (rare + epic) : 0f;
        w[0] -= moved; w[2] += rare * k; w[3] += epic * k;
        return w;
    }

    // (예전 방식) 재능마다 희귀도 가중치 — '무작위' 재능에서만 씀
    private List<AugmentData> RollPerItem(PlayerAugments pa, int count, AugmentData exclude, int minTier)
    {
        var high = new List<AugmentData>();
        var low = new List<AugmentData>();
        foreach (AugmentData a in AugmentDatabase.All)
        {
            if (a == exclude || !IsEligible(pa, a) || Weight(a) <= 0f) continue;
            (RarityIndex(a.rarity) >= minTier ? high : low).Add(a);
        }
        var result = new List<AugmentData>();
        foreach (List<AugmentData> pool in new[] { high, low })
        {
            while (result.Count < count && pool.Count > 0)
            {
                AugmentData pick = WeightedPick(pool);
                result.Add(pick);
                pool.Remove(pick);
            }
        }
        return result;
    }

    private static bool IsEligible(PlayerAugments pa, AugmentData a)
    {
        if (!a.enabled) return false;
        if (RunProgress.IsTalentPassed(a.code)) return false; // 전에 고르지 않은 재능
        if (pa.StackOf(a.code) >= a.maxStacks) return false;
        if (!string.IsNullOrEmpty(a.prerequisite) && !HasAnyPrerequisite(pa, a.prerequisite)) return false;
        if (PlayerSkills.IsSkillEffect(a.effect) && (pa.Skills == null || !pa.Skills.HasFreeSlot)) return false; // 빈 스킬 칸이 없으면 안 나옴
        if (a.effect == PlayerAugments.SemiBoost && !pa.CanTakeSemiBoost()) return false; // 들고 있는 무기가 대상일 때만
        if (a.effect == PlayerAugments.GiveWeaponFx && !pa.CanTakeWeaponTalent(a)) return false; // v113: 그 무기가 없을 때만
        return true;
    }

    // 선행 재능: 'A036/A040' 처럼 여러 개를 적으면 그중 하나만 있어도 됨
    private static bool HasAnyPrerequisite(PlayerAugments pa, string prerequisite)
    {
        foreach (string c in prerequisite.Split('/', '|', ','))
        {
            string code = c.Trim();
            if (code.Length > 0 && pa.StackOf(code) > 0) return true;
        }
        return false;
    }

    private AugmentData WeightedPick(List<AugmentData> pool)
    {
        float total = 0f;
        foreach (AugmentData a in pool) total += Weight(a);
        float r = Random.value * total;
        foreach (AugmentData a in pool) { r -= Weight(a); if (r <= 0f) return a; }
        return pool[pool.Count - 1];
    }

    private float Weight(AugmentData a)
    {
        int i = RarityIndex(a.rarity);
        return rarityWeights != null && i < rarityWeights.Length ? Mathf.Max(0f, rarityWeights[i]) : 0f;
    }

    // '무작위' 재능용: 정상화·무작위를 제외한 재능 중 하나
    public static AugmentData PickRandomFor(PlayerAugments pa, AugmentData self)
    {
        var pool = new List<AugmentData>();
        foreach (AugmentData a in AugmentDatabase.All)
            if (a != self && a.effect != PlayerAugments.Normalize && a.effect != PlayerAugments.RandomFx && IsEligible(pa, a)
                && (Instance == null || Instance.Weight(a) > 0f)) pool.Add(a);
        if (pool.Count == 0) return null;
        return Instance != null ? Instance.WeightedPick(pool) : pool[Random.Range(0, pool.Count)];
    }

    private static PlayerAugments FindPlayer()
    {
        PlayerController pc = FindFirstObjectByType<PlayerController>();
        if (pc == null) return null;
        PlayerAugments pa = pc.GetComponent<PlayerAugments>();
        if (pa == null) pa = pc.gameObject.AddComponent<PlayerAugments>();
        return pa;
    }

    private static string OwnedSummary(PlayerAugments pa)
    {
        if (pa.Acquired.Count == 0) return "보유 재능: 없음";
        var counts = new Dictionary<string, int>();
        var order = new List<AugmentData>();
        foreach (AugmentData a in pa.Acquired)
        {
            if (!counts.ContainsKey(a.code)) { counts[a.code] = 0; order.Add(a); }
            counts[a.code]++;
        }
        var sb = new StringBuilder("보유 재능: ");
        for (int i = 0; i < order.Count; i++)
        {
            if (i > 0) sb.Append("   ");
            sb.Append(order[i].name);
            if (counts[order[i].code] > 1) sb.Append(" ×").Append(counts[order[i].code]);
        }
        return sb.ToString();
    }

    // ===== 짧은 알림 =====
    public static void Toast(string message)
    {
        AugmentManager m = EnsureExists();
        m.toastText.text = message;
        m.toastText.gameObject.SetActive(true);
        m.toastUntil = Time.unscaledTime + 3f;
    }

    // ===== UI =====
    // 테마: 바탕 #3A3A3A, 강조 #C6E7A7, 글씨 #EDEDE6 (Galmuri11)
    private static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out Color c); return c; }
    private static readonly Color PanelBg = Hex("#3A3A3A");
    private static readonly Color RowBg = Hex("#333331");
    private static readonly Color Line = Hex("#4C4C4A");
    private static readonly Color LineStrong = Hex("#6A6A66");
    private static readonly Color DisabledBg = Hex("#2E2E2C");
    private static readonly Color Accent = Hex("#C6E7A7");
    private static readonly Color Dark = Hex("#232323");
    private static readonly Color DarkSub = Hex("#3B4A30");
    private static readonly Color TextMain = Hex("#EDEDE6");
    private static readonly Color TextSub = Hex("#A8A8A0");
    private static readonly Color Muted = Hex("#8A8A84");
    private static readonly Color Gold = Hex("#E8C27A");

    public static Color RarityColor(string rarity)
    {
        switch (RarityIndex(rarity))
        {
            case 1: return Hex("#7EC4CF"); // 고급
            case 2: return Hex("#8FA8F0"); // 희귀
            case 3: return Hex("#C49AF0"); // 영웅
            case 4: return Hex("#F0C46A"); // 전설
            case 5: return Hex("#F08A7A"); // 신화
            default: return Hex("#B5B5AD"); // 일반
        }
    }

    private void BuildUI()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        root = NewRect("AugmentSelect", transform);
        Stretch(root);
        dim = root.gameObject.AddComponent<Image>();
        dim.color = new Color(0.08f, 0.08f, 0.08f, DimAlpha);

        // ── 왼쪽 판 ──
        panel = NewRect("Panel", root);
        panel.anchorMin = new Vector2(0f, 0f); panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.sizeDelta = new Vector2(PanelW, 0f);
        panel.gameObject.AddComponent<Image>().color = PanelBg;
        RectTransform edge = NewRect("Edge", panel);           // 오른쪽 가장자리 강조선
        edge.anchorMin = new Vector2(1f, 0f); edge.anchorMax = new Vector2(1f, 1f);
        edge.pivot = new Vector2(0f, 0.5f); edge.sizeDelta = new Vector2(4f, 0f); edge.anchoredPosition = Vector2.zero;
        edge.gameObject.AddComponent<Image>().color = Accent;
        RectTransform shade = NewRect("Shade", panel);         // 그 바깥 그림자 띠
        shade.anchorMin = new Vector2(1f, 0f); shade.anchorMax = new Vector2(1f, 1f);
        shade.pivot = new Vector2(0f, 0.5f); shade.sizeDelta = new Vector2(8f, 0f); shade.anchoredPosition = new Vector2(4f, 0f);
        shade.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

        eyebrowText = NewText(panel, "Eyebrow", 24, FontStyle.Normal, TextAnchor.MiddleLeft, Accent);
        TL(eyebrowText.rectTransform, PadX, 96f, ListW, 36f);
        titleText = NewText(panel, "Title", 48, FontStyle.Normal, TextAnchor.MiddleLeft, TextMain);
        TL(titleText.rectTransform, PadX, 136f, ListW, 64f);
        noteText = NewText(panel, "Note", 24, FontStyle.Normal, TextAnchor.MiddleLeft, Gold);
        TL(noteText.rectTransform, PadX, 208f, ListW, 36f);

        listRoot = NewRect("List", panel);
        TL(listRoot, PadX, 264f, ListW, 3 * RowH + 2 * RowGap);
        keyGrid = NewRect("SkillKeys", panel);
        TL(keyGrid, PadX, 264f, ListW, 336f);
        keyGrid.pivot = new Vector2(0.5f, 1f);
        keyGrid.anchoredPosition = new Vector2(PadX + ListW * 0.5f, -264f);
        keyGrid.gameObject.SetActive(false);

        ownedText = NewText(panel, "Owned", 24, FontStyle.Normal, TextAnchor.LowerLeft, TextSub);
        BL(ownedText.rectTransform, PadX, 124f, ListW, 72f);
        hintText = NewText(panel, "Hint", 24, FontStyle.Normal, TextAnchor.MiddleLeft, TextMain);
        BL(hintText.rectTransform, PadX, 72f, ListW, 36f);

        // ── 오른쪽 상세 ──
        detail = NewRect("Detail", root);
        detail.anchorMin = detail.anchorMax = new Vector2(0f, 1f);
        detail.pivot = new Vector2(0f, 1f);
        detail.sizeDelta = new Vector2(DetailW, DetailH);
        detail.anchoredPosition = new Vector2(DetailX, -DetailY);
        detail.gameObject.AddComponent<Image>().color = LineStrong;
        detailGroup = detail.gameObject.AddComponent<CanvasGroup>();
        detailGroup.blocksRaycasts = false;
        RectTransform dIn = Inset(NewRect("Inner", detail), 4f);
        dIn.gameObject.AddComponent<Image>().color = new Color(0.165f, 0.165f, 0.157f, 0.95f);

        RectTransform icon = NewRect("Icon", detail);
        TL(icon, 48f, 48f, 192f, 192f);
        icon.gameObject.AddComponent<Image>().color = Line;
        Inset(NewRect("Inner", icon), 4f).gameObject.AddComponent<Image>().color = Hex("#2C2C2C");
        detailIconLabel = NewText(icon, "Label", 12, FontStyle.Normal, TextAnchor.MiddleCenter, Muted);
        Stretch(detailIconLabel.rectTransform);
        detailIconLabel.text = "아이콘";
        detailIcon = Inset(NewRect("Picture", icon), 16f).gameObject.AddComponent<Image>();
        detailIcon.preserveAspect = true;
        detailIcon.raycastTarget = false;
        detailIcon.enabled = false;

        RectTransform tag = NewRect("Tag", detail);
        TL(tag, 276f, 48f, 200f, 40f);
        detailTagBg = tag.gameObject.AddComponent<Image>();
        detailTag = NewText(tag, "Text", 24, FontStyle.Bold, TextAnchor.MiddleCenter, Dark);
        Stretch(detailTag.rectTransform);
        detailName = NewText(detail, "Name", 48, FontStyle.Normal, TextAnchor.MiddleLeft, TextMain);
        TL(detailName.rectTransform, 276f, 100f, 492f, 64f);
        detailDesc = NewText(detail, "Desc", 24, FontStyle.Normal, TextAnchor.UpperLeft, TextMain);
        TL(detailDesc.rectTransform, 276f, 176f, 492f, 144f);
        detailDesc.lineSpacing = 1.4f;

        RectTransform divider = NewRect("Divider", detail);
        TL(divider, 48f, 336f, DetailW - 96f, 4f);
        divider.gameObject.AddComponent<Image>().color = Hex("#555552");
        detailLabels = NewText(detail, "Labels", 24, FontStyle.Normal, TextAnchor.UpperLeft, TextSub);
        TL(detailLabels.rectTransform, 48f, 368f, 216f, 168f);
        detailLabels.lineSpacing = 1.5f;
        detailValues = NewText(detail, "Values", 24, FontStyle.Normal, TextAnchor.UpperLeft, TextMain);
        TL(detailValues.rectTransform, 288f, 368f, DetailW - 336f, 168f);
        detailValues.lineSpacing = 1.5f;

        root.gameObject.SetActive(false);

        toastText = NewText(transform, "Toast", 30, FontStyle.Bold, TextAnchor.MiddleCenter, Accent);
        toastText.rectTransform.anchorMin = toastText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        toastText.rectTransform.pivot = new Vector2(0.5f, 1f);
        toastText.rectTransform.anchoredPosition = new Vector2(0f, -150f);
        toastText.rectTransform.sizeDelta = new Vector2(1400f, 100f);
        var shadow = toastText.gameObject.AddComponent<Outline>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(2f, -2f);
        toastText.gameObject.SetActive(false);
    }

    // 목록 한 줄: [번호] 이름 / 짧은 설명 ........ [희귀도]
    private Row MakeRow(AugmentData a, int index)
    {
        var r = new Row { data = a };
        r.rt = NewRect("Row_" + a.code, listRoot);
        TL(r.rt, 0f, 0f, ListW, RowH);
        r.rt.anchoredPosition = new Vector2(0f, RowY(index));
        r.group = r.rt.gameObject.AddComponent<CanvasGroup>();
        r.frame = r.rt.gameObject.AddComponent<Image>();
        r.inner = Inset(NewRect("Inner", r.rt), 4f).gameObject.AddComponent<Image>();
        r.inner.raycastTarget = false;

        var hover = r.rt.gameObject.AddComponent<GameHUDHoverRelay>();
        hover.onEnter = () => { if (!closing && pendingSkill == null) Select(index); };
        var button = r.rt.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Choose(index));

        RectTransform keyBox = NewRect("Key", r.rt);
        keyBox.anchorMin = keyBox.anchorMax = new Vector2(0f, 0.5f);
        keyBox.pivot = new Vector2(0f, 0.5f);
        keyBox.sizeDelta = new Vector2(48f, 48f);
        keyBox.anchoredPosition = new Vector2(24f, 0f);
        r.keyFrame = keyBox.gameObject.AddComponent<Image>();
        r.keyFrame.raycastTarget = false;
        r.keyInner = Inset(NewRect("Inner", keyBox), 4f).gameObject.AddComponent<Image>();
        r.keyInner.raycastTarget = false;
        r.key = NewText(keyBox, "Num", 24, FontStyle.Normal, TextAnchor.MiddleCenter, TextMain);
        Stretch(r.key.rectTransform);
        r.key.text = (index + 1).ToString();

        r.title = NewText(r.rt, "Name", 36, FontStyle.Bold, TextAnchor.MiddleLeft, TextMain);
        LeftMid(r.title.rectTransform, 96f, 22f, 440f, 48f);
        r.title.horizontalOverflow = HorizontalWrapMode.Overflow;
        r.title.text = a.name;
        r.desc = NewText(r.rt, "Desc", 24, FontStyle.Normal, TextAnchor.MiddleLeft, TextSub);
        LeftMid(r.desc.rectTransform, 96f, -24f, 440f, 36f);
        r.desc.verticalOverflow = VerticalWrapMode.Truncate;
        r.desc.text = ShortDesc(a);

        RectTransform tagRt = NewRect("Rarity", r.rt);
        tagRt.anchorMin = tagRt.anchorMax = new Vector2(1f, 0.5f);
        tagRt.pivot = new Vector2(1f, 0.5f);
        tagRt.anchoredPosition = new Vector2(-24f, 0f);
        r.tagBg = tagRt.gameObject.AddComponent<Image>();
        r.tagBg.raycastTarget = false;
        r.tag = NewText(tagRt, "Text", 24, FontStyle.Bold, TextAnchor.MiddleCenter, Dark);
        Stretch(r.tag.rectTransform);
        r.tag.text = string.IsNullOrEmpty(a.rarity) ? "일반" : a.rarity;
        tagRt.sizeDelta = new Vector2(r.tag.preferredWidth + 24f, 40f);

        r.group.alpha = 0f;
        return r;
    }

    // 고른 줄은 강조색으로 뒤집힘
    private static void StyleRow(Row r, bool on)
    {
        Color rc = RarityColor(r.data.rarity);
        r.frame.color = on ? Accent : Line;
        r.inner.color = on ? Accent : RowBg;
        r.keyFrame.color = on ? Dark : LineStrong;
        r.keyInner.color = on ? Dark : RowBg;
        r.key.color = on ? Accent : TextMain;
        r.title.color = on ? Dark : TextMain;
        r.desc.color = on ? DarkSub : TextSub;
        r.tagBg.color = on ? Dark : rc;
        r.tag.color = on ? rc : Dark;
    }

    private void ShowDetail(AugmentData a)
    {
        bool skill = PlayerSkills.IsSkillEffect(a.effect);
        string rarity = string.IsNullOrEmpty(a.rarity) ? "일반" : a.rarity;
        detailTagBg.color = RarityColor(a.rarity);
        detailTag.text = rarity + " · " + (skill ? "스킬" : "재능");
        ((RectTransform)detailTagBg.transform).sizeDelta = new Vector2(detailTag.preferredWidth + 24f, 40f);
        detailName.text = a.name;
        detailDesc.text = a.description;
        Sprite pic = a.Icon;
        detailIcon.sprite = pic;
        detailIcon.enabled = pic != null;
        detailIconLabel.enabled = pic == null;

        var labels = new StringBuilder();
        var values = new StringBuilder();
        void Add(string l, string v) { if (labels.Length > 0) { labels.Append('\n'); values.Append('\n'); } labels.Append(l); values.Append(v); }
        if (a.cooldown > 0f) Add("재사용 대기", $"{a.cooldown:0.#}초");
        if (skill) Add("사용 키", "고르면 Shift·Q·E·F 중 지정");
        int owned = player != null ? player.StackOf(a.code) : 0;
        Add("중복", a.maxStacks > 1 ? $"보유 {owned} / 최대 {a.maxStacks}" : "불가");
        if (!string.IsNullOrEmpty(a.prerequisite)) Add("필요 재능", PrerequisiteNames(a.prerequisite));
        detailLabels.text = labels.ToString();
        detailValues.text = values.ToString();
    }

    private static string PrerequisiteNames(string prerequisite)
    {
        var names = new List<string>();
        foreach (string c in prerequisite.Split('/', '|', ','))
        {
            AugmentData p = AugmentDatabase.Get(c.Trim());
            if (p != null) names.Add(p.name);
        }
        return names.Count > 0 ? string.Join(" 또는 ", names) : prerequisite;
    }

    // 목록에 쓰는 한 줄 설명: 설명의 첫 문장
    private static string ShortDesc(AugmentData a)
    {
        string d = (a.description ?? "").Replace('\n', ' ').Trim();
        int cut = -1;
        foreach (string end in new[] { ". ", "! ", "? " })
        {
            int i = d.IndexOf(end, System.StringComparison.Ordinal);
            if (i >= 0 && (cut < 0 || i < cut)) cut = i;
        }
        return cut > 0 ? d.Substring(0, cut + 1) : d;
    }

    // 왼쪽 위 기준 배치 (x, y = 부모 왼쪽 위에서 오른쪽·아래로)
    private static void TL(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
        r.pivot = new Vector2(0f, 1f);
        r.sizeDelta = new Vector2(w, h);
        r.anchoredPosition = new Vector2(x, -y);
    }

    // 왼쪽 아래 기준 배치
    private static void BL(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 0f);
        r.pivot = new Vector2(0f, 0f);
        r.sizeDelta = new Vector2(w, h);
        r.anchoredPosition = new Vector2(x, y);
    }

    // 왼쪽 가운데 기준 배치 (y = 가운데에서 위로)
    private static void LeftMid(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
        r.pivot = new Vector2(0f, 0.5f);
        r.sizeDelta = new Vector2(w, h);
        r.anchoredPosition = new Vector2(x, y);
    }

    // 부모를 꽉 채우되 사방으로 d만큼 안쪽 (도트 테두리용)
    private static RectTransform Inset(RectTransform r, float d)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(d, d); r.offsetMax = new Vector2(-d, -d);
        return r;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
        es.AddComponent<StandaloneInputModule>();
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform r, Vector2 pos, Vector2 size)
    {
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
    }

    // 카드 위쪽에서 top 만큼 내려온 곳에 높이 height, 좌우 여백 24
    private static void TopPlace(RectTransform r, float top, float height)
    {
        r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
        r.pivot = new Vector2(0.5f, 1f);
        r.sizeDelta = new Vector2(-48f, height);
        r.anchoredPosition = new Vector2(0f, -top);
    }

    private Text NewText(Transform parent, string name, int size, FontStyle style, TextAnchor anchor, Color color)
    {
        RectTransform r = NewRect(name, parent);
        var t = r.gameObject.AddComponent<Text>();
        t.fontSize = size;
        UIFont.Apply(t, style);
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }
}
