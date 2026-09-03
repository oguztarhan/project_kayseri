using System;
using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The workshop. Because <see cref="Crafting"/> takes its roll as an argument, every gate on
    /// the ladder — no Epic before 6, no Legendary before 16, no Mythic before 26 — is walked
    /// exactly over the unit interval rather than sampled and hoped over. The service tests pin
    /// the two contracts the maths cannot: a point can never buy nothing, and every scrap teaches.
    /// </summary>
    public class CraftingTests
    {
        private static Crafting.Tuning T => Crafting.Tuning.Default;

        /// <summary>Every grade the unit interval can land on at this level.</summary>
        private static bool[] Reachable(int level, int steps = 4000)
        {
            var seen = new bool[Captains.GradeCount];
            for (int i = 0; i < steps; i++)
                seen[Crafting.RollGrade((i + 0.5d) / steps, level)] = true;
            return seen;
        }

        // ---- the odds table ----------------------------------------------------------------------

        [Test]
        public void RollGradeIsAlwaysARealGradeAtEveryLevel()
        {
            for (int level = 1; level <= Crafting.MaxLevel; level++)
                for (int i = 0; i <= 200; i++)
                    Assert.That(Crafting.RollGrade(i / 200d, level),
                                Is.InRange(0, Captains.GradeCount - 1));
        }

        [Test]
        public void RollsOutsideZeroToOneAreClampedRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => Crafting.RollGrade(-5d, 1));
            Assert.DoesNotThrow(() => Crafting.RollGrade(1d, 1));
            Assert.That(Crafting.RollGrade(-5d, 1), Is.EqualTo(0));
        }

        [Test]
        public void FirstBracketRollsOnlyCommonAndRare()
        {
            for (int level = 1; level <= 5; level++)
            {
                bool[] seen = Reachable(level);
                Assert.That(seen[0], Is.True);
                Assert.That(seen[1], Is.True);
                for (int g = 2; g < Captains.GradeCount; g++)
                    Assert.That(seen[g], Is.False, $"grade {g} reachable at level {level}");
            }
        }

        [Test]
        public void LegendaryOpensAtSixteenAndNotBefore()
        {
            for (int level = 1; level <= 15; level++)
                Assert.That(Reachable(level)[3], Is.False, $"Legendary reachable at level {level}");
            Assert.That(Reachable(16)[3], Is.True);
        }

        [Test]
        public void MythicOpensAtTwentySixAndNotBefore()
        {
            for (int level = 1; level <= 25; level++)
                Assert.That(Reachable(level)[4], Is.False, $"Mythic reachable at level {level}");
            Assert.That(Reachable(26)[4], Is.True);
        }

        [Test]
        public void TheDistributionMatchesTheTable()
        {
            // Sweep the unit interval per bracket: the share landing on a grade IS its probability.
            const int n = 100000;
            for (int bracket = 0; bracket < Crafting.LevelOdds.Length; bracket++)
            {
                int level = bracket * Crafting.BracketSize + 1;
                var count = new int[Captains.GradeCount];
                for (int i = 0; i < n; i++) count[Crafting.RollGrade((i + 0.5d) / n, level)]++;

                double total = 0d;
                foreach (double w in Crafting.LevelOdds[bracket]) total += w;
                for (int g = 0; g < Captains.GradeCount; g++)
                    Assert.That(count[g] / (double)n,
                                Is.EqualTo(Crafting.LevelOdds[bracket][g] / total).Within(0.001d),
                                $"bracket {bracket} grade {g}");
            }
        }

        [Test]
        public void OddsOfSumsToOneAndMatchesTheTable()
        {
            for (int level = 1; level <= Crafting.MaxLevel; level++)
            {
                double sum = 0d;
                for (int g = 0; g < Captains.GradeCount; g++) sum += Crafting.OddsOf(level, g);
                Assert.That(sum, Is.EqualTo(1d).Within(1e-9), $"level {level}");
            }
            Assert.That(Crafting.OddsOf(1, 0), Is.EqualTo(0.82d).Within(1e-9));
            Assert.That(Crafting.OddsOf(30, 4), Is.EqualTo(0.01d).Within(1e-9));
        }

        [Test]
        public void UnlockLevelsReadStraightOffTheTable()
        {
            Assert.That(Crafting.UnlockLevelOf(0), Is.EqualTo(1));
            Assert.That(Crafting.UnlockLevelOf(1), Is.EqualTo(1));
            Assert.That(Crafting.UnlockLevelOf(2), Is.EqualTo(6));
            Assert.That(Crafting.UnlockLevelOf(3), Is.EqualTo(16));
            Assert.That(Crafting.UnlockLevelOf(4), Is.EqualTo(26));
        }

        // ---- the xp curve ------------------------------------------------------------------------

        [Test]
        public void LevelForXpRoundTripsTheCurve()
        {
            for (int level = 1; level <= Crafting.MaxLevel; level++)
            {
                long floor = Crafting.XpForLevel(level);
                Assert.That(Crafting.LevelForXp(floor), Is.EqualTo(level), $"at level {level}'s floor");
                if (level > 1)
                    Assert.That(Crafting.LevelForXp(floor - 1L), Is.EqualTo(level - 1),
                                $"one XP under level {level}'s floor");
            }
        }

        [Test]
        public void TheCurveNeverGetsCheaperAndEndsAtThirty()
        {
            for (int level = 1; level < Crafting.MaxLevel - 1; level++)
                Assert.That(Crafting.XpToNext(level + 1), Is.GreaterThanOrEqualTo(Crafting.XpToNext(level)));
            Assert.That(Crafting.XpToNext(Crafting.MaxLevel), Is.EqualTo(0L));
            Assert.That(Crafting.LevelForXp(long.MaxValue / 2L), Is.EqualTo(Crafting.MaxLevel));
        }

        // ---- the gates ---------------------------------------------------------------------------

        [Test]
        public void GatesCapTheLevelInTens()
        {
            Assert.That(Crafting.CapForGates(0), Is.EqualTo(10));
            Assert.That(Crafting.CapForGates(1), Is.EqualTo(20));
            Assert.That(Crafting.CapForGates(2), Is.EqualTo(30));
            Assert.That(Crafting.CapForGates(3), Is.EqualTo(30));
            Assert.That(Crafting.CapForGates(-1), Is.EqualTo(10));

            long xpFor30 = Crafting.XpForLevel(Crafting.MaxLevel);
            Assert.That(Crafting.LevelAt(xpFor30, 0), Is.EqualTo(10));
            Assert.That(Crafting.LevelAt(xpFor30, 1), Is.EqualTo(20));
            Assert.That(Crafting.LevelAt(xpFor30, 3), Is.EqualTo(30));
        }

        [Test]
        public void AtGateFiresExactlyOnTheTenthLevel()
        {
            long xpFor10 = Crafting.XpForLevel(10);
            Assert.That(Crafting.AtGate(xpFor10 - 1L, 0), Is.False);
            Assert.That(Crafting.AtGate(xpFor10, 0), Is.True);
            Assert.That(Crafting.AtGate(xpFor10, 1), Is.False, "a cleared stop must not re-arm");
            Assert.That(Crafting.AtGate(long.MaxValue / 2L, Crafting.GateCount), Is.False,
                        "past the last stop nothing is ever gated");
        }

        [Test]
        public void GateSecondsAndTiersFollowTheLadder()
        {
            Assert.That(Crafting.GateSeconds(0, T), Is.EqualTo(6d * 3600d));
            Assert.That(Crafting.GateSeconds(1, T), Is.EqualTo(12d * 3600d));
            Assert.That(Crafting.GateSeconds(2, T), Is.EqualTo(24d * 3600d));
            Assert.That(Crafting.TierFor(0), Is.EqualTo(0));
            Assert.That(Crafting.TierFor(2), Is.EqualTo(2));
            Assert.That(Crafting.TierFor(99), Is.EqualTo(Voyages.TierCount - 1));
            Assert.That(Crafting.TierFor(-1), Is.EqualTo(0));
        }

        [Test]
        public void SalvageXpClampsIntoTheLadder()
        {
            Assert.That(Crafting.SalvageXpFor(0), Is.EqualTo(6L));
            Assert.That(Crafting.SalvageXpFor(4), Is.EqualTo(250L));
            Assert.That(Crafting.SalvageXpFor(99), Is.EqualTo(250L));
            Assert.That(Crafting.SalvageXpFor(-1), Is.EqualTo(0L));
        }

        // ---- the service -------------------------------------------------------------------------

        private static CraftingService Bench(SaveData data, int seed = 12345)
            => new CraftingService(data, null, new TimeService(),
                                   Crafting.Tuning.Default, SeaCombat.Tuning.Default,
                                   new Random(seed));

        [Test]
        public void CraftSpendsThePointAndParksThePending()
        {
            var data = new SaveData { craftPoints = 2L };
            var bench = Bench(data);

            Assert.That(bench.TryCraft(out SeaCombat.Item item), Is.True);
            Assert.That(data.craftPoints, Is.EqualTo(1L));
            Assert.That(bench.HasPending, Is.True);
            Assert.That(item.Grade, Is.InRange(0, 1), "level 1 rolls Common or Rare only");

            Assert.That(bench.TryCraft(out _), Is.False, "a second craft must wait for the decision");
            Assert.That(data.craftPoints, Is.EqualTo(1L), "a refused craft must not charge");
        }

        [Test]
        public void CraftRefusedWhenBroke()
        {
            var data = new SaveData();
            var bench = Bench(data);
            Assert.That(bench.TryCraft(out _), Is.False);
        }

        [Test]
        public void TheBenchAtLevelOneNeverMakesAnEpic()
        {
            var data = new SaveData { craftPoints = 500L };
            var bench = Bench(data, seed: 7);
            for (int i = 0; i < 500; i++)
            {
                Assert.That(bench.TryCraft(out SeaCombat.Item item), Is.True);
                Assert.That(item.Grade, Is.InRange(0, 1));
                bench.SalvagePending(out _);
                data.craftXp = 0L;   // stay at level 1 — this test is about the bracket, not the curve
            }
        }

        [Test]
        public void SalvagePaysHurdaAndTeaches()
        {
            var data = new SaveData { craftPoints = 1L };
            var bench = Bench(data);
            bench.TryCraft(out SeaCombat.Item item);

            long scrap = bench.SalvagePending(out long xp);
            Assert.That(scrap, Is.EqualTo(SeaCombat.ScrapFor(item.Grade)));
            Assert.That(xp, Is.EqualTo(Crafting.SalvageXpFor(item.Grade)));
            Assert.That(data.salvage, Is.EqualTo(scrap));
            Assert.That(data.craftXp, Is.EqualTo(xp));
            Assert.That(bench.HasPending, Is.False);
        }

        [Test]
        public void ThePendingItemSurvivesAReload()
        {
            var data = new SaveData { craftPoints = 1L };
            Bench(data).TryCraft(out SeaCombat.Item made);

            var reloaded = Bench(data, seed: 999);   // fresh service over the same save
            Assert.That(reloaded.HasPending, Is.True);
            SeaCombat.Item back = reloaded.PendingItem();
            Assert.That(back.Grade, Is.EqualTo(made.Grade));
            Assert.That(back.Slot, Is.EqualTo(made.Slot));
            Assert.That(back.Hull, Is.EqualTo(made.Hull));
            Assert.That(back.Shot, Is.EqualTo(made.Shot));
            Assert.That(back.Def, Is.EqualTo(made.Def));
            Assert.That(back.Spd, Is.EqualTo(made.Spd));
            Assert.That(back.Sec, Is.EqualTo(made.Sec));
            Assert.That(back.SecAmt, Is.EqualTo(made.SecAmt));
        }

        [Test]
        public void EquipGoesThroughTheSeaAndTheDisplacedItemTeaches()
        {
            var data = new SaveData { craftPoints = 1L };
            var bench = Bench(data);
            var sea = new ExpeditionService(null, new TimeService(), data);
            sea.Crafting = bench;
            bench.Expeditions = sea;

            // Every slot already wears a Rare, so whatever slot the craft rolls displaces one.
            for (int s = 0; s < SeaCombat.SlotCount; s++)
                sea.Equip(SeaCombat.ItemFor(s, 0, 1, 0.5d, SeaCombat.Tuning.Default));
            long xpBefore = data.craftXp;
            long hurdaBefore = data.salvage;

            bench.TryCraft(out SeaCombat.Item item);
            long scrap = bench.EquipPending();

            Assert.That(bench.HasPending, Is.False);
            Assert.That(data.seaGearGrade[item.Slot], Is.EqualTo(item.Grade + 1));
            Assert.That(scrap, Is.EqualTo(SeaCombat.ScrapFor(1)));
            Assert.That(data.salvage - hurdaBefore, Is.EqualTo(scrap));
            Assert.That(data.craftXp - xpBefore, Is.EqualTo(Crafting.SalvageXpFor(1)),
                        "the displaced Rare must teach like any other scrap");
        }

        [Test]
        public void SeaScrapsTeachTheBench()
        {
            var data = new SaveData();
            var bench = Bench(data);
            var sea = new ExpeditionService(null, new TimeService(), data);
            sea.Crafting = bench;

            sea.Scrap(2);
            Assert.That(data.craftXp, Is.EqualTo(Crafting.SalvageXpFor(2)));
        }

        [Test]
        public void ReachingTenStampsTheStopAndTheStopOpensOnTheClock()
        {
            var data = new SaveData { craftXp = Crafting.XpForLevel(10) };
            var bench = Bench(data);   // the constructor's tick finds level 10 and stamps

            Assert.That(bench.Level, Is.EqualTo(10));
            Assert.That(bench.IsGated, Is.True);
            Assert.That(data.craftGateEndUnix, Is.GreaterThan(0L));
            Assert.That(bench.GateSecondsLeft, Is.GreaterThan(6d * 3600d - 60d));
            Assert.That(bench.CurrentTier, Is.EqualTo(0), "the budget only rises when the stop opens");

            data.craftGateEndUnix = 1L;   // the deadline is long past
            bench.Poll();
            Assert.That(bench.IsGated, Is.False);
            Assert.That(data.craftGatesCleared, Is.EqualTo(1));
            Assert.That(bench.CurrentTier, Is.EqualTo(1));
        }

        [Test]
        public void BankedXpRunsStraightIntoTheNextStop()
        {
            var data = new SaveData { craftXp = Crafting.XpForLevel(30) };
            var bench = Bench(data);
            Assert.That(bench.Level, Is.EqualTo(10), "the first stop holds everything");

            data.craftGateEndUnix = 1L;
            bench.Poll();
            Assert.That(data.craftGatesCleared, Is.EqualTo(1));
            Assert.That(bench.Level, Is.EqualTo(20), "banked XP lands the moment the stop opens");
            Assert.That(bench.IsGated, Is.True, "and the banked XP is already on the next stop");

            data.craftGateEndUnix = 1L;
            bench.Poll();
            Assert.That(data.craftGatesCleared, Is.EqualTo(2));
            Assert.That(bench.Level, Is.EqualTo(30));
        }

        [Test]
        public void PointDropsRideTheTunedChance()
        {
            var data = new SaveData();
            var bench = Bench(data);
            Assert.That(bench.TryDropPoint(0.199d), Is.True);
            Assert.That(data.craftPoints, Is.EqualTo(1L));
            Assert.That(bench.TryDropPoint(0.200d), Is.False, "the chance is exclusive at the edge");
            Assert.That(data.craftPoints, Is.EqualTo(1L));

            bench.OnVoyageClaimed();
            Assert.That(data.craftPoints, Is.EqualTo(1L + T.PointsPerVoyage));
        }

        [Test]
        public void ANewSaveNormalisesQuietly()
        {
            var data = new SaveData
            {
                craftPoints = -5L,
                craftXp = -1L,
                craftGatesCleared = 99,
                craftPendingGrade = 42,   // a broken pending cell is cleared, not worn
            };
            var bench = Bench(data);
            Assert.That(data.craftPoints, Is.EqualTo(0L));
            Assert.That(data.craftXp, Is.EqualTo(0L));
            Assert.That(data.craftGatesCleared, Is.EqualTo(Crafting.GateCount));
            Assert.That(bench.HasPending, Is.False);
            Assert.That(bench.Level, Is.EqualTo(1));
        }
        [Test]
        public void ThePendingItemCanBeKeptInsteadOfDecided()
        {
            var data = new SaveData { craftPoints = 2L };
            var bench = Bench(data);
            var sea = new ExpeditionService(null, new TimeService(), data, null,
                                            SeaCombat.Tuning.Default);
            bench.Expeditions = sea;
            sea.Crafting = bench;

            Assert.That(bench.TryCraft(out SeaCombat.Item item), Is.True);
            Assert.That(bench.StowPending(), Is.True);

            Assert.That(bench.HasPending, Is.False, "the bench is clear");
            Assert.That(sea.StashCount, Is.EqualTo(1), "and the item is on the shelf");
            Assert.That(sea.StashItemAt(0).Grade, Is.EqualTo(item.Grade));
            Assert.That(sea.StashItemAt(0).Slot, Is.EqualTo(item.Slot));
            Assert.That(data.salvage, Is.Zero, "keeping it pays no hurda");
            Assert.That(data.craftXp, Is.Zero, "and teaches nothing");

            Assert.That(bench.TryCraft(out _), Is.True, "and the bench is free to work again");
        }

        [Test]
        public void AKeptItemIsNeverOnTheBenchAndTheShelfAtOnce()
        {
            var data = new SaveData { craftPoints = 1L };
            var bench = Bench(data);
            var sea = new ExpeditionService(null, new TimeService(), data, null,
                                            SeaCombat.Tuning.Default);
            bench.Expeditions = sea;

            bench.TryCraft(out _);
            bench.StowPending();

            Assert.That(data.craftPendingGrade, Is.Zero, "the cell is cleared in the same save");
            Assert.That(data.gearStash.Count, Is.EqualTo(1));
        }

        [Test]
        public void KeepingIsRefusedWithNothingPendingNoShelfOrAFullOne()
        {
            var data = new SaveData { craftPoints = 1L };
            var bench = Bench(data);

            Assert.That(bench.StowPending(), Is.False, "nothing pending");

            bench.TryCraft(out _);
            Assert.That(bench.StowPending(), Is.False, "no sea service wired");
            Assert.That(bench.HasPending, Is.True, "and the item is still on the bench");

            SeaCombat.Tuning tuning = SeaCombat.Tuning.Default;
            tuning.StashCapacity = 1;
            var sea = new ExpeditionService(null, new TimeService(), data, null, tuning);
            bench.Expeditions = sea;
            Assert.That(sea.Stow(SeaCombat.ItemFor(0, 0, 0, 0.5d, tuning)), Is.True, "premise: full");

            Assert.That(bench.StowPending(), Is.False, "a full shelf");
            Assert.That(bench.HasPending, Is.True, "the item stays exactly where it was");
            Assert.That(sea.StashCount, Is.EqualTo(1));

            // And the refusal did not damage the cell: the item is still the one that was crafted.
            Assert.That(bench.PendingItem().Grade, Is.InRange(0, Captains.GradeCount - 1));
        }

        [Test]
        public void ScrappingFromTheShelfTeachesTheBenchLikeAnyOtherScrap()
        {
            var data = new SaveData();
            var bench = Bench(data);
            var sea = new ExpeditionService(null, new TimeService(), data, null,
                                            SeaCombat.Tuning.Default);
            sea.Crafting = bench;

            sea.Stow(SeaCombat.ItemFor(SeaCombat.SlotCannon, 0, 3, 0.5d, SeaCombat.Tuning.Default));
            sea.Stow(SeaCombat.ItemFor(SeaCombat.SlotCharm, 0, 1, 0.5d, SeaCombat.Tuning.Default));

            sea.ScrapFromStash(sea.StashIdAt(0), out long xp);
            Assert.That(xp, Is.EqualTo(Crafting.SalvageXpFor(3)));
            Assert.That(data.craftXp, Is.EqualTo(Crafting.SalvageXpFor(3)), "one lesson, applied");

            sea.ScrapAllStash(out long allXp);
            Assert.That(allXp, Is.EqualTo(Crafting.SalvageXpFor(1)));
            Assert.That(data.craftXp, Is.EqualTo(Crafting.SalvageXpFor(3) + Crafting.SalvageXpFor(1)),
                        "emptying the shelf teaches once per item");
        }
    }
}
