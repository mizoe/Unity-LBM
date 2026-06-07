Shader "Custom/LbmParticleVisualizer"
{
    Properties
    {
        _ParticleSize ("Line Width", Float) = 0.03
        _Color ("Base Color", Color) = (0.5, 0.8, 1.0, 1.0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "UnityCG.cginc"

            struct Particle {
                float3 pos0, pos1, pos2, pos3, pos4;
                float3 velocity;
                float life;
            };

            StructuredBuffer<Particle> particles;
            float3 _GridSize;
            float3 _DomainSize;
            float3 _Origin;
            
            float _ParticleSize;
            float4 _Color;

            struct v2f {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            v2f vert (uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                v2f o;
                Particle p = particles[instanceID];

                // 24頂点から、自分が担当する関節(0~3)と四角形の頂点(0~5)を割り出す
                int segment = vertexID / 6;
                int quadVert = vertexID % 6;

                float3 localA, localB;
                if (segment == 0)      { localA = p.pos0; localB = p.pos1; }
                else if (segment == 1) { localA = p.pos1; localB = p.pos2; }
                else if (segment == 2) { localA = p.pos2; localB = p.pos3; }
                else                   { localA = p.pos3; localB = p.pos4; }

                float3 worldA = _Origin + (localA / _GridSize) * _DomainSize;
                float3 worldB = _Origin + (localB / _GridSize) * _DomainSize;

                float3 viewA = UnityObjectToViewPos(float4(worldA, 1.0));
                float3 viewB = UnityObjectToViewPos(float4(worldB, 1.0));

                float2 dir = viewB.xy - viewA.xy;
                float len = length(dir);

                // まだ履歴が溜まっていない（関節が重なっている）場合は描画しない
                if (len < 0.0001 || p.life <= 0) {
                    o.pos = float4(0,0,0,0); o.color = float4(0,0,0,0); o.uv = float2(0,0); return o;
                }

                dir /= len;
                float2 normal = float2(-dir.y, dir.x);
                float width = _ParticleSize * 0.5;

                // 頂点の配置 (AとBを結ぶ太さのあるリボン)
                float3 viewPos = (quadVert == 0 || quadVert == 1 || quadVert == 3) ? viewA : viewB;
                float2 uv;
                
                if (quadVert == 0) { viewPos.xy += normal * width; uv = float2(0, segment/4.0); }
                if (quadVert == 1) { viewPos.xy -= normal * width; uv = float2(1, segment/4.0); }
                if (quadVert == 2) { viewPos.xy += normal * width; uv = float2(0, (segment+1)/4.0); }
                if (quadVert == 3) { viewPos.xy -= normal * width; uv = float2(1, segment/4.0); }
                if (quadVert == 4) { viewPos.xy -= normal * width; uv = float2(1, (segment+1)/4.0); }
                if (quadVert == 5) { viewPos.xy += normal * width; uv = float2(0, (segment+1)/4.0); }

                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                o.uv = uv;

                float speed = length(p.velocity);
                o.color = lerp(_Color, float4(1, 1, 1, 1), saturate(speed * 3.0));

                // 尻尾にいくほど透明になるグラデーション
                float alpha = 1.0 - (segment / 4.0);
                o.color.a *= saturate(alpha * p.life);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}