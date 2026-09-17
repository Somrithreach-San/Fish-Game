// UnderwaterLaserBeam.shader
// Custom unlit shader used by RiverBoat laser LineRenderers.
// Two overlapping noise layers scroll at different speeds to mimic
// light refracting through moving water.  Edges are softened into
// a radial glow.  Blend mode is Additive so the beam appears to
// cast light rather than sit on top of geometry.
//
// Built-in Render Pipeline only.  If you move to URP, swap
//   #pragma surface ... for
//   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
// and change the pass to a URP Unlit pass.

Shader "Custom/UnderwaterLaserBeam"
{
    Properties
    {
        _CoreColor   ("Core Color",      Color)  = (1, 0.12, 0.08, 1)
        _GlowColor   ("Glow Color",      Color)  = (1, 0.04, 0.05, 0.4)
        _NoiseTex    ("Noise Texture",   2D)     = "white" {}

        // Scrolling speed for the two noise layers (x = layer-A, y = layer-B).
        // Negative values scroll in the opposite direction for that "crossing ripple" look.
        _NoiseSpeedA ("Noise Speed A",   Vector) = (0.6, 0.15, 0, 0)
        _NoiseSpeedB ("Noise Speed B",   Vector) = (-0.4, 0.22, 0, 0)

        // How much the noise modulates brightness (0 = solid, 1 = full variation)
        _NoiseStrength ("Noise Strength", Range(0,1)) = 0.38

        // Radial glow falloff — larger = softer edge (0 is the core, 0.5 is the edge)
        _GlowFalloff ("Glow Falloff",    Range(0.5, 8)) = 3.0

        // Overall brightness multiplier (raise >1 if you have HDR / Bloom)
        _Brightness  ("Brightness",      Range(0.5, 4)) = 1.4
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Blend One One           // Additive — looks like emitted light
            ZWrite Off
            Cull Off
            Lighting Off

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            // ── uniforms ──────────────────────────────────────────────────────
            fixed4  _CoreColor;
            fixed4  _GlowColor;
            sampler2D _NoiseTex;
            float4  _NoiseTex_ST;       // tiling / offset from material
            float4  _NoiseSpeedA;
            float4  _NoiseSpeedB;
            float   _NoiseStrength;
            float   _GlowFalloff;
            float   _Brightness;

            // ── vertex input / output ─────────────────────────────────────────
            struct appdata
            {
                float4 vertex   : POSITION;
                float2 uv       : TEXCOORD0;
                fixed4 color    : COLOR;      // per-vertex color from LineRenderer.startColor/endColor
            };

            struct v2f
            {
                float4 pos  : SV_POSITION;
                float2 uv   : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos   = UnityObjectToClipPos(v.vertex);
                o.uv    = TRANSFORM_TEX(v.uv, _NoiseTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ── noise layers ──────────────────────────────────────────────
                // UV x  → along the beam length (used for noise tiling)
                // UV y  → across beam width (0 = one edge, 1 = other edge)

                float2 uvA = i.uv + _NoiseSpeedA.xy * _Time.y;
                float2 uvB = i.uv + _NoiseSpeedB.xy * _Time.y;

                float noiseA = tex2D(_NoiseTex, uvA).r;
                float noiseB = tex2D(_NoiseTex, uvB).r;

                // Blend the two noise layers and remap to [-0.5, +0.5] range
                float noise = (noiseA * 0.6 + noiseB * 0.4) * 2.0 - 1.0;

                // ── radial glow falloff ───────────────────────────────────────
                // UV.y goes 0→1 across the beam width.
                // Center = 0.5.  We remap so 0 = edge, 1 = core.
                float crossNorm = abs(i.uv.y - 0.5) * 2.0;          // 0 at center, 1 at edge
                float glow      = pow(1.0 - crossNorm, _GlowFalloff); // smooth falloff

                // ── combine ───────────────────────────────────────────────────
                // Core: bright tinted color at beam center, amplified by noise ripple
                // Glow: softer tinted halo that falls off toward edges
                float noiseModulation = 1.0 + noise * _NoiseStrength;

                fixed4 core = _CoreColor * i.color * glow         * noiseModulation;
                fixed4 halo = _GlowColor * i.color * (1.0 - glow) * max(0.0, noiseModulation * 0.6);

                fixed4 col = (core + halo) * _Brightness;

                // Preserve the per-vertex alpha envelope from LineRenderer
                // (LineRenderer doesn't alpha-clip, but this lets startColor.a fade ends)
                col.a = i.color.a;
                return col;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
