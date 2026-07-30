Shader "Hidden/OutlineComposite"
{
    // Pass 3 of OutlineRendererFeature: dilates the precise edge mask from pass 1 (cheap — single
    // channel, one evaluation per pixel already baked in) over a depth-scaled radius, and blends
    // the outline color over the scene color copy from pass 2.
    //
    // Dilating is a full small GRID of samples covering the whole disc (not just a ring at one
    // radius) — a fixed sample count spread over a ring that grows with radius leaves gaps between
    // samples once the ring gets big enough (thin edges slip through undetected), which read as
    // patchy/broken lines. A grid has no such gap as radius changes, it's always fully covered.
    // Each hit is weighted by how close it is to the search center (1 at the center, fading to 0 at
    // the radius) instead of a flat max — that's what turns "thickness" into an actual graduated
    // stroke width instead of a binary within-range/out-of-range disc.
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)

        [Header(Perspective Thickness)]
        _OutlineThickness ("Outline Thickness (texels @ Reference Distance)", Range(0.1, 10)) = 1.5
        _ThicknessReferenceDistance ("Reference Distance (world units)", Float) = 10
        _MinThicknessScale ("Min Thickness (x Base, far away)", Range(0, 1)) = 0.3
        _MaxThicknessScale ("Max Thickness (x Base, close up)", Range(1, 8)) = 4

        [Header(Debug)]
        [KeywordEnum(Off, RawMask, DilatedMask)] _DebugView ("Debug View", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "OutlineComposite"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_EdgeMask);
            float4 _EdgeMask_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineThickness;
                float _ThicknessReferenceDistance;
                float _MinThicknessScale;
                float _MaxThicknessScale;
                float _DebugView;
            CBUFFER_END

            static const int GRID_RADIUS = 2; // 5x5 grid (-2..2 on each axis)

            float SampleLinearDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            // How far (in texels) the dilation should reach at this pixel's depth. Scaled by
            // ThicknessReferenceDistance/depth so a physically-sized line shrinks with distance the
            // way perspective actually looks. Clamped as a PROPORTION of the base thickness (a
            // multiplier on _OutlineThickness) rather than an absolute texel count, so the clamp
            // always scales together with the base slider instead of fighting it at its limits.
            //
            // maxR is floored at minR: clamp(x, minVal, maxVal) with minVal > maxVal collapses to
            // maxVal regardless of x — so if _MaxThicknessScale is ever left at/dragged to 0 (its
            // slider allows it) while _MinThicknessScale is above 0, every radius silently became 0
            // no matter what _OutlineThickness was set to. Guarding here means a bad Max value can't
            // permanently zero out the whole effect.
            float PerspectiveRadius(float centerDepth)
            {
                float scaled = _OutlineThickness * (_ThicknessReferenceDistance / max(centerDepth, 0.01));
                float minR = _OutlineThickness * _MinThicknessScale;
                float maxR = max(minR, _OutlineThickness * _MaxThicknessScale);
                return clamp(scaled, minR, maxR);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

                float centerDepth = SampleLinearDepth(uv);
                float radius = PerspectiveRadius(centerDepth);
                float2 texelSize = _EdgeMask_TexelSize.xy;
                float2 step = (radius / GRID_RADIUS) * texelSize;

                float edge = 0;
                UNITY_UNROLL
                for (int y = -GRID_RADIUS; y <= GRID_RADIUS; y++)
                {
                    UNITY_UNROLL
                    for (int x = -GRID_RADIUS; x <= GRID_RADIUS; x++)
                    {
                        float2 offset = float2(x, y);
                        float dist = length(offset) * (radius / GRID_RADIUS);
                        float hit = SAMPLE_TEXTURE2D_X_LOD(_EdgeMask, sampler_PointClamp, uv + offset * step, 0).r;
                        float falloff = 1.0 - saturate(dist / max(radius, 0.001));
                        edge = max(edge, hit * falloff);
                    }
                }

                // Debug: 1 = raw mask straight from pass 1, no dilation — if this is black, the
                // edge-detect pass isn't writing anything (or this pass isn't reading it). 2 = the
                // dilated value used for blending, before the color lerp — if 1 shows edges but this
                // doesn't, the bug is in the dilation loop above; if this shows edges but Off doesn't,
                // the bug is in the final lerp/blend below.
                if (_DebugView > 1.5) return half4(edge.xxx, 1);
                if (_DebugView > 0.5) {
                    float raw = SAMPLE_TEXTURE2D_X_LOD(_EdgeMask, sampler_PointClamp, uv, 0).r;
                    return half4(raw.xxx, 1);
                }

                half3 finalColor = lerp(sceneColor.rgb, _OutlineColor.rgb, edge * _OutlineColor.a);
                return half4(finalColor, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
