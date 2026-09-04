namespace Game.Core
{
    /// <summary>
    /// The chapter spine as pure maths: which island is which chapter, what each chapter asks of the
    /// player, and how far along one is.
    ///
    /// WHY IT EXISTS. The island ladder is eight purchases with nothing between them. A player who
    /// buys copper is told nothing about what copper is for, gets no landmark on the way through it,
    /// and finds out it is finished only by running out of things to buy. The ladder is the pacing;
    /// it was never the story. This is the story — one chapter per island, five beats inside each,
    /// every beat a thing the player was going to do anyway, now named and paid for.
    ///
    /// THREE DELIBERATE CHOICES:
    ///
    /// A completed chapter now gates the next island, but it does so only with state the core loop
    /// already produces: station levels, expansions and a staffed yard. It never asks for a won
    /// voyage resource or an event currency, so a player cannot be stalled behind an optional system.
    /// Cash remains the second half of the purchase gate.
    ///
    /// Every beat is COUNT-based, and the thresholds are the same on all eight islands. This is
    /// <see cref="Goals"/>'s reasoning applied one layer up: cash and bars inflate 3.2x per ore tier,
    /// so a threshold in money is a wall on coal and a formality on diamond, and one that scaled with
    /// the player would be a second economy to balance. Levels bought and buildings raised mean the
    /// same thing on every island forever.
    ///
    /// Beats are OBSERVED, never reported. Everything below is readable from the save on its own —
    /// levels bought, buildings unlocked, hires made — so no other system has to call into the
    /// chapter service, and none of them has to know it exists. <see cref="Goals"/> needed one
    /// Record() call in six files; this needed none.
    /// </summary>
    public static class Chapters
    {
        /// <summary>One chapter per island, in the ladder's own order.</summary>
        public const int Count = 8;

        /// <summary>
        /// The island each chapter belongs to, mirroring <c>Game.Gameplay.WorldIslands.DefaultLadder()</c>.
        ///
        /// Authored here rather than read from the ladder because Game.Systems cannot see
        /// Game.Gameplay — the same reason <see cref="Foremen.Count"/> restates the length of
        /// <see cref="IslandEconomy.Stations"/>. ChaptersTests pins the order against this comment;
        /// if the ladder is ever re-cut, that test is what fails first.
        /// </summary>
        public static readonly string[] Islands =
        { "coal", "copper", "iron", "silver", "gold", "ruby", "emerald", "diamond" };

        // Beat indices. Saves address these by number, so they must never be reordered. New beats are
        // APPENDED — the save arrays are padded on load (ChapterService.Normalise), so growing this
        // list costs nothing, while inserting into it would silently re-label everything after it.
        public const int Landfall = 0, FirstSmoke = 1, TheWorks = 2, TheYard = 3, FullSteam = 4;
        public const int BeatCount = 5;

        /// <summary>
        /// One island's state, as far as a chapter cares. Filled from the save by the service; kept a
        /// plain struct so every rule below is testable without a SaveData, a wallet or a scene.
        /// </summary>
        public struct Progress
        {
            /// <summary>The player owns this island.</summary>
            public bool Owned;

            /// <summary>Axis levels bought here, summed across all eight stations.</summary>
            public int AxisLevels;

            /// <summary>Ghost buildings raised here, of the ten.</summary>
            public int Unlocks;

            /// <summary>This island's yard runs itself — every job hired and levelled out.</summary>
            public bool YardStaffed;
        }

        /// <summary>
        /// Everything a designer can move. Mirrors the shape of <see cref="Foremen.Tuning"/> and
        /// <see cref="Voyages.Tuning"/>: the numbers here are defaults, and ChapterConfig is how they
        /// get changed without a rebuild.
        /// </summary>
        public struct Tuning
        {
            /// <summary>Axis levels for FIRST SMOKE, and again for FULL STEAM.</summary>
            public int FirstSmokeLevels, FullSteamLevels;

            /// <summary>Ghost buildings for THE WORKS, and again for FULL STEAM.</summary>
            public int WorksUnlocks, FullSteamUnlocks;

            /// <summary>A beat's gem payout is Base + Step x chapter, so later chapters pay more.</summary>
            public long GemsBase, GemsStep;

            /// <summary>Foreman cards, the same shape. Beat 0 pays none — see <see cref="BeatCards"/>.</summary>
            public int CardsBase, CardsStep;

            public static Tuning Default => new Tuning
            {
                // FIRST SMOKE wants to land in the first few minutes on an island: ten levels is
                // roughly the point the chain is visibly moving rather than one truck at a time.
                FirstSmokeLevels = 10,

                // THE WORKS is the second mine, the second smelter and the trade post — the three
                // cheapest of the ten, and the ones that change what the island looks like.
                WorksUnlocks = 3,

                // FULL STEAM is the capstone: an island with 200 levels and eight of the ten
                // buildings up is one the player has finished with, not one they are passing through.
                FullSteamLevels = 200,
                FullSteamUnlocks = 8,

                // Gems, not cash. Cash has one faucet (MarketService) and a chapter reward paid in it
                // would be a second one — Docs/VOYAGES.md R1. Gems and cards feed the roster, which is
                // the long tail prestige was retired in favour of.
                GemsBase = 40, GemsStep = 15,
                CardsBase = 1, CardsStep = 1,
            };
        }

        // ------------------------------------------------------------------ rules
        /// <summary>
        /// Whether a beat has been earned. The whole rule set, in one switch — an unknown beat index
        /// reads as unearned rather than throwing, because a save from a later build can carry beats
        /// this one has never heard of.
        /// </summary>
        public static bool Satisfied(int beat, in Progress p, in Tuning t)
        {
            switch (beat)
            {
                case Landfall:   return p.Owned;
                case FirstSmoke: return p.Owned && p.AxisLevels >= t.FirstSmokeLevels;
                case TheWorks:   return p.Owned && p.Unlocks >= t.WorksUnlocks;
                case TheYard:    return p.Owned && p.YardStaffed;
                case FullSteam:  return p.Owned && p.AxisLevels >= t.FullSteamLevels
                                                && p.Unlocks    >= t.FullSteamUnlocks;
                default:         return false;
            }
        }

        /// <summary>
        /// How far along a beat is, 0..1, for the bar under it. FULL STEAM asks two things at once and
        /// reports the WORSE of them: a bar that fills on levels while the buildings are still missing
        /// would sit at 100% and refuse to pay, which is the one thing a progress bar must never do.
        /// </summary>
        public static float BeatProgress(int beat, in Progress p, in Tuning t)
        {
            if (!p.Owned) return 0f;
            switch (beat)
            {
                case Landfall:   return 1f;
                case FirstSmoke: return Goals.Progress(p.AxisLevels, t.FirstSmokeLevels);
                case TheWorks:   return Goals.Progress(p.Unlocks, t.WorksUnlocks);
                case TheYard:    return p.YardStaffed ? 1f : 0f;
                case FullSteam:
                {
                    float lv = Goals.Progress(p.AxisLevels, t.FullSteamLevels);
                    float un = Goals.Progress(p.Unlocks, t.FullSteamUnlocks);
                    return lv < un ? lv : un;
                }
                default: return 0f;
            }
        }

        /// <summary>
        /// The beat the player is working on: the LOWEST unsatisfied one, or -1 when the chapter is
        /// finished. Lowest rather than the first one after the last satisfied beat: THE YARD can be
        /// earned before THE WORKS by a player who staffed the yard early, and an objective banner
        /// that skipped past THE WORKS because a later beat happened to land would name a target the
        /// player has already met and never come back to the one they have not.
        /// </summary>
        public static int NextBeat(in Progress p, in Tuning t)
        {
            for (int b = 0; b < BeatCount; b++) if (!Satisfied(b, p, t)) return b;
            return -1;
        }

        /// <summary>
        /// The two numbers under a progress bar - what the player has, and what the beat wants.
        /// <see cref="BeatProgress"/> answers the same question as a fraction; this answers it in the
        /// units the beat is actually counted in, because "18/25 levels" tells a player what to do
        /// next and "72%" does not.
        ///
        /// FULL STEAM asks two things at once and reports whichever half is FURTHER BEHIND, matching
        /// <see cref="BeatProgress"/> exactly. Reporting levels while the buildings were missing is
        /// how a bar ends up sitting at 100% refusing to pay.
        /// </summary>
        public static void BeatCounts(int beat, in Progress p, in Tuning t, out int have, out int need)
        {
            switch (beat)
            {
                case Landfall:   have = p.Owned ? 1 : 0; need = 1; return;
                case FirstSmoke: have = p.AxisLevels;    need = t.FirstSmokeLevels; return;
                case TheWorks:   have = p.Unlocks;       need = t.WorksUnlocks; return;
                case TheYard:    have = p.YardStaffed ? 1 : 0; need = 1; return;
                case FullSteam:
                {
                    // Fractions, not raw counts: 8 of 8 buildings is further along than 150 of 200
                    // levels even though 150 is the bigger number.
                    float lv = Goals.Progress(p.AxisLevels, t.FullSteamLevels);
                    float un = Goals.Progress(p.Unlocks, t.FullSteamUnlocks);
                    if (lv <= un) { have = p.AxisLevels; need = t.FullSteamLevels; }
                    else          { have = p.Unlocks;    need = t.FullSteamUnlocks; }
                    return;
                }
                default: have = 0; need = 0; return;
            }
        }

        /// <summary>How many of a chapter's beats have been earned.</summary>
        public static int BeatsSatisfied(in Progress p, in Tuning t)
        {
            int n = 0;
            for (int b = 0; b < BeatCount; b++) if (Satisfied(b, p, t)) n++;
            return n;
        }

        /// <summary>A chapter is done when every beat in it is.</summary>
        public static bool Complete(in Progress p, in Tuning t)
            => BeatsSatisfied(p, t) >= BeatCount;

        // ---------------------------------------------------------------- rewards
        /// <summary>
        /// What one beat pays in gems. Later chapters pay more because they are reached later, not
        /// because they are harder — the thresholds are identical on all eight islands by design, so
        /// the chapter index is the only thing left to scale by.
        /// </summary>
        public static long BeatGems(int chapter, int beat, in Tuning t)
        {
            if (chapter < 0 || chapter >= Count || beat < 0 || beat >= BeatCount) return 0L;
            long gems = t.GemsBase + t.GemsStep * chapter;

            // FULL STEAM closes the chapter, so it is worth the rest of it put together. Without this
            // the last beat — much the longest of the five — pays the same as landing on the beach.
            if (beat == FullSteam) gems *= 3L;
            return gems < 0L ? 0L : gems;
        }

        /// <summary>
        /// Foreman cards for a beat. LANDFALL pays none: it fires the moment an island is bought, and
        /// a card handed over for a purchase the player has already been charged for reads as change,
        /// not as a reward.
        /// </summary>
        public static int BeatCards(int chapter, int beat, in Tuning t)
        {
            if (chapter < 0 || chapter >= Count || beat < 0 || beat >= BeatCount) return 0;
            if (beat == Landfall) return 0;

            int cards = t.CardsBase + t.CardsStep * (chapter / 2);
            if (beat == FullSteam) cards *= 2;
            return cards < 0 ? 0 : cards;
        }

        // ------------------------------------------------------------------ names
        /// <summary>
        /// The island key a chapter belongs to, or "" for an index off the end. Callers localise it
        /// through <c>Loc.Id("ada", key)</c>; the raw key is also the save key, so it stays English
        /// here for the same reason station names do.
        /// </summary>
        public static string Island(int chapter)
            => chapter >= 0 && chapter < Islands.Length ? Islands[chapter] : string.Empty;

        /// <summary>Which chapter an island key belongs to, or -1 for a key off the ladder.</summary>
        public static int Of(string islandKey)
        {
            if (string.IsNullOrEmpty(islandKey)) return -1;
            for (int i = 0; i < Islands.Length; i++)
                if (Islands[i] == islandKey) return i;
            return -1;
        }
    }
}
