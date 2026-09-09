using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The half of the captain roster that touches the save: padding, charts, crates and levelling.
    /// The rules are covered in CaptainsTests and the odds in CaptainCrateTests; what is tested here
    /// is the spending, the padding, and that charts bank into the roster the way the crate expects.
    /// </summary>
    public class CaptainServiceTests
    {

        private static Captains.Tuning T => Captains.Tuning.Default;
        private static CaptainCrate.Tuning C => CaptainCrate.Tuning.Default;

        /// <summary>A seeded generator, so a crate test asserts a fact rather than a coin flip.</summary>
        private static CaptainService Make(SaveData data, int seed = 12345)
            => new CaptainService(data, T, C, new System.Random(seed));

        // ---- the save contract -------------------------------------------------------------------

        [Test]
        public void ASaveFromBeforeCaptainsExistedWorks()
        {
            var data = new SaveData();
            data.captainLevels = null;
            data.captainDuplicates = null;

            CaptainService s = Make(data);
            Assert.That(data.captainLevels.Length, Is.EqualTo(Captains.Count));
            Assert.That(data.captainDuplicates.Length, Is.EqualTo(Captains.Count));
            Assert.That(s.OwnedCount, Is.Zero);
            Assert.That(s.Charts, Is.Zero);
        }

        [Test]
        public void AShortArrayIsPaddedAndKeepsWhatItHad()
        {
            // This is what makes appending a captain to the roster free.
            var data = new SaveData();
            data.captainLevels = new[] { 4, 2 };
            data.captainDuplicates = new[] { 9, 1 };

            CaptainService s = Make(data);
            Assert.That(data.captainLevels.Length, Is.EqualTo(Captains.Count));
            Assert.That(s.Level(0), Is.EqualTo(4));
            Assert.That(s.Level(1), Is.EqualTo(2));
            Assert.That(s.Duplicates(0), Is.EqualTo(9));
            for (int c = 2; c < Captains.Count; c++)
                Assert.That(s.Level(c), Is.EqualTo(Captains.NotOwned), "captain " + c);
        }

        [Test]
        public void ANullSaveIsSurvivable()
        {
            var s = new CaptainService(null, T, C);
            Assert.That(s.Charts, Is.Zero);
            Assert.That(s.OwnedCount, Is.Zero);
            Assert.That(s.TryOpen(1), Is.Null);
            Assert.That(s.TryLevelUp(0), Is.False);
            Assert.That(s.PendingCount(), Is.Zero);
            Assert.DoesNotThrow(() => s.AddCharts(50L));
        }

        // ---- charts ------------------------------------------------------------------------------

        [Test]
        public void ChartsBankAndNegativeGrantsAreIgnored()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            s.AddCharts(120L);
            s.AddCharts(-50L);
            s.AddCharts(0L);
            Assert.That(s.Charts, Is.EqualTo(120L));
        }

        // ---- the crate ---------------------------------------------------------------------------

        [Test]
        public void OpeningWithoutEnoughChartsChangesNothing()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            s.AddCharts(C.ChartCost - 1);

            Assert.That(s.CanOpen(1), Is.False);
            Assert.That(s.TryOpen(1), Is.Null);
            Assert.That(s.Charts, Is.EqualTo(C.ChartCost - 1));
            Assert.That(s.OwnedCount, Is.Zero);
            Assert.That(s.CratesOpened, Is.Zero);
        }

        [Test]
        public void OpeningSpendsTheChartsAndHandsSomebodyOver()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            s.AddCharts(C.ChartCost * 3);

            int[] got = s.TryOpen(1);
            Assert.That(got, Is.Not.Null);
            Assert.That(got.Length, Is.EqualTo(1));
            Assert.That(Captains.Exists(got[0]), Is.True);
            Assert.That(s.Charts, Is.EqualTo(C.ChartCost * 2));
            Assert.That(s.CratesOpened, Is.EqualTo(1));
            Assert.That(s.Owned(got[0]), Is.True);
            Assert.That(s.Level(got[0]), Is.EqualTo(1));
        }

        [Test]
        public void TheFirstCopyIsTheCaptainAndEveryOneAfterIsADuplicate()
        {
            // A crate that paid a new player "1 duplicate of a captain you do not have" would be
            // paying them in something they cannot look at.
            var data = new SaveData();
            CaptainService s = Make(data);
            s.AddCharts(C.ChartCost * 400);

            int target = -1;
            for (int i = 0; i < 400 && target < 0; i++)
            {
                int[] got = s.TryOpen(1);
                if (s.Duplicates(got[0]) > 0) target = got[0];
            }

            Assert.That(target, Is.Not.EqualTo(-1), "400 pulls produced no duplicate at all");
            Assert.That(s.Level(target), Is.EqualTo(1), "a duplicate must not level a captain on its own");
            Assert.That(s.Duplicates(target), Is.GreaterThan(0));
        }

        [Test]
        public void ABulkOpenCostsTheBulkPriceAndPaysTheBulkCount()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            s.AddCharts(C.BulkChartCost);

            int[] got = s.TryOpen(C.BulkCount);
            Assert.That(got, Is.Not.Null);
            Assert.That(got.Length, Is.EqualTo(C.BulkCount));
            Assert.That(s.Charts, Is.Zero);
            Assert.That(s.CratesOpened, Is.EqualTo(C.BulkCount));
        }

        [Test]
        public void ABulkOpenAlwaysContainsAnEpic()
        {
            // The pity counters advance across the batch exactly as they would across ten presses.
            for (int seed = 1; seed <= 40; seed++)
            {
                var data = new SaveData();
                CaptainService s = Make(data, seed);
                s.AddCharts(C.BulkChartCost);

                int[] got = s.TryOpen(C.BulkCount);
                bool any = false;
                for (int i = 0; i < got.Length; i++)
                    if (Captains.RankOf(got[i]) >= Captains.Grade.Epic) any = true;
                Assert.That(any, Is.True, "seed " + seed);
            }
        }

        [Test]
        public void ThePityCountersSurviveInTheSave()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            s.AddCharts(C.ChartCost * 5);
            s.TryOpen(5);

            Assert.That(data.crateSinceEpic, Is.EqualTo(s.SinceEpic));
            Assert.That(data.crateSinceLegendary, Is.EqualTo(s.SinceLegendary));

            // A second service over the same save reads the same counters — a pity that reset on
            // launch would be a pity the player could farm by closing the app.
            CaptainService reloaded = Make(data);
            Assert.That(reloaded.SinceEpic, Is.EqualTo(s.SinceEpic));
            Assert.That(reloaded.SinceLegendary, Is.EqualTo(s.SinceLegendary));
        }

        // ---- levelling ---------------------------------------------------------------------------

        [Test]
        public void LevellingSpendsExactlyTheDuplicatesItQuoted()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            data.captainLevels[0] = 1;

            int need = s.DuplicatesNeeded(0);
            Assert.That(need, Is.GreaterThan(0));

            data.captainDuplicates[0] = need - 1;
            Assert.That(s.CanLevel(0), Is.False);
            Assert.That(s.TryLevelUp(0), Is.False);

            data.captainDuplicates[0] = need + 3;
            Assert.That(s.TryLevelUp(0), Is.True);
            Assert.That(s.Level(0), Is.EqualTo(2));
            Assert.That(s.Duplicates(0), Is.EqualTo(3));
        }

        [Test]
        public void ACaptainYouDoNotOwnCannotBeLevelled()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            data.captainDuplicates[0] = 9999;
            Assert.That(s.DuplicatesNeeded(0), Is.Zero);
            Assert.That(s.CanLevel(0), Is.False);
            Assert.That(s.TryLevelUp(0), Is.False);
        }

        [Test]
        public void TheCeilingHolds()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            data.captainLevels[0] = Captains.MaxLevel;
            data.captainDuplicates[0] = 9999;

            Assert.That(s.DuplicatesNeeded(0), Is.Zero);
            Assert.That(s.TryLevelUp(0), Is.False);
            Assert.That(s.Level(0), Is.EqualTo(Captains.MaxLevel));
        }

        [Test]
        public void PendingCountIsWhatTheBadgeShows()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            Assert.That(s.PendingCount(), Is.Zero);

            data.captainLevels[0] = 1;
            data.captainDuplicates[0] = s.DuplicatesNeeded(0);
            Assert.That(s.PendingCount(), Is.EqualTo(1));
        }

        [Test]
        public void SharedCardStateCarriesCaptainRoleRarityAndUpgradeReadiness()
        {
            var data = new SaveData();
            CaptainService s = Make(data);
            int captain = 0;

            Assert.That(s.CardState(captain).CardStatus, Is.EqualTo(RosterCardState.Status.Locked));

            data.captainLevels[captain] = 1;
            data.captainDuplicates[captain] = s.DuplicatesNeeded(captain);
            RosterCardState ready = s.CardState(captain);

            Assert.That(ready.Role, Is.EqualTo(Captains.RoleOf(captain)));
            Assert.That((int)ready.Tier, Is.EqualTo((int)Captains.RankOf(captain)));
            Assert.That(ready.CanUpgrade, Is.True);
            Assert.That(ready.Effect, Is.GreaterThan(0d));
        }
    }
}
