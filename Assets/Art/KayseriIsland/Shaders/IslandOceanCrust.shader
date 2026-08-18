// Plated surfaces: lava, molten gold, pack ice.
//
// Wave trains are the wrong primitive for these. Molten rock is not water with a hot colour - it is
// a sheet of solidified crust broken into irregular plates, with the melt showing in the gaps. Pack
// ice is the same structure at the other temperature. So this is a Voronoi cellular field, which is
// exactly what "a plane broken into irregular plates" means mathematically:
//
//   plates   one cell per plate, each tinted slightly differently off its own site hash, because a
//            field of identical plates reads as a pattern and a field of varied ones reads as rock.
//   cracks   F2 - F1, the distance between the two nearest sites, which is zero exactly on a plate
//            boundary and rises into the plate interior. That gives a connected, irregular web -
//            the thing the old level-set version was imitating and getting wrong.
//   heat     the crack is coloured ACROSS its width: red at the shoulder where the crust is only
//            warm, orange through the middle, yellow-white in the core. That gradient is what real
//            lava looks like, and it is why this reads as heat rather than as glowing paint.
//   drift    the whole field creeps and is slowly domain-warped, so plates deform instead of
//            sliding rigidly. Slow on purpose: crust moves at a crawl.
//
// On ice the same three crack colours simply run the other way - pale wet floe edge, shallow lead,
// deep open water - and emission is zero. One code path, opposite ends of the thermometer.
//
// Cost: nine cells x two hashes for the Voronoi, plus three ValueNoise for warp, heat and grain.
// That is the heavier of the two ocean shaders and it is on three islands, only one of which is
// ever loaded at a time. Branchless - the nearest/second-nearest search is min/max, not if.
//
// No ShadowCaster pass on purpose, as with the wave shader.
Shader "Kayseri/IslandOceanCrust"
{
    Properties
    {
        [Header(Crust)]
        _CrustColor("Crust", Color) = (0.05,0.03,0.03,1)
        _CrustColor2("Crust Variant", Color) = (0.09,0.06,0.06,1)
        // How much of the plate-to-plate difference between those two actually shows.
        _PlateVariation("Plate Variation", Range(0,1)) = 1.0
        _Grain("Crust Grain", Range(0,1)) = 0.30

        [Header(Cracks)]
        // Read outward from the plate boundary: the core of the crack, its middle, and the warm
        // shoulder where it meets the crust. On ice these run pale to dark instead.
        _CrackHot("Crack Core", Color) = (1.0,0.80,0.30,1)
        _CrackMid("Crack Middle", Color) = (0.95,0.30,0.02,1)
        _CrackCool("Crack Shoulder", Color) = (0.42,0.05,0.01,1)
        // How far into the plate the crack reaches. Wide reads as mostly-molten, narrow as a
        // cooled sheet with a little heat left in the seams.
        _CrackWidth("Crack Width", Range(0.02,1)) = 0.30

        [Header(Plates)]
        // 1/_Scale is roughly the plate size in world units, so 0.017 is a 60-unit plate.
        _Scale("Plate Scale", Range(0.002,0.2)) = 0.017
        _DriftSpeed("Drift Speed", Range(0,1)) = 0.05
        _FlowDir("Drift Direction", Vector) = (1,0.25,0,0)
        // Bends the cell grid so the plates are irregular rather than a jittered lattice.
        _Warp("Warp", Range(0,3)) = 0.60
        _WarpScale("Warp Scale", Range(0.05,2)) = 0.35
        // How much the large-scale field cools the cracks. At 0 the whole sheet is equally hot,
        // which is the giveaway that it is a texture and not a surface.
        _HeatAmount("Heat Variation", Range(0,1)) = 0.45

        [Header(Surface)]
        _NormalStrength("Crust Relief", Range(0,6)) = 1.5
        _Smoothness("Smoothness", Range(0,1)) = 0.25
        _SpecularStrength("Specular", Range(0,4)) = 0.30
        _SpecularTint("Specular Tint", Color) = (1,1,1,1)

        [Header(Emission)]
        // The cracks light themselves - they emit in their own colour rather than a separate one,
        // which is what keeps the glow and the surface agreeing. 0 turns the sheet to ice.
        _Emission("Emission Strength", Range(0,6)) = 0
        _EmissionNight("Night Boost", Range(0,3)) = 0.7

        [Header(Lighting)]
        _ShadowTint("Shadow Tint", Color) = (0.30,0.34,0.48,1)
        _AmbientAmount("Ambient", Range(0,2)) = 0.55
        _Wrap("Light Wrap", Range(0,1)) = 0.30
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
            half4 _CrustColor;
            half4 _CrustColor2;
            half _PlateVariation;
            half _Grain;
            half4 _CrackHot;
            half4 _CrackMid;
            half4 _CrackCool;
            half _CrackWidth;
            half _Scale;
            half _DriftSpeed;
            half4 _FlowDir;
            half _Warp;
            half _WarpScale;
            half _HeatAmount;
            half _NormalStrength;
            half _Smoothness;
            half _SpecularStrength;
            half4 _SpecularTint;
            half _Emission;
            half _EmissionNight;
            half4 _ShadowTint;
            half _AmbientAmount;
            half _Wrap;
        CBUFFER_END

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

        struct Plates
        {
            half edge;   // F2 - F1: 0 exactly on a plate boundary, rising into the plate
            half id;     // per-plate random, for tinting one plate differently from the next
        };

        /// Nearest and second-nearest site over the 3x3 neighbourhood.
        ///
        /// The plate id is taken from the site's own x offset rather than a third hash - it is
        /// already a per-cell random and nine extra hashes for a tint is not a trade worth making.
        Plates Cells(float2 p)
        {
            float2 cell = floor(p);
            float2 f = frac(p);

            half d1 = 8.0h, d2 = 8.0h, id = 0.0h;

            [unroll]
            for (int j = -1; j <= 1; j++)
            {
                [unroll]
                for (int i = -1; i <= 1; i++)
                {
                    float2 g = float2(i, j);
                    float2 c = cell + g;
                    float2 o = float2(Hash21(c), Hash21(c + 71.3));
                    half d = length(g + o - f);

                    // Before d1 moves, or the id belongs to the previous winner. Branchless: the
                    // step is 1 exactly when this site is the new nearest.
                    id = lerp(id, (half)o.x, step(d, d1));
                    d2 = min(d2, max(d1, d));
                    d1 = min(d1, d);
                }
            }

            Plates o2;
            o2.edge = d2 - d1;
            o2.id = id;
            return o2;
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

                float t = _Time.y;
                float2 uv = IN.positionWS.xz * _Scale + _FlowDir.xy * (t * _DriftSpeed * 0.05);

                // One field does two jobs: it warps the cell grid so the plates are irregular, and
                // the same value says how hot this stretch of the sheet is. Two noises for the
                // price of one, and they SHOULD correlate - crust is thinnest where it is deformed.
                half w = ValueNoise(uv * _WarpScale + float2(t * 0.012, -t * 0.009));
                float2 p = uv + (w - 0.5h) * _Warp;

                Plates plates = Cells(p);

                // 1 on a plate boundary, falling to 0 into the plate.
                half crack = 1.0h - smoothstep(0.0h, _CrackWidth, plates.edge);
                // Cooler where the sheet is thick. Without this every seam is the same temperature
                // and the whole surface reads as one flat pattern.
                crack *= lerp(1.0h - _HeatAmount, 1.0h, w);

                // Crust first, one plate to the next.
                half3 albedo = lerp(_CrustColor.rgb, _CrustColor2.rgb, plates.id * _PlateVariation);

                // Rock grain, and the gradient of the same sample bent into the normal below. Three
                // noise calls total, of which this is two - the third was the warp above.
                half g = ValueNoise(p * 1.7);
                albedo *= 1.0h - _Grain * 0.5h + _Grain * g;

                // The crack, coloured ACROSS its width. Shoulder, middle, core, in that order, so
                // a hot seam runs black -> red -> orange -> yellow outward from the plate edge.
                albedo = lerp(albedo, _CrackCool.rgb, smoothstep(0.02h, 0.38h, crack));
                albedo = lerp(albedo, _CrackMid.rgb,  smoothstep(0.38h, 0.74h, crack));
                albedo = lerp(albedo, _CrackHot.rgb,  smoothstep(0.74h, 1.00h, crack));

                half gx = ValueNoise(p * 1.7 + float2(0.5, 0.0)) - g;
                half gy = ValueNoise(p * 1.7 + float2(0.0, 0.5)) - g;
                // 0.2, for the same reason the wave shader damps its slope: this gradient is a
                // difference of two noise samples and lands near +-0.5, which is a 25 degree tilt
                // per unit of relief.
                half3 normalWS = normalize(IN.normalWS
                                 + half3(-gx, 0, -gy) * (_NormalStrength * 0.2h));

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                half ndl = dot(normalWS, mainLight.direction);
                half diffuse = saturate((ndl + _Wrap) / (1.0h + _Wrap)) * mainLight.shadowAttenuation;

                half3 dusk = lerp(half3(1, 1, 1), _KayseriNightTint.rgb, _KayseriNight);
                half3 ramp = lerp(_ShadowTint.rgb * dusk, mainLight.color, diffuse);

                half3 viewDir = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                half3 halfDir = SafeNormalize(mainLight.direction + viewDir);
                // float for the same reason as the wave shader: half's log2 near 1 cannot carry
                // a tight specular lobe and the highlight vanishes.
                float gloss = _Smoothness * _Smoothness * 200.0 + 2.0;
                float ndh = saturate(dot(normalWS, halfDir));
                // Scaled by smoothness as well as strength: at _Smoothness 0.2 the exponent is
                // about 10, a lobe so broad it covers the whole plate. Unscaled, that is not a
                // highlight, it is a grey film over black basalt.
                half3 specular = _SpecularTint.rgb * mainLight.color
                               * (half)pow(ndh, gloss)
                               * (_SpecularStrength * _Smoothness * diffuse);

                half3 ambient = SampleSH(normalWS) * _AmbientAmount;

                // Emits in the crack's own colour, squared so the core carries almost all of it and
                // the crust stays genuinely dark. On ice _Emission is 0 and this whole term is gone.
                half3 crackColor = lerp(_CrackMid.rgb, _CrackHot.rgb, smoothstep(0.6h, 1.0h, crack));
                half3 emission = crackColor * (crack * crack * _Emission)
                               * (1.0h + _KayseriNight * _EmissionNight);

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
