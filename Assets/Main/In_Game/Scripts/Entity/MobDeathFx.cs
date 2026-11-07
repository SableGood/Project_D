using System.Collections.Generic;
using UnityEngine;

// 몹이 죽을 때의 연출 (v97)
//  몹 자체는 죽는 즉시 풀로 돌아가고(게임 로직은 그대로), 이 컴포넌트가 따로 그림을 그려 연출만 함
//  ■ 날아감: 맞음(Death_Hit) → 뒤로 포물선을 그리며 날아감(Death_Fly) → 누움(Death_Corpse)
//  ■ 쓰러짐: 맞음 → 무릎 꿇음(Death_Fall) → 엎드림(Death_Down)
//    큰 한 방(최대 체력의 30% 이상)이면 날아감, 아니면 40% 확률로 날아감
//  ■ 쓰러지는 그림은 Death_Down1(앞으로)·Death_Down2(뒤로) 중 무작위 (v105. 없으면 Death_Down)
//  ■ 그 뒤 잠깐 누워 있다가 3~4초에 걸쳐 바닥으로 스르륵 가라앉아 사라짐 (v105. 예전엔 깜빡이며 사라짐)
//  그림: Resources/Image/Mobs/Anim/{몹 코드}/Death_Hit, Death_Fly, Death_Fall, Death_Down, Death_Corpse
//        (그림 크기·발 위치는 걷기 그림과 같게 맞춰 둠. 그림이 없는 몹은 원래 그림이 뒤로 넘어지며 쓰러짐)
public class MobDeathFx : MonoBehaviour
{
    public static int MaxActive = 60;          // 동시에 남아 있는 시체 수 (넘으면 오래된 것부터 지움)
    public static float RestTime = 1.4f;       // 누워 있는 시간
    public static float SinkTime = 3.5f;       // 바닥으로 가라앉는 시간 (v105)
    public static float FlyDistance = 1.3f;    // 날아가는 거리 (미터)
    public static float FlyHeight = 0.55f;
    public static float FlyTime = 0.32f;
    public static float BigHitRatio = 0.3f;
    public static float FlyChance = 0.4f;

    private class Set { public Sprite hit, fly, fall, down, down1, down2, corpse; }
    private static readonly Dictionary<string, Set> sets = new Dictionary<string, Set>();
    private static readonly List<MobDeathFx> active = new List<MobDeathFx>();
    private static readonly Stack<MobDeathFx> pool = new Stack<MobDeathFx>();
    private static readonly int FeetPosId = Shader.PropertyToID("_FeetPos");
    private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
    private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
    public static float KillFlashTime = 0.07f; // 죽는 한 방에도 피격 번쩍임 (v102: 즉사하면 번쩍임이 안 보이던 문제)

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { sets.Clear(); active.Clear(); pool.Clear(); }

    private SpriteRenderer sr;
    private MaterialPropertyBlock block;
    private Set set;
    private bool fly, fallback;
    private float t0;
    private Vector3 startCenter, startFeet, push;
    private Sprite fallbackSprite;
    private float fallbackSide;
    private Sprite downSprite;
    private bool fallBack; // 뒤로 쓰러짐 (Death_Down2)
    private float sinkHeight;
    private float flashStrength;
    private Color flashColor = Color.white;
    private float flyDist, collideDmg;
    private float rest;   // v125 누워 있는 시간 (재능 '알뜰살뜰'이면 길게)
    private readonly HashSet<Health> hitDone = new HashSet<Health>();
    private static readonly Collider[] hitBuf = new Collider[16];
    private static int mobMask = -1;

    private static Set GetSet(string code)
    {
        if (string.IsNullOrEmpty(code)) return null;
        if (sets.TryGetValue(code, out Set s)) return s;
        string p = $"Image/Mobs/Anim/{code}/";
        s = new Set
        {
            hit = Resources.Load<Sprite>(p + "Death_Hit"),
            fly = Resources.Load<Sprite>(p + "Death_Fly"),
            fall = Resources.Load<Sprite>(p + "Death_Fall"),
            down = Resources.Load<Sprite>(p + "Death_Down"),
            down1 = Resources.Load<Sprite>(p + "Death_Down1"),
            down2 = Resources.Load<Sprite>(p + "Death_Down2"),
            corpse = Resources.Load<Sprite>(p + "Death_Corpse"),
        };
        if (s.down == null) s.down = s.down1 != null ? s.down1 : s.down2;
        if (s.hit == null || s.corpse == null) s = null;
        sets[code] = s;
        return s;
    }

