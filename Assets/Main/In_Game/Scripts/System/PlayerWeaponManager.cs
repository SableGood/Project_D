using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerWeaponManager : MonoBehaviour
{
    [Header("무기 장착 위치")]
    public Transform weaponMountPoint;

    [Header("테스트용 무기 프리팹 리스트")]
    [Tooltip("Tools/플레이어/2. 테스트: 모든 플레이어 무기 장착 메뉴로 자동으로 채울 수 있습니다.")]
    public GameObject[] weaponPrefabs;

    [Header("1인칭 무기 위치 (모든 무기 공통) - 플레이 중에 조절해 보고 값을 기록해 두세요")]
    [Tooltip("카메라 기준 위치 (x: 오른쪽, y: 위, z: 앞). x를 줄이면 몸 안쪽으로, z를 줄이면 몸 가까이로 옵니다.")]
    public Vector3 fpsHoldOffset = new Vector3(0.16f, -0.2f, 0.42f);
    [Tooltip("카메라 기준 회전. Y를 -90에 가깝게 하면 총구가 정면을 향하고, -180에 가깝게 하면 옆모습이 보입니다. Z는 무기 기울기.")]
    public Vector3 fpsHoldRotation = new Vector3(0f, -108f, -4f);
    [Tooltip("1인칭에서의 무기 크기 배율 (탑뷰 크기 기준)")]
    public float fpsHoldScale = 0.4f;

    [Header("발사 연출 (총기만, 근접무기 제외)")]
    [Tooltip("총구 화염 크기 (미터). 포탑(0.7)보다 작게")]
    public float muzzleFlashSize = 0.32f;
    [Tooltip("반동 세기 배율 (무기 위력에 따라 자동 계산된 값에 곱함)")]
    public float recoilMultiplier = 1f;
    [Tooltip("탄피 배출")]
    public bool ejectCasings = true;
    [Tooltip("탄피 길이 (미터): 권총탄(권총·기관단총) / 소총탄(소총·기관총·저격총·전투소총) / 산탄")]
    public float pistolCasingSize = 0.045f;
    public float rifleCasingSize = 0.075f;
    public float shotgunCasingSize = 0.08f;

    [Header("무기 크기 (모든 캐릭터 공통)")]
    [Tooltip("탑뷰에서 무기 그림의 실제 크기 배율. 캐릭터 크기(루트 스케일)와 상관없이 이 크기로 맞춤")]
    public float weaponWorldScale = 1.56f;

    [Header("조준")]
    [Tooltip("1인칭 조준 레이가 무시할 레이어 (자기 자신 등)")]
    public LayerMask tpsAimIgnoreLayers;

    private int currentWeaponIndex = 0;

    // ★ 무기별 남은 탄약 기억 (무기 교체로 탄창이 공짜로 채워지는 버그 방지). -1 = 아직 안 든 무기
    private int[] savedAmmo;
    private GameObject currentWeaponInstance;
    private Weapon currentWeapon;
    private WeaponVisual currentVisual;
    private PlayerController playerController;
    private TowerBuilder towerBuilder;

    void Start()
    {
        playerController = GetComponent<PlayerController>();
        towerBuilder = GetComponent<TowerBuilder>();

        if (tpsAimIgnoreLayers.value == 0) tpsAimIgnoreLayers = LayerMask.GetMask("Player");

        // 하단 통합 HUD (무기·탄약 카드 포함)
        GameHUD.Ensure();

        EnsureMountPoint();

        if (weaponPrefabs != null && weaponPrefabs.Length > 0)
        {
            EquipWeapon(0);
        }
    }

    void Update()
    {
        if (weaponPrefabs == null || weaponPrefabs.Length == 0) return;

        // 마우스 휠 무기 교체
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0f)
        {
            EquipWeapon((currentWeaponIndex + 1) % weaponPrefabs.Length);
        }
        else if (scroll < 0f)
        {
            EquipWeapon((currentWeaponIndex - 1 + weaponPrefabs.Length) % weaponPrefabs.Length);
        }

        if (currentWeapon == null) return;

        // 조준점 계산 → 무기 이미지 방향 갱신 (사격 여부와 무관하게 항상)
        Vector3 aimPoint = GetAimPoint();
        if (currentVisual != null)
        {
            currentVisual.SetAimPoint(aimPoint);
            currentVisual.SetFirstPersonPose(fpsHoldOffset, fpsHoldRotation, fpsHoldScale);
            currentVisual.SetFirstPerson(playerController != null && playerController.isFirstPerson);
            currentVisual.Refresh();
        }

        // 총구 화염 크기: 1인칭에서는 카메라 앞 무기 크기에 맞춤
        currentWeapon.muzzleFlashSize = muzzleFlashSize * ((playerController != null && playerController.isFirstPerson) ? fpsHoldScale : 1f);

        // 이동 중이면 이동 탄퍼짐 적용
        currentWeapon.SetOwnerMoving(playerController != null && playerController.IsMoving);

        if (currentWeapon.playerWepData == null) return;

        // R키 수동 장전
        if (Input.GetKeyDown(KeyCode.R))
        {
            currentWeapon.StartReload();
        }

        // 타워 건설 중(또는 방금 건설한 클릭)에는 사격하지 않음
        if (towerBuilder != null && towerBuilder.BlocksFiring) return;

        // 탑뷰에서 UI(버튼 등) 위를 클릭할 때는 사격하지 않음
        bool firstPerson = playerController != null && playerController.isFirstPerson;
        if (!firstPerson && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        // 발사 방식(Auto/Semi)
        if (currentWeapon.playerWepData.IsAuto)
        {
            // 누르고 있으면 연사 (쿨타임은 Weapon 내부에서 처리)
            if (Input.GetMouseButton(0)) currentWeapon.ManualAttackCommand(aimPoint);
        }
        else
        {
            // 누를 때마다 단발
            if (Input.GetMouseButtonDown(0)) currentWeapon.ManualAttackCommand(aimPoint);
        }
    }

    public void EquipWeapon(int index)
    {
        if (weaponPrefabs == null || index < 0 || index >= weaponPrefabs.Length || weaponPrefabs[index] == null) return;

        EnsureAmmoSlots();

        if (currentWeaponInstance != null)
        {
            // 들고 있던 무기의 남은 탄약 저장 (재장전 도중 교체하면 재장전은 취소됨)
            if (currentWeapon != null && currentWeaponIndex < savedAmmo.Length)
                savedAmmo[currentWeaponIndex] = currentWeapon.currentAmmo;
            Destroy(currentWeaponInstance);
        }

        EnsureMountPoint();

        currentWeaponInstance = Instantiate(weaponPrefabs[index], weaponMountPoint, false);
        currentWeaponInstance.transform.localPosition = Vector3.zero;
        currentWeaponInstance.transform.localRotation = Quaternion.identity;
        // 무기 크기 통일: 캐릭터 크기(원숭이 0.7, 오랑우탄 1.3 …)를 상쇄하고 공통 배율로
        float ownerScale = Mathf.Max(0.0001f, Mathf.Abs(weaponMountPoint.lossyScale.x));
        currentWeaponInstance.transform.localScale = weaponPrefabs[index].transform.localScale * (weaponWorldScale / ownerScale);

        currentWeapon = currentWeaponInstance.GetComponent<Weapon>();
        currentVisual = currentWeaponInstance.GetComponentInChildren<WeaponVisual>();
        if (currentWeapon != null) currentWeapon.Fired += OnWeaponFired;

        if (currentWeapon != null)
        {
            currentWeapon.aimType = Weapon.AimType.Manual;

            // ★ 무기 데이터: 프리팹에 연결된 데이터 우선, 없으면 프리팹 이름(=코드명)으로 검색
            PlayerWeaponData data = currentWeapon.playerWepData;
            if (data == null)
            {
                string weaponID = weaponPrefabs[index].name;
                data = Resources.Load<PlayerWeaponData>($"Data/Weapons/PlayerWeapons/{weaponID}");
            }

            if (data != null)
            {
                currentWeapon.InitPlayerWeapon(data);
                // 총기: 작은 총구 화염 (근접무기는 없음)
                currentWeapon.muzzleFlash = data.UsesAmmo;

                // 전에 들었던 무기라면 남은 탄약 복원
                if (data.UsesAmmo && savedAmmo[index] >= 0)
                    currentWeapon.currentAmmo = Mathf.Min(savedAmmo[index], data.maxAmmo);
                Debug.Log($"무기 장착: [{data.weaponID}] {data.inGameName} / 탄약: {(data.UsesAmmo ? currentWeapon.currentAmmo.ToString() : "∞")}");
            }
            else
            {
                Debug.LogWarning($"[경고] '{weaponPrefabs[index].name}' 무기 데이터를 찾을 수 없습니다. Tools/CSV 데이터 생성/3. 무기 를 실행하세요.");
            }
        }

        currentWeaponIndex = index;
    }

    // ---- 발사 연출: 반동(무기 밀림 + 크로스헤어 요동) + 탄피 ----
    private void OnWeaponFired(Weapon w)
    {
        if (w == null) return;
        if (!w.IsGun)
        {
            // 근접무기: 휘두르기 + 베기 궤적 (탑뷰만. 1인칭은 아직 없음)
            if (currentVisual != null && !(playerController != null && playerController.isFirstPerson))
                currentVisual.Swing(w.attackCooldown);
            return;
        }
        float strength = RecoilStrength(w.playerWepData) * recoilMultiplier;

        if (currentVisual != null) { currentVisual.Cycle(w.attackCooldown); currentVisual.Kick(strength); } // 권총 슬라이드 + 반동
        PlayerHUD.Recoil(strength);
        if (ejectCasings) EjectCasing(w, strength);
    }

    // 한 발의 위력(공격력 × 탄 수)이 클수록 반동이 큼. 권총(18) ≈ 1.2, 기관단총 ≈ 0.75, 저격·산탄 ≈ 2
    private static float RecoilStrength(PlayerWeaponData d)
    {
        if (d == null) return 1f;
        float power = Mathf.Max(1f, d.damage) * Mathf.Max(1, d.projectileCount);
        return Mathf.Clamp(Mathf.Sqrt(power / 12f), 0.6f, 2.2f);
    }

    private void EjectCasing(Weapon w, float strength)
    {
        Transform muzzle = w.muzzle != null ? w.muzzle : w.transform;
        // 탄피 배출구 = 탄창 위 노리쇠 자리 (무기 그림이 계산)
        Vector3 pos = currentVisual != null ? currentVisual.EjectionPortWorld(muzzle.position)
                                            : Vector3.Lerp(w.transform.position, muzzle.position, 0.25f);

        Vector3 fwd = w.LastFireDirection; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd); // 총의 오른쪽으로 배출
        Vector3 vel = right * Random.Range(1.4f, 2.2f) - fwd * Random.Range(0.2f, 0.6f) + Vector3.up * Random.Range(1.8f, 2.6f);

        HitEffects.CasingType type = CasingTypeOf(w.playerWepData);
        float size = type == HitEffects.CasingType.Shotgun ? shotgunCasingSize
                   : type == HitEffects.CasingType.Rifle ? rifleCasingSize : pistolCasingSize;
        HitEffects.EjectCasing(pos, vel, type, size * Random.Range(0.95f, 1.05f), FloorHeight());
    }

    // 무기 종류(CSV '역할')로 탄피 종류 결정: 산탄총 → 산탄, 권총·기관단총 → 권총탄, 그 외(소총·기관총·저격총·전투소총) → 소총탄
    private static HitEffects.CasingType CasingTypeOf(PlayerWeaponData d)
    {
        if (d == null) return HitEffects.CasingType.Rifle;
        string role = d.role ?? "";
        if (d.projectileCount > 1 || role.Contains("산탄")) return HitEffects.CasingType.Shotgun;
        if (role.Contains("권총") || role.Contains("기관단총")) return HitEffects.CasingType.Pistol;
        return HitEffects.CasingType.Rifle;
    }

    private CharacterController cc;
    private float FloorHeight()
    {
        if (cc == null) cc = GetComponent<CharacterController>();
        return cc != null ? cc.bounds.min.y : transform.position.y;
    }

    // 현재 들고 있는 무기 (HUD 등에서 탄약 표시용)
    public Weapon CurrentWeapon => currentWeapon;
    public int CurrentIndex => currentWeaponIndex;
    public int WeaponCount => weaponPrefabs != null ? weaponPrefabs.Length : 0;

    // 인스펙터에서 컴포넌트 우클릭 → 이 메뉴로 1인칭 무기 위치를 기본값으로 되돌릴 수 있습니다.
    [ContextMenu("1인칭 무기 위치 기본값으로")]
    private void ResetFirstPersonHold()
    {
        fpsHoldOffset = new Vector3(0.16f, -0.2f, 0.42f);
        fpsHoldRotation = new Vector3(0f, -108f, -4f);
        fpsHoldScale = 0.4f;
    }

    private void EnsureAmmoSlots()
    {
        int count = weaponPrefabs != null ? weaponPrefabs.Length : 0;
        if (savedAmmo != null && savedAmmo.Length == count) return;
        savedAmmo = new int[count];
        for (int i = 0; i < count; i++) savedAmmo[i] = -1;
    }

    private void EnsureMountPoint()
    {
        if (weaponMountPoint != null) return;
        Transform found = transform.Find("WeaponMountPoint");
        weaponMountPoint = found != null ? found : transform;
    }

    private Vector3 GetAimPoint()
    {
        Camera cam = Camera.main;
        if (cam == null) return transform.position + transform.forward * 10f;

        if (playerController != null && playerController.isFirstPerson)
        {
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, ~tpsAimIgnoreLayers, QueryTriggerInteraction.Ignore)) return hit.point;
            return ray.GetPoint(100f);
        }
        else
        {
            // 탑뷰: 마우스가 가리키는 지점을 "무기 높이"의 평면에서 구합니다.
            // (바닥 높이로 구하면 탄이 아래로 꺾여 나가므로)
            float aimHeight = weaponMountPoint != null ? weaponMountPoint.position.y : transform.position.y;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Plane aimPlane = new Plane(Vector3.up, new Vector3(0f, aimHeight, 0f));
            if (!aimPlane.Raycast(ray, out float distance)) return transform.position + transform.forward * 10f;
            Vector3 planePoint = ray.GetPoint(distance);

            // 커서가 몹 '그림' 위에 있으면 그 몹의 '가리킨 부위'를 조준.
            // 탑뷰의 몹 그림은 카메라 쪽으로 세워져 있어서, 어깨·머리를 가리키면 무기 높이 평면 위의 점은
            // 몹 뒤쪽 바닥으로 잡히고 → 탄이 몹 옆으로 비껴감. 그래서 화면에 보이는 그림 기준으로 판정하고,
            // 그림 위의 커서 지점(머리면 머리 높이)을 몹 히트박스 안으로 당겨서 조준점으로 씀 → 탄이 가리킨 부위로 날아가고 반드시 맞음
            if (PickMobUnderCursor(cam, ray, planePoint, out Vector3 mobPoint)) return mobPoint;
            return planePoint;
        }
    }

    private readonly Collider[] pickHits = new Collider[32];
    private const float PickSearchRadius = 8f; // 커서 아래 평면 지점에서 이 거리 안의 몹만 검사 (큰 몹의 머리까지 포함)

    private bool PickMobUnderCursor(Camera cam, Ray ray, Vector3 planePoint, out Vector3 aimPoint)
    {
        aimPoint = planePoint;
        if (currentWeapon == null || currentWeapon.targetLayer.value == 0) return false;
        Vector2 mouse = Input.mousePosition;
        int n = Physics.OverlapSphereNonAlloc(planePoint, PickSearchRadius, pickHits, currentWeapon.targetLayer, QueryTriggerInteraction.Ignore);
        Collider best = null;
        SpriteRenderer bestSprite = null;
        float bestDepth = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Health hp = pickHits[i].GetComponentInParent<Health>();
            if (hp == null) continue;
            Billboard board = hp.GetComponentInChildren<Billboard>();
            SpriteRenderer sr = board != null ? board.GetComponent<SpriteRenderer>() : null;
            if (sr == null || !sr.enabled || sr.sprite == null) continue;
            float depth = cam.WorldToScreenPoint(hp.transform.position).z;
            if (depth >= bestDepth || !ScreenHitsSprite(cam, sr, mouse)) continue;
            bestDepth = depth; // 여러 마리가 겹쳐 보이면 카메라에 가장 가까운(앞에 그려진) 몹
            best = pickHits[i];
            bestSprite = sr;
        }
        if (best == null) return false;

        // 커서가 가리킨 그림 위의 3D 지점 (그림이 놓인 평면과 커서 레이의 교점)
        Vector3 onSprite = best.bounds.center;
        Plane spritePlane = new Plane(bestSprite.transform.forward, bestSprite.transform.position);
        if (spritePlane.Raycast(ray, out float d)) onSprite = ray.GetPoint(d);
        // 히트박스 안의 가장 가까운 점 (머리를 가리키면 히트박스 위쪽, 발을 가리키면 아래쪽)
        aimPoint = best.ClosestPoint(onSprite);
        // 조금 더 몹 중심 쪽으로 넣어서 판정 레이가 표면에서 끝나 빗나가는 일이 없게
        Vector3 c = best.bounds.center;
        aimPoint += new Vector3(c.x - aimPoint.x, 0f, c.z - aimPoint.z) * 0.5f;
        return true;
    }

    // 화면의 한 점이 스프라이트의 실제 그림(타이트 메시 삼각형) 안에 있는지
    private static bool ScreenHitsSprite(Camera cam, SpriteRenderer sr, Vector2 p)
    {
        Vector2[] v = sr.sprite.vertices;
        ushort[] tri = sr.sprite.triangles;
        Transform t = sr.transform;
        float fx = sr.flipX ? -1f : 1f, fy = sr.flipY ? -1f : 1f;
        Vector2[] s = new Vector2[v.Length];
        for (int i = 0; i < v.Length; i++)
            s[i] = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(v[i].x * fx, v[i].y * fy, 0f)));
        for (int i = 0; i + 2 < tri.Length; i += 3)
            if (InTriangle(p, s[tri[i]], s[tri[i + 1]], s[tri[i + 2]])) return true;
        return false;
    }

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
        float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
}
