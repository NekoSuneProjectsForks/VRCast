Shader "Hidden/VRCast/BloomTonemap"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // 0: 明るい部分だけ抽出（alpha は元のまま）
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Threshold;

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

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float lum = dot(c.rgb, float3(0.2126, 0.7152, 0.0722));
                float knee = max(lum - _Threshold, 0.0);
                float soft = knee * knee / max(_Threshold * 0.25 + 1e-4, 1e-4);
                float contrib = max(soft, lum - _Threshold) / max(lum, 1e-4);
                // 透過画素はブルームに入れない
                return fixed4(c.rgb * contrib * c.a, c.a);
            }
            ENDCG
        }

        // 1: ガウシアンぼかし（横）
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float2 _BlurDir;

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

            fixed4 frag(v2f i) : SV_Target
            {
                float2 step = _BlurDir * _MainTex_TexelSize.xy;
                fixed4 c = tex2D(_MainTex, i.uv) * 0.227027;
                c += tex2D(_MainTex, i.uv + step) * 0.316216;
                c += tex2D(_MainTex, i.uv - step) * 0.316216;
                c += tex2D(_MainTex, i.uv + step * 2.0) * 0.070270;
                c += tex2D(_MainTex, i.uv - step * 2.0) * 0.070270;
                return c;
            }
            ENDCG
        }

        // 2: 合成 + トーンマップ（出力 alpha = 入力 alpha）
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BloomTex;
            float _BloomIntensity;
            float _Tonemap;
            float _TonemapStrength;

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

            float3 AcesTonemap(float3 x)
            {
                const float a = 2.51;
                const float b = 0.03;
                const float c = 2.43;
                const float d = 0.59;
                const float e = 0.14;
                return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 src = tex2D(_MainTex, i.uv);
                fixed3 bloom = tex2D(_BloomTex, i.uv).rgb * _BloomIntensity;
                // ブルームは不透明側だけ足し、透過を汚さない
                float3 color = src.rgb + bloom * src.a;

                if (_Tonemap > 0.5)
                {
                    float3 mapped = AcesTonemap(color);
                    color = lerp(color, mapped, saturate(_TonemapStrength));
                }

                return fixed4(color, src.a);
            }
            ENDCG
        }
    }
}
