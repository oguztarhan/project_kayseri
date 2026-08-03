// Screen-space toon outline. Roberts-cross edge detect over scene depth and
// scene normals, drawn as one fullscreen pass so the cost is independent of
// how many renderers the island has (~1600, which rules out inverted hull).
//
// Driven by FullScreenPassRendererFeature on both URP renderers. That feature
// must request Depth + Normal, and have Fetch Color Buffer on so _BlitTexture
// holds the opaque camera colour.
Shader "Kayseri/ToonOutline"
{
    Properties
    {
        [HDR]_OutlineColor("Outline Colour", Color) = (0.06, 0.05, 0.10, 1)
        _Thickness("Thickness (pixels)", Range(0.5, 4)) = 1.2
        _OutlineTint("Tint By Surface", Range(0,1)) = 0.45
        _Strength("Strength", Range(0,1)) = 1.0

        [Header(Depth Edges)]
        _DepthThreshold("Depth Threshold", Range(0.0005, 0.05)) = 0.006
        _DepthSoftness("Depth Softness", Range(1, 4)) = 2.0
        _GrazingRejection("Grazing Rejection", Range(0, 80)) = 26

        [Header(Normal Edges)]
        _NormalThreshold("Normal Threshold", Range(0, 2)) = 0.55
        _NormalSoftness("Normal Softness", Range(0.01, 1)) = 0.22
        _NormalStrength("Normal Edge Strength", Range(0,1)) = 1.0

        // World units, not normalised. The island renders at eye depths of
        // roughly 400-950 at default zoom, so these have to be big.
        [Header(Distance)]
        _FadeStart("Fade Start", Float) = 1400
        _FadeEnd("Fade End", Float) = 3000
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "ToonOutline"

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            half4 _OutlineColor;
            half  _Thickness;
            half  _OutlineTint;
            half  _Strength;
            half  _DepthThreshold;
            half  _DepthSoftness;
            half  _GrazingRejection;
            half  _NormalThreshold;
            half  _NormalSoftness;
            half  _NormalStrength;
            float _FadeStart;
            float _FadeEnd;

            // Sky sits at the far plane. Its normal is whatever the prepass
            // cleared to, so every horizon pixel would otherwise read as a
            // crease. Masking on the centre tap also means silhouettes get
            // drawn on the object side only, which is the cleaner read.
            float IsSky(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 1e-6 ? 1.0 : 0.0;
                #else
                    return rawDepth >= 1.0 - 1e-6 ? 1.0 : 0.0;
                #endif
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                float2 uv = IN.texcoord;

                half4 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);

                float rawC = SampleSceneDepth(uv);
                if (IsSky(rawC) > 0.5) return src;

                // _ScreenParams.zw is (1 + 1/w, 1 + 1/h).
                float2 texel = (_ScreenParams.zw - 1.0) * _Thickness;

                float2 uv0 = uv + float2(-texel.x, -texel.y);
                float2 uv1 = uv + float2( texel.x,  texel.y);
                float2 uv2 = uv + float2(-texel.x,  texel.y);
                float2 uv3 = uv + float2( texel.x, -texel.y);

                float dC = LinearEyeDepth(rawC, _ZBufferParams);
                float d0 = LinearEyeDepth(SampleSceneDepth(uv0), _ZBufferParams);
                float d1 = LinearEyeDepth(SampleSceneDepth(uv1), _ZBufferParams);
                float d2 = LinearEyeDepth(SampleSceneDepth(uv2), _ZBufferParams);
                float d3 = LinearEyeDepth(SampleSceneDepth(uv3), _ZBufferParams);

                float3 nC = SampleSceneNormals(uv);

                // Ground seen at a shallow angle has a big depth gradient with no
                // actual edge in it. Raise the threshold as the surface turns away
                // from the camera so those don't smear into a haze of false lines.
                float3 posWS = ComputeWorldSpacePosition(uv, rawC, UNITY_MATRIX_I_VP);
                float3 V = GetWorldSpaceNormalizeViewDir(posWS);
                float graze = 1.0 - saturate(dot(normalize(nC), V));
                float threshold = _DepthThreshold * (1.0 + graze * graze * _GrazingRejection);

                // Normalised by centre depth so a thin gap reads the same near and far.
                float depthDiff = (abs(d1 - d0) + abs(d3 - d2)) / max(dC, 1e-4);
                float depthEdge = smoothstep(threshold, threshold * _DepthSoftness, depthDiff);

                float3 n0 = SampleSceneNormals(uv0);
                float3 n1 = SampleSceneNormals(uv1);
                float3 n2 = SampleSceneNormals(uv2);
                float3 n3 = SampleSceneNormals(uv3);
                float3 nd = abs(n1 - n0) + abs(n3 - n2);
                float normalDiff = sqrt(dot(nd, nd));
                float normalEdge = smoothstep(_NormalThreshold,
                                              _NormalThreshold + _NormalSoftness,
                                              normalDiff) * _NormalStrength;

                float edge = max(depthEdge, normalEdge);

                float fade = 1.0 - saturate((dC - _FadeStart) / max(_FadeEnd - _FadeStart, 0.001));
                edge *= fade * _Strength * _OutlineColor.a;

                // Tinting toward a darkened version of the surface reads as painted
                // linework; a flat black line reads as a filter sitting on top.
                half3 lineCol = lerp(_OutlineColor.rgb, _OutlineColor.rgb * src.rgb * 2.0h, _OutlineTint);
                return half4(lerp(src.rgb, lineCol, saturate(edge)), src.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
