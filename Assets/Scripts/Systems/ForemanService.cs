using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Owns the master roster: who you have, how many stars they carry, the spare cards waiting to be
    /// spent, and which one of each station's three is actually posted there. The maths is all in
    /// <see cref="Foremen"/> and <see cref="MasterChest"/>; this holds the state, takes the money, and
    /// tells everyone who cares that something moved.
    ///
    /// WHAT IT IS FOR, in the order the problems were found:
    ///   - Gems had no gameplay sink. TrySpendGems was called in exactly two places, both inside the
    ///     premium store, so every gem a contract or a rewarded ad paid out could only ever be turned
    ///     back into cash. Chests are now the thing gems are actually for.
    ///   - There was no late game. Eight islands cap out and prestige has been retired, so the roster
    ///     is the long tail: fifteen masters at fifty to a hundred cards each, earned rather than
    ///     bought.
    ///   - There was nothing to collect. Every other number in the game is a level on a bar.
    ///
    /// HIRING IS GONE. A master used to be bought outright for gems and then levelled with cards, which
    /// meant every card that arrived for somebody you had not bought yet was dead weight — and the
    /// screen had to explain two different prices to justify it. The first card now puts a master at
    /// one star, gems buy chests rather than people, and a star-up costs cards alone. One currency at
    /// the door, one at the counter, and nothing a chest hands over is ever worthless.
    ///
    /// POSTING IS AUTOMATIC UNTIL IT IS NOT. A station with nobody posted takes the best card you own
    /// there the moment one arrives (<see cref="Foremen.BestOwnedAt"/>), so the roster works without
    /// the player ever opening the screen; the screen is where you override that. A card that would be
    /// a downgrade never steals the post, and a post is never left empty while an owned card exists.
    ///
    /// The state arrays are handed to <see cref="Foremen"/> directly rather than copied, and the income
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
        private readonly SaveService _save;
        private readonly UnityEngine.Color[] _rarityTint;
        private readonly Random _random = new Random();

        /// <summary>Used when no config is wired, so an unconfigured project still reads correctly
        /// rather than drawing every rarity white.</summary>
        private static readonly UnityEngine.Color[] DefaultRarityTint =
        {
            new UnityEngine.Color(0.48f, 0.54f, 0.62f, 1f),   // Common
            new UnityEngine.Color(0.26f, 0.60f, 0.92f, 1f),   // Rare
            new UnityEngine.Color(0.96f, 0.66f, 0.18f, 1f),   // Legendary
        };

        private double _incomeMultiplier = 1d;
        private double _offlineBonus;

        /// <summary>
        /// The per-station speeds, handed to IslandEconomy as a LIVE array and rewritten in place
        /// whenever the roster moves. Same contract as MaintenanceService's condition array, and the
        /// same reason: the island reads it every frame, so handing over a fresh array per change
        /// would allocate on a path that must not, and copying it per read would be worse.
        ///
        /// Indexed by SAVED ECONOMY SLOT, not by roster station — IslandEconomy.ForemanSpeed is called
        /// with the legacy eight-wide numbering and the roster only covers five of those. The three it
        /// does not cover stay at 1 forever. <see cref="Foremen.EconomyStation"/> is the mapping.
        /// </summary>
        private readonly float[] _stationSpeeds = new float[IslandEconomy.Stations.Length];

        /// <summary>Which master changed, so a roster screen can refresh one card rather than all
        /// fifteen. -1 means "several, or none in particular".</summary>
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
        /// wall clock the same way <see cref="GoalService"/> does. The save service is optional for the
        /// same reason; without one a chest is simply not written until the next autosave.
        /// </summary>
        public ForemanService(SaveData data, WalletService wallet, Foremen.Tuning tuning,
                              MasterChest.Tuning chest = default, TimeService time = null,
                              UnityEngine.Color[] rarityTint = null, SaveService save = null)
        {
            _rarityTint = rarityTint != null && rarityTint.Length >= Foremen.RarityCount
                ? rarityTint : DefaultRarityTint;
            _data = data;
            _wallet = wallet;
            _save = save;
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
            _data.masterStars = Fit(_data.masterStars, Foremen.Count);
            _data.masterCards = Fit(_data.masterCards, Foremen.Count);
            _data.masterActive = FitActive(_data.masterActive);

            // Cards for a master nobody has met yet: goals, contracts, chapters and voyages all pay
            // cards, and a card arriving is what stands a master up. Without this his cards would sit
            // invisible and unspendable until a roll happened to land on him again. The cards stay
            // banked, exactly as Bank() leaves them.
            for (int m = 0; m < Foremen.Count; m++)
                if (_data.masterStars[m] <= Foremen.NotHired && _data.masterCards[m] > 0)
                    _data.masterStars[m] = 1;

            FillEmptyPosts();

            if (_data.masterFreeChestClaimUnix < 0L) _data.masterFreeChestClaimUnix = 0L;
            if (_data.masterChestsOpened < 0) _data.masterChestsOpened = 0;
        }

        private static int[] Fit(int[] src, int length)
        {
            if (src != null && src.Length == length) return src;
            var fitted = new int[length];
            if (src != null)
            {
                int n = src.Length < length ? src.Length : length;
                for (int i = 0; i < n; i++) fitted[i] = src[i];
            }
            return fitted;
        }

        /// <summary>Like <see cref="Fit"/>, but an unwritten posting is -1 rather than 0 — slot 0 is a
        /// real master, so a zeroed array would post the Common mine master at every station.</summary>
        private static int[] FitActive(int[] src)
        {
            var fitted = Foremen.NewActive();
            if (src != null)
            {
                int n = src.Length < fitted.Length ? src.Length : fitted.Length;
                for (int i = 0; i < n; i++) fitted[i] = src[i];
            }
            return fitted;
        }

        /// <summary>
        /// Posts the best owned card at any station standing empty. Called on load and after every card
        /// arrives, so a player who never opens the roster screen still gets everything the chests gave
        /// them. It only ever FILLS: a station you deliberately posted a Common at keeps him when his
        /// Legendary turns up, because silently overriding a choice is worse than a bonus arriving one
        /// tap late — and the card that arrived is announced anyway.
        /// </summary>
        private void FillEmptyPosts()
        {
            for (int s = 0; s < Foremen.StationCount; s++)
            {
                if (Foremen.ActiveAt(_data.masterActive, _data.masterStars, s) >= 0) continue;
                _data.masterActive[s] = Foremen.BestOwnedAt(_data.masterStars, s);
            }
        }

        private void Recompute()
        {
            int[] active = _data != null ? _data.masterActive : null;
            _incomeMultiplier = Foremen.IncomeMultiplier(active, Stars, _tuning);
            _offlineBonus = Foremen.OfflineBonus(active, Stars, _tuning);

            for (int i = 0; i < _stationSpeeds.Length; i++) _stationSpeeds[i] = 1f;
            for (int s = 0; s < Foremen.StationCount; s++)
            {
                int slot = Foremen.EconomyStation[s];
                if (slot < 0 || slot >= _stationSpeeds.Length) continue;
                _stationSpeeds[slot] = (float)Foremen.StationMultiplier(active, Stars, s, _tuning);
            }
        }

        // ------------------------------------------------------------------ read
        /// <summary>Stars per master, indexed by <see cref="Foremen.Roster"/> position.</summary>
        public int[] Stars => _data != null ? _data.masterStars : null;

        /// <summary>Who is posted at each of the five stations, or -1. Read through
        /// <see cref="ActiveAt"/> rather than indexed directly — the raw array is not validated.</summary>
        public int[] Active => _data != null ? _data.masterActive : null;

        public Foremen.Tuning Tuning => _tuning;

        /// <summary>What the whole roster is worth to income. Cached — read once a second by the yards.</summary>
        public double IncomeMultiplier => _incomeMultiplier;

        /// <summary>Percentage points the posted masters add to offline efficiency. Cached for the same
        /// reason; read once per resume by <see cref="GameBootstrap"/>.</summary>
        public double OfflineBonus => _offlineBonus;

        /// <summary>The live per-station speeds. Hand straight to IslandEconomy.SetForemen; do not copy.</summary>
        public float[] StationSpeeds => _stationSpeeds;

        /// <summary>Who is working at this station, or -1 for nobody.</summary>
        public int ActiveAt(int station) => Foremen.ActiveAt(Active, Stars, station);

        /// <summary>What the master posted at this station is worth to its throughput.</summary>
        public double StationMultiplier(int station)
            => Foremen.StationMultiplier(Active, Stars, station, _tuning);

        /// <summary>Is anybody working at this station? What the island asks before it stands a body
        /// on a plinth.</summary>
        public bool StationStaffed(int station) => ActiveAt(station) >= 0;

        /// <summary>Stars of the master posted at this station — his plinth size. 0 for an empty post.</summary>
        public int StationStars(int station)
        {
            int master = ActiveAt(station);
            return master < 0 ? 0 : LevelOf(master);
        }

        /// <summary>Rarity of the master posted at this station — his plinth colour. Common for an
        /// empty post, which is the colour an unmanned station is drawn in and not a claim that a
        /// Common is standing there; ask <see cref="StationStaffed"/> for that.</summary>
        public Foremen.Rarity StationRarity(int station)
        {
            int master = ActiveAt(station);
            return master < 0 ? Foremen.Rarity.Common : Foremen.RankOf(master);
        }

        public int LevelOf(int master) => Foremen.StarsOf(Stars, master);
        public bool IsHired(int master) => Foremen.IsHired(Stars, master);
        public bool IsMaxed(int master) => Foremen.IsMaxed(Stars, master);
        public int HiredCount => Foremen.HiredCount(Stars);

        public int DuplicatesOf(int master)
            => _data != null && _data.masterCards != null
               && master >= 0 && master < _data.masterCards.Length
                ? _data.masterCards[master] : 0;

        public int DuplicatesToLevel(int master)
            => Foremen.CardsToStar(master, LevelOf(master), _tuning);

        /// <summary>What a master is worth at one of his three skills, at the stars he carries now.</summary>
        public double SkillValue(int master, Foremen.Skill skill)
            => Foremen.SkillValue(master, LevelOf(master), skill, _tuning);

        /// <summary>The same skill one star further on, for the "what the next star buys" line.</summary>
        public double SkillValueAtStar(int master, int stars, Foremen.Skill skill)
            => Foremen.SkillValue(master, stars, skill, _tuning);

        /// <summary>Which rarity a master is — the colour of his card and of his plinth.</summary>
        public Foremen.Rarity RankOf(int master) => Foremen.RankOf(master);

        /// <summary>
        /// Shared roster-card state. The UI should not have to reconstruct ownership, upgrade
        /// readiness or progress from separate arrays; captains expose the same contract.
        /// </summary>
        public RosterCardState CardState(int master)
        {
            int stars = LevelOf(master);
            return new RosterCardState(
                master,
                Foremen.CardRarity(Foremen.RankOf(master)),
                Foremen.StationOf(master),
                stars,
                Foremen.MaxStars,
                DuplicatesOf(master),
                DuplicatesToLevel(master),
                Foremen.SkillValue(master, stars, Foremen.Skill.Throughput, _tuning),
                ActiveAt(Foremen.StationOf(master)) == master);
        }

        /// <summary>
        /// One rarity's colour. Lives here rather than on either screen because BOTH read it — the card
        /// frame in Game.UI and the plinth under his feet in Game.Gameplay — and the two must never
        /// disagree about what Legendary looks like.
        /// </summary>
        public UnityEngine.Color RarityTint(Foremen.Rarity rank)
        {
            int i = (int)rank;
            if (i < 0) i = 0;
            if (i >= _rarityTint.Length) i = _rarityTint.Length - 1;
            return _rarityTint[i];
        }

        public UnityEngine.Color RarityTintOf(int master) => RarityTint(Foremen.RankOf(master));

        /// <summary>The colour of the plinth at a station: whoever is posted there.</summary>
        public UnityEngine.Color StationTint(int station) => RarityTint(StationRarity(station));

        /// <summary>True when the player could add a star right now. Cards alone — gems buy chests.</summary>
        public bool CanLevel(int master)
        {
            if (!IsHired(master) || IsMaxed(master)) return false;
            return DuplicatesOf(master) >= DuplicatesToLevel(master);
        }

        /// <summary>Cards currently ready to star up; used by the roster opener badge.</summary>
        public int PendingCount()
        {
            int count = 0;
            for (int master = 0; master < Foremen.Count; master++)
                if (CardState(master).NeedsAttention) count++;
            return count;
        }

        // ----------------------------------------------------------------- posting
        /// <summary>
        /// Put a master to work at his own station, taking the post off whoever held it. Refused for a
        /// card nobody owns and for a master who does not belong to that station — the screen only ever
        /// offers the three that do, but a save is not a screen.
        /// </summary>
        public bool TrySetActive(int master)
        {
            if (_data == null || !Foremen.Exists(master)) return false;
            if (!IsHired(master)) return false;

            int station = Foremen.StationOf(master);
            if (_data.masterActive[station] == master) return false;

            _data.masterActive[station] = master;
            Recompute();
            RosterChanged?.Invoke(master);
            return true;
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
        /// after, so a half-paid open cannot exist; the returned masters are in reveal order and are
        /// what the ceremony flips. Null when it could not be paid for.
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
        /// cards at whoever is furthest behind and rolls the rest, then the whole batch lands in one
        /// go: fifteen separate RosterChanged events for a ten-chest open would rebuild the roster
        /// screen forty times while the reveal was still playing.
        ///
        /// The gems (or the free chest's claim stamp) and the cards are written in one save before the
        /// reveal starts. Left to the next autosave, a force-close during the reveal refunded the gems
        /// and let the chest be rolled again.
        /// </summary>
        private int[] Deal(int chests, int cardsPerChest)
        {
            if (cardsPerChest <= 0)
            {
                _save?.Save(_data);
                RosterChanged?.Invoke(-1);
                return new int[0];
            }

            int aimed = MasterChest.DirectedIn(_chest);
            var picks = new int[chests * cardsPerChest];
            int at = 0;

            for (int c = 0; c < chests; c++)
                for (int i = 0; i < cardsPerChest; i++)
                {
                    int master = i < aimed ? AimedMaster() : Roll();
                    Bank(master, 1);
                    picks[at++] = master;
                }

            FillEmptyPosts();
            Recompute();
            _save?.Save(_data);
            RosterChanged?.Invoke(-1);
            return picks;
        }

        private int Roll() => MasterChest.RollMaster(_random.NextDouble(), _random.NextDouble(), _chest);

        // ----------------------------------------------------------------- write
        /// <summary>
        /// Take a master up one star. Costs cards alone — the gems were spent at the chest, and
        /// charging twice for the same card is what made the old roster feel like a price list.
        /// </summary>
        public bool TryLevelUp(int master)
        {
            if (_data == null) return false;
            if (!Foremen.Exists(master)) return false;
            if (!IsHired(master) || IsMaxed(master)) return false;

            int stars = LevelOf(master);
            int needCards = Foremen.CardsToStar(master, stars, _tuning);
            if (DuplicatesOf(master) < needCards) return false;

            _data.masterCards[master] -= needCards;
            _data.masterStars[master] = stars + 1;
            Recompute();
            Levelled?.Invoke(master);
            RosterChanged?.Invoke(master);
            return true;
        }

        /// <summary>Award cards for a named master — a contract reward, a daily, an achievement.</summary>
        public void GrantDuplicates(int master, int count)
        {
            if (_data == null || count <= 0) return;
            if (!Foremen.Exists(master)) return;
            if (Bank(master, count)) FillEmptyPosts();
            Recompute();
            RosterChanged?.Invoke(master);
        }

        /// <summary>
        /// Puts cards on a master, standing him up if this is the first. Returns true when a master
        /// actually appeared, which is the caller's cue to fill an empty post.
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
        private bool Bank(int master, int count)
        {
            _data.masterCards[master] += count;
            if (_data.masterStars[master] > Foremen.NotHired) return false;
            _data.masterStars[master] = 1;
            return true;
        }

        /// <summary>
        /// The owned master who is furthest behind: fewest stars, then fewest cards, then lowest index.
        /// -1 when the roster is empty.
        ///
        /// OWNED ONLY, deliberately. An unowned master is trivially the furthest behind, so counting
        /// them would send every aimed card in the game at unlocking the next empty slot until there
        /// were none left — and finding a new master would stop being something a chest DOES and
        /// become a schedule. Meeting somebody new stays a roll; helping the laggard is the aim.
        ///
        /// It does NOT prefer the posted master over a benched one. The bench is the collection and the
        /// collection is the point; a rule that fed only the five in work would leave the other ten at
        /// one star forever and make the whole roster five cards wide.
        /// </summary>
        private int FurthestBehind()
        {
            int pick = -1;
            for (int m = 0; m < Foremen.Count; m++)
            {
                if (_data.masterStars[m] <= Foremen.NotHired) continue;
                if (pick < 0) { pick = m; continue; }

                if (_data.masterStars[m] < _data.masterStars[pick]
                    || (_data.masterStars[m] == _data.masterStars[pick]
                        && _data.masterCards[m] < _data.masterCards[pick]))
                    pick = m;
            }
            return pick;
        }

        /// <summary>Where an aimed card goes: the laggard, or a roll when nobody is owned yet.</summary>
        private int AimedMaster()
        {
            int pick = FurthestBehind();
            return pick >= 0 ? pick : Roll();
        }

        /// <summary>
        /// Award cards for a master picked at random, which is what a generic reward pays out. Rolled
        /// on the chest's own rarity weights rather than flat over the fifteen, so a contract reward
        /// and a chest card mean the same thing — a flat roll would make a free contract the best
        /// source of Legendaries in the game.
        ///
        /// Returns the master it landed on; contracts keep that to say who the card was for.
        /// </summary>
        public int GrantRandomDuplicates(int count)
        {
            if (_data == null || count <= 0) return -1;
            int pick = Roll();
            GrantDuplicates(pick, count);
            return pick;
        }

        /// <summary>
        /// Award cards to whoever is FURTHEST BEHIND — fewest stars, then fewest cards, then lowest
        /// index. This is what a purser aboard a voyage buys (<see cref="Game.Core.Captains.Purser"/>),
        /// and what one card in every chest does: the same cards, aimed instead of scattered.
        ///
        /// Aimed at the one furthest behind rather than at one the player nominates, because a
        /// nomination is a screen, a saved choice and a thing to forget to change, and the answer it
        /// would nearly always be set to is this one. Fifty to a hundred cards per master is a long
        /// enough road that a card landing where it is shortest is worth as much as an extra card, and
        /// it costs the balance nothing at all — the count is unchanged.
        /// </summary>
        public int GrantDirectedDuplicates(int count)
        {
            if (_data == null || count <= 0) return -1;
            int pick = AimedMaster();
            GrantDuplicates(pick, count);
            return pick;
        }
    }
}
