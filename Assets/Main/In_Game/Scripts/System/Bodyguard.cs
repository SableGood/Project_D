using System.Collections.Generic;
using UnityEngine;

// 대통령 직업의 경호실장 (2명). 플레이어 양옆 뒤를 따라다니며 싸움
//  v183 플레이어 캐릭터처럼 진짜 권총을 듦: 사이보그와 같은 자동 조준(AutoAim) → 조준 → 사격(총구 화염·예광탄·탄피·반동) → 재장전(탄창 교체 그림)
//  - 무기: 대통령이 상점에서 산 권총(HG). 1번 경호실장 = 보유 1번 칸, 2번 = 2번 칸(없으면 1번과 같은 총). 없으면 콜트(W002)
//    강화하면 경호실장 총도 바로 바뀜. 공격력·연사 등 무기 수치와 플레이어 재능 배율이 그대로 적용
//  - 피해·처치는 플레이어 몫 (Weapon.attackerOverride). 재장전 재능 등 플레이어 전용 알림은 안 보냄 (Weapon.companion)
//  - 그림: 플레이어 캐릭터 그림을 어둡게(정장 색) 칠해서 작게. 걸을 때 걷는 그림, 조준 방향을 바라봄
//  - 맞지 않음 (몹의 목표가 되지 않음)
//  - 멀티 대비: 주인(owner)·주인의 무기 목록(pwm)만 봄 (단일 플레이어 검색 없음)
public class Bodyguard : MonoBehaviour
{
    public const float FollowSpeed = 7f, Scale = 0.8f, IdleReload = 1.2f, WalkFps = 10f;
    public const string DefaultWeapon = "W002";
    public static float ReactionMin = 1.0f, ReactionMax = 1.3f;   // 사격 간격 = 무기 간격 × 이 범위 (사람처럼 조금씩 늦게)
    private static readonly Color SuitTint = new Color(0.55f, 0.6f, 0.78f, 1f);
    public static readonly List<Bodyguard> All = new List<Bodyguard>();

    private Transform owner;
    private PlayerWeaponManager pwm;
    private int slot;
    private Vector3 offset, mountOffset;
    private SpriteRenderer body, upper;
    private Sprite[] walkFrames; private int standFrame;
    private PlayerBodyAnimator.PistolSet pistol;   // v184 권총 12방향 상체 (손 포함). 있으면 이 그림으로
    private float walkT;
    private Vector3 lastPos;

    private Transform mount;
    private GameObject gunGo;
    private Weapon gun;
    private WeaponVisual visual;
    private int gunIndex = -1;
    private Health target;
    private float nextShot, idleSince;
    private Vector3 aimDir = Vector3.forward;
    private bool wasReloading;

    // v188 경호실장 수: 처음 2명, 엘리트 상점에서 추가 (최대 5명)
    // v189 자리: 인원에 따라 대통령 둘레에 고르게 (시계 방향 기준, 12시 = 화면 위)
    //  2명 = 9·3시 / 3명 = 12·4·8시 (삼각형) / 4명 = 10·2·4·8시 / 5명 = 오각형 (12시부터 72°씩)
    public const int StartCount = 2, MaxCount = 5;
    public static float Radius = 1.3f;
    private static readonly float[][] Clock =
    {
        new float[] { 0f },
        new float[] { 270f, 90f },
        new float[] { 240f, 120f, 0f },
        new float[] { 300f, 60f, 120f, 240f },
        new float[] { 0f, 72f, 144f, 216f, 288f },
    };

    public static Vector3 SlotOffset(int index, int count)
    {
        float[] set = Clock[Mathf.Clamp(count, 1, Clock.Length) - 1];
        float a = set[Mathf.Clamp(index, 0, set.Length - 1)] * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * Radius;
    }

    // 같은 주인의 경호실장들 중 이 경호실장의 순번 (고용 순서)
    private int IndexAmongOwner(out int count)
    {
        int idx = 0; count = 0;
        foreach (Bodyguard g in All)
        {
            if (g == null || g.owner != owner) continue;
            if (g.slot < slot) idx++;
            count++;
        }
        return idx;
    }

    public static int CountFor(Transform owner)
    {
        int n = 0;
        foreach (Bodyguard g in All) if (g != null && g.owner == owner) n++;
        return n;
    }

    public static void SpawnFor(PlayerWeaponManager pwm)
    {
        if (pwm == null) return;
        for (int i = 0; i < StartCount; i++) Spawn(pwm, i);
    }

    // 한 명 더 (엘리트 상점). 최대 수면 false
    public static bool Add(PlayerWeaponManager pwm)
    {
        if (pwm == null) return false;
        int n = CountFor(pwm.transform);
        if (n >= MaxCount) return false;
        Spawn(pwm, n);
        return true;
    }

