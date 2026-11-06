using System.Collections.Generic;
using UnityEngine;

// 도트 이펙트 재생기 (v107, pixelfx 오픈소스로 구운 스프라이트 시트)
//  - 그림: Resources/Image/FX/Fx_<이름>.png  (격자로 늘어선 프레임)
//  - 정보: Resources/Image/FX/Fx_<이름>.txt  "프레임수 열수 칸너비 칸높이 fps 기준점X 기준점Y 반복(0/1)"
//  - 카메라를 바라보는 판에 그림. 그림은 '오른쪽을 향해' 그려져 있어서, 화면에서의 방향만큼 돌리고
//    왼쪽을 향할 땐 좌우 반전 (연기가 위로 피어오르는 방향이 뒤집히지 않게)
//  - 크기: metersPerPixel = 도트 한 칸의 월드 크기
public class PixelFx : MonoBehaviour
{
    public static bool Enabled = true;
    public const string Hit = "Fx_hit", Crit = "Fx_crit", MuzzleFlash = "Fx_muzzle_flash", MuzzleSmoke = "Fx_muzzle_smoke",
                        ShotgunFlash = "Fx_shotgun_flash", ShotgunSmoke = "Fx_shotgun_smoke", Slash = "Fx_slash", Claw = "Fx_claw";
    public static int MaxActive = 160;

    private class Clip { public Sprite[] frames; public float fps; public bool loop; }
    private class Live
    {
        public SpriteRenderer sr; public Clip clip; public float t, speed, angle, alpha;
        public Transform follow; public Vector3 local; public bool flip;
    }

    private static PixelFx instance;
    private static Material mat;
    private static readonly Dictionary<string, Clip> clips = new Dictionary<string, Clip>();
    private readonly List<Live> active = new List<Live>();
    private readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
    private Camera cam;

    public static bool Has(string name) => Enabled && Load(name) != null;

    // worldDir: 이펙트가 향할 월드 방향 (화면 평면으로 투영해서 각도를 구함). zero면 오른쪽
    public static void Play(string name, Vector3 pos, Vector3 worldDir, float metersPerPixel, Transform follow = null, float speed = 1f, float alpha = 1f)
    {
        Camera c = Cam();
        float ang = 0f;
        if (c != null && worldDir.sqrMagnitude > 1e-6f)
        {
            float x = Vector3.Dot(worldDir, c.transform.right), y = Vector3.Dot(worldDir, c.transform.up);
            if (Mathf.Abs(x) + Mathf.Abs(y) > 1e-5f) ang = Mathf.Atan2(y, x) * Mathf.Rad2Deg;
        }
        PlayAngle(name, pos, ang, metersPerPixel, follow, speed, alpha);
    }

    // angleDeg: 화면에서의 각도 (0 = 오른쪽, 90 = 위)
    public static void PlayAngle(string name, Vector3 pos, float angleDeg, float metersPerPixel, Transform follow = null, float speed = 1f, float alpha = 1f)
    {
        if (!Enabled) return;
        Clip clip = Load(name);
        if (clip == null) return;
        Ensure();
        if (instance.active.Count >= MaxActive) return;
        float a = Mathf.Repeat(angleDeg + 180f, 360f) - 180f;
        bool flip = a > 90f || a < -90f;
        if (flip) a = a > 0f ? a - 180f : a + 180f;

        SpriteRenderer sr = instance.pool.Count > 0 ? instance.pool.Pop() : instance.Create();
        sr.sprite = clip.frames[0];
        sr.flipX = flip;
        sr.color = new Color(1f, 1f, 1f, alpha);
        sr.transform.position = pos;
        sr.transform.localScale = Vector3.one * Mathf.Max(0.0005f, metersPerPixel);
        sr.gameObject.SetActive(true);
        var l = new Live { sr = sr, clip = clip, t = 0f, speed = Mathf.Max(0.05f, speed), angle = a, alpha = alpha, follow = follow, flip = flip };
        if (follow != null) l.local = follow.InverseTransformPoint(pos);
        instance.active.Add(l);
        instance.Orient(l);
    }

    private static Camera Cam()
    {
        if (instance != null)
        {
            if (instance.cam == null || !instance.cam.isActiveAndEnabled) instance.cam = Camera.main;
            return instance.cam;
        }
        return Camera.main;
    }

    private static void Ensure()
    {
        if (instance != null) return;
        GameObject go = new GameObject("PixelFx");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PixelFx>();
    }

    private static Clip Load(string name)
    {
        if (clips.TryGetValue(name, out Clip c)) return c;
        c = null;
        Texture2D tex = Resources.Load<Texture2D>("Image/FX/" + name);
        TextAsset info = Resources.Load<TextAsset>("Image/FX/" + name);
        if (tex != null && info != null)
        {
            string[] v = info.text.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (v.Length >= 8)
            {
                int n = int.Parse(v[0]), cols = int.Parse(v[1]), fw = int.Parse(v[2]), fh = int.Parse(v[3]);
                float fps = float.Parse(v[4], System.Globalization.CultureInfo.InvariantCulture);
                float px = float.Parse(v[5], System.Globalization.CultureInfo.InvariantCulture), py = float.Parse(v[6], System.Globalization.CultureInfo.InvariantCulture);
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                var frames = new Sprite[n];
                Vector2 pivot = new Vector2(px / fw, 1f - py / fh);
                for (int i = 0; i < n; i++)
                {
                    int x = (i % cols) * fw, y = (i / cols) * fh;
                    frames[i] = Sprite.Create(tex, new Rect(x, tex.height - y - fh, fw, fh), pivot, 1f, 0, SpriteMeshType.FullRect);
                    frames[i].name = name + "_" + i;
                }
                c = new Clip { frames = frames, fps = Mathf.Max(1f, fps), loop = v[7] == "1" };
            }
        }
        if (c == null) Debug.LogWarning($"[PixelFx] {name} 그림(.png)이나 정보(.txt)를 찾지 못했어요 (Resources/Image/FX)");
        clips[name] = c;
        return c;
    }

    private SpriteRenderer Create()
    {
        if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
        GameObject go = new GameObject("Fx");
        go.layer = 2; // Ignore Raycast
        go.transform.SetParent(transform, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sharedMaterial = mat;
        sr.sortingOrder = 20;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        return sr;
    }

    private void Orient(Live l)
    {
        Camera c = Cam();
        if (c != null) l.sr.transform.rotation = c.transform.rotation * Quaternion.Euler(0f, 0f, l.angle);
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Live l = active[i];
            l.t += dt * l.speed;
            int f = Mathf.FloorToInt(l.t * l.clip.fps);
            if (f >= l.clip.frames.Length)
            {
                if (l.clip.loop) { l.t = 0f; f = 0; }
                else { l.sr.gameObject.SetActive(false); pool.Push(l.sr); active.RemoveAt(i); continue; }
            }
            l.sr.sprite = l.clip.frames[f];
            if (l.follow != null) l.sr.transform.position = l.follow.TransformPoint(l.local);
            Orient(l);
        }
    }
}
