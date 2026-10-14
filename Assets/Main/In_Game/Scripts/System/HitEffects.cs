using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 피격 이펙트 (몹 / 플레이어 / 포탑 공통)
//  - 모든 피해는 Health를 거치므로, Health.AnyDamaged 하나만 듣고 자동으로 연출합니다. (따로 붙일 것 없음)
//  - 도트 스타일: 맞은 자리에 작은 '팍' 섬광 + 사방으로 튀는 픽셀 조각
//      몹      : 흰/노랑 섬광 + 노란 조각 (몹 자체의 번쩍임·흔들림은 MobAI가 담당)
//      플레이어: 빨간 섬광 + 빨간 조각 + 캐릭터 그림이 붉게 번쩍이고 흔들림
//      포탑    : 주황 불꽃 + 회색 파편
//  - 그림은 코드로 만든 도트(16x16, 2x2)라 별도 이미지가 필요 없습니다.
//  + 플레이어 총기 전용
//      Impact()      : 몹이 아닌 곳(커서 위치·포탑·바닥)에 맞은 탄의 작은 회색 착탄
//      EjectCasing() : 탄피 배출 (도트 그림, 바닥에서 한 번 튀고 잠시 뒤 사라짐)
//                      권총탄(짧은 황동) / 소총탄(길고 목이 가는 황동) / 산탄(빨간 몸통 + 황동 바닥) 세 가지
public class HitEffects : MonoBehaviour
{
    public static bool Enabled = true;

    public enum Kind { Mob, Player, Tower }
    public enum CasingType { Pistol, Rifle, Shotgun }

    // ── 종류별 모양 ──
    private struct Style
    {
        public Color burst, spark, spark2;
        public float burstSize;   // 섬광 크기(미터)
        public int sparks;        // 튀는 조각 수
        public float sparkSpeed;  // 조각 속도
        public float gravity;
    }

    private static readonly Style MobStyle = new Style
    {
        burst = new Color(1f, 1f, 0.9f, 1f), spark = new Color(1f, 0.9f, 0.35f, 1f), spark2 = Color.white,
        burstSize = 0.55f, sparks = 5, sparkSpeed = 3.2f, gravity = 9f,
    };
    private static readonly Style PlayerStyle = new Style
    {
        burst = new Color(1f, 0.35f, 0.3f, 1f), spark = new Color(1f, 0.2f, 0.2f, 1f), spark2 = new Color(1f, 0.75f, 0.6f, 1f),
        burstSize = 0.6f, sparks = 6, sparkSpeed = 3f, gravity = 9f,
    };
    private static readonly Style TowerStyle = new Style
    {
        burst = new Color(1f, 0.75f, 0.3f, 1f), spark = new Color(1f, 0.55f, 0.15f, 1f), spark2 = new Color(0.55f, 0.55f, 0.6f, 1f),
        burstSize = 0.65f, sparks = 7, sparkSpeed = 3.6f, gravity = 12f,
    };

    // 착탄 (몹 타격보다 약하게: 작고 회색)
    private static readonly Style ImpactStyle = new Style
    {
        burst = new Color(0.92f, 0.92f, 0.9f, 1f), spark = new Color(0.65f, 0.65f, 0.65f, 1f), spark2 = new Color(0.9f, 0.9f, 0.88f, 1f),
        burstSize = 0.26f, sparks = 3, sparkSpeed = 2.2f, gravity = 9f,
    };

    [Header("탄피")]
    public float casingLife = 1.4f;
    public int maxCasings = 40;

    [Header("플레이어 피격 연출")]
    public Color playerFlashColor = new Color(1f, 0.25f, 0.25f, 1f);
    [Range(0f, 1f)] public float playerFlashStrength = 0.55f;
    public float playerFlashDuration = 0.1f;
    public float playerShake = 0.06f;

    [Tooltip("같은 대상에 이펙트가 너무 자주 겹치지 않게 (초). 연사 무기도 깔끔하게")]
    public float perTargetCooldown = 0.06f;
    public int maxPieces = 240;

    private static HitEffects instance;
    private static Sprite burstSprite, pixelSprite;
    private static Sprite[] casingSprites;
    private static Material material;
    private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
    private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