    private static void Spawn(PlayerWeaponManager pwm, int i)
    {
        {
            GameObject go = new GameObject($"Bodyguard_{i + 1}");
            Bodyguard g = go.AddComponent<Bodyguard>();
            g.owner = pwm.transform;
            g.pwm = pwm;
            g.slot = i;
            g.offset = SlotOffset(i, Mathf.Max(StartCount, i + 1));
            go.transform.position = pwm.transform.position + g.offset;
            g.lastPos = go.transform.position;
            Transform pm = pwm.weaponMountPoint != null ? pwm.weaponMountPoint : pwm.transform;
            g.mountOffset = (pm.position - pwm.transform.position) * Scale;
            g.nextShot = Time.time + 0.3f + i * 0.2f;
            g.BuildVisual(pwm.GetComponentInChildren<PlayerBodyAnimator>(true));
        }
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    private void BuildVisual(PlayerBodyAnimator pba)
    {
        GameObject s = new GameObject("Sprite");
        s.transform.SetParent(transform, false);
        body = s.AddComponent<SpriteRenderer>();
        if (pba != null && pba.lowerRenderer != null)
        {
            body.sharedMaterial = pba.lowerRenderer.sharedMaterial;
            walkFrames = pba.walkFrames; standFrame = pba.standFrame;
            if (walkFrames != null && walkFrames.Length > 0) body.sprite = walkFrames[Mathf.Clamp(standFrame, 0, walkFrames.Length - 1)];
            s.transform.localScale = pba.lowerRenderer.transform.lossyScale * Scale;
            pistol = pba.GetPistolSet();
            if (pistol != null && pistol.walk.Length > 0) { walkFrames = pistol.walk; body.sprite = walkFrames[Mathf.Clamp(standFrame, 0, walkFrames.Length - 1)]; }
            if (pba.upperRenderer != null && pba.upperSprites != null && pba.upperSprites.Length > 0 && pba.upperSprites[0] != null)
            {
                GameObject u = new GameObject("Upper");
                u.transform.SetParent(s.transform, false);
                u.transform.localPosition = pba.upperRenderer.transform.localPosition;
                if (pistol != null) u.transform.localPosition = new Vector3(0f, pistol.upperY, 0f);
                upper = u.AddComponent<SpriteRenderer>();
                upper.sharedMaterial = pba.upperRenderer.sharedMaterial;
                upper.sprite = pistol != null ? pistol.upper[Mathf.Clamp(pistol.idle, 0, pistol.upper.Length - 1)] : pba.upperSprites[0];
                upper.sortingOrder = body.sortingOrder + 1;
                upper.color = SuitTint;
            }
        }
        body.color = SuitTint;
        s.AddComponent<Billboard>();

        mount = new GameObject("WeaponMount").transform;
        mount.SetParent(transform, false);
        mount.localPosition = mountOffset;
    }

    // 이 경호실장이 들 무기 (목록 번호): 보유 권총 중 내 칸 → 없으면 첫 칸 → 없으면 콜트
    private int WantedWeapon()
    {
        if (pwm == null) return -1;
        var pistols = new List<int>();
        foreach (int o in pwm.Owned)
        {
            PlayerWeaponData d = pwm.DataAt(o);
            if (d != null && (d.Category == "HG" || PlayerWeaponManager.TestModeAllWeapons) && d.UsesAmmo) pistols.Add(o);
        }
        if (pistols.Count > 0) return pistols[slot % pistols.Count];
        return pwm.FindCatalogIndex(DefaultWeapon);
    }

    private void Equip(int index)
    {
        gunIndex = index;
        if (gunGo != null) Destroy(gunGo);
        gun = null; visual = null;
        GameObject prefab = pwm != null && pwm.weaponPrefabs != null && index >= 0 && index < pwm.weaponPrefabs.Length ? pwm.weaponPrefabs[index] : null;
        if (prefab == null) return;
        gunGo = Instantiate(prefab, mount, false);
        gunGo.transform.localPosition = Vector3.zero;
        gunGo.transform.localRotation = Quaternion.identity;
        gunGo.transform.localScale = prefab.transform.localScale * (pwm.weaponWorldScale * Scale);
        gun = gunGo.GetComponent<Weapon>();
        visual = gunGo.GetComponentInChildren<WeaponVisual>();
        if (gun == null) return;
        gun.companion = true;
        gun.attackerOverride = owner;
        gun.aimType = Weapon.AimType.Manual;
        PlayerWeaponData data = pwm.DataAt(index);
        if (data != null) gun.InitPlayerWeapon(data);
        gun.muzzleFlash = true;
        gun.muzzleFlashSize = pwm.muzzleFlashSize * Scale;
        if (gun.targetLayer.value == 0) gun.targetLayer = LayerMask.GetMask("Mob");
        gun.Fired += OnFired;
        // 몸보다 앞에 그림
        int order = (upper != null ? upper.sortingOrder : body != null ? body.sortingOrder : 0) + 1;
        foreach (SpriteRenderer r in gunGo.GetComponentsInChildren<SpriteRenderer>(true)) r.sortingOrder = Mathf.Max(r.sortingOrder, order);
        nextShot = Mathf.Max(nextShot, Time.time + 0.35f);
    }

    void LateUpdate()
    {
        if (owner == null) { Destroy(gameObject); return; }
        // 플레이어 뒤쪽 양옆 (플레이어 회전과 무관하게 화면 기준)
        int idx = IndexAmongOwner(out int cnt);
        offset = SlotOffset(idx, Mathf.Max(1, cnt));   // v189 인원이 늘면 모두 새 자리로
        Vector3 goal = owner.position + offset;
        transform.position = Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-FollowSpeed * Time.deltaTime));
        float speed = Time.deltaTime > 0f ? (transform.position - lastPos).magnitude / Time.deltaTime : 0f;
        lastPos = transform.position;
        Animate(speed);

