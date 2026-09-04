Shader "Custom/InkTrail"
{
    // Renders LinePathVisualizer's procedural ground-conforming ribbon mesh as a broken, blotchy
    // ink stroke instead of a clean continuous line — replaces PathLine.shader (which only ever
    // drew a smooth solid-to-faded strip). Everything here is procedural noise, no textures, so
    // it needs no authored art to look hand-inked:
    //   - "breakup": low-frequency 2D value noise thresholded into gaps/blots along the stroke,
    //     optionally drifting over time (_FlowSpeed) so still puddles feel faintly alive.
    //   - "edge roughness": the width-based core/edge falloff is perturbed by a second, coarser
    //     noise so the stroke's silhouette is uneven instead of a razor-straight ribbon border.
    // The mesh's vertex-alpha carries LinePathVisualizer's "is this point actually resting on
    // geometry" flag (raycast hit vs fallback) — multiplied in here so the ink fades out smoothly
    // over any stretch of the path that isn't currently sitting on a surface, rather than showing
    // a straight airborne segment.
    Properties
    {
        _CoreColor ("Core Color (centerline, wet ink)", Color) = (0.05, 0.05, 0.08, 1)
        _EdgeColor ("Edge Color (outer, dry ink)", Color) = (0.05, 0.05, 0.08, 0)
        _CoreWidth ("Core Width (0-1 of half-width, solid before fading)", Range(0, 1)) = 0.3
        _Softness ("Fade Softness (0-1, how gradual the core->edge blend is)", Range(0.01, 1)) = 0.6

        [Header(Ink Breakup)]
        _BreakupScale ("Breakup Noise Scale (cycles per world unit)", Float) = 0.6
        _BreakupThreshold ("Breakup Threshold (0 = fully solid, 1 = fully gone)", Range(0, 1)) = 0.45
        _BreakupSoftness ("Breakup Edge Softness", Range(0.001, 0.5)) = 0.12
        _FlowSpeed ("Breakup Drift Speed (0 = static ink)", Float) = 0

        [Header(Rough Edge)]
        _EdgeNoiseScale ("Edge Noise Scale (cycles per world unit)", Float) = 1.5
        _EdgeRoughness ("Edge Roughness Amount", Range(0, 0.5)) = 0.18

        _AlphaMultiplier ("Overall Alpha Multiplier", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test", Float) = 4
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [_ZTest]
        Offset -1, -1
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

            float _BreakupScale;
            float _BreakupThreshold;
            float _BreakupSoftness;
            float _FlowSpeed;

            float _EdgeNoiseScale;
            float _EdgeRoughness;

            float _AlphaMultiplier;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                // texcoord.x carries cumulative WORLD-SPACE distance along the strand (set by
                // LinePathVisualizer, not a 0..1 normalized length) so noise frequency below reads
                // consistently regardless of how long an individual strand is.
                OUT.texcoord = IN.texcoord;
                OUT.color = IN.color;
                return OUT;
            }

            // Cheap hash-based value noise — no texture lookups, so the whole effect needs zero
            // authored art. Good enough at the low frequencies this shader actually uses.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Two octaves is plenty at these frequencies and keeps this cheap for a full-screen
            // amount of thin strip geometry — a single octave reads too uniform/grid-like, three+
            // adds cost with no visible gain at the low _BreakupScale/_EdgeNoiseScale defaults.
            float fbm(float2 p)
            {
                float v = valueNoise(p) * 0.6;
                v += valueNoise(p * 2.13) * 0.4;
                return v;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // LinePathVisualizer maps texcoord.y across the ribbon's width: 0 at one edge, 0.5
                // on the centerline, 1 at the other edge.
                float distFromCenter = abs(IN.texcoord.y - 0.5) * 2.0; // 0 = centerline, 1 = edge

                // Rough edge: perturb the width-based distance with coarse noise sampled along the
                // stroke's length so the silhouette wobbles instead of running dead straight.
                float edgeN = fbm(float2(IN.texcoord.x * _EdgeNoiseScale, 0.0)) - 0.5;
                float distPerturbed = saturate(distFromCenter + edgeN * _EdgeRoughness);

                float fadeEnd = max(_CoreWidth + _Softness, _CoreWidth + 0.0001);
                float t = smoothstep(_CoreWidth, fadeEnd, distPerturbed);
                fixed4 col = lerp(_CoreColor, _EdgeColor, t);

                // Breakup: 2D noise (varies across width too, not just length) thresholded into
                // gaps — this is what turns a continuous strip into a broken/blotchy ink stroke.
                float flow = _FlowSpeed * _Time.y;
                float breakupN = fbm(float2(IN.texcoord.x * _BreakupScale + flow, IN.texcoord.y * _BreakupScale * 0.5));
                float breakupAlpha = smoothstep(_BreakupThreshold - _BreakupSoftness, _BreakupThreshold + _BreakupSoftness, breakupN);

                // Fade fully to transparent right at the ribbon's physical edge so the mesh's own
                // rectangular boundary never reads as a hard line once breakup/roughness are low.
                float edgeFade = 1.0 - smoothstep(1.0 - max(_Softness * 0.5, 0.05), 1.0, distPerturbed);

                col.a *= breakupAlpha * edgeFade * _AlphaMultiplier;
                // Vertex alpha carries the "resting on geometry" flag from LinePathVisualizer (1 =
                // grounded, faded towards 0 = projection found no surface) — GPU-interpolated, so
                // grounded/ungrounded stretches blend smoothly instead of popping.
                col *= IN.color;

                return col;
            }
            ENDCG
        }
    }
}
