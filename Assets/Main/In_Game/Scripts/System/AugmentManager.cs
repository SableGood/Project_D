using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;
using System.Text;

// 재능 선택 (웨이브 클리어 후 3개 중 1개 고르기)  ※ 코드 안의 Augment(증강) = 게임 용어 '재능'
//  - 재능 목록: Resources/Augment_Data.csv (구글 시트 Augment_Data 탭 → Tools/CSV 데이터 생성/0 으로 가져오기)
//  - 언제: Wave_Data의 Info가 Elite / Boss인 웨이브를 클리어했을 때. 보스 웨이브 뒤에는 높은 등급(희귀 이상)만 등장
//          Info를 아직 읽어오지 않았다면 스테이지 안에서 fallbackWaveNumbers 번째(기본 3, 6) 웨이브 클리어 시
//  - 선택하는 동안 게임 일시정지. F3 = 테스트로 바로 열기. testEveryWave = 모든 웨이브 뒤에 등장 (테스트용)
//  - 연출: 화면 위에서 카드 3장이 차례로 내려옴 → 마우스를 올린 카드는 살짝 들려 앞으로 당겨짐 → 고르면 고른 카드가 튀고 나머지는 사라지며 위로 올라감
//    (일시정지 중이라 모든 연출은 Time.unscaledTime 기준)
//  - 스킬 재능(은신·수류탄 등)을 고르면 "사용할 키를 눌러주세요" 단계로 넘어감 (Q / E / Shift / F 중 빈 칸)
//    빈 스킬 칸이 없으면 스킬 재능은 선택지에 나오지 않음
public class AugmentManager : MonoBehaviour
{
    public static AugmentManager Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;
    // 선택 창이 닫힌 프레임 (창에서 누른 키가 바로 스킬로 쓰이지 않게)
    public static int ClosedFrame { get; private set; } = -10;

    [Header("선택지")]
    public int choiceCount = 3;
    // 희귀도 (낮은 등급 → 높은 등급)
    public static readonly string[] Rarities = { "일반", "고급", "희귀", "영웅", "전설", "신화" };
    [Tooltip("희귀도별 등장 가중치 (일반 / 고급 / 희귀 / 영웅 / 전설 / 신화). 0이면 등장하지 않음")]
    public float[] rarityWeights = { 50f, 30f, 15f, 5f, 0f, 0f };

    [Header("등장 시점")]
    [Tooltip("이 종류(Wave_Data의 Info)의 웨이브를 클리어하면 재능 선택")]
    public string[] offerOnWaveTypes = { "Elite", "Boss" };
    [Tooltip("이 종류의 웨이브 뒤에는 높은 등급만 등장")]
    public string highTierWaveType = "Boss";
    [Tooltip("높은 등급 선택창의 최소 희귀도")]
    public string highTierMinRarity = "희귀";
    [Tooltip("웨이브 종류 정보가 없을 때: 스테이지 안에서 이 번째 웨이브를 클리어하면 재능 선택")]
    public int[] fallbackWaveNumbers = { 3, 6 };

    [Header("테스트")]
    public bool debugKey = true; // F3
    [Tooltip("테스트: 모든 웨이브를 클리어할 때마다 재능 선택 (끄면 Elite·Boss 웨이브 뒤에만)")]
    public bool testEveryWave = true;

    [Header("연출")]
    [Tooltip("화면 위에서 내려오는 시간 (초)")]
    public float dropDuration = 0.55f;
    [Tooltip("카드가 차례로 내려오는 간격 (초)")]
    public float cardStagger = 0.08f;
    [Tooltip("마우스를 올린 카드: 커지는 배율 / 위로 들리는 거리(px)")]
    public float hoverScale = 1.08f;
    public float hoverLift = 24f;

    private RectTransform content;
    private Image dim;
    private const float DimAlpha = 0.72f, CloseDuration = 0.4f, OffscreenY = 1150f;
    private class CardFx
    {
        public RectTransform inner, shadow;
        public Image shadowImage;
        public CanvasGroup group;
        public float hover;
        public bool hovered;
        public int index;
    }
    private readonly List<CardFx> cardFx = new List<CardFx>();
    private float openedAt, closeAt, keyShownAt;
    private bool closing;
    private CardFx chosenFx;
    private string pendingToast;

