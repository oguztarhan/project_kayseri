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
    ///   sea    the ocean quad in Terrain          -> Kayseri/IslandOcean
    ///   water  the river, where the island has one -> Kayseri/IslandOcean
    ///   foam   shore surf, waterfall spray and every ship's wake (parts.wake)
    ///                                              -> Kayseri/IslandVertexLit
    ///
    /// Sea and river are the same liquid, so the river is DERIVED from the sea rather than authored
    /// twice - a finer pattern and a faster flow, because it is a narrow channel with a current in
    /// it rather than an open swell. Retuning an ocean therefore retunes its river automatically,
    /// which is the whole reason the two are not two tables.
    ///
    /// Foam stays on the lit shader: it is a scatter of small spheres, not a surface, and putting
    /// flowing noise on a 2-unit blob buys nothing. It only needs to stop being white, which on a
    /// lava sea it very much has to be - hence `_EmissionAlways` on IslandVertexLit, so an ember
    /// foam glows at noon like the sea it is breaking on.
    ///
    /// These materials live OUTSIDE Materials/ on purpose: "Toggle Flat Colours" rewrites
    /// `_VertexColorAmount` across everything under that folder.
    /// </summary>
    public static class IslandOceans
    {
        private const string OceanRoot = "Assets/Art/KayseriIsland/Oceans";
        private const string PrefabRoot = "Assets/Prefabs/Island";
        private const string OceanShader = "Kayseri/IslandOcean";
        private const string LitShader = "Kayseri/IslandVertexLit";
        private const int PhaseCount = 3;

        /// <summary>The palette materials an ocean replaces. Matched exactly or with an island
        /// suffix, so re-running over an already-recoloured prefab re-targets rather than skips.
        /// None of the other 78 palette names begin with these plus an underscore - "seabed" is
        /// the near miss and it is neither "sea" nor "sea_".</summary>
        private static readonly string[] Liquids = { "sea", "foam", "water" };

        /// <summary>
        /// One liquid surface, as Kayseri/IslandOcean draws it. Colours are authored in sRGB, the
        /// way palette.json stores them, and converted on the way to the shader.
        ///
        /// The three colours are the point: a sea is Deep AND Mid AND Hot at the same moment, in
        /// moving patches, which is what the previous flat-tint pass was missing. Band/Sharp decide
        /// how much of each is showing and how hard the edge between them is.
        /// </summary>
        private struct Surface
        {
            public Color Deep, Mid, Hot, Vein;
            public float BandLow, BandHigh, BandSharp;
            public float Scale, FlowSpeed, Warp, Stretch;
            public Vector2 Flow;
            public float VeinLevel, VeinWidth, VeinStrength;
            public float Ripple, Smoothness, Specular;
            public Color Glow;            // emission colour
            public float GlowStrength;    // over the whole surface
            public float GlowFloor;       // what the troughs keep - 0 reads as paint, not heat
            public float VeinGlow;        // extra along the cracks
            public float NightGlow;       // lava does not cool at sunset
            public float ToonSteps, ToonSoft;
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
            public Surface Sea;
            public Spray Foam;
        }

        private static Color Rgb(float r, float g, float b) { return new Color(r, g, b, 1f); }

        // ─────────────────────────────────────────────────────────────────────── the eight oceans
        private static readonly Ocean[] Oceans =
        {
            // Tar. Viscous, so the flow is the slowest of the eight and the swirl is high - thick
            // liquid folds rather than ripples. Near-black, so the ONLY thing that reads on it is
            // the sheen: smoothness 0.93 and a hard specular, with a petrol-iridescent vein.
            new Ocean
            {
                Island = "Coal", Name = "tar",
                Sea = new Surface
                {
                    Deep = Rgb(0.020f, 0.019f, 0.025f),
                    Mid  = Rgb(0.082f, 0.078f, 0.094f),
                    Hot  = Rgb(0.310f, 0.305f, 0.365f),
                    Vein = Rgb(0.370f, 0.220f, 0.430f),      // the rainbow film on standing oil
                    BandLow = 0.24f, BandHigh = 0.55f, BandSharp = 0.30f,
                    Scale = 0.013f, FlowSpeed = 0.14f, Warp = 0.42f, Stretch = 0.40f,
                    Flow = new Vector2(1f, 0.40f),
                    VeinLevel = 0.55f, VeinWidth = 0.12f, VeinStrength = 0.45f,
                    Ripple = 1.6f, Smoothness = 0.93f, Specular = 1.30f,
                    ToonSteps = 4f, ToonSoft = 0.70f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.34f, 0.33f, 0.31f),
                    Smoothness = 0.35f, Metallic = 0.05f, Specular = 0.35f, Saturation = 1.10f,
                },
            },

            // Acid. Yellow-green rather than the verdigris the leach ponds are: verdigris put this
            // sea within a few degrees of emerald's, and those two are the pair most likely to be
            // seen one after the other on the island select. Fast, thin, frothing.
            new Ocean
            {
                Island = "Copper", Name = "acid",
                Sea = new Surface
                {
                    Deep = Rgb(0.100f, 0.220f, 0.020f),
                    Mid  = Rgb(0.340f, 0.550f, 0.040f),
                    Hot  = Rgb(0.720f, 0.880f, 0.120f),
                    Vein = Rgb(0.900f, 1.000f, 0.350f),      // froth streaks on the surface film
                    BandLow = 0.28f, BandHigh = 0.58f, BandSharp = 0.20f,
                    Scale = 0.016f, FlowSpeed = 0.34f, Warp = 0.52f, Stretch = 0.38f,
                    Flow = new Vector2(1f, -0.30f),
                    VeinLevel = 0.48f, VeinWidth = 0.07f, VeinStrength = 0.35f,
                    Ripple = 1.9f, Smoothness = 0.82f, Specular = 0.55f,
                    Glow = Rgb(0.55f, 1.00f, 0.10f),
                    GlowStrength = 0.30f, GlowFloor = 0.25f, VeinGlow = 0.90f, NightGlow = 1.0f,
                    ToonSteps = 4f, ToonSoft = 0.70f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.88f, 0.97f, 0.55f),
                    Smoothness = 0.35f, Metallic = 0f, Specular = 0.35f, Saturation = 1.40f,
                    Glow = Rgb(0.80f, 1.00f, 0.35f), GlowStrength = 0.30f,
                },
            },

            // Rust. Iron oxide in suspension - thick, matte, opaque. Deliberately the least shiny
            // of the eight: a crisp highlight is what makes a surface read as CLEAN water, which is
            // the one thing this is not.
            new Ocean
            {
                Island = "Iron", Name = "rust",
                Sea = new Surface
                {
                    Deep = Rgb(0.070f, 0.024f, 0.011f),
                    Mid  = Rgb(0.360f, 0.145f, 0.058f),
                    Hot  = Rgb(0.680f, 0.360f, 0.150f),
                    Vein = Rgb(0.780f, 0.520f, 0.260f),      // scum lines where the sludge separates
                    BandLow = 0.28f, BandHigh = 0.58f, BandSharp = 0.17f,
                    Scale = 0.015f, FlowSpeed = 0.16f, Warp = 0.60f, Stretch = 0.44f,
                    Flow = new Vector2(1f, 0.55f),
                    VeinLevel = 0.53f, VeinWidth = 0.10f, VeinStrength = 0.32f,
                    Ripple = 1.7f, Smoothness = 0.38f, Specular = 0.22f,
                    ToonSteps = 4f, ToonSoft = 0.75f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.70f, 0.48f, 0.32f),
                    Smoothness = 0.25f, Metallic = 0f, Specular = 0.25f, Saturation = 1.25f,
                },
            },

            // Mercury. The only ocean that gets HARD bands: BandSharp 0.10 and six toon steps, so
            // the ramp posterises into sheets of light instead of blending. That, plus a ripple and
            // specular well above everything else, is how a metal reads without a reflection probe.
            new Ocean
            {
                Island = "Silver", Name = "mercury",
                Sea = new Surface
                {
                    Deep = Rgb(0.240f, 0.270f, 0.330f),
                    Mid  = Rgb(0.620f, 0.660f, 0.720f),
                    Hot  = Rgb(0.970f, 0.990f, 1.000f),
                    Vein = Rgb(1.000f, 1.000f, 1.000f),
                    BandLow = 0.26f, BandHigh = 0.50f, BandSharp = 0.13f,
                    Scale = 0.014f, FlowSpeed = 0.24f, Warp = 0.34f, Stretch = 0.36f,
                    Flow = new Vector2(1f, 0.20f),
                    VeinLevel = 0.50f, VeinWidth = 0.06f, VeinStrength = 0.35f,
                    Ripple = 2.6f, Smoothness = 0.96f, Specular = 1.90f,
                    ToonSteps = 6f, ToonSoft = 0.25f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.90f, 0.92f, 0.96f),
                    Smoothness = 0.88f, Metallic = 0.70f, Specular = 0.90f, Saturation = 0.85f,
                },
            },

            // Molten gold. Metal that is glowing rather than burning: it keeps a real metallic
            // highlight on top of the emission, where ruby has almost none. Cooler crust in the
            // troughs, orange body, yellow crest.
            new Ocean
            {
                Island = "Gold", Name = "molten gold",
                Sea = new Surface
                {
                    Deep = Rgb(0.220f, 0.090f, 0.010f),
                    Mid  = Rgb(0.800f, 0.420f, 0.030f),
                    Hot  = Rgb(1.000f, 0.850f, 0.300f),
                    Vein = Rgb(1.000f, 0.950f, 0.600f),
                    BandLow = 0.24f, BandHigh = 0.58f, BandSharp = 0.24f,
                    Scale = 0.015f, FlowSpeed = 0.17f, Warp = 0.62f, Stretch = 0.85f,
                    Flow = new Vector2(1f, 0.30f),
                    VeinLevel = 0.50f, VeinWidth = 0.075f, VeinStrength = 0.80f,
                    Ripple = 1.4f, Smoothness = 0.68f, Specular = 0.50f,
                    Glow = Rgb(1.00f, 0.72f, 0.18f),
                    GlowStrength = 1.10f, GlowFloor = 0.30f, VeinGlow = 2.20f, NightGlow = 0.9f,
                    ToonSteps = 4f, ToonSoft = 0.70f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(1.00f, 0.90f, 0.48f),
                    Smoothness = 0.60f, Metallic = 0.40f, Specular = 0.70f, Saturation = 1.40f,
                    Glow = Rgb(1.00f, 0.88f, 0.38f), GlowStrength = 1.35f,
                },
            },

            // Lava, and the reason the three-colour ramp exists at all: black crust in the troughs,
            // RED through the body, ORANGE at the crest and a YELLOW vein burning between the
            // plates - all four visible at once and trading places as the flow drags them. Slowest
            // flow bar tar and the widest swirl, because magma folds rather than sloshes.
            //
            // Its foam is the opposite of foam: cooled black crust with embers in it. A white surf
            // line is what stops magma reading as magma.
            new Ocean
            {
                Island = "Ruby", Name = "lava",
                Sea = new Surface
                {
                    Deep = Rgb(0.090f, 0.012f, 0.006f),
                    Mid  = Rgb(0.620f, 0.090f, 0.012f),
                    Hot  = Rgb(1.000f, 0.420f, 0.030f),
                    Vein = Rgb(1.000f, 0.820f, 0.220f),
                    BandLow = 0.22f, BandHigh = 0.56f, BandSharp = 0.22f,
                    Scale = 0.013f, FlowSpeed = 0.14f, Warp = 0.68f, Stretch = 0.90f,
                    Flow = new Vector2(1f, 0.25f),
                    VeinLevel = 0.50f, VeinWidth = 0.085f, VeinStrength = 1.00f,
                    Ripple = 1.2f, Smoothness = 0.40f, Specular = 0.25f,
                    Glow = Rgb(1.00f, 0.38f, 0.06f),
                    GlowStrength = 1.50f, GlowFloor = 0.18f, VeinGlow = 3.00f, NightGlow = 1.2f,
                    ToonSteps = 4f, ToonSoft = 0.75f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.10f, 0.050f, 0.045f),
                    Smoothness = 0.20f, Metallic = 0f, Specular = 0.20f, Saturation = 1.20f,
                    Glow = Rgb(0.95f, 0.25f, 0.04f), GlowStrength = 0.45f,
                },
            },

            // Toxic bloom. Algal, not molten: the glow comes off the whole surface rather than out
            // of cracks in it, so GlowFloor is high and VeinGlow low - the reverse of lava. The
            // highest NightGlow of the eight, because bioluminescence is a night thing.
            new Ocean
            {
                Island = "Emerald", Name = "toxic bloom",
                Sea = new Surface
                {
                    Deep = Rgb(0.005f, 0.090f, 0.060f),
                    Mid  = Rgb(0.030f, 0.340f, 0.200f),
                    Hot  = Rgb(0.220f, 0.780f, 0.420f),
                    Vein = Rgb(0.550f, 1.000f, 0.620f),
                    BandLow = 0.26f, BandHigh = 0.60f, BandSharp = 0.24f,
                    Scale = 0.018f, FlowSpeed = 0.28f, Warp = 0.50f, Stretch = 0.40f,
                    Flow = new Vector2(1f, -0.45f),
                    VeinLevel = 0.49f, VeinWidth = 0.08f, VeinStrength = 0.40f,
                    Ripple = 1.8f, Smoothness = 0.74f, Specular = 0.45f,
                    Glow = Rgb(0.10f, 0.95f, 0.45f),
                    GlowStrength = 0.55f, GlowFloor = 0.55f, VeinGlow = 1.30f, NightGlow = 1.4f,
                    ToonSteps = 4f, ToonSoft = 0.70f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.60f, 1.00f, 0.80f),
                    Smoothness = 0.35f, Metallic = 0f, Specular = 0.35f, Saturation = 1.45f,
                    Glow = Rgb(0.30f, 1.00f, 0.68f), GlowStrength = 0.60f,
                },
            },

            // Frozen. The one ocean that is not liquid, and it is built out of the same parts read
            // backwards: near-zero flow, a narrow BandSharp so the ramp breaks into hard PLATES of
            // pack ice, and a vein that is DARKER than the ice it cuts, because the crack between
            // two floes is open water. Its foam is the only white one left; here it is snow.
            new Ocean
            {
                Island = "Diamond", Name = "frozen",
                Sea = new Surface
                {
                    Deep = Rgb(0.185f, 0.400f, 0.580f),      // meltwater between the floes
                    Mid  = Rgb(0.615f, 0.815f, 0.900f),
                    Hot  = Rgb(0.985f, 1.000f, 1.000f),      // snow lying on top of one
                    Vein = Rgb(0.255f, 0.495f, 0.680f),      // a fissure, so it goes DOWN in value
                    BandLow = 0.34f, BandHigh = 0.56f, BandSharp = 0.07f,
                    Scale = 0.011f, FlowSpeed = 0.05f, Warp = 0.28f, Stretch = 0.75f,
                    Flow = new Vector2(1f, 0.15f),
                    VeinLevel = 0.47f, VeinWidth = 0.035f, VeinStrength = 0.80f,
                    Ripple = 0.7f, Smoothness = 0.90f, Specular = 0.70f,
                    ToonSteps = 5f, ToonSoft = 0.40f,
                },
                Foam = new Spray
                {
                    Tint = Rgb(0.98f, 0.99f, 1.00f),
                    Smoothness = 0.70f, Metallic = 0f, Specular = 0.60f, Saturation = 1.00f,
                },
            },
        };

        // ───────────────────────────────────────────────────────────────────────────── materials
        [MenuItem("Kayseri/Island/Create Ocean Materials", false, 22)]
        public static void CreateMaterials()
        {
            var ocean = Shader.Find(OceanShader);
            var lit = Shader.Find(LitShader);
            if (ocean == null || lit == null)
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
                    WriteSurface(ocean, "sea", o.Island, o.Sea);
                    WriteSurface(ocean, "water", o.Island, River(o.Sea));
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
        /// The same liquid seen in a channel a few units wide instead of an open swell: the pattern
        /// has to be finer or a river shows one flat patch of it, and it runs faster because a river
        /// has a current. Derived rather than authored so retuning a sea retunes its river too.
        /// </summary>
        private static Surface River(Surface sea)
        {
            sea.Scale *= 3.2f;
            sea.FlowSpeed = sea.FlowSpeed * 2.2f + 0.08f;   // the floor is for ice, which barely moves
            sea.Ripple *= 1.3f;
            sea.VeinWidth *= 0.8f;
            return sea;
        }

        private static void WriteSurface(Shader shader, string liquid, string island, Surface s)
        {
            var mat = Load(shader, $"{OceanRoot}/{liquid}_{island}.mat");

            mat.SetColor("_DeepColor", ToLinear(s.Deep, 1f));
            mat.SetColor("_MidColor", ToLinear(s.Mid, 1f));
            mat.SetColor("_HotColor", ToLinear(s.Hot, 1f));
            mat.SetColor("_VeinColor", ToLinear(s.Vein, 1f));
            mat.SetFloat("_BandLow", s.BandLow);
            mat.SetFloat("_BandHigh", s.BandHigh);
            mat.SetFloat("_BandSharp", s.BandSharp);

            mat.SetFloat("_Scale", s.Scale);
            mat.SetFloat("_FlowSpeed", s.FlowSpeed);
            mat.SetFloat("_Warp", s.Warp);
            mat.SetFloat("_Stretch", s.Stretch);
            mat.SetVector("_FlowDir", new Vector4(s.Flow.x, s.Flow.y, 0f, 0f));

            mat.SetFloat("_VeinLevel", s.VeinLevel);
            mat.SetFloat("_VeinWidth", s.VeinWidth);
            mat.SetFloat("_VeinStrength", s.VeinStrength);

            mat.SetFloat("_NormalStrength", s.Ripple);
            mat.SetFloat("_Smoothness", s.Smoothness);
            mat.SetFloat("_SpecularStrength", s.Specular);

            mat.SetFloat("_ToonSteps", s.ToonSteps);
            mat.SetFloat("_ToonSmoothness", s.ToonSoft);

            if (s.GlowStrength > 0f || s.VeinGlow > 0f)
            {
                mat.SetColor("_EmissionColor", ToLinear(s.Glow, 1f));
                mat.SetFloat("_Emission", s.GlowStrength);
                mat.SetFloat("_EmissionFloor", s.GlowFloor);
                mat.SetFloat("_VeinEmission", s.VeinGlow);
                mat.SetFloat("_EmissionNight", s.NightGlow);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.SetColor("_EmissionColor", Color.black);
                mat.SetFloat("_Emission", 0f);
                mat.SetFloat("_VeinEmission", 0f);
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
            Debug.Log($"[Ocean] Recoloured {renderers} renderers across {prefabs} phase prefabs.");
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
