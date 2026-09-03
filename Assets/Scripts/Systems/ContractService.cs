using Game.Core;
using Game.Data;

namespace Game.Systems
{
    /// <summary>
    /// The port contract loop (GDD §9). A cargo ship sails in, tables three jobs at three difficulties,
    /// waits while one of them runs, and leaves once it is settled — delivered or missed, the ship goes.
    ///
    /// A job is "process N units inside T minutes", measured on smelter output rather than on cash. That
    /// is the difference from the old rolling cash goal: cash arrives from anywhere and made the contract
    /// a passive number-watch, while processing is the thing the player's upgrades actually move. Every
    /// island's smelters count toward the same job (<see cref="ReportProcessed"/> is called by all of
    /// them), so travelling mid-contract never voids progress — the same forgiveness the cash version got
    /// for free by reading lifetime cash.
    ///
    /// Targets are sized off the empire's own measured throughput, not authored numbers: one contract is
    /// roughly one focused window whether the islands smelt 200 units a minute or 200 trillion. The
    /// config's floor only covers the opening minutes, before the meter has anything in it.
    ///
    /// Nothing here expires on its own except the running clock. The ship waits indefinitely on the
    /// offers and indefinitely on an unclaimed reward — an idle game must never punish a player for
    /// looking away, and the port is the one place in this game where the answer to "what now?" lives.
    /// </summary>
    public sealed class ContractService
    {
        /// <summary>Where the ship is in its visit. Drives both the screen and the hull in the water.</summary>
        public enum PortState
        {
            Away,       // over the horizon; a countdown is running toward the next visit
            Arriving,   // sailing in from the sea lane
            Offering,   // docked, three jobs on the table, waiting on the player
            Active,     // a job is running; the clock is ticking
            Reward,     // target met, clock stopped, waiting for the player to claim
            Departing,  // sailing back out, settled either way
        }

        public enum Result { None, Success, Failed }

        /// <summary>
        /// One card on the table. Everything is locked in when the offers are rolled.
        ///
        /// <see cref="Id"/> is what a tap is matched against rather than the slot it was drawn in. The
        /// slot is not enough on its own: a board that can replace a single card leaves the player
        /// looking at a job that is no longer in the slot their finger is over, and a tier-only accept
        /// would sign whatever took its place — a different target, a different clock, a different
        /// payout from the one they read. An id that does not match is simply refused.
        /// </summary>
        public struct Offer
        {
            public int Id;
            public int Tier;
            public double Units;
            public float Seconds;
            public double Cash;
            public long Gems;
            public int Cards;
        }

        public const int EasyTier = 0, NormalTier = 1, HardTier = 2;
        public const int TierCount = 3;

        // Each claim makes the next set of offers this much heavier, each miss walks it back. It was 1.12
        // on the cash contract against a 90-second window; the windows here are 7-15 minutes, so a streak
        // compounds far more slowly in wall-clock terms and the step has to be gentler to match.
        private const double StreakStep = 1.08d;
        private const double StreakCap = 3d;

        // The processing meter folds in a sample this often. Short enough that a fresh session has a real
        // number before the first ship docks (the opening Away period is a minute), long enough that one
        // truck arriving does not spike it.
        private const float SampleSeconds = 10f;
        private const double SampleBlend = 0.5d;

        private readonly WalletService _wallet;
        private readonly ForemanService _foremen;
        private readonly GoalService _goals;
        private readonly SaveService _saveService;
        private readonly IAnalytics _analytics;
        private readonly BoostService _boost;
        private readonly int _cardsPerContract;
        private readonly int _cardsStreakStep;

        /// <summary>Which slot the last claim paid cards into, and how many — so the reward screen can
        /// name the foreman rather than saying "you got some cards". -1 when nothing was paid.</summary>
        public int LastCardStation { get; private set; } = -1;
        public int LastCards { get; private set; }
        private readonly SaveData _data;
        private readonly ContractSaveData _save;
        private readonly TimeService _time;

        private readonly double _floorUnits, _rewardFloor, _rewardFraction;
        private readonly long _rewardGems;
        private readonly float _normalMinutes;
        private readonly float[] _tierRate = new float[TierCount];
        private readonly float[] _tierMinutes = new float[TierCount];
        private readonly float[] _tierPay = new float[TierCount];
        private readonly long[] _tierGems = new long[TierCount];
        private readonly float _arriveSeconds, _departSeconds, _cooldownSeconds;
        private readonly double _boardRefreshFactor;
        private readonly int _swapsPerVisit;
        private int _rerollsUsed;

