// The island's night lights: the bulb, the pool of light it throws on the road, and the shaft of
// air between the two.
//
// The lamp, window and headlight geometry the map generates carries emissive materials, but the
// _Overrides pass swaps those meshes for imported FBX models and switches the originals off, so
// none of that emission is ever drawn. Even when it was, a lamp head measures about 4x9 pixels at
// the game camera - too small to read once antialiasing has had it.
//
// So every light is drawn here instead, as three quads fed by IslandGlow.cs:
//
//   Bulb   a camera-facing disc at the lamp head. This is the source, not the effect - on its own
//          it is the round dot the lights used to be.
//   Pool   what the lamp actually lights. A camera-facing quad covering the lamp's reach, which
//          reads _CameraDepthTexture, rebuilds the world position of whatever surface is behind
//          each pixel, and shades it by distance and angle from the lamp. That is a point light
//          drawn as a screen-space decal: it lands on the road, the kerb or the wall - whatever is
//          genuinely under the lamp - with no ground height to guess and nothing to z-fight.
//   Beam   the visible shaft. A quad billboarded around the light's own AXIS rather than around the
//          camera, so it stays a cone hanging off the lamp head, and faded against scene depth so
//          it dissolves into the road instead of cutting a line across it.
//
// Two passes because the bulb and the other two want opposite depth tests: a bulb behind a hill
// must be hidden (ZTest LEqual), while a pool is *about* the geometry in front of it and has to
// draw over it (ZTest Always) - it does its own occlusion arithmetic from the depth buffer.
// SRPDefaultUnlit is the second LightMode URP's forward pass draws, which is what makes a second
// pass on one material possible at all.
Shader "Kayseri/IslandGlow"
{
    Properties
    {
        _Intensity("Bulb Intensity", Range(0,8)) = 0.75
        _Falloff("Bulb Edge Falloff", Range(0.5,6)) = 2.2
        // A halo centred exactly on the lamp is half-buried in the lamp's own housing, because the
        // override model is solid geometry at that same depth. Float it toward the camera.
        _DepthPush("Toward Camera", Range(0,4)) = 0.6

        [Header(Ground Pool)]
        _PoolIntensity("Pool Intensity", Range(0,6)) = 1.3
        // What a surface outside the pool still catches from the same lamp. At 0 a street lamp
        // lights the road and leaves its own post black, which reads as a hole rather than as
        // night; much above this and every lamp's spill sums into a flat haze over the island.
        _ConeFloor("Off Beam Amount", Range(0,1)) = 0.05
        // How much of the lamp's reach is spent coming up to full brightness. Keeps the lamp from
        // lighting its own post; too high and it stops reaching the road as well.
        _NearFade("Near Fade", Range(0.05,1)) = 0.5

        [Header(Beam)]
        _BeamIntensity("Beam Intensity", Range(0,4)) = 0.22
        // How deep into the geometry the shaft is allowed to fade out over. Too small and the beam
        // ends in a hard line on the road; too large and it disappears before it lands.
        _SoftFade("Depth Softness", Range(0.1,20)) = 4.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+100"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        // One CBUFFER shared by both passes, so the SRP Batcher still takes this material.
        CBUFFER_START(UnityPerMaterial)
            half _Intensity;
            half _Falloff;
            half _DepthPush;
            half _PoolIntensity;
            half _ConeFloor;
            half _NearFade;
            half _BeamIntensity;
            half _SoftFade;
        CBUFFER_END

        // Written by DayNightCycle. Zero by default, so with nothing driving it the lights are
        // simply absent rather than blazing away at noon.
        half _KayseriNight;

        // Shapes, in the order the mesh writes them into TEXCOORD2.x. A pool is aimed and has a
        // width; a wash is what a lit window does to the wall around it, which has neither.
        #define GLOW_BULB 0.0h
        #define GLOW_POOL 1.0h
        #define GLOW_WASH 2.0h
        #define GLOW_BEAM 3.0h

        struct Attributes
        {
            float4 positionOS : POSITION;   // every corner sits at the light's own position
            float3 axisWS     : NORMAL;     // where the light points: down for a lamp, ahead for a car
            float2 uv         : TEXCOORD0;  // 0..1 across the quad
            float2 corner     : TEXCOORD1;  // corner offset in world units, already sized
            float2 shape      : TEXCOORD2;  // x = shape, y = reach (pool range / beam length)
            float2 params     : TEXCOORD3;  // x = pool width on the ground, y = per-light brightness
            half4  color      : COLOR;      // per-light colour, brightness in alpha
        };

        // Off to the side of the frustum, with a valid w, so the clipper throws the triangle away.
        // Cheaper than clip() in the fragment: the quad is never rasterised at all.
        #define GLOW_DISCARD float4(-2.0, -2.0, -2.0, 1.0)
        ENDHLSL

        // ------------------------------------------------------------------ the bulb itself
        Pass
        {
            Name "Bulb"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One       // additive: light adds to what is behind it, never darkens it
            ZWrite Off
            ZTest LEqual        // a lamp behind a hill stays hidden
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.uv = IN.uv;
                OUT.color = IN.color;

                if (IN.shape.x > GLOW_BULB + 0.5h)
                {
                    OUT.positionCS = GLOW_DISCARD;
                    return OUT;
                }

                float3 centreWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 centreVS = TransformWorldToView(centreWS);

                // Expanding in view space is what makes these face the camera without any CPU work.
                centreVS.xy += IN.corner;
                centreVS.z += _DepthPush;       // view space looks down -Z, so +Z is toward the eye

                OUT.positionCS = TransformWViewToHClip(centreVS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 d = IN.uv * 2.0 - 1.0;
                half r = saturate(1.0 - dot(d, d));          // round, not square
                half a = pow(r, _Falloff) * IN.color.a;
                return half4(IN.color.rgb * a * _Intensity * _KayseriNight, 0.0h);
            }
            ENDHLSL
        }

        // ------------------------------------------------- what the light does to the world
        Pass
        {
            Name "Volume"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend One One
            ZWrite Off
            ZTest Always        // both shapes occlude themselves from the depth buffer instead
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 centreWS   : TEXCOORD1;
                half3  axisWS     : TEXCOORD2;
                float4 shape      : TEXCOORD3;  // xy = shape/reach, zw = pool width/brightness
                float  viewDepth  : TEXCOORD4;
                half4  color      : COLOR;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                OUT.shape = float4(IN.shape, IN.params);

                if (IN.shape.x < GLOW_POOL - 0.5h)
                {
                    OUT.positionCS = GLOW_DISCARD;
                    return OUT;
                }

                float3 centreWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 axisWS = SafeNormalize(IN.axisWS);
                OUT.centreWS = centreWS;
                OUT.axisWS = axisWS;

                float3 positionVS;
                if (IN.shape.x < GLOW_BEAM - 0.5h)
                {
                    // Pool: a camera-facing quad big enough to cover the lamp's reach on screen.
                    // Nothing about the pool's shape lives in this geometry - it is only the area
                    // the fragment shader is allowed to think about.
                    positionVS = TransformWorldToView(centreWS);
                    positionVS.xy += IN.corner;
                }
                else
                {
                    // Beam: billboarded around the light's axis, not around the camera, so the shaft
                    // stays welded to the lamp head and only spins to keep its widest face to us.
                    float3 toCamera = _WorldSpaceCameraPos - centreWS;
                    float3 side = SafeNormalize(cross(axisWS, toCamera));
                    positionVS = TransformWorldToView(centreWS + axisWS * IN.corner.y + side * IN.corner.x);

                    // Seen end-on there is no shaft to see, and the billboard would be edge-on and
                    // flicker. Fading it out is both the cheaper and the truer answer.
                    half endOn = length(cross(axisWS, SafeNormalize(toCamera)));
                    OUT.color.a *= saturate(endOn * 2.0h);
                }

                OUT.positionCS = TransformWViewToHClip(positionVS);
                OUT.viewDepth = -positionVS.z;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                half3 emission;

                if (IN.shape.x < GLOW_BEAM - 0.5h)
                {
                    // Rebuild the surface behind this pixel and light it as a point light would.
                    // Sky pixels come back at the far plane, land thousands of units away, and fall
                    // out of the attenuation on their own - no depth test needed.
                    float rawDepth = SampleSceneDepth(screenUV);
                    float3 surfaceWS = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);

                    float3 toSurface = surfaceWS - IN.centreWS;
                    float distance = length(toSurface);
                    half range = max(IN.shape.y, 1e-3h);

                    // Every light gets a plain distance falloff. On its own this is a bare bulb
                    // hanging in space - it is what a lit window does to the wall around it, and
                    // nothing more.
                    half bleed = saturate(1.0h - distance / range);
                    bleed *= bleed;

                    half attenuation;
                    if (IN.shape.x > GLOW_WASH - 0.5h)
                    {
                        attenuation = bleed;
                    }
                    else
                    {
                        // A street lamp is not a bulb, it is a bulb with a shade on it, and the
                        // difference is the whole effect. Split the surface's offset into how far
                        // DOWN the beam it sits and how far OUT to the side, and fall off on the
                        // two separately: the drop from the lamp head to the road costs almost
                        // nothing, while a step sideways off the pool costs everything. Falling
                        // off on the raw distance instead - which is what a point light does -
                        // spends most of the lamp's range just reaching the ground, and what
                        // arrives is an even haze with no edge to it.
                        half along = dot(toSurface, IN.axisWS);
                        half sideways = length(toSurface - IN.axisWS * along);

                        half depth = saturate(1.0h - along / range) * step(0.0h, along);

                        // Ramp in over the first stretch of the beam. Without it the lamp's own post
                        // hangs directly down the axis and takes the pool's full value along its
                        // whole length, so every lamp stands on a glowing white pole. A real lamp
                        // throws its light past its own housing, not onto it.
                        depth *= saturate(along / (range * _NearFade));

                        half width = IN.shape.z * (0.2h + 0.8h * saturate(along / range));
                        half across = saturate(1.0h - sideways / max(width, 1e-3h));

                        attenuation = depth * across * across + bleed * _ConeFloor;
                    }

                    emission = IN.color.rgb * (attenuation * IN.color.a * IN.shape.w * _PoolIntensity);
                }
                else
                {
                    // Soft-particle fade against the scene. Doubles as the beam's occlusion: a shaft
                    // behind a building has scene depth in front of it and comes out at zero.
                    float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    half soft = saturate((sceneDepth - IN.viewDepth) * rcp(max(_SoftFade, 1e-3h)));

                    half across = 1.0h - abs(IN.uv.x * 2.0h - 1.0h);
                    across *= across;
                    half along = 1.0h - IN.uv.y;
                    along *= along;
                    half head = saturate(IN.uv.y * 8.0h);   // the shaft starts below the housing

                    emission = IN.color.rgb * (across * along * head * soft * IN.color.a
                                               * IN.shape.w * _BeamIntensity);
                }

                return half4(emission * _KayseriNight, 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
