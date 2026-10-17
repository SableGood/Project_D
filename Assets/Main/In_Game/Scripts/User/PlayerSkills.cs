using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

// 스킬 (Q / E / Shift / F)  ※ 재능으로 얻고, 얻을 때 키를 고름 (AugmentManager)
//  - 칸 번호는 GameHUD 스킬 칸 순서와 같음: 0 Shift, 1 Q, 2 E, 3 F  (4번 Passive 칸은 나중에 직업 칸)
//  - 스킬 종류는 Augment_Data의 '효과' 열: 은신 / 고폭수류탄 / 파편수류탄,  재사용 대기시간은 '쿨타임' 열
//  - 투척 스킬: 키를 누르면 조준(포물선과 떨어질 곳 표시) → 마우스 왼쪽 클릭으로 던짐. 같은 키를 다시 누르거나 마우스 오른쪽 = 취소
//    조준 중에는 총을 쏘지 않음 (던진 클릭을 뗄 때까지)
//      탑뷰: 커서 위치 / 1인칭: 조준점이 가리키는 바닥 (최대 사거리까지)
//  - PlayerAugments가 자동으로 붙임
[DefaultExecutionOrder(1100)]
public class PlayerSkills : MonoBehaviour
{
    public static PlayerSkills Instance { get; private set; }

    // ===== 스킬 효과 이름 (구글 시트 '효과' 열) =====
    public const string Cloak = "은신", HeGrenade = "고폭수류탄", FragGrenade = "파편수류탄";
    public static bool IsSkillEffect(string effect)
    {
        string e = (effect ?? "").Trim();
        return e == Cloak || e == HeGrenade || e == FragGrenade;
    }
    public static bool IsThrowable(string effect) => effect == HeGrenade || effect == FragGrenade;

    // ===== 칸 =====
    public const int SlotCount = 4;
    public static readonly string[] SlotKeyNames = { "Shift", "Q", "E", "F" };
    private static readonly int[] AutoOrder = { 1, 2, 0, 3 }; // 키를 고르지 않았을 때(무작위 재능 등) 채우는 순서: Q → E → Shift → F
    private static readonly KeyCode[][] SlotKeys =
    {
        new[] { KeyCode.LeftShift, KeyCode.RightShift }, new[] { KeyCode.Q }, new[] { KeyCode.E }, new[] { KeyCode.F }
    };

    // 이번 프레임에 눌린 스킬 키의 칸 번호 (없으면 -1). 키 선택 창에서도 사용
    public static int SlotFromKeyDown()
    {
        for (int i = 0; i < SlotCount; i++)
            foreach (KeyCode k in SlotKeys[i]) if (Input.GetKeyDown(k)) return i;
        return -1;
    }

    // 칸에 표시할 짧은 이름 (아이콘이 생기기 전 그레이박스 글자)
    public static string ShortName(AugmentData a)
    {
        if (a == null) return "";
        switch (a.effect)
        {
            case Cloak: return "은신";
            case HeGrenade: return "고폭";
            case FragGrenade: return "파편";
            default: return a.name;
        }
    }

    public class Skill
    {
        public AugmentData data;
        public float readyTime;     // 이 시간 이후 사용 가능
        public float cooldownUsed;  // 마지막으로 적용된 쿨타임 (칸 표시 비율용)
    }
    private readonly Skill[] slots = new Skill[SlotCount];
    public Skill GetSlot(int i) => i >= 0 && i < SlotCount ? slots[i] : null;
    public bool HasFreeSlot { get { foreach (Skill s in slots) if (s == null) return true; return false; } }
    public bool IsSlotFree(int i) => i >= 0 && i < SlotCount && slots[i] == null;

    [Header("투척")]
    [Tooltip("최대 투척 거리 (m)")]
    public float maxThrowRange = 14f;
    [Tooltip("가장 가까이 던지는 거리 (m)")]
    public float minThrowRange = 1.5f;
    public float throwGravity = 20f;
    [Tooltip("가까이 / 멀리 던질 때 날아가는 시간 (초)")]
    public float minFlightTime = 0.45f, maxFlightTime = 1.1f;
    public Color trajectoryColor = new Color(1f, 0.9f, 0.45f, 0.9f);

    [Header("은신")]
    [Tooltip("은신 중 캐릭터·무기 색 (반투명 대신 색으로 표시)")]
    public Color cloakTint = new Color(0.45f, 0.62f, 1f, 1f);
    [Tooltip("은신 중 화면 전체에 덮는 색")]
    public Color cloakScreenTint = new Color(0.35f, 0.55f, 1f, 0.12f);