        private readonly Offer[] _offers = new Offer[TierCount];

        private PortState _state;
        private float _stateLeft;      // seconds left in Away / Arriving / Departing
        private float _stateSpan;      // how long that state runs, for the 0..1 the ship rides

        private double _procAccum;     // units reported since the last meter sample
        private float _procSpan;
        private double _procPerMinute;
        private double _cashPerMinute;

        // The pair the board on the table was cut against. Read once when the board is cut and then
        // frozen: the live meter keeps moving while the ship waits, and a card priced off a later
        // reading than the one beside it would make the three cards incomparable.
        private double _boardProc;
        private double _boardCash;

        private double _difficulty = 1d;

        private double _target, _done;
        private double _activeCash;
        private long _activeGems;
        private int _activeTier = -1;   // -1 = restored from a save written before this was tracked
        private int _nextOfferId = 1;   // ids start at 1 so that 0 can mean "this save predates them"
        private int _activeOfferId;
        private int _activeCards;
        private float _left;

        public ContractService(WalletService wallet, ContractConfig config, SaveData data = null,
                               TimeService time = null, ForemanService foremen = null,
                               GoalService goals = null, SaveService saveService = null,
                               IAnalytics analytics = null, BoostService boost = null)
        {
            _wallet = wallet;
            _foremen = foremen;
            _goals = goals;
            _saveService = saveService;
            _analytics = analytics;
            _boost = boost;
            _data = data;
            _time = time;
            if (_data != null && _data.contract == null) _data.contract = new ContractSaveData();
            _save = _data != null ? _data.contract : new ContractSaveData();

            _floorUnits = config != null && config.FloorUnits > 0d ? config.FloorUnits : 50d;
            _normalMinutes = config != null && config.NormalMinutes > 0.1f ? config.NormalMinutes : 10f;
            _rewardFloor = config != null ? config.RewardCash : 500d;
            _rewardGems = config != null ? config.RewardGems : 2L;
            _cardsPerContract = config != null ? config.CardsPerContract : 2;
            _cardsStreakStep = config != null && config.CardsStreakStep > 0 ? config.CardsStreakStep : 5;
            _rewardFraction = config != null && config.RewardFraction > 0d ? config.RewardFraction : 0.45d;
            _boardRefreshFactor = config != null && config.BoardRefreshFactor > 1d
                                ? config.BoardRefreshFactor : 2d;
            _swapsPerVisit = config != null ? config.SwapsPerVisit : 1;

            _tierRate[EasyTier] = config != null ? config.EasyRate : 0.6f;
            _tierMinutes[EasyTier] = config != null ? config.EasyMinutes : 15f;
            _tierPay[EasyTier] = config != null ? config.EasyPay : 0.5f;
            _tierGems[EasyTier] = config != null ? config.EasyGems : 1L;

            _tierRate[NormalTier] = 1f;
            _tierMinutes[NormalTier] = _normalMinutes;
            _tierPay[NormalTier] = 1f;
            _tierGems[NormalTier] = _rewardGems;

            _tierRate[HardTier] = config != null ? config.HardRate : 1.6f;
            _tierMinutes[HardTier] = config != null ? config.HardMinutes : 7f;
            _tierPay[HardTier] = config != null ? config.HardPay : 2.2f;
            _tierGems[HardTier] = config != null ? config.HardGems : 4L;

            _arriveSeconds = config != null && config.ShipArriveSeconds > 0.5f ? config.ShipArriveSeconds : 14f;
            _departSeconds = config != null && config.ShipDepartSeconds > 0.5f ? config.ShipDepartSeconds : 16f;
            _cooldownSeconds = config != null && config.ShipCooldownSeconds > 1f ? config.ShipCooldownSeconds : 60f;

            // The session opens with the horizon empty and a ship on its way in. That minute is also what
            // fills the processing meter, so the first three offers are sized off a real number instead
            // of the floor.
            RestoreOrBegin();
        }

        public PortState State => _state;
        public Result LastResult { get; private set; }
        public int Streak { get; private set; }

        /// <summary>Three jobs are on the table. The only state where <see cref="Accept"/> does anything.</summary>
        public bool HasOffers => _state == PortState.Offering;

