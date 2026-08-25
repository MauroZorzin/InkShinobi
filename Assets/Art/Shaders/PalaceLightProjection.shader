Shader "InkShinobi/PalaceLightProjection" {
  Properties {
    _Color ("Light Color", Color) = (1, 0.72, 0.28, 0.7)
    _EdgeSoftness ("Edge Softness", Range(0.01, 1)) = 0.35
    _Intensity ("Intensity", Range(0, 3)) = 1
  }

  SubShader {
    Tags {
      "RenderPipeline" = "UniversalPipeline"
      "Queue" = "Transparent"
      "RenderType" = "Transparent"
    }

    Pass {
      Name "PalaceLightProjection"
      Tags { "LightMode" = "UniversalForward" }
      Blend SrcAlpha OneMinusSrcAlpha
      ZWrite Off
      ZTest LEqual
      Cull Off

      HLSLPROGRAM
      #pragma vertex Vert
      #pragma fragment Frag

      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

      struct Attributes {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
      };

      struct Varyings {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
      };

      CBUFFER_START(UnityPerMaterial)
        half4 _Color;
        half _EdgeSoftness;
        half _Intensity;
      CBUFFER_END

      Varyings Vert(Attributes input) {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = input.uv;
        return output;
      }

      half4 Frag(Varyings input) : SV_Target {
        half radialDistance = length(input.uv - 0.5h) * 2.0h;
        half innerEdge = saturate(1.0h - _EdgeSoftness);
        half coverage = 1.0h - smoothstep(innerEdge, 1.0h, radialDistance);
        half alpha = coverage * _Color.a;
        return half4(_Color.rgb * _Intensity, alpha);
      }
      ENDHLSL
    }
  }
}
