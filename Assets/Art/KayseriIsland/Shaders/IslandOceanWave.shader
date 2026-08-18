// Open liquid: tar, acid, rust slurry, mercury, algal bloom.
//
// This replaces a noise-swirl version that read as a lava lamp. The fix is not more noise, it is
// the RIGHT primitive: real open water is a sum of directional wave trains, not a field of blobs.
// Four of them run here at non-harmonic frequencies and four angles, so the sum never visibly
// repeats, and everything the eye uses to read a liquid comes out of that one height field:
//
//   crests    four noise octaves, each drifting its own way. Summed sine trains were tried first
//             and are a dead end: any fixed set of directions interferes into a periodic lattice,
//             and nothing removes it - displacing the coordinate bends the grid without
//             decorrelating it, and phase noise strong enough to break it dissolves the waves too.
//   colour    a NARROW range - deep body to slightly lighter crest. This is the whole difference
//             between realistic and cartoon: real water is basically one colour with light on it,
//             so the wide three-colour ramp that made the old version look painted is gone.
//   whitecaps only at the very top of the tallest crests, thin, and never white unless the liquid
//             actually is (tar breaks grey-purple, rust breaks ochre).
//   sky       upward-facing water picks up sky colour. Cheap, and it is most of what separates
//             mercury from grey paint.
//   glitter   two specular lobes off the wave slope - a broad sheen plus a very tight sparkle.
//
// Lighting is deliberately SMOOTH here, not the island's toon bands: the ask was realistic. It
// still reads the same night globals, so the sea goes down with the sun.
//
// Cost is four ValueNoise calls for the bend, 4 sin + 4 cos issued as vector ops, and two pow
// for the specular lobes. No branches, no texture fetch. SRP Batcher compatible.
//
// No ShadowCaster pass on purpose: this is a horizontal plane at sea level with the whole island
// standing above it.
Shader "Kayseri/IslandOceanWave"
{
    Properties
    {
        [Header(Colour)]
        _DeepColor("Body", Color) = (0.02,0.09,0.14,1)
        _ShallowColor("Crest", Color) = (0.06,0.20,0.27,1)
        _FoamColor("Whitecap", Color) = (0.85,0.92,0.95,1)
        // How far toward the crest colour the tall water actually gets. Keep this and the two
        // colours close together - separation is what made the old pass look like poster paint.
        _Depth("Crest Blend", Range(0,1)) = 1.0
        _FoamLevel("Whitecap Height", Range(0,1)) = 0.80
        _FoamWidth("Whitecap Softness", Range(0.01,0.5)) = 0.15
        _FoamAmount("Whitecap Amount", Range(0,1)) = 0.65

        [Header(Waves)]
        // 1/_Scale is the wavelength of the longest train in world units, so 0.03 is a 33-unit swell.
        _Scale("Wave Scale", Range(0.004,0.3)) = 0.035
        _WaveSpeed("Wave Speed", Range(0,4)) = 0.45
        // Crest sharpening. 0 is a plain sine - a smooth, viscous liquid like tar. 1 is w^4, narrow
        // peaks and wide troughs, which is what open water actually looks like.
        _Choppy("Choppiness", Range(0,1)) = 0.5
        // How far each train's fronts are bent off straight, in wave-coordinate units. 0 is four
        // straight trains, which interfere into plaid; a couple of units curves them enough that
        // the regularity is gone but the crest lines survive.
        _Bend("Front Bend", Range(0,8)) = 3.0
        _BendScale("Bend Scale", Range(0.01,1)) = 0.08
        // Unit vector the whole wave set travels along; the four trains fan out around it.
        _FlowDir("Wave Direction", Vector) = (1,0.3,0,0)

        [Header(Surface)]
        _NormalStrength("Slope", Range(0,6)) = 1.3
        _Smoothness("Smoothness", Range(0,1)) = 0.85
        _SpecularStrength("Sheen", Range(0,6)) = 1.2
        _GlitterStrength("Sun Glitter", Range(0,8)) = 2.0
        _SpecularTint("Specular Tint", Color) = (1,1,1,1)
        // What the flat water reflects. Small amounts only - this is a tint, not a mirror.
        _SkyTint("Sky Tint", Color) = (0.42,0.55,0.70,1)
        _SkyAmount("Sky Amount", Range(0,1)) = 0.12

        [Header(Emission)]
        // For the two that genuinely luminesce. Low by day and carried by _EmissionNight after
        // dark, which is how a real bloom or a real acid reads - you see it at night.
        [HDR]_EmissionColor("Emission Colour", Color) = (0,0,0,0)
        _Emission("Emission Strength", Range(0,4)) = 0
        _EmissionNight("Night Boost", Range(0,4)) = 0

        [Header(Lighting)]
        _ShadowTint("Shadow Tint", Color) = (0.30,0.38,0.55,1)
        _AmbientAmount("Ambient", Range(0,2)) = 0.55
        _Wrap("Light Wrap", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor;
            half4 _ShallowColor;
            half4 _FoamColor;
            half _Depth;
            half _FoamLevel;
            half _FoamWidth;
            half _FoamAmount;
            half _Scale;
            half _WaveSpeed;
            half _Choppy;
            half _Bend;
            half _BendScale;
            half4 _FlowDir;
            half _NormalStrength;
            half _Smoothness;
            half _SpecularStrength;
            half _GlitterStrength;
            half4 _SpecularTint;
            half4 _SkyTint;
            half _SkyAmount;
            half4 _EmissionColor;
            half _Emission;
            half _EmissionNight;
            half4 _ShadowTint;
            half _AmbientAmount;
            half _Wrap;
        CBUFFER_END

        // Written once per frame by DayNightCycle. Outside UnityPerMaterial on purpose - globals
        // are not material data, and the SRP Batcher would stop batching if they were.
        half  _KayseriNight;
        half4 _KayseriNightTint;

        half Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        half ValueNoise(float2 p)
        {
            float2 cell = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            half a = Hash21(cell);
            half b = Hash21(cell + float2(1, 0));
            half c = Hash21(cell + float2(0, 1));
            half d = Hash21(cell + float2(1, 1));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        struct WaveField
        {
            half height;    // 0 trough .. 1 crest
            half2 slope;    // d(height) in world XZ, bent into the normal below
        };

        /// Rotate v by the unit complex number r. The four trains are authored as fixed unit
        /// vectors and turned as a set by _FlowDir, which is one multiply each instead of four
        /// sincos of a per-material angle.
        float2 Turn(float2 v, float2 r)
        {
            return float2(v.x * r.x - v.y * r.y, v.x * r.y + v.y * r.x);
        }

        WaveField Waves(float2 uv, float t)
        {
            // Four directional trains, and each one's WAVE FRONTS ARE BENT by its own slow field.
            //
            // Both simpler versions failed, in opposite directions. Straight sine trains interfere
            // into a visible plaid lattice that no amount of coordinate jitter removes. Plain fBm
            // octaves cannot lattice, but noise has no direction in it, so the sea came out as
            // camouflage - cloud, not water. Bending the fronts keeps the long crest lines that
            // make a surface read as water while destroying the regularity, because two curved
            // fronts have no fixed relationship to interfere with.
            const float4 FREQ = float4(1.00, 1.71, 2.83, 4.61);
            const float4 AMP  = float4(0.44, 0.28, 0.18, 0.10);
            const float4 SPD  = float4(1.00, 1.29, 0.81, 1.63);

            float2 r = normalize(_FlowDir.xy + float2(1e-4, 0));
            float2 d0 = Turn(float2(1.00,  0.00), r);
            float2 d1 = Turn(float2(0.62,  0.78), r);
            float2 d2 = Turn(float2(0.85, -0.53), r);
            float2 d3 = Turn(float2(0.10,  0.99), r);

            float2 bu = uv * _BendScale;
            float4 bend = float4(
                ValueNoise(bu + float2( t * 0.050, -t * 0.040) +  5.7),
                ValueNoise(bu + float2(-t * 0.040,  t * 0.060) + 23.1),
                ValueNoise(bu + float2( t * 0.030,  t * 0.050) + 47.9),
                ValueNoise(bu + float2(-t * 0.060, -t * 0.030) + 83.3)) - 0.5;

            // The bend goes into the ALONG-direction coordinate, not into uv: displacing uv moves
            // every train together and leaves their relationship intact, which was the whole
            // reason the lattice survived last time.
            float4 along = float4(dot(uv, d0), dot(uv, d1), dot(uv, d2), dot(uv, d3))
                         + bend * _Bend;
            float4 ph = along * FREQ + t * SPD;

            half4 s = sin(ph);
            half4 c = cos(ph);

            // w is the sine mapped to 0..1; w^4 is the same wave with its peaks pulled in and its
            // troughs spread out, which is the shape water actually has. _Choppy blends the two.
            half4 w = s * 0.5h + 0.5h;
            half4 w2 = w * w;
            half4 shaped = lerp(w, w2 * w2, _Choppy);

            // Exact derivative of that blend: d(w)/dph is c/2, and d(w^4)/dph is 2*w^3*c.
            half4 dshaped = c * lerp(0.5h, 2.0h * w2 * w, _Choppy);

            WaveField f;
            f.height = dot(shaped, AMP);
            half4 k = FREQ * AMP * dshaped;
            f.slope = d0 * k.x + d1 * k.y + d2 * k.z + d3 * k.w;
            return f;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half   fogCoord   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.fogCoord = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float t = _Time.y * _WaveSpeed;
                float2 uv = IN.positionWS.xz * _Scale;

                WaveField wave = Waves(uv, t);

                // Body to crest, and nothing else. The narrowness is the realism.
                half3 albedo = lerp(_DeepColor.rgb, _ShallowColor.rgb,
                                    smoothstep(0.28h, 0.74h, wave.height) * _Depth);

                // Only the tallest water breaks, and it breaks in the liquid's own colour.
                half cap = smoothstep(_FoamLevel, _FoamLevel + _FoamWidth, wave.height);
                albedo = lerp(albedo, _FoamColor.rgb, cap * _FoamAmount);

                // 0.12 because the four trains sum to a slope of about +-1.5: undamped, a
                // _NormalStrength of 2 tilts the surface past 50 degrees and whole patches sit at
                // ndh = 1, which reads as spilled white paint rather than as a sun path.
                half3 normalWS = normalize(IN.normalWS
                                 + half3(-wave.slope.x, 0, -wave.slope.y) * (_NormalStrength * 0.25h));

                // Flat water faces the sky and takes its colour; the sides of the waves take less.
                // On mercury this carries most of the read, which is why the amount goes to 0.30
                // there and stays near 0.1 on everything else.
                albedo = lerp(albedo, _SkyTint.rgb, _SkyAmount * saturate(normalWS.y));

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                half ndl = dot(normalWS, mainLight.direction);
                // Wrapped, not banded. A hard terminator on open water reads as a solid, and the
                // brief was realistic - the island's toon steps stay on the island.
                half diffuse = saturate((ndl + _Wrap) / (1.0h + _Wrap)) * mainLight.shadowAttenuation;

                half3 dusk = lerp(half3(1, 1, 1), _KayseriNightTint.rgb, _KayseriNight);
                half3 ramp = lerp(_ShadowTint.rgb * dusk, mainLight.color, diffuse);

                half3 viewDir = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                half3 halfDir = SafeNormalize(mainLight.direction + viewDir);
                float ndh = saturate(dot(normalWS, halfDir));

                // Two lobes. The broad one is the sheen over the whole sea; the tight one is the
                // sun's own reflection breaking up across the crests, which is the single most
                // recognisable thing about real water seen from above.
                // float, not half: pow() is exp2(log2(x)*n), and half's log2 near x=1 has so few
                // bits that a tight lobe collapses to zero everywhere. The glitter exponent is
                // capped for the same reason - past a few hundred the highlight has no width left.
                float gloss = _Smoothness * _Smoothness * 220.0 + 2.0;
                float sheen = pow(ndh, gloss);
                float glitter = pow(ndh, min(gloss * 1.5, 160.0));
                half3 specular = _SpecularTint.rgb * mainLight.color
                               * (sheen * _SpecularStrength + glitter * _GlitterStrength)
                               * (_Smoothness * diffuse);

                half3 ambient = SampleSH(normalWS) * _AmbientAmount;

                half3 emission = _EmissionColor.rgb * _Emission * (1.0h + _KayseriNight * _EmissionNight);

                half3 lit = albedo * (ramp + ambient) + specular + emission;

                half4 color = half4(lit, 1);
                color.rgb = MixFog(color.rgb, IN.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes IN)
            {
                DepthVaryings OUT = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
