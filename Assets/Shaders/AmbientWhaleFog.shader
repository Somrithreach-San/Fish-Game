Shader "Custom/AmbientWhaleFog"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Master Tint & Alpha", Color) = (1.0, 1.0, 1.0, 0.72)
        _ShadowColor ("Dorsal / Deep Shadow Color", Color) = (0.015, 0.12, 0.28, 1.0)
        _MidColor ("Mid-Body Ocean Blue", Color) = (0.04, 0.28, 0.58, 1.0)
        _HighlightColor ("Fin & Belly Scattering", Color) = (0.16, 0.52, 0.85, 1.0)
        _WaterFogColor ("Deep Water Ambient Fog", Color) = (0.02, 0.22, 0.50, 1.0)
        _FogBlend ("Water Column Fog Blend", Range(0, 1)) = 0.42
        _BlurRadius ("Atmospheric Gaussian Blur", Range(0.0005, 0.006)) = 0.0028
        _EdgeFeather ("Edge Softness / Diffusion", Range(0.5, 3.0)) = 1.4
        _WaveSpeed ("Swimming Flex Speed", Float) = 0.95
        _WaveFreq ("Swimming Flex Frequency", Float) = 0.18
        _WaveAmp ("Swimming Flex Amplitude", Float) = 0.28
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float3 localPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            fixed4 _ShadowColor;
            fixed4 _MidColor;
            fixed4 _HighlightColor;
            fixed4 _WaterFogColor;
            float _FogBlend;
            float _BlurRadius;
            float _EdgeFeather;
            float _WaveSpeed;
            float _WaveFreq;
            float _WaveAmp;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                float4 v = IN.vertex;

                // Organic traveling spine wave: displacement amplitude increases smoothly toward tail (X > 0)
                float tailFactor = saturate((v.x + 3.0) / 10.0);
                float wave = sin(_Time.y * _WaveSpeed + v.x * _WaveFreq) * (_WaveAmp * tailFactor * tailFactor);
                v.y += wave;

                OUT.vertex = UnityObjectToClipPos(v);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color = IN.color * _Color;
                OUT.localPos = v.xyz;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float b = _BlurRadius;

                // 13-tap Gaussian blur kernel for soft underwater dispersion
                fixed4 s0  = tex2D(_MainTex, uv);
                fixed4 s1  = tex2D(_MainTex, uv + float2( b,       0.0));
                fixed4 s2  = tex2D(_MainTex, uv - float2( b,       0.0));
                fixed4 s3  = tex2D(_MainTex, uv + float2( 0.0,     b));
                fixed4 s4  = tex2D(_MainTex, uv - float2( 0.0,     b));
                fixed4 s5  = tex2D(_MainTex, uv + float2( b * 0.7, b * 0.7));
                fixed4 s6  = tex2D(_MainTex, uv - float2( b * 0.7, b * 0.7));
                fixed4 s7  = tex2D(_MainTex, uv + float2(-b * 0.7, b * 0.7));
                fixed4 s8  = tex2D(_MainTex, uv + float2( b * 0.7,-b * 0.7));
                fixed4 s9  = tex2D(_MainTex, uv + float2( b * 1.5, 0.0));
                fixed4 s10 = tex2D(_MainTex, uv - float2( b * 1.5, 0.0));
                fixed4 s11 = tex2D(_MainTex, uv + float2( 0.0,     b * 1.5));
                fixed4 s12 = tex2D(_MainTex, uv - float2( 0.0,     b * 1.5));

                fixed4 sampleCol = s0 * 0.22 + (s1 + s2 + s3 + s4) * 0.12 + (s5 + s6 + s7 + s8) * 0.05 + (s9 + s10 + s11 + s12) * 0.025;

                // Alpha with soft Gaussian feathering
                float alpha = saturate(sampleCol.a * _EdgeFeather) * IN.color.a;

                // Calculate luminance from blurred texture details
                float lum = dot(sampleCol.rgb, float3(0.299, 0.587, 0.114));

                // 3-way color remapping to perfectly match the deep ocean water gradient:
                // Dark shadows -> _ShadowColor, Mid body -> _MidColor, Highlights (fin/throat) -> _HighlightColor
                fixed3 remappedColor;
                if (lum < 0.45)
                {
                    float t = lum / 0.45;
                    remappedColor = lerp(_ShadowColor.rgb, _MidColor.rgb, t);
                }
                else
                {
                    float t = (lum - 0.45) / 0.55;
                    remappedColor = lerp(_MidColor.rgb, _HighlightColor.rgb, t);
                }

                // Submerge inside the deep water column fog
                fixed3 finalRgb = lerp(remappedColor, _WaterFogColor.rgb, _FogBlend);

                return fixed4(finalRgb, alpha);
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
