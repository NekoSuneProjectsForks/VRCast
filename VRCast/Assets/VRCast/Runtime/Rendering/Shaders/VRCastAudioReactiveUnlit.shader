Shader "VRCast/AudioReactive/Unlit"
{
    Properties
    {
        _MainTex ("Base Texture", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _GlowColor ("Music Glow", Color) = (0.2,1,0.4,1)
        [Enum(Bass,0,LowMids,1,HighMids,2,Treble,3)] _Band ("Audio Band", Float) = 0
        _Strength ("Glow Strength", Range(0,5)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _AudioTexture;
            float4 _BaseColor;
            float4 _GlowColor;
            float _Band;
            float _Strength;
            float _AudioLinkEnabled;

            struct Input
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Interpolated
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Interpolated vert(Input v)
            {
                Interpolated o;
                o.position = UnityObjectToClipPos(v.position);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(Interpolated i) : SV_Target
            {
                fixed4 albedo = tex2D(_MainTex, i.uv) * _BaseColor;
                // Row center of the 128x64 global texture: y=0..3 are the
                // four AudioLink-inspired bands. Pixel 0 is the latest value.
                float2 audioUv = float2(0.5 / 128.0, (clamp(_Band, 0.0, 3.0) + 0.5) / 64.0);
                float amplitude = tex2D(_AudioTexture, audioUv).r * saturate(_AudioLinkEnabled);
                albedo.rgb += _GlowColor.rgb * (_GlowColor.a * amplitude * _Strength);
                return albedo;
            }
            ENDCG
        }
    }
    Fallback "Unlit/Texture"
}
