Shader "Custom/RainLensDistortion"
{
    Properties
    {
        _RainIntensity ("Rain Intensity", Range(0,1)) = 0
        _DistortionStrength ("Distortion Strength", Range(0,1)) = 0.05
        _DropletTex ("Droplet Texture", 2D) = "white" {}
        _AnimationSpeed ("Animation Speed", Float) = 2
        _Darkness ("Darkness", Range(0,1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "RainLensDistortion"

            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);

            TEXTURE2D(_DropletTex);
            SAMPLER(sampler_DropletTex);

            float _RainIntensity;
            float _DistortionStrength;
            float _AnimationSpeed;
            float _Darkness;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
               return float4(1,0,0,1);
            }
            ENDHLSL
        }
    }
}
