using UnityEngine;

// 수류탄 도트 그림 (Resources/Image/Effects/Grenade)
//  Grenade_HE / Grenade_Frag : 날아가는 수류탄
//  HE_1~3, Frag_1~3          : 폭발 (터짐 → 커짐 → 연기 고리)  카메라를 바라보는 그림으로 재생
//  HE_Scorch, Frag_Scorch    : 바닥에 남는 그을음 (바닥에 눕혀서 몇 초 뒤 사라짐)
public static class GrenadeArt
{
    private const string Folder = "Image/Effects/Grenade/";
    private static bool loaded;
    private static Sprite heBody, fragBody, heScorch, fragScorch;
    private static Sprite[] heFrames, fragFrames;

    private static Sprite Load(string n)
    {
        Sprite s = Resources.Load<Sprite>(Folder + n);
        if (s != null) s.texture.filterMode = FilterMode.Point;
        return s;
    }

    private static void Ensure()
    {
        if (loaded) return;
        loaded = true;
        heBody = Load("Grenade_HE");
        fragBody = Load("Grenade_Frag");
        heFrames = new[] { Load("HE_1"), Load("HE_2"), Load("HE_3") };
        fragFrames = new[] { Load("Frag_1"), Load("Frag_2"), Load("Frag_3") };
        heScorch = Load("HE_Scorch");
        fragScorch = Load("Frag_Scorch");
    }

    public static Sprite Body(bool highExplosive)
    {
        Ensure();
        return highExplosive ? heBody : fragBody;
    }

    // 그림이 없으면 false (호출한 쪽이 그레이박스 연출)
    public static bool PlayExplosion(bool highExplosive, Vector3 center, float radius, float floorY)
    {
        Ensure();
        Sprite[] frames = highExplosive ? heFrames : fragFrames;
        if (frames[0] == null || frames[1] == null || frames[2] == null) return false;
        // 그림 한 장(100칸)에서 연기 구름은 가운데 약 60% → 구름이 폭발 반경과 비슷하게 보이도록
        float width = Mathf.Clamp(radius * 3.3f, 1.2f, 9f);
        Sprite scorch = highExplosive ? heScorch : fragScorch;
        if (scorch != null) ScorchMark.Spawn(scorch, new Vector3(center.x, floorY + 0.02f, center.z), width);
        HitEffects.Impact(center + Vector3.up * 0.2f, false);

        Camera cam = Camera.main;
        float dist = cam != null ? Vector3.Distance(cam.transform.position, center) : 999f;
        float near = Mathf.Clamp01(1f - dist / (radius * 4f + 8f));   // 가까울수록 1

        if (CameraController.FirstPersonView)
        {
            // 1인칭: 위에서 본 그림을 정면에 크게 세우면 납작한 포스터처럼 보이므로
            //  - 가운데 터짐은 작게, 바닥 조금 위에
            //  - 연기 고리 그림을 작은 연기 덩이로 여러 장 낮게 흩뿌리고 천천히 떠오르며 사라짐
            //  - 가까우면 화면 번쩍임·흔들림
            float burstW = width * 0.55f;
            ExplosionAnim.Spawn(new[] { frames[0], frames[1] }, new[] { 0.07f, 0.16f }, center + Vector3.up * (burstW * 0.3f),
                burstW, 0f, 0f, 0.1f, 0.12f);
            int puffs = Mathf.Clamp(Mathf.RoundToInt(radius * 2.5f), 5, 9);
            for (int i = 0; i < puffs; i++)
            {
                Vector2 d = Random.insideUnitCircle * radius * 0.7f;
                float pw = width * Random.Range(0.28f, 0.4f);
                Vector3 p = new Vector3(center.x + d.x, floorY + pw * 0.22f, center.z + d.y);
                ExplosionAnim.Spawn(new[] { frames[2] }, new[] { Random.Range(0.8f, 1.2f) }, p,
                    pw, Random.Range(0.4f, 0.9f), Random.Range(0.03f, 0.15f), 0.6f, 0.05f);
            }
            Color flash = highExplosive ? new Color(1f, 0.72f, 0.35f) : new Color(1f, 1f, 0.95f);
            CameraController.Flash(flash, 0.45f * near, 0.25f);
            CameraController.Shake(0.22f * near, 0.35f);
        }
        else
        {
            ExplosionAnim.Spawn(frames, new[] { 0.07f, 0.13f, 0.5f }, center + Vector3.up * 0.15f, width, 0f, 0f, 0.3f, 0.45f);
            CameraController.Shake(0.08f * near, 0.25f);
        }
        return true;
    }
}

