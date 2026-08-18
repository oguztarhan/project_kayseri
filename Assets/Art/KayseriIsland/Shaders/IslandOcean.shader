// The archipelago's water, whatever that island's water happens to be made of.
//
// The sea is a FLAT QUAD WITH EIGHT VERTICES. Everything the eye reads as liquid therefore has to
// happen in the fragment shader, in world space, off the clock - there is no mesh detail, no UV
// worth using and no vertex colour but Blender's one blue ramp. So this draws:
//
//   flow      two noise fields dragging a third through a domain warp, which is what makes the
//             surface SWIRL rather than slide. A single scrolling layer reads as a moving texture
//             and never as a fluid, no matter how good the texture is.
//   body      a three-colour ramp - trough, body, crest - so lava is red AND orange AND yellow at
//             once, mixing as it moves, instead of one tint with grain on it.
//   veins     a level set of the fine octave: the thin bright cracks between cooling crust plates
//             on lava, and the same shape in white on the frozen sea, where it is pressure ridges.
//   ripple    the fine octave's gradient bent into the normal, so the sun's highlight breaks into
//             a moving glitter field. This is the single cheapest thing that says "liquid".
//
// Cost is seven ValueNoise calls per pixel and no texture fetch at all, which suits a scene whose
// bottleneck is vertices and draw calls rather than arithmetic. Nothing here branches.
//
// Shading matches Kayseri/IslandVertexLit - same toon bands, same shadow tint, same night globals -
// so the water sits in the island's art style instead of looking like a different game. Emission is
// NOT gated by _KayseriLightsOn the way the lit shader's is: lava glows at noon.
//
// No ShadowCaster pass on purpose. The sea is a horizontal plane at sea level with the whole island
// standing above it; anything it could cast a shadow on is already underwater.
Shader "Kayseri/IslandOcean"
{
    Properties
    {
        // Trough, body, crest. The ramp is what carries the mixing - see _BandLow/_BandHigh.
        [Header(Colour)]
        _DeepColor("Deep / Crust", Color) = (0.02,0.10,0.16,1)
        _MidColor("Body", Color) = (0.05,0.22,0.30,1)
        _HotColor("Crest / Hot", Color) = (0.12,0.38,0.44,1)
        // Where the ramp crosses. Low is deep->body, high is body->crest; sharp is how abruptly.
        // A wide sharp reads as a smooth liquid, a narrow one as something with a skin on it.
        _BandLow("Band Low", Range(0,1)) = 0.30
        _BandHigh("Band High", Range(0,1)) = 0.62
        _BandSharp("Band Softness", Range(0.01,0.6)) = 0.22

        [Header(Flow)]
        // World units per noise cell is 1/_Scale, so 0.02 is a 50-unit swell.
        _Scale("Pattern Scale", Range(0.002,0.5)) = 0.022
        _FlowSpeed("Flow Speed", Range(0,2)) = 0.35
        _FlowDir("Flow Direction", Vector) = (1,0.35,0,0)
        // How far the warp field drags the pattern. 0 is a plain scroll; past about 1.5 the
        // surface tears rather than swirls.
        _Warp("Swirl", Range(0,2)) = 0.50
        // Squashes the pattern along one axis. Round cells read as marble no matter how well they
        // flow - open water is made of long swells, and this is what stretches them into some.
        // 1 is round, which is what the molten seas want: those are crust plates, not waves.
        _Stretch("Swell Stretch", Range(0.1,3)) = 0.42

        [Header(Veins)]
        // A band around one value of the fine octave. On lava these are the glowing cracks between
        // crust plates; on ice, ridges; on the metal seas, the rolling edge of a wave.
        _VeinColor("Vein Colour", Color) = (1,1,1,1)
        _VeinLevel("Vein Level", Range(0,1)) = 0.52
        _VeinWidth("Vein Width", Range(0.01,0.5)) = 0.11
        _VeinStrength("Vein Strength", Range(0,2)) = 0.0

        [Header(Surface)]
        // Bends the fine octave's gradient into the normal. This is what breaks the sun highlight
        // into moving glitter; at 0 the water is a mirror-flat slab lit by one broad specular.
        _NormalStrength("Ripple", Range(0,8)) = 2.2
        _Smoothness("Smoothness", Range(0,1)) = 0.80
        _SpecularStrength("Specular Strength", Range(0,4)) = 1.0
        _SpecularTint("Specular Tint", Color) = (1,1,1,1)

        [Header(Emission)]
        // Ungated, unlike IslandVertexLit's - that one is a street lamp and waits for dusk.
        [HDR]_EmissionColor("Emission Colour", Color) = (0,0,0,0)
        _Emission("Emission Strength", Range(0,4)) = 0
        _EmissionFloor("Emission In Troughs", Range(0,1)) = 0.30
        _VeinEmission("Vein Emission", Range(0,4)) = 0
        // Lava does not cool at sunset; it gets MORE obvious as the sky goes down.
        _EmissionNight("Night Boost", Range(0,3)) = 0.6

        [Header(Lighting)]
        _ToonSteps("Light Bands", Range(1,8)) = 4
        _ToonSmoothness("Band Softness", Range(0,1)) = 0.65
        _ShadowTint("Shadow Tint", Color) = (0.42,0.48,0.68,1)
        _AmbientAmount("Ambient", Range(0,1)) = 0.40
        _AmbientHemi("Hemisphere Amount", Range(0,1)) = 0.65
        _AmbientGround("Ambient Ground Tint", Color) = (0.55,0.48,0.40,1)
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
            half4 _MidColor;
            half4 _HotColor;
            half _BandLow;
            half _BandHigh;
            half _BandSharp;
            half _Scale;
            half _FlowSpeed;
            half4 _FlowDir;
            half _Warp;
            half _Stretch;
            half4 _VeinColor;
            half _VeinLevel;
            half _VeinWidth;
            half _VeinStrength;
            half _NormalStrength;
            half _Smoothness;
            half _SpecularStrength;
            half4 _SpecularTint;
            half4 _EmissionColor;
            half _Emission;
            half _EmissionFloor;
            half _VeinEmission;
            half _EmissionNight;
            half _ToonSteps;
            half _ToonSmoothness;
            half4 _ShadowTint;
            half _AmbientAmount;
            half _AmbientHemi;
            half4 _AmbientGround;
        CBUFFER_END

        // Written once per frame by DayNightCycle. Outside UnityPerMaterial on purpose - globals
        // are not material data, and the SRP Batcher would stop batching if they were. See the
        // same three in IslandVertexLit.
        half  _KayseriNight;
        half4 _KayseriNightTint;

        // Same hash and lattice noise as IslandVertexLit, deliberately duplicated: the two shaders
        // share no header today, and one include file for two functions is not worth the coupling.
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

        /// Everything the surface needs, from one place, so the octaves can be shared.
        ///
        /// Packed rather than returned one at a time because the fine octave is wanted three times
        /// over - as the vein level set, as the ripple gradient and as half the body - and sampling
        /// it once instead of three times is most of what keeps this affordable.
        struct OceanSurface
        {
            half heat;     // 0 trough .. 1 crest, drives the colour ramp
            half fine;     // the fine octave alone, which is what the veins cut
            half2 slope;   // its gradient, bent into the normal below
        };

        OceanSurface SampleSurface(float3 positionWS)
        {
            float2 uv = positionWS.xz * _Scale * float2(1.0, _Stretch);
            float t = _Time.y * _FlowSpeed;

            // Two slow fields, each drifting its own way, used to displace where the third is
            // read. Sliding one field is a conveyor belt; dragging it through another is a
            // current, and a current is the whole difference between a texture and a liquid.
            half wx = ValueNoise(uv * 0.62 + float2(t * 0.21, -t * 0.13));
            half wy = ValueNoise(uv * 0.62 + float2(-t * 0.17, t * 0.19) + 31.7);
            float2 p = uv + _FlowDir.xy * t * 0.35 + (float2(wx, wy) - 0.5) * _Warp;

            // The coarse octave is the swell; the fine one is the chop riding on it. Weighted
            // toward the swell so the colour masses stay large enough to read at the play camera,
            // which is a long way up.
            half coarse = ValueNoise(p);
            float2 fp = p * 2.53 + 13.1;
            half fine = ValueNoise(fp);

            // Forward differences on the fine octave only. A central difference would be twice the
            // cost for a symmetry no one can see on moving water.
            half gx = ValueNoise(fp + float2(0.55, 0.0)) - fine;
            half gy = ValueNoise(fp + float2(0.0, 0.55)) - fine;

            // A third, much larger field so the sea is not the same average brightness everywhere -
            // without it the pattern is uniform and the eye reads a tiled material.
            half swell = ValueNoise(uv * 0.17 + float2(-t * 0.07, t * 0.05) + 77.3);

            OceanSurface s;
            s.heat = saturate(coarse * 0.46h + fine * 0.18h + swell * 0.36h);
            s.fine = fine;
            s.slope = half2(gx, gy);
            return s;
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

                OceanSurface surf = SampleSurface(IN.positionWS);

                // Trough -> body -> crest, both crossings soft, so at any one moment the surface is
                // carrying all three colours at once and they trade places as the flow moves them.
                half toBody = smoothstep(_BandLow, _BandLow + _BandSharp, surf.heat);
                half toCrest = smoothstep(_BandHigh, _BandHigh + _BandSharp, surf.heat);
                half3 albedo = lerp(_DeepColor.rgb, _MidColor.rgb, toBody);
                albedo = lerp(albedo, _HotColor.rgb, toCrest);

                // A band either side of one value of the fine octave, squared to pull its shoulders
                // in. Following a level set rather than the value itself is what gives a connected
                // WEB of cracks instead of a field of blobs.
                half vein = 1.0h - smoothstep(0.0h, _VeinWidth, abs(surf.fine - _VeinLevel));
                vein = vein * vein * _VeinStrength;
                albedo = lerp(albedo, _VeinColor.rgb, saturate(vein));

                // The chop bent into the normal. The quad is horizontal, but adding to the
                // geometric normal rather than replacing it keeps this correct on the river ribbon
                // and anything else the swap lands on.
                // 0.25 because a forward difference on value noise lands in about +-0.5: undamped,
            // _NormalStrength 2 tilts the surface through 45 degrees and every pixel on the sea
            // catches the sun at once, which is a sheet of white, not a glitter path.
            half3 normalWS = normalize(IN.normalWS
                                 + half3(-surf.slope.x, 0, -surf.slope.y) * (_NormalStrength * 0.25h));

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogCoord;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                Light mainLight = GetMainLight(inputData.shadowCoord);
                half ndl = saturate(dot(normalWS, mainLight.direction));
                half lit = ndl * mainLight.shadowAttenuation;

                // Same anti-aliased banding as IslandVertexLit, and defaulted much softer here:
                // water wants the island's palette, not its hard cel edges.
                half steps = max(1.0h, _ToonSteps);
                half scaled = lit * steps;
                half width = clamp(fwidth(scaled), 1e-3h, 1.0h);
                half banded = (floor(scaled) + smoothstep(1.0h - width, 1.0h, frac(scaled))) / steps;
                half toon = lerp(banded, lit, saturate(_ToonSmoothness));

                half3 dusk = lerp(half3(1, 1, 1), _KayseriNightTint.rgb, _KayseriNight);
                half3 ramp = lerp(_ShadowTint.rgb * dusk, mainLight.color, toon);

                // Blinn-Phong off the rippled normal. On a flat sea this is the whole show: it is
                // the only term that changes as the surface moves under a fixed camera.
                half3 halfDir = SafeNormalize(mainLight.direction + inputData.viewDirectionWS);
                half gloss = _Smoothness * _Smoothness * 256.0h + 1.0h;
                half spec = pow(saturate(dot(normalWS, halfDir)), gloss);
                half3 specular = _SpecularTint.rgb * mainLight.color * spec
                               * (_SpecularStrength * toon);

                half3 ambientSH = SampleSH(normalWS);
                half up = normalWS.y * 0.5h + 0.5h;
                half3 ambient = lerp(ambientSH,
                                     lerp(ambientSH * _AmbientGround.rgb, ambientSH, up),
                                     _AmbientHemi);

                // Never fully off in the troughs: molten rock between two crust plates is still
                // hot, and a hard cutoff there reads as glowing paint on a dark surface.
                half glow = lerp(_EmissionFloor, 1.0h, toCrest);
                half3 emission = _EmissionColor.rgb * (_Emission * glow + saturate(vein) * _VeinEmission);
                emission *= 1.0h + _KayseriNight * _EmissionNight;

                half3 lit3 = albedo * (ramp + ambient * _AmbientAmount) + specular + emission;

                half4 color = half4(lit3, 1);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
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
