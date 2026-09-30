using UnityEngine;

public class PlayerWeaponManager : MonoBehaviour
{
    [Header("무기 장착 위치")]
    public Transform weaponMountPoint;

    [Header("테스트용 무기 프리팹 리스트")]
    [Tooltip("프리팹 이름은 반드시 W001, W002 등 무기 ID와 동일해야 합니다.")]
    public GameObject[] weaponPrefabs;

    private int currentWeaponIndex = 0;
    private GameObject currentWeaponInstance;
    private Weapon currentWeapon;
    private PlayerController playerController;

    void Start()
    {
        playerController = GetComponent<PlayerController>();

        if (weaponMountPoint == null)
        {
            Transform found = transform.Find("WeaponMountPoint");
            weaponMountPoint = found != null ? found : transform;
        }

        if (weaponPrefabs.Length > 0)
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

        // ★ 발사 방식(Auto/Semi) 및 재장전 데이터 연동
        if (currentWeapon != null && currentWeapon.playerWepData != null)
        {
            // R키 수동 장전
            if (Input.GetKeyDown(KeyCode.R) && currentWeapon.currentAmmo < currentWeapon.playerWepData.maxAmmo)
            {
                StartCoroutine(currentWeapon.ReloadCoroutine());
            }

            Vector3 aimPoint = GetAimPoint();

            if (currentWeapon.playerWepData.fireMechanism == "Auto")
            {
                // 누르고 있으면 연사 (쿨타임은 Weapon 내부에서 처리)
                if (Input.GetMouseButton(0)) currentWeapon.ManualAttackCommand(aimPoint);
            }
            else if (currentWeapon.playerWepData.fireMechanism == "Semi")
            {
                // 누를 때마다 단발 (광클 유도)
                if (Input.GetMouseButtonDown(0)) currentWeapon.ManualAttackCommand(aimPoint);
            }
        }
    }

    public void EquipWeapon(int index)
    {
        if (index < 0 || index >= weaponPrefabs.Length || weaponPrefabs[index] == null) return;

        if (currentWeaponInstance != null)
        {
            Destroy(currentWeaponInstance);
        }

        if (weaponMountPoint == null)
        {
            Transform found = transform.Find("WeaponMountPoint");
            weaponMountPoint = found != null ? found : transform;
        }

        currentWeaponInstance = Instantiate(weaponPrefabs[index], weaponMountPoint, false);
        currentWeaponInstance.transform.localPosition = Vector3.zero;
        currentWeaponInstance.transform.localRotation = Quaternion.identity;

        currentWeapon = currentWeaponInstance.GetComponent<Weapon>();
        if (currentWeapon != null)
        {
            currentWeapon.aimType = Weapon.AimType.Manual;

            // ★ 무기 데이터 주입 (프리팹 이름 기반 동적 로드)
            string weaponID = weaponPrefabs[index].name;
            PlayerWeaponData data = Resources.Load<PlayerWeaponData>($"Data/Weapons/{weaponID}");

            if (data != null)
            {
                currentWeapon.InitPlayerWeapon(data);
                Debug.Log($"무기 데이터 주입 완료: {data.inGameName} / 최대 탄약: {currentWeapon.currentAmmo}");
            }
            else
            {
                Debug.LogWarning($"[경고] Resources/Data/Weapons 폴더에서 {weaponID}.asset을 찾을 수 없습니다.");
            }
        }

        currentWeaponIndex = index;
    }

    private Vector3 GetAimPoint()
    {
        if (playerController != null && playerController.isTPS)
        {
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, 100f)) return hit.point;
            return ray.GetPoint(100f);
        }
        else
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float distance)) return ray.GetPoint(distance);
            return transform.position + transform.forward * 10f;
        }
    }
}