// 폭발 그림 여러 장을 차례로 보여주고, 마지막 장은 서서히 사라짐 (카메라를 바라봄)
//  rise: 사는 동안 위로 떠오르는 높이, delay: 늦게 나타나기, fade: 마지막 장 끝에서 사라지는 시간
//  pull: 그림 폭 대비 카메라 쪽으로 당기는 비율 (아래쪽이 바닥에 파묻히지 않게)
public class ExplosionAnim : MonoBehaviour
{
    private SpriteRenderer sr;
    private Sprite[] frames;
    private float[] durations;
    private Vector3 center;
    private float width, start, rise, fade, pull, total;
    private Camera cam;

    public static void Spawn(Sprite[] frames, float[] durations, Vector3 center, float width,
        float rise, float delay, float fade, float pull)
    {
        var go = new GameObject("GrenadeExplosion");
        var a = go.AddComponent<ExplosionAnim>();
        a.sr = go.AddComponent<SpriteRenderer>();
        a.sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        a.sr.sortingOrder = 5;
        a.frames = frames;
        a.durations = durations;
        a.center = center;
        a.width = width;
        a.rise = rise;
        a.fade = Mathf.Max(0.01f, fade);
        a.pull = pull;
        a.start = Time.time + Mathf.Max(0f, delay);
        foreach (float d in durations) a.total += d;
        a.sr.enabled = delay <= 0f;
        a.Show(0);
        a.Place(0f);
    }

    private void Show(int i)
    {
        if (sr.sprite == frames[i]) return;
        sr.sprite = frames[i];
        float w = Mathf.Max(0.01f, frames[i].bounds.size.x);
        transform.localScale = Vector3.one * (width / w);
    }

    private void Place(float k)
    {
        if (cam == null) cam = Camera.main;
        Vector3 p = center + Vector3.up * (rise * k);
        if (cam == null) { transform.position = p; return; }
        transform.rotation = cam.transform.rotation;
        transform.position = p - cam.transform.forward * (width * pull);
    }

    void LateUpdate()
    {
        float t = Time.time - start;
        if (t < 0f) return;              // 아직 나타날 때가 아님
        if (!sr.enabled) sr.enabled = true;
        float acc = 0f;
        for (int i = 0; i < durations.Length; i++)
        {
            acc += durations[i];
            if (t < acc)
            {
                Show(i);
                if (i == durations.Length - 1)
                    sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01((acc - t) / fade));
                Place(Mathf.Clamp01(t / Mathf.Max(0.01f, total)));
                return;
            }
        }
        Destroy(gameObject);
    }
}

// 바닥 그을음: 바닥에 눕힌 그림, 잠깐 남았다가 사라짐
public class ScorchMark : MonoBehaviour
{
    private const float Hold = 2.5f, Fade = 2f;
    private SpriteRenderer sr;
    private float start;

    public static void Spawn(Sprite sprite, Vector3 pos, float width)
    {
        var go = new GameObject("GrenadeScorch");
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
        var m = go.AddComponent<ScorchMark>();
        m.sr = go.AddComponent<SpriteRenderer>();
        m.sr.sprite = sprite;
        m.sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m.sr.sortingOrder = -1;
        go.transform.localScale = Vector3.one * (width / Mathf.Max(0.01f, sprite.bounds.size.x));
        m.start = Time.time;
    }

    void Update()
    {
        float t = Time.time - start;
        if (t >= Hold + Fade) { Destroy(gameObject); return; }
        if (t > Hold) sr.color = new Color(1f, 1f, 1f, 1f - (t - Hold) / Fade);
    }
}
