Shader "Hidden/InkShinobi/SelectiveColorComposite" {
  SubShader {
    Tags { "RenderPipeline" = "UniversalPipeline" }
    ZWrite Off
    ZTest Always
    Cull Off

    Pass {
      Name "SelectiveColorComposite"

      HLSLPROGRAM
      #pragma vertex Vert
      #pragma fragment Frag

      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
      #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
      #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

      TEXTURE2D_X(_SelectiveColorMask);
      TEXTURE2D_X(_LightReceiverMask);
      TEXTURE2D_X(_AimPreviewColor);

      float _SelectiveColorIntensity;
      float _SelectiveColorSaturation;
      float _SelectiveColorPreserveStrength;

      int _FixedLightCount;
      float4 _FixedLightPositions[24];
      half4 _FixedLightColors[24];
      float _FixedLightFeathers[24];
      float4 _FixedLightLooks[24];
      float4 _FixedVisibilityRanges[768];

      int _ConeLightCount;
      float4 _ConeLightPositions[24];
      float4 _ConeLightDirections[24];
      half4 _ConeLightColors[24];
      float4 _ConeLightFeathers[24];
      float4 _ConeLightLooks[24];
      float4 _ConeVisibilityRanges[288];
      float4 _ConeEndWallPositions[24];
      float4 _ConeEndWallNormals[24];

      float ReadPackedFixedVisibility(int scalarIndex) {
        float4 packed = _FixedVisibilityRanges[scalarIndex / 4];
        int component = scalarIndex % 4;
        return component == 0 ? packed.x : component == 1 ? packed.y : component == 2 ? packed.z : packed.w;
      }

      float ReadPackedConeVisibility(int scalarIndex) {
        float4 packed = _ConeVisibilityRanges[scalarIndex / 4];
        int component = scalarIndex % 4;
        return component == 0 ? packed.x : component == 1 ? packed.y : component == 2 ? packed.z : packed.w;
      }

      float SampleFixedVisibilityRange(int lightIndex, float2 directionToSurface) {
        float angle = atan2(directionToSurface.x, directionToSurface.y);
        float samplePosition = frac(angle / 6.2831853 + 1.0) * 128.0;
        int lowerSample = min((int)floor(samplePosition), 127);
        int upperSample = lowerSample == 127 ? 0 : lowerSample + 1;
        int baseIndex = lightIndex * 128;
        // Favor slight over-occlusion at ray boundaries over allowing color to leak through a wall.
        return min(
          ReadPackedFixedVisibility(baseIndex + lowerSample),
          ReadPackedFixedVisibility(baseIndex + upperSample));
      }

      float SampleConeVisibilityRange(int coneIndex, float signedAngle, float halfAngle) {
        float normalizedAngle = saturate(signedAngle / max(halfAngle * 2.0, 0.0001) + 0.5);
        float samplePosition = normalizedAngle * 47.0;
        int lowerSample = min((int)floor(samplePosition), 46);
        float fraction = frac(samplePosition);
        int baseIndex = coneIndex * 48;
        return lerp(
          ReadPackedConeVisibility(baseIndex + lowerSample),
          ReadPackedConeVisibility(baseIndex + lowerSample + 1),
          fraction);
      }

      float EvaluateLightFlicker(float3 lightOrigin, float4 look) {
        float amount = look.y;
        if (amount <= 0.0) return 1.0;

        float phase = frac(sin(dot(lightOrigin.xz, float2(12.9898, 78.233))) * 43758.5453) * 6.2831853;
        float t = _Time.y * max(look.z, 0.01);
        float slow = sin(t + phase);
        float layered = sin(t * 0.73 + phase) * 0.55
                        + sin(t * 1.91 + phase * 1.37) * 0.3
                        + sin(t * 4.17 + phase * 2.11) * 0.15;
        float signal = lerp(slow, layered, saturate(look.w));
        return max(0.0, 1.0 + signal * amount);
      }

      half4 Frag(Varyings input) : SV_Target {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = input.texcoord;
        half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        half coverage = SAMPLE_TEXTURE2D_X(_SelectiveColorMask, sampler_LinearClamp, uv).a;
        half receiverCoverage = SAMPLE_TEXTURE2D_X(_LightReceiverMask, sampler_LinearClamp, uv).a;
        half receiverMask = smoothstep(0.0h, 0.15h, receiverCoverage);

        half luminance = dot(source.rgb, half3(0.2126h, 0.7152h, 0.0722h));
        half3 monochrome = luminance.xxx;
        half3 saturatedBackground = lerp(monochrome, source.rgb, _SelectiveColorSaturation);
        half3 processedBackground = lerp(source.rgb, saturatedBackground, _SelectiveColorIntensity);

        // Transparent selective-color objects have already contributed to Source according to
        // their alpha. Using that same alpha linearly here would attenuate their color twice,
        // making low-opacity light volumes and particles appear almost monochrome. Expand mask
        // coverage while retaining a short smooth ramp for antialiased and feathered edges.
        half expandedCoverage = smoothstep(0.0h, 0.15h, coverage);
        half preserve = saturate(expandedCoverage * _SelectiveColorPreserveStrength * _SelectiveColorIntensity);
        half3 finalColor = lerp(processedBackground, source.rgb, preserve);

        float rawDepth = SampleSceneDepth(uv);
#if UNITY_REVERSED_Z
        bool hasSurface = rawDepth > 0.00001;
#else
        bool hasSurface = rawDepth < 0.99999;
#endif
        if (hasSurface && (_FixedLightCount > 0 || _ConeLightCount > 0)) {
          float3 worldPosition = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
          half strongestFixedWeight = 0.0h;
          half4 strongestFixedLight = 0.0h;
          float4 strongestFixedLook = 0.0;
          float3 strongestFixedPosition = 0.0;

          if (_FixedLightCount > 0) {
            [loop]
            for (int lightIndex = 0; lightIndex < _FixedLightCount; lightIndex++) {
              float radius = _FixedLightPositions[lightIndex].w;
              float feather = min(_FixedLightFeathers[lightIndex], radius);
              float innerRadius = max(0.0, radius - feather);
              float distanceToLight = distance(worldPosition, _FixedLightPositions[lightIndex].xyz);
              float2 horizontalOffset = worldPosition.xz - _FixedLightPositions[lightIndex].xz;
              float horizontalDistance = length(horizontalOffset);
              float2 horizontalDirection = horizontalOffset / max(horizontalDistance, 0.0001);
              float visibleRange = SampleFixedVisibilityRange(lightIndex, horizontalDirection);
              half visibilityWeight = 1.0h - smoothstep(
                visibleRange + 0.005, visibleRange + 0.03, horizontalDistance);
              half weight = (1.0h - smoothstep(innerRadius, max(innerRadius + 0.0001, radius), distanceToLight))
                            * visibilityWeight * receiverMask;
              if (weight > strongestFixedWeight) {
                strongestFixedWeight = weight;
                strongestFixedLight = _FixedLightColors[lightIndex];
                strongestFixedLook = _FixedLightLooks[lightIndex];
                strongestFixedPosition = _FixedLightPositions[lightIndex].xyz;
              }
            }

            half tintStrength = saturate(strongestFixedWeight * strongestFixedLight.a);
            half tintLuminance = max(dot(strongestFixedLight.rgb, half3(0.2126h, 0.7152h, 0.0722h)), 0.0001h);
            half fixedFlicker = (half)EvaluateLightFlicker(strongestFixedPosition, strongestFixedLook);
            half3 tintedLight = strongestFixedLight.rgb
                                * ((half)strongestFixedLook.x * fixedFlicker / tintLuminance);
            finalColor = lerp(finalColor, tintedLight, tintStrength);
          }

          if (_ConeLightCount > 0) {
            half3 worldNormal = SampleSceneNormals(uv);
            half floorReceiver = step(0.65h, worldNormal.y) * receiverMask;
            half wallReceiver = (1.0h - step(0.45h, abs(worldNormal.y))) * receiverMask;
            half strongestNearWeight = 0.0h;
            half strongestFarWeight = 0.0h;
            half4 strongestNear = 0.0h;
            half4 strongestFar = 0.0h;
            float4 strongestNearLook = 0.0;
            float4 strongestFarLook = 0.0;
            float3 strongestNearPosition = 0.0;
            float3 strongestFarPosition = 0.0;

            [loop]
            for (int coneIndex = 0; coneIndex < _ConeLightCount; coneIndex++) {
              float3 toSurface = worldPosition - _ConeLightPositions[coneIndex].xyz;
              float distanceFromOrigin = length(toSurface.xz);
              float2 directionToSurface = toSurface.xz / max(distanceFromOrigin, 0.0001);

              float range = _ConeLightPositions[coneIndex].w;
              float rangeFeather = min(_ConeLightFeathers[coneIndex].x, range);
              float innerRange = max(0.0, range - rangeFeather);
              float rangeWeight = 1.0 - smoothstep(innerRange, max(innerRange + 0.0001, range), distanceFromOrigin);

              float2 coneDirection = normalize(_ConeLightDirections[coneIndex].xz);
              float directionDot = distanceFromOrigin < 0.0001 ? 1.0 : dot(directionToSurface, coneDirection);
              float outerCosine = _ConeLightDirections[coneIndex].w;
              float innerCosine = _ConeLightFeathers[coneIndex].y;
              float angleWeight = smoothstep(outerCosine, innerCosine, directionDot);

              // Sample the same horizontal ray fan used by the guard presentation so the
              // authoritative floor field stops at walls instead of shining through them.
              float halfAngle = acos(clamp(outerCosine, -1.0, 1.0));
              float signedAngle = atan2(
                coneDirection.y * directionToSurface.x - coneDirection.x * directionToSurface.y,
                directionDot);
              float visibleRange = SampleConeVisibilityRange(coneIndex, signedAngle, halfAngle);
              float visibilityWeight = 1.0 - smoothstep(
                max(0.0, visibleRange - 0.04), visibleRange + 0.01, distanceFromOrigin);
              half floorWeight = (half)(rangeWeight * angleWeight * visibilityWeight) * floorReceiver;

              // Walls only receive a compact circular mark where the guard looks directly at
              // an end wall. Side walls intentionally receive no literal cone intersection.
              float4 endWall = _ConeEndWallPositions[coneIndex];
              float4 endNormal = _ConeEndWallNormals[coneIndex];
              float radiusAtWall = max(endWall.w, 0.0001);
              float circleDistance = distance(worldPosition, endWall.xyz);
              float endFacing = abs(dot(normalize(worldNormal.xz), normalize(endNormal.xz)));
              half endWallWeight = (half)(
                (1.0 - smoothstep(radiusAtWall * 0.82, radiusAtWall, circleDistance))
                * step(0.72, endFacing) * endNormal.w) * wallReceiver;

              half weight = max(floorWeight, endWallWeight);
              bool isNearField = _ConeLightFeathers[coneIndex].z > 0.5;
              if (isNearField && weight > strongestNearWeight) {
                strongestNearWeight = weight;
                strongestNear = _ConeLightColors[coneIndex];
                strongestNearLook = _ConeLightLooks[coneIndex];
                strongestNearPosition = _ConeLightPositions[coneIndex].xyz;
              } else if (!isNearField && weight > strongestFarWeight) {
                strongestFarWeight = weight;
                strongestFar = _ConeLightColors[coneIndex];
                strongestFarLook = _ConeLightLooks[coneIndex];
                strongestFarPosition = _ConeLightPositions[coneIndex].xyz;
              }
            }

            // The pale far field communicates conditional visibility. Where it overlaps a fixed
            // light it becomes the same saturated yellow as an active danger region.
            half farLuminance = max(dot(strongestFar.rgb, half3(0.2126h, 0.7152h, 0.0722h)), 0.0001h);
            half farFlicker = (half)EvaluateLightFlicker(strongestFarPosition, strongestFarLook);
            half3 farTint = strongestFar.rgb * ((half)strongestFarLook.x * farFlicker / farLuminance);
            if (strongestFixedWeight > 0.0h) {
              half fixedLuminance = max(dot(strongestFixedLight.rgb, half3(0.2126h, 0.7152h, 0.0722h)), 0.0001h);
              half fixedFlicker = (half)EvaluateLightFlicker(strongestFixedPosition, strongestFixedLook);
              half3 fixedTint = strongestFixedLight.rgb
                                * ((half)strongestFixedLook.x * fixedFlicker / fixedLuminance);
              farTint = lerp(farTint, fixedTint, saturate(strongestFixedWeight));

              // A restrained luminance lift and narrow rim make the conditional far field
              // visibly activate where it enters a fixed-light pool without changing its shape.
              half activeOverlap = saturate(strongestFarWeight * strongestFixedWeight);
              half fixedBoundary = 1.0h - smoothstep(0.12h, 0.32h, abs(strongestFixedWeight - 0.5h));
              farTint *= 1.0h + activeOverlap * 0.12h
                         + fixedBoundary * strongestFarWeight * 0.08h;
            }
            finalColor = lerp(finalColor, farTint, saturate(strongestFarWeight * strongestFar.a));

            // Near is drawn over far. Its feather therefore reveals the pale far field directly,
            // eliminating the previous monochrome band between the two gameplay regions.
            half nearLuminance = max(dot(strongestNear.rgb, half3(0.2126h, 0.7152h, 0.0722h)), 0.0001h);
            half nearFlicker = (half)EvaluateLightFlicker(strongestNearPosition, strongestNearLook);
            half3 nearTint = strongestNear.rgb * ((half)strongestNearLook.x * nearFlicker / nearLuminance);
            finalColor = lerp(finalColor, nearTint, saturate(strongestNearWeight * strongestNear.a));
          }
        }

        // Aim colors are gameplay semantics. Restore their unprocessed material output after
        // monochrome, fake-light tinting, and Volume post effects have all been evaluated.
        half4 aimPreview = SAMPLE_TEXTURE2D_X(_AimPreviewColor, sampler_LinearClamp, uv);
        // Expand translucent antialiasing and soft-edge coverage so colored light cannot survive
        // inside a nominally black trajectory and create a misleading yellow/colored fringe.
        half aimCoverage = smoothstep(0.0h, 0.15h, aimPreview.a);
        finalColor = lerp(finalColor, aimPreview.rgb, aimCoverage);

        return half4(finalColor, source.a);
      }
      ENDHLSL
    }
  }
}
