using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// Where packs come from, besides the daily one: the weekly milestone track and the achievement
    /// ladder, both paid through <see cref="GoalService"/>'s existing claims.
    ///
    /// The tables are asserted as the approved numbers in <c>Docs/PLAN_14</c> — this is an eighth
    /// consumer of metrics seven systems already pay on, so a pack count that drifted upward in a
    /// casual edit is exactly the change that should have to be made in two places. Everything else
    /// here computes its expectation off the tables, so it keeps checking the plumbing if they move.
    /// </summary>
    public class CardCollectionGoalTests
    {
        private static GoalService Build(SaveData data, out CardCollectionService cards)
        {
            var wallet = new WalletService(data.wallet);
            cards = new CardCollectionService(data, null, new TimeService(), wallet);
            return new GoalService(data, wallet, null, new TimeService(), null, cards);
        }

        /// <summary>Weekly points enough to reach this milestone, earned the way a player earns them.</summary>
        private static void EarnWeekly(GoalService goals, int points)
        {
            for (int i = 0; i < Goals.WeeklyTasks.Length && points > 0; i++)
            {
                Goals.WeeklyTask task = Goals.WeeklyTasks[i];
                goals.Record(task.Metric, task.Target);
                points -= task.Points;
            }
        }

        private static int IndexOfMilestone(string id)
        {
            for (int i = 0; i < Goals.WeeklyMilestones.Length; i++)
                if (Goals.WeeklyMilestones[i].Id == id) return i;
            Assert.Fail("no milestone " + id);
            return -1;
        }

        // ---- the approved tables ------------------------------------------------------------------

        [Test]
        public void TheWeeklyTrackPaysThePlannedPacks()
        {
            Assert.That(Goals.WeeklyMilestones[IndexOfMilestone("weekly_25")].Packs, Is.EqualTo(0));
            Assert.That(Goals.WeeklyMilestones[IndexOfMilestone("weekly_50")].Packs, Is.EqualTo(1));
            Assert.That(Goals.WeeklyMilestones[IndexOfMilestone("weekly_75")].Packs, Is.EqualTo(1));
            Assert.That(Goals.WeeklyMilestones[IndexOfMilestone("weekly_100")].Packs, Is.EqualTo(3));
        }

        [Test]
        public void EveryAchievementPaysTwoPacksATierFromTierThree()
        {
            Assert.That(Goals.PackFirstTier, Is.EqualTo(3));
            for (int i = 0; i < Goals.Ladder.Length; i++)
            {
                Goals.Achievement a = Goals.Ladder[i];
                Assert.That(a.PacksPerTier, Is.EqualTo(2), "achievement " + i);
                Assert.That(Goals.TierPacks(a, 1), Is.Zero, "tier 1 of achievement " + i);
                Assert.That(Goals.TierPacks(a, 2), Is.Zero, "tier 2 of achievement " + i);
                Assert.That(Goals.TierPacks(a, 3), Is.EqualTo(2), "tier 3 of achievement " + i);
                Assert.That(Goals.TierPacks(a, a.Tiers.Length), Is.EqualTo(2), "the top tier of " + i);
            }
        }

        [Test]
        public void ANonsenseTierPaysNoPacks()
        {
            Goals.Achievement a = Goals.Ladder[0];
            Assert.That(Goals.TierPacks(a, 0), Is.Zero);
            Assert.That(Goals.TierPacks(a, -4), Is.Zero);
            var broken = new Goals.Achievement { PacksPerTier = -3, Tiers = new[] { 1L, 2L, 3L } };
            Assert.That(Goals.TierPacks(broken, 3), Is.Zero, "a negative table entry cannot take packs away");
        }

        [Test]
        public void TheDailyPoolIsUntouchedByTheCollection()
        {
            // The plan's one hard constraint on this file: packs ride on the weekly track and the
            // ladder, never as a sixth daily task. A sixth entry would make the pool composite and
            // DailyIndex would start repeating tasks within a day.
            Assert.That(Goals.DailyPool.Length, Is.EqualTo(5));
        }

        // ---- the weekly track ---------------------------------------------------------------------

        [Test]
        public void AWeeklyMilestoneBanksItsPacksUnopened()
        {
            var data = new SaveData();
            GoalService goals = Build(data, out CardCollectionService cards);
            int index = IndexOfMilestone("weekly_100");
            EarnWeekly(goals, Goals.WeeklyMilestones[index].Points);

            Assert.That(goals.ClaimWeeklyMilestone(index, out GoalService.ClaimReceipt receipt), Is.True);

            int expected = Goals.WeeklyMilestones[index].Packs;
            Assert.That(receipt.Packs, Is.EqualTo(expected), "the reveal is told");
            Assert.That(cards.UnopenedPackCount, Is.EqualTo(expected), "and the collection has them");
            Assert.That(cards.PacksOpened, Is.Zero, "banked, never auto-opened");
        }

        [Test]
        public void AMilestoneWithNoPacksBanksNone()
        {
            var data = new SaveData();
            GoalService goals = Build(data, out CardCollectionService cards);
            int index = IndexOfMilestone("weekly_25");
            EarnWeekly(goals, Goals.WeeklyMilestones[index].Points);

            Assert.That(goals.ClaimWeeklyMilestone(index, out GoalService.ClaimReceipt receipt), Is.True);
            Assert.That(receipt.Packs, Is.Zero);
            Assert.That(cards.UnopenedPackCount, Is.Zero);
        }

        [Test]
        public void ASecondTapOnAMilestonePaysNoSecondPack()
        {
            var data = new SaveData();
            GoalService goals = Build(data, out CardCollectionService cards);
            int index = IndexOfMilestone("weekly_50");
            EarnWeekly(goals, Goals.WeeklyMilestones[index].Points);

            goals.ClaimWeeklyMilestone(index);
            int afterFirst = cards.UnopenedPackCount;
            Assert.That(goals.ClaimWeeklyMilestone(index), Is.False);
            Assert.That(cards.UnopenedPackCount, Is.EqualTo(afterFirst));
        }

        // ---- the ladder ---------------------------------------------------------------------------

        [Test]
        public void TheFirstTwoTiersOfAnAchievementPayNoPacks()
        {
            var data = new SaveData();
            GoalService goals = Build(data, out CardCollectionService cards);
            int index = 0;
            Goals.Achievement a = Goals.Ladder[index];
            goals.Record(a.Metric, a.Tiers[1]);   // exactly two tiers passed

            Assert.That(goals.ClaimAchievement(index, out GoalService.ClaimReceipt receipt), Is.True);
            Assert.That(receipt.Gems, Is.GreaterThan(0L), "the premise: two tiers were paid");
            Assert.That(receipt.Packs, Is.Zero);
            Assert.That(cards.UnopenedPackCount, Is.Zero);
        }

        [Test]
        public void ClaimingSeveralTiersAtOncePaysEachTiersPacks()
        {
            // The ladder a player passed while offline, collected in one press.
            var data = new SaveData();
            GoalService goals = Build(data, out CardCollectionService cards);
            int index = 0;
            Goals.Achievement a = Goals.Ladder[index];
            goals.Record(a.Metric, a.Tiers[a.Tiers.Length - 1]);

            int expected = 0;
            for (int t = 1; t <= a.Tiers.Length; t++) expected += Goals.TierPacks(a, t);
            Assert.That(expected, Is.GreaterThan(0), "the premise");

            Assert.That(goals.ClaimAchievement(index, out GoalService.ClaimReceipt receipt), Is.True);
            Assert.That(receipt.Packs, Is.EqualTo(expected));
            Assert.That(cards.UnopenedPackCount, Is.EqualTo(expected));
        }

        [Test]
        public void TiersClaimedBeforeTheCollectionExistedAreNotPaidAgain()
        {
            // A save from before this update, with its ladder already collected. The packs those
            // tiers would have paid are gone — backfilling them would be a mass pack grant on the
            // first launch, which nobody decided to give.
            var data = new SaveData();
            Goals.Achievement a = Goals.Ladder[0];
            data.goals.lifetime[a.Metric] = a.Tiers[3];
            data.goals.tiersClaimed[0] = 4;

            GoalService goals = Build(data, out CardCollectionService cards);
            Assert.That(goals.ClaimAchievement(0), Is.False);
            Assert.That(cards.UnopenedPackCount, Is.Zero);

            goals.Record(a.Metric, a.Tiers[4] - a.Tiers[3]);   // and the NEXT tier does pay
            Assert.That(goals.ClaimAchievement(0, out GoalService.ClaimReceipt receipt), Is.True);
            Assert.That(receipt.Packs, Is.EqualTo(Goals.TierPacks(a, 5)));
        }

        // ---- claim everything ---------------------------------------------------------------------

        [Test]
        public void ClaimAllBanksEveryPackItReportsExactlyOnce()
        {
            var data = new SaveData();
            GoalService goals = Build(data, out CardCollectionService cards);
            for (int metric = 0; metric < Goals.MetricCount; metric++) goals.Record(metric, 30000000L);

            int expected = 0;
            for (int i = 0; i < Goals.WeeklyMilestones.Length; i++) expected += Goals.WeeklyMilestones[i].Packs;
            for (int i = 0; i < Goals.Ladder.Length; i++)
                for (int t = 1; t <= Goals.Ladder[i].Tiers.Length; t++)
                    expected += Goals.TierPacks(Goals.Ladder[i], t);

            Assert.That(goals.ClaimAll(out GoalService.ClaimReceipt receipt), Is.True);
            Assert.That(receipt.Packs, Is.EqualTo(expected));
            Assert.That(cards.UnopenedPackCount, Is.EqualTo(expected));

            Assert.That(goals.ClaimAll(out _), Is.False);
            Assert.That(cards.UnopenedPackCount, Is.EqualTo(expected), "nothing twice");
        }

        [Test]
        public void TheLifetimePackSupplyFromGoalsIsWhatThePlanBudgeted()
        {
            // 24 tiers at or above tier 3 across the six achievements, at 2 packs each. If this
            // moves, the pacing table in Docs/PLAN_14 was simulated against a different supply.
            int ladder = 0;
            for (int i = 0; i < Goals.Ladder.Length; i++)
                for (int t = 1; t <= Goals.Ladder[i].Tiers.Length; t++)
                    ladder += Goals.TierPacks(Goals.Ladder[i], t);

            int week = 0;
            for (int i = 0; i < Goals.WeeklyMilestones.Length; i++) week += Goals.WeeklyMilestones[i].Packs;

            Assert.That(ladder, Is.EqualTo(48));
            Assert.That(week, Is.EqualTo(5));
        }

        // ---- no collection wired ------------------------------------------------------------------

        [Test]
        public void AGoalServiceWithNoCollectionStillPaysEverythingElse()
        {
            // Every existing test builds GoalService without a collection. That has to keep meaning
            // "the checklist works exactly as before", not "the checklist throws".
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet, null, new TimeService());
            int index = IndexOfMilestone("weekly_100");
            EarnWeekly(goals, Goals.WeeklyMilestones[index].Points);

            long before = wallet.Gems;
            Assert.That(goals.ClaimWeeklyMilestone(index, out GoalService.ClaimReceipt receipt), Is.True);
            Assert.That(wallet.Gems, Is.EqualTo(before + Goals.WeeklyMilestones[index].Gems));
            Assert.That(receipt.Packs, Is.EqualTo(Goals.WeeklyMilestones[index].Packs),
                        "the receipt still says what the milestone is worth");
            Assert.That(data.cardCollection.unopenedPacks, Is.Zero, "nobody banked them");
        }

        // ---- across a reload ----------------------------------------------------------------------

        [Test]
        public void BankedPacksSurviveAReloadAndTheMilestoneStaysClaimed()
        {
            var data = new SaveData();
            GoalService goals = Build(data, out _);
            int index = IndexOfMilestone("weekly_50");
            EarnWeekly(goals, Goals.WeeklyMilestones[index].Points);
            goals.ClaimWeeklyMilestone(index);

            SaveData reloaded = UnityEngine.JsonUtility.FromJson<SaveData>(UnityEngine.JsonUtility.ToJson(data));
            GoalService again = Build(reloaded, out CardCollectionService cards);

            Assert.That(cards.UnopenedPackCount, Is.EqualTo(Goals.WeeklyMilestones[index].Packs));
            Assert.That(again.ClaimWeeklyMilestone(index), Is.False, "a reload is not a second claim");
            Assert.That(cards.UnopenedPackCount, Is.EqualTo(Goals.WeeklyMilestones[index].Packs));
        }
    }
}
