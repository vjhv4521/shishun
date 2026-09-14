#ifndef SURVIVAL_ENGINE_GRASS_PASS_INCLUDED
#define SURVIVAL_ENGINE_GRASS_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

#define BLADE_SEGMENTS 3

TEXTURE2D(_WindDistortionMap);
SAMPLER(sampler_WindDistortionMap);

CBUFFER_START(UnityPerMaterial)
    half4 _TopColor;
    half4 _BottomColor;
    half4 _Color;
    float4 _MainTex_ST;
    float4 _WindDistortionMap_ST;
    float4 _WindFrequency;
    float _TranslucentGain;
    float _BladeWidth;
    float _BladeWidthRandom;
    float _BladeHeight;
    float _BladeHeightRandom;
    float _BendRotationRandom;
    float _BladeForward;
    float _BladeCurve;
    float _TessellationUniform;
    float _WindStrength;
CBUFFER_END

#if defined(SURVIVAL_GRASS_SHADOW_PASS)
float3 _LightDirection;
float3 _LightPosition;
#endif

struct GrassAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
};

struct GrassControlPoint
{
    float4 positionOS : INTERNALTESSPOS;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
};

struct GrassTessellationFactors
{
    float edge[3] : SV_TessFactor;
    float inside : SV_InsideTessFactor;
};

struct GrassVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float2 uv : TEXCOORD2;
    half fogFactor : TEXCOORD3;
};

float GrassRandom(float3 value)
{
    return frac(sin(dot(value, float3(12.9898, 78.233, 53.539))) * 43758.5453);
}

float3x3 GrassAngleAxis(float angle, float3 axis)
{
    float sine;
    float cosine;
    sincos(angle, sine, cosine);
    float oneMinusCosine = 1.0 - cosine;

    return float3x3(
        oneMinusCosine * axis.x * axis.x + cosine,
        oneMinusCosine * axis.x * axis.y - sine * axis.z,
        oneMinusCosine * axis.x * axis.z + sine * axis.y,
        oneMinusCosine * axis.x * axis.y + sine * axis.z,
        oneMinusCosine * axis.y * axis.y + cosine,
        oneMinusCosine * axis.y * axis.z - sine * axis.x,
        oneMinusCosine * axis.x * axis.z - sine * axis.y,
        oneMinusCosine * axis.y * axis.z + sine * axis.x,
        oneMinusCosine * axis.z * axis.z + cosine);
}

GrassControlPoint GrassVertex(GrassAttributes input)
{
    GrassControlPoint output;
    output.positionOS = input.positionOS;
    output.normalOS = input.normalOS;
    output.tangentOS = input.tangentOS;
    return output;
}

GrassTessellationFactors GrassPatchConstant(InputPatch<GrassControlPoint, 3> patch)
{
    GrassTessellationFactors factors;
    factors.edge[0] = _TessellationUniform;
    factors.edge[1] = _TessellationUniform;
    factors.edge[2] = _TessellationUniform;
    factors.inside = _TessellationUniform;
    return factors;
}

[domain("tri")]
[outputcontrolpoints(3)]
[outputtopology("triangle_cw")]
[partitioning("integer")]
[patchconstantfunc("GrassPatchConstant")]
GrassControlPoint GrassHull(InputPatch<GrassControlPoint, 3> patch, uint id : SV_OutputControlPointID)
{
    return patch[id];
}

[domain("tri")]
GrassControlPoint GrassDomain(
    GrassTessellationFactors factors,
    const OutputPatch<GrassControlPoint, 3> patch,
    float3 barycentricCoordinates : SV_DomainLocation)
{
    GrassControlPoint output;
    output.positionOS =
        patch[0].positionOS * barycentricCoordinates.x +
        patch[1].positionOS * barycentricCoordinates.y +
        patch[2].positionOS * barycentricCoordinates.z;
    output.normalOS =
        patch[0].normalOS * barycentricCoordinates.x +
        patch[1].normalOS * barycentricCoordinates.y +
        patch[2].normalOS * barycentricCoordinates.z;
    output.tangentOS =
        patch[0].tangentOS * barycentricCoordinates.x +
        patch[1].tangentOS * barycentricCoordinates.y +
        patch[2].tangentOS * barycentricCoordinates.z;
    return output;
}

