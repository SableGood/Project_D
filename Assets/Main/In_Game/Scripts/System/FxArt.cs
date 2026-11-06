using System.Collections.Generic;
using UnityEngine;

// 작은 그림 모음 (v109)
//  - Resources/Image/FX 의 낱장 그림(포탄·요정)을 스프라이트로 읽기 (도트 그대로: 점 필터)
//  - 코드로 그리는 작은 도트 그림: 몹 총탄(예광탄), 머리 위 느낌표(!)
//  스프라이트는 1픽셀 = 1유닛으로 만들고, 쓰는 쪽에서 localScale로 크기를 정함
public static class FxArt
{
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    public static Sprite Load(string name, Vector2 pivot)
    {
        string key = name + pivot;
        if (cache.TryGetValue(key, out Sprite s)) return s;
        Texture2D tex = Resources.Load<Texture2D>("Image/FX/" + name);
        s = null;
        if (tex != null)
        {
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, 1f, 0, SpriteMeshType.FullRect);
            s.name = name;
        }
        cache[key] = s;
        return s;
    }

    // 몹 총탄: 위쪽이 머리 (흰 심 → 노랑 → 주황 꼬리)
    private static Sprite bullet;
    public static Sprite Bullet
    {
        get
        {
            if (bullet != null) return bullet;
            string[] rows =
            {
                ".oWo.",
                "oWWWo",
                "oWYWo",
                "oYYYo",
                ".YOY.",
                ".OOO.",
                ".ORO.",
                "..R..",
                "..r..",
            };
            bullet = FromPattern(rows, new Vector2(0.5f, 0.75f), c =>
            {
                switch (c)
                {
                    case 'W': return new Color(1f, 1f, 0.92f, 1f);
                    case 'Y': return new Color(1f, 0.9f, 0.35f, 1f);
                    case 'O': return new Color(1f, 0.58f, 0.16f, 1f);
                    case 'R': return new Color(0.9f, 0.28f, 0.1f, 0.85f);
                    case 'r': return new Color(0.8f, 0.2f, 0.08f, 0.45f);
                    case 'o': return new Color(0.45f, 0.12f, 0.04f, 1f);
                }
                return Color.clear;
            });
            return bullet;
        }
    }

    // 머리 위 느낌표 (빨강, 검은 테두리)
    private static Sprite alert;
    public static Sprite Alert
    {
        get
        {
            if (alert != null) return alert;
            string[] rows =
            {
                ".kkkk.",
                "kRRRRk",
                "kRrrRk",
                "kRrrRk",
                "kRrrRk",
                "kRrrRk",
                ".kRRk.",
                ".kRRk.",
                "..kk..",
                ".kkkk.",
                "kRRRRk",
                "kRrrRk",
                ".kkkk.",
            };
            alert = FromPattern(rows, new Vector2(0.5f, 0f), c =>
            {
                switch (c)
                {
                    case 'k': return new Color(0.12f, 0.02f, 0.02f, 1f);
                    case 'R': return new Color(0.92f, 0.12f, 0.1f, 1f);
                    case 'r': return new Color(1f, 0.42f, 0.36f, 1f);
                }
                return Color.clear;
            });
            return alert;
        }
    }

    private static Sprite FromPattern(string[] rows, Vector2 pivot, System.Func<char, Color> pal)
    {
        int h = rows.Length, w = rows[0].Length;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, h - 1 - y, x < rows[y].Length ? pal(rows[y][x]) : Color.clear);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), pivot, 1f, 0, SpriteMeshType.FullRect);
    }
}
