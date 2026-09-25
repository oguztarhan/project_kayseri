using Game.Core;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>The flying reward character: when it may cross, what a catch pays, the daily cap and the card streak.</summary>
    public sealed class FlierTests
    {
        private static readonly Flier.Tuning T = Flier.Tuning.Default;

        /// <summary>A screen on which a due crossing is allowed; each test spoils one thing.</summary>
        private static Flier.Conditions Clear() => new Flier.Conditions
        {
            ChargesLeft = 8, BalloonReady = false, AdReady = true, TutorialBlocking = false, PanelOpen = false,
            SecondsSinceReward = 1e9
        };

        [Test]
        public void ItCrossesOnlyWhenNothingElseWantsTheScreen()
        {
            Assert.That(Flier.MaySpawn(Clear(), T), Is.True);
            Flier.Conditions c = Clear(); c.BalloonReady = true;
            Assert.That(Flier.MaySpawn(c, T), Is.False, "never beside a claimable balloon");
            c = Clear(); c.ChargesLeft = 0;
            Assert.That(Flier.MaySpawn(c, T), Is.False, "the day's catches are spent");
            c = Clear(); c.AdReady = false;
            Assert.That(Flier.MaySpawn(c, T), Is.False, "no ad to pay it with");
            c = Clear(); c.TutorialBlocking = true;
            Assert.That(Flier.MaySpawn(c, T), Is.False, "the tutorial");
            c = Clear(); c.PanelOpen = true;
            Assert.That(Flier.MaySpawn(c, T), Is.False, "a screen is open");
            c = Clear(); c.SecondsSinceReward = 89d;
            Assert.That(Flier.MaySpawn(c, T), Is.False, "too soon after another reward");
            c.SecondsSinceReward = 90d;
            Assert.That(Flier.MaySpawn(c, T), Is.True);
        }

        [Test]
        public void GapsPayoutAndStreakReadTheTuning()
        {
            Assert.That(Flier.Gap(0d, T), Is.EqualTo(240d));
            Assert.That(Flier.Gap(1d, T), Is.EqualTo(420d));
            Assert.That(Flier.Gap(0.5d, T), Is.EqualTo(330d));
            Assert.That(Flier.Gap(double.NaN, T), Is.EqualTo(240d));
            Assert.That(Flier.Payout(1000d, T), Is.EqualTo(4000d), "four income-minutes");
            Assert.That(Flier.Payout(0d, T), Is.EqualTo(100d), "never under the floor");

            int streak = 0;
            bool card = false;
            for (int i = 1; i <= 4; i++)
            {
                streak = Flier.NextStreak(streak, T, out card);
                Assert.That(card, Is.False);
                Assert.That(streak, Is.EqualTo(i));
            }
            streak = Flier.NextStreak(streak, T, out card);
            Assert.That(card, Is.True, "the fifth catch");
            Assert.That(streak, Is.Zero, "and the streak starts again");
            Assert.Throws<System.ArgumentException>(() => { Flier.Tuning bad = T; bad.StreakLength = 0; bad.Validate(); });
        }

        [Test]
        public void TheClockWaitsForAnAllowedMomentThenDrawsTheNextGap()
        {
            var data = new SaveData();
            var free = new FreeRewardService(data, new TimeService());
            var flier = new FlierService(free, new WalletService(data.wallet), null, null, data, T, 0d);
            Assert.That(flier.SecondsToNext, Is.EqualTo(240d));
            Assert.That(flier.Tick(239d, Clear(), 0d), Is.False);
            Flier.Conditions blocked = Clear(); blocked.PanelOpen = true;
            Assert.That(flier.Tick(5d, blocked, 0d), Is.False, "due but a screen is open");
            Assert.That(flier.SecondsToNext, Is.Zero, "it waits rather than skipping");
            Assert.That(flier.Tick(0.5d, Clear(), 1d), Is.True);
            Assert.That(flier.SecondsToNext, Is.EqualTo(420d), "the next gap comes from the roll");
            Assert.That(flier.Tick(0.5d, Clear(), 1d), Is.False);
        }

        [Test]
        public void EveryCatchPaysCashAndEveryFifthAMasterCardUntilTheDayRunsOut()
        {
            var data = new SaveData();
            var time = new TimeService();
            var free = new FreeRewardService(data, time);
            var wallet = new WalletService(data.wallet);
            var foremen = new ForemanService(data, wallet, Foremen.Tuning.Default);
            var flier = new FlierService(free, wallet, foremen, null, data, T, 0d);
            long gems = wallet.Gems;

            int cards = 0;
            for (int i = 1; i <= T.ChargesPerDay; i++)
            {
                FlierService.Receipt r = flier.TryClaim(600d);
                Assert.That(r.Paid, Is.True);
                Assert.That(r.Cash, Is.EqualTo(2400d));
                if (r.CardMaster >= 0) cards++;
                Assert.That(r.CardMaster >= 0, Is.EqualTo(i == 5), "catch " + i);
            }
            Assert.That(cards, Is.EqualTo(1));
            Assert.That(foremen.HiredCount, Is.EqualTo(1), "the card stood a master up");
            Assert.That(wallet.Cash.ToDouble(), Is.EqualTo(2400d * T.ChargesPerDay).Within(1e-6));
            Assert.That(wallet.Gems, Is.EqualTo(gems), "the flier pays no gems");
            Assert.That(data.flierStreak, Is.EqualTo(3), "eight catches: one card, three toward the next");
            Assert.That(flier.ChargesLeft, Is.Zero);
            Assert.That(flier.TryClaim(600d).Paid, Is.False, "capped for the day");

            data.freeRewardDay -= 1;    // the UTC day turns over
            Assert.That(flier.ChargesLeft, Is.EqualTo(T.ChargesPerDay));
            Assert.That(flier.Streak, Is.EqualTo(3), "the streak survives the reset");
        }

        [Test]
        public void TheBalloonsOwnSlotIsUntouched()
        {
            var data = new SaveData();
            var free = new FreeRewardService(data, new TimeService());
            var wallet = new WalletService(data.wallet);
            var flier = new FlierService(free, wallet, null, null, data, T, 0d);
            var balloon = new BalloonRewardService(free, wallet, null, data);
            flier.TryClaim(100d);
            Assert.That(balloon.ChargesLeft, Is.EqualTo(BalloonRewardService.ChargesPerDay));
            Assert.That(free.SecondsSinceAnyWatch(), Is.LessThan(5d), "a catch counts as a reward for the quiet time");
        }
    }
}