GrassVaryings BuildGrassVertex(
    float3 basePositionOS,
    float width,
    float height,
    float forward,
    float2 uv,
    float3x3 transformMatrix)
{
    GrassVaryings output;
    float3 tangentPoint = float3(width, forward, height);
    float3 tangentNormal = normalize(float3(0.0, -1.0, forward));
    float3 normalOS = normalize(mul(transformMatrix, tangentNormal));
    float3 positionOS = basePositionOS + mul(transformMatrix, tangentPoint);
    float3 positionWS = TransformObjectToWorld(positionOS);
    float3 normalWS = TransformObjectToWorldNormal(normalOS);

    output.positionWS = positionWS;
    output.normalWS = normalWS;
    output.uv = uv;

    #if defined(SURVIVAL_GRASS_SHADOW_PASS)
        #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
            float3 lightDirectionWS = normalize(_LightPosition - positionWS);
        #else
            float3 lightDirectionWS = _LightDirection;
        #endif
        output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
        output.positionCS = ApplyShadowClamping(output.positionCS);
        output.fogFactor = 0.0;
    #else
        output.positionCS = TransformWorldToHClip(positionWS);
        output.fogFactor = ComputeFogFactor(output.positionCS.z);
    #endif

    return output;
}

[maxvertexcount(BLADE_SEGMENTS * 2 + 1)]
void GrassGeometry(triangle GrassControlPoint input[3], inout TriangleStream<GrassVaryings> stream)
{
    float3 positionOS = input[0].positionOS.xyz;
    float3 normalOS = normalize(input[0].normalOS);
    float4 tangentOS = input[0].tangentOS;
    float3 bitangentOS = cross(normalOS, tangentOS.xyz) * tangentOS.w;

    float3x3 tangentToObject = float3x3(
        tangentOS.x, bitangentOS.x, normalOS.x,
        tangentOS.y, bitangentOS.y, normalOS.y,
        tangentOS.z, bitangentOS.z, normalOS.z);

    float2 windSample = float2(0.0, 1.0);
    if (_WindStrength > 0.01)
    {
        float2 windUV = positionOS.xz * _WindDistortionMap_ST.xy + _WindDistortionMap_ST.zw + _WindFrequency.xy * _Time.y;
        windSample = (SAMPLE_TEXTURE2D_LOD(_WindDistortionMap, sampler_WindDistortionMap, windUV, 0).xy * 2.0 - 1.0) * _WindStrength;
    }

    float windLength = max(length(windSample), 0.0001);
    float3 windAxis = float3(windSample / windLength, 0.0);
    float3x3 windRotation = GrassAngleAxis(PI * windLength, windAxis);
    float3x3 facingRotation = GrassAngleAxis(GrassRandom(positionOS) * TWO_PI, float3(0.0, 0.0, 1.0));
    float3x3 bendRotation = GrassAngleAxis(GrassRandom(positionOS.zzx) * _BendRotationRandom * PI * 0.5, float3(-1.0, 0.0, 0.0));
    float3x3 transformation = mul(mul(mul(tangentToObject, windRotation), facingRotation), bendRotation);
    float3x3 facingTransformation = mul(tangentToObject, facingRotation);

    float height = (GrassRandom(positionOS.zyx) * 2.0 - 1.0) * _BladeHeightRandom + _BladeHeight;
    float width = (GrassRandom(positionOS.xzy) * 2.0 - 1.0) * _BladeWidthRandom + _BladeWidth;
    float forward = GrassRandom(positionOS.yyz) * _BladeForward;

    [unroll]
    for (int segment = 0; segment < BLADE_SEGMENTS; ++segment)
    {
        float t = segment / (float)BLADE_SEGMENTS;
        float segmentHeight = height * t;
        float segmentWidth = width * (1.0 - t);
        float segmentForward = pow(t, _BladeCurve) * forward;
        float3x3 segmentTransform = segment == 0 ? facingTransformation : transformation;
        stream.Append(BuildGrassVertex(positionOS, segmentWidth, segmentHeight, segmentForward, float2(0.0, t), segmentTransform));
        stream.Append(BuildGrassVertex(positionOS, -segmentWidth, segmentHeight, segmentForward, float2(1.0, t), segmentTransform));
    }

    stream.Append(BuildGrassVertex(positionOS, 0.0, height, forward, float2(0.5, 1.0), transformation));
}

half4 GrassFragment(GrassVaryings input, half facing : VFACE) : SV_Target
{
    half3 normalWS = NormalizeNormalPerPixel(facing >= 0 ? input.normalWS : -input.normalWS);
    Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
    half ndotl = saturate(dot(normalWS, mainLight.direction) + _TranslucentGain);
    half3 lighting = SampleSH(normalWS) + mainLight.color * ndotl * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
    half4 color = lerp(_BottomColor, _TopColor, input.uv.y);
    color.rgb = MixFog(color.rgb * lighting, input.fogFactor);
    return color;
}

half4 GrassShadowFragment(GrassVaryings input) : SV_Target
{
    return 0;
}

#endif
