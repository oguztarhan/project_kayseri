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
        /// The one district the holder is wrong for.
        ///
        /// CoalOperation.PrepareAuthoredIsland binds the rolling stock by name out of the Vehicles
        /// group — "train", "wagon", "truck_road_ore" — reparents those very transforms to the island
        /// root and drives them on a rig whose base pose, coupling lengths and cargo placement all
        /// come off the authored meshes. Standing a replacement in the holder instead would leave the
        /// operation driving objects whose renderers this swap just switched off: an invisible fleet.
        ///
        /// So a driven object keeps its transform and gains the replacement as a child. The gameplay
        /// layer still finds what it looks for, every measurement still reads the authored mesh
        /// through the disabled renderer's MeshFilter, and only the drawing changes hands.
        /// </summary>
        public const string DrivenGroup = "Vehicles";

        /// <summary>What the in-place replacement is called under the object it draws for.</summary>
        public const string DrivenChildName = "_Model";

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
        /// Every distinct district/model pair across all eight islands and all three phases, so the
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
                        foreach (Transform obj in group)
                        {
                            if (obj.name == HolderName) continue;
                            set.Add(ModelName(obj.name, group.name));
                        }
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

            var groups = new List<Transform>();
            foreach (Transform g in phaseRoot.transform)
                if (g.name != HolderName) groups.Add(g);

            foreach (Transform group in groups)
            {
                bool driven = group.name == DrivenGroup;
                // One holder per DISTRICT, inside it — not one for the whole phase.
                //
                // IslandPhaseController leaves all three phase roots active and switches districts
                // within them, so a group it does not know about draws under every phase at once. Its
                // own Theme comment records that happening. A holder parked at the phase root is
                // exactly such a group: it made all three phases' replacements stand in the same place,
                // which reads as the buildings growing into each other on upgrade. Inside the district
                // it simply inherits that district's phase. The groups sit at identity, so nothing about
                // the placement below changes.
                Transform holder = null;
                foreach (Transform obj in group)
                {
                    if (obj.name == HolderName) continue;
                    var entry = catalogue.Resolve(group.name, ModelName(obj.name, group.name),
                                                  isleF, phaseF);
                    if (entry == null) continue;

                    Bounds? original = RendererBounds(obj);
                    if (original == null) continue;   // nothing drawn here, nothing to stand in for

                    // Taken before the replacement exists. A driven object parents it underneath,
                    // and "hide everything below obj" would then switch off the very thing that
                    // replaced it.
                    var hide = obj.GetComponentsInChildren<Renderer>(true);

                    // Two ways for a row to end in nothing being drawn: the whole model is unwanted
                    // (parked scenery that reads as the player's fleet), or this particular copy has
                    // landed on ground the player is meant to build on. Both switch the original off
                    // and stand nothing up, which is exactly what Clear already knows how to undo.
                    if (entry.Hide ||
                        (entry.SkipOverBuildings && OverBuildingArea(phaseRoot, original.Value)))
                    {
                        for (int i = 0; i < hide.Length; i++) hide[i].enabled = false;
                        swapped++;
                        continue;
                    }

                    Transform parent;
                    if (driven) parent = obj;
                    else
                    {
                        if (holder == null)
                        {
                            holder = new GameObject(HolderName).transform;
                            holder.SetParent(group, false);
                        }
                        parent = holder;
                    }

                    var copy = (GameObject)PrefabUtility.InstantiatePrefab(entry.Replacement);
                    if (copy == null) continue;
                    copy.name = driven ? DrivenChildName : obj.name;
                    copy.transform.SetParent(parent, false);

                    Bounds ob = original.Value;
                    copy.transform.SetPositionAndRotation(ob.center, obj.rotation);
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

                    // Stood on the footprint the original DRAWS, matched box to box — and done last,
                    // because scale and rotation both move the box.
                    //
                    // Neither side's pivot can be trusted. On the map only the scattered foliage and
                    // rock carry a placement in their transforms; every unique building is one mesh
                    // with its position baked in and a pivot left at the district origin, so
                    // Mine.Adit0, Adit1 and Adit2 all sit at (0, 16, 0) while their meshes stand 150
                    // metres east. And Kenney is no more consistent — a tree stands on its origin,
                    // an industrial shed hangs off one bottom corner of it.
                    //
                    // Centre in X/Z and rest on the same floor, so the stand-in covers the ground the
                    // original covered whatever either pivot happens to mean.
                    Bounds? placed = RendererBounds(copy.transform);
                    if (placed != null)
                        copy.transform.position += new Vector3(ob.center.x - placed.Value.center.x,
                                                               ob.min.y - placed.Value.min.y,
                                                               ob.center.z - placed.Value.center.z);

                    if (entry.Offset != Vector3.zero)
                        copy.transform.position += entry.Offset;

                    for (int i = 0; i < hide.Length; i++) hide[i].enabled = false;
                    // The builder keeps the whole Vehicles group out of the static batch, because a
                    // batched renderer stops following the transform it is driven by. The stand-in
                    // has to inherit that, not the phase root's static flag.
                    if (driven) SetStaticRecursive(copy, obj.gameObject.isStatic);
                    swapped++;
                }

                if (holder != null) SetStaticRecursive(holder.gameObject, group.gameObject.isStatic);
            }

            return swapped;
        }

        /// <summary>The district groups. Scatter is unwelcome on these; Terrain and Roads it belongs to.</summary>
        private static readonly string[] Districts =
            { "Mine", "Depot", "Refinery", "Market", "Port", "Power", "Civic", "Fleet", "Haul", "Sites", "Theme" };

        /// <summary>How far back off a building the ground is kept clear, in metres.</summary>
        private const float BuildingMargin = 5f;

        // Where the island is built on, in world X/Z, cached per phase root for one Apply run.
        //
        // Two things count. The slabs, which the map names itself — Mine.Apron, Depot.Yard, Market.Pad,
        // Civic.Plaza — and the buildings standing on them. The slabs alone are not enough: on Gold not
        // one boulder touches a slab while a dozen stand in the middle of the works, because the
        // districts spill off their aprons onto open ground.
        private static GameObject _areaOwner;
        private static List<Rect> _areas;

        private static bool OverBuildingArea(GameObject phaseRoot, Bounds b)
        {
            if (_areaOwner != phaseRoot)
            {
                _areaOwner = phaseRoot;
                _areas = new List<Rect>();
                foreach (Transform group in phaseRoot.transform)
                {
                    if (group.name == HolderName) continue;
                    bool district = false;
                    for (int i = 0; i < Districts.Length; i++)
                        if (group.name == Districts[i]) { district = true; break; }

                    foreach (Transform obj in group)
                    {
                        string n = obj.name;
                        bool slab = n.EndsWith("Pad", System.StringComparison.Ordinal) ||
                                    n.EndsWith("Yard", System.StringComparison.Ordinal) ||
                                    n.EndsWith("Apron", System.StringComparison.Ordinal) ||
                                    n.EndsWith("Plaza", System.StringComparison.Ordinal);
                        if (!slab && !district) continue;

                        Bounds? area = RendererBounds(obj);
                        if (area == null) continue;
                        Bounds a = area.Value;
                        // A slab is taken as drawn. A district object only counts once it is tall
                        // enough to be a structure — the markers, ghosts and painted ground in these
                        // groups are not things a rock can be in the way of.
                        if (!slab && a.size.y < 3f) continue;
                        float m = slab ? 0f : BuildingMargin;
                        _areas.Add(new Rect(a.min.x - m, a.min.z - m,
                                            a.size.x + 2f * m, a.size.z + 2f * m));
                    }
                }
            }

            // Tested on the footprint, not the centre: a boulder with two thirds of itself on the pad
            // is still in the way of a building.
            var foot = new Rect(b.min.x, b.min.z, b.size.x, b.size.z);
            for (int i = 0; i < _areas.Count; i++) if (_areas[i].Overlaps(foot)) return true;
            return false;
        }

        /// <summary>Puts the generated map back: drops the replacements and re-enables what they hid.</summary>
        public static void Clear(GameObject phaseRoot)
        {
            if (phaseRoot == null) return;
            var groups = new List<Transform>();
            foreach (Transform g in phaseRoot.transform) groups.Add(g);

            for (int i = 0; i < groups.Count; i++)
            {
                Transform group = groups[i];
                // A holder sitting at the phase root is one an older build left there, back when the
                // swap was ungated by phase. Drop it whole.
                if (group.name == HolderName) { Object.DestroyImmediate(group.gameObject); continue; }

                Transform holder = group.Find(HolderName);
                if (holder != null) Object.DestroyImmediate(holder.gameObject);

                foreach (Transform obj in group)
                {
                    // Dropped before the renderers come back on, so the ones being re-enabled are
                    // the object's own and not a stand-in that is about to be deleted anyway.
                    Transform inPlace = obj.Find(DrivenChildName);
                    if (inPlace != null) Object.DestroyImmediate(inPlace.gameObject);
                    HideRenderers(obj, false);
                }
            }
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
