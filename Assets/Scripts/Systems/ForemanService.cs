using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Owns the master roster: who you have, how many stars they carry, and the spare cards waiting to
    /// be spent. The maths is all in <see cref="Foremen"/> and <see cref="MasterChest"/>; this holds
    /// the state, takes the money, and tells everyone who cares that something moved.
    ///
    /// WHAT IT IS FOR, in the order the problems were found:
    ///   - Gems had no gameplay sink. TrySpendGems was called in exactly two places, both inside the
    ///     premium store, so every gem a contract or a rewarded ad paid out could only ever be turned
    ///     back into cash. Chests are now the thing gems are actually for.
    ///   - There was no late game. Eight islands cap out and prestige has been retired, so the roster
    ///     is the long tail: ninety cards per master, eight masters, earned rather than bought.
    ///   - There was nothing to collect. Every other number in the game is a level on a bar.
    ///
    /// HIRING IS GONE. A master used to be bought outright for gems and then levelled with cards, which
    /// meant every card that arrived for somebody you had not bought yet was dead weight — and the
    /// screen had to explain two different prices to justify it. The first card now puts a master at
    /// one star, gems buy chests rather than people, and a star-up costs cards alone. One currency at
    /// the door, one at the counter, and nothing a chest hands over is ever worthless.
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
        private readonly MasterChest.Tuning _chest;
        private readonly TimeService _time;
        private readonly UnityEngine.Color[] _tierTint;
        private readonly Random _random = new Random();

        /// <summary>Used when no config is wired, so an unconfigured project still reads correctly
        /// rather than drawing every tier white.</summary>
        private static readonly UnityEngine.Color[] DefaultTierTint =
        {
            new UnityEngine.Color(0.48f, 0.54f, 0.62f, 1f),   // Common
            new UnityEngine.Color(0.26f, 0.60f, 0.92f, 1f),   // Rare
            new UnityEngine.Color(0.62f, 0.38f, 0.92f, 1f),   // Epic
            new UnityEngine.Color(0.96f, 0.66f, 0.18f, 1f),   // Legendary
            new UnityEngine.Color(0.94f, 0.28f, 0.42f, 1f),   // Mythic
        };

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
        /// Raised only when the player SPENDS cards to add a star. Distinct from
        /// <see cref="RosterChanged"/>, which also fires when cards merely arrive: the goal system
        /// counts stars gained, and cards it awarded itself must not count as progress toward a goal
        /// about gaining them. That is why the first card unlocking a master is a RosterChanged and not
        /// one of these — it is a card arriving, not a star bought.
        ///
        /// An event rather than a GoalService reference because the goal system already holds one of
        /// these to pay cards with, and taking the dependency both ways would be a construction cycle.
        /// </summary>
        public event Action<int> Levelled;

        /// <summary>
        /// The chest tuning and the clock are optional so the ~thirty test sites that build a service
        /// to get at the roster maths keep compiling untouched, and so a null clock falls back to the
        /// wall clock the same way <see cref="GoalService"/> does.
        /// </summary>
        public ForemanService(SaveData data, WalletService wallet, Foremen.Tuning tuning,
                              MasterChest.Tuning chest = default, TimeService time = null,
                              UnityEngine.Color[] tierTint = null)
        {
            _tierTint = tierTint != null && tierTint.Length >= Foremen.TierCount ? tierTint : DefaultTierTint;
            _data = data;
            _wallet = wallet;
            _tuning = tuning;
            // default(Tuning) is all zeroes, which would price a chest at nothing and hand over no
            // cards; an unsupplied tuning means "the defaults", not "free".
            _chest = chest.CardsPerChest > 0 ? chest : MasterChest.Tuning.Default;
            _time = time;
            Normalise();
            Recompute();
        }

        private long NowUnix()
            => _time != null ? _time.NowUnix() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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

            // A save from before hiring was deleted can hold cards for a master nobody ever paid to
            // hire — goals, contracts, chapters and voyages all paid cards from the first hour, while
            // the first hire cost 150 gems. Standing him up is what the till used to do, and there is
            // no longer a till: unowned slots are skipped by the aimed card, so without this his cards
            // would sit invisible and unspendable until a flat one-in-eight roll happened to land on
            // him. The cards stay banked, exactly as Bank() leaves them.
            for (int s = 0; s < Foremen.Count; s++)
                if (_data.foremanLevels[s] <= Foremen.NotHired && _data.foremanDuplicates[s] > 0)
                    _data.foremanLevels[s] = 1;

            if (_data.masterFreeChestClaimUnix < 0L) _data.masterFreeChestClaimUnix = 0L;
            if (_data.masterChestsOpened < 0) _data.masterChestsOpened = 0;
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

        public int DuplicatesToLevel(int station) => Foremen.DuplicatesToLevel(LevelOf(station), _tuning);

        /// <summary>Which tier a master is at — the colour of his card, his plinth and his size.</summary>
        public Foremen.Tier TierOf(int station) => Foremen.TierOfStation(Levels, station);

        /// <summary>
        /// Shared roster-card state. The UI should not have to reconstruct ownership, upgrade
        /// readiness or progress from separate arrays; captains expose the same contract.
        /// </summary>
        public RosterCardState CardState(int station)
        {
            int level = LevelOf(station);
            return new RosterCardState(
                station,
                (RosterCardState.Rarity)(int)TierOf(station),
                station,
                level,
                Foremen.MaxLevel,
                DuplicatesOf(station),
                DuplicatesToLevel(station),
                Foremen.Boost(level, _tuning),
                false);
        }

        /// <summary>
        /// One tier's colour. Lives here rather than on either screen because BOTH read it — the card
        /// frame in Game.UI and the plinth under his feet in Game.Gameplay — and the two must never
        /// disagree about what Legendary looks like.
        /// </summary>
        public UnityEngine.Color TierTint(Foremen.Tier tier)
        {
            int i = (int)tier;
            if (i < 0) i = 0;
            if (i >= _tierTint.Length) i = _tierTint.Length - 1;
            return _tierTint[i];
        }

        public UnityEngine.Color TierTintOf(int station) => TierTint(TierOf(station));

        /// <summary>True when the player could add a star right now. Cards alone — gems buy chests.</summary>
        public bool CanLevel(int station)
        {
            if (!IsHired(station) || IsMaxed(station)) return false;
            return DuplicatesOf(station) >= DuplicatesToLevel(station);
        }

        /// <summary>Cards currently ready to star up; used by the roster opener badge.</summary>
        public int PendingCount()
        {
            int count = 0;
            for (int station = 0; station < Foremen.Count; station++)
                if (CardState(station).NeedsAttention) count++;
            return count;
        }

        // ------------------------------------------------------------------ chest
        public MasterChest.Tuning ChestTuning => _chest;
        public int CardsPerChest => _chest.CardsPerChest;
        public int ChestsOpened => _data != null ? _data.masterChestsOpened : 0;

        public long ChestCost(int chests) => MasterChest.Cost(chests, _chest);

        public bool CanOpenChest(int chests)
            => chests > 0 && _wallet != null && _wallet.Gems >= ChestCost(chests);

        /// <summary>When the free chest next comes due, and whether it is due now.</summary>
        public bool FreeChestReady
            => _data != null && MasterChest.FreeReady(NowUnix(), _data.masterFreeChestClaimUnix, _chest);

        public long FreeChestSecondsLeft
            => _data != null
                ? MasterChest.FreeSecondsLeft(NowUnix(), _data.masterFreeChestClaimUnix, _chest) : 0L;

        /// <summary>
        /// Open <paramref name="chests"/> at once. Gems come out first and the whole batch is rolled
        /// after, so a half-paid open cannot exist; the returned slots are in reveal order and are what
        /// the ceremony flips. Null when it could not be paid for.
        /// </summary>
        public int[] TryOpenChest(int chests)
        {
            if (_data == null || _wallet == null || chests <= 0) return null;
            if (!_wallet.TrySpendGems(ChestCost(chests))) return null;

            _data.masterChestsOpened += chests;
            return Deal(chests, _chest.CardsPerChest);
        }

        /// <summary>
        /// Take the free chest. Stamps the claim at NOW rather than at when it came due, so a player
        /// who is away for a week comes back to one chest and not seven.
        /// </summary>
        public int[] TryClaimFreeChest()
        {
            if (_data == null || !FreeChestReady) return null;
            _data.masterFreeChestClaimUnix = NowUnix();
            return Deal(1, _chest.FreeCards);
        }

        /// <summary>
        /// Hands over one batch of cards. Each chest aims <see cref="MasterChest.DirectedIn"/> of its
        /// cards at whoever is furthest behind and rolls the rest flat, then the whole batch lands in
        /// one go: eight separate RosterChanged events for a ten-chest open would rebuild the roster
        /// screen thirty times while the reveal was still playing.
        /// </summary>
        private int[] Deal(int chests, int cardsPerChest)
        {
            if (cardsPerChest <= 0) { RosterChanged?.Invoke(-1); return new int[0]; }

            int aimed = MasterChest.DirectedIn(_chest);
            var slots = new int[chests * cardsPerChest];
            int at = 0;

            for (int c = 0; c < chests; c++)
                for (int i = 0; i < cardsPerChest; i++)
                {
                    int slot = i < aimed ? AimedSlot() : MasterChest.RollSlot(_random.NextDouble());
                    Bank(slot, 1);
                    slots[at++] = slot;
                }

            Recompute();
            RosterChanged?.Invoke(-1);
            return slots;
        }

        // ----------------------------------------------------------------- write
        /// <summary>
        /// Take a master up one star. Costs cards alone — the gems were spent at the chest, and
        /// charging twice for the same card is what made the old roster feel like a price list.
        /// </summary>
        public bool TryLevelUp(int station)
        {
            if (_data == null) return false;
            if (station < 0 || station >= Foremen.Count) return false;
            if (!IsHired(station) || IsMaxed(station)) return false;

            int level = LevelOf(station);
            int needCards = Foremen.DuplicatesToLevel(level, _tuning);
            if (DuplicatesOf(station) < needCards) return false;

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
            if (Bank(station, count)) Recompute();
            RosterChanged?.Invoke(station);
        }

        /// <summary>
        /// Puts cards in a slot, standing a master up in an empty one. Returns true when a master
        /// actually appeared, which is the caller's cue to recompute the multipliers.
        ///
        /// THE UNLOCK IS FREE — every card handed over is banked, including the one that stood him up.
        /// Charging a card for the unlock is the tidier fiction, but it makes every reward in the game
        /// lie: a contract that says "+1 usta kartı" would bank nothing at all, and the player who
        /// went to look at the bar would find it exactly where they left it. The master used to cost
        /// gems anyway, so arriving for nothing is no worse than what he replaced.
        ///
        /// The unlock does NOT raise <see cref="Levelled"/>: a card arriving is not a star bought, and
        /// the goal system counts the latter — see that event's own note.
        /// </summary>
        private bool Bank(int station, int count)
        {
            _data.foremanDuplicates[station] += count;
            if (_data.foremanLevels[station] > Foremen.NotHired) return false;
            _data.foremanLevels[station] = 1;
            return true;
        }

        /// <summary>
        /// The owned master who is furthest behind: fewest stars, then fewest cards, then lowest slot.
        /// -1 when the roster is empty.
        ///
        /// OWNED ONLY, deliberately. An unowned master is trivially the furthest behind, so counting
        /// them would send every aimed card in the game at unlocking the next empty slot until there
        /// were none left — and finding a new master would stop being something a chest DOES and
        /// become a schedule. Meeting somebody new stays a roll; helping the laggard is the aim.
        /// </summary>
        private int FurthestBehind()
        {
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
            return pick;
        }

        /// <summary>Where an aimed card goes: the laggard, or a roll when nobody is owned yet.</summary>
        private int AimedSlot()
        {
            int pick = FurthestBehind();
            return pick >= 0 ? pick : MasterChest.RollSlot(_random.NextDouble());
        }

        /// <summary>
        /// Award cards for a slot picked at random, which is what a generic reward pays out. Flat over
        /// the whole roster: it used to be weighted toward masters you already had, because a card for
        /// somebody unhired was dead weight until you found the gems to hire them — the first card now
        /// unlocks its master outright, so the unweighted roll is the better one and a reward landing
        /// on an empty slot is the best outcome rather than the worst.
        ///
        /// Returns the slot it landed on; contracts keep that to say who the card was for.
        /// </summary>
        public int GrantRandomDuplicates(int count)
        {
            if (_data == null || count <= 0) return -1;
            int pick = MasterChest.RollSlot(_random.NextDouble());
            GrantDuplicates(pick, count);
            return pick;
        }

        /// <summary>
        /// Award cards to whoever is FURTHEST BEHIND — fewest stars, then fewest cards, then lowest
        /// slot. This is what a purser aboard a voyage buys (<see cref="Game.Core.Captains.Purser"/>),
        /// and what one card in every chest does: the same cards, aimed instead of scattered.
        ///
        /// Aimed at the one furthest behind rather than at one the player nominates, because a
        /// nomination is a screen, a saved choice and a thing to forget to change, and the answer it
        /// would nearly always be set to is this one. Ninety cards per master is a long enough road
        /// that a card landing where it is shortest is worth as much as an extra card, and it costs the
        /// balance nothing at all — the count is unchanged.
        /// </summary>
        public int GrantDirectedDuplicates(int count)
        {
            if (_data == null || count <= 0) return -1;
            int pick = AimedSlot();
            GrantDuplicates(pick, count);
            return pick;
        }
    }
}
