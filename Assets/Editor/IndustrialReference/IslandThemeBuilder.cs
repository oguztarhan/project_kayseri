using System.Collections.Generic;
using System.IO;
using Game.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kayseri.IslandThemeTools
{
    /// <summary>
    /// Makes a chapter's theme out of the island that is already in the scene.
    /// Menu: Kayseri &gt; Island Themes &gt; New Theme From Island.
    ///
    /// WHAT IT SAVES. Re-skinning the island by hand means finding which of the map's materials are
    /// actually on it (36 of the 37 in the folder are; grassLight is not), duplicating each one into
    /// somewhere it will not collide, and typing 36 pairs into a theme asset without transposing two.
    /// That is an hour of clerical work per chapter, seven times over, and every one of those steps
    /// fails silently if it is got wrong. This does all of it from the scene itself, so the list is
    /// never out of date with the map.
    ///
    /// THE DEFAULT IS AN EXACT COPY. With no tint asked for, the 36 new materials are byte-for-byte
    /// the island's own and the theme changes nothing. That is the useful starting point for real
    /// art: the artist opens the theme's folder, works through it a material at a time, and the
    /// island shows their work as they go instead of only once the whole set is finished. The tint is
    /// for a proof, or for roughing a chapter's mood in before the textures exist.
    ///
    /// A ROW CAN ALWAYS BE EMPTIED. Clearing a replacement in the generated asset puts the island's
    /// own material back for that one slot, which is how a chapter opts out of theming the sea, the
    /// roads, or anything else it should leave alone.
    /// </summary>
    public sealed class IslandThemeBuilder : EditorWindow
    {
        private const string ThemesRoot = "Assets/Art/IndustrialReference/Themes";
        private const string DefaultSet = "Assets/Data/IslandThemeSet.asset";

        private string _themeId = "";
        private string _displayName = "";
        private int _fromChapter = 1;
        private string _islandRootName = "Island_Shipyard";

        private float _hueShift;
        private float _saturation = 1f;
        private float _brightness = 1f;

        private IslandThemeSet _set;
        private bool _addToSet = true;

        /// <summary>Index into <see cref="ChapterThemeRecipe.All"/>, or -1 for a plain copy / tint.</summary>
        private int _recipe = -1;

        private Vector2 _scroll;
        private string _report = "";

        [MenuItem("Kayseri/Island Themes/New Theme From Island")]
        public static void Open()
        {
            var window = GetWindow<IslandThemeBuilder>(true, "Island Theme Builder");
            window.minSize = new Vector2(420f, 460f);
            if (window._set == null) window._set = AssetDatabase.LoadAssetAtPath<IslandThemeSet>(DefaultSet);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Sahnedeki adanın kullandığı her malzemeyi kopyalar ve hazır bir tema varlığı yazar.\n\n" +
                "Renk ayarları 0 / 1 / 1 bırakılırsa kopyalar birebir aynıdır — gerçek doku çalışması " +
                "için doğru başlangıç budur. Tema varlığında bir satırı boşaltmak, o malzemeyi adanın " +
                "kendi hâlinde bırakır.",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Bölüm tarifi", EditorStyles.boldLabel);
            var names = new string[ChapterThemeRecipe.All.Length + 1];
            names[0] = "— yok (kopya / renk kaydırma) —";
            for (int i = 0; i < ChapterThemeRecipe.All.Length; i++) names[i + 1] = ChapterThemeRecipe.All[i].DisplayName;
            int picked = EditorGUILayout.Popup("Recipe", _recipe + 1, names) - 1;
            if (picked != _recipe)
            {
                _recipe = picked;
                if (_recipe >= 0)
                {
                    ChapterThemeRecipe r = ChapterThemeRecipe.All[_recipe];
                    _themeId = r.Id;
                    _displayName = r.DisplayName;
                    _fromChapter = r.FromChapter;
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Kimlik", EditorStyles.boldLabel);
            _themeId = EditorGUILayout.TextField("Theme id", _themeId);
            _displayName = EditorGUILayout.TextField("Display name", _displayName);
            _fromChapter = EditorGUILayout.IntField(
                new GUIContent("From chapter (0 tabanlı)", "0 = Bölüm 1. Tema, bir sonraki tema açılana kadar sürer."),
                _fromChapter);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Kaynak", EditorStyles.boldLabel);
            _islandRootName = EditorGUILayout.TextField("Island root", _islandRootName);

            EditorGUILayout.Space();
            if (_recipe < 0)
            {
                EditorGUILayout.LabelField("Renk (isteğe bağlı)", EditorStyles.boldLabel);
                _hueShift = EditorGUILayout.Slider("Hue shift", _hueShift, -0.5f, 0.5f);
                _saturation = EditorGUILayout.Slider("Saturation", _saturation, 0f, 2f);
                _brightness = EditorGUILayout.Slider("Brightness", _brightness, 0f, 2f);
                if (!Tints) EditorGUILayout.LabelField(" ", "Birebir kopya.", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("Tarif, yalnızca değişen malzemeleri yazar. Işık veren malzemeler, " +
                                        "deniz, kalas, yol (sikke kenarlarıyla ortak) ve renk kodlu " +
                                        "konteynerler adanın kendi hâlinde kalır.", MessageType.None);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tema listesi", EditorStyles.boldLabel);
            _set = (IslandThemeSet)EditorGUILayout.ObjectField("Set", _set, typeof(IslandThemeSet), false);
            _addToSet = EditorGUILayout.Toggle("Listeye ekle", _addToSet);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_themeId.Trim())))
                if (GUILayout.Button("Create theme", GUILayout.Height(30f))) Create();

            if (!string.IsNullOrEmpty(_report))
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(_report, MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        private bool Tints => !Mathf.Approximately(_hueShift, 0f)
                           || !Mathf.Approximately(_saturation, 1f)
                           || !Mathf.Approximately(_brightness, 1f);

        private void Create()
        {
            string id = _themeId.Trim();
            string folder = ThemesRoot + "/" + id;

            // A folder that already holds THIS theme is rebuilt in place — that is how a recipe is
            // tuned. A folder holding anything else is somebody's work, and is not written over.
            bool rebuild = AssetDatabase.IsValidFolder(folder) &&
                           AssetDatabase.LoadAssetAtPath<IslandThemeDefinition>(ThemePath(folder, id)) != null;
            if (AssetDatabase.IsValidFolder(folder) && !rebuild)
            {
                _report = "Already exists: " + folder + "\nIt holds no theme called '" + id + "', so it is left alone. " +
                          "Pick another id.";
                return;
            }

            Transform root = FindIslandRoot();
            if (root == null)
            {
                _report = "No scene root named '" + _islandRootName + "' is open.";
                return;
            }

            List<Material> sources = DistinctMaterials(root);
            if (sources.Count == 0)
            {
                _report = "'" + _islandRootName + "' has no renderers with materials on it.";
                return;
            }

            // The sources have to be the island's OWN materials. If it is wearing a theme — previewed,
            // or left on by play mode — the scene holds that theme's copies, and a theme built from
            // them keys its rows on materials a freshly loaded island never has: every row silently
            // dead. It also derives every colour from the wrong palette.
            Material worn = FirstThemeMaterial(sources);
            if (worn != null)
            {
                _report = "The island is wearing theme paint ('" + worn.name + "' from " +
                          AssetDatabase.GetAssetPath(worn) + ").\nPut it back to its own materials — reload " +
                          "the scene — and build again.";
                return;
            }

            EnsureFolder(ThemesRoot);
            if (!rebuild) AssetDatabase.CreateFolder(ThemesRoot, id);

            if (_recipe >= 0)
            {
                CreateFromRecipe(ChapterThemeRecipe.All[_recipe], folder, id, sources);
                return;
            }

            var replacements = new List<Material>(sources.Count);
            int skipped = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                string from = AssetDatabase.GetAssetPath(sources[i]);
                if (string.IsNullOrEmpty(from))
                {
                    // A material built at runtime has no asset to copy. It cannot be themed from
                    // here, and the row is left out rather than written half-formed.
                    skipped++;
                    replacements.Add(null);
                    continue;
                }

                Material copy = CopyOf(sources[i], folder);
                if (copy != null && Tints) Retint(copy);
                replacements.Add(copy);
            }

            IslandThemeDefinition theme = WriteTheme(folder, id, sources, replacements);
            bool listed = _addToSet && AddToSet(theme);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = theme;
            EditorGUIUtility.PingObject(theme);

            _report = "Wrote " + (sources.Count - skipped) + " materials to " + folder +
                      "\nTheme: " + AssetDatabase.GetAssetPath(theme) +
                      "\nOpens at chapter " + (_fromChapter + 1) +
                      (Tints ? "\nTinted." : "\nExact copies — fill them in as the art arrives.") +
                      (skipped > 0 ? "\n" + skipped + " runtime materials skipped." : "") +
                      (listed ? "\nAdded to " + _set.name + "." : "\nNOT added to a set — assign it yourself.");
        }

        /// <summary>
        /// The materials actually ON the island, in name order, each one once. Read from the scene
        /// rather than from the materials folder: the folder holds one the map does not use, and a
        /// theme with a row for a material no renderer has is a row that can never do anything.
        /// </summary>
        private static List<Material> DistinctMaterials(Transform root)
        {
            var seen = new HashSet<Material>();
            var found = new List<Material>();
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] slots = renderers[r].sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] != null && seen.Add(slots[i])) found.Add(slots[i]);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }

        private Transform FindIslandRoot()
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                    if (roots[i].name == _islandRootName) return roots[i].transform;
            }
            return null;
        }

        /// <summary>
        /// Shifts a material's base colour in HSV and leaves everything else — shader, maps, metallic,
        /// smoothness — exactly as the island authored it.
        ///
        /// EMISSION IS NOT TOUCHED. The island's emissive materials are what light it; moving them
        /// would make a theme a lighting change, and the brief is that a theme changes the paint only.
        /// </summary>
        private void Retint(Material material)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Shift(material.GetColor("_BaseColor")));
            if (material.HasProperty("_Color")) material.SetColor("_Color", Shift(material.GetColor("_Color")));
            EditorUtility.SetDirty(material);
        }

        private Color Shift(Color c)
        {
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            Color shifted = Color.HSVToRGB(Mathf.Repeat(h + _hueShift, 1f),
                                           Mathf.Clamp01(s * _saturation),
                                           Mathf.Clamp01(v * _brightness));
            shifted.a = c.a;
            return shifted;
        }

        /// <summary>
        /// Writes the theme asset through <see cref="SerializedObject"/> rather than reflection, so
        /// the private serialized fields are set the way the Inspector would set them and the asset is
        /// marked dirty for free.
        /// </summary>
        private IslandThemeDefinition WriteTheme(string folder, string id,
                                                 List<Material> sources, List<Material> replacements)
        {
            // A rebuild rewrites the theme it already has rather than creating a new one on the same
            // path: the set holds it by GUID, and a recreated asset is not guaranteed to keep it.
            string path = ThemePath(folder, id);
            var theme = AssetDatabase.LoadAssetAtPath<IslandThemeDefinition>(path);
            if (theme == null)
            {
                theme = CreateInstance<IslandThemeDefinition>();
                AssetDatabase.CreateAsset(theme, path);
            }

            var so = new SerializedObject(theme);
            so.FindProperty("themeId").stringValue = id;
            so.FindProperty("displayName").stringValue =
                string.IsNullOrEmpty(_displayName.Trim()) ? id : _displayName.Trim();
            so.FindProperty("fromChapter").intValue = _fromChapter;

            SerializedProperty swaps = so.FindProperty("swaps");
            swaps.arraySize = sources.Count;
            for (int i = 0; i < sources.Count; i++)
            {
                SerializedProperty row = swaps.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("source").objectReferenceValue = sources[i];
                row.FindPropertyRelative("replacement").objectReferenceValue = replacements[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return theme;
        }

        /// <summary>
        /// Lists the theme, taking the place of any other theme that opens at the same chapter.
        ///
        /// TWO THEMES OPENING TOGETHER HAVE NO RIGHT ANSWER — IslandThemes picks the later one, so the
        /// earlier is dead weight that only looks live in the Inspector. A new theme for a chapter is a
        /// replacement for the old one, so it is treated as one. The displaced asset is NOT deleted: it
        /// comes out of the list, and its files stay where they are for someone to decide about.
        /// </summary>
        private bool AddToSet(IslandThemeDefinition theme, List<string> displaced = null)
        {
            if (_set == null) return false;

            var so = new SerializedObject(_set);
            SerializedProperty themes = so.FindProperty("themes");
            for (int i = themes.arraySize - 1; i >= 0; i--)
            {
                var listed = themes.GetArrayElementAtIndex(i).objectReferenceValue as IslandThemeDefinition;
                if (listed == theme) return true;
                if (listed == null || listed.FromChapter != theme.FromChapter) continue;
                if (displaced != null) displaced.Add(listed.ThemeId);
                themes.GetArrayElementAtIndex(i).objectReferenceValue = null;   // DeleteArrayElementAtIndex
                themes.DeleteArrayElementAtIndex(i);                            // only clears a reference first
            }

            themes.arraySize++;
            themes.GetArrayElementAtIndex(themes.arraySize - 1).objectReferenceValue = theme;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        // ------------------------------------------------------------------ recipe mode
        /// <summary>
        /// A family of materials that moves together: one anchor whose target the recipe names, and
        /// members that keep their relation to it.
        /// </summary>
        private struct Family
        {
            public string Anchor;
            public string[] Members;
            public float MaxSaturation;
            public System.Func<ChapterThemeRecipe, Color> Target;
        }

        /// <summary>
        /// Coal rides the rock family at a capped saturation. It is the island's ore, and a
        /// crystal chapter's pale-cyan rock would otherwise hand it a cyan tint that stops it reading as
        /// coal at all.
        /// </summary>
        private const float CoalSaturationCap = 0.22f;

        private const float SandSaturationCap = 0.30f;

        /// <summary>
        /// Below this saturation a colour's hue is noise — rock1 and rock0 are "grey" with hues 80
        /// degrees apart — so a near-grey member takes the target's hue instead of keeping an offset
        /// that would scatter the rocks across the colour wheel once they have colour to show.
        /// </summary>
        private const float GreyCutoff = 0.15f;

        /// <summary>How much of a member's hue offset from its anchor survives. Half keeps the family together.</summary>
        private const float HueOffsetKept = 0.5f;

        private const string SharedFolder = ThemesRoot + "/_Shared";
        private const string NeutralMeadowPath = SharedFolder + "/Meadow_Neutral.png";

        /// <summary>
        /// The greyscale meadow is normalised to this mean. A map that averaged 0.4 would multiply every
        /// chapter's ground colour down to mud; this one lets the base colour arrive close to itself.
        /// </summary>
        private const float NeutralMean = 0.85f;

        private static readonly string[] Metals = { "steel", "steelDark", "white", "rubber", "concrete" };

        private static Family[] Families()
        {
            return new[]
            {
                new Family { Anchor = "grassDark", Members = new[] { "grassDark", "grassLight" }, MaxSaturation = 1f, Target = r => r.Ground },
                new Family { Anchor = "pine", Members = new[] { "pine", "pineDark", "pineLight" }, MaxSaturation = 1f, Target = r => r.Vegetation },
                new Family { Anchor = "bark", Members = new[] { "bark" }, MaxSaturation = 1f, Target = r => r.Bark },
                new Family { Anchor = "rock1", Members = new[] { "rock0", "rock1", "rock2", "rock3", "rock4" }, MaxSaturation = 1f, Target = r => r.Rock },
                new Family { Anchor = "rock1", Members = new[] { "coal" }, MaxSaturation = CoalSaturationCap, Target = r => r.Rock },
                // Sand is the MOUNTAINS' light faces as much as it is beach — five of its twelve slots
                // are on the range. Left alone it paints warm yellow facets onto slate, snow and crystal
                // peaks, so it rides the rock family, capped so it stays a highlight rather than a colour.
                new Family { Anchor = "rock1", Members = new[] { "sand" }, MaxSaturation = SandSaturationCap, Target = r => r.Rock },
                new Family { Anchor = "yellow", Members = new[] { "yellow", "orange" }, MaxSaturation = 1f, Target = r => r.Accent },
            };
        }

        /// <summary>
        /// Writes only what the recipe changes. Every material that no family names — the emissive
        /// ones, the sea, the beach, the planks, the road, the colour-coded containers — gets no copy
        /// and no row, so it draws the island's own material in this chapter as in every other.
        /// </summary>
        private void CreateFromRecipe(ChapterThemeRecipe recipe, string folder, string id, List<Material> island)
        {
            _displayName = recipe.DisplayName;   // WriteTheme reads these; the recipe is the authority
            _fromChapter = recipe.FromChapter;

            var byName = new Dictionary<string, Material>();
            for (int i = 0; i < island.Count; i++) byName[island[i].name] = island[i];

            var sources = new List<Material>();
            var replacements = new List<Material>();
            var log = new List<string>();

            foreach (Family family in Families())
            {
                Color target = family.Target(recipe);
                if (ChapterThemeRecipe.IsOriginal(target)) { log.Add(family.Anchor + " family kept"); continue; }

                Material anchor;
                if (!byName.TryGetValue(family.Anchor, out anchor))
                {
                    log.Add("anchor " + family.Anchor + " is not on the island — family skipped");
                    continue;
                }
                Color anchorFrom = BaseColor(anchor);

                foreach (string name in family.Members)
                {
                    Material source;
                    if (!byName.TryGetValue(name, out source)) continue;   // grassLight is not on this map
                    Color from = BaseColor(source);
                    Color to = Relate(from, anchorFrom, target, family.MaxSaturation);
                    if (Same(from, to)) continue;
                    AddRecoloured(folder, source, to, sources, replacements);
                }
            }

            foreach (string name in Metals)
            {
                Material source;
                if (!byName.TryGetValue(name, out source) || recipe.MetalAmount <= 0f) continue;
                Color from = BaseColor(source);
                Color to = Temper(from, recipe.MetalTint, recipe.MetalAmount);
                if (Same(from, to)) continue;
                AddRecoloured(folder, source, to, sources, replacements);
            }

            Material grass;
            if (byName.TryGetValue("grass", out grass))
            {
                Texture2D neutral = NeutralMeadow(grass, log);
                if (neutral != null)
                {
                    Material copy = AddRecoloured(folder, grass, recipe.Ground, sources, replacements);
                    copy.SetTexture("_BaseMap", neutral);
                    EditorUtility.SetDirty(copy);
                }
                else log.Add("grass kept — no meadow texture to neutralise");
            }

            IslandThemeDefinition theme = WriteTheme(folder, id, sources, replacements);

            // A rebuild of a recipe that now changes less leaves copies nothing points at. They are
            // named, not deleted — deleting is a person's call.
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var stale = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (stale != null && !replacements.Contains(stale)) log.Add("no longer used: " + stale.name + ".mat");
            }

            var displaced = new List<string>();
            bool listed = _addToSet && AddToSet(theme, displaced);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = theme;

            _report = recipe.DisplayName + ": " + sources.Count + " of " + island.Count + " materials themed, the rest " +
                      "left as the island has them.\nTheme: " + AssetDatabase.GetAssetPath(theme) +
                      "\nOpens at chapter " + (recipe.FromChapter + 1) +
                      (listed ? "\nListed in " + _set.name + "." : "\nNOT listed in a set.") +
                      (displaced.Count > 0 ? "\nReplaced in the list: " + string.Join(", ", displaced.ToArray()) : "") +
                      (log.Count > 0 ? "\n" + string.Join("\n", log.ToArray()) : "");
        }

        private static Material AddRecoloured(string folder, Material source, Color colour,
                                              List<Material> sources, List<Material> replacements)
        {
            Material copy = CopyOf(source, folder);
            if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", colour);
            if (copy.HasProperty("_Color")) copy.SetColor("_Color", colour);
            EditorUtility.SetDirty(copy);
            sources.Add(source);
            replacements.Add(copy);
            return copy;
        }

        private static string ThemePath(string folder, string id) => folder + "/IslandTheme_" + id + ".asset";

        /// <summary>The first material on the island that belongs to a theme rather than to the island.</summary>
        private static Material FirstThemeMaterial(List<Material> materials)
        {
            for (int i = 0; i < materials.Count; i++)
                if (AssetDatabase.GetAssetPath(materials[i]).StartsWith(ThemesRoot + "/", System.StringComparison.Ordinal))
                    return materials[i];
            return null;
        }

        /// <summary>
        /// The theme's copy of an island material, made fresh or — on a rebuild — reset to be an exact
        /// copy again before anything is changed on it, so a rebuild never inherits what a previous
        /// version of the recipe did.
        ///
        /// The SOURCE NAME IS KEPT. The folder is what tells the two apart, and it means a replacement
        /// for the lamps' emissive material would still carry the name they hunt for — the one case
        /// IslandThemeView refuses outright.
        /// </summary>
        private static Material CopyOf(Material source, string folder)
        {
            string to = folder + "/" + source.name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(to);
            if (existing == null)
            {
                AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), to);
                return AssetDatabase.LoadAssetAtPath<Material>(to);
            }
            existing.shader = source.shader;
            existing.CopyPropertiesFromMaterial(source);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Color BaseColor(Material m)
            => m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;

        /// <summary>
        /// Moves a family member the way its anchor moved: the anchor's new hue plus half the member's
        /// old offset from it, the member's saturation shifted by the anchor's shift, and its value
        /// scaled by the anchor's ratio. The anchor itself lands exactly on the target.
        /// </summary>
        private static Color Relate(Color member, Color anchorFrom, Color anchorTo, float maxSaturation)
        {
            float mh, ms, mv, ah, asat, av, th, ts, tv;
            Color.RGBToHSV(member, out mh, out ms, out mv);
            Color.RGBToHSV(anchorFrom, out ah, out asat, out av);
            Color.RGBToHSV(anchorTo, out th, out ts, out tv);

            float offset = ms < GreyCutoff || asat < GreyCutoff
                ? 0f
                : Mathf.DeltaAngle(ah * 360f, mh * 360f) / 360f * HueOffsetKept;
            float h = Mathf.Repeat(th + offset, 1f);
            float s = Mathf.Clamp(ms + (ts - asat), 0f, maxSaturation);
            float v = av > 0.0001f ? Mathf.Clamp01(mv * tv / av) : tv;

            Color c = Color.HSVToRGB(h, s, v);
            c.a = member.a;
            return c;
        }

        /// <summary>
        /// Nudges a metal toward the chapter's temperature WITHOUT moving its brightness — the tyres
        /// stay black and the white panels stay white; only the cast changes. That is what keeps the
        /// machinery reading as the same machinery in every chapter.
        /// </summary>
        private static Color Temper(Color metal, Color tint, float amount)
        {
            float h, s, v, ignoredH, ignoredS, keptV;
            Color.RGBToHSV(metal, out ignoredH, out ignoredS, out keptV);
            Color.RGBToHSV(Color.Lerp(metal, tint, amount), out h, out s, out v);
            Color c = Color.HSVToRGB(h, s, keptV);
            c.a = metal.a;
            return c;
        }

        private static bool Same(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

        /// <summary>
        /// The meadow texture with its colour taken out, made once and shared by every chapter.
        ///
        /// WHY. Base colour MULTIPLIES the map, and multiplying can only darken: a green meadow tinted
        /// white is still green, so the frost chapter would have had a green lawn in the snow. A grey
        /// meadow keeps the grass's detail and lets each chapter's ground colour be the colour. One
        /// texture for all seven chapters — the whole of the texture work this pass needs.
        ///
        /// Read from the file's bytes rather than the imported texture, so the source does not have to
        /// be marked readable, and imported with the source's own settings so it tiles and compresses
        /// exactly as the meadow does.
        /// </summary>
        private static Texture2D NeutralMeadow(Material grass, List<string> log)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(NeutralMeadowPath);
            if (existing != null) return existing;

            Texture source = grass.HasProperty("_BaseMap") ? grass.GetTexture("_BaseMap") : null;
            string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
            if (string.IsNullOrEmpty(sourcePath)) return null;

            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!image.LoadImage(File.ReadAllBytes(sourcePath)))
            {
                DestroyImmediate(image);
                return null;
            }

            Color32[] pixels = image.GetPixels32();
            var luminance = new float[pixels.Length];
            double sum = 0d;
            for (int i = 0; i < pixels.Length; i++)
            {
                luminance[i] = (0.2126f * pixels[i].r + 0.7152f * pixels[i].g + 0.0722f * pixels[i].b) / 255f;
                sum += luminance[i];
            }
            float scale = sum > 0d ? NeutralMean / (float)(sum / pixels.Length) : 1f;
            for (int i = 0; i < pixels.Length; i++)
            {
                byte l = (byte)Mathf.Clamp(Mathf.RoundToInt(luminance[i] * scale * 255f), 0, 255);
                pixels[i] = new Color32(l, l, l, pixels[i].a);
            }
            image.SetPixels32(pixels);

            EnsureFolder(SharedFolder);
            File.WriteAllBytes(NeutralMeadowPath, image.EncodeToPNG());
            DestroyImmediate(image);
            AssetDatabase.ImportAsset(NeutralMeadowPath);

            var from = (TextureImporter)AssetImporter.GetAtPath(sourcePath);
            var to = (TextureImporter)AssetImporter.GetAtPath(NeutralMeadowPath);
            var settings = new TextureImporterSettings();
            from.ReadTextureSettings(settings);
            to.SetTextureSettings(settings);
            to.textureCompression = from.textureCompression;
            to.maxTextureSize = from.maxTextureSize;
            to.SetPlatformTextureSettings(from.GetPlatformTextureSettings("Android"));
            to.SaveAndReimport();

            log.Add("made the shared neutral meadow: " + NeutralMeadowPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(NeutralMeadowPath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
