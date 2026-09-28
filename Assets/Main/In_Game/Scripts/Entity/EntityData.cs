using UnityEngine;

// 개체의 종류를 구분하기 위한 타입 (에러 해결의 핵심!)
public enum EntityType { Mob, Player, Turret }

[CreateAssetMenu(fileName = "NewEntityData", menuName = "Data/EntityData")]
public class EntityData : ScriptableObject
{
    [Header("개체 정보")]
    public EntityType entityType; // 이 데이터가 몹인지, 플레이어인지, 타워인지 선택
    public string entityCode;
    public string entityName;

    [Header("본체 피지컬 스탯")]
    public float maxHp;
    public float defense;
    public float moveSpeed;

    [Header("건설/소환 비용")]
    public int cost;          // 타워 건설 비용

    [Header("기본 장착 장비")]
    public WeaponData defaultWeapon;
}