Shader "InkShinobi/PalaceWaterPuddle" {
  Properties {
    _DeepColor ("Deep Color", Color) = (0.015, 0.42, 0.72, 0.9)
    _ShallowColor ("Shallow Color", Color) = (0.16, 0.78, 0.95, 0.72)
    _RippleColor ("Ripple Color", Color) = (0.7, 0.95, 1, 0.7)
    _RippleSpeed ("Ripple Speed", Range(0, 3)) = 0.45
    _EdgeSoftness ("Edge Softness", Range(0.01, 0.4)) = 0.12
  }

  SubShader {
    Tags {
      "RenderPipeline" = "UniversalPipeline"
      "Queue" = "Transparent"
      "RenderType" = "Transparent"
    }

    Pass {
      Name "PalaceWaterPuddle"
      Tags { "LightMode" = "UniversalForward" }
      Blend SrcAlpha OneMinusSrcAlpha
      ZWrite Off
      ZTest LEqual
      Cull Off

      HLSLPROGRAM
      #pragma vertex Vert
      #pragma fragment Frag
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

      struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
      struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

      CBUFFER_START(UnityPerMaterial)
        half4 _DeepColor;
        half4 _ShallowColor;
        half4 _RippleColor;
        half _RippleSpeed;
        half _EdgeSoftness;
      CBUFFER_END

      Varyings Vert(Attributes input) {
        Varyings output;
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
        output.uv = input.uv;
        return output;
      }

      half4 Frag(Varyings input) : SV_Target {
        float2 centered = (input.uv - 0.5) * 2.0;
        half irregularity = 0.035h * sin(input.uv.x * 19.0h + input.uv.y * 11.0h)
                          + 0.025h * sin(input.uv.x * 37.0h - input.uv.y * 23.0h);
        half radius = length(centered);
        half edge = 1.0h - smoothstep(0.84h + irregularity, 0.84h + irregularity + _EdgeSoftness, radius);

        half time = _Time.y * _RippleSpeed;
        half rippleA = 0.5h + 0.5h * sin(radius * 28.0h - time * 4.0h);
        half rippleB = 0.5h + 0.5h * sin((input.uv.x - input.uv.y) * 31.0h + time * 2.7h);
        half ripples = pow(saturate(rippleA * rippleB), 5.0h) * edge;
        half depthBlend = saturate(radius * 0.9h);
        half4 water = lerp(_DeepColor, _ShallowColor, depthBlend);
        water.rgb = lerp(water.rgb, _RippleColor.rgb, ripples * _RippleColor.a);
        water.a *= edge;
        return water;
      }
      ENDHLSL
    }
  }
}
