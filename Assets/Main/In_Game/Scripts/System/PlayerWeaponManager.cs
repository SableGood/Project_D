using UnityEngine;

public class PlayerWeaponManager : MonoBehaviour
{
    [Header("무기 장착 위치")]
    public Transform weaponMountPoint; // 총을 쥐고 있을 손이나 총구 위치

    [Header("테스트용 무기 프리팹 리스트")]
    public GameObject[] weaponPrefabs;

    private int currentWeaponIndex = 0;
    private GameObject currentWeaponInstance;
    private Weapon currentWeapon;
    private PlayerController playerController;

    void Start()
    {
        playerController = GetComponent<PlayerController>();

        // ★ [핵심 안전장치] 인스펙터 연결이 풀렸거나 비어있어도 플레이어 자식 중에서 'WeaponMountPoint'를 강제로 찾아냅니다.
        if (weaponMountPoint == null)
        {
            Transform found = transform.Find("WeaponMountPoint");
            if (found != null)
            {
                weaponMountPoint = found;
                Debug.Log("PlayerWeaponManager: 자식 오브젝트에서 WeaponMountPoint를 자동으로 찾았습니다.");
            }
            else
            {
                weaponMountPoint = transform; // 자식에 없다면 플레이어 본인 위치를 부모로 지정
                Debug.LogWarning("PlayerWeaponManager: WeaponMountPoint를 찾지 못해 플레이어 본인을 기준으로 장착합니다.");
            }
        }

        if (weaponPrefabs.Length > 0)
        {
            EquipWeapon(0);
        }
    }

    void Update()
    {
        if (weaponPrefabs == null || weaponPrefabs.Length == 0) return;

        // 마우스 휠로 무기 교체
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0f)
        {
            EquipWeapon((currentWeaponIndex + 1) % weaponPrefabs.Length);
        }
        else if (scroll < 0f)
        {
            EquipWeapon((currentWeaponIndex - 1 + weaponPrefabs.Length) % weaponPrefabs.Length);
        }

        // 수동(Manual) 조준 무기일 경우 마우스 좌클릭 시 사격 명령 하달
        if (currentWeapon != null && currentWeapon.aimType == Weapon.AimType.Manual)
        {
            if (Input.GetMouseButton(0))
            {
                Vector3 aimPoint = GetAimPoint();
                currentWeapon.ManualAttackCommand(aimPoint);
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

        // 만약의 경우를 대비해 Equip 시점에도 마운트 포인트 재확인
        if (weaponMountPoint == null)
        {
            Transform found = transform.Find("WeaponMountPoint");
            weaponMountPoint = found != null ? found : transform;
        }

        // weaponMountPoint를 부모로 지정하고, 월드 좌표 유지(worldPositionStays)를 false로 설정
        currentWeaponInstance = Instantiate(weaponPrefabs[index], weaponMountPoint, false);

        // 부모 기준 로컬 좌표와 회전값을 0으로 강제 스냅하여 손에 딱 붙도록 고정
        currentWeaponInstance.transform.localPosition = Vector3.zero;
        currentWeaponInstance.transform.localRotation = Quaternion.identity;

        currentWeapon = currentWeaponInstance.GetComponent<Weapon>();
        if (currentWeapon != null)
        {
            // 플레이어가 장착한 무기이므로 무조건 수동 조준 모드로 고정
            currentWeapon.aimType = Weapon.AimType.Manual;
        }

        currentWeaponIndex = index;
        Debug.Log($"무기 장착 완료: {weaponPrefabs[index].name}");
    }

    private Vector3 GetAimPoint()
    {
        if (playerController != null && playerController.isTPS)
        {
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                return hit.point;
            }
            return ray.GetPoint(100f);
        }
        else
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float distance))
            {
                return ray.GetPoint(distance);
            }
            return transform.position + transform.forward * 10f;
        }
    }
}