        /// <summary>
        /// Set when the board re-cut itself under the player, cleared by whoever reads it. The screen
        /// uses it to redraw immediately: a refresh must never be able to slide new numbers under a
        /// finger that is already on its way down, and between this and the id on every card a tap that
        /// lands a moment too late is refused rather than signed against the wrong job.
        /// </summary>
        public bool BoardRefreshed { get; private set; }

        public bool ConsumeBoardRefreshed()
        {
            if (!BoardRefreshed) return false;
            BoardRefreshed = false;
            return true;
        }

        /// <summary>Swaps the player can still make on the ship at the pier. Zero unless it is offering.</summary>
        public int SwapsLeft
            => _state == PortState.Offering && _swapsPerVisit > _rerollsUsed ? _swapsPerVisit - _rerollsUsed : 0;

        public bool CanSwap => SwapsLeft > 0;
        public bool IsRunning => _state == PortState.Active;
        public bool Claimable => _state == PortState.Reward;

        /// <summary>The running job's clock. Stops the moment the target is met.</summary>
        public float SecondsLeft => _left;
        /// <summary>Seconds until the next ship appears on the horizon. 0 unless it is away.</summary>
        public float SecondsToShip => _state == PortState.Away ? _stateLeft : 0f;
        public float SecondsUntilOffers
            => _state == PortState.Away ? _stateLeft + _arriveSeconds
             : _state == PortState.Arriving ? _stateLeft
             : 0f;

        public double TargetUnits => _target;
        public double DoneUnits => _done;
        public BigDouble Reward => new BigDouble(_activeCash);
        public long RewardGems => _activeGems;
        public double ProcessedPerMinute => _procPerMinute;

        /// <summary>What the running job's units are called — the ore word of the island that took it.</summary>
        public string UnitWord { get; private set; } = "COAL";

        public double Progress01
        {
            get
            {
                if (_state == PortState.Reward) return 1d;
                if (_target <= 0d) return 0d;
                double p = _done / _target;
                return p < 0d ? 0d : p > 1d ? 1d : p;
            }
        }

        /// <summary>
        /// Where the contract ship sits on its run, 0 = out past the sea lane and off screen, 1 = moored
        /// at the pier. Eased at both ends so a hull the size of a building does not start and stop dead.
        /// Every island reads this to place its own ship, which is what makes the port on whichever
        /// island you are standing on show the same visit.
        /// </summary>
        public float ShipDock01
        {
            get
            {
                switch (_state)
                {
                    case PortState.Away: return 0f;
                    case PortState.Arriving: return Ease(_stateSpan <= 0f ? 1f : 1f - _stateLeft / _stateSpan);
                    case PortState.Departing: return Ease(_stateSpan <= 0f ? 0f : _stateLeft / _stateSpan);
                    default: return 1f;
                }
            }
        }

        public Offer GetOffer(int tier)
        {
            if (tier < 0 || tier >= TierCount) return default(Offer);
            return _offers[tier];
        }

        /// <summary>
        /// Primes the reward maths from the rate the previous session persisted, so a returning player's
        /// first offers are not priced off a live income meter that is still reading zero. Call after the
        /// offline earnings are paid.
        /// </summary>
        public void Seed(double incomePerMinute)
        {
            if (incomePerMinute > _cashPerMinute) _cashPerMinute = incomePerMinute;
            Sync();
        }

        /// <summary>
        /// Units the smelters converted this frame, from any island. Feeds both the running job and the
        /// throughput meter that sizes the next set of offers.
        /// </summary>
        public void ReportProcessed(double units)
        {
            if (units <= 0d) return;
            _procAccum += units;
            if (_state == PortState.Active) _done += units;
        }

