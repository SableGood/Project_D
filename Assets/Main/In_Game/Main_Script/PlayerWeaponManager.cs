using UnityEngine;

public class PlayerWeaponManager : MonoBehaviour
{
    [Header("무기 장착 위치")]
    public Transform weaponMountPoint; // 총을 쥐고 있을 손이나 총구 위치

    [Header("테스트용 무기 프리팹 리스트")]
    public GameObject[] weaponPrefabs;

    private int currentWeaponIndex = 0;
    private GameObject currentWeaponInstance;

    void Start()
    {
        // 게임 시작 시 첫 번째 무기 자동 장착
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
            int nextIndex = (currentWeaponIndex + 1) % weaponPrefabs.Length;
            EquipWeapon(nextIndex);
        }
        else if (scroll < 0f)
        {
            int prevIndex = (currentWeaponIndex - 1 + weaponPrefabs.Length) % weaponPrefabs.Length;
            EquipWeapon(prevIndex);
        }

        // Z, X 키로 이전/다음 무기 교체 (타워 단축키 1~5와 충돌 방지)
        if (Input.GetKeyDown(KeyCode.Z))
        {
            int prevIndex = (currentWeaponIndex - 1 + weaponPrefabs.Length) % weaponPrefabs.Length;
            EquipWeapon(prevIndex);
        }
        if (Input.GetKeyDown(KeyCode.X))
        {
            int nextIndex = (currentWeaponIndex + 1) % weaponPrefabs.Length;
            EquipWeapon(nextIndex);
        }
    }

    public void EquipWeapon(int index)
    {
        if (index < 0 || index >= weaponPrefabs.Length || weaponPrefabs[index] == null) return;

        // 기존에 들고 있던 무기 파괴
        if (currentWeaponInstance != null)
        {
            Destroy(currentWeaponInstance);
        }

        // 새 무기 생성 및 부모(MountPoint) 설정
        currentWeaponInstance = Instantiate(weaponPrefabs[index], weaponMountPoint.position, weaponMountPoint.rotation);
        currentWeaponInstance.transform.SetParent(weaponMountPoint);

        // 플레이어가 장착한 무기이므로 강제로 수동 발사 모드(isManualFire = true)로 고정
        Weapon weaponScript = currentWeaponInstance.GetComponent<Weapon>();
        if (weaponScript != null)
        {
            weaponScript.isManualFire = true;
        }

        currentWeaponIndex = index;
        Debug.Log($"무기 교체 완료: {weaponPrefabs[index].name}");
    }
}