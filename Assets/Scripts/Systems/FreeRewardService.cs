namespace Game.Systems
{
    /// <summary>
    /// The rewarded-ad slots (GDD §10): a handful of free grabs a day, each gated by a cooldown so the
    /// screen cannot be farmed in one sitting. State only — how many charges a slot has and what it pays
    /// live on <see cref="Game.UI.AdRewardUI"/>, where they are Inspector fields the designer can tune.
    ///
    /// Charges reset on the UTC day number rather than 24h after the last watch: a rolling window pushes
    /// the refill later every day until it lands in the middle of the player's night.
    /// </summary>
    public sealed class FreeRewardService
    {
        private const long SecondsPerDay = 86400L;

        private readonly SaveData _data;
        private readonly TimeService _time;

        public FreeRewardService(SaveData data, TimeService time)
        {
            _data = data;
            _time = time;
        }

        /// <summary>The remove-ads entitlement. Persisted, so it survives a restart.</summary>
        public bool AdsRemoved
        {
            get { return _data.adsRemoved; }
            set { _data.adsRemoved = value; }
        }

        public int UsedToday(string id)
        {
            RollDay();
            FreeRewardState s = Find(id);
            return s != null ? s.used : 0;
        }

        public int ChargesLeft(string id, int chargesPerDay)
        {
            int left = chargesPerDay - UsedToday(id);
            return left > 0 ? left : 0;
        }

        /// <summary>Seconds until this slot's cooldown expires, 0 when it is ready.</summary>
        public float CooldownLeft(string id, float cooldownSeconds)
        {
            RollDay();
            FreeRewardState s = Find(id);
            if (s == null || s.lastWatchUnix <= 0L) return 0f;
            float left = cooldownSeconds - _time.ElapsedSince(s.lastWatchUnix);
            return left > 0f ? left : 0f;
        }

        public bool CanWatch(string id, int chargesPerDay, float cooldownSeconds)
            => ChargesLeft(id, chargesPerDay) > 0 && CooldownLeft(id, cooldownSeconds) <= 0f;

        /// <summary>Spends one charge and starts the cooldown. Call after the ad actually paid out.</summary>
        public void Consume(string id)
        {
            RollDay();
            FreeRewardState s = Find(id);
            if (s == null)
            {
                s = new FreeRewardState { id = id };
                _data.freeRewards.Add(s);
            }
            s.used++;
            s.lastWatchUnix = _time.NowUnix();
        }

        /// <summary>Seconds until midnight UTC — what the screen shows once every slot is spent.</summary>
        public long SecondsUntilReset()
        {
            long now = _time.NowUnix();
            return SecondsPerDay - now % SecondsPerDay;
        }

        private void RollDay()
        {
            int today = (int)(_time.NowUnix() / SecondsPerDay);
            if (_data.freeRewardDay == today) return;
            _data.freeRewardDay = today;
            // Cooldowns are deliberately not cleared: a watch just before midnight still has to wait out
            // its timer, otherwise the last charge of one day and the first of the next are back to back.
            for (int i = 0; i < _data.freeRewards.Count; i++) _data.freeRewards[i].used = 0;
        }

        private FreeRewardState Find(string id)
        {
            for (int i = 0; i < _data.freeRewards.Count; i++)
                if (_data.freeRewards[i].id == id) return _data.freeRewards[i];
            return null;
        }
    }
}
