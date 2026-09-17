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

            /// <summary>Sells bars — which a points season does not count, and a pre-points one does.</summary>
            public void Sell(long bars) => Goals.Record(LadderService.ScoreMetric, bars);

            /// <summary>Buys upgrades: one point each, up to the season cap.</summary>
            public void Upgrade(long count) => Goals.Record(Game.Core.Goals.Upgrades, count);

            /// <summary>Takes every scoring rule to its cap — the season's ceiling.</summary>
            public void MaxOut()
            {
                foreach (Game.Core.Ladder.ScoringRule rule in Game.Core.Ladder.Scoring) Goals.Record(rule.Metric, rule.SeasonCap);
            }
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

            Assert.That(Ladder.RewardFor(0, tuning).Gems, Is.EqualTo(100L));
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
        /// The baselines are what stop an existing player's whole career counting as one season's
        /// work. Every save that exists today has upgrades and contracts on it.
        /// </summary>
        [Test]
        public void ANewSeasonStartsFromTodaysCountersNotACareerTotal()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet);
            goals.Record(Goals.Upgrades, 5000L);
            goals.Record(Goals.Contracts, 70L);

            Rig rig = New(data);

            Assert.That(rig.Data.ladder.points, Is.True);
            Assert.That(rig.Ladder.Score, Is.EqualTo(0L));
            Assert.That(rig.Data.ladder.baselines[Goals.Upgrades], Is.EqualTo(5000L));
            Assert.That(rig.Data.ladder.baselines[Goals.Contracts], Is.EqualTo(70L));
        }

        [Test]
        public void TheScoreIsCappedPointsEarnedSinceTheSeasonOpened()
        {
            Rig rig = New();
            rig.Goals.Record(Goals.Contracts, 1L);
            rig.Goals.Record(Goals.Repairs, 2L);
            rig.Upgrade(300L);   // past the 250 cap

            long expected = Ladder.PointsFor(Ladder.Scoring[0], 300L) + 10L + 12L;
            Assert.That(expected, Is.EqualTo(250L + 10L + 12L));
            Assert.That(rig.Ladder.Score, Is.EqualTo(expected));
            Assert.That(rig.Data.ladder.bestScore, Is.EqualTo(expected));
            Assert.That(rig.Data.ladder.bestAchievedUnix, Is.GreaterThan(0L));
        }

        /// <summary>The reason points replaced bars: output inflates x3.2 per ore tier, counts do not,
        /// so selling a mountain of bars must not move a points season at all.</summary>
        [Test]
        public void BarsSoldDoNotScoreInAPointsSeason()
        {
            Rig rig = New();
            rig.Sell(9_000_000L);
            Assert.That(rig.Ladder.Score, Is.EqualTo(0L));
        }

        [Test]
        public void TheSeasonCeilingIsEveryRuleAtItsCap()
        {
            Assert.That(Ladder.MaxSeasonPoints, Is.EqualTo(250L + 300L + 180L + 150L));

            Rig rig = New();
            rig.MaxOut();
            rig.MaxOut();   // past every cap
            Assert.That(rig.Ladder.Score, Is.EqualTo(Ladder.MaxSeasonPoints));
        }

        // ------------------------------------------------------------------ pre-points seasons
        /// <summary>
        /// A player who updates mid-season keeps that season in bars: their saved best is in bars, and
        /// rescoring it would either wipe what they earned or compare bars with points. It settles in
        /// bars, and the season after it opens in points.
        /// </summary>
        [Test]
        public void ASeasonOpenedByAnOlderBuildFinishesInBarsAndTheNextScoresPoints()
        {
            var data = new SaveData();
            data.ladder.seasonId = Season();
            data.ladder.baseline = 0L;
            data.ladder.points = false;
            data.ladder.baselines = null;   // an older save has no such field

            Rig rig = New(data);
            rig.Sell(500L);
            rig.Upgrade(40L);
            Assert.That(rig.Data.ladder.points, Is.False);
            Assert.That(rig.Ladder.Score, Is.EqualTo(500L), "the running season still ranks bars");
            Assert.That(rig.Data.ladder.baselines.Length, Is.EqualTo(Goals.MetricCount), "repaired on load");

            rig.Board.TimeOffsetSeconds = ThreeDays;
            rig.Ladder.Sync();

            Assert.That(rig.Data.ladder.inbox.Count, Is.EqualTo(1), "the bars season settled");
            Assert.That(rig.Data.ladder.points, Is.True);
            rig.Sell(500L);
            rig.Upgrade(7L);
            Assert.That(rig.Ladder.Score, Is.EqualTo(7L), "the new season counts points, not bars");
        }

        /// <summary>
        /// What the points card shows as "x / cap": actions since the season opened, stopped at the
        /// rule's cap, and nothing at all in a season that still ranks bars.
        /// </summary>
        [Test]
        public void CountedActionsStartAtTheSeasonStopAtTheCapAndAreZeroInABarsSeason()
        {
            Rig rig = New();
            rig.Upgrade(40L);   // the rig's constructor has already opened the season, so these count
            Game.Core.Ladder.ScoringRule upgrades = Game.Core.Ladder.Scoring[0];
            Game.Core.Ladder.ScoringRule contracts = Game.Core.Ladder.Scoring[1];
            Assert.That(upgrades.Metric, Is.EqualTo(Goals.Upgrades));
            Assert.That(contracts.Metric, Is.EqualTo(Goals.Contracts));

            Assert.That(rig.Ladder.ScoresPoints, Is.True);
            Assert.That(rig.Ladder.CountedActions(upgrades), Is.EqualTo(40L));
            rig.Upgrade(upgrades.SeasonCap);
            Assert.That(rig.Ladder.CountedActions(upgrades), Is.EqualTo(upgrades.SeasonCap), "stops at the cap");
            Assert.That(rig.Ladder.CountedActions(contracts), Is.EqualTo(0L));

            var data = new SaveData();
            data.ladder.seasonId = Season();
            data.ladder.points = false;
            Rig bars = New(data);
            bars.Upgrade(12L);
            Assert.That(bars.Ladder.ScoresPoints, Is.False);
            Assert.That(bars.Ladder.CountedActions(upgrades), Is.EqualTo(0L));
        }

        /// <summary>The card's automatic open is a one-time thing, and closing it is what is saved.</summary>
        [Test]
        public void PointsHelpIsUnseenUntilMarkedAndStaysMarked()
        {
            Rig rig = New();
            Assert.That(rig.Ladder.PointsHelpSeen, Is.False);
            rig.Ladder.MarkPointsHelpSeen();
            Assert.That(rig.Ladder.PointsHelpSeen, Is.True);
            Assert.That(rig.Data.ladder.pointsHelpSeen, Is.True);
        }

        /// <summary>
        /// The generated cohort follows the season's unit. A points season is the same target in every
        /// band — the whole point of counting — and sits just under the ceiling, so maxing out wins.
        /// </summary>
        [Test]
        public void APointsSeasonCohortIsTheSameScaleInEveryBandAndAMaxedPlayerWins()
        {
            long topCoal = TopOpponent(1);
            long topDiamond = TopOpponent(8);

            long ceiling = (long)(Ladder.MaxSeasonPoints * LocalLeaderboardService.PointsTopShare);
            Assert.That(topCoal, Is.LessThanOrEqualTo(ceiling));
            Assert.That(topDiamond, Is.LessThanOrEqualTo(ceiling));
            Assert.That(topDiamond, Is.LessThan(topCoal * 2L), "a higher band must not multiply the target");

            Rig rig = New();
            rig.Data.unlockedIslands.Add("coal");
            rig.MaxOut();
            LeaderboardBoard board = null;
            rig.Ladder.RequestBoard(b => board = b);
            Assert.That(board.PlayerRank, Is.EqualTo(1));
        }

        private static long TopOpponent(int islands)
        {
            Rig rig = New();
            for (int i = 0; i < islands; i++) rig.Data.unlockedIslands.Add("ada" + i);
            rig.Ladder.Sync();
            LeaderboardBoard board = null;
            rig.Ladder.RequestBoard(b => board = b);
            long top = 0L;
            foreach (LeaderboardEntry entry in board.Entries)
                if (!entry.IsPlayer && entry.Score > top) top = entry.Score;
            return top;
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

            rig.Upgrade(53L);
            LeaderboardBoard first = null;
            rig.Ladder.RequestBoard(b => first = b);
            Assert.That(first, Is.Not.Null);
            Assert.That(first.PlayerScore, Is.EqualTo(53L));

            rig.Upgrade(47L);
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
            rig.Upgrade(200L);
            Assert.That(rig.Ladder.Score, Is.EqualTo(200L));

            rig.Board.TimeOffsetSeconds = ThreeDays;
            rig.Ladder.Sync();

            Assert.That(rig.Data.ladder.inbox.Count, Is.EqualTo(1));
            Assert.That(rig.Data.ladder.inbox[0].seasonId, Is.EqualTo(Season()));
            Assert.That(rig.Data.ladder.seasonId, Is.EqualTo(Season(1)));
            Assert.That(rig.Ladder.Score, Is.EqualTo(0L), "the new season starts empty");
            Assert.That(rig.Data.ladder.baselines[Goals.Upgrades], Is.EqualTo(200L));
        }

        /// <summary>
        /// The idempotency key. A settlement delivered twice — by a second sync, a re-open, or two
        /// launches racing the same rollover — files one row.
        /// </summary>
        [Test]
        public void ASeasonIsSettledExactlyOnceHoweverOftenItIsSynced()
        {
            Rig rig = New();
            rig.Upgrade(200L);
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
            first.MaxOut();
            Assert.That(first.Data.ladder.bestScore, Is.EqualTo(Ladder.MaxSeasonPoints));

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
            rig.MaxOut();
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
            rig.Upgrade(200L);

            Assert.That(rig.Ladder.Claim(Season()), Is.False);
            Assert.That(rig.Ladder.Claim("bilinmeyen"), Is.False);
            Assert.That(rig.Wallet.Gems, Is.EqualTo(0L));
        }

        [Test]
        public void ClaimAllTakesEveryWaitingSeasonAndTheBadgeEmpties()
        {
            Rig rig = New();

            // Two seasons played and closed back to back.
            rig.MaxOut();
            rig.Board.TimeOffsetSeconds = ThreeDays;
            rig.Ladder.Sync();
            rig.MaxOut();
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
            rig.MaxOut();
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
            rig.Upgrade(200L);

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
