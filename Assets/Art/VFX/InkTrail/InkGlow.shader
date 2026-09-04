Shader "Custom/InkGlow"
{
    Properties
    {
        _GlowColor ("Glow Color", Color) = (0.4, 0.85, 1, 1)
        _GlowIntensity ("Glow Intensity", Float) = 2.5
        _FadeRadius ("Player Fade Radius", Float) = 3
        _FadeSoftness ("Player Fade Softness", Float) = 2
        _TopFade ("Top Fade Amount", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            fixed4 _GlowColor;
            float _GlowIntensity;
            float _FadeRadius;
            float _FadeSoftness;
            float _TopFade;

            float4 _PlayerWorldPosition;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.worldPos = mul(unity_ObjectToWorld, IN.vertex).xyz;
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float distToPlayer = distance(IN.worldPos.xz, _PlayerWorldPosition.xz);
                float playerFade = smoothstep(_FadeRadius, _FadeRadius + max(_FadeSoftness, 0.001), distToPlayer);
                float verticalFade = 1.0 - saturate(IN.texcoord.y) * _TopFade;

                float intensity = _GlowIntensity * playerFade * verticalFade * IN.color.a * _GlowColor.a;
                return fixed4(_GlowColor.rgb * intensity, 0);
            }
            ENDCG
        }
    }
}
