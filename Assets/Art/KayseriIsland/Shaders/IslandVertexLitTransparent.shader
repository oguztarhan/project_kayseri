// Transparent companion to IslandVertexLit - used by water, sea, glass, smoke
// and the locked-plot ghost previews. Same vertex-colour tinting, alpha blended.
Shader "Kayseri/IslandVertexLitTransparent"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1,1,1,0.5)
        _VertexColorAmount("Vertex Color Amount", Range(0,1)) = 1.0
        _Saturation("Saturation", Range(0,3)) = 1.70
        _Vibrance("Brightness", Range(0.5,2)) = 1.02
        _Metallic("Metallic", Range(0,1)) = 0.0
        _Smoothness("Smoothness", Range(0,1)) = 0.85
        [HDR]_EmissionColor("Emission Color", Color) = (0,0,0,0)

        // Matches IslandVertexLit — see there for why per-vertex detail needs help.
        [Header(Detail)]
        _DetailStrength("Grain Strength", Range(0,0.4)) = 0.05
        _DetailScale("Grain Scale", Range(0.02,2)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
                half _VertexColorAmount;
                half _Metallic;
                half _Smoothness;
                half _Saturation;
                half _Vibrance;
                half _DetailStrength;
                half _DetailScale;
            CBUFFER_END

            // No day-night work in this pass. It goes through UniversalFragmentPBR, so water, sea,
            // foam, glass and smoke already darken with the sun and the ambient probe; and the one
            // emissive material on this shader is 'ghost', the locked-plot preview, which is a
            // marker rather than a lamp and stays lit around the clock.

            // Matches IslandVertexLit so glass and smoke are graded with everything else.
            half3 IslandGrade(half3 c)
            {
                half l = dot(c, half3(0.2126h, 0.7152h, 0.0722h));
                return saturate(lerp(l.xxx, c, _Saturation) * _Vibrance);
            }

            // Same value noise as IslandVertexLit. Duplicated rather than shared through an
            // include for two functions - the two shaders have no common header today.
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  color      : COLOR;
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
                OUT.color = IN.color;
                OUT.fogCoord = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Blend, not multiply - see IslandVertexLit for why.
                half3 albedo = IslandGrade(lerp(_BaseColor.rgb, IN.color.rgb, _VertexColorAmount));

                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalize(IN.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogCoord;
                inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                albedo *= 1.0h + IslandGrain(IN.positionWS, inputData.normalWS) * _DetailStrength;

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1.0h;
                surfaceData.alpha = _BaseColor.a;
                surfaceData.emission = _EmissionColor.rgb;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = _BaseColor.a;
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
