// Stylised exterior-diorama surface for the window portal. Unlit but self-shaded by world normal
// (a fixed fake "sun"), so the 3D city outside reads with real form WITHOUT depending on any scene
// light (the gameplay sun is toggled off indoors, so we can't rely on it). Vertical-ish faces also
// get a free procedural grid of lit windows (hash-randomised), so plain boxes look like buildings
// with zero extra geometry. Use _LitChance = 0 for ground / non-building pieces.
Shader "Sim/DioramaBuilding"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.13, 0.15, 0.22, 1)
        _SunDir ("Fake Sun Dir (world)", Vector) = (0.4, 0.7, 0.55, 0)
        _Ambient ("Ambient", Range(0, 1)) = 0.45

        [Header(Procedural windows)]
        _WindowColor ("Window Glow", Color) = (1.0, 0.82, 0.5, 1)
        _WindowScale ("Windows per metre", Float) = 1.6
        _LitChance ("Lit Fraction", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "DioramaBuilding"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _SunDir;
                float  _Ambient;
                float4 _WindowColor;
                float  _WindowScale;
                float  _LitChance;
            CBUFFER_END

            // ── Time-of-day globals, published by ExteriorPortalCamera each frame from the
            // shared TimeManager clock (same source as MeteoManager) so the exterior matches
            // the current sky. When _ExtActive == 0 (no controller, e.g. editor preview) the
            // shader falls back to the per-material constants above.
            float4 _ExtSunDir;    // xyz = direction toward the sun
            float4 _ExtSunColor;  // directional light colour * intensity (≈0 at night)
            float4 _ExtSkyColor;  // ambient/sky colour (dim indigo at night → bright at noon)
            float  _ExtWindowLit; // 0..1 emissive-window multiplier (1 at night, 0 at midday)
            float  _ExtActive;    // 1 while the portal controller is driving the lighting

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS  = p.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            float hash21(float2 c)
            {
                return frac(sin(dot(c, float2(12.9898, 78.233))) * 43758.5453);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);

                float3 lightCol;
                float  winLit;
                if (_ExtActive > 0.5)
                {
                    float3 sun = normalize(_ExtSunDir.xyz);
                    float  ndl = saturate(dot(N, sun));
                    // Coloured sky ambient (per-material _Ambient keeps relative brightness,
                    // e.g. ground brighter than walls) + directional sun term.
                    lightCol = _ExtSkyColor.rgb * (_Ambient * 1.5 + 0.2) + _ExtSunColor.rgb * ndl;
                    winLit   = _ExtWindowLit;
                }
                else
                {
                    float3 sun = normalize(_SunDir.xyz);
                    float  ndl = saturate(dot(N, sun));
                    lightCol = (_Ambient + (1.0 - _Ambient) * ndl).xxx;
                    winLit   = 1.0;
                }
                float3 base = _BaseColor.rgb * lightCol;

                // Window grid only on vertical-ish faces.
                float vmask = 1.0 - smoothstep(0.35, 0.6, abs(N.y));
                float2 p = (abs(N.x) >= abs(N.z)) ? float2(IN.positionWS.z, IN.positionWS.y)
                                                  : float2(IN.positionWS.x, IN.positionWS.y);
                p *= _WindowScale;
                float2 cell = floor(p);
                float2 f = frac(p);
                float h = hash21(cell);
                float lit = step(1.0 - _LitChance, h);
                float pane = (f.x > 0.18 && f.x < 0.82 && f.y > 0.24 && f.y < 0.80) ? 1.0 : 0.0;
                // slight warm flicker of brightness per window
                float warm = 0.7 + 0.6 * hash21(cell + 3.17);
                float windows = lit * pane * vmask * warm;

                float3 col = base + windows * _WindowColor.rgb * winLit;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
