Shader "Custom/TriangleSpikeGeometry"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _SpikeHeight ("Spike Height", Float) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2g
            {
                float3 objPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
            };

            struct g2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
            };

            float4 _Color;
            float _SpikeHeight;

            v2g vert (appdata v)
            {
                v2g o;
                o.objPos = v.vertex.xyz;
                o.normal = v.normal;
                return o;
            }

            [maxvertexcount(9)]
            void geom(triangle v2g i[3], inout TriangleStream<g2f> stream)
            {
                float3 p0 = i[0].objPos;
                float3 p1 = i[1].objPos;
                float3 p2 = i[2].objPos;

                float3 normal = normalize(cross(p1 - p0, p2 - p0));

                float3 center = (p0 + p1 + p2) / 3.0;
                float3 tip = center + normal * _SpikeHeight;

                g2f o;

                float3 triNormals[3] =
                {
                    normalize(cross(p1 - p0, tip - p0)),
                    normalize(cross(p2 - p1, tip - p1)),
                    normalize(cross(p0 - p2, tip - p2))
                };

                float3 base[3] = { p0, p1, p2 };

                for (int k = 0; k < 3; k++)
                {
                    o.normal = triNormals[k];

                    o.pos = UnityObjectToClipPos(float4(base[k],1));
                    stream.Append(o);

                    o.pos = UnityObjectToClipPos(float4(base[(k+1)%3],1));
                    stream.Append(o);

                    o.pos = UnityObjectToClipPos(float4(tip,1));
                    stream.Append(o);

                    stream.RestartStrip();
                }
            }

            fixed4 frag (g2f i) : SV_Target
            {
                float3 lightDir = normalize(float3(0.4,1,0.2));
                float light = saturate(dot(i.normal, lightDir));
                return _Color * (light * 0.7 + 0.3);
            }

            ENDHLSL
        }
    }
}
