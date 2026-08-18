// URP lit shader that multiplies the material colour by the mesh's vertex
// colour. The island's surface detail (noise, corrugation, coal grain) was
// baked from Blender's procedural materials into vertex colours, so this is
// what makes the map look textured without a single texture map.
//
// SRP Batcher compatible: every pass shares one UnityPerMaterial CBUFFER.
Shader "Kayseri/IslandVertexLit"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _VertexColorAmount("Vertex Color Amount", Range(0,1)) = 1.0
        _Metallic("Metallic", Range(0,1)) = 0.0
        _Smoothness("Smoothness", Range(0,1)) = 0.15
        [HDR]_EmissionColor("Emission Color", Color) = (0,0,0,0)
        // Emission on this shader is a LAMP: multiplied by _KayseriLightsOn below, so it is
        // black in daylight. A lava or acid sea is not a lamp - it glows at noon too - so this
        // floors that gate. 0 on every authored material, which is exactly the old behaviour.
        _EmissionAlways("Emission In Daylight", Range(0,1)) = 0

        // The bake carries Blender's procedural colour through faithfully, which reads
        // muted once URP lights it. These push it back toward the toy-bright look the
        // island is drawn in, without touching any of the 63 authored colours.
        _Saturation("Saturation", Range(0,3)) = 1.70
        _Vibrance("Brightness", Range(0.5,2)) = 1.02

        // Toon lighting. Steps is how many bands the light breaks into; smoothness
        // eases their edges back toward smooth shading. Shadow tint is what the
        // unlit side becomes - a cool blue reads far more "stylised" than grey.
        [Header(Toon)]
        _ToonSteps("Light Bands", Range(1,8)) = 3
        _ToonSmoothness("Band Softness", Range(0,1)) = 0.10
        _ShadowTint("Shadow Tint", Color) = (0.42,0.48,0.68,1)
        _AmbientAmount("Ambient", Range(0,1)) = 0.40
        _RimColor("Rim Colour", Color) = (1,1,1,1)
        _RimPower("Rim Falloff", Range(0.5,8)) = 3.0
        _RimStrength("Rim Strength", Range(0,2)) = 0.35

        // The surface detail is baked per VERTEX, so on the coarse parts of the terrain you are
        // looking straight at the vertices: flat blotches with stair-stepped edges. This adds
        // grain back in world space, at a frequency the mesh could never carry, which is what
        // makes the ground read as material instead of as polygons.
        [Header(Detail)]
        _DetailStrength("Grain Strength", Range(0,0.4)) = 0.13
        _DetailScale("Grain Scale", Range(0.02,2)) = 0.35

        // Driven by the per-material _Smoothness above, which is the one value the 78 island
        // materials genuinely differ on (0 on soot, 0.92 on chrome) - so this costs nothing to
        // author and immediately separates metal from rock.
        [Header(Specular)]
        _SpecularStrength("Specular Strength", Range(0,2)) = 0.45
        _SpecularTint("Specular Tint", Color) = (1,1,1,1)

        // The unlit side used to take one flat ambient value. Splitting it by world normal into
        // a sky and a ground term is what stops upward and downward faces reading identically.
        [Header(Ambient)]
        _AmbientGround("Ambient Ground Tint", Color) = (0.55,0.48,0.40,1)
        _AmbientHemi("Hemisphere Amount", Range(0,1)) = 0.65

        // Neglect. Zero on all 78 authored materials, so nothing on the island changes until
        // DistrictWear swaps a district onto a worn variant of the same material. It lives in
        // UnityPerMaterial with everything else, which is what lets the worn and the clean variants
        // of a material sit in the SAME SRP Batcher batch - the batcher keys on the shader, not on
        // the material, so dirtying a district costs no extra draw calls.
        [Header(Wear)]
        _Grime("Grime", Range(0,1)) = 0
        _GrimeColor("Grime Colour", Color) = (0.26,0.21,0.15,1)
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
            half4 _BaseColor;
            half4 _EmissionColor;
            half _EmissionAlways;
            half _VertexColorAmount;
            half _Metallic;
            half _Smoothness;
            half _Saturation;
            half _Vibrance;
            half _ToonSteps;
            half _ToonSmoothness;
            half4 _ShadowTint;
            half _AmbientAmount;
            half4 _RimColor;
            half _RimPower;
            half _RimStrength;
            half _DetailStrength;
            half _DetailScale;
            half _SpecularStrength;
            half4 _SpecularTint;
            half4 _AmbientGround;
            half _AmbientHemi;
            half _Grime;
            half4 _GrimeColor;
        CBUFFER_END

        // Time of day, written once per frame by DayNightCycle through Shader.SetGlobal*.
        // Declared OUTSIDE UnityPerMaterial and left out of Properties on purpose: globals are
        // not material data, so the SRP Batcher keeps batching the island. All three read zero
        // until something sets them, and zero is the day state - a scene with no DayNightCycle
        // in it renders exactly as it did before, minus the lamp glow.
        half  _KayseriNight;        // 0 day .. 1 night
        half4 _KayseriNightTint;    // what the constant shading terms fall to at night
        half  _KayseriLightsOn;     // 0 lamps dark .. 1 lamps lit

        // Pull the colour away from its own grey, then lift it. Luminance-preserving so a
        // saturation push brightens the hue rather than washing the whole island out.
        half3 IslandGrade(half3 c)
        {
            half l = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
            return saturate(lerp(l.xxx, c, _Saturation) * _Vibrance);
        }

        // Value noise off a hashed integer lattice. No texture fetch and no gradients - this runs
        // on the arithmetic units, which are the ones sitting idle on a scene this vertex-heavy.
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

        /// Three octaves of grain in world space, projected top-down on flat ground and from the
        /// side on walls so cliffs are not smeared into streaks. Returns -1..1.
        ///
        /// The coarse octave carries most of the weight on purpose: the baked vertex blotches are
        /// 10-20 units across, so grain finer than that only decorates them - it takes a wavelength
        /// on their own scale to actually break their stair-stepped edges up.
        half IslandGrain(float3 positionWS, half3 normalWS)
        {
            float2 top = positionWS.xz * _DetailScale;
            float2 side = float2(positionWS.x + positionWS.z, positionWS.y) * _DetailScale;
            float2 uv = lerp(side, top, saturate(abs(normalWS.y)));
            half n = ValueNoise(uv * 0.18h) * 0.45h
                   + ValueNoise(uv) * 0.35h
                   + ValueNoise(uv * 3.1h) * 0.20h;
            return n * 2.0h - 1.0h;
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            // Forward+ clusters its lights instead of handing each object a short list, which is the
            // only way the island's one 640x640 ground renderer can be lit by more than four lamps.
            // URP 17 spells the keyword _CLUSTER_LIGHT_LOOP; without this pragma LIGHT_LOOP_BEGIN
            // silently falls back to the per-object loop.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                half4  color        : COLOR;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  color      : COLOR;
                half   fogCoord   : TEXCOORD2;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 3);
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
                OUT.color = IN.color;
                OUT.fogCoord = ComputeFogFactor(pos.positionCS.z);

                OUTPUT_LIGHTMAP_UV(IN.staticLightmapUV, unity_LightmapST, OUT.staticLightmapUV);
                OUTPUT_SH(nrm.normalWS, OUT.vertexSH);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // BLEND, do not multiply. The bake writes the material's FULL
                // colour into vertex colours, so multiplying would square it
                // (grass 0.44*0.44, coal 0.02*0.02 -> black).
                //   amount 1 = baked colour + all its procedural detail
                //   amount 0 = flat _BaseColor fallback
                half3 albedo = IslandGrade(lerp(_BaseColor.rgb, IN.color.rgb, _VertexColorAmount));

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalize(IN.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogCoord;
                inputData.bakedGI = SAMPLE_GI(IN.staticLightmapUV, IN.vertexSH, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                // Toon shading rather than UniversalFragmentPBR. PBR's smooth falloff is
                // exactly what a stylised look is trying not to have: the light is quantised
                // into a few bands, the unlit side goes to a tinted colour instead of black,
                // and a fresnel rim picks out silhouettes. Main light only - additional
                // lights would cost per-light bands on a 1600-renderer mobile scene.
                // The bake is per vertex, so on the coarse parts of the terrain the eye is reading
                // the mesh itself - flat patches with stair-stepped edges. This puts detail back
                // at a frequency no vertex count here could carry.
                half grain = IslandGrain(IN.positionWS, inputData.normalWS);
                albedo *= 1.0h + grain * _DetailStrength;

                // Neglect. Three things together are what make this read as DIRT rather than as a
                // brown tint: it collects on upward faces (roofs, decks, the tops of walls) and
                // leaves the undersides alone, it is blotchy rather than even, and it takes the
                // colour down as well as toward brown, because grime absorbs light.
                //
                // The blotches ride the SAME grain the line above uses. A second noise would have
                // looked marginally better and cost a second set of hashes on every island pixel of
                // every frame, which on a 1600-renderer mobile scene is not a trade worth making.
                half dirt = _Grime
                          * saturate(0.35h + 0.65h * inputData.normalWS.y)   // settles on top
                          * saturate(0.70h + 0.90h * grain);                 // in patches
                albedo = lerp(albedo, lerp(albedo, _GrimeColor.rgb, 0.75h) * 0.58h, saturate(dirt));

                Light mainLight = GetMainLight(inputData.shadowCoord);
                half ndl = saturate(dot(inputData.normalWS, mainLight.direction));
                half lit = ndl * mainLight.shadowAttenuation;

                // Anti-aliased banding. A bare floor() steps in screen space, which is a large
                // part of what reads as low resolution; widening each band edge by the screen-space
                // derivative of the light term makes the steps resolution-correct instead. With the
                // derivative at zero this collapses back to exactly the old floor().
                half steps = max(1.0h, _ToonSteps);
                half scaled = lit * steps;
                half width = clamp(fwidth(scaled), 1e-3h, 1.0h);
                half banded = (floor(scaled) + smoothstep(1.0h - width, 1.0h, frac(scaled))) / steps;
                half toon = lerp(banded, lit, saturate(_ToonSmoothness));

                // Night only has to reach the terms that are constants in the material. The lit
                // side already follows the sun - mainLight.color carries its intensity - and
                // bakedGI follows RenderSettings.ambientLight, both of which DayNightCycle moves.
                // Left alone, the shadow tint and the rim would keep the island bright at midnight.
                half3 dusk = lerp(half3(1, 1, 1), _KayseriNightTint.rgb, _KayseriNight);
                half3 ramp = lerp(_ShadowTint.rgb * dusk, mainLight.color, toon);

                // Street lamps, headlights and the rest. Guarded by the keyword URP already sets, so
                // with no additional lights in range this compiles away and the daytime island is
                // byte for byte what it was. Banded through the same _ToonSteps as the sun, because
                // a smooth realistic hotspot on a hard-banded island reads as a bug.
                // URP's own Lit guards this with _ADDITIONAL_LIGHTS alone, but that keyword is not
                // what carries Forward+ — the cluster does, and it is driven by USE_CLUSTER_LIGHT_LOOP.
                // Guarding on the keyword alone left this loop compiled out of every Forward+ variant,
                // which is why a 90 unit, intensity 40 test light lit a URP/Lit cube in the town and
                // left the ground under it black.
                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light extra = GetAdditionalLight(lightIndex, inputData.positionWS, half4(1, 1, 1, 1));
                    half reach = saturate(dot(inputData.normalWS, extra.direction)) * extra.distanceAttenuation;
                    half pooled = floor(reach * steps) / steps;
                    ramp += extra.color * lerp(pooled, reach, saturate(_ToonSmoothness));
                LIGHT_LOOP_END
                #endif

                half fres = 1.0h - saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
                half3 rim = _RimColor.rgb * pow(fres, _RimPower) * _RimStrength * toon * dusk;

                // Blinn-Phong off the authored _Smoothness, which is the one value the island
                // materials actually differ on. Gated by the toon term so a face in shadow cannot
                // catch a highlight, and by dusk so it fades out with the sun.
                half3 halfDir = SafeNormalize(mainLight.direction + inputData.viewDirectionWS);
                half gloss = _Smoothness * _Smoothness * 256.0h + 1.0h;
                half spec = pow(saturate(dot(inputData.normalWS, halfDir)), gloss);
                half3 specular = _SpecularTint.rgb * mainLight.color * spec
                               * (_SpecularStrength * _Smoothness * toon) * dusk;

                // Sky above, warm ground bounce below, instead of one flat value on every face.
                // Both ends come from bakedGI, so DayNightCycle dimming the probe still dims this.
                half up = inputData.normalWS.y * 0.5h + 0.5h;
                half3 ambient = lerp(inputData.bakedGI,
                                     lerp(inputData.bakedGI * _AmbientGround.rgb, inputData.bakedGI, up),
                                     _AmbientHemi);

                half3 lit3 = albedo * (ramp + ambient * _AmbientAmount)
                           + specular + rim + _EmissionColor.rgb * max(_EmissionAlways, _KayseriLightsOn);

                half4 color = half4(lit3, _BaseColor.a);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(ShadowAttributes IN)
            {
                ShadowVaryings OUT = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target
            {
                return 0;
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
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DNVaryings DepthNormalsVert(DNAttributes IN)
            {
                DNVaryings OUT = (DNVaryings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DepthNormalsFrag(DNVaryings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                return half4(normalize(IN.normalWS) * 0.5 + 0.5, 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
