using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Dresses the island in the current chapter's theme, and re-dresses it the moment the player
    /// advances.
    ///
    /// WHAT IT ACTUALLY DOES. It walks the island root once and rewrites material SLOTS — no object
    /// is created, moved, enabled or destroyed, and no mesh is touched. The island the player knows
    /// stays exactly where it was; only what it is painted with changes. That is the whole feature:
    /// eight chapters, one map, a different look on each.
    ///
    /// WHY THE SWAP IS COMPUTED FROM BOTH THEMES. Going from one chapter to the next is not "apply
    /// the new table" — by then the island is already wearing the old one, and the new table's rows
    /// are keyed on the materials the island was AUTHORED with, which are no longer on any renderer.
    /// So the map is built as old-replacement to original to new-replacement, collapsed into a single
    /// lookup. That also makes the sweep repeatable: running it twice with the same theme is a no-op
    /// for anything already dressed, and still catches a renderer that appeared in between.
    ///
    /// WHY <see cref="Awake"/>. <c>CoalOperation.Start</c> copies a map material as the template for
    /// every road, rail and pile it builds at runtime, so the theme has to be on the island before
    /// that happens — otherwise the chapter's island is themed and its roads are not.
    /// </summary>
    public sealed class IslandThemeView : MonoBehaviour
    {
        /// <summary>
        /// The emissive material the night lights find BY NAME, and the one material a theme is not
        /// allowed to move. See <c>BuildingLights</c> and <c>StreetLamps</c>.
        /// </summary>
        private const string LampMaterial = "lamp_glow";

        [Header("Tema")]
        [Tooltip("Oyunun tüm ada temaları. Boş bırakılırsa ada kendi malzemeleriyle kalır.")]
        [SerializeField] private IslandThemeSet themes;

        [Header("Hedef")]
        [Tooltip("Boyanacak sahne kökü. CoalOperation ile AYNI ad olmalıdır.")]
        [SerializeField] private string islandRootName = "Island_Shipyard";

        private Transform _root;
        private ChapterProgressionService _progression;

        /// <summary>What the island is wearing right now; null means its own authored materials.</summary>
        private IslandThemeDefinition _worn;

        private readonly Dictionary<Material, Material> _map = new Dictionary<Material, Material>(64);
        private readonly List<Renderer> _renderers = new List<Renderer>(2048);

        /// <summary>The theme on the island, for anything that wants to name it. Null until one is on.</summary>
        public IslandThemeDefinition Worn => _worn;

        private void Awake()
        {
            // A scene-root scan rather than Find, for the reason CoalOperation gives: an island
            // activated this very frame still resolves.
            var roots = gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name == islandRootName) { _root = roots[i].transform; break; }

            if (_root == null)
            {
                Debug.LogWarning("IslandThemeView: '" + islandRootName + "' not found — disabled.", this);
                enabled = false;
                return;
            }

            _progression = ServiceLocator.Get<ChapterProgressionService>();
            if (_progression != null) _progression.Advanced += ApplyChapter;

            ApplyChapter(_progression != null ? _progression.Current : 0);
        }

        private void OnDestroy()
        {
            if (_progression != null) _progression.Advanced -= ApplyChapter;
        }

        /// <summary>
        /// Puts the island into the look <paramref name="chapter"/> calls for. Safe to call with the
        /// chapter it is already showing, and safe to call when no theme covers it — that undresses
        /// the island back to its authored materials rather than leaving the last chapter's paint on.
        /// </summary>
        public void ApplyChapter(int chapter)
        {
            if (_root == null) return;

            IslandThemeDefinition next = themes != null ? themes.ForChapter(chapter) : null;
            BuildSwapMap(_worn, next, _map);
            Repaint(_root, _map, _renderers);
            _worn = next;
        }

        /// <summary>
        /// Collapses "take the old theme off, put the new one on" into one material-to-material table.
        ///
        /// Three passes, and the last one is the one that matters: a renderer may be holding the old
        /// theme's material OR the island's own, depending on whether it existed when the last theme
        /// went on, and both have to land on the same answer.
        ///
        /// A row is ignored when either side is empty — an empty replacement is how a half-authored
        /// theme says "keep this one", which is what lets the art arrive a material at a time.
        /// </summary>
        public static void BuildSwapMap(IslandThemeDefinition from, IslandThemeDefinition to,
                                        Dictionary<Material, Material> map)
        {
            if (map == null) return;
            map.Clear();

            // 1. Back to what the island was authored with.
            for (int i = 0; from != null && i < from.SwapCount; i++)
            {
                IslandThemeDefinition.MaterialSwap row = from.SwapAt(i);
                if (!Usable(row)) continue;
                map[row.replacement] = row.source;
            }

            // 2. Forward into the new theme.
            for (int i = 0; to != null && i < to.SwapCount; i++)
            {
                IslandThemeDefinition.MaterialSwap row = to.SwapAt(i);
                if (!Usable(row)) continue;
                map[row.source] = row.replacement;
            }

            // 3. Straight through, for a material the old theme laid down over an original the new
            //    theme also dresses. Without this the island would step back to its own material and
            //    stay there, one chapter behind.
            for (int i = 0; from != null && to != null && i < from.SwapCount; i++)
            {
                IslandThemeDefinition.MaterialSwap row = from.SwapAt(i);
                if (!Usable(row)) continue;
                Material through;
                if (map.TryGetValue(row.source, out through) && through != row.source)
                    map[row.replacement] = through;
            }

            // A row that asks for the material it already has is work with nothing to show for it.
            var identities = new List<Material>();
            foreach (KeyValuePair<Material, Material> pair in map)
                if (pair.Key == pair.Value) identities.Add(pair.Key);
            for (int i = 0; i < identities.Count; i++) map.Remove(identities[i]);
        }

        /// <summary>
        /// Rewrites every slot the table names, under <paramref name="root"/> and including inactive
        /// objects — a building the player has not bought yet is switched off, and it has to be
        /// wearing the right paint for the day they buy it. Returns how many renderers changed.
        /// </summary>
        public static int Repaint(Transform root, Dictionary<Material, Material> map, List<Renderer> buffer)
        {
            if (root == null || map == null || map.Count == 0) return 0;

            root.GetComponentsInChildren(true, buffer);
            int changed = 0;
            for (int r = 0; r < buffer.Count; r++)
            {
                Renderer renderer = buffer[r];
                Material[] slots = renderer.sharedMaterials;
                bool touched = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    Material replacement;
                    if (slots[i] == null || !map.TryGetValue(slots[i], out replacement)) continue;
                    slots[i] = replacement;
                    touched = true;
                }
                if (!touched) continue;
                renderer.sharedMaterials = slots;   // sharedMaterials, never materials: instancing the
                changed++;                          // island would cost 1,866 materials and the batcher
            }
            buffer.Clear();
            return changed;
        }

        /// <summary>
        /// Whether a row says anything. Half-filled rows are the normal state of theme art in
        /// progress; a row aimed at the lamps is not — it would put the island's night lights out
        /// with nothing logged, because they are found by material NAME at startup.
        /// </summary>
        private static bool Usable(IslandThemeDefinition.MaterialSwap row)
        {
            if (row.source == null || row.replacement == null || row.replacement == row.source) return false;
            if (row.source.name != LampMaterial) return true;

            if (row.replacement.name == LampMaterial) return true;
            Debug.LogWarning("IslandThemeView: a theme swaps '" + LampMaterial + "' for '" +
                             row.replacement.name + "' — ignored, the night lights find that material " +
                             "by name. Name the replacement '" + LampMaterial + "' to theme it.");
            return false;
        }
    }
}
