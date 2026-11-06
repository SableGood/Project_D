using UnityEngine;

// 재능 '누가 알아서 해주겠지'(자동재장전)의 요정 (v109)
//  탄을 다 쓴 무기를 넣어 두면 요정이 대신 재장전해 줌 → 그때 플레이어 옆에 요정이 나타나 둥실 떠 있다가 반짝이며 사라짐
//  그림: Resources/Image/FX/Fairy.png
public class FairyHelper : MonoBehaviour
{
    public static float Size = 0.55f;    // 요정 키 (m)
    public static float Life = 1.4f;

    private static FairyHelper instance;
    private SpriteRenderer sr;
    private Transform follow;
    private float t0;
    private float side = 1f;

    public static void Show(Transform player)
    {
        if (player == null) return;
        Sprite s = FxArt.Load("Fairy", new Vector2(0.5f, 0.5f));
        if (s == null) return;
        if (instance == null)
        {
            GameObject go = new GameObject("FairyHelper");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<FairyHelper>();
            instance.sr = go.AddComponent<SpriteRenderer>();
            instance.sr.sortingOrder = 25;
            instance.sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        instance.sr.sprite = s;
        instance.follow = player;
        instance.t0 = Time.time;
        instance.side = -instance.side; // 번갈아 왼쪽·오른쪽
        instance.gameObject.SetActive(true);
    }

    void LateUpdate()
    {
        float t = Time.time - t0;
        if (follow == null || t > Life) { gameObject.SetActive(false); return; }
        Camera cam = Camera.main;
        Vector3 right = cam != null ? cam.transform.right : Vector3.right;
        float fadeIn = Mathf.Clamp01(t / 0.2f), fadeOut = Mathf.Clamp01((Life - t) / 0.35f);
        float a = Mathf.Min(fadeIn, fadeOut);
        transform.position = follow.position + right * (0.75f * side) + Vector3.up * (0.9f + Mathf.Sin(t * 6f) * 0.08f + (1f - fadeIn) * -0.2f);
        if (cam != null) transform.rotation = cam.transform.rotation;
        float px = Size / Mathf.Max(1f, sr.sprite.rect.height);
        transform.localScale = new Vector3(side < 0f ? -px : px, px, px) * Mathf.Lerp(0.7f, 1f, fadeIn);
        // 끝날 때 깜빡깜빡
        bool blink = fadeOut < 1f && Mathf.Repeat(t * 16f, 1f) < 0.35f;
        sr.color = new Color(1f, 1f, 1f, blink ? a * 0.4f : a);
    }
}