    // 기본값 (시트 수치가 비었을 때)
    private const float DefaultHeRadius = 3f, DefaultHeFuse = 4f, DefaultFragRange = 6f, DefaultCloak = 3f;
    private const int DefaultFragCount = 8;

    private PlayerController pc;
    private Health health;
    private PlayerWeaponManager weapons;

    // 투척 조준
    private int aimSlot = -1;
    private LineRenderer arcLine, ringLine;
    private const int ArcPoints = 40, RingPoints = 40;
    private Vector3 aimOrigin, aimVelocity;
    private bool aimValid;

    // 은신
    private float cloakUntil;
    private readonly Dictionary<SpriteRenderer, Color> tinted = new Dictionary<SpriteRenderer, Color>();
    private Image screenTint;

    // HUD 갱신 캐시
    private readonly string[] shownLabel = new string[SlotCount];

    public static bool IsCloaked(Transform t) =>
        Instance != null && t != null && Instance.CloakActive && (t.IsChildOf(Instance.transform) || Instance.transform.IsChildOf(t));
    public bool CloakActive => Time.time < cloakUntil;
    public bool IsAiming => aimSlot >= 0;
    // 사격을 막아야 하는지 (조준 중 / 던진 클릭을 아직 떼지 않음)
    public static bool BlocksFiring => Instance != null && (Instance.IsAiming || Instance.holdFireUntilRelease);
    private bool holdFireUntilRelease;

    void Awake()
    {
        Instance = this;
        pc = GetComponent<PlayerController>();
        health = GetComponent<Health>();
        weapons = GetComponent<PlayerWeaponManager>();
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        CancelAim();
        cloakUntil = 0f;
        RestoreTint();
        if (screenTint != null) screenTint.enabled = false;
    }

    void OnDestroy()
    {
        if (screenTint != null) Destroy(screenTint.canvas.gameObject);
    }

    // ===== 재능에서 호출 =====
    // slot: 고른 칸 (-1 이거나 이미 찬 칸이면 빈 칸에 자동). 돌려주는 값: 배정된 칸 (-1 = 빈 칸 없음)
    public int Grant(AugmentData a, int slot)
    {
        if (a == null) return -1;
        if (!IsSlotFree(slot))
        {
            slot = -1;
            foreach (int i in AutoOrder) if (slots[i] == null) { slot = i; break; }
        }
        if (slot < 0) return -1;
        slots[slot] = new Skill { data = a, readyTime = 0f, cooldownUsed = 0f };
        return slot;
    }

    // 정상화: 재능과 함께 스킬도 모두 잃음
    public void ClearAll()
    {
        CancelAim();
        for (int i = 0; i < SlotCount; i++) slots[i] = null;
        cloakUntil = 0f;
    }

    // ===== 입력 =====
    void Update()
    {
        UpdateHud();
        UpdateCloakScreen();

        bool blocked = AugmentManager.IsOpen || Time.timeScale <= 0f || (health != null && health.IsDead)
                       || Time.frameCount <= AugmentManager.ClosedFrame + 1; // 키 선택 창에서 누른 키로 바로 발동되지 않게
        if (blocked) { CancelAim(); return; }

        if (holdFireUntilRelease && !Input.GetMouseButton(0)) holdFireUntilRelease = false;

        int pressed = SlotFromKeyDown();

        // 투척 조준 중: 왼쪽 클릭 = 던지기, 같은 키 다시 / 오른쪽 클릭 = 취소, 다른 스킬 키 = 그 스킬로 넘어감
        if (aimSlot >= 0)
        {
            if (slots[aimSlot] == null || Input.GetMouseButtonDown(1) || pressed == aimSlot) { CancelAim(); return; }
            if (pressed < 0)
            {
                UpdateAim(slots[aimSlot]);
                if (Input.GetMouseButtonDown(0)) { holdFireUntilRelease = true; Throw(aimSlot); }
                return;
            }
            CancelAim();
        }

        if (pressed < 0) return;
        Skill s = slots[pressed];
        if (s == null || Time.time < s.readyTime) return;

        switch (s.data.effect)
        {
            case Cloak:
                cloakUntil = Time.time + Positive(s.data.value1, DefaultCloak);
                StartCooldown(s);
                break;
            case HeGrenade:
            case FragGrenade:
                aimSlot = pressed;
                UpdateAim(s);
                break;
        }
    }

    private void StartCooldown(Skill s)
    {
        float cd = Mathf.Max(0f, s.data.cooldown);
        if (IsThrowable(s.data.effect)) cd *= PlayerAugments.ThrowCooldownMul;
        s.cooldownUsed = cd;
        s.readyTime = Time.time + cd;
    }

    private static float Positive(float v, float fallback) => v > 0f ? v : fallback;

