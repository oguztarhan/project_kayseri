using System;
using System.Collections.Generic;
using Game.Core;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;
using Goal = Game.Core.TutorialProgress.Goal;
using Facts = Game.Core.TutorialProgress.Facts;
using Intro = Game.Core.TutorialProgress.Intro;

namespace Game.Tests
{
    public sealed class TutorialProgressTests
    {
        private static TutorialProgress Fresh(out List<string> seen)
        {
            seen = new List<string>();
            return new TutorialProgress(seen, TutorialProgress.StepFresh);
        }

        private static void CompleteFirst(TutorialProgress progress, int count)
        {
            for (int i = 0; i < count; i++) progress.Complete(TutorialProgress.CoreLessons[i].Id);
        }

        // ------------------------------------------------------------------ the basics

        [Test]
        public void AFreshSaveStartsAtTheWelcome()
        {
            TutorialProgress progress = Fresh(out _);

            Assert.That(progress.CoreDone, Is.False);
            Assert.That(progress.NextCoreIndex, Is.EqualTo(0));
            Assert.That(TutorialProgress.CoreLessons[0].Id, Is.EqualTo("ftue.welcome"));
        }

        [Test]
        public void TheLessonsTeachTheWholeShopLoopInOrder()
        {
            var goals = new List<Goal>();
            for (int i = 0; i < TutorialProgress.CoreLessons.Length; i++) goals.Add(TutorialProgress.CoreLessons[i].Goal);

            // Craft, carry, sell, earn, spend — each before the next.
            int produce = goals.IndexOf(Goal.Produce), carry = goals.IndexOf(Goal.Carry), sell = goals.IndexOf(Goal.Sell);
            int afford = goals.IndexOf(Goal.Afford), open = goals.IndexOf(Goal.OpenBench), buy = goals.IndexOf(Goal.BuySpeed);
            Assert.That(new[] { produce, carry, sell, afford, open, buy }, Is.Ordered.And.All.GreaterThan(0));
            Assert.That(TutorialProgress.CoreLessons[TutorialProgress.CoreLessons.Length - 1].Id, Is.EqualTo("ftue.loop"));
        }

        [Test]
        public void EveryLessonIdIsUniqueAndNamespaced()
        {
            var ids = new HashSet<string>();
            foreach (TutorialProgress.Lesson lesson in TutorialProgress.CoreLessons)
            {
                Assert.That(lesson.Id, Does.StartWith("ftue."));
                Assert.That(ids.Add(lesson.Id), Is.True, lesson.Id + " twice");
            }
            Assert.That(ids, Does.Not.Contain(TutorialProgress.SkippedMarker));
        }

        [Test]
        public void FinishingEveryLessonFinishesTheBasics()
        {
            TutorialProgress progress = Fresh(out _);

            for (int i = 0; i < TutorialProgress.CoreLessons.Length; i++)
            {
                Assert.That(progress.NextCoreIndex, Is.EqualTo(i));
                Assert.That(progress.CoreDone, Is.False);
                Assert.That(progress.Complete(TutorialProgress.CoreLessons[i].Id), Is.True);
            }

            Assert.That(progress.CoreDone, Is.True);
            Assert.That(progress.Step, Is.EqualTo(TutorialProgress.StepDone));
            Assert.That(progress.NextCoreIndex, Is.EqualTo(-1));
        }

        [Test]
        public void AQuitMidLessonResumesAtThatLessonNotTheStart()
        {
            // Closed the app while saving up for the first upgrade.
            var seen = new List<string> { "ftue.welcome", "ftue.craft", "ftue.carry", "ftue.sell" };
            var progress = new TutorialProgress(seen, TutorialProgress.StepFresh);

            Assert.That(TutorialProgress.CoreLessons[progress.NextCoreIndex].Id, Is.EqualTo("ftue.save_up"));
            Assert.That(progress.CoreDone, Is.False);
        }

        [Test]
        public void ALessonDoneOutOfOrderIsNotAskedForAgain()
        {
            TutorialProgress progress = Fresh(out _);
            progress.Complete("ftue.buy_speed");
            CompleteFirst(progress, 6);

            Assert.That(TutorialProgress.CoreLessons[progress.NextCoreIndex].Id, Is.EqualTo("ftue.speed_value"));
        }

        [Test]
        public void CompletingTwiceWritesOnce()
        {
            TutorialProgress progress = Fresh(out List<string> seen);

            Assert.That(progress.Complete("ftue.welcome"), Is.True);
            Assert.That(progress.Complete("ftue.welcome"), Is.False);
            Assert.That(progress.Complete(null), Is.False);
            Assert.That(progress.Complete(string.Empty), Is.False);
            Assert.That(seen, Is.EqualTo(new[] { "ftue.welcome" }));
        }

