using System;

namespace Game.Core
{
    /// <summary>
    /// Voyages as pure maths: how long a hold takes to fill, how long a ship is away, and what it
    /// brings home. The fourth of these files, after <see cref="IslandEconomy"/>,
    /// <see cref="MarketFlow"/> and <see cref="Foremen"/>, and it exists for the same reason as all
    /// three — a number the player is asked to invest in should be readable in one testable place
    /// rather than discovered by playing.
    ///
    /// WHY IT EXISTS. Every bar this game produced had exactly one destination: the counter, where it
    /// became cash, which bought more production. There was no point anywhere in the chain where the
    /// player decided what a bar was FOR. A voyage is the second destination — you send bars to sea
    /// and they come back as foreman cards, which is the one currency the roster is built on and the
    /// one thing nothing in the game was producing at any rate worth waiting for.
    ///
    /// THE FOUR RULES, all load-bearing (Docs/VOYAGES.md §3):
    ///
    ///   1. A voyage NEVER pays cash. Cash has one faucet, MarketService, and two faucets competing
    ///      for the same reward would make whichever paid less pointless.
    ///   2. A voyage NEVER pays a rate. Every island's income is clamped by incomeCapPerMin, so a
    ///      reward expressed as throughput is swallowed whole for exactly the player who has been
    ///      playing long enough to own one. Cards are not a rate.
    ///   3. Nothing expires. A returned voyage waits at the dock forever. Same rule ContractService
    ///      states: an idle game must never punish a player for looking away.
    ///   4. Costs are FRACTIONS OF DELIVERY, never absolute bars. The ore ladder multiplies output by
    ///      3.2 per tier, so an absolute bar count is correct on coal and meaningless by diamond.
    ///      This is <see cref="MarketFlow"/>'s convention and the reason is unchanged.
    ///
    /// HOW A VOYAGE IS PAID FOR. Not with a lump sum: <see cref="MarketFlow.MaxDepositSlots"/> caps a
    /// yard's pads at a few minutes of delivery, so there is never a pile big enough to buy one with.
    /// Instead the ship has a hold and the hold fills over time, the way the pads themselves do. The
    /// felt cost is "for the next N minutes a share of this island's output goes to sea instead of the
    /// till" — a decision the player makes against a number already on the HUD.
    /// </summary>
    public static class Voyages
    {
        /// <summary>How many route tiers exist. Saves address tiers by index, so this must never shrink.</summary>
        public const int TierCount = 4;

        /// <summary>Ship upgrade tracks. Saves address these by index — never reorder.</summary>
        public const int Hold = 0, Speed = 1, Crew = 2, Berths = 3;
        public const int ShipTrackCount = 4;

        /// <summary>The most berths a fleet can ever have. Berths 3 and 4 are the gem sink (V3).</summary>
        public const int MaxBerths = 4;

        /// <summary>Ceiling on the Hold, Speed and Crew tracks.</summary>
        public const int MaxShipLevel = 8;

        // ------------------------------------------------------------------ tuning
        /// <summary>
        /// Everything a designer can move. Mirrored by <c>Data/VoyageConfig</c> so the maths stays here
        /// and the balance stays in the Inspector — the same split <see cref="Foremen.Tuning"/> uses.
        ///
        /// EVERY NUMBER HERE IS A DEFAULT in the sense GDD §14 means it. Docs/VOYAGES.md §14 records
        /// why they will not be hand-guessed into final values: this economy is queue-driven on scene
        /// geometry and has no closed form, and the last attempt to guess a curve for it came out wrong
        /// in four independent ways at once (REMAKE_PLAN §P7).
        /// </summary>
        public struct Tuning
        {
            /// <summary>Share of an island's delivery that goes into a loading hold instead of the till.</summary>
            public double DivertShare;

            /// <summary>Hold size at ship level 0, measured in minutes of the island's own delivery.</summary>
            public double HoldMinutesBase;

            /// <summary>Minutes of delivery each Hold level adds.</summary>
            public double HoldMinutesPerLevel;

            /// <summary>A tier-1 voyage's length, before the tier and ship speed multipliers.</summary>
            public double BaseVoyageMinutes;

            /// <summary>What one Speed level takes off the clock, as a fraction. 0.08 = 8% faster per level.</summary>
            public double SpeedPerLevel;

            /// <summary>What one Crew level adds to the payout, as a fraction.</summary>
            public double CrewPerLevel;

            /// <summary>Cards a full tier-1 hold is worth. Every other payout is this times the tier.</summary>
            public double CardRate;

