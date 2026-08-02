namespace Game.Systems
{
    /// <summary>
    /// Temporary income boost (GDD §10): a rewarded ad's ×2 for five minutes, or one of the timed
    /// packages the store sells for gems.
    ///
    /// The deadline is a wall-clock unix timestamp kept in the save, not a offset from
    /// <c>Time.realtimeSinceStartup</c>. That distinction is the whole point: an idle player buys an
    /// eight-hour boost and then closes the game, which is exactly the behaviour the package is sold
    /// for. Timed against session uptime, the purchase evaporated the moment the app was backgrounded —
    /// and a purchase a restart forgets is a refund request. Timed against the clock it keeps running,
    /// and <see cref="GameBootstrap.GrantOffline"/> pays the slice of the away window it covered.
    /// </summary>
    public sealed class BoostService
    {
        private readonly SaveData _data;
        private readonly TimeService _time;

        public BoostService(SaveData data, TimeService time)
        {
            _data = data;
            _time = time;
        }

        /// <summary>Starts a boost, replacing any running one rather than stacking with it.</summary>
        public void SetBoost(double mult, double seconds)
        {
            if (mult <= 1d || seconds <= 0d) return;
            _data.boostMultiplier = mult;
            _data.boostEndUnix = _time.NowUnix() + (long)seconds;
        }

        public double ActiveMultiplier => _time.NowUnix() < _data.boostEndUnix ? _data.boostMultiplier : 1d;
        public bool IsActive => ActiveMultiplier > 1d;
        public float SecondsLeft
        {
            get
            {
                long left = _data.boostEndUnix - _time.NowUnix();
                return left > 0L ? left : 0f;
            }
        }
    }
}
