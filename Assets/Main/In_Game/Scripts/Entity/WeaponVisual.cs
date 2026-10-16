using System.Collections.Generic;
using UnityEngine;

// 무기 이미지 표시 담당.
//  ■ 탑뷰: 무기 이미지가 항상 카메라를 향하면서(빌보드) 화면 위에서 조준점 방향으로 회전합니다.
//          왼쪽을 볼 때는 상하 반전해 무기가 뒤집혀 보이지 않게 합니다.
//  ■ 1인칭: 무기를 카메라 오른쪽 아래에 고정해 "손에 들고 있는" 것처럼 보여줍니다. (FPS 뷰모델)
//  ■ 분리 이미지(선택): Resources/Image/Weapons/Parts/{무기 이미지 이름}/ 폴더가 있으면
//      - 탑뷰  : 탄창 없는 옆모습(Side_NoMag) + 탄창(Side_Mag)을 따로 그림
//      - 1인칭 : 1인칭 전용 그림(FPS_NoMag) + 탄창(FPS_Mag)을 화면 오른쪽 아래에 붙여 그림
//      - 재장전: 무기를 기울이고 → 탄창이 빠져 바닥에 떨어지고 → 새 탄창이 아래에서 올라와 끼워짐
//    (폴더가 없는 무기는 예전처럼 한 장짜리 이미지)
//
// 구조:  Visual (이 스크립트) ─┬─ Sprite (SpriteRenderer)  ─ Magazine (분리 이미지일 때 자동 생성)
//                              └─ Muzzle (총구)  ← 함께 움직여 항상 총구 끝에 위치
//
// ※ 카메라가 이동한 뒤에 위치를 잡아야 흔들림이 없으므로 CameraController(기본 0)보다 늦게 실행합니다.
[DefaultExecutionOrder(100)]
public class WeaponVisual : MonoBehaviour
{
    [Header("탑뷰")]
    [Tooltip("조준 방향이 화면 왼쪽일 때 무기 이미지를 상하 반전합니다.")]
    public bool flipWhenAimingLeft = true;

    [Header("반동 (쏠 때 무기가 뒤로 밀림)")]
    [Tooltip("1회 반동 세기 1일 때 뒤로 밀리는 거리 (미터)")]
    public float recoilDistance = 0.07f;
    [Tooltip("1회 반동 세기 1일 때 총구가 들리는 각도")]
    public float recoilAngle = 7f;
    [Tooltip("반동에서 제자리로 돌아오는 빠르기")]
    public float recoilRecover = 16f;
    private float kick;   // 현재 반동량 (세기 단위)

    [Header("1인칭 전용 그림 (Parts 폴더가 있는 무기)")]
    [Tooltip("그림(캔버스) 높이 = 화면 높이의 이 비율. 모든 1인칭 그림이 같은 캔버스 크기라 총 크기도 같은 배율로 그려짐")]
    public float fpsScreenHeight = 0.62f;
    [Tooltip("총구 높이 = 화면 아래에서 이 비율 (총마다 그림 속 총구 위치가 달라도 총구 높이를 똑같이 맞춤)")]
    [Range(0f, 1f)] public float fpsMuzzleHeight = 0.36f;
    [Tooltip("그림이 화면 오른쪽 밖으로 나가는 비율 (그림 폭 기준). 클수록 오른쪽으로 가고 개머리판이 덜 보임")]
    [Range(0f, 0.5f)] public float fpsRightOverhang = 0.14f;
    [Tooltip("화면 비율 단위로 그림을 옮김 (x: 오른쪽 +, y: 위 +)")]
    public Vector2 fpsScreenOffset = Vector2.zero;
    [Tooltip("카메라에서 그림까지 거리 (미터)")]
    public float fpsDistance = 0.55f;

    [Header("재장전 모션")]
    [Tooltip("재장전 중 무기가 기우는 각도 (탑뷰 / 1인칭)")]
    public float reloadTiltTopDown = 14f;
    public float reloadTiltFps = 12f;
    [Tooltip("재장전 진행도 중 탄창이 빠지는 구간의 끝 (0.15 = 처음 15%)")]
    [Range(0.02f, 0.5f)] public float magOutEnd = 0.075f;
    [Tooltip("탑뷰: 새 탄창이 몸 쪽에서 이 각도만큼 기울어진 채 다가와 탁 끼워짐")]
    public float insertTiltAngle = 30f;
    [Tooltip("빠진 탄창이 바닥에 남아 있는 시간 (초)")]
    public float droppedMagLife = 2f;
    [Tooltip("1인칭 탄피 배출 위치: 총구→탄창 윗부분 선을 이만큼 늘인 곳 (1 = 탄창 바로 위, 클수록 오른쪽 기관부)")]
    public float ejectFpsReach = 1.35f;
    [Header("1인칭 탄창 탈부착 연출")]
    [Tooltip("탄창을 뺄 때: 총을 한 번 강하게 털기 (총구가 아래로 꺾였다가 위로 튐) - 최대 각도 / 시간(초)")]
    public float fpsFlickAngle = 14f;
    public float fpsFlickDuration = 0.22f;
    [Tooltip("탄창을 끼울 때: 손바닥으로 탄창 바닥을 때리는 충격 (총이 위로 툭 튀었다가 가라앉음) - 화면 높이 대비 / 각도 / 시간(초)")]
    public float fpsSlapAmount = 0.035f;
    public float fpsSlapAngle = 5f;
    public float fpsSlapDuration = 0.28f;

    [Header("1인칭 조명 음영 (해 방향·그늘에 따라 무기가 어두워짐)")]
    public bool fpsLighting = true;
    [Tooltip("가장 어두울 때 밝기 (0~1)")]
    [Range(0f, 1f)] public float fpsShadeMin = 0.45f;
    [Tooltip("빛 반대쪽이 어두워지는 정도 (그림 안 음영)")]
    [Range(0f, 1f)] public float fpsShadeGradient = 0.35f;
    [Tooltip("건물·포탑 그늘에 들어갔을 때 밝기 배율")]
    [Range(0f, 1f)] public float fpsShadowDarken = 0.6f;

    // 1인칭 배치값은 PlayerWeaponManager(플레이어)에서 모든 무기에 공통으로 넘겨줍니다.
    private Vector3 fpsOffset = new Vector3(0.3f, -0.25f, 0.6f);
    private Vector3 fpsRotation = new Vector3(0f, -125f, 0f);
    private float fpsScale = 0.45f;

    private Camera cam;
    private Vector3 aimPoint;
    private bool hasAimPoint = false;
    private bool isFirstPerson = false;

    private Vector3 baseLocalPosition;
    private Vector3 baseLocalScale;

    // ---- 분리 이미지 (탄창) ----
    private class Parts
    {
        public Sprite sideNoMag, sideMag, fpsNoMag, fpsMag;
        public Sprite fpsFull;   // 1인칭: 탄창이 결합된 원본 그림 (평소에는 이것을 사용)
        public Vector2 sideMagOffset, fpsMagOffset, fpsMuzzle; // 그림 기준 (유닛)
        // 기관총(탄통) 전용
        public bool belt;               // 재장전: 덮개 열기 → 탄통 교체 → 덮개 닫기
        public bool magInFront;         // 탄창(탄통)을 무기 그림 앞에 그림
        public Sprite sideFull;         // 덮개까지 닫힌 옆모습 (HUD용)
        public Sprite sideCover;        // 상부 덮개
        public Vector2 coverHinge, coverCenter; // 옆모습 그림 기준 (유닛)
        public float coverOpenAngle;    // 열렸을 때 각도 (도, 반시계 +)
        public bool hasSideMuzzle; public Vector2 sideMuzzle;
        // 기관총 1인칭: 덮개가 열린 그림(탄통 있음/없음) + 덮개 그림 (경첩을 축으로 회전)
        public Sprite fpsOpen, fpsOpenNoMag, fpsCover;
        public Vector2 fpsCoverHinge, fpsCoverCenter;
        public float fpsCoverOpenAngle;
        public bool hudOriginal;        // HUD: 재장전 중이 아닐 때는 원본 한 장짜리 그림 사용
        // 개런드(클립 장전식): 노리쇠 후퇴 고정 그림 + 빈 클립 그림. sideMag = 탄 클립
        public bool clip;
        public Sprite sideOpen, sideClipEmpty;  // sideOpen = 노리쇠/슬라이드가 뒤로 간 옆모습 (Side_Open)
        // 권총(슬라이드): 쏠 때마다 슬라이드가 잠깐 뒤로, 탄이 떨어지면 후퇴 고정, 재장전 중에도 후퇴
        public bool slide;
        // 샷건(펌프): 탄창 그림 없음. 쏘고 나서 잠깐 뒤에 펌프를 당겼다 밀기 (cycleDelay 후 cycleTime 동안 열린 그림)
        public bool pump;
        public float cycleDelay, cycleTime = -1f;
        public bool hasLoadPort; public Vector2 loadPort;   // 탄 넣는 구멍 (기관부 아래)
        // 탄창이 빠지고 끼워지는 방향 (그림 기준, 아래쪽). 있으면 회전 없이 이 방향으로 곧게 (권총: 손잡이 기울기)
        public bool hasMagDir; public Vector2 magDir;
    }
    [System.Serializable] private class PartsCoverJson { public float[] hinge; public float[] center; public float openAngle; }
    [System.Serializable] private class PartsSideJson { public float[] magOffset; public bool magInFront; public float[] muzzle; public float[] magDir; public float[] loadPort; public PartsCoverJson cover; }
    [System.Serializable] private class PartsFpsJson { public float[] magOffset; public float[] muzzle; public PartsCoverJson cover; }
    [System.Serializable] private class PartsJson { public string magType; public float ppu = 100f; public bool hudOriginal; public float cycleDelay; public float cycleTime = -1f; public PartsSideJson side; public PartsFpsJson fps; }
    private static readonly Dictionary<string, Parts> partsCache = new Dictionary<string, Parts>();

