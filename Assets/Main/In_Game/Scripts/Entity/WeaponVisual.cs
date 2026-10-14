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
    [Tooltip("빠진 탄창이 바닥에 남아 있는 시간 (초)")]
    public float droppedMagLife = 2f;
    [Header("1인칭 탄창 탈부착 연출")]
    [Tooltip("탄창을 뺄 때: 총을 한 번 강하게 털기 (총구가 아래로 꺾였다가 위로 튐) - 최대 각도 / 시간(초)")]
    public float fpsFlickAngle = 14f;
    public float fpsFlickDuration = 0.22f;
    [Tooltip("탄창을 끼울 때: 손바닥으로 탄창 바닥을 때리는 충격 (총이 위로 툭 튀었다가 가라앉음) - 화면 높이 대비 / 각도 / 시간(초)")]
    public float fpsSlapAmount = 0.035f;
    public float fpsSlapAngle = 5f;
    public float fpsSlapDuration = 0.28f;

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
    }
    [System.Serializable] private class PartsSideJson { public float[] magOffset; }
    [System.Serializable] private class PartsFpsJson { public float[] magOffset; public float[] muzzle; }
    [System.Serializable] private class PartsJson { public string magType; public float ppu = 100f; public PartsSideJson side; public PartsFpsJson fps; }
    private static readonly Dictionary<string, Parts> partsCache = new Dictionary<string, Parts>();

    private Weapon weapon;
    private Parts parts;
    private SpriteRenderer body;
    private SpriteRenderer mag;
    private Transform muzzleT;
    private Vector3 bodySideLocalPos, bodySideLocalScale, muzzleSideLocal;

    // 재장전 상태 (LateUpdate에서 한 번 계산 → Refresh에서 사용)
    private float reloadTilt;   // 0~1 기울기
    private float magDrop;      // 0 = 제자리, 1 = 탄창 길이만큼 아래로
    private bool magHidden;
    private bool wasReloading, droppedThisReload, seatedThisReload;
    private bool magDetached;          // 1인칭: 탄창을 뺀 순간 ~ 다시 끼운 순간 (이때만 분리 그림 사용)
    private float flickStartTime = -999f, slapStartTime = -999f;

    public bool HasMagazineParts => parts != null;
    // HUD 무기 칸용: 옆모습 본체·탄창 그림과 탄창 위치(픽셀), 현재 재장전 상태
    public Sprite SideNoMagSprite => parts != null ? parts.sideNoMag : null;
    public Sprite SideMagSprite => parts != null ? parts.sideMag : null;
    public Vector2 SideMagOffsetPx => parts != null ? parts.sideMagOffset * parts.sideNoMag.pixelsPerUnit : Vector2.zero;
    public float MagDrop => magDrop;
    public bool MagHidden => magHidden;
    // 탑뷰(옆모습) 본체 그림 (1인칭 그림자용)
    public Sprite TopDownSprite { get; private set; }

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
        transform.rotation = camT.rotation * Quaternion.Euler(0f, 0f, angle + climb);

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

        if (parts != null)
        {
            body.sprite = parts.sideNoMag;
            body.transform.localPosition = bodySideLocalPos;
            body.transform.localScale = bodySideLocalScale;
            if (muzzleT != null) muzzleT.localPosition = muzzleSideLocal;
            PlaceMagazine(parts.sideMag, parts.sideMagOffset, 0f);
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
            body.sprite = parts.sideNoMag;
            body.transform.localPosition = bodySideLocalPos;
            body.transform.localScale = bodySideLocalScale;
            if (muzzleT != null) muzzleT.localPosition = muzzleSideLocal;
            PlaceMagazine(parts.sideMag, parts.sideMagOffset, 0f);
            if (mag != null) // 무기 그림 바로 뒤로 (카메라에서 조금 더 멀리)
                mag.transform.localPosition += body.transform.InverseTransformDirection(cam.transform.forward).normalized * 0.02f;
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

    // 탄창 위치: 제자리 + 재장전 중 아래로 빠짐
    private void PlaceMagazine(Sprite sprite, Vector2 offset, float localZ)
    {
        if (mag == null) return;
        mag.sprite = sprite;
        mag.enabled = body.enabled && !magHidden && sprite != null;
        float len = sprite != null ? sprite.bounds.size.y : 0f;
        mag.transform.localPosition = new Vector3(offset.x, offset.y - magDrop * len * 1.2f, localZ);
        mag.transform.localRotation = Quaternion.identity;
        mag.transform.localScale = Vector3.one;
        Color c = mag.color; c.a = 1f; mag.color = c;
    }

    void LateUpdate()
    {
        if (kick > 0f) kick = kick < 0.002f ? 0f : kick * Mathf.Exp(-recoilRecover * Time.deltaTime);
        UpdateReload();
        Refresh();
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
            reloadTilt = Mathf.MoveTowards(reloadTilt, 0f, Time.deltaTime * 6f);
            magDrop = 0f; magHidden = false;
            wasReloading = false; magDetached = false;
            return;
        }
        if (!wasReloading)
        {
            droppedThisReload = false; seatedThisReload = false; wasReloading = true;
            if (parts != null) { magDetached = true; flickStartTime = Time.time; } // 탄창을 빼는 순간: 총 털기
        }

        float r = weapon.ReloadProgress;
        reloadTilt = Mathf.Sin(Mathf.PI * Mathf.Clamp01(r / 0.97f));
        if (parts == null) return; // 한 장짜리 무기는 기울이기만

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
            float t = (r - 0.55f) / 0.3f;
            magDrop = (1f - t) * (1f - t) * 1.3f; magHidden = false;
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

        // 화면 아래쪽으로 툭 빠지며 바닥으로
        Vector3 screenDown = -mt.up * Mathf.Sign(lossy.y); screenDown.y = Mathf.Min(screenDown.y, 0f);
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
        if (parts == null) return;

        // 탑뷰: 탄창 없는 옆모습을 예전과 같은 길이로
        float length = data.spriteLength > 0f ? data.spriteLength : body.bounds.size.x;
        float s = length / Mathf.Max(0.0001f, parts.sideNoMag.bounds.size.x);
        body.sprite = parts.sideNoMag;
        TopDownSprite = parts.sideNoMag;
        body.transform.localScale = new Vector3(s, s, 1f);
        bodySideLocalPos = body.transform.localPosition;
        bodySideLocalScale = body.transform.localScale;

        GameObject go = new GameObject("Magazine");
        go.layer = body.gameObject.layer;
        go.transform.SetParent(body.transform, false);
        mag = go.AddComponent<SpriteRenderer>();
        mag.sharedMaterial = body.sharedMaterial;
        mag.sortingOrder = body.sortingOrder - 1; // 무기 그림 뒤 (탄창 윗부분이 총 안으로 들어간 모습)
        mag.shadowCastingMode = body.shadowCastingMode;
        mag.receiveShadows = body.receiveShadows;
        mag.sprite = parts.sideMag;
        mag.transform.localPosition = parts.sideMagOffset;
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
            if (d != null && d.side != null) p.sideMagOffset = V2(d.side.magOffset) / ppu;
            if (d != null && d.fps != null)
            {
                p.fpsMagOffset = V2(d.fps.magOffset) / ppu;
                p.fpsMuzzle = V2(d.fps.muzzle) / ppu;
            }
            if (p.sideNoMag == null || p.sideMag == null)
            {
                Debug.LogWarning($"[무기 분리 이미지] {root} 의 Side_NoMag / Side_Mag 그림을 찾지 못했습니다. (텍스처 형식이 Sprite인지 확인)");
                p = null;
            }
        }
        partsCache[spriteName] = p;
        return p;
    }

    private static Vector2 V2(float[] a) => (a != null && a.Length >= 2) ? new Vector2(a[0], a[1]) : Vector2.zero;
}
