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
            EditorGUILayout.LabelField("Renk (isteğe bağlı)", EditorStyles.boldLabel);
            _hueShift = EditorGUILayout.Slider("Hue shift", _hueShift, -0.5f, 0.5f);
            _saturation = EditorGUILayout.Slider("Saturation", _saturation, 0f, 2f);
            _brightness = EditorGUILayout.Slider("Brightness", _brightness, 0f, 2f);
            if (!Tints) EditorGUILayout.LabelField(" ", "Birebir kopya.", EditorStyles.miniLabel);

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
            if (AssetDatabase.IsValidFolder(folder))
            {
                _report = "Already exists: " + folder + "\nDelete it or pick another id.";
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

            EnsureFolder(ThemesRoot);
            AssetDatabase.CreateFolder(ThemesRoot, id);

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

                // The SOURCE NAME IS KEPT. The folder is what tells the two apart, and it means a
                // replacement for the lamps' emissive material would still carry the name they hunt
                // for — the one case IslandThemeView refuses outright.
                string to = folder + "/" + sources[i].name + ".mat";
                AssetDatabase.CopyAsset(from, to);
                var copy = AssetDatabase.LoadAssetAtPath<Material>(to);
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
            var theme = CreateInstance<IslandThemeDefinition>();
            AssetDatabase.CreateAsset(theme, folder + "/IslandTheme_" + id + ".asset");

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

        private bool AddToSet(IslandThemeDefinition theme)
        {
            if (_set == null) return false;

            var so = new SerializedObject(_set);
            SerializedProperty themes = so.FindProperty("themes");
            for (int i = 0; i < themes.arraySize; i++)
                if (themes.GetArrayElementAtIndex(i).objectReferenceValue == theme) return true;

            themes.arraySize++;
            themes.GetArrayElementAtIndex(themes.arraySize - 1).objectReferenceValue = theme;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
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
