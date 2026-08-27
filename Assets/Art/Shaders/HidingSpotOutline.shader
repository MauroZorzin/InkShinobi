Shader "InkShinobi/HidingSpotOutline" {
  Properties {
    _OutlineColor ("Outline Color", Color) = (0.82, 0.84, 0.9, 0.85)
    _Thickness ("Rim Width", Range(0, 0.1)) = 0.025
  }

  SubShader {
    Tags {
      "RenderPipeline" = "UniversalPipeline"
      "Queue" = "Geometry+10"
      "RenderType" = "Transparent"
    }

    Pass {
      Name "Hiding Spot Outline"
      Tags { "LightMode" = "UniversalForward" }
      Cull Back
      ZWrite Off
      ZTest LEqual
      Offset -1, -1
      Blend SrcAlpha OneMinusSrcAlpha

      HLSLPROGRAM
      #pragma vertex Vert
      #pragma fragment Frag
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

      CBUFFER_START(UnityPerMaterial)
        half4 _OutlineColor;
        float _Thickness;
      CBUFFER_END

      struct Attributes {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
      };

      struct Varyings {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        half3 normalWS : TEXCOORD1;
      };

      Varyings Vert(Attributes input) {
        Varyings output;
        float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
        output.positionCS = TransformWorldToHClip(positionWS);
        output.positionWS = positionWS;
        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
        return output;
      }

      half4 Frag(Varyings input) : SV_Target {
        half3 normalWS = normalize(input.normalWS);
        half3 viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
        half grazing = 1.0h - saturate(abs(dot(normalWS, viewDirectionWS)));
        // Keep the existing compact 0..0.1 control while turning it into a view-dependent rim.
        // At the authored default (0.025), only the outer/grazing surfaces are highlighted.
        half rimStart = saturate(1.0h - _Thickness * 12.0h);
        half rim = smoothstep(rimStart, 1.0h, grazing);
        return half4(_OutlineColor.rgb, _OutlineColor.a * rim);
      }
      ENDHLSL
    }
  }
}