        int want = WantedWeapon();
        if (want != gunIndex) Equip(want);
        if (gun == null) return;

        // 조준
        Vector3 from = mount.position;
        float range = Mathf.Max(1.5f, gun.attackRange);
        target = AutoAim.Find(from, range, target);
        Vector3 aim;
        if (target != null)
        {
            aim = AutoAim.AimPointOf(target);
            Vector3 d = aim - from; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) aimDir = d.normalized;
        }
        else
        {
            if (speed > 0.5f) { Vector3 mv = goal - transform.position; mv.y = 0f; if (mv.sqrMagnitude > 0.01f) aimDir = mv.normalized; }
            aim = from + aimDir * 4f;
        }
        Face(aim);   // 손 자리(무기 장착점)를 먼저 옮김
        if (visual != null)
        {
            visual.SetAimPoint(aim);
            visual.SetFirstPerson(false);
            visual.Refresh();
        }
        gun.SetOwnerMoving(speed > 0.5f);

        // 재장전 소리
        if (gun.isReloading && !wasReloading) Sfx.CompanionReload();
        wasReloading = gun.isReloading;

        if (Time.timeScale <= 0f || AugmentManager.IsOpen) return;
        if (target != null)
        {
            idleSince = Time.time;
            if (Time.time >= nextShot && !gun.isReloading)
            {
                gun.ManualAttackCommand(aim);
                nextShot = Time.time + gun.EffectiveCooldown * Random.Range(ReactionMin, ReactionMax);
            }
        }
        else if (Time.time - idleSince > IdleReload && gun.playerWepData != null && gun.playerWepData.maxAmmo > 0 && gun.currentAmmo < gun.playerWepData.maxAmmo)
            gun.StartReload();   // 적이 없으면 덜 찬 탄창을 갈아 끼움
    }

    private void Animate(float speed)
    {
        if (body == null || walkFrames == null || walkFrames.Length == 0) return;
        if (speed > 0.5f)
        {
            walkT += Time.deltaTime * WalkFps;
            body.sprite = walkFrames[(int)walkT % walkFrames.Length];
        }
        else { walkT = 0f; body.sprite = pistol != null && pistol.stand != null ? pistol.stand : walkFrames[Mathf.Clamp(standFrame, 0, walkFrames.Length - 1)]; }
    }

    private void Face(Vector3 aim)
    {
        Camera cam = Camera.main;
        if (cam == null || body == null) return;
        Vector3 a = cam.WorldToScreenPoint(aim), o = cam.WorldToScreenPoint(transform.position);
        bool left = a.x < o.x;
        body.flipX = left;
        if (upper == null) return;
        if (pistol != null)
        {
            // v184 권총 12방향: 조준 각도에 맞는 칸 + 그 칸의 손 자리에 총 손잡이
            int f = PlayerBodyAnimator.NearestPistolFrame(pistol, Mathf.Atan2(a.y - o.y, a.x - o.x) * Mathf.Rad2Deg);
            upper.sprite = pistol.upper[f];
            upper.flipX = false;
            mount.position = upper.transform.TransformPoint(pistol.grip[f]);
        }
        else upper.flipX = left;
    }

    // 발사 연출: 슬라이드·반동 + 탄피 + 총소리
    private void OnFired(Weapon w)
    {
        if (w == null || !w.IsGun) return;
        if (visual != null) { visual.Cycle(w.attackCooldown); visual.Kick(1f); }
        Sfx.CompanionFire(w);
        Transform muzzle = w.muzzle != null ? w.muzzle : w.transform;
        Vector3 pos = visual != null ? visual.EjectionPortWorld(muzzle.position) : Vector3.Lerp(w.transform.position, muzzle.position, 0.25f);
        Vector3 fwd = w.LastFireDirection; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = aimDir;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 vel = right * Random.Range(1.2f, 2f) - fwd * Random.Range(0.2f, 0.5f) + Vector3.up * Random.Range(1.6f, 2.4f);
        float size = pwm != null ? pwm.pistolCasingSize : 0.045f;
        HitEffects.EjectCasing(pos, vel, HitEffects.CasingType.Pistol, size * Scale, transform.position.y);
    }

    void OnDestroy() { if (gun != null) gun.Fired -= OnFired; }
}