        /// <summary>
        /// Advances the visit. <paramref name="cashPerMinute"/> prices the offers and is only read when
        /// they are rolled — a reward that moved while the player worked toward it would be a cheat.
        /// </summary>
        public void Tick(float dt, double cashPerMinute)
        {
            if (cashPerMinute > 0d) _cashPerMinute = Unboosted(cashPerMinute);
            SampleRate(dt);

            switch (_state)
            {
                case PortState.Away:
                    _stateLeft -= dt;
                    if (_stateLeft <= 0f) Enter(PortState.Arriving, _arriveSeconds);
                    break;

                case PortState.Arriving:
                    _stateLeft -= dt;
                    if (_stateLeft <= 0f)
                    {
                        NewVisit();
                        Enter(PortState.Offering, 0f);
                    }
                    break;

                case PortState.Offering:
                    // The ship waits — an offer the player did not see is not an offer. But a board cut
                    // against an empire they have since doubled is not an offer either, so it is re-cut
                    // in place rather than left on the table insulting them.
                    RefreshBoardIfStale();
                    break;

                case PortState.Active:
                    if (_done >= _target)
                    {
                        LastResult = Result.Success;
                        Enter(PortState.Reward, 0f);
                        break;
                    }
                    _left -= dt;
                    if (_left <= 0f)
                    {
                        // Missed. Ease the next set off rather than punish — a wall the player cannot
                        // clear stops being a goal, and the ship leaving is punishment enough.
                        _left = 0f;
                        LastResult = Result.Failed;
                        Streak = 0;
                        _difficulty = _difficulty > 1d ? _difficulty / StreakStep : 1d;
                        Enter(PortState.Departing, _departSeconds);
                        _analytics?.Log("contract_missed", "tier", _activeTier);
                    }
                    break;

                case PortState.Reward:
                    break;    // clock stopped, hull moored, waiting on a tap

                case PortState.Departing:
                    _stateLeft -= dt;
                    if (_stateLeft <= 0f) Enter(PortState.Away, _cooldownSeconds);
                    break;
            }
            Sync();
        }

        /// <summary>
        /// Takes one of the three jobs. <paramref name="unitWord"/> is the ore word of the island whose
        /// port it was signed at, kept so the card still reads right after the player travels.
        /// </summary>
        /// <summary>
        /// Takes the job the player actually pressed. <paramref name="offerId"/> is the id the card was
        /// drawn with: if the slot no longer holds it the tap is refused rather than signing whatever
        /// moved in. Pass 0 to skip the check — that is the path a caller with no id takes.
        /// </summary>
        public bool Accept(int tier, int offerId, string unitWord)
        {
            if (offerId > 0 && (tier < 0 || tier >= TierCount || _offers[tier].Id != offerId)) return false;
            return Accept(tier, unitWord);
        }

        public bool Accept(int tier, string unitWord)
        {
            if (_state != PortState.Offering || tier < 0 || tier >= TierCount) return false;

            Offer o = _offers[tier];
            if (o.Units <= 0d) return false;

            _target = o.Units;
            _done = 0d;
            _left = o.Seconds;
            _activeCash = o.Cash;
            _activeGems = o.Gems;
            _activeTier = tier;
            _activeOfferId = o.Id;
            _activeCards = o.Cards;
            if (!string.IsNullOrEmpty(unitWord)) UnitWord = unitWord;
            LastResult = Result.None;
            Enter(PortState.Active, 0f);
            Sync();
            Commit();
            _analytics?.Log("contract_accept", "tier", tier);
            return true;
        }

        /// <summary>Pays the delivered job and sends the ship out. No-op unless the target was met.</summary>
        public bool Claim()
        {
            if (_state != PortState.Reward) return false;

            _wallet.AddCash(new BigDouble(_activeCash));
            _wallet.AddGems(_activeGems);

            // Foreman cards. Contracts were dead content — a whole ship, a timer and a state machine
            // paying out about a second of a maxed island's income plus a handful of gems that had
            // nowhere to go. Cards are the reward that cannot be bought, so this is the loop that
            // makes finishing one worth the trip, and it scales with the streak the player has built.
            LastCardStation = -1;
            LastCards = 0;
            if (_foremen != null)
            {
                // A job signed before offers carried a card count restores with activeOfferId 0, and
                // there is no frozen number to pay — work it out the way that save would have.
                int cards = _activeOfferId > 0 ? _activeCards : CardsFor();
                if (cards > 0)
                {
                    LastCards = cards;
                    LastCardStation = _foremen.GrantRandomDuplicates(cards);
                }
            }

            _goals?.Record(Game.Core.Goals.Contracts);

            Streak++;
            _difficulty *= StreakStep;
            if (_difficulty > StreakCap) _difficulty = StreakCap;
            Enter(PortState.Departing, _departSeconds);
            Sync();
            Commit();
            _analytics?.Log("contract_claim", "tier", _activeTier);
            return true;
        }

        private void Enter(PortState state, float seconds)
        {
            _state = state;
            _stateSpan = seconds;
            _stateLeft = seconds;
            _save.stateEndUnix = IsWallClockState(state) && seconds > 0f
                ? NowUnix() + (long)System.Math.Ceiling(seconds)
                : 0L;
            Sync();
        }

