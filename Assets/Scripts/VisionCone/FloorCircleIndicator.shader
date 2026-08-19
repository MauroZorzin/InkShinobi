Shader "Hidden/FloorCircleIndicator"
{
    Properties
    {
        _FillColor ("Fill Color", Color) = (1, 0.85, 0.3, 0.15)
        _RingColor ("Ring Color", Color) = (1, 0.85, 0.3, 1)
        _RingStart ("Ring Start", Range(0, 1)) = 0.85
        _Softness ("Softness", Range(0.001, 0.3)) = 0.05
        _OcclusionMask ("Occlusion Mask", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue"="Transparent-100" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_OcclusionMask); SAMPLER(sampler_OcclusionMask);

            CBUFFER_START(UnityPerMaterial)
                half4 _FillColor;
                half4 _RingColor;
                float _RingStart;
                float _Softness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 centered = IN.uv - 0.5;
                float dist = length(centered) * 2;
                clip(1 - dist);

                float ring = smoothstep(_RingStart - _Softness, _RingStart + _Softness, dist);
                half3 color = lerp(_FillColor.rgb, _RingColor.rgb, ring);
                half alpha = lerp(_FillColor.a, _RingColor.a, ring);

                float edgeFade = 1 - smoothstep(1 - _Softness, 1, dist);
                alpha *= edgeFade;

                half occlusion = SAMPLE_TEXTURE2D(_OcclusionMask, sampler_OcclusionMask, IN.uv).r;
                alpha *= occlusion;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
