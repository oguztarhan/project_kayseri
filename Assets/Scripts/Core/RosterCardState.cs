using System;

namespace Game.Core
{
    /// <summary>
    /// The presentation-neutral grammar shared by every collectible roster card. A roster service
    /// translates its own save state into this value; UI can then render owned, locked, maxed and
    /// upgrade-ready cards without re-interpreting progression rules.
    /// </summary>
    public readonly struct RosterCardState
    {
        public enum Rarity { Common = 0, Rare = 1, Epic = 2, Legendary = 3, Mythic = 4 }
        public enum Status { Locked = 0, Owned = 1, Maxed = 2 }

        public readonly int Index;
        public readonly Rarity Tier;
        public readonly int Role;
        public readonly int Level;
        public readonly int MaxLevel;
        public readonly int Duplicates;
        public readonly int DuplicatesRequired;
        public readonly double Effect;
        public readonly bool Busy;

        public RosterCardState(int index, Rarity tier, int role, int level, int maxLevel,
                               int duplicates, int duplicatesRequired, double effect, bool busy)
        {
            Index = index;
            Tier = ClampTier(tier);
            Role = role;
            MaxLevel = Math.Max(1, maxLevel);
            Level = Math.Max(0, Math.Min(level, MaxLevel));
            Duplicates = Math.Max(0, duplicates);
            DuplicatesRequired = Math.Max(0, duplicatesRequired);
            Effect = Math.Max(0d, effect);
            Busy = busy;
        }

        public bool Owned => Level > 0;
        public bool IsMaxed => Owned && Level >= MaxLevel;
        public bool CanUpgrade => Owned && !IsMaxed && DuplicatesRequired > 0
                                  && Duplicates >= DuplicatesRequired;
        public bool NeedsAttention => CanUpgrade;
        public Status CardStatus => !Owned ? Status.Locked : IsMaxed ? Status.Maxed : Status.Owned;

        public float Progress
        {
            get
            {
                if (!Owned) return 0f;
                if (IsMaxed) return 1f;
                if (DuplicatesRequired <= 0) return 0f;
                double value = Duplicates / (double)DuplicatesRequired;
                return (float)Math.Max(0d, Math.Min(1d, value));
            }
        }

        private static Rarity ClampTier(Rarity tier)
        {
            int value = (int)tier;
            if (value < (int)Rarity.Common) return Rarity.Common;
            if (value > (int)Rarity.Mythic) return Rarity.Mythic;
            return tier;
        }
    }
}
