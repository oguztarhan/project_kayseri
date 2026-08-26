using System;

namespace Game.Core
{
    /// <summary>
    /// Wear and repair as pure maths: what an absence costs a station, what putting it right costs
    /// the player, and how long the crew takes over it.
    ///
    /// Kept here rather than in the service for the same reason <see cref="OfflineEarnings"/> is here.
    /// Three separate places quote these numbers — the repair sheet prices a repair, the world map
    /// badges a neglected island, and the service actually applies the decay — and a mechanic that
    /// takes money away from a returning player is the last one that can afford two copies of its own
    /// arithmetic. It is also the only way to test the pacing without leaving a phone alone for
    /// three days.
    ///
    /// WEAR IS AN ABSENCE, NOT A CLOCK. Decay is measured per gap between sessions and the first
    /// <see cref="Tuning.GraceHours"/> of each gap are free, so a player who opens the game daily
    /// never sees a speck of dirt, nothing rots while they are stood there watching it, and there is
    /// no meter ticking down in the corner to feel bad about. What the mechanic asks is that you come
    /// back — which is the only thing it should be asking.
    /// </summary>
    public static class Maintenance
    {
        /// <summary>One entry per <see cref="IslandEconomy"/> station, and in that order.</summary>
        public const int Stations = 8;

        /// <summary>
        /// How fast each station wears, relative to the others. The mine face and the furnace are
        /// worked hardest and go first; the haul roads pothole next; the town is last, because a
        /// grubby marketplace reads as neglect of the PLAYER rather than of the machinery.
        ///
        /// This is what stops a neglected island looking like one uniform brown tint: at any point
        /// during a long absence the districts are at visibly different states of repair, which is
        /// the difference between a place that has been left and a place that has been recoloured.
        ///
        /// Indices are <see cref="IslandEconomy"/>'s station constants — Mine, Train, Storage,
        /// OreTrucks, Smelter, CargoTrucks, Market, Power. Saved games address stations by number,
        /// so this must never be reordered.
        /// </summary>
        public static readonly float[] Wear = { 1.30f, 1.00f, 0.90f, 1.15f, 1.25f, 1.00f, 0.70f, 1.10f };

        /// <summary>Everything the designer sets, in one place. Built from <c>MaintenanceConfig</c>.</summary>
        public struct Tuning
        {
            /// <summary>Free hours at the start of every absence. Below this nothing wears at all.</summary>
            public float GraceHours;

            /// <summary>
            /// The absence, in hours, that takes an average station from perfect to the floor. Measured
            /// from the moment the player left, so the decay itself spans
            /// <c>DecayHours - GraceHours</c>.
            /// </summary>
            public float DecayHours;

            /// <summary>Worst a station can get. Absence caps out; it does not spiral.</summary>
            public float Floor;

            /// <summary>
            /// What one station's full repair costs, expressed in minutes of the island's own income.
            /// Eight stations at 1.25 is ten minutes of production to put a wholly neglected island
            /// right — the bill for a whole island is simply this summed over its stations — a real sink, and never a wall. Pricing it off measured income rather than off
            /// upgrade costs is deliberate: upgrade prices climb exponentially and income does not, so
            /// anything anchored to them turns into an unpayable bill by the third island.
            /// </summary>
            public float RepairIncomeMinutes;

            /// <summary>Crew time for a scratch and for a wreck; real damage lands between them.</summary>
            public float RepairSecondsMin, RepairSecondsMax;

            /// <summary>
            /// The thank-you for putting a whole island right, and how long it runs. The same numbers
            /// framed the other way round: an idle player will tend an empire that pays them for it and
            /// resent one that fines them for sleeping.
            /// </summary>
            public float BonusMultiplier, BonusMinutes;

            /// <summary>The shipping values. See the fields for what each one buys.</summary>
            public static Tuning Default => new Tuning
            {
                GraceHours = 8f,
                DecayHours = 72f,
                Floor = 0.55f,
                RepairIncomeMinutes = 1.25f,
                RepairSecondsMin = 20f,
                RepairSecondsMax = 180f,
                BonusMultiplier = 1.10f,
                BonusMinutes = 10f,
            };
        }

        /// <summary>A fresh island: eight stations, all perfect.</summary>
        public static float[] NewConditions()
        {
            var c = new float[Stations];
            for (int s = 0; s < Stations; s++) c[s] = 1f;
            return c;
        }

