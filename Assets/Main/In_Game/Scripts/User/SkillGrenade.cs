using UnityEngine;
using System.Collections.Generic;

// 투척 스킬의 수류탄 (PlayerSkills가 만듦, 그레이박스 모양)
//  - 고폭: 땅에 떨어진 뒤 신관 시간(던진 순간부터)이 지나면 폭발, 범위 안의 적 모두에게 피해
//  - 파편: 땅·벽·적에 닿는 즉시 폭발, 파편을 사방으로 흩뿌림 (파편 하나가 적 하나에게 피해)
public class SkillGrenade : MonoBehaviour
{
    public enum Kind { HighExplosive, Fragment }

    private const float Radius = 0.11f;
    private const float BodySize = 0.42f;   // 수류탄 그림 높이 (미터)
    private float spin;
    private Camera cam;

    private Kind kind;
    private Vector3 velocity;
    private float gravity, explodeAt, dieAt, floorY;
    private bool landed;
    private float damage, radius, fragRange;
    private int fragCount;
    private Transform owner;
    private Health ownerHealth;
    private int mobMask, envMask;

    private static readonly Dictionary<Color, Material> mats = new Dictionary<Color, Material>();
    private static readonly Collider[] hits = new Collider[64];

    public static SkillGrenade Launch(Kind kind, Vector3 origin, Vector3 velocity, float gravity,
        float damage, float radius, float fuse, int fragCount, float fragRange,
        Transform owner, int mobMask, int envMask, float floorY)
    {
        GameObject go;
        Sprite body = GrenadeArt.Body(kind == Kind.HighExplosive);
        if (body != null)
        {
            // 도트 그림 (카메라를 바라보며 날아가는 동안 빙글빙글 돎)
            go = new GameObject();
            go.transform.position = origin;
            var art = new GameObject("Art");
            art.transform.SetParent(go.transform, false);
            var sr = art.AddComponent<SpriteRenderer>();
            sr.sprite = body;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            float s = BodySize / Mathf.Max(0.01f, body.bounds.size.y);
            art.transform.localScale = Vector3.one * s;
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.position = origin;
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(kind == Kind.HighExplosive ? new Color(0.32f, 0.45f, 0.26f) : new Color(0.38f, 0.38f, 0.42f));
        }
        go.name = kind == Kind.HighExplosive ? "Skill_HEGrenade" : "Skill_FragGrenade";
        go.transform.localScale = body != null ? Vector3.one : Vector3.one * Radius * 2f;

        var g = go.AddComponent<SkillGrenade>();
        g.kind = kind;
        g.velocity = velocity;
        g.gravity = gravity;
        g.damage = damage;
        g.radius = radius;
        g.fragCount = fragCount;
        g.fragRange = fragRange;
        g.owner = owner;
        g.ownerHealth = owner != null ? owner.GetComponent<Health>() : null;
        g.mobMask = mobMask;
        g.envMask = envMask;
        g.floorY = floorY;
        g.explodeAt = Time.time + Mathf.Max(0.1f, fuse);
        g.dieAt = Time.time + 12f;
        g.hasArt = body != null;
        g.spin = Random.Range(0f, 360f);
        return g;
    }

    public static Material Mat(Color c)
    {
        if (mats.TryGetValue(c, out Material m) && m != null) return m;
        Shader s = Shader.Find("Unlit/Color");
        m = new Material(s != null ? s : Shader.Find("Standard")) { color = c };
        mats[c] = m;
        return m;
    }

    private bool hasArt;

