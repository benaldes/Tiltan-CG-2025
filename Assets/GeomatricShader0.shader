Shader "Custom/WireframeTransparentPush"
{
    Properties
    {
        _LineColor ("Line Color", Color) = (1,1,1,1)
        _Thickness ("Line Thickness", Float) = 1.0
        _PushAmount ("Vertex Push Amount", Float) = 0.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
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
            };

            struct v2g
            {
                float3 objPos : TEXCOORD0;
            };

            struct g2f
            {
                float4 pos  : SV_POSITION;
                float3 bary : TEXCOORD0;
            };

            float4 _LineColor;
            float _Thickness;
            float _PushAmount;

            v2g vert (appdata v)
            {
                v2g o;
                o.objPos = v.vertex.xyz;
                return o;
            }

            [maxvertexcount(3)]
            void geom(triangle v2g i[3], inout TriangleStream<g2f> stream)
            {
                float3 p0 = i[0].objPos;
                float3 p1 = i[1].objPos;
                float3 p2 = i[2].objPos;

                float3 normal = normalize(cross(p1 - p0, p2 - p0));

                float3 bary[3] =
                {
                    float3(1,0,0),
                    float3(0,1,0),
                    float3(0,0,1)
                };

                for (int k = 0; k < 3; k++)
                {
                    g2f o;

                    float3 pushedPos = i[k].objPos + normal * _PushAmount;
                    o.pos = UnityObjectToClipPos(float4(pushedPos, 1));

                    o.bary = bary[k];
                    stream.Append(o);
                }
            }

            fixed4 frag (g2f i) : SV_Target
            {
                float3 d = fwidth(i.bary);
                float3 a = smoothstep(0, d * _Thickness, i.bary);
                float edge = 1.0 - min(min(a.x, a.y), a.z);

                return float4(_LineColor.rgb, edge * _LineColor.a);
            }

            ENDHLSL
        }
    }
}
