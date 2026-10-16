using System.Collections.Generic;
using UnityEngine;

// 플레이어 상·하체 분리 애니메이션 (탑뷰)
//  ■ 상체: 조준 방향(마우스)에 따라 8방향 그림 + 가만히 있으면 대기(Idle) 그림
//  ■ 하체: 움직이면 걷기 프레임, 멈추면 서 있는 프레임. 이동 방향에 따라 좌우 반전
//  ■ 무기: 상체 그림의 '손' 위치에 무기 손잡이가 오도록 무기 장착점을 매 프레임 옮김
//  ■ 이음새: 상체 아래쪽·하체 위쪽 가장자리에는 검은 외곽선을 그리지 않음 (셰이더 _OutlineSkip)
//  ■ 1인칭: 실제 무기는 카메라 앞(뷰모델)에 있으므로, 손 위치에 '그림자 전용' 무기를 두어 바닥에 무기 그림자가 보이게 함
//
// 구조:  플레이어 ─ Sprite (하체, SpriteRenderer + Billboard)
//                     └─ Upper (상체, SpriteRenderer, 피벗 = 엉덩이)
//  * [Tools/플레이어/3. 상·하체 애니메이션 적용]이 Resources/Image/Players/{이름}/ 의 그림과 rig.json으로 자동 구성합니다.
//  * 깊이: Project_D/Sprite Shadow 셰이더의 '발밑 세운 판' 깊이에 작은 앞당김(상체 0.03m, 무기 0.06m)을 줘서
//          하체 < 상체 < 무기 순서로 깨끗하게 겹침
[DefaultExecutionOrder(50)] // Billboard(0) 다음, WeaponVisual(100) 전에
public class PlayerBodyAnimator : MonoBehaviour
{
    public enum Dir { Idle, Up, UpRight, Right, DownRight, Down, DownLeft, Left, UpLeft }

    [Header("연결")]
    public SpriteRenderer lowerRenderer;
    public SpriteRenderer upperRenderer;

    [Header("상체 (Dir 순서: Idle, Up, UpRight, Right, DownRight, Down, DownLeft, Left, UpLeft)")]
    public Sprite[] upperSprites = new Sprite[9];
    [Tooltip("상체 그림 기준 손 위치(미터, 상체 피벗=엉덩이 기준, x 오른쪽 / y 위)")]
    public Vector2[] handOffsets = new Vector2[9];

    [Header("하체")]
    public Sprite[] walkFrames;
    public int standFrame = 1;
    public float walkFps = 10f;
    [Tooltip("하체 그림이 오른쪽을 보고 그려졌으면 체크 (왼쪽 이동 시 반전)")]
    public bool legsFaceRight = true;

    [Header("대기")]
    [Tooltip("멈춘 채로 쏘지 않고 이 시간(초)이 지나면 대기 자세")]
    public float idleDelay = 1.0f;

    [Header("초상화 (HUD·캐릭터 선택용)")]
    public Sprite portrait;

    [Header("겹침 깊이 앞당김 (미터)")]
    public float upperDepthBias = 0.03f;
    public float weaponDepthBias = 0.06f;

    [Header("상·하체 이음새 (외곽선 생략)")]
    [Tooltip("상체 그림에서 엉덩이(이음새) 선의 높이 (0 = 그림 맨 아래, 1 = 맨 위). 메뉴가 자동 설정")]
    [Range(0f, 1f)] public float upperJointV = 0f;
    [Tooltip("하체 그림에서 허리 윗선의 높이 (0 = 맨 아래, 1 = 맨 위). 메뉴가 자동 설정")]
    [Range(0f, 1f)] public float lowerJointV = 1f;
    [Tooltip("상체 이음새 선 아래로 이 폭(텍셀)만큼은 외곽선을 그리지 않음")]
    public float upperJoinTexels = 3f;
    [Tooltip("하체 허리 윗선 위로 이 폭(텍셀)만큼은 외곽선을 그리지 않음")]
    public float lowerJoinTexels = 2f;

    [Header("1인칭 무기 그림자")]
    public bool fpsWeaponShadow = true;

    public Dir CurrentDir { get; private set; }

    private PlayerController pc;
    private PlayerWeaponManager weapons;
    private Billboard board;
    private Camera cam;
    private float walkPhase;
    private bool facingLeft;
    private float lastActiveTime;
    private MaterialPropertyBlock block;
    private readonly List<SpriteRenderer> weaponRenderers = new List<SpriteRenderer>();
    private int weaponChildSignature = -1;
    private static readonly int FeetPosId = Shader.PropertyToID("_FeetPos");
    private static readonly int OutlineSkipId = Shader.PropertyToID("_OutlineSkip");
    private static readonly int OutlineSkipSpanId = Shader.PropertyToID("_OutlineSkipSpan");
    private readonly Dictionary<Sprite, Vector2> waistCache = new Dictionary<Sprite, Vector2>();

