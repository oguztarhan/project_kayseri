// Stylised gradient skybox. Replaces the flat solid-colour camera clear so the
// island sits in something with depth to it. No texture, no cubemap - it's a
// handful of ALU on background pixels, which is what a mobile target wants.
Shader "Kayseri/ToonSky"
{
    Properties
    {
        _ZenithColor("Zenith", Color) = (0.16, 0.44, 0.82, 1)
        _SkyColor("Mid Sky", Color) = (0.42, 0.70, 0.94, 1)
        _HorizonColor("Horizon", Color) = (0.86, 0.94, 0.97, 1)
        _GroundColor("Below Horizon", Color) = (0.62, 0.68, 0.72, 1)

        _HorizonHeight("Horizon Falloff", Range(0.02, 1)) = 0.42
        _SkyCurve("Sky Curve", Range(0.2, 4)) = 1.35
        _GroundFalloff("Below Falloff", Range(0.02, 1)) = 0.28

        [HDR]_SunColor("Sun Glow", Color) = (1.0, 0.92, 0.72, 1)
        _SunFalloff("Sun Tightness", Range(4, 4096)) = 240
        _SunStrength("Sun Strength", Range(0, 4)) = 0.9
        _HaloStrength("Halo Strength", Range(0, 2)) = 0.35

        _Exposure("Exposure", Range(0, 4)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "ToonSky"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _SkyColor;
                half4 _HorizonColor;
                half4 _GroundColor;
                half4 _SunColor;
                half  _HorizonHeight;
                half  _SkyCurve;
                half  _GroundFalloff;
                half  _SunFalloff;
                half  _SunStrength;
                half  _HaloStrength;
                half  _Exposure;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS      : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                // Unity draws the skybox mesh centred on the camera, so the object
                // space position doubles as the view ray.
                OUT.dirOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float3 dir = normalize(IN.dirOS);

                // Three stops above the horizon rather than two - a single lerp
                // reads as a wash, the extra stop is what makes it feel painted.
                half up = saturate(dir.y / max(_HorizonHeight, 1e-3h));
                up = pow(up, _SkyCurve);
                half3 col = lerp(_HorizonColor.rgb, _SkyColor.rgb, saturate(up * 2.0h));
                col = lerp(col, _ZenithColor.rgb, saturate(up * 2.0h - 1.0h));

                half below = saturate(-dir.y / max(_GroundFalloff, 1e-3h));
                col = lerp(col, _GroundColor.rgb, below);

                half sd = saturate(dot(dir, _MainLightPosition.xyz));
                col += _SunColor.rgb * pow(sd, _SunFalloff) * _SunStrength;
                col += _SunColor.rgb * pow(sd, 6.0h) * _HaloStrength;

                col *= _Exposure;

                // An 8-bit target quantises a gradient this smooth into visible
                // bands; a sub-LSB dither costs nothing and removes them.
                half d = InterleavedGradientNoise(IN.positionCS.xy, 0) - 0.5h;
                col += d * (1.0h / 255.0h);

                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
