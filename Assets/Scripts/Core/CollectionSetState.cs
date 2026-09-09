using System;

namespace Game.Core
{
    /// <summary>
    /// One set as a screen needs to see it: how far along, what finishing it turned on, and whether
    /// there is still something to collect.
    ///
    /// The card half of this grammar already exists — <see cref="RosterCardState"/> is shared by every
    /// collectible in the project and the collection speaks it too, which is what lets
    /// <see cref="RosterCardQuery"/>'s allocation-free filter and sort work on a card grid nobody had
    /// to write a second time. Sets had no equivalent, so this is it.
    ///
    /// THE BONUS AND THE REWARD ARE SEPARATE FACTS, and the separation is the whole point.
    /// <see cref="BonusActive"/> is derived from <see cref="Complete"/> and nothing else, so a player
    /// who never taps Claim keeps the permanent bonus they earned. <see cref="RewardClaimable"/> is
    /// the one-time payment, and once <see cref="RewardClaimed"/> is true it can never come back.
    /// </summary>
    public readonly struct CollectionSetState
    {
        /// <summary>Position in <see cref="CardCollectionCatalogue.Sets"/>, or -1 for a set that does
        /// not exist.</summary>
        public readonly int Index;

        /// <summary>Save key. "" for a set that does not exist.</summary>
        public readonly string Id;

        public readonly int OwnedCount;
        public readonly int TotalCount;

        public readonly CardCollection.EffectKind Bonus;
        public readonly double BonusValue;

        public readonly CardCollection.SetRewardKind RewardKind;
        public readonly long RewardAmount;

        public readonly bool RewardClaimed;

        public CollectionSetState(int index, string id, int ownedCount, int totalCount,
                                  CardCollection.EffectKind bonus, double bonusValue,
                                  CardCollection.SetRewardKind rewardKind, long rewardAmount,
                                  bool rewardClaimed)
        {
            Index = index;
            Id = id ?? string.Empty;
            TotalCount = Math.Max(0, totalCount);
            OwnedCount = Math.Max(0, Math.Min(ownedCount, TotalCount));
            Bonus = bonus;
            BonusValue = bonusValue < 0d ? 0d : bonusValue;
            RewardKind = rewardKind;
            RewardAmount = Math.Max(0L, rewardAmount);
            RewardClaimed = rewardClaimed;
        }

        /// <summary>A set nobody authored — what a screen gets for an id that is not in the
        /// catalogue. Reads as an empty, uncompletable set rather than as set zero.</summary>
        public static CollectionSetState None => new CollectionSetState(
            -1, string.Empty, 0, 0, CardCollection.EffectKind.IncomeMultiplier, 0d,
            CardCollection.SetRewardKind.Gems, 0L, false);

        public bool Exists => Index >= 0;

        /// <summary>Complete when every card in it is owned at level 1 or better. Levels beyond the
        /// first do not matter — a set is a set of cards found, not of cards finished.</summary>
        public bool Complete => TotalCount > 0 && OwnedCount >= TotalCount;

        /// <summary>
        /// The permanent bonus is live the moment the last card lands, and stays live for ever. It is
        /// derived from <see cref="Complete"/> rather than from the claim, so forgetting to collect a
        /// reward can never cost a player a bonus they have already earned.
        /// </summary>
        public bool BonusActive => Complete;

        /// <summary>There is a reward sitting here waiting to be taken.</summary>
        public bool RewardClaimable => Complete && !RewardClaimed && RewardAmount > 0L;

        /// <summary>What the collection screen puts a badge on.</summary>
        public bool NeedsAttention => RewardClaimable;

        public int Remaining => TotalCount - OwnedCount;

        public float Progress
        {
            get
            {
                if (TotalCount <= 0) return 0f;
                double value = OwnedCount / (double)TotalCount;
                return (float)Math.Max(0d, Math.Min(1d, value));
            }
        }
    }
}