    private class Piece
    {
        public SpriteRenderer sr;
        public Vector3 velocity;
        public float life, age, size, gravity, roll;
        public bool isBurst;
        public Color color;
        // 탄피
        public bool isCasing;        // 탄피 + 빠진 탄창 (바닥에 떨어지는 조각)
        public bool isDebris;        // 빠진 탄창 (탄피 개수 제한에 포함 안 함)
        public float floorY, spin, restHeight;
        public Vector3 scaleV;
        public int bounces;
    }
    private int casingCount;

    private readonly List<Piece> active = new List<Piece>();
    private readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();
    private readonly Dictionary<Health, float> lastEffectTime = new Dictionary<Health, float>();
    private readonly Dictionary<SpriteRenderer, Coroutine> playerFlashes = new Dictionary<SpriteRenderer, Coroutine>();
    private MaterialPropertyBlock block;
    private Camera cam;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (instance != null) return;
        GameObject go = new GameObject("HitEffects");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HitEffects>();
    }

    void OnEnable() { Health.AnyDamaged += OnAnyDamaged; }
    void OnDisable() { Health.AnyDamaged -= OnAnyDamaged; }

    // ------------------------------------------------------------------
    private void OnAnyDamaged(Health h, float amount)
    {
        if (!Enabled || h == null || !h.isActiveAndEnabled) return;

        float now = Time.time;
        if (lastEffectTime.TryGetValue(h, out float last) && now - last < perTargetCooldown) return;
        lastEffectTime[h] = now;
        if (lastEffectTime.Count > 512) lastEffectTime.Clear(); // 풀/파괴된 대상 정리

        Kind kind = Classify(h);
        Vector3 point = HitPoint(h, kind, out float radius);
        Style style = kind == Kind.Mob ? MobStyle : kind == Kind.Player ? PlayerStyle : TowerStyle;
        Spawn(point, style, radius);

        if (kind == Kind.Player) PlayerReaction(h);
    }

    private static Kind Classify(Health h)
    {
        if (h.GetComponent<MobAI>() != null) return Kind.Mob;
        if (h.GetComponent<PlayerController>() != null || h.CompareTag("Player")) return Kind.Player;
        return Kind.Tower;
    }

    // 맞은 자리: 그림(빌보드)의 가운데 근처, 카메라 쪽으로 살짝 앞에 (그림에 가려지지 않게)
    private Vector3 HitPoint(Health h, Kind kind, out float radius)
    {
        Bounds b = new Bounds(h.transform.position + Vector3.up, Vector3.one);
        bool found = false;

        Billboard board = kind != Kind.Tower ? h.GetComponentInChildren<Billboard>() : null;
        SpriteRenderer body = board != null ? board.GetComponent<SpriteRenderer>() : null;
        if (body != null) { b = body.bounds; found = true; }
        else
        {
            foreach (Renderer r in h.GetComponentsInChildren<Renderer>())
            {
                if (r is LineRenderer || r is TrailRenderer || r is ParticleSystemRenderer || r is SpriteRenderer) continue;
                if (!found) { b = r.bounds; found = true; } else b.Encapsulate(r.bounds);
            }
        }

        radius = Mathf.Clamp(Mathf.Min(b.extents.x, b.extents.y), 0.2f, 1f);
        Vector3 p = b.center + new Vector3(Random.Range(-0.35f, 0.35f) * b.extents.x,
                                           Random.Range(-0.15f, 0.35f) * b.extents.y,
                                           Random.Range(-0.35f, 0.35f) * b.extents.z);
        if (cam == null) cam = Camera.main;
        if (cam != null) p -= cam.transform.forward * (kind == Kind.Tower ? 0.15f : 0.35f);
        return p;
    }

    // ------------------------------------------------------------------
    // 플레이어 총탄이 몹이 아닌 곳에 멈춘 자리 (blocked = 포탑·바닥 등에 막힘 → 조금 더 진하게)
    public static void Impact(Vector3 point, bool blocked)
    {
        if (!Enabled || instance == null) return;
        if (instance.cam == null) instance.cam = Camera.main;
        if (instance.cam != null) point -= instance.cam.transform.forward * 0.08f; // 맞은 면보다 살짝 앞에
        instance.Spawn(point, ImpactStyle, blocked ? 0.45f : 0.3f);
    }

    // 탄피 배출: 위치, 초기 속도, 색, 크기(미터), 바닥 높이
    public static void EjectCasing(Vector3 pos, Vector3 velocity, CasingType type, float size, float floorY)
    {
        if (!Enabled || instance == null) return;
        instance.AddCasing(pos, velocity, type, size, floorY);
    }

    private void AddCasing(Vector3 pos, Vector3 vel, CasingType type, float size, float floorY)
    {
        if (casingCount >= maxCasings)
        {
            // 가장 오래된 탄피를 먼저 치움
            for (int i = 0; i < active.Count; i++)
                if (active[i].isCasing && !active[i].isDebris) { active[i].age = active[i].life; break; }
        }
        EnsureAssets();
        SpriteRenderer sr = pool.Count > 0 ? pool.Pop() : CreateRenderer();
        Color color = Color.white; // 탄피 그림 자체에 색이 있음
        sr.sprite = casingSprites[(int)type];
        sr.color = color;
        sr.transform.position = pos;
        sr.transform.localScale = Vector3.one * size;
        sr.gameObject.SetActive(true);
        casingCount++;
        active.Add(new Piece
        {
            sr = sr, velocity = vel, life = casingLife, age = 0f, size = size, gravity = 14f,
            color = color, roll = Random.Range(0f, 360f), isCasing = true, floorY = floorY,
            spin = Random.Range(720f, 1440f) * (Random.value < 0.5f ? -1f : 1f),
            scaleV = Vector3.one * size, restHeight = size * 0.25f,
        });
    }

    // 재장전 때 빠진 탄창 등: 주어진 그림을 그대로 떨어뜨림 (scale은 월드 크기, roll은 화면상 기울기)
    public static void DropDebris(Sprite sprite, Vector3 pos, Vector3 velocity, Vector3 scale, float roll, float floorY, float life)
    {
        if (!Enabled || instance == null || sprite == null) return;
        instance.EnsureCam();
        EnsureAssets();
        SpriteRenderer sr = instance.pool.Count > 0 ? instance.pool.Pop() : instance.CreateRenderer();
        sr.sprite = sprite;
        sr.color = Color.white;
        sr.transform.position = pos;
        sr.transform.localScale = scale;
        sr.gameObject.SetActive(true);
        instance.active.Add(new Piece
        {
            sr = sr, velocity = velocity, life = Mathf.Max(0.2f, life), age = 0f, size = Mathf.Abs(scale.x), gravity = 12f,
            color = Color.white, roll = roll, isCasing = true, isDebris = true, floorY = floorY,
            spin = Random.Range(60f, 160f) * (Random.value < 0.5f ? -1f : 1f),
            scaleV = scale, restHeight = 0.02f,
        });
    }

    private void EnsureCam() { if (cam == null) cam = Camera.main; }

    private void Spawn(Vector3 point, Style s, float radius)
    {
        if (active.Count > maxPieces) return;
        EnsureAssets();
        float scale = Mathf.Lerp(0.85f, 1.2f, Mathf.InverseLerp(0.3f, 1f, radius));

        // 섬광
        Add(point, Vector3.zero, s.burst, s.burstSize * scale, 0.12f, 0f, true);

        // 튀는 픽셀 조각
        Vector3 right = cam != null ? cam.transform.right : Vector3.right;
        Vector3 up = cam != null ? cam.transform.up : Vector3.up;
        for (int i = 0; i < s.sparks; i++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 dir = (right * Mathf.Cos(a) + up * (Mathf.Sin(a) * 0.8f + 0.6f)).normalized;
            Color c = Random.value < 0.7f ? s.spark : s.spark2;
            Add(point, dir * s.sparkSpeed * Random.Range(0.6f, 1.2f), c, Random.Range(0.06f, 0.1f), Random.Range(0.18f, 0.3f), s.gravity, false);
        }
    }

    private void Add(Vector3 pos, Vector3 vel, Color color, float size, float life, float gravity, bool burst)
    {
        SpriteRenderer sr = pool.Count > 0 ? pool.Pop() : CreateRenderer();
        sr.sprite = burst ? burstSprite : pixelSprite;
        sr.color = color;
        sr.transform.position = pos;
        sr.gameObject.SetActive(true);
        active.Add(new Piece
        {
            sr = sr, velocity = vel, life = life, age = 0f, size = size, gravity = gravity,
            isBurst = burst, color = color, roll = burst ? Random.Range(0, 2) * 45f : 0f,
        });
    }

    private SpriteRenderer CreateRenderer()
    {
        GameObject go = new GameObject("HitFx");
        go.layer = 2; // Ignore Raycast
        go.transform.SetParent(transform, false);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sharedMaterial = material;
        sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sr.receiveShadows = false;
        sr.sortingOrder = 20;
        return sr;
    }

    void LateUpdate()
    {
        if (active.Count == 0) return;
        if (cam == null) cam = Camera.main;
        Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
        float dt = Time.deltaTime;

        for (int i = active.Count - 1; i >= 0; i--)
        {
            Piece p = active[i];
            p.age += dt;
            if (p.age >= p.life || p.sr == null)
            {
                if (p.sr != null) { p.sr.gameObject.SetActive(false); pool.Push(p.sr); }
                if (p.isCasing && !p.isDebris) casingCount--;
                active.RemoveAt(i);
                continue;
            }

            float k = p.age / p.life; // 0 → 1
            Transform t = p.sr.transform;
            if (p.isCasing)
            {
                // 포물선 → 바닥에서 한 번 튀고 멈춤 → 마지막에 서서히 사라짐
                if (p.bounces < 2)
                {
                    p.velocity += Vector3.down * p.gravity * dt;
                    Vector3 np = t.position + p.velocity * dt;
                    float rest = p.floorY + p.restHeight;
                    if (np.y <= rest)
                    {
                        np.y = rest;
                        p.bounces++;
                        p.velocity = new Vector3(p.velocity.x * 0.45f, -p.velocity.y * 0.35f, p.velocity.z * 0.45f);
                        p.spin *= 0.4f;
                        if (p.bounces >= 2 || p.velocity.y < 0.4f) { p.bounces = 2; if (!p.isDebris) p.roll = Mathf.Round(p.roll / 90f) * 90f; }
                    }
                    t.position = np;
                    if (p.bounces < 2) p.roll += p.spin * dt;
                }
                Color c = p.color; c.a = k < 0.75f ? 1f : Mathf.Lerp(1f, 0f, (k - 0.75f) / 0.25f);
                p.sr.color = c;
                t.localScale = p.scaleV;
            }
            else if (p.isBurst)
            {
                // 빠르게 커졌다가 사라짐 (처음 2프레임은 꽉 찬 섬광)
                float grow = Mathf.Lerp(0.6f, 1.15f, Mathf.Min(1f, k * 2.5f));
                t.localScale = Vector3.one * (p.size * grow);
                Color c = p.color; c.a = k < 0.35f ? 1f : Mathf.Lerp(1f, 0f, (k - 0.35f) / 0.65f);
                p.sr.color = c;
            }
            else
            {
                p.velocity += Vector3.down * p.gravity * dt;
                p.velocity *= 1f - Mathf.Min(1f, 3f * dt); // 공기 저항
                t.position += p.velocity * dt;
                t.localScale = Vector3.one * (p.size * (1f - k * 0.5f));
                Color c = p.color; c.a = k < 0.6f ? 1f : Mathf.Lerp(1f, 0f, (k - 0.6f) / 0.4f);
                p.sr.color = c;
            }
            t.rotation = face * Quaternion.Euler(0f, 0f, p.roll);
        }
    }

    // ------------------------------------------------------------------
    // 플레이어: 그림이 붉게 번쩍 + 흔들림
    private void PlayerReaction(Health h)
    {
        Billboard board = h.GetComponentInChildren<Billboard>();
        if (board == null) return;
        board.Shake(playerShake, 0.15f);

        // 하체(빌보드) + 그 아래 상체 그림까지 모두 번쩍
        foreach (SpriteRenderer sr in board.GetComponentsInChildren<SpriteRenderer>())
        {
            if (sr == null || sr.sharedMaterial == null || !sr.sharedMaterial.HasProperty(FlashAmountId)) continue;
            if (playerFlashes.TryGetValue(sr, out Coroutine running) && running != null) StopCoroutine(running);
            playerFlashes[sr] = StartCoroutine(FlashRoutine(sr));
        }
    }

    private IEnumerator FlashRoutine(SpriteRenderer sr)
    {
        if (block == null) block = new MaterialPropertyBlock();
        sr.GetPropertyBlock(block); // 발 위치 등 다른 값 유지
        block.SetFloat(FlashAmountId, playerFlashStrength);
        block.SetColor(FlashColorId, playerFlashColor);
        sr.SetPropertyBlock(block);
        yield return new WaitForSeconds(playerFlashDuration);
        if (sr != null)
        {
            sr.GetPropertyBlock(block);
            block.SetFloat(FlashAmountId, 0f);
            sr.SetPropertyBlock(block);
        }
        playerFlashes.Remove(sr);
    }

    // ------------------------------------------------------------------
    // 도트 그림 (코드로 생성)
    private static void EnsureAssets()
    {
        if (material == null) material = new Material(Shader.Find("Sprites/Default"));
        if (burstSprite == null) burstSprite = MakeBurst();
        if (casingSprites == null)
        {
            // 도트 탄피 (왼쪽 = 바닥/뇌관, 오른쪽 = 탄두가 빠진 입구). 문자 한 칸 = 1픽셀
            //   L/M/D = 황동 밝음/중간/어두움, K = 입구 구멍, r/R/X = 빨강 밝음/중간/어두움, . = 투명
            casingSprites = new Sprite[3];
            casingSprites[(int)CasingType.Pistol] = MakePixelSprite(new[] {   // 짧고 굵은 권총탄
                "MLLL",
                "DMMK",
                "DDDD" });
            casingSprites[(int)CasingType.Rifle] = MakePixelSprite(new[] {    // 길고 목이 가는 소총탄
                "MLLLLM..",
                "DMMMMMMK",
                "DDDDDM.." });
            casingSprites[(int)CasingType.Shotgun] = MakePixelSprite(new[] {  // 황동 바닥 + 빨간 플라스틱 몸통
                "MLrrrR",
                "DMRRRX",
                "DMRRRX",
                "DDXXXX" });
        }
        if (pixelSprite == null)
        {
            Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            t.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            t.Apply();
            pixelSprite = Sprite.Create(t, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2);
        }
    }

    // 문자 그림 → 도트 스프라이트 (가로 길이 = 1 유닛, 가운데 피벗)
    private static Sprite MakePixelSprite(string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            Color c;
            switch (rows[h - 1 - y][x]) // 텍스처는 아래 줄부터
            {
                case 'L': c = new Color(1f, 0.87f, 0.48f); break;
                case 'M': c = new Color(0.88f, 0.66f, 0.26f); break;
                case 'D': c = new Color(0.6f, 0.42f, 0.15f); break;
                case 'K': c = new Color(0.32f, 0.22f, 0.1f); break;
                case 'r': c = new Color(0.95f, 0.32f, 0.26f); break;
                case 'R': c = new Color(0.78f, 0.17f, 0.15f); break;
                case 'X': c = new Color(0.48f, 0.08f, 0.08f); break;
                default: c = new Color(0, 0, 0, 0); break;
            }
            tex.SetPixel(x, y, c);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
    }

    // 16x16 '팍' 섬광: 가운데 꽉 찬 원 + 십자 가시 + 대각선 점 (흰색, 색은 SpriteRenderer.color로 입힘)
    private static Sprite MakeBurst()
    {
        const int N = 16;
        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        float c = (N - 1) * 0.5f;
        Color clear = new Color(1, 1, 1, 0), solid = Color.white, soft = new Color(1, 1, 1, 0.75f);
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float dx = Mathf.Abs(x - c), dy = Mathf.Abs(y - c);
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            Color col = clear;
            if (r < 2.4f) col = solid;                                              // 중심
            else if ((dx < 1f && dy < 7.6f) || (dy < 1f && dx < 7.6f)) col = solid; // 십자 가시
            else if (Mathf.Abs(dx - dy) < 0.8f && r > 3.2f && r < 5.8f) col = soft;  // 대각선 점
            else if (r < 3.3f) col = soft;                                          // 테두리
            tex.SetPixel(x, y, col);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);
    }
}
