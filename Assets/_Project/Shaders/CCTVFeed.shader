// Look of a cheap low-light subsea CCTV camera, for UI RawImages showing a camera feed:
// barrel lens distortion, colour fringing, gain + sensor noise, desaturated tint, scanlines,
// rolling interference, vignette, and a full "static" mode used when switching cameras.
Shader "TheDeep/CCTVFeed"
{
    Properties
    {
        [PerRendererData] _MainTex ("Feed", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FeedTint ("Feed Tint", Color) = (0.82, 1, 0.95, 1)
        _Gain ("Gain", Range(0.5, 4)) = 2.3
        _Saturation ("Saturation", Range(0, 1)) = 0.4
        _Noise ("Sensor Noise", Range(0, 0.5)) = 0.045
        _Scanlines ("Scanlines", Range(0, 1)) = 0.22
        _Distortion ("Lens Distortion", Range(0, 0.5)) = 0.12
        _Aberration ("Colour Fringing", Range(0, 0.02)) = 0.004
        _Vignette ("Vignette", Range(0, 2)) = 0.9
        _Static ("Static", Range(0, 1)) = 0

        // Standard UI masking support.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _FeedTint;
            float _Gain, _Saturation, _Noise, _Scanlines, _Distortion, _Aberration, _Vignette, _Static;
            float4 _ClipRect;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 worldPos : TEXCOORD1; fixed4 color : COLOR; };

            // Sin-free hash (Dave Hoskins' hash12). A frac(sin(x) * big) hash turns into stripes or freezes
            // once its input grows large, and _Time keeps growing all session.
            float Hash(float2 p)
            {
                float3 p3 = frac(p.xyx * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y;
                float2 lines = _MainTex_TexelSize.zw;

                // Barrel distortion from a wide-angle lens behind a domed port.
                float2 c = i.uv - 0.5;
                float r2 = dot(c, c);
                float2 uv = 0.5 + c * (1.0 + _Distortion * r2 * 4.0) / (1.0 + _Distortion);

                // Occasional horizontal line jitter + a slow rolling interference band.
                // Frame counters wrap (at a prime) so the hash inputs stay small in long sessions.
                float row = floor(uv.y * lines.y);
                float jitter = (Hash(float2(row, fmod(floor(t * 24.0), 991.0))) - 0.5) * 0.004;
                float band = smoothstep(0.03, 0.0, abs(frac(uv.y - frac(t * 0.06)) - 0.5));
                uv.x += jitter + band * 0.006;

                float3 col;
                col.r = tex2D(_MainTex, uv + float2(_Aberration, 0)).r;
                col.g = tex2D(_MainTex, uv).g;
                col.b = tex2D(_MainTex, uv - float2(_Aberration, 0)).b;

                // Low-light gain, washed-out colour, cold tint.
                col *= _Gain;
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(luma.xxx, col, _Saturation) * _FeedTint.rgb;

                // Sensor noise (stronger in the dark) and scanlines.
                float n = Hash(floor(uv * lines) + frac(t * 17.13) * 311.0) - 0.5;
                col += n * _Noise * (1.0 - 0.6 * saturate(luma));
                col *= 1.0 - _Scanlines * (0.5 + 0.5 * sin(uv.y * lines.y * 3.14159 * 2.0));
                col += band * 0.04;

                // Vignette, and black outside the distorted image.
                col *= saturate(1.0 - r2 * _Vignette * 1.6);
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                col *= inside;

                // Full static while switching cameras / signal lost.
                float snowFrame = fmod(floor(t * 60.0), 997.0);
                float snow = Hash(floor(i.uv * lines) + snowFrame * 7.31);
                col = lerp(col, snow.xxx * 0.85, _Static);

                fixed4 result = fixed4(saturate(col), 1) * i.color;
                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
