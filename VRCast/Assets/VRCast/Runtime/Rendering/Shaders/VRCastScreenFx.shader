Shader "Hidden/VRCast/ScreenFx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: SSAO（深度から。RGB のみ暗く、alpha はそのまま）
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;
            float4 _MainTex_TexelSize;
            float _SsaoIntensity;
            float _SsaoRadius;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float SampleDepth(float2 uv)
            {
                return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv);
                if (src.a < 0.01 || _SsaoIntensity < 1e-4)
                {
                    return src;
                }

                float depth = SampleDepth(i.uv);
                if (depth > 80.0)
                {
                    return src;
                }

                // 近い遮蔽（首元・あご下・しわ）を優先して暗くする
                float radius = lerp(0.35, 2.0, saturate(_SsaoRadius));
                float range = lerp(0.08, 0.45, saturate(_SsaoRadius));
                float ao = 0.0;
                float weightSum = 0.0;
                static const float2 dirs[8] = {
                    float2(1, 0), float2(-1, 0), float2(0, 1), float2(0, -1),
                    float2(0.707, 0.707), float2(-0.707, 0.707),
                    float2(0.707, -0.707), float2(-0.707, -0.707)
                };
                [unroll]
                for (int s = 0; s < 8; s++)
                {
                    float2 uv = i.uv + dirs[s] * _MainTex_TexelSize.xy * radius * 12.0;
                    float sampleDepth = SampleDepth(uv);
                    // 周囲が手前（近い）ならくぼみ側＝遮蔽
                    float delta = depth - sampleDepth;
                    float occ = saturate(delta / max(range, 1e-3));
                    float falloff = saturate(1.0 - abs(delta) / max(range * 2.5, 1e-3));
                    ao += occ * falloff;
                    weightSum += falloff;
                }

                float occlusion = saturate(ao / max(weightSum, 1e-4));
                float shade = saturate(1.0 - occlusion * _SsaoIntensity);
                return fixed4(src.rgb * shade, src.a);
            }
            ENDCG
        }

        // 1: 画面空間リム（深度法線の輪郭ハイライト。透過は汚さない）
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;
            float4 _MainTex_TexelSize;
            float _RimIntensity;
            float _RimPower;
            float4 _RimColor;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float SampleDepth(float2 uv)
            {
                return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv);
                if (src.a < 0.01 || _RimIntensity < 1e-4)
                {
                    return src;
                }

                float depth = SampleDepth(i.uv);
                if (depth > 80.0)
                {
                    return src;
                }

                float2 texel = _MainTex_TexelSize.xy;
                float ddx = SampleDepth(i.uv + float2(texel.x, 0)) - depth;
                float ddy = SampleDepth(i.uv + float2(0, texel.y)) - depth;
                float3 normal = normalize(float3(-ddx, -ddy, depth * 0.02 + 1e-3));
                // カメラ向き（ビュー空間 +Z）との角度でリム
                float ndotv = saturate(normal.z);
                float rim = pow(1.0 - ndotv, max(_RimPower, 0.5));
                // シルエット付近（周囲との深度差）も少し足す
                float edge = saturate(abs(ddx) + abs(ddy)) * 2.0;
                rim = saturate(rim + edge * 0.15);
                float3 add = _RimColor.rgb * rim * _RimIntensity * src.a;
                return fixed4(src.rgb + add, src.a);
            }
            ENDCG
        }

        // 2: 色調補正 + シャープ + ビネット（alpha 維持）
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Exposure;
            float _Contrast;
            float _Saturation;
            float _Temperature;
            float _Tint;
            float _Sharpen;
            float _Vignette;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float3 ApplyTemperatureTint(float3 c, float temperature, float tint)
            {
                // temperature: -1 暖色〜 +1 寒色、tint: -1 緑〜 +1 マゼンタ
                c.r += temperature * 0.08;
                c.b -= temperature * 0.08;
                c.g += tint * 0.06;
                c.r -= tint * 0.03;
                c.b -= tint * 0.03;
                return c;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv);
                float3 color = src.rgb;

                // シャープ（不透明画素のみ）
                if (_Sharpen > 1e-4 && src.a > 0.01)
                {
                    float2 t = _MainTex_TexelSize.xy;
                    float3 blur = tex2D(_MainTex, i.uv + float2(t.x, 0)).rgb
                        + tex2D(_MainTex, i.uv - float2(t.x, 0)).rgb
                        + tex2D(_MainTex, i.uv + float2(0, t.y)).rgb
                        + tex2D(_MainTex, i.uv - float2(0, t.y)).rgb;
                    blur *= 0.25;
                    color = lerp(color, color + (color - blur), _Sharpen);
                }

                // 露出（EV 風）
                color *= pow(2.0, _Exposure);

                // コントラスト（中間灰 0.5 基準）
                color = (color - 0.5) * _Contrast + 0.5;

                // 彩度
                float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
                color = lerp(luma.xxx, color, _Saturation);

                color = ApplyTemperatureTint(color, _Temperature, _Tint);

                // ビネット（透過部分は弱く。RGB のみ）
                if (_Vignette > 1e-4)
                {
                    float2 d = i.uv * 2.0 - 1.0;
                    float v = saturate(1.0 - dot(d, d) * 0.55);
                    v = lerp(1.0, v, _Vignette);
                    color *= lerp(1.0, v, src.a);
                }

                return fixed4(max(color, 0), src.a);
            }
            ENDCG
        }
    }
}
