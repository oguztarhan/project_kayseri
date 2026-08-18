using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kayseri.IslandTools
{
    /// <summary>
    /// The window you swap models from: every model the generator makes, listed by district and by
    /// name, each with a slot to drop a replacement into.
    ///
    /// Scan reads the twenty-four phase prefabs and fills the list in, so the catalogue is never typed by
    /// hand and never goes stale when the map changes. Apply writes the swaps into the prefabs; the
    /// same pass runs automatically at the end of a rebuild, so a re-export from Blender keeps them.
    /// </summary>
    public sealed class IslandModelOverridesWindow : EditorWindow
    {
        private const string AssetPath = "Assets/Art/KayseriIsland/IslandModelOverrides.asset";
        // All eight. The four derived islands (silver, ruby, emerald, diamond) re-export one of
        // the four authored maps, so they carry the SAME district/model names and add nothing new
        // to the catalogue - but Apply still has to walk their prefabs, or a swap authored against
        // the copper map would land on copper and not on silver.
        private static readonly string[] Islands =
        {
            "Coal", "Copper", "Iron", "Silver", "Gold", "Ruby", "Emerald", "Diamond"
        };
        private const int Phases = 3;

        private IslandModelOverrides _catalogue;
        private Vector2 _scroll;
        private string _filter = "";
        private bool _onlyAssigned;
        private readonly HashSet<string> _closed = new HashSet<string>();

        [MenuItem("Kayseri/Island/Model Overrides", false, 30)]
        public static void Open()
        {
            var w = GetWindow<IslandModelOverridesWindow>("Island Models");
            w.minSize = new Vector2(560f, 400f);
            w.Load();
        }

        /// <summary>The catalogue asset, made on first use so there is nothing to set up by hand.</summary>
        public static IslandModelOverrides LoadOrCreate()
        {
            var c = AssetDatabase.LoadAssetAtPath<IslandModelOverrides>(AssetPath);
            if (c != null) return c;
            c = CreateInstance<IslandModelOverrides>();
            string dir = System.IO.Path.GetDirectoryName(AssetPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(dir), System.IO.Path.GetFileName(dir));
            AssetDatabase.CreateAsset(c, AssetPath);
            AssetDatabase.SaveAssets();
            return c;
        }

        private void Load() { _catalogue = LoadOrCreate(); }

        private void OnGUI()
        {
            if (_catalogue == null) Load();

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scan islands", GUILayout.Height(24f))) Rescan();
                if (GUILayout.Button("Apply to prefabs", GUILayout.Height(24f))) ApplyAll();
                if (GUILayout.Button("Clear from prefabs", GUILayout.Height(24f))) ClearAll();
            }
            EditorGUILayout.LabelField(
                "Drop a model onto a row to replace every copy of it. Rows left empty keep the " +
                "generated model. Re-exporting from Blender re-applies these automatically.",
                EditorStyles.wordWrappedMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _filter = EditorGUILayout.TextField("Search", _filter);
                _onlyAssigned = GUILayout.Toggle(_onlyAssigned, "Assigned only", EditorStyles.miniButton,
                                                 GUILayout.Width(110f));
            }

            var entries = _catalogue.Entries;
            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("Nothing listed yet — press Scan islands.", MessageType.Info);
                return;
            }

            // Grouped by district, in the order the generator exports them.
            var byGroup = new SortedDictionary<string, List<IslandModelOverrides.Entry>>(
                System.StringComparer.Ordinal);
            int assigned = 0;
            foreach (var e in entries)
            {
                if (e == null) continue;
                if (e.Replacement != null) assigned++;
                if (_onlyAssigned && e.Replacement == null) continue;
                if (!string.IsNullOrEmpty(_filter) &&
                    (e.Model ?? "").IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    (e.Group ?? "").IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!byGroup.TryGetValue(e.Group ?? "?", out var list))
                    byGroup[e.Group ?? "?"] = list = new List<IslandModelOverrides.Entry>();
                list.Add(e);
            }

            EditorGUILayout.LabelField(entries.Count + " models listed, " + assigned + " replaced",
                                       EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var kv in byGroup)
            {
                bool open = !_closed.Contains(kv.Key);
                bool now = EditorGUILayout.Foldout(open, kv.Key + "  (" + kv.Value.Count + ")", true,
                                                   EditorStyles.foldoutHeader);
                if (now != open)
                {
                    if (now) _closed.Remove(kv.Key); else _closed.Add(kv.Key);
                }
                if (!now) continue;

                EditorGUI.indentLevel++;
                foreach (var e in kv.Value) DrawEntry(e);
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4f);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawEntry(IslandModelOverrides.Entry e)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(e.Title, EditorStyles.boldLabel, GUILayout.Width(260f));
                    EditorGUI.BeginChangeCheck();
                    var pick = (GameObject)EditorGUILayout.ObjectField(
                        e.Replacement, typeof(GameObject), false);
                    if (EditorGUI.EndChangeCheck()) { e.Replacement = pick; Dirty(); }

                    if (GUILayout.Button("+", GUILayout.Width(24f))) AddException(e);
                    // Only the extra island/phase rows can be removed; the base row comes from Scan
                    // and would just reappear.
                    bool exception = e.Island != IslandModelOverrides.IslandFilter.Any ||
                                     e.Phase != IslandModelOverrides.PhaseFilter.Any;
                    using (new EditorGUI.DisabledScope(!exception))
                        if (GUILayout.Button("−", GUILayout.Width(24f)))
                        {
                            _catalogue.Entries.Remove(e);
                            Dirty();
                            GUIUtility.ExitGUI();
                        }
                }

                if (e.Replacement == null) return;

                EditorGUI.BeginChangeCheck();
                using (new EditorGUILayout.HorizontalScope())
                {
                    e.FitToOriginal = EditorGUILayout.ToggleLeft(
                        "Fit to original size", e.FitToOriginal, GUILayout.Width(150f));
                    e.Scale = EditorGUILayout.FloatField("Scale", e.Scale);
                }
                e.Offset = EditorGUILayout.Vector3Field("Offset", e.Offset);
                e.Rotation = EditorGUILayout.Vector3Field("Rotation", e.Rotation);
                if (EditorGUI.EndChangeCheck()) Dirty();
            }
        }

        /// <summary>Adds a second row for the same model, so one island or one phase can differ.</summary>
        private void AddException(IslandModelOverrides.Entry from)
        {
            _catalogue.Entries.Add(new IslandModelOverrides.Entry
            {
                Group = from.Group,
                Model = from.Model,
                Island = IslandModelOverrides.IslandFilter.Coal,
                Phase = IslandModelOverrides.PhaseFilter.Any,
                Replacement = from.Replacement,
                FitToOriginal = from.FitToOriginal,
                Scale = from.Scale,
                Offset = from.Offset,
                Rotation = from.Rotation,
            });
            Dirty();
        }

        private void Rescan()
        {
            var found = IslandModelSwapper.Scan(Islands, Phases);
            if (found.Count == 0)
            {
                EditorUtility.DisplayDialog("Island Models",
                    "No phase prefabs found under Assets/Prefabs/Island. Build the islands first.", "OK");
                return;
            }

            var have = new HashSet<string>();
            foreach (var e in _catalogue.Entries)
                if (e != null && e.Island == IslandModelOverrides.IslandFilter.Any &&
                    e.Phase == IslandModelOverrides.PhaseFilter.Any)
                    have.Add(e.Group + "/" + e.Model);

            int added = 0;
            foreach (var kv in found)
                foreach (var model in kv.Value)
                {
                    if (!have.Add(kv.Key + "/" + model)) continue;
                    _catalogue.Entries.Add(new IslandModelOverrides.Entry { Group = kv.Key, Model = model });
                    added++;
                }

            Dirty();
            Debug.Log("[Island] Scan: " + found.Count + " districts, " + added + " new models listed, " +
                      _catalogue.Entries.Count + " rows total.");
        }

        private void ApplyAll() { Run(false); }

        private void ClearAll() { Run(true); }

        private void Run(bool clear)
        {
            int touched = 0, prefabs = 0;
            try
            {
                foreach (var isle in Islands)
                {
                    for (int p = 1; p <= Phases; p++)
                    {
                        string path = "Assets/Prefabs/Island/" + isle + "/Island_Phase" + p + ".prefab";
                        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                        EditorUtility.DisplayProgressBar("Island Models", isle + " phase " + p,
                                                         prefabs / (float)(Islands.Length * Phases));

                        // Edited off-stage: LoadPrefabContents gives a real hierarchy without opening
                        // a prefab stage or touching whatever scene is open.
                        var root = PrefabUtility.LoadPrefabContents(path);
                        if (clear) IslandModelSwapper.Clear(root);
                        else touched += IslandModelSwapper.Apply(root, isle, p, _catalogue);
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        PrefabUtility.UnloadPrefabContents(root);
                        prefabs++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            AssetDatabase.SaveAssets();
            Debug.Log(clear
                ? "[Island] Overrides cleared from " + prefabs + " phase prefabs."
                : "[Island] " + touched + " objects replaced across " + prefabs + " phase prefabs.");
        }

        private void Dirty()
        {
            EditorUtility.SetDirty(_catalogue);
            AssetDatabase.SaveAssetIfDirty(_catalogue);
        }
    }
}
