using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kayseri.IslandTools
{
    /// <summary>
    /// Gives every island its own ocean.
    ///
    /// The map generator writes one `sea` material into palette.json and every island's Sea quad
    /// remaps onto it, so all eight archipelago maps used to be surrounded by the same water. The
    /// islands are already told apart by their ore, their ground tint and their signature props
    /// (16_theme.py); the sea was the one surface that stayed identical, and it is the largest
    /// thing on screen.
    ///
    /// Three surfaces carry the theme, and they are swapped by MATERIAL rather than by object name
    /// so nothing has to know where the generator put them:
    ///
    ///   sea    the ocean quad in Terrain           -> IslandOceanWave or IslandOceanCrust
    ///   water  the river, where the island has one -> the same shader as its sea
    ///   foam   shore surf, waterfall spray and every ship's wake (parts.wake)
    ///                                              -> Kayseri/IslandVertexLit
    ///
    /// TWO ocean shaders, because two genuinely different things are being drawn and one shader
    /// doing both is what made the first attempt look like a lava lamp:
    ///
    ///   Wave    open liquid - tar, acid, rust slurry, mercury, algal bloom. Directional wave
    ///           trains, a narrow colour range and sun glitter.
    ///   Crust   a plated sheet - lava, molten gold, pack ice. Voronoi plates with the melt (or
    ///           the open water) showing in the cracks between them.
    ///
    /// Sea and river are the same liquid, so the river is DERIVED from the sea rather than authored
    /// twice - a finer pattern and a faster flow, because it is a narrow channel with a current in
    /// it rather than an open swell. Retuning an ocean therefore retunes its river automatically,
    /// which is the whole reason the two are not two tables.
    ///
    /// Foam stays on the lit shader: it is a scatter of small spheres, not a surface, and putting
    /// a wave field on a 2-unit blob buys nothing. It only needs to stop being white, which on a
    /// lava shore it very much has to be - hence `_EmissionAlways` on IslandVertexLit, so an ember
    /// foam glows at noon like the sea it is breaking on.
    ///
    /// These materials live OUTSIDE Materials/ on purpose: "Toggle Flat Colours" rewrites
    /// `_VertexColorAmount` across everything under that folder.
    /// </summary>
    public static class IslandOceans
    {
        private const string OceanRoot = "Assets/Art/KayseriIsland/Oceans";
        private const string PrefabRoot = "Assets/Prefabs/Island";
        private const string WaveShader = "Kayseri/IslandOceanWave";
        private const string CrustShader = "Kayseri/IslandOceanCrust";
        private const string LitShader = "Kayseri/IslandVertexLit";
        private const int PhaseCount = 3;

        /// <summary>The palette materials an ocean replaces. Matched exactly or with an island
        /// suffix, so re-running over an already-recoloured prefab re-targets rather than skips.
        /// None of the other 78 palette names begin with these plus an underscore - "seabed" is
        /// the near miss and it is neither "sea" nor "sea_".</summary>
        private static readonly string[] Liquids = { "sea", "foam", "water" };

        private enum Kind { Wave, Crust }

        /// <summary>
        /// Open liquid, as Kayseri/IslandOceanWave draws it. Colours are authored in sRGB, the way
        /// palette.json stores them, and converted on the way to the shader.
        ///
        /// Body and Crest are deliberately CLOSE together on every one of these. A wide spread is
        /// what made the previous pass read as poster paint: real liquid is one colour with light
        /// moving on it, and the light is what the wave field and the two specular lobes supply.
        /// </summary>
        private struct Wave
        {
            public Color Body, Crest, Cap;
            public float CrestBlend, CapLevel, CapWidth, CapAmount;
            public float Scale, Speed, Choppy, Bend, BendScale;
            public Vector2 Dir;
            public float Slope, Smoothness, Sheen, Glitter;
            public Color Sky;
            public float SkyAmount;
            public Color Glow;
            public float GlowStrength, NightGlow;
            public float Wrap, Ambient;
        }

        /// <summary>A plated sheet, as Kayseri/IslandOceanCrust draws it. The three crack colours
        /// run outward from the plate edge: shoulder, middle, core.</summary>
        private struct Crust
        {
            public Color Plate, PlateAlt;
            public float Variation, Grain;
            public Color CrackCool, CrackMid, CrackHot;
            public float CrackWidth;
            public float Scale, Drift, Warp, WarpScale, Heat;
            public Vector2 Dir;
            public float Relief, Smoothness, Specular;
            public float GlowStrength, NightGlow;
            public float Wrap, Ambient;
        }

        /// <summary>Surf, spray and ship wakes: a colour and a sheen, on the lit shader.</summary>
        private struct Spray
        {
            public Color Tint;
            public float Smoothness, Metallic, Specular, Saturation;
            public Color Glow;
            public float GlowStrength;
        }

        private struct Ocean
        {
            public string Island;
            public string Name;      // for the build log, and for whoever reads this table next
            public Kind Kind;
            public Wave Wave;        // read when Kind is Wave
            public Crust Crust;      // read when Kind is Crust
            public Spray Foam;
        }

        private static Color Rgb(float r, float g, float b) { return new Color(r, g, b, 1f); }

        // ─────────────────────────────────────────────────────────────────────── the eight oceans
        private static readonly Ocean[] Oceans =
        {
            // Crude oil. Almost no colour at all and almost no chop - it is viscous, so the swell
            // is long and smooth and Choppy stays near the bottom of its range. Everything you see
            // is the sheen: smoothness 0.96 with the hardest glitter of the five wave seas, and a
            // break that is grey-purple film rather than white water.
            new Ocean
            {
                Island = "Coal", Name = "tar", Kind = Kind.Wave,
                Wave = new Wave
                {
                    Body  = Rgb(0.090f, 0.086f, 0.115f),
                    Crest = Rgb(0.225f, 0.212f, 0.275f),
                    Cap   = Rgb(0.400f, 0.340f, 0.460f),
                    CrestBlend = 1.0f, CapLevel = 0.84f, CapWidth = 0.15f, CapAmount = 0.40f,
                    Scale = 0.045f, Speed = 0.30f, Choppy = 0.25f, Bend = 4.6f, BendScale = 0.170f,
                    Dir = new Vector2(1f, 0.35f),
                    Slope = 1.6f, Smoothness = 0.96f, Sheen = 0.70f, Glitter = 0.30f,
                    Sky = Rgb(0.20f, 0.21f, 0.30f), SkyAmount = 0.10f,
                    Wrap = 0.30f, Ambient = 0.45f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.19f, 0.17f, 0.20f),
                    Smoothness = 0.55f, Metallic = 0.05f, Specular = 0.45f, Saturation = 1.05f,
                },
            },

            // Sulphuric leach liquor, which is what the copper island's ponds are full of. Thin and
            // fast, so the shortest wavelength and the highest speed of the five, with a milky
            // yellow-green scum on the crests. Glows only faintly by day and is carried after dark
            // by NightGlow - which is how a real chemical pond reads.
            new Ocean
            {
                Island = "Copper", Name = "acid", Kind = Kind.Wave,
                Wave = new Wave
                {
                    Body  = Rgb(0.055f, 0.115f, 0.020f),
                    Crest = Rgb(0.300f, 0.460f, 0.045f),
                    Cap   = Rgb(0.720f, 0.820f, 0.220f),
                    CrestBlend = 1.0f, CapLevel = 0.78f, CapWidth = 0.16f, CapAmount = 0.75f,
                    Scale = 0.070f, Speed = 0.75f, Choppy = 0.45f, Bend = 4.4f, BendScale = 0.240f,
                    Dir = new Vector2(1f, -0.30f),
                    Slope = 2.2f, Smoothness = 0.82f, Sheen = 0.60f, Glitter = 0.22f,
                    Sky = Rgb(0.35f, 0.45f, 0.30f), SkyAmount = 0.12f,
                    Glow = Rgb(0.28f, 0.62f, 0.06f), GlowStrength = 0.12f, NightGlow = 1.6f,
                    Wrap = 0.35f, Ambient = 0.55f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.72f, 0.82f, 0.22f),
                    Smoothness = 0.35f, Metallic = 0f, Specular = 0.30f, Saturation = 1.30f,
                    Glow = Rgb(0.45f, 0.75f, 0.12f), GlowStrength = 0.18f,
                },
            },

            // Iron-oxide slurry: opaque, heavy and matte. The lowest sheen and glitter of the eight
            // on purpose - a crisp highlight is the single thing that makes a surface read as CLEAN
            // water, which is what this must not be. Its foam is dirty ochre, never white.
            new Ocean
            {
                Island = "Iron", Name = "rust", Kind = Kind.Wave,
                Wave = new Wave
                {
                    Body  = Rgb(0.068f, 0.026f, 0.012f),
                    Crest = Rgb(0.290f, 0.125f, 0.052f),
                    Cap   = Rgb(0.420f, 0.260f, 0.130f),
                    CrestBlend = 1.0f, CapLevel = 0.80f, CapWidth = 0.18f, CapAmount = 0.60f,
                    Scale = 0.055f, Speed = 0.35f, Choppy = 0.40f, Bend = 3.6f, BendScale = 0.190f,
                    Dir = new Vector2(1f, 0.55f),
                    Slope = 1.8f, Smoothness = 0.30f, Sheen = 0.60f, Glitter = 0.10f,
                    Sky = Rgb(0.30f, 0.26f, 0.22f), SkyAmount = 0.08f,
                    Wrap = 0.40f, Ambient = 0.55f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.42f, 0.26f, 0.13f),
                    Smoothness = 0.20f, Metallic = 0f, Specular = 0.20f, Saturation = 1.20f,
                },
            },

            // Mercury. Low viscosity, so the shortest, choppiest wave set of the five, and its read
            // comes almost entirely from two terms the others keep small: SkyAmount at 0.30, because
            // a metal is mostly a reflection of what is above it, and a glitter of 4 that turns the
            // sun into a hard broken path across the surface.
            new Ocean
            {
                Island = "Silver", Name = "mercury", Kind = Kind.Wave,
                Wave = new Wave
                {
                    Body  = Rgb(0.115f, 0.125f, 0.145f),
                    Crest = Rgb(0.620f, 0.650f, 0.700f),
                    Cap   = Rgb(0.950f, 0.960f, 1.000f),
                    CrestBlend = 1.0f, CapLevel = 0.82f, CapWidth = 0.10f, CapAmount = 0.70f,
                    Scale = 0.085f, Speed = 0.55f, Choppy = 0.55f, Bend = 4.0f, BendScale = 0.290f,
                    Dir = new Vector2(1f, 0.20f),
                    Slope = 2.6f, Smoothness = 0.97f, Sheen = 1.00f, Glitter = 0.45f,
                    Sky = Rgb(0.55f, 0.62f, 0.75f), SkyAmount = 0.30f,
                    Wrap = 0.20f, Ambient = 0.50f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.95f, 0.96f, 1.00f),
                    Smoothness = 0.92f, Metallic = 0.75f, Specular = 1.00f, Saturation = 0.85f,
                },
            },

            // Algal bloom: turbid green water with pale scum gathering on the crests. Like the acid
            // it is nearly unlit by day and lit at night, only more so - NightGlow 2.2 is the
            // highest of the eight, because bioluminescence is a thing you only ever see after dark.
            new Ocean
            {
                Island = "Emerald", Name = "toxic bloom", Kind = Kind.Wave,
                Wave = new Wave
                {
                    Body  = Rgb(0.020f, 0.085f, 0.055f),
                    Crest = Rgb(0.075f, 0.300f, 0.175f),
                    Cap   = Rgb(0.420f, 0.620f, 0.350f),
                    CrestBlend = 1.0f, CapLevel = 0.76f, CapWidth = 0.18f, CapAmount = 0.70f,
                    Scale = 0.065f, Speed = 0.45f, Choppy = 0.42f, Bend = 4.2f, BendScale = 0.220f,
                    Dir = new Vector2(1f, -0.45f),
                    Slope = 2.0f, Smoothness = 0.78f, Sheen = 0.58f, Glitter = 0.20f,
                    Sky = Rgb(0.30f, 0.42f, 0.38f), SkyAmount = 0.12f,
                    Glow = Rgb(0.05f, 0.55f, 0.28f), GlowStrength = 0.10f, NightGlow = 2.2f,
                    Wrap = 0.38f, Ambient = 0.55f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.45f, 0.65f, 0.38f),
                    Smoothness = 0.35f, Metallic = 0f, Specular = 0.30f, Saturation = 1.30f,
                    Glow = Rgb(0.20f, 0.70f, 0.35f), GlowStrength = 0.16f,
                },
            },

            // Lava, and the whole reason the crust shader exists. Dark basalt plates about 60 units
            // across, drifting at a crawl, with the melt showing only in the seams between them -
            // and the seam runs red at the shoulder, orange through the middle and yellow-white in
            // the core, which is the gradient that reads as heat rather than as a glowing line.
            //
            // Its foam is the opposite of foam: cooled black crust with embers in it. A white surf
            // line is what stops magma reading as magma.
            new Ocean
            {
                Island = "Ruby", Name = "lava", Kind = Kind.Crust,
                Crust = new Crust
                {
                    Plate     = Rgb(0.045f, 0.030f, 0.028f),
                    PlateAlt  = Rgb(0.090f, 0.064f, 0.058f),
                    Variation = 1.0f, Grain = 0.16f,
                    CrackCool = Rgb(0.420f, 0.055f, 0.010f),
                    CrackMid  = Rgb(0.950f, 0.300f, 0.020f),
                    CrackHot  = Rgb(1.000f, 0.800f, 0.300f),
                    CrackWidth = 0.30f,
                    Scale = 0.017f, Drift = 0.05f, Warp = 1.15f, WarpScale = 0.22f, Heat = 0.40f,
                    Dir = new Vector2(1f, 0.25f),
                    Relief = 1.4f, Smoothness = 0.20f, Specular = 0.20f,
                    GlowStrength = 2.4f, NightGlow = 0.8f,
                    Wrap = 0.30f, Ambient = 0.45f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.075f, 0.048f, 0.045f),
                    Smoothness = 0.20f, Metallic = 0f, Specular = 0.15f, Saturation = 1.15f,
                    Glow = Rgb(0.95f, 0.28f, 0.05f), GlowStrength = 0.55f,
                },
            },

            // Molten gold: the same sheet much closer to fully liquid. Smaller plates and a crack
            // half again as wide, so the melt is most of what you see and the crust is the minority
            // - the reverse of lava - and it keeps a real metallic highlight on top of the glow.
            new Ocean
            {
                Island = "Gold", Name = "molten gold", Kind = Kind.Crust,
                Crust = new Crust
                {
                    Plate     = Rgb(0.115f, 0.070f, 0.022f),
                    PlateAlt  = Rgb(0.175f, 0.115f, 0.040f),
                    Variation = 0.85f, Grain = 0.14f,
                    CrackCool = Rgb(0.620f, 0.280f, 0.020f),
                    CrackMid  = Rgb(1.000f, 0.620f, 0.055f),
                    CrackHot  = Rgb(1.000f, 0.940f, 0.620f),
                    CrackWidth = 0.34f,
                    Scale = 0.026f, Drift = 0.09f, Warp = 1.05f, WarpScale = 0.24f, Heat = 0.45f,
                    Dir = new Vector2(1f, 0.30f),
                    Relief = 1.0f, Smoothness = 0.55f, Specular = 0.55f,
                    GlowStrength = 2.0f, NightGlow = 0.7f,
                    Wrap = 0.30f, Ambient = 0.50f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(1.00f, 0.80f, 0.35f),
                    Smoothness = 0.65f, Metallic = 0.45f, Specular = 0.75f, Saturation = 1.30f,
                    Glow = Rgb(1.00f, 0.72f, 0.25f), GlowStrength = 0.90f,
                },
            },

            // Pack ice - the same plated sheet at the other end of the thermometer. Big floes,
            // barely drifting, and the three crack colours run the other way: the pale wet edge of
            // a floe, then a shallow lead, then deep open water in the core. Emission is 0, so the
            // whole glow term compiles down to nothing. Its foam is the only white one left, and
            // here it is snow.
            new Ocean
            {
                Island = "Diamond", Name = "frozen", Kind = Kind.Crust,
                Crust = new Crust
                {
                    Plate     = Rgb(0.880f, 0.925f, 0.960f),
                    PlateAlt  = Rgb(0.970f, 0.990f, 1.000f),
                    Variation = 1.0f, Grain = 0.12f,
                    CrackCool = Rgb(0.620f, 0.780f, 0.860f),
                    CrackMid  = Rgb(0.280f, 0.520f, 0.660f),
                    CrackHot  = Rgb(0.085f, 0.235f, 0.400f),
                    CrackWidth = 0.22f,
                    Scale = 0.013f, Drift = 0.012f, Warp = 0.95f, WarpScale = 0.20f, Heat = 0.40f,
                    Dir = new Vector2(1f, 0.15f),
                    Relief = 0.9f, Smoothness = 0.75f, Specular = 0.75f,
                    GlowStrength = 0f, NightGlow = 0f,
                    Wrap = 0.35f, Ambient = 0.60f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.97f, 0.99f, 1.00f),
                    Smoothness = 0.70f, Metallic = 0f, Specular = 0.60f, Saturation = 1.00f,
                },
            },
        };

        // ───────────────────────────────────────────────────────────────────────────── materials
        [MenuItem("Kayseri/Island/Create Ocean Materials", false, 22)]
        public static void CreateMaterials()
        {
            var wave = Shader.Find(WaveShader);
            var crust = Shader.Find(CrustShader);
            var lit = Shader.Find(LitShader);
            if (wave == null || crust == null || lit == null)
            {
                Debug.LogError("[Ocean] Island shaders not found - let Unity compile them first.");
                return;
            }

            EnsureFolder(OceanRoot);
            int written = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var o in Oceans)
                {
                    if (o.Kind == Kind.Wave)
                    {
                        WriteWave(wave, "sea", o.Island, o.Wave);
                        WriteWave(wave, "water", o.Island, RiverWave(o.Wave));
                    }
                    else
                    {
                        WriteCrust(crust, "sea", o.Island, o.Crust);
                        WriteCrust(crust, "water", o.Island, RiverCrust(o.Crust));
                    }
                    WriteSpray(lit, "foam", o.Island, o.Foam);
                    written += 3;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[Ocean] {written} ocean materials in {OceanRoot}.");
        }

        /// <summary>
        /// The same liquid in a channel a few units wide instead of an open swell: shorter waves,
        /// because a river shows one flat patch of an open-sea wavelength, and faster, because a
        /// river has a current. Derived rather than authored so retuning a sea retunes its river.
        /// </summary>
        private static Wave RiverWave(Wave sea)
        {
            sea.Scale *= 3.0f;
            sea.Speed = sea.Speed * 1.8f + 0.15f;
            sea.Slope *= 1.25f;
            sea.CapLevel -= 0.06f;      // a river breaks more readily than open water
            return sea;
        }

        /// <summary>A river of the same sheet: smaller plates, drifting faster, because a channel
        /// wide enough for two plates has to show more than two.</summary>
        private static Crust RiverCrust(Crust sea)
        {
            sea.Scale *= 3.4f;
            sea.Drift = sea.Drift * 2.5f + 0.03f;
            sea.CrackWidth = Mathf.Min(1f, sea.CrackWidth * 1.2f);
            return sea;
        }

        private static void WriteWave(Shader shader, string liquid, string island, Wave w)
        {
            var mat = Load(shader, $"{OceanRoot}/{liquid}_{island}.mat");

            mat.SetColor("_DeepColor", ToLinear(w.Body, 1f));
            mat.SetColor("_ShallowColor", ToLinear(w.Crest, 1f));
            mat.SetColor("_FoamColor", ToLinear(w.Cap, 1f));
            mat.SetFloat("_Depth", w.CrestBlend);
            mat.SetFloat("_FoamLevel", w.CapLevel);
            mat.SetFloat("_FoamWidth", w.CapWidth);
            mat.SetFloat("_FoamAmount", w.CapAmount);

            mat.SetFloat("_Scale", w.Scale);
            mat.SetFloat("_WaveSpeed", w.Speed);
            mat.SetFloat("_Choppy", w.Choppy);
            mat.SetFloat("_Bend", w.Bend);
            mat.SetFloat("_BendScale", w.BendScale);
            mat.SetVector("_FlowDir", new Vector4(w.Dir.x, w.Dir.y, 0f, 0f));

            mat.SetFloat("_NormalStrength", w.Slope);
            mat.SetFloat("_Smoothness", w.Smoothness);
            mat.SetFloat("_SpecularStrength", w.Sheen);
            mat.SetFloat("_GlitterStrength", w.Glitter);
            mat.SetColor("_SkyTint", ToLinear(w.Sky, 1f));
            mat.SetFloat("_SkyAmount", w.SkyAmount);

            mat.SetFloat("_Wrap", w.Wrap);
            mat.SetFloat("_AmbientAmount", w.Ambient);
            mat.SetColor("_SpecularTint", Color.white);
            mat.SetColor("_ShadowTint", ToLinear(Rgb(0.30f, 0.38f, 0.55f), 1f));

            SetGlow(mat, w.GlowStrength > 0f, ToLinear(w.Glow, 1f), w.GlowStrength, w.NightGlow);

            mat.renderQueue = 2000;
            EditorUtility.SetDirty(mat);
        }

        private static void WriteCrust(Shader shader, string liquid, string island, Crust c)
        {
            var mat = Load(shader, $"{OceanRoot}/{liquid}_{island}.mat");

            mat.SetColor("_CrustColor", ToLinear(c.Plate, 1f));
            mat.SetColor("_CrustColor2", ToLinear(c.PlateAlt, 1f));
            mat.SetFloat("_PlateVariation", c.Variation);
            mat.SetFloat("_Grain", c.Grain);

            mat.SetColor("_CrackCool", ToLinear(c.CrackCool, 1f));
            mat.SetColor("_CrackMid", ToLinear(c.CrackMid, 1f));
            mat.SetColor("_CrackHot", ToLinear(c.CrackHot, 1f));
            mat.SetFloat("_CrackWidth", c.CrackWidth);

            mat.SetFloat("_Scale", c.Scale);
            mat.SetFloat("_DriftSpeed", c.Drift);
            mat.SetFloat("_Warp", c.Warp);
            mat.SetFloat("_WarpScale", c.WarpScale);
            mat.SetFloat("_HeatAmount", c.Heat);
            mat.SetVector("_FlowDir", new Vector4(c.Dir.x, c.Dir.y, 0f, 0f));

            mat.SetFloat("_NormalStrength", c.Relief);
            mat.SetFloat("_Smoothness", c.Smoothness);
            mat.SetFloat("_SpecularStrength", c.Specular);

            mat.SetFloat("_Wrap", c.Wrap);
            mat.SetFloat("_AmbientAmount", c.Ambient);
            mat.SetColor("_SpecularTint", Color.white);
            mat.SetColor("_ShadowTint", ToLinear(Rgb(0.30f, 0.34f, 0.48f), 1f));

            // The cracks emit in their own colour, so there is no emission colour to set here -
            // only how much, and how much more of it after dark.
            mat.SetFloat("_Emission", c.GlowStrength);
            mat.SetFloat("_EmissionNight", c.NightGlow);
            if (c.GlowStrength > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            mat.renderQueue = 2000;
            EditorUtility.SetDirty(mat);
        }

        private static void WriteSpray(Shader shader, string liquid, string island, Spray s)
        {
            var mat = Load(shader, $"{OceanRoot}/{liquid}_{island}.mat");

            mat.SetColor("_BaseColor", ToLinear(s.Tint, 1f));
            // The foam bake is Blender's white spray, which would wash every one of these back
            // toward white - the exact problem on a lava shore.
            mat.SetFloat("_VertexColorAmount", 0f);
            mat.SetFloat("_Metallic", Mathf.Clamp01(s.Metallic));
            mat.SetFloat("_Smoothness", Mathf.Clamp01(s.Smoothness));
            mat.SetFloat("_SpecularStrength", Mathf.Clamp(s.Specular, 0f, 2f));
            mat.SetFloat("_Saturation", Mathf.Clamp(s.Saturation, 0f, 3f));
            mat.SetFloat("_DetailStrength", 0.08f);
            mat.SetFloat("_DetailScale", 0.35f);

            if (s.GlowStrength > 0f)
            {
                mat.SetColor("_EmissionColor", ToLinear(s.Glow, s.GlowStrength));
                // Ember foam on a lava shore has to burn at noon, not wait for the street lamps.
                mat.SetFloat("_EmissionAlways", 1f);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.SetColor("_EmissionColor", Color.black);
                mat.SetFloat("_EmissionAlways", 0f);
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            mat.renderQueue = 2000;
            EditorUtility.SetDirty(mat);
        }

        private static void SetGlow(Material mat, bool on, Color glow, float strength, float night)
        {
            if (on)
            {
                mat.SetColor("_EmissionColor", glow);
                mat.SetFloat("_Emission", strength);
                mat.SetFloat("_EmissionNight", night);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.SetColor("_EmissionColor", Color.black);
                mat.SetFloat("_Emission", 0f);
                mat.SetFloat("_EmissionNight", 0f);
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
        }

        private static Material Load(Shader shader, string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }
            return mat;
        }

        // ───────────────────────────────────────────────────────────────────────────────── apply
        /// <summary>
        /// Points every sea, foam and river renderer under <paramref name="root"/> at this
        /// island's ocean. Called from IslandBuilder.BuildPhasePrefabs before the prefab is
        /// written, which is what makes the recolour survive a re-export - same reason the model
        /// overrides are stamped on there rather than living inside the prefab.
        /// </summary>
        public static int Apply(GameObject root, string island)
        {
            var lookup = LookupFor(island);
            if (lookup == null) return 0;

            int touched = 0;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = renderers[i].sharedMaterials;
                bool changed = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null) continue;
                    string liquid = LiquidOf(mats[m].name);
                    if (liquid == null) continue;
                    var swap = lookup[liquid];
                    if (swap == null || mats[m] == swap) continue;
                    mats[m] = swap;
                    changed = true;
                }
                if (!changed) continue;
                renderers[i].sharedMaterials = mats;
                touched++;
            }
            return touched;
        }

        /// <summary>
        /// Recolours the oceans on the phase prefabs that are already built, without going near
        /// the FBX. This is the cheap path: a full Build All rewrites the prefabs from the models
        /// and takes the scene with it, and nothing about an ocean needs that.
        /// </summary>
        [MenuItem("Kayseri/Island/Recolour Oceans (every island)", false, 23)]
        public static void RecolourAll()
        {
            CreateMaterials();

            int prefabs = 0, renderers = 0;
            foreach (var ocean in Oceans)
            {
                for (int phase = 1; phase <= PhaseCount; phase++)
                {
                    string path = $"{PrefabRoot}/{ocean.Island}/Island_Phase{phase}.prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;

                    var contents = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        int n = Apply(contents, ocean.Island);
                        if (n == 0) continue;
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        prefabs++;
                        renderers += n;
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(contents);
                    }
                }
                Debug.Log($"[Ocean] {ocean.Island}: {ocean.Name} sea.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Ocean] Recoloured {renderers} renderers across {prefabs} phase prefabs. "
                    + "Zero is expected when only the materials changed - the prefabs already "
                    + "point at them.");
        }

        // ─────────────────────────────────────────────────────────────────────────────── helpers
        /// <summary>Which of the three liquids a material is, whether it is still the shared
        /// palette one or an ocean it was pointed at on an earlier run.</summary>
        private static string LiquidOf(string materialName)
        {
            for (int i = 0; i < Liquids.Length; i++)
            {
                if (materialName == Liquids[i]) return Liquids[i];
                if (materialName.StartsWith(Liquids[i] + "_", System.StringComparison.Ordinal))
                    return Liquids[i];
            }
            return null;
        }

        private static Dictionary<string, Material> LookupFor(string island)
        {
            bool known = false;
            foreach (var ocean in Oceans) if (ocean.Island == island) { known = true; break; }
            if (!known) return null;

            var lookup = new Dictionary<string, Material>(Liquids.Length);
            foreach (var liquid in Liquids)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{OceanRoot}/{liquid}_{island}.mat");
                if (mat == null)
                {
                    Debug.LogWarning($"[Ocean] {liquid}_{island}.mat missing - run Create Ocean Materials.");
                    return null;
                }
                lookup[liquid] = mat;
            }
            return lookup;
        }

        /// <summary>Authored sRGB to what the shader is handed raw, matching IslandBuilder.ToColor.
        /// Skipping this in a Linear project reads about a stop and a half too bright.</summary>
        private static Color ToLinear(Color c, float scale)
        {
            if (PlayerSettings.colorSpace == ColorSpace.Linear) c = c.linear;
            return new Color(c.r * scale, c.g * scale, c.b * scale, 1f);
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
