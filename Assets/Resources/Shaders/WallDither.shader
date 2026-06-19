// Camera-occlusion wall fade (URP). A faithful URP/Lit forward pass with a screen-space
// ordered-dither dissolve driven by _Fade:
//   _Fade = 0  -> nothing clipped, renders exactly like URP/Lit (no visual change)
//   _Fade = 1  -> every pixel clipped, the wall is fully see-through
// Cutout/clip in the opaque queue (ZWrite On) so there are NO transparency sorting issues
// even when several walls overlap between the camera and the target.
//
// Shadow / depth / depth-normals / meta passes are reused verbatim from URP/Lit, so a wall
// at _Fade=0 is indistinguishable from a normal Lit wall. While dissolving, the wall still
// casts its real shadow and writes full depth — it is physically present, only the camera
// sees through it. _Fade is meant to be driven per-renderer via a MaterialPropertyBlock.
Shader "Sim/WallDither"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1
        _Smoothness("Smoothness", Range(0,1)) = 0
        _Metallic("Metallic", Range(0,1)) = 0

        _Fade("Dissolve Fade", Range(0,1)) = 0
        _DitherScale("Dither Cell Pixels", Range(1,4)) = 1

        // Kept so UsePass'd URP/Lit shadow/depth passes find every property they declare.
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        [HideInInspector] _Surface("__surface", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFragment

            // Material keyword (matches the wall's URP/Lit material).
            #pragma shader_feature_local _NORMALMAP

            // URP lighting keywords.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _BumpScale;
                float  _Smoothness;
                float  _Metallic;
                float  _Fade;
                float  _DitherScale;
                float  _Cutoff;
                float  _Surface;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            // 4x4 ordered Bayer matrix -> threshold in (0,1). Clipped when threshold < _Fade.
            float DitherThreshold(float2 pixel)
            {
                const float bayer[16] = {
                    0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
                    12.0 / 16.0, 4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                    3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
                    15.0 / 16.0, 7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
                };
                float2 p = floor(pixel / max(_DitherScale, 1.0));
                int x = (int)fmod(p.x, 4.0);
                int y = (int)fmod(p.y, 4.0);
                return bayer[y * 4 + x] + (0.5 / 16.0);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 4);
                half  fogFactor   : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings LitPassVertex(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = normInputs.normalWS;
                real sign = IN.tangentOS.w * GetOddNegativeScale();
                OUT.tangentWS = float4(normInputs.tangentWS, sign);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);

                OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.lightmapUV);
                OUTPUT_SH(OUT.normalWS.xyz, OUT.vertexSH);
                OUT.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return OUT;
            }

            half4 LitPassFragment(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Dissolve: discard before any lighting work.
                clip(DitherThreshold(IN.positionCS.xy) - _Fade);

                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = baseMap.rgb * _BaseColor.rgb;
                surfaceData.metallic   = _Metallic;
                surfaceData.specular   = half3(0, 0, 0);
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion  = 1.0h;
                surfaceData.alpha      = 1.0h;

            #ifdef _NORMALMAP
                surfaceData.normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
            #else
                surfaceData.normalTS = half3(0, 0, 1);
            #endif

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;

            #ifdef _NORMALMAP
                float sgn = IN.tangentWS.w;
                float3 bitangent = sgn * cross(IN.normalWS.xyz, IN.tangentWS.xyz);
                half3x3 tbn = half3x3(IN.tangentWS.xyz, bitangent, IN.normalWS.xyz);
                inputData.normalWS = TransformTangentToWorld(surfaceData.normalTS, tbn);
            #else
                inputData.normalWS = IN.normalWS;
            #endif
                inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);

            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
            #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif

                inputData.fogCoord = IN.fogFactor;
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SAMPLE_GI(IN.lightmapUV, IN.vertexSH, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(IN.lightmapUV);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }

        // Reuse URP/Lit for everything else so a wall at _Fade=0 is byte-identical to a Lit wall.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }

    FallBack "Universal Render Pipeline/Lit"
}