        /// <summary>Reconciles ship travel/cooldown after the app returns from the background.</summary>
        public void ResumeWallClock()
        {
            RestoreWallClockState();
            Sync();
        }

        private void RestoreOrBegin()
        {
            if (!_save.initialized)
            {
                Enter(PortState.Away, _cooldownSeconds);
                return;
            }

            _state = ValidState(_save.state) ? (PortState)_save.state : PortState.Away;
            LastResult = ValidResult(_save.lastResult) ? (Result)_save.lastResult : Result.None;
            Streak = _save.streak < 0 ? 0 : _save.streak;
            _difficulty = _save.difficulty > 0d ? _save.difficulty : 1d;
            _target = _save.target;
            _done = _save.done;
            _activeCash = _save.rewardCash;
            _activeGems = _save.rewardGems;
            _left = _save.secondsLeft;
            _stateSpan = _save.stateSpan;
            UnitWord = string.IsNullOrEmpty(_save.unitWord) ? "COAL" : _save.unitWord;
            _procPerMinute = _save.processingPerMinute;
            _cashPerMinute = _save.cashPerMinute;
            _boardProc = _save.boardProcPerMinute;
            _boardCash = _save.boardCashPerMinute;
            _rerollsUsed = _save.rerollsUsed < 0 ? 0 : _save.rerollsUsed;
            _nextOfferId = _save.nextOfferId > 0 ? _save.nextOfferId : 1;
            _activeOfferId = _save.activeOfferId;
            _activeCards = _save.activeCards;

            if (_save.offers != null)
                for (int i = 0; i < TierCount && i < _save.offers.Count; i++)
                {
                    ContractOfferSave o = _save.offers[i];
                    if (o == null) continue;
                    _offers[i] = new Offer
                    {
                        Id = o.id,
                        Tier = o.tier,
                        Units = o.units,
                        Seconds = o.seconds,
                        Cash = o.cash,
                        Gems = o.gems,
                        Cards = o.cards,
                    };
                }

            Normalise();
            RestoreWallClockState();
            Sync();
        }

        /// <summary>
        /// Re-stamps a board restored from a save written before offers carried identity. Those rows come
        /// back with id 0, tier 0 and no card count — three cards all claiming to be the easy one, none
        /// of which a tap can be matched against.
        ///
        /// Bumping the save version would fix it by deleting every live save on every device, which is
        /// an absurd price for three fields that can be derived: the slot IS the tier, the ids come from
        /// the sequence, and the card count follows from the streak restored alongside it.
        /// </summary>
        private void Normalise()
        {
            for (int i = 0; i < TierCount; i++)
            {
                Offer o = _offers[i];
                o.Tier = i;
                if (o.Id <= 0) o.Id = _nextOfferId++;
                if (o.Cards <= 0 && o.Units > 0d) o.Cards = CardsFor();
                _offers[i] = o;
            }

            // A board restored from a save written before the frozen meter existed has nothing to say
            // what it was cut against. Adopting the restored meter is the one honest answer available:
            // it cannot be wrong in the direction that matters, because the alternative — leaving it at
            // zero — would either never refresh the board or refresh it on the very next tick.
            if (_boardProc <= 0d && _save.initialized)
            {
                _boardProc = _procPerMinute;
                _boardCash = _cashPerMinute;
            }
        }

        private void RestoreWallClockState()
        {
            if (!IsWallClockState(_state)) return;
            long now = NowUnix();
            long left = _save.stateEndUnix - now;
            if (left > 0L)
            {
                _stateLeft = left > int.MaxValue ? int.MaxValue : (float)left;
                return;
            }

            if (_state == PortState.Departing)
            {
                long awayEnd = _save.stateEndUnix + (long)System.Math.Ceiling(_cooldownSeconds);
                if (awayEnd > now)
                {
                    _state = PortState.Away;
                    _stateSpan = _cooldownSeconds;
                    _stateLeft = (float)(awayEnd - now);
                    _save.stateEndUnix = awayEnd;
                    return;
                }
            }

            NewVisit();
            _state = PortState.Offering;
            _stateLeft = 0f;
            _stateSpan = 0f;
            _save.stateEndUnix = 0L;
        }

