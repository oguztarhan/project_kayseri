using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The bookkeeping around the wear curve: what an absence does to the save rows, what a repair
    /// does to the wallet, and the sharing contract the island relies on.
    ///
    /// Time is the real clock, so the absences here are staged by winding the save's own stamp
    /// backwards rather than by waiting. That covers everything except the crew's working minutes,
    /// which cannot be fast-forwarded without a fake clock — <see cref="MaintenanceService.SkipRepair"/>
    /// runs the same completion path, so what is left untested is the lerp in between.
    /// </summary>
    public class MaintenanceServiceTests
    {
        private const string Coal = "coal";
        private const long Hour = 3600L;

        private static MaintenanceService Build(out SaveData data, out WalletService wallet,
                                                double startingCash = 1e9d)
        {
            data = new SaveData();
            data.wallet.cash = new BigDouble(startingCash);
            wallet = new WalletService(data.wallet);
            return new MaintenanceService(data, new TimeService(), wallet,
                                          Maintenance.Tuning.Default, true);
        }

        /// <summary>Stages an absence of <paramref name="hours"/> ending now.</summary>
        private static void Away(SaveData data, float hours)
        {
            long now = new TimeService().NowUnix();
            data.conditionStampUnix = now - (long)(hours * 3600f);
            data.savedUnixSeconds = data.conditionStampUnix;
        }

        // ---- wearing ----------------------------------------------------------------------------

        [Test]
        public void AFirstLaunch_WearsNothing()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);                    // the island registers itself

            m.Evaluate();                          // no stamp on disk: the update just landed

            Assert.That(m.NeedsRepair(Coal), Is.False);
            Assert.That(data.conditionStampUnix, Is.GreaterThan(0L));
        }

        [Test]
        public void ALongAbsence_WearsEveryStation()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 40f);

            m.Evaluate();

            Assert.That(m.NeedsRepair(Coal), Is.True);
            Assert.That(m.WornCount(Coal), Is.EqualTo(Maintenance.Stations));
            Assert.That(m.IslandCondition(Coal), Is.LessThan(1f));
        }

        [Test]
        public void TheSecondEvaluate_ChargesNothingMore()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 40f);

            m.Evaluate();
            float after = m.IslandCondition(Coal);
            m.Evaluate();

            // Evaluate restamps, so the same hours must never be charged twice. This is the one that
            // would be caught late and look like the mechanic being unfair.
            Assert.That(m.IslandCondition(Coal), Is.EqualTo(after).Within(1e-6f));
        }

        [Test]
        public void AnIslandNeverAskedAbout_IsNotInTheSave()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);

            Assert.That(data.conditions.Count, Is.EqualTo(0));

            m.Conditions(Coal);
            Assert.That(data.conditions.Count, Is.EqualTo(1));
            Assert.That(data.conditions[0].id, Is.EqualTo(Coal));
        }

        [Test]
        public void TheConditionArray_IsSharedRatherThanCopied()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);

            float[] first = m.Conditions(Coal);
            Away(data, 40f);
            m.Evaluate();

            // the island holds this array from Awake and reads it every frame; handing out a fresh
            // copy would leave the simulation running on a snapshot of a clean island forever
            Assert.That(m.Conditions(Coal), Is.SameAs(first));
            Assert.That(first[IslandEconomy.Mine], Is.LessThan(1f));
        }

        // ---- repairing --------------------------------------------------------------------------

        [Test]
        public void ARepair_TakesTheMoneyAndPutsACrewOnSite()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 40f);
            m.Evaluate();

            double before = wallet.Cash.ToDouble();
            bool started = m.TryRepair(Coal, IslandEconomy.Mine, 5000d);

            Assert.That(started, Is.True);
            Assert.That(wallet.Cash.ToDouble(), Is.LessThan(before));
            Assert.That(m.Repairing(Coal), Is.True);
            Assert.That(m.Repairing(Coal, IslandEconomy.Mine), Is.True);
            Assert.That(m.Repairing(Coal, IslandEconomy.Smelter), Is.False);
        }

        [Test]
        public void ASecondCrew_GoesOutOnADifferentBuilding()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 40f);
            m.Evaluate();
            m.TryRepair(Coal, IslandEconomy.Mine, 5000d);

            Assert.That(m.TryRepair(Coal, IslandEconomy.Smelter, 5000d), Is.True);
            Assert.That(m.Repairing(Coal, IslandEconomy.Mine), Is.True);
            Assert.That(m.Repairing(Coal, IslandEconomy.Smelter), Is.True);
        }

        [Test]
        public void ABuildingAlreadyUnderRepair_IsNotStartedTwice()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 40f);
            m.Evaluate();
            m.TryRepair(Coal, IslandEconomy.Mine, 5000d);

            double before = wallet.Cash.ToDouble();

            Assert.That(m.TryRepair(Coal, IslandEconomy.Mine, 5000d), Is.False);
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(before));   // and charged nothing for the refusal
        }

        [Test]
        public void RepairAll_DoesNotChargeForTheCrewsAlreadyOut()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 200f);
            m.Evaluate();

            double whole = m.RepairCostAll(Coal, 5000d);
            m.TryRepair(Coal, IslandEconomy.Mine, 5000d);
            double rest = m.RepairCostAll(Coal, 5000d);

            // the mine's share, and only the mine's share, has come off the quote
            Assert.That(rest, Is.LessThan(whole));
            Assert.That(whole - rest,
                        Is.EqualTo(Maintenance.RepairCost(Maintenance.Tuning.Default.Floor, 5000d,
                                                          Maintenance.Tuning.Default)).Within(1e-6d));
        }

        [Test]
        public void APerfectStation_CannotBeRepaired()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);

            Assert.That(m.TryRepair(Coal, IslandEconomy.Mine, 5000d), Is.False);
            Assert.That(m.TryRepair(Coal, -1, 5000d), Is.False);
        }

        [Test]
        public void AnEmptyWallet_ChangesNothing()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet, 0d);
            m.Conditions(Coal);
            Away(data, 40f);
            m.Evaluate();
            float worn = m.IslandCondition(Coal);

            Assert.That(m.TryRepair(Coal, -1, 5000d), Is.False);
            Assert.That(m.Repairing(Coal), Is.False);
            Assert.That(m.IslandCondition(Coal), Is.EqualTo(worn).Within(1e-6f));
        }

        [Test]
        public void FinishingTheIsland_RestoresItAndPaysTheBonus()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 40f);
            m.Evaluate();

            m.TryRepair(Coal, -1, 5000d);
            m.SkipRepair(Coal);                    // what the rewarded ad buys

            Assert.That(m.NeedsRepair(Coal), Is.False);
            Assert.That(m.Repairing(Coal), Is.False);
            Assert.That(m.BonusActive(Coal), Is.True);

            // the bonus is a buff, not a state of repair: the simulation runs above 1 while the save
            // row stays at exactly 1, so the next absence decays from new rather than from 1.1
            Assert.That(m.Condition(Coal, IslandEconomy.Mine),
                        Is.EqualTo(Maintenance.Tuning.Default.BonusMultiplier).Within(1e-5f));
            Assert.That(m.StateOfRepair(Coal, IslandEconomy.Mine), Is.EqualTo(1f));
        }

        [Test]
        public void FinishingOneStationOfMany_PaysNoBonus()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 40f);
            m.Evaluate();

            m.TryRepair(Coal, IslandEconomy.Mine, 5000d);
            m.SkipRepair(Coal);

            Assert.That(m.StateOfRepair(Coal, IslandEconomy.Mine), Is.EqualTo(1f));
            Assert.That(m.NeedsRepair(Coal), Is.True);        // seven still worn
            Assert.That(m.BonusActive(Coal), Is.False);
        }

        [Test]
        public void ABoughtIsland_ArrivesAsNew()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 100f);
            m.Evaluate();
            Assert.That(m.NeedsRepair(Coal), Is.True);

            m.Reset(Coal);

            // rows wear whether or not anyone owns them yet, so the purchase has to wipe one — a
            // brand-new island handed over already filthy is indefensible
            Assert.That(m.NeedsRepair(Coal), Is.False);
            Assert.That(m.BonusActive(Coal), Is.False);
        }

        // ---- the shield -------------------------------------------------------------------------

        /// <summary>Stages a shield with <paramref name="hours"/> left to run.</summary>
        private static void Shield(SaveData data, float hours)
            => data.shieldEndUnix = new TimeService().NowUnix() + (long)(hours * 3600f);

        [Test]
        public void ShieldCoveringTheAbsence_WearsNothing()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 40f);          // long enough to wear every station without one
            Shield(data, 4f);         // ...but the whole gap was paid for, and there is time left

            m.Evaluate();

            Assert.That(m.NeedsRepair(Coal), Is.False);
            Assert.That(m.IslandCondition(Coal), Is.EqualTo(1f));
        }

        [Test]
        public void ShieldThatExpiredMidAbsence_ChargesOnlyTheRemainder()
        {
            SaveData shielded; WalletService wallet;
            MaintenanceService a = Build(out shielded, out wallet);
            a.Conditions(Coal);
            Away(shielded, 40f);
            // Ran out ten hours ago: 30 of the 40 hours were covered, so what bites is a 10-hour
            // absence — and 10 is inside the 8-hour grace by only two hours.
            shielded.shieldEndUnix = new TimeService().NowUnix() - 10L * Hour;
            a.Evaluate();

            SaveData bare;
            MaintenanceService b = Build(out bare, out wallet);
            b.Conditions(Coal);
            Away(bare, 10f);
            b.Evaluate();

            Assert.That(a.IslandCondition(Coal), Is.EqualTo(b.IslandCondition(Coal)).Within(0.0001f));
            Assert.That(a.IslandCondition(Coal), Is.LessThan(1f));   // it did bite, just not for 40h
        }

        [Test]
        public void ShieldIsSpentBeforeTheGraceWindowIs()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 12f);          // unshielded this wears: 8 hours free, 4 hours biting
            // Ran out an hour ago, so it covered the first eleven. Charging the grace FIRST would
            // leave three biting hours to pay for; charging the shield first leaves one, and the
            // grace then swallows it whole.
            data.shieldEndUnix = new TimeService().NowUnix() - Hour;

            m.Evaluate();

            Assert.That(m.NeedsRepair(Coal), Is.False);
        }

        [Test]
        public void BuyingAShield_RepairsEverythingOnTheSpot()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 200f);
            m.Evaluate();
            Assert.That(m.NeedsRepair(Coal), Is.True, "arrange: the island should be worn");

            m.AddShield(8f);

            Assert.That(m.NeedsRepair(Coal), Is.False);
            Assert.That(m.IslandCondition(Coal), Is.EqualTo(1f));
            Assert.That(m.ShieldActive, Is.True);
            Assert.That(m.ShieldSecondsLeft, Is.EqualTo(8f * 3600f).Within(2f));
            // The free top-up is the sale, not a completed repair — no crew, so no crew's bonus.
            Assert.That(m.BonusActive(Coal), Is.False);
        }

        [Test]
        public void ASecondShield_ExtendsRatherThanReplaces()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);

            m.AddShield(24f);
            m.AddShield(8f);          // the shorter card must not shorten the longer one

            Assert.That(m.ShieldSecondsLeft, Is.EqualTo(32f * 3600f).Within(2f));
        }

        [Test]
        public void AShieldBoughtMidRepair_DropsTheCrewAndPaysNoBonus()
        {
            SaveData data; WalletService wallet;
            MaintenanceService m = Build(out data, out wallet);
            m.Conditions(Coal);
            Away(data, 200f);
            m.Evaluate();
            Assert.That(m.TryRepair(Coal, -1, 5000d), Is.True, "arrange: a crew should be out");

            m.AddShield(8f);

            Assert.That(m.Repairing(Coal), Is.False);
            Assert.That(m.IslandCondition(Coal), Is.EqualTo(1f));
            Assert.That(m.BonusActive(Coal), Is.False);
        }

        [Test]
        public void DisabledMaintenance_SellsNoShield()
        {
            var data = new SaveData();
            data.wallet.cash = new BigDouble(1e9d);
            var wallet = new WalletService(data.wallet);
            var m = new MaintenanceService(data, new TimeService(), wallet,
                                           Maintenance.Tuning.Default, false);

            m.AddShield(24f);

            Assert.That(m.ShieldActive, Is.False);
            Assert.That(data.shieldEndUnix, Is.EqualTo(0L));
        }

        // ---- the off switch ---------------------------------------------------------------------

        [Test]
        public void Disabled_WearsNothingAndChargesNothing()
        {
            var data = new SaveData();
            data.wallet.cash = new BigDouble(1e9d);
            var wallet = new WalletService(data.wallet);
            var m = new MaintenanceService(data, new TimeService(), wallet,
                                           Maintenance.Tuning.Default, false);
            m.Conditions(Coal);
            Away(data, 200f);

            m.Evaluate();

            Assert.That(m.NeedsRepair(Coal), Is.False);
            Assert.That(m.IslandCondition(Coal), Is.EqualTo(1f));
            Assert.That(m.TryRepair(Coal, -1, 5000d), Is.False);
        }
    }
}
