Shader "Hidden/PopupBlur" {
  Properties {
    _MainTex ("Texture", 2D) = "white" {}
    _BlurRadius ("Blur Radius", Float) = 2
  }

  SubShader {
    Tags { "RenderType" = "Opaque" }
    ZWrite Off
    ZTest Always
    Cull Off

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    float _BlurRadius;

    struct Attributes {
      float4 vertex : POSITION;
      float2 uv : TEXCOORD0;
    };

    struct Varyings {
      float4 position : SV_POSITION;
      float2 uv : TEXCOORD0;
    };

    Varyings Vert(Attributes input) {
      Varyings output;
      output.position = UnityObjectToClipPos(input.vertex);
      output.uv = input.uv;
      return output;
    }

    half4 SampleBlur(float2 uv, float2 direction) {
      float2 offset = _MainTex_TexelSize.xy * direction * _BlurRadius;
      half4 color = tex2D(_MainTex, uv) * 0.227027;
      color += tex2D(_MainTex, uv + offset * 1.384615) * 0.316216;
      color += tex2D(_MainTex, uv - offset * 1.384615) * 0.316216;
      color += tex2D(_MainTex, uv + offset * 3.230769) * 0.070270;
      color += tex2D(_MainTex, uv - offset * 3.230769) * 0.070270;
      return color;
    }
    ENDCG

    Pass {
      Name "Horizontal"
      CGPROGRAM
      #pragma vertex Vert
      #pragma fragment FragHorizontal

      half4 FragHorizontal(Varyings input) : SV_Target {
        return SampleBlur(input.uv, float2(1, 0));
      }
      ENDCG
    }

    Pass {
      Name "Vertical"
      CGPROGRAM
      #pragma vertex Vert
      #pragma fragment FragVertical

      half4 FragVertical(Varyings input) : SV_Target {
        return SampleBlur(input.uv, float2(0, 1));
      }
      ENDCG
    }
  }
}
