using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

// v154 설치된 포탑 선택: 커서를 올리면 포탑 커서, 우클릭 = 선택 (연두색 외곽선) → Q 판매 / E 업그레이드
//  선택한 포탑 위에 안내 글자. 업그레이드가 아직 없으면(미해금) 회색
// v156 입구 벽(FortWall)도 같은 방식: 우클릭 → Q 수리 / E 강화 (무너졌으면 잔해를 골라 E = 다시 짓기)
public class TowerSelection : MonoBehaviour
{
    public static GameObject Hovered { get; private set; }
    public static GameObject Selected { get; private set; }
    public static bool HoveredWall { get; private set; }
    public static bool WallSelected { get; private set; }
    public static bool HoveringSelected => (Selected != null && Hovered == Selected) || (WallSelected && HoveredWall);
    private static int usedFrame = -1;
    // 이번 프레임 Q/E는 포탑·벽 몫 (스킬 키 막기)
    public static bool UsesQE => Selected != null || WallSelected || usedFrame == Time.frameCount;

    public static Color OutlineColor = new Color(0.62f, 1f, 0.38f, 1f);
    public static Color HoverColor = new Color(0.86f, 1f, 0.78f, 1f);   // v159 마우스를 올렸을 때
    public static float HoverWidth = 2f;
    public static float PromptHeight = 1.9f;

    private TowerBuilder builder;
    private PlayerController player;
    private Text prompt;
    private RectTransform promptRt;
    private TowerOutline outline;
    private GameObject outlined;   // 외곽선을 씌운 대상 (벽은 서 있음 ↔ 잔해로 바뀜)
    private int outlinedLevel = -3;
    private TowerOutline hoverOutline;
    private GameObject hoverOutlined;
    private RawImage fpsCursor;
    private bool fpsNow;

    void Awake() { builder = GetComponent<TowerBuilder>(); }

    void OnDestroy()
    {
        if (prompt != null) Destroy(prompt.canvas.gameObject);
        Hovered = Selected = null; HoveredWall = WallSelected = false;
    }