    private Weapon weapon;
    private Parts parts;
    private SpriteRenderer body;
    private SpriteRenderer mag;
    private SpriteRenderer cover;
    private Transform coverPivot;
    private float coverOpen;           // 0 = 닫힘, 1 = 열림 (기관총 상부 덮개)
    private Transform muzzleT;
    private Vector3 bodySideLocalPos, bodySideLocalScale, muzzleSideLocal;

    // 재장전 상태 (LateUpdate에서 한 번 계산 → Refresh에서 사용)
    private float reloadTilt;   // 0~1 기울기
    private float magDrop;      // 0 = 제자리, 1 = 탄창 길이만큼 아래로
    private float magInsert = -1f; // 새 탄창 끼우는 중 진행도 0~1 (-1 = 아님)
    private bool insertSnapped;
    private bool coverPopped;
    private bool magHidden;
    private bool wasReloading, droppedThisReload, seatedThisReload;
    private bool magDetached;          // 1인칭: 탄창을 뺀 순간 ~ 다시 끼운 순간 (이때만 분리 그림 사용)
    private float flickStartTime = -999f, slapStartTime = -999f;
    // 클립 장전식 (개런드)
    private bool boltOpen;             // 노리쇠 후퇴 고정 (그림 교체)
    private bool clipEjected;          // 지금 클립이 빠져 있음 (빈 클립 배출 ~ 새 클립 장전 완료)
    private float cycleStart = -1f, cycleUntil = -1f;    // 권총·샷건: 이 구간 동안 슬라이드/펌프가 뒤로 (사격 직후)
    private bool slideReleased;        // 권총: 이번 재장전에서 슬라이드를 놓았는지
    // 샷건 (한 발씩 장전)
    private bool pumpStartedEmpty;     // 빈 총으로 장전 시작 → 장전 내내 펌프가 뒤로, 끝나면 앞으로 '철컥'
    private int lastAmmoSeen;
    private float shellPhase = -1f;    // 지금 넣는 한 발의 진행도 0~1 (-1 = 없음)
    private SpriteRenderer shellR;
    [Header("샷건 한 발 장전")]
    [Tooltip("손에 든 탄 길이 (무기 길이 대비)")]
    public float shellLength = 0.065f;
    [Header("슬라이드 (권총)")]
    [Tooltip("쏠 때 슬라이드가 뒤로 가 있는 시간 (초). 연사 간격의 60%를 넘지 않음")]
    public float slideCycleTime = 0.07f;
    [Tooltip("재장전 진행도 중 슬라이드를 놓아 앞으로 보내는 지점 (탄창은 0.85에 끼워짐)")]
    [Range(0.86f, 0.99f)] public float slideReleaseAt = 0.93f;

    // 노리쇠/슬라이드가 뒤로 간 그림을 써야 하는지
    private bool OpenBody => parts != null && parts.sideOpen != null
                             && (boltOpen || (parts.slide && Time.time >= cycleStart && Time.time < cycleUntil));

    // 한 발 쏠 때마다 (PlayerWeaponManager): 권총 슬라이드가 잠깐 뒤로
    public void Cycle(float cooldown)
    {
        if (parts == null || !parts.slide) return;
        if (parts.pump)
        {
            // 펌프: 반동이 조금 가라앉은 뒤 당겼다 밂. 연사 간격 안에 끝나게 줄임
            float delay = Mathf.Max(0f, parts.cycleDelay), time = parts.cycleTime > 0f ? parts.cycleTime : 0.16f;
            float k = Mathf.Min(1f, cooldown * 0.85f / Mathf.Max(0.01f, delay + time));
            cycleStart = Time.time + delay * k;
            cycleUntil = cycleStart + time * k;
            return;
        }
        cycleStart = Time.time;
        cycleUntil = Time.time + Mathf.Min(slideCycleTime, Mathf.Max(0.02f, cooldown * 0.6f));
    }

    public bool HasMagazineParts => parts != null;
    // HUD 무기 칸용: 옆모습 본체·탄창 그림과 탄창 위치(픽셀), 현재 재장전 상태
    public Sprite SideNoMagSprite => parts != null ? (parts.sideFull != null ? parts.sideFull : parts.sideNoMag) : null;
    public bool MagInFront => parts != null && parts.magInFront;
    public Sprite SideMagSprite => parts != null ? parts.sideMag : null;
    public Vector2 SideMagOffsetPx => parts != null ? parts.sideMagOffset * parts.sideNoMag.pixelsPerUnit : Vector2.zero;
    public float MagDrop => magInsert >= 0f ? Mathf.Pow(1f - Mathf.Clamp01(magInsert / InsertSnapEnd), 2f) * 1.3f : magDrop;
    public bool MagHidden => magHidden;
    // HUD: 이 무기는 평소(재장전 중 아님)에 원본 한 장짜리 그림을 씀 (톰슨 계열)
    public bool HudUseOriginal => parts != null && parts.hudOriginal;
    // HUD용 상부 덮개 (기관총): 덮개 없는 본체 + 덮개 그림 + 경첩/중심(픽셀, 본체 가운데 기준) + 현재 열린 각도
    public Sprite SideBodySprite => parts != null ? parts.sideNoMag : null;
    public Sprite SideCoverSprite => parts != null && coverPivot != null ? parts.sideCover : null;
    public Vector2 CoverHingePx => parts != null ? parts.coverHinge * parts.sideNoMag.pixelsPerUnit : Vector2.zero;
    public Vector2 CoverCenterPx => parts != null ? parts.coverCenter * parts.sideNoMag.pixelsPerUnit : Vector2.zero;
    public float CoverAngle => parts != null ? parts.coverOpenAngle * coverOpen : 0f;
    // 탑뷰(옆모습) 본체 그림 (1인칭 그림자용)
    public Sprite TopDownSprite { get; private set; }
    // HUD: 지금 그려야 할 본체 그림이 따로 있으면 (개런드 노리쇠 후퇴 고정) 그 그림, 없으면 null
    public Sprite SideCurrentBodySprite => OpenBody ? parts.sideOpen : null;
    // HUD: 탄창 제자리를 그림 크기 계산에 넣을지 (클립은 총 안에 숨어 있으므로 제외)
    public bool MagFitsInIcon => parts == null || !parts.clip;

    void Awake()
    {
        baseLocalPosition = transform.localPosition;
        baseLocalScale = transform.localScale;
        SetupParts();
    }

    // PlayerWeaponManager가 매 프레임 조준점을 넘겨줍니다.
    public void SetAimPoint(Vector3 worldPoint)
    {
        aimPoint = worldPoint;
        hasAimPoint = true;
    }

    public void SetFirstPersonPose(Vector3 offset, Vector3 rotation, float scale)
    {
        fpsOffset = offset;
        fpsRotation = rotation;
        fpsScale = scale;
    }

    public void SetFirstPerson(bool firstPerson)
    {
        if (isFirstPerson == firstPerson) return;
        isFirstPerson = firstPerson;

        if (!isFirstPerson)
        {
            // 탑뷰로 돌아오면 원래 자리(플레이어 손 위치)로 복귀
            transform.localPosition = baseLocalPosition;
            transform.localScale = baseLocalScale;
        }
    }

