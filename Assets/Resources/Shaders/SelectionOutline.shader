// v168 선택 외곽선 (항상 보임): 지형·옆 포탑에 가려져도 실루엣 바깥 테두리를 맨 위에 그림
//  1패스: 물체 모양을 스텐실에 표시 (색은 안 씀, 깊이 무시)
//  2패스: 물체를 화면에서 바깥쪽으로 몇 픽셀 부풀려 그리되, 1패스 자리(물체 안쪽)는 빼고 테두리만
//  * 부풀리는 방향 = 물체 중심(_Center, 월드)에서 꼭짓점 쪽 (각진 ProBuilder 모델도 모서리가 끊기지 않음)
//  * Resources 폴더에 있어 빌드에 자동 포함 (TowerOutline이 Resources.Load로 불러옴)
Shader "Project_D/Selection Outline"
{
    Properties
    {
        _Color ("Color", Color) = (0.62, 1, 0.38, 1)
        _Width ("Width (px)", Float) = 3
        _Center ("Center (world)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Overlay+10" "RenderType"="Transparent" "IgnoreProjector"="True" "DisableBatching"="True" }

        CGINCLUDE
        #include "UnityCG.cginc"
        float4 _Color;
        float _Width;
        float4 _Center;
        struct appdata { float4 vertex : POSITION; };
        struct v2f { float4 pos : SV_POSITION; };
        v2f vertMask (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
        v2f vertOutline (appdata v)
        {
            v2f o;
            float4 p = UnityObjectToClipPos(v.vertex);
            float4 c = mul(UNITY_MATRIX_VP, float4(_Center.xyz, 1));
            float2 d = p.xy / p.w - c.xy / max(c.w, 1e-4);
            d.x *= _ScreenParams.x / _ScreenParams.y;          // 화면 비율 보정 (원형으로 부풀림)
            float len = length(d);
            d = len > 1e-5 ? d / len : float2(0, 1);
            d.x *= _ScreenParams.y / _ScreenParams.x;
            p.xy += d * (_Width * 2.0 / _ScreenParams.y) * p.w;
            o.pos = p;
            return o;
        }
        fixed4 fragMask (v2f i) : SV_Target { return 0; }
        fixed4 fragOutline (v2f i) : SV_Target { return _Color; }
        ENDCG

        Pass
        {
            Name "MASK"
            Cull Off ZWrite Off ZTest Always ColorMask 0
            Stencil { Ref 173 Comp Always Pass Replace }
            CGPROGRAM
            #pragma vertex vertMask
            #pragma fragment fragMask
            ENDCG
        }
        Pass
        {
            Name "OUTLINE"
            Cull Off ZWrite Off ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 173 Comp NotEqual }
            CGPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline
            ENDCG
        }
    }
}
