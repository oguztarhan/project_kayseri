using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The market yards, as a ledger. One row per island: what has been delivered to it, what it has
    /// sold, and what that was worth.
    ///
    /// This is now the ONLY place cash enters the game. It used to be
    /// <c>CoalOperation.TruckTick</c> — a cargo truck reaching the market building paid the wallet on
    /// the spot — which made the market a prop and made "go and work in your market" impossible to
    /// express: there was nothing left for the player to do that the truck had not already done.
    /// So the truck delivers and the yard sells, and everything about how fast the yard sells lives
    /// in <see cref="MarketFlow"/> where it can be measured instead of played.
    ///
    /// ONE ISLAND SIMULATES AT A TIME, and the yards have to keep working for the other seven. The
    /// trick is the same one <see cref="WorldIslands"/> has always used, one level deeper: while an
    /// island is live its trucks call <see cref="Deliver"/> for real, and this measures the rate they
    /// manage. The moment you sail away, that measured rate becomes the island's delivery rate on
    /// paper and its yard keeps filling from a number instead of from lorries. Nothing about the yard
    /// cares which of the two it is being fed by.
    ///
    /// It also owns the $/min meter that used to live on each island, for the same reason: the meter
    /// measures money, money is made here, and two copies of it would disagree the first time a yard
    /// was short-staffed.
    /// </summary>
    public sealed class MarketService
    {
        /// <summary>How often the ledger settles. This is bookkeeping, not animation.</summary>
        private const float TickSeconds = 1f;

        /// <summary>
        /// How long the trailing windows are, in seconds. Matched to the island meter this replaces so
        /// the number on the top bar keeps behaving the way players have already learned it does.
        /// </summary>
        private const int WindowSeconds = 60;

        /// <summary>
        /// How full a window has to be before its rate is worth persisting. A yard that has been
        /// running for three seconds has measured a lump, not a rate, and banking that as the island's
        /// offline income would pay out a spike for the next eight hours.
        /// </summary>
        private const int MinTrustedSeconds = 15;

        private sealed class Yard
        {
            public MarketYard save;
            public IIslandSaleTerms terms;

            // delivery meter — bars per minute arriving from the island, on an unboosted clock
            public readonly double[] deliverBuckets = new double[WindowSeconds];
            public int deliverIndex, deliverFilled;
            public double deliverTrailing, deliveredThisTick;

            // income meter — cash per minute leaving the counter, boost excluded
            public readonly double[] earnBuckets = new double[WindowSeconds];
            public int earnIndex, earnFilled;
            public double earnTrailing, earnedThisTick;
            public double cashPerMin;

            public double overflowSeconds;   // how long the pads have been full — the "come and help" signal
            public bool overflowedThisTick;  // set by Deliver, which is the only place a live yard spills

            public readonly int[] hires = new int[MarketFlow.JobCount];   // scratch, refilled from the save row
        }

        private readonly SaveData _data;
        private readonly WalletService _wallet;
        private readonly BoostService _boost;
        private readonly MaintenanceService _maintenance;   // null in tests: everything reads as new
        private readonly ForemanService _foremen;           // null in tests: an empty roster is x1
        private readonly GoalService _goals;                // null in tests: nothing is counted

        private readonly Dictionary<string, Yard> _yards = new Dictionary<string, Yard>();
        private readonly List<Yard> _order = new List<Yard>();   // stable iteration without allocating

        private string _activeIsland;    // the one whose trucks are really driving; null in the market scene
        private string _simulatedYard;   // the one being acted out on screen; null when nobody is in the hall
        private float _accum;
        private double _legacyIncomeMult = 1d, _boostMult = 1d, _permanentSpeed = 1d, _simSpeed = 1d;
        private double _foremanMult = 1d;

        // A yard sells a fraction of a bar per tick, so casting per call would floor to zero forever
        // and the bar counter would never move. The remainder carries between ticks instead.
        private double _barsCounted;

        /// <summary>Frozen compensation for investors earned before prestige was retired.</summary>
        public double LegacyIncomeMultiplier => _data != null && _data.legacyIncomeMultiplier > 1d
            ? _data.legacyIncomeMultiplier
            : 1d;

        /// <summary>
        /// Raised when a yard actually sells: which island, and what the wallet was paid. The floating
        /// cash label hangs off this. It is an event rather than a call because the simulation and the
        /// scene must not have to know each other — whichever of the two is on screen listens.
        /// </summary>
        public event Action<string, double> Sold;

        public MarketService(SaveData data, WalletService wallet, BoostService boost,
                             MaintenanceService maintenance = null, ForemanService foremen = null,
                             GoalService goals = null)
        {
            _data = data;
            _wallet = wallet;
            _boost = boost;
            _maintenance = maintenance;
            _foremen = foremen;
            _goals = goals;
        }

        // ------------------------------------------------------------------ wiring
        /// <summary>An island hands over its price list. Called once, as the island loads.</summary>
        public void Register(string islandKey, IIslandSaleTerms terms)
        {
            if (string.IsNullOrEmpty(islandKey)) return;
            Get(islandKey).terms = terms;
        }

        /// <summary>
        /// Which island is being simulated for real. Its yard is fed by its trucks; every other yard is
        /// fed by the rate its trucks last managed. Pass null when nobody is on an island at all — which
        /// is what standing in the market scene means.
        /// </summary>
        public void SetActiveIsland(string islandKey) => _activeIsland = islandKey;

        /// <summary>
        /// The island being simulated, or the last one that was. The market scene reads this to know
        /// whose yard it just opened: the island scene is gone by then, but this object outlives it.
        /// </summary>
        public string ActiveIsland => _activeIsland;

        /// <summary>
        /// How fast the island being simulated should run its clock. Permanent station speed and any
        /// temporary boost are combined here so trains, lorries and stations visibly run at that speed.
        ///
        /// A ×2 that only ever landed on the price was invisible exactly where the player was looking:
        /// the trains kept their pace, the lorries kept theirs, and a boosted island was indistinguishable
        /// from an unboosted one except for a number on the top bar. So on the island being simulated the
        /// boost is spent on TIME rather than on price — the whole chain runs at ×2, delivers twice the
        /// bars, and earns the same ×2 it always did, except now it can be watched.
        ///
        /// Only that island. Every other yard is fed by a rate rather than by lorries, and a number has no
        /// clock to speed up, so those keep taking their boost on the price. <see cref="Earn"/> applies
        /// exactly one of the two, and this is what tells it which.
        ///
        /// Latched once a second alongside the multipliers rather than read live, so the speed the island
        /// ticked at and the speed <see cref="Earn"/> divides back out can never disagree mid-second — the
        /// bars from a boost that expired half a second ago still have to be priced as boosted bars.
        /// </summary>
        public double IslandTimeScale => _simSpeed;

        /// <summary>
        /// The yard the player is standing in, whose sales are being acted out rather than calculated.
        ///
        /// This is the "hands off" flag, and it is what stops the market being paid twice. Everywhere
        /// else, a yard's throughput is <see cref="MarketFlow.ServiceRate"/> settled once a second. In
        /// the yard on screen there are real bars going from a real pad onto a real counter, real
        /// customers taking them and real notes landing on the floor — so this ledger stops selling for
        /// it and lets what the player can see be the truth. The meters keep running either way, off
        /// whichever of the two made the money.
        ///
        /// Pass null on the way out, or the yard the player last visited will stay frozen for good.
        /// </summary>
        public void SetSimulatedYard(string islandKey) => _simulatedYard = islandKey;

        /// <summary>
        /// Lifts bars off a yard's pads and into whoever asked for them — the player's back, or a hire's.
        /// Returns how many were actually there to take, which is what the caller may carry.
        /// </summary>
        public double TakeFromStock(string islandKey, double bars)
        {
            if (bars <= 0d) return 0d;
            Yard y = Get(islandKey);
            double taken = y.save.stock < bars ? y.save.stock : bars;
            if (taken <= 0d) return 0d;
            y.save.stock -= taken;
            return taken;
        }

        /// <summary>
        /// A sale made by hand at the counter: a customer took a bar and paid for it.
        ///
        /// Returns the cash rather than banking it, because the money it made is now lying on the yard
        /// floor and is not the player's until they walk over it. <see cref="Collect"/> is the other
        /// half. The ceiling, the meter and the boost are all applied here, at the moment the value was
        /// actually created — a note nobody picks up was still earned.
        /// </summary>
        public double SellByHand(string islandKey, double bars)
        {
            if (bars <= 0d) return 0d;
            return Earn(Get(islandKey), bars);
        }

        /// <summary>Money picked up off the floor. The only thing that puts a hand-made sale in the wallet.</summary>
        public void Collect(string islandKey, double cash)
        {
            if (cash <= 0d || _wallet == null) return;
            _wallet.AddCash(new BigDouble(cash));
            if (Sold != null) Sold(islandKey, cash);
        }

        /// <summary>A cargo truck tipping its load onto the pads. The island's only remaining say in income.</summary>
        public void Deliver(string islandKey, double bars)
        {
            if (bars <= 0d || string.IsNullOrEmpty(islandKey)) return;
            Yard y = Get(islandKey);
            // The pads get the real load; the METER gets it clean, the same way deliveredPerMin is
            // stored clean of wear. A boosted island is running its clock at ×2, so twice the lorries
            // arrive per real second — and none of that is a rate it can sustain once the ad expires.
            // Divided out here rather than at the meter because a boost starts and stops abruptly:
            // scaling a whole 60-second window by whatever the speed happened to be at the end of it
            // would leave the saved rate reading up to double for a minute after every boost, and that
            // rate is what the next launch's offline grant is paid from.
            y.deliveredThisTick += bars / SpeedFor(y);

            double supply = SupplyPerSecond(y);
            double capacity = MarketFlow.StockCapacity(supply, y.save.depositSlots);
            // Capacity is measured against the DELIVERY RATE, so a yard whose meter has not filled yet
            // has no capacity to speak of. Letting the first deliveries through uncapped is the honest
            // reading: the pads are not full, the game just does not know how big they are yet.
            if (capacity <= 0d) { y.save.stock += bars; return; }

            double overflow;
            y.save.stock = MarketFlow.AddStock(y.save.stock, bars, capacity, out overflow);
            if (overflow > 0d) y.overflowedThisTick = true;
        }

        // ------------------------------------------------------------------ reading
        /// <summary>What an island earns per minute right now — the figure the top bar and the map show.</summary>
        public double RatePerMin(string islandKey)
        {
            Yard y;
            return _yards.TryGetValue(islandKey ?? string.Empty, out y)
                ? TrustedRate(y)
                : SavedRate(islandKey);
        }

        /// <summary>
        /// A yard's rate, or the last one it persisted while its window is still filling.
        ///
        /// This guard is not decoration. For the first <see cref="MinTrustedSeconds"/> of every launch
        /// no yard has sold anything, so every live meter honestly reads zero — and a zero here goes
        /// straight into <see cref="SaveData.incomeRatePerSec"/>, which is the ONLY thing the next
        /// launch's offline grant is computed from. Without it, quitting inside the first quarter
        /// minute saves an empire that earns nothing and the player wakes up to no offline income at
        /// all. The island meter this replaced guarded exactly this, and the guard has to move with it.
        /// </summary>
        private double TrustedRate(Yard y)
            => y.earnFilled >= MinTrustedSeconds ? y.cashPerMin : SavedRate(y.save.id);

        /// <summary>Bars sitting on a yard's pads.</summary>
        public double Stock(string islandKey) => Get(islandKey).save.stock;

        /// <summary>
        /// What the pads can hold, in bars. Zero until the delivery meter has something to say — capacity
        /// is measured in minutes of what the island sends, and on a fresh yard nothing has been sent yet.
        /// </summary>
        public double StockCapacity(string islandKey)
        {
            Yard y = Get(islandKey);
            return MarketFlow.StockCapacity(SupplyPerSecond(y), y.save.depositSlots);
        }

        /// <summary>How full the pads are, 0..1. Reads 0 until the delivery meter has something to say.</summary>
        public double StockFraction(string islandKey)
        {
            double capacity = StockCapacity(islandKey);
            return capacity > 0d ? Get(islandKey).save.stock / capacity : 0d;
        }

        /// <summary>The yard's throughput, 0..1 — the number the whole design turns on.</summary>
        public double ServiceRate(string islandKey)
        {
            Yard y = Get(islandKey);
            return MarketFlow.ServiceRate(HireLevels(y));
        }

        /// <summary>True once the yard runs itself and the player never has to come back to it.</summary>
        public bool IsMaxed(string islandKey) => MarketFlow.IsMaxed(HireLevels(Get(islandKey)));

        /// <summary>How long this yard's pads have been full, in seconds. Zero while it is coping.</summary>
        public double OverflowSeconds(string islandKey) => Get(islandKey).overflowSeconds;

        /// <summary>The save row, for the yard scene and the upgrade pads to read and write.</summary>
        public MarketYard Row(string islandKey) => Get(islandKey).save;

        // ------------------------------------------------------------------ upgrades
        /// <summary>What the player has bought on one track.</summary>
        public int Level(string islandKey, YardUpgrade kind)
        {
            MarketYard row = Get(islandKey).save;
            switch (kind)
            {
                case YardUpgrade.DepositSlot: return row.depositSlots < 1 ? 1 : row.depositSlots;
                case YardUpgrade.QueueSlot: return row.queueSlots < 1 ? 1 : row.queueSlots;
                case YardUpgrade.HireCarry: return row.hireCarry;
                case YardUpgrade.HireServe: return row.hireServe;
                case YardUpgrade.HireCollect: return row.hireCollect;
                // One body, one back — the carry upgrade is the player's, not the yard's, so it lives
                // outside the per-island row and every yard sees the same stack.
                case YardUpgrade.CarryCapacity: return _data != null ? _data.marketCarryLevel : 0;
                default: return 0;
            }
        }

        /// <summary>The price of the next step, or 0 when the track is finished.</summary>
        public double Cost(string islandKey, YardUpgrade kind)
        {
            Yard y = Get(islandKey);
            if (y.terms == null) return 0d;
            return MarketPrices.Cost(kind, Level(islandKey, kind),
                                     y.terms.IncomeCapPerMinuteRaw * _legacyIncomeMult);
        }

        /// <summary>True when this track has nothing left to sell.</summary>
        public bool IsTrackMaxed(string islandKey, YardUpgrade kind)
            => MarketPrices.IsMaxed(kind, Level(islandKey, kind));

        /// <summary>
        /// Buys one step, if the track has one left and the wallet covers it. Returns false rather than
        /// throwing on either — a pad is stood on continuously, so "no" is the common answer, not an error.
        /// </summary>
        public bool TryBuy(string islandKey, YardUpgrade kind)
        {
            if (_wallet == null || _data == null) return false;
            if (IsTrackMaxed(islandKey, kind)) return false;

            double cost = Cost(islandKey, kind);
            if (cost <= 0d) return false;                      // no ceiling measured yet: nothing is for sale
            if (!_wallet.TrySpendCash(new BigDouble(cost))) return false;

            MarketYard row = Get(islandKey).save;
            switch (kind)
            {
                case YardUpgrade.DepositSlot: row.depositSlots = Level(islandKey, kind) + 1; break;
                case YardUpgrade.QueueSlot: row.queueSlots = Level(islandKey, kind) + 1; break;
                case YardUpgrade.HireCarry: row.hireCarry++; break;
                case YardUpgrade.HireServe: row.hireServe++; break;
                case YardUpgrade.HireCollect: row.hireCollect++; break;
                case YardUpgrade.CarryCapacity: _data.marketCarryLevel++; break;
            }
            return true;
        }

        // ------------------------------------------------------------------ the tick
        /// <summary>
        /// Settles every owned yard once a second: what arrived, what sold, what it paid.
        /// Driven from <see cref="GameBootstrap"/> so it keeps running across a scene load.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_data == null || _wallet == null) return;
            _accum += deltaTime;
            if (_accum < TickSeconds) return;
            double seconds = _accum;
            _accum = 0f;

            // Once a second, off the sale path — the same cadence the island meter used.
            _legacyIncomeMult = LegacyIncomeMultiplier;
            _boostMult = _boost != null ? _boost.ActiveMultiplier : 1d;
            _permanentSpeed = _boost != null ? _boost.PermanentMultiplier : 1d;
            // The roster lifts the ceiling as well as the payout, which is the whole reason it enters
            // here rather than only speeding the stations up. Every island's income is capped, so a
            // bonus that moved throughput alone would do nothing at all for the player who has been
            // playing long enough to own foremen — see Game.Core.Foremen.
            _foremanMult = _foremen != null ? _foremen.IncomeMultiplier : 1d;
            // Spent on the live island's clock when there is one running, and on price otherwise. The
            // guard is what stops a boost going nowhere while the player stands in a market hall: no
            // island is simulating there, so there is nothing to speed up and the price keeps it.
            _simSpeed = string.IsNullOrEmpty(_activeIsland) ? 1d : _permanentSpeed * _boostMult;

            double totalPerMin = 0d;
            for (int i = 0; i < _order.Count; i++)
            {
                Yard y = _order[i];
                if (!IsOwned(y.save.id)) continue;
                SettleYard(y, seconds);
                totalPerMin += TrustedRate(y);
            }

            // Offline earnings are granted on the next launch from this figure, and they cover the whole
            // empire — while the app is shut, no yard is being worked, but none of them are switched off
            // either. It is the one number the notification, the welcome-back screen and the grant share.
            _data.incomeRatePerSec = totalPerMin / 60d;
        }

        private void SettleYard(Yard y, double seconds)
        {
            bool live = y.save.id == _activeIsland;

            // ---- what arrived -------------------------------------------------------------------
            // A live island's trucks already called Deliver. Everywhere else the yard is fed by the
            // rate those trucks last managed, which is what keeps seven islands earning at once.
            double supply = SupplyPerSecond(y);
            if (live)
            {
                // The trucks already filled the pads; all that is left is to notice whether they spilled.
                y.overflowSeconds = y.overflowedThisTick ? y.overflowSeconds + seconds : 0d;
                y.overflowedThisTick = false;
            }
            else if (supply > 0d)
            {
                double capacity = MarketFlow.StockCapacity(supply, y.save.depositSlots);
                double overflow;
                y.save.stock = MarketFlow.AddStock(y.save.stock, supply * seconds, capacity, out overflow);
                y.overflowSeconds = overflow > 0d ? y.overflowSeconds + seconds : 0d;
            }

            // ---- what sold ----------------------------------------------------------------------
            // Not this one, if the player is standing in it: its bars are being carried to its counter
            // by hands you can watch, and selling them here as well would pay for the load twice.
            if (y.save.id != _simulatedYard)
            {
                double rate = MarketFlow.ServiceRate(HireLevels(y));
                double sellCapacity = MarketFlow.SellCapacityPerSecond(supply, y.save.queueSlots);
                double sold = MarketFlow.SoldInTick(y.save.stock, sellCapacity, rate, seconds);
                if (sold > 0d)
                {
                    y.save.stock -= sold;
                    Pay(y, sold);
                }
            }

            // ---- meters -------------------------------------------------------------------------
            AdvanceDeliveryMeter(y);
            AdvanceIncomeMeter(y);
        }

        /// <summary>
        /// Turns bars into money, through the same ceiling the island sale used to pass through, and
        /// returns what they were worth. Banking it is the caller's business — a hire's sale goes
        /// straight to the wallet, a counter sale lands on the floor first.
        ///
        /// The order matters and is inherited deliberately: the CAP is measured against the un-boosted
        /// rate, and the rewarded-ad boost multiplies whatever got through afterwards. A ×2 ad is then
        /// worth ×2, instead of being quietly eaten by a ceiling it was never meant to push against.
        /// </summary>
        private double Earn(Yard y, double bars)
        {
            if (y.terms == null) return 0d;

            // The live island has both multipliers in its clock, so divide the delivered bars back to a
            // clean base first. Permanent speed then belongs in the saved income rate; the temporary
            // boost is put back only in the payout and can never leak into the next offline session.
            double speed = SpeedFor(y);

            // Everything from here to the return is in CLEAN money — what this yard would be making on an
            // unboosted clock — because that is the denomination both the ceiling and the meter need. It
            // is what keeps the paragraph above true whichever way the boost was spent: the cap bites at
            // the same place either way, and the income meter (which feeds SaveData.incomeRatePerSec, and
            // through it the NEXT session's offline grant) never banks a rate that only existed while an
            // ad was running.
            double sale = bars * y.terms.BarPriceRaw * _legacyIncomeMult / speed * _permanentSpeed * _foremanMult;
            if (sale <= 0d) return 0d;

            double cap = y.terms.IncomeCapPerMinuteRaw * _legacyIncomeMult * _permanentSpeed * _foremanMult;
            double headroom = cap - (y.earnTrailing + y.earnedThisTick);
            if (sale > headroom) sale = headroom > 0d ? headroom : 0d;
            if (sale <= 0d) return 0d;

            y.earnedThisTick += sale;
            return sale * _boostMult;
        }

        /// <summary>What the hires sold this tick, banked on the spot — a hired collector picks it up.</summary>
        private void Pay(Yard y, double bars)
        {
            double paid = Earn(y, bars);
            if (paid <= 0d) return;
            _wallet.AddCash(new BigDouble(paid));

            if (_goals != null && bars > 0d)
            {
                _barsCounted += bars;
                if (_barsCounted >= 1d)
                {
                    long whole = (long)_barsCounted;
                    _barsCounted -= whole;
                    _goals.Record(Game.Core.Goals.BarsSold, whole);
                }
            }

            if (Sold != null) Sold(y.save.id, paid);
        }

        // ------------------------------------------------------------------ offline
        /// <summary>
        /// Advances the pads for time the app was closed. Cash is NOT paid here — the welcome-back
        /// grant already paid it, off the same rate this ledger persisted. What is missing after a long
        /// absence is the physical consequence: an unstaffed yard should be found buried in stock, and
        /// a maxed one should be found clear. Without this every yard would read empty on every launch,
        /// which is the one state that would tell the player their market does not matter.
        /// </summary>
        public void SettleOffline(long elapsedSeconds)
        {
            if (elapsedSeconds <= 0L || _data == null) return;
            for (int i = 0; i < _order.Count; i++)
            {
                Yard y = _order[i];
                if (!IsOwned(y.save.id)) continue;
                double supply = SupplyPerSecond(y);
                if (supply <= 0d) continue;

                double capacity = MarketFlow.StockCapacity(supply, y.save.depositSlots);
                double sellCapacity = MarketFlow.SellCapacityPerSecond(supply, y.save.queueSlots);
                double rate = MarketFlow.ServiceRate(HireLevels(y));
                double net = (supply - sellCapacity * rate) * elapsedSeconds;

                double stock = y.save.stock + net;
                if (stock < 0d) stock = 0d;
                if (capacity > 0d && stock > capacity) stock = capacity;
                y.save.stock = stock;
            }
        }

        // ------------------------------------------------------------------ meters
        private void AdvanceDeliveryMeter(Yard y)
        {
            y.deliverTrailing += y.deliveredThisTick - y.deliverBuckets[y.deliverIndex];
            y.deliverBuckets[y.deliverIndex] = y.deliveredThisTick;
            y.deliveredThisTick = 0d;
            y.deliverIndex = (y.deliverIndex + 1) % y.deliverBuckets.Length;
            if (y.deliverFilled < y.deliverBuckets.Length) y.deliverFilled++;

            // Only a live island measures its own delivery rate — an idle yard is being fed BY this
            // number, so letting it re-measure itself would feed it back into itself and drift.
            if (y.save.id != _activeIsland || y.deliverFilled < MinTrustedSeconds) return;

            // Stored CLEAN — what this island WOULD send if it were in good repair. The lorries that
            // were just counted were already running slow, so their state of repair is divided back
            // out here and multiplied in again by SupplyPerSecond, which is the only reader. Skipping
            // that would bake one moment's damage into the save and then apply the damage a second
            // time on every read: an island neglected for a week would end up crediting a fraction of
            // a fraction, and the deeper the neglect the worse the double-count.
            float condition = Condition(y.save.id);
            double measured = y.deliverTrailing * (60d / y.deliverFilled);
            y.save.deliveredPerMin = condition > 0.01f ? measured / condition : measured;
        }

        private void AdvanceIncomeMeter(Yard y)
        {
            y.earnTrailing += y.earnedThisTick - y.earnBuckets[y.earnIndex];
            y.earnBuckets[y.earnIndex] = y.earnedThisTick;
            y.earnedThisTick = 0d;
            y.earnIndex = (y.earnIndex + 1) % y.earnBuckets.Length;
            if (y.earnFilled < y.earnBuckets.Length) y.earnFilled++;

            double cap = y.terms != null
                ? y.terms.IncomeCapPerMinuteRaw * _legacyIncomeMult * _permanentSpeed * _foremanMult
                : double.MaxValue;
            // Clamp the extrapolation rather than the buckets: while the window is still filling, one
            // good second scaled up by 60/filled reads far above anything the yard can sustain.
            double measured = y.earnTrailing * (60d / y.earnFilled);
            y.cashPerMin = measured < cap ? measured : cap;

            if (y.earnFilled >= MinTrustedSeconds) SaveRate(y.save.id, y.cashPerMin);
        }

        // ------------------------------------------------------------------ rows
        private Yard Get(string islandKey)
        {
            string key = islandKey ?? string.Empty;
            Yard y;
            if (_yards.TryGetValue(key, out y)) return y;

            y = new Yard { save = FindRow(key) ?? NewRow(key) };
            _yards[key] = y;
            _order.Add(y);
            return y;
        }

        private MarketYard FindRow(string id)
        {
            if (_data == null || _data.marketYards == null) return null;
            var list = _data.marketYards;
            for (int i = 0; i < list.Count; i++) if (list[i].id == id) return list[i];
            return null;
        }

        private MarketYard NewRow(string id)
        {
            var row = new MarketYard { id = id };
            if (_data != null && _data.marketYards != null) _data.marketYards.Add(row);
            return row;
        }

        /// <summary>
        /// The three hire levels in the order <see cref="MarketFlow"/> indexes them, refreshed into the
        /// yard's own scratch array. Read from the save row every time rather than cached across ticks:
        /// an upgrade pad writes straight into that row, and a stale copy would leave a worker you just
        /// paid for doing nothing until the next scene load.
        /// </summary>
        private static int[] HireLevels(Yard y)
        {
            y.hires[MarketFlow.Carry] = y.save.hireCarry;
            y.hires[MarketFlow.Serve] = y.save.hireServe;
            y.hires[MarketFlow.Collect] = y.save.hireCollect;
            return y.hires;
        }

        /// <summary>
        /// What an island is sending its yard, right now, in bars a second.
        ///
        /// <see cref="MarketYard.deliveredPerMin"/> is stored CLEAN — measured while the island was
        /// running worn, then divided back out by the state of repair it was measured under, so the
        /// number on disk describes a healthy island. The wear is put back here, at the point of use.
        ///
        /// That split is the whole reason wear works while the player is elsewhere. An idle island
        /// keeps feeding its yard from this number for days, and it goes on decaying the whole time; a
        /// rate stored WITH the damage baked in would freeze it at whatever state of repair the island
        /// happened to be in the moment the player sailed off, and a fortnight of neglect would cost
        /// nothing anywhere except the one island being looked at.
        ///
        /// A running boost is divided out and put back the same way, and for a sharper reason: every
        /// capacity in this yard is a multiple of THIS number — how fast the counter can move, how much
        /// the pads hold. Leave the boost out of it and a ×2 island sends twice the bars into a yard
        /// still sized for half of them, so the counter sells what it always sold and the surplus spills
        /// off the pads and is destroyed. The ad would have bought nothing but a fuller-looking yard.
        /// </summary>
        private double SupplyPerSecond(Yard y)
            => y.save.deliveredPerMin / 60d * Condition(y.save.id) * SpeedFor(y);

        /// <summary>
        /// The clock this yard's supply is being produced at. Only the island whose lorries are really
        /// driving can run fast — see <see cref="IslandTimeScale"/> — so this is 1 everywhere else.
        /// </summary>
        private double SpeedFor(Yard y) => y.save.id == _activeIsland ? _simSpeed : 1d;

        /// <summary>
        /// How well an island is running, or 1 when nothing is tracking it. The worst station, because
        /// the chain is serial — see <see cref="Game.Core.Maintenance.IslandCondition"/>.
        /// </summary>
        private float Condition(string islandKey)
            => _maintenance != null ? _maintenance.IslandCondition(islandKey) : 1f;

        /// <summary>
        /// Whether the player owns this island, and therefore whether its yard exists at all. The hall
        /// asks before it builds: an unowned island's yard would be a room full of ore nobody has mined.
        /// </summary>
        public bool IsOwned(string islandKey)
        {
            if (string.IsNullOrEmpty(islandKey)) return false;
            if (_data == null || _data.unlockedIslands == null) return false;
            // The home island is never in the unlocked list — it is where the player starts.
            return islandKey == "coal" || _data.unlockedIslands.Contains(islandKey);
        }

        private double SavedRate(string islandKey)
        {
            if (_data == null || _data.islandRates == null || string.IsNullOrEmpty(islandKey)) return 0d;
            for (int i = 0; i < _data.islandRates.Count; i++)
                if (_data.islandRates[i].id == islandKey) return _data.islandRates[i].perMin;
            return 0d;
        }

        private void SaveRate(string islandKey, double perMin)
        {
            if (_data == null || _data.islandRates == null) return;
            for (int i = 0; i < _data.islandRates.Count; i++)
                if (_data.islandRates[i].id == islandKey) { _data.islandRates[i].perMin = perMin; return; }
            _data.islandRates.Add(new IslandRate { id = islandKey, perMin = perMin });
        }
    }
}
