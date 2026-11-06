using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 타격감 모음 (v97). 씬에 자동으로 만들어짐 — 플레이 중 Hierarchy의 'Juice'에서 값을 바로 바꿔 볼 수 있음
//  ■ 피해 숫자: 몹이 맞으면 머리 위로 떠오름 (기본 흰색, 치명타 노란색·크게). 아주 짧은 사이의 연속 피해는 한 숫자로 합침
//  ■ 명중 멈춤(히트스톱) + 화면 흔들림: 플레이어가 맞힐 때 아주 약하게, 치명타·처치·엘리트 처치는 더 세게
//  ■ 골드: 몹이 죽으면 동전이 튀어 오른 뒤 플레이어에게 빨려 와서 들어옴 (닿는 순간 골드 추가)
public class Juice : MonoBehaviour
{
    public static Juice Instance { get; private set; }

    [Header("피해 숫자")]
    public bool damageNumbers = true;
    public int numberSize = 18;      // v106: 20% 줄임 (22 → 18)
    public int critNumberSize = 26;  // v106: 20% 줄임 (32 → 26)
    public Color numberColor = Color.white;
    public Color critColor = new Color(1f, 0.84f, 0.29f);
    public float numberLife = 0.65f;
    public float numberRise = 55f;            // 화면 픽셀
    [Tooltip("같은 대상에게 이 시간 안에 들어온 피해는 한 숫자로 합침 (산탄총 등)")]
    public float mergeWindow = 0.08f;

    [Header("명중 멈춤 (실제 시간, 초)")]
    public float hitStopHit = 0.012f;
    public float hitStopCrit = 0.03f;
    public float hitStopKill = 0.045f;
    public float hitStopHeavyKill = 0.09f;
    [Tooltip("멈춤 사이 최소 간격 (연사 무기가 계속 멈추지 않게)")]
    public float hitStopCooldown = 0.12f;
    [Range(0f, 0.5f)] public float hitStopTimeScale = 0.05f;

    [Header("화면 흔들림 (세기 미터, 시간 초)")]
    public Vector2 shakeHit = new Vector2(0.015f, 0.06f);
    public Vector2 shakeCrit = new Vector2(0.04f, 0.1f);
    public Vector2 shakeKill = new Vector2(0.06f, 0.12f);
    public Vector2 shakeHeavyKill = new Vector2(0.16f, 0.25f);

    [Header("골드 동전")]
    public int maxCoinsPerKill = 8;
    public float coinPopTime = 0.45f;
    public float coinSize = 0.22f;