            /// <summary>
            /// The least of a hold that may be sent to sea. A ship carrying almost nothing that still
            /// costs a full voyage's wait is a trap, so the dock refuses rather than letting the player
            /// walk into it.
            /// </summary>
            public double MinLaunchFraction;

            /// <summary>
            /// What a foreman aboard takes off the route's risk, per level. 0.02 against a level-10
            /// foreman is 20 points — enough to make the far reach comfortable, not enough to make it
            /// free. A route whose risk a foreman could erase would not be a decision any more.
            /// </summary>
            public double ForemanRiskPerLevel;

            /// <summary>
            /// The share of the payout a failed voyage still brings home. Never zero: a hold spent, a
            /// wait served and nothing at all to show is the outcome players quit over, and the point
            /// of the gamble is the size of the win, not the cruelty of the loss.
            /// </summary>
            public double FailPayout;

            /// <summary>
            /// How long a berth is out of use after a failure, as a FRACTION OF THE ROUTE'S OWN LENGTH.
            ///
            /// A flat repair window was the first draft and it scaled backwards: twenty minutes is
            /// most of a fifteen-minute coastal run and nothing at all beside a three-hour far reach,
            /// so the punishment landed hardest exactly where the gamble was smallest. Tying it to the
            /// route makes the cost proportional to what was being attempted, which is the only reading
            /// a player can predict before committing.
            /// </summary>
            public double RepairFraction;

            /// <summary>
            /// The most of an island's delivery that may be at the dock AT ONCE, across every berth
            /// that is loading. This is the guardrail on berths: without it four berths each taking
            /// <see cref="DivertShare"/> would take 1.4x of everything the island makes, the counter
            /// would sell nothing, and buying a berth would read as switching the game off.
            /// </summary>
            public double MaxDivertShare;

            /// <summary>Salvage a full tier-0 hold brings back. Every other route is this times the tier.</summary>
            public double SalvageRate;

            /// <summary>
            /// Charts a full tier-0 hold brings back — what a captain crate is bought with, and the
            /// only thing charts are ever spent on.
            ///
            /// A SECOND CLOSED LOOP, deliberately shaped like the first. Salvage comes from sailing
            /// and goes back into sailing, which is why Docs/VOYAGES.md §2 can say it "carries zero
            /// balance risk to the main economy". Charts come from sailing and go into the captain
            /// roster, which only ever affects sailing. Neither can reach the cash economy however
            /// badly it is tuned, and that is the whole reason the collection was given a currency of
            /// its own instead of being priced in gems beside the foremen.
            /// </summary>
            public double ChartRate;

            /// <summary>First level of a ship track costs this; each one after it multiplies.</summary>
            public double ShipCostBase;
            public double ShipCostGrowth;

            /// <summary>The second berth, in salvage. The third and fourth are bought with gems.</summary>
            public long BerthSalvageCost;
            public long ThirdBerthGems;
            public long FourthBerthGems;

            /// <summary>Skipping a repair outright, in gems. Never sold for a guaranteed success —
            /// see Docs/VOYAGES.md §10: that would turn the one real decision into a wallet check.</summary>
            public long RepairSkipGems;

            public static Tuning Default => new Tuning
            {
                DivertShare        = 0.35d,
                HoldMinutesBase    = 3d,
                HoldMinutesPerLevel= 0.6d,
                BaseVoyageMinutes  = 35d,
                SpeedPerLevel      = 0.04d,
                CrewPerLevel       = 0.05d,
                CardRate           = 1d,
                MinLaunchFraction  = 0.25d,
                ForemanRiskPerLevel= 0.02d,
                FailPayout         = 0.40d,
                RepairFraction     = 0.25d,
                MaxDivertShare     = 0.50d,
                SalvageRate        = 1.5d,
                ChartRate          = 4d,
                ShipCostBase       = 20d,
                ShipCostGrowth     = 1.45d,
                BerthSalvageCost   = 250L,
                ThirdBerthGems     = 1200L,
                FourthBerthGems    = 3000L,
                RepairSkipGems     = 120L,
            };
        }

