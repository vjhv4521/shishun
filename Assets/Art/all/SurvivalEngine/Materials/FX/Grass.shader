Shader "FX/Grass"
{
    Properties
    {
        [Header(Shading)]
        _TopColor("Top Color", Color) = (1,1,1,1)
        _BottomColor("Bottom Color", Color) = (1,1,1,1)
        _TranslucentGain("Translucent Gain", Range(0,1)) = 0.5

        [Header(Grass Shape)]
        _BladeWidth("Blade Width", Float) = 0.05
        _BladeWidthRandom("Blade Width Random", Float) = 0.02
        _BladeHeight("Blade Height", Float) = 0.5
        _BladeHeightRandom("Blade Height Random", Float) = 0.3
        _BendRotationRandom("Bend Rotation Random", Range(0,1)) = 0.2
        _BladeForward("Blade Forward Amount", Float) = 0.38
        _BladeCurve("Blade Curvature Amount", Range(1,4)) = 2

        [Header(Grass Density)]
        _TessellationUniform("Grass Density", Range(1,64)) = 1

        [Header(Wind)]
        _WindStrength("Wind Strength", Float) = 1
        _WindFrequency("Wind Frequency", Vector) = (0.05,0.05,0,0)
        _WindDistortionMap("Wind Distortion Map", 2D) = "white" {}

        [Header(Fallback)]
        _Color("Color", Color) = (1,1,1,1)
        _MainTex("Base (RGB)", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
        }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex GrassVertex
            #pragma hull GrassHull
            #pragma domain GrassDomain
            #pragma geometry GrassGeometry
            #pragma fragment GrassFragment
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma exclude_renderers gles gles3 glcore

            #define SURVIVAL_GRASS_FORWARD_PASS 1
            #include "Assets/SurvivalEngine/Materials/FX/GrassPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex GrassVertex
            #pragma hull GrassHull
            #pragma domain GrassDomain
            #pragma geometry GrassGeometry
            #pragma fragment GrassShadowFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma exclude_renderers gles gles3 glcore

            #define SURVIVAL_GRASS_SHADOW_PASS 1
            #include "Assets/SurvivalEngine/Materials/FX/GrassPass.hlsl"
            ENDHLSL
        }
    }

    Fallback "FX/GrassMobile"
}
