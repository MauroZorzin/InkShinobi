Shader "Custom/PathLine"
{
    // Renders a LineRenderer strip as a black-and-white "sfumato" path marker: solid black
    // along the centerline, fading smoothly out to pure white towards the edges. Intended for
    // LinePathVisualizer, so the player can actually see a walkable LinePath in Play mode/builds
    // (LinePath's own gizmos only ever show in the editor Scene view).
    Properties
    {
        _CoreColor ("Core Color (centerline)", Color) = (0, 0, 0, 1)
        _EdgeColor ("Edge Color (outer fade)", Color) = (1, 1, 1, 1)
        _CoreWidth ("Core Width (0-1 of half-width, solid before fading)", Range(0, 1)) = 0.35
        _Softness ("Fade Softness (0-1, how gradual the sfumato blend is)", Range(0.01, 1)) = 0.5
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

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
            };

            fixed4 _CoreColor;
            fixed4 _EdgeColor;
            float _CoreWidth;
            float _Softness;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // LineRenderer always maps V (texcoord.y) across the strip's width: 0 at one
                // edge, 0.5 on the centerline, 1 at the other edge — regardless of texture mode.
                float distFromCenter = abs(IN.texcoord.y - 0.5) * 2.0; // 0 = centerline, 1 = edge
                float fadeEnd = max(_CoreWidth + _Softness, _CoreWidth + 0.0001);
                float t = smoothstep(_CoreWidth, fadeEnd, distFromCenter);

                fixed4 col = lerp(_CoreColor, _EdgeColor, t);
                col *= IN.color;
                return col;
            }
            ENDCG
        }
    }
}
