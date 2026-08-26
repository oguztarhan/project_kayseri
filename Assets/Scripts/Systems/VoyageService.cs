using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Owns the fleet: which berths are loading, which ships are at sea, and what is waiting on the
    /// dock to be taken. The maths is all in <see cref="Voyages"/>; this holds the state, moves the
    /// bars, and tells anyone who cares that something happened.
    ///
    /// This is the game's second destination for a bar. Everything a player produced used to go to
    /// the counter and become cash; a voyage is where a bar goes instead, and it comes back as foreman
    /// cards. <see cref="ForemanService.GrantRandomDuplicates"/> has been sitting in the codebase
    /// documented as "what a generic foreman crate pays out" with no crate attached to it. This is the
    /// crate.
    ///
    /// THREE THINGS THAT LOOK LIKE OVERSIGHTS AND ARE NOT:
    ///
    /// A HOLD DOES NOT FILL WHILE THE APP IS SHUT — but a ship at sea still comes home. Offline cash
    /// is granted from a persisted RATE, not by selling the bars on the pads, so a hold that filled
    /// during the absence would take bars the player was paid for anyway: the voyage would be free.
    /// Loading is therefore something that happens while the game is open, and the voyage itself is
    /// what runs while it is shut. That is also the half the player actually wants running overnight.
    ///
    /// THE DOCK REFUSES A YARD THAT HAS NEVER SHIPPED ANYTHING. A hold is measured in minutes of the
    /// island's delivery (rule 4), and a yard whose meter is still empty honestly reports zero — so
    /// opening a voyage there would lock in a hold of size zero. Same guard, same reason, as
    /// <see cref="MarketService.StockCapacity"/> reading 0 until its meter has something to say.
    ///
    /// NOTHING IS EVER TAKEN AWAY. A settled voyage sits on the dock indefinitely, a loading hold
    /// waits indefinitely, and abandoning a load puts every bar back on the pads. Rule 3.
    /// </summary>
    public sealed class VoyageService
    {
        private readonly SaveData _data;
        private readonly MarketService _market;
        private readonly ForemanService _foremen;
        private readonly CaptainService _captains;
        private readonly WalletService _wallet;
        private readonly TimeService _time;
        private readonly Voyages.Tuning _tuning;
        private readonly Random _random = new Random();

        /// <summary>Any move at all — a berth opened, a hold grew, a ship sailed, cards were taken.</summary>
        public event Action Changed;

        /// <summary>A voyage just came home and has cards on it. The berth index. For juice and badges.</summary>
        public event Action<int> Returned;

        public VoyageService(SaveData data, MarketService market, ForemanService foremen,
                             WalletService wallet, TimeService time, Voyages.Tuning tuning,
                             CaptainService captains = null)
        {
            _data = data;
            _market = market;
            _foremen = foremen;
            _captains = captains;
            _wallet = wallet;
            _time = time;
            _tuning = tuning;
            Normalise();
        }

        /// <summary>
        /// A save written before voyages existed arrives with a null list and a null array — JsonUtility
        /// gives null rather than the field initialiser. Padding here is what lets this ship without a
        /// save-version bump, and a bump wipes progress, so it is not something to spend on a feature
        /// that only adds fields. Same job, same reason, as <c>ForemanService.Normalise</c>.
        /// </summary>
        private void Normalise()
        {
            if (_data == null) return;
            if (_data.voyages == null) _data.voyages = new List<VoyageState>();
            if (_data.shipLevels == null || _data.shipLevels.Length != Voyages.ShipTrackCount)
            {
                var fitted = new int[Voyages.ShipTrackCount];
                if (_data.shipLevels != null)
                {
                    int n = Math.Min(_data.shipLevels.Length, Voyages.ShipTrackCount);
                    for (int i = 0; i < n; i++) fitted[i] = _data.shipLevels[i];
                }
                _data.shipLevels = fitted;
            }
            if (_data.hullReadyUnix == null || _data.hullReadyUnix.Length != Voyages.MaxBerths)
            {
                var fitted = new long[Voyages.MaxBerths];
                if (_data.hullReadyUnix != null)
                {
                    int n = Math.Min(_data.hullReadyUnix.Length, Voyages.MaxBerths);
                    for (int i = 0; i < n; i++) fitted[i] = _data.hullReadyUnix[i];
                }
                _data.hullReadyUnix = fitted;
            }
        }

        // ------------------------------------------------------------------ read
        public Voyages.Tuning Tuning => _tuning;

        private int ShipLevel(int track)
            => _data != null && _data.shipLevels != null && track >= 0 && track < _data.shipLevels.Length
                ? _data.shipLevels[track] : 0;

        /// <summary>How many ships the fleet can have out or loading at once.</summary>
        public int BerthCount => Voyages.BerthCount(ShipLevel(Voyages.Berths));

        /// <summary>Whatever is in this berth, or null if it is empty.</summary>
        public VoyageState At(int berth)
        {
            if (_data == null || _data.voyages == null) return null;
            for (int i = 0; i < _data.voyages.Count; i++)
                if (_data.voyages[i] != null && _data.voyages[i].berth == berth) return _data.voyages[i];
            return null;
        }

        /// <summary>The first berth with nothing in it, or -1 when the fleet is fully committed.</summary>
        public int FreeBerth()
        {
            int berths = BerthCount;
            for (int b = 0; b < berths; b++) if (At(b) == null) return b;
            return -1;
        }

        /// <summary>Voyages that have come home, won or lost. What opens the further routes.</summary>
        public int Completed => _data != null ? _data.voyagesCompleted : 0;

        /// <summary>The furthest route open to this fleet. A fresh account has only tier 0.</summary>
        public int MaxTier()
        {
            int best = 0;
            for (int t = 0; t < Voyages.TierCount; t++) if (Voyages.TierUnlocked(t, Completed)) best = t;
            return best;
        }

        public bool TierUnlocked(int tier) => Voyages.TierUnlocked(tier, Completed);

        /// <summary>Voyages still to sail before <paramref name="tier"/> opens. 0 once it has.</summary>
        public int VoyagesToUnlock(int tier)
        {
            if (tier < 0 || tier >= Voyages.TierCount) return 0;
            int need = Voyages.TierVoyagesRequired[tier] - Completed;
            return need < 0 ? 0 : need;
        }

        /// <summary>
        /// The odds this route comes home short, with <paramref name="foreman"/> aboard (-1 = nobody).
        /// The number the player is shown BEFORE committing — a gamble whose odds are hidden is not a
        /// decision, it is a surprise.
        /// </summary>
        public double RiskFor(int tier, int foreman)
            => Voyages.RiskFor(tier, ForemanLevel(foreman), _tuning);

        /// <summary>
        /// As above, with a captain aboard as well. A bosun's nerve STACKS with the foreman's rather
        /// than replacing it — see <see cref="Game.Core.Captains.RiskReduction"/> for why taking the
        /// better of the two would make one of the two officers pointless the moment the other was
        /// levelled.
        /// </summary>
        public double RiskFor(int tier, int foreman, int captain)
            => Voyages.RiskFor(tier, ForemanLevel(foreman), CaptainRisk(captain), _tuning);

        private double CaptainRisk(int captain)
            => _captains != null ? _captains.RiskReduction(captain) : 0d;

        private int ForemanLevel(int station)
            => _foremen != null && station >= 0 && station < Foremen.Count ? _foremen.LevelOf(station) : 0;

        /// <summary>True when this foreman is already at sea on another voyage.</summary>
        public bool ForemanBusy(int station)
        {
            if (station < 0 || _data == null || _data.voyages == null) return false;
            for (int i = 0; i < _data.voyages.Count; i++)
            {
                VoyageState v = _data.voyages[i];
                if (v != null && v.foreman == station && !v.settled) return true;
            }
            return false;
        }

        /// <summary>Seconds until a wrecked berth is seaworthy again. 0 when it already is.</summary>
        public long RepairSecondsLeft(int berth)
        {
            if (_data == null || _data.hullReadyUnix == null
                || berth < 0 || berth >= _data.hullReadyUnix.Length) return 0L;
            long left = _data.hullReadyUnix[berth] - (_time != null ? _time.NowUnix() : 0L);
            return left < 0L ? 0L : left;
        }

        public bool BerthDamaged(int berth) => RepairSecondsLeft(berth) > 0L;

        // ------------------------------------------------------------------ the shipyard
        public long Salvage => _data != null ? _data.salvage : 0L;

        public int LevelOf(int track) => ShipLevel(track);
        public int MaxLevelOf(int track) => Voyages.MaxLevelOf(track);
        public bool IsShipMaxed(int track) => ShipLevel(track) >= Voyages.MaxLevelOf(track);

        /// <summary>Salvage the next level of this track costs. 0 when it is bought with gems, or maxed.</summary>
        public long SalvageCostOf(int track)
        {
            if (IsShipMaxed(track)) return 0L;
            return track == Voyages.Berths
                ? Voyages.BerthSalvageCost(ShipLevel(Voyages.Berths), _tuning)
                : Voyages.ShipCost(ShipLevel(track), _tuning);
        }

        /// <summary>Gems the next level costs. Only ever the third and fourth berth.</summary>
        public long GemCostOf(int track)
        {
            if (IsShipMaxed(track) || track != Voyages.Berths) return 0L;
            return Voyages.BerthGemCost(ShipLevel(Voyages.Berths), _tuning);
        }

        public bool CanBuyShip(int track)
        {
            if (IsShipMaxed(track)) return false;
            long gems = GemCostOf(track);
            if (gems > 0L) return _wallet != null && _wallet.Gems >= gems;
            long salvage = SalvageCostOf(track);
            return salvage > 0L && Salvage >= salvage;
        }

        /// <summary>
        /// Buy the next level of a ship track. Salvage for Hold, Speed, Crew and the second berth;
        /// gems for the third and fourth. The split is deliberate — see Docs/VOYAGES.md §7: salvage is
        /// a closed loop that cannot disturb the cash economy, and berths past the second are the one
        /// place in this feature gems are asked for.
        /// </summary>
        public bool TryBuyShip(int track)
        {
            if (_data == null || track < 0 || track >= Voyages.ShipTrackCount) return false;
            if (IsShipMaxed(track)) return false;

            long gems = GemCostOf(track);
            if (gems > 0L)
            {
                if (_wallet == null || !_wallet.TrySpendGems(gems)) return false;
            }
            else
            {
                long salvage = SalvageCostOf(track);
                if (salvage <= 0L || _data.salvage < salvage) return false;
                _data.salvage -= salvage;
            }

            _data.shipLevels[track]++;
            Changed?.Invoke();
            return true;
        }

        // ------------------------------------------------------------- paid shortcuts
        /// <summary>
        /// Bring a ship home now. The rewarded-ad reward, and the caller is expected to have shown the
        /// ad already — this service does not know what an ad is, the same way nothing else in
        /// Game.Systems does.
        ///
        /// It does NOT settle the voyage on the spot. It pulls the arrival time back to now and lets
        /// the ordinary tick roll the risk, so a skipped wait and a served one resolve through exactly
        /// the same code and there is no second place for the odds to be decided.
        /// </summary>
        public bool TryFinishNow(int berth)
        {
            VoyageState v = At(berth);
            if (v == null || v.sailedUnix <= 0L || v.settled) return false;
            v.returnsUnix = _time != null ? _time.NowUnix() : 0L;
            Tick(0.0001f);              // settles it through the one path that knows how
            return true;
        }

        /// <summary>Put a wrecked berth right now, for gems.</summary>
        public bool TryRepairNow(int berth)
        {
            if (!BerthDamaged(berth)) return false;
            long gems = Math.Max(0L, _tuning.RepairSkipGems);
            if (gems > 0L && (_wallet == null || !_wallet.TrySpendGems(gems))) return false;
            _data.hullReadyUnix[berth] = 0L;
            Changed?.Invoke();
            return true;
        }

        public long RepairSkipGems => Math.Max(0L, _tuning.RepairSkipGems);

        // ------------------------------------------------------------------- the dock
        /// <summary>The berth of a hold still loading off this island, or -1. What the dock pad acts on.</summary>
        public int LoadingBerthOn(string islandKey)
        {
            if (_data == null || _data.voyages == null || string.IsNullOrEmpty(islandKey)) return -1;
            for (int i = 0; i < _data.voyages.Count; i++)
            {
                VoyageState v = _data.voyages[i];
                if (v != null && v.sailedUnix <= 0L && v.island == islandKey) return v.berth;
            }
            return -1;
        }

        /// <summary>The berth of a ship home from this island and waiting to be unloaded, or -1.</summary>
        public int SettledBerthOn(string islandKey)
        {
            if (_data == null || _data.voyages == null || string.IsNullOrEmpty(islandKey)) return -1;
            for (int i = 0; i < _data.voyages.Count; i++)
            {
                VoyageState v = _data.voyages[i];
                if (v != null && v.settled && v.island == islandKey) return v.berth;
            }
            return -1;
        }

        /// <summary>
        /// Bars carried to the dock on the player's own back, going straight into the hold.
        ///
        /// ON TOP of what the yard diverts by itself, never instead of it — the same relationship
        /// <see cref="MarketFlow"/> spells out between a staffed yard and the player standing in it:
        /// the automatic share is what the dock manages ON ITS OWN, and this is the pair of hands.
        /// Being there is worth something; not being there still works.
        ///
        /// Returns how many bars the hold actually took, which is what the caller may take off the
        /// player's back. A full hold takes none, and the carrier keeps them.
        /// </summary>
        public double DepositByHand(string islandKey, double bars)
        {
            if (bars <= 0d) return 0d;
            int berth = LoadingBerthOn(islandKey);
            if (berth < 0) return 0d;

            VoyageState v = At(berth);
            if (v == null || v.holdSize <= 0d) return 0d;

            double room = v.holdSize - v.held;
            if (room <= 0d) { Sail(v); Changed?.Invoke(); return 0d; }

            double taken = bars < room ? bars : room;
            v.held += taken;
            if (v.held >= v.holdSize) Sail(v);
            Changed?.Invoke();
            return taken;
        }

        /// <summary>How many holds are filling off this island right now — what they share the yard between.</summary>
        public int LoadingOn(string islandKey)
        {
            if (_data == null || _data.voyages == null || string.IsNullOrEmpty(islandKey)) return 0;
            int n = 0;
            for (int i = 0; i < _data.voyages.Count; i++)
            {
                VoyageState v = _data.voyages[i];
                if (v != null && v.sailedUnix <= 0L && v.island == islandKey) n++;
            }
            return n;
        }

        /// <summary>Bars this island's yard would put in a hold opened right now. 0 = the dock refuses.</summary>
        public double HoldSizeFor(string islandKey)
        {
            if (_market == null || string.IsNullOrEmpty(islandKey)) return 0d;
            MarketYard row = _market.Row(islandKey);
            double rate = row != null ? row.deliveredPerMin : 0d;
            return Voyages.HoldSize(rate, ShipLevel(Voyages.Hold), _tuning);
        }

        /// <summary>How full a berth's hold is, 0..1.</summary>
        public double HoldFraction(int berth)
        {
            VoyageState v = At(berth);
            if (v == null || v.holdSize <= 0d) return 0d;
            double f = v.held / v.holdSize;
            return f < 0d ? 0d : (f > 1d ? 1d : f);
        }

        /// <summary>Seconds until a sailed voyage is home. 0 once it is (or if it never sailed).</summary>
        public long SecondsLeft(int berth)
        {
            VoyageState v = At(berth);
            if (v == null || v.sailedUnix <= 0L || v.settled) return 0L;
            long left = v.returnsUnix - (_time != null ? _time.NowUnix() : 0L);
            return left < 0L ? 0L : left;
        }

        public bool IsLoading(int berth) { VoyageState v = At(berth); return v != null && v.sailedUnix <= 0L; }
        public bool IsAtSea(int berth)   { VoyageState v = At(berth); return v != null && v.sailedUnix > 0L && !v.settled; }
        public bool IsWaiting(int berth) { VoyageState v = At(berth); return v != null && v.settled; }

        /// <summary>True when a voyage could be opened on this island right now.</summary>
        public bool CanStart(string islandKey)
        {
            int berth = FreeBerth();
            return berth >= 0 && !BerthDamaged(berth) && HoldSizeFor(islandKey) > 0d;
        }

        // ----------------------------------------------------------------- write
        /// <summary>
        /// Claim a berth and start loading. The hold size is fixed here, not read live — see the note
        /// on <see cref="VoyageState.holdSize"/> for why a hold that grew under the player would be a
        /// progress bar that runs backwards after they buy something.
        /// </summary>
        public bool TryStart(string islandKey, int tier, int foreman = -1, int captain = -1)
        {
            if (_data == null || string.IsNullOrEmpty(islandKey)) return false;
            if (tier < 0 || tier >= Voyages.TierCount) return false;
            if (!TierUnlocked(tier)) return false;

            int berth = FreeBerth();
            if (berth < 0 || BerthDamaged(berth)) return false;

            double hold = HoldSizeFor(islandKey);
            if (hold <= 0d) return false;

            // An unhired slot is nobody: a level-0 foreman would cut no risk and would only make the
            // panel claim someone is aboard who does not exist yet.
            if (foreman >= 0 && (foreman >= Foremen.Count || ForemanLevel(foreman) <= Foremen.NotHired
                                 || ForemanBusy(foreman)))
                foreman = -1;

            // Same rule for the captain, for the same reason: somebody you have never pulled is
            // nobody, and somebody already at sea cannot be in two places.
            if (captain >= 0 && !CaptainAvailable(captain)) captain = -1;

            _data.voyages.Add(new VoyageState
            {
                island   = islandKey,
                berth    = berth,
                tier     = tier,
                held     = 0d,
                holdSize = hold,
                foreman  = foreman,
                captain  = captain,
            });
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Put a foreman aboard, or take them off with -1, while the ship is still at the dock. Refused
        /// once it has sailed — the crew list is fixed at the moment the risk becomes real, or the
        /// player could wait to see the outcome and then decide who was aboard for it.
        /// </summary>
        public bool TrySetForeman(int berth, int foreman)
        {
            VoyageState v = At(berth);
            if (v == null || v.sailedUnix > 0L) return false;
            if (foreman >= 0 && (foreman >= Foremen.Count || ForemanLevel(foreman) <= Foremen.NotHired
                                 || ForemanBusy(foreman)))
                return false;
            v.foreman = foreman < 0 ? -1 : foreman;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Whether this captain could be put aboard right now: pulled, and not already at sea.</summary>
        public bool CaptainAvailable(int captain)
            => _captains != null && _captains.CanSail(captain) && !_captains.Busy(captain);

        /// <summary>
        /// Put a captain aboard, or take them off with -1. Fixed at sailing like the foreman is, and
        /// for the same reason — a crew list settled after the outcome is not a decision.
        /// </summary>
        public bool TrySetCaptain(int berth, int captain)
        {
            VoyageState v = At(berth);
            if (v == null || v.sailedUnix > 0L) return false;
            if (captain >= 0 && !CaptainAvailable(captain)) return false;
            v.captain = captain < 0 ? -1 : captain;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Send a partly loaded ship out now. The payout scales with what is actually aboard, so this
        /// is a choice rather than a penalty — but not below <see cref="Voyages.Tuning.MinLaunchFraction"/>,
        /// because a near-empty hold that still costs a full voyage's wait is a trap and the dock
        /// should refuse rather than let the player walk into one.
        /// </summary>
        public bool TrySail(int berth)
        {
            VoyageState v = At(berth);
            if (v == null || v.sailedUnix > 0L) return false;
            if (v.holdSize <= 0d) return false;
            if (v.held / v.holdSize < _tuning.MinLaunchFraction) return false;
            Sail(v);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Give up on a load. Every bar goes back on the pads — this is not a sunk cost, and a player
        /// who changes their mind about a route should not be punished for it (rule 3's spirit).
        /// Refuses once the ship has sailed: it is over the horizon and no longer anybody's to recall.
        /// </summary>
        public bool TryAbandon(int berth)
        {
            VoyageState v = At(berth);
            if (v == null || v.sailedUnix > 0L) return false;
            if (v.held > 0d && _market != null) _market.ReturnToStock(v.island, v.held);
            _data.voyages.Remove(v);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Take the cards off the dock and free the berth. Returns how many cards were granted, or 0
        /// when there was nothing to take.
        /// </summary>
        public int TryClaim(int berth)
        {
            VoyageState v = At(berth);
            if (v == null || !v.settled) return 0;

            int cards = v.payoutCards;
            if (cards > 0 && _foremen != null)
            {
                // A purser aboard aims a share of the cards at whichever foreman is furthest behind;
                // the rest scatter as they always have. The COUNT is untouched either way — this
                // moves where a card lands, never how many arrive, which is why it needed no balance
                // pass at all (Game.Core.Captains.DirectedShare).
                double share = _captains != null ? _captains.DirectedShare(v.captain) : 0d;
                int aimed = share > 0d ? (int)Math.Round(cards * share, MidpointRounding.AwayFromZero) : 0;

                // A purser aboard ALWAYS places at least one card. Without this floor the rounding
                // eats them whole on the short routes: a tier-0 voyage pays one card, a Common
                // purser's share of it is 0.4, and 0.4 rounds to nothing — so the role did exactly
                // nothing on the only route a new player has open. A role that is inert precisely
                // where it is first met reads as broken, and the fix costs the balance nothing
                // because the count is not what is moving.
                if (share > 0d && aimed < 1) aimed = 1;
                if (aimed > cards) aimed = cards;
                if (aimed > 0) _foremen.GrantDirectedDuplicates(aimed);
                if (cards - aimed > 0) _foremen.GrantRandomDuplicates(cards - aimed);
            }
            if (v.payoutSalvage > 0 && _data != null) _data.salvage += v.payoutSalvage;
            if (v.payoutCharts > 0 && _captains != null) _captains.AddCharts(v.payoutCharts);

            _data.voyages.Remove(v);
            Changed?.Invoke();
            return cards;
        }

        // ------------------------------------------------------------------ tick
        /// <summary>
        /// Driven from GameBootstrap.Update, after <see cref="MarketService.Tick"/> — the yard has to
        /// take its deliveries before the dock is allowed to pull from the pads.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_data == null || _data.voyages == null || deltaTime <= 0f) return;

            bool moved = false;
            long now = _time != null ? _time.NowUnix() : 0L;

            // Reverse, because settling can leave the list alone but loading can sail a voyage and a
            // forward index over a mutating list is the kind of bug that only shows up with two berths.
            for (int i = _data.voyages.Count - 1; i >= 0; i--)
            {
                VoyageState v = _data.voyages[i];
                if (v == null) { _data.voyages.RemoveAt(i); moved = true; continue; }

                if (v.settled) continue;

                if (v.sailedUnix <= 0L)
                {
                    if (Load(v, deltaTime)) moved = true;
                    continue;
                }

                if (now >= v.returnsUnix)
                {
                    Settle(v);
                    moved = true;
                    Returned?.Invoke(v.berth);
                }
            }

            if (moved) Changed?.Invoke();
        }

        /// <summary>
        /// Divert a tick's worth of the island's delivery into the hold. Takes from the pads rather
        /// than intercepting deliveries: it is the same bars either way, and a pull needs no changes
        /// inside MarketService's tick — which is the one path in this game every cash figure runs
        /// through. A starved yard simply loads slower, which is the honest behaviour.
        /// </summary>
        private bool Load(VoyageState v, float deltaTime)
        {
            if (_market == null || v.holdSize <= 0d) return false;

            MarketYard row = _market.Row(v.island);
            double rate = row != null ? row.deliveredPerMin : 0d;
            // Every hold filling off THIS island shares one capped budget — see Voyages.DivertShareEach.
            // Without it four berths would take more than the island makes and the counter would sell
            // nothing, so buying a berth would read as switching the game off.
            double want = Voyages.FillPerSecond(rate, LoadingOn(v.island), _tuning) * deltaTime;
            if (want <= 0d) return false;

            double room = v.holdSize - v.held;
            if (room <= 0d) { Sail(v); return true; }
            if (want > room) want = room;

            double taken = _market.TakeFromStock(v.island, want);
            if (taken <= 0d) return false;

            v.held += taken;
            if (v.held >= v.holdSize) Sail(v);
            return true;
        }

        private void Sail(VoyageState v)
        {
            long now = _time != null ? _time.NowUnix() : 0L;
            v.sailedUnix = now;
            v.returnsUnix = now + (long)Math.Round(
                Voyages.VoyageSeconds(v.tier, ShipLevel(Voyages.Speed), _tuning));
        }

        /// <summary>
        /// The ship is home. Roll the risk, work out the payout, and leave it on the dock — claiming is
        /// the player's move, not ours, because a reward that banks itself while nobody is looking is a
        /// reward nobody ever sees.
        ///
        /// Tier 0 carries no risk at all (<see cref="Voyages.RiskChance"/>), so a fleet that has never
        /// sailed further always succeeds. The roll is written generically because the rule is "the tier
        /// decides" — and whoever is aboard, which is the other half of it.
        ///
        /// A failure still pays, and the berth takes the real punishment: it is out of use for a repair
        /// window. That split is deliberate. Losing the cards outright would make the far routes a coin
        /// flip nobody sane takes; losing the BERTH costs the thing the player was actually spending,
        /// which is time, and it is a cost they can see coming and plan around.
        /// </summary>
        /// <summary>
        /// Applies an officer's multiplier to a payout that has already been rounded to a whole
        /// number. Never rounds a positive payout down to nothing: a voyage that sailed pays
        /// something, which is the floor Voyages.Cards and Voyages.Salvage already set for themselves.
        /// </summary>
        private static int Scale(int amount, double multiplier)
        {
            if (amount <= 0) return amount;
            if (multiplier <= 1d) return amount;
            int scaled = (int)Math.Round(amount * multiplier, MidpointRounding.AwayFromZero);
            return scaled < amount ? amount : scaled;
        }

        private void Settle(VoyageState v)
        {
            double risk = Voyages.RiskFor(v.tier, ForemanLevel(v.foreman), CaptainRisk(v.captain), _tuning);
            v.succeeded = risk <= 0d || _random.NextDouble() >= risk;

            double loaded = v.holdSize > 0d ? v.held / v.holdSize : 0d;
            int crew = ShipLevel(Voyages.Crew);
            int hold = ShipLevel(Voyages.Hold);

            // THE CAPTAIN IS APPLIED HERE, NOT INSIDE Voyages. Docs/VOYAGES.md §21 records that the
            // first defaults were wrong by 2.5x and names the cause: "a multiplicative stack — tier
            // payout x hold x crew — where each factor was defensible alone and the product was not."
            // The cards keep exactly that stack and gain no fourth factor. A captain moves the two
            // closed-loop currencies and the repair window instead, none of which is in that solve.
            v.payoutCards = v.succeeded
                ? Voyages.Cards(v.tier, loaded, hold, crew, _tuning)
                : Voyages.CardsOnFailure(v.tier, loaded, hold, crew, _tuning);

            v.payoutSalvage = Scale(Voyages.Salvage(v.tier, loaded, hold, v.succeeded, _tuning),
                                    _captains != null ? _captains.SalvageMultiplier(v.captain) : 1d);
            v.payoutCharts  = Scale(Voyages.Charts(v.tier, loaded, hold, v.succeeded, _tuning),
                                    _captains != null ? _captains.ChartMultiplier(v.captain) : 1d);
            v.settled = true;

            // The voyage happened either way, so it counts toward the routes ahead. A ladder that only
            // advanced on wins would punish the player twice for one bad roll.
            if (_data != null) _data.voyagesCompleted++;

            if (!v.succeeded && _data != null && _data.hullReadyUnix != null
                && v.berth >= 0 && v.berth < _data.hullReadyUnix.Length)
            {
                long now = _time != null ? _time.NowUnix() : 0L;
                double repair = Voyages.RepairSeconds(v.tier, ShipLevel(Voyages.Speed), _tuning);
                if (_captains != null) repair *= _captains.RepairMultiplier(v.captain);
                _data.hullReadyUnix[v.berth] = now + (long)Math.Round(repair);
            }
        }
    }
}
