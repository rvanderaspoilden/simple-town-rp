Shader "Custom/StylizedRibbon"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _MainTex("Arrow Texture", 2D) = "white" {}
        _ScrollSpeed("Scroll Speed", Vector) = (0, -1.2, 0, 0)
        _SoftEdges("Soft Edges", Range(0, 1)) = 0.5
        _AlphaMultiplier("Alpha Multiplier", Range(0, 5)) = 1.0
        _GlowIntensity("Glow Intensity", Range(1, 10)) = 2.0
        _UseAdditive("Use Additive Blending", Float) = 0
    }

    SubShader
    {
        Tags 
        { 
            "RenderPipeline" = "UniversalPipeline" 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            // Dynamic blend mode based on _UseAdditive
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _BaseColor;
            float4 _ScrollSpeed;
            float _SoftEdges;
            float _AlphaMultiplier;
            float _GlowIntensity;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Animated UVs
                float2 scrolledUV = IN.uv + _ScrollSpeed.xy * _Time.y;
                half4 tex = tex2D(_MainTex, scrolledUV);
                
                // Soft edge mask using U coordinate (0-1)
                float edgeMask = saturate(1.0 - abs(IN.uv.x - 0.5) * 2.0);
                edgeMask = pow(edgeMask, _SoftEdges * 4.0);
                
                half3 finalRGB = _BaseColor.rgb * tex.rgb * IN.color.rgb * _GlowIntensity;
                
                // If texture has no alpha (black background), use luminosity as alpha
                float alpha = tex.a;
                if (alpha < 0.01) alpha = saturate(tex.r + tex.g + tex.b);
                
                float finalAlpha = alpha * _BaseColor.a * IN.color.a * edgeMask * _AlphaMultiplier;

                return half4(finalRGB, finalAlpha);
            }
            ENDHLSL
        }
    }
}
