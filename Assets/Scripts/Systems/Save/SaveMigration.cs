namespace Game.Systems
{
    /// <summary>
    /// Decides whether a save written by an older build still means anything in this one, and
    /// starts the player over when it does not.
    ///
    /// This is not housekeeping — it is a balance tool. A save records LEVELS, and levels only
    /// have meaning against the cost and income curves they were bought under. When those curves
    /// are rewritten, an old save is not "slightly off": a tester who arrived with every island
    /// maxed on the previous economy has nothing left to play, and the new pacing is exactly the
    /// thing that needs testing. So the update wipes progress rather than carrying it forward.
    ///
    /// HOW TO TRIGGER IT AGAIN: bump <see cref="CurrentVersion"/>. Every save on a device carries
    /// the version it was written under, so raising the number here wipes once, on the first
    /// launch after the update, and never again. Leaving it alone ships an update that keeps
    /// progress — which is what a normal content patch should do. Nothing else needs changing.
    ///
    /// What survives is what the player PAID for, not what they played for: gems, the remove-ads
    /// purchase, and the permanent perks bought in the store. There is no receipt-based restore
    /// in this build (<c>StubIAPService</c>), so a wipe of those would be unrecoverable, and none
    /// of them shortcut the progression this reset exists to re-test. A running gem boost is not
    /// kept — it is a time-limited effect mid-flight, and starting a fresh economy already
    /// multiplied would misreport the pacing.
    /// </summary>
    public static class SaveMigration
    {
        /// <summary>
        /// Version 4: the economy rebalance — cost and unlock curves, offline cap, prestige and
        /// the fleet caps all moved, so every level on a version-3 save was bought at a price
        /// that no longer exists.
        /// </summary>
        public const int CurrentVersion = 4;

        /// <summary>True when <paramref name="data"/> came from a build whose progress cannot carry over.</summary>
        public static bool NeedsReset(SaveData data) => data == null || data.version != CurrentVersion;

        /// <summary>
        /// A fresh run, carrying across only what was bought with money.
        ///
        /// Built by starting from a default <see cref="SaveData"/> and copying the keep-list onto
        /// it, deliberately the opposite way round from clearing fields on the old object: a field
        /// added to the save later then defaults to "wiped" unless someone chooses to keep it,
        /// which is the safe direction for a reset to fail in.
        /// </summary>
        public static SaveData Reset(SaveData old)
        {
            var fresh = new SaveData();
            if (old == null) return fresh;

            if (old.wallet != null) fresh.wallet.gems = old.wallet.gems;
            fresh.adsRemoved = old.adsRemoved;
            if (old.purchasedOffers != null) fresh.purchasedOffers.AddRange(old.purchasedOffers);
            fresh.offlineEfficiencyBonus = old.offlineEfficiencyBonus;
            fresh.offlineCapBonusSeconds = old.offlineCapBonusSeconds;
            fresh.dailyRewardBonusMult = old.dailyRewardBonusMult;
            fresh.freeRewardBonusCharges = old.freeRewardBonusCharges;
            fresh.dailyGemStipend = old.dailyGemStipend;
            return fresh;
        }
    }
}
