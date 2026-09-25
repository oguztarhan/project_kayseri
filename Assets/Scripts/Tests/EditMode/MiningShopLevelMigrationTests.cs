using System;
using System.Text.RegularExpressions;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class MiningShopLevelMigrationTests
    {
        private const string BusinessId = "mining-shop.island-01-01";

        /// <summary>An old business save: every bench built, levels on the two tracks, not yet migrated.</summary>
        private static MiningShopState OldSave(int speedLevel, int valueLevel)
        {
            var state = new MiningShopState { BusinessId = BusinessId, Business = new MiningShopBusinessState() };
            state.Business.AvailableProductCount = MiningShopCampaign.ProductCount;
            for (int i = 0; i < MiningShopCampaign.ProductCount; i++)
                state.Business.Lines.Add(new MiningShopProductLineState
                {
                    ProductId = MiningShopCampaign.ProductIdAt(i),
                    TableBuilt = true,
                    SpeedLevel = speedLevel,
                    ValueLevel = valueLevel
                });
            return state;
        }

        private static MiningShopBusinessSimulation Open(MiningShopState state) => new MiningShopBusinessSimulation(state,
            MiningShopCampaign.ProductCount, MiningShopBusinessSimulation.Tuning.Default, sale => { });

        [Test]
        public void EveryOldPairKeepsItsIncomeAndItsCash()
        {
            MiningShopBusinessSimulation.Tuning tuning = MiningShopBusinessSimulation.Tuning.Default;
            for (int p = 0; p < MiningShopCampaign.ProductCount; p++)
            {
                MiningShopBusinessSimulation.ProductTuning product = tuning.Products[p];
                for (int speed = 1; speed <= MiningShopLevelMigration.LegacyMaxLevel; speed++)
                for (int value = 1; value <= MiningShopLevelMigration.LegacyMaxLevel; value++)
                {
                    string at = p + " @ " + speed + "/" + value;
                    int level = MiningShopLevelMigration.Level(p, speed, value, product.FirstLevelCost, product.UnitPrice,
                        product.CraftSeconds, tuning.Mastery, out double refund);
                    double spent = MiningShopLevelMigration.LegacySpend(speed, value);
                    double levelsCost = BenchMastery.CostOfLevels(product.FirstLevelCost, 1, level - 1, tuning.Mastery);

                    Assert.That(level, Is.InRange(BenchMastery.MinLevel, BenchMastery.MaxLevel), at);
                    Assert.That(BenchMastery.IncomePerSecond(product.UnitPrice, product.CraftSeconds, level, tuning.Mastery),
                        Is.GreaterThanOrEqualTo(MiningShopLevelMigration.LegacyIncomePerSecond(p, speed, value)), "income " + at);
                    Assert.That(refund, Is.GreaterThanOrEqualTo(0d), "refund " + at);
                    Assert.That(levelsCost + refund, Is.GreaterThanOrEqualTo(spent), "value kept " + at);
                    Assert.That(refund, Is.LessThan(BenchMastery.LevelCost(product.FirstLevelCost, level, tuning.Mastery)),
                        "a refund that buys another level means a level was held back " + at);
                }
            }
        }

        [Test]
        public void TheOldMaximumPickaxeBecomesLevel29()
        {
            MiningShopBusinessSimulation.Tuning tuning = MiningShopBusinessSimulation.Tuning.Default;
            MiningShopBusinessSimulation.ProductTuning pickaxe = tuning.Products[0];
            int level = MiningShopLevelMigration.Level(0, 21, 21, pickaxe.FirstLevelCost, pickaxe.UnitPrice,
                pickaxe.CraftSeconds, tuning.Mastery, out double refund);
            Assert.That(level, Is.EqualTo(29));
            Assert.That(refund, Is.EqualTo(815d));
        }

        [Test]
        public void TheMigrationRunsOnceAndKeepsTheCraftShare()
        {
            MiningShopState state = OldSave(21, 21);
            MiningShopProductLineState line = state.Business.Lines[0];
            line.Crafting = true;
            line.CraftDuration = 4d;
            line.CraftRemaining = 1d;
            line.OutputStock = 0;

            MiningShopBusinessSimulation first = Open(state);
            Assert.That(state.Business.LevelSchema, Is.EqualTo(MiningShopLevelMigration.Schema));
            Assert.That(first.View.ProductAt(0).Level, Is.EqualTo(29));
            Assert.That(first.View.ProductAt(1).Level, Is.EqualTo(13));
            Assert.That(first.LevelMigrationRefund, Is.EqualTo(815d + 834d + 3186d + 11746d));
            Assert.That(line.CraftRemaining / line.CraftDuration, Is.EqualTo(0.25d).Within(1e-12));

            MiningShopBusinessSimulation second = Open(state);
            Assert.That(second.LevelMigrationRefund, Is.Zero, "a migrated save is never paid twice");
            Assert.That(second.View.ProductAt(0).Level, Is.EqualTo(29));
            Assert.That(line.CraftRemaining / line.CraftDuration, Is.EqualTo(0.25d).Within(1e-12));
        }

        [Test]
        public void ALevelAlreadySetIsNeverLowered()
        {
            MiningShopState state = OldSave(2, 1);
            state.Business.Lines[0].Level = 40;
            MiningShopBusinessSimulation sim = Open(state);
            Assert.That(sim.View.ProductAt(0).Level, Is.EqualTo(40));
            Assert.That(sim.View.ProductAt(1).Level, Is.EqualTo(1));
            Assert.That(sim.LevelMigrationRefund, Is.EqualTo(3d * 40d), "the pickaxe paid nothing back; the other three did");
        }

        [Test]
        public void OldLevelsOutsideTheOldRangeAreRefusedUntouched()
        {
            MiningShopState state = OldSave(22, 1);
            Assert.Throws<ArgumentException>(() => Open(state));
            Assert.That(state.Business.LevelSchema, Is.Zero);
            Assert.That(state.Business.Lines[0].SpeedLevel, Is.EqualTo(22));

            MiningShopState future = OldSave(1, 1);
            future.Business.LevelSchema = MiningShopLevelMigration.Schema + 1;
            Assert.Throws<ArgumentException>(() => Open(future));
        }

        [Test]
        public void AJsonSaveWrittenBeforeTheLevelFieldsMigrates()
        {
            string json = JsonUtility.ToJson(OldSave(10, 10));
            // What the previous build wrote: no Level on the lines, no LevelSchema on the business.
            json = Regex.Replace(json, "\"Level\":\\d+,", "");
            json = Regex.Replace(json, ",\"LevelSchema\":\\d+", "");
            Assert.That(json, Does.Not.Contain("\"Level\"").And.Not.Contain("LevelSchema"));

            MiningShopState loaded = JsonUtility.FromJson<MiningShopState>(json);
            MiningShopBusinessSimulation sim = Open(loaded);
            Assert.That(sim.View.ProductAt(0).Level, Is.EqualTo(15));
            Assert.That(sim.View.ProductAt(3).Level, Is.EqualTo(1));
            Assert.That(sim.LevelMigrationRefund, Is.EqualTo(23d + 158d + 1534d + 1534d));

            MiningShopState reloaded = JsonUtility.FromJson<MiningShopState>(JsonUtility.ToJson(loaded));
            MiningShopBusinessSimulation again = Open(reloaded);
            Assert.That(again.View.ProductAt(0).Level, Is.EqualTo(15));
            Assert.That(again.LevelMigrationRefund, Is.Zero);
        }
    }
}
