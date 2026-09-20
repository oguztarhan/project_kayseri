namespace Game.Core
{
    /// <summary>
    /// The numbered business-stage catalogue.
    ///
    /// A chapter has one arrival beat (Landfall) followed by four business beats, so this catalogue
    /// deliberately exposes four player-facing stages per chapter: 1-1 through 8-4. It owns the
    /// stable stage coordinate and ID only; Chapters continues to own observed objectives, rewards,
    /// claims and the chapter-transition transaction.
    /// </summary>
    public static class Stages
    {
        /// <summary>Business stages in every chapter. Landfall is the arrival reward, not a stage.</summary>
        public const int PerChapter = Chapters.BeatCount - 1;

        /// <summary>All authored chapter-stage coordinates: eight chapters by four stages.</summary>
        public const int Count = Chapters.Count * PerChapter;

        /// <summary>Whether a zero-based chapter and one-based stage form one of the 32 coordinates.</summary>
        public static bool IsValid(int chapter, int stage)
            => chapter >= 0 && chapter < Chapters.Count && stage >= 1 && stage <= PerChapter;

        /// <summary>
        /// The beat whose completion advances this stage. Stage numbers intentionally equal beats 1..4:
        /// changing that mapping would silently give a published stage a different objective.
        /// </summary>
        public static int CompletionBeat(int stage) => stage >= 1 && stage <= PerChapter ? stage : -1;

        /// <summary>
        /// Stable, save-safe content ID. This is never the player-facing label; chapter namespaces
        /// remain fixed even though all chapters use the same physical island.
        /// </summary>
        public static string Id(int chapter, int stage)
        {
            if (!IsValid(chapter, stage)) return string.Empty;
            return Chapters.Namespace(chapter) + ".stage." + stage;
        }

        /// <summary>Player-facing coordinate such as 1-1 or 8-4.</summary>
        public static string Label(int chapter, int stage)
        {
            if (!IsValid(chapter, stage)) return string.Empty;
            return (chapter + 1) + "-" + stage;
        }

        /// <summary>Resolves an authored stable ID back to its chapter-stage coordinate.</summary>
        public static bool TryCoordinate(string id, out int chapter, out int stage)
        {
            chapter = -1;
            stage = -1;
            if (string.IsNullOrEmpty(id)) return false;

            for (int c = 0; c < Chapters.Count; c++)
                for (int s = 1; s <= PerChapter; s++)
                    if (id == Id(c, s))
                    {
                        chapter = c;
                        stage = s;
                        return true;
                    }
            return false;
        }
    }
}