    // 한 번 쏠 때마다 (PlayerWeaponManager)
    // ------------------------------------------------------------------
    // 근접무기 휘두르기 (탑뷰): 살짝 들었다가(뒤로) → 빠르게 아래로 베고 → 제자리로. 베는 순간 반달 모양 궤적
    // ------------------------------------------------------------------
    [Header("근접무기 휘두르기 (탑뷰)")]
    public float swingDuration = 0.24f;
    public float swingBackAngle = 55f;     // 들어 올리는 각도
    public float swingForwardAngle = 75f;  // 베는 끝 각도
    public Color slashColor = new Color(1f, 1f, 0.95f, 0.9f);
    private float swingStart = -999f, swingDur = 0.24f;
    private bool slashSpawned = true;
    private const float SwingWindup = 0.18f, SwingStrikeEnd = 0.45f;

    public void Swing(float cooldown)
    {
        swingDur = Mathf.Clamp(Mathf.Min(swingDuration, cooldown * 0.9f), 0.08f, 1f);
        swingStart = Time.time;
        slashSpawned = false;
    }

    // 휘두르기 각도 (오른쪽 조준 기준, +는 위로)
    private float SwingOffset(out float u)
    {
        u = (Time.time - swingStart) / Mathf.Max(0.01f, swingDur);
        if (u < 0f || u >= 1f) return 0f;
        if (u < SwingWindup) { float t = u / SwingWindup; return swingBackAngle * (1f - (1f - t) * (1f - t)); }
        if (u < SwingStrikeEnd)
        {
            float t = (u - SwingWindup) / (SwingStrikeEnd - SwingWindup); t = 1f - (1f - t) * (1f - t) * (1f - t);
            return Mathf.Lerp(swingBackAngle, -swingForwardAngle, t);
        }
        float r = (u - SwingStrikeEnd) / (1f - SwingStrikeEnd); r = r * r * (3f - 2f * r);
        return Mathf.Lerp(-swingForwardAngle, 0f, r);
    }

    public void Kick(float strength)
    {
        kick = Mathf.Min(kick + strength, strength * 1.8f); // 연사해도 무한히 쌓이지 않게
    }