    // ===== 투척 =====
    private void UpdateAim(Skill s)
    {
        EnsureLines();
        aimOrigin = ThrowOrigin();
        Vector3 target = ThrowTarget(aimOrigin);

        float dist = FlatDistance(aimOrigin, target);
        float t = Mathf.Lerp(minFlightTime, maxFlightTime, Mathf.Clamp01(dist / Mathf.Max(0.1f, maxThrowRange)));
        aimVelocity = (target - aimOrigin) / t + Vector3.up * (0.5f * throwGravity * t);

        // 실제 수류탄과 같은 식으로 그리고, 벽·포탑에 막히면 거기서 끊음
        int env = EnvMask();
        Vector3 land = target;
        int count = 0;
        Vector3 prev = aimOrigin;
        arcLine.positionCount = ArcPoints;
        for (int i = 0; i < ArcPoints; i++)
        {
            float tt = t * i / (ArcPoints - 1);
            Vector3 p = aimOrigin + aimVelocity * tt + Vector3.down * (0.5f * throwGravity * tt * tt);
            if (i > 0 && Physics.Linecast(prev, p, out RaycastHit hit, env, QueryTriggerInteraction.Ignore))
            {
                arcLine.SetPosition(count++, hit.point);
                land = hit.point;
                break;
            }
            arcLine.SetPosition(count++, p);
            prev = p;
        }
        arcLine.positionCount = count;
        arcLine.enabled = true;

        // 떨어질 곳 표시: 고폭 = 폭발 범위, 파편 = 파편이 닿는 거리
        float r = s.data.effect == HeGrenade ? Positive(s.data.value2, DefaultHeRadius) : Positive(s.data.value3, DefaultFragRange);
        r *= PlayerAugments.ExplosionMul;
        for (int i = 0; i < RingPoints; i++)
        {
            float a = i * Mathf.PI * 2f / RingPoints;
            ringLine.SetPosition(i, land + new Vector3(Mathf.Cos(a) * r, 0.06f, Mathf.Sin(a) * r));
        }
        ringLine.enabled = true;

        bool fps = pc != null && pc.isFirstPerson;
        arcLine.widthMultiplier = fps ? 0.03f : 0.07f;
        ringLine.widthMultiplier = fps ? 0.04f : 0.07f;
        aimValid = true;
    }

    private void Throw(int slot)
    {
        Skill s = slots[slot];
        bool ok = aimValid && s != null;
        Vector3 origin = aimOrigin, velocity = aimVelocity;
        CancelAim();
        if (!ok) return;

        float dmgMul = PlayerAugments.DamageMul * PlayerAugments.Power * PlayerAugments.ExplosionMul;
        float sizeMul = PlayerAugments.ExplosionMul;
        int mobs = MobMask(), env = EnvMask();
        float floorY = pc != null ? pc.FloorY : transform.position.y;

        if (s.data.effect == HeGrenade)
            SkillGrenade.Launch(SkillGrenade.Kind.HighExplosive, origin, velocity, throwGravity,
                s.data.value1 * dmgMul, Positive(s.data.value2, DefaultHeRadius) * sizeMul, Positive(s.data.value3, DefaultHeFuse),
                0, 0f, transform, mobs, env, floorY);
        else
            SkillGrenade.Launch(SkillGrenade.Kind.Fragment, origin, velocity, throwGravity,
                s.data.value1 * dmgMul, 0f, 0f,
                Mathf.Max(1, Mathf.RoundToInt(Positive(s.data.value2, DefaultFragCount))), Positive(s.data.value3, DefaultFragRange) * sizeMul,
                transform, mobs, env, floorY);
        StartCooldown(s);
    }

    private void CancelAim()
    {
        aimSlot = -1;
        aimValid = false;
        if (arcLine != null) arcLine.enabled = false;
        if (ringLine != null) ringLine.enabled = false;
    }

    // 던지는 손 위치
    private Vector3 ThrowOrigin()
    {
        Camera cam = Camera.main;
        if (pc != null && pc.isFirstPerson && cam != null)
            return cam.transform.position + cam.transform.forward * 0.4f + cam.transform.right * 0.25f - cam.transform.up * 0.2f;
        CharacterController cc = GetComponent<CharacterController>();
        Vector3 c = cc != null ? cc.bounds.center : transform.position + Vector3.up * 0.5f;
        return c + Vector3.up * 0.3f;
    }