        [Test]
        public void SkippingMarksEveryLessonAndSaysItWasSkipped()
        {
            TutorialProgress progress = Fresh(out List<string> seen);
            CompleteFirst(progress, 2);

            Assert.That(progress.FinishCore(true), Is.True);

            Assert.That(progress.CoreDone, Is.True);
            Assert.That(progress.NextCoreIndex, Is.EqualTo(-1));
            foreach (TutorialProgress.Lesson lesson in TutorialProgress.CoreLessons) Assert.That(seen, Does.Contain(lesson.Id));
            Assert.That(seen, Does.Contain(TutorialProgress.SkippedMarker));
            Assert.That(seen.Count, Is.EqualTo(TutorialProgress.CoreLessons.Length + 1), "no duplicates of the two already done");
        }

        [Test]
        public void AnExperiencedShopkeeperIsFinishedWithoutASkipMark()
        {
            TutorialProgress progress = Fresh(out List<string> seen);

            progress.FinishCore(false);

            Assert.That(progress.CoreDone, Is.True);
            Assert.That(seen, Does.Not.Contain(TutorialProgress.SkippedMarker));
            Assert.That(progress.FinishCore(false), Is.False, "nothing left to write the second time");
        }

        [Test]
        public void ReplayRestartsTheBasicsAndKeepsEveryIntroduction()
        {
            var seen = new List<string> { "gunluk", "feature.crafting", "sea.boss" };
            var progress = new TutorialProgress(seen, TutorialProgress.StepFresh);
            progress.FinishCore(true);

            progress.ResetCore();

            Assert.That(progress.Step, Is.EqualTo(TutorialProgress.StepFresh));
            Assert.That(progress.NextCoreIndex, Is.EqualTo(0));
            Assert.That(seen, Is.EquivalentTo(new[] { "gunluk", "feature.crafting", "sea.boss" }));
        }

        // ------------------------------------------------------------------ old saves

        [Test]
        public void APlayerWhoFinishedThePreviousTutorialIsNotTaughtTheBasicsAgain()
        {
            var seen = new List<string> { "core.shop.overview.production", "gunluk" };
            var progress = new TutorialProgress(seen, TutorialProgress.StepDone);

            Assert.That(progress.CoreDone, Is.True);
            Assert.That(progress.NextCoreIndex, Is.EqualTo(-1));
            Assert.That(progress.Step, Is.EqualTo(TutorialProgress.StepDone));
        }

        [Test]
        public void AnOldSaveStillGetsIntroductionsForFeaturesItHasNotUsed()
        {
            var progress = new TutorialProgress(new List<string>(), TutorialProgress.StepDone);

            Assert.That(progress.DecideIntro("feature.pets", true, false), Is.EqualTo(Intro.Show));
        }

        [Test]
        public void StepsBetweenTheTwoKnownValuesReadAsFresh()
        {
            Assert.That(new TutorialProgress(new List<string>(), 37).Step, Is.EqualTo(TutorialProgress.StepFresh));
            Assert.That(new TutorialProgress(new List<string>(), 250).Step, Is.EqualTo(TutorialProgress.StepDone));
        }

        [Test]
        public void TheSavesOwnListIsRequired()
        {
            Assert.Throws<ArgumentNullException>(() => new TutorialProgress(null, 0));
        }

        [Test]
        public void TheSaveFieldsItReadsRoundTripThroughJson()
        {
            var data = JsonUtility.FromJson<SaveData>("{\"tutorialStep\":100,\"tutorialTipsSeen\":[\"sea.combat\"]}");
            var progress = new TutorialProgress(data.tutorialTipsSeen, data.tutorialStep);

            progress.DecideIntro("feature.captain", false, true);
            data.tutorialStep = progress.Step;
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data));

