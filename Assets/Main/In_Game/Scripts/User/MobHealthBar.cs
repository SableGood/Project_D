using UnityEngine;
using UnityEngine.UI;

// 몹 머리 위 체력바 + 상태이상 아이콘
//  - MobAI가 자동으로 붙임 (보스 제외: 보스는 화면 위 BossHealthBar)
//  - 피해를 입었거나 상태이상이 있을 때만 보임 (멀쩡한 몹 무리는 화면이 깔끔하게)
//  - 바 오른쪽에 아이콘: 화상 · 중독 · 둔화 · 기절 · 치명타 (Resources/Image/UI/Status)
//  - 줄어든 체력은 밝은 띠로 잠깐 남았다가 따라 줄어듦
//  - 바닥·몹 그림에 가려지지 않게 깊이 검사 없이(항상 위에) 그림
public class MobHealthBar : MonoBehaviour
{
    public static bool Visible = true;
    // 켜면 체력이 가득 차도 항상 표시
    public static bool AlwaysShow = false;

    [Tooltip("1인칭: 그림 맨 위에서 이만큼 위 (미터)")]
    public float heightOffset = 0.18f;
    [Tooltip("탑뷰: 발밑에서 카메라 쪽(화면 아래쪽)으로 이만큼 떨어진 바닥 위에 표시 (미터)")]
    public float footOffset = 0.45f;
    [Tooltip("월드 크기 배율 (0.01 = 100픽셀이 1미터)")]
    public float worldScale = 0.011f;
    [Tooltip("탑뷰에서 크기 배율 (멀리서 내려다보므로 조금 크게)")]
    public float topViewScale = 1.35f;

    const float BarW = 84f, BarH = 10f, Icon = 22f, IconGap = 2f;
    static readonly Color FillColor = new Color(0.88f, 0.36f, 0.3f, 1f);
    static readonly Color ChipColor = new Color(1f, 0.92f, 0.85f, 0.9f);
    static readonly Color BackColor = new Color(0.16f, 0.16f, 0.16f, 0.92f);

    static readonly string[] IconNames = { "Burn", "Poison", "Slow", "Stun", "Crit" };
    static Sprite[] iconSprites;

    private Health health;
    private StatusEffects status;
    private SpriteRenderer body;
    private Canvas canvas;
    private RectTransform fill, chip;
    private readonly Image[] icons = new Image[5];
    private float chipRatio = 1f;
    private float shownRatio = -1f;
    private Camera cam;
    private bool disabledForBoss;
    private float baseScale;
    private static Material overlayMat;

