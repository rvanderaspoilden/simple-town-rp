// Camera-occlusion wall fade, "vision circle" variant (URP). Same faithful URP/Lit forward
// pass as Sim/WallDither, but the dither dissolve is confined to a SOFT SCREEN-SPACE DISC
// centred on the target instead of dissolving the whole wall:
//   - outside the disc : never clipped (the wall stays fully solid)
//   - inside the disc  : ordered-dither clip driven by per-renderer _Fade, so a clean hole
//                        opens up around the character/vehicle with a soft dithered rim.
//
// The disc is described by three GLOBAL uniforms set once per frame by CameraWallFader:
//   _VisionCenter.xy : target centre in normalized screen UV (0..1, bottom-left origin)
//   _VisionRadius    : disc radius in screen-height-normalized units (aspect-corrected here)
//   _VisionSoftness  : 0..1 fraction of the radius used for the soft rim
// Per-renderer _Fade (0..1, MaterialPropertyBlock) still gates fade-in/out per occluding wall.
//
// Opaque queue / ZWrite On (no transparency sorting). Shadow/depth/meta reused from URP/Lit.
Shader "Sim/WallVisionCircle"
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

            #pragma shader_feature_local _NORMALMAP

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

            // Global vision-disc uniforms (set per-frame via Shader.SetGlobal* by CameraWallFader).
            float4 _VisionCenter;
            float  _VisionRadius;
            float  _VisionSoftness;

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

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

            // 1 at the disc centre -> 0 outside, with a soft rim. 0 when radius<=0 (disabled).
            float VisionMask(float4 positionCS)
            {
                if (_VisionRadius <= 0.0) return 0.0;
                float2 uv = GetNormalizedScreenSpaceUV(positionCS);
                float2 d = uv - _VisionCenter.xy;
                d.x *= (_ScreenParams.x / max(_ScreenParams.y, 1.0)); // aspect-correct -> true circle
                float dist = length(d);
                float inner = _VisionRadius * (1.0 - saturate(_VisionSoftness));
                return 1.0 - smoothstep(inner, _VisionRadius, dist);
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

                // Dissolve only inside the vision disc.
                float localFade = _Fade * VisionMask(IN.positionCS);
                clip(DitherThreshold(IN.positionCS.xy) - localFade);

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

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
        UsePass "Universal Render Pipeline/Lit/Meta"
    }

    FallBack "Universal Render Pipeline/Lit"
}
