// Mask shader used as the override material when rendering the hovered / highlighted
// prop(s) into the outline mask render target. Geometry only.
//
// Writes TWO channels (RG):
//   R = silhouette COMPLÈTE du prop — toujours 1, indépendamment de l'occlusion. La
//       forme reste pleine (aucun trou en forme de personnage) → la détection de bord
//       ne contoure jamais le personnage qui passe devant.
//   G = VISIBILITÉ — 0 si le fragment est occludé par une géométrie plus proche (ex. le
//       personnage local devant le prop), 1 sinon. Sert à COUPER l'outline devant les
//       occludeurs sans dessiner de contour autour d'eux.
// Depth-aware : compare la profondeur du fragment à la depth opaque de la scène.
// Requires the URP "Depth Texture" option enabled.
Shader "Hidden/OutlineMask"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "OutlineMask"
            Tags { "LightMode"="UniversalForward" }

            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Profondeur de la scène opaque (joueur + décor) à ce pixel.
                float2 uv       = IN.positionHCS.xy / _ScreenParams.xy;
                float  sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float  fragEye  = LinearEyeDepth(IN.positionHCS.z, _ZBufferParams);

                // R = silhouette complète (toujours 1). G = visibilité : 0 si ce fragment
                // est DERRIÈRE la scène opaque (occludé, ex. le personnage local devant le
                // prop), 1 sinon. Petit biais pour éviter le z-fighting quand le prop est
                // lui-même dans le depth.
                float visible = (fragEye > sceneEye + 0.03) ? 0.0 : 1.0;
                return half4(1, visible, 0, 1);
            }
            ENDHLSL
        }
    }
}
