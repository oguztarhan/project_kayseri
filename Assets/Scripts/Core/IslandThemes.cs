namespace Game.Core
{
    /// <summary>
    /// Which visual theme a chapter wears, as pure maths.
    ///
    /// WHY IT EXISTS. The island never changes — one map, eight chapters, each one starting its
    /// progression over on a harder curve (see <see cref="Chapters"/>). What tells the player they
    /// have moved on is the LOOK of the place: its materials, and later its textures. The rule for
    /// picking one is small enough to live here, away from any asset, so it can be tested without a
    /// scene, a renderer or a single material.
    ///
    /// A THEME OPENS AT A CHAPTER AND HOLDS UNTIL THE NEXT ONE OPENS. That one rule covers both
    /// shapes that have been asked for: eight themes opening at 0..7 is one theme per chapter, and
    /// three themes opening at 0, 2 and 5 groups the chapters 1-2, 3-5, 6-8. Which of those the
    /// game ships is authoring, not code.
    ///
    /// The table arrives as a plain int[] rather than as the theme assets themselves because
    /// Game.Core references nothing — the same reason <see cref="Chapters.Tuning"/> is a struct here
    /// and a ScriptableObject in Game.Data.
    /// </summary>
    public static class IslandThemes
    {
        /// <summary>No theme covers this chapter: the island draws the materials it was authored with.</summary>
        public const int None = -1;

        /// <summary>
        /// The theme covering <paramref name="chapter"/>, as an index into <paramref name="fromChapters"/>,
        /// or <see cref="None"/>.
        ///
        /// Entries are NOT required to be in order, and an entry below zero is skipped rather than
        /// treated as chapter 0 — that is how an empty slot in a half-authored set arrives, and a set
        /// with a hole in it should lose one theme rather than silently hand that hole chapter 1's.
        /// Two entries opening at the same chapter is an authoring mistake with no right answer, so
        /// the later one wins and the earlier is unreachable.
        /// </summary>
        public static int IndexFor(int chapter, int[] fromChapters)
        {
            if (chapter < 0 || fromChapters == null) return None;

            int best = None;
            int bestFrom = None;
            for (int i = 0; i < fromChapters.Length; i++)
            {
                int from = fromChapters[i];
                if (from < 0 || from > chapter || from < bestFrom) continue;
                best = i;
                bestFrom = from;
            }
            return best;
        }
    }
}
