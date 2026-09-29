using UnityEngine;

public class SimpleWeaponMountTest : MonoBehaviour
{
    public GameObject weaponPrefab; // 테스트할 무기 프리팹
    public Transform handPoint;     // 손 위치 오브젝트

    void Start()
    {
        if (weaponPrefab != null && handPoint != null)
        {
            // 1. 무기를 handPoint의 자식으로 생성
            GameObject weapon = Instantiate(weaponPrefab, handPoint);

            // 2. 위치와 회전값을 무조건 0으로 초기화하여 손에 딱 붙임
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.identity;

            Debug.Log("무기 강제 장착 완료!");
        }
        else
        {
            Debug.LogError("무기 프리팹 또는 손 위치가 연결되지 않았습니다!");
        }
    }
}