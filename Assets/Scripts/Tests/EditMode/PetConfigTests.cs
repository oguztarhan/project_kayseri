using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Game.Core;
using Game.Data;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>The PetConfig asset is the designer-facing copy of the pet balance contract. These
    /// tests keep a fresh Inspector-created asset aligned with runtime defaults and ensure invalid
    /// edits are reported instead of silently being replaced by fallback values.</summary>
    public class PetConfigTests
    {
        private PetConfig _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<PetConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        [Test]
        public void ANewAssetCarriesTheApprovedChestRewardAndSlotDefaults()
        {
            PetChest.Tuning chest = _config.ToChestTuning();
            PetChest.Tuning shippedChest = PetChest.Tuning.Default;
            Pets.RewardTuning rewards = _config.ToRewardTuning();
            Pets.RewardTuning shippedRewards = Pets.RewardTuning.Default;

            Assert.That(chest.CommonWeight, Is.EqualTo(shippedChest.CommonWeight).Within(1e-12));
            Assert.That(chest.RareWeight, Is.EqualTo(shippedChest.RareWeight).Within(1e-12));
            Assert.That(chest.EpicWeight, Is.EqualTo(shippedChest.EpicWeight).Within(1e-12));
            Assert.That(chest.LegendaryWeight, Is.EqualTo(shippedChest.LegendaryWeight).Within(1e-12));
            Assert.That(chest.MythicWeight, Is.EqualTo(shippedChest.MythicWeight).Within(1e-12));
            Assert.That(chest.EpicPity, Is.EqualTo(shippedChest.EpicPity));
            Assert.That(chest.LegendaryPity, Is.EqualTo(shippedChest.LegendaryPity));
            Assert.That(chest.SoftPityStart, Is.EqualTo(shippedChest.SoftPityStart));
            Assert.That(chest.SoftPityStep, Is.EqualTo(shippedChest.SoftPityStep).Within(1e-12));
            Assert.That(chest.PearlCost, Is.EqualTo(100L));
            Assert.That(chest.BulkCount, Is.EqualTo(10));
            Assert.That(chest.BulkPearlCost, Is.EqualTo(900L));
            Assert.That(rewards.BootstrapPearls, Is.EqualTo(shippedRewards.BootstrapPearls));
            Assert.That(rewards.DailyPearls, Is.EqualTo(shippedRewards.DailyPearls));
            Assert.That(rewards.WeeklyMilestonePearls, Is.EqualTo(shippedRewards.WeeklyMilestonePearls));
            Assert.That(rewards.SeaFightMilestonePearls, Is.EqualTo(shippedRewards.SeaFightMilestonePearls));
            Assert.That(rewards.AchievementPearls, Is.EqualTo(shippedRewards.AchievementPearls));
            Assert.That(_config.SlotUnlockFightsWon(0), Is.Zero);
            Assert.That(_config.SlotUnlockFightsWon(1), Is.EqualTo(10));
            Assert.That(_config.SlotUnlockFightsWon(2), Is.EqualTo(25));
        }

        [Test]
        public void ANewAssetHasEveryRequiredBalanceArrayAndPassesValidation()
        {
            Assert.That(_config.ToTuning().EffectPerRarity.Length,
                        Is.EqualTo(Pets.EffectKindCount * Pets.RarityCount));
            Assert.That(_config.RarityTint.Length, Is.EqualTo(Pets.RarityCount));
            Assert.That(_config.TryValidate(out string message), Is.True, message);
        }

        [Test]
        public void InvalidArrayShapesWeightsPityGatesAndPricesAreReported()
        {
            Set("effectPerRarity", new double[1]);
            Assert.That(_config.TryValidate(out string message), Is.False);
            StringAssert.Contains("effect table", message);

            Set("effectPerRarity", Pets.Tuning.Default.EffectPerRarity);
            Set("commonWeight", 0d);
            Assert.That(_config.TryValidate(out message), Is.False);
            StringAssert.Contains("weight", message);

            Set("commonWeight", PetChest.Tuning.Default.CommonWeight);
            Set("epicPity", 11);
            Assert.That(_config.TryValidate(out message), Is.False);
            StringAssert.Contains("bulk", message);

            Set("epicPity", PetChest.Tuning.Default.EpicPity);
            Set("slotUnlockFightsWon", new[] { 0, 25, 10 });
            Assert.That(_config.TryValidate(out message), Is.False);
            StringAssert.Contains("Slot gates", message);

            Set("slotUnlockFightsWon", new[] { 0, 10, 25 });
            Set("bulkPearlCost", 1000L);
            Assert.That(_config.TryValidate(out message), Is.False);
            StringAssert.Contains("Bulk", message);
        }

        [Test]
        public void AConfiguredAssetDrivesPetServiceInsteadOfRuntimeDefaults()
        {
            Set("pearlCost", 17L);
            Set("bulkCount", 4);
            Set("bulkPearlCost", 60L);
            Set("epicPity", 4);
            Set("slotUnlockFightsWon", new[] { 0, 2, 4 });
            Set("effectPerRarity", new double[Pets.EffectKindCount * Pets.RarityCount]);
            double[] effect = _config.ToTuning().EffectPerRarity;
            effect[(int)Pets.EffectKind.Dodge * Pets.RarityCount] = 0.75d;

            var data = new SaveData();
            var gates = new int[Pets.SlotCount];
            for (int i = 0; i < gates.Length; i++) gates[i] = _config.SlotUnlockFightsWon(i);
            var service = new PetService(data, _config.ToTuning(), _config.ToChestTuning(), gates);

            Assert.That(service.ChestCost(1), Is.EqualTo(17L));
            Assert.That(service.ChestCost(4), Is.EqualTo(60L));
            data.seaFightsWon = 2;
            Assert.That(service.SlotsUnlocked, Is.EqualTo(2));
            data.pets.counts[Pets.CellIndex(0, RosterCardState.Rarity.Common, 1)] = 1;
            Assert.That(service.Equip(0, 0), Is.True);
            Assert.That(service.CombatBonus()[(int)Pets.EffectKind.Dodge], Is.EqualTo(0.75d));
        }

        private void Set(string fieldName, object value)
        {
            FieldInfo field = typeof(PetConfig).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName + " must remain serialized for the Inspector");
            field.SetValue(_config, value);
        }
    }
}
