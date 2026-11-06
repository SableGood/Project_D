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
    public int maxSameClip = 4;

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

    void OnEnable() { Health.AnyDamaged += OnAnyDamaged; }
    void OnDisable() { Health.AnyDamaged -= OnAnyDamaged; }

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

    private void PlayClip(AudioClip clip, float volume, float pitchJitter)
    {
        if (clip == null || volume <= 0f) return;
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
            if (sources.Count >= 32) return;
            free = gameObject.AddComponent<AudioSource>();
            free.playOnAwake = false;
            free.spatialBlend = 0f;
            sources.Add(free);
        }
        free.clip = clip;
        free.volume = Mathf.Clamp01(volume * master);
        free.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        free.Play();
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

    // ===== 몹 타격음 =====
    private void OnAnyDamaged(Health h, float amount)
    {
        if (h == null || amount <= 0f || Health.DotTick || h.invincible) return;
        if (h.GetComponent<MobAI>() == null) return;
        if (Time.time - lastMobHit < mobHitInterval) return;
        lastMobHit = Time.time;
        PlayClip(Clip("Mob/Mob_HitSound_1"), mobHit * (Health.LastHitCrit ? 1.3f : 1f), 0.1f);
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
        { "MAG",      new[] { new Step(0.03f, "MAG_coverup"), new Step(0.13f, "MAG_boxout"), new Step(0.35f, "MAG_chain"), new Step(0.50f, "MAG_boxin"), new Step(0.68f, "MAG_coverdown"), new Step(0.78f, "MAG_boltpull"), new Step(0.87f, "MAG_boltBack") } },
    };
    private static readonly string[] Families = { "Thompson", "Mossberg", "Garand", "Eugene", "Battle", "Colt", "MAG" };

    private static string FamilyOf(PlayerWeaponData d)
    {
        if (d == null) return null;
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
            if (w != null)
            {
                w.Fired += OnFired;
                wasReloading = w.isReloading; lastAmmo = w.currentAmmo;
                if (hadWeapon) PlayClip(Clip("Gun/draw"), gunReload * 0.8f, 0.03f); // 무기 꺼내기
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
            else if (ReloadSteps.TryGetValue(family, out Step[] steps))
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
        else if (wasReloading && family == "Mossberg") PlayClip(Clip("Gun/Mossberg_Pump"), gunReload, 0.03f); // 장전을 마치면 펌프
        wasReloading = w.isReloading;
        lastAmmo = w.currentAmmo;
    }

    private void OnFired(Weapon w)
    {
        if (family == null) return;
        string fire = suppressed ? "Gun/Suppress" : family == "Battle" ? "Gun/Battle_fire" : "Gun/" + family + "_Fire";
        AudioClip[] v = Variants(fire);
        if (v.Length > 0) PlayClip(v[Random.Range(0, v.Length)], gunFire, 0.05f);
        if (family == "Mossberg") pumpAt = Time.time + 0.3f;
        if (family == "Garand" && w.currentAmmo <= 0) PlayClip(Clip("Gun/Garand_Ping"), gunReload, 0.02f); // 마지막 탄: 빈 클립 '핑'
    }
}
