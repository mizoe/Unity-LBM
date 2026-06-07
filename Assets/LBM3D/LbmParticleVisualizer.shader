Shader "Custom/LbmParticleVisualizer"
{
    Properties
    {
        _VelocityScale ("Velocity Scale (Color)", Range(0.1, 50)) = 10.0
    }
    SubShader
    {
        // 加算合成（光るエフェクト）
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0 // ComputeBufferを使うために必須

            #include "UnityCG.cginc"

            struct Particle {
                float3 position;
                float3 velocity;
                float life;
            };

            // C#から渡されるパーティクルバッファ
            StructuredBuffer<Particle> particlesBuffer;
            float _VelocityScale;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            // 速度ベクトルをヒートマップ色に変換
            fixed4 GetHeatMapColor(float value)
            {
                value = clamp(value, 0.0, 1.0);
                float r = smoothstep(0.4, 0.7, value);
                float g = smoothstep(0.0, 0.4, value) - smoothstep(0.6, 0.9, value);
                float b = 1.0 - smoothstep(0.0, 0.5, value);
                return fixed4(r, g, b, 1.0);
            }

            // ポリゴンを使わず、バッファのインデックス(SV_VertexID)だけで直接座標を計算
            v2f vert (uint vertexID : SV_VertexID)
            {
                v2f o;
                Particle p = particlesBuffer[vertexID];

                // パーティクルのローカル座標をクリップ空間に変換
                o.pos = UnityObjectToClipPos(float4(p.position, 1.0));
                
                float speed = length(p.velocity) * _VelocityScale;
                fixed4 heatColor = GetHeatMapColor(speed);
                
                // 生まれた直後と消える直前をフェードアウトさせてチラつきを防ぐ
                float alpha = smoothstep(0.0, 0.2, p.life) * smoothstep(0.0, 0.5, 4.0 - p.life);
                
                // 加算合成用にAlphaをRGBに乗算して出力（さらに全体の明るさを調整）
                o.color = float4(heatColor.rgb * alpha * 0.5, alpha);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 点(Point)としてそのまま色を出力
                return i.color;
            }
            ENDCG
        }
    }
}