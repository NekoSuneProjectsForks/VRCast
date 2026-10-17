Shader "Hidden/VRCast/FXAA"
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
            float4 _MainTex_TexelSize;

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

            float Luma(fixed3 c)
            {
                return dot(c, float3(0.299, 0.587, 0.114));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 texel = _MainTex_TexelSize.xy;
                fixed4 m = tex2D(_MainTex, i.uv);
                // 透過画素はそのまま（縁を汚さない）
                if (m.a < 0.01)
                {
                    return m;
                }

                fixed3 nw = tex2D(_MainTex, i.uv + float2(-texel.x, texel.y)).rgb;
                fixed3 ne = tex2D(_MainTex, i.uv + float2(texel.x, texel.y)).rgb;
                fixed3 sw = tex2D(_MainTex, i.uv + float2(-texel.x, -texel.y)).rgb;
                fixed3 se = tex2D(_MainTex, i.uv + float2(texel.x, -texel.y)).rgb;

                float lumaM = Luma(m.rgb);
                float lumaMin = min(lumaM, min(min(Luma(nw), Luma(ne)), min(Luma(sw), Luma(se))));
                float lumaMax = max(lumaM, max(max(Luma(nw), Luma(ne)), max(Luma(sw), Luma(se))));
                float2 dir = float2(
                    -((Luma(nw) + Luma(ne)) - (Luma(sw) + Luma(se))),
                    ((Luma(nw) + Luma(sw)) - (Luma(ne) + Luma(se))));
                float dirReduce = max((Luma(nw) + Luma(ne) + Luma(sw) + Luma(se)) * 0.03125, 0.0078125);
                float rcpDir = 1.0 / (min(abs(dir.x), abs(dir.y)) + dirReduce);
                dir = clamp(dir * rcpDir, -8.0, 8.0) * texel;

                fixed3 rgbA = 0.5 * (
                    tex2D(_MainTex, i.uv + dir * (1.0 / 3.0 - 0.5)).rgb +
                    tex2D(_MainTex, i.uv + dir * (2.0 / 3.0 - 0.5)).rgb);
                fixed3 rgbB = rgbA * 0.5 + 0.25 * (
                    tex2D(_MainTex, i.uv + dir * -0.5).rgb +
                    tex2D(_MainTex, i.uv + dir * 0.5).rgb);
                float lumaB = Luma(rgbB);
                fixed3 rgb = (lumaB < lumaMin || lumaB > lumaMax) ? rgbA : rgbB;
                return fixed4(rgb, m.a);
            }
            ENDCG
        }
    }
}
