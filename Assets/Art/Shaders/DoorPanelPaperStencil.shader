Shader "InkShinobi/Door Panel Paper Stencil" {
  Properties {
    [MainTexture] _BaseMap("Paper Texture", 2D) = "white" {}
    [MainColor] _BaseColor("Paper Color", Color) = (0.8, 0.8, 0.8, 1)
    [HDR] _EmissionColor("Emission Color", Color) = (0.1436, 0.1436, 0.1436, 1)
    _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
    [HideInInspector] _MainTex("Legacy Paper Texture", 2D) = "white" {}
    [HideInInspector] _Color("Legacy Paper Color", Color) = (0.8, 0.8, 0.8, 1)
  }

  SubShader {
    Tags {
      "RenderPipeline" = "UniversalPipeline"
      "Queue" = "AlphaTest"
      "RenderType" = "TransparentCutout"
    }

    Pass {
      Name "DoorPanelPaperStencil"
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

      CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half4 _EmissionColor;
        half4 _Color;
        half _Cutoff;
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
        half4 paper = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
        half alpha = paper.a * _BaseColor.a;
        clip(alpha - _Cutoff);
        return half4(paper.rgb * (_BaseColor.rgb + _EmissionColor.rgb), alpha);
      }
      ENDHLSL
    }
  }
}