    // 그림은 카메라를 바라보고, 날아가는 동안만 회전
    void LateUpdate()
    {
        if (!hasArt) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        if (!landed) spin += Time.deltaTime * 540f;
        transform.rotation = cam.transform.rotation * Quaternion.Euler(0f, 0f, spin);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (Time.time > dieAt) { Destroy(gameObject); return; }

        if (!landed)
        {
            Vector3 pos = transform.position;
            velocity += Vector3.down * gravity * dt;
            Vector3 next = pos + velocity * dt;

            if (kind == Kind.Fragment && TouchesMob(next)) { transform.position = next; Explode(); return; }

            if (Physics.Linecast(pos, next, out RaycastHit hit, envMask, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point + hit.normal * Radius;
                Land();
                return;
            }
            if (velocity.y < 0f && next.y <= floorY + Radius)
            {
                next.y = floorY + Radius;
                transform.position = next;
                Land();
                return;
            }
            transform.position = next;
        }
        else if (kind == Kind.HighExplosive)
        {
            // 터지기 직전 1초: 깜빡임
            float left = explodeAt - Time.time;
            if (left < 1f) transform.localScale = (hasArt ? Vector3.one : Vector3.one * Radius * 2f) * (1f + 0.25f * Mathf.Abs(Mathf.Sin(Time.time * 18f)));
        }

        if (kind == Kind.HighExplosive && Time.time >= explodeAt) Explode();
    }

    private void Land()
    {
        landed = true;
        if (kind == Kind.Fragment) Explode();
    }

    private bool TouchesMob(Vector3 p)
    {
        int n = Physics.OverlapSphereNonAlloc(p, 0.3f, hits, mobMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Health h = hits[i].GetComponentInParent<Health>();
            if (h != null && h != ownerHealth && !h.IsDead) return true;
        }
        return false;
    }

    private void Explode()
    {
        Vector3 c = transform.position;
        Sfx.Play(kind == Kind.HighExplosive ? "Gun/HE_Explode" : "Gun/Frag_Explode", 0.85f);
        if (kind == Kind.HighExplosive)
        {
            DamageArea(c, radius, damage, null);
            ExplosionFx(c, radius, true, floorY);
        }
        else
        {
            // 직접 맞은 적: 파편 하나만큼 피해 (파편은 그 적을 지나침)
            var direct = new HashSet<Health>();
            DamageArea(c, 0.7f, damage, direct);
            ExplosionFx(c, 0.45f, false, floorY);
            float step = 360f / Mathf.Max(1, fragCount);
            float offset = Random.Range(0f, step);
            for (int i = 0; i < fragCount; i++)
            {
                float ang = offset + i * step + Random.Range(-step * 0.25f, step * 0.25f);
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                SkillFragment.Spawn(c + Vector3.up * 0.35f, dir, fragRange, damage, owner, ownerHealth, mobMask | envMask, direct);
            }
        }
        Destroy(gameObject);
    }

    private void DamageArea(Vector3 c, float r, float dmg, HashSet<Health> done)
    {
        if (done == null) done = new HashSet<Health>();
        int n = Physics.OverlapSphereNonAlloc(c, r, hits, mobMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Health h = hits[i].GetComponentInParent<Health>();
            if (h == null || h == ownerHealth || h.IsDead || !done.Add(h)) continue;
            if (dmg > 0f) h.TakeDamage(dmg, 0f, owner);
        }
    }

    // 폭발 연출: 도트 폭발 그림(GrenadeArt)이 있으면 그것, 없으면 그레이박스 (번쩍임 구 + 연기)
    public static void ExplosionFx(Vector3 center, float radius, bool highExplosive, float floorY)
    {
        if (GrenadeArt.PlayExplosion(highExplosive, center, radius, floorY)) return;
        ExplosionFx(center, radius);
    }

    public static void ExplosionFx(Vector3 center, float radius)
    {
        HitEffects.Impact(center + Vector3.up * 0.2f, false);
        int smokes = Mathf.Clamp(Mathf.RoundToInt(radius * 5f), 6, 18);
        for (int i = 0; i < smokes; i++)
        {
            Vector2 d = Random.insideUnitCircle.normalized;
            Vector3 v = new Vector3(d.x, 0.4f, d.y) * radius * 2.2f;
            HitEffects.Smoke(center + Vector3.up * 0.3f, v, Mathf.Max(0.3f, radius * 0.45f), 0.5f, 0.85f, 0.2f);
        }
        ExplosionFlash.Spawn(center, radius);
    }
}

// 폭발 범위를 잠깐 보여주는 반투명 구
public class ExplosionFlash : MonoBehaviour
{
    private static Material mat;
    private MeshRenderer mr;
    private MaterialPropertyBlock block;
    private float start, size;
    private const float Life = 0.25f;

