Shader "InkShinobi/Finale Blood Gush" {
  Properties {
    _BaseMap ("Vefects Blood Mask", 2D) = "white" {}
    _BaseColor ("Blood Color", Color) = (0.72, 0.01, 0.015, 0.98)
  }

  SubShader {
    Tags {
      "RenderPipeline" = "UniversalPipeline"
      "Queue" = "Transparent+100"
      "RenderType" = "Transparent"
    }

    Pass {
      Name "BloodGushUnlit"
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
        float4 color : COLOR;
        float2 uv : TEXCOORD0;
      };

      struct Varyings {
        float4 positionCS : SV_POSITION;
        float4 color : COLOR;
        float2 uv : TEXCOORD0;
      };

      TEXTURE2D(_BaseMap);
      SAMPLER(sampler_BaseMap);

      CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseColor;
      CBUFFER_END

      Varyings Vert(Attributes input) {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
        output.color = input.color;
        return output;
      }

      half4 Frag(Varyings input) : SV_Target {
        // Give the dense impact head extra volume without globally widening the loose spray.
        // The head occupies the right-hand end after the directional mask is rotated below.
        float originWeight = smoothstep(0.62, 0.86, input.uv.x);
        float originHeight = lerp(1.0, 1.35, originWeight);
        float2 shapedUv = input.uv;
        shapedUv.y = 0.47 + ((shapedUv.y - 0.47) / originHeight);

        // Ease into the dense head more gradually. Sampling farther into the source head across
        // this middle-right band lengthens the solid body, while UVs in the loose tail stay put.
        float headReach = smoothstep(0.46, 0.82, input.uv.x);
        shapedUv.x += 0.26 * headReach * (1.0 - input.uv.x);

        // The Vefects source travels diagonally from upper-left to lower-right. Rotate that mask
        // into a fitted horizontal sweep in UV space so its motion is deterministically
        // left-to-right, independent of ParticleSystem billboard/floor rotation conventions.
        float2 centredUv = shapedUv - 0.5;
        float2 bloodUv = float2(
          centredUv.x + centredUv.y,
          -centredUv.x + centredUv.y
        ) + 0.5;
        half mask = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, bloodUv).r;
        // Preserve the broad grey shoulders of the Vefects mask. A high upper threshold leaves
        // only its bright centre visible and makes the gush read as a narrow streak.
        half alpha = smoothstep(0.01h, 0.18h, mask) * input.color.a * _BaseColor.a;
        half highlight = lerp(0.55h, 1.08h, saturate(mask));
        return half4(_BaseColor.rgb * input.color.rgb * highlight, alpha);
      }
      ENDHLSL
    }
  }
}