        /// <summary>
        /// Puts the save on disk. Called from <see cref="Accept"/> and <see cref="Claim"/> and NOWHERE
        /// ELSE — never from <see cref="Tick"/>: <see cref="SaveService.Save"/> is an AES pass, an HMAC
        /// and a whole-file write, and one of those per frame would stall the frame and burn GC.
        ///
        /// It is what makes a claim survive being killed. The paid cash, the paid gems and the state
        /// flip that stops it being claimable again all live in the same <see cref="SaveData"/>, so one
        /// write means the file holds every part of the claim or none of it.
        /// </summary>
        private void Commit()
        {
            if (_saveService != null && _data != null) _saveService.Save(_data);
        }

        private void Sync()
        {
            if (_save == null) return;
            _save.initialized = true;
            _save.state = (int)_state;
            _save.lastResult = (int)LastResult;
            _save.streak = Streak;
            _save.difficulty = _difficulty;
            _save.target = _target;
            _save.done = _done;
            _save.rewardCash = _activeCash;
            _save.rewardGems = _activeGems;
            _save.secondsLeft = _left;
            _save.stateSpan = _stateSpan;
            _save.unitWord = UnitWord;
            _save.processingPerMinute = _procPerMinute;
            _save.cashPerMinute = _cashPerMinute;
            _save.boardProcPerMinute = _boardProc;
            _save.boardCashPerMinute = _boardCash;
            _save.rerollsUsed = _rerollsUsed;
            _save.nextOfferId = _nextOfferId;
            _save.activeOfferId = _activeOfferId;
            _save.activeCards = _activeCards;
            if (_save.offers == null) _save.offers = new System.Collections.Generic.List<ContractOfferSave>();
            while (_save.offers.Count < TierCount) _save.offers.Add(new ContractOfferSave());
            for (int i = 0; i < TierCount; i++)
            {
                ContractOfferSave o = _save.offers[i];
                Offer source = _offers[i];
                o.units = source.Units;
                o.seconds = source.Seconds;
                o.cash = source.Cash;
                o.gems = source.Gems;
                o.id = source.Id;
                o.tier = source.Tier;
                o.cards = source.Cards;
            }
        }

        private long NowUnix() => _time != null
            ? _time.NowUnix()
            : System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static bool IsWallClockState(PortState state)
            => state == PortState.Away || state == PortState.Arriving || state == PortState.Departing;

        private static bool ValidState(int value)
            => value >= (int)PortState.Away && value <= (int)PortState.Departing;

        private static bool ValidResult(int value)
            => value >= (int)Result.None && value <= (int)Result.Failed;

        /// <summary>
        /// The income figure with any running boost divided back out.
        ///
        /// The meter handed to <see cref="Tick"/> comes from the market, which multiplies every sale by
        /// the active boost — while the smelter output that SIZES the job does not move at all, because
        /// a boost is applied when a bar is sold, not when ore is processed. Priced off the raw meter, a
        /// board rolled during a x2 boost asks for the same units at twice the cash, and
        /// <see cref="Accept"/> then freezes that number for the whole contract: watch a boost ad, wait
        /// a minute for the ship, and every offer on the table pays double for the same ore.
        ///
        /// An offer has to be priced off what the empire earns, not off what it happens to be earning
        /// in the ninety seconds the player arranged for the ship to arrive in.
        /// </summary>
        private double Unboosted(double cashPerMinute)
        {
            double mult = _boost != null ? _boost.ActiveMultiplier : 1d;
            return mult > 1d ? cashPerMinute / mult : cashPerMinute;
        }

        /// <summary>
        /// Folds the units reported since the last sample into a per-minute figure. Blended rather than
        /// replaced: the smelters run in bursts as trucks arrive, and a target sized off a single ten
        /// second window that happened to catch a lull would be trivial.
        /// </summary>
        private void SampleRate(float dt)
        {
            _procSpan += dt;
            if (_procSpan < SampleSeconds) return;

            double sample = _procAccum / _procSpan * 60d;
            _procPerMinute = _procPerMinute <= 0d
                ? sample
                : _procPerMinute * (1d - SampleBlend) + sample * SampleBlend;
            _procAccum = 0d;
            _procSpan = 0f;
        }

        /// <summary>
        /// A ship docking is the one thing that refills the swap budget. Kept apart from
        /// <see cref="RollOffers"/> because a board also re-cuts itself when the empire outgrows it, and
        /// that is the board's doing, not the player's — see <see cref="RefreshBoardIfStale"/>.
        /// </summary>
        private void NewVisit()
        {
            _rerollsUsed = 0;
            RollOffers();
        }