    // ---------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("Juice");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<Juice>();
    }

    void OnEnable() { Health.AnyDamagedBy += OnAnyDamaged; }
    void OnDisable() { Health.AnyDamagedBy -= OnAnyDamaged; }

    private static Transform Player => GameManager.Instance != null ? GameManager.Instance.playerTransform : null;
    private static bool IsPlayer(Transform t)
    {
        Transform p = Player;
        return t != null && p != null && (t == p || t.IsChildOf(p));
    }

    private void OnAnyDamaged(Health h, float amount, Transform attacker)
    {
        if (h == null || h.invincible || amount <= 0f) return;
        if (h.GetComponent<MobAI>() == null) return; // 몹이 맞았을 때만
        bool crit = Health.LastHitCrit;
        if (damageNumbers) ShowNumber(h, amount, crit, Health.DotTick);
        if (Health.DotTick || !IsPlayer(attacker) || h.IsDead || h.CurrentHealth <= 0f) return; // 처치는 OnMobKilled에서
        if (crit) { HitStop(hitStopCrit); Shake(shakeCrit); }
        else { HitStop(hitStopHit); Shake(shakeHit); }
    }

    // MobAI가 죽을 때 호출
    public static void OnMobKilled(Transform attacker, bool heavy)
    {
        Juice j = Instance;
        if (j == null) return;
        bool byPlayer = IsPlayer(attacker);
        if (heavy) { j.HitStop(j.hitStopHeavyKill, true); j.Shake(j.shakeHeavyKill); }
        else if (byPlayer) { j.HitStop(j.hitStopKill, true); j.Shake(j.shakeKill); }
    }

    private static void ShakeStatic(Vector2 s) { CameraController.Shake(s.x, s.y); }
    private void Shake(Vector2 s) { ShakeStatic(s); }

    // ===== 명중 멈춤 =====
    private float lastStopReal = -999f;
    private bool stopping;
    private void HitStop(float seconds, bool force = false)
    {
        if (seconds <= 0f || stopping) return;
        if (Time.timeScale < 0.99f) return; // 일시정지·선택창 중에는 안 함
        if (!force && Time.unscaledTime - lastStopReal < hitStopCooldown) return;
        lastStopReal = Time.unscaledTime;
        StartCoroutine(StopRoutine(seconds));
    }

    private IEnumerator StopRoutine(float seconds)
    {
        stopping = true;
        float ts = Mathf.Max(0.001f, hitStopTimeScale);
        Time.timeScale = ts;
        yield return new WaitForSecondsRealtime(seconds);
        if (Mathf.Approximately(Time.timeScale, ts)) Time.timeScale = 1f; // 그 사이 일시정지되었으면 건드리지 않음
        stopping = false;
    }

    // ===== 피해 숫자 =====
    private class Num { public Text text; public Outline outline; public Health target; public Vector3 world; public float born; public float value; public bool crit; public Vector2 jitter; public float baseA; }
    private readonly List<Num> nums = new List<Num>();
    private readonly Stack<Num> numPool = new Stack<Num>();
    private Canvas canvas;

    private void EnsureCanvas()
    {
        if (canvas != null) return;
        GameObject go = new GameObject("DamageNumbers", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -5; // HUD·선택창 아래
        CanvasScaler cs = go.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920f, 1080f);
        cs.matchWidthOrHeight = 0.5f;
    }

    private static Vector3 HeadPoint(Health h)
    {
        Collider c = h.GetComponent<Collider>();
        if (c != null) return new Vector3(c.bounds.center.x, c.bounds.max.y + 0.15f, c.bounds.center.z);
        return h.transform.position + Vector3.up * 1.6f;
    }

    private void ShowNumber(Health h, float amount, bool crit, bool dot)
    {
        EnsureCanvas();
        float now = Time.time;
        // 합치기
        for (int i = nums.Count - 1; i >= 0; i--)
        {
            Num n = nums[i];
            if (n.target == h && n.crit == crit && now - n.born < mergeWindow)
            {
                n.value += amount;
                n.text.text = Format(n.value);
                return;
            }
        }
        Num m = numPool.Count > 0 ? numPool.Pop() : NewNum();
        m.text.gameObject.SetActive(true);
        m.target = h; m.world = HeadPoint(h); m.born = now; m.value = amount; m.crit = crit;
        m.jitter = new Vector2(Random.Range(-22f, 22f), Random.Range(-6f, 10f));
        m.text.text = Format(amount);
        m.text.fontSize = crit ? critNumberSize : dot ? Mathf.RoundToInt(numberSize * 0.75f) : numberSize;
        Color col = crit ? critColor : numberColor;
        if (dot) col.a = 0.75f;
        m.baseA = col.a;
        m.text.color = col;
        nums.Add(m);
    }

    private static string Format(float v) => Mathf.Max(1, Mathf.RoundToInt(v)).ToString();

    private Num NewNum()
    {
        GameObject go = new GameObject("Num", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        Text t = go.AddComponent<Text>();
        UIFont.Apply(t, FontStyle.Bold);
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        ((RectTransform)go.transform).sizeDelta = new Vector2(160f, 50f);
        Outline o = go.AddComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.9f);
        o.effectDistance = new Vector2(2f, -2f);
        return new Num { text = t, outline = o };
    }

    void LateUpdate()
    {
        UpdateNumbers();
        UpdateCoins();
    }

    private void UpdateNumbers()
    {
        if (nums.Count == 0) return;
        Camera cam = Camera.main;
        RectTransform cr = canvas != null ? (RectTransform)canvas.transform : null;
        float now = Time.time;
        for (int i = nums.Count - 1; i >= 0; i--)
        {
            Num n = nums[i];
            float age = now - n.born;
            if (age > numberLife || cam == null || cr == null)
            {
                n.text.gameObject.SetActive(false); numPool.Push(n); nums.RemoveAt(i); continue;
            }
            Vector3 sp = cam.WorldToScreenPoint(n.world);
            if (sp.z < 0f) { n.text.enabled = false; continue; }
            n.text.enabled = true;
            float u = age / numberLife;
            // 톡 튀어나왔다가(0~0.15) 떠오르며 사라짐
            float pop = u < 0.15f ? Mathf.Lerp(1.6f, 1f, u / 0.15f) : 1f;
            if (n.crit) pop *= 1.1f;
            float rise = numberRise * (1f - (1f - u) * (1f - u));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(cr, sp, null, out Vector2 lp);
            RectTransform rt = (RectTransform)n.text.transform;
            rt.anchoredPosition = lp + n.jitter + Vector2.up * rise;
            rt.localScale = Vector3.one * pop;
            Color c = n.text.color; c.a = u < 0.6f ? n.baseA : Mathf.Lerp(n.baseA, 0f, (u - 0.6f) / 0.4f);
            n.text.color = c;
            Color oc = n.outline.effectColor; oc.a = 0.9f * c.a; n.outline.effectColor = oc;
        }
    }

    // ===== 골드 동전 =====
    private class Coin { public Transform tr; public SpriteRenderer sr; public Vector3 vel; public float born; public float groundY; public int value; public float spin; public float speed; }
    private readonly List<Coin> coins = new List<Coin>();
    private readonly Stack<Coin> coinPool = new Stack<Coin>();
    private static Sprite coinSprite;

    public static void DropGold(Vector3 at, int amount)
    {
        if (amount <= 0) return;
        Juice j = Instance;
        GoldManager gm = GoldManager.Instance;
        if (j == null || Player == null) { if (gm != null) gm.Add(amount); return; }
        int n = Mathf.Clamp(amount <= 3 ? amount : 3 + amount / 10, 1, Mathf.Max(1, j.maxCoinsPerKill));
        int each = amount / n, rest = amount - each * n;
        for (int i = 0; i < n; i++) j.SpawnCoin(at, each + (i < rest ? 1 : 0));
    }

    private void SpawnCoin(Vector3 at, int value)
    {
        Coin c = coinPool.Count > 0 ? coinPool.Pop() : null;
        if (c == null)
        {
            GameObject go = new GameObject("Coin");
            go.transform.SetParent(transform, false);
            c = new Coin { tr = go.transform, sr = go.AddComponent<SpriteRenderer>() };
            c.sr.sprite = CoinSprite();
            c.sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        c.tr.gameObject.SetActive(true);
        c.groundY = at.y + 0.12f;
        c.tr.position = at + Vector3.up * 0.6f;
        Vector2 r = Random.insideUnitCircle.normalized * Random.Range(1.2f, 2.6f);
        c.vel = new Vector3(r.x, Random.Range(3.5f, 5f), r.y);
        c.born = Time.time + Random.Range(0f, 0.08f);
        c.value = value;
        c.spin = Random.Range(0f, 6f);
        c.speed = 4f;
        c.sr.enabled = true;
        float s = coinSize;
        c.tr.localScale = new Vector3(s, s, s);
        coins.Add(c);
    }

    private void UpdateCoins()
    {
        if (coins.Count == 0) return;
        Camera cam = Camera.main;
        Transform p = Player;
        float dt = Time.deltaTime, now = Time.time;
        Vector3 target = p != null ? p.position + Vector3.up * 0.7f : Vector3.zero;
        for (int i = coins.Count - 1; i >= 0; i--)
        {
            Coin c = coins[i];
            float age = now - c.born;
            Vector3 pos = c.tr.position;
            if (p == null || age < coinPopTime)
            {
                // 튀어 오름 → 바닥에서 통통
                c.vel.y -= 18f * dt;
                pos += c.vel * dt;
                if (pos.y < c.groundY) { pos.y = c.groundY; c.vel.y = Mathf.Abs(c.vel.y) * 0.35f; c.vel.x *= 0.6f; c.vel.z *= 0.6f; }
                if (p == null && age > 1.5f) { Collect(c, i); continue; }
            }
            else if (TalentEffects.ManualPickup && c.speed <= 4f && (target - pos).sqrMagnitude > TalentEffects.PickupRadius * TalentEffects.PickupRadius)
            {
                // 발품 팔기 (재능): 바닥에 남아 있다가 가까이 가야 빨려 옴. 오래 두면 깜빡이다 사라짐
                c.vel.y -= 18f * dt;
                pos += c.vel * dt;
                if (pos.y < c.groundY) { pos.y = c.groundY; c.vel = Vector3.zero; }
                float left = TalentEffects.CoinLife - age;
                if (left <= 0f) { c.tr.gameObject.SetActive(false); coins.RemoveAt(i); coinPool.Push(c); continue; }
                c.sr.enabled = left > 3f || ((int)(left / 0.12f)) % 2 == 0;
            }
            else
            {
                c.sr.enabled = true;
                // 플레이어에게 빨려 감 (점점 빨라짐)
                c.speed = Mathf.Min(30f, c.speed + 45f * dt);
                Vector3 d = target - pos;
                float dist = d.magnitude;
                if (dist < 0.35f || dist < c.speed * dt) { Collect(c, i); continue; }
                pos += d / dist * c.speed * dt;
            }
            c.tr.position = pos;
            if (cam != null) c.tr.rotation = cam.transform.rotation;
            // 빙글빙글 도는 느낌 (가로 폭만 줄었다 늘었다)
            float w = Mathf.Abs(Mathf.Cos((now + c.spin) * 9f)) * 0.8f + 0.2f;
            c.tr.localScale = new Vector3(coinSize * w, coinSize, coinSize);
        }
    }

    private void Collect(Coin c, int index)
    {
        GoldManager gm = GoldManager.Instance;
        if (gm != null && c.value > 0) gm.Add(c.value);
        c.tr.gameObject.SetActive(false);
        coins.RemoveAt(index);
        coinPool.Push(c);
    }

    // 9×9 픽셀 동전 (코드로 그림)
    private static Sprite CoinSprite()
    {
        if (coinSprite != null) return coinSprite;
        string[] art =
        {
            "..OOOOO..",
            ".OYYYYYO.",
            "OYWYYYYGO",
            "OYYYDYYGO",
            "OYYYDYYGO",
            "OYYYDYYGO",
            "OYYYYYYGO",
            ".OGGGGGO.",
            "..OOOOO..",
        };
        var tex = new Texture2D(9, 9, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 9; x++)
            {
                char ch = art[8 - y][x];
                Color c = ch == 'O' ? new Color32(70, 40, 10, 255)
                        : ch == 'Y' ? new Color32(255, 200, 60, 255)
                        : ch == 'W' ? new Color32(255, 250, 210, 255)
                        : ch == 'G' ? new Color32(205, 130, 30, 255)
                        : ch == 'D' ? new Color32(215, 150, 35, 255)
                        : new Color(0, 0, 0, 0);
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        coinSprite = Sprite.Create(tex, new Rect(0, 0, 9, 9), new Vector2(0.5f, 0.5f), 9f);
        return coinSprite;
    }
}
