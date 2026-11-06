using System.Collections.Generic;
using UnityEngine;

// 머리 위 느낌표 (v109)
//  원거리 몹이 플레이어를 사거리 안에 두고 조준을 시작할 때 머리 위에 빨간 ! 가 톡 튀어나왔다가 사라짐
public class AlertMark : MonoBehaviour
{
    public static float Height = 0.42f;     // 느낌표 높이 (m)
    public static float Life = 0.9f;        // 보이는 시간

    private SpriteRenderer sr;
    private Transform follow;
    private float offsetY, t0;
    private static readonly Stack<AlertMark> pool = new Stack<AlertMark>();

    // mob: 따라갈 몹, topY: 몹 발밑에서 머리 꼭대기까지의 높이
    public static void Show(Transform mob, float topY)
    {
        if (mob == null) return;
        AlertMark a = null;
        while (pool.Count > 0 && a == null) a = pool.Pop();
        if (a == null)
        {
            GameObject go = new GameObject("AlertMark");
            a = go.AddComponent<AlertMark>();
            a.sr = go.AddComponent<SpriteRenderer>();
            a.sr.sprite = FxArt.Alert;
            a.sr.sortingOrder = 30;
            a.sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        a.follow = mob;
        a.offsetY = topY + 0.15f;
        a.t0 = Time.time;
        a.gameObject.SetActive(true);
        a.LateUpdate();
    }

    void LateUpdate()
    {
        float t = Time.time - t0;
        if (follow == null || !follow.gameObject.activeInHierarchy || t > Life)
        {
            gameObject.SetActive(false);
            pool.Push(this);
            return;
        }
        // 톡 튀어나옴(0.12초) → 살짝 출렁 → 끝에서 줄어들며 사라짐
        float pop = t < 0.12f ? Mathf.Lerp(0.3f, 1.25f, t / 0.12f) : t < 0.22f ? Mathf.Lerp(1.25f, 1f, (t - 0.12f) / 0.1f) : 1f;
        float end = Mathf.Clamp01((Life - t) / 0.15f);
        float px = Height / 13f; // 그림 높이 13픽셀
        transform.localScale = Vector3.one * px * pop * Mathf.Lerp(0.6f, 1f, end);
        transform.position = follow.position + Vector3.up * (offsetY + Mathf.Sin(t * 14f) * 0.03f);
        Camera cam = Camera.main;
        if (cam != null) transform.rotation = cam.transform.rotation;
        sr.color = new Color(1f, 1f, 1f, end);
    }
}