    public static void Spawn(Vector3 center, float radius)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ExplosionFlash";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = center;
        if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
        var f = go.AddComponent<ExplosionFlash>();
        f.mr = go.GetComponent<MeshRenderer>();
        f.mr.sharedMaterial = mat;
        f.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        f.mr.receiveShadows = false;
        f.block = new MaterialPropertyBlock();
        f.start = Time.time;
        f.size = radius * 2f;
        f.Apply(0f);
    }

    void Update()
    {
        float k = (Time.time - start) / Life;
        if (k >= 1f) { Destroy(gameObject); return; }
        Apply(k);
    }

    private void Apply(float k)
    {
        transform.localScale = Vector3.one * size * Mathf.Lerp(0.55f, 1f, Mathf.Sqrt(k));
        block.SetColor("_Color", new Color(1f, 0.75f, 0.35f, 0.45f * (1f - k)));
        mr.SetPropertyBlock(block);
    }
}

// 파편 수류탄의 파편 하나: 직선으로 날아가 처음 맞은 적에게 피해
public class SkillFragment : MonoBehaviour
{
    private Vector3 dir;
    private float range, travelled, damage;
    private Transform owner;
    private Health ownerHealth;
    private int mask;
    private HashSet<Health> skip;
    private const float Speed = 32f, CastRadius = 0.12f;
    private static Material trailMat;

    public static void Spawn(Vector3 pos, Vector3 dir, float range, float damage, Transform owner, Health ownerHealth, int mask, HashSet<Health> skip)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Skill_Fragment";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.07f;
        go.GetComponent<MeshRenderer>().sharedMaterial = SkillGrenade.Mat(new Color(0.25f, 0.25f, 0.27f));

        var trail = go.AddComponent<TrailRenderer>();
        if (trailMat == null) trailMat = new Material(Shader.Find("Sprites/Default"));
        trail.sharedMaterial = trailMat;
        trail.time = 0.08f;
        trail.widthMultiplier = 0.05f;
        trail.startColor = new Color(1f, 0.85f, 0.45f, 0.9f);
        trail.endColor = new Color(1f, 0.6f, 0.2f, 0f);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var f = go.AddComponent<SkillFragment>();
        f.dir = dir.normalized;
        f.range = range;
        f.damage = damage;
        f.owner = owner;
        f.ownerHealth = ownerHealth;
        f.mask = mask;
        f.skip = skip;
    }

    void Update()
    {
        float step = Speed * Time.deltaTime;
        if (step <= 0f) return;
        step = Mathf.Min(step, range - travelled);

        RaycastHit[] cast = Physics.SphereCastAll(transform.position, CastRadius, dir, step, mask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        RaycastHit first = default;
        Health firstHealth = null;
        foreach (RaycastHit h in cast)
        {
            if (h.distance <= 0f && h.point == Vector3.zero) continue; // 시작 위치에서 이미 겹친 것
            Health hp = h.collider.GetComponentInParent<Health>();
            if (hp != null && (hp == ownerHealth || hp.IsDead || (skip != null && skip.Contains(hp)))) continue;
            if (h.distance < best) { best = h.distance; first = h; firstHealth = hp; }
        }
        if (best < float.MaxValue)
        {
            if (firstHealth != null) firstHealth.TakeDamage(damage, 0f, owner);
            HitEffects.Impact(first.point, firstHealth == null);
            Destroy(gameObject);
            return;
        }

        transform.position += dir * step;
        travelled += step;
        if (travelled >= range - 0.001f) Destroy(gameObject);
    }
}
