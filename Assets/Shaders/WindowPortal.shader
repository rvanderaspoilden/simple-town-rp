// Window-as-portal: the glass shows a live 3D exterior rendered by a dedicated "portal" camera
// into a screen-sized RenderTexture (_ExteriorRT, published as a global by ExteriorPortalCamera).
//
// The portal camera shares the gameplay camera's exact viewpoint but only draws the Exterior-layer
// diorama, so sampling the RT at THIS fragment's screen position lines the exterior up perfectly
// inside the window opening, with true parallax (it is real 3D geometry seen from the real eye) and
// correct occlusion by the window frame. Unlit on purpose; a faint fresnel/glint sells the glass.
//
// When no portal is active (_PortalActive == 0, e.g. outdoors), the glass falls back to a flat tint
// so windows never show stale RT garbage.
Shader "Sim/WindowPortal"
{
    Properties
    {
        _FallbackColor ("Fallback (no portal)", Color) = (0.12, 0.13, 0.16, 1)

        [Header(Glass)]
        _GlassTint ("Glass Reflection Tint", Color) = (0.8, 0.9, 1, 1)
        _FresnelPower ("Fresnel Power", Range(1, 8)) = 4
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.12
        _GlassSpecTint ("Glint Tint", Color) = (1, 1, 1, 1)
        _GlassSpecDir ("Glint Light Dir (world)", Vector) = (0.3, 0.6, -0.5, 0)
        _GlassSpecPower ("Glint Sharpness", Range(4, 256)) = 60
        _GlassSpecStrength ("Glint Strength", Range(0, 2)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "WindowPortal"
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ExteriorRT);
            SAMPLER(sampler_ExteriorRT);

            CBUFFER_START(UnityPerMaterial)
                float4 _FallbackColor;
                float4 _GlassTint;
                float  _FresnelPower;
                float  _FresnelStrength;
                float4 _GlassSpecTint;
                float4 _GlassSpecDir;
                float  _GlassSpecPower;
                float  _GlassSpecStrength;
            CBUFFER_END

            // Published per-frame by ExteriorPortalCamera (1 = portal RT valid, 0 = fallback).
            float _PortalActive;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 screenPos   : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 viewDirWS   : TEXCOORD2;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.screenPos   = ComputeScreenPos(p.positionCS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS   = GetWorldSpaceViewDir(p.positionWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);
                half3 ext = SAMPLE_TEXTURE2D(_ExteriorRT, sampler_ExteriorRT, uv).rgb;
                half3 col = lerp(_FallbackColor.rgb, ext, saturate(_PortalActive));

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewDirWS);

                // Fresnel reflection of a soft glass tint, strong only at grazing edges.
                float fres = pow(saturate(1.0 - saturate(dot(N, V))), _FresnelPower);
                col = lerp(col, _GlassTint.rgb, fres * _FresnelStrength);

                // Moving specular glint (slides as the camera moves).
                float3 H = normalize(V + normalize(_GlassSpecDir.xyz));
                float spec = pow(saturate(dot(N, H)), _GlassSpecPower) * _GlassSpecStrength;
                col += spec * _GlassSpecTint.rgb;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