        // ------------------------------------------------------------------- tiers
        /// <summary>
        /// The route table. Payout grows FASTER than duration on purpose — a tier-4 voyage is 14x as
        /// long and pays 28x — so committing to a long absence is strictly better if it lands, and the
        /// risk is what buys that back. That trade is the entire decision this feature adds, and it is
        /// the decision the reference game charges an entire auto-battler to produce.
        ///
        /// Tier 0 is risk-free and always available: a player who never engages with the gamble still
        /// has a working card faucet, just a slow one.
        /// </summary>
        /// <summary>
        /// BALANCED 2026-08-26, and not by hand. The first defaults were a guess and they were wrong by
        /// about 2.5x: a simulated player maxed the fleet AND collected the entire foreman roster in
        /// roughly 48 hours, which collapsed the long tail this whole feature exists to create. The
        /// cause was a multiplicative stack — tier payout x hold x crew — where each factor was
        /// defensible alone and the product was not.
        ///
        /// These numbers come out of a solver run against four constraints at once (Docs/VOYAGES.md
        /// §21): cards per hour must rise with every tier, a fully bought ship must beat a stock one by
        /// more than 2x, a single foreman must take four to six weeks of ordinary play to reach level
        /// 10, and the far reach must stay worth sailing. Move any one of them and the others move.
        /// </summary>
        public static readonly double[] DurationMult = { 1d, 2.5d, 6d, 14d };
        public static readonly double[] PayoutMult   = { 1d, 3d,   9d, 24d };
        public static readonly double[] RiskChance   = { 0d, 0.08d, 0.18d, 0.30d };

        /// <summary>
        /// Voyages that must have been sailed before a tier opens.
        ///
        /// Gated on VOYAGES SAILED rather than on a ship level, which is what an earlier draft said.
        /// Two reasons. The ship tracks are bought with salvage, and salvage is itself only earned by
        /// sailing — so gating routes on ship levels made the whole ladder wait on a currency the
        /// player had no way to earn yet. And it kept one upgrade doing two unrelated jobs: the Hold
        /// track should decide how much a ship carries, not which sea it is allowed on. Sailing is now
        /// what teaches the system and what opens it, which is the same thing the player was going to
        /// be doing anyway.
        /// </summary>
        public static readonly int[] TierVoyagesRequired = { 0, 3, 10, 25 };

        // -------------------------------------------------------------------- hold
        /// <summary>
        /// Bars a hold takes, given what the island actually delivers. Zero when the yard's meter has
        /// nothing to say yet — the same honest reading <see cref="MarketFlow.StockCapacity"/> gives,
        /// and the reason the dock refuses to open a voyage on a yard that has never shipped anything.
        /// </summary>
        public static double HoldSize(double deliveredPerMin, int holdLevel, Tuning t)
        {
            if (deliveredPerMin <= 0d) return 0d;
            int level = Clamp(holdLevel, 0, MaxShipLevel);
            double minutes = t.HoldMinutesBase + t.HoldMinutesPerLevel * level;
            return minutes > 0d ? deliveredPerMin * minutes : 0d;
        }

        /// <summary>
        /// The share of an island's delivery ONE loading hold gets, when <paramref name="loading"/>
        /// holds are filling off the same yard.
        ///
        /// WHAT BERTHS ACTUALLY BUY, because it is not what it looks like. The total diverted is capped
        /// at <see cref="Tuning.MaxDivertShare"/> however many berths are open, so a second berth does
        /// NOT send more ore to sea — it sends the same ore in two ships. What it buys is PIPELINING.
        /// A single berth spends almost all of its life idle: a far-reach voyage loads for nine minutes
        /// and then sails for three and a half hours, during which the berth diverts nothing at all and
        /// the dock does nothing. A second berth loads through that gap. On the long routes that is
        /// close to a doubling, and on the coastal run it is almost nothing — which is the right shape,
        /// because the player who wants a second berth is the player sailing far.
        /// </summary>
        public static double DivertShareEach(int loading, Tuning t)
        {
            if (loading <= 0) return 0d;
            double want = Clamp01(t.DivertShare) * loading;
            double ceiling = Clamp01(t.MaxDivertShare);
            double total = want < ceiling ? want : ceiling;
            return total / loading;
        }

        /// <summary>Bars per second into one hold, with <paramref name="loading"/> holds sharing the yard.</summary>
        public static double FillPerSecond(double deliveredPerMin, int loading, Tuning t)
        {
            if (deliveredPerMin <= 0d) return 0d;
            return deliveredPerMin / 60d * DivertShareEach(loading, t);
        }

        // ------------------------------------------------------------------ salvage & the yard
        /// <summary>
        /// Salvage a voyage brings home — the ship-upgrade currency, and the only thing it is ever
        /// spent on. A closed loop on purpose: salvage comes from sailing and goes back into sailing,
        /// so nothing here can touch the cash economy however badly it is tuned.
        ///
        /// A failed voyage pays the same reduced share its cards do. The hull came back; it just came
        /// back light.
        /// </summary>
        public static int Salvage(int tier, double loadFraction, int holdLevel, bool succeeded, Tuning t)
        {
            if (loadFraction <= 0d) return 0;
            int row = Clamp(tier, 0, TierCount - 1);
            double paid = Math.Max(0d, t.SalvageRate) * PayoutMult[row]
                          * Clamp01(loadFraction) * HoldMultiplier(holdLevel, t);
            if (!succeeded) paid *= Clamp01(t.FailPayout);
            int salvage = (int)Math.Round(paid, MidpointRounding.AwayFromZero);
            return salvage < 1 ? 1 : salvage;
        }

