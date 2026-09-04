Shader "InkShinobi/Door Panel Wood Stencil" {
  Properties {
    [MainTexture] _BaseMap("Wood Texture", 2D) = "white" {}
    [MainColor] _BaseColor("Wood Color", Color) = (1, 1, 1, 1)
    _EmissionMap("Wood Emission", 2D) = "black" {}
    [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
    [HideInInspector] _MainTex("Legacy Wood Texture", 2D) = "white" {}
    [HideInInspector] _Color("Legacy Wood Color", Color) = (1, 1, 1, 1)
  }

  SubShader {
    Tags {
      "RenderPipeline" = "UniversalPipeline"
      "Queue" = "Geometry"
      "RenderType" = "Opaque"
    }

    Pass {
      Name "DoorPanelWoodStencil"
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
      #pragma multi_compile_instancing
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

      struct Attributes {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
      };

      struct Varyings {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
      };

      TEXTURE2D(_BaseMap);
      SAMPLER(sampler_BaseMap);
      TEXTURE2D(_EmissionMap);
      SAMPLER(sampler_EmissionMap);

      CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half4 _EmissionColor;
        half4 _Color;
      CBUFFER_END

      Varyings Vert(Attributes input) {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
        return output;
      }

      half4 Frag(Varyings input) : SV_Target {
        UNITY_SETUP_INSTANCE_ID(input);
        half4 wood = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
        half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb
          * _EmissionColor.rgb;
        return half4(wood.rgb + emission, wood.a);
      }
      ENDHLSL
    }
  }
}