    void Update()
    {
        Camera cam = Camera.main;
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (Selected != null && !Selected.activeInHierarchy) Deselect();

        bool blocked = cam == null || builder == null || Time.timeScale == 0f || AugmentManager.IsOpen || builder.SelectedSlot >= 0
            || (PlayerSkills.Instance != null && PlayerSkills.Instance.IsAiming);
        bool fps = player != null && player.isFirstPerson;
        fpsNow = fps;
        bool onUI = !fps && PointerOverVisibleUI();
        if (WallSelected && FortWall.Instance == null) Deselect();
        Hovered = null; HoveredWall = false;
        if (!blocked && !onUI)
        {
            Ray ray = fps ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : cam.ScreenPointToRay(Input.mousePosition);
            Hovered = builder.TowerUnder(ray, out float td);
            if (FortWall.Instance != null && FortWall.Instance.Pick(ray, out float wd) && (Hovered == null || wd < td)) { Hovered = null; HoveredWall = true; }
        }
        if (builder != null && builder.SelectedSlot >= 0 && (Selected != null || WallSelected)) Deselect();   // 건설 모드에 들어가면 해제

        if (!blocked && !onUI && Input.GetMouseButtonDown(1))
        {
            if (Hovered != null && Hovered != Selected) Select(Hovered);
            else if (HoveredWall && !WallSelected) SelectWall();
            else Deselect();
        }

        if (WallSelected && !blocked)
        {
            if (Input.GetKeyDown(KeyCode.Q)) { usedFrame = Time.frameCount; FortWall.Instance.TryRepair(); }
            else if (Input.GetKeyDown(KeyCode.E)) { usedFrame = Time.frameCount; FortWall.Instance.TryBuildOrUpgrade(); }
        }
        if (WallSelected) KeepWallOutline();
        UpdateHoverOutline();

        if (Selected != null && !blocked)
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                usedFrame = Time.frameCount;
                GameObject t = Selected; Deselect();
                builder.SellTower(t);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                usedFrame = Time.frameCount;
                AugmentManager.Toast("포탑 업그레이드는 아직 해금되지 않았어요");
            }
        }
        UpdatePrompt(cam);
    }

    // v158 탑뷰에서 포탑 커서·선택이 안 되던 문제: 화면 전체를 덮는 '보이지 않는' UI(투명 이미지 등)가 있으면
    //  IsPointerOverGameObject가 항상 참 → 포탑을 못 찾음. 실제로 보이는 UI(알파 > 0.05) 위일 때만 UI 위로 봄
    private static readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private PointerEventData ped;
    private static bool loggedBlocker;
    private bool PointerOverVisibleUI()
    {
        EventSystem es = EventSystem.current;
        if (es == null) return false;
        ped = new PointerEventData(es) { position = Input.mousePosition };
        uiHits.Clear();
        es.RaycastAll(ped, uiHits);
        foreach (RaycastResult h in uiHits)
        {
            if (h.gameObject == null) continue;
            Graphic g = h.gameObject.GetComponent<Graphic>();
            float a = g != null ? g.color.a : 1f;
            CanvasGroup cg = h.gameObject.GetComponentInParent<CanvasGroup>();
            if (cg != null) a *= cg.alpha;
            if (a > 0.05f) return true;
            if (!loggedBlocker && Application.isEditor)
            {
                loggedBlocker = true;
                Debug.Log($"[포탑 선택] 보이지 않는 UI가 마우스를 가리고 있어 무시함: {h.gameObject.name} (부모 {(h.gameObject.transform.parent != null ? h.gameObject.transform.parent.name : "-")})");
            }
        }
        return false;
    }

    // v159 마우스를 올린 포탑·벽에 옅은 외곽선 (선택된 것은 선택 외곽선이 있으므로 빼고)
    private void UpdateHoverOutline()
    {
        GameObject target = Hovered != null ? Hovered : (HoveredWall && FortWall.Instance != null ? FortWall.Instance.Visual : null);
        if (target != null && target == outlined) target = null;
        if (target == hoverOutlined && (target == null || hoverOutline != null)) return;
        ClearHover();
        if (target != null) { hoverOutline = TowerOutline.Add(target, HoverColor, HoverWidth); hoverOutlined = target; }
    }

    private void ClearHover()
    {
        if (hoverOutline != null) DestroyImmediate(hoverOutline);   // 같은 물체에 바로 선택 외곽선을 씌울 수 있게 즉시 되돌림
        hoverOutline = null; hoverOutlined = null;
    }

    private void Select(GameObject t)
    {
        Deselect();
        ClearHover();
        Selected = t;
        outline = t.AddComponent<TowerOutline>(); outlined = t;
        Sfx.Play(Sfx.UIClickSound, 0.6f);
    }

    private void SelectWall()
    {
        Deselect();
        ClearHover();
        WallSelected = true;
        KeepWallOutline();
        Sfx.Play(Sfx.UIClickSound, 0.6f);
    }

    // 벽이 무너지거나 다시 지어지면 외곽선 대상이 바뀜
    private void KeepWallOutline()
    {
        GameObject v = FortWall.Instance != null ? FortWall.Instance.Visual : null;
        int lv = FortWall.Instance != null ? FortWall.Instance.CurrentLevel : -2;
        if (v == outlined && outline != null && lv == outlinedLevel) return;   // 강화하면 재질(무늬)이 바뀌므로 다시 씌움
        outlinedLevel = lv;
        if (hoverOutlined == v) ClearHover();
        if (outline != null) DestroyImmediate(outline);   // 같은 물체에 바로 다시 씌우므로 원래 재질을 먼저 되돌림
        outline = v != null ? v.AddComponent<TowerOutline>() : null;
        outlined = v;
    }

    private void Deselect()
    {
        if (outline != null) DestroyImmediate(outline);   // v159 바로 올림 외곽선을 씌울 수 있게 즉시 되돌림
        outline = null; outlined = null;
        Selected = null; WallSelected = false;
    }

    private void UpdatePrompt(Camera cam)
    {
        if (prompt == null) BuildPrompt();
        if (fpsCursor != null) fpsCursor.enabled = fpsNow && (Hovered != null || HoveredWall) && Time.timeScale > 0f;
        bool wall = WallSelected && FortWall.Instance != null;
        bool show = (Selected != null || wall) && cam != null && Time.timeScale > 0f;
        Vector3 anchor = wall ? FortWall.Instance.PromptAnchor : (Selected != null ? Selected.transform.position + Vector3.up * PromptHeight : Vector3.zero);
        Vector3 sp = show ? cam.WorldToScreenPoint(anchor) : Vector3.zero;
        if (sp.z <= 0f) show = false;
        prompt.enabled = show;
        if (!show) return;
        promptRt.position = new Vector3(Mathf.Round(sp.x), Mathf.Round(sp.y), 0f);
        prompt.text = wall ? FortWall.Instance.PromptText()
            : $"<color=#FFFFFF>[Q] 판매 +{builder.RefundOf(Selected)} G</color>\n<color=#7A7A7A>[E] 업그레이드 (미해금)</color>";
    }

    private void BuildPrompt()
    {
        GameObject cgo = new GameObject("TowerSelectPrompt");
        Canvas c = cgo.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 40;
        CanvasScaler cs = cgo.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(cgo.transform, false);
        promptRt = (RectTransform)go.transform;
        promptRt.sizeDelta = new Vector2(230f, 60f);
        promptRt.pivot = new Vector2(0.5f, 0f);
        prompt = go.AddComponent<Text>();
        prompt.supportRichText = true;
        prompt.raycastTarget = false;
        prompt.alignment = TextAnchor.LowerLeft;   // v155 왼쪽 정렬
        prompt.fontSize = Mathf.RoundToInt(16f * Mathf.Max(1f, Screen.height / 1080f));
        prompt.horizontalOverflow = HorizontalWrapMode.Overflow;
        prompt.verticalOverflow = VerticalWrapMode.Overflow;
        UIFont.Apply(prompt, FontStyle.Normal);   // v155 얇은 글자 + 얇은 그림자
        Shadow o = go.AddComponent<Shadow>();
        o.effectColor = new Color(0.13f, 0.08f, 0.1f, 0.9f);
        o.effectDistance = new Vector2(1f, -1f);
        prompt.enabled = false;

        // v155 1인칭: 커서가 숨겨져 있으므로 포탑을 조준하면 화면 가운데에 포탑 커서 그림
        Texture2D ct = Resources.Load<Texture2D>("Image/UI/Cursors/Cursor_Tower");
        if (ct != null)
        {
            GameObject ig = new GameObject("FpsTowerCursor", typeof(RectTransform));
            ig.transform.SetParent(cgo.transform, false);
            RectTransform irt = (RectTransform)ig.transform;
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
            float s = Mathf.Max(1f, Mathf.Round(Screen.height / 1080f * 1.5f));
            irt.sizeDelta = new Vector2(ct.width, ct.height) * s;
            irt.anchoredPosition = Vector2.zero;
            fpsCursor = ig.AddComponent<RawImage>();
            fpsCursor.texture = ct; fpsCursor.raycastTarget = false; fpsCursor.enabled = false;
        }
    }
}

