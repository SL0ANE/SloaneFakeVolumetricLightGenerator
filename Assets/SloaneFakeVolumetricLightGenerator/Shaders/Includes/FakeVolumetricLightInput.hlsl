CBUFFER_START(UnityPerMaterial)
    sampler2D _BaseMap;
    float4 _BaseMap_ST;
    float _BeamSoftness;
    float _BeamThreshold;
    float4 _BeamSpeed;

    sampler2D _DustMap;
    float4 _DustMap_ST;
    float _DustSoftness;
    float _DustThreshold;
    float4 _DustSpeed;
    float4 _DustColor;

    float _ViewDirContribution;

    float _BeamDensity;
    float _DustDensity;

    float4 _StartColor;
    float4 _EndColor;
CBUFFER_END