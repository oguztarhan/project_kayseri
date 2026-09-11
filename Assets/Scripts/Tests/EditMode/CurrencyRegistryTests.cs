using System.Collections.Generic;
using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>Phase 1 verification for the read-only currency definition and snapshot contract.</summary>
    public class CurrencyRegistryTests
    {
        [Test]
        public void DefinitionsHaveOneEntryForEveryCurrencyIdentifier()
        {
            var registry = new CurrencyRegistry(null, null, null, null, null, null, null);
            var seen = new HashSet<CurrencyId>();

            Assert.That(registry.Definitions.Count, Is.EqualTo(10));
            for (int i = 0; i < registry.Definitions.Count; i++)
            {
                CurrencyDefinition definition = registry.Definitions[i];
                Assert.That(seen.Add(definition.Id), Is.True, definition.Id + " was registered twice.");
                Assert.That(definition.LocalizedNameKey, Is.Not.Empty);
                Assert.That(definition.IconKey, Is.Not.Empty);
                Assert.That(registry.TryGetDefinition(definition.Id, out CurrencyDefinition found), Is.True);
                Assert.That(found, Is.SameAs(definition));
            }

            foreach (CurrencyId id in System.Enum.GetValues(typeof(CurrencyId)))
                Assert.That(seen.Contains(id), Is.True, id + " is missing from the registry.");
        }

        [Test]
        public void SnapshotsReadValuesFromTheirExistingServices()
        {
            var data = new SaveData
            {
                salvage = 11L,
                charts = 13L,
                craftPoints = 17L,
                miningPoints = 19L,
                miningScrap = 23L,
                pearls = 29L
            };
            data.wallet.cash = new BigDouble(12345d);
            data.wallet.gems = 7L;
            data.pets.petEssence = 31L;

            var time = new TimeService();
            var wallet = new WalletService(data.wallet);
            var captains = new CaptainService(data, Captains.Tuning.Default, CaptainCrate.Tuning.Default);
            var expeditions = new ExpeditionService(time, data, captains);
            var crafting = new CraftingService(data, null, time);
            var mining = new MiningGearService(data, null, time);
            var pets = new PetService(data, Pets.Tuning.Default, PetChest.Tuning.Default, null, null, null,
                                      null, Pets.RewardTuning.Default, time.NowUnix);
            var registry = new CurrencyRegistry(time, wallet, expeditions, crafting, mining, captains, pets);

            Assert.That(registry.GetSnapshot(CurrencyId.Cash).Current, Is.EqualTo(wallet.Cash));
            Assert.That(registry.GetSnapshot(CurrencyId.Gems).WholeAmount, Is.EqualTo(wallet.Gems));
            Assert.That(registry.GetSnapshot(CurrencyId.Salvage).WholeAmount, Is.EqualTo(expeditions.Salvage));
            Assert.That(registry.GetSnapshot(CurrencyId.Charts).WholeAmount, Is.EqualTo(captains.Charts));
            Assert.That(registry.GetSnapshot(CurrencyId.CraftPoints).WholeAmount, Is.EqualTo(crafting.Points));
            Assert.That(registry.GetSnapshot(CurrencyId.MiningPoints).WholeAmount, Is.EqualTo(mining.Points));
            Assert.That(registry.GetSnapshot(CurrencyId.MiningScrap).WholeAmount, Is.EqualTo(mining.Scrap));
            Assert.That(registry.GetSnapshot(CurrencyId.Pearls).WholeAmount, Is.EqualTo(pets.Pearls));
            Assert.That(registry.GetSnapshot(CurrencyId.PetEssence).WholeAmount, Is.EqualTo(pets.PetEssence));

            CurrencySnapshot energy = registry.GetSnapshot(CurrencyId.CombatEnergy);
            Assert.That(energy.WholeAmount, Is.EqualTo(expeditions.Energy));
            Assert.That(energy.Maximum, Is.EqualTo(expeditions.EnergyMax));
            Assert.That(energy.NextRegenerationUnix, Is.GreaterThanOrEqualTo(0L));
            registry.Dispose();
        }

        [Test]
        public void ExistingSourceEventsRefreshTheRegistryWithoutMovingMutationAuthority()
        {
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var registry = new CurrencyRegistry(null, wallet, null, null, null, null, null);
            int changed = 0;
            registry.Changed += () => changed++;

            wallet.AddCash(new BigDouble(1d));
            wallet.AddGems(1L);

            Assert.That(changed, Is.EqualTo(2));
            Assert.That(registry.GetSnapshot(CurrencyId.Cash).Current, Is.EqualTo(wallet.Cash));
            Assert.That(registry.GetSnapshot(CurrencyId.Gems).WholeAmount, Is.EqualTo(wallet.Gems));
            registry.Dispose();
        }
    }
}
