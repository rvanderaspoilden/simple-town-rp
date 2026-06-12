// Stylized construction reveal (URP). Reveals a mesh bottom-to-top driven by _Progress:
//  - below the reveal line : the real surface (copied _BaseMap/_BaseColor), flat-lit
//  - at the reveal line     : a bright emissive sweep band (the "dissolve" edge)
//  - above the reveal line  : a fresnel rim only (clips the fill) → white sketch-line
//                             silhouette of the object (Phase 2)
// Cutout (alpha-test) so there are no transparency sorting issues. Tunable for a cozy look.
Shader "Sim/ConstructionReveal"
{
    Properties
    {
        _BaseMap     ("Base Map", 2D) = "white" {}
        _BaseColor   ("Base Color", Color) = (1,1,1,1)
        _Progress    ("Progress", Range(0,1)) = 0
        _MinY        ("Bounds Min Y (world)", Float) = 0
        _MaxY        ("Bounds Max Y (world)", Float) = 1
        _AmbientTint ("Revealed Brightness", Range(0,2)) = 0.9
        [HDR] _EdgeColor ("Edge Sweep Color", Color) = (0.6, 1.6, 0.9, 1)
        _EdgeWidth   ("Edge Width (world)", Float) = 0.06
        [HDR] _SketchColor ("Sketch Line Color", Color) = (0.7, 1.4, 1.8, 1)
        _RimPower    ("Sketch Rim Power", Range(0.2, 8)) = 2.5
        _RimThreshold("Sketch Rim Threshold", Range(0,1)) = 0.4
        _ScanDensity ("Blueprint Scan Density", Float) = 16
        _ScanThickness ("Blueprint Scan Thickness", Range(0,0.5)) = 0.07
    }

    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }

        Pass
        {
            Name "ConstructionReveal"
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _Progress;
                float  _MinY;
                float  _MaxY;
                float  _AmbientTint;
                float4 _EdgeColor;
                float  _EdgeWidth;
                float4 _SketchColor;
                float  _RimPower;
                float  _RimThreshold;
                float  _ScanDensity;
                float  _ScanThickness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS  = p.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float revealLine = lerp(_MinY, _MaxY, saturate(_Progress));
                float d = IN.positionWS.y - revealLine;

                half4 baseC = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                if (d < -_EdgeWidth)
                {
                    // Revealed solid surface.
                    return half4(baseC.rgb * _AmbientTint, 1.0);
                }
                else if (d < _EdgeWidth)
                {
                    // Bright sweep band riding the reveal line.
                    return _EdgeColor;
                }

                // Above the line: blueprint sketch — silhouette rim (fresnel) + horizontal
                // scan lines, everything else clipped so the object reads as drawn lines.
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                float fres = pow(1.0 - saturate(dot(N, V)), _RimPower);
                float rim = step(_RimThreshold, fres);
                float scan = step(frac(IN.positionWS.y * _ScanDensity), _ScanThickness);
                clip(max(rim, scan) - 0.5);
                return _SketchColor;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
