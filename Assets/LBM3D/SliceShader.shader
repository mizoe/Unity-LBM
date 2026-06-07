Shader "Custom/CFD_Slice_Contour"
{
    Properties {
        _VelocityField ("Velocity Field (3D)", 3D) = "" {}
        _MaxVal ("Max Value", Float) = 0.2
    }
    SubShader {
        Tags { "RenderType"="Opaque" }
        Cull Off
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float3 texcoord : TEXCOORD0;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            sampler3D _VelocityField;
            float3 _DomainOrigin;
            float3 _DomainSize;
            float _MaxVal;
            float _Uinlet;
            int _Mode;

            v2f vert (appdata v) {
                v2f o;
                // ★修正：UnityObjectToClipPosを使用
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // ジェットカラーマップ（青→緑→黄→赤）
            float3 getJetColor(float v) {
                v = clamp(v, 0, 1);
                float3 c = float3(0,0,0);
                c.r = clamp(min(4.0 * v - 1.5, -4.0 * v + 4.5), 0.0, 1.0);
                c.g = clamp(min(4.0 * v - 0.5, -4.0 * v + 3.5), 0.0, 1.0);
                c.b = clamp(min(4.0 * v + 0.5, -4.0 * v + 2.5), 0.0, 1.0);
                return c;
            }

            fixed4 frag (v2f i) : SV_Target {
                // ワールド座標からLBMテクスチャのUVW座標(0~1)を計算
                float3 uvw = (i.worldPos - _DomainOrigin) / _DomainSize;

                // 範囲外ならグレー
                if(any(uvw < 0) || any(uvw > 1)) return fixed4(0.2, 0.2, 0.2, 1);

                float4 data = tex3D(_VelocityField, uvw);
                float3 u_vec = data.xyz;
                float rho = data.w;

                float val = 0;
                
                // モード別の計算式
                if (_Mode == 0) { // Cd_induced (誘導抵抗成分: vとwのエネルギー)
                    val = (u_vec.y * u_vec.y + u_vec.z * u_vec.z) / (_Uinlet * _Uinlet);
                }
                else if (_Mode == 1) { // Cp (圧力係数)
                    // LBMでは圧力P = rho/3。 Cp = (P - Pref) / (0.5 * rho * U^2)
                    val = (rho - 1.0) / (1.5 * _Uinlet * _Uinlet);
                    val = val * 0.5 + 0.5; // -1~1を0~1に変換
                }
                else if (_Mode == 2) { // Cpt (全圧係数)
                    float Cp = (rho - 1.0) / (1.5 * _Uinlet * _Uinlet);
                    val = Cp + dot(u_vec, u_vec) / (_Uinlet * _Uinlet);
                    val = val * 0.5 + 0.5;
                }
                else if (_Mode == 3) val = abs(u_vec.x) / _MaxVal; // u成分
                else if (_Mode == 4) val = abs(u_vec.y) / _MaxVal; // v成分
                else if (_Mode == 5) val = abs(u_vec.z) / _MaxVal; // w成分

                return fixed4(getJetColor(val), 1.0);
            }
            ENDCG
        }
    }
}