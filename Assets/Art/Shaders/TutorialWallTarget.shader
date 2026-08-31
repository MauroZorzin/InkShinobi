Shader "InkShinobi/TutorialWallTarget" {
  Properties {
    _Color ("Color", Color) = (0.12, 1, 0.2, 1)
    _PulseSpeed ("Pulse Speed", Float) = 2
  }
  SubShader {
    Tags { "Queue"="Overlay" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
    Pass {
      Name "TutorialWallTarget"
      Blend SrcAlpha OneMinusSrcAlpha
      Cull Off
      ZWrite Off
      ZTest Always

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
        float _PulseSpeed;
      CBUFFER_END

      Varyings Vert(Attributes input) {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = input.uv;
        return output;
      }

      half4 Frag(Varyings input) : SV_Target {
        float2 centeredUv = input.uv - 0.5;
        float distanceFromCenter = length(centeredUv);
        float pulse = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed * 6.2831853);
        float radius = lerp(0.265, 0.305, pulse);
        float ring = 1.0 - smoothstep(0.028, 0.065, abs(distanceFromCenter - radius));
        float centerDot = 1.0 - smoothstep(0.035, 0.075, distanceFromCenter);
        float cardinal = max(
          (1.0 - smoothstep(0.018, 0.042, abs(centeredUv.x))) * step(abs(centeredUv.y), 0.19),
          (1.0 - smoothstep(0.018, 0.042, abs(centeredUv.y))) * step(abs(centeredUv.x), 0.19));
        float alpha = saturate(ring + centerDot + cardinal * 0.65) * _Color.a;
        half brightness = (half)lerp(0.72, 1.0, pulse);
        return half4(_Color.rgb * brightness, alpha);
      }
      ENDHLSL
    }
  }
}
