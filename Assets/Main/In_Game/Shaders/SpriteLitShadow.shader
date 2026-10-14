// Project_D - 2D sprite in 3D world
// - Sprite itself is UNLIT: no lighting / no shadows drawn on the sprite (keeps the art readable)
// - Still CASTS a shadow on the ground (ShadowCaster pass)
// - Cull Off: visible from both sides (billboard / flipX safe)
// - Alpha cutout (_Cutoff): transparent pixels are not drawn and cast no shadow
// - Supports SpriteRenderer.flipX/flipY and SpriteRenderer.color like Sprites/Default
// - _FlashAmount/_FlashColor: hit flash overlay (set per renderer with MaterialPropertyBlock)
// - _OutlineColor/_OutlineWidth: dark pixel-art outline along the silhouette edge
//     width is in SCREEN pixels (same thickness at any zoom), drawn just inside the edge
//     (no extra padding needed in the sprite image). 0 = off
// - Upright depth (_FeetPos from Billboard): the sprite is drawn tilted toward the camera, but its DEPTH is
//     taken from an upright plane standing at the feet. So a tower behind the feet no longer cuts through
//     the upper half of the image, and a tower in front correctly covers it. Screen position is unchanged.
Shader "Project_D/Sprite Shadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [PerRendererData] _FlashColor ("Flash Color", Color) = (1,1,1,1)
        [PerRendererData] _FlashAmount ("Flash Amount", Range(0,1)) = 0
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width (screen px)", Range(0, 4)) = 1.5
        [Toggle] _UprightDepth ("Upright Depth (feet plane)", Float) = 1
        [PerRendererData] _FeetPos ("Feet Position (set by Billboard)", Vector) = (0,0,0,0)
        [PerRendererData] _OutlineSkip ("Outline Skip (x,y: bottom joint v / band texels, z,w: top joint v / band)", Vector) = (0,0,0,0)
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
            #pragma target 3.0
            #pragma multi_compile_fog
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "UnitySprites.cginc"

            fixed _Cutoff;
            fixed4 _FlashColor;
            fixed _FlashAmount;
            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _UprightDepth;
            float4 _FeetPos;
            // joined body parts (player upper/lower body): a thin band at the joint line counts as solid,
            // so no outline is drawn where the two pictures meet.
            //   x = joint line (uv v) at the BOTTOM of this picture, y = band height below it (texels, 0 = off)
            //   z = joint line (uv v) at the TOP of this picture,    w = band height above it (texels, 0 = off)
            float4 _OutlineSkip;
            float4 _MainTex_TexelSize;

            // alpha of the neighbour pixel used for the outline test
            fixed EdgeAlpha(float2 uv, float2 o)
            {
                float2 p = uv + o;
                float tv = _MainTex_TexelSize.y;
                if (_OutlineSkip.y > 0 && p.y < _OutlineSkip.x + 0.5 * tv && p.y > _OutlineSkip.x - _OutlineSkip.y * tv) return 1; // joint below (upper body)
                if (_OutlineSkip.w > 0 && p.y > _OutlineSkip.z - 0.5 * tv && p.y < _OutlineSkip.z + _OutlineSkip.w * tv) return 1; // joint above (lower body)
                if (p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1) return 0;          // outside the image = transparent
                return tex2Dlod(_MainTex, float4(p, 0, 0)).a; // no mips on sprites; lod avoids gradient-in-branch issues
            }

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
                float3 w = mul(unity_ObjectToWorld, float4(p.xyz, 1)).xyz;
                o.pos = UnityWorldToClipPos(w);

                // depth as if the sprite stood upright at its feet (same pixel, different depth)
                if (_UprightDepth > 0.5 && _FeetPos.w > 0.5)
                {
                    float3 camPos = _WorldSpaceCameraPos;
                    float3 fwd = -UNITY_MATRIX_V[2].xyz;              // camera forward (world)
                    float2 ff = fwd.xz;
                    float fl = length(ff);
                    if (fl > 0.05)                                     // skip when looking straight down
                    {
                        float3 fwdFlat = float3(ff.x / fl, 0, ff.y / fl);
                        float3 dir = w - camPos;
                        float denom = dot(dir, fwdFlat);
                        if (denom > 1e-4)
                        {
                            // w = 1 + bias: plane moved toward the camera by 'bias' meters (layering: legs < torso < weapon)
                            float3 feet = _FeetPos.xyz - fwdFlat * max(0, _FeetPos.w - 1);
                            float t = dot(feet - camPos, fwdFlat) / denom; // ray hits the upright plane at the feet
                            float4 cd = UnityWorldToClipPos(camPos + dir * t);
                            if (cd.w > 1e-4)
                            {
                                float nz = cd.z / cd.w;
                                #if UNITY_REVERSED_Z
                                nz = clamp(nz, 0.00001, 0.99999);
                                #else
                                nz = clamp(nz, -0.99999, 0.99999);
                                #endif
                                o.pos.z = nz * o.pos.w;
                            }
                        }
                    }
                }
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

                // outline: this pixel is solid, but a neighbour (N screen px away) is transparent -> edge
                if (_OutlineWidth > 0.01)
                {
                    float2 d = fwidth(i.uv) * _OutlineWidth;
                    fixed n = 1;
                    n = min(n, EdgeAlpha(i.uv, float2( d.x, 0)));
                    n = min(n, EdgeAlpha(i.uv, float2(-d.x, 0)));
                    n = min(n, EdgeAlpha(i.uv, float2(0,  d.y)));
                    n = min(n, EdgeAlpha(i.uv, float2(0, -d.y)));
                    n = min(n, EdgeAlpha(i.uv, d * 0.7071));
                    n = min(n, EdgeAlpha(i.uv, -d * 0.7071));
                    n = min(n, EdgeAlpha(i.uv, float2(d.x, -d.y) * 0.7071));
                    n = min(n, EdgeAlpha(i.uv, float2(-d.x, d.y) * 0.7071));
                    if (n * i.color.a < _Cutoff) c.rgb = _OutlineColor.rgb;
                }
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
