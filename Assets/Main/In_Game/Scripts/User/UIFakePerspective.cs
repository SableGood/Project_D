using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// v144 UI 가짜 원근 (Screen Space Overlay 캔버스는 원근이 없어 Y축으로 돌려도 폭만 좁아짐)
//  → 그림·글자의 점을 직접 옮겨 한쪽이 멀어 보이게 (먼 쪽이 작아짐)
//  yaw > 0: 오른쪽이 멀어짐, yaw < 0: 왼쪽이 멀어짐
//  reference: 기준 사각형 (비우면 자기 자신). 글자에 붙일 때는 패널을 넣으면 패널과 같은 원근이 됨
//  Image(단순 사각형)는 세로 띠 여러 개로 나눠서 그림이 비틀려 보이지 않게
[RequireComponent(typeof(Graphic))]
public class UIFakePerspective : BaseMeshEffect
{
    public float yaw = 18f;
    public float distance = 2.5f;   // 보는 거리 (기준 사각형 너비의 배수). 작을수록 원근이 강함
    public int columns = 12;
    public RectTransform reference;

    private static readonly List<UIVertex> verts = new List<UIVertex>();
    private static readonly List<UIVertex> grid = new List<UIVertex>();

    public void Set(float newYaw, RectTransform refRect = null)
    {
        yaw = newYaw;
        if (refRect != null) reference = refRect;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || Mathf.Abs(yaw) < 0.01f) return;
        verts.Clear();
        vh.GetUIVertexStream(verts);
        if (verts.Count == 0) return;

        // 단순 사각형 그림 → 세로 띠로 나눔 (bl, tl, tr / tr, br, bl 순서)
        List<UIVertex> src = verts;
        if (verts.Count == 6 && graphic is Image && columns > 1)
        {
            UIVertex bl = verts[0], tl = verts[1], tr = verts[2], br = verts[4];
            grid.Clear();
            for (int i = 0; i < columns; i++)
            {
                float a = (float)i / columns, b = (float)(i + 1) / columns;
                UIVertex b0 = Lerp(bl, br, a), t0 = Lerp(tl, tr, a), t1 = Lerp(tl, tr, b), b1 = Lerp(bl, br, b);
                grid.Add(b0); grid.Add(t0); grid.Add(t1);
                grid.Add(t1); grid.Add(b1); grid.Add(b0);
            }
            src = grid;
        }

        RectTransform self = (RectTransform)transform;
        RectTransform rt = reference != null ? reference : self;
        Rect r = rt.rect;
        float half = Mathf.Max(1f, r.width * 0.5f);
        float ang = yaw * Mathf.Deg2Rad, D = distance * r.width;
        bool other = rt != self;
        for (int i = 0; i < src.Count; i++)
        {
            UIVertex v = src[i];
            Vector3 p = other ? ToRef(self, rt, v.position) : v.position;
            float nx = (p.x - r.center.x) / half;
            float z = nx * half * Mathf.Sin(ang);
            float f = D / Mathf.Max(1f, D + z);
            p.x = r.center.x + nx * half * Mathf.Cos(ang) * f;
            p.y = r.center.y + (p.y - r.center.y) * f;
            v.position = other ? FromRef(self, rt, p) : p;
            src[i] = v;
        }
        vh.Clear();
        vh.AddUIVertexTriangleStream(src);
    }

    // v148: 자기 → 기준 좌표 변환을 부모 사슬의 '로컬' 값으로 계산
    //  (예전: 월드 좌표를 거쳤는데, 홀로그램이 크기 0에서 펼쳐지는 순간 변환이 깨져 글자가 사라졌음)
    private static Vector3 ToRef(Transform self, Transform reference, Vector3 p)
    {
        for (Transform t = self; t != null && t != reference; t = t.parent)
            p = t.localRotation * Vector3.Scale(t.localScale, p) + t.localPosition;
        return p;
    }
    private static Vector3 FromRef(Transform self, Transform reference, Vector3 p)
    {
        // 기준에서 자기까지 내려가며 거꾸로
        var chain = new List<Transform>();
        for (Transform t = self; t != null && t != reference; t = t.parent) chain.Add(t);
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            Transform t = chain[i];
            Vector3 s = t.localScale;
            p = Quaternion.Inverse(t.localRotation) * (p - t.localPosition);
            p = new Vector3(s.x != 0f ? p.x / s.x : 0f, s.y != 0f ? p.y / s.y : 0f, s.z != 0f ? p.z / s.z : 0f);
        }
        return p;
    }

    private static UIVertex Lerp(UIVertex a, UIVertex b, float t)
    {
        UIVertex v = a;
        v.position = Vector3.Lerp(a.position, b.position, t);
        v.uv0 = Vector4.Lerp(a.uv0, b.uv0, t);
        v.uv1 = Vector4.Lerp(a.uv1, b.uv1, t);
        v.color = Color32.Lerp(a.color, b.color, t);
        v.normal = a.normal; v.tangent = a.tangent;
        return v;
    }
}