    // 떨어질 곳: 탑뷰 = 커서가 가리키는 바닥, 1인칭 = 조준점이 가리키는 바닥 (하늘을 보면 정면 최대 거리)
    private Vector3 ThrowTarget(Vector3 origin)
    {
        Camera cam = Camera.main;
        float floorY = pc != null ? pc.FloorY : transform.position.y;
        Vector3 me = transform.position;
        Vector3 target;
        if (cam == null) target = me + transform.forward * 5f;
        else
        {
            Ray ray = pc != null && pc.isFirstPerson ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, EnvMask(), QueryTriggerInteraction.Ignore)) target = hit.point;
            else if (new Plane(Vector3.up, new Vector3(0f, floorY, 0f)).Raycast(ray, out float d)) target = ray.GetPoint(d);
            else { Vector3 f = ray.direction; f.y = 0f; target = me + (f.sqrMagnitude > 0.0001f ? f.normalized : transform.forward) * maxThrowRange; }
        }

        // 거리 제한 (수평)
        Vector3 flat = target - me; flat.y = 0f;
        float dist = flat.magnitude;
        if (dist < 0.0001f) { flat = transform.forward; flat.y = 0f; dist = 0f; }
        Vector3 dir = flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
        float clamped = Mathf.Clamp(dist, minThrowRange, maxThrowRange);
        if (!Mathf.Approximately(clamped, dist))
        {
            target = me + dir * clamped;
            target.y = floorY;
            // 그 자리의 실제 바닥 높이
            if (Physics.Raycast(target + Vector3.up * 6f, Vector3.down, out RaycastHit g, 12f, EnvMask(), QueryTriggerInteraction.Ignore)) target = g.point;
        }
        return target;
    }

    private static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    private int MobMask()
    {
        int mask = weapons != null && weapons.CurrentWeapon != null ? weapons.CurrentWeapon.targetLayer.value : 0;
        int mob = LayerMask.GetMask("Mob");
        return mob != 0 ? mob : (mask != 0 ? mask : ~0);
    }

    // 수류탄이 부딪히는 것: 플레이어·몹·UI·Ignore Raycast 를 뺀 전부 (바닥, 벽, 포탑)
    public static int EnvMask()
    {
        int exclude = (1 << 2) | (1 << 5) | LayerMask.GetMask("Player", "Mob");
        return ~exclude;
    }

    private void EnsureLines()
    {
        if (arcLine != null) return;
        Shader sh = Shader.Find("Sprites/Default");
        var mat = new Material(sh);
        arcLine = NewLine("SkillAimArc", mat, trajectoryColor, new Color(trajectoryColor.r, trajectoryColor.g, trajectoryColor.b, trajectoryColor.a * 0.5f));
        ringLine = NewLine("SkillAimRing", mat, trajectoryColor, trajectoryColor);
        ringLine.positionCount = RingPoints;
        ringLine.loop = true;
    }

    private LineRenderer NewLine(string name, Material mat, Color a, Color b)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.sharedMaterial = mat;
        lr.startColor = a; lr.endColor = b;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    // ===== 은신 연출 =====
    void LateUpdate()
    {
        if (CloakActive)
        {
            foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>())
            {
                if (sr == null) continue;
                if (!tinted.ContainsKey(sr)) tinted[sr] = sr.color;
                Color orig = tinted[sr];
                sr.color = new Color(orig.r * cloakTint.r, orig.g * cloakTint.g, orig.b * cloakTint.b, orig.a);
            }
        }
        else if (tinted.Count > 0) RestoreTint();
    }

    private void RestoreTint()
    {
        foreach (var kv in tinted) if (kv.Key != null) kv.Key.color = kv.Value;
        tinted.Clear();
    }

    private void UpdateCloakScreen()
    {
        bool on = CloakActive;
        if (screenTint == null)
        {
            if (!on) return;
            var go = new GameObject("CloakScreenTint");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -2; // HUD 아래
            var rt = new GameObject("Tint", typeof(RectTransform));
            rt.transform.SetParent(go.transform, false);
            var r = (RectTransform)rt.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            screenTint = rt.AddComponent<Image>();
            screenTint.raycastTarget = false;
        }
        screenTint.color = cloakScreenTint;
        if (screenTint.enabled != on) screenTint.enabled = on;
    }

    // ===== HUD =====
    private void UpdateHud()
    {
        GameHUD hud = GameHUD.Instance;
        if (hud == null) return;
        for (int i = 0; i < SlotCount; i++)
        {
            Skill s = slots[i];
            string label = s != null ? ShortName(s.data) : null;
            if (label != shownLabel[i]) { hud.SetSkillSlot(i, label); shownLabel[i] = label; }
            float left = s != null ? Mathf.Max(0f, s.readyTime - Time.time) : 0f;
            float ratio = s != null && s.cooldownUsed > 0f ? left / s.cooldownUsed : 0f;
            hud.SetSkillCooldown(i, ratio, left);
            hud.SetSkillActive(i, i == aimSlot || (s != null && s.data.effect == Cloak && CloakActive));
        }
    }
}
