using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The HUD balloon's reward and cooldown. The icon decides when to show an ad; this service
    /// makes the payout atomic with FreeRewardService's persisted cooldown state.
    /// </summary>
    public sealed class BalloonRewardService
    {
        public const string RewardId = "balloon";
        public const float CooldownSeconds = 180f;

        /// <summary>
        /// Claims per UTC day. Without a cap the three-minute cooldown was the only limit, which with
        /// Remove-Ads paid five income-minutes every three minutes of play — a faucet the ad budget in
        /// EconomyCurve never counted. Six claims is thirty income-minutes a day.
        /// </summary>
        public const int ChargesPerDay = 6;

        public readonly struct Receipt
        {
            public readonly bool Paid;
            public readonly bool Gems;
            public readonly long GemAmount;
            public readonly double CashAmount;

            public Receipt(bool paid, bool gems, long gemAmount, double cashAmount)
            {
                Paid = paid;
                Gems = gems;
                GemAmount = gemAmount;
                CashAmount = cashAmount;
            }
        }

        private readonly FreeRewardService _free;
        private readonly WalletService _wallet;
        private readonly SaveService _save;
        private readonly SaveData _data;

        public BalloonRewardService(FreeRewardService free, WalletService wallet, SaveService save,
                                    SaveData data)
        {
            _free = free;
            _wallet = wallet;
            _save = save;
            _data = data;
        }

        public bool Ready => _free != null && _free.CanWatch(RewardId, ChargesPerDay, CooldownSeconds);

        public int ChargesLeft => _free != null ? _free.ChargesLeft(RewardId, ChargesPerDay) : 0;

        /// <summary>Seconds until the next claim: the cooldown, or the UTC reset once today's
        /// charges are spent, so the HUD timer never goes blank on a disabled button.</summary>
        public float CooldownLeft
        {
            get
            {
                if (_free == null) return 0f;
                if (_free.ChargesLeft(RewardId, ChargesPerDay) <= 0) return _free.SecondsUntilReset();
                return _free.CooldownLeft(RewardId, CooldownSeconds);
            }
        }

        /// <summary>
        /// Pays a fixed gem award on the low roll; otherwise pays the greater of the authored floor
        /// and the player's own income-minute reward. The caller supplies the roll so this remains
        /// deterministic in tests and has no hidden random source.
        /// </summary>
        public Receipt TryClaim(double incomePerMinute, double cashMinutes, double cashFloor,
                                double gemChance, long gemAmount, double roll)
        {
            if (!Ready) return new Receipt(false, false, 0L, 0d);

            bool gems = roll >= 0d && roll < gemChance;
            if (gems)
            {
                long amount = gemAmount > 0L ? gemAmount : 0L;
                _wallet?.AddGems(amount);
                _free.Consume(RewardId);
                _save?.Save(_data);
                return new Receipt(true, true, amount, 0d);
            }

            double scaled = incomePerMinute > 0d && cashMinutes > 0d ? incomePerMinute * cashMinutes : 0d;
            double cash = Math.Max(cashFloor > 0d ? cashFloor : 0d, scaled);
            _wallet?.AddCash(new BigDouble(cash));
            _free.Consume(RewardId);
            _save?.Save(_data);
            return new Receipt(true, false, 0L, cash);
        }
    }
}
