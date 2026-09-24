using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// What the player has been taught, as pure bookkeeping over the two save fields the tutorial has
    /// always had: <c>tutorialStep</c> (0 = the basics are owed, 100 = they are done) and
    /// <c>tutorialTipsSeen</c> (one id per lesson or intro already given). No new save field, so no
    /// migration, and every old save already means something here.
    ///
    /// THE BASICS ARE A LIST, NOT A COUNTER. The first session teaches the mining shop's whole loop —
    /// a bench crafts, the carrier stocks the shelf, a customer buys, the cash buys an upgrade — and
    /// several of those lessons wait on the game itself: the first sale, enough money for the first
    /// upgrade. A player can close the app in the middle of one, so each lesson is written down when
    /// it finishes and the next launch resumes at the first one missing. The step flips to 100 only
    /// when the last one is in, which keeps the two screens that already gate on "step &lt; 100" (the
    /// offer popup, the sea's hints) working unchanged.
    ///
    /// A SAVE AT 100 WITH NONE OF THESE IDS is a player who finished the previous tutorial. They are
    /// done with the basics; nothing here would put them through the shop loop a second time.
    ///
    /// INTRODUCTIONS are the one-shot cards for everything that comes later — the sea, captains, the
    /// workshop. They wait for the basics, they wait until the feature matters, and a player whose
    /// save shows they have already used the feature is marked as introduced without being shown
    /// anything. The ids are whatever each screen already uses ("feature.crafting", "sea.boss"), so
    /// a card a player saw under the old tutorial is still counted as seen.
    ///
    /// The list is mutated in place — it is the save's own list — and every mutating call says
    /// whether it changed anything, so the caller writes the save only when there is something to
    /// write and copies <see cref="Step"/> back into it.
    /// </summary>
    public sealed class TutorialProgress
    {
        public const int StepFresh = 0;
        public const int StepDone = 100;

        /// <summary>Recorded beside the lessons when the player skips the basics, so the two are distinguishable.</summary>
        public const string SkippedMarker = "ftue.core.skipped";

        /// <summary>What finishes a lesson. <see cref="Goal.Tap"/> is the only one the player's DEVAM decides.</summary>
        public enum Goal
        {
            Tap,
            Produce,    // a bench finished an item
            Carry,      // the carrier put an item on the shelf
            Sell,       // a customer bought one
            Afford,     // the wallet covers the first bench's speed upgrade
            OpenBench,  // the bench's upgrade panel is open
            BuySpeed    // the first bench's speed level went up
        }

        public readonly struct Lesson
        {
            public readonly string Id;
            public readonly Goal Goal;

            public Lesson(string id, Goal goal)
            {
                Id = id;
                Goal = goal;
            }
        }

        /// <summary>
        /// The shop, read at one moment. Counters are cumulative so a lesson can compare against the
        /// moment it started: a bench that had already made forty pickaxes before the card appeared
        /// has not "just made one" for this card's purposes.
        /// </summary>
        public struct Facts
        {
            public long Produced;
            /// <summary>Items that ever reached the shelf: on it now plus sold.</summary>
            public long Carried;
            public long Sold;
            public bool CanAffordSpeed;
            public bool BenchPanelOpen;
            /// <summary>The first bench's speed level; 1 until the first upgrade.</summary>
            public int SpeedLevel;
        }

        public enum Intro
        {
            /// <summary>Not now — the basics are still owed, or the feature does not matter yet.</summary>
            Wait,
            Show,
            /// <summary>Given before, or skipped because the player already knew it. Never again.</summary>
            Done
        }

        /// <summary>The first session, in the order it is taught.</summary>
        public static readonly Lesson[] CoreLessons =
        {
            new Lesson("ftue.welcome",     Goal.Tap),
            new Lesson("ftue.craft",       Goal.Produce),
            new Lesson("ftue.carry",       Goal.Carry),
            new Lesson("ftue.sell",        Goal.Sell),
            new Lesson("ftue.save_up",     Goal.Afford),
            new Lesson("ftue.open_bench",  Goal.OpenBench),
            new Lesson("ftue.buy_speed",   Goal.BuySpeed),
            new Lesson("ftue.speed_value", Goal.Tap),
            new Lesson("ftue.loop",        Goal.Tap),
        };

        /// <summary>Early progression: each taught once, the first time it matters, never blocking.</summary>
        public const string SecondBenchLesson = "progress.second_bench";
        public const string GoalsLesson = "progress.goals";
        public static readonly string[] ProgressLessons = { SecondBenchLesson, GoalsLesson };

        /// <summary>
        /// The sea, in the order a player meets it: the sail button on the island, then the fight,
        /// the first win's spoils and the first boss out at sea. The last three keep the ids the old
        /// sea hints wrote, so a player who saw those is not taught again.
        /// </summary>
        public const string SailLesson = "sea.sail";
        public const string SeaFightLesson = "sea.combat";
        public const string SeaRewardLesson = "sea.reward";
        public const string SeaBossLesson = "sea.boss";
        public static readonly string[] SeaLessons = { SailLesson, SeaFightLesson, SeaRewardLesson, SeaBossLesson };

        /// <summary>
        /// The screens that introduce themselves the first time they open once usable. Recorded as
        /// "feature." + id; the card's text is "egitim.ipucu_" + id + "_b" / "_m".
        /// </summary>
        public static readonly string[] FeatureIntros =
            { "stage", "crafting", "captain", "pets", "collection", "events", "league", "gear" };

        /// <summary>
        /// The text-table key for a lesson's title or body: "ftue.save_up" → "egitim.ftue_save_up_b".
        /// One rule, so the table test can check every lesson without a second list to keep in step.
        /// </summary>
        public static string TextKey(string lessonId, bool title)
            => "egitim." + lessonId.Replace('.', '_') + (title ? "_b" : "_m");

        private readonly List<string> _seen;

        public TutorialProgress(List<string> seen, int step)
        {
            _seen = seen ?? throw new ArgumentNullException(nameof(seen));
            Step = step >= StepDone ? StepDone : StepFresh;
        }

        /// <summary>The value the save's <c>tutorialStep</c> should hold.</summary>
        public int Step { get; private set; }

        public bool CoreDone => Step >= StepDone;

        public bool Has(string id) => !string.IsNullOrEmpty(id) && _seen.Contains(id);

        /// <summary>Index into <see cref="CoreLessons"/> of the first lesson still owed, or -1 when the basics are done.</summary>
        public int NextCoreIndex
        {
            get
            {
                if (CoreDone) return -1;
                for (int i = 0; i < CoreLessons.Length; i++)
                    if (!_seen.Contains(CoreLessons[i].Id)) return i;
                return -1;
            }
        }

        /// <summary>
        /// Writes a lesson or introduction down. When it completes the last missing core lesson, the
        /// step moves to done as well. False when there was nothing new to write.
        /// </summary>
        public bool Complete(string id)
        {
            if (string.IsNullOrEmpty(id) || _seen.Contains(id)) return false;
            _seen.Add(id);
            if (!CoreDone && AllCoreSeen()) Step = StepDone;
            return true;
        }

        /// <summary>
        /// The basics are over without being finished: the player skipped them, or the save shows they
        /// already run the shop. Every core lesson is marked so a replay is the only way back in.
        /// </summary>
        public bool FinishCore(bool skipped)
        {
            bool changed = !CoreDone;
            Step = StepDone;
            for (int i = 0; i < CoreLessons.Length; i++)
                if (!_seen.Contains(CoreLessons[i].Id)) { _seen.Add(CoreLessons[i].Id); changed = true; }
            if (skipped && !_seen.Contains(SkippedMarker)) { _seen.Add(SkippedMarker); changed = true; }
            return changed;
        }

        /// <summary>The settings screen's replay: the basics only. Introductions already given stay given.</summary>
        public void ResetCore()
        {
            for (int i = 0; i < CoreLessons.Length; i++) _seen.Remove(CoreLessons[i].Id);
            _seen.Remove(SkippedMarker);
            Step = StepFresh;
        }

        /// <summary>
        /// Whether an introduction should appear now. <paramref name="relevant"/>: the feature matters
        /// to this player today. <paramref name="alreadyUsed"/>: the save shows they have used it, in
        /// which case the card is recorded as given — and this call changes the list.
        /// </summary>
        public Intro DecideIntro(string id, bool relevant, bool alreadyUsed)
        {
            if (string.IsNullOrEmpty(id) || _seen.Contains(id)) return Intro.Done;
            if (alreadyUsed)
            {
                _seen.Add(id);
                return Intro.Done;
            }
            if (!CoreDone || !relevant) return Intro.Wait;
            return Intro.Show;
        }

        /// <summary>
        /// Whether a lesson's goal has been reached since it began. <see cref="Goal.Tap"/> is never met
        /// here; the player's DEVAM ends it.
        /// </summary>
        public static bool GoalMet(Goal goal, Facts now, Facts atStart)
        {
            switch (goal)
            {
                case Goal.Produce:   return now.Produced > atStart.Produced;
                case Goal.Carry:     return now.Carried > atStart.Carried;
                case Goal.Sell:      return now.Sold > atStart.Sold;
                case Goal.Afford:    return now.CanAffordSpeed || now.SpeedLevel > 1;
                case Goal.OpenBench: return now.BenchPanelOpen || now.SpeedLevel > 1;
                case Goal.BuySpeed:  return now.SpeedLevel > atStart.SpeedLevel;
                default:             return false;
            }
        }

        /// <summary>
        /// A lesson that asks the player to do something they have already done on their own is
        /// passed over rather than shown. Only the actions count: the loop lessons explain what the
        /// island is doing, and that is worth seeing however far it has already run.
        /// </summary>
        public static bool AlreadyDone(Goal goal, Facts now)
        {
            switch (goal)
            {
                case Goal.Afford:    return now.CanAffordSpeed || now.SpeedLevel > 1;
                case Goal.OpenBench: return now.SpeedLevel > 1;
                case Goal.BuySpeed:  return now.SpeedLevel > 1;
                default:             return false;
            }
        }

        private bool AllCoreSeen()
        {
            for (int i = 0; i < CoreLessons.Length; i++)
                if (!_seen.Contains(CoreLessons[i].Id)) return false;
            return true;
        }
    }
}