            Assert.That(back.tutorialStep, Is.EqualTo(100));
            Assert.That(back.tutorialTipsSeen, Is.EqualTo(new[] { "sea.combat", "feature.captain" }));
        }

        // ------------------------------------------------------------------ introductions

        [Test]
        public void IntroductionsWaitForTheBasics()
        {
            TutorialProgress progress = Fresh(out _);

            Assert.That(progress.DecideIntro("sea.combat", true, false), Is.EqualTo(Intro.Wait));
            progress.FinishCore(false);
            Assert.That(progress.DecideIntro("sea.combat", true, false), Is.EqualTo(Intro.Show));
        }

        [Test]
        public void IntroductionsWaitUntilTheFeatureMatters()
        {
            var progress = new TutorialProgress(new List<string>(), TutorialProgress.StepDone);

            Assert.That(progress.DecideIntro("feature.crafting", false, false), Is.EqualTo(Intro.Wait));
        }

        [Test]
        public void AFeatureAlreadyUsedIsMarkedWithoutBeingShown()
        {
            var seen = new List<string>();
            var progress = new TutorialProgress(seen, TutorialProgress.StepDone);

            Assert.That(progress.DecideIntro("feature.pets", true, true), Is.EqualTo(Intro.Done));
            Assert.That(seen, Does.Contain("feature.pets"));
            Assert.That(progress.DecideIntro("feature.pets", true, false), Is.EqualTo(Intro.Done));
        }

        [Test]
        public void AnIntroductionIsShownOnce()
        {
            var progress = new TutorialProgress(new List<string>(), TutorialProgress.StepDone);

            Assert.That(progress.DecideIntro("sea.boss", true, false), Is.EqualTo(Intro.Show));
            progress.Complete("sea.boss");
            Assert.That(progress.DecideIntro("sea.boss", true, false), Is.EqualTo(Intro.Done));
        }

        [Test]
        public void IntroductionsDoNotTouchTheBasics()
        {
            TutorialProgress progress = Fresh(out _);
            progress.DecideIntro("feature.stage", true, true);
            progress.Complete("sea.combat");

            Assert.That(progress.CoreDone, Is.False);
            Assert.That(progress.NextCoreIndex, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------ goals

        [Test]
        public void WatchLessonsNeedSomethingNewSinceTheyBegan()
        {
            var start = new Facts { Produced = 40, Carried = 38, Sold = 35 };

            Assert.That(TutorialProgress.GoalMet(Goal.Produce, start, start), Is.False, "forty old pickaxes are not a new one");
            Assert.That(TutorialProgress.GoalMet(Goal.Carry, start, start), Is.False);
            Assert.That(TutorialProgress.GoalMet(Goal.Sell, start, start), Is.False);

            var later = new Facts { Produced = 41, Carried = 39, Sold = 36 };
            Assert.That(TutorialProgress.GoalMet(Goal.Produce, later, start), Is.True);
            Assert.That(TutorialProgress.GoalMet(Goal.Carry, later, start), Is.True);
            Assert.That(TutorialProgress.GoalMet(Goal.Sell, later, start), Is.True);
        }

        [Test]
        public void TheTapGoalIsOnlyEverEndedByThePlayer()
        {
            var everything = new Facts { Produced = 9, Carried = 9, Sold = 9, CanAffordSpeed = true, BenchPanelOpen = true, SpeedLevel = 5 };

            Assert.That(TutorialProgress.GoalMet(Goal.Tap, everything, default), Is.False);
            Assert.That(TutorialProgress.AlreadyDone(Goal.Tap, everything), Is.False);
        }

        [Test]
        public void TheUpgradeLessonsFollowTheWalletThePanelAndTheLevel()
        {
            var poor = new Facts { SpeedLevel = 1 };
            var rich = new Facts { SpeedLevel = 1, CanAffordSpeed = true };
            var open = new Facts { SpeedLevel = 1, CanAffordSpeed = true, BenchPanelOpen = true };
            var bought = new Facts { SpeedLevel = 2, BenchPanelOpen = true };

            Assert.That(TutorialProgress.GoalMet(Goal.Afford, poor, poor), Is.False);
            Assert.That(TutorialProgress.GoalMet(Goal.Afford, rich, poor), Is.True);
            Assert.That(TutorialProgress.GoalMet(Goal.OpenBench, rich, rich), Is.False);
            Assert.That(TutorialProgress.GoalMet(Goal.OpenBench, open, rich), Is.True);
            Assert.That(TutorialProgress.GoalMet(Goal.BuySpeed, open, open), Is.False);
            Assert.That(TutorialProgress.GoalMet(Goal.BuySpeed, bought, open), Is.True);
        }

        [Test]
        public void UpgradeStepsThePlayerAlreadyDidAreSkipped()
        {
            var upgraded = new Facts { SpeedLevel = 2 };

            Assert.That(TutorialProgress.AlreadyDone(Goal.Afford, upgraded), Is.True);
            Assert.That(TutorialProgress.AlreadyDone(Goal.OpenBench, upgraded), Is.True);
            Assert.That(TutorialProgress.AlreadyDone(Goal.BuySpeed, upgraded), Is.True);
            Assert.That(TutorialProgress.AlreadyDone(Goal.Afford, new Facts { SpeedLevel = 1, CanAffordSpeed = true }), Is.True,
                        "no point telling a player to save up for what they can already buy");
        }

        [Test]
        public void TheLoopLessonsAreNeverSkippedForBeingOldNews()
        {
            var busyShop = new Facts { Produced = 500, Carried = 500, Sold = 500, SpeedLevel = 1 };

            Assert.That(TutorialProgress.AlreadyDone(Goal.Produce, busyShop), Is.False);
            Assert.That(TutorialProgress.AlreadyDone(Goal.Carry, busyShop), Is.False);
            Assert.That(TutorialProgress.AlreadyDone(Goal.Sell, busyShop), Is.False);
        }
    }
}