        /// <summary>
        /// Charts a voyage brings home. Shaped exactly like <see cref="Salvage"/> — same tier
        /// multiplier, same hold multiplier, same reduced share on a failure — because it is the same
        /// kind of thing: a closed-loop currency whose only job is to pace a collection.
        ///
        /// Deliberately does NOT know captains exist. A quartermaster aboard multiplies the result,
        /// and that multiplication happens in VoyageService where the two systems meet, so this file
        /// stays a function of the route and the ship alone.
        /// </summary>
        public static int Charts(int tier, double loadFraction, int holdLevel, bool succeeded, Tuning t)
        {
            if (loadFraction <= 0d) return 0;
            int row = Clamp(tier, 0, TierCount - 1);
            double paid = Math.Max(0d, t.ChartRate) * PayoutMult[row]
                          * Clamp01(loadFraction) * HoldMultiplier(holdLevel, t);
            if (!succeeded) paid *= Clamp01(t.FailPayout);
            int charts = (int)Math.Round(paid, MidpointRounding.AwayFromZero);
            return charts < 1 ? 1 : charts;
        }

        /// <summary>
        /// What the next level of a ship track costs, in salvage. <see cref="Berths"/> is not on this
        /// curve — see <see cref="BerthSalvageCost"/> and <see cref="BerthGemCost"/>, because a berth
        /// is a step change rather than another point on a bar.
        /// </summary>
        public static long ShipCost(int level, Tuning t)
        {
            if (level < 0) level = 0;
            double cost = Math.Max(1d, t.ShipCostBase) * Math.Pow(Math.Max(1.01d, t.ShipCostGrowth), level);
            return (long)Math.Round(cost, MidpointRounding.AwayFromZero);
        }

        /// <summary>Salvage for the next berth, or 0 when the next one is bought with gems instead.</summary>
        public static long BerthSalvageCost(int berthLevel, Tuning t)
            => berthLevel == 0 ? Math.Max(0L, t.BerthSalvageCost) : 0L;

        /// <summary>Gems for the next berth, or 0 when it is bought with salvage instead.</summary>
        public static long BerthGemCost(int berthLevel, Tuning t)
        {
            if (berthLevel == 1) return Math.Max(0L, t.ThirdBerthGems);
            if (berthLevel == 2) return Math.Max(0L, t.FourthBerthGems);
            return 0L;
        }

        /// <summary>The ceiling on a track. Berths run shorter than the other three.</summary>
        public static int MaxLevelOf(int track)
            => track == Berths ? MaxBerths - 1 : MaxShipLevel;

        /// <summary>
        /// Seconds a hold takes to fill from empty. Independent of the delivery rate — both sides of
        /// the division scale with it — which is rule 4 doing its job: the wait is the same on coal and
        /// on diamond, and only the bar count differs.
        /// </summary>
        public static double SecondsToFill(int holdLevel, Tuning t) => SecondsToFill(holdLevel, 1, t);

        /// <summary>As above, with <paramref name="loading"/> holds sharing the yard's output.</summary>
        public static double SecondsToFill(int holdLevel, int loading, Tuning t)
        {
            double share = DivertShareEach(loading, t);
            if (share <= 0d) return 0d;
            int level = Clamp(holdLevel, 0, MaxShipLevel);
            double minutes = t.HoldMinutesBase + t.HoldMinutesPerLevel * level;
            return minutes / share * 60d;
        }

        // ----------------------------------------------------------------- voyage
        /// <summary>How long a ship is away, in seconds.</summary>
        public static double VoyageSeconds(int tier, int speedLevel, Tuning t)
        {
            int row = Clamp(tier, 0, TierCount - 1);
            int level = Clamp(speedLevel, 0, MaxShipLevel);
            double faster = 1d + Math.Max(0d, t.SpeedPerLevel) * level;
            double minutes = Math.Max(0d, t.BaseVoyageMinutes) * DurationMult[row] / faster;
            return minutes * 60d;
        }

