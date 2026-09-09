using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Owns the captain roster and the crate: who has been found, how far they are levelled, how many
    /// charts are in hand, and how long the two pity counters have been running. The maths is in
    /// <see cref="Captains"/> and <see cref="CaptainCrate"/>; this spends, grants and remembers.
    ///
    /// THE RANDOMNESS LIVES HERE AND NOWHERE ELSE. <see cref="CaptainCrate"/> takes a roll and returns
    /// a result; this is the only thing in the feature that calls a generator. The same split
    /// <see cref="VoyageService"/> already uses for its risk roll, and it is what lets the crate's
    /// whole distribution be asserted over ten thousand pulls in an EditMode test.
    ///
    /// PULLING SOMEONE NEW IS NOT A DUPLICATE. The first copy of a captain sets them to level 1 and
    /// consumes nothing; every copy after that is a duplicate waiting to be spent. A crate that handed
    /// a new player "1 duplicate of a captain you do not have" would be paying them in something they
    /// cannot look at, which is the failure <see cref="ForemanService.GrantRandomDuplicates"/> already
    /// documents for cards aimed at an unhired slot.
    ///
    /// LEVELS COST DUPLICATES AND NOTHING ELSE. The foremen charge gems on top of their cards; the
    /// captains do not, because charts already paid for the crate. Charging twice would put the two
    /// rosters back in competition for one wallet, which is the whole reason charts exist.
    /// </summary>
    public sealed class CaptainService
    {
        private readonly SaveData _data;
        private readonly Random _random;
        private readonly UnityEngine.Color[] _gradeTint;
        private Captains.Tuning _tuning;
        private CaptainCrate.Tuning _crate;

        /// <summary>Used when no config is wired, so an unconfigured project still reads correctly
        /// rather than drawing every grade white. Matches the master roster's palette where the two
        /// overlap — Legendary is gold everywhere in the game.</summary>
        private static readonly UnityEngine.Color[] DefaultGradeTint =
        {
            new UnityEngine.Color(0.48f, 0.54f, 0.62f, 1f),   // Common
            new UnityEngine.Color(0.26f, 0.60f, 0.92f, 1f),   // Rare
            new UnityEngine.Color(0.62f, 0.38f, 0.92f, 1f),   // Epic
            new UnityEngine.Color(0.96f, 0.66f, 0.18f, 1f),   // Legendary
            new UnityEngine.Color(0.94f, 0.28f, 0.42f, 1f),   // Mythic
        };

        /// <summary>Raised when anything the roster or crate screen shows has moved.</summary>
        public event Action Changed;

        /// <summary>Raised once per captain handed over by a crate, in the order they came out.</summary>
        public event Action<int> Pulled;

        public CaptainService(SaveData data, Captains.Tuning tuning, CaptainCrate.Tuning crate,
                              Random random = null, UnityEngine.Color[] gradeTint = null)
        {
            _data = data;
            _tuning = tuning;
            _crate = crate;
            _random = random ?? new Random();
            _gradeTint = gradeTint != null && gradeTint.Length >= Captains.GradeCount
                ? gradeTint : DefaultGradeTint;
            Normalise();
        }

        /// <summary>
        /// One grade's colour. Lives here rather than on the roster screen because THREE things read
        /// it now — the row stripe on the collection screen, and the captain standing at the ship's
        /// wheel out at sea, which is in another scene entirely and cannot see a screen's Inspector.
        /// The masters' rarity tint moved here for the same reason; a Legendary that is gold on the
        /// card and purple at the helm is worse than no colour at all.
        /// </summary>
        public UnityEngine.Color GradeTint(Captains.Grade grade)
        {
            int i = (int)grade;
            if (i < 0) i = 0;
            if (i >= _gradeTint.Length) i = _gradeTint.Length - 1;
            return _gradeTint[i];
        }

        public UnityEngine.Color GradeTintOf(int captain) => GradeTint(Captains.RankOf(captain));

        public Captains.Tuning Tuning => _tuning;
        public CaptainCrate.Tuning CrateTuning => _crate;

        /// <summary>
        /// Pads the two arrays, the way the foreman roster and the dock already do. A save written
        /// before captains existed arrives with them null; one written before a captain was APPENDED
        /// to the roster arrives short, and keeps everything it had.
        /// </summary>
        private void Normalise()
        {
            if (_data == null) return;
            _data.captainLevels = Fit(_data.captainLevels, Captains.Count);
            _data.captainDuplicates = Fit(_data.captainDuplicates, Captains.Count);
            if (_data.charts < 0L) _data.charts = 0L;
            if (_data.crateSinceEpic < 0) _data.crateSinceEpic = 0;
            if (_data.crateSinceLegendary < 0) _data.crateSinceLegendary = 0;
        }

        private static int[] Fit(int[] src, int len)
        {
            if (src != null && src.Length == len) return src;
            var fitted = new int[len];
            if (src != null)
            {
                int n = src.Length < len ? src.Length : len;
                for (int i = 0; i < n; i++) fitted[i] = src[i];
            }
            return fitted;
        }

        // ------------------------------------------------------------------ read
        public long Charts => _data != null ? _data.charts : 0L;
        public int CratesOpened => _data != null ? _data.cratesOpened : 0;
        public int SinceEpic => _data != null ? _data.crateSinceEpic : 0;
        public int SinceLegendary => _data != null ? _data.crateSinceLegendary : 0;

        public int Level(int captain)
            => _data != null && Captains.Exists(captain) ? _data.captainLevels[captain] : Captains.NotOwned;

        public int Duplicates(int captain)
            => _data != null && Captains.Exists(captain) ? _data.captainDuplicates[captain] : 0;

        public bool Owned(int captain) => Level(captain) > Captains.NotOwned;

        public int OwnedCount => _data != null ? Captains.OwnedCount(_data.captainLevels) : 0;

        /// <summary>Duplicates still wanted for this captain's next level. 0 at the ceiling.</summary>
        public int DuplicatesNeeded(int captain)
        {
            int level = Level(captain);
            if (level <= Captains.NotOwned || level >= Captains.MaxLevel) return 0;
            return Captains.DuplicatesToLevel(captain, level, _tuning);
        }

        public bool CanLevel(int captain)
        {
            int need = DuplicatesNeeded(captain);
            return need > 0 && Duplicates(captain) >= need;
        }

        /// <summary>
        /// Shared roster-card state. Effect is the captain's current primary contribution: chart or
        /// salvage bonus, risk reduction, or directed-card share according to role.
        /// </summary>
        public RosterCardState CardState(int captain)
        {
            int role = Captains.RoleOf(captain);
            double effect;
            switch (role)
            {
                case Captains.Gunner: effect = SalvageMultiplier(captain) - 1d; break;
                case Captains.Bosun:  effect = RiskReduction(captain); break;
                case Captains.Purser: effect = DirectedShare(captain); break;
                default:              effect = ChartMultiplier(captain) - 1d; break;
            }

            return new RosterCardState(
                captain,
                (RosterCardState.Rarity)(int)Captains.RankOf(captain),
                role,
                Level(captain),
                Captains.MaxLevel,
                Duplicates(captain),
                DuplicatesNeeded(captain),
                effect,
                // Never busy. A captain used to be occupied by the voyage they were sent on; with
                // the dock gone nothing takes one away — sea combat picks the best owned captain
                // itself (see ExpeditionService.CaptainAboard) rather than assigning one.
                false);
        }

        /// <summary>How many captains are waiting to be levelled — the number on the opener's badge.</summary>
        public int PendingCount()
        {
            int n = 0;
            for (int c = 0; c < Captains.Count; c++) if (CardState(c).NeedsAttention) n++;
            return n;
        }

        // ---------------------------------------------------------------- charts
        /// <summary>
        /// Bank charts. The dock is the only caller — charts are earned by sailing and by nothing
        /// else, which is what keeps this loop sealed off from the cash economy.
        /// </summary>
        public void AddCharts(long amount)
        {
            if (_data == null || amount <= 0L) return;
            _data.charts += amount;
            Changed?.Invoke();
        }

        public long CrateCost(int crates) => CaptainCrate.Cost(crates, _crate);

        public bool CanOpen(int crates)
            => _data != null && crates > 0 && _data.charts >= CaptainCrate.Cost(crates, _crate);

        // ----------------------------------------------------------------- crate
        /// <summary>
        /// Open <paramref name="crates"/> at once. Returns who came out, in order, or null when there
        /// were not enough charts.
        ///
        /// The charts are taken BEFORE the first roll and the whole batch is rolled in one call, so a
        /// bulk open cannot be interrupted half-paid, and the pity counters advance across the batch
        /// exactly as they would across ten separate presses.
        /// </summary>
        public int[] TryOpen(int crates)
        {
            if (_data == null || crates <= 0) return null;
            long cost = CaptainCrate.Cost(crates, _crate);
            if (_data.charts < cost) return null;

            _data.charts -= cost;

            var pulled = new int[crates];
            for (int i = 0; i < crates; i++)
            {
                int captain = CaptainCrate.RollCaptain(_random.NextDouble(), _random.NextDouble(),
                                                       _data.crateSinceEpic, _data.crateSinceLegendary,
                                                       _crate);
                // The roster carries nobody at any reachable grade. Refund and stop rather than
                // handing back a batch of -1s: this cannot happen with the shipped roster, and if it
                // ever can, silently eating the charts is the worst of the available answers.
                if (!Captains.Exists(captain))
                {
                    _data.charts += cost;
                    return null;
                }

                CaptainCrate.Advance(Captains.RankOf(captain),
                                     ref _data.crateSinceEpic, ref _data.crateSinceLegendary);
                Grant(captain);
                pulled[i] = captain;
            }

            _data.cratesOpened += crates;
            Changed?.Invoke();
            for (int i = 0; i < pulled.Length; i++) Pulled?.Invoke(pulled[i]);
            return pulled;
        }

        /// <summary>
        /// Hand a captain over. The first copy is the captain; every copy after it is a duplicate.
        /// </summary>
        private void Grant(int captain)
        {
            if (_data == null || !Captains.Exists(captain)) return;
            if (_data.captainLevels[captain] <= Captains.NotOwned) _data.captainLevels[captain] = 1;
            else _data.captainDuplicates[captain]++;
        }

        /// <summary>Spend the duplicates a captain has earned on their next level.</summary>
        public bool TryLevelUp(int captain)
        {
            if (!CanLevel(captain)) return false;
            _data.captainDuplicates[captain] -= Captains.DuplicatesToLevel(captain, Level(captain), _tuning);
            _data.captainLevels[captain]++;
            Changed?.Invoke();
            return true;
        }

        // ---------------------------------------------------------------- aboard
        // What the sea asks: the captain on the bridge (-1 = nobody), answered for the level they
        // are actually at, so the caller never has to look one up.

        public double ChartMultiplier(int captain) => Captains.ChartMultiplier(captain, Level(captain), _tuning);
        public double SalvageMultiplier(int captain) => Captains.SalvageMultiplier(captain, Level(captain), _tuning);
        public double RiskReduction(int captain) => Captains.RiskReduction(captain, Level(captain), _tuning);
        public double RepairMultiplier(int captain) => Captains.RepairMultiplier(captain, Level(captain), _tuning);
        public double DirectedShare(int captain) => Captains.DirectedShare(captain, Level(captain), _tuning);
    }
}
