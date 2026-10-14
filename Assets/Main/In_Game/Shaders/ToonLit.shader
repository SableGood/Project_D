// Project_D - pixel-art style lighting for 3D objects (towers, floor)
// - Light is cut into a few flat steps (no smooth gradient), like hand-shaded pixel art
// - Shadow side is tinted with _ShadowColor instead of going black
// - No specular highlight, no sky reflection, no ambient sky tint
// - Receives and casts shadows; optional normal map (_BumpMap)
Shader "Project_D/Toon Lit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1.0
        _Steps ("Light Steps", Range(1, 5)) = 3
        _FullLight ("Full Light At (lower = brighter)", Range(0.2, 1)) = 0.6
        _StepOffset ("Step Offset (bright <-> dark)", Range(-0.5, 0.5)) = 0.1
        _ShadowColor ("Shadow Tint", Color) = (0.45, 0.43, 0.56, 1)
        _Brightness ("Brightness", Range(0.5, 1.5)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Toon fullforwardshadows noambient noforwardadd nolightmap nodynlightmap nodirlightmap exclude_path:deferred exclude_path:prepass
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        fixed4 _Color;
        half _BumpScale;
        half _Steps;
        half _StepOffset;
        half _FullLight;
        fixed4 _ShadowColor;
        half _Brightness;

        struct Input
        {
            float2 uv_MainTex;
        };

        half4 LightingToon (SurfaceOutput s, half3 lightDir, half atten)
        {
            half ndl = saturate(dot(s.Normal, lightDir));
            half lit = saturate(ndl * atten / max(_FullLight, 0.01)); // light x shadow, 0..1 (>= _FullLight counts as full light)
            half band = saturate(floor((lit + _StepOffset) * _Steps) / _Steps); // cut into flat steps
            half3 tint = lerp(_ShadowColor.rgb, half3(1, 1, 1), band);
            half4 c;
            c.rgb = s.Albedo * tint * _LightColor0.rgb * _Brightness;
            c.a = s.Alpha;
            return c;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
            half3 n = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
            n.xy *= _BumpScale;
            o.Normal = normalize(n);
        }
        ENDCG
    }

    Fallback "Diffuse"
}
