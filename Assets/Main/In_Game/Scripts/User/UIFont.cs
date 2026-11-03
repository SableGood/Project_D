using UnityEngine;
using UnityEngine.UI;

// 게임 UI 공용 글꼴: Galmuri11 (도트 글꼴, SIL Open Font License 1.1 — Resources/Fonts/Galmuri_OFL.txt)
//  - 12의 배수 크기(12, 24, 36, 48)에서 가장 또렷함
//  - 굵게는 가짜 굵게 대신 Galmuri11-Bold 글꼴을 씀 (가짜 굵게는 도트가 뭉개짐)
public static class UIFont
{
    private static Font regular, bold;

    public static Font Regular => regular != null ? regular : (regular = Load("Fonts/Galmuri11", null));
    public static Font Bold => bold != null ? bold : (bold = Load("Fonts/Galmuri11-Bold", Regular));

    private static Font Load(string path, Font fallback)
    {
        Font f = Resources.Load<Font>(path);
        if (f == null) f = fallback;
        if (f == null) f = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Arial" }, 32);
        if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return f;
    }

    // 글꼴과 굵기를 함께 지정
    public static void Apply(Text t, FontStyle style)
    {
        if (t == null) return;
        bool b = style == FontStyle.Bold || style == FontStyle.BoldAndItalic;
        t.font = b ? Bold : Regular;
        t.fontStyle = b ? (style == FontStyle.BoldAndItalic ? FontStyle.Italic : FontStyle.Normal) : style;
    }

    // 이미 만든 UI 아래의 '굵게' 글자를 Bold 글꼴로 바꿈 (UI를 다 만든 뒤 한 번 호출)
    public static void FixBold(Component root)
    {
        if (root == null) return;
        foreach (Text t in root.GetComponentsInChildren<Text>(true))
            if (t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic) Apply(t, t.fontStyle);
    }
}
