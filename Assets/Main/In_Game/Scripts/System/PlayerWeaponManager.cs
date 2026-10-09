using UnityEngine;

public class PlayerWeaponManager : MonoBehaviour
{
    [Header("무기 장착 위치")]
    public Transform weaponMountPoint;

    [Header("테스트용 무기 프리팹 리스트")]
    [Tooltip("Tools/플레이어/2. 테스트: 모든 플레이어 무기 장착 메뉴로 자동으로 채울 수 있습니다.")]
    public GameObject[] weaponPrefabs;

    [Header("조준")]
    [Tooltip("TPS 조준 레이가 무시할 레이어 (자기 자신 등)")]
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
            currentVisual.Refresh();
        }

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

        currentWeapon = currentWeaponInstance.GetComponent<Weapon>();
        currentVisual = currentWeaponInstance.GetComponentInChildren<WeaponVisual>();

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

    // 현재 들고 있는 무기 (HUD 등에서 탄약 표시용)
    public Weapon CurrentWeapon => currentWeapon;

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

        if (playerController != null && playerController.isTPS)
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
            if (aimPlane.Raycast(ray, out float distance)) return ray.GetPoint(distance);
            return transform.position + transform.forward * 10f;
        }
    }
}
