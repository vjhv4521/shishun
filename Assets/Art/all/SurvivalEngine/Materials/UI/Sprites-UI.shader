Shader "Sprites/UI"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        [MaterialToggle] PixelSnap("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha("Enable External Alpha", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
        [Toggle(Z_WRITE)] _ZWrite("Z Write", Float) = 0
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_AlphaTex);
            SAMPLER(sampler_AlphaTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
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

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                #if ETC1_EXTERNAL_ALPHA
                    half alpha = SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, input.uv).r;
                    color.a = lerp(color.a, alpha, _EnableExternalAlpha);
                #endif
                color *= input.color;
                color.rgb *= color.a;
                return color;
            }
            ENDHLSL
        }
    }
}
