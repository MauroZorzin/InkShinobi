Shader "InkShinobi/WallSwitchMarker"
{
    Properties
    {
        _Color ("Ink Color", Color) = (0.02, 0.55, 0.08, 0.95)
        _EdgeSoftness ("Edge Softness", Range(0.005, 0.2)) = 0.045
        _MotionSpeed ("Ink Motion Speed", Range(0, 5)) = 1.2
        _Distortion ("Edge Distortion", Range(0, 0.35)) = 0.16
    }

    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            float _EdgeSoftness;
            float _MotionSpeed;
            float _Distortion;
            float _UnscaledTime;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv * 2.0 - 1.0;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float angle = atan2(input.uv.y, input.uv.x);
                float time = _UnscaledTime * _MotionSpeed;
                float irregularity = sin(angle * 5.0 + time) * 0.48
                                  + sin(angle * 8.0 - time * 0.73) * 0.31
                                  + sin(angle * 13.0 + time * 1.37) * 0.21;
                float boundary = 0.72 + irregularity * _Distortion;
                float radius = length(input.uv);
                float alpha = 1.0 - smoothstep(boundary - _EdgeSoftness, boundary + _EdgeSoftness, radius);

                // A faint moving interior wash keeps the marker feeling like wet ink without
                // changing its overall size or making the cursor harder to place precisely.
                float wash = 0.9 + sin(input.uv.x * 7.0 + input.uv.y * 5.0 + time * 0.8) * 0.1;
                return fixed4(_Color.rgb * wash, _Color.a * alpha);
            }
            ENDCG
        }
    }
}
