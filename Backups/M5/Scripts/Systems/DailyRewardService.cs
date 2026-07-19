using System;

namespace Game.Systems
{
    /// <summary>Daily reward (GDD §11): grants gems once per calendar day (UTC), tracked in the save.</summary>
    public sealed class DailyRewardService
    {
        private readonly SaveData _data;
        private readonly TimeService _time;
        private readonly long _rewardGems;

        public DailyRewardService(SaveData data, TimeService time, long rewardGems)
        {
            _data = data; _time = time; _rewardGems = rewardGems;
        }

        public long RewardGems => _rewardGems;

        public bool CanClaim()
        {
            if (_data.lastDailyClaimUnix <= 0L) return true;
            DateTime last = DateTimeOffset.FromUnixTimeSeconds(_data.lastDailyClaimUnix).UtcDateTime.Date;
            DateTime today = DateTimeOffset.FromUnixTimeSeconds(_time.NowUnix()).UtcDateTime.Date;
            return today > last;
        }

        public long Claim(WalletService wallet)
        {
            if (!CanClaim() || wallet == null) return 0;
            _data.lastDailyClaimUnix = _time.NowUnix();
            wallet.AddGems(_rewardGems);
            return _rewardGems;
        }
    }
}