        /// <summary>
        /// Seconds of an absence that actually bite, after the grace window. Rollback-safe, so a
        /// device clock wound backwards wears nothing rather than repairing everything.
        /// </summary>
        public static long BitingSeconds(long elapsedSeconds, Tuning t)
        {
            long grace = (long)(t.GraceHours * 3600f);
            long biting = elapsedSeconds - grace;
            return biting > 0L ? biting : 0L;
        }

        /// <summary>
        /// How much of an absence a maintenance shield paid for.
        ///
        /// A shield is bought in the store and banked as a wall-clock deadline, so it keeps running
        /// while the game is shut — which is the only window it exists to cover. The seconds it
        /// covers are removed from the gap before any of it is charged, so a shield that outlasts
        /// the absence leaves nothing to wear and one that expires halfway through charges the
        /// remainder as if the player had only just left.
        ///
        /// The cover is measured from the START of the window rather than from wherever the shield
        /// was actually bought. A shield can only ever be bought with the game open, and an open
        /// game re-evaluates every minute — so the two differ by at most that minute, against a
        /// grace window of hours. Taking the whole front of the window errs toward charging LESS
        /// wear, which is the right direction for anything the player has paid for.
        /// </summary>
        public static long ShieldedSeconds(long windowStart, long windowEnd, long shieldEndUnix)
        {
            if (shieldEndUnix <= windowStart) return 0L;
            long until = shieldEndUnix < windowEnd ? shieldEndUnix : windowEnd;
            long covered = until - windowStart;
            return covered > 0L ? covered : 0L;
        }

        /// <summary>
        /// What one station comes back as after an absence.
        ///
        /// Subtractive rather than absolute, so two long weekends without a repair in between stack
        /// toward the floor instead of the second one merely restating the first.
        /// </summary>
        public static float Decay(float condition, long elapsedSeconds, float wear, Tuning t)
        {
            long biting = BitingSeconds(elapsedSeconds, t);
            if (biting <= 0L || wear <= 0f) return Clamp(condition, t);

            float span = (t.DecayHours - t.GraceHours) * 3600f;
            if (span <= 0f) return t.Floor;

            float lost = (biting / span) * (1f - t.Floor) * wear;
            return Clamp(condition - lost, t);
        }

        /// <summary>
        /// How badly one station is worn, 0 (perfect) to 1 (at the floor). This is the number the
        /// repair price, the crew's working time and the grime tier are all read off, so they cannot
        /// drift apart as the floor is tuned.
        /// </summary>
        public static float Damage(float condition, Tuning t)
        {
            float range = 1f - t.Floor;
            if (range <= 0f) return condition < 1f ? 1f : 0f;
            float d = (1f - condition) / range;
            return d < 0f ? 0f : (d > 1f ? 1f : d);
        }

        /// <summary>
        /// What an island as a whole is running at.
        ///
        /// The WORST station, not the average. The chain is serial — ore that never leaves the mine
        /// cannot be smelted — so a single seized station already throttles everything downstream, and
        /// an average would quietly promise that neglecting one building is a fraction as bad as it is.
        /// </summary>
        public static float IslandCondition(float[] conditions)
        {
            if (conditions == null || conditions.Length == 0) return 1f;
            float worst = conditions[0];
            for (int s = 1; s < conditions.Length; s++)
                if (conditions[s] < worst) worst = conditions[s];
            return worst;
        }

        /// <summary>
        /// The bill for one station, in cash.
        ///
        /// An island that earns nothing repairs for nothing, on purpose. A player who has just landed
        /// on a fresh island has no income to price a repair against and nothing to lose by neglecting
        /// it, so charging them would be a wall in front of the one part of the game they cannot yet
        /// afford to get past.
        /// </summary>
        public static double RepairCost(float condition, double islandRatePerMinute, Tuning t)
        {
            if (islandRatePerMinute <= 0d) return 0d;
            float damage = Damage(condition, t);
            if (damage <= 0f) return 0d;
            return islandRatePerMinute * t.RepairIncomeMinutes * damage;
        }

        /// <summary>
        /// How long the crew is on site. Scaled by damage so a quick tidy is a quick tidy: a fixed
        /// timer would make the trivial repairs the annoying ones.
        /// </summary>
        public static float RepairSeconds(float condition, Tuning t)
        {
            float damage = Damage(condition, t);
            if (damage <= 0f) return 0f;
            return t.RepairSecondsMin + (t.RepairSecondsMax - t.RepairSecondsMin) * damage;
        }

        private static float Clamp(float condition, Tuning t)
        {
            if (condition > 1f) return 1f;
            return condition < t.Floor ? t.Floor : condition;
        }
    }
}
