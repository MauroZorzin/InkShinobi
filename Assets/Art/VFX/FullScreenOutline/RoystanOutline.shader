Shader "Hidden/RoystanOutline"
{
    // Port of Roystan's outline shader (https://roystan.net/articles/outline-shader/) to Unity 6
    // URP — same property names, same algorithm, same limitations as the original. The original
    // was a Post Processing Stack v2 image effect; here it's a plain full-screen HLSLPROGRAM pass
    // meant to be driven by a ScriptableRendererFeature (RecordRenderGraph), reading the camera's
    // depth texture plus a dedicated view-space normals texture (see ViewSpaceNormals.shader /
    // ViewSpaceNormalsTexturePass) instead of Post Processing's camera.depthTextureMode.
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

        _DepthThreshold ("Depth Threshold", Float) = 1.5
        _NormalThreshold ("Normal Threshold", Range(0, 1)) = 0.4
        _DepthNormalThreshold ("Depth Normal Threshold", Range(0, 1)) = 0.5
        _DepthNormalThresholdScale ("Depth Normal Threshold Scale", Float) = 7

        [Header(Edge Contribution)]
        _NormalContribution ("Normal Edge Contribution", Range(0, 1)) = 1
        _DepthContribution ("Depth Edge Contribution", Range(0, 1)) = 1

        [Header(Debug)]
        [KeywordEnum(Off, Normals, DepthEdge, NormalEdge)] _DebugView ("Debug View", Float) = 0
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
            // see the note in OutlineEdgeMask.shader for why the include order matters.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Written by ViewSpaceNormalsTexturePass; same name as the RTHandle allocated there.
            TEXTURE2D(_ScreenViewSpaceNormals);
            SAMPLER(sampler_ScreenViewSpaceNormals);

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
                float _NormalThreshold;
                float _DepthNormalThreshold;
                float _DepthNormalThresholdScale;
                float _NormalContribution;
                float _DepthContribution;
                float _DebugView;
            CBUFFER_END

            // Set every frame from C# as camera.projectionMatrix.inverse — lets the fragment
            // reconstruct a view-space ray through the current pixel without a full world-position
            // reconstruction, just to get the direction back to the camera for the angle test below.
            float4x4 _ClipToView;

            // mask (alpha) is 1 where ViewSpaceNormalsTexturePass actually drew an outlined-layer
            // object, 0 where the texture was left at its cleared value (background, or an object
            // excluded by layerMask) — see the comment on desc.clearColor in ScreenSpaceOutline.cs.
            float3 SampleNormal(float2 uv, out float mask) {
                half4 raw = SAMPLE_TEXTURE2D_X_LOD(_ScreenViewSpaceNormals, sampler_ScreenViewSpaceNormals, uv, 0);
                mask = raw.a;
                // Undo the 0..1 remap ViewSpaceNormals.shader applied before writing (the render
                // target is an unsigned color format, normals are -1..1).
                return raw.rgb * 2 - 1;
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

                // Normal edge: Roberts cross via self-dot (== squared length) on view-space normals.
                float mask0, mask1, mask2, mask3;
                float3 normal0 = SampleNormal(uvBottomLeft, mask0);
                float3 normal1 = SampleNormal(uvTopRight, mask1);
                float3 normal2 = SampleNormal(uvBottomRight, mask2);
                float3 normal3 = SampleNormal(uvTopLeft, mask3);

                float3 normalFiniteDifference0 = normal1 - normal0;
                float3 normalFiniteDifference1 = normal3 - normal2;
                float edgeNormal = sqrt(dot(normalFiniteDifference0, normalFiniteDifference0) + dot(normalFiniteDifference1, normalFiniteDifference1));
                edgeNormal = edgeNormal > _NormalThreshold ? 1 : 0;

                // True as long as ONE side of the sampled quad belongs to an outlined-layer object —
                // that's enough for its silhouette against anything else (background, or a
                // non-outlined object) to count as an edge. Two non-outlined objects both read mask
                // 0 here, so their shared boundary is suppressed even though it's still a real depth
                // discontinuity in _CameraDepthTexture (that texture is populated by every opaque
                // object, not just the outlined layers).
                float outlineMask = max(max(mask0, mask1), max(mask2, mask3)) > 0.5 ? 1 : 0;

                // View-angle modulation: reconstruct the view-space ray through this pixel (far
                // plane, w=1, so we only need its direction, not a depth-accurate position) to get
                // NdotV, then use it to scale up the depth threshold on grazing-angle surfaces —
                // those naturally show a big depth delta between neighbouring pixels from viewing
                // angle alone, which would otherwise false-positive as an edge.
                // _ProjectionParams.x flips to -1 on platforms whose projection is Y-flipped
                // relative to OpenGL convention — without it the reconstructed ray's Y component
                // (and therefore NdotV) comes out upside-down, which shows up specifically as wrong
                // grazing-angle suppression on sloped/near-horizontal surfaces like floors.
                float4 clipPos = float4(uv.x * 2 - 1, (uv.y * 2 - 1) * _ProjectionParams.x, 1, 1);
                float4 viewRay = mul(_ClipToView, clipPos);
                float3 viewDir = normalize(-viewRay.xyz / viewRay.w);
                float NdotV = 1 - dot(normal0, viewDir);

                float normalThreshold01 = saturate((NdotV - _DepthNormalThreshold) / (1 - _DepthNormalThreshold));
                float normalThreshold = normalThreshold01 * _DepthNormalThresholdScale + 1;

                float depthThreshold = _DepthThreshold * depth0 * normalThreshold;
                edgeDepth = edgeDepth > depthThreshold ? 1 : 0;

                // Debug: 1 = the view-space normal at this pixel, re-encoded to a viewable color —
                // should look like a smooth normal-map preview (facing-camera surfaces read as a
                // flat blue-ish/purple tone, since a normal of roughly (0,0,-1) in view space
                // encodes to about (0.5, 0.5, 0)). If this is a solid uniform color everywhere (or
                // black), ViewSpaceNormalsTexturePass isn't writing real per-object normals — check
                // that normalsMaterial is assigned and the pass runs before this one.
                // 2/3 = the raw depth/normal edge terms before combining, to tell which one is
                // misbehaving if the final outline looks wrong.
                if (_DebugView > 2.5) return half4(edgeNormal.xxx, 1);
                if (_DebugView > 1.5) return half4(edgeDepth.xxx, 1);
                if (_DebugView > 0.5) return half4(normal0 * 0.5 + 0.5, 1);

                // Distance fade: blend from _Color (near) to _FarColor (far, defaults to fully
                // transparent) between _FadeStartDistance and _FadeEndDistance, using the same
                // linear depth already sampled for the perspective scale above.
                float fadeT = saturate((centerDepthLinear - _FadeStartDistance) / max(_FadeEndDistance - _FadeStartDistance, 0.01));
                half4 fadedColor = lerp(_Color, _FarColor, fadeT);

                // Combine both edge tests, gate by layer mask, then alpha-blend the outline color
                // over the scene. Contribution scales each edge AFTER its own threshold decided
                // whether it fired at all — it doesn't change WHERE an edge is detected, only how
                // much that edge type is allowed to show once it has. Lowering _NormalContribution
                // fades out normal-only edges (e.g. curved-surface shading breaks) while leaving
                // depth-detected silhouettes (the ones from _DepthThreshold) untouched, and vice
                // versa — a knob for balance, independent of the detection thresholds above.
                float edge = max(edgeDepth * _DepthContribution, edgeNormal * _NormalContribution) * outlineMask;
                half4 outlineColor = half4(fadedColor.rgb, fadedColor.a * edge);
                return half4(lerp(sceneColor.rgb, outlineColor.rgb, outlineColor.a), sceneColor.a);
            }
            ENDHLSL
        }
    }
}
