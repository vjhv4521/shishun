Shader "Sprites/Water"
{
    Properties
    {
        _MainTex("Sprite Texture", 2D) = "white" {}
        [NoScaleOffset] _FlowMap("Flow (RG)", 2D) = "black" {}
        _Color("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha("Enable External Alpha", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
        [Toggle(Z_WRITE)] _ZWrite("Z Write", Float) = 0
        _BorderColor("Border color", Color) = (1,1,1,1)
        _BorderWidth("Border width", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Unlit"
        }

        Cull Off
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Blend One OneMinusSrcAlpha

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #pragma shader_feature_local_fragment _ Z_WRITE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_FlowMap);
            SAMPLER(sampler_FlowMap);
            TEXTURE2D(_AlphaTex);
            SAMPLER(sampler_AlphaTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _BorderColor;
                half _BorderWidth;
            CBUFFER_END

            half4 _RendererColor;
            float4 _Flip;
            half _EnableExternalAlpha;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            float4 PixelSnapPosition(float4 positionCS)
            {
                float2 halfScreen = max(_ScreenParams.xy * 0.5, 1.0);
                float2 ndc = positionCS.xy / positionCS.w;
                ndc = round(ndc * halfScreen) / halfScreen;
                positionCS.xy = ndc * positionCS.w;
                return positionCS;
            }

            half4 SampleSpriteTexture(float2 uv)
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                #if ETC1_EXTERNAL_ALPHA
                    half alpha = SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, uv).r;
                    color.a = lerp(color.a, alpha, _EnableExternalAlpha);
                #endif
                return color;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                input.positionOS.xy *= _Flip.xy;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                #if PIXELSNAP_ON
                    output.positionCS = PixelSnapPosition(output.positionCS);
                #endif
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color * _RendererColor;
                return output;
            }

            float2 FlowUV(float2 uv, float2 flowVector, float time)
            {
                float progress = cos(time * 0.5) / 30.0;
                return uv - flowVector * progress;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 flowVector = SAMPLE_TEXTURE2D(_FlowMap, sampler_FlowMap, input.uv).rg * 2.0 - 1.0;
                half4 color = SampleSpriteTexture(FlowUV(input.uv, flowVector, _Time.y)) * input.color;
                #if Z_WRITE
                    clip(color.a - 0.04);
                #endif
                color.rgb *= color.a;
                return color;
            }
            ENDHLSL
        }
    }
}