// 선택된 포탑의 연두색 외곽선: 포탑의 그림마다 뒤에 단색 복사본 4장 (상하좌우 1.5px)
public class TowerOutline : MonoBehaviour
{
    private static Material mat;
    private readonly List<SpriteRenderer> src = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer[]> copies = new List<SpriteRenderer[]>();
    private static readonly Vector2[] Dirs = { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) };

    // v155 3D 포탑(메쉬): Toon Lit 외곽선 색·굵기를 연두색으로 바꾼 복사 재질을 씌움
    private readonly List<Renderer> meshSrc = new List<Renderer>();
    private readonly List<Material[]> meshOrig = new List<Material[]>();
    private readonly List<Material> made = new List<Material>();
    public static float MeshOutlineWidth = 3f;
    // v159 마우스를 올렸을 때(선택 전) 외곽선: 색·굵기를 다르게
    private static Color? pendingColor;
    private static float pendingWidth;
    private Color color;
    private float width;
    public static TowerOutline Add(GameObject go, Color c, float w)
    {
        pendingColor = c; pendingWidth = w;
        TowerOutline o = go.AddComponent<TowerOutline>();
        pendingColor = null;
        return o;
    }

    void OnEnable()
    {
        color = pendingColor ?? TowerSelection.OutlineColor;
        width = pendingColor.HasValue ? pendingWidth : MeshOutlineWidth;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is SpriteRenderer || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            Material[] orig = r.sharedMaterials; bool any = false;
            Material[] repl = new Material[orig.Length];
            for (int i = 0; i < orig.Length; i++)
            {
                repl[i] = orig[i];
                if (orig[i] == null || !orig[i].HasProperty("_OutlineColor")) continue;
                Material m = new Material(orig[i]);
                m.SetColor("_OutlineColor", color);
                if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", Mathf.Max(m.GetFloat("_OutlineWidth"), width));
                repl[i] = m; made.Add(m); any = true;
            }
            if (!any) continue;
            meshSrc.Add(r); meshOrig.Add(orig);
            r.sharedMaterials = repl;
        }
        if (mat == null)
        {
            // v160 GUI/Text Shader는 빌드에 넣을 수 없는 내장 셰이더라 빌드 오류 → Sprites/Default (포탑은 거의 메쉬라 영향 적음)
            mat = new Material(Shader.Find("Sprites/Default")) { name = "TowerOutline (runtime)" };
        }
        mat.color = Color.white;   // 색은 그림마다 (선택 = 연두, 올림 = 옅은 연두)
        foreach (SpriteRenderer r in GetComponentsInChildren<SpriteRenderer>())
        {
            if (!r.enabled || r.sprite == null || r.gameObject.layer == 2) continue;
            src.Add(r);
            var arr = new SpriteRenderer[Dirs.Length];
            for (int i = 0; i < Dirs.Length; i++)
            {
                GameObject go = new GameObject("SelOutline");
                go.layer = 2;
                go.transform.SetParent(r.transform, false);
                SpriteRenderer c = go.AddComponent<SpriteRenderer>();
                c.sharedMaterial = mat;
                c.color = color;
                c.sortingLayerID = r.sortingLayerID;
                c.sortingOrder = r.sortingOrder - 1;
                c.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                c.receiveShadows = false;
                arr[i] = c;
            }
            copies.Add(arr);
        }
        LateUpdate();
    }

    void OnDisable()
    {
        for (int i = 0; i < meshSrc.Count; i++) if (meshSrc[i] != null) meshSrc[i].sharedMaterials = meshOrig[i];
        foreach (Material m in made) if (m != null) Destroy(m);
        meshSrc.Clear(); meshOrig.Clear(); made.Clear();
        foreach (var arr in copies) foreach (SpriteRenderer c in arr) if (c != null) Destroy(c.gameObject);
        copies.Clear(); src.Clear();
    }

    void LateUpdate()
    {
        for (int k = 0; k < src.Count; k++)
        {
            SpriteRenderer r = src[k];
            SpriteRenderer[] arr = copies[k];
            bool on = r != null && r.enabled && r.gameObject.activeInHierarchy && r.sprite != null;
            for (int i = 0; i < arr.Length; i++)
            {
                SpriteRenderer c = arr[i];
                if (c == null) continue;
                c.enabled = on;
                if (!on) continue;
                c.sprite = r.sprite; c.flipX = r.flipX; c.flipY = r.flipY;
                float px = 1.5f / Mathf.Max(1f, r.sprite.pixelsPerUnit);
                c.transform.localPosition = new Vector3(Dirs[i].x * px, Dirs[i].y * px, 0.01f);
            }
        }
    }
}
