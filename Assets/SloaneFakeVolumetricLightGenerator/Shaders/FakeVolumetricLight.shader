Shader "Sloane/FakeVolumetricLight"
{
    Properties
    {
        _BaseMap ("Beam Noise Texture", 2D) = "white" {}
        _DustMap ("Dust Texture", 2D) = "white" {}
        [HDR]_StartColor ("Beam Start Color", Color) = (1,1,1,1)
        [HDR]_EndColor ("Beam End Color", Color) = (1,1,1,1)

        _BeamThreshold ("Beam Threshold", Range(0.0, 1.0)) = 0.0
        _BeamSoftness ("Beam Softness", Range(0.0, 1.0)) = 0.0
        _BeamSpeed ("Beam Speed", Vector) = (0.618, 1.0, 0, 0)

        [HDR]_DustColor ("Dust Color", Color) = (1,1,1,1)
        _DustThreshold ("Dust Threshold", Range(0.0, 1.0)) = 0.0
        _DustSoftness ("Dust Softness", Range(0.0, 1.0)) = 0.0
        _DustSpeed ("Dust Speed", Vector) = (0.618, -1, 0, 0)

        _BeamDensity ("Beam Density", Range(0.0, 8.0)) = 1.0
        _DustDensity ("Dust Density", Range(0.0, 8.0)) = 1.0

        _ViewDirContribution ("View Direction Contribution", Range(0.0, 1.0)) = 0.8
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "RenderType"="Transparent"}
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            ZWrite Off
            Cull Off

            Blend SrcAlpha OneMinusSrcAlpha
            
            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex FakeVolumetricLightVertex
            #pragma fragment FakeVolumetricLightFragment

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Includes/FakeVolumetricLightInput.hlsl"
            #include "Includes/FakeVolumetricLightPass.hlsl"

            ENDHLSL
        }
    }
}
