#include "Packages/SloaneShaderGeneric/Includes/Noise.hlsl"

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
    float4 texcoord     : TEXCOORD0;

    float4 color  : COLOR0;

    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 uv                       : TEXCOORD0;
    float3 positionWS               : TEXCOORD1;
    float3 normalWS                 : TEXCOORD2;
    float4 color                    : COLOR0;

    float4 positionCS               : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings FakeVolumetricLightVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    output.uv = input.texcoord;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.positionWS = vertexInput.positionWS;
    output.positionCS = vertexInput.positionCS;

    output.color = input.color;

    return output;
}

half4 FakeVolumetricLightFragment(Varyings input) : SV_Target
{
    float3 normal = normalize(input.normalWS);

    float value = 1.0 - input.color.r;
    float2 noiseUV = input.uv.xy / input.uv.z + float2(0.5, 0.5);
    float noise = 1.0 - voronoi(noiseUV, _NoiseDensity * float2(8.0, 8.0), float2(65535.0, 65535.0), float2(0.0, 0.0));
    noise *= pow(value, 0.5);
    float noiseAAFactor = fwidth(noise);
    noiseAAFactor = lerp(noiseAAFactor, 1.0, _BeamSoftness);
    noise = smoothstep(_BeamThreshold, _BeamThreshold + noiseAAFactor, noise);

    value *= input.color.g;
    value *= noise;
    half4 color = lerp(_StartColor, _EndColor, input.color.r);
    color.a *= value;
    color.a = saturate(color.a);
    return color;
}