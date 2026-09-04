using Game.Core;
using Game.Systems;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>
    /// The three-day league as the game plays it: what the score is, what a rollover settles, and
    /// what a claim pays. <see cref="LeaderboardsTests"/> already pins the ranking arithmetic
    /// underneath this — seasons, the total order, brackets, the merge rule — so nothing here
    /// re-tests those.
    /// </summary>
    public sealed class LadderTests
    {
        private const long ThreeDays = Leaderboards.ThreeDayCadenceSeconds;

        private static long Now => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>Seconds until the season the clock is really in closes. The epoch is months in
        /// the past, so the live season index is a large number rather than 0 — every expectation
        /// below is derived from the clock instead of assuming a fresh ladder.</summary>
        private static long SecondsLeftNow
            => Leaderboards.SecondsLeftInSeason(Leaderboards.SeasonEpochUnix, ThreeDays, Now);

        private static string Season(long stepsAhead = 0L)
            => Leaderboards.SeasonId("lig",
                   Leaderboards.SeasonIndex(Leaderboards.SeasonEpochUnix, ThreeDays, Now) + stepsAhead);

        private sealed class Rig
        {
            public SaveData Data;
            public GoalService Goals;
            public WalletService Wallet;
            public LocalLeaderboardService Board;
            public LadderService Ladder;

            /// <summary>Sells bars, which is the only thing the league measures.</summary>
            public void Sell(long bars) => Goals.Record(LadderService.ScoreMetric, bars);
        }

        private static Rig New(SaveData data = null, long offsetSeconds = 0L)
        {
            if (data == null) data = new SaveData();

            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet);
            var board = new LocalLeaderboardService(null, Leaderboards.SeasonEpochUnix, ThreeDays);

            // Set before the service is built: LadderService syncs in its constructor, and a rig that
            // moved its clock afterwards would have already opened the wrong season.
            board.TimeOffsetSeconds = offsetSeconds;

            var ladder = new LadderService(data, null, goals, board, wallet);
            return new Rig { Data = data, Goals = goals, Wallet = wallet, Board = board, Ladder = ladder };
        }

        // ------------------------------------------------------------------------ the payout table
        [Test]
        public void ShippedRewardTableIsWellFormedAndMatchesTheDocument()
        {
            Ladder.Tuning tuning = Ladder.Tuning.Default;

            Assert.That(Ladder.IsWellFormed(tuning), Is.True);
            Assert.That(tuning.Brackets.Length, Is.EqualTo(Leaderboards.DefaultBracketEnds.Length));

            Assert.That(Ladder.RewardFor(0, tuning).Gems, Is.EqualTo(150L));
            Assert.That(Ladder.RewardFor(0, tuning).Cards, Is.EqualTo(3));
            Assert.That(Ladder.RewardFor(5, tuning).Gems, Is.EqualTo(10L));
            Assert.That(Ladder.RewardFor(5, tuning).Cards, Is.EqualTo(0));
        }

        /// <summary>
        /// -1 is what <see cref="Leaderboards.RewardTier"/> answers for a player who was not on the
        /// board at all. An unchecked array read would turn it into a reward, so it is pinned.
        /// </summary>
        [Test]
        public void ARankOutsideEveryBracketPaysNothing()
        {
            Ladder.Tuning tuning = Ladder.Tuning.Default;

            Assert.That(Ladder.RewardFor(-1, tuning).Gems, Is.EqualTo(0L));
            Assert.That(Ladder.RewardFor(99, tuning).Gems, Is.EqualTo(0L));
            Assert.That(Ladder.Pays(-1, tuning), Is.False);
            Assert.That(Ladder.Pays(0, tuning), Is.True);
        }

        /// <summary>A tuning that arrives short must be refused outright rather than paying nothing
        /// for the ranks past its end — a silent failure that looks exactly like finishing off the
        /// board.</summary>
        [Test]
        public void AShortOrNegativeRewardTableIsRefused()
        {
            Assert.That(Ladder.IsWellFormed(new Ladder.Tuning { Brackets = null }), Is.False);
            Assert.That(Ladder.IsWellFormed(new Ladder.Tuning
            {
                Brackets = new[] { new Ladder.Reward { Gems = 10L } },
            }), Is.False);

            var negative = Ladder.Tuning.Default;
            negative.Brackets[0].Gems = -1L;
            Assert.That(Ladder.IsWellFormed(negative), Is.False);
        }

        // ----------------------------------------------------------------------------- the score
        /// <summary>
        /// The baseline is what stops an existing player's whole career counting as one season's
        /// work. Every save that exists today has bars on it.
        /// </summary>
        [Test]
        public void ANewSeasonStartsFromTodaysBarsNotACareerTotal()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            new GoalService(data, wallet).Record(Goals.BarsSold, 5000L);

            Rig rig = New(data);

            Assert.That(rig.Ladder.Score, Is.EqualTo(0L));
            Assert.That(rig.Data.ladder.baseline, Is.EqualTo(5000L));
        }

        [Test]
        public void TheScoreIsBarsSoldSinceTheSeasonOpened()
        {
            Rig rig = New();
            rig.Sell(40L);

            Assert.That(rig.Ladder.Score, Is.EqualTo(40L));
            Assert.That(rig.Data.ladder.bestScore, Is.EqualTo(40L));
            Assert.That(rig.Data.ladder.bestAchievedUnix, Is.GreaterThan(0L));
        }

        /// <summary>
        /// THE SCORE MUST NOT FREEZE AT THE FIRST NUMBER IT SUBMITTED. The hot path moves the season's
        /// best in the save without telling the board, so anything that then asks "has the score moved
        /// since the save last changed?" answers no forever — the board keeps the first figure it was
        /// given while the player goes on selling, which is what shipped and stuck at 53.
        /// </summary>
        [Test]
        public void TheBoardFollowsTheScoreInsteadOfFreezingAtTheFirstSubmission()
        {
            Rig rig = New();

            rig.Sell(53L);
            LeaderboardBoard first = null;
            rig.Ladder.RequestBoard(b => first = b);
            Assert.That(first, Is.Not.Null);
            Assert.That(first.PlayerScore, Is.EqualTo(53L));

            rig.Sell(47L);
            LeaderboardBoard second = null;
            rig.Ladder.RequestBoard(b => second = b);
            Assert.That(second.PlayerScore, Is.EqualTo(100L),
                        "the board froze at the score it was first handed");
        }

        [Test]
        public void TheSeasonIdOnlyMovesOnTheThreeDayCadence()
        {
            // Aligned to the real boundary: ten seconds before it, and ten seconds after.
            Rig before = New(offsetSeconds: SecondsLeftNow - 10L);
            Rig after = New(offsetSeconds: SecondsLeftNow + 10L);

            Assert.That(before.Ladder.CurrentSeasonId, Is.EqualTo(Season()));
            Assert.That(after.Ladder.CurrentSeasonId, Is.EqualTo(Season(1)));
            Assert.That(before.Ladder.SecondsLeftInSeason, Is.InRange(1L, 12L));
        }

        // ------------------------------------------------------------------------- the rollover
        [Test]
        public void ARolloverSettlesTheClosedSeasonAndOpensTheNextOnAFreshBaseline()
        {
            Rig rig = New();
            rig.Sell(500L);
            Assert.That(rig.Ladder.Score, Is.EqualTo(500L));

            rig.Board.TimeOffsetSeconds = ThreeDays;
            rig.Ladder.Sync();

            Assert.That(rig.Data.ladder.inbox.Count, Is.EqualTo(1));
            Assert.That(rig.Data.ladder.inbox[0].seasonId, Is.EqualTo(Season()));
            Assert.That(rig.Data.ladder.seasonId, Is.EqualTo(Season(1)));
            Assert.That(rig.Ladder.Score, Is.EqualTo(0L), "the new season starts empty");
            Assert.That(rig.Data.ladder.baseline, Is.EqualTo(500L));
        }

        /// <summary>
        /// The idempotency key. A settlement delivered twice — by a second sync, a re-open, or two
        /// launches racing the same rollover — files one row.
        /// </summary>
        [Test]
        public void ASeasonIsSettledExactlyOnceHoweverOftenItIsSynced()
        {
            Rig rig = New();
            rig.Sell(500L);
            rig.Board.TimeOffsetSeconds = ThreeDays;

            for (int i = 0; i < 5; i++) rig.Ladder.Sync();

            Assert.That(rig.Data.ladder.inbox.Count, Is.EqualTo(1));
            Assert.That(rig.Data.ladder.settledSeasons.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// A reward for absence would make the bottom of the board worth as much as playing, so a
        /// season the player never scored in is not settled at all.
        /// </summary>
        [Test]
        public void ASeasonThePlayerNeverScoredInIsNotSettled()
        {
            Rig rig = New();

            rig.Board.TimeOffsetSeconds = ThreeDays;
            rig.Ladder.Sync();

            Assert.That(rig.Data.ladder.inbox, Is.Empty);
            Assert.That(rig.Data.ladder.seasonId, Is.EqualTo(Season(1)));
        }

        /// <summary>
        /// THE RESTART CASE, and the reason <c>LocalLeaderboardService.Restore</c> exists. The double
        /// persists nothing, so a season that closes while the app is shut would otherwise settle on
        /// the zero a fresh dictionary reports — paying a player who led the board the tail bracket.
        /// </summary>
        [Test]
        public void AScoreSurvivesARestartAndSettlesOnWhatWasActuallyEarned()
        {
            Rig first = New();
            first.Sell(100000L);
            Assert.That(first.Data.ladder.bestScore, Is.EqualTo(100000L));

            // Same save, brand-new services, and the clock has moved past the end of that season:
            // the app was closed inside season 0 and re-opened inside season 1.
            Rig restarted = New(first.Data, ThreeDays);

            Assert.That(restarted.Data.ladder.inbox.Count, Is.EqualTo(1));
            LadderInboxRow row = restarted.Data.ladder.inbox[0];
            Assert.That(row.seasonId, Is.EqualTo(Season()));
            Assert.That(row.rank, Is.GreaterThan(0));
            Assert.That(row.rank, Is.LessThan(Leaderboards.CohortSize),
                        "a score that large must not settle as last place");
            Assert.That(row.tier, Is.GreaterThanOrEqualTo(0));
        }

        // ---------------------------------------------------------------------------- the claim
        [Test]
        public void ClaimPaysTheBracketOnceAndRefusesTheSecondTap()
        {
            Rig rig = New();
            rig.Sell(100000L);
            rig.Board.TimeOffsetSeconds = ThreeDays;
            rig.Ladder.Sync();

            LadderInboxRow row = rig.Data.ladder.inbox[0];
            long expected = rig.Ladder.RewardFor(row.tier).Gems;
            Assert.That(expected, Is.GreaterThan(0L));

            long before = rig.Wallet.Gems;
            Assert.That(rig.Ladder.Claim(row.seasonId), Is.True);
            Assert.That(rig.Wallet.Gems, Is.EqualTo(before + expected));
            Assert.That(row.claimed, Is.True);

            Assert.That(rig.Ladder.Claim(row.seasonId), Is.False);
            Assert.That(rig.Wallet.Gems, Is.EqualTo(before + expected), "a second tap pays nothing");
        }

        [Test]
        public void ClaimingASeasonThatWasNeverSettledPaysNothing()
        {
            Rig rig = New();
            rig.Sell(500L);

            Assert.That(rig.Ladder.Claim(Season()), Is.False);
            Assert.That(rig.Ladder.Claim("bilinmeyen"), Is.False);
            Assert.That(rig.Wallet.Gems, Is.EqualTo(0L));
        }

        [Test]
        public void ClaimAllTakesEveryWaitingSeasonAndTheBadgeEmpties()
        {
            Rig rig = New();

            // Two seasons played and closed back to back.
            rig.Sell(100000L);
            rig.Board.TimeOffsetSeconds = ThreeDays;
            rig.Ladder.Sync();
            rig.Sell(100000L);
            rig.Board.TimeOffsetSeconds = ThreeDays * 2L;
            rig.Ladder.Sync();

            Assert.That(rig.Data.ladder.inbox.Count, Is.EqualTo(2));
            Assert.That(rig.Ladder.UnclaimedCount, Is.EqualTo(2));

            long before = rig.Wallet.Gems;
            Assert.That(rig.Ladder.ClaimAll(), Is.EqualTo(2));
            Assert.That(rig.Wallet.Gems, Is.GreaterThan(before));
            Assert.That(rig.Ladder.UnclaimedCount, Is.EqualTo(0));
            Assert.That(rig.Ladder.ClaimAll(), Is.EqualTo(0));
        }

        /// <summary>Nothing expires: a row from a season three weeks ago is still collectable, the
        /// same promise the port board makes about an unclaimed contract.</summary>
        [Test]
        public void AnOldSeasonsRewardIsStillWaitingWeeksLater()
        {
            Rig rig = New();
            rig.Sell(100000L);
            rig.Board.TimeOffsetSeconds = ThreeDays;
            rig.Ladder.Sync();

            rig.Board.TimeOffsetSeconds = ThreeDays * 7L;
            rig.Ladder.Sync();

            Assert.That(rig.Ladder.UnclaimedCount, Is.EqualTo(1));
            Assert.That(rig.Ladder.Claim(Season()), Is.True);
        }

        /// <summary>
        /// The board the screen draws. It must always come back labelled as generated — decision D4,
        /// and the one thing a UI is not allowed to be able to forget.
        /// </summary>
        [Test]
        public void EveryBoardTheLeagueHandsAScreenIsLabelledSynthetic()
        {
            Rig rig = New();
            rig.Sell(500L);

            LeaderboardBoard board = null;
            rig.Ladder.RequestBoard(b => board = b);

            Assert.That(rig.Ladder.Synthetic, Is.True);
            Assert.That(board, Is.Not.Null);
            Assert.That(board.Synthetic, Is.True);
            Assert.That(board.Entries.Length, Is.EqualTo(Leaderboards.CohortSize));
            Assert.That(board.PlayerRank, Is.GreaterThan(0));
        }

        /// <summary>Buying an island moves the band the player is matched in, and the league has to
        /// follow it — a board built for the wrong band measures them against a target their island
        /// cannot reach.</summary>
        [Test]
        public void OwningMoreIslandsMovesTheMatchingBand()
        {
            Rig rig = New();
            Assert.That(rig.Board.IslandsOwned, Is.EqualTo(1));

            rig.Data.unlockedIslands.Add("coal");
            rig.Data.unlockedIslands.Add("copper");
            rig.Data.unlockedIslands.Add("iron");
            rig.Ladder.Sync();

            Assert.That(rig.Board.IslandsOwned, Is.EqualTo(3));
            Assert.That(Leaderboards.BandOf(rig.Board.IslandsOwned), Is.EqualTo(1));
        }
    }
}
