using System;
using System.Collections.Generic;
using Game.Core;
using Game.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// The Top 50's own rules: the player's profile (name rules, saving, surviving a reload and a
    /// progress reset), the 49 rivals (unique, persistent, labelled), their progress through a season,
    /// and that ranks 31-50 settle without a reward.
    /// </summary>
    public sealed class LeagueProfileTests
    {
        private const long ThreeDays = Leaderboards.ThreeDayCadenceSeconds;

        // ---------------------------------------------------------------------------- name rules
        [Test]
        public void ANameIsTrimmedCollapsedAndStrippedOfMarkup()
        {
            Assert.That(PlayerProfiles.Sanitize("  Kaya    Kartal  "), Is.EqualTo("Kaya Kartal"));
            Assert.That(PlayerProfiles.Sanitize("<b>Ali</b>"), Is.EqualTo("bAlib"));
            Assert.That(PlayerProfiles.Sanitize("a\tb\nc"), Is.EqualTo("a b c"));
            Assert.That(PlayerProfiles.Sanitize("Maden_Kurdu-7.x"), Is.EqualTo("Maden_Kurdu-7.x"));
            Assert.That(PlayerProfiles.Sanitize("Çağrı Öztürk"), Is.EqualTo("Çağrı Öztürk"), "letters in any script");
            Assert.That(PlayerProfiles.Sanitize("Игрок"), Is.EqualTo("Игрок"));
            Assert.That(PlayerProfiles.Sanitize("Sea😀Dog"), Is.EqualTo("SeaDog"), "no emoji halves");
            Assert.That(PlayerProfiles.Sanitize(null), Is.Empty);
            Assert.That(PlayerProfiles.Sanitize("ABCDEFGHIJKLMNOPQRSTUVWXYZ"), Has.Length.EqualTo(PlayerProfiles.MaxNameLength));
        }

        [Test]
        public void ANameMustBeThreeToSixteenCharacters()
        {
            Assert.That(PlayerProfiles.IsValidName(""), Is.False);
            Assert.That(PlayerProfiles.IsValidName("ab"), Is.False);
            Assert.That(PlayerProfiles.IsValidName("abc"), Is.True);
            Assert.That(PlayerProfiles.IsValidName(new string('x', 16)), Is.True);
            Assert.That(PlayerProfiles.IsValidName(new string('x', 17)), Is.False);
        }

        // ---------------------------------------------------------------------------- the profile
        [Test]
        public void AProfileIsSavedCleanAndSurvivesAReload()
        {
            var data = new SaveData();
            var profiles = new PlayerProfileService(data, null);
            int changes = 0;
            profiles.Changed += () => changes++;

            Assert.That(profiles.HasName, Is.False);
            Assert.That(profiles.Prompted, Is.False);
            Assert.That(profiles.TrySet("  Deniz   Kurdu ", 7), Is.True);
            Assert.That(changes, Is.EqualTo(1));

            // Through the same serializer the save file uses.
            SaveData reloaded = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(data));
            var again = new PlayerProfileService(reloaded, null);
            Assert.That(again.Name, Is.EqualTo("Deniz Kurdu"));
            Assert.That(again.Avatar, Is.EqualTo(7));
            Assert.That(again.Prompted, Is.True);
        }

        [Test]
        public void ARefusedProfileWritesNothing()
        {
            var data = new SaveData();
            var profiles = new PlayerProfileService(data, null);
            Assert.That(profiles.TrySet("Kaptan", 3), Is.True);

            Assert.That(profiles.TrySet("<>", 3), Is.False, "nothing left after cleaning");
            Assert.That(profiles.TrySet("Kaptan", -1), Is.False);
            Assert.That(profiles.TrySet("Kaptan", PlayerProfiles.AvatarCount), Is.False);
            Assert.That(data.profile.name, Is.EqualTo("Kaptan"));
            Assert.That(data.profile.avatar, Is.EqualTo(3));
        }

        [Test]
        public void AHandEditedSaveStillReadsAsAValidProfile()
        {
            var data = new SaveData();
            data.profile.name = "<size=99>X";   // cleans to "size99X"
            data.profile.avatar = 999;
            var profiles = new PlayerProfileService(data, null);
            Assert.That(profiles.Name, Is.EqualTo("size99X"));
            Assert.That(profiles.Avatar, Is.EqualTo(0));

            data.profile.name = "a";
            Assert.That(profiles.Name, Is.Empty, "too short reads as no name, never as the raw text");
        }

        [Test]
        public void AProgressResetKeepsTheProfile()
        {
            var old = new SaveData();
            old.profile.name = "Kaptan";
            old.profile.avatar = 5;
            old.profile.prompted = true;

            SaveData fresh = SaveMigration.Reset(old);
            Assert.That(fresh.profile, Is.Not.SameAs(old.profile));
            Assert.That(fresh.profile.name, Is.EqualTo("Kaptan"));
            Assert.That(fresh.profile.avatar, Is.EqualTo(5));
            Assert.That(fresh.profile.prompted, Is.True);
        }

        // ------------------------------------------------------------------------------ rivals
        [Test]
        public void ThereAre49RivalsWithUniqueHandlesAndIds()
        {
            Assert.That(LeagueRivals.Count, Is.EqualTo(49));
            Assert.That(LeagueRivals.Handles, Has.Length.EqualTo(LeagueRivals.Count));

            var handles = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < LeagueRivals.Count; i++)
            {
                string handle = LeagueRivals.HandleOf(i);
                Assert.That(handles.Add(handle), Is.True, "duplicate handle " + handle);
                Assert.That(PlayerProfiles.Sanitize(handle), Is.EqualTo(handle), "a handle obeys the name rules: " + handle);
                Assert.That(PlayerProfiles.IsValidName(handle), Is.True, handle);

                Assert.That(LeagueRivals.IndexOf(LeagueRivals.IdOf(i)), Is.EqualTo(i));
                Assert.That(LeagueRivals.AvatarOf(i), Is.InRange(0, PlayerProfiles.AvatarCount - 1));
                if (i > 0) Assert.That(LeagueRivals.AvatarOf(i), Is.Not.EqualTo(LeagueRivals.AvatarOf(i - 1)));
            }

            Assert.That(LeagueRivals.IndexOf(LocalLeaderboardService.PlayerEntrantId), Is.EqualTo(-1));
            Assert.That(LeagueRivals.IndexOf("rakip-49"), Is.EqualTo(-1));
            Assert.That(LeagueRivals.IndexOf("rakip-x1"), Is.EqualTo(-1));
        }

        [Test]
        public void EverySeasonSeatsEveryRivalExactlyOnce()
        {
            foreach (int seed in new[] { 0, 1, 48, 49, 343, 12345, 2147483647, Leaderboards.CohortSeed("lig-99", 2) })
            {
                var seen = new bool[LeagueRivals.Count];
                for (int slot = 0; slot < LeagueRivals.Count; slot++)
                {
                    int rival = LeagueRivals.RivalInSlot(seed, slot);
                    Assert.That(rival, Is.InRange(0, LeagueRivals.Count - 1));
                    Assert.That(seen[rival], Is.False, "seed " + seed + " seats rival " + rival + " twice");
                    seen[rival] = true;
                }
            }
        }

        // ---------------------------------------------------------------------------- the board
        private static long SeasonStart(long now)
            => Leaderboards.SeasonStartUnix(Leaderboards.SeasonEpochUnix, ThreeDays,
                   Leaderboards.SeasonIndex(Leaderboards.SeasonEpochUnix, ThreeDays, now));

        private static LocalLeaderboardService Board(long clock)
        {
            var service = new LocalLeaderboardService(null, Leaderboards.SeasonEpochUnix, ThreeDays)
            {
                ClockOverrideUnix = clock,
            };
            service.MarkPointsSeason(service.CurrentSeasonId);
            return service;
        }

        private static LeaderboardBoard BoardOf(LocalLeaderboardService service)
        {
            LeaderboardBoard board = null;
            service.RequestBoard(b => board = b);
            return board;
        }

        private static Dictionary<string, long> ScoresByName(LeaderboardBoard board)
        {
            var scores = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (LeaderboardEntry e in board.Entries) if (!e.IsPlayer) scores.Add(e.Name, e.Score);
            return scores;
        }

        [Test]
        public void TheBoardIsATop50OfTheRivalsAndThePlayersProfile()
        {
            long start = SeasonStart(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            LocalLeaderboardService service = Board(start + ThreeDays / 2);
            service.PlayerName = "Kaptan";
            service.PlayerAvatar = 9;
            service.SubmitScore(120L, null);

            LeaderboardBoard board = BoardOf(service);
            Assert.That(board.Entries, Has.Length.EqualTo(50));
            Assert.That(board.Synthetic, Is.True);

            var names = new HashSet<string>(StringComparer.Ordinal);
            int players = 0;
            foreach (LeaderboardEntry e in board.Entries)
            {
                Assert.That(names.Add(e.Name), Is.True, "two rows named " + e.Name);
                if (e.IsPlayer)
                {
                    players++;
                    Assert.That(e.Name, Is.EqualTo("Kaptan"));
                    Assert.That(e.Avatar, Is.EqualTo(9));
                    Assert.That(e.Rank, Is.EqualTo(board.PlayerRank));
                    continue;
                }
                int rival = Array.IndexOf(LeagueRivals.Handles, e.Name);
                Assert.That(rival, Is.Not.EqualTo(-1), e.Name + " is not a rival");
                Assert.That(e.Avatar, Is.EqualTo(LeagueRivals.AvatarOf(rival)), "a rival always wears the same face");
            }
            Assert.That(players, Is.EqualTo(1));
        }

        /// <summary>A save/load is a fresh service fed the same season, band and score: it must draw
        /// the same board, row for row.</summary>
        [Test]
        public void AReloadRebuildsTheSameBoard()
        {
            long now = SeasonStart(DateTimeOffset.UtcNow.ToUnixTimeSeconds()) + ThreeDays / 3;
            LocalLeaderboardService before = Board(now);
            before.IslandsOwned = 3;
            before.SubmitScore(250L, null);
            LeaderboardBoard a = BoardOf(before);

            LocalLeaderboardService after = Board(now);
            after.IslandsOwned = 3;
            after.Restore(after.CurrentSeasonId, 250L, now);
            LeaderboardBoard b = BoardOf(after);

            for (int i = 0; i < a.Entries.Length; i++)
            {
                Assert.That(b.Entries[i].Name, Is.EqualTo(a.Entries[i].Name), "row " + i);
                Assert.That(b.Entries[i].Score, Is.EqualTo(a.Entries[i].Score), "row " + i);
                Assert.That(b.Entries[i].Avatar, Is.EqualTo(a.Entries[i].Avatar), "row " + i);
            }
            Assert.That(b.PlayerRank, Is.EqualTo(a.PlayerRank));
        }

        /// <summary>
        /// The rivals play through the season: every one of them has more points later in the season
        /// than earlier, none has more than the season-end target, and the first hour is not a board of
        /// zeros. A player with no points is last, not first on the tie-break.
        /// </summary>
        [Test]
        public void RivalsClimbThroughTheSeasonToTheirFinalTarget()
        {
            long start = SeasonStart(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Dictionary<string, long> early = ScoresByName(BoardOf(Board(start + 60L)));
            Dictionary<string, long> middle = ScoresByName(BoardOf(Board(start + ThreeDays / 2)));
            Dictionary<string, long> late = ScoresByName(BoardOf(Board(start + ThreeDays - 60L)));

            Assert.That(early.Count, Is.EqualTo(49));
            long earlyBest = 0L;
            foreach (KeyValuePair<string, long> rival in early)
            {
                Assert.That(rival.Value, Is.LessThanOrEqualTo(middle[rival.Key]), rival.Key);
                Assert.That(middle[rival.Key], Is.LessThanOrEqualTo(late[rival.Key]), rival.Key);
                if (rival.Value > earlyBest) earlyBest = rival.Value;
            }
            Assert.That(earlyBest, Is.GreaterThan(0L), "the first hour already has a board to read");

            // The last hour is the last step, and the last step is the full target: the same numbers a
            // settlement of this season ranks against (see ASettlementIsTheSameWheneverItIsAsked).
            LeaderboardBoard lastSecond = BoardOf(Board(start + ThreeDays - 1L));
            foreach (LeaderboardEntry e in lastSecond.Entries)
                if (!e.IsPlayer) Assert.That(e.Score, Is.EqualTo(late[e.Name]), e.Name);

            LeaderboardBoard idle = BoardOf(Board(start + 60L));
            Assert.That(idle.PlayerRank, Is.EqualTo(50), "a player on zero is behind every rival on zero");
        }

        /// <summary>The same season settles against the full targets whenever it is asked — the reward a
        /// season pays cannot depend on when the player came back to collect it.</summary>
        [Test]
        public void ASettlementIsTheSameWheneverItIsAsked()
        {
            long start = SeasonStart(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            LocalLeaderboardService soon = Board(start + ThreeDays / 2);
            string season = soon.CurrentSeasonId;
            soon.SubmitScore(300L, null);

            LocalLeaderboardService later = Board(start + ThreeDays / 2);
            later.SubmitScore(300L, null);

            soon.ClockOverrideUnix = start + ThreeDays + 5L;
            later.ClockOverrideUnix = start + 5L * ThreeDays;

            LeaderboardSettlement a = default, b = default;
            soon.RequestSettlement(season, s => a = s);
            later.RequestSettlement(season, s => b = s);
            Assert.That(a.Status, Is.EqualTo(LeaderboardStatus.Ok));
            Assert.That(b.PlayerRank, Is.EqualTo(a.PlayerRank));
            Assert.That(b.RewardTier, Is.EqualTo(a.RewardTier));
        }

        // ------------------------------------------------------------------------ the league
        /// <summary>
        /// A season the player scored one point in: they finish in the tail of the Top 50, below every
        /// paying rank. The season is settled (the player is told where they finished), and nothing is
        /// owed.
        /// </summary>
        [Test]
        public void ARankPastThirtySettlesWithNoReward()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet);
            var board = new LocalLeaderboardService(null, Leaderboards.SeasonEpochUnix, ThreeDays);
            var ladder = new LadderService(data, null, goals, board, wallet);
            goals.Record(Goals.Upgrades, 1L);
            ladder.Sync();
            Assert.That(data.ladder.bestScore, Is.EqualTo(1L));
            long gems = wallet.Gems;

            long left = Leaderboards.SecondsLeftInSeason(Leaderboards.SeasonEpochUnix, ThreeDays,
                                                         DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var restartedBoard = new LocalLeaderboardService(null, Leaderboards.SeasonEpochUnix, ThreeDays)
            {
                TimeOffsetSeconds = left + 5L,
            };
            var restarted = new LadderService(data, null, new GoalService(data, wallet), restartedBoard, wallet);

            Assert.That(data.ladder.inbox, Has.Count.EqualTo(1));
            LadderInboxRow row = data.ladder.inbox[0];
            Assert.That(row.rank, Is.GreaterThan(Leaderboards.RewardedRanks));
            Assert.That(row.rank, Is.LessThanOrEqualTo(Leaderboards.CohortSize));
            Assert.That(row.tier, Is.EqualTo(-1));
            Assert.That(restarted.UnclaimedCount, Is.EqualTo(0));
            Assert.That(restarted.Claim(row.seasonId), Is.False);
            Assert.That(wallet.Gems, Is.EqualTo(gems));
        }

        [Test]
        public void TheLeagueShowsTheSavedProfileOnThePlayersRow()
        {
            var data = new SaveData();
            data.profile.name = "Kaptan";
            data.profile.avatar = 11;
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet);
            var board = new LocalLeaderboardService(null, Leaderboards.SeasonEpochUnix, ThreeDays);
            var ladder = new LadderService(data, null, goals, board, wallet);

            LeaderboardBoard seen = null;
            ladder.RequestBoard(b => seen = b);
            LeaderboardEntry me = seen.Entries[seen.PlayerRank - 1];
            Assert.That(me.IsPlayer, Is.True);
            Assert.That(me.Name, Is.EqualTo("Kaptan"));
            Assert.That(me.Avatar, Is.EqualTo(11));

            // An edit shows on the next read, with no restart.
            new PlayerProfileService(data, null).TrySet("Yeni Ad", 2);
            ladder.RequestBoard(b => seen = b);
            me = seen.Entries[seen.PlayerRank - 1];
            Assert.That(me.Name, Is.EqualTo("Yeni Ad"));
            Assert.That(me.Avatar, Is.EqualTo(2));
        }
    }
}
