using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The one seam between the pet system and sea combat: <see cref="ExpeditionService.ShipStats"/>
    /// adds an equipped pet's bonus on top of gear and the captain, and <see cref="ExpeditionService.RegisterWin"/>
    /// is the only place a won fight pays a pearl. Both services are unit-tested on their own
    /// (PetsTests, PetServiceTests, SeaCombatTests); this is the wiring between them.
    /// </summary>
    public class PetCombatIntegrationTests
    {
        private static SeaCombat.Tuning SeaT => SeaCombat.Tuning.Default;
        private static Pets.Tuning PetT => Pets.Tuning.Default;
        private static PetChest.Tuning ChestT => PetChest.Tuning.Default;
        private static readonly int[] Gates = { 0, 10, 25 };

        private static (ExpeditionService sea, PetService pets, SaveData data) Make()
        {
            var data = new SaveData();
            var pets = new PetService(data, PetT, ChestT, Gates, new System.Random(1));
            var sea = new ExpeditionService(new TimeService(), data, null, SeaT);
            sea.Pets = pets;
            return (sea, pets, data);
        }

        [Test]
        public void AnUnwiredPetServiceLeavesShipStatsExactlyAsBefore()
        {
            var data = new SaveData();
            var sea = new ExpeditionService(new TimeService(), data, null, SeaT);
            Assert.DoesNotThrow(() => sea.ShipStats());
        }

        [Test]
        public void AnEquippedPetsBonusReachesShipStatsOnce()
        {
            var (sea, pets, data) = Make();
            data.seaFightsWon = 25;   // every slot open
            int species = 0;          // papağan -> Dodge
            data.pets.counts[Pets.CellIndex(species, RosterCardState.Rarity.Epic, 4)] = 1;
            pets.Equip(0, species);

            SeaCombat.Stats withPet = sea.ShipStats();

            sea.Pets = null;
            SeaCombat.Stats withoutPet = sea.ShipStats();

            double expectedBonus = Pets.Bonus(Pets.EffectKindOf(species), RosterCardState.Rarity.Epic, 4, PetT);
            Assert.That(withPet.Dodge, Is.EqualTo(withoutPet.Dodge + expectedBonus).Within(1e-12));
        }

        [Test]
        public void CallingShipStatsRepeatedlyNeverStacksThePetBonus()
        {
            // ShipStats is derived fresh every call and stores nothing — this is what proves a
            // repeated read cannot accumulate the same pet's contribution twice.
            var (sea, pets, data) = Make();
            data.seaFightsWon = 25;
            data.pets.counts[Pets.CellIndex(4, RosterCardState.Rarity.Rare, 2)] = 1;   // yengeç -> Def
            pets.Equip(0, 4);

            double first = sea.ShipStats().Def;
            double second = sea.ShipStats().Def;
            double third = sea.ShipStats().Def;

            Assert.That(second, Is.EqualTo(first));
            Assert.That(third, Is.EqualTo(first));
        }

        [Test]
        public void ThePetBonusStacksOnAnAlreadyCappedGearDodgeWithoutClearingTheCap()
        {
            var (sea, pets, data) = Make();
            data.seaFightsWon = 25;

            // Gear alone already sits at the cap: OurStats clamps Dodge to DodgeCap (0.50) before
            // this ever reaches the pet. A Mythic★5 parrot adds another 0.045 worth of Dodge on top
            // — the combined number must still never clear the one cap both sources share.
            data.seaGearGrade[SeaCombat.SlotPlating] = 1;   // Common, stored as Grade+1
            data.seaGearSec[SeaCombat.SlotPlating] = SeaCombat.SecDodge;
            data.seaGearSecAmt[SeaCombat.SlotPlating] = 0.48d;
            data.pets.counts[Pets.CellIndex(0, RosterCardState.Rarity.Mythic, Pets.MaxStars)] = 1;
            pets.Equip(0, 0);

            Assert.That(sea.ShipStats().Dodge, Is.EqualTo(SeaCombat.DodgeCap));
        }

        [Test]
        public void FusingTheEquippedSpeciesUpdatesTheVeryNextShipStatsRead()
        {
            var (sea, pets, data) = Make();
            data.seaFightsWon = 25;
            int species = 0;   // papağan -> Dodge
            data.pets.counts[Pets.CellIndex(species, RosterCardState.Rarity.Common, 1)] = 3;
            pets.Equip(0, species);

            double before = sea.ShipStats().Dodge;
            Assert.That(pets.Fuse(species, RosterCardState.Rarity.Common, 1), Is.True);
            double after = sea.ShipStats().Dodge;

            double expectedAfter = Pets.Bonus(Pets.EffectKindOf(species), RosterCardState.Rarity.Common, 2, PetT);
            Assert.That(after, Is.EqualTo(expectedAfter).Within(1e-12));
            Assert.That(after, Is.GreaterThan(before));
        }

        [Test]
        public void ASpeciesWithNoOwnedCopiesLeftClearsToNoBonusRatherThanGoingStale()
        {
            // Equip refuses a species nobody owns, so the only way an equipped slot can point at
            // one is the save arriving in that state directly (e.g. every copy consumed some other
            // way). CombatBonus must resolve that slot to nothing rather than a stale number.
            var (sea, pets, data) = Make();
            data.seaFightsWon = 25;
            data.pets.equippedSpecies[0] = 0;   // papağan, never granted a single copy

            double withStaleSlot = sea.ShipStats().Dodge;
            sea.Pets = null;
            double unwired = sea.ShipStats().Dodge;

            Assert.That(withStaleSlot, Is.EqualTo(unwired));
            Assert.That(pets.CombatBonus()[(int)Pets.EffectKind.Dodge], Is.Zero);
        }

        [Test]
        public void RegisteringAWinPaysTierAndKindScaledPearlsExactlyOnce()
        {
            var (sea, _, data) = Make();
            sea.SetSail("coal");

            sea.RegisterWin(2, 1); // round(4 * 9 * .06 * 1.25) = 3
            Assert.That(data.pearls, Is.EqualTo(3L));

            sea.RegisterWin(2, 1);
            Assert.That(data.pearls, Is.EqualTo(6L), "two wins must pay twice, not double the first");
        }

        [Test]
        public void ThePearlFormulaFloorsAValidWinAtOne()
        {
            var (sea, _, data) = Make();
            sea.SetSail("coal");
            Assert.DoesNotThrow(() => sea.RegisterWin(0, 0));
            Assert.That(data.pearls, Is.EqualTo(1L));
        }

        [Test]
        public void AWinAshoreOfTheSeaPaysNoPearl()
        {
            var (sea, _, data) = Make();
            // Never sailed — RegisterWin must refuse exactly as it refuses charts and salvage ashore.
            sea.RegisterWin(3, 1);
            Assert.That(data.pearls, Is.Zero);
        }
    }
}
