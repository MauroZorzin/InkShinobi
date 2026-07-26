Shader "Hidden/OutlineEdgeMask"
{
    // Pass 1 of OutlineRendererFeature: writes a precise, single-channel edge-strength mask,
    // evaluated exactly once per pixel at a fixed 1-texel offset (never blurred by a variable
    // sampling radius — that's pass 3's job, dilating THIS mask). Two independent tests combined:
    //  - Depth: Roberts cross over linear-eye depth, divided by the pixel's own depth so a surface
    //    receding from the camera doesn't read as a false edge just because raw depth deltas grow
    //    with distance (every screen pixel covers more world-space area the farther away it is).
    //  - Normal: Roberts cross over the normal buffer — catches creases where depth barely changes
    //    (e.g. a cube's edge), and is naturally distance/angle independent since normals are unit
    //    vectors.
    // The depth term additionally fades out on grazing-angle surfaces (dot(normal,viewDir) near
    // zero, e.g. a floor stretching to the horizon), which produce a large depth gradient purely
    // from viewing angle rather than an actual silhouette break; the normal term still catches real
    // edges there.
    Properties
    {
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
            Name "OutlineEdgeMask"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // Core.hlsl (pulled in transitively by the two Declare*.hlsl includes below) must be
            // included before Blit.hlsl — Blit.hlsl uses TEXTURE2D_X, which Core.hlsl's own include
            // chain (TextureXR.hlsl) defines, not Blit.hlsl's plain Common.hlsl include.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _DepthSensitivity;
                float _NormalSensitivity;
                float _EdgeThreshold;
                float _GrazingAngleSuppression;
            CBUFFER_END

            float SampleLinearDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

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

            float NormalEdgeAt(float2 uv, float2 texelSize)
            {
                float3 n0 = SampleSceneNormals(uv + texelSize * float2(-1, -1));
                float3 n1 = SampleSceneNormals(uv + texelSize * float2( 1,  1));
                float3 n2 = SampleSceneNormals(uv + texelSize * float2( 1, -1));
                float3 n3 = SampleSceneNormals(uv + texelSize * float2(-1,  1));

                return max(length(n0 - n1), length(n2 - n3));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texelSize = _CameraDepthTexture_TexelSize.xy;

                float3 centerNormal = SampleSceneNormals(uv);
                float rawDepth = SampleSceneDepth(uv);
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                float facing = saturate(dot(centerNormal, viewDir));
                float grazingWeight = lerp(1.0, facing, _GrazingAngleSuppression);

                float depthEdge = DepthEdgeAt(uv, texelSize) * _DepthSensitivity * grazingWeight;
                float normalEdge = NormalEdgeAt(uv, texelSize) * _NormalSensitivity;

                float edge = max(depthEdge, normalEdge);
                edge = smoothstep(_EdgeThreshold - 0.05, _EdgeThreshold + 0.05, edge);

                return half4(edge, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
