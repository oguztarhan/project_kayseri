using Game.Core;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// The update wipe. These lock down the two halves of it that are easy to get wrong: that a
    /// save from the previous build really is cleared, and that the things a player paid money
    /// for really are not.
    /// </summary>
    public class SaveMigrationTests
    {
        /// <summary>A save as a tester arrives with it: version 3, every island maxed, rich.</summary>
        private static SaveData VeteranSave()
        {
            var d = new SaveData { version = 3, savedUnixSeconds = 1_700_000_000L, incomeRatePerSec = 1234d };
            d.wallet.cash = new BigDouble(9.9d, 12);
            d.wallet.lifetimeCash = new BigDouble(4.2d, 14);
            d.wallet.investors = 850d;
            d.wallet.gems = 640;
            d.stationLevels.Add(new StationLevel { id = "mine", level = 50 });
            d.islandLevels.Add(new StationLevel { id = "coal_mine0", level = 50 });
            d.islandLevels.Add(new StationLevel { id = "worldactive", level = 7 });
            d.islandRates.Add(new IslandRate { id = "coal", perMin = 102626d });
            d.unlockedIslands.AddRange(new[] { "copper", "iron", "silver", "gold", "ruby", "emerald", "diamond" });
            d.unlockedMountains.Add("dag1");
            d.hiredManagers.Add("mine");
            d.dailyStreak = 6;
            d.boostMultiplier = 2d;
            d.boostEndUnix = 1_700_090_000L;
            // ...and a wallet's worth of real purchases behind it
            d.adsRemoved = true;
            d.purchasedOffers.AddRange(new[] { "remove_ads", "gece_vardiyasi" });
            d.stationSpeedMultiplier = 2d;
            d.offlineEfficiencyBonus = 0.2d;
            d.offlineCapBonusSeconds = 7200L;
            d.dailyRewardBonusMult = 1d;
            d.freeRewardBonusCharges = 2;
            d.dailyGemStipend = 15L;
            return d;
        }

        [Test]
        public void SaveFromTheOldEconomyIsReset()
        {
            Assert.IsTrue(SaveMigration.NeedsReset(VeteranSave()));
            Assert.IsTrue(SaveMigration.NeedsReset(null));
        }

        [Test]
        public void SaveFromThisBuildIsLeftAlone()
        {
            Assert.IsFalse(SaveMigration.NeedsReset(new SaveData()));
        }

        /// <summary>Wiping once is the feature; wiping every launch is a bug. The reset stamps the version.</summary>
        [Test]
        public void ResettingDoesNotArmAnotherReset()
        {
            var fresh = SaveMigration.Reset(VeteranSave());
            Assert.AreEqual(SaveMigration.CurrentVersion, fresh.version);
            Assert.IsFalse(SaveMigration.NeedsReset(fresh));
        }

        [Test]
        public void ResetClearsEveryTraceOfTheOldRun()
        {
            var fresh = SaveMigration.Reset(VeteranSave());

            Assert.IsTrue(fresh.wallet.cash.IsZero, "cash");
            Assert.IsTrue(fresh.wallet.lifetimeCash.IsZero, "lifetime cash feeds prestige");
            Assert.AreEqual(0d, fresh.wallet.investors, "prestige multiplier");
            CollectionAssert.IsEmpty(fresh.stationLevels);
            CollectionAssert.IsEmpty(fresh.islandLevels, "levels AND the active island live here");
            CollectionAssert.IsEmpty(fresh.islandRates);
            CollectionAssert.IsEmpty(fresh.unlockedIslands, "back to coal only");
            CollectionAssert.IsEmpty(fresh.unlockedMountains);
            CollectionAssert.IsEmpty(fresh.hiredManagers);
            Assert.AreEqual(0, fresh.dailyStreak);
            Assert.AreEqual(0d, fresh.boostMultiplier, "a boost mid-flight would distort the fresh pacing");
        }

        /// <summary>
        /// The offline grant reads these two. Carrying them over would pay the new run for the
        /// time the player spent away from the OLD one, at the old empire's rate.
        /// </summary>
        [Test]
        public void ResetStopsTheOfflineClock()
        {
            var fresh = SaveMigration.Reset(VeteranSave());
            Assert.AreEqual(0L, fresh.savedUnixSeconds);
            Assert.AreEqual(0d, fresh.incomeRatePerSec);
        }

        [Test]
        public void ResetKeepsWhatWasPaidFor()
        {
            var fresh = SaveMigration.Reset(VeteranSave());

            Assert.AreEqual(640L, fresh.wallet.gems);
            Assert.IsTrue(fresh.adsRemoved);
            CollectionAssert.AreEquivalent(new[] { "remove_ads", "gece_vardiyasi" }, fresh.purchasedOffers);
            Assert.AreEqual(2d, fresh.stationSpeedMultiplier, 1e-9);
            Assert.AreEqual(0.2d, fresh.offlineEfficiencyBonus, 1e-9);
            Assert.AreEqual(7200L, fresh.offlineCapBonusSeconds);
            Assert.AreEqual(1d, fresh.dailyRewardBonusMult, 1e-9);
            Assert.AreEqual(2, fresh.freeRewardBonusCharges);
            Assert.AreEqual(15L, fresh.dailyGemStipend);
        }

        /// <summary>The reset must not write through to the save it was handed.</summary>
        [Test]
        public void ResetDoesNotMutateTheOldSave()
        {
            var old = VeteranSave();
            SaveMigration.Reset(old).purchasedOffers.Add("later");

            Assert.AreEqual(3, old.version);
            Assert.AreEqual(2, old.purchasedOffers.Count);
            Assert.AreEqual(7, old.unlockedIslands.Count);
        }
    }
}
