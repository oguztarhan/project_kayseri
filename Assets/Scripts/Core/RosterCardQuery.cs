namespace Game.Core
{
    public enum RosterSortMode { Default = 0, UpgradeReady = 1, Rarity = 2, Level = 3 }
    public enum RosterFilterMode { All = 0, Owned = 1, Locked = 2, UpgradeReady = 3 }

    /// <summary>
    /// Allocation-free roster browsing. Screens own their fixed state/order arrays and this fills
    /// them, so changing a filter never creates garbage on mobile.
    /// </summary>
    public static class RosterCardQuery
    {
        public static bool Matches(in RosterCardState card, RosterFilterMode filter)
        {
            switch (filter)
            {
                case RosterFilterMode.Owned:        return card.Owned;
                case RosterFilterMode.Locked:       return !card.Owned;
                case RosterFilterMode.UpgradeReady: return card.CanUpgrade;
                default:                            return true;
            }
        }

        public static int Fill(RosterCardState[] cards, int count, RosterSortMode sort,
                               RosterFilterMode filter, int[] order)
        {
            if (cards == null || order == null || count <= 0) return 0;
            int limit = count;
            if (limit > cards.Length) limit = cards.Length;
            if (limit > order.Length) limit = order.Length;

            int written = 0;
            for (int i = 0; i < limit; i++)
                if (Matches(cards[i], filter)) order[written++] = i;

            // The rosters contain eight and ten cards. Insertion sort is smaller than a comparer,
            // stable for equal cards, and allocates nothing.
            for (int i = 1; i < written; i++)
            {
                int value = order[i];
                int j = i - 1;
                while (j >= 0 && Compare(cards[value], cards[order[j]], sort) < 0)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = value;
            }
            return written;
        }

        private static int Compare(in RosterCardState a, in RosterCardState b, RosterSortMode sort)
        {
            int result;
            switch (sort)
            {
                case RosterSortMode.UpgradeReady:
                    result = Desc(a.CanUpgrade, b.CanUpgrade);
                    if (result != 0) return result;
                    result = Desc(a.Owned, b.Owned);
                    if (result != 0) return result;
                    result = Desc((int)a.Tier, (int)b.Tier);
                    if (result != 0) return result;
                    break;

                case RosterSortMode.Rarity:
                    result = Desc((int)a.Tier, (int)b.Tier);
                    if (result != 0) return result;
                    result = Desc(a.Level, b.Level);
                    if (result != 0) return result;
                    break;

                case RosterSortMode.Level:
                    result = Desc(a.Owned, b.Owned);
                    if (result != 0) return result;
                    result = Desc(a.Level, b.Level);
                    if (result != 0) return result;
                    result = Desc((int)a.Tier, (int)b.Tier);
                    if (result != 0) return result;
                    break;
            }
            return a.Index.CompareTo(b.Index);
        }

        private static int Desc(int a, int b) => b.CompareTo(a);
        private static int Desc(bool a, bool b) => b.CompareTo(a);
    }
}