    // 사격 직전에 호출하면 이번 프레임 기준으로 즉시 갱신 (총구 위치 정확도↑)
    public void Refresh()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        if (isFirstPerson)
        {
            if (parts != null && parts.fpsNoMag != null) RefreshFirstPersonImage();
            else RefreshFirstPerson();
        }
        else RefreshTopDown();
    }

    private void RefreshTopDown()
    {
        Vector3 dir = hasAimPoint
            ? aimPoint - transform.position
            : (transform.parent != null ? transform.parent.forward : Vector3.forward);

        // 조준 방향을 화면(카메라) 평면으로 투영해 화면상 각도를 구합니다.
        Transform camT = cam.transform;
        float x = Vector3.Dot(dir, camT.right);
        float y = Vector3.Dot(dir, camT.up);

        float angle = (Mathf.Abs(x) + Mathf.Abs(y) < 0.0001f) ? 0f : Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        // 반동: 총구가 화면 위쪽으로 살짝 들림 (왼쪽을 볼 땐 반대 방향 회전) + 재장전 중 기울임
        float side = x < 0f ? -1f : 1f;
        float climb = (kick * recoilAngle + reloadTilt * reloadTiltTopDown) * side;
        float swing = SwingOffset(out float su);
        climb += swing * side;
        transform.rotation = camT.rotation * Quaternion.Euler(0f, 0f, angle + climb);
        // 베기 시작하는 순간 궤적 (무기 길이만큼의 반달, 조준 방향 중심)
        if (!slashSpawned && su >= SwingWindup)
        {
            slashSpawned = true;
            float len = body != null && body.sprite != null ? body.sprite.bounds.size.x * Mathf.Abs(body.transform.lossyScale.x) : 1f;
            HitEffects.Slash(transform.position - camT.forward * 0.05f, len * 1.05f, angle, side < 0f, slashColor, swingDur * 0.75f);
        }

        Vector3 scale = transform.localScale;
        float sy = Mathf.Abs(scale.y);
        scale.y = (flipWhenAimingLeft && x < 0f) ? -sy : sy;
        transform.localScale = scale;

        // 반동: 발사 반대 방향으로 밀림 (화면상 조준 방향의 반대)
        transform.localPosition = baseLocalPosition;
        if (kick > 0.0001f)
        {
            Vector3 aimScreen = (camT.right * Mathf.Cos(angle * Mathf.Deg2Rad) + camT.up * Mathf.Sin(angle * Mathf.Deg2Rad));
            transform.position -= aimScreen * (kick * recoilDistance);
        }

        if (parts != null) PlaceSideParts(0f);
    }

    // 옆모습 본체 + 탄창(탄통) + 상부 덮개 배치. behindZ: 1인칭에서 '뒤'로 보낼 로컬 z (탑뷰는 깊이 앞당김으로 처리하므로 0)
    private void PlaceSideParts(float behindZ)
    {
        body.sprite = OpenBody ? parts.sideOpen : parts.sideNoMag;
        body.transform.localPosition = bodySideLocalPos;
        body.transform.localScale = bodySideLocalScale;
        if (muzzleT != null) muzzleT.localPosition = muzzleSideLocal;
        PlaceMagazine(parts.sideMag, parts.sideMagOffset, parts.magInFront ? -behindZ : behindZ, true);
        PlaceShell(behindZ);
        if (coverPivot != null)
        {
            coverPivot.localPosition = new Vector3(parts.coverHinge.x, parts.coverHinge.y, -behindZ);
            coverPivot.localRotation = Quaternion.Euler(0f, 0f, parts.coverOpenAngle * coverOpen);
            cover.sprite = parts.sideCover;
            cover.transform.localPosition = parts.coverCenter - parts.coverHinge;
            cover.enabled = body.enabled;
        }
    }

    private void RefreshFirstPerson()
    {
        Transform camT = cam.transform;
        // 반동: 카메라 쪽으로 당겨지고 살짝 들림
        Vector3 recoil = new Vector3(0f, kick * recoilDistance * 0.25f, -kick * recoilDistance * 0.9f);
        transform.position = camT.TransformPoint(fpsOffset + recoil);
        transform.rotation = camT.rotation * Quaternion.Euler(-kick * recoilAngle * 0.6f, 0f, 0f) * Quaternion.Euler(fpsRotation);

        // 부모(플레이어) 크기와 상관없이 일정한 크기가 되도록 월드 기준으로 맞춤
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(
            Mathf.Abs(baseLocalScale.x) * fpsScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            Mathf.Abs(baseLocalScale.y) * fpsScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            Mathf.Abs(baseLocalScale.z) / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));

        // 분리 이미지는 있지만 1인칭 전용 그림이 없는 무기: 옆모습 + 탄창 그대로 (재장전 모션 포함)
        if (parts != null)
        {
            // 탄창은 무기 그림 바로 뒤, 덮개·앞 탄통은 바로 앞 (같은 면이면 깜빡거림)
            float away = Vector3.Dot(body.transform.forward, camT.forward) >= 0f ? 1f : -1f;
            PlaceSideParts(away * 0.002f / Mathf.Max(0.0001f, Mathf.Abs(body.transform.lossyScale.z)));
        }
    }

    // 1인칭 전용 그림: 카메라를 정면으로 바라보는 그림을 화면 오른쪽 아래 모서리에 붙임
    private void RefreshFirstPersonImage()
    {
        Transform camT = cam.transform;
        Sprite s = parts.fpsNoMag;
        Vector2 size = s.bounds.size;

        float halfH = fpsDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * cam.aspect;
        float worldH = 2f * halfH * fpsScreenHeight;
        float k = worldH / Mathf.Max(0.0001f, size.y);       // 그림 1유닛 → 월드 크기
        float worldW = size.x * k;

        // 그림 가운데의 카메라 기준 위치
        //  가로: 오른쪽 끝을 화면 오른쪽 밖으로 조금 내보냄 (개머리판이 덜 보이게)
        //  세로: 그림 속 총구가 화면 아래에서 fpsMuzzleHeight 높이에 오도록 (총마다 총구 높이 통일)
        float cx = halfW - worldW * 0.5f + fpsRightOverhang * worldW + fpsScreenOffset.x * 2f * halfW;
        float muzzleY = -halfH + fpsMuzzleHeight * 2f * halfH;
        float cy = muzzleY - parts.fpsMuzzle.y * k + fpsScreenOffset.y * 2f * halfH;
        cy = Mathf.Min(cy, -halfH + worldH * 0.5f); // 그림 아래쪽이 화면 위로 떠 보이지 않게
        // 반동: 오른쪽 아래·뒤로 밀림 / 재장전: 아래로 내려가며 기울어짐
        cx += kick * worldW * 0.02f;
        cy -= kick * worldH * 0.03f + reloadTilt * worldH * 0.12f;
        float dz = -kick * recoilDistance * 0.5f;
        // 탄창 탈부착 순간의 연출 (그림이 바뀌는 순간을 가려 줌)
        float extraRoll = 0f;
        //  빼기: 총을 한 번 강하게 턺 → 총구가 아래로 꺾였다가(+) 위로 튀고(-) 제자리로
        float ft = Time.time - flickStartTime;
        if (ft >= 0f && ft < fpsFlickDuration)
        {
            float u = ft / fpsFlickDuration;
            float wave = Mathf.Sin(u * 2f * Mathf.PI) * (1f - u);  // 한 번 아래·위
            extraRoll += wave * fpsFlickAngle;
            cy -= Mathf.Abs(wave) * worldH * 0.02f;
        }
        //  끼우기: 탄창 바닥을 손바닥으로 때림 → 총이 순간 위로 툭 튀었다가 짧게 출렁이며 가라앉음
        float st = Time.time - slapStartTime;
        if (st >= 0f && st < fpsSlapDuration)
        {
            const float rise = 0.025f;
            float env = st < rise ? st / rise
                      : Mathf.Exp(-(st - rise) * 16f) * Mathf.Cos((st - rise) * 38f);
            env *= 1f - st / fpsSlapDuration;
            cy += env * fpsSlapAmount * 2f * halfH;
            extraRoll -= env * fpsSlapAngle; // 총구가 위로 들림
        }

        transform.position = camT.position + camT.right * cx + camT.up * cy + camT.forward * (fpsDistance + dz);
        // 총구가 왼쪽 위에 있으므로 시계방향(-) 회전 = 총구가 들림
        transform.rotation = camT.rotation * Quaternion.Euler(0f, 0f, -kick * recoilAngle * 0.35f - reloadTilt * reloadTiltFps + extraRoll);

        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(
            k / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
            k / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
            k / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));

        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = Vector3.one;
        if (muzzleT != null) muzzleT.localPosition = parts.fpsMuzzle;
        if (cover != null) cover.enabled = false;
        if (parts.belt && parts.fpsOpen != null && parts.fpsOpenNoMag != null)
        {
            RefreshFirstPersonBelt(k);
            return;
        }
        if (parts.fpsFull != null && !magDetached)
        {
            // 평소: 탄창이 결합된 원본 그림 한 장 (같은 캔버스라 위치·크기 동일)
            body.sprite = parts.fpsFull;
            if (mag != null) mag.enabled = false;
        }
        else
        {
            body.sprite = s;
            // 탄창은 무기 그림 바로 뒤 (카메라에서 2mm 더 멀리)
            PlaceMagazine(parts.fpsMag, parts.fpsMagOffset, 0.002f / Mathf.Max(0.0001f, k));
        }
    }

    // 기관총 1인칭: 평소엔 완성 그림 한 장. 재장전 중에는 '덮개 열린 그림' 위에 덮개 그림을 경첩 기준으로 회전시키고,
    // 탄통이 빠져 있는 동안은 '덮개 열림 + 탄통 없음' 그림 + 탄통 그림(아래로 빠짐 / 아래에서 올라옴)
    private void RefreshFirstPersonBelt(float k)
    {
        bool reloading = weapon != null && weapon.isReloading;
        bool boxOff = magHidden || magDrop > 0f || magInsert >= 0f;
        if (!reloading || (coverOpen <= 0f && !boxOff))
        {
            body.sprite = parts.fpsFull != null ? parts.fpsFull : parts.fpsNoMag;
            if (mag != null) mag.enabled = false;
            return;
        }
        body.sprite = boxOff ? parts.fpsOpenNoMag : parts.fpsOpen;
        if (boxOff) PlaceMagazine(parts.fpsMag, parts.fpsMagOffset, -0.002f / Mathf.Max(0.0001f, k)); // 탄통은 총 앞
        else if (mag != null) mag.enabled = false;
        if (cover != null && coverPivot != null && parts.fpsCover != null)
        {
            coverPivot.localPosition = new Vector3(parts.fpsCoverHinge.x, parts.fpsCoverHinge.y, -0.003f / Mathf.Max(0.0001f, k));
            coverPivot.localRotation = Quaternion.Euler(0f, 0f, parts.fpsCoverOpenAngle * coverOpen);
            cover.sprite = parts.fpsCover;
            cover.transform.localPosition = parts.fpsCoverCenter - parts.fpsCoverHinge;
            cover.enabled = body.enabled;
        }
    }

    // 새 탄창 끼우기 진행도 중 '탁' 걸리는 지점 (그 뒤는 제자리)
    private const float InsertSnapEnd = 0.72f;

    // 탄창 위치: 제자리 + 재장전 중 아래로 빠짐
    //  tactical = true (옆모습): 새 탄창이 몸 쪽(뒤·아래)에서 기울어진 채 다가와 → 회전하며 빠르게 탁 끼워짐
    //  tactical = false (1인칭 그림): 아래에서 위로 올라와 끼워짐
    private void PlaceMagazine(Sprite sprite, Vector2 offset, float localZ, bool tactical = false)
    {
        if (mag == null) return;
        mag.sprite = sprite;
        mag.enabled = body.enabled && !magHidden && sprite != null;
        float len = sprite != null ? sprite.bounds.size.y : 0f;
        magGravity = ScreenDownLocal();
        MagPose(len, tactical, out Vector2 off, out float rot);
        mag.transform.localPosition = new Vector3(offset.x + off.x, offset.y + off.y, localZ);
        mag.transform.localRotation = Quaternion.Euler(0f, 0f, rot);
        mag.transform.localScale = Vector3.one;
        Color c = mag.color; c.a = 1f; mag.color = c;
    }

    // 빠지는 탄창이 떨어지는 방향 (무기 그림 기준). 총이 어느 쪽을 향하든 화면 아래(중력)로. HUD는 총이 수평이라 (0,-1)
    private Vector2 magGravity = Vector2.down;
    [Tooltip("탄창이 빠질 때 탄창 방향으로 먼저 미끄러져 나오는 길이 (탄창 길이 대비). 그 뒤로는 화면 아래로 떨어짐")]
    public float magSlideOut = 0.35f;

    private Vector2 ScreenDownLocal()
    {
        if (cam == null || body == null) return Vector2.down;
        Vector3 v = body.transform.InverseTransformVector(-cam.transform.up);   // 화면 아래 = 중력 (탑뷰·1인칭 모두)
        Vector2 g = new Vector2(v.x, v.y);
        return g.sqrMagnitude > 1e-6f ? g.normalized : Vector2.down;
    }

    // 빠지는 탄창: 처음엔 탄창 방향(axis)으로 쑥 빠지고, 그 뒤로는 중력 방향으로 떨어짐 (회전한 총에서도 위로 '떨어지지' 않게)
    private Vector2 DropOffset(float dist, float len, Vector2 axis)
    {
        float slide = Mathf.Min(dist, magSlideOut * len);
        return axis * slide + magGravity * (dist - slide);
    }

    // 탄창의 제자리 기준 이동량(len = 탄창 그림 높이, 같은 단위)과 기울기(도)
    private void MagPose(float len, bool tactical, out Vector2 off, out float rot)
    {
        off = DropOffset(magDrop * len * 1.2f, len, Vector2.down);
        rot = 0f;
        if (parts != null && parts.clip)
        {
            // 클립: 위에서 기울어진 채 빠르게 내려와 기관부 위에 멈춤 → 엄지로 꾹 눌러 밀어넣음 (점점 빨라지며 쑥)
            off = Vector2.zero;
            if (magInsert < 0f) return;
            float t = magInsert, lift;
            const float approach = 0.45f;
            if (t < approach)
            {
                float u = t / approach; u = 1f - (1f - u) * (1f - u) * (1f - u);
                lift = Mathf.Lerp(clipStartLift, 1f, u);
                rot = Mathf.Lerp(-12f, 0f, u);
            }
            else
            {
                float u = (t - approach) / (1f - approach); u = u * u;
                lift = 1f - u;
            }
            off = new Vector2(-Mathf.Max(0f, lift - 1f) * 0.25f * len, lift * len);
            return;
        }
        if (parts != null && parts.hasMagDir)
        {
            // 권총: 손잡이 기울기를 따라 곧게 — 손잡이 아래로 쭉 빠지고, 아래에서 손잡이 속으로 밀려 들어감 (회전 없음)
            float dist = magDrop * len * 1.2f;
            off = DropOffset(dist, len, parts.magDir);
            if (magInsert >= 0f)
            {
                float u = Mathf.Clamp01(magInsert / InsertSnapEnd);
                const float reach = 0.55f;   // 이 지점까지 빠르게 다가와 손잡이 입구에 걸침
                if (u < reach) { float k = u / reach; k = 1f - (1f - k) * (1f - k); dist = Mathf.Lerp(1.5f, 0.3f, k) * len; }
                else { float k = (u - reach) / (1f - reach); dist = Mathf.Lerp(0.3f, 0f, k * k) * len; } // 점점 빨라지며 탁
                off = parts.magDir * dist;   // 끼울 때는 손잡이 방향 그대로
            }
            return;
        }
        if (magInsert >= 0f)
        {
            float t = magInsert;
            if (!tactical)
            {
                float u = Mathf.Clamp01(t / InsertSnapEnd);
                off = new Vector2(0f, -(1f - u) * (1f - u) * 1.3f * len * 1.2f);
            }
            else
            {
                Vector2 p0 = new Vector2(-0.9f, -1.3f) * len;   // 몸 쪽 (손잡이 뒤·아래)에서 등장
                Vector2 p1 = new Vector2(-0.12f, -0.3f) * len;  // 탄창 구멍 바로 아래, 기울어진 채 앞쪽을 먼저 걸침
                float tilt = insertTiltAngle;
                if (parts != null && parts.belt)
                {
                    // 기관총 탄통: 몸 쪽(뒤)에서 낮은 각도(약 20°)로 날아와 걸침 (바로 아래에서 올라오지 않게)
                    p0 = new Vector2(-2.4f, -1.15f) * len;
                    p1 = new Vector2(-0.2f, -0.3f) * len;
                    tilt = insertTiltAngle * 0.5f;
                }
                const float approach = 0.5f;
                if (t < approach)
                {
                    float u = t / approach; u = 1f - (1f - u) * (1f - u) * (1f - u); // 빠르게 다가와 멈춤
                    off = Vector2.Lerp(p0, p1, u); rot = -tilt;
                }
                else if (t < InsertSnapEnd)
                {
                    float u = (t - approach) / (InsertSnapEnd - approach); u = u * u * u; // 점점 빨라지며 탁
                    off = Vector2.Lerp(p1, Vector2.zero, u); rot = -tilt * (1f - u);
                }
                else { off = Vector2.zero; rot = 0f; }
            }
        }
    }

    // 탄피 배출구 (월드 좌표): 총열이 아니라 탄창 위 노리쇠 자리
    //  - 탑뷰(옆모습): 탄창 바로 위, 총열 높이. 탄창 그림이 없는 무기는 손잡이에서 총구 쪽으로 20% 지점
    //  - 1인칭 그림: 총구 → 탄창 윗부분을 잇는 선을 탄창 너머(오른쪽)까지 늘인 기관부 자리
    public Vector3 EjectionPortWorld(Vector3 muzzleWorld)
    {
        if (body == null) return Vector3.Lerp(transform.position, muzzleWorld, 0.25f);
        Transform bt = body.transform;
        Vector3 m = bt.InverseTransformPoint(muzzleWorld);
        Vector3 g = bt.InverseTransformPoint(transform.position);
        Vector3 local;
        if (parts != null && isFirstPerson && parts.fpsNoMag != null && parts.fpsMag != null)
        {
            Vector2 magTop = parts.fpsMagOffset + Vector2.up * parts.fpsMag.bounds.extents.y;
            Vector2 p = (Vector2)m + ((magTop - (Vector2)m) * ejectFpsReach);
            local = new Vector3(p.x, p.y, m.z);
        }
        else if (parts != null && !isFirstPerson)
            local = new Vector3(parts.sideMagOffset.x, m.y, m.z);
        else
            local = new Vector3(Mathf.Lerp(g.x, m.x, 0.2f), m.y, m.z);
        return bt.TransformPoint(local);
    }

    // HUD용: 옆모습 탄창의 현재 위치(그림 픽셀, 본체 그림 가운데 기준)와 기울기 — 탑뷰 그림과 같은 움직임
    public void GetSideMagPose(out Vector2 offsetPx, out float angle)
    {
        offsetPx = Vector2.zero; angle = 0f;
        if (parts == null || parts.sideMag == null) return;
        Vector2 g = magGravity; magGravity = Vector2.down;   // HUD 그림은 총이 늘 수평
        MagPose(parts.sideMag.bounds.size.y, true, out Vector2 off, out angle);
        magGravity = g;
        offsetPx = (parts.sideMagOffset + off) * parts.sideNoMag.pixelsPerUnit;
    }

    void LateUpdate()
    {
        if (kick > 0f) kick = kick < 0.002f ? 0f : kick * Mathf.Exp(-recoilRecover * Time.deltaTime);
        UpdateReload();
        Refresh();
        ApplyLighting();
    }

    // ------------------------------------------------------------------
    // 1인칭 조명 음영: 무기 그림은 빛을 받지 않는 그림이라, 해 방향·그늘 여부로 밝기와 음영 방향을 직접 입힘
    // ------------------------------------------------------------------
    private static Light sun;
    private static float nextSunSearch;
    private float shadowK = 1f;
    private bool lightingApplied;
    private static readonly int FpsLightId = Shader.PropertyToID("_FpsLight");
    private MaterialPropertyBlock lightBlock;

    private void ApplyLighting()
    {
        bool on = fpsLighting && isFirstPerson && cam != null;
        if (!on)
        {
            if (lightingApplied) { SetShade(Color.white, Vector4.zero); lightingApplied = false; }
            return;
        }
        if (sun == null && Time.unscaledTime >= nextSunSearch)
        {
            nextSunSearch = Time.unscaledTime + 2f;
            sun = RenderSettings.sun;
            if (sun == null)
                foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && l.isActiveAndEnabled) { sun = l; break; }
        }
        Transform camT = cam.transform;
        Vector3 L = sun != null ? -sun.transform.forward : Vector3.up;   // 빛이 오는 쪽
        float sunI = sun != null ? sun.intensity : 0.6f;
        Color sunC = sun != null ? sun.color : Color.white;

        // 그늘: 카메라에서 해 쪽으로 막히는 것이 있으면 어둡게 (부드럽게 전환)
        bool blocked = sun != null && Physics.Raycast(camT.position, L, 80f, ~((1 << 2) | PlayerMask()), QueryTriggerInteraction.Ignore);
        shadowK = Mathf.MoveTowards(shadowK, blocked ? fpsShadowDarken : 1f, Time.deltaTime * 3f);

        // 무기 옆면(카메라 쪽 + 위쪽을 보는 면)이 빛을 얼마나 받는지
        Vector3 n = (-camT.forward + camT.up * 0.8f).normalized;
        float lambert = Mathf.Max(0f, Vector3.Dot(n, L));
        Color amb = RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Flat ? RenderSettings.ambientLight : RenderSettings.ambientSkyColor;
        float ambL = amb.r * 0.3f + amb.g * 0.59f + amb.b * 0.11f;
        float k = Mathf.Clamp01(ambL + sunI * lambert * 0.8f) * shadowK;
        float shade = Mathf.Lerp(fpsShadeMin, 1f, k);
        Color tint = Color.Lerp(Color.white, sunC, 0.2f * shadowK) * shade; tint.a = 1f;
        // 총구 섬광: 쏘는 순간 무기가 따뜻한 빛으로 확 밝아짐
        float glow = weapon != null ? weapon.MuzzleGlow01 : 0f;
        if (glow > 0f) { Color warm = weapon.muzzleGlowColor; warm.a = 1f; tint = Color.Lerp(tint, warm, glow * 0.85f); }

        // 그림 안 음영 방향: 화면에서 빛이 오는 방향 (정면·뒤에서 오면 위에서 오는 것으로)
        Vector2 d = new Vector2(Vector3.Dot(L, camT.right), Vector3.Dot(L, camT.up));
        d = Vector2.Lerp(Vector2.up, d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.up, Mathf.Clamp01(d.magnitude * 2f));
        if (d.sqrMagnitude < 0.0001f) d = Vector2.up;
        d.Normalize();
        float grad = fpsShadeGradient * Mathf.Lerp(0.4f, 1f, (shadowK - fpsShadowDarken) / Mathf.Max(0.001f, 1f - fpsShadowDarken));
        grad *= 1f - glow * 0.7f;
        SetShade(tint, new Vector4(d.x, d.y, grad, 0f));
        lightingApplied = true;
    }

    private void SetShade(Color tint, Vector4 fpsLight)
    {
        if (lightBlock == null) lightBlock = new MaterialPropertyBlock();
        foreach (SpriteRenderer r in new[] { body, mag, cover, shellR })
        {
            if (r == null) continue;
            Color c = tint; c.a = r.color.a; r.color = c;
            r.GetPropertyBlock(lightBlock);
            lightBlock.SetVector(FpsLightId, fpsLight);
            r.SetPropertyBlock(lightBlock);
        }
    }

    // ------------------------------------------------------------------
    // 재장전 모션 (진행도 0~1)
    //   0.00~0.15 : 무기가 기울고 탄창이 아래로 빠짐 → 0.15에 손을 떠나 바닥으로 떨어짐
    //   0.15~0.55 : 탄창 없음 (새 탄창을 꺼내는 시간)
    //   0.55~0.85 : 새 탄창이 아래에서 올라와 끼워짐
    //   0.85      : 철컥! (작은 반동)
    //   0.85~1.00 : 원래 자세로
    // ------------------------------------------------------------------
    private void UpdateReload()
    {
        bool reloading = weapon != null && weapon.isReloading;
        if (!reloading)
        {
            bool justEnded = wasReloading;
            reloadTilt = Mathf.MoveTowards(reloadTilt, 0f, Time.deltaTime * 6f);
            magDrop = 0f; magHidden = false; coverOpen = 0f; magInsert = -1f;
            wasReloading = false; magDetached = false; shellPhase = -1f;
            if (parts != null && parts.clip) UpdateClipIdle();
            else if (parts != null && parts.slide)
            {
                bool open = weapon.IsGun && weapon.currentAmmo <= 0; // 탄이 떨어지면 슬라이드·펌프 후퇴 고정
                if (parts.pump && justEnded && boltOpen && !open) Kick(0.35f); // 샷건: 장전을 마치거나 멈추면 펌프를 앞으로 '철컥'
                boltOpen = open;
            }
            return;
        }
        if (!wasReloading)
        {
            droppedThisReload = false; seatedThisReload = false; insertSnapped = false; coverPopped = false; wasReloading = true;
            slideReleased = false;
            pumpStartedEmpty = weapon.currentAmmo <= 0; lastAmmoSeen = weapon.currentAmmo;
            if (parts != null)
            {
                magDetached = true;
                if (!parts.belt && !parts.clip) flickStartTime = Time.time; // 탄창을 빼는 순간: 총 털기 (기관총은 총을 천천히 내리기만)
            }
        }

        float r = weapon.ReloadProgress;
        reloadTilt = Mathf.Sin(Mathf.PI * Mathf.Clamp01(r / 0.97f));
        magInsert = -1f; // 끼우는 구간에서만 SetInsert로 켬
        if (parts == null) return; // 한 장짜리 무기는 기울이기만
        if (parts.belt) { UpdateBeltReload(r); return; }
        if (parts.clip) { UpdateClipReload(r); return; }
        if (parts.pump) { UpdatePumpReload(r); return; }
        if (parts.slide)
        {
            // 권총: 재장전 내내 슬라이드 후퇴 → 탄창을 끼운 뒤 슬라이드를 놓으면 '철컥' 앞으로
            boltOpen = r < slideReleaseAt;
            if (!boltOpen && !slideReleased) { slideReleased = true; Kick(0.3f); }
            if (parts.pump) { magHidden = true; return; } // 샷건: 탄창 없음 (펌프만 뒤로 → 장전 끝에 앞으로 '철컥')
        }

        if (r < magOutEnd)
        {
            float t = r / magOutEnd;
            magDrop = t * t; magHidden = false;
        }
        else if (r < 0.55f)
        {
            if (!droppedThisReload) { DropOldMagazine(); droppedThisReload = true; }
            magHidden = true;
        }
        else if (r < 0.85f)
        {
            if (!droppedThisReload) { DropOldMagazine(); droppedThisReload = true; }
            SetInsert((r - 0.55f) / 0.3f);
        }
        else
        {
            magDrop = 0f; magHidden = false;
            if (!seatedThisReload)
            {
                seatedThisReload = true;
                magDetached = false; slapStartTime = Time.time; // 탄창을 끼우는 순간: 바닥을 때리는 충격
                if (!isFirstPerson) Kick(0.35f);
            }
        }
    }

    // ------------------------------------------------------------------
    // 기관총 재장전 (진행도 0~1)
    //   0.00~0.12 : 상부 덮개가 열림
    //   0.12~0.20 : 빈 탄통이 아래로 빠져 바닥으로 떨어짐
    //   0.20~0.55 : 탄통 없음 (새 탄통을 꺼내는 시간)
    //   0.55~0.75 : 새 탄통이 아래에서 올라와 걸림
    //   0.75~0.85 : 덮개를 내려 닫음 → 0.85 철컥!
    // ------------------------------------------------------------------
    private void UpdateBeltReload(float r)
    {
        const float open = 0.12f, boxOut = 0.20f, boxIn0 = 0.55f, boxIn1 = 0.75f, close = 0.85f;
        if (r < open)
        {
            float t = r / open; coverOpen = 1f - (1f - t) * (1f - t); // 빠르게 열리고 끝에서 멈춤
            magDrop = 0f; magHidden = false;
        }
        else if (r < boxOut)
        {
            if (!coverPopped) { coverPopped = true; Kick(0.2f); } // 덮개가 끝까지 열리는 순간 '탁'
            coverOpen = 1f;
            float t = (r - open) / (boxOut - open); magDrop = t * t; magHidden = false;
        }
        else if (r < boxIn0)
        {
            coverOpen = 1f;
            if (!droppedThisReload) { DropOldMagazine(); droppedThisReload = true; }
            magHidden = true;
        }
        else if (r < boxIn1)
        {
            coverOpen = 1f;
            if (!droppedThisReload) { DropOldMagazine(); droppedThisReload = true; }
            SetInsert((r - boxIn0) / (boxIn1 - boxIn0));
        }
        else if (r < close)
        {
            magDrop = 0f; magHidden = false;
            float t = (r - boxIn1) / (close - boxIn1); coverOpen = 1f - t * t; // 점점 빨라지며 탁 닫힘
        }
        else
        {
            coverOpen = 0f; magDrop = 0f; magHidden = false;
            if (!seatedThisReload)
            {
                seatedThisReload = true;
                magDetached = false; slapStartTime = Time.time; // 덮개를 닫는 순간: 충격
                Kick(0.45f);
            }
        }
    }

    // ------------------------------------------------------------------
    // 개런드 (클립 장전식)
    //   평소   : 클립은 총 안 (안 보임). 마지막 탄을 쏘면 노리쇠가 후퇴 고정되고 빈 클립이 '핑' 튕겨 나감
    //   재장전 (진행도 0~1)
    //     0.00       : (탄이 남아 있으면) 노리쇠를 당겨 후퇴 고정 → 남은 클립이 튕겨 나감
    //     0.00~0.40  : 새 클립을 꺼내는 시간
    //     0.40~0.74  : 새 클립이 위에서 내려와 → 엄지로 밀어넣음
    //     0.80       : 노리쇠 전진 '철컥' (원래 그림)
    // ------------------------------------------------------------------
    [Header("클립 장전 (개런드)")]
    [Tooltip("새 클립이 처음 나타나는 높이 (클립 높이 배수, 기관부 위)")]
    public float clipStartLift = 2.2f;
    [Tooltip("튕겨 나가는 빈 클립의 속도 (위로 / 뒤로) 와 회전 속도(도/초)")]
    public float clipEjectUp = 3.4f;
    public float clipEjectBack = 0.9f;
    public float clipEjectSpin = 900f;

    private void UpdateClipIdle()
    {
        magHidden = true; // 클립은 총 안
        bool empty = weapon != null && weapon.IsGun && weapon.currentAmmo <= 0;
        if (empty)
        {
            if (!clipEjected) { clipEjected = true; EjectClip(parts.sideClipEmpty); }
            boltOpen = true;
        }
        else { boltOpen = false; clipEjected = false; }
    }

    private void UpdateClipReload(float r)
    {
        const float insert0 = 0.40f, seat = 0.74f, release = 0.80f;
        magDrop = 0f; magHidden = true;
        if (!clipEjected)
        {
            // 탄이 남은 채 재장전: 노리쇠를 당기면 남은 클립이 튕겨 나감
            clipEjected = true;
            EjectClip(parts.sideMag);
            Kick(0.2f);
        }
        boltOpen = r < release;
        if (r >= insert0 && r < seat)
        {
            magHidden = false;
            magInsert = (r - insert0) / (seat - insert0);
        }
        else if (r >= seat && !insertSnapped)
        {
            insertSnapped = true; // 클립이 끝까지 들어가는 순간 '탁'
            Kick(0.15f);
        }
        if (r >= release && !seatedThisReload)
        {
            seatedThisReload = true; // (clipEjected는 재장전이 끝난 뒤 UpdateClipIdle에서 풂 — 여기서 풀면 남은 시간 동안 새 클립이 다시 튕겨 나감)
            magDetached = false; slapStartTime = Time.time;
            Kick(0.45f); // 노리쇠 전진 '철컥'
        }
    }

    // ------------------------------------------------------------------
    // 샷건: 한 발씩 장전 (진행도 r = 지금 넣는 한 발의 0~1)
    //   0.15~0.60 : 손에 든 탄이 아래에서 탄 넣는 구멍으로 다가옴
    //   0.60~0.88 : 구멍 속으로 밀어 넣음 (총 그림 뒤로 사라짐)
    //   한 발 들어갈 때마다 '탁' (작은 반동). 빈 총에서 시작했으면 내내 펌프가 뒤로 → 끝나면 앞으로
    // ------------------------------------------------------------------
    private void UpdatePumpReload(float r)
    {
        boltOpen = pumpStartedEmpty;
        magHidden = true; magDrop = 0f;
        reloadTilt = 0.45f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(r));   // 한 발마다 살짝 기울였다 돌아옴
        if (weapon.currentAmmo > lastAmmoSeen) Kick(0.12f);
        lastAmmoSeen = weapon.currentAmmo;
        shellPhase = r;
    }

    private void PlaceShell(float behindZ)
    {
        if (shellR == null) return;
        float u = shellPhase;
        bool show = parts.pump && parts.hasLoadPort && weapon != null && weapon.isReloading && body.enabled && u >= 0.15f && u <= 0.88f;
        shellR.enabled = show;
        if (!show) return;
        float len = shellLength * parts.sideNoMag.bounds.size.x;
        Vector2 off;
        if (u < 0.6f)
        {
            float k = (u - 0.15f) / 0.45f; k = 1f - (1f - k) * (1f - k);          // 빠르게 다가와 멈춤
            off = Vector2.Lerp(new Vector2(-3.0f * len, -2.0f * len), new Vector2(0f, -0.7f * len), k);   // 몸 쪽(뒤)에서 약 25° 각도로 날아옴
        }
        else
        {
            float k = (u - 0.6f) / 0.28f; k *= k;                                  // 점점 빨라지며 쑥
            off = Vector2.Lerp(new Vector2(0f, -0.7f * len), new Vector2(0.5f * len, 0.25f * len), k);
        }
        shellR.sprite = HitEffects.CasingSprite(HitEffects.CasingType.Shotgun);
        shellR.transform.localPosition = new Vector3(parts.loadPort.x + off.x, parts.loadPort.y + off.y, behindZ);
        shellR.transform.localRotation = Quaternion.identity;
        shellR.transform.localScale = new Vector3(len, len, 1f);
    }

    // 클립이 기관부 위로 '핑' 튕겨 나감 (빙글빙글 돌며 뒤쪽으로 떨어짐)
    private void EjectClip(Sprite sprite)
    {
        if (cam == null) cam = Camera.main;
        if (sprite == null || body == null || cam == null || !body.enabled) return;
        Transform camT = cam.transform;
        Transform bt = body.transform;
        Vector3 lossy = bt.lossyScale;
        float sy = Mathf.Sign(lossy.y), sx = Mathf.Sign(lossy.x);
        Vector3 pos = bt.TransformPoint(new Vector3(parts.sideMagOffset.x, parts.sideMagOffset.y + sprite.bounds.extents.y, 0f));
        Vector3 scale = new Vector3(Mathf.Abs(lossy.x), lossy.y, 1f);
        Vector3 gunUp = bt.up * sy, gunBack = -bt.right * sx;
        float roll = Vector3.SignedAngle(camT.up, Vector3.ProjectOnPlane(gunUp, camT.forward), camT.forward);
        Vector3 vel = Vector3.up * clipEjectUp + gunUp * (clipEjectUp * 0.35f) + gunBack * clipEjectBack;
        float floorY = pos.y - 1.5f;
        if (Physics.Raycast(pos + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 10f, ~((1 << 2) | PlayerMask()), QueryTriggerInteraction.Ignore))
            floorY = hit.point.y;
        HitEffects.DropDebris(sprite, pos, vel, scale, roll, floorY, droppedMagLife, clipEjectSpin * (sx * sy > 0f ? 1f : -1f));
    }

    // 새 탄창 끼우는 중 (0~1). 탑뷰에서 탄창이 걸리는 순간 '탁' (끼운 뒤 0.85에 한 번 더 '탁')
    private void SetInsert(float t)
    {
        magDrop = 0f; magHidden = false;
        magInsert = Mathf.Clamp01(t);
        if (!insertSnapped && magInsert >= InsertSnapEnd)
        {
            insertSnapped = true;
            if (!isFirstPerson) Kick(0.25f);
        }
    }

    // 빠진 탄창: 그 자리에서 바닥으로 떨어져 잠시 남았다가 사라짐
    private void DropOldMagazine()
    {
        if (mag == null || mag.sprite == null || !mag.enabled || cam == null) return;
        Transform camT = cam.transform;
        Transform mt = mag.transform;
        Vector3 pos = mt.position;

        Vector3 lossy = mt.lossyScale;
        Vector3 scale = new Vector3(Mathf.Abs(lossy.x), lossy.y, 1f);
        float roll = Vector3.SignedAngle(camT.up, Vector3.ProjectOnPlane(mt.up * Mathf.Sign(lossy.y), camT.forward), camT.forward);

        // 화면 아래쪽으로 툭 빠지며 바닥으로 (권총: 손잡이 기울기 방향으로)
        // 이미 화면 아래로 떨어지던 중 → 그대로 이어서 (탄창 방향은 조금만 섞음)
        Vector3 axisDown = parts != null && parts.hasMagDir && mt.parent != null
            ? mt.parent.TransformVector(parts.magDir).normalized
            : -mt.up * Mathf.Sign(lossy.y);
        Vector3 screenDown = Vector3.Lerp(axisDown, -camT.up, 0.75f);
        screenDown.y = Mathf.Min(screenDown.y, 0f);
        Vector3 vel = screenDown.normalized * 1.2f + Vector3.up * 0.6f;

        float floorY = pos.y - 1.5f;
        if (Physics.Raycast(pos + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 10f, ~((1 << 2) | PlayerMask()), QueryTriggerInteraction.Ignore))
            floorY = hit.point.y;
        HitEffects.DropDebris(mag.sprite, pos, vel, scale, roll, floorY, droppedMagLife);
    }

    private static int playerMask = -1;
    private static int PlayerMask()
    {
        if (playerMask < 0) { int l = LayerMask.NameToLayer("Player"); playerMask = l >= 0 ? 1 << l : 0; }
        return playerMask;
    }

    // ------------------------------------------------------------------
    // 분리 이미지 불러오기 (무기 이미지 이름으로)
    // ------------------------------------------------------------------
    private void SetupParts()
    {
        weapon = GetComponentInParent<Weapon>();
        Transform spriteT = transform.Find("Sprite");
        body = spriteT != null ? spriteT.GetComponent<SpriteRenderer>() : GetComponentInChildren<SpriteRenderer>(true);
        if (weapon != null && weapon.muzzle != null) muzzleT = weapon.muzzle;
        else muzzleT = transform.Find("Muzzle");
        if (muzzleT != null) muzzleSideLocal = muzzleT.localPosition;

        if (body != null) TopDownSprite = body.sprite;
        PlayerWeaponData data = weapon != null ? weapon.playerWepData : null;
        if (data == null || body == null) return;
        parts = LoadParts(data.spriteName);
        if (parts == null)
        {
            // 한 장짜리 그림: 그림에서 찾아 둔 총구 끝(muzzles.json)으로 총구를 옮김
            if (muzzleT != null && body.sprite != null && TryTableMuzzle(data.spriteName, out Vector2 px))
            {
                muzzleSideLocal = body.transform.localPosition + Vector3.Scale(body.transform.localScale, (Vector3)(px / body.sprite.pixelsPerUnit));
                muzzleT.localPosition = muzzleSideLocal;
            }
            return;
        }

        // 탑뷰: 탄창 없는 옆모습을 예전과 같은 길이로
        float length = data.spriteLength > 0f ? data.spriteLength : body.bounds.size.x;
        float s = length / Mathf.Max(0.0001f, parts.sideNoMag.bounds.size.x);
        body.sprite = parts.sideNoMag;
        TopDownSprite = parts.sideNoMag;
        body.transform.localScale = new Vector3(s, s, 1f);
        bodySideLocalPos = body.transform.localPosition;
        bodySideLocalScale = body.transform.localScale;
        // 총구 위치가 그림에 지정돼 있으면 그 자리로 (기관총: 양각대 때문에 그림 높이가 달라짐)
        if (parts.hasSideMuzzle)
            muzzleSideLocal = bodySideLocalPos + Vector3.Scale(bodySideLocalScale, (Vector3)parts.sideMuzzle);

        // 탄창: 보통은 무기 그림 뒤 (윗부분이 총 안으로 들어간 모습), 기관총 탄통은 앞
        GameObject go = new GameObject(parts.magInFront ? "MagazineFront" : "Magazine");
        go.layer = body.gameObject.layer;
        go.transform.SetParent(body.transform, false);
        mag = go.AddComponent<SpriteRenderer>();
        mag.sharedMaterial = body.sharedMaterial;
        mag.sortingOrder = body.sortingOrder + (parts.magInFront ? 2 : -1);
        mag.shadowCastingMode = body.shadowCastingMode;
        mag.receiveShadows = body.receiveShadows;
        mag.sprite = parts.sideMag;
        mag.transform.localPosition = parts.sideMagOffset;

        // 샷건: 손에 든 탄 (탄창과 같은 깊이 = 무기 그림 바로 뒤 → 구멍 속으로 사라지는 것처럼)
        if (parts.pump && parts.hasLoadPort)
        {
            GameObject sg = new GameObject("Magazine");   // 이름 'Magazine' = 무기 그림 바로 뒤 깊이 (PlayerBodyAnimator)
            sg.layer = body.gameObject.layer;
            sg.transform.SetParent(body.transform, false);
            shellR = sg.AddComponent<SpriteRenderer>();
            shellR.sharedMaterial = body.sharedMaterial;
            shellR.sortingOrder = body.sortingOrder - 1;
            shellR.shadowCastingMode = body.shadowCastingMode;
            shellR.receiveShadows = body.receiveShadows;
            shellR.enabled = false;
            // 외곽선 끔: 아주 작은 그림이라 외곽선이 전부 덮어 까맣게 보임 → 탄피와 같은 색으로
            MaterialPropertyBlock mb = new MaterialPropertyBlock();
            shellR.GetPropertyBlock(mb);
            mb.SetFloat("_OutlineWidth", 0f);
            shellR.SetPropertyBlock(mb);
        }

        // 기관총 상부 덮개: 앞쪽 경첩(CoverPivot)을 축으로 회전
        if (parts.sideCover != null)
        {
            GameObject pv = new GameObject("CoverPivot");
            pv.layer = body.gameObject.layer;
            pv.transform.SetParent(body.transform, false);
            pv.transform.localPosition = parts.coverHinge;
            coverPivot = pv.transform;
            GameObject cg = new GameObject("Cover");
            cg.layer = body.gameObject.layer;
            cg.transform.SetParent(coverPivot, false);
            cg.transform.localPosition = parts.coverCenter - parts.coverHinge;
            cover = cg.AddComponent<SpriteRenderer>();
            cover.sprite = parts.sideCover;
            cover.sharedMaterial = body.sharedMaterial;
            cover.sortingOrder = body.sortingOrder + 1;
            cover.shadowCastingMode = body.shadowCastingMode;
            cover.receiveShadows = body.receiveShadows;
        }
    }

    private static Parts LoadParts(string spriteName)
    {
        if (string.IsNullOrEmpty(spriteName)) return null;
        if (partsCache.TryGetValue(spriteName, out Parts cached)) return cached;

        Parts p = null;
        string root = $"Image/Weapons/Parts/{spriteName}/";
        TextAsset json = Resources.Load<TextAsset>(root + "parts");
        if (json != null)
        {
            PartsJson d = JsonUtility.FromJson<PartsJson>(json.text);
            float ppu = d != null && d.ppu > 0f ? d.ppu : 100f;
            p = new Parts
            {
                sideNoMag = Resources.Load<Sprite>(root + "Side_NoMag"),
                sideMag = Resources.Load<Sprite>(root + "Side_Mag"),
                fpsNoMag = Resources.Load<Sprite>(root + "FPS_NoMag"),
                fpsMag = Resources.Load<Sprite>(root + "FPS_Mag"),
                fpsFull = Resources.Load<Sprite>(root + "FPS_Full"),
            };
            p.sideOpen = Resources.Load<Sprite>(root + "Side_Open");
            if (d != null && d.magType == "Clip")
            {
                p.clip = true;
                p.sideClipEmpty = Resources.Load<Sprite>(root + "Side_ClipEmpty");
            }
            else if (d != null && d.magType == "Box" && p.sideOpen != null) p.slide = true;
            else if (d != null && d.magType == "Pump" && p.sideOpen != null)
            {
                p.slide = true; p.pump = true;
                p.cycleDelay = d.cycleDelay; p.cycleTime = d.cycleTime;
            }
            if (d != null && d.side != null)
            {
                p.sideMagOffset = V2(d.side.magOffset) / ppu;
                p.magInFront = d.side.magInFront;
                if (d.side.loadPort != null && d.side.loadPort.Length >= 2) { p.hasLoadPort = true; p.loadPort = V2(d.side.loadPort) / ppu; }
                if (d.side.magDir != null && d.side.magDir.Length >= 2 && V2(d.side.magDir).sqrMagnitude > 0.01f)
                { p.hasMagDir = true; p.magDir = V2(d.side.magDir).normalized; }
                if (d.side.muzzle != null && d.side.muzzle.Length >= 2) { p.hasSideMuzzle = true; p.sideMuzzle = V2(d.side.muzzle) / ppu; }
                p.sideFull = Resources.Load<Sprite>(root + "Side_Full");
                p.sideCover = Resources.Load<Sprite>(root + "Side_Cover");
                if (p.sideCover != null && d.side.cover != null)
                {
                    p.coverHinge = V2(d.side.cover.hinge) / ppu;
                    p.coverCenter = V2(d.side.cover.center) / ppu;
                    p.coverOpenAngle = d.side.cover.openAngle;
                }
            }
            p.belt = d != null && d.magType == "Belt";
            p.hudOriginal = d != null && d.hudOriginal;
            if (d != null && d.fps != null)
            {
                p.fpsMagOffset = V2(d.fps.magOffset) / ppu;
                p.fpsMuzzle = V2(d.fps.muzzle) / ppu;
                if (d.fps.cover != null)
                {
                    p.fpsOpen = Resources.Load<Sprite>(root + "FPS_Open");
                    p.fpsOpenNoMag = Resources.Load<Sprite>(root + "FPS_OpenNoMag");
                    p.fpsCover = Resources.Load<Sprite>(root + "FPS_Cover");
                    p.fpsCoverHinge = V2(d.fps.cover.hinge) / ppu;
                    p.fpsCoverCenter = V2(d.fps.cover.center) / ppu;
                    p.fpsCoverOpenAngle = d.fps.cover.openAngle;
                }
            }
            if (p.sideNoMag == null || (p.sideMag == null && !p.pump))
            {
                Debug.LogWarning($"[무기 분리 이미지] {root} 의 Side_NoMag / Side_Mag 그림을 찾지 못했습니다. (텍스처 형식이 Sprite인지 확인)");
                p = null;
            }
        }
        partsCache[spriteName] = p;
        return p;
    }

    // 한 장짜리 무기 그림의 총구 끝 (그림 가운데 기준 픽셀) — Resources/Image/Weapons/muzzles.json
    [System.Serializable] private class MuzzleItem { public string name; public float x, y; }
    [System.Serializable] private class MuzzleTable { public MuzzleItem[] items; }
    private static Dictionary<string, Vector2> muzzleTable;
    private static bool TryTableMuzzle(string spriteName, out Vector2 px)
    {
        px = Vector2.zero;
        if (muzzleTable == null)
        {
            muzzleTable = new Dictionary<string, Vector2>();
            TextAsset ta = Resources.Load<TextAsset>("Image/Weapons/muzzles");
            MuzzleTable t = ta != null ? JsonUtility.FromJson<MuzzleTable>(ta.text) : null;
            if (t != null && t.items != null)
                foreach (MuzzleItem it in t.items)
                    if (it != null && !string.IsNullOrEmpty(it.name)) muzzleTable[it.name] = new Vector2(it.x, it.y);
        }
        return !string.IsNullOrEmpty(spriteName) && muzzleTable.TryGetValue(spriteName, out px);
    }

    private static Vector2 V2(float[] a) => (a != null && a.Length >= 2) ? new Vector2(a[0], a[1]) : Vector2.zero;
}
