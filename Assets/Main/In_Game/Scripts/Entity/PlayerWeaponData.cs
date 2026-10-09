using UnityEngine;

// 유니티 우클릭 메뉴에 "Data -> Player Weapon Data" 항목 생성
// ※ 값은 Weapon_Data.csv → [Tools/CSV 데이터 생성/3. 무기] 로 자동 생성/갱신됩니다. (직접 수정해도 다음 임포트 때 덮어씀)
[CreateAssetMenu(fileName = "NewPlayerWeaponData", menuName = "Data/Player Weapon Data")]
public class PlayerWeaponData : ScriptableObject
{
    [Header("기본 정보")]
    public string weaponID;           // W001
    public string weaponName;         // Knife (CSV '이름')
    public string inGameName;         // 코알라 대검 (CSV '인게임 별칭')
    public string role;               // 기본 근접무장

    [Header("발사 방식")]
    public string aimType;            // Multi
    public string fireMode;           // Hitscan / Projectile
    public string fireMechanism;      // Auto / Semi

    [Header("능력치")]
    public float range;               // 유효거리
    public float damage;              // 공격력
    public float reloadTime;          // 재장전속도 (초)
    public int maxAmmo;               // 장탄수 (0 = 무한/재장전 없음)
    public float fireRate;            // 공격속도 (초당 발사 횟수)

    public int projectileCount;       // 투사체 갯수 (한 번 쏠 때 나가는 탄 수)
    public float spread;              // 탄퍼짐 (각도, 0 = 정확히 조준점으로)
    public float moveSpread;          // 이동 탄퍼짐 (이동 중 추가되는 각도)
    public float knockback;           // 넉백 (명중 시 밀어내는 거리, 유닛)
    public bool canPierce;            // 타겟 관통 여부
    public float armorPenetration;    // 방어구 무시
    public float projectileSpeed;     // 투사체 속도 (0 = 기본값)
    public string projectilePrefabName; // Projectile Prefab 이름

    [Header("외형")]
    public string spriteName;         // 무기 이미지 이름 (Resources/Image/Weapons)
    public float spriteLength = 1f;   // 게임 화면에서의 무기 길이 (유닛)

    [Header("자동 연결 (임포터가 채움)")]
    public Sprite weaponSprite;
    public GameObject projectilePrefab;

    [TextArea]
    public string description;        // 장비 설명

    public bool IsProjectile => !string.IsNullOrEmpty(fireMode) && fireMode.Trim().ToLower() == "projectile";
    public bool IsAuto => !string.IsNullOrEmpty(fireMechanism) && fireMechanism.Trim().ToLower() == "auto";
    public bool UsesAmmo => maxAmmo > 0;
}
