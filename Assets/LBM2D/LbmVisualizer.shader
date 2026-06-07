Shader "Custom/LbmVisualizer"
{
    Properties
    {
        _MainTex ("LBM Result Texture", 2D) = "white" {}
        _VelocityScale ("Velocity Scale", Range(1, 100)) = 20.0
        _DensityScale ("Density Scale", Range(0, 10)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "PreviewType"="Plane" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _VelocityScale;
            float _DensityScale;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 GetHeatMapColor(float value)
            {
                value = clamp(value, 0.0, 1.0);
                float r = smoothstep(0.4, 0.7, value);
                float g = smoothstep(0.0, 0.4, value) - smoothstep(0.6, 0.9, value);
                float b = 1.0 - smoothstep(0.0, 0.5, value);
                return fixed4(r, g, b, 1.0);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float4 data = tex2D(_MainTex, i.uv);
                
                // Compute ShaderでAlpha=0にされた場所は「壁」としてグレー描画
                if (data.a < 0.5) {
                    return fixed4(0.5, 0.5, 0.5, 1.0);
                }

                float2 velocity = data.xy;
                float density = data.z;

                float speed = length(velocity) * _VelocityScale;
                fixed4 col = GetHeatMapColor(speed);

                float pressureChange = abs(density - 1.0) * _DensityScale;
                col.rgb += pressureChange;

                return col;
            }
            ENDCG
        }
    }
}