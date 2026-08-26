using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Owns the foreman roster: who is hired, how far they are levelled, and the spare cards waiting
    /// to be spent. The maths is all in <see cref="Foremen"/>; this holds the state, takes the money,
    /// and tells everyone who cares that something moved.
    ///
    /// WHAT IT IS FOR, in the order the problems were found:
    ///   - Gems had no gameplay sink. TrySpendGems was called in exactly two places, both inside the
    ///     premium store, so every gem a contract or a rewarded ad paid out could only ever be turned
    ///     back into cash. Hiring and levelling is now the thing gems are actually for.
    ///   - There was no late game. Eight islands cap out and prestige has been retired, so the roster
    ///     is the long tail: ninety duplicates per foreman, eight foremen, earned rather than bought.
    ///   - There was nothing to collect. Every other number in the game is a level on a bar.
    ///
    /// The levels array is handed to <see cref="Foremen"/> directly rather than copied, and the income
    /// multiplier is cached, because <see cref="MarketService"/> reads it once a second and the island
    /// simulation reads the per-station multiplier far more often than that.
    /// </summary>
    public sealed class ForemanService
    {
        private readonly SaveData _data;
        private readonly WalletService _wallet;
        private readonly Foremen.Tuning _tuning;
        private readonly Random _random = new Random();

        private double _incomeMultiplier = 1d;

        /// <summary>
        /// The per-station speeds, handed to IslandEconomy as a LIVE array and rewritten in place
        /// whenever the roster moves. Same contract as MaintenanceService's condition array, and the
        /// same reason: the island reads it every frame, so handing over a fresh array per change
        /// would allocate on a path that must not, and copying it per read would be worse.
        /// </summary>
        private readonly float[] _stationSpeeds = new float[Foremen.Count];

        /// <summary>Which slot changed, so a roster screen can refresh one card rather than all eight.
        /// -1 means "several, or none in particular".</summary>
        public event Action<int> RosterChanged;

        /// <summary>
        /// Raised only when a foreman actually gains a level — a hire is level 1, a level-up is one
        /// more. Distinct from <see cref="RosterChanged"/>, which also fires when cards merely arrive:
        /// the goal system counts levels gained, and cards it awarded itself must not count as progress
        /// toward a goal about gaining levels.
        ///
        /// An event rather than a GoalService reference because the goal system already holds one of
        /// these to pay cards with, and taking the dependency both ways would be a construction cycle.
        /// </summary>
        public event Action<int> Levelled;

        public ForemanService(SaveData data, WalletService wallet, Foremen.Tuning tuning)
        {
            _data = data;
            _wallet = wallet;
            _tuning = tuning;
            Normalise();
            Recompute();
        }

        /// <summary>
        /// A save written before the roster existed arrives with these fields missing, which
        /// JsonUtility turns into null or a zero-length array rather than into a default. Padding here
        /// is what lets the roster ship without bumping the save version — and a version bump wipes
        /// progress, so it is not something to spend on a feature that adds fields and takes none away.
        /// </summary>
        private void Normalise()
        {
            if (_data == null) return;
            _data.foremanLevels = Fit(_data.foremanLevels);
            _data.foremanDuplicates = Fit(_data.foremanDuplicates);
        }

        private static int[] Fit(int[] src)
        {
            if (src != null && src.Length == Foremen.Count) return src;
            var fitted = new int[Foremen.Count];
            if (src != null)
            {
                int n = src.Length < Foremen.Count ? src.Length : Foremen.Count;
                for (int i = 0; i < n; i++) fitted[i] = src[i];
            }
            return fitted;
        }

        private void Recompute()
        {
            _incomeMultiplier = Foremen.IncomeMultiplier(Levels, _tuning);
            for (int s = 0; s < Foremen.Count; s++)
                _stationSpeeds[s] = (float)Foremen.StationMultiplier(Levels, s, _tuning);
        }

        // ------------------------------------------------------------------ read
        public int[] Levels => _data != null ? _data.foremanLevels : null;
        public Foremen.Tuning Tuning => _tuning;

        /// <summary>What the whole roster is worth to income. Cached — read once a second by the yards.</summary>
        public double IncomeMultiplier => _incomeMultiplier;

        /// <summary>The live per-station speeds. Hand straight to IslandEconomy.SetForemen; do not copy.</summary>
        public float[] StationSpeeds => _stationSpeeds;

        /// <summary>What one station's foreman is worth to that station's throughput.</summary>
        public double StationMultiplier(int station) => Foremen.StationMultiplier(Levels, station, _tuning);

        public int LevelOf(int station) => Foremen.LevelOf(Levels, station);
        public bool IsHired(int station) => Foremen.IsHired(Levels, station);
        public bool IsMaxed(int station) => Foremen.IsMaxed(Levels, station);
        public int HiredCount => Foremen.HiredCount(Levels);

        public int DuplicatesOf(int station)
            => _data != null && _data.foremanDuplicates != null
               && station >= 0 && station < _data.foremanDuplicates.Length
                ? _data.foremanDuplicates[station] : 0;

        public long HireGems(int station) => Foremen.HireGems(station, _tuning);
        public int DuplicatesToLevel(int station) => Foremen.DuplicatesToLevel(LevelOf(station), _tuning);
        public long GemsToLevel(int station) => Foremen.GemsToLevel(LevelOf(station), _tuning);

        /// <summary>True when the player could level this slot right now — both costs covered.</summary>
        public bool CanLevel(int station)
        {
            if (!IsHired(station) || IsMaxed(station)) return false;
            return DuplicatesOf(station) >= DuplicatesToLevel(station)
                && _wallet != null && _wallet.Gems >= GemsToLevel(station);
        }

        /// <summary>True when the player could hire this slot right now.</summary>
        public bool CanHire(int station)
            => !IsHired(station) && _wallet != null && _wallet.Gems >= HireGems(station);

        // ----------------------------------------------------------------- write
        /// <summary>Hire the foreman in this slot. Costs gems; puts them at level 1.</summary>
        public bool TryHire(int station)
        {
            if (_data == null || _wallet == null) return false;
            if (station < 0 || station >= Foremen.Count) return false;
            if (IsHired(station)) return false;
            if (!_wallet.TrySpendGems(Foremen.HireGems(station, _tuning))) return false;

            _data.foremanLevels[station] = 1;
            Recompute();
            Levelled?.Invoke(station);
            RosterChanged?.Invoke(station);
            return true;
        }

        /// <summary>
        /// Take a hired foreman up one level. Costs duplicates AND gems: the duplicates are what make
        /// this a collection rather than a price list, and the gems are what stop a player who has been
        /// hoarding cards from spending the whole roster in one sitting.
        /// </summary>
        public bool TryLevelUp(int station)
        {
            if (_data == null || _wallet == null) return false;
            if (station < 0 || station >= Foremen.Count) return false;
            if (!IsHired(station) || IsMaxed(station)) return false;

            int level = LevelOf(station);
            int needCards = Foremen.DuplicatesToLevel(level, _tuning);
            long needGems = Foremen.GemsToLevel(level, _tuning);
            if (DuplicatesOf(station) < needCards) return false;
            if (!_wallet.TrySpendGems(needGems)) return false;

            _data.foremanDuplicates[station] -= needCards;
            _data.foremanLevels[station] = level + 1;
            Recompute();
            Levelled?.Invoke(station);
            RosterChanged?.Invoke(station);
            return true;
        }

        /// <summary>Award cards for a named slot — a contract reward, a daily, an achievement.</summary>
        public void GrantDuplicates(int station, int count)
        {
            if (_data == null || count <= 0) return;
            if (station < 0 || station >= Foremen.Count) return;
            _data.foremanDuplicates[station] += count;
            RosterChanged?.Invoke(station);
        }

        /// <summary>
        /// Award cards for a slot picked at random, which is what a generic "foreman crate" pays out.
        /// Weighted toward what the player has already hired: cards for a foreman you have never hired
        /// are dead weight until you find the gems, and a reward that reads as nothing is worse than a
        /// smaller reward that reads as progress. Falls back to the whole roster before anyone is hired.
        /// </summary>
        public int GrantRandomDuplicates(int count)
        {
            if (_data == null || count <= 0) return -1;

            int hired = HiredCount;
            int pick;
            if (hired > 0)
            {
                int nth = _random.Next(hired);
                pick = 0;
                for (int s = 0; s < Foremen.Count; s++)
                {
                    if (_data.foremanLevels[s] <= Foremen.NotHired) continue;
                    if (nth == 0) { pick = s; break; }
                    nth--;
                }
            }
            else pick = _random.Next(Foremen.Count);

            GrantDuplicates(pick, count);
            return pick;
        }

        /// <summary>
        /// Award cards to the hired foreman who is FURTHEST BEHIND — lowest level, then fewest
        /// duplicates, then lowest slot. This is what a purser aboard a voyage buys
        /// (<see cref="Game.Core.Captains.Purser"/>): the same cards, aimed instead of scattered.
        ///
        /// Aimed at the one furthest behind rather than at one the player nominates, because a
        /// nomination is a screen, a saved choice and a thing to forget to change, and the answer it
        /// would nearly always be set to is this one. Ninety duplicates per foreman is a long enough
        /// road that a card landing where it is shortest is worth as much as an extra card, and it
        /// costs the balance nothing at all — the count is unchanged.
        ///
        /// Falls back to <see cref="GrantRandomDuplicates"/> before anyone is hired, for the reason
        /// that method already gives: cards for a foreman nobody has hired read as nothing.
        /// </summary>
        public int GrantDirectedDuplicates(int count)
        {
            if (_data == null || count <= 0) return -1;
            if (HiredCount <= 0) return GrantRandomDuplicates(count);

            int pick = -1;
            for (int s = 0; s < Foremen.Count; s++)
            {
                if (_data.foremanLevels[s] <= Foremen.NotHired) continue;
                if (pick < 0) { pick = s; continue; }

                if (_data.foremanLevels[s] < _data.foremanLevels[pick]
                    || (_data.foremanLevels[s] == _data.foremanLevels[pick]
                        && _data.foremanDuplicates[s] < _data.foremanDuplicates[pick]))
                    pick = s;
            }

            if (pick < 0) return GrantRandomDuplicates(count);
            GrantDuplicates(pick, count);
            return pick;
        }
    }
}
