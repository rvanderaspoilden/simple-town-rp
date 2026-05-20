// URP inverted-hull outline. Designed to be added as an EXTRA material on a prop's
// renderer(s) while hovered: it re-draws the mesh expanded along its normals with
// back-face culling, producing a coloured silhouette border around the original.
Shader "Custom/PropHoverOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.25, 0.8, 1.0, 1.0)
        _OutlineWidth ("Outline Width (world units)", Range(0, 0.1)) = 0.025
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" "RenderType"="Opaque" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode"="UniversalForward" }

            Cull Front      // draw only back faces of the expanded hull → a border
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            float4 _OutlineColor;
            float  _OutlineWidth;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = normalize(TransformObjectToWorldNormal(IN.normalOS));
                positionWS += normalWS * _OutlineWidth;
                OUT.positionHCS = TransformWorldToHClip(positionWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
