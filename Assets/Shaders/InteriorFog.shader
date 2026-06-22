// Fullscreen "interior atmosphere": while the local player is inside a building, every
// pixel whose reconstructed world position falls OUTSIDE the current room's bounds (and
// the sky) is blended toward a soft cosy backdrop colour, sealing the interior. Driven
// entirely by globals published by InteriorAtmosphere — no per-object material work.
//
//   _InteriorBlend    0..1 master (0 = feature inert)
//   _InteriorCenter   world XZ centre of the room (xy used)
//   _InteriorExtents  world XZ half-size of the room (xy used)
//   _InteriorCeilingY world Y above which fragments fade out
//   _InteriorSoftness world-unit fade band past the walls / ceiling
//   _InteriorColor    backdrop colour the exterior melts into
//   _InteriorInvVP    inverse GPU view-projection (pushed from C# for blit-pass safety)
Shader "Hidden/InteriorFog"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "InteriorFog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            // Core.hlsl first — defines TEXTURE2D_X etc. that Blit.hlsl relies on, plus
            // ComputeWorldSpacePosition / UNITY_REVERSED_Z / UNITY_RAW_FAR_CLIP_VALUE.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _InteriorCenter;
            float4 _InteriorExtents;
            float  _InteriorCeilingY;
            float  _InteriorSoftness;
            float4 _InteriorColor;
            float  _InteriorBlend;
            float4x4 _InteriorInvVP;

            half4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

                if (_InteriorBlend < 0.001)
                    return sceneColor;

                float rawDepth = SampleSceneDepth(uv);

                // Sky = the far clip value (0 under reversed-Z, 1 otherwise). Reconstruction at
                // the far plane is numerically unstable, so classify it explicitly as outside.
                #if UNITY_REVERSED_Z
                    float skyAmount = step(rawDepth, 1e-5);
                #else
                    float skyAmount = step(1.0 - 1e-5, rawDepth);
                #endif

                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, _InteriorInvVP);

                // Distance outside the padded room box, measured in XZ (0 inside the room).
                float2 d = abs(worldPos.xz - _InteriorCenter.xy) - _InteriorExtents.xy;
                float distXZ = length(max(d, 0.0));
                float soft = max(_InteriorSoftness, 1e-3);

                float outsideXZ = smoothstep(0.0, soft, distXZ);
                float aboveCeil = smoothstep(0.0, soft, worldPos.y - _InteriorCeilingY);
                float outside   = max(skyAmount, max(outsideXZ, aboveCeil));

                float fog = saturate(outside) * saturate(_InteriorBlend);
                return half4(lerp(sceneColor.rgb, _InteriorColor.rgb, fog), sceneColor.a);
            }
            ENDHLSL
        }
    }
}
