Shader "InkShinobi/Door Handle Cue" {
  Properties {
    [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
    [HideInInspector] _Color("Legacy Color", Color) = (1, 1, 1, 1)
  }

  SubShader {
    Tags {
      "RenderPipeline" = "UniversalPipeline"
      "Queue" = "Geometry"
      "RenderType" = "Opaque"
    }

    Pass {
      Name "DoorHandleCue"
      Tags { "LightMode" = "UniversalForward" }
      Cull Off
      ZWrite On
      ZTest LEqual

      Stencil {
        Ref 64
        ReadMask 64
        Comp Equal
        Pass Keep
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

      CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half4 _Color;
      CBUFFER_END

      Varyings Vert(Attributes input) {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        return output;
      }

      half4 Frag(Varyings input) : SV_Target {
        return _BaseColor;
      }
      ENDHLSL
    }
  }
}
