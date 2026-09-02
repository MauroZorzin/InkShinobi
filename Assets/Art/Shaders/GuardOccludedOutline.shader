Shader "Hidden/InkShinobi/GuardOccludedOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SolidOutline ("Outline Color", Color) = (1,1,1,1)
        _Thickness ("Width", Float) = 8
        _AAWidth ("Edge Smoothing", Range(0.001, 0.25)) = 0.05
        [HideInInspector] _AlphaThreshold ("Alpha Threshold", Range(0,1)) = 0
        [MaterialToggle] _ConnectedAlpha ("Connected Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
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
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            struct Attributes
            {
                float4 vertex : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_AlphaTex);
            SAMPLER(sampler_AlphaTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _SolidOutline;
                float _Thickness;
                float _AAWidth;
                float _AlphaThreshold;
                float _ConnectedAlpha;
                float _AlphaSplitEnabled;
                float4 _MainTex_ST;
                float4 _MainTex_TexelSize;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                SetUpSpriteInstanceProperties();
                float3 positionOS = UnityFlipSprite(input.vertex.xyz, unity_SpriteProps.xy);
                output.positionCS = TransformObjectToHClip(positionOS);
                output.color = input.color;
                output.uv = input.uv;
                #ifdef PIXELSNAP_ON
                    float2 halfPixelCount = _ScreenParams.xy * 0.5;
                    float2 pixelPosition = round((output.positionCS.xy / output.positionCS.w) * halfPixelCount);
                    output.positionCS.xy = pixelPosition / halfPixelCount * output.positionCS.w;
                #endif
                return output;
            }

            half SampleAlpha(float2 uv)
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, uv).a;
                #if UNITY_TEXTURE_ALPHASPLIT_ALLOWED
                    if (_AlphaSplitEnabled)
                        alpha = SAMPLE_TEXTURE2D(_AlphaTex, sampler_LinearClamp, uv).r;
                #endif
                return alpha;
            }

            half SampleAlphaLod(float2 uv)
            {
                half alpha = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_LinearClamp, uv, 0).a;
                #if UNITY_TEXTURE_ALPHASPLIT_ALLOWED
                    if (_AlphaSplitEnabled)
                        alpha = SAMPLE_TEXTURE2D_LOD(_AlphaTex, sampler_LinearClamp, uv, 0).r;
                #endif
                return alpha;
            }

            half4 frag(Varyings input) : SV_Target
            {
                const int sampleCount = 32;
                half centerAlpha = SampleAlpha(input.uv);
                half alphaThreshold = _AlphaThreshold / 10.0h;

                // The occluded pass draws only the exterior contour, never the hidden sprite fill.
                if (centerAlpha > alphaThreshold + _AAWidth)
                    return half4(0, 0, 0, 0);

                float2 thickness = float2(
                    _Thickness / _MainTex_TexelSize.z,
                    _Thickness / _MainTex_TexelSize.w);
                half coverage = 0;
                for (int i = 0; i < sampleCount; i++)
                {
                    float angle = i * (TWO_PI / sampleCount);
                    float2 offset = thickness * float2(cos(angle), sin(angle));
                    half alpha = SampleAlphaLod(input.uv + offset);
                    coverage = max(coverage, smoothstep(alphaThreshold, alphaThreshold + _AAWidth, alpha));
                }

                half4 color = _SolidOutline;
                if (_ConnectedAlpha != 0)
                    color.a *= _Color.a;
                color.a *= coverage * input.color.a;
                color.rgb *= color.a;
                return color;
            }
            ENDHLSL
        }
    }
}
