using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The three warm pools of light in a market yard, and the dust turning over in the air.
    ///
    /// The yard was lit by the sun and nothing else, which is a strange thing for a building to be. It
    /// half worked because the live yard's roof comes off — so daylight really does pour into the one
    /// room the player is in — but the result was a shed with no interior of its own: every surface
    /// took the same light from the same angle and the room had no centre, no pools, and nowhere that
    /// looked warmer than anywhere else. The lights are what turn a floor plan into somewhere to stand.
    ///
    /// THE LIGHTS HAVE NO FIXTURES, and that is on purpose rather than unfinished. Pendant lampshades
    /// hung here first and they read wrong: this is a room whose ceiling is taken away while you are
    /// standing in it, so a lamp on a flex hangs off nothing, in the middle of the shot, at exactly the
    /// height the camera is looking through. What the room needed was the light, not the lamp — and a
    /// warm pool on the floor with the sky above it is what an open-roofed yard should look like anyway.
    ///
    /// THREE of them, not one per feature. The renderer is set to four additional lights per object and
    /// this is a phone; the yard the player is standing in is the only one whose lights are on, because
    /// the whole holder goes off with the rest of a parked yard. So the budget is three point lights in
    /// the entire hall, and they go where the player actually works: the queue lane he serves from, the
    /// stock pad he loads at, and the rank he buys from. None of them cast shadows — an additional
    /// light with shadows on is several times its own price and there is a directional light already
    /// doing that job for everything in the room.
    ///
    /// The dust is not lighting and is here anyway, because it is the same idea: the yard is a still
    /// photograph until something in it moves that the player did not move. It costs forty particles.
    ///
    /// A turning ceiling fan hung here too and went the same way as the lampshades, for the same
    /// reason: it was a fitting bolted to a ceiling the game takes away while you are standing under
    /// it, and it spun in the middle of the shot with nothing above it.
    /// </summary>
    public sealed class MarketYardLighting : MonoBehaviour
    {
        /// <summary>What the holder is called under the yard root, so the yard can park it.</summary>
        public const string HolderName = "Tavan";

        /// <summary>
        /// <summary>
        /// Where the three lights sit, and every one of them is over a job rather than over a gap: the
        /// queue lane the player serves from, the stock pad he loads at, the rank he buys from.
        ///
        /// Well BELOW the ceiling. A point light at roof height in a room this wide throws a pool
        /// twenty units across and lights nothing in particular; dropped to head height it lands on the
        /// floor as something you can see the edge of, which is the whole reason for having it. With no
        /// lampshade in the way there is nothing to stop it going where the light should be rather than
        /// where a fitting could hang.
        /// </summary>
        private static readonly Vector3[] LampSpots =
        {
            new Vector3(-9f, 3.4f, -13.2f),      // the queue lane, and the counter across it
            new Vector3(11f, 3.4f, 3f),          // the stock pad
            new Vector3(-17.5f, 3.4f, 7f),       // the north half of the upgrade rank
        };

        /// <summary>Which of the three is the bad one. See <see cref="Update"/>.</summary>
        private const int FlickerLamp = 2;

        private Light _flickerLight;
        private float _flickerBase;
        private float _phase;

        /// <summary>
        /// Lights the yard and hands back the holder. The light is pulled toward the island's ore so a
        /// copper yard is lit copper, but only part way: light is light, and a room lit emerald green
        /// stops reading as lit at all.
        /// </summary>
        public static MarketYardLighting Build(Transform yardRoot, Color accent, MarketTheme.Palette theme)
        {
            var holder = new GameObject(HolderName);
            holder.transform.SetParent(yardRoot, false);
            var rig = holder.AddComponent<MarketYardLighting>();
            rig.BuildAll(accent, theme);
            return rig;
        }

        private void BuildAll(Color accent, MarketTheme.Palette theme)
        {
            Color warm = Color.Lerp(new Color(1f, 0.86f, 0.64f), accent, 0.22f);

            for (int i = 0; i < LampSpots.Length; i++) Lamp(i, LampSpots[i], warm);
            Dust(accent);
        }

        private void Lamp(int index, Vector3 at, Color warm)
        {
            var mount = new GameObject("Isik_" + index, typeof(Light)).transform;
            mount.SetParent(transform, false);
            mount.localPosition = at;

            var light = mount.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = warm;
            light.intensity = 3.2f;
            light.range = 14f;
            // No shadows. On this renderer an additional light with shadows on is several times the
            // price of one without, and the sun is already casting for everything in the room.
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;

            if (index != FlickerLamp) return;
            _flickerLight = light;
            _flickerBase = light.intensity;
        }

        /// <summary>
        /// Dust hanging in the air. Forty particles over the whole floor, drifting up at walking pace
        /// and turning over — which is what a warehouse's air actually does under a fan.
        ///
        /// Unlit and small on purpose. Lit particles would take a per-particle light sample for the
        /// sake of forty specks, and specks any larger than this stop being dust and become snow.
        /// </summary>
        private void Dust(Color accent)
        {
            var go = new GameObject("Toz");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.2f, 0f);

            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.loop = true;
            main.duration = 8f;
            main.startLifetime = 9f;
            main.startSpeed = 0.16f;
            main.startSize = 0.075f;
            main.startColor = new Color(1f, 0.95f, 0.86f, 0.30f);
            main.maxParticles = 44;
            main.gravityModifier = -0.006f;              // barely; dust falls slower than it drifts
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = true;

            var emission = system.emission;
            emission.rateOverTime = 5f;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(38f, 3.2f, 30f);

            // Fade in and out at both ends of the life, or every mote pops into being and pops out.
            var over = system.colorOverLifetime;
            over.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f)
                });
            over.color = new ParticleSystem.MinMaxGradient(fade);

            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.28f;
            noise.frequency = 0.22f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            Shader unlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (unlit == null) unlit = Shader.Find("Sprites/Default");
            var mat = new Material(unlit);
            Color speck = Color.Lerp(new Color(1f, 0.96f, 0.88f), accent, 0.15f);
            mat.color = speck;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", speck);
            // Additive, so a mote crossing a dark wall still reads and one crossing a lit one does not
            // turn into a white dot stuck to it.
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 1f);
            mat.renderQueue = 3000;
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void Update()
        {
            if (_flickerLight == null) return;
            _phase += Time.deltaTime;

            // A bad tube rather than a candle. Most of the time it sits just under full and wanders a
            // little; every few seconds it drops out for a fraction of one. Two sines beating against
            // each other give the wander without a random number generator, and the dropout is a
            // threshold on the slower of the two — so it is irregular but never twice in a row.
            float wander = 0.94f + Mathf.Sin(_phase * 11.3f) * 0.03f + Mathf.Sin(_phase * 2.7f) * 0.03f;
            float slow = Mathf.Sin(_phase * 0.83f);
            float drop = slow > 0.985f ? 0.35f : 1f;
            float level = wander * drop;

            _flickerLight.intensity = _flickerBase * level;
        }
    }
}
