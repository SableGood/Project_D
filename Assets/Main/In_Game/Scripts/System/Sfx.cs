using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 효과음 (v98). 씬에 자동으로 만들어짐 — 플레이 중 Hierarchy의 'Sfx'에서 음량 조절
//  소리 파일: Resources/Sound/{Gun, Mob, UI}/이름  (wav/mp3, 확장자 없이 이름으로 부름)
//  ■ 총: 쏠 때(이름_Fire, 이름_Fire_1, 이름_Fire_2 중 무작위), 재장전 단계별(탄창 빼기·끼우기·장전), 무기 꺼낼 때(draw)
//      무기 이름 = PlayerWeaponData의 이름에 들어 있는 Colt / Thompson / Eugene / Garand / Battle / Mossberg / MAG
//  ■ 수류탄: 핀 뽑기(Frag_Pull), 고폭 바닥에 떨어짐(Frag_Bounce), 폭발(HE_Explode, Frag_Explode) — PlayerSkills·SkillGrenade에서 호출
//  ■ 몹 타격음: 몹이 맞을 때 (너무 자주 겹치지 않게 간격 제한)
//  ■ UI 선택음: 버튼·토글을 누를 때
public class Sfx : MonoBehaviour
{
    public static Sfx Instance { get; private set; }

    [Range(0f, 1f)] public float master = 1f;
    [Range(0f, 1f)] public float gunFire = 0.5f;
    [Range(0f, 1f)] public float gunReload = 0.75f;
    [Range(0f, 1f)] public float mobHit = 0.45f;
    [Range(0f, 1f)] public float ui = 0.6f;
    [Tooltip("몹 타격음 최소 간격 (초)")]
    public float mobHitInterval = 0.05f;
    [Tooltip("같은 소리가 동시에 겹칠 수 있는 최대 수")]
    public int maxSameClip = 6;   // v117: 4 → 6 (빠른 연사에서 소리가 끊겨 들리지 않게)

