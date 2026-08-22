using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kayseri.IslandTools
{
    /// <summary>
    /// Makes each island's LAND agree with its sea.
    ///
    /// The oceans are eight different liquids (see IslandOceans) but the ground they surround was
    /// still one temperate coast on all eight maps, so a lava sea broke on green grass. This tints
    /// the nature materials per island the same way the oceans are swapped per island: a variant
    /// material per island, retargeted in BuildPhasePrefabs.
    ///
    /// It is a TINT, not a colour override, and that distinction is the whole design. The island's
    /// surface detail - terrain noise, coal grain, bark, the blotching on the rock - lives in baked
    /// VERTEX COLOUR, and `_BaseColor` is ignored while `_VertexColorAmount` is 1. Overriding the
    /// colour the way the sea does would throw every bit of that away, which is fine on an
    /// eight-vertex quad and ruinous on terrain. So `_Tint` shifts the hue of what was baked and
    /// leaves its luminance exactly alone.
    ///
    /// Blender is the other place this could live - each isle_*.py already declares GROUND_ROCK and
    /// friends, and 01_setup bakes them. That is the tidier home for the base colours, but it costs
    /// a headless re-export of all eight islands, and vertex colour cannot carry emission, so the
    /// lava island's mountains could never glow from there. This can do both.
    ///
    /// Embers are the lava mountains. The bake's dark pixels are already the cracks and hollows of
    /// the rock, so `_EmberStrength` lights exactly those - no new geometry, no new samples, and it
    /// follows whatever the terrain happens to be shaped like.
    ///
    /// Materials land in Assets/Art/KayseriIsland/Themes/ - outside Materials/, because "Toggle
    /// Flat Colours" rewrites `_VertexColorAmount` across everything under that folder and would
    /// flatten the very bake these tints are riding on.
    /// </summary>
    public static class IslandTerrainTheme
    {
        private const string ThemeRoot = "Assets/Art/KayseriIsland/Themes";

        /// <summary>
        /// Where the visible trees, bushes and boulders actually come from.
        ///
        /// This was the trap: the `pine`, `bush` and `rock_dark` materials above are what BLENDER
        /// exported, but IslandModelSwapper replaces those meshes with imported KENNY models, and
        /// every one of them draws on this single shared `colormap` atlas - 745 pines, 295 bushes
        /// and 465 boulders on one island. Tinting the Blender materials therefore changed nothing
        /// the player can see, which is exactly how the first pass came out with green trees on the
        /// lava island.
        ///
        /// The atlas is also on the buildings and vehicles, so it cannot simply be tinted in place.
        /// Instead each island gets a copy with `_BaseColor` shifted, applied ONLY to renderers
        /// that are foliage or boulders - see IsNature. All three source FBX resolve to the same
        /// colormap.png, so one copy per island covers the lot.
        /// </summary>
        private const string FoliageSource = "Assets/KENNYASSETS/SURVİVAL/FBX format/tree.fbx";
        private const string FoliageAtlas = "colormap";
        private const string MaterialsRoot = "Assets/Art/KayseriIsland/Materials";
        private const string PrefabRoot = "Assets/Prefabs/Island";
        private const int PhaseCount = 3;

        /// <summary>
        /// The nature materials, and nothing else.
        ///
        /// Counted off a built prefab rather than guessed: the ground mesh carries grass, rock,
        /// cliff, sand, grass_dry and seabed as submeshes - only three renderers each, but by far
        /// the largest area on screen - while trunk, pine, pine_lt and bush are the ~4700 foliage
        /// instances and rock_dark/rock the scattered boulders.
        ///
        /// Deliberately NOT included here: `colormap`. It is handled separately below, because it
        /// is one atlas shared by BOTH the landscape and the buildings - see FoliageSource.
        /// </summary>
        private static readonly string[] Themed =
        {
            "grass", "grass_dry", "bush",            // ground cover
            "rock", "rock_dark", "cliff",            // stone
            "sand", "dirt", "gravel", "seabed",      // bare ground
            "pine", "pine_lt", "trunk",              // trees
        };

        /// <summary>
        /// One island's landscape.
        ///
        /// Every colour here is a HUE the material is pulled toward, not a colour it becomes - the
        /// bake's luminance survives. Value scales brightness on top, so an ashen island can go
        /// darker as well as greyer, and Amount is how far the shift is taken at all: 1 fully
        /// adopts the hue, and the few materials left near 0 keep their authored look.
        /// </summary>
        private struct Tint
        {
            public Color Hue;
            public float Value;      // 1 keeps the baked brightness, below 1 darkens
            public float Amount;     // 0 leaves the material untouched
        }

        /// <summary>
        /// A network of glowing splits across the open ground - magma showing through from below.
        ///
        /// Applied ONLY to the open-ground submeshes. That is what keeps it out of the districts:
        /// pads, roads and yards are separate geometry on concrete, asphalt and kerb, so a fissure
        /// can never run through a building without anyone having to mask it out.
        /// </summary>
        private struct Crack
        {
            public Color Colour;
            public float Strength;
            public float Scale;      // 1/Scale is roughly the spacing between fissures
            public float Width;
            public float Warp;
            public float Coverage;   // how much of the ground splits at all
            public float Speed;
            public float Night;
        }

        private struct Ember
        {
            public Color Colour;
            public float Strength;   // 0 on six of the eight islands
            public float Level;      // how dark a pixel has to be to count as a crevice
            public float Soft;
            public float Night;
        }

        private struct Theme
        {
            public string Island;
            public string Name;
            /// <summary>Applied to every themed material that has no entry of its own.</summary>
            public Tint Ground;
            /// <summary>Per-material overrides, for the ones the whole-island tint gets wrong.</summary>
            public Dictionary<string, Tint> Overrides;
            /// <summary>Which materials glow, and how. Empty on the six cold islands.</summary>
            public Dictionary<string, Ember> Embers;
            /// <summary>Multiplied over the imported foliage atlas. A multiply can only darken, so
            /// these sit near 1 on the channels the island wants to keep.</summary>
            public Color Foliage;
            /// <summary>Which open-ground materials split open. Null on the seven that do not.</summary>
            public Dictionary<string, Crack> Cracks;
        }

        private static Tint T(float r, float g, float b, float value, float amount)
        {
            return new Tint { Hue = new Color(r, g, b, 1f), Value = value, Amount = amount };
        }

        private static Crack C(float r, float g, float b, float strength, float scale,
                               float width, float warp, float coverage, float speed, float night)
        {
            return new Crack
            {
                Colour = new Color(r, g, b, 1f), Strength = strength, Scale = scale,
                Width = width, Warp = warp, Coverage = coverage, Speed = speed, Night = night,
            };
        }

        private static Ember E(float r, float g, float b, float strength,
                               float level, float soft, float night)
        {
            return new Ember
            {
                Colour = new Color(r, g, b, 1f),
                Strength = strength, Level = level, Soft = soft, Night = night,
            };
        }

        // ─────────────────────────────────────────────────────────────────────── the eight lands
        private static readonly Theme[] Themes =
        {
            // Industrial blight under a tar sea. Everything is desaturated toward soot and taken
            // down in value; the trees are the tell, pulled grey-brown so the island reads as dead
            // rather than as an ordinary coast at dusk.
            new Theme
            {
                Island = "Coal", Name = "temperate",
                // The lightest touch of the eight, on purpose and for two reasons.
                //
                // First, this is the first island a player sees, so it has to look alive rather
                // than themed - the blighted grey it replaced was the right idea for a coal map and
                // exactly the wrong first impression.
                //
                // Second, Coal is one of the two BASE maps in the generator: unlike iron, gold,
                // ruby and the rest it declares no GROUND_ROCK or GROUND_SAND override, so what
                // Blender baked into it already IS a temperate coast. The job here is mostly to
                // stay out of the bake's way and lift it, which is why every Amount is low and the
                // stone is left almost exactly as authored.
                Ground = T(0.55f, 0.70f, 0.46f, 1.02f, 0.25f),
                Overrides = new Dictionary<string, Tint>
                {
                    { "grass",     T(0.42f, 0.78f, 0.34f, 1.06f, 0.45f) },   // lush, not neon
                    { "grass_dry", T(0.62f, 0.74f, 0.36f, 1.03f, 0.35f) },
                    { "bush",      T(0.38f, 0.74f, 0.36f, 1.02f, 0.40f) },
                    { "pine",      T(0.30f, 0.66f, 0.34f, 0.98f, 0.35f) },
                    { "pine_lt",   T(0.40f, 0.76f, 0.40f, 1.03f, 0.35f) },
                    { "sand",      T(0.92f, 0.84f, 0.62f, 1.08f, 0.55f) },   // golden beach
                    // Stone stays the granite Blender authored - a green cast on the cliffs is the
                    // quickest way to make a temperate island look like a filter was applied to it.
                    { "rock",      T(0.72f, 0.72f, 0.70f, 1.00f, 0.10f) },
                    { "rock_dark", T(0.70f, 0.70f, 0.70f, 1.00f, 0.10f) },
                    { "cliff",     T(0.72f, 0.72f, 0.70f, 1.00f, 0.10f) },
                },
                // Near white: the imported atlas is already the right green, and this is a multiply,
                // so anything below 1 only takes life out of it.
                Foliage = new Color(0.98f, 1.00f, 0.94f, 1f),
            },

            // Chemical scarring around an acid sea. The ground goes sickly yellow-green and the
            // stone picks up the same oxide stain, so the shoreline does not look like clean rock
            // meeting poison.
            new Theme
            {
                Island = "Copper", Name = "leached",
                Ground = T(0.72f, 0.78f, 0.42f, 0.95f, 0.55f),
                Overrides = new Dictionary<string, Tint>
                {
                    { "grass",     T(0.68f, 0.80f, 0.30f, 0.92f, 0.80f) },
                    { "grass_dry", T(0.78f, 0.76f, 0.32f, 0.95f, 0.80f) },
                    { "bush",      T(0.62f, 0.78f, 0.34f, 0.88f, 0.75f) },
                    { "rock",      T(0.66f, 0.74f, 0.52f, 0.92f, 0.60f) },   // oxide-stained stone
                    { "rock_dark", T(0.58f, 0.70f, 0.50f, 0.88f, 0.60f) },
                    { "cliff",     T(0.64f, 0.72f, 0.50f, 0.92f, 0.55f) },
                    { "sand",      T(0.80f, 0.82f, 0.52f, 1.00f, 0.70f) },   // salt crust
                },
                Foliage = new Color(0.80f, 0.86f, 0.44f, 1f),
            },

            // Oxide desert. Iron already had the warmest country rock of the eight in the bake, so
            // this is the lightest touch of the set - it pushes the ground the rest of the way into
            // red and leaves the stone close to what Blender authored.
            new Theme
            {
                Island = "Iron", Name = "oxidised",
                Ground = T(0.82f, 0.48f, 0.28f, 0.95f, 0.55f),
                Overrides = new Dictionary<string, Tint>
                {
                    { "grass",     T(0.80f, 0.58f, 0.26f, 0.90f, 0.75f) },   // dry ochre
                    { "grass_dry", T(0.84f, 0.56f, 0.24f, 0.95f, 0.80f) },
                    { "bush",      T(0.72f, 0.52f, 0.28f, 0.85f, 0.70f) },
                    { "pine",      T(0.62f, 0.48f, 0.32f, 0.80f, 0.55f) },
                    { "pine_lt",   T(0.68f, 0.52f, 0.34f, 0.85f, 0.55f) },
                    { "rock",      T(0.84f, 0.46f, 0.26f, 1.00f, 0.40f) },
                    { "cliff",     T(0.82f, 0.44f, 0.25f, 1.00f, 0.40f) },
                },
                Foliage = new Color(0.86f, 0.60f, 0.40f, 1f),
            },

            // Cold monochrome under a mercury sea. Almost no hue at all - the point is that the
            // whole island reads as one desaturated blue-grey so the metal offshore looks like the
            // only bright thing in the frame.
            new Theme
            {
                Island = "Silver", Name = "cold",
                Ground = T(0.72f, 0.78f, 0.88f, 0.95f, 0.85f),
                Overrides = new Dictionary<string, Tint>
                {
                    { "grass",     T(0.68f, 0.76f, 0.74f, 0.88f, 0.92f) },   // pale sage
                    { "grass_dry", T(0.76f, 0.79f, 0.76f, 0.92f, 0.92f) },
                    { "bush",      T(0.64f, 0.74f, 0.74f, 0.85f, 0.90f) },
                    { "pine",      T(0.56f, 0.72f, 0.76f, 0.82f, 0.65f) },   // frosted blue-green
                    { "pine_lt",   T(0.62f, 0.76f, 0.80f, 0.88f, 0.65f) },
                },
                Foliage = new Color(0.70f, 0.80f, 0.90f, 1f),
            },

            // Sun-baked, around a sea of molten gold. Warm and pale rather than saturated: the
            // brief is a dry golden coast, and pushing the hue any harder turns the whole island
            // into the same orange as the water.
            new Theme
            {
                Island = "Gold", Name = "sun-baked",
                Ground = T(0.88f, 0.76f, 0.44f, 1.00f, 0.55f),
                Overrides = new Dictionary<string, Tint>
                {
                    { "grass",     T(0.86f, 0.74f, 0.34f, 0.98f, 0.75f) },   // golden dry
                    { "grass_dry", T(0.90f, 0.76f, 0.32f, 1.00f, 0.80f) },
                    { "bush",      T(0.76f, 0.70f, 0.38f, 0.92f, 0.70f) },
                    { "pine",      T(0.66f, 0.66f, 0.38f, 0.88f, 0.60f) },   // olive
                    { "pine_lt",   T(0.74f, 0.72f, 0.42f, 0.92f, 0.60f) },
                    { "rock",      T(0.86f, 0.80f, 0.62f, 1.00f, 0.45f) },   // pale quartz
                    { "cliff",     T(0.84f, 0.78f, 0.60f, 1.00f, 0.45f) },
                },
                Foliage = new Color(0.96f, 0.86f, 0.54f, 1f),
            },

            // Volcanic. The strongest tint of the eight and the only one where the stone goes all
            // the way to black - a lava sea breaking on grey granite was the thing that looked
            // wrong. Both rock materials and the cliffs carry embers, so the mountains glow from
            // their own crevices; the ground cover does not, or the whole island would be on fire.
            new Theme
            {
                Island = "Ruby", Name = "volcanic",
                Ground = T(0.50f, 0.44f, 0.43f, 0.34f, 0.95f),
                Overrides = new Dictionary<string, Tint>
                {
                    { "rock",      T(0.46f, 0.42f, 0.44f, 0.32f, 1.00f) },   // black basalt
                    { "rock_dark", T(0.44f, 0.40f, 0.42f, 0.26f, 1.00f) },
                    { "cliff",     T(0.48f, 0.42f, 0.42f, 0.34f, 1.00f) },
                    { "grass",     T(0.56f, 0.38f, 0.30f, 0.26f, 1.00f) },   // scorched
                    { "grass_dry", T(0.60f, 0.40f, 0.28f, 0.30f, 1.00f) },
                    { "bush",      T(0.54f, 0.36f, 0.30f, 0.24f, 1.00f) },
                    { "pine",      T(0.44f, 0.38f, 0.36f, 0.28f, 1.00f) },   // burnt skeletons
                    { "pine_lt",   T(0.48f, 0.40f, 0.36f, 0.32f, 1.00f) },
                    { "trunk",     T(0.42f, 0.36f, 0.34f, 0.30f, 1.00f) },
                    { "sand",      T(0.44f, 0.40f, 0.40f, 0.35f, 1.00f) },   // black volcanic sand
                },
                Embers = new Dictionary<string, Ember>
                {
                    { "rock",  E(1.00f, 0.34f, 0.05f, 1.90f, 0.480f, 0.095f, 2.60f) },
                    { "cliff", E(1.00f, 0.30f, 0.04f, 1.55f, 0.560f, 0.085f, 2.60f) },
                },
                Foliage = new Color(0.40f, 0.28f, 0.24f, 1f),
                // Only the open ground. Every one of these is a submesh of the terrain itself, so
                // the districts standing on top of it stay dark and unlit by their own floor.
                //
                // Scale 0.011 puts roughly 90 world units between runs and Coverage 0.42 leaves
                // well over half the ground unbroken, so these read as fissures rather than as a
                // lava field with islands of dirt in it.
                Cracks = new Dictionary<string, Crack>
                {
                    { "grass",     C(1.00f, 0.30f, 0.035f, 3.0f, 0.011f, 0.085f, 1.15f, 0.42f, 0.22f, 1.7f) },
                    { "grass_dry", C(1.00f, 0.32f, 0.040f, 3.0f, 0.011f, 0.085f, 1.15f, 0.42f, 0.22f, 1.7f) },
                    { "dirt",      C(1.00f, 0.28f, 0.030f, 2.8f, 0.013f, 0.080f, 1.10f, 0.46f, 0.20f, 1.7f) },
                    { "sand",      C(1.00f, 0.34f, 0.045f, 2.6f, 0.014f, 0.075f, 1.20f, 0.50f, 0.24f, 1.6f) },
                },
            },

            // Overgrown, around an algal sea. The one island the tint makes MORE lush rather than
            // less: deep saturated green everywhere, with the stone mossed over so nothing reads as
            // bare. Its embers are a faint cold bioluminescence in the rock rather than heat.
            new Theme
            {
                Island = "Emerald", Name = "overgrown",
                Ground = T(0.44f, 0.78f, 0.46f, 0.92f, 0.70f),
                Overrides = new Dictionary<string, Tint>
                {
                    { "grass",     T(0.32f, 0.82f, 0.40f, 0.95f, 0.85f) },
                    { "grass_dry", T(0.42f, 0.78f, 0.38f, 0.90f, 0.85f) },
                    { "bush",      T(0.30f, 0.80f, 0.42f, 0.92f, 0.85f) },
                    { "pine",      T(0.26f, 0.76f, 0.40f, 0.88f, 0.80f) },
                    { "pine_lt",   T(0.34f, 0.82f, 0.44f, 0.95f, 0.80f) },
                    { "rock",      T(0.46f, 0.66f, 0.44f, 0.80f, 0.65f) },   // mossed over
                    { "rock_dark", T(0.42f, 0.62f, 0.42f, 0.72f, 0.65f) },
                    { "cliff",     T(0.46f, 0.64f, 0.44f, 0.78f, 0.60f) },
                },
                Embers = new Dictionary<string, Ember>
                {
                    { "rock", E(0.18f, 0.95f, 0.48f, 0.40f, 0.455f, 0.075f, 2.40f) },
                },
                Foliage = new Color(0.60f, 1.00f, 0.66f, 1f),
            },

            // Arctic. The ground goes white rather than merely pale, which is why Value sits ABOVE
            // 1 here - the only theme of the eight that brightens the bake instead of darkening it,
            // because snow is lighter than anything Blender put down.
            new Theme
            {
                Island = "Diamond", Name = "frozen",
                Ground = T(0.86f, 0.92f, 1.00f, 1.25f, 0.80f),
                Overrides = new Dictionary<string, Tint>
                {
                    { "grass",     T(0.88f, 0.94f, 1.00f, 1.35f, 0.90f) },   // snow cover
                    { "grass_dry", T(0.86f, 0.92f, 0.98f, 1.30f, 0.90f) },
                    { "bush",      T(0.80f, 0.88f, 0.96f, 1.15f, 0.85f) },
                    { "sand",      T(0.90f, 0.95f, 1.00f, 1.30f, 0.90f) },
                    { "rock",      T(0.74f, 0.82f, 0.94f, 0.95f, 0.75f) },   // pale blue-grey
                    { "rock_dark", T(0.70f, 0.78f, 0.92f, 0.85f, 0.75f) },
                    { "cliff",     T(0.72f, 0.80f, 0.94f, 0.90f, 0.75f) },
                    { "pine",      T(0.60f, 0.72f, 0.78f, 0.70f, 0.70f) },   // dark under snow
                    { "pine_lt",   T(0.78f, 0.86f, 0.94f, 1.05f, 0.75f) },   // snow-laden branches
                },
                Foliage = new Color(0.78f, 0.87f, 1.00f, 1f),
            },
        };

        // ───────────────────────────────────────────────────────────────────────────── materials
        [MenuItem("Kayseri/Island/Create Terrain Themes", false, 24)]
        public static void CreateMaterials()
        {
            EnsureFolder(ThemeRoot);
            int written = 0, missing = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var theme in Themes)
                    foreach (var name in Themed)
                    {
                        var source = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsRoot}/{name}.mat");
                        if (source == null) { missing++; continue; }

                        string path = $"{ThemeRoot}/{name}_{theme.Island}.mat";
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (mat == null)
                        {
                            // Copied from the palette material, so everything this theme does NOT
                            // touch - smoothness, metallic, the transparent shader on the few that
                            // use it - comes across for free and stays in step with palette.json.
                            mat = new Material(source);
                            AssetDatabase.CreateAsset(mat, path);
                        }
                        else
                        {
                            mat.shader = source.shader;
                            mat.CopyPropertiesFromMaterial(source);
                        }

                        var tint = theme.Overrides != null && theme.Overrides.ContainsKey(name)
                                 ? theme.Overrides[name]
                                 : theme.Ground;
                        Apply(mat, tint);

                        var ember = new Ember();
                        if (theme.Embers != null && theme.Embers.ContainsKey(name))
                            ember = theme.Embers[name];
                        Apply(mat, ember);

                        var crack = new Crack();
                        if (theme.Cracks != null && theme.Cracks.ContainsKey(name))
                            crack = theme.Cracks[name];
                        Apply(mat, crack);

                        EditorUtility.SetDirty(mat);
                        written++;
                    }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            WriteFoliage();

            if (missing > 0)
                Debug.LogWarning($"[Theme] {missing} palette materials missing - run Create Materials first.");
            Debug.Log($"[Theme] {written} terrain materials in {ThemeRoot}.");
        }

        /// <summary>One tinted copy of the imported atlas per island.</summary>
        private static void WriteFoliage()
        {
            Material source = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(FoliageSource))
            {
                var m = o as Material;
                if (m != null && m.name == FoliageAtlas) { source = m; break; }
            }
            if (source == null)
            {
                Debug.LogWarning($"[Theme] No '{FoliageAtlas}' material in {FoliageSource} - "
                               + "trees and boulders keep the shared atlas.");
                return;
            }

            foreach (var theme in Themes)
            {
                string path = $"{ThemeRoot}/foliage_{theme.Island}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    // Copied, so the atlas texture and every import setting come across untouched.
                    mat = new Material(source);
                    AssetDatabase.CreateAsset(mat, path);
                }
                else
                {
                    mat.shader = source.shader;
                    mat.CopyPropertiesFromMaterial(source);
                }

                // URP/Lit multiplies its albedo texture by _BaseColor, which is the whole tint
                // mechanism here - no shader change needed for the imported art.
                var c = theme.Foliage;
                if (PlayerSettings.colorSpace == ColorSpace.Linear) c = c.linear;
                mat.SetColor("_BaseColor", c);

                // GPU instancing, and ONLY here. This one material carries roughly 745 pines, 295
                // bushes and 465 boulders per island — the same handful of meshes drawn over and over,
                // which is exactly the case instancing exists for. It is switched on nowhere else on
                // purpose: instancing and the SRP Batcher are mutually exclusive per draw, and the
                // building materials sit on meshes that are each used once, so turning it on there
                // would cost a batch and buy nothing.
                mat.enableInstancing = true;

                EditorUtility.SetDirty(mat);
            }
        }

        /// <summary>
        /// Whether this renderer is landscape rather than a building or a vehicle. The boulders sit
        /// in the Terrain group next to the ground mesh, so a group test alone would miss them.
        /// </summary>
        private static bool IsNature(Transform t, Transform root)
        {
            if (t.name.StartsWith("Rock", System.StringComparison.Ordinal)) return true;
            var p = t;
            while (p.parent != null && p.parent != root) p = p.parent;
            return p.name == "Foliage";
        }

        private static void Apply(Material mat, Tint tint)
        {
            if (!mat.HasProperty("_Tint")) return;

            // Pre-divided by its own luminance, so the shader's `_Tint * bakeLum` lands on this hue
            // at the pixel's original brightness. Value then scales that deliberately. Without the
            // division a dark hue would double as a darkener and the two controls would fight.
            var h = tint.Hue;
            float lum = 0.2126f * h.r + 0.7152f * h.g + 0.0722f * h.b;
            float k = tint.Value / Mathf.Max(lum, 1e-3f);
            mat.SetColor("_Tint", new Color(h.r * k, h.g * k, h.b * k, 1f));
            mat.SetFloat("_TintAmount", Mathf.Clamp01(tint.Amount));
        }

        private static void Apply(Material mat, Crack crack)
        {
            if (!mat.HasProperty("_CrackStrength")) return;

            if (crack.Strength <= 0f)
            {
                // Explicitly off, keyword and all: a material that stops being cracked has to stop
                // paying for the cellular lookup, not just multiply it by zero.
                mat.SetFloat("_CrackStrength", 0f);
                mat.SetFloat("_Cracks", 0f);
                mat.DisableKeyword("_ISLANDCRACKS");
                return;
            }

            var c = crack.Colour;
            if (PlayerSettings.colorSpace == ColorSpace.Linear) c = c.linear;
            mat.SetColor("_CrackColor", c);
            mat.SetFloat("_CrackStrength", crack.Strength);
            mat.SetFloat("_CrackScale", crack.Scale);
            mat.SetFloat("_CrackWidth", crack.Width);
            mat.SetFloat("_CrackWarp", crack.Warp);
            mat.SetFloat("_CrackCoverage", crack.Coverage);
            mat.SetFloat("_CrackSpeed", crack.Speed);
            mat.SetFloat("_CrackNight", crack.Night);
            mat.SetFloat("_Cracks", 1f);
            mat.EnableKeyword("_ISLANDCRACKS");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        private static void Apply(Material mat, Ember ember)
        {
            if (!mat.HasProperty("_EmberStrength")) return;

            mat.SetFloat("_EmberStrength", Mathf.Max(0f, ember.Strength));
            if (ember.Strength <= 0f)
            {
                // Not touching the GI flag here: the fissures are written after this and set it
                // themselves, and clearing it unconditionally would switch them straight back off
                // on the four ground materials that have cracks but no embers.
                return;
            }

            var c = ember.Colour;
            if (PlayerSettings.colorSpace == ColorSpace.Linear) c = c.linear;
            mat.SetColor("_EmberColor", c);
            mat.SetFloat("_EmberLevel", ember.Level);
            mat.SetFloat("_EmberSoft", ember.Soft);
            mat.SetFloat("_EmberNight", ember.Night);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        // ───────────────────────────────────────────────────────────────────────────────── apply
        /// <summary>
        /// Retargets every themed material under <paramref name="root"/> onto this island's
        /// variant. Called from IslandBuilder.BuildPhasePrefabs next to IslandOceans.Apply, and for
        /// the same reason: a re-export rebuilds the prefab from the FBX and would otherwise drop
        /// the whole theme.
        /// </summary>
        public static int Apply(GameObject root, string island)
        {
            var lookup = LookupFor(island);
            if (lookup == null) return 0;

            var foliage = AssetDatabase.LoadAssetAtPath<Material>(
                $"{ThemeRoot}/foliage_{island}.mat");

            int touched = 0;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = renderers[i].sharedMaterials;
                bool changed = false;
                bool nature = foliage != null && IsNature(renderers[i].transform, root.transform);
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null) continue;

                    // The imported atlas, but only where it is landscape - the same material is on
                    // every building and vehicle and those keep the shared one.
                    if (nature && (mats[m].name == FoliageAtlas
                                || mats[m].name == $"foliage_{island}"
                                || mats[m].name.StartsWith("foliage_", System.StringComparison.Ordinal)))
                    {
                        if (mats[m] == foliage) continue;
                        mats[m] = foliage;
                        changed = true;
                        continue;
                    }

                    string baseName = BaseNameOf(mats[m].name, island);
                    if (baseName == null) continue;
                    var swap = lookup[baseName];
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
        /// Re-themes the phase prefabs that are already built, without touching the FBX. The cheap
        /// path, exactly as with the oceans - a full Build All would rewrite the prefabs from the
        /// models and take the open scene with it.
        /// </summary>
        [MenuItem("Kayseri/Island/Re-theme Islands (every island)", false, 25)]
        public static void RethemeAll()
        {
            CreateMaterials();

            int prefabs = 0, renderers = 0;
            foreach (var theme in Themes)
            {
                for (int phase = 1; phase <= PhaseCount; phase++)
                {
                    string path = $"{PrefabRoot}/{theme.Island}/Island_Phase{phase}.prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;

                    var contents = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        int n = Apply(contents, theme.Island);
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
                Debug.Log($"[Theme] {theme.Island}: {theme.Name}.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Theme] Re-themed {renderers} renderers across {prefabs} phase prefabs. "
                    + "Zero is expected when only the tints changed - the prefabs already point "
                    + "at these materials.");
        }

        // ─────────────────────────────────────────────────────────────────────────────── helpers
        /// <summary>
        /// Which themed material this is, whether it is still the shared palette one or a variant
        /// pointed at on an earlier run - including another island's, so re-theming a prefab that
        /// was built for a different island retargets rather than silently keeps the wrong land.
        /// </summary>
        private static string BaseNameOf(string materialName, string island)
        {
            for (int i = 0; i < Themed.Length; i++)
            {
                if (materialName == Themed[i]) return Themed[i];
                if (!materialName.StartsWith(Themed[i] + "_", System.StringComparison.Ordinal)) continue;
                // "rock_dark" starts with "rock_", so only a known island suffix counts.
                string suffix = materialName.Substring(Themed[i].Length + 1);
                foreach (var t in Themes) if (suffix == t.Island) return Themed[i];
            }
            return null;
        }

        private static Dictionary<string, Material> LookupFor(string island)
        {
            bool known = false;
            foreach (var t in Themes) if (t.Island == island) { known = true; break; }
            if (!known) return null;

            var lookup = new Dictionary<string, Material>(Themed.Length);
            foreach (var name in Themed)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{ThemeRoot}/{name}_{island}.mat");
                if (mat == null)
                {
                    Debug.LogWarning($"[Theme] {name}_{island}.mat missing - run Create Terrain Themes.");
                    return null;
                }
                lookup[name] = mat;
            }
            return lookup;
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
