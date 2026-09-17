using Game.Core;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Every island theme the game ships, in one asset — the single reference the island scene is
    /// wired to.
    /// Create via: Assets &gt; Create &gt; Ore Empire &gt; Island Theme Set.
    ///
    /// Adding next chapter's look is dropping an asset into this list. Nothing in the scene is
    /// re-wired, no code is touched, and a chapter with no theme keeps the island's own materials,
    /// so the list is allowed to be shorter than the chapter ladder for as long as the art takes.
    ///
    /// The rule for picking one lives in <see cref="IslandThemes"/>, which cannot see this assembly;
    /// this only hands it the opening chapters and reads the answer back.
    /// </summary>
    [CreateAssetMenu(fileName = "IslandThemeSet", menuName = "Ore Empire/Island Theme Set", order = 27)]
    public sealed class IslandThemeSet : ScriptableObject
    {
        [Tooltip("Sıra önemli değildir; her tema kendi açılış bölümünü taşır. Boş yuva atlanır.")]
        [SerializeField] private IslandThemeDefinition[] themes = new IslandThemeDefinition[0];

        public int Count => themes != null ? themes.Length : 0;

        public IslandThemeDefinition At(int index)
            => themes != null && index >= 0 && index < themes.Length ? themes[index] : null;

        /// <summary>The theme covering a chapter, or null when the island keeps its own materials.</summary>
        public IslandThemeDefinition ForChapter(int chapter) => At(IndexForChapter(chapter));

        /// <summary>The covering theme's index, or <see cref="IslandThemes.None"/>.</summary>
        public int IndexForChapter(int chapter) => IslandThemes.IndexFor(chapter, FromChapters());

        /// <summary>
        /// Opening chapters in the order the list is authored, with an empty slot as -1 so
        /// <see cref="IslandThemes.IndexFor"/> skips it.
        ///
        /// Built fresh on every call rather than cached. It is read when the island loads and when a
        /// chapter changes — twice a session, over at most a handful of entries — and a cache here
        /// would go stale the moment a designer edited a theme's opening chapter on the THEME asset,
        /// where this one's OnValidate never runs.
        /// </summary>
        private int[] FromChapters()
        {
            var opens = new int[Count];
            for (int i = 0; i < opens.Length; i++)
                opens[i] = themes[i] != null ? themes[i].FromChapter : IslandThemes.None;
            return opens;
        }
    }
}
