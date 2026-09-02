Shader "Hidden/RoystanOutline"
{
    // Basata sull'outline shader di Roystan: https://roystan.net/articles/outline-shader/
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
        [KeywordEnum(Linear, Exponential)] _ThresholdFunction ("Distance Function", Float) = 0
        _ThresholdNearDistance ("Near Distance (world units)", Float) = 5
        _ThresholdFarDistance ("Far Distance (world units)", Float) = 40
        _ThresholdMultiplierNear ("Multiplier @ Near Distance", Float) = 1
        _ThresholdMultiplierFar ("Multiplier @ Far Distance", Float) = 4
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

            // Lordine di questi due includ conta: Blit.hlsl si aspetta che Core.hlsl sia gia' stato incluso.
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

            // Quanto e' "severa" la soglia di depth in base a quanto si è lontani dalla camera:
            // vicino puo restare stretta, lontano va allargata (altrimenti sul fondo della scena
            // bordi ovunque, la depth perde precisione con la distanza).
            float DistanceThresholdMultiplier(float distance) {
                float span = max(_ThresholdFarDistance - _ThresholdNearDistance, 0.0001);
                float t = saturate((distance - _ThresholdNearDistance) / span);
                if (_ThresholdFunction > 0.5) t = pow(t, max(_ThresholdExponent, 0.0001));
                return lerp(_ThresholdMultiplierNear, _ThresholdMultiplierFar, t);
            }

            half4 Frag(Varyings input) : SV_Target {
                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);
                float2 texelSize = _CameraDepthTexture_TexelSize.xy;

                // Spessore del bordo in texel
                // Il bordo fisso è inguardabile
                float centerDepthLinear = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float minScale = min(_MinScale, _MaxScale);
                float maxScale = max(_MinScale, _MaxScale);
                float scale = clamp(_Scale * (_ThicknessReferenceDistance / max(centerDepthLinear, 0.01)), minScale, maxScale);

                // Quattro punti attorno al pixel corrente, disposti a X, distanziati dello scale sopra
                float halfScaleFloor = floor(scale * 0.5);
                float halfScaleCeil = ceil(scale * 0.5);
                float2 uvBottomLeft = uv - texelSize * halfScaleFloor;
                float2 uvTopRight = uv + texelSize * halfScaleCeil;
                float2 uvBottomRight = uv + texelSize * float2(halfScaleCeil, -halfScaleFloor);
                float2 uvTopLeft = uv + texelSize * float2(-halfScaleFloor, halfScaleCeil);

                //(Roberts cross): confronta la depth tra le due coppie di
                // punti opposti. Se cambia bruscamente, vuol dire che li' c'e' un salto tra due
                //  oggetti diversi (o tra un oggetto e lo sfondo) quindi e' un bordo
                float depth0 = SampleSceneDepth(uvBottomLeft);
                float depth1 = SampleSceneDepth(uvTopRight);
                float depth2 = SampleSceneDepth(uvBottomRight);
                float depth3 = SampleSceneDepth(uvTopLeft);

                float depthFiniteDifference0 = depth1 - depth0;
                float depthFiniteDifference1 = depth3 - depth2;
                float edgeDepth = sqrt(pow(depthFiniteDifference0, 2) + pow(depthFiniteDifference1, 2)) * 100;

                // Sotto la soglia niente bordo
                float depthThreshold = _DepthThreshold * depth0 * DistanceThresholdMultiplier(centerDepthLinear);
                edgeDepth = edgeDepth > depthThreshold ? 1 : 0;

                if (_DebugView > 0.5) return half4(edgeDepth.xxx, 1);

                // Man mano che ci si allontana, il colore del bordo sfuma da _Color a _FarColor
                float fadeT = saturate((centerDepthLinear - _FadeStartDistance) / max(_FadeEndDistance - _FadeStartDistance, 0.01));
                half4 fadedColor = lerp(_Color, _FarColor, fadeT);

                // se c'è bordo mostro outline
                half4 outlineColor = half4(fadedColor.rgb, fadedColor.a * edgeDepth);
                return half4(lerp(sceneColor.rgb, outlineColor.rgb, outlineColor.a), sceneColor.a);
            }
            ENDHLSL
        }
    }
}
