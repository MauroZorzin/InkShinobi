Shader "Sprites/Outline2"
{
    // Modernized from a legacy Built-in RP (CGPROGRAM + UnityCG.cginc) sprite outline shader to
    // native URP HLSL. Same properties, same modes (Solid/Gradient/Image, Contour/Frame), same
    // defaults — behavior is unchanged except the outline's own edge is now antialiased instead
    // of a hard per-sample threshold, which is what caused the stair-stepped/pixelated look.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Main texture Tint", Color) = (1,1,1,1)

        [Header(General Settings)]
        [MaterialToggle] _OutlineEnabled ("Outline Enabled", Float) = 1
        [MaterialToggle] _ShowFill ("Show Sprite Fill", Float) = 1
        [MaterialToggle] _ConnectedAlpha ("Connected Alpha", Float) = 0
        [HideInInspector] _AlphaThreshold ("Alpha clean", Range (0, 1)) = 0
        _Thickness ("Width (Max recommended 100)", Float) = 10
        _AAWidth ("Outline Edge Smoothing", Range(0.001, 0.25)) = 0.05
        [KeywordEnum(Solid, Gradient, Image)] _OutlineMode("Outline mode", Float) = 0
        [KeywordEnum(Contour, Frame)] _OutlineShape("Outline shape", Float) = 0
        [KeywordEnum(Inside under sprite, Inside over sprite, Outside)] _OutlinePosition("Outline Position (Frame Only)", Float) = 0

        [Header(Solid Settings)]
        _SolidOutline ("Outline Color Base", Color) = (1,1,1,1)

        [Header(Gradient Settings)]
        _GradientOutline1 ("Outline Color 1", Color) = (1,1,1,1)
        _GradientOutline2 ("Outline Color 2", Color) = (1,1,1,1)
        _Weight ("Weight", Range (0, 1)) = 0.5
        _Angle ("Gradient Angle (General gradient Only)", Float) = 45

        [Header(Image Settings)]
        _FrameTex ("Frame Texture", 2D) = "white" {}
        _ImageOutline ("Outline Color Base", Color) = (1,1,1,1)
        [KeywordEnum(Stretch, Tile)] _TileMode("Frame mode", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex   : SV_POSITION;
                half4 color     : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_AlphaTex);
            SAMPLER(sampler_AlphaTex);
            TEXTURE2D(_FrameTex);
            SAMPLER(sampler_FrameTex);

            // sampler_LinearClamp is declared globally by Core.hlsl (GlobalSamplers.hlsl) — used
            // only for the outline ring test below so its antialiasing doesn't depend on the
            // sprite texture's own Filter Mode (which usually stays Point for crisp pixel art).

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Thickness;
                float _AAWidth;
                float _OutlineEnabled;
                float _ShowFill;
                float _ConnectedAlpha;
                float _OutlineShape;
                float _OutlinePosition;
                float _OutlineMode;

                half4 _SolidOutline;

                half4 _GradientOutline1;
                half4 _GradientOutline2;
                float _Weight;
                float _AlphaThreshold;
                float _Angle;

                half4 _ImageOutline;
                float _TileMode;

                float _AlphaSplitEnabled;
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
                float4 _FrameTex_ST;
                float4 _FrameTex_TexelSize;
            CBUFFER_END

            float4 PixelSnap(float4 pos)
            {
                float2 hpc = _ScreenParams.xy * 0.5;
                float2 pixelPos = round((pos.xy / pos.w) * hpc);
                pos.xy = pixelPos / hpc * pos.w;
                return pos;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                OUT.vertex = TransformObjectToHClip(IN.vertex.xyz);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color * _Color;
                #ifdef PIXELSNAP_ON
                OUT.vertex = PixelSnap(OUT.vertex);
                #endif
                return OUT;
            }

            // ---------------------------------------------------------------
            // FIX: Removed UV remapping that assumed UVs span [0,1].
            // That assumption breaks sprite atlases/sheets where UVs are a
            // sub-region of the full texture. Now we sample directly.
            // Sprites need padding (Extrude Edges >= Thickness) set in the
            // Unity Sprite Editor so outline samples land on transparent pixels.
            // ---------------------------------------------------------------
            half4 SampleSpriteTexture(float2 uv)
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);

                #if UNITY_TEXTURE_ALPHASPLIT_ALLOWED
                    if (_AlphaSplitEnabled)
                        color.a = SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, uv).r;
                #endif

                return color;
            }

            // Same sample as above but through a forced bilinear sampler, used only by the ring
            // test below so the outline's own edge can be smoothed independently of the sprite's
            // Filter Mode setting.
            half SampleAlphaAA(float2 uv)
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, uv).a;

                #if UNITY_TEXTURE_ALPHASPLIT_ALLOWED
                    if (_AlphaSplitEnabled)
                        a = SAMPLE_TEXTURE2D(_AlphaTex, sampler_LinearClamp, uv).r;
                #endif

                return a;
            }

            // Antialiased replacement for the old "is any neighbour within `thickness` opaque"
            // ring test. Same idea (scan a ring of samples at the outline's radius, threshold
            // against alpha), but instead of a hard boolean OR over 100 fixed samples it takes a
            // soft maximum via smoothstep, so the ring's own boundary fades over _AAWidth instead
            // of stair-stepping. Returns 0..1 coverage instead of a bool.
            half CheckOriginalSpriteTextureCoverage(float2 uv, float thicknessX, float thicknessY)
            {
                const float alphaThreshold = _AlphaThreshold / 10;
                const int steps = 32;

                half coverage = 0;
                for (int i = 0; i < steps; i++)
                {
                    float angle = i * (TWO_PI / steps);
                    float2 offset = float2(thicknessX * cos(angle), thicknessY * sin(angle));
                    half a = SampleAlphaAA(uv + offset);
                    coverage = max(coverage, smoothstep(alphaThreshold, alphaThreshold + _AAWidth, a));
                }

                return coverage;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float thicknessX = _Thickness / _MainTex_TexelSize.z;
                float thicknessY = _Thickness / _MainTex_TexelSize.w;

                half4 c = SampleSpriteTexture(IN.texcoord) * IN.color;
                c.rgb *= c.a;

                if (_OutlineEnabled == 0)
                    return c;

                half4 fillOrEmpty = _ShowFill != 0 ? c : half4(0, 0, 0, 0);
                half4 outlineC = half4(0, 0, 0, 1);

                // Ring coverage only matters for Contour shape on pixels outside the sprite —
                // computed once and reused by both the Gradient ratio gate and the final blend.
                half coverage = 0;
                if (_OutlineShape == 0 && c.a == 0)
                    coverage = CheckOriginalSpriteTextureCoverage(IN.texcoord, thicknessX, thicknessY);

                // ── Solid ───────────────────────────────────────────────
                if (_OutlineMode == 0)
                {
                    outlineC = _SolidOutline;
                    if (_ConnectedAlpha != 0)
                        outlineC.a *= _Color.a;
                    outlineC.rgb *= outlineC.a;
                }
                // ── Gradient ────────────────────────────────────────────
                else if (_OutlineMode == 1)
                {
                    float x = IN.texcoord.x;
                    float y = IN.texcoord.y;
                    float ratio1 = 0;
                    float ratio2 = 0;

                    if (_OutlineShape == 0) // Contour
                    {
                        if (coverage > 0)
                        {
                            float angle = _Angle;
                            if (angle >= 360)
                            {
                                int div = angle / 360;
                                angle = (angle / 360 - div) * 360;
                            }
                            angle *= TWO_PI / 360;

                            ratio1 = (0.5 - x) * cos(angle) + (0.5 - y) * sin(angle) + 0.5;
                            ratio2 = (x - 0.5) * cos(angle) + (y - 0.5) * sin(angle) + 0.5;

                            ratio1 *= 2 * _Weight;
                            ratio2 *= 2 * (1 - _Weight);

                            half4 g1 = _GradientOutline1;
                            half4 g2 = _GradientOutline2;
                            if (_ConnectedAlpha != 0)
                            {
                                g1.a *= _Color.a;
                                g2.a *= _Color.a;
                            }
                            g1.rgb *= g1.a;
                            g2.rgb *= g2.a;
                            outlineC = g1 * ratio1 + g2 * ratio2;
                        }
                    }
                    else if (_OutlineShape == 1) // Frame
                    {
                        if (
                            IN.texcoord.y + thicknessY > 1 ||
                            IN.texcoord.y - thicknessY < 0 ||
                            IN.texcoord.x + thicknessX > 1 ||
                            IN.texcoord.x - thicknessX < 0
                        )
                        {
                            // Left edge
                            if ( y * thicknessX - x * thicknessY > 0 &&
                                 y * thicknessX + x * thicknessY - thicknessX < 0 &&
                                 x < 0.5)
                            {
                                ratio1 = 1 - x / thicknessX;
                                ratio2 = x / thicknessX;
                            }
                            // Bottom edge
                            else if (y * thicknessX - x * thicknessY < 0 &&
                                     y * thicknessX + x * thicknessY - thicknessY < 0 &&
                                     y < 0.5)
                            {
                                ratio1 = 1 - y / thicknessY;
                                ratio2 = y / thicknessY;
                            }
                            // Right edge
                            else if (y * thicknessX - x * thicknessY - thicknessX + thicknessY < 0 &&
                                     y * thicknessX + x * thicknessY - thicknessY > 0 &&
                                     x > 0.5)
                            {
                                ratio1 = (x - 1) / thicknessX + 1;
                                ratio2 = -(x - 1) / thicknessX;
                            }
                            // Top edge
                            else if (y * thicknessX - x * thicknessY - thicknessX + thicknessY > 0 &&
                                     y * thicknessX + x * thicknessY - thicknessX > 0 &&
                                     y > 0.5)
                            {
                                ratio1 = (y - 1) / thicknessY + 1;
                                ratio2 = -(y - 1) / thicknessY;
                            }

                            ratio1 *= 2 * _Weight;
                            ratio2 *= 2 * (1 - _Weight);

                            half4 g1 = _GradientOutline1;
                            half4 g2 = _GradientOutline2;
                            if (_ConnectedAlpha != 0)
                            {
                                g1.a *= _Color.a;
                                g2.a *= _Color.a;
                            }
                            g1.rgb *= g1.a;
                            g2.rgb *= g2.a;
                            outlineC = g1 * ratio1 + g2 * ratio2;
                        }
                    }
                }
                // ── Image ───────────────────────────────────────────────
                else if (_OutlineMode == 2)
                {
                    outlineC = _ImageOutline;
                    float2 frame_coord;

                    if (_TileMode == 0)
                    {
                        frame_coord = IN.texcoord;
                    }
                    else // Tile
                    {
                        frame_coord = float2(
                            _FrameTex_ST.x * IN.texcoord.x * _MainTex_TexelSize.z / _FrameTex_TexelSize.z - _FrameTex_ST.z,
                            _FrameTex_ST.y * IN.texcoord.y * _MainTex_TexelSize.w / _FrameTex_TexelSize.w - _FrameTex_ST.w
                        );

                        if (frame_coord.x > 1)
                            frame_coord.x -= floor(frame_coord.x);
                        if (frame_coord.y > 1)
                            frame_coord.y -= floor(frame_coord.y);
                    }

                    half4 text = SAMPLE_TEXTURE2D(_FrameTex, sampler_FrameTex, frame_coord);
                    text.rgb *= text.a;
                    outlineC.rgb *= text.rgb;
                    outlineC.a *= text.a;

                    if (_ConnectedAlpha != 0)
                        outlineC.a *= _Color.a;
                    outlineC.rgb *= outlineC.a;
                }

                // ── Shape dispatch ──────────────────────────────────────
                if (_OutlineShape == 1) // Frame
                {
                    bool onFrameEdge =
                        IN.texcoord.y + thicknessY > 1 ||
                        IN.texcoord.y - thicknessY < 0 ||
                        IN.texcoord.x + thicknessX > 1 ||
                        IN.texcoord.x - thicknessX < 0;

                    if (onFrameEdge)
                    {
                        if (_OutlinePosition == 0 && c.a != 0 && _Thickness > 0)
                            return fillOrEmpty;
                        return outlineC;
                    }
                    return fillOrEmpty;
                }
                else if (_OutlineShape == 0 && _Thickness > 0) // Contour
                {
                    if (c.a == 0)
                        return lerp(fillOrEmpty, outlineC, coverage);
                    return fillOrEmpty;
                }

                return fillOrEmpty;
            }
            ENDHLSL
        }
    }
}
