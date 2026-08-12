Shader "Hidden/RoystanOutline"
{
    // Port of Roystan's outline shader (https://roystan.net/articles/outline-shader/) to Unity 6
    // URP — depth-only variant. The original (and our earlier port) also sampled a dedicated
    // view-space normals texture (ViewSpaceNormals.shader / ViewSpaceNormalsTexturePass) to run a
    // second Roberts-cross edge test and to correct the depth threshold on grazing-angle surfaces.
    // That pass has been removed entirely — this shader now reads ONLY the camera depth texture,
    // so it costs one fewer full-scene redraw and no longer needs per-object normals at all. The
    // trade-off: curved-surface shading breaks (normal-only edges) no longer outline, and there's
    // no more grazing-angle compensation — instead, the depth threshold is scaled by a
    // camera-distance function (see "Depth Threshold" below) so false edges from oblique surfaces
    // can be tuned back down per-scene without normals.
    //
    // Limitation carried over on purpose: outline width comes from a SINGLE Roberts-cross tap
    // offset by _Scale texels, not a separate dilation pass — so large _Scale values will show the
    // same stair-stepping/blockiness the original article has. That's the trade-off for staying
    // faithful to this reference instead of our earlier, more complex two-pass mask+dilate version.
    Properties
    {
        _Color ("Outline Color (Near)", Color) = (0, 0, 0, 1)

        [Header(Distance Fade)]
        _FarColor ("Outline Color (Far)", Color) = (0, 0, 0, 0)
        _FadeStartDistance ("Fade Start Distance (world units)", Float) = 15
        _FadeEndDistance ("Fade End Distance (world units)", Float) = 30

        [Header(Perspective Scale)]
        _Scale ("Scale (texels @ Reference Distance)", Float) = 1
        _ThicknessReferenceDistance ("Reference Distance (world units)", Float) = 10
        _MinScale ("Min Scale (texels)", Float) = 0.5
        _MaxScale ("Max Scale (texels)", Float) = 10

        [Header(Depth Threshold)]
        _DepthThreshold ("Depth Threshold (Base)", Float) = 1.5
        // Threshold actually used at a pixel is _DepthThreshold * a distance-based multiplier
        // (see DistanceThresholdMultiplier below), so the effective sensitivity can be tuned
        // separately for close-up and far-away geometry instead of one fixed value for the whole
        // scene.
        [KeywordEnum(Linear, Exponential)] _ThresholdFunction ("Distance Function", Float) = 0
        _ThresholdNearDistance ("Near Distance (world units)", Float) = 5
        _ThresholdFarDistance ("Far Distance (world units)", Float) = 40
        _ThresholdMultiplierNear ("Multiplier @ Near Distance", Float) = 1
        _ThresholdMultiplierFar ("Multiplier @ Far Distance", Float) = 4
        // Only used when _ThresholdFunction is Exponential: > 1 keeps the multiplier near
        // _ThresholdMultiplierNear for most of the range then rises sharply close to
        // _ThresholdFarDistance (ease-in); < 1 rises sharply right away then flattens out
        // (ease-out). 1 is identical to Linear.
        _ThresholdExponent ("Exponential: Curve Exponent", Float) = 2.5

        [Header(Debug)]
        [KeywordEnum(Off, DepthEdge)] _DebugView ("Debug View", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "RoystanOutline"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // DeclareDepthTexture.hlsl (which pulls in URP's Core.hlsl) must come before Blit.hlsl —
            // both define macros around SAMPLE_TEXTURE2D_X and friends, and Blit.hlsl expects
            // Core.hlsl's versions to already be in scope.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _FarColor;
                float _FadeStartDistance;
                float _FadeEndDistance;
                float _Scale;
                float _ThicknessReferenceDistance;
                float _MinScale;
                float _MaxScale;
                float _DepthThreshold;
                float _ThresholdFunction;
                float _ThresholdNearDistance;
                float _ThresholdFarDistance;
                float _ThresholdMultiplierNear;
                float _ThresholdMultiplierFar;
                float _ThresholdExponent;
                float _DebugView;
            CBUFFER_END

            // How much _DepthThreshold is scaled by at a given camera distance. Replaces the old
            // grazing-angle (NdotV) correction that used to require the normals texture: instead of
            // reacting to surface angle, the threshold is widened/narrowed purely as a function of
            // how far the pixel is from the camera, which is enough to tune out false edges on
            // distant geometry (where a fixed-size depth gap covers far fewer raw-depth units)
            // without sampling normals at all.
            float DistanceThresholdMultiplier(float distance) {
                float span = max(_ThresholdFarDistance - _ThresholdNearDistance, 0.0001);
                float t = saturate((distance - _ThresholdNearDistance) / span);
                // Exponential: t raised to _ThresholdExponent reshapes the ramp into an ease-in
                // (exponent > 1) or ease-out (exponent < 1) curve; Linear leaves t untouched.
                if (_ThresholdFunction > 0.5) t = pow(t, max(_ThresholdExponent, 0.0001));
                return lerp(_ThresholdMultiplierNear, _ThresholdMultiplierFar, t);
            }

            half4 Frag(Varyings input) : SV_Target {
                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
                float2 texelSize = _CameraDepthTexture_TexelSize.xy;

                // Perspective-scaled outline width: _Scale is the texel offset AT
                // _ThicknessReferenceDistance: closer than that it grows (up to _MaxScale), farther
                // it shrinks (down to _MinScale) — a physically-sized line should look thinner far
                // away, not the same width regardless of distance. Sampled at the centre pixel,
                // before the diagonal offsets below (which depend on this), to avoid a circular
                // dependency on depths that haven't been sampled yet.
                float centerDepthLinear = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float minScale = min(_MinScale, _MaxScale);
                float maxScale = max(_MinScale, _MaxScale);
                float scale = clamp(_Scale * (_ThicknessReferenceDistance / max(centerDepthLinear, 0.01)), minScale, maxScale);

                // Four diagonal sample points around this pixel, offset by the scale above. The
                // floor/ceil split (rather than just scale * 0.5 both ways) keeps it landing on
                // whole-pixel offsets instead of splitting a texel down the middle.
                float halfScaleFloor = floor(scale * 0.5);
                float halfScaleCeil = ceil(scale * 0.5);
                float2 uvBottomLeft = uv - texelSize * halfScaleFloor;
                float2 uvTopRight = uv + texelSize * halfScaleCeil;
                float2 uvBottomRight = uv + texelSize * float2(halfScaleCeil, -halfScaleFloor);
                float2 uvTopLeft = uv + texelSize * float2(-halfScaleFloor, halfScaleCeil);

                // Depth edge: Roberts cross on raw device depth between the diagonal pairs.
                float depth0 = SampleSceneDepth(uvBottomLeft);
                float depth1 = SampleSceneDepth(uvTopRight);
                float depth2 = SampleSceneDepth(uvBottomRight);
                float depth3 = SampleSceneDepth(uvTopLeft);

                float depthFiniteDifference0 = depth1 - depth0;
                float depthFiniteDifference1 = depth3 - depth2;
                float edgeDepth = sqrt(pow(depthFiniteDifference0, 2) + pow(depthFiniteDifference1, 2)) * 100;

                // depth0 compensates for raw depth's non-linear precision falloff with distance (a
                // fixed world-space gap produces a much smaller raw-depth delta far from the camera
                // than close to it) — that part of the original algorithm is unchanged. On top of
                // that, DistanceThresholdMultiplier lets the base threshold itself be widened or
                // narrowed by actual camera distance (via the artist-chosen Linear/Exponential
                // curve above), which is what used to be done per-pixel from the surface's grazing
                // angle using normals.
                float depthThreshold = _DepthThreshold * depth0 * DistanceThresholdMultiplier(centerDepthLinear);
                edgeDepth = edgeDepth > depthThreshold ? 1 : 0;

                // Debug: the raw depth edge term before the distance fade below, to tell whether a
                // missing/wrong outline comes from the edge test itself or from the fade/blend after.
                if (_DebugView > 0.5) return half4(edgeDepth.xxx, 1);

                // Distance fade: blend from _Color (near) to _FarColor (far, defaults to fully
                // transparent) between _FadeStartDistance and _FadeEndDistance, using the same
                // linear depth already sampled for the perspective scale above.
                float fadeT = saturate((centerDepthLinear - _FadeStartDistance) / max(_FadeEndDistance - _FadeStartDistance, 0.01));
                half4 fadedColor = lerp(_Color, _FarColor, fadeT);

                half4 outlineColor = half4(fadedColor.rgb, fadedColor.a * edgeDepth);
                return half4(lerp(sceneColor.rgb, outlineColor.rgb, outlineColor.a), sceneColor.a);
            }
            ENDHLSL
        }
    }
}
