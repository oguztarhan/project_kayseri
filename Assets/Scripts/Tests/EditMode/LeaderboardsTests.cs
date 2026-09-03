using System;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The ladder's arithmetic and the offline double that exercises its contracts.
    ///
    /// Everything about seasons is asked of <see cref="Leaderboards"/> directly, where the clock is an
    /// argument — which is what lets the exact second a season rolls over be asserted instead of
    /// waited a week for. The service tests then run against real epochs positioned relative to the
    /// real clock, and move <see cref="LocalLeaderboardService.TimeOffsetSeconds"/> when a rollover is
    /// the thing under test.
    ///
    /// The half of this file that matters most is the idempotency block: every one of those tests is a
    /// duplicate delivery or a lost acknowledgement, which is what actually goes wrong in a submission
    /// path and what a max-merge is supposed to make harmless.
    /// </summary>
    public class LeaderboardsTests
    {
        private const long Epoch = Leaderboards.SeasonEpochUnix;
        private const long Week = Leaderboards.WeeklyCadenceSeconds;

        private static Leaderboards.Standing S(string id, long score, long achieved)
            => new Leaderboards.Standing { EntrantId = id, Score = score, AchievedUnix = achieved };

        // ---- seasons ------------------------------------------------------------------------------

        [Test]
        public void BeforeTheEpochEverythingIsSeasonZero()
        {
            Assert.That(Leaderboards.SeasonIndex(Epoch, Week, Epoch - 1L), Is.EqualTo(0L));
            Assert.That(Leaderboards.SeasonIndex(Epoch, Week, 1L), Is.EqualTo(0L));
        }

        /// <summary>The start second is INSIDE the season — the boundary a countdown reaches zero on.</summary>
        [Test]
        public void TheStartSecondBelongsToTheSeasonItOpens()
        {
            Assert.That(Leaderboards.SeasonIndex(Epoch, Week, Epoch), Is.EqualTo(0L));
            Assert.That(Leaderboards.SeasonIndex(Epoch, Week, Epoch + Week), Is.EqualTo(1L));
        }

        /// <summary>And the end second is OUTSIDE it. Half-open, so no score can be submitted into two
        /// seasons at once.</summary>
        [Test]
        public void TheLastSecondOfASeasonIsStillThatSeason()
        {
            Assert.That(Leaderboards.SeasonIndex(Epoch, Week, Epoch + Week - 1L), Is.EqualTo(0L));
        }

        [Test]
        public void ASeasonEndsExactlyWhereTheNextBegins()
        {
            Assert.That(Leaderboards.SeasonEndUnix(Epoch, Week, 3L),
                        Is.EqualTo(Leaderboards.SeasonStartUnix(Epoch, Week, 4L)));
        }

        [Test]
        public void SecondsLeftRunsDownToTheBoundaryAndStopsAtZero()
        {
            Assert.That(Leaderboards.SecondsLeftInSeason(Epoch, Week, Epoch), Is.EqualTo(Week));
            Assert.That(Leaderboards.SecondsLeftInSeason(Epoch, Week, Epoch + Week - 1L), Is.EqualTo(1L));
            // The boundary second is the next season's first, so a full week is left again.
            Assert.That(Leaderboards.SecondsLeftInSeason(Epoch, Week, Epoch + Week), Is.EqualTo(Week));
        }

        [Test]
        public void ABrokenCadenceIsRefusedRatherThanDividedBy()
        {
            Assert.That(Leaderboards.IsWellFormedCadence(Epoch, 0L), Is.False);
            Assert.That(Leaderboards.SeasonIndex(Epoch, 0L, Epoch + Week), Is.EqualTo(0L));
            Assert.That(Leaderboards.SecondsLeftInSeason(Epoch, -5L, Epoch), Is.EqualTo(0L));
        }

        // ---- season ids ---------------------------------------------------------------------------

        [Test]
        public void SeasonIdRoundTrips()
        {
            string id = Leaderboards.SeasonId("lig", 137L);
            Assert.That(id, Is.EqualTo("lig-137"));
            Assert.That(Leaderboards.TryParseSeasonIndex("lig", id, out long back), Is.True);
            Assert.That(back, Is.EqualTo(137L));
        }

        /// <summary>
        /// The bug this pins is a shipped one in this project's history: a number formatted under the
        /// machine's own culture. A season id built on Turkish Windows must be byte-identical to one
        /// built anywhere else, or the two devices address different rows and a reward is lost.
        /// </summary>
        [Test]
        public void SeasonIdIsTheSameStringInEveryCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
                Assert.That(Leaderboards.SeasonId("lig", 1234567L), Is.EqualTo("lig-1234567"));
                Assert.That(Leaderboards.TryParseSeasonIndex("lig", "lig-1234567", out long back), Is.True);
                Assert.That(back, Is.EqualTo(1234567L));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void AnIdItDidNotBuildIsRefusedRatherThanGuessedAt()
        {
            Assert.That(Leaderboards.TryParseSeasonIndex("lig", "turnuva-3", out _), Is.False);
            Assert.That(Leaderboards.TryParseSeasonIndex("lig", "lig3", out _), Is.False);
            Assert.That(Leaderboards.TryParseSeasonIndex("lig", "lig-", out _), Is.False);
            Assert.That(Leaderboards.TryParseSeasonIndex("lig", "lig--1", out _), Is.False);
            Assert.That(Leaderboards.TryParseSeasonIndex("lig", "lig-1,5", out _), Is.False);
            Assert.That(Leaderboards.TryParseSeasonIndex("lig", null, out _), Is.False);
        }

        // ---- ranking and ties ---------------------------------------------------------------------

        [Test]
        public void TheHigherScoreRanksAhead()
        {
            Assert.That(Leaderboards.Compare(S("a", 100L, 50L), S("b", 99L, 10L)), Is.LessThan(0));
        }

        /// <summary>Level on score, whoever got there FIRST places ahead — the tie-break, and the
        /// reason a submission carries a timestamp.</summary>
        [Test]
        public void ATieBreaksOnWhoReachedItFirst()
        {
            Assert.That(Leaderboards.Compare(S("a", 100L, 10L), S("b", 100L, 11L)), Is.LessThan(0));
            Assert.That(Leaderboards.Compare(S("a", 100L, 12L), S("b", 100L, 11L)), Is.GreaterThan(0));
        }

        /// <summary>And when even that is level, the entrant id. The order has to be TOTAL or the same
        /// board renders in two orders on two devices.</summary>
        [Test]
        public void ADeadHeatBreaksOnTheEntrantId()
        {
            Assert.That(Leaderboards.Compare(S("a", 100L, 10L), S("b", 100L, 10L)), Is.LessThan(0));
            Assert.That(Leaderboards.Compare(S("b", 100L, 10L), S("b", 100L, 10L)), Is.EqualTo(0));
        }

        [Test]
        public void RankingIsTheSameWhateverOrderTheEntriesArriveIn()
        {
            var forwards = new[] { S("a", 30L, 5L), S("b", 20L, 5L), S("c", 20L, 4L), S("d", 10L, 1L) };
            var backwards = new[] { S("d", 10L, 1L), S("c", 20L, 4L), S("b", 20L, 5L), S("a", 30L, 5L) };

            Leaderboards.Rank(forwards, forwards.Length);
            Leaderboards.Rank(backwards, backwards.Length);

            for (int i = 0; i < forwards.Length; i++)
                Assert.That(backwards[i].EntrantId, Is.EqualTo(forwards[i].EntrantId), "row " + i);

            // c beats b on the earlier achievement despite the identical score.
            Assert.That(forwards[0].EntrantId, Is.EqualTo("a"));
            Assert.That(forwards[1].EntrantId, Is.EqualTo("c"));
            Assert.That(forwards[2].EntrantId, Is.EqualTo("b"));
            Assert.That(forwards[3].EntrantId, Is.EqualTo("d"));
        }

        [Test]
        public void RankOfIsOneBasedAndZeroForAnEntrantWhoIsNotThere()
        {
            var ranked = new[] { S("a", 30L, 5L), S("b", 20L, 5L), S("c", 10L, 5L) };
            Leaderboards.Rank(ranked, ranked.Length);

            Assert.That(Leaderboards.RankOf(ranked, ranked.Length, "a"), Is.EqualTo(1));
            Assert.That(Leaderboards.RankOf(ranked, ranked.Length, "c"), Is.EqualTo(3));
            Assert.That(Leaderboards.RankOf(ranked, ranked.Length, "zzz"), Is.EqualTo(0));
        }

        [Test]
        public void RankSurvivesANullListAndAnOverlongCount()
        {
            Leaderboards.Rank(null, 4);                       // must not throw
            var one = new[] { S("a", 1L, 1L) };
            Leaderboards.Rank(one, 99);
            Assert.That(one[0].EntrantId, Is.EqualTo("a"));
        }

        // ---- reward brackets ----------------------------------------------------------------------

        [Test]
        public void BracketsCoverThePodiumThenTheBandsBeneathIt()
        {
            int[] b = Leaderboards.DefaultBracketEnds;
            Assert.That(Leaderboards.RewardTier(1, b), Is.EqualTo(0));
            Assert.That(Leaderboards.RewardTier(2, b), Is.EqualTo(1));
            Assert.That(Leaderboards.RewardTier(3, b), Is.EqualTo(2));
            Assert.That(Leaderboards.RewardTier(4, b), Is.EqualTo(3));
            Assert.That(Leaderboards.RewardTier(10, b), Is.EqualTo(3));
            Assert.That(Leaderboards.RewardTier(11, b), Is.EqualTo(4));
            Assert.That(Leaderboards.RewardTier(20, b), Is.EqualTo(4));
            Assert.That(Leaderboards.RewardTier(21, b), Is.EqualTo(5));
            Assert.That(Leaderboards.RewardTier(Leaderboards.CohortSize, b), Is.EqualTo(5));
        }

        /// <summary>Rank 0 means "not on the board" and must never map to the top bracket.</summary>
        [Test]
        public void NoRankMeansNoBracket()
        {
            Assert.That(Leaderboards.RewardTier(0, Leaderboards.DefaultBracketEnds), Is.EqualTo(-1));
            Assert.That(Leaderboards.RewardTier(-3, Leaderboards.DefaultBracketEnds), Is.EqualTo(-1));
            Assert.That(Leaderboards.RewardTier(Leaderboards.CohortSize + 1,
                                                Leaderboards.DefaultBracketEnds), Is.EqualTo(-1));
            Assert.That(Leaderboards.RewardTier(1, null), Is.EqualTo(-1));
        }

        // ---- idempotency --------------------------------------------------------------------------

        [Test]
        public void TheRecordKeepsTheLargerOfWhatItHasAndWhatItIsSent()
        {
            Assert.That(Leaderboards.MergeScore(100L, 250L), Is.EqualTo(250L));
            Assert.That(Leaderboards.MergeScore(250L, 100L), Is.EqualTo(250L));
        }

        /// <summary>The whole point of an absolute score: a duplicate delivery is a no-op.</summary>
        [Test]
        public void ReplayingASubmissionChangesNothing()
        {
            long best = Leaderboards.MergeScore(0L, 500L);
            for (int i = 0; i < 10; i++) best = Leaderboards.MergeScore(best, 500L);
            Assert.That(best, Is.EqualTo(500L));
        }

        [Test]
        public void ACorruptNegativeCannotDeleteARealScore()
        {
            Assert.That(Leaderboards.MergeScore(500L, -9L), Is.EqualTo(500L));
            Assert.That(Leaderboards.MergeScore(-9L, -9L), Is.EqualTo(0L));
        }

        [Test]
        public void AnAcknowledgementLosesNeitherSide()
        {
            Assert.That(Leaderboards.AdoptAck(700L, 400L), Is.EqualTo(700L));  // earned mid-flight
            Assert.That(Leaderboards.AdoptAck(400L, 700L), Is.EqualTo(700L));  // sent from elsewhere
        }

        [Test]
        public void AMalformedSubmissionIsRefusedAtTheClient()
        {
            var good = new Leaderboards.Submission
            { SeasonId = "lig-1", Score = 10L, AchievedUnix = 99L, Sequence = 1L };
            Assert.That(Leaderboards.IsWellFormed(good), Is.True);

            var noSeason = good; noSeason.SeasonId = "";
            var noStamp = good; noStamp.AchievedUnix = 0L;
            var noSeq = good; noSeq.Sequence = 0L;
            var negative = good; negative.Score = -1L;

            Assert.That(Leaderboards.IsWellFormed(noSeason), Is.False);
            Assert.That(Leaderboards.IsWellFormed(noStamp), Is.False);
            Assert.That(Leaderboards.IsWellFormed(noSeq), Is.False);
            Assert.That(Leaderboards.IsWellFormed(negative), Is.False);
        }

        [Test]
        public void TheOutboxCollapsesRatherThanQueues()
        {
            var pending = new Leaderboards.Submission
            { SeasonId = "lig-1", Score = 100L, AchievedUnix = 10L, Sequence = 1L };
            var better = new Leaderboards.Submission
            { SeasonId = "lig-1", Score = 250L, AchievedUnix = 20L, Sequence = 2L };
            var worse = new Leaderboards.Submission
            { SeasonId = "lig-1", Score = 40L, AchievedUnix = 20L, Sequence = 3L };
            var nextSeason = new Leaderboards.Submission
            { SeasonId = "lig-2", Score = 250L, AchievedUnix = 20L, Sequence = 4L };

            Assert.That(Leaderboards.Supersedes(pending, better), Is.True);
            Assert.That(Leaderboards.Supersedes(pending, worse), Is.False);
            Assert.That(Leaderboards.Supersedes(pending, nextSeason), Is.False);
        }

        // ---- bands and cohort seeds ---------------------------------------------------------------

        [Test]
        public void BandsGroupTwoIslandsAndThenStopGrowing()
        {
            Assert.That(Leaderboards.BandOf(0), Is.EqualTo(0));   // clamped: nobody owns nothing
            Assert.That(Leaderboards.BandOf(1), Is.EqualTo(0));
            Assert.That(Leaderboards.BandOf(2), Is.EqualTo(0));
            Assert.That(Leaderboards.BandOf(3), Is.EqualTo(1));
            Assert.That(Leaderboards.BandOf(8), Is.EqualTo(3));
            Assert.That(Leaderboards.BandOf(99), Is.EqualTo(Leaderboards.BandCount - 1));
        }

        [Test]
        public void ACohortSeedIsStableAndNeverNegative()
        {
            int a = Leaderboards.CohortSeed("lig-4", 2);
            Assert.That(Leaderboards.CohortSeed("lig-4", 2), Is.EqualTo(a));
            Assert.That(a, Is.GreaterThanOrEqualTo(0));
            Assert.That(Leaderboards.CohortSeed("lig-5", 2), Is.Not.EqualTo(a));
            Assert.That(Leaderboards.CohortSeed("lig-4", 3), Is.Not.EqualTo(a));
        }

        // ---- the stub -----------------------------------------------------------------------------

        /// <summary>What a build actually registers today. Every call answers Unavailable and nothing
        /// throws, so a screen written against the seam is safe before a backend exists.</summary>
        [Test]
        public void TheStubAnswersUnavailableToEverything()
        {
            ILeaderboardService stub = new StubLeaderboardService();
            Assert.That(stub.Available, Is.False);
            Assert.That(stub.CurrentSeasonId, Is.EqualTo(""));
            Assert.That(stub.SecondsLeftInSeason, Is.EqualTo(0L));

            LeaderboardSubmitResult submit = default;
            stub.SubmitScore(500L, r => submit = r);
            Assert.That(submit.Status, Is.EqualTo(LeaderboardStatus.Unavailable));

            LeaderboardBoard board = null;
            stub.RequestBoard(b => board = b);
            Assert.That(board, Is.Not.Null);
            Assert.That(board.Status, Is.EqualTo(LeaderboardStatus.Unavailable));
            Assert.That(board.Entries, Is.Not.Null);
            Assert.That(board.Entries.Length, Is.EqualTo(0));

            LeaderboardSettlement settled = default;
            stub.RequestSettlement("lig-1", s => settled = s);
            Assert.That(settled.Status, Is.EqualTo(LeaderboardStatus.Unavailable));
            Assert.That(settled.RewardTier, Is.EqualTo(-1));

            Assert.DoesNotThrow(() => stub.Flush());
        }

        // ---- the double ---------------------------------------------------------------------------

        private const long TestCadence = 100L;

        /// <summary>A double whose current season is index 0 and has just opened, measured from the
        /// real clock — the only clock <see cref="TimeService"/> reads.</summary>
        private static LocalLeaderboardService Double(int islands = 1)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var service = new LocalLeaderboardService(null, now - 1L, TestCadence);
            service.IslandsOwned = islands;
            return service;
        }

        private static LeaderboardBoard BoardOf(LocalLeaderboardService service)
        {
            LeaderboardBoard board = null;
            service.RequestBoard(b => board = b);
            return board;
        }

        [Test]
        public void TheDoubleAcceptsAScoreAndKeepsIt()
        {
            var service = Double();
            LeaderboardSubmitResult result = default;
            service.SubmitScore(1500L, r => result = r);

            Assert.That(result.Status, Is.EqualTo(LeaderboardStatus.Ok));
            Assert.That(result.AcceptedScore, Is.EqualTo(1500L));
            Assert.That(service.AcceptedScore(service.CurrentSeasonId), Is.EqualTo(1500L));
        }

        [Test]
        public void SubmittingTheSameScoreTwiceLeavesTheBoardWhereItWas()
        {
            var service = Double();
            service.SubmitScore(1500L, null);
            int firstRank = BoardOf(service).PlayerRank;

            LeaderboardSubmitResult again = default;
            service.SubmitScore(1500L, r => again = r);

            Assert.That(again.AcceptedScore, Is.EqualTo(1500L));
            Assert.That(BoardOf(service).PlayerRank, Is.EqualTo(firstRank));
        }

        [Test]
        public void ALowerScoreNeverReplacesABetterOne()
        {
            var service = Double();
            service.SubmitScore(2000L, null);
            service.SubmitScore(50L, null);
            Assert.That(service.AcceptedScore(service.CurrentSeasonId), Is.EqualTo(2000L));
        }

        [Test]
        public void OfflineTheSubmissionWaitsInTheOutboxAndTheFlushDeliversIt()
        {
            var service = Double();
            service.Reachable = false;

            LeaderboardSubmitResult offline = default;
            service.SubmitScore(900L, r => offline = r);

            Assert.That(offline.Status, Is.EqualTo(LeaderboardStatus.Offline));
            Assert.That(offline.Pending, Is.True);
            Assert.That(service.HasPending, Is.True);
            Assert.That(service.AcceptedScore(service.CurrentSeasonId), Is.EqualTo(0L));

            service.Flush();                                    // still offline: nothing moves
            Assert.That(service.HasPending, Is.True);

            service.Reachable = true;
            service.Flush();

            Assert.That(service.HasPending, Is.False);
            Assert.That(service.AcceptedScore(service.CurrentSeasonId), Is.EqualTo(900L));
        }

        [Test]
        public void ManyOfflineSubmissionsCollapseIntoOneAndTheBestSurvives()
        {
            var service = Double();
            service.Reachable = false;

            service.SubmitScore(100L, null);
            service.SubmitScore(700L, null);
            LeaderboardSubmitResult last = default;
            service.SubmitScore(300L, r => last = r);

            Assert.That(service.HasPending, Is.True);
            // The player's own row reads the best of the three, not the last of them.
            Assert.That(last.AcceptedScore, Is.EqualTo(700L));

            service.Reachable = true;
            service.Flush();
            Assert.That(service.AcceptedScore(service.CurrentSeasonId), Is.EqualTo(700L));
        }

        /// <summary>
        /// The plane-lands-on-Monday case. A score earned in a season that has since closed is DROPPED
        /// on flush, never carried into the running one — that would be a head start in a season the
        /// player had not played.
        /// </summary>
        [Test]
        public void AScoreStrandedInAClosedSeasonIsDroppedRatherThanCarriedForward()
        {
            var service = Double();
            string oldSeason = service.CurrentSeasonId;

            service.Reachable = false;
            service.SubmitScore(5000L, null);

            service.TimeOffsetSeconds = TestCadence;            // a season rolls over while offline
            string newSeason = service.CurrentSeasonId;
            Assert.That(newSeason, Is.Not.EqualTo(oldSeason));

            service.Reachable = true;
            service.Flush();

            Assert.That(service.HasPending, Is.False);
            Assert.That(service.AcceptedScore(oldSeason), Is.EqualTo(0L));
            Assert.That(service.AcceptedScore(newSeason), Is.EqualTo(0L));
        }

        [Test]
        public void TheBoardIsFullTheSameEveryTimeAndSaysThatItIsSynthetic()
        {
            var service = Double();
            service.SubmitScore(1200L, null);

            LeaderboardBoard first = BoardOf(service);
            LeaderboardBoard second = BoardOf(service);

            Assert.That(first.Status, Is.EqualTo(LeaderboardStatus.Ok));
            Assert.That(first.Synthetic, Is.True);
            Assert.That(first.Entries.Length, Is.EqualTo(Leaderboards.CohortSize));
            Assert.That(first.PlayerRank, Is.InRange(1, Leaderboards.CohortSize));

            for (int i = 0; i < first.Entries.Length; i++)
            {
                Assert.That(second.Entries[i].Name, Is.EqualTo(first.Entries[i].Name), "row " + i);
                Assert.That(second.Entries[i].Score, Is.EqualTo(first.Entries[i].Score), "row " + i);
                Assert.That(first.Entries[i].Rank, Is.EqualTo(i + 1));
            }
        }

        [Test]
        public void ExactlyOneRowIsThePlayerAndItIsTheirRank()
        {
            var service = Double();
            service.SubmitScore(1200L, null);
            LeaderboardBoard board = BoardOf(service);

            int found = 0;
            for (int i = 0; i < board.Entries.Length; i++)
                if (board.Entries[i].IsPlayer)
                {
                    found++;
                    Assert.That(board.Entries[i].Rank, Is.EqualTo(board.PlayerRank));
                    Assert.That(board.Entries[i].Score, Is.EqualTo(board.PlayerScore));
                }

            Assert.That(found, Is.EqualTo(1));
        }

        /// <summary>
        /// The cohort must not rescale itself around the player. Two runs of the same season and band
        /// with wildly different player scores have to produce the SAME opponents — anything else is a
        /// rigged ladder, and it would make every ranking test above meaningless.
        /// </summary>
        [Test]
        public void TheCohortDoesNotChaseThePlayer()
        {
            var quiet = Double();
            quiet.SubmitScore(1L, null);
            var loud = Double();
            loud.SubmitScore(9_000_000L, null);

            LeaderboardBoard a = BoardOf(quiet);
            LeaderboardBoard b = BoardOf(loud);

            // Compare the opponents only; the player's own row is expected to differ.
            int compared = 0;
            for (int i = 0, j = 0; i < a.Entries.Length && j < b.Entries.Length; i++, j++)
            {
                while (i < a.Entries.Length && a.Entries[i].IsPlayer) i++;
                while (j < b.Entries.Length && b.Entries[j].IsPlayer) j++;
                if (i >= a.Entries.Length || j >= b.Entries.Length) break;

                Assert.That(b.Entries[j].Name, Is.EqualTo(a.Entries[i].Name));
                Assert.That(b.Entries[j].Score, Is.EqualTo(a.Entries[i].Score));
                compared++;
            }

            Assert.That(compared, Is.EqualTo(Leaderboards.CohortSize - 1));
            Assert.That(loud.AcceptedScore(loud.CurrentSeasonId), Is.GreaterThan(quiet.AcceptedScore(quiet.CurrentSeasonId)));
        }

        [Test]
        public void AHigherScoreNeverRanksWorse()
        {
            var quiet = Double();
            quiet.SubmitScore(1L, null);
            var loud = Double();
            loud.SubmitScore(9_000_000L, null);

            Assert.That(BoardOf(loud).PlayerRank, Is.LessThanOrEqualTo(BoardOf(quiet).PlayerRank));
            Assert.That(BoardOf(loud).PlayerRank, Is.EqualTo(1));
        }

        [Test]
        public void OfflineTheBoardSaysSoInsteadOfInventingOne()
        {
            var service = Double();
            service.Reachable = false;

            LeaderboardBoard board = BoardOf(service);
            Assert.That(board.Status, Is.EqualTo(LeaderboardStatus.Offline));
            Assert.That(board.Entries.Length, Is.EqualTo(0));
        }

        [Test]
        public void ASeasonThatHasNotFinishedHasNothingToSettle()
        {
            var service = Double();
            LeaderboardSettlement settled = default;
            service.RequestSettlement(service.CurrentSeasonId, s => settled = s);

            Assert.That(settled.Status, Is.EqualTo(LeaderboardStatus.Rejected));
            Assert.That(settled.RewardTier, Is.EqualTo(-1));
        }

        [Test]
        public void AClosedSeasonSettlesToARankAndABracket()
        {
            var service = Double();
            string season = service.CurrentSeasonId;
            service.SubmitScore(9_000_000L, null);

            service.TimeOffsetSeconds = TestCadence;            // the season closes

            LeaderboardSettlement settled = default;
            service.RequestSettlement(season, s => settled = s);

            Assert.That(settled.Status, Is.EqualTo(LeaderboardStatus.Ok));
            Assert.That(settled.SeasonId, Is.EqualTo(season));
            Assert.That(settled.PlayerRank, Is.EqualTo(1));
            Assert.That(settled.RewardTier, Is.EqualTo(0));
            Assert.That(settled.Synthetic, Is.True);
        }

        /// <summary>Settling twice must answer the same thing. A settlement is a QUESTION, and the
        /// idempotent claim flag lives in whichever service grants the reward.</summary>
        [Test]
        public void SettlingTheSameSeasonTwiceAnswersTheSame()
        {
            var service = Double();
            string season = service.CurrentSeasonId;
            service.SubmitScore(4000L, null);
            service.TimeOffsetSeconds = TestCadence;

            LeaderboardSettlement first = default, second = default;
            service.RequestSettlement(season, s => first = s);
            service.RequestSettlement(season, s => second = s);

            Assert.That(second.PlayerRank, Is.EqualTo(first.PlayerRank));
            Assert.That(second.RewardTier, Is.EqualTo(first.RewardTier));
        }

        [Test]
        public void AnUnparseableSeasonIdIsRefused()
        {
            var service = Double();
            LeaderboardSettlement settled = default;
            service.RequestSettlement("turnuva-1", s => settled = s);
            Assert.That(settled.Status, Is.EqualTo(LeaderboardStatus.Rejected));
        }

        [Test]
        public void AMovedScoreRaisesChangedAndAFlatOneDoesNot()
        {
            var service = Double();
            int changes = 0;
            service.Changed += () => changes++;

            service.SubmitScore(500L, null);
            Assert.That(changes, Is.EqualTo(1));

            service.SubmitScore(500L, null);
            Assert.That(changes, Is.EqualTo(1));

            service.SubmitScore(900L, null);
            Assert.That(changes, Is.EqualTo(2));
        }

        [Test]
        public void ADifferentBandGetsADifferentCohort()
        {
            var coal = Double(1);
            var diamond = Double(8);
            coal.SubmitScore(1L, null);
            diamond.SubmitScore(1L, null);

            LeaderboardBoard a = BoardOf(coal);
            LeaderboardBoard b = BoardOf(diamond);

            bool anyDifferent = false;
            for (int i = 0; i < a.Entries.Length; i++)
                if (a.Entries[i].Score != b.Entries[i].Score) { anyDifferent = true; break; }

            Assert.That(anyDifferent, Is.True);
        }

        [Test]
        public void TheDoubleNeverPretendsToBeReal()
        {
            var service = Double();
            Assert.That(service.Available, Is.True);
            Assert.That(service.Synthetic, Is.True);
            Assert.That(BoardOf(service).Synthetic, Is.True);
        }
    }
}