    // 깊이 검사 없이 그리는 UI 재질 (바닥이나 다른 그림에 가려지지 않음)
    private static Material OverlayMaterial()
    {
        if (overlayMat != null) return overlayMat;
        overlayMat = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "MobHealthBar (always on top)" };
        overlayMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
        return overlayMat;
    }

    void Start()
    {
        health = GetComponent<Health>();
        status = GetComponent<StatusEffects>();
        MobAI mob = GetComponent<MobAI>();
        body = mob != null ? mob.BodySprite : null;
        disabledForBoss = GetComponent<BossPhases>() != null;
        if (!disabledForBoss) BuildUI();
    }

    void OnEnable()
    {
        chipRatio = 1f; shownRatio = -1f;
        if (canvas != null) canvas.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (canvas == null || health == null) return;
        float max = Mathf.Max(1f, health.maxHealth);
        float k = Mathf.Clamp01(health.CurrentHealth / max);
        bool anyStatus = status != null && status.AnyShown;
        bool show = Visible && !health.IsDead && (AlwaysShow || k < 0.999f || anyStatus);
        if (canvas.gameObject.activeSelf != show) canvas.gameObject.SetActive(show);
        if (!show) { chipRatio = k; return; }

        if (cam == null) cam = Camera.main;
        if (cam != null) canvas.transform.rotation = cam.transform.rotation;
        bool top = cam != null && !CameraController.FirstPersonView;
        canvas.transform.localScale = Vector3.one * baseScale * (top ? topViewScale : 1f);
        if (top)
        {
            // 탑뷰: 발밑 (바닥 바로 위, 화면에서 캐릭터 아래쪽 = 카메라 쪽으로 조금)
            Vector3 toCam = -cam.transform.forward; toCam.y = 0f;
            if (toCam.sqrMagnitude < 0.0001f) toCam = Vector3.back;
            toCam.Normalize();
            float floor = body != null ? body.bounds.min.y : transform.position.y;
            Vector3 feet = new Vector3(transform.position.x, floor + 0.12f, transform.position.z);
            canvas.transform.position = feet + toCam * footOffset;
        }
        else
        {
            float headY = body != null ? body.bounds.max.y : transform.position.y + 1.5f;
            canvas.transform.position = new Vector3(transform.position.x, headY + heightOffset, transform.position.z);
        }

        if (!Mathf.Approximately(k, shownRatio))
        {
            shownRatio = k;
            fill.anchorMax = new Vector2(k, 1f);
        }
        if (chipRatio < k) chipRatio = k;
        else if (chipRatio > k) chipRatio = Mathf.Max(k, chipRatio - Time.deltaTime * 0.8f);
        chip.anchorMax = new Vector2(chipRatio, 1f);

        // 상태이상 아이콘 (켜진 것만 왼쪽부터 채움)
        bool[] on = {
            status != null && status.Burning,
            status != null && status.Poisoned,
            status != null && status.Slowed,
            status != null && status.Stunned,
            status != null && status.CritShown };
        int slot = 0;
        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i] == null) continue;
            bool v = on[i] && icons[i].sprite != null;
            if (icons[i].enabled != v) icons[i].enabled = v;
            if (!v) continue;
            icons[i].rectTransform.anchoredPosition = new Vector2(BarW * 0.5f + 6f + slot * (Icon + IconGap), 0f);
            slot++;
        }
    }

    private static void LoadIcons()
    {
        if (iconSprites != null) return;
        iconSprites = new Sprite[IconNames.Length];
        for (int i = 0; i < IconNames.Length; i++)
        {
            iconSprites[i] = Resources.Load<Sprite>("Image/UI/Status/" + IconNames[i]);
            if (iconSprites[i] != null) iconSprites[i].texture.filterMode = FilterMode.Point;
        }
    }

    private void BuildUI()
    {
        LoadIcons();
        GameObject root = new GameObject("MobHealthBar", typeof(RectTransform), typeof(Canvas));
        root.layer = 5;
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 9;
        RectTransform rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(BarW, Icon);
        baseScale = worldScale / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
        rt.localScale = Vector3.one * baseScale;

        RectTransform frame = Rect(rt, "Frame", Vector2.zero, new Vector2(BarW + 4f, BarH + 4f), new Color(0f, 0f, 0f, 0.9f));
        RectTransform back = Rect(frame, "Back", Vector2.zero, new Vector2(BarW, BarH), BackColor);
        chip = Stretch(Rect(back, "Chip", Vector2.zero, Vector2.zero, ChipColor));
        fill = Stretch(Rect(back, "Fill", Vector2.zero, Vector2.zero, FillColor));

        for (int i = 0; i < icons.Length; i++)
        {
            RectTransform ic = Rect(rt, "Icon_" + IconNames[i], Vector2.zero, new Vector2(Icon, Icon), Color.white);
            ic.anchorMin = ic.anchorMax = new Vector2(0.5f, 0.5f);
            ic.pivot = new Vector2(0f, 0.5f);
            icons[i] = ic.GetComponent<Image>();
            icons[i].sprite = iconSprites[i];
            icons[i].preserveAspect = true;
            icons[i].enabled = false;
        }
        root.SetActive(false);
    }

    private static RectTransform Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.pivot = new Vector2(0f, 0.5f);
        r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        return r;
    }

    private static RectTransform Rect(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = 5;
        RectTransform r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchoredPosition = pos;
        r.sizeDelta = size;
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        img.material = OverlayMaterial();
        return r;
    }
}