    public static void Play(MobAI mob, SpriteRenderer body, string code, Transform attacker, float lastDamage, float maxHealth, bool artFacesRight)
    {
        if (body == null || mob == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        // 밀려나는 방향: 때린 쪽의 반대 (바닥 평면)
        Vector3 away = attacker != null ? mob.transform.position - attacker.position : Vector3.zero;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = cam.transform.right * (Random.value < 0.5f ? -1f : 1f);
        away.Normalize();

        MobDeathFx fx = pool.Count > 0 ? pool.Pop() : null;
        if (fx == null)
        {
            GameObject go = new GameObject("MobDeathFx");
            fx = go.AddComponent<MobDeathFx>();
            fx.sr = go.AddComponent<SpriteRenderer>();
        }
        fx.gameObject.SetActive(true);
        fx.gameObject.layer = body.gameObject.layer;
        if (active.Count >= MaxActive) active[0].Finish();
        active.Add(fx);

        SpriteRenderer sr = fx.sr;
        sr.sharedMaterial = body.sharedMaterial;
        sr.sortingLayerID = body.sortingLayerID;
        sr.sortingOrder = body.sortingOrder;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
        sr.receiveShadows = true;
        sr.color = Color.white;
        sr.enabled = true;
        fx.transform.localScale = body.transform.lossyScale;

        fx.set = GetSet(code);
        fx.fallback = fx.set == null;
        fx.fallbackSprite = body.sprite;
        fx.startCenter = body.transform.position;
        fx.startFeet = mob.transform.position;
        fx.push = away;
        bool big = maxHealth > 0f && lastDamage >= maxHealth * BigHitRatio;
        fx.fly = big || Random.value < FlyChance;
        if (fx.fly && fx.set != null && fx.set.fly == null) fx.fly = false;
        if (!fx.fly && fx.set != null && (fx.set.fall == null || fx.set.down == null)) fx.fly = true;
        // 볼링 핀·홈런 더비 (재능): 날아가는 시체가 다른 적을 침
        fx.collideDmg = TalentEffects.CorpseHitDamage(attacker, big, out bool forceFly, out float distMul);
        if (forceFly && fx.set != null && fx.set.fly != null) fx.fly = true;
        fx.flyDist = FlyDistance * distMul;
        fx.hitDone.Clear();
        // 쓰러지는 방향: 앞(Down1)·뒤(Down2) 중 무작위. 한쪽 그림만 있으면 그것, 둘 다 없으면 Death_Down
        if (fx.set != null)
        {
            bool has1 = fx.set.down1 != null, has2 = fx.set.down2 != null;
            fx.fallBack = has2 && (!has1 || Random.value < 0.5f);
            fx.downSprite = fx.fallBack ? fx.set.down2 : has1 ? fx.set.down1 : fx.set.down;
        }
        fx.t0 = Time.time;
        fx.rest = TalentExtra.CorpseTime > 0f ? TalentExtra.CorpseTime : RestTime;
        fx.sinkHeight = 0f;
        fx.flashStrength = mob.hitFlashStrength;
        fx.flashColor = mob.hitFlashOverlay;

        // 그림이 바라보는 방향: 때린 쪽을 봄 (밀려나는 방향의 반대)
        float side = Vector3.Dot(away, cam.transform.right);
        bool faceLeft = side > 0f; // 오른쪽으로 밀려나면 왼쪽(때린 쪽)을 보고 있음
        sr.flipX = artFacesRight ? faceLeft : !faceLeft;
        fx.fallbackSide = side >= 0f ? -1f : 1f;
        if (fx.fallback) { sr.sprite = body.sprite; sr.flipX = body.flipX; }
        fx.Step();
    }

    void LateUpdate() { Step(); }

    private void Step()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float t = Time.time - t0;
        Quaternion rot = cam.transform.rotation;
        Vector3 offset = Vector3.zero;
        float roll = 0f;
        float end;

        if (fallback)
        {
            // 그림 없음: 원래 그림이 뒤로 넘어지며 바닥에 눕고 살짝 밀림
            float k = Mathf.Clamp01(t / 0.3f); k = 1f - (1f - k) * (1f - k);
            roll = 80f * k * fallbackSide;
            offset = push * 0.4f * k;
            sr.sprite = fallbackSprite;
            end = 0.3f + rest;
        }
        else if (fly)
        {
            const float hitT = 0.07f;
            if (t < hitT) sr.sprite = set.hit;
            else if (t < hitT + FlyTime)
            {
                float u = (t - hitT) / FlyTime;
                sr.sprite = set.fly;
                offset = push * flyDist * (1f - (1f - u) * (1f - u)) + Vector3.up * (4f * FlyHeight * u * (1f - u));
                if (collideDmg > 0f) HitOthers(startCenter + offset);
            }
            else
            {
                sr.sprite = set.corpse;
                offset = push * flyDist;
            }
            end = hitT + FlyTime + rest;
        }
        else
        {
            const float hitT = 0.09f, fallT = 0.18f;
            if (t < hitT) sr.sprite = set.hit;
            else if (t < hitT + fallT) sr.sprite = set.fall;
            else sr.sprite = downSprite != null ? downSprite : set.down;
            // 앞으로 고꾸라짐(때린 쪽으로) / 뒤로 넘어짐(밀려난 쪽으로)
            offset = (fallBack ? push * 0.25f : -push * 0.15f) * Mathf.Clamp01((t - hitT) / (fallT + 0.1f));
            end = hitT + fallT + rest;
        }

        // 가라앉기: 누워 있는 시간이 끝나면 SinkTime 동안 바닥 아래로 내려감
        float sink = 0f;
        bool sinking = t > end;
        // v125 알뜰살뜰: 누워 있는 시체를 밟으면 체력 회복 + 시체가 사라짐
        if (!sinking && t > end - rest + 0.1f && TalentExtra.CorpseHeal > 0f &&
            TalentExtra.TryPickCorpse(startFeet + new Vector3(offset.x, 0f, offset.z))) { Finish(); return; }
        if (sinking)
        {
            float b = (t - end) / Mathf.Max(0.1f, SinkTime);
            if (b >= 1f) { Finish(); return; }
            if (sinkHeight <= 0f) sinkHeight = Mathf.Max(0.3f, sr.bounds.max.y - startFeet.y + 0.05f); // 그림 맨 위까지 바닥 아래로
            float e = b * b * (3f - 2f * b); // 천천히 시작해서 천천히 끝남
            sink = sinkHeight * e;
        }

        transform.rotation = rot * Quaternion.Euler(0f, 0f, roll);
        transform.position = startCenter + new Vector3(offset.x, offset.y - sink, offset.z);

        // 깊이: 발밑 세운 판 (Billboard와 같은 방식). 가라앉는 중에는 끔 → 바닥 아래로 내려간 부분이 바닥에 가려짐
        Vector3 feet = startFeet + new Vector3(offset.x, 0f, offset.z);
        if (block == null) block = new MaterialPropertyBlock();
        sr.GetPropertyBlock(block);
        block.SetVector(FeetPosId, new Vector4(feet.x, feet.y, feet.z, sinking ? 0f : 1f));
        if (sr.sharedMaterial != null && sr.sharedMaterial.HasProperty(FlashAmountId))
        {
            block.SetFloat(FlashAmountId, t < KillFlashTime ? flashStrength : 0f);
            block.SetColor(FlashColorId, flashColor);
        }
        sr.SetPropertyBlock(block);
    }

    private void HitOthers(Vector3 at)
    {
        if (mobMask < 0) mobMask = LayerMask.GetMask("Mob");
        int n = Physics.OverlapSphereNonAlloc(at, 0.7f, hitBuf, mobMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Health h = hitBuf[i].GetComponentInParent<Health>();
            if (h == null || h.IsDead || !hitDone.Add(h)) continue;
            TalentEffects.DealTalentDamage(h, collideDmg);
            KnockbackReceiver.TryApply(h.transform, push, 1.2f);
        }
    }

    private void Finish()
    {
        active.Remove(this);
        sr.enabled = false;
        gameObject.SetActive(false);
        pool.Push(this);
    }

    void OnDestroy() { active.Remove(this); }
}
