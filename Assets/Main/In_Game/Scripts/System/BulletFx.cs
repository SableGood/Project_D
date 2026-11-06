using System.Collections.Generic;
using UnityEngine;

// 빠르게 날아가는 총탄 (v105)
//  예전의 '총구에서 끝까지 한 번에 그어지는 빛줄기(예광탄)' 대신, 짧은 탄 하나가 총구에서 맞은 곳까지 아주 빠르게 날아감
//  - 판정은 예전처럼 쏘는 순간 바로 (그림만 따라감)
//  - 머리(앞)는 밝은 노란빛 흰색, 꼬리로 갈수록 투명
public class BulletFx : MonoBehaviour
{
    public static float Speed = 140f;      // 초당 미터
    public static float Length = 0.55f;    // 탄 그림 길이 (꼬리 포함)
    public static float Width = 0.035f;
    public static Color Head = new Color(1f, 0.96f, 0.78f, 1f);
    public static Color Tail = new Color(1f, 0.72f, 0.3f, 0f);

    private class Shot { public LineRenderer lr; public Vector3 from, dir; public float dist, travel; }

    private static BulletFx instance;
    private static Material mat;
    private readonly List<Shot> active = new List<Shot>();
    private readonly Stack<LineRenderer> pool = new Stack<LineRenderer>();

    public static void Spawn(Vector3 from, Vector3 to, float widthMul = 1f)
    {
        Vector3 d = to - from;
        float dist = d.magnitude;
        if (dist < 0.3f) return; // 바로 앞에서 맞음 → 탄 그림 없이 타격만
        if (instance == null)
        {
            GameObject go = new GameObject("BulletFx");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<BulletFx>();
        }
        instance.Add(from, d / dist, dist, widthMul);
    }

    private void Add(Vector3 from, Vector3 dir, float dist, float widthMul)
    {
        if (active.Count > 120) return;
        LineRenderer lr = pool.Count > 0 ? pool.Pop() : Create();
        lr.widthMultiplier = Width * Mathf.Clamp(widthMul, 0.3f, 2.5f);
        Vector3 head = from + dir * Mathf.Min(dist, Length * 0.5f); // 첫 프레임: 총구 화염 바로 앞에 보임
        lr.SetPosition(0, from);
        lr.SetPosition(1, head);
        lr.enabled = true;
        active.Add(new Shot { lr = lr, from = from, dir = dir, dist = dist, travel = Mathf.Min(dist, Length * 0.5f) });
    }

    private LineRenderer Create()
    {
        if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));
        GameObject go = new GameObject("Bullet");
        go.layer = 2; // Ignore Raycast
        go.transform.SetParent(transform, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.sharedMaterial = mat;
        lr.numCapVertices = 2;
        lr.startColor = Tail; // 0 = 꼬리
        lr.endColor = Head;   // 1 = 머리
        lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 1f));
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.enabled = false;
        return lr;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Shot s = active[i];
            s.travel += Speed * dt;
            float head = Mathf.Min(s.travel, s.dist);
            float tail = Mathf.Max(0f, s.travel - Length);
            if (tail >= s.dist)
            {
                s.lr.enabled = false;
                pool.Push(s.lr);
                active.RemoveAt(i);
                continue;
            }
            s.lr.SetPosition(0, s.from + s.dir * tail);
            s.lr.SetPosition(1, s.from + s.dir * head);
        }
    }
}
