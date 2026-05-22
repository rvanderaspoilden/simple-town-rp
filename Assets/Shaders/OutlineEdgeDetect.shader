// Fullscreen composite: reads the camera colour (_BlitTexture) and the outline mask
// (_OutlineMaskTex, set as a global by the mask pass), detects the mask's outer edge,
// and draws the outline colour there. Mesh-agnostic — outlines the full silhouette.
Shader "Hidden/OutlineEdgeDetect"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.25, 0.8, 1.0, 1.0)
        _Thickness    ("Thickness (px)", Range(1, 8)) = 2
        [Toggle] _UseGradient ("Use Moving Gradient", Float) = 0
        _GradientColor ("Gradient Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _GradientSpeed ("Gradient Speed", Float) = 2.0
        _GradientFrequency ("Gradient Frequency", Float) = 6.0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "OutlineComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            // Core.hlsl first — it defines TEXTURE2D_X and friends that Blit.hlsl uses.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_OutlineMaskTex);
            float4 _OutlineMaskTex_TexelSize;
            float4 _OutlineColor;
            float  _Thickness;
            float  _UseGradient;
            float4 _GradientColor;
            float  _GradientSpeed;
            float  _GradientFrequency;

            float SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_OutlineMaskTex, sampler_LinearClamp, uv).r;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

                float center = SampleMask(uv);
                float2 t = _OutlineMaskTex_TexelSize.xy * _Thickness;

                // Max of 8 neighbours — outline appears where the centre is outside the
                // mask but a neighbour is inside (the outer border of the silhouette).
                float n = 0;
                n = max(n, SampleMask(uv + float2( t.x, 0)));
                n = max(n, SampleMask(uv + float2(-t.x, 0)));
                n = max(n, SampleMask(uv + float2(0,  t.y)));
                n = max(n, SampleMask(uv + float2(0, -t.y)));
                n = max(n, SampleMask(uv + float2( t.x,  t.y)));
                n = max(n, SampleMask(uv + float2(-t.x,  t.y)));
                n = max(n, SampleMask(uv + float2( t.x, -t.y)));
                n = max(n, SampleMask(uv + float2(-t.x, -t.y)));

                float edge = saturate(n - center);

                // Couleur d'outline : soit plate (_OutlineColor), soit une bande
                // lumineuse qui balaie la silhouette en diagonale pour attirer l'œil.
                half4 outlineCol = _OutlineColor;
                if (_UseGradient > 0.5)
                {
                    // Phase diagonale qui défile dans le temps → bande mobile.
                    float phase = (uv.x + uv.y) * _GradientFrequency - _Time.y * _GradientSpeed;
                    float band = sin(phase) * 0.5 + 0.5; // 0..1
                    band = band * band;                  // resserre la bande (pic plus net)
                    outlineCol = lerp(_OutlineColor, _GradientColor, band);
                }

                return lerp(sceneColor, outlineCol, edge * outlineCol.a);
            }
            ENDHLSL
        }
    }
}
