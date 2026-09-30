using UnityEngine;

// 유니티 우클릭 메뉴에 "Data -> Player Weapon Data" 항목 생성
[CreateAssetMenu(fileName = "NewPlayerWeaponData", menuName = "Data/Player Weapon Data")]
public class PlayerWeaponData : ScriptableObject
{
    public string weaponID;           // W001
    public string inGameName;         // 코알라 대검
    public string role;               // 기본 근접무장

    public string aimType;            // Multi
    public string fireMode;           // Hitscan / Projectile
    public string fireMechanism;      // Auto / Semi

    public float range;               // 유효거리
    public float damage;              // 공격력
    public float reloadTime;          // 재장전속도
    public int maxAmmo;               // 장탄수
    public float fireRate;            // 공격속도

    public int projectileCount;       // 투사체 갯수
    public bool canPierce;            // 타겟 관통 여부
    public float armorPenetration;    // 방어구 무시
    public float projectileSpeed;     // 투사체 속도
    public string projectilePrefabName; // Projectile Prefab 이름

    [TextArea]
    public string description;        // 장비 설명
}