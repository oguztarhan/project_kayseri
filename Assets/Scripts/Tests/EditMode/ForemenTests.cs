using NUnit.Framework;
using Game.Core;

namespace Game.Tests
{
    public class ForemenTests
    {
        private static Foremen.Tuning T => Foremen.Tuning.Default;

        private static int[] Empty() => Foremen.NewLevels();

        private static int[] With(int station, int level)
        {
            var l = Foremen.NewLevels();
            l[station] = level;
            return l;
        }

        // ---- an empty roster must change nothing anywhere ----------------------------------------

        [Test]
        public void EmptyRoster_PaysNothing()
        {
            Assert.That(Foremen.IncomeMultiplier(Empty(), T), Is.EqualTo(1d).Within(1e-9));
            for (int s = 0; s < Foremen.Count; s++)
                Assert.That(Foremen.StationMultiplier(Empty(), s, T), Is.EqualTo(1d).Within(1e-9),
                            "station " + s);
        }

        [Test]
        public void NullRoster_IsTreatedAsEmpty()
        {
            Assert.That(Foremen.IncomeMultiplier(null, T), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Foremen.StationMultiplier(null, IslandEconomy.Mine, T), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Foremen.HiredCount(null), Is.Zero);
        }

        // ---- the roster is exactly the station list ----------------------------------------------

        [Test]
        public void SlotCount_MatchesTheStationList()
        {
            // Saves address foremen by station index. If these ever drift apart, every roster in the
            // wild silently points at the wrong station.
            Assert.That(Foremen.Count, Is.EqualTo(IslandEconomy.Stations.Length));
            Assert.That(Foremen.Slots.Length, Is.EqualTo(Foremen.Count));
        }

        // ---- what a level is worth ----------------------------------------------------------------

        [Test]
        public void OneLevel_IsWorthItsRarity()
        {
            Assert.That(Foremen.StationMultiplier(With(IslandEconomy.Mine, 1), IslandEconomy.Mine, T),
                        Is.EqualTo(1d + T.EpicPerLevel).Within(1e-9));
            Assert.That(Foremen.StationMultiplier(With(IslandEconomy.Train, 1), IslandEconomy.Train, T),
                        Is.EqualTo(1d + T.CommonPerLevel).Within(1e-9));
        }

        [Test]
        public void AForeman_OnlySpeedsTheirOwnStation()
        {
            var l = With(IslandEconomy.Smelter, Foremen.MaxLevel);
            Assert.That(Foremen.StationMultiplier(l, IslandEconomy.Smelter, T), Is.GreaterThan(1d));
            Assert.That(Foremen.StationMultiplier(l, IslandEconomy.Mine, T), Is.EqualTo(1d).Within(1e-9));
        }

        [Test]
        public void IncomeMultiplier_IsTheWholeRosterAdded()
        {
            var l = Foremen.NewLevels();
            l[IslandEconomy.Mine] = 3;      // epic
            l[IslandEconomy.Train] = 5;     // common
            double expected = 1d + T.EpicPerLevel * 3 + T.CommonPerLevel * 5;
            Assert.That(Foremen.IncomeMultiplier(l, T), Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void FullRoster_LandsOnTheIntendedSecondGear()
        {
            // The roster is the replacement for a retired prestige, and it is deliberately an order of
            // magnitude smaller than the 70x prestige used to hand out at coal. If a tuning change
            // moves this far, the ladder needs re-solving with it.
            var l = Foremen.NewLevels();
            for (int s = 0; s < Foremen.Count; s++) l[s] = Foremen.MaxLevel;
            double m = Foremen.IncomeMultiplier(l, T);
            Assert.That(m, Is.GreaterThan(2.5d));
            Assert.That(m, Is.LessThan(4.5d));
        }

        // ---- levels are clamped, not trusted ------------------------------------------------------

        [Test]
        public void LevelsAboveMax_AreClamped()
        {
            var honest = With(IslandEconomy.Mine, Foremen.MaxLevel);
            var tampered = With(IslandEconomy.Mine, Foremen.MaxLevel * 100);
            Assert.That(Foremen.IncomeMultiplier(tampered, T),
                        Is.EqualTo(Foremen.IncomeMultiplier(honest, T)).Within(1e-9));
            Assert.That(Foremen.LevelOf(tampered, IslandEconomy.Mine), Is.EqualTo(Foremen.MaxLevel));
        }

        [Test]
        public void NegativeLevel_ReadsAsUnhired()
        {
            var l = With(IslandEconomy.Mine, -4);
            Assert.That(Foremen.LevelOf(l, IslandEconomy.Mine), Is.EqualTo(Foremen.NotHired));
            Assert.That(Foremen.IsHired(l, IslandEconomy.Mine), Is.False);
            Assert.That(Foremen.IncomeMultiplier(l, T), Is.EqualTo(1d).Within(1e-9));
        }

        [Test]
        public void ShortRoster_DoesNotThrow()
        {
            // A save written before the roster existed arrives short; the service pads it, but the
            // maths must survive being handed one anyway.
            var stunted = new int[2];
            Assert.That(Foremen.IncomeMultiplier(stunted, T), Is.EqualTo(1d).Within(1e-9));
            Assert.That(Foremen.StationMultiplier(stunted, IslandEconomy.Market, T), Is.EqualTo(1d).Within(1e-9));
        }

        // ---- the cost of the road ------------------------------------------------------------------

        [Test]
        public void LevellingCost_GrowsWithLevel()
        {
            int prev = 0;
            for (int level = 1; level < Foremen.MaxLevel; level++)
            {
                int cards = Foremen.DuplicatesToLevel(level, T);
                Assert.That(cards, Is.GreaterThan(prev), "level " + level);
                prev = cards;
            }
        }

        [Test]
        public void AMaxedForeman_CostsNothingFurther()
        {
            Assert.That(Foremen.DuplicatesToLevel(Foremen.MaxLevel, T), Is.Zero);
            Assert.That(Foremen.GemsToLevel(Foremen.MaxLevel, T), Is.Zero);
        }

        [Test]
        public void DuplicatesToMax_IsTheSumOfEveryStep()
        {
            int sum = 0;
            for (int level = 1; level < Foremen.MaxLevel; level++) sum += Foremen.DuplicatesToLevel(level, T);
            Assert.That(Foremen.DuplicatesToMax(T), Is.EqualTo(sum));
            Assert.That(Foremen.DuplicatesToMax(T), Is.GreaterThan(50), "the long tail must actually be long");
        }

        [Test]
        public void RarerSlots_CostMoreToHire()
        {
            Assert.That(Foremen.HireGems(IslandEconomy.Mine, T),      // epic
                        Is.GreaterThan(Foremen.HireGems(IslandEconomy.Smelter, T)));   // rare
            Assert.That(Foremen.HireGems(IslandEconomy.Smelter, T),   // rare
                        Is.GreaterThan(Foremen.HireGems(IslandEconomy.Train, T)));     // common
        }

        // ---- completion --------------------------------------------------------------------------

        [Test]
        public void RosterComplete_OnlyWhenEverySlotIsMaxed()
        {
            var l = Foremen.NewLevels();
            for (int s = 0; s < Foremen.Count; s++) l[s] = Foremen.MaxLevel;
            Assert.That(Foremen.RosterComplete(l), Is.True);
            Assert.That(Foremen.HiredCount(l), Is.EqualTo(Foremen.Count));

            l[IslandEconomy.Power] = Foremen.MaxLevel - 1;
            Assert.That(Foremen.RosterComplete(l), Is.False);
        }
    }
}
