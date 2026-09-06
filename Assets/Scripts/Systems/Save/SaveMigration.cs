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
    /// multiplied would misreport the pacing. The support id survives too, which is not a payment but
    /// is not progress either — see <see cref="SaveData.playerId"/>.
    /// </summary>
    public static class SaveMigration
    {
        /// <summary>
        /// Version 7: the open-testing reset. Every tester starts the run over on this build, so the
        /// pacing that open testing is meant to measure is measured from zero rather than on top of
        /// whatever the previous economy had already paid out.
        ///
        /// Version 6 was the market yard: cash stopped entering the game when a cargo truck reached
        /// the market building — the truck delivers, and the yard sells. Every island's income runs
        /// through a yard that starts unstaffed and therefore starts SLOW.
        ///
        /// Version 5 was the ad-economy pass: offline efficiency 50% → 35%, the welcome-back ad
        /// stopped paying a second unlimited grant, and the unlock ladder was re-solved.
        /// </summary>
        public const int CurrentVersion = 7;

        /// <summary>True when <paramref name="data"/> came from a build whose progress cannot carry over.</summary>
        public static bool NeedsReset(SaveData data) => data == null || data.version != CurrentVersion;

        /// <summary>
        /// Retires prestige without taking an earned multiplier away from an existing player. This is
        /// deliberately independent of the save version: bumping the version would wipe progression,
        /// while this compatibility pass only freezes the old investor benefit once.
        /// </summary>
        public static bool RetirePrestige(SaveData data, double bonusPerInvestor)
        {
            if (data == null || data.prestigeRetired) return false;

            double investors = data.wallet != null ? data.wallet.investors : 0d;
            double multiplier = 1d + System.Math.Max(0d, investors) * System.Math.Max(0d, bonusPerInvestor);
            data.legacyIncomeMultiplier = System.Math.Max(1d, multiplier);
            data.prestigeRetired = true;

            // Leave the legacy fields serialized for backwards compatibility, but make it explicit that
            // nothing in the retired feature can continue accumulating or be paid out.
            if (data.wallet != null)
            {
                data.wallet.investors = 0d;
                data.wallet.lifetimeCash = Game.Core.BigDouble.Zero;
            }
            return true;
        }

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

            // Presentation choice is not progression, but it is a player-facing preference. Preserve it
            // across the existing reset path so a rollback cannot silently turn itself back on.
            fresh.UsePortraitShipyard = old.UsePortraitShipyard;
            if (old.wallet != null) fresh.wallet.gems = old.wallet.gems;
            fresh.adsRemoved = old.adsRemoved;
            if (old.purchasedOffers != null) fresh.purchasedOffers.AddRange(old.purchasedOffers);
            if (old.processedIapTransactions != null)
                fresh.processedIapTransactions.AddRange(old.processedIapTransactions);
            if (old.islandOffersBought != null)
                fresh.islandOffersBought.AddRange(old.islandOffersBought);
            fresh.starterOffersMigrated = old.starterOffersMigrated;
            if (old.starterOfferWindows != null)
                fresh.starterOfferWindows.AddRange(old.starterOfferWindows);
            fresh.pendingStarterIsland = old.pendingStarterIsland;
            fresh.stationSpeedMultiplier = old.stationSpeedMultiplier > 1d
                ? old.stationSpeedMultiplier
                : 1d;
            fresh.offlineEfficiencyBonus = old.offlineEfficiencyBonus;
            fresh.offlineCapBonusSeconds = old.offlineCapBonusSeconds;
            fresh.dailyRewardBonusMult = old.dailyRewardBonusMult;
            fresh.freeRewardBonusCharges = old.freeRewardBonusCharges;
            fresh.dailyGemStipend = old.dailyGemStipend;
            // Identity, not progress. See SaveData.playerId.
            fresh.playerId = old.playerId;
            fresh.prestigeRetired = true;
            fresh.legacyIncomeMultiplier = old.legacyIncomeMultiplier > 1d
                ? old.legacyIncomeMultiplier
                : 1d;
            return fresh;
        }
    }
}