    // 1인칭 무기 그림자용 (탑뷰에서 본 무기 그림의 손잡이 기준 위치·크기를 기억해 둠)
    private SpriteRenderer shadowProxy;
    private Vector3 weaponSpriteOffset = new Vector3(0.2f, 0f, 0f); // 손(장착점) → 무기 그림 중심 (미터, 오른쪽 조준 기준)
    private Vector3 weaponSpriteScale = Vector3.zero;
    private bool lastFps;

    // HUD/메뉴용 초상화: 이 컴포넌트가 있으면 지정 초상화, 없으면 빌보드 그림
    public static Sprite GetPortrait(GameObject player)
    {
        if (player == null) return null;
        PlayerBodyAnimator a = player.GetComponentInChildren<PlayerBodyAnimator>(true);
        if (a != null && a.portrait != null) return a.portrait;
        Billboard b = player.GetComponentInChildren<Billboard>(true);
        SpriteRenderer sr = b != null ? b.GetComponent<SpriteRenderer>() : null;
        return sr != null ? sr.sprite : null;
    }

    void Awake()
    {
        pc = GetComponent<PlayerController>();
        weapons = GetComponent<PlayerWeaponManager>();
        if (lowerRenderer != null) board = lowerRenderer.GetComponent<Billboard>();
        lastActiveTime = -999f;
    }

    void LateUpdate()
    {
        if (lowerRenderer == null || upperRenderer == null) return;
        if (cam == null) cam = Camera.main;
        bool fps = pc != null && pc.isFirstPerson;

        // 1인칭에서는 내 몸이 안 보이므로(그림자만) 하체 설정을 상체에도 그대로
        upperRenderer.shadowCastingMode = lowerRenderer.shadowCastingMode;

        bool moving = pc != null && pc.IsMoving;
        if (moving || Input.GetMouseButton(0)) lastActiveTime = Time.time;

        // ---- 조준 방향 (화면 기준) ----
        Vector2 aim = Vector2.right;
        if (cam != null)
        {
            Vector3 sp = cam.WorldToScreenPoint(transform.position);
            aim = (Vector2)Input.mousePosition - new Vector2(sp.x, sp.y);
        }

        // ---- 상체 ----
        Dir dir;
        if (fps) dir = Dir.Right; // 1인칭: 몸은 그림자로만 보이므로 무기를 든 옆모습으로 고정
        else if (!moving && Time.time - lastActiveTime > idleDelay) dir = Dir.Idle;
        else dir = DirFromVector(aim);
        CurrentDir = dir;
        Sprite us = Get(upperSprites, (int)dir);
        if (us != null && upperRenderer.sprite != us) upperRenderer.sprite = us;

        // ---- 하체 ----
        float h = Input.GetAxisRaw("Horizontal");
        if (moving && Mathf.Abs(h) > 0.01f) facingLeft = h < 0f;          // 좌우로 움직이면 그쪽을 봄
        else if (!moving && Mathf.Abs(aim.x) > 4f) facingLeft = aim.x < 0f; // 서 있으면 조준 쪽을 봄
        if (fps) facingLeft = false; // 1인칭: 상체(오른쪽 고정)와 그림자 방향을 맞춤
        if (walkFrames != null && walkFrames.Length > 0)
        {
            Sprite ls;
            if (moving)
            {
                walkPhase = (walkPhase + walkFps * Time.deltaTime) % walkFrames.Length;
                ls = walkFrames[Mathf.Clamp((int)walkPhase, 0, walkFrames.Length - 1)];
            }
            else
            {
                walkPhase = 0f;
                ls = walkFrames[Mathf.Clamp(standFrame, 0, walkFrames.Length - 1)];
            }
            if (ls != null && lowerRenderer.sprite != ls) lowerRenderer.sprite = ls;
        }
        lowerRenderer.flipX = legsFaceRight ? facingLeft : !facingLeft;

        // ---- 무기 손잡이 → 손 ----
        RefreshWeaponRenderers();
        if (!fps && weapons != null && weapons.weaponMountPoint != null)
        {
            Vector2 hand = Get(handOffsets, (int)dir);
            weapons.weaponMountPoint.position = upperRenderer.transform.TransformPoint(hand);
            CacheWeaponPose();
        }
        UpdateWeaponShadow(fps, dir);

        // ---- 겹침 깊이 (발 위치 + 앞당김) ----
        bool valid = !fps && board != null && board.HasFeet;
        Vector3 feet = board != null ? board.FeetWorld : transform.position;
        SetFeet(upperRenderer, feet, valid ? 1f + upperDepthBias : 0f);
        foreach (SpriteRenderer r in weaponRenderers)
            if (r != null)
            {
                // 탄창(Magazine)은 무기 그림 바로 뒤에 (같은 깊이면 깜빡거림)
                float bias = r.name == "Magazine" ? weaponDepthBias - 0.01f                                   // 무기 뒤
                           : (r.name == "MagazineFront" || r.name == "Cover") ? weaponDepthBias + 0.01f   // 무기 앞 (기관총 탄통·덮개)
                           : weaponDepthBias;
                SetFeet(r, feet, valid ? 1f + bias : 0f);
            }

        // ---- 이음새: 상체 아래 / 하체 위 가장자리 외곽선 생략 ----
        // 상체: 허리 아래로 늘어진 털(최대 6텍셀)까지 외곽선 생략, 단 하체가 실제로 있는 허리 폭 안에서만 (옆의 손은 외곽선 유지)
        SetVector(upperRenderer, OutlineSkipId, new Vector4(upperJointV, Mathf.Max(upperJoinTexels, 7f), 0f, 0f));
        SetVector(upperRenderer, OutlineSkipSpanId, WaistSpan());
        SetVector(lowerRenderer, OutlineSkipId, new Vector4(0f, 0f, lowerJointV, lowerJoinTexels));
    }

