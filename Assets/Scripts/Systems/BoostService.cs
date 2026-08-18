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

        /// <summary>
        /// Adds a boost on top of whatever is already running. It never shortens and never replaces.
        ///
        /// Overwriting was the old behaviour and it was a refund request waiting to happen: a player who
        /// paid for the 24-hour package and then tapped a free ×2/5dk ad was left holding five minutes.
        /// It also quietly punished buying two packages, which is the opposite of what the store wants.
        ///
        /// Plain extension does not work either, because the two boosts can carry different multipliers
        /// and only one of them can be THE multiplier. So the stronger one is kept and the other's time
        /// is converted into it. What a boost is worth is (mult − 1) × seconds — the income it adds over
        /// running unboosted — and that total is what the conversion preserves exactly. A ×2/5dk ad
        /// dropped onto a running ×3 buys 150 seconds rather than 300, and pays the same money for it.
        /// </summary>
        public void AddBoost(double mult, double seconds)
        {
            if (mult <= 1d || seconds <= 0d) return;

            long now = _time.NowUnix();
            long left = _data.boostEndUnix - now;
            if (left <= 0L || _data.boostMultiplier <= 1d)
            {
                _data.boostMultiplier = mult;
                _data.boostEndUnix = now + (long)seconds;
                return;
            }

            double running = _data.boostMultiplier;
            double keep = running > mult ? running : mult;
            double worth = left * (running - 1d) + seconds * (mult - 1d);

            _data.boostMultiplier = keep;
            _data.boostEndUnix = now + (long)(worth / (keep - 1d));
        }

        public double ActiveMultiplier => _time.NowUnix() < _data.boostEndUnix ? _data.boostMultiplier : 1d;
        public double PermanentMultiplier => _data.stationSpeedMultiplier > 1d
            ? _data.stationSpeedMultiplier
            : 1d;
        public double EffectiveMultiplier => PermanentMultiplier * ActiveMultiplier;
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
