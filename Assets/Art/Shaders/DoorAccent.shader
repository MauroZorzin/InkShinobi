Shader "Hidden/InkShinobi/DoorAccentMask" {
  SubShader {
    Tags {
      "RenderPipeline" = "UniversalPipeline"
      "Queue" = "Geometry"
      "RenderType" = "Opaque"
    }

    Pass {
      Name "DoorAccentMask"
      Cull Off
      ZWrite Off
      ZTest LEqual
      ColorMask A

      Stencil {
        Ref 64
        ReadMask 64
        Comp Equal
        Pass Keep
      }

      HLSLPROGRAM
      #pragma vertex Vert
      #pragma fragment Frag
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

      struct Attributes {
        float4 positionOS : POSITION;
      };

      struct Varyings {
        float4 positionCS : SV_POSITION;
      };

      Varyings Vert(Attributes input) {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        return output;
      }

      half4 Frag(Varyings input) : SV_Target {
        return half4(0, 0, 0, 1);
      }
      ENDHLSL
    }
  }
}
