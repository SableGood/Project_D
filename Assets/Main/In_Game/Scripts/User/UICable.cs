using UnityEngine;
using UnityEngine.UI;

// v130 UI용 늘어진 전깃줄 (메인 메뉴 배경). 바람에 흔들림
//  - loose = true : 천장에 한쪽만 매달려 늘어진 줄 (끝이 진자처럼 흔들림)
//  - loose = false: 두 점 사이에 처진 줄 (가운데가 살짝 흔들림)
//  좌표 a, b는 이 RectTransform 안에서 0~1 비율 (왼쪽 아래 0,0). 굵기·처짐·길이는 rect 너비 대비 비율
[RequireComponent(typeof(CanvasRenderer))]
public class UICable : MaskableGraphic
{
    public Vector2 a, b;
    public bool loose;
    public float length = 0.12f;      // loose: 줄 길이 (rect 높이 비율)
    public float sag = 0.04f;         // 두 점 사이 처짐 (rect 높이 비율)
    public float thickness = 0.0025f; // rect 너비 비율
    public float swayAmp = 6f;        // loose: 흔들리는 각도(도) / 처진 줄: 옆으로 흔들리는 폭(rect 너비의 0.1% 단위)
    public float swaySpeed = 1.1f;
    public float phase;
    public int segments = 18;

    void Update() { SetVerticesDirty(); }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        if (r.width <= 0f || r.height <= 0f) return;
        float t = Application.isPlaying ? Time.unscaledTime : 0f;
        Vector2[] pts = new Vector2[segments + 1];
        Vector2 pa = new Vector2(r.xMin + a.x * r.width, r.yMin + a.y * r.height);
        if (loose)
        {
            // 위는 거의 고정, 아래로 갈수록 크게 흔들림 + 두 번째 파동으로 줄이 휘어 보이게
            float ang = swayAmp * Mathf.Sin(t * swaySpeed + phase) * Mathf.Deg2Rad;
            float wob = swayAmp * 0.35f * Mathf.Sin(t * swaySpeed * 2.3f + phase * 1.7f) * Mathf.Deg2Rad;
            float L = length * r.height;
            for (int i = 0; i <= segments; i++)
            {
                float s = (float)i / segments;
                float a1 = ang * s * s + wob * Mathf.Sin(s * Mathf.PI);
                pts[i] = pa + new Vector2(Mathf.Sin(a1) * L * s, -Mathf.Cos(a1) * L * s);
            }
        }
        else
        {
            Vector2 pb = new Vector2(r.xMin + b.x * r.width, r.yMin + b.y * r.height);
            float side = swayAmp * 0.001f * r.width * Mathf.Sin(t * swaySpeed + phase);
            float dip = sag * r.height * (1f + 0.06f * Mathf.Sin(t * swaySpeed * 0.7f + phase));
            for (int i = 0; i <= segments; i++)
            {
                float s = (float)i / segments;
                float k = 4f * s * (1f - s);
                pts[i] = Vector2.Lerp(pa, pb, s) + new Vector2(side * k, -dip * k);
            }
        }

        float half = thickness * r.width * 0.5f;
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        for (int i = 0; i <= segments; i++)
        {
            Vector2 dir = i < segments ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1];
            Vector2 n = new Vector2(-dir.y, dir.x).normalized * half;
            v.position = pts[i] + n; vh.AddVert(v);
            v.position = pts[i] - n; vh.AddVert(v);
            if (i > 0)
            {
                int k = i * 2;
                vh.AddTriangle(k - 2, k - 1, k);
                vh.AddTriangle(k - 1, k + 1, k);
            }
        }
    }
}
