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
        _HaloColor    ("Halo Color (alpha=0 disables)", Color) = (0, 0, 0, 0)
        _HaloThickness("Halo Extra Thickness (px)", Range(0, 8)) = 0
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
            float4 _HaloColor;
            float  _HaloThickness;

            // .r = silhouette complète du prop, .g = visibilité (0 si occludé).
            float2 SampleMask(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_OutlineMaskTex, sampler_LinearClamp, uv).rg;
            }

            half4 frag (Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

                float center = SampleMask(uv).r;
                float2 t      = _OutlineMaskTex_TexelSize.xy * _Thickness;
                float2 tHalo  = _OutlineMaskTex_TexelSize.xy * (_Thickness + _HaloThickness);

                // Max sur 8 voisins, à deux rayons :
                //   nFull / nVis = main ring (silhouette complète + visibilité), rayon _Thickness.
                //   nFullHalo    = halo ring (silhouette uniquement), rayon _Thickness + _HaloThickness.
                // Le halo entoure le main outline : le main reste à sa largeur normale, le halo
                // ajoute une bande extérieure (la différence des deux bandes) qui sert de fond
                // sombre pour faire ressortir l'outline sur les surfaces de même couleur.
                float nFull     = 0;
                float nFullHalo = 0;
                float nVis      = 0;
                #define ACCUM_MAIN(o) { float2 m = SampleMask(uv + (o)); nFull = max(nFull, m.r); nVis = max(nVis, m.g); }
                #define ACCUM_HALO(o) { float r = SampleMask(uv + (o)).r; nFullHalo = max(nFullHalo, r); }
                ACCUM_MAIN(float2( t.x, 0))      ACCUM_HALO(float2( tHalo.x, 0))
                ACCUM_MAIN(float2(-t.x, 0))      ACCUM_HALO(float2(-tHalo.x, 0))
                ACCUM_MAIN(float2(0,  t.y))      ACCUM_HALO(float2(0,  tHalo.y))
                ACCUM_MAIN(float2(0, -t.y))      ACCUM_HALO(float2(0, -tHalo.y))
                ACCUM_MAIN(float2( t.x,  t.y))   ACCUM_HALO(float2( tHalo.x,  tHalo.y))
                ACCUM_MAIN(float2(-t.x,  t.y))   ACCUM_HALO(float2(-tHalo.x,  tHalo.y))
                ACCUM_MAIN(float2( t.x, -t.y))   ACCUM_HALO(float2( tHalo.x, -tHalo.y))
                ACCUM_MAIN(float2(-t.x, -t.y))   ACCUM_HALO(float2(-tHalo.x, -tHalo.y))
                #undef ACCUM_MAIN
                #undef ACCUM_HALO

                // Bord extérieur de la silhouette pleine, gardé UNIQUEMENT là où le prop
                // bordant est visible → l'outline est coupé devant le personnage (G=0)
                // sans jamais le contourer (la forme pleine n'a pas de trou).
                float visStep  = step(0.5, nVis);
                float edgeMain = saturate(nFull     - center) * visStep;
                float edgeHalo = saturate(nFullHalo - center) * visStep;
                // Anneau halo = bande extérieure au-delà du main outline.
                float haloRing = saturate(edgeHalo - edgeMain);

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

                // Composite : halo d'abord (sous le main), puis main sur le dessus.
                half4 col = sceneColor;
                col = lerp(col, _HaloColor, haloRing * _HaloColor.a);
                col = lerp(col, outlineCol, edgeMain * outlineCol.a);
                return col;
            }
            ENDHLSL
        }
    }
}
