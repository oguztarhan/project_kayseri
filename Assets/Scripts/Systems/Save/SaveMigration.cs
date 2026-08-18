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
    /// purchase, and the permanent perks bought in the store. Device builds also restore store
    /// entitlements, but keeping the local copy prevents a migration from briefly removing them, and none
    /// of them shortcut the progression this reset exists to re-test. A running gem boost is not
    /// kept — it is a time-limited effect mid-flight, and starting a fresh economy already
    /// multiplied would misreport the pacing.
    /// </summary>
    public static class SaveMigration
    {
        /// <summary>
        /// Version 6: the market yard. Cash no longer enters the game when a cargo truck reaches the
        /// market building — the truck delivers, and the yard sells. Every island's income now runs
        /// through a yard that starts unstaffed and therefore starts SLOW, so a version-5 save's
        /// levels were bought against a curve where the same island paid several times as much. The
        /// rates persisted in that save describe an economy this build no longer has.
        ///
        /// Version 5 was the ad-economy pass: offline efficiency 50% → 35%, the welcome-back ad
        /// stopped paying a second unlimited grant, and the unlock ladder was re-solved.
        /// </summary>
        public const int CurrentVersion = 6;

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
            fresh.stationSpeedMultiplier = old.stationSpeedMultiplier > 1d
                ? old.stationSpeedMultiplier
                : 1d;
            fresh.offlineEfficiencyBonus = old.offlineEfficiencyBonus;
            fresh.offlineCapBonusSeconds = old.offlineCapBonusSeconds;
            fresh.dailyRewardBonusMult = old.dailyRewardBonusMult;
            fresh.freeRewardBonusCharges = old.freeRewardBonusCharges;
            fresh.dailyGemStipend = old.dailyGemStipend;
            return fresh;
        }
    }
}
