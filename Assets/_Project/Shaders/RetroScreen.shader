// Final screen pass for the pixelated look: reads the low-res camera image (drawn with point
// filtering by a RawImage) and reduces it to a limited colour palette with ordered (Bayer) dithering.
Shader "TheDeep/RetroScreen"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ColorLevels ("Colour Levels Per Channel", Float) = 32
        _DitherStrength ("Dither Strength", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _ColorLevels;
            float _DitherStrength;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            static const float Bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                // Quantise in gamma space so dark underwater scenes don't band into black.
                #ifndef UNITY_COLORSPACE_GAMMA
                c = LinearToGammaSpace(saturate(c));
                #endif
                int2 p = int2(floor(i.uv * _MainTex_TexelSize.zw)) % 4;
                float dither = (Bayer[p.y * 4 + p.x] + 0.5) / 16.0 - 0.5;
                float levels = max(_ColorLevels - 1, 1);
                c = saturate(floor(c * levels + 0.5 + dither * _DitherStrength) / levels);
                #ifndef UNITY_COLORSPACE_GAMMA
                c = GammaToLinearSpace(c);
                #endif
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
