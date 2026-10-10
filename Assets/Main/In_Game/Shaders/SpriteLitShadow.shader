// Project_D - 2D sprite in 3D world
// - Sprite itself is UNLIT: no lighting / no shadows drawn on the sprite (keeps the art readable)
// - Still CASTS a shadow on the ground (ShadowCaster pass)
// - Cull Off: visible from both sides (billboard / flipX safe)
// - Alpha cutout (_Cutoff): transparent pixels are not drawn and cast no shadow
// - Supports SpriteRenderer.flipX/flipY and SpriteRenderer.color like Sprites/Default
// - _FlashAmount/_FlashColor: hit flash overlay (set per renderer with MaterialPropertyBlock)
Shader "Project_D/Sprite Shadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [PerRendererData] _FlashColor ("Flash Color", Color) = (1,1,1,1)
        [PerRendererData] _FlashAmount ("Flash Amount", Range(0,1)) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="AlphaTest"
            "IgnoreProjector"="True"
            "RenderType"="TransparentCutout"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Cull Off

        // ---------------- Color (unlit) ----------------
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            fixed _Cutoff;
            fixed4 _FlashColor;
            fixed _FlashAmount;

            struct appdata_pd
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f_pd
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv    : TEXCOORD0;
                UNITY_FOG_COORDS(1)
            };

            v2f_pd vert (appdata_pd v)
            {
                v2f_pd o;
                float4 p = UnityFlipSprite(v.vertex.xyz, _Flip);
                o.pos = UnityObjectToClipPos(p);
                o.uv = v.uv;
                o.color = v.color * _Color * _RendererColor;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f_pd i) : SV_Target
            {
                fixed4 c = SampleSpriteTexture(i.uv) * i.color;
                clip(c.a - _Cutoff);
                c.rgb = lerp(c.rgb, _FlashColor.rgb, _FlashAmount); // hit flash (MaterialPropertyBlock)
                c.a = 1;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }

        // ---------------- Shadow caster (ground shadow) ----------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off

            CGPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_shadowcaster
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnityCG.cginc"
            #include "UnitySprites.cginc"

            fixed _Cutoff;

            struct v2f_shadow
            {
                V2F_SHADOW_CASTER;
                float2 uv : TEXCOORD1;
            };

            v2f_shadow vertShadow (appdata_base v)
            {
                v2f_shadow o;
                v.vertex = UnityFlipSprite(v.vertex.xyz, _Flip);
                v.normal = float3(0, 0, -1); // sprite faces -Z
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                o.uv = v.texcoord.xy;
                return o;
            }

            float4 fragShadow (v2f_shadow i) : SV_Target
            {
                fixed a = SampleSpriteTexture(i.uv).a;
                clip(a - _Cutoff);
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }

    Fallback Off
}
