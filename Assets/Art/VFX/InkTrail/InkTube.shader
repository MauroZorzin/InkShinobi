Shader "Custom/InkTube"
{
    // Renders LinePathTubeVisualizer's round 3D tube as a single-color, thin, continuously
    // slithering line — unlike InkTrail (which projects onto the terrain below), this floats
    // exactly where the traceable LinePath is, moving like a snake rather than sitting static on it.
    //
    // The tube's circular cross-section is baked once on the CPU using a twist-free parallel
    // transport frame (see LinePathTubeVisualizer.ComputeParallelTransportFrames); that SAME
    // ring-shared (normal, binormal) pair — the plane perpendicular to travel direction — is also
    // carried per vertex via the NORMAL and TANGENT channels (both repurposed; the tube is unlit, so
    // neither is needed for shading). The vertex shader below rigidly translates the whole ring
    // along those exact two axes by two traveling sine waves (different frequency/speed/phase) — so
    // the tube's entire body continuously curves through 3D space in every direction as it slithers,
    // correctly from any viewing angle since none of this depends on the camera, and smoothly since
    // it rides the same twist-free frame the tube's own shape uses (no re-derived axes to jump
    // unpredictably at path corners). A world-space "ink
    // front" (_ProgressDistance, pushed every frame by LinePathTubeVisualizer from
    // LineFollowController.GetDistanceAlongLine()) fades the line in as the player advances,
    // communicating path completion without needing a second color.
    Properties
    {
        _Color ("Ink Color", Color) = (0.35, 0.85, 1, 1)

        [Header(Serpentine Motion)]
        _WaveFrequency1 ("Primary Wave Frequency (cycles per world unit)", Float) = 1.5
        _WaveAmplitude1 ("Primary Wave Amplitude (world units)", Float) = 0.15
        _WaveSpeed1 ("Primary Wave Speed", Float) = 1.5

        _WaveFrequency2 ("Secondary Wave Frequency (cycles per world unit)", Float) = 0.6
        _WaveAmplitude2 ("Secondary Wave Amplitude (world units)", Float) = 0.08
        _WaveSpeed2 ("Secondary Wave Speed", Float) = 0.9

        _WaveFrequency3 ("Tertiary Wave Frequency (cycles per world unit)", Float) = 3
        _WaveAmplitude3 ("Tertiary Wave Amplitude (world units)", Float) = 0.04
        _WaveSpeed3 ("Tertiary Wave Speed", Float) = 2.2

        [Header(Completion Front)]
        _ProgressDistance ("Completed Distance (world units along strand)", Float) = 0
        _ProgressSoftness ("Completion Edge Softness (world units)", Float) = 0.6
        _MinAlpha ("Alpha Before The Front Reaches It (0-1)", Range(0, 1)) = 0.25

        [Header(Player Anchor)]
        _PlayerAnchorWorldPos ("Player Anchor World Position", Vector) = (0, 0, 0, 0)
        _PlayerPathPointWorldPos ("Path's Own Point At Player's Distance", Vector) = (0, 0, 0, 0)
        _PlayerDistance ("Player's Distance Along Strand (world units)", Float) = -1000000
        _PlayerPullRadius ("Player Pull Falloff Radius (world units)", Float) = 1.2
        _PlayerPullStrength ("Player Pull Strength (0-1)", Range(0, 1)) = 1

        _AlphaMultiplier ("Overall Alpha Multiplier", Range(0, 1)) = 1
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
                float3 normal : NORMAL; // repurposed: ring's parallel-transport normal, world-space
                float4 tangent : TANGENT; // repurposed: ring's parallel-transport binormal (xyz), world-space
                float2 texcoord : TEXCOORD0; // x = world-space distance along strand, y = fraction around circumference
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _Color;

            float _WaveFrequency1;
            float _WaveAmplitude1;
            float _WaveSpeed1;
            float _WaveFrequency2;
            float _WaveAmplitude2;
            float _WaveSpeed2;
            float _WaveFrequency3;
            float _WaveAmplitude3;
            float _WaveSpeed3;

            float _ProgressDistance;
            float _ProgressSoftness;
            float _MinAlpha;

            float4 _PlayerAnchorWorldPos;
            float4 _PlayerPathPointWorldPos;
            float _PlayerDistance;
            float _PlayerPullRadius;
            float _PlayerPullStrength;

            float _AlphaMultiplier;

            v2f vert(appdata_t IN)
            {
                v2f OUT;

                // The two wave axes are exactly the ring's own twist-free parallel-transport frame,
                // precomputed on the CPU and carried in via NORMAL/TANGENT — see the file header.
                float3 n = normalize(IN.normal);
                float3 b = normalize(IN.tangent.xyz);

                // Three traveling sine waves (phase scrolls with _Time.y) rigidly translate the
                // whole ring along the plane perpendicular to travel direction — wave1 and wave3
                // share axis n (a second harmonic layered on the primary curve, for finer detail or
                // a less perfectly periodic motion), wave2 is on the other axis b. Together this is
                // the "moves like a snake curving in every direction" motion, in true 3D rather than
                // a single flat ripple.
                float dist = IN.texcoord.x;
                float wave1 = sin(dist * _WaveFrequency1 - _Time.y * _WaveSpeed1) * _WaveAmplitude1;
                float wave2 = sin(dist * _WaveFrequency2 - _Time.y * _WaveSpeed2 + 1.7) * _WaveAmplitude2;
                float wave3 = sin(dist * _WaveFrequency3 - _Time.y * _WaveSpeed3 + 4.1) * _WaveAmplitude3;

                // Pull toward the player: 1 exactly at the ring matching the player's current
                // distance along the strand, fading to 0 over _PlayerPullRadius. The wave is faded
                // out by the same amount right where the pull takes over, so the tube passes exactly
                // through the player's anchor position instead of still wiggling around it — like a
                // rope threaded through them, relaxing back into its normal slither as you move away.
                float distFromPlayer = abs(dist - _PlayerDistance);
                float pull = (1.0 - smoothstep(0.0, max(_PlayerPullRadius, 0.001), distFromPlayer)) * _PlayerPullStrength;

                float3 serpentine = (n * (wave1 + wave3) + b * wave2) * (1.0 - pull);
                float3 pullOffset = (_PlayerAnchorWorldPos.xyz - _PlayerPathPointWorldPos.xyz) * pull;

                float3 worldPos = IN.vertex.xyz + serpentine + pullOffset;

                OUT.vertex = UnityObjectToClipPos(float4(worldPos, 1.0));
                OUT.texcoord = IN.texcoord;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // texcoord.x carries cumulative WORLD-SPACE distance along the strand, same units
                // as _ProgressDistance, so this needs no normalization. The tube's silhouette is
                // real geometry now, so no procedural edge/width falloff is needed here.
                float dist = IN.texcoord.x;
                float completed = 1.0 - smoothstep(_ProgressDistance - _ProgressSoftness, _ProgressDistance + _ProgressSoftness, dist);
                float progressAlpha = lerp(_MinAlpha, 1.0, completed);

                fixed4 col = _Color;
                col.a *= progressAlpha * _AlphaMultiplier;
                return col;
            }
            ENDCG
        }
    }
}
