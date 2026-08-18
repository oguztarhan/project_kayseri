using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    /// <summary>
    /// The wear curve, without leaving a phone alone for three days.
    ///
    /// Everything a player will feel about this mechanic is in these numbers: whether playing daily
    /// keeps an island clean, how bad a fortnight away actually is, and whether the repair bill is a
    /// sink or a wall. They are the whole reason the maths is not inside the service.
    /// </summary>
    public class MaintenanceTests
    {
        private const long Hour = 3600L;
        private static Maintenance.Tuning T => Maintenance.Tuning.Default;

        // ---- the grace window -------------------------------------------------------------------

        [Test]
        public void AnOpenGame_NeverWears()
        {
            // the service re-reads the clock every minute; each of those gaps must cost nothing, or
            // the island would rot while the player sat watching it
            float c = 1f;
            for (int minute = 0; minute < 120; minute++) c = Maintenance.Decay(c, 60L, 1f, T);

            Assert.That(c, Is.EqualTo(1f));
        }

        [Test]
        public void ADailyPlayer_NeverWears()
        {
            float c = Maintenance.Decay(1f, 8 * Hour, Maintenance.Wear[IslandEconomy.Mine], T);

            // eight hours is the promise: sleep, a working day, a flight. None of them cost anything.
            Assert.That(c, Is.EqualTo(1f));
        }

        [Test]
        public void PastTheGrace_OnlyTheExcessIsCharged()
        {
            // 10h away with an 8h grace is 2h of wear, not 10 — the same as a straight 2h gap would
            // be if the grace were zero
            float ten = Maintenance.Decay(1f, 10 * Hour, 1f, T);

            var noGrace = T;
            noGrace.GraceHours = 0f;
            noGrace.DecayHours = T.DecayHours - T.GraceHours;
            float two = Maintenance.Decay(1f, 2 * Hour, 1f, noGrace);

            Assert.That(ten, Is.EqualTo(two).Within(1e-5f));
        }

        // ---- the curve --------------------------------------------------------------------------

        [Test]
        public void TheFullWindow_ReachesTheFloorExactly()
        {
            float c = Maintenance.Decay(1f, (long)(T.DecayHours * 3600f), 1f, T);

            Assert.That(c, Is.EqualTo(T.Floor).Within(1e-5f));
        }

        [Test]
        public void AbsenceBottomsOut_RatherThanSpiralling()
        {
            float c = Maintenance.Decay(1f, 30L * 24L * Hour, 1.3f, T);   // a month, on the fastest station

            // the floor is the contract: come back after a month and the island is slow, not dead
            Assert.That(c, Is.EqualTo(T.Floor));
        }

        [Test]
        public void TwoAbsences_StackTowardTheFloor()
        {
            float once = Maintenance.Decay(1f, 40 * Hour, 1f, T);
            float twice = Maintenance.Decay(once, 40 * Hour, 1f, T);

            // without this a player could take a long weekend every weekend and never repair anything
            Assert.That(twice, Is.LessThan(once));
            Assert.That(twice, Is.EqualTo(T.Floor).Within(1e-5f));
        }

        [Test]
        public void TheMineWearsFasterThanTheTown()
        {
            long away = 30 * Hour;
            float mine = Maintenance.Decay(1f, away, Maintenance.Wear[IslandEconomy.Mine], T);
            float market = Maintenance.Decay(1f, away, Maintenance.Wear[IslandEconomy.Market], T);

            // the districts must not arrive at the same shade of brown, or the island reads as
            // recoloured rather than as neglected
            Assert.That(mine, Is.LessThan(market));
        }

        [Test]
        public void AClockRolledBackwards_RepairsNothing()
        {
            float c = Maintenance.Decay(0.7f, -9999L, 1f, T);

            Assert.That(c, Is.EqualTo(0.7f).Within(1e-6f));
        }

        [Test]
        public void EveryStationHasAWearRate()
        {
            Assert.That(Maintenance.Wear.Length, Is.EqualTo(Maintenance.Stations));
            Assert.That(Maintenance.Stations, Is.EqualTo(IslandEconomy.Stations.Length));
        }

        // ---- what it costs the island -----------------------------------------------------------

        [Test]
        public void AnIslandRunsAtItsWorstStation()
        {
            var c = Maintenance.NewConditions();
            c[IslandEconomy.Smelter] = 0.6f;

            // the chain is serial, so one seized station is the island's speed — an average here
            // would quietly promise that neglecting one building barely matters
            Assert.That(Maintenance.IslandCondition(c), Is.EqualTo(0.6f).Within(1e-6f));
        }

        [Test]
        public void DamageIsNormalisedAgainstTheFloor()
        {
            Assert.That(Maintenance.Damage(1f, T), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(Maintenance.Damage(T.Floor, T), Is.EqualTo(1f).Within(1e-6f));
        }

        // ---- what it costs the player -----------------------------------------------------------

        [Test]
        public void AWhollyNeglectedIsland_CostsTenMinutesOfItsOwnIncome()
        {
            const double ratePerMin = 5000d;
            var c = Maintenance.NewConditions();
            for (int s = 0; s < c.Length; s++) c[s] = T.Floor;

            double bill = 0d;
            for (int s = 0; s < c.Length; s++) bill += Maintenance.RepairCost(c[s], ratePerMin, T);

            // the whole balance of the mechanic in one number: enough to be a sink, never enough to
            // be a wall. Ten minutes of production to undo three days of neglect.
            Assert.That(bill / ratePerMin, Is.EqualTo(10d).Within(0.01d));
        }

        [Test]
        public void APerfectStation_CostsNothing()
        {
            Assert.That(Maintenance.RepairCost(1f, 5000d, T), Is.EqualTo(0d));
            Assert.That(Maintenance.RepairSeconds(1f, T), Is.EqualTo(0f));
        }

        [Test]
        public void AnIslandThatEarnsNothing_RepairsForNothing()
        {
            // a fresh island has no income to price a repair against, and charging against a rate it
            // has not got yet would be a wall in front of the one thing the player cannot skip
            Assert.That(Maintenance.RepairCost(T.Floor, 0d, T), Is.EqualTo(0d));
        }

        // ---- what wear does to the island -------------------------------------------------------

        private static IslandEconomy Worn(int station, float condition)
        {
            var econ = new IslandEconomy(IslandEconomy.Tuning.Default, IslandEconomy.NewLevels(), null);
            var c = Maintenance.NewConditions();
            c[station] = condition;
            econ.SetConditions(c);
            return econ;
        }

        private static IslandEconomy Fresh()
            => new IslandEconomy(IslandEconomy.Tuning.Default, IslandEconomy.NewLevels(), null);

        [Test]
        public void AWornMine_PausesLonger()
        {
            // dwell DIVIDES, so the worn island must come out with the BIGGER number here
            Assert.That(Worn(IslandEconomy.Mine, 0.55f).MineDwell,
                        Is.GreaterThan(Fresh().MineDwell));
        }

        [Test]
        public void AWornSmelter_BurnsSlower()
        {
            Assert.That(Worn(IslandEconomy.Smelter, 0.55f).SmeltRate,
                        Is.EqualTo(Fresh().SmeltRate * 0.55f).Within(1e-4f));
        }

        [Test]
        public void AWornPowerPlant_ReachesTheTrainAndBothFleets()
        {
            IslandEconomy worn = Worn(IslandEconomy.Power, 0.5f), fresh = Fresh();

            // the one station that touches everything else — its state of repair has to travel
            Assert.That(worn.TrainSpeed, Is.LessThan(fresh.TrainSpeed));
            Assert.That(worn.OreTruckSpeed, Is.LessThan(fresh.OreTruckSpeed));
            Assert.That(worn.CargoTruckSpeed, Is.LessThan(fresh.CargoTruckSpeed));
        }

        [Test]
        public void WearNeverTouchesPrice()
        {
            IslandEconomy worn = Worn(IslandEconomy.Market, Maintenance.Tuning.Default.Floor);

            // a neglected market sells SLOWER, but a bar is worth what a bar is worth. Wear is a
            // throughput penalty; the moment it starts discounting the goods it is a second, hidden
            // economy nobody tuned.
            Assert.That(worn.BarPrice, Is.EqualTo(Fresh().BarPrice).Within(1e-6f));
            Assert.That(worn.MarketDwell, Is.GreaterThan(Fresh().MarketDwell));
        }

        [Test]
        public void WearNeverTouchesCostsOrCapacities()
        {
            IslandEconomy worn = Worn(IslandEconomy.Storage, Maintenance.Tuning.Default.Floor), fresh = Fresh();

            // charging more to upgrade a worn station would be a second penalty nobody asked for, and
            // shrinking its yard would make the visible heap jump about as the island decayed
            Assert.That(worn.AxisCost(IslandEconomy.Storage, 0),
                        Is.EqualTo(fresh.AxisCost(IslandEconomy.Storage, 0)).Within(1e-9d));
            Assert.That(worn.StorageFull, Is.EqualTo(fresh.StorageFull).Within(1e-6f));
        }

        [Test]
        public void APerfectIsland_RunsExactlyAsItDidBeforeAnyOfThis()
        {
            IslandEconomy tracked = Fresh(), untracked = Fresh();
            tracked.SetConditions(Maintenance.NewConditions());   // all ones

            // the regression that matters most: switching the mechanic on must not move a single
            // number on an island nobody has neglected
            Assert.That(tracked.SmeltRate, Is.EqualTo(untracked.SmeltRate).Within(1e-6f));
            Assert.That(tracked.MineDwell, Is.EqualTo(untracked.MineDwell).Within(1e-6f));
            Assert.That(tracked.TrainSpeed, Is.EqualTo(untracked.TrainSpeed).Within(1e-6f));
            Assert.That(tracked.MarketDwell, Is.EqualTo(untracked.MarketDwell).Within(1e-6f));
        }

        [Test]
        public void TheMaintenanceBonus_RunsTheIslandAboveNormal()
        {
            IslandEconomy boosted = Worn(IslandEconomy.Smelter, 1.10f);

            // the bonus arrives through the same array as the damage and is deliberately not clamped
            Assert.That(boosted.SmeltRate, Is.GreaterThan(Fresh().SmeltRate));
        }

        [Test]
        public void CrewTime_ScalesWithTheDamage()
        {
            float light = Maintenance.RepairSeconds(0.95f, T);
            float heavy = Maintenance.RepairSeconds(T.Floor, T);

            Assert.That(light, Is.GreaterThanOrEqualTo(T.RepairSecondsMin));
            Assert.That(light, Is.LessThan(heavy));
            Assert.That(heavy, Is.EqualTo(T.RepairSecondsMax).Within(1e-4f));
        }
    }
}
