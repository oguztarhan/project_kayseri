using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Kayseri.IslandTools
{
    /// <summary>
    /// Reads <see cref="IslandModelOverrides"/> and stands the replacement models up in place of the
    /// generated ones.
    ///
    /// The generated objects are never destroyed, only their renderers switched off, and every
    /// replacement goes under one "_Overrides" holder. That keeps the swap reversible — Clear puts the
    /// map back exactly — and it keeps the district groups as untouched FBX prefab instances, which is
    /// what stops the phase prefabs from ballooning: deleting a child of a prefab instance would strip
    /// the linkage and inline the whole group.
    /// </summary>
    public static class IslandModelSwapper
    {
        public const string HolderName = "_Overrides";

        /// <summary>
        /// The catalogue name for one generated object: its district and its model name with the
        /// instance number taken off.
        ///
        /// The generator names an object for its district and then numbers it — "Depot.Silo3",
        /// "Pine.040", "truck_road_ore1". The district prefix is dropped when it repeats the collection
        /// it already sits in, Blender's ".040" duplicate suffix goes, and so does a trailing run of
        /// digits. What is left is what a person would call the thing: Silo, Pine, truck_road_ore.
        /// </summary>
        public static string ModelName(string objectName, string group)
        {
            string n = objectName;

            if (n.StartsWith(group + ".", System.StringComparison.Ordinal))
                n = n.Substring(group.Length + 1);

            // Blender's duplicate suffix: ".001". Only when every character after the dot is a digit,
            // so "Train.wagon" keeps its dot and "Pine.040" loses one.
            int dot = n.LastIndexOf('.');
            if (dot > 0 && dot + 1 < n.Length)
            {
                bool digits = true;
                for (int i = dot + 1; i < n.Length && digits; i++)
                    if (!char.IsDigit(n[i])) digits = false;
                if (digits) n = n.Substring(0, dot);
            }

            int end = n.Length;
            while (end > 0 && char.IsDigit(n[end - 1])) end--;
            // "Silo0" -> "Silo", but never strip a name that is all digits down to nothing.
            return end > 0 ? n.Substring(0, end) : n;
        }

        /// <summary>
        /// Every distinct district/model pair across all four islands and all three phases, so the
        /// window can offer the whole map as a list without anyone typing a name.
        /// </summary>
        public static SortedDictionary<string, SortedSet<string>> Scan(string[] islands, int phases)
        {
            var found = new SortedDictionary<string, SortedSet<string>>(System.StringComparer.Ordinal);
            foreach (var isle in islands)
            {
                for (int p = 1; p <= phases; p++)
                {
                    var root = AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/Prefabs/Island/" + isle + "/Island_Phase" + p + ".prefab");
                    if (root == null) continue;
                    foreach (Transform group in root.transform)
                    {
                        if (group.name == HolderName) continue;
                        if (!found.TryGetValue(group.name, out var set))
                            found[group.name] = set = new SortedSet<string>(System.StringComparer.Ordinal);
                        foreach (Transform obj in group) set.Add(ModelName(obj.name, group.name));
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// Applies the catalogue to one phase hierarchy, in place. Returns how many objects were
        /// replaced. Call it on the in-memory root BEFORE it is saved as a prefab.
        /// </summary>
        public static int Apply(GameObject phaseRoot, string island, int phase,
                                IslandModelOverrides catalogue)
        {
            if (phaseRoot == null || catalogue == null) return 0;
            Clear(phaseRoot);

            var isleF = IslandModelOverrides.ToIsland(island);
            var phaseF = IslandModelOverrides.ToPhase(phase);
            int swapped = 0;
            Transform holder = null;

            var groups = new List<Transform>();
            foreach (Transform g in phaseRoot.transform)
                if (g.name != HolderName) groups.Add(g);

            foreach (Transform group in groups)
            {
                foreach (Transform obj in group)
                {
                    var entry = catalogue.Resolve(group.name, ModelName(obj.name, group.name),
                                                  isleF, phaseF);
                    if (entry == null) continue;

                    Bounds? original = RendererBounds(obj);
                    if (original == null) continue;   // nothing drawn here, nothing to stand in for

                    if (holder == null)
                    {
                        holder = new GameObject(HolderName).transform;
                        holder.SetParent(phaseRoot.transform, false);
                    }

                    var copy = (GameObject)PrefabUtility.InstantiatePrefab(entry.Replacement);
                    if (copy == null) continue;
                    copy.name = obj.name;
                    copy.transform.SetParent(holder, false);

                    // The generated object carries its world placement in its own transform, and the
                    // holder sits at the phase root's origin, so the placement copies straight across.
                    copy.transform.SetPositionAndRotation(obj.position, obj.rotation);
                    copy.transform.localScale = Vector3.one;

                    float fit = entry.Scale;
                    if (entry.FitToOriginal)
                    {
                        Bounds? mine = RendererBounds(copy.transform);
                        // Compared on the diagonal rather than one axis: a Kenney silo and a generated
                        // one rarely agree about which axis is the tall one.
                        if (mine != null && mine.Value.size.magnitude > 1e-4f)
                            fit *= original.Value.size.magnitude / mine.Value.size.magnitude;
                    }
                    copy.transform.localScale = Vector3.one * fit;

                    if (entry.Rotation != Vector3.zero)
                        copy.transform.rotation = obj.rotation * Quaternion.Euler(entry.Rotation);
                    if (entry.Offset != Vector3.zero)
                        copy.transform.position += entry.Offset;

                    HideRenderers(obj, true);
                    swapped++;
                }
            }

            if (holder != null) SetStaticRecursive(holder.gameObject, phaseRoot.isStatic);
            return swapped;
        }

        /// <summary>Puts the generated map back: drops the replacements and re-enables what they hid.</summary>
        public static void Clear(GameObject phaseRoot)
        {
            if (phaseRoot == null) return;
            foreach (Transform group in phaseRoot.transform)
            {
                if (group.name == HolderName) continue;
                foreach (Transform obj in group) HideRenderers(obj, false);
            }
            Transform holder = phaseRoot.transform.Find(HolderName);
            if (holder != null) Object.DestroyImmediate(holder.gameObject);
        }

        private static void HideRenderers(Transform t, bool hidden)
        {
            var rends = t.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++) rends[i].enabled = !hidden;
        }

        private static Bounds? RendererBounds(Transform t)
        {
            var rends = t.GetComponentsInChildren<Renderer>(true);
            Bounds b = default;
            bool any = false;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] is ParticleSystemRenderer) continue;
                if (!any) { b = rends[i].bounds; any = true; }
                else b.Encapsulate(rends[i].bounds);
            }
            return any ? b : (Bounds?)null;
        }

        private static void SetStaticRecursive(GameObject go, bool value)
        {
            go.isStatic = value;
            foreach (Transform child in go.transform) SetStaticRecursive(child.gameObject, value);
        }
    }
}
