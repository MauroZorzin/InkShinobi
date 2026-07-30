Shader "Hidden/FullScreenOutline"
{
    // Screen-space outline over the whole frame, from the camera's depth and normal buffers.
    //
    // Detection and stroke width are deliberately kept SEPARATE:
    //  - EdgeAt(uv) detects an edge at a FIXED 1-texel offset (Roberts cross) — maximum precision,
    //    always comparing genuinely adjacent pixels, never blurred by a variable sampling radius.
    //  - The fragment then samples EdgeAt() at several points around a ring whose radius is scaled
    //    by depth (see PerspectiveOffset) and keeps the strongest hit — this "dilates" the crisp
    //    1-texel edge out to a perspective-correct on-screen width (thicker close to the camera,
    //    thinner far away) without the detection itself ever losing precision.
    //
    // The depth test also fades out on surfaces seen at a grazing angle (dot(normal, viewDir) near
    // zero, e.g. a floor stretching to the horizon) via _GrazingAngleSuppression — such surfaces
    // produce a large raw depth gradient per screen pixel purely from viewing angle, independent of
    // distance, which a depth-only test can't otherwise tell apart from a real silhouette break;
    // the normal test (angle/distance-independent) is what actually catches real edges there.
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)

        [Header(Perspective Thickness)]
        _OutlineThickness ("Outline Thickness (texels @ Reference Distance)", Range(0.1, 10)) = 1.5
        _ThicknessReferenceDistance ("Reference Distance (world units)", Float) = 10
        _MinThicknessScale ("Min Thickness (x Base, far away)", Range(0, 1)) = 0.3
        _MaxThicknessScale ("Max Thickness (x Base, close up)", Range(0, 8)) = 4

        [Header(Edge Detection)]
        _DepthSensitivity ("Depth Sensitivity", Range(0, 500)) = 150
        _NormalSensitivity ("Normal Sensitivity", Range(0, 50)) = 4
        _EdgeThreshold ("Edge Threshold", Range(0.01, 2)) = 0.25
        _GrazingAngleSuppression ("Grazing Angle Suppression", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "FullScreenOutline"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // Core.hlsl (pulled in transitively by the two Declare*.hlsl includes below) must be
            // included before Blit.hlsl — Blit.hlsl uses TEXTURE2D_X, which is defined by
            // Core.hlsl's own include chain (TextureXR.hlsl), not by Blit.hlsl's plain Common.hlsl
            // include. Included in the wrong order this fails to compile with "unrecognized
            // identifier 'TEXTURE2D_X'".
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineThickness;
                float _ThicknessReferenceDistance;
                float _MinThicknessScale;
                float _MaxThicknessScale;
                float _DepthSensitivity;
                float _NormalSensitivity;
                float _EdgeThreshold;
                float _GrazingAngleSuppression;
            CBUFFER_END

            static const int RING_SAMPLES = 8;

            // Raw depth -> linear eye-space distance, so the gradient means the same thing near
            // and far from the camera instead of being dominated by the projection's non-linear
            // depth precision curve.
            float SampleLinearDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            // How far (in texels) the outline should reach at this pixel's depth. Scaled by
            // ThicknessReferenceDistance/depth so a physically-sized line shrinks with distance the
            // way perspective actually looks. Clamped as a PROPORTION of the base thickness rather
            // than an absolute texel count — MinThicknessScale/MaxThicknessScale are multipliers on
            // _OutlineThickness, so the clamp always scales together with the base slider instead
            // of being pinned to disconnected absolute values.
            float PerspectiveRadius(float centerDepth)
            {
                float scaled = _OutlineThickness * (_ThicknessReferenceDistance / max(centerDepth, 0.01));
                return clamp(scaled, _OutlineThickness * _MinThicknessScale, _OutlineThickness * _MaxThicknessScale);
            }

            // Roberts cross at a FIXED 1-texel offset — always compares genuinely adjacent pixels,
            // so the result stays crisp regardless of what stroke-width radius the caller ends up
            // dilating it over. Returns the RELATIVE depth gradient (divided by centerDepth): a
            // surface receding from the camera naturally produces a bigger raw depth delta between
            // neighbouring pixels the farther away it is (each pixel covers more world-space area),
            // so a fixed sensitivity/threshold against the raw gradient reads false edges far away
            // and misses real ones up close. Dividing by depth cancels that out.
            float DepthEdgeAt(float2 uv, float2 texelSize)
            {
                float centerDepth = SampleLinearDepth(uv);
                float d0 = SampleLinearDepth(uv + texelSize * float2(-1, -1));
                float d1 = SampleLinearDepth(uv + texelSize * float2( 1,  1));
                float d2 = SampleLinearDepth(uv + texelSize * float2( 1, -1));
                float d3 = SampleLinearDepth(uv + texelSize * float2(-1,  1));

                float g1 = d0 - d1;
                float g2 = d2 - d3;
                return sqrt(g1 * g1 + g2 * g2) / max(centerDepth, 0.01);
            }

            // Same fixed-offset Roberts cross, over normals instead of depth. Normals are already
            // unit vectors, so unlike depth their difference doesn't scale with distance — no
            // relative correction needed here.
            float NormalEdgeAt(float2 uv, float2 texelSize)
            {
                float3 n0 = SampleSceneNormals(uv + texelSize * float2(-1, -1));
                float3 n1 = SampleSceneNormals(uv + texelSize * float2( 1,  1));
                float3 n2 = SampleSceneNormals(uv + texelSize * float2( 1, -1));
                float3 n3 = SampleSceneNormals(uv + texelSize * float2(-1,  1));

                return max(length(n0 - n1), length(n2 - n3));
            }

            // Combined, precise edge strength at this exact uv — grazingWeight (computed once per
            // output pixel, not per ring sample) suppresses the depth term on grazing-angle
            // surfaces while leaving the normal term untouched.
            float EdgeAt(float2 uv, float2 texelSize, float grazingWeight)
            {
                float depthEdge = DepthEdgeAt(uv, texelSize) * _DepthSensitivity * grazingWeight;
                float normalEdge = NormalEdgeAt(uv, texelSize) * _NormalSensitivity;
                return max(depthEdge, normalEdge);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
                float2 texelSize = _CameraDepthTexture_TexelSize.xy;

                float centerDepth = SampleLinearDepth(uv);
                float3 centerNormal = SampleSceneNormals(uv);

                float rawDepth = SampleSceneDepth(uv);
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                float facing = saturate(dot(centerNormal, viewDir));
                float grazingWeight = lerp(1.0, facing, _GrazingAngleSuppression);

                // Dilate the precise, fixed-offset edge test out to a perspective-correct stroke
                // width: sample it at several points around a ring of that radius (plus the centre)
                // and keep the strongest response, instead of widening the detection's own sampling
                // offset (which is what made the line wobble/blur before).
                float radius = PerspectiveRadius(centerDepth);
                float edge = EdgeAt(uv, texelSize, grazingWeight);

                UNITY_UNROLL
                for (int i = 0; i < RING_SAMPLES; i++)
                {
                    float angle = i * (TWO_PI / RING_SAMPLES);
                    float2 ringUV = uv + texelSize * radius * float2(cos(angle), sin(angle));
                    edge = max(edge, EdgeAt(ringUV, texelSize, grazingWeight));
                }

                edge = smoothstep(_EdgeThreshold - 0.05, _EdgeThreshold + 0.05, edge);

                half3 finalColor = lerp(sceneColor.rgb, _OutlineColor.rgb, edge * _OutlineColor.a);
                return half4(finalColor, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
