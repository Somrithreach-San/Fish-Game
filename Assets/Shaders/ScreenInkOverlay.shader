// ScreenInkOverlay.shader
// Custom UI shader for the player's Cuttlefish ink blinding effect.
// Renders rich, viscous liquid squid ink with glossy wet rim highlights,
// organic edge distortion, and fluid ocean wash-away dissolve.

Shader "Custom/ScreenInkOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _InkColor       ("Ink Core Color", Color)       = (0.05, 0.03, 0.09, 0.96)
        _RimColor       ("Wet Rim / Sheen Color", Color)= (0.28, 0.20, 0.42, 0.85)
        _MurkColor      ("Ambient Water Murk", Color)   = (0.04, 0.07, 0.12, 0.40)
        _Dissolve       ("Dissolve Progress", Range(0, 1)) = 0.0
        _DripOffset     ("Drip Y Offset", Float)        = 0.0
        _WaveDistort    ("Wave Distortion", Range(0, 0.05)) = 0.012
        _Alpha          ("Overall Alpha", Range(0, 1))  = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

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
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            fixed4 _InkColor;
            fixed4 _RimColor;
            fixed4 _MurkColor;
            float _Dissolve;
            float _DripOffset;
            float _WaveDistort;
            float _Alpha;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // Subtle fluid wave ripple on the ink edges
                float waveX = sin(uv.y * 35.0 + _Time.y * 2.5) * _WaveDistort;
                float waveY = cos(uv.x * 30.0 + _Time.y * 2.0) * _WaveDistort * 0.6;
                float2 distortedUV = uv + float2(waveX, waveY - _DripOffset);

                fixed4 mask = tex2D(_MainTex, distortedUV);

                // Multi-channel mask unpack:
                // R: Base ink thickness & density
                // G: Wet liquid rim & specular highlights
                // B: Secondary splatter & micro-droplets
                // A: Silhouette coverage
                float inkDensity = mask.r;
                float wetSheen = mask.g;
                float droplets = mask.b;
                float rawAlpha = mask.a;

                // Fluid dissolve thresholding (washes out organically from thin edges to thick cores)
                float dissolveThreshold = _Dissolve * 1.15;
                float effectiveDensity = saturate((inkDensity - dissolveThreshold) / max(0.01, 1.0 - dissolveThreshold * 0.7));

                if (effectiveDensity <= 0.005)
                {
                    discard;
                }

                // Blend ink core with wet specular rim
                fixed3 inkRgb = lerp(_InkColor.rgb, _RimColor.rgb, wetSheen * 0.85);

                // Add subtle glossy highlight gleam along top-left edges
                float specularGleam = saturate(wetSheen * (1.0 - _Dissolve) * 1.2);
                inkRgb += fixed3(0.15, 0.12, 0.22) * specularGleam;

                // Ambient oceanic murk blend in thin areas
                inkRgb = lerp(_MurkColor.rgb, inkRgb, saturate(effectiveDensity * 1.5));

                float finalAlpha = saturate(effectiveDensity * rawAlpha * _Alpha * _InkColor.a);

                return fixed4(inkRgb, finalAlpha);
            }
            ENDCG
        }
    }
}
