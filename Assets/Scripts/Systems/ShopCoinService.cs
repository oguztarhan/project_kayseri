using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The collectible shop coins: at most one on screen, twelve collected per 48-hour UTC cycle (eight in its first
    /// day), a random reward per coin, paid and saved the moment it is tapped.
    ///
    /// Time only moves the coins through <see cref="Tick"/>, which the view calls with seconds of ELIGIBLE play — the
    /// shop on camera, the app in front, nothing covering it. Wall-clock time decides only which cycle it is.
    ///
    /// A coin nobody taps goes back to the cycle: the cap counts collected coins. The coin on screen is not saved,
    /// but the wait before the next one is set the moment a coin spawns, so killing the app with a coin showing
    /// neither loses it for good nor brings it straight back. Rewards are keyed by the collected index
    /// (<see cref="ShopCoins.RewardFor"/>), so skipping, missing or restarting never changes what the next coin pays.
    ///
    /// Setting the clock back freezes the coins until the clock catches up with the cycle already opened; moving it
    /// forward opens one fresh cycle, never a backlog.
    /// </summary>
    public sealed class ShopCoinService
    {
        public enum TickResult
        {
            None = 0,
            Spawned = 1,
            Expired = 2
        }

        public readonly struct Receipt
        {
            public readonly ShopCoins.RewardKind Kind;
            public readonly double Cash;
            public readonly long Gems;
            public readonly double BoostMultiplier;
            public readonly double BoostSeconds;
            /// <summary>Coins collected in the current cycle after this one, for "+X · 7/12".</summary>
            public readonly int Collected;
            public readonly int Cap;

            public Receipt(ShopCoins.RewardKind kind, double cash, long gems, double boostMultiplier,
                double boostSeconds, int collected, int cap)
            {
                Kind = kind;
                Cash = cash;
                Gems = gems;
                BoostMultiplier = boostMultiplier;
                BoostSeconds = boostSeconds;
                Collected = collected;
                Cap = cap;
            }
        }

        private readonly WalletService _wallet;
        private readonly BoostService _boost;
        private readonly SaveService _save;
        private readonly SaveData _data;
        private readonly Func<long> _now;
        private readonly IAnalytics _analytics;
        private readonly ShopCoins.Tuning _tuning;
        private readonly ShopCoinSaveData _store;
        private readonly string _playerId;

        private bool _live;
        private long _liveCycle;
        private int _liveIndex;
        private double _liveSecondsLeft;

        public ShopCoinService(WalletService wallet, ShopCoins.Tuning tuning, SaveData data, Func<long> nowUnix,
            BoostService boost = null, SaveService save = null, IAnalytics analytics = null)
        {
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _now = nowUnix ?? throw new ArgumentNullException(nameof(nowUnix));
            tuning.Validate();
            _tuning = tuning;
            _boost = boost;
            _save = save;
            _analytics = analytics;
            if (_data.shopCoins == null) _data.shopCoins = new ShopCoinSaveData();
            _store = _data.shopCoins;
            double maxDelay = tuning.GapMaxSeconds > tuning.FirstSpawnMaxSeconds ? tuning.GapMaxSeconds : tuning.FirstSpawnMaxSeconds;
            _store.Normalise(maxDelay);
            _playerId = PlayerIdentity.Ensure(_data, _save);
        }

        /// <summary>Coins wait for the opening tutorial to finish.</summary>
        public bool Unlocked => _data.tutorialStep >= TutorialProgress.StepDone;

        /// <summary>The clock reads earlier than the cycle already opened: nothing spawns or pays until it catches up.</summary>
        public bool Frozen => ShopCoins.CycleAt(_now()) < _store.cycle;

        public int Collected
        {
            get { Sync(); return _store.collected; }
        }

        public int CoinsPerCycle => _tuning.CoinsPerCycle;

        /// <summary>Seconds until the next cycle opens: the HUD countdown and the notification time.</summary>
        public long SecondsToReset => ShopCoins.SecondsToReset(_now());

        public long NextCycleStartUnix => ShopCoins.CycleStartUnix(ShopCoins.CycleAt(_now()) + 1L);

        /// <summary>A coin is on screen and can be tapped.</summary>
        public bool HasLiveCoin => _live;

        public double LiveSecondsLeft => _live ? _liveSecondsLeft : 0d;

        public double LifetimeSeconds => _tuning.LifetimeSeconds;

        /// <summary>Eligible play left before the next coin; 0 while one is showing or the cycle is spent.</summary>
        public double SecondsToNextSpawn => _store.secondsToNextSpawn;

        public ShopCoins.Tuning Tuning => _tuning;

        /// <summary>Whether another coin may still spawn in this cycle right now (first-day cap included).</summary>
        public bool CanSpawnMore
        {
            get
            {
                if (!Unlocked || Frozen) return false;
                Sync();
                return _store.collected < ShopCoins.CapAt(_now(), _tuning);
            }
        }

        /// <summary>
        /// Advances the coins by <paramref name="eligibleSeconds"/> of eligible play. Returns what happened, so the
        /// view can pop a coin in or shrink one away. Allocates nothing.
        /// </summary>
        public TickResult Tick(double eligibleSeconds)
        {
            if (!(eligibleSeconds > 0d) || double.IsInfinity(eligibleSeconds)) return TickResult.None;
            if (!Unlocked) return TickResult.None;
            if (Frozen)
            {
                if (!_live) return TickResult.None;
                _live = false;
                return TickResult.Expired;
            }
            Sync();

            if (_live)
            {
                _liveSecondsLeft -= eligibleSeconds;
                if (_liveSecondsLeft > 0d) return TickResult.None;
                // Missed: the coin goes back to the cycle. The wait for the next one was set when this one spawned.
                _live = false;
                _analytics?.Log("shop_coin_missed", "index", _liveIndex);
                return TickResult.Expired;
            }

            if (_store.collected >= ShopCoins.CapAt(_now(), _tuning)) return TickResult.None;
            _store.secondsToNextSpawn -= eligibleSeconds;
            if (_store.secondsToNextSpawn > 0d) return TickResult.None;

            _live = true;
            _liveCycle = _store.cycle;
            _liveIndex = _store.collected;
            _liveSecondsLeft = _tuning.LifetimeSeconds;
            _store.spawns++;
            _store.secondsToNextSpawn = ShopCoins.DelayBefore(_playerId, _store.cycle, _store.spawns, _tuning);
            return TickResult.Spawned;
        }

        /// <summary>
        /// Collects the coin on screen: pays its reward and saves before returning, so the animation that follows
        /// can be cut short by a kill without the reward being lost or paid twice. False when there is no coin
        /// (already collected, expired, or the clock was set back).
        /// </summary>
        public bool TryCollect(double incomePerMinute, out Receipt receipt)
        {
            receipt = default;
            if (!_live) return false;
            _live = false;
            if (Frozen) return false;
            Sync();

            ShopCoins.Reward reward = ShopCoins.RewardFor(_playerId, _liveCycle, _liveIndex, _tuning);
            double cash = 0d;
            switch (reward.Kind)
            {
                case ShopCoins.RewardKind.Gems:
                    _wallet.AddGems(reward.Gems);
                    break;
                case ShopCoins.RewardKind.Boost:
                    _boost?.AddBoost(reward.BoostMultiplier, reward.BoostSeconds);
                    break;
                default:
                    cash = ShopCoins.Cash(reward.CashMinutes, incomePerMinute, _tuning.CashFloor);
                    _wallet.AddCash(new BigDouble(cash));
                    break;
            }

            // A coin that outlived its cycle's reset counts toward the cycle it came from, which is already gone.
            if (_liveCycle == _store.cycle) _store.collected++;
            _save?.Save(_data);
            _analytics?.Log("shop_coin_collected", "kind", (int)reward.Kind);

            receipt = new Receipt(reward.Kind, cash, reward.Kind == ShopCoins.RewardKind.Gems ? reward.Gems : 0L,
                reward.BoostMultiplier, reward.BoostSeconds, _store.collected, _tuning.CoinsPerCycle);
            return true;
        }

        /// <summary>Opens the current cycle when the clock has moved past the stored one. Never moves backwards.</summary>
        private void Sync()
        {
            long cycle = ShopCoins.CycleAt(_now());
            if (cycle <= _store.cycle) return;
            _store.cycle = cycle;
            _store.collected = 0;
            _store.spawns = 0;
            _store.secondsToNextSpawn = ShopCoins.DelayBefore(_playerId, cycle, 0, _tuning);
        }
    }
}