        /// <summary>
        /// Cards a voyage brings home. <paramref name="loadFraction"/> is how full the hold was when it
        /// sailed, so a half-loaded ship pays half — the player may leave early, and pays for it in
        /// payout rather than in a refusal.
        ///
        /// Always at least one card on a voyage that sailed at all. A wait that ends in nothing is the
        /// outcome players quit over, and rounding is not a good enough reason to hand them one.
        /// </summary>
        public static int Cards(int tier, double loadFraction, int holdLevel, int crewLevel, Tuning t)
        {
            if (loadFraction <= 0d) return 0;
            int row = Clamp(tier, 0, TierCount - 1);
            int level = Clamp(crewLevel, 0, MaxShipLevel);
            double crew = 1d + Math.Max(0d, t.CrewPerLevel) * level;
            double paid = Math.Max(0d, t.CardRate) * PayoutMult[row]
                          * Clamp01(loadFraction) * HoldMultiplier(holdLevel, t) * crew;
            int cards = (int)Math.Round(paid, MidpointRounding.AwayFromZero);
            return cards < 1 ? 1 : cards;
        }

        /// <summary>
        /// What a hold at <paramref name="holdLevel"/> carries, as a multiple of a base hold — and
        /// therefore what it is worth.
        ///
        /// THIS IS LOAD-BEARING AND WAS MISSING. Payout used to scale on the load FRACTION alone, which
        /// is capped at 1 by definition, so a bigger hold took proportionally longer to fill and paid
        /// exactly the same. A fully upgraded ship came out SLOWER than a stock one — 1.9 cards an hour
        /// against 2.5 — and the whole Hold track was a trap the player paid salvage for. Fraction says
        /// how full she is; this says how big she is; the payout needs both.
        /// </summary>
        public static double HoldMultiplier(int holdLevel, Tuning t)
        {
            double base_ = Math.Max(0.0001d, t.HoldMinutesBase);
            int level = Clamp(holdLevel, 0, MaxShipLevel);
            return (base_ + Math.Max(0d, t.HoldMinutesPerLevel) * level) / base_;
        }

        /// <summary>Berths a fleet has. Level 0 is one berth — there is no fleet with none.</summary>
        public static int BerthCount(int berthLevel)
        {
            int berths = 1 + Clamp(berthLevel, 0, MaxBerths - 1);
            return berths > MaxBerths ? MaxBerths : berths;
        }

        /// <summary>True when enough voyages have been sailed to open this tier.</summary>
        public static bool TierUnlocked(int tier, int voyagesCompleted)
        {
            int row = Clamp(tier, 0, TierCount - 1);
            return voyagesCompleted >= TierVoyagesRequired[row];
        }

        // -------------------------------------------------------------------- risk
        /// <summary>
        /// The chance this voyage comes home short, after whoever is aboard. Clamped at zero rather
        /// than allowed to go negative, so a very good foreman on a very safe route reads as "no risk"
        /// instead of as a number nobody can act on.
        /// </summary>
        public static double RiskFor(int tier, int foremanLevel, Tuning t)
            => RiskFor(tier, foremanLevel, 0d, t);

        /// <summary>
        /// As above, with a further reduction in absolute risk points from whoever else is aboard —
        /// a bosun, in practice (see <see cref="Captains.RiskReduction"/>).
        ///
        /// An OVERLOAD rather than a fourth parameter on the original, because the three-argument
        /// form is called from four places and pinned by the voyage tests, and a signature change
        /// there would have rewritten the balance surface Docs/VOYAGES.md §21 solved. This adds a
        /// term; it moves nothing.
        /// </summary>
        public static double RiskFor(int tier, int foremanLevel, double extraReduction, Tuning t)
        {
            int row = Clamp(tier, 0, TierCount - 1);
            double risk = RiskChance[row]
                        - Math.Max(0d, t.ForemanRiskPerLevel) * Math.Max(0, foremanLevel)
                        - Math.Max(0d, extraReduction);
            return risk < 0d ? 0d : risk;
        }

        /// <summary>
        /// What a failed voyage still pays. At least one card whenever a full one would have paid
        /// anything — see <see cref="Tuning.FailPayout"/> for why the floor is not zero.
        /// </summary>
        public static int CardsOnFailure(int tier, double loadFraction, int holdLevel, int crewLevel, Tuning t)
        {
            int full = Cards(tier, loadFraction, holdLevel, crewLevel, t);
            if (full <= 0) return 0;
            int paid = (int)Math.Round(full * Clamp01(t.FailPayout), MidpointRounding.AwayFromZero);
            return paid < 1 ? 1 : paid;
        }

        /// <summary>How long a berth is out of use after a failure on this route, in seconds.</summary>
        public static double RepairSeconds(int tier, int speedLevel, Tuning t)
            => VoyageSeconds(tier, speedLevel, t) * Math.Max(0d, t.RepairFraction);

        private static double Clamp01(double v) => v < 0d ? 0d : (v > 1d ? 1d : v);

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
