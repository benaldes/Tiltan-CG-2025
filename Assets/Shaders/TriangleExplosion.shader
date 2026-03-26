Shader "Custom/TriangleExplosion"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1.0, 0.53, 0.25, 1.0)
        _StatelessSpeed ("Stateless Speed", Float) = 2.25
        _StatelessDistance ("Stateless Distance", Float) = 1.75
        _ExplosionMode ("Explosion Mode", Float) = 0
        _TriangleCount ("Triangle Count", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Geometry"
            "RenderType"="Opaque"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma require geometry
            #pragma vertex Vert
            #pragma geometry Geo
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct TriangleState
            {
                float3 offset;
                float3 velocity;
                float lifetime;
                uint active;
            };

            StructuredBuffer<TriangleState> _TriangleStates;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _StatelessSpeed;
                float _StatelessDistance;
                int _ExplosionMode;
                int _TriangleCount;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 triangleData : TEXCOORD1;
            };

            struct VaryingsToGeo
            {
                float3 positionOS : TEXCOORD0;
                float3 normalOS : TEXCOORD1;
                float triangleId : TEXCOORD2;
            };

            struct VaryingsToFrag
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            VaryingsToGeo Vert(Attributes input)
            {
                VaryingsToGeo output;
                output.positionOS = input.positionOS;
                output.normalOS = input.normalOS;
                output.triangleId = input.triangleData.x;
                return output;
            }

            [maxvertexcount(3)]
            void Geo(triangle VaryingsToGeo input[3], inout TriangleStream<VaryingsToFrag> triStream)
            {
                float3 p0OS = input[0].positionOS;
                float3 p1OS = input[1].positionOS;
                float3 p2OS = input[2].positionOS;

                float3 faceNormalOS = cross(p1OS - p0OS, p2OS - p0OS);
                float normalLengthSq = dot(faceNormalOS, faceNormalOS);
                if (normalLengthSq < 0.000001)
                {
                    return;
                }

                faceNormalOS *= rsqrt(normalLengthSq);

                uint triangleId = (uint)round(input[0].triangleId);
                float3 offsetOS = 0.0;

                if (_ExplosionMode == 0)
                {
                    float pulse = abs(sin(_Time.y * _StatelessSpeed));
                    offsetOS = faceNormalOS * (_StatelessDistance * pulse);
                }
                else
                {
                    if (triangleId >= (uint)_TriangleCount)
                    {
                        return;
                    }

                    TriangleState state = _TriangleStates[triangleId];
                    if (state.active == 0u)
                    {
                        return;
                    }

                    offsetOS = state.offset;
                }

                float3 faceNormalWS = SafeNormalize(TransformObjectToWorldDir(faceNormalOS));

                for (int vertexIndex = 0; vertexIndex < 3; vertexIndex++)
                {
                    float3 displacedPositionOS = input[vertexIndex].positionOS + offsetOS;
                    VertexPositionInputs positionInputs = GetVertexPositionInputs(displacedPositionOS);

                    VaryingsToFrag output;
                    output.positionCS = positionInputs.positionCS;
                    output.normalWS = faceNormalWS;
                    triStream.Append(output);
                }

                triStream.RestartStrip();
            }

            half4 Frag(VaryingsToFrag input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 litColor = _BaseColor.rgb * (ambient + mainLight.color * ndotl);
                return half4(litColor, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
