using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core;
using Game.Data;
using Game.Systems;
using Game.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// The free gem economy as one number. Every table that pays gems to a player who never spends
    /// real money is read from where the game reads it — code defaults, the wired config assets and the
    /// UI prefabs — and summed into a week. No single tuning file can see how much the others pay, so
    /// this is the one place that can.
    ///
    /// THE BUDGET (decided 2026-09-17): about 250 free gems a day, with 225-275 accepted, in two
    /// windows. STEADY STATE counts only what recurs. MONTH ONE adds the one-off progression rewards
    /// (chapters, the achievement tiers a first month reaches), which is why those are held small —
    /// the two windows have to fit in the same 50-gem band. Rewarded ads count: a free player earns
    /// them. The premium pass, paid packs and offers are a separate paid economy and are not here.
    ///
    /// Three inputs are behaviour, not tables, and are named below so a re-measure is one edit.
    /// When this fails, the message prints the whole breakdown: retune the source that grew, not the
    /// band.
    /// </summary>
    public sealed class RewardBudgetTests
    {
        private const double MinGemsPerDay = 225d;
        private const double MaxGemsPerDay = 275d;

        /// <summary>No single source may be more than this share of the steady-state week.</summary>
        private const double MaxSourceShare = 0.30d;

        /// <summary>What one festival-slot event may pay in total, free track only.</summary>
        private const long MaxGemsPerFestival = 280L;

        // ------------------------------------------------------------------ behaviour estimates
        /// <summary>Shop contracts finished a day by an engaged twice-a-day player.</summary>
        private const double ContractsPerDay = 6d;

        /// <summary>Duplicate-overflow gems from the card collection, a day. Roll-driven and small.</summary>
        private const double CollectionOverflowGemsPerDay = 5d;

        /// <summary>A typical finish is somewhere in 4th-20th: the budget counts the mean of those ranks.</summary>
        private const int TypicalRankFirst = 4, TypicalRankLast = 20;

        private const double LeagueSeasonDays = Leaderboards.ThreeDayCadenceSeconds / 86400d;
        private const double MonthDays = 30d;
        // The game opens one shop island (GameBootstrap.miningShopBusinessId) and cannot switch yet. Every island
        // added to month one adds its benches' stars again: raise this when island switching ships.
        private const int ShopIslandsInMonthOne = 1;

        /// <summary>Event rows that ended before this second predate the one-festival calendar rule.
        /// 2026-09-17 00:00 UTC: the September overlap of the Foundry and Harbor festivals is history.</summary>
        private const long CalendarRuleStartsUnix = 1789603200L;

        private const string ChapterConfigPath = "Assets/Data/ChapterConfig.asset";
        // The shop's contracts replaced the port ship's in the budget: the port only runs in ore mode.
        private const string ShopContractConfigPath = "Assets/Data/ShopContractConfig.asset";
        private const string ShopCoinConfigPath = "Assets/Data/ShopCoinConfig.asset";
        private const string FoundryConfigPath = "Assets/Data/FoundryFestivalConfig.asset";
        private const string PassConfigPath = "Assets/Data/SeasonalIndustryPassConfig.asset";
        private const string LiveEventConfigPath = "Assets/Data/LiveEventConfig.asset";
        private const string DailyRewardPrefab = "Assets/Prefabs/UI/UI_GunlukOdul.prefab";
        private const string AdRewardPrefab = "Assets/Prefabs/UI/UI_Reklam.prefab";
        private const string HudPrefab = "Assets/Prefabs/UI/UI_HUD.prefab";

        // ------------------------------------------------------------------------------ tests
        [Test]
        public void SteadyStateFreeGemsPerDayStayInsideTheBudget()
        {
            Budget b = Measure();
            double perDay = b.WeeklyTotal / 7d;
            Assert.That(perDay, Is.InRange(MinGemsPerDay, MaxGemsPerDay), b.Report());
        }

        [Test]
        public void MonthOneFreeGemsPerDayStayInsideTheBudget()
        {
            Budget b = Measure();
            double perDay = b.WeeklyTotal / 7d + b.MonthOneOneOffs / MonthDays;
            Assert.That(perDay, Is.InRange(MinGemsPerDay, MaxGemsPerDay), b.Report());
        }

        [Test]
        public void NoSingleSourceDominatesTheWeek()
        {
            Budget b = Measure();
            foreach (KeyValuePair<string, double> source in b.Weekly)
                Assert.That(source.Value / b.WeeklyTotal, Is.LessThanOrEqualTo(MaxSourceShare),
                            source.Key + " is too large a share.\n" + b.Report());
        }

        [Test]
        public void EveryFestivalSlotEventPaysAtMostTheSlot()
        {
            Assert.That(FoundryGems(), Is.LessThanOrEqualTo(MaxGemsPerFestival), "Foundry Festival");
            Assert.That(HarborMaxGems(HarborFestival.Tuning.Default), Is.LessThanOrEqualTo(MaxGemsPerFestival),
                        "Harbor Festival (free track, best catalogue use plus expiry)");
            Assert.That(SprintGems(), Is.LessThanOrEqualTo(MaxGemsPerFestival), "Production Sprint");
        }

        /// <summary>
        /// The calendar half of the stacking rule: at most one festival-slot event (Foundry, Harbor or
        /// Sprint) at a time, beside the Seasonal Pass. Every one of them scores the same count
        /// metrics, so two at once pay one action twice — and the budget above assumes they never do.
        /// </summary>
        [Test]
        public void FestivalSlotEventsNeverOverlap()
        {
            var config = AssetDatabase.LoadAssetAtPath<LiveEventConfig>(LiveEventConfigPath);
            Assert.That(config, Is.Not.Null, LiveEventConfigPath);

            List<LiveEvents.Definition> slot = new List<LiveEvents.Definition>();
            foreach (LiveEvents.Definition d in config.Definitions())
            {
                bool festival = d.Kind == FoundryFestival.Kind || d.Kind == HarborFestival.Kind
                                || d.Kind == ProductionSprint.Kind;
                if (festival && d.EndUnix > CalendarRuleStartsUnix) slot.Add(d);
            }

            for (int i = 0; i < slot.Count; i++)
                for (int j = i + 1; j < slot.Count; j++)
                {
                    bool overlap = slot[i].StartUnix < slot[j].EndUnix && slot[j].StartUnix < slot[i].EndUnix;
                    Assert.That(overlap, Is.False, slot[i].Id + " overlaps " + slot[j].Id);
                }
        }

        // --------------------------------------------------------------------------- measure
        private sealed class Budget
        {
            public readonly List<KeyValuePair<string, double>> Weekly = new List<KeyValuePair<string, double>>();
            public readonly List<KeyValuePair<string, double>> OneOffs = new List<KeyValuePair<string, double>>();
            public double WeeklyTotal, MonthOneOneOffs;

            public void AddWeekly(string name, double gems)
            {
                Weekly.Add(new KeyValuePair<string, double>(name, gems));
                WeeklyTotal += gems;
            }

            public void AddOneOff(string name, double gems)
            {
                OneOffs.Add(new KeyValuePair<string, double>(name, gems));
                MonthOneOneOffs += gems;
            }

            public string Report()
            {
                var sb = new StringBuilder();
                sb.AppendLine("Free gems per week (steady state):");
                foreach (KeyValuePair<string, double> s in Weekly)
                    sb.AppendLine("  " + s.Key + ": " + s.Value.ToString("F0", CultureInfo.InvariantCulture) + "  (" +
                                  (s.Value / WeeklyTotal).ToString("P0", CultureInfo.InvariantCulture) + ")");
                sb.AppendLine("  total " + WeeklyTotal.ToString("F0", CultureInfo.InvariantCulture) + " = " +
                              (WeeklyTotal / 7d).ToString("F1", CultureInfo.InvariantCulture) + "/day");
                sb.AppendLine("One-off rewards reached in month one:");
                foreach (KeyValuePair<string, double> s in OneOffs)
                    sb.AppendLine("  " + s.Key + ": " + s.Value.ToString("F0", CultureInfo.InvariantCulture));
                sb.AppendLine("  total " + MonthOneOneOffs.ToString("F0", CultureInfo.InvariantCulture) + " -> month one " +
                              (WeeklyTotal / 7d + MonthOneOneOffs / MonthDays).ToString("F1", CultureInfo.InvariantCulture) + "/day");
                return sb.ToString();
            }
        }

        /// <summary>What one shop contract pays in gems on average, over the size weights.</summary>
        private static double ShopContractGemsEach(in ShopContract.Tuning t)
        {
            double weight = 0d, gems = 0d;
            for (int i = 0; i < t.Sizes.Length; i++)
            {
                weight += t.Sizes[i].Weight;
                gems += t.Sizes[i].Weight * t.Sizes[i].Gems;
            }
            return gems / weight;
        }

        private static Budget Measure()
        {
            var b = new Budget();

            // Daily goals: the three slots draw evenly from the pool.
            double poolGems = 0d;
            foreach (Goals.Task t in Goals.DailyPool) poolGems += t.Gems;
            b.AddWeekly("Daily goals", poolGems / Goals.DailyPool.Length * Goals.DailySlots * 7d);

            double weekly = 0d;
            foreach (Goals.WeeklyMilestone m in Goals.WeeklyMilestones) weekly += m.Gems;
            b.AddWeekly("Weekly track", weekly);

            b.AddWeekly("Daily login", LoginLadderGems());

            var contracts = AssetDatabase.LoadAssetAtPath<ShopContractConfig>(ShopContractConfigPath);
            Assert.That(contracts, Is.Not.Null, ShopContractConfigPath);
            b.AddWeekly("Contracts", ShopContractGemsEach(contracts.ToTuning()) * ContractsPerDay * 7d);

            // Shop coins: an engaged player taps the whole cap every 48-hour cycle.
            var coins = AssetDatabase.LoadAssetAtPath<ShopCoinConfig>(ShopCoinConfigPath);
            Assert.That(coins, Is.Not.Null, ShopCoinConfigPath);
            ShopCoins.Tuning coinTuning = coins.ToTuning();
            b.AddWeekly("Shop coins", ShopCoins.ExpectedGems(coinTuning) * coinTuning.CoinsPerCycle
                                      * 7d * ShopCoins.DaySeconds / ShopCoins.CycleSeconds);

            b.AddWeekly("League", TypicalLeagueGems() * 7d / LeagueSeasonDays);
            b.AddWeekly("Ad gem slot", AdGemSlotGemsPerDay() * 7d);
            b.AddWeekly("Balloon", BalloonGemsPerDay() * 7d);
            b.AddWeekly("Collection overflow", CollectionOverflowGemsPerDay * 7d);

            SeasonalIndustryPass.Tuning pass = PassTuning();
            double passFree = 0d;
            foreach (SeasonalIndustryPass.Tier t in pass.Tiers) passFree += t.Free.Gems;
            b.AddWeekly("Seasonal Pass (free)", passFree / PassSeasonDays() * 7d);

            // One festival-slot event a week; the budget carries the richest of them.
            double festival = Math.Max(FoundryGems(), Math.Max(HarborMaxGems(HarborFestival.Tuning.Default), SprintGems()));
            b.AddWeekly("Festival slot", festival);

            // ------------------------------------------------------- one-offs in month one
            var chapterConfig = AssetDatabase.LoadAssetAtPath<ChapterConfig>(ChapterConfigPath);
            Assert.That(chapterConfig, Is.Not.Null, ChapterConfigPath);
            Chapters.Tuning chapters = chapterConfig.ToTuning();
            double chapterGems = 0d;
            for (int c = 0; c < Chapters.Count; c++)
                for (int beat = 0; beat < Chapters.BeatCount; beat++)
                    chapterGems += Chapters.BeatGems(c, beat, chapters);
            b.AddOneOff("Chapters (all eight: the archipelago is the first month)", chapterGems);

            // The islands ladder finishes with the chapters; every other ladder reaches its third tier.
            double islands = 0d, early = 0d;
            foreach (Goals.Achievement a in Goals.Ladder)
            {
                int tiers = a.Metric == Goals.Islands ? a.Tiers.Length : Math.Min(3, a.Tiers.Length);
                double gems = 0d;
                for (int tier = 1; tier <= tiers; tier++) gems += Goals.TierGems(a, tier);
                if (a.Metric == Goals.Islands) islands += gems; else early += gems;
            }
            b.AddOneOff("Islands achievement (all tiers)", islands);
            b.AddOneOff("Other achievements (tiers 1-3)", early);

            // Every bench's five stars. Bootstrap wires no MiningShopConfig, so the game runs its Inspector defaults;
            // read the wired asset here instead if one is ever assigned.
            var shop = ScriptableObject.CreateInstance<MiningShopConfig>();
            try
            {
                BenchMastery.Tuning mastery = shop.ToMasteryTuning();
                double stars = 0d;
                for (int star = 0; star < BenchMastery.StarCount; star++) stars += BenchMastery.StarGems(star, mastery);
                b.AddOneOff("Bench stars (" + MiningShopCampaign.ProductCount + " benches x " + ShopIslandsInMonthOne +
                            " island)", stars * MiningShopCampaign.ProductCount * ShopIslandsInMonthOne);
            }
            finally { UnityEngine.Object.DestroyImmediate(shop); }

            return b;
        }

        // ---------------------------------------------------------------------------- sources
        private static double LoginLadderGems()
        {
            SerializedProperty ladder = PrefabProperty<DailyRewardUI>(DailyRewardPrefab, "ladder");
            double gems = 0d;
            for (int i = 0; i < ladder.arraySize; i++)
                gems += ladder.GetArrayElementAtIndex(i).FindPropertyRelative("gems").longValue;
            return gems * 7d / DailyRewardService.CycleDays;
        }

        private static double AdGemSlotGemsPerDay()
        {
            SerializedProperty slots = PrefabProperty<AdRewardUI>(AdRewardPrefab, "slots");
            double gems = 0d;
            for (int i = 0; i < slots.arraySize; i++)
            {
                SerializedProperty slot = slots.GetArrayElementAtIndex(i);
                if (slot.FindPropertyRelative("kind").enumValueIndex != (int)AdRewardUI.RewardKind.Gems) continue;
                gems += slot.FindPropertyRelative("gems").longValue
                        * slot.FindPropertyRelative("chargesPerDay").intValue;
            }
            return gems;
        }

        private static double BalloonGemsPerDay()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefab);
            Assert.That(hud, Is.Not.Null, HudPrefab);
            var so = new SerializedObject(hud.GetComponentInChildren<HudUI>(true));
            double chance = so.FindProperty("balloonDiamondChance").floatValue;
            double amount = so.FindProperty("balloonDiamondAmount").longValue;
            return chance * amount * BalloonRewardService.ChargesPerDay;
        }

        private static double TypicalLeagueGems()
        {
            Ladder.Tuning tuning = Ladder.Tuning.Default;
            double gems = 0d;
            for (int rank = TypicalRankFirst; rank <= TypicalRankLast; rank++)
                gems += Ladder.RewardFor(Leaderboards.RewardTier(rank, Leaderboards.DefaultBracketEnds), tuning).Gems;
            return gems / (TypicalRankLast - TypicalRankFirst + 1);
        }

        private static SeasonalIndustryPass.Tuning PassTuning()
        {
            var config = AssetDatabase.LoadAssetAtPath<SeasonalIndustryPassConfig>(PassConfigPath);
            Assert.That(config, Is.Not.Null, PassConfigPath);
            return config.ToTuning();
        }

        /// <summary>The pass's season length, from the newest pass row on the calendar.</summary>
        private static double PassSeasonDays()
        {
            var config = AssetDatabase.LoadAssetAtPath<LiveEventConfig>(LiveEventConfigPath);
            Assert.That(config, Is.Not.Null, LiveEventConfigPath);
            long start = long.MinValue, seconds = 0L;
            foreach (LiveEvents.Definition d in config.Definitions())
                if (d.Kind == SeasonalIndustryPass.Kind && d.StartUnix > start)
                {
                    start = d.StartUnix;
                    seconds = d.EndUnix - d.StartUnix;
                }
            Assert.That(seconds, Is.GreaterThan(0L), "no Seasonal Pass row on the calendar");
            return seconds / 86400d;
        }

        private static long FoundryGems()
        {
            var config = AssetDatabase.LoadAssetAtPath<FoundryFestivalConfig>(FoundryConfigPath);
            Assert.That(config, Is.Not.Null, FoundryConfigPath);
            FoundryFestival.Tuning t = config.ToTuning();
            long gems = 0L;
            foreach (FoundryFestival.Task task in t.Tasks) gems += task.Gems;
            foreach (FoundryFestival.Milestone m in t.Milestones) gems += m.Gems;
            return gems;
        }

        /// <summary>
        /// The most gems a free Harbor player can take: every task and free tier, plus the best mix of
        /// catalogue buys and the gems unspent tokens convert to when the festival closes.
        /// </summary>
        private static long HarborMaxGems(in HarborFestival.Tuning t)
        {
            long gems = 0L;
            int tokens = 0;
            foreach (HarborFestival.Task task in t.Tasks) { gems += task.Reward.Gems; tokens += task.Tokens; }
            foreach (HarborFestival.Tier tier in t.Tiers) gems += tier.Free.Gems;

            long best = 0L;
            int items = t.Catalogue.Length;
            for (int mask = 0; mask < 1 << items; mask++)
            {
                int spent = 0;
                long bought = 0L;
                for (int i = 0; i < items; i++)
                    if ((mask & (1 << i)) != 0) { spent += t.Catalogue[i].Cost; bought += t.Catalogue[i].Reward.Gems; }
                if (spent > tokens) continue;
                long total = bought + (tokens - spent) / t.TokensPerExpiryGem;
                if (total > best) best = total;
            }
            return gems + best;
        }

        private static long SprintGems()
        {
            long gems = 0L;
            foreach (ProductionSprint.Milestone m in ProductionSprint.Tuning.Default.Milestones) gems += m.Reward.Gems;
            return gems;
        }

        private static SerializedProperty PrefabProperty<T>(string prefabPath, string field) where T : Component
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);
            T component = prefab.GetComponentInChildren<T>(true);
            Assert.That(component, Is.Not.Null, typeof(T).Name + " in " + prefabPath);
            SerializedProperty property = new SerializedObject(component).FindProperty(field);
            Assert.That(property, Is.Not.Null, typeof(T).Name + "." + field);
            return property;
        }
    }
}
