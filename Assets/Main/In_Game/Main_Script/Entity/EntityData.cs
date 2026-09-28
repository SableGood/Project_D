using UnityEngine;

// 유니티 우클릭 메뉴에 "Data -> EntityData" 항목을 만들어 줍니다.
[CreateAssetMenu(fileName = "NewEntityData", menuName = "Data/EntityData")]
public class EntityData : ScriptableObject
{
    [Header("개체 정보")]
    public string entityCode; // 코드명 (예: MM001)
    public string entityName; // 이름 (예: 기본 근접 몹)

    [Header("본체 피지컬 스탯")]
    public float maxHp;       // 최대 체력
    public float defense;     // 방어력
    public float moveSpeed;   // 이동속도

    [Header("기본 장착 장비")]
    public WeaponData defaultWeapon; // 태어날 때 들고 있을 무기 (위에서 만든 WeaponData를 넣을 공간)
}