Shader "Custom/WallCutout"
{
    // Lit URP wall shader with a world-space CONE-shaped cutout: pixels within the cone from
    // _CutoutApex (radius 0, at the player) to _CutoutBase (radius _CutoutBaseRadius, at the
    // viewer/camera) are discarded, punching a hole in the wall so it doesn't block the camera's
    // view of the player. The hole tapers to nothing right at the player but widens near the
    // camera, matching how much of the view each point along that line actually blocks. Meant to
    // be driven by WallCutoutController, which pushes the apex/base/baseRadius as GLOBAL shader
    // properties each frame — every material using this shader reacts to the same cutout
    // automatically, no per-object wiring needed. The edge of the hole gets a soft, slightly
    // noisy dissolve boundary with an optional glow tint instead of a hard cut, and the same
    // cutout is honored in the ShadowCaster pass so a cut-out wall doesn't still cast a solid
    // shadow where the hole is.
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Cutout Edge Look)]
        _EdgeSoftness ("Edge Softness (world units)", Range(0.01, 2)) = 0.35
        _EdgeColor ("Edge Glow Color", Color) = (0.6, 0.85, 1, 1)
        _EdgeGlowIntensity ("Edge Glow Intensity", Range(0, 5)) = 2
        _NoiseScale ("Edge Noise Scale", Range(0, 10)) = 3
        _EdgeNoiseStrength ("Edge Noise Strength (world units)", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "WallCutoutInclude.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS = normInputs.normalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                ClipWallCutout(IN.positionWS);

                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                half3 albedo = baseTex.rgb * _BaseColor.rgb;

                float3 normalWS = normalize(IN.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 diffuse = albedo * mainLight.color * saturate(dot(normalWS, mainLight.direction)) * mainLight.shadowAttenuation;
                half3 ambient = albedo * SampleSH(normalWS);

                half3 color = diffuse + ambient + WallCutoutEdgeGlow(IN.positionWS);
                return half4(color, _BaseColor.a * baseTex.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "WallCutoutInclude.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionWS = posInputs.positionWS;
                OUT.positionCS = TransformWorldToHClip(posInputs.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                ClipWallCutout(IN.positionWS);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
