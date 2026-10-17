Shader "Hidden/VRCast/ContactShadow"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _CameraDepthTexture;
            float4 _MainTex_TexelSize;
            float4 _LightDir; // xyz = ワールド光方向（光源→面の向き）、w = 柔らかさ
            float _ShadowStrength;
            float _Softness;

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
                if (src.a < 0.01)
                {
                    return src;
                }

                float depth = SampleDepth(i.uv);
                if (depth > 100.0)
                {
                    return src;
                }

                // ライト方向へ画面上で数ステップ辿り、手前に遮蔽があれば暗くする（PCSS 風のペンアンブラ）
                float2 lightUv = normalize(_LightDir.xy + float2(1e-5, 1e-5)) * _MainTex_TexelSize.xy;
                float penumbra = lerp(0.6, 2.8, saturate(_Softness));
                float occlusion = 0.0;
                float weightSum = 0.0;
                const int steps = 8;
                [unroll]
                for (int s = 1; s <= steps; s++)
                {
                    float t = (float)s / (float)steps;
                    float2 uv = i.uv + lightUv * (t * penumbra * 18.0);
                    float sampleDepth = SampleDepth(uv);
                    float blocker = saturate((depth - sampleDepth) * 4.0);
                    float w = 1.0 - t;
                    occlusion += blocker * w;
                    weightSum += w;
                }

                occlusion = saturate(occlusion / max(weightSum, 1e-4));
                // 距離に応じた柔らかさ（遠い遮蔽ほど薄い）
                float soft = lerp(1.0, 0.35, saturate(_Softness));
                float shade = saturate(1.0 - occlusion * _ShadowStrength * soft);
                return fixed4(src.rgb * shade, src.a);
            }
            ENDCG
        }
    }
}
