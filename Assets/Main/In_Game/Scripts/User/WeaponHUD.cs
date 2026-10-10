using UnityEngine;
using UnityEngine.UI;

// 무기 HUD (화면 오른쪽 아래): 무기 이름 / 남은 탄약 / 재장전 진행바
// ※ 씬에 따로 배치할 필요 없음. PlayerWeaponManager가 시작할 때 자동으로 만들어 줍니다.
//    위치·크기·색을 바꾸고 싶으면 플레이 중 Hierarchy의 "WeaponHUD" 오브젝트를 보며 아래 값들을 조절하세요.
public class WeaponHUD : MonoBehaviour
{
    public static WeaponHUD Instance { get; private set; }

    [Header("배치")]
    public Vector2 screenMargin = new Vector2(48f, 40f);   // 오른쪽/아래 여백 (1920x1080 기준)
    public float panelWidth = 340f;

    [Header("색상")]
    public Color mainColor = new Color(1f, 1f, 1f, 0.95f);
    public Color subColor = new Color(1f, 1f, 1f, 0.55f);
    public Color lowAmmoColor = new Color(1f, 0.35f, 0.3f, 1f);
    public Color reloadBarColor = new Color(1f, 0.85f, 0.4f, 1f);
    [Range(0f, 1f)] public float lowAmmoRatio = 0.25f;     // 이 비율 이하면 탄약 숫자를 빨갛게

    private PlayerWeaponManager weaponManager;
    private Text nameText;
    private Text ammoText;
    private Text hintText;
    private GameObject reloadBarRoot;
    private RectTransform reloadBarFill;

    // PlayerWeaponManager가 호출: HUD가 없으면 만들고, 표시할 플레이어를 연결
    public static void Attach(PlayerWeaponManager manager)
    {
        if (Instance == null)
        {
            GameObject go = new GameObject("WeaponHUD");
            Instance = go.AddComponent<WeaponHUD>();
            Instance.Build();
        }
        Instance.weaponManager = manager;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        Weapon weapon = weaponManager != null ? weaponManager.CurrentWeapon : null;
        PlayerWeaponData data = weapon != null ? weapon.playerWepData : null;

        bool visible = data != null;
        if (nameText.gameObject.activeSelf != visible) SetVisible(visible);
        if (!visible) return;

        nameText.text = string.IsNullOrEmpty(data.inGameName) ? data.weaponID : data.inGameName;

        if (!data.UsesAmmo)
        {
            ammoText.text = "<size=56><b>∞</b></size>";
            hintText.text = "";
            reloadBarRoot.SetActive(false);
            return;
        }

        int cur = weapon.currentAmmo;
        int max = data.maxAmmo;
        bool low = cur <= Mathf.CeilToInt(max * lowAmmoRatio);
        string curColor = ColorUtility.ToHtmlStringRGBA(low ? lowAmmoColor : mainColor);
        string subHex = ColorUtility.ToHtmlStringRGBA(subColor);

        ammoText.text = $"<size=56><b><color=#{curColor}>{cur}</color></b></size><size=28><color=#{subHex}>  /  {max}</color></size>";

        if (weapon.isReloading)
        {
            reloadBarRoot.SetActive(true);
            reloadBarFill.anchorMax = new Vector2(weapon.ReloadProgress, 1f);
            hintText.text = "재장전 중...";
        }
        else
        {
            reloadBarRoot.SetActive(false);
            hintText.text = cur <= 0 ? "R 키로 재장전" : "";
        }
    }

    private void SetVisible(bool visible)
    {
        nameText.gameObject.SetActive(visible);
        ammoText.gameObject.SetActive(visible);
        hintText.gameObject.SetActive(visible);
        if (!visible) reloadBarRoot.SetActive(false);
    }

    // ------------------------------------------------------------------
    // UI 자동 생성 (Canvas → 오른쪽 아래 패널)
    // ------------------------------------------------------------------
    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1; // 일시정지·게임오버 화면(기본 0)이 HUD를 덮도록 한 단계 아래

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Font font = LoadFont();

        RectTransform panel = CreateRect("Panel", transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(-screenMargin.x, screenMargin.y);
        panel.sizeDelta = new Vector2(panelWidth, 130f);

        // 위에서부터: 무기 이름 / 탄약 / 재장전 바 / 안내 문구
        nameText = CreateText("WeaponName", panel, font, 24, subColor, new Vector2(0f, 100f), 30f);
        ammoText = CreateText("Ammo", panel, font, 28, mainColor, new Vector2(0f, 34f), 66f);
        hintText = CreateText("Hint", panel, font, 20, reloadBarColor, new Vector2(0f, 0f), 24f);

        RectTransform barBg = CreateRect("ReloadBar", panel);
        barBg.anchorMin = barBg.anchorMax = barBg.pivot = new Vector2(1f, 0f);
        barBg.anchoredPosition = new Vector2(0f, 28f);
        barBg.sizeDelta = new Vector2(panelWidth, 6f);
        Image bg = barBg.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.5f);
        bg.raycastTarget = false;

        reloadBarFill = CreateRect("Fill", barBg);
        reloadBarFill.anchorMin = Vector2.zero;
        reloadBarFill.anchorMax = new Vector2(0f, 1f);
        reloadBarFill.offsetMin = reloadBarFill.offsetMax = Vector2.zero;
        Image fill = reloadBarFill.gameObject.AddComponent<Image>();
        fill.color = reloadBarColor;
        fill.raycastTarget = false;

        reloadBarRoot = barBg.gameObject;
        reloadBarRoot.SetActive(false);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Text CreateText(string name, Transform parent, Font font, int size, Color color, Vector2 pos, float height)
    {
        RectTransform rt = CreateRect(name, parent);
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(0f, height);

        Text t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.LowerRight;
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false; // 화면 버튼 클릭을 막지 않도록

        Shadow shadow = rt.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    // 한글이 나오도록 윈도우 기본 한글 폰트(맑은 고딕)를 우선 사용
    private static Font LoadFont()
    {
        Font font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Arial" }, 32);
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return font;
    }
}