        /// <summary>
        /// Cuts a fresh board. The meter is read ONCE, here, and frozen for the board's life — every
        /// slot filled afterwards reads the frozen pair, so the three cards stay one coherent offer
        /// rather than a pile of jobs priced at different moments.
        /// </summary>
        private void RollOffers()
        {
            _boardProc = _procPerMinute;
            _boardCash = _cashPerMinute;
            for (int i = 0; i < TierCount; i++) _offers[i] = CutOffer(i, 1f);
        }

        /// <summary>
        /// Replaces one card with a different job of the same tier, on the visit's budget. Replace, not
        /// decline: an empty slot is a board with fewer choices on it, which is worse than the board the
        /// player was unhappy with. The tier keeps its rate and its pay-per-minute — only the window
        /// moves, and the units and cash move with it, so there is nothing to gain by swapping other than
        /// a job whose length suits the player better.
        ///
        /// Written to disk at once. A swap that was only in memory would come back on the next launch,
        /// and "kill the app to get your swap back" is the kind of trick players share.
        /// </summary>
        public bool Swap(int tier, int offerId)
        {
            if (_state != PortState.Offering || tier < 0 || tier >= TierCount) return false;
            if (_rerollsUsed >= _swapsPerVisit) return false;
            Offer old = _offers[tier];
            if (old.Units <= 0d) return false;
            if (offerId > 0 && old.Id != offerId) return false;

            float authored = _tierMinutes[tier] * 60f;
            float current = authored > 0f ? old.Seconds / authored : 1f;
            _rerollsUsed++;
            _offers[tier] = CutOffer(tier, ContractBoard.WindowScale(_nextOfferId, current));
            Sync();
            Commit();
            _analytics?.Log("contract_swap", "tier", tier);
            return true;
        }

        /// <summary>
        /// Cuts the job for one slot against the board's frozen meter. <paramref name="windowScale"/> is
        /// 1 for a rolled card and one of <see cref="ContractBoard.WindowScale"/>'s shapes for a swapped
        /// one; it is chosen from the id the card is about to get, so the save alone can re-cut it.
        /// </summary>
        private Offer CutOffer(int tier, float windowScale)
        {
            ContractBoard.Terms terms = ContractBoard.Cut(
                new ContractBoard.Tier
                {
                    Rate = _tierRate[tier],
                    Minutes = _tierMinutes[tier] * windowScale,
                    Pay = _tierPay[tier],
                    Gems = _tierGems[tier],
                },
                new ContractBoard.Meter
                {
                    ProcPerMinute = _boardProc,
                    CashPerMinute = _boardCash,
                    Difficulty = _difficulty,
                },
                new ContractBoard.Floors
                {
                    Units = _floorUnits,
                    Cash = _rewardFloor,
                    RewardFraction = _rewardFraction,
                    NormalMinutes = _normalMinutes,
                },
                windowScale);

            return new Offer
            {
                Id = _nextOfferId++,
                Tier = tier,
                Units = terms.Units,
                Seconds = terms.Seconds,
                Cash = terms.Cash,
                Gems = terms.Gems,
                Cards = CardsFor(),
            };
        }

        /// <summary>
        /// Re-cuts the board if the empire has outgrown it. <see cref="ContractBoard.IsStale"/> carries
        /// why that is measured as growth and not as elapsed time.
        ///
        /// It deliberately leaves the swap budget alone. That budget is the player's to spend on a card
        /// they do not like, and a board correcting its own arithmetic is not the player spending
        /// anything — refilling it here would make growing the empire the cheapest way to buy re-rolls.
        /// </summary>
        private void RefreshBoardIfStale()
        {
            if (!ContractBoard.IsStale(_procPerMinute, _boardProc, _boardRefreshFactor)) return;
            RollOffers();
            BoardRefreshed = true;
            Sync();
        }

        /// <summary>
        /// How many foreman cards a job rolled right now would pay. Read at roll time and carried on the
        /// card itself, because it is a number the player is shown before they choose: recomputing it at
        /// claim time would let the promise on the card and the payout drift apart the moment anything
        /// else moves the streak.
        /// </summary>
        private int CardsFor()
            => _cardsPerContract <= 0 ? 0 : _cardsPerContract + (int)(Streak / _cardsStreakStep);

        /// <summary>Smoothstep, so the hull eases off the horizon and settles onto the pier.</summary>
        private static float Ease(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }
    }
}
