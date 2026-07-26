Shader "Hidden/ViewSpaceNormals"
{
    // Override material for ViewSpaceNormalsTexturePass: draws every opaque object's normal,
    // transformed into VIEW space (relative to the camera) instead of its usual color. Written as
    // a normal object shader (not a full-screen blit) because it needs the real per-vertex normal
    // of every mesh in the scene, not just a screen-space buffer.
    // Output is remapped from -1..1 to 0..1 (n * 0.5 + 0.5) since the render target is an unsigned
    // color format — the outline pass reading this texture must undo that remap (n * 2 - 1) before
    // using it as a direction again.
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ViewSpaceNormals"
            // Depth is already correct from the main opaque pass (this redraws the same geometry
            // after it) — we only need to TEST against it to reject occluded fragments, not write
            // it again, so the depth attachment can be bound read-only on the C# side.
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 normalVS : TEXCOORD0;
            };

            Varyings Vert(Attributes IN) {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);

                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                // The view matrix is rigid (rotation + translation, no scale), so multiplying by
                // its 3x3 part directly is exact here — no inverse-transpose needed like you'd use
                // for a non-uniformly scaled object-to-world transform.
                OUT.normalVS = mul((float3x3)UNITY_MATRIX_V, normalWS);

                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target {
                float3 n = normalize(IN.normalVS);
                return half4(n * 0.5 + 0.5, 1);
            }
            ENDHLSL
        }
    }
}
