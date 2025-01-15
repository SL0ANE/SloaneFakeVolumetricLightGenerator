#include "Packages/SloaneShaderGeneric/Includes/noise.hlsl"

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
    float3 positionVS               : TEXCOORD2;
    float3 normalWS                 : TEXCOORD3;
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
    float3 originPosWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    output.positionVS = TransformWorldToView(output.positionWS) - TransformWorldToView(originPosWS);
    output.positionCS = vertexInput.positionCS;

    output.color = input.color;

    return output;
}

half4 FakeVolumetricLightFragment(Varyings input) : SV_Target
{
    float3 normal = normalize(input.normalWS);
    float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);

    float value = 1.0 - input.color.r;
    float2 beamUV = input.uv.xy / input.uv.z + float2(0.5, 0.5);
    beamUV -= _Time.y * _BeamSpeed.xy / 32.0;
    beamUV = beamUV * _BaseMap_ST.xy + _BaseMap_ST.zw;
    float beam = 1.0 - voronoi(beamUV, _BeamDensity * float2(8.0, 8.0), float2(65535.0, 65535.0), float2(0.0, 0.0));
    beam *= pow(value, 0.5);
    float beamAAFactor = fwidth(beam);
    beamAAFactor = lerp(beamAAFactor, 1.0, _BeamSoftness);
    beam = smoothstep(_BeamThreshold, _BeamThreshold + beamAAFactor, beam);

    float2 dustUV = input.positionVS.xy;
    dustUV = dustUV * _DustMap_ST.xy + _DustMap_ST.zw;
    dustUV -= _Time.y * _DustSpeed.xy;
    float dust = 1.0 - voronoi(dustUV, _DustDensity * float2(16.0, 16.0), float2(65535.0, 65535.0), float2(0.0, 0.0), 0.996);
    float dustAAFactor = fwidth(dust);
    dustAAFactor = lerp(dustAAFactor, 1.0, _DustSoftness);
    dust = smoothstep(_DustThreshold, _DustThreshold + dustAAFactor, dust);

    float fog = fbm(dustUV, 0.8, _DustDensity * float2(0.2, 0.2), float2(65535.0, 65535.0), float2(0.0, 0.0), 6);

    value *= input.color.g;
    value *= beam;
    dust *= value;
    value *= lerp(1.0, abs(dot(normal, viewDir)), _ViewDirContribution);
    half4 color = lerp(_StartColor, _EndColor, input.color.r);
    color.a *= value;

    color += dust * _DustColor;

    color.a = saturate(color.a);
    return color;
}