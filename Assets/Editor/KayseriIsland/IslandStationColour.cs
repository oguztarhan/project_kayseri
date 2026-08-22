using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kayseri.IslandTools
{
    /// <summary>
    /// Gives each district its own colour, so the player can tell the mine from the depot from the
    /// refinery at a glance.
    ///
    /// THE PROBLEM. Every building on the island is some shade of industrial grey, which is honest to
    /// the subject and useless to play: at the framing this game uses, MINE, DEPOT, FABRİKA and PAZAR
    /// read as four indistinguishable clusters, and the only thing telling them apart is a floating
    /// text label. Every game in the genre colour-codes its production units, and this is why.
    ///
    /// HOW, WITHOUT NEW ART. Kayseri/IslandVertexLit already carries _Tint and _TintAmount — a hue
    /// shift that preserves the baked vertex-colour luminance, added for the terrain themes. That is
    /// exactly what colour-coding needs: the corrugation, grain and blotching in the bake all survive,
    /// and only the hue moves. No shader work, no re-export, no new textures.
    ///
    /// WHY THE VARIANTS ARE ISLAND-INDEPENDENT. IslandTerrainTheme only themes ground, stone and
    /// trees — its Themed list is grass/rock/sand/pine and nothing else. Building materials are shared
    /// across all eight islands, so one set of district variants serves the whole archipelago rather
    /// than eight sets of the same thing.
    ///
    /// WHERE THEY LIVE. Assets/Art/KayseriIsland/Stations, deliberately NOT the Materials folder:
    /// IslandBuilder.CreateMaterials rewrites every material in there from palette.json on each run,
    /// and "Toggle Flat Colours" rewrites _VertexColorAmount across it. Same reason Themes/ and
    /// Oceans/ sit outside it.
    /// </summary>
    public static class IslandStationColour
    {
        private const string StationRoot = "Assets/Art/KayseriIsland/Stations";
        private const string MaterialsRoot = "Assets/Art/KayseriIsland/Materials";
        private const string PrefabRoot = "Assets/Prefabs/Island";
        private const int PhaseCount = 3;

        private static readonly string[] Islands =
        { "Coal", "Copper", "Iron", "Silver", "Gold", "Ruby", "Emerald", "Diamond" };

        /// <summary>A district's identity: the hue its buildings are pulled toward.</summary>
        private struct District
        {
            public string Group;    // the phase-prefab child name, from IslandBuilder.Groups
            public Color Hue;
        }

        /// <summary>
        /// Nine of the seventeen groups.
        ///
        /// It was six, and Haul, Fleet and Civic were left out on the reasoning that the fleets are
        /// told apart by their livery. That was wrong, and visibly so: those three plus Power are the
        /// four town yards inside the ring road, they are the closest buildings to the middle of the
        /// frame, and three of the four were the only grey things left in shot. Whatever the vehicles
        /// do, the yards themselves are buildings and need to belong to something.
        ///
        /// The four town hues are spaced against EACH OTHER first, because unlike the outer districts
        /// these four sit side by side in one quadrant ring and are compared at a glance.
        ///
        /// Still left alone: Terrain, Roads, Rail, Foliage, Props and Sites are the ground the
        /// districts stand on, Vehicles carry their own livery, and Theme is the island's signature.
        /// </summary>
        private static readonly District[] Districts =
        {
            new District { Group = "Mine",     Hue = new Color(0.95f, 0.62f, 0.18f) },  // ochre
            new District { Group = "Depot",    Hue = new Color(0.28f, 0.55f, 0.88f) },  // cool blue
            new District { Group = "Refinery", Hue = new Color(0.92f, 0.36f, 0.18f) },  // furnace orange
            new District { Group = "Market",   Hue = new Color(0.30f, 0.78f, 0.42f) },  // money green
            new District { Group = "Port",     Hue = new Color(0.20f, 0.70f, 0.72f) },  // harbour teal
            new District { Group = "Power",    Hue = new Color(0.62f, 0.42f, 0.90f) },  // violet   272deg
            // the other three town yards, spaced against Power and against each other
            new District { Group = "Haul",     Hue = new Color(0.92f, 0.34f, 0.60f) },  // rose     333deg
            new District { Group = "Fleet",    Hue = new Color(0.62f, 0.86f, 0.24f) },  // lime      82deg
            new District { Group = "Civic",    Hue = new Color(0.96f, 0.74f, 0.26f) },  // amber     46deg
        };

        /// <summary>
        /// Roofs take the colour hard, because a 40-degree camera pitch is mostly looking at roofs and
        /// they are what carries the read across the island.
        /// </summary>
        private static readonly string[] Roofs =
        { "roof_grey", "roof_blue", "roof_teal", "roof_red", "roof_orange", "roof_green" };

        /// <summary>
        /// Walls. The list started at the obviously-structural names and that was the mistake — measured
        /// by visible surface, the biggest untinted things in a district were plain "white" and "cream",
        /// which is what the buildings' WALLS are made of. Colouring the frame and leaving the cladding
        /// white gave a grey shed with a coloured roof.
        ///
        /// Tinting a name as generic as "white" is safe because the swap is scoped to renderers under a
        /// district root: the variant is Refinery__white, and plain white anywhere else on the island is
        /// untouched.
        /// </summary>
        private static readonly string[] Bodies =
        {
            "clad", "steel", "steel_lt", "steel_dk", "concrete", "concrete_dk", "metal_gal", "brick",
            "white", "offwhite", "cream", "wood", "wood_lt", "metal_dark",
        };

        // These were 0.55 / 0.28 and the district colour was invisible in game. The reason is in the
        // shader: albedo = lerp(albedo, _Tint.rgb * bakeLum, _TintAmount) — the tint PRESERVES the
        // baked luminance and only moves the hue. The district buildings are baked near-white, and a
        // 28% hue shift on a near-white surface is a barely-tinted white. On dark art those numbers
        // would have been about right; on this art they had to roughly double before a refinery read
        // as orange rather than as a grey shed with a warm cast.
        // These are high on purpose, and the reason is measured rather than taste: the building art
        // has no colour in it to boost. Vertex colours on Haul.Fence come out at 0.056 saturation, and
        // the palette agrees — clad is 0.053, white is 0.013, steel is 0.099. Grading saturation
        // multiplies existing chroma, so on a grey source it does nothing at all, which is exactly what
        // it did. The tint is the only thing in the pipeline that can INJECT hue rather than amplify
        // it, so it has to do essentially all of the work.
        //
        // At 0.55 the districts were still grey. These make a refinery orange and a depot blue outright.
        private const float RoofAmount = 0.95f;
        private const float BodyAmount = 0.88f;

        /// <summary>
        /// The imported override models — the buildings you actually see — do NOT use the island's toon
        /// shader. IslandModelSwapper stands them up under an "_Overrides" holder and they arrive on
        /// stock Universal Render Pipeline/Lit with a shared 512x512 "colormap" atlas, which has no
        /// _Tint, no _Saturation and no _VertexColorAmount. Everything this file does to the generated
        /// meshes therefore missed them completely: proved by forcing every station material to pure
        /// magenta, after which the fences and pads went magenta and the buildings did not move.
        ///
        /// That mismatch is also why the buildings read as flat and untextured next to the terrain —
        /// the terrain is toon-shaded with saturation 1.7 and vibrance, and they are not.
        ///
        /// URP/Lit multiplies its albedo texture by _BaseColor, so a district colour goes on there
        /// instead. Lerped from WHITE rather than used raw: multiplying a texture by a saturated colour
        /// darkens it, and the point is to tint the buildings, not to black them out.
        /// </summary>
        private const string OverrideAtlas = "colormap";
        private const float OverrideTint = 0.55f;

        // ───────────────────────────────────────────────────────────────── materials
        [MenuItem("Kayseri/Island/Create Station Colours", false, 26)]
        public static void CreateMaterials()
        {
            EnsureFolder(StationRoot);
            // Resolved BEFORE StartAssetEditing: asset operations are deferred inside that block and
            // AssetDatabase.FindAssets comes back empty there, which is why the first version of this
            // silently created no override variants at all.
            Material atlas = FindAtlas();

            int created = 0, updated = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int d = 0; d < Districts.Length; d++)
                {
                    Write(Districts[d], Roofs, RoofAmount, ref created, ref updated);
                    Write(Districts[d], Bodies, BodyAmount, ref created, ref updated);
                    WriteOverride(Districts[d], atlas, ref created, ref updated);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            Debug.Log($"[Island] Station colours: {created} created, {updated} updated.");
        }

        /// <summary>
        /// Any one of the imported models' embedded "colormap" materials. They are all the same stock
        /// URP/Lit plus the shared atlas, so the first one found serves as the template.
        /// </summary>
        private static Material FindAtlas()
        {
            var guids = AssetDatabase.FindAssets("t:Material " + OverrideAtlas);
            for (int i = 0; i < guids.Length; i++)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (m != null && m.name == OverrideAtlas) return m;
            }
            return null;
        }

        /// <summary>
        /// One district's variant of the override atlas — the material the buildings you can actually
        /// see are rendered with. See <see cref="OverrideAtlas"/> for why this is a separate path from
        /// everything else in this file.
        /// </summary>
        private static void WriteOverride(District district, Material atlas, ref int created, ref int updated)
        {
            if (atlas == null) return;

            string path = $"{StationRoot}/{district.Group}__{OverrideAtlas}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(atlas);
                AssetDatabase.CreateAsset(mat, path);
                created++;
            }
            else
            {
                mat.shader = atlas.shader;
                mat.CopyPropertiesFromMaterial(atlas);
                updated++;
            }

            // URP/Lit multiplies its albedo texture by _BaseColor. Lerped from WHITE rather than used
            // raw, because multiplying a texture by a saturated colour darkens it — the point is to
            // tint the buildings, not to black them out.
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", Color.Lerp(Color.white, district.Hue, OverrideTint));
            EditorUtility.SetDirty(mat);
        }

        /// <summary>
        /// The palette entries that are DARK, and therefore need the tint to lift them as well as
        /// turn them. See <see cref="ApplyTint"/> for why.
        /// </summary>
        private static bool IsDark(string baseName)
        {
            return baseName == "steel_dk" || baseName == "concrete_dk"
                || baseName == "metal_dark" || baseName == "roof_grey";
        }

        private static void Write(District district, string[] bases, float amount,
                                  ref int created, ref int updated)
        {
            for (int i = 0; i < bases.Length; i++)
            {
                var source = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsRoot}/{bases[i]}.mat");
                if (source == null) continue;   // palette does not carry this one

                string path = $"{StationRoot}/{district.Group}__{bases[i]}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(source);
                    AssetDatabase.CreateAsset(mat, path);
                    created++;
                }
                else
                {
                    mat.shader = source.shader;
                    mat.CopyPropertiesFromMaterial(source);
                    updated++;
                }
                // Dark entries get the additive lift; light ones need none and would blow out.
                ApplyTint(mat, district.Hue, amount, IsDark(bases[i]) ? 0.30f : 0.10f);
                EditorUtility.SetDirty(mat);
            }
        }

        /// <summary>
        /// The shader does <c>albedo = lerp(albedo, _Tint.rgb * bakeLum, _TintAmount)</c>, so _Tint is
        /// pre-divided by its own luminance: that makes the shift preserve the baked brightness and
        /// only move the hue, which is what IslandTerrainTheme wants for ground.
        ///
        /// For buildings it is not enough, and this is the thing that took three passes to see. The
        /// biggest surfaces in a district are steel_dk and concrete_dk, and they are DARK. Rotating the
        /// hue of a dark pixel while holding its luminance gives a dark version of that hue, and the
        /// eye reads any sufficiently dark colour as grey. The town yards stayed grey at tint amount
        /// 0.28, and they stayed grey at 0.55, because the amount was never the problem — the value was.
        ///
        /// Scaling _Tint up was the first attempt and it does not work, because the shader MULTIPLIES
        /// the baked luminance: 2.4 times a near-black pixel is still near-black. Measured directly —
        /// at full tint the yard fence, which is mid grey, went clearly rose while the building tops,
        /// which are baked almost black, did not shift at all.
        ///
        /// <paramref name="lift"/> is the fix and it is ADDITIVE: _TintLift is added to the luminance
        /// before the tint multiplies it, raising dark pixels into a range where a hue can be seen.
        /// Only the dark entries get a meaningful amount; light cladding needs almost none and would
        /// blow out to white.
        /// </summary>
        private static void ApplyTint(Material mat, Color hue, float amount, float lift)
        {
            if (!mat.HasProperty("_Tint")) return;
            float lum = 0.2126f * hue.r + 0.7152f * hue.g + 0.0722f * hue.b;
            float k = 1f / Mathf.Max(lum, 1e-3f);
            mat.SetColor("_Tint", new Color(hue.r * k, hue.g * k, hue.b * k, 1f));
            mat.SetFloat("_TintAmount", Mathf.Clamp01(amount));
            if (mat.HasProperty("_TintLift")) mat.SetFloat("_TintLift", Mathf.Clamp01(lift));
        }

        // ───────────────────────────────────────────────────────────────────── apply
        /// <summary>
        /// Swaps every whitelisted building material under a district root for that district's variant.
        /// Called from IslandBuilder.BuildPhasePrefabs after the terrain theme, so a re-export cannot
        /// silently drop the colour-coding.
        /// </summary>
        public static int Apply(GameObject root, string island)
        {
            var lookup = Lookup();
            if (lookup.Count == 0) return 0;

            int touched = 0;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                string district = DistrictOf(renderers[i].transform, root.transform);
                if (district == null) continue;

                var mats = renderers[i].sharedMaterials;
                bool changed = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    // A MISSING material under a district root can only be this district's override
                    // atlas: the override models arrive on one shared "colormap" and nothing else in
                    // here is ever removed. Repairing it rather than skipping it matters, because a
                    // slot left null renders as Unity's magenta error shader in a shipped build.
                    if (mats[m] == null)
                    {
                        if (!lookup.TryGetValue($"{district}__{OverrideAtlas}", out Material heal)) continue;
                        if (heal == null) continue;
                        mats[m] = heal;
                        changed = true;
                        continue;
                    }
                    if (!lookup.TryGetValue($"{district}__{mats[m].name}", out Material swap)) continue;
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

        /// <summary>Which district group a renderer sits under, or null when it is not in one.</summary>
        private static string DistrictOf(Transform t, Transform root)
        {
            var p = t;
            while (p.parent != null && p.parent != root) p = p.parent;
            if (p.parent != root) return null;
            for (int d = 0; d < Districts.Length; d++)
                if (p.name == Districts[d].Group) return Districts[d].Group;
            return null;
        }

        private static Dictionary<string, Material> Lookup()
        {
            var map = new Dictionary<string, Material>();
            var guids = AssetDatabase.FindAssets("t:Material", new[] { StationRoot });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) map[mat.name] = mat;
            }
            return map;
        }

        // ───────────────────────────────────────────────────────────────── the cheap path
        /// <summary>
        /// Recolours the phase prefabs already on disk without touching the FBX — the same cheap path
        /// the oceans and the terrain themes have. A full Build All would rewrite the prefabs from the
        /// models and take the open scene with it.
        /// </summary>
        [MenuItem("Kayseri/Island/Colour Stations (every island)", false, 27)]
        public static void ColourAll()
        {
            CreateMaterials();

            int prefabs = 0, touched = 0;
            for (int i = 0; i < Islands.Length; i++)
            {
                for (int phase = 1; phase <= PhaseCount; phase++)
                {
                    string path = $"{PrefabRoot}/{Islands[i]}/Island_Phase{phase}.prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;

                    var contents = PrefabUtility.LoadPrefabContents(path);
                    int n = Apply(contents, Islands[i]);
                    if (n > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        touched += n;
                        prefabs++;
                    }
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Island] Station colours applied: {touched} renderers across {prefabs} prefabs.");
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            int cut = folder.LastIndexOf('/');
            string parent = folder.Substring(0, cut), leaf = folder.Substring(cut + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