    private readonly List<AudioSource> sources = new List<AudioSource>();
    private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    private readonly Dictionary<string, AudioClip[]> variants = new Dictionary<string, AudioClip[]>();
    private float lastMobHit = -999f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (Instance != null) return;
        GameObject go = new GameObject("Sfx");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<Sfx>();
    }

    void OnEnable() { Health.AnyDamaged += OnAnyDamaged; Health.AnyDied += OnAnyDied; }
    void OnDisable() { Health.AnyDamaged -= OnAnyDamaged; Health.AnyDied -= OnAnyDied; }

    // ===== 쓰러짐 소리 (v119) — 몹·플레이어가 쓰러질 때 Mob/Down_1·2 중 무작위 =====
    [Range(0f, 1f)] public float down = 0.6f;
    private float lastDown = -999f;
    private void OnAnyDied(Health h)
    {
        if (h == null || (h.GetComponent<MobAI>() == null && h.GetComponent<PlayerController>() == null)) return;
        if (Time.time - lastDown < 0.04f) return;   // 한꺼번에 많이 죽을 때 너무 겹치지 않게
        lastDown = Time.time;
        AudioClip[] v = Variants("Mob/Down");
        if (v.Length > 0) PlayClip(v[Random.Range(0, v.Length)], down, 0.08f, 64);
    }

    // ===== 재생 =====
    // path: "Gun/Colt_Fire" 처럼 Sound 폴더 기준
    public static void Play(string path, float volume = 1f, float pitchJitter = 0.04f)
    {
        if (Instance == null || string.IsNullOrEmpty(path)) return;
        Instance.PlayClip(Instance.Clip(path), volume, pitchJitter);
    }

    private AudioClip Clip(string path)
    {
        if (clips.TryGetValue(path, out AudioClip c)) return c;
        c = Resources.Load<AudioClip>("Sound/" + path);
        clips[path] = c;
        return c;
    }

    // priority: 0(가장 중요) ~ 256. 소리가 많이 겹치면 유니티가 우선순위 낮은 것부터 끔 → 발사음은 높게
    private AudioSource PlayClip(AudioClip clip, float volume, float pitchJitter, int priority = 128)
    {
        if (clip == null || volume <= 0f) return null;
        int same = 0;
        AudioSource free = null;
        foreach (AudioSource s in sources)
        {
            if (s.isPlaying) { if (s.clip == clip) same++; }
            else if (free == null) free = s;
        }
        if (same >= Mathf.Max(1, maxSameClip))
        {
            // 가장 오래 재생된 같은 소리를 끊고 다시
            AudioSource oldest = null;
            foreach (AudioSource s in sources) if (s.isPlaying && s.clip == clip && (oldest == null || s.time > oldest.time)) oldest = s;
            free = oldest;
        }
        if (free == null)
        {
            if (sources.Count >= 32)
            {
                // 다 쓰는 중: 덜 중요한(우선순위 숫자가 큰) 소리 중 가장 오래된 것을 끊고 씀 (v117: 연사 발사음이 빠지던 문제)
                AudioSource victim = null;
                foreach (AudioSource s in sources)
                    if (s.priority >= priority && (victim == null || s.priority > victim.priority || (s.priority == victim.priority && s.time > victim.time))) victim = s;
                if (victim == null) return null;
                victim.Stop();
                free = victim;
            }
            else
            {
                free = gameObject.AddComponent<AudioSource>();
                free.playOnAwake = false;
                free.spatialBlend = 0f;
                sources.Add(free);
            }
        }
        free.clip = clip;
        free.volume = Mathf.Clamp01(volume * master);
        free.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        free.priority = priority;
        free.Play();
        return free;
    }

    // 쓰지 않는 소리 (파일이 남아 있어도 무시)
    private static readonly HashSet<string> Excluded = new HashSet<string> { "Gun/Garand_Fire_2", "Gun/Thompson_Fire_1" };

    // 이름_Fire / 이름_Fire_1 / 이름_Fire_2 … 중 있는 것들
    private AudioClip[] Variants(string baseName)
    {
        if (variants.TryGetValue(baseName, out AudioClip[] v)) return v;
        var list = new List<AudioClip>();
        AudioClip one = Clip(baseName);
        if (one != null) list.Add(one);
        for (int i = 1; i <= 4; i++)
        {
            string n = baseName + "_" + i;
            if (Excluded.Contains(n)) continue;
            AudioClip c = Clip(n); if (c != null) list.Add(c);
        }
        v = list.ToArray();
        variants[baseName] = v;
        return v;
    }

    // ===== 몹에 맞지 않은 총알이 부딪히는 소리 (v114) — 맞은 곳에 따라 =====
    //  포탑(금속) Gun/Ric_Metal_1·2 · 물가 Gun/Ric_Water_1 · 그 밖의 바닥·지형 Gun/Ric_Concrete_1·2 (여러 개면 무작위)
    [Range(0f, 1f)] public float ricochet = 0.35f;
    [Range(0f, 1f)] public float ricochetMetal = 0.4f;    // v117: +35% (격발음에 묻혀서)
    [Range(0f, 1f)] public float ricochetWater = 0.47f;
    [Range(0f, 1f)] public float ricochetGround = 0.6f;
    [Tooltip("착탄 소리 최소 간격 (초)")]
    public float ricochetInterval = 0.07f;
    private float lastRicochet = -999f;
    // point: 착탄 지점, hit: 막은 콜라이더 (바닥·조준점이면 null)
    public static void BulletImpact(Vector3 point, Collider hit)
    {
        if (Instance == null || Time.time - Instance.lastRicochet < Instance.ricochetInterval) return;
        Instance.lastRicochet = Time.time;
        string name; float vol;
        if (hit != null && hit.GetComponentInParent<TurretAI>() != null) { name = "Gun/Ric_Metal"; vol = Instance.ricochetMetal; }
        else if (IslandMap.IsWater(point, 0.05f)) { name = "Gun/Ric_Water"; vol = Instance.ricochetWater; }
        else { name = "Gun/Ric_Concrete"; vol = Instance.ricochetGround; }
        AudioClip[] v = Instance.Variants(name);
        if (v.Length > 0) Instance.PlayClip(v[Random.Range(0, v.Length)], vol * Instance.ricochet / 0.35f, 0.08f);
    }

    // ===== 몹 타격음 =====
    [Range(0f, 1f)] public float critHit = 0.95f;  // v120: 치명타 소리 (일반 타격음과 따로). v121: 0.7 → 0.95
    // v121: 중장갑 타격음(Hit_Light + Hit_Armor)을 내는 몹 코드. ME001은 경장갑 소리로
    public static readonly HashSet<string> HeavyArmorMobs = new HashSet<string> { "MM002", "MB001" };
    private float lastCrit = -999f;
    private void OnAnyDamaged(Health h, float amount)
    {
        if (h == null || amount <= 0f || Health.DotTick || h.invincible) return;
        if (h.GetComponent<MobAI>() == null) return;

        // v120: 치명타 소리는 일반 타격음의 간격 제한과 따로, 높은 우선순위로 (다른 타격음에 밀려 빠지지 않게)
        if (Health.LastHitCrit && Time.time - lastCrit >= 0.03f)
        {
            lastCrit = Time.time;
            AudioClip[] cv = Variants("Mob/Hit_Flesh");
            if (cv.Length > 0) PlayClip(cv[Random.Range(0, cv.Length)], critHit, 0.06f, 40);
        }

        if (Time.time - lastMobHit < mobHitInterval) return;
        lastMobHit = Time.time;
        // 일반 타격음: 경장갑 Mob/Hit_Light_1·2·3. 중장갑(HeavyArmorMobs: MM002·MB001)은 Hit_Light + Mob/Hit_Armor_1·2 를 겹쳐서
        float vol = Mathf.Clamp01(mobHit * 1.2f);
        AudioClip[] v = Variants("Mob/Hit_Light");
        PlayClip(v.Length > 0 ? v[Random.Range(0, v.Length)] : Clip("Mob/Mob_Hit_Ric_1"), vol, 0.08f);
        MobAI mob = h.GetComponent<MobAI>();
        if (mob != null && HeavyArmorMobs.Contains(mob.Code ?? ""))
        {
            AudioClip[] a = Variants("Mob/Hit_Armor");
            if (a.Length > 0) PlayClip(a[Random.Range(0, a.Length)], vol * 0.85f, 0.08f);
        }
    }

    // ===== UI 선택음 =====
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private PointerEventData ped;
    private void CheckUIClick()
    {
        if (!Input.GetMouseButtonDown(0) || EventSystem.current == null) return;
        if (ped == null || ped.selectedObject != null) ped = new PointerEventData(EventSystem.current);
        ped.position = Input.mousePosition;
        uiHits.Clear();
        EventSystem.current.RaycastAll(ped, uiHits);
        foreach (RaycastResult r in uiHits)
        {
            Selectable s = r.gameObject.GetComponentInParent<Selectable>();
            if (s != null && s.interactable && s.isActiveAndEnabled) { PlayClip(Clip("UI/UI_Select_1"), ui, 0.02f); return; }
            if (r.gameObject.GetComponentInParent<IPointerClickHandler>() != null) { PlayClip(Clip("UI/UI_Select_1"), ui, 0.02f); return; }
        }
    }

    // ===== 총 =====
    private PlayerWeaponManager pwm;
    private Weapon watched;
    private string family;
    private bool suppressed;
    private bool wasReloading;
    private float lastProgress;
    private int lastAmmo;
    private readonly HashSet<int> firedSteps = new HashSet<int>();
    private float pumpAt = -1f;

    private struct Step { public float at; public string clip; public Step(float a, string c) { at = a; clip = c; } }
    private static readonly Dictionary<string, Step[]> ReloadSteps = new Dictionary<string, Step[]>
    {
        // v102: 탄창 빼기 → 끼우기 사이를 줄이고(83% → 55%), 끼운 뒤 장전(노리쇠) 소리까지 여유를 둠
        { "Colt",     new[] { new Step(0.02f, "Colt_magout"), new Step(0.55f, "Colt_magin"), new Step(0.74f, "Colt_Cock") } },
        { "Thompson", new[] { new Step(0.02f, "Thompson_clipout"), new Step(0.55f, "Thompson_clipin"), new Step(0.75f, "Thompson_Reload") } },
        { "Eugene",   new[] { new Step(0.02f, "Eugene_magout"), new Step(0.55f, "Eugene_magin"), new Step(0.74f, "Eugene_Cock") } },
        { "Battle",   new[] { new Step(0.02f, "Battle_clipout"), new Step(0.55f, "Battle_clipin"), new Step(0.72f, "Battle_boltBack"), new Step(0.83f, "Battle_boltpull") } },
        { "Garand",   new[] { new Step(0.01f, "Garand_pull"), new Step(0.55f, "Garand_clipin"), new Step(0.70f, "Garand_release") } },
        { "ALA",      new[] { new Step(0.72f, "ALA_Magin") } },   // 로켓포: 로켓을 몸체에 결합
        { "MAG",      new[] { new Step(0.03f, "MAG_coverup"), new Step(0.13f, "MAG_boxout"), new Step(0.35f, "MAG_chain"), new Step(0.50f, "MAG_boxin"), new Step(0.68f, "MAG_coverdown"), new Step(0.78f, "MAG_boltpull"), new Step(0.87f, "MAG_boltBack") } },
    };
    private static readonly string[] Families = { "Thompson", "Mossberg", "Garand", "Eugene", "Battle", "Colt", "MAG" };
    // v113 새 무기: 같은 분류의 기존 총 소리를 빌려 씀 (무기 '이름' → 소리 계열)
    private static readonly Dictionary<string, string> SoundAlias = new Dictionary<string, string>
    {
        { "MAX", "Thompson" },   // 기관단총
        { "Duty", "Eugene" },    // 돌격소총
        { "Heavy", "Battle" },   // 전투소총
        { "ALA", "ALA" },        // 로켓포 (전용 소리: ALA_Deploy · ALA_Magin · ALA_Fire)
        { "SPA5", "Mossberg" },  // v121 반자동 산탄총: 한 발씩 넣는 소리는 모스버그, 마지막 펌프만 SPA5_Pump
        { "Envy", "Envy" },      // v121 6연장 유탄발사기
    };
    // v121: 쏠 때마다 펌프하지 않고, 재장전 끝에만 이 소리로 펌프 (반자동 산탄총)
    private static readonly Dictionary<string, string> EndPumpOnly = new Dictionary<string, string>
    {
        { "SPA5", "Gun/SPA5_Pump" },
    };
    private string endPumpClip;   // null = 보통 모스버그 (쏠 때마다 펌프 + 재장전 끝 Mossberg_Pump)
    private static string BaseName(PlayerWeaponData d)
    {
        string nm = d != null ? (d.weaponName ?? "").Trim() : "";
        int us = nm.IndexOf('_');
        return us < 0 ? nm : nm.Substring(0, us);   // Duty_Upgrade → Duty (같은 소리)
    }

    // 발사음만 따로 쓰는 무기 (무기 '이름' → 소리). 재장전 등은 위 계열 소리 그대로
    private static readonly Dictionary<string, string> FireOverride = new Dictionary<string, string>
    {
        { "Duty", "Gun/K2_Fire" },   // v114 (Duty_Upgrade 포함)
        { "Envy", "Gun/GL40_Fire" }, // v121 유탄 발사
        { "Mossberg", "Gun/Mossberg_Fire_1" },   // v123: 모스버그 계열은 1번만 (두 소리가 섞이면 어색)
        { "SPA5", "Gun/Mossberg_Fire_2" },       // v123: SPA5는 2번
    };
    private string fireOverride;
    private AudioSource lastFireSource;
    // 재장전 소리를 따로 쓰는 무기 (무기 '이름' → 단계)
    private static readonly Dictionary<string, Step[]> ReloadOverride = new Dictionary<string, Step[]>
    {
        { "Duty", new[] { new Step(0.02f, "Eugene_magout"), new Step(0.55f, "Eugene_magin"), new Step(0.72f, "K2_Reload_Pull") } },   // v115: K2 장전손잡이
        { "Envy", new[] { new Step(0.03f, "Battle_clipout"), new Step(0.58f, "Battle_clipin") } },   // v121: 실린더 분리 → 새 실린더 결합
    };
    private string reloadOverride;

    private static string FamilyOf(PlayerWeaponData d)
    {
        if (d == null) return null;
        string nm = (d.weaponName ?? "").Trim();
        int us = nm.IndexOf('_');
        if (SoundAlias.TryGetValue(us < 0 ? nm : nm.Substring(0, us), out string alias)) return alias;
        string s = (d.weaponName ?? "") + " " + (d.spriteName ?? "");
        foreach (string f in Families) if (s.IndexOf(f, System.StringComparison.Ordinal) >= 0) return f;
        return null;
    }

    // 소음기 총: 3티어 어썰트 (콜트·톰슨·유진·개런드·맥·모스버그. 배틀 어썰트는 소염기라 제외)
    private static bool IsSuppressed(PlayerWeaponData d, string fam)
    {
        if (d == null || fam == null || fam == "Battle") return false;
        string s = (d.weaponName ?? "") + " " + (d.spriteName ?? "");
        return s.Contains("Assault");
    }

    void Update()
    {
        CheckUIClick();
        if (pwm == null)
        {
            Transform p = GameManager.Instance != null ? GameManager.Instance.playerTransform : null;
            if (p != null) pwm = p.GetComponent<PlayerWeaponManager>();
        }
        Weapon w = pwm != null ? pwm.CurrentWeapon : null;
        if (w != watched)
        {
            if (watched != null) watched.Fired -= OnFired;
            bool hadWeapon = watched != null;
            watched = w;
            family = w != null ? FamilyOf(w.playerWepData) : null;
            suppressed = w != null && IsSuppressed(w.playerWepData, family);
            fireOverride = null; reloadOverride = null; endPumpClip = null;
            string bn = w != null ? BaseName(w.playerWepData) : "";
            if (ReloadOverride.ContainsKey(bn)) reloadOverride = bn;
            FireOverride.TryGetValue(bn, out fireOverride);
            EndPumpOnly.TryGetValue(bn, out endPumpClip);
            if (w != null)
            {
                w.Fired += OnFired;
                wasReloading = w.isReloading; lastAmmo = w.currentAmmo;
                if (hadWeapon) PlayClip(Clip(family == "ALA" ? "Gun/ALA_Deploy" : "Gun/draw"), gunReload * 0.8f, 0.03f); // 무기 꺼내기
            }
            pumpAt = -1f;
        }
        if (w == null || family == null) return;

        // 펌프: 쏘고 잠시 뒤 '철컥' (산탄총)
        if (pumpAt > 0f && Time.time >= pumpAt) { pumpAt = -1f; if (w.currentAmmo > 0 && !w.isReloading) PlayClip(Clip("Gun/Mossberg_Pump"), gunReload, 0.03f); }

        if (w.isReloading)
        {
            float r = w.ReloadProgress;
            if (!wasReloading) { firedSteps.Clear(); lastProgress = 0f; }
            if (family == "Mossberg" && !TalentEffects.Overheat)
            {
                if (w.currentAmmo > lastAmmo) PlayClip(Clip("Gun/Mossberg_Shellinsert"), gunReload, 0.05f); // 한 발씩
            }
            else if ((reloadOverride != null && ReloadOverride.TryGetValue(reloadOverride, out Step[] steps)) || ReloadSteps.TryGetValue(family, out steps))
            {
                for (int i = 0; i < steps.Length; i++)
                {
                    if (firedSteps.Contains(i) || r < steps[i].at) continue;
                    if (family == "Garand" && i == 0 && lastAmmo <= 0) { firedSteps.Add(i); continue; } // 빈 총은 이미 노리쇠가 열려 있음
                    firedSteps.Add(i);
                    PlayClip(Clip("Gun/" + steps[i].clip), gunReload, 0.03f);
                }
            }
            lastProgress = r;
        }
        else if (wasReloading && family == "Mossberg") PlayClip(Clip(endPumpClip ?? "Gun/Mossberg_Pump"), gunReload, 0.03f); // 장전을 마치면 펌프
        wasReloading = w.isReloading;
        lastAmmo = w.currentAmmo;
    }

    private void OnFired(Weapon w)
    {
        if (family == null) return;
        // v116: 권총·기관단총(콜트·톰슨) 소음기는 더 얇은 소리 Gun/Suppress_Light_*
        // v118: 연발(톰슨)은 짧게 자른 Suppress_Light_Auto — 원래 소리(0.1초 꽉 찬 소리)는 초당 15발이면 겹쳐서 한 덩어리로 들림
        string light = w.playerWepData != null && w.playerWepData.IsAuto && Variants("Gun/Suppress_Light_Auto").Length > 0 ? "Gun/Suppress_Light_Auto" : "Gun/Suppress_Light";
        float fv = suppressed && light.StartsWith("Gun/Suppress_Light") && (family == "Colt" || family == "Thompson") ? 1.35f : 1f; // v119: 얇은 소음기는 묻혀서 크게
        string fire = suppressed ? ((family == "Colt" || family == "Thompson") && Variants(light).Length > 0 ? light : "Gun/Suppress") : fireOverride != null ? fireOverride : family == "Battle" ? "Gun/Battle_fire" : "Gun/" + family + "_Fire"; // ALA → Gun/ALA_Fire
        AudioClip[] v = Variants(fire);
        // v120: 연발 소음기는 새 발을 쏠 때 앞 발 소리를 끊고 다시 시작 → 발마다 시작음이 또렷하게 (꼬리가 겹쳐 뭉개지지 않게)
        bool choke = fire == "Gun/Suppress_Light_Auto";
        if (choke && lastFireSource != null && lastFireSource.isPlaying && lastFireSource.clip != null && lastFireSource.clip.name.StartsWith("Suppress_Light_Auto")) lastFireSource.Stop();
        if (v.Length > 0) lastFireSource = PlayClip(v[Random.Range(0, v.Length)], Mathf.Clamp01(gunFire * fv), 0.05f, 16); // v117: 발사음은 다른 소리에 밀려 꺼지지 않게
        if (family == "Mossberg" && endPumpClip == null) pumpAt = Time.time + 0.3f;   // 반자동(SPA5)은 쏠 때 펌프 없음
        if (family == "Garand" && w.currentAmmo <= 0) PlayClip(Clip("Gun/Garand_Ping"), gunReload, 0.02f); // 마지막 탄: 빈 클립 '핑'
    }
}
