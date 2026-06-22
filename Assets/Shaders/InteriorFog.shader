// Fullscreen "interior atmosphere": while the local player is inside a building AND the camera
// sits above the roof line, every pixel whose reconstructed world position falls OUTSIDE the
// room's XZ footprint (and the sky) is blended toward a soft cosy backdrop colour, sealing the
// interior. The "above the roof" gate lives in InteriorAtmosphere (C#): it ramps _InteriorBlend
// up only as the camera climbs through a few-unit band at the roof height, so a normal top-down
// view is sealed but zooming the camera down to an eye-level view below the roof fades the fog
// out and reveals the exterior through doors / windows naturally. Driven entirely by globals
// published by InteriorAtmosphere — no per-object material work.
//
//   _InteriorBlend    0..1 master (0 = feature inert) — already folded with the camera-height gate
//   _InteriorCenter   world XZ centre of the room (xy used)
//   _InteriorExtents  world XZ half-size of the room (xy used)
//   _InteriorSoftness world-unit fade band past the walls
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

                // Distance outside the padded room footprint, measured in XZ (0 inside the room).
                // The interior always stays fully clear; the fade band starts at the wall line.
                float2 d = abs(worldPos.xz - _InteriorCenter.xy) - _InteriorExtents.xy;
                float distXZ = length(max(d, 0.0));
                float soft = max(_InteriorSoftness, 1e-3);

                float outsideXZ = smoothstep(0.0, soft, distXZ);
                float outside   = max(skyAmount, outsideXZ);

                float fog = saturate(outside) * saturate(_InteriorBlend);
                return half4(lerp(sceneColor.rgb, _InteriorColor.rgb, fog), sceneColor.a);
            }
            ENDHLSL
        }
    }
}