    private bool open;
    private PlayerAugments player;
    private Canvas canvas;
    private RectTransform root, cardRow;
    private Text titleText, subtitleText, noteText, ownedText, toastText;
    private readonly List<GameObject> cards = new List<GameObject>();
    // 스킬 키 선택 단계
    private AugmentData pendingSkill;
    private RectTransform keyRow;
    private readonly List<GameObject> keyBoxes = new List<GameObject>();
    private float toastUntil;
    private float prevTimeScale = 1f;
    private CursorLockMode prevLock;
    private bool prevCursorVisible;
    private Font font;

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
        if (debugKey && !open && Input.GetKeyDown(KeyCode.F3)) Open("재능 선택 (테스트)", "F3으로 연 테스트 선택창입니다", 0);
        if (open) Animate();
        if (open && !closing && pendingSkill != null)
        {
            int slot = PlayerSkills.SlotFromKeyDown();
            if (slot >= 0) PickSkillKey(slot);
        }
        if (toastText != null && toastText.gameObject.activeSelf && Time.unscaledTime > toastUntil) toastText.gameObject.SetActive(false);
    }

    // ===== 웨이브 클리어 후 (WaveManager가 호출) =====
    public IEnumerator OfferAfterWave(WaveData wave, int waveNumberInStage)
    {
        PlayerAugments pa = FindPlayer();
        string waveNote = pa != null ? pa.OnWaveCleared() : null;
        if (!ShouldOffer(wave, waveNumberInStage))
        {
            if (!string.IsNullOrEmpty(waveNote)) Toast(waveNote);
            yield break;
        }
        bool high = wave != null && string.Equals((wave.waveType ?? "").Trim(), highTierWaveType, System.StringComparison.OrdinalIgnoreCase);
        int minTier = high ? Mathf.Max(0, RarityIndex(highTierMinRarity)) : 0;
        string sub = high ? $"{waveNumberInStage}웨이브(보스) 클리어 · {highTierMinRarity} 이상 재능 중 하나를 고르세요"
                          : $"{waveNumberInStage}웨이브 클리어 · 하나를 고르세요";
        Open("재능 선택", sub, minTier, waveNote);
        while (open) yield return null;
    }

    public static int RarityIndex(string rarity)
    {
        string r = (rarity ?? "").Trim();
        for (int i = 0; i < Rarities.Length; i++) if (Rarities[i] == r) return i;
        return 0; // 비어 있거나 모르는 값 = 일반
    }

    // 이 웨이브를 클리어하면 재능 선택창이 뜨는지 (WaveManager가 '웨이브 종료' 문구를 얼마나 보여줄지 정할 때)
    public bool WillOffer(WaveData wave, int waveNumberInStage) => ShouldOffer(wave, waveNumberInStage);

    private bool ShouldOffer(WaveData wave, int waveNumberInStage)
    {
        if (testEveryWave) return true;
        string type = wave != null ? (wave.waveType ?? "").Trim() : "";
        if (!string.IsNullOrEmpty(type))
        {
            foreach (string t in offerOnWaveTypes) if (string.Equals(t, type, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        foreach (int n in fallbackWaveNumbers) if (n == waveNumberInStage) return true;
        return false;
    }

    // ===== 선택 창 =====
    public void Open(string title, string subtitle, int minTier, string note = null)
    {
        if (open) return;
        player = FindPlayer();
        if (player == null) { Debug.LogWarning("[재능] 플레이어를 찾을 수 없어 재능 선택을 건너뜁니다."); return; }

        List<AugmentData> choices = Roll(player, choiceCount, null, minTier);
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

        titleText.text = title;
        subtitleText.text = subtitle;
        noteText.text = note ?? "";
        noteText.gameObject.SetActive(!string.IsNullOrEmpty(note));
        ownedText.text = OwnedSummary(player);

        foreach (GameObject c in cards) Destroy(c);
        cards.Clear();
        cardFx.Clear();
        for (int i = 0; i < choices.Count; i++) cards.Add(MakeCard(choices[i], i));
        pendingSkill = null;
        cardRow.gameObject.SetActive(true);
        keyRow.gameObject.SetActive(false);

        // 연출 시작: 화면 위에서 내려옴
        closing = false;
        chosenFx = null;
        pendingToast = null;
        openedAt = Time.unscaledTime;
        content.anchoredPosition = new Vector2(0f, OffscreenY);
        SetDim(0f);
        root.gameObject.SetActive(true);
        Animate();
    }

    private void Choose(AugmentData a, CardFx fx)
    {
        if (!open || closing || player == null || pendingSkill != null) return;
        if (PlayerSkills.IsSkillEffect(a.effect) && player.Skills != null && player.Skills.HasFreeSlot)
        {
            ShowKeySelect(a);
            return;
        }
        string note = player.Acquire(a);
        BeginClose(fx, string.IsNullOrEmpty(note) ? $"'{a.name}' 획득" : $"'{a.name}' 획득\n{note}");
    }

    // ===== 연출 =====
    private static float EaseOutBack(float t) { const float c1 = 1.4f, c3 = c1 + 1f; t -= 1f; return 1f + c3 * t * t * t + c1 * t * t; }
    private static float EaseOutCubic(float t) { t = 1f - t; return 1f - t * t * t; }
    private static float EaseInCubic(float t) => t * t * t;

    private void SetDim(float a) { if (dim != null) { Color c = dim.color; c.a = a; dim.color = c; } }

    private void Animate()
    {
        float now = Time.unscaledTime;
        if (!closing)
        {
            float t = now - openedAt;
            // 판 전체가 위에서 내려옴 (끝에서 살짝 지나쳤다가 제자리)
            float u = Mathf.Clamp01(t / Mathf.Max(0.05f, dropDuration));
            content.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(OffscreenY, 0f, EaseOutBack(u)));
            SetDim(DimAlpha * Mathf.Clamp01(t / 0.25f));
            float dt = Time.unscaledDeltaTime;
            foreach (CardFx fx in cardFx)
            {
                if (fx.inner == null) continue;
                // 카드는 판보다 조금 늦게, 왼쪽부터 차례로 내려와 '착' 자리 잡음
                float cu = Mathf.Clamp01((t - 0.1f - fx.index * cardStagger) / 0.45f);
                float drop = Mathf.LerpUnclamped(260f, 0f, EaseOutBack(cu));
                bool canHover = cu >= 1f && pendingSkill == null;
                fx.hover = Mathf.MoveTowards(fx.hover, fx.hovered && canHover ? 1f : 0f, dt * 7f);
                float h = EaseOutCubic(fx.hover);
                PoseCard(fx, drop + h * hoverLift, 1f + (hoverScale - 1f) * h, h);
            }
            if (keyRow.gameObject.activeSelf)
            {
                float k = Mathf.Clamp01((now - keyShownAt) / 0.28f);
                keyRow.localScale = Vector3.one * Mathf.LerpUnclamped(0.8f, 1f, EaseOutBack(k));
            }
            return;
        }

        // 닫기: 고른 카드는 한 번 툭 튀고, 나머지는 아래로 가라앉으며 사라짐 → 판 전체가 위로 올라감
        float c = Mathf.Clamp01((now - closeAt) / CloseDuration);
        foreach (CardFx fx in cardFx)
        {
            if (fx.inner == null) continue;
            if (fx == chosenFx)
            {
                float pop = Mathf.Sin(Mathf.Clamp01(c / 0.45f) * Mathf.PI) * 0.1f;
                PoseCard(fx, hoverLift * fx.hover + pop * 100f, 1f + (hoverScale - 1f) * fx.hover + pop, fx.hover);
            }
            else
            {
                float f = Mathf.Clamp01(c / 0.4f);
                PoseCard(fx, -90f * EaseInCubic(f), 1f - 0.08f * f, 0f);
                fx.group.alpha = 1f - f;
            }
        }
        float up = Mathf.Clamp01((c - 0.35f) / 0.65f);
        content.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, OffscreenY, EaseInCubic(up)));
        SetDim(DimAlpha * (1f - c));
        if (c >= 1f)
        {
            string msg = pendingToast;
            Close();
            if (!string.IsNullOrEmpty(msg)) Toast(msg);
        }
    }

    // 카드 자세: y = 위로 들린 정도(px), 그림자는 들릴수록 멀어지고 진해짐
    private static void PoseCard(CardFx fx, float y, float scale, float lift)
    {
        fx.inner.anchoredPosition = new Vector2(0f, y);
        fx.inner.localScale = Vector3.one * scale;
        if (fx.shadow != null)
        {
            fx.shadow.anchoredPosition = new Vector2(6f + lift * 8f, y - 10f - lift * 18f);
            fx.shadow.localScale = Vector3.one * scale;
            Color sc = fx.shadowImage.color; sc.a = (0.35f + lift * 0.2f) * (fx.group != null ? fx.group.alpha : 1f); fx.shadowImage.color = sc;
        }
    }

    private void BeginClose(CardFx chosen, string toast)
    {
        closing = true;
        closeAt = Time.unscaledTime;
        chosenFx = chosen;
        pendingToast = toast;
        foreach (CardFx fx in cardFx) if (fx.group != null) fx.group.interactable = false;
    }

    // ===== 스킬 키 선택 =====
    private void ShowKeySelect(AugmentData a)
    {
        pendingSkill = a;
        cardRow.gameObject.SetActive(false);
        titleText.text = "스킬 키 선택";
        subtitleText.text = $"'{a.name}' — 사용할 키를 눌러주세요";
        noteText.text = "";
        noteText.gameObject.SetActive(false);

        foreach (GameObject k in keyBoxes) Destroy(k);
        keyBoxes.Clear();
        // 키보드 배치 순서로 보여줌: Q  E  Shift  F
        foreach (int slot in new[] { 1, 2, 0, 3 }) keyBoxes.Add(MakeKeyBox(slot));
        keyRow.gameObject.SetActive(true);
        keyShownAt = Time.unscaledTime;
        keyRow.localScale = Vector3.one * 0.8f;
    }

    private void PickSkillKey(int slot)
    {
        if (pendingSkill == null || player == null) return;
        if (player.Skills == null || !player.Skills.IsSlotFree(slot))
        {
            PlayerSkills.Skill s = player.Skills != null ? player.Skills.GetSlot(slot) : null;
            noteText.text = s != null ? $"{PlayerSkills.SlotKeyNames[slot]} 키에는 이미 '{s.data.name}'이(가) 있어요. 빈 키를 골라주세요" : "빈 키를 골라주세요";
            noteText.gameObject.SetActive(true);
            return;
        }
        AugmentData a = pendingSkill;
        pendingSkill = null;
        player.Acquire(a, slot);
        BeginClose(null, $"'{a.name}' 획득\n{PlayerSkills.SlotKeyNames[slot]} 키로 사용");
    }

    private GameObject MakeKeyBox(int slot)
    {
        PlayerSkills.Skill owned = player != null && player.Skills != null ? player.Skills.GetSlot(slot) : null;
        bool free = owned == null;
        RectTransform box = NewRect("Key_" + PlayerSkills.SlotKeyNames[slot], keyRow);
        box.sizeDelta = new Vector2(190f, 190f);
        var bg = box.gameObject.AddComponent<Image>();
        bg.color = free ? CardBg : new Color(0.07f, 0.07f, 0.08f, 0.9f);
        var outline = box.gameObject.AddComponent<Outline>();
        outline.effectColor = free ? new Color(1f, 0.85f, 0.35f) : new Color(0.35f, 0.35f, 0.38f);
        outline.effectDistance = new Vector2(3f, -3f);
        if (free)
        {
            var button = box.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(() => PickSkillKey(slot));
        }

        Text key = NewText(box, "Key", 52, FontStyle.Bold, TextAnchor.MiddleCenter, free ? Color.white : new Color(0.5f, 0.5f, 0.53f));
        TopPlace(key.rectTransform, 30f, 80f);
        key.text = PlayerSkills.SlotKeyNames[slot];
        Text sub = NewText(box, "Owned", 22, FontStyle.Normal, TextAnchor.MiddleCenter, free ? new Color(0.62f, 0.65f, 0.7f) : new Color(0.5f, 0.5f, 0.53f));
        TopPlace(sub.rectTransform, 120f, 50f);
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
    // minTier 이상 등급에서 먼저 뽑고, 모자라면 낮은 등급으로 채움
    private List<AugmentData> Roll(PlayerAugments pa, int count, AugmentData exclude, int minTier)
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
        if (pa.StackOf(a.code) >= a.maxStacks) return false;
        if (!string.IsNullOrEmpty(a.prerequisite) && !HasAnyPrerequisite(pa, a.prerequisite)) return false;
        if (PlayerSkills.IsSkillEffect(a.effect) && (pa.Skills == null || !pa.Skills.HasFreeSlot)) return false; // 빈 스킬 칸이 없으면 안 나옴
        if (a.effect == PlayerAugments.SemiBoost && !pa.CanTakeSemiBoost()) return false; // 들고 있는 무기가 대상일 때만
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
    private static readonly Color CardBg = new Color(0.11f, 0.12f, 0.15f, 0.97f);
    private static Color RarityColor(string rarity)
    {
        switch (RarityIndex(rarity))
        {
            case 1: return new Color(0.42f, 0.85f, 0.45f);  // 고급
            case 2: return new Color(0.32f, 0.64f, 1f);     // 희귀
            case 3: return new Color(0.74f, 0.45f, 1f);     // 영웅
            case 4: return new Color(1f, 0.66f, 0.2f);      // 전설
            case 5: return new Color(1f, 0.32f, 0.32f);     // 신화
            default: return new Color(0.70f, 0.74f, 0.80f); // 일반
        }
    }

    private void BuildUI()
    {
        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Arial" }, 32);
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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
        dim.color = new Color(0f, 0f, 0f, DimAlpha);
        // 내려오고 올라가는 판 (제목·카드·보유 목록)
        content = NewRect("Content", root);
        Stretch(content);

        titleText = NewText(content, "Title", 56, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Place(titleText.rectTransform, new Vector2(0f, 380f), new Vector2(1200f, 70f));
        subtitleText = NewText(content, "Subtitle", 26, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.8f, 0.82f, 0.86f));
        Place(subtitleText.rectTransform, new Vector2(0f, 320f), new Vector2(1200f, 40f));
        noteText = NewText(content, "Note", 26, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.35f));
        Place(noteText.rectTransform, new Vector2(0f, 276f), new Vector2(1400f, 40f));

        cardRow = NewRect("Cards", content);
        Place(cardRow, new Vector2(0f, -10f), new Vector2(1400f, 480f));
        var layout = cardRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 40f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false; layout.childControlHeight = false;
        layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;

        keyRow = NewRect("SkillKeys", content);
        Place(keyRow, new Vector2(0f, 20f), new Vector2(1000f, 220f));
        var keyLayout = keyRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        keyLayout.spacing = 36f;
        keyLayout.childAlignment = TextAnchor.MiddleCenter;
        keyLayout.childControlWidth = false; keyLayout.childControlHeight = false;
        keyLayout.childForceExpandWidth = false; keyLayout.childForceExpandHeight = false;
        keyRow.gameObject.SetActive(false);

        ownedText = NewText(content, "Owned", 22, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.75f, 0.77f, 0.82f));
        Place(ownedText.rectTransform, new Vector2(0f, -320f), new Vector2(1600f, 60f));
        ownedText.horizontalOverflow = HorizontalWrapMode.Wrap;

        root.gameObject.SetActive(false);

        toastText = NewText(transform, "Toast", 30, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.55f));
        toastText.rectTransform.anchorMin = toastText.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        toastText.rectTransform.pivot = new Vector2(0.5f, 1f);
        toastText.rectTransform.anchoredPosition = new Vector2(0f, -150f);
        toastText.rectTransform.sizeDelta = new Vector2(1400f, 100f);
        var shadow = toastText.gameObject.AddComponent<Outline>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(2f, -2f);
        toastText.gameObject.SetActive(false);
    }

    private GameObject MakeCard(AugmentData a, int index)
    {
        Color rc = RarityColor(a.rarity);
        // 자리(레이아웃이 위치를 정함) 안에 그림자 + 실제 카드. 연출은 카드와 그림자만 움직임
        RectTransform slot = NewRect("Card_" + a.code, cardRow);
        slot.sizeDelta = new Vector2(380f, 480f);
        RectTransform shadow = NewRect("Shadow", slot);
        Stretch(shadow);
        var shadowImage = shadow.gameObject.AddComponent<Image>();
        shadowImage.color = new Color(0f, 0f, 0f, 0.35f);
        shadowImage.raycastTarget = false;
        RectTransform card = NewRect("Card", slot);
        Stretch(card);
        var fx = new CardFx { inner = card, shadow = shadow, shadowImage = shadowImage, index = index };
        fx.group = card.gameObject.AddComponent<CanvasGroup>();
        cardFx.Add(fx);
        var hover = card.gameObject.AddComponent<GameHUDHoverRelay>();
        hover.onEnter = () => fx.hovered = true;
        hover.onExit = () => fx.hovered = false;
        var bg = card.gameObject.AddComponent<Image>();
        bg.color = CardBg;
        var outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = rc;
        outline.effectDistance = new Vector2(3f, -3f);

        var button = card.gameObject.AddComponent<Button>();
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.35f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.85f);
        colors.fadeDuration = 0.05f;
        button.colors = colors;
        button.targetGraphic = bg;
        button.onClick.AddListener(() => Choose(a, fx));

        RectTransform band = NewRect("Band", card);
        band.anchorMin = new Vector2(0f, 1f); band.anchorMax = new Vector2(1f, 1f);
        band.pivot = new Vector2(0.5f, 1f);
        band.sizeDelta = new Vector2(0f, 10f);
        band.anchoredPosition = Vector2.zero;
        band.gameObject.AddComponent<Image>().color = rc;

        Text rarity = NewText(card, "Rarity", 22, FontStyle.Bold, TextAnchor.MiddleCenter, rc);
        TopPlace(rarity.rectTransform, 30f, 34f);
        rarity.text = string.IsNullOrEmpty(a.rarity) ? "일반" : a.rarity;

        Text name = NewText(card, "Name", 34, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        TopPlace(name.rectTransform, 74f, 90f);
        name.text = a.name;

        Text desc = NewText(card, "Desc", 23, FontStyle.Normal, TextAnchor.UpperCenter, new Color(0.86f, 0.88f, 0.92f));
        TopPlace(desc.rectTransform, 182f, 230f);
        desc.lineSpacing = 1.15f;
        desc.text = a.description;

        int owned = player != null ? player.StackOf(a.code) : 0;
        Text foot = NewText(card, "Stack", 21, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.62f, 0.65f, 0.7f));
        foot.rectTransform.anchorMin = new Vector2(0f, 0f); foot.rectTransform.anchorMax = new Vector2(1f, 0f);
        foot.rectTransform.pivot = new Vector2(0.5f, 0f);
        foot.rectTransform.sizeDelta = new Vector2(-40f, 40f);
        foot.rectTransform.anchoredPosition = new Vector2(0f, 22f);
        foot.text = a.maxStacks > 1 ? (owned > 0 ? $"보유 {owned} / {a.maxStacks}" : $"최대 {a.maxStacks}번 중복") : "중복 불가";
        if (PlayerSkills.IsSkillEffect(a.effect)) foot.text = "스킬 · 고른 뒤 사용할 키 선택";
        return slot.gameObject;
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
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = anchor;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }
}
