Shader "Hidden/InkShinobi/DoorStencilMask" {
  SubShader {
    Tags {
      "RenderPipeline" = "UniversalPipeline"
      "Queue" = "Geometry-100"
      "RenderType" = "Opaque"
    }

    Pass {
      Name "DoorStencilMask"
      Cull Off
      ZWrite Off
      ZTest Always
      ColorMask 0

      Stencil {
        Ref 64
        ReadMask 64
        WriteMask 64
        Comp Always
        Pass Replace
      }

      HLSLPROGRAM
      #pragma target 2.0
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
        return 0;
      }
      ENDHLSL
    }
  }
}