    void OnDisable()
    {
        if (shadowProxy != null) shadowProxy.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (shadowProxy != null) Destroy(shadowProxy.gameObject);
    }

    // 탑뷰일 때 무기 그림이 손에서 얼마나 떨어져 있고 얼마나 큰지 기억 (1인칭 그림자에 사용)
    private void CacheWeaponPose()
    {
        SpriteRenderer w = MainWeaponRenderer();
        if (w == null) return;
        Transform mount = weapons.weaponMountPoint;
        Transform visual = w.transform.parent != null ? w.transform.parent : w.transform;
        // 손(장착점) → 무기 그림 중심을 '무기가 겨누는 방향' 기준으로 (조준 각도·좌우 반전과 무관하게)
        Vector3 d = w.transform.position - mount.position;
        float flip = visual.lossyScale.y < 0f ? -1f : 1f;
        weaponSpriteOffset = new Vector3(Vector3.Dot(d, visual.right), Vector3.Dot(d, visual.up) * flip, 0f);
        Vector3 ls = w.transform.lossyScale;
        weaponSpriteScale = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), 1f);
    }

    // 1인칭: 손 위치에 그림자만 드리우는 무기 그림을 둠 / 탑뷰: 숨김
    private void UpdateWeaponShadow(bool fps, Dir dir)
    {
        bool want = fps && fpsWeaponShadow;
        SpriteRenderer w = MainWeaponRenderer();

        // 실제 무기 그림: 1인칭에선 카메라 앞이라 그림자를 끔 (엉뚱한 곳에 그림자가 생기지 않게)
        if (fps != lastFps || want)
        {
            var mode = fps ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (SpriteRenderer r in weaponRenderers)
                if (r != null && r.shadowCastingMode != mode) r.shadowCastingMode = mode;
            lastFps = fps;
        }

        if (!want || w == null || w.sprite == null)
        {
            if (shadowProxy != null && shadowProxy.gameObject.activeSelf) shadowProxy.gameObject.SetActive(false);
            return;
        }

        if (shadowProxy == null)
        {
            var go = new GameObject($"{name}_WeaponShadow");
            go.layer = upperRenderer.gameObject.layer;
            shadowProxy = go.AddComponent<SpriteRenderer>();
            shadowProxy.receiveShadows = false;
        }
        if (!shadowProxy.gameObject.activeSelf) shadowProxy.gameObject.SetActive(true);
        shadowProxy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        if (upperRenderer.sharedMaterial != null && shadowProxy.sharedMaterial != upperRenderer.sharedMaterial)
            shadowProxy.sharedMaterial = upperRenderer.sharedMaterial;
        // 그림자는 탑뷰(옆모습) 그림으로 (1인칭 전용 그림은 원근이 들어가 있어 그림자로 부적합)
        WeaponVisual wv = w.GetComponentInParent<WeaponVisual>();
        Sprite shadowSprite = (wv != null && wv.TopDownSprite != null) ? wv.TopDownSprite : w.sprite;
        if (shadowProxy.sprite != shadowSprite) shadowProxy.sprite = shadowSprite;

        // 상체(빌보드) 평면 위, 손 위치에서 오른쪽을 겨눈 모습
        Transform ut = upperRenderer.transform;
        Vector3 hand = ut.TransformPoint(Get(handOffsets, (int)dir));
        Transform pt = shadowProxy.transform;
        pt.rotation = ut.rotation;
        pt.position = hand + ut.right * weaponSpriteOffset.x + ut.up * weaponSpriteOffset.y;
        Vector3 sc = weaponSpriteScale.sqrMagnitude > 0f ? weaponSpriteScale : w.transform.lossyScale;
        pt.localScale = new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), 1f);
    }

    // 무기 그림 중 가장 큰 것(본체)
    private SpriteRenderer MainWeaponRenderer()
    {
        SpriteRenderer best = null;
        float bestArea = -1f;
        foreach (SpriteRenderer r in weaponRenderers)
        {
            if (r == null || r.sprite == null || !r.gameObject.activeInHierarchy) continue;
            Vector2 sz = r.sprite.bounds.size;
            float area = sz.x * sz.y;
            if (area > bestArea) { bestArea = area; best = r; }
        }
        return best;
    }

    // 하체 그림 맨 윗부분(허리)의 가로 범위 → 상체 그림의 u 좌표 (상체 외곽선 생략 범위)
    private Vector4 WaistSpan()
    {
        Sprite ls = lowerRenderer.sprite, us = upperRenderer.sprite;
        if (ls == null || us == null) return Vector4.zero;
        if (!waistCache.TryGetValue(ls, out Vector2 span))
        {
            span = new Vector2(float.MaxValue, float.MinValue);
            Vector2[] v = ls.vertices;
            float top = ls.bounds.max.y, band = ls.bounds.size.y * 0.15f;
            foreach (Vector2 q in v)
                if (q.y >= top - band) { span.x = Mathf.Min(span.x, q.x); span.y = Mathf.Max(span.y, q.x); }
            if (span.x > span.y) span = new Vector2(ls.bounds.min.x, ls.bounds.max.x);
            waistCache[ls] = span;
        }
        float a = span.x, b = span.y;
        if (lowerRenderer.flipX) { float t = -b; b = -a; a = t; }
        float w = us.rect.width / Mathf.Max(1f, us.pixelsPerUnit);
        float pu = us.pivot.x / Mathf.Max(1f, us.rect.width);
        float ox = upperRenderer.transform.localPosition.x;
        return new Vector4(pu + (a - ox) / w, pu + (b - ox) / w, 0f, 0f);
    }

    // 화면 기준 각도 → 8방향
    private static Dir DirFromVector(Vector2 v)
    {
        if (v.sqrMagnitude < 1f) return Dir.Down;
        float a = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg; // 0 = 오른쪽, 90 = 위
        int s = Mathf.RoundToInt(a / 45f);
        s = ((s % 8) + 8) % 8;
        switch (s)
        {
            case 0: return Dir.Right;
            case 1: return Dir.UpRight;
            case 2: return Dir.Up;
            case 3: return Dir.UpLeft;
            case 4: return Dir.Left;
            case 5: return Dir.DownLeft;
            case 6: return Dir.Down;
            default: return Dir.DownRight;
        }
    }

    // 무기를 바꾸면 무기 그림도 같은 셰이더(그림자·외곽선·깊이)로
    private void RefreshWeaponRenderers()
    {
        if (weapons == null || weapons.weaponMountPoint == null) return;
        Transform mount = weapons.weaponMountPoint;
        int sig = mount.childCount;
        for (int i = 0; i < mount.childCount; i++) sig = sig * 31 + mount.GetChild(i).GetInstanceID();
        if (sig == weaponChildSignature) return;
        weaponChildSignature = sig;

        weaponRenderers.Clear();
        foreach (SpriteRenderer r in mount.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (r == lowerRenderer || r == upperRenderer) continue;
            if (upperRenderer.sharedMaterial != null) r.sharedMaterial = upperRenderer.sharedMaterial;
            r.shadowCastingMode = (pc != null && pc.isFirstPerson)
                ? UnityEngine.Rendering.ShadowCastingMode.Off
                : UnityEngine.Rendering.ShadowCastingMode.On;
            weaponRenderers.Add(r);
        }
    }

    private void SetFeet(SpriteRenderer r, Vector3 feet, float w)
    {
        SetVector(r, FeetPosId, new Vector4(feet.x, feet.y, feet.z, w));
    }

    // 다른 값(피격 번쩍임 등)은 그대로 두고 한 값만 바꿈
    private void SetVector(SpriteRenderer r, int id, Vector4 v)
    {
        if (block == null) block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetVector(id, v);
        r.SetPropertyBlock(block);
    }

    private static T Get<T>(T[] arr, int i) => (arr != null && i >= 0 && i < arr.Length) ? arr[i] : default;
}
