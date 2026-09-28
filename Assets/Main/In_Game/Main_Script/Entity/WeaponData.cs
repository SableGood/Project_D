using UnityEngine;

// 무기 공격 타입을 정의 (근접, 원거리, 복합, 없음)
public enum AttackType { Melee, Ranged, Multi, None }

// 유니티 우클릭 메뉴에 "Data -> WeaponData" 항목을 만들어 줍니다.
[CreateAssetMenu(fileName = "NewWeaponData", menuName = "Data/WeaponData")]
public class WeaponData : ScriptableObject
{
    [Header("기본 정보")]
    public string weaponName;       // 무기 이름 (예: 근거리 발톱)
    public AttackType attackType;   // 공격 유형

    [Header("공격 스펙")]
    public float attackPower;       // 공격력
    public float attackSpeed;       // 공격속도 (초당 타격 횟수)
    public float attackRange;       // 사거리

    [Header("투사체 (원거리용)")]
    public GameObject projectilePrefab; // 총알이나 마법 프리팹 (근거리는 비워둠)
}