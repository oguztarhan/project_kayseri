using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Moves the player from one chapter to the next, on the same island.
    ///
    /// WHAT A CHAPTER CHANGE ACTUALLY IS. Every chapter is played on the one map, and each one files
    /// its progression under its own prefix — see <see cref="Chapters.Namespaces"/>. Advancing does
    /// not delete a level, a building or a yard: it changes WHICH PREFIX the island reads, and the new
    /// one has no rows yet, so the chapter starts from nothing while the last one stays on disk
    /// exactly as it was left. A reset that writes nothing cannot half-finish.
    ///
    /// UNLOCKING IS ONE LIST ENTRY. <see cref="ChapterService.Progress"/> already calls a chapter
    /// owned when <c>unlockedIslands</c> holds its namespace, so adding the string is the whole
    /// transaction — <see cref="ChapterService.Current"/> moves on by itself, and
    /// <see cref="MarketService"/> lets the new chapter's yard sell for the same reason.
    ///
    /// NOTHING OWED IS LEFT BEHIND. A chapter can be finished with beats still uncollected, and the
    /// player would have no way back to them once the screen moved on, so the advance sweeps them
    /// first and pays out. That sweep is <see cref="ChapterService.ClaimChapter"/>, the same one the
    /// COLLECT ALL button uses — a beat cannot be paid twice because a claimed beat is marked in the
    /// save and <see cref="ChapterService.CanClaim"/> refuses it from then on.
    ///
    /// WHAT THIS DELIBERATELY DOES NOT DO: it does not touch the island. Rebinding the simulation to
    /// the new namespace and re-skinning the map belong to the components that own those things; this
    /// raises <see cref="Advanced"/> and they listen. Keeping the transaction separate from the
    /// rebuild is what lets the save be written BEFORE anything reloads.
    /// </summary>
    public sealed class ChapterProgressionService
    {
        private readonly SaveData _data;
        private readonly ChapterService _chapters;

        /// <summary>
        /// How this asks for the save to be written. A callback rather than the
        /// <see cref="SaveService"/> itself, the arrangement <c>IslandYardUpgradeUI.Configure</c>
        /// uses: the advance is a save-shape transaction and is tested as one, and a test should not
        /// have to put a file on disk to watch it happen.
        /// </summary>
        private readonly Action _save;

        /// <summary>
        /// Raised after the new chapter is in the save, with its index. Listeners may reload the
        /// world; by the time this fires there is nothing left to lose if they do.
        /// </summary>
        public event Action<int> Advanced;

        public ChapterProgressionService(SaveData data, ChapterService chapters, Action save)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _chapters = chapters ?? throw new ArgumentNullException(nameof(chapters));
            _save = save;
        }

        /// <summary>The chapter being played: the furthest one the save says is owned.</summary>
        public int Current => _chapters.Current;

        /// <summary>The save-key prefix the island's progression is filed under right now.</summary>
        public string CurrentNamespace => Chapters.Namespace(Current);

        /// <summary>True when there is no chapter after this one.</summary>
        public bool IsFinalChapter => Current >= Chapters.Count - 1;

        /// <summary>
        /// Whether the player may move on: this chapter is finished, and there is another.
        ///
        /// Gated on FINISHED, not on collected. Holding the advance until every beat had been claimed
        /// would let a player who ignored the rewards sit on a completed chapter with no way forward,
        /// and the sweep in <see cref="TryAdvance"/> means they lose nothing by not having tapped.
        /// </summary>
        public bool CanAdvance => !IsFinalChapter && _chapters.Complete(Current);

        /// <summary>
        /// Collects anything still owed on this chapter, opens the next one, and writes the save.
        /// Returns false — changing nothing — when the chapter is not finished or there is no next.
        ///
        /// IDEMPOTENT BY CONSTRUCTION. The moment the next namespace is in the list, Current is that
        /// chapter, and a chapter that has just started is not complete, so a second call refuses. A
        /// double tap cannot skip a chapter or pay a beat twice.
        ///
        /// THE SAVE IS WRITTEN HERE rather than left to the next autosave, for the reason
        /// <c>WorldIslands.Travel</c> writes one: this hands the player a reward and moves the island
        /// they are standing on, and an app killed in between must not take either back.
        /// </summary>
        public bool TryAdvance()
        {
            if (!CanAdvance) return false;

            int from = Current;
            int to = from + 1;

            _chapters.ClaimChapter(from);

            string opened = Chapters.Namespace(to);
            if (_data.unlockedIslands == null)
                _data.unlockedIslands = new System.Collections.Generic.List<string>();
            if (!_data.unlockedIslands.Contains(opened)) _data.unlockedIslands.Add(opened);

            _save?.Invoke();

            Advanced?.Invoke(to);
            return true;
        }
    }
}
