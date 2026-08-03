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
        CBUFFER_END

        // Pull the colour away from its own grey, then lift it. Luminance-preserving so a
        // saturation push brightens the hue rather than washing the whole island out.
        half3 IslandGrade(half3 c)
        {
            half l = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
            return saturate(lerp(l.xxx, c, _Saturation) * _Vibrance);
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
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half ndl = saturate(dot(inputData.normalWS, mainLight.direction));
                half lit = ndl * mainLight.shadowAttenuation;

                half steps = max(1.0h, _ToonSteps);
                half banded = floor(lit * steps) / steps;
                half toon = lerp(banded, lit, saturate(_ToonSmoothness));

                half3 ramp = lerp(_ShadowTint.rgb, mainLight.color, toon);

                half fres = 1.0h - saturate(dot(inputData.normalWS, inputData.viewDirectionWS));
                half3 rim = _RimColor.rgb * pow(fres, _RimPower) * _RimStrength * toon;

                half3 lit3 = albedo * (ramp + inputData.bakedGI * _AmbientAmount)
                           + rim + _EmissionColor.rgb;

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
