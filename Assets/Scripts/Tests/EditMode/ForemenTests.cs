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
        }

        // ---- stars promote tiers -----------------------------------------------------------------

        [Test]
        public void EveryTier_IsTwoStarsWide()
        {
            Assert.That(Foremen.TierOf(1), Is.EqualTo(Foremen.Tier.Common));
            Assert.That(Foremen.TierOf(2), Is.EqualTo(Foremen.Tier.Common));
            Assert.That(Foremen.TierOf(3), Is.EqualTo(Foremen.Tier.Rare));
            Assert.That(Foremen.TierOf(4), Is.EqualTo(Foremen.Tier.Rare));
            Assert.That(Foremen.TierOf(5), Is.EqualTo(Foremen.Tier.Epic));
            Assert.That(Foremen.TierOf(6), Is.EqualTo(Foremen.Tier.Epic));
            Assert.That(Foremen.TierOf(7), Is.EqualTo(Foremen.Tier.Legendary));
            Assert.That(Foremen.TierOf(8), Is.EqualTo(Foremen.Tier.Legendary));
            Assert.That(Foremen.TierOf(9), Is.EqualTo(Foremen.Tier.Mythic));
            Assert.That(Foremen.TierOf(Foremen.MaxLevel), Is.EqualTo(Foremen.Tier.Mythic));
        }

        [Test]
        public void TheTopStar_IsTheTopTier()
        {
            // The card frames, the plinth and his size on the island all index a tint array by tier.
            // If MaxLevel ever moves past the last tier floor, every one of them reads out of range.
            Assert.That((int)Foremen.TierOf(Foremen.MaxLevel), Is.EqualTo(Foremen.TierCount - 1));
            Assert.That((int)Foremen.TierOf(Foremen.MaxLevel * 100), Is.EqualTo(Foremen.TierCount - 1));
        }

        [Test]
        public void AnEmptySlot_ReadsAsTheLockedColour()
        {
            Assert.That(Foremen.TierOf(0), Is.EqualTo(Foremen.Tier.Common));
            Assert.That(Foremen.TierOfStation(Empty(), IslandEconomy.Mine), Is.EqualTo(Foremen.Tier.Common));
            Assert.That(Foremen.IsHired(Empty(), IslandEconomy.Mine), Is.False);
        }

        // ---- what a star is worth ------------------------------------------------------------------

        [Test]
        public void LegendaryTopsOutAtTripleOutput()
        {
            // +300% at the last Legendary star is the number the card advertises and the one the
            // feature was asked for. A tuning change that moves it is changing the promise.
            Assert.That(Foremen.Boost(8, T), Is.EqualTo(3.00d).Within(1e-9));
            Assert.That(Foremen.StationMultiplier(With(IslandEconomy.Mine, 8), IslandEconomy.Mine, T),
                        Is.EqualTo(4.00d).Within(1e-9));
        }

        [Test]
        public void EveryStar_IsWorthMoreThanTheOneBelow()
        {
            double prev = 0d;
            for (int stars = 1; stars <= Foremen.MaxLevel; stars++)
            {
                double boost = Foremen.Boost(stars, T);
                Assert.That(boost, Is.GreaterThan(prev), "star " + stars);
                prev = boost;
            }
        }

        [Test]
        public void APromotion_IsWorthMoreThanAStarInsideATier()
        {
            // The second star of a tier should be a step and the first star of the next should be a
            // jump — that is what makes a promotion something the player feels rather than reads.
            for (int tier = 0; tier < Foremen.TierCount - 1; tier++)
            {
                int lastOfTier = 2 * tier + 2;                          // 2, 4, 6, 8
                double inside = Foremen.Boost(lastOfTier, T) - Foremen.Boost(lastOfTier - 1, T);
                double across = Foremen.Boost(lastOfTier + 1, T) - Foremen.Boost(lastOfTier, T);
                Assert.That(across, Is.GreaterThan(inside), "promotion into tier " + (tier + 1));
            }
        }

        [Test]
        public void AMaster_OnlySpeedsTheirOwnStation()
        {
            var l = With(IslandEconomy.Smelter, Foremen.MaxLevel);
            Assert.That(Foremen.StationMultiplier(l, IslandEconomy.Smelter, T), Is.GreaterThan(1d));
            Assert.That(Foremen.StationMultiplier(l, IslandEconomy.Mine, T), Is.EqualTo(1d).Within(1e-9));
        }

        [Test]
        public void IncomeMultiplier_IsAShareOfTheWholeRoster()
        {
            var l = Foremen.NewLevels();
            l[IslandEconomy.Mine] = 3;
            l[IslandEconomy.Train] = 5;
            double expected = 1d + (Foremen.Boost(3, T) + Foremen.Boost(5, T)) * T.IncomeShare;
            Assert.That(Foremen.IncomeMultiplier(l, T), Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void AllLegendary_LandsOnTheIntendedSecondGear()
        {
            // The roster replaced a retired prestige that handed out 70x at coal, which the economy
            // pass measured as the thing breaking the ladder. A Legendary roster must land where the
            // old maxed roster did — 3.4x — because that is where the ladder was solved.
            var l = Foremen.NewLevels();
            for (int s = 0; s < Foremen.Count; s++) l[s] = 8;
            Assert.That(Foremen.IncomeMultiplier(l, T), Is.EqualTo(3.4d).Within(0.05d));
        }

        [Test]
        public void FullMythic_StretchesTheTailWithoutBreakingIt()
        {
            var l = Foremen.NewLevels();
            for (int s = 0; s < Foremen.Count; s++) l[s] = Foremen.MaxLevel;
            double m = Foremen.IncomeMultiplier(l, T);
            Assert.That(m, Is.GreaterThan(4.5d));
            Assert.That(m, Is.LessThan(5.5d), "still an order of magnitude below the 70x that broke the ladder");
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
        public void AMaxedMaster_CostsNothingFurther()
        {
            Assert.That(Foremen.DuplicatesToLevel(Foremen.MaxLevel, T), Is.Zero);
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
        public void TheCardCurve_IsUnchangedFromBeforeTheMastersRework()
        {
            // Live saves carry banked cards against this exact curve, and every roster screen in the
            // wild is drawing a have/need bar from it. Ninety per master is the number those bars were
            // filled against — moving it would silently rewrite how far along every player is.
            Assert.That(Foremen.DuplicatesToMax(T), Is.EqualTo(90));
            Assert.That(Foremen.DuplicatesToLevel(1, T), Is.EqualTo(2));
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
