using NUnit.Framework;
using Game.Core;
using Game.Systems;

namespace Game.Tests
{
    /// <summary>
    /// The half of the pet system that touches the save: padding, pearls, chests, fusion, equip and
    /// the slot gate. The odds are covered in PetChestTests and the table in PetsTests; what is
    /// tested here is the spending, the grid mutation, and that a save from before pets existed
    /// opens exactly as a fresh one does.
    /// </summary>
    public class PetServiceTests
    {
        private static Pets.Tuning T => Pets.Tuning.Default;
        private static PetChest.Tuning C => PetChest.Tuning.Default;
        private static readonly int[] Gates = { 0, 10, 25 };

        /// <summary>A seeded generator, so a chest test asserts a fact rather than a coin flip.</summary>
        private static PetService Make(SaveData data, int seed = 12345)
            => new PetService(data, T, C, Gates, new System.Random(seed));

        // ---- the save contract -------------------------------------------------------------------

        [Test]
        public void ASaveFromBeforePetsExistedWorks()
        {
            var data = new SaveData();
            data.pets = null;
            data.pearls = -5L;   // a corrupt negative, the kind a hand-edited save could carry

            PetService s = Make(data);
            Assert.That(data.pets, Is.Not.Null);
            Assert.That(data.pets.counts.Length, Is.EqualTo(Pets.CountsLength));
            Assert.That(data.pets.equippedSpecies.Length, Is.EqualTo(Pets.SlotCount));
            Assert.That(s.Pearls, Is.Zero);
            Assert.That(s.ChestsOpened, Is.Zero);
            Assert.That(s.Owned(0), Is.False);
        }

        [Test]
        public void AShortCountsArrayIsPaddedAndKeepsWhatItHad()
        {
            var data = new SaveData();
            data.pets.counts = new[] { 7, 2 };

            PetService s = Make(data);
            Assert.That(data.pets.counts.Length, Is.EqualTo(Pets.CountsLength));
            Assert.That(data.pets.counts[0], Is.EqualTo(7));
            Assert.That(data.pets.counts[1], Is.EqualTo(2));
        }

        [Test]
        public void AnEquippedSpeciesOutsideTheRosterIsDroppedOnLoad()
        {
            // A species cut from the roster between builds must not keep a dead slot pinned.
            var data = new SaveData();
            data.pets.equippedSpecies = new[] { 99, -1, -1 };

            Make(data);
            Assert.That(data.pets.equippedSpecies[0], Is.EqualTo(-1));
        }

        [Test]
        public void ANullSaveIsSurvivable()
        {
            var s = new PetService(null, T, C, Gates);
            Assert.That(s.Pearls, Is.Zero);
            Assert.That(s.Owned(0), Is.False);
            Assert.That(s.TryOpenChests(1), Is.Null);
            Assert.That(s.Fuse(0, RosterCardState.Rarity.Common, 1), Is.False);
            Assert.That(s.Equip(0, 0), Is.False);
            Assert.That(s.CombatBonus().Length, Is.EqualTo(Pets.EffectKindCount));
            Assert.DoesNotThrow(() => s.CombatBonus());
        }

        // ---- chests ------------------------------------------------------------------------------

        [Test]
        public void OpeningWithoutEnoughPearlsChangesNothing()
        {
            var data = new SaveData();
            PetService s = Make(data);
            data.pearls = C.PearlCost - 1;

            Assert.That(s.CanOpenChest(1), Is.False);
            Assert.That(s.TryOpenChests(1), Is.Null);
            Assert.That(data.pearls, Is.EqualTo(C.PearlCost - 1));
            Assert.That(s.ChestsOpened, Is.Zero);
        }

        [Test]
        public void OpeningSpendsPearlsGrantsAStarOneCopyAndAdvancesPity()
        {
            var data = new SaveData();
            PetService s = Make(data);
            data.pearls = C.PearlCost * 5;

            var pulled = s.TryOpenChests(1);
            Assert.That(pulled, Is.Not.Null);
            Assert.That(pulled.Length, Is.EqualTo(1));
            Assert.That(data.pearls, Is.EqualTo(C.PearlCost * 4));
            Assert.That(s.ChestsOpened, Is.EqualTo(1));

            (int species, RosterCardState.Rarity rarity) = pulled[0];
            Assert.That(s.CountAt(species, rarity, 1), Is.EqualTo(1));
            Assert.That(s.Owned(species), Is.True);
        }

        [Test]
        public void ABulkOpenIsOneAllOrNothingSpend()
        {
            var data = new SaveData();
            PetService s = Make(data);
            data.pearls = C.BulkPearlCost - 1;

            Assert.That(s.TryOpenChests(C.BulkCount), Is.Null);
            Assert.That(data.pearls, Is.EqualTo(C.BulkPearlCost - 1), "a failed open must refund nothing because it spent nothing");

            data.pearls = C.BulkPearlCost;
            var pulled = s.TryOpenChests(C.BulkCount);
            Assert.That(pulled.Length, Is.EqualTo(C.BulkCount));
            Assert.That(data.pearls, Is.Zero);
        }

        // ---- fusion --------------------------------------------------------------------------------

        [Test]
        public void FusingWithFewerThanThreeCopiesFails()
        {
            var data = new SaveData();
            PetService s = Make(data);
            int idx = Pets.CellIndex(0, RosterCardState.Rarity.Common, 1);
            data.pets.counts[idx] = 2;

            Assert.That(s.Fuse(0, RosterCardState.Rarity.Common, 1), Is.False);
            Assert.That(data.pets.counts[idx], Is.EqualTo(2));
        }

        [Test]
        public void FusingThreeConsumesThemAndGrantsOneAtTheNextRung()
        {
            var data = new SaveData();
            PetService s = Make(data);
            int fromIdx = Pets.CellIndex(1, RosterCardState.Rarity.Common, 1);
            data.pets.counts[fromIdx] = 3;

            Assert.That(s.Fuse(1, RosterCardState.Rarity.Common, 1), Is.True);
            Assert.That(data.pets.counts[fromIdx], Is.Zero);
            int toIdx = Pets.CellIndex(1, RosterCardState.Rarity.Common, 2);
            Assert.That(data.pets.counts[toIdx], Is.EqualTo(1));
        }

        [Test]
        public void FusingLeavesAnyOverflowCopiesUntouched()
        {
            var data = new SaveData();
            PetService s = Make(data);
            int fromIdx = Pets.CellIndex(1, RosterCardState.Rarity.Common, 1);
            data.pets.counts[fromIdx] = 5;

            Assert.That(s.Fuse(1, RosterCardState.Rarity.Common, 1), Is.True);
            Assert.That(data.pets.counts[fromIdx], Is.EqualTo(2));
        }

        [Test]
        public void AMythicFiveStarCannotBeFusedEvenWithMaterials()
        {
            var data = new SaveData();
            PetService s = Make(data);
            int idx = Pets.CellIndex(0, RosterCardState.Rarity.Mythic, Pets.MaxStars);
            data.pets.counts[idx] = 9;   // three times over, just to be sure

            Assert.That(s.Fuse(0, RosterCardState.Rarity.Mythic, Pets.MaxStars), Is.False);
            Assert.That(data.pets.counts[idx], Is.EqualTo(9));
        }

        // ---- equip and the slot gate ----------------------------------------------------------------

        [Test]
        public void SlotsUnlockOnlyAsWinsAccumulate()
        {
            var data = new SaveData();
            PetService s = Make(data);

            data.seaFightsWon = 0;
            Assert.That(s.SlotsUnlocked, Is.EqualTo(1));
            Assert.That(s.FightsUntilSlot(1), Is.EqualTo(10));

            data.seaFightsWon = 10;
            Assert.That(s.SlotsUnlocked, Is.EqualTo(2));

            data.seaFightsWon = 25;
            Assert.That(s.SlotsUnlocked, Is.EqualTo(3));
        }

        [Test]
        public void EquippingALockedSlotFails()
        {
            var data = new SaveData();
            PetService s = Make(data);
            data.seaFightsWon = 0;
            int idx = Pets.CellIndex(0, RosterCardState.Rarity.Common, 1);
            data.pets.counts[idx] = 1;

            Assert.That(s.Equip(1, 0), Is.False);
            Assert.That(s.EquippedAt(1), Is.EqualTo(-1));
        }

        [Test]
        public void EquippingASpeciesNobodyOwnsFails()
        {
            var data = new SaveData();
            PetService s = Make(data);
            Assert.That(s.Equip(0, 0), Is.False);
        }

        [Test]
        public void TheSameSpeciesCannotRideTwoSlotsAtOnce()
        {
            var data = new SaveData();
            PetService s = Make(data);
            data.seaFightsWon = 25;
            data.pets.counts[Pets.CellIndex(0, RosterCardState.Rarity.Common, 1)] = 1;

            Assert.That(s.Equip(0, 0), Is.True);
            Assert.That(s.Equip(1, 0), Is.False);
            Assert.That(s.EquippedAt(1), Is.EqualTo(-1));
        }

        [Test]
        public void UnequipAlwaysClearsAnUnlockedSlot()
        {
            var data = new SaveData();
            PetService s = Make(data);
            data.seaFightsWon = 25;
            data.pets.counts[Pets.CellIndex(0, RosterCardState.Rarity.Common, 1)] = 1;
            s.Equip(0, 0);

            Assert.That(s.Unequip(0), Is.True);
            Assert.That(s.EquippedAt(0), Is.EqualTo(-1));
        }

        // ---- persistence ---------------------------------------------------------------------------
        // Not "a save happened" — the stronger thing: what the disk holds straight after the call is
        // the whole mutation. A file with the pearls spent and no pet, or the pet and the pearls
        // still there, is exactly the force-close refund or re-roll these rule out.

        private static PetService MakeSaving(SaveData data, SaveService save)
            => new PetService(data, T, C, Gates, new System.Random(12345), null, save);

        [Test]
        public void AChestOpenReachesTheDiskBeforeItIsAcknowledged()
        {
            var save = new SaveService("pets-test.dat");
            var data = new SaveData();
            PetService s = MakeSaving(data, save);
            data.pearls = C.PearlCost * 3;

            var pulled = s.TryOpenChests(1);
            Assert.That(pulled, Is.Not.Null);
            Assert.That(save.TryLoad(out SaveData onDisk), Is.True);
            Assert.That(onDisk.pearls, Is.EqualTo(C.PearlCost * 2), "the pearls are spent on disk");
            Assert.That(onDisk.pets.chestsOpened, Is.EqualTo(1));
            Assert.That(onDisk.pets.counts[Pets.CellIndex(pulled[0].Species, pulled[0].Rarity, 1)],
                        Is.EqualTo(1), "and the pull is in the same write");
            Assert.That(onDisk.pets.chestSinceEpic, Is.EqualTo(data.pets.chestSinceEpic));
            Assert.That(onDisk.pets.chestSinceLegendary, Is.EqualTo(data.pets.chestSinceLegendary));
        }

        [Test]
        public void ABulkOpenReachesTheDiskWhole()
        {
            var save = new SaveService("pets-test.dat");
            var data = new SaveData();
            PetService s = MakeSaving(data, save);
            data.pearls = C.BulkPearlCost;

            var pulled = s.TryOpenChests(C.BulkCount);
            Assert.That(pulled.Length, Is.EqualTo(C.BulkCount));
            Assert.That(save.TryLoad(out SaveData onDisk), Is.True);
            Assert.That(onDisk.pearls, Is.Zero);
            Assert.That(onDisk.pets.chestsOpened, Is.EqualTo(C.BulkCount));
            int copies = 0;
            foreach (int c in onDisk.pets.counts) copies += c;
            Assert.That(copies, Is.EqualTo(C.BulkCount), "every pet of the batch is on disk");
        }

        [Test]
        public void AFusionReachesTheDiskBeforeItIsAcknowledged()
        {
            var save = new SaveService("pets-test.dat");
            var data = new SaveData();
            PetService s = MakeSaving(data, save);
            int fromIdx = Pets.CellIndex(1, RosterCardState.Rarity.Common, 1);
            int toIdx = Pets.CellIndex(1, RosterCardState.Rarity.Common, 2);
            data.pets.counts[fromIdx] = 3;

            Assert.That(s.Fuse(1, RosterCardState.Rarity.Common, 1), Is.True);
            Assert.That(save.TryLoad(out SaveData onDisk), Is.True);
            Assert.That(onDisk.pets.counts[fromIdx], Is.Zero, "the three are gone from the file");
            Assert.That(onDisk.pets.counts[toIdx], Is.EqualTo(1), "and the result is in the same write");
        }

        [Test]
        public void EquipAndUnequipEachReachTheDisk()
        {
            var save = new SaveService("pets-test.dat");
            var data = new SaveData();
            PetService s = MakeSaving(data, save);
            data.seaFightsWon = 25;
            data.pets.counts[Pets.CellIndex(2, RosterCardState.Rarity.Common, 1)] = 1;

            Assert.That(s.Equip(0, 2), Is.True);
            Assert.That(save.TryLoad(out SaveData equipped), Is.True);
            Assert.That(equipped.pets.equippedSpecies[0], Is.EqualTo(2));

            Assert.That(s.Unequip(0), Is.True);
            Assert.That(save.TryLoad(out SaveData cleared), Is.True);
            Assert.That(cleared.pets.equippedSpecies[0], Is.EqualTo(-1));
        }

        // ---- combat ------------------------------------------------------------------------------

        [Test]
        public void CombatBonusIsZeroWithNothingEquipped()
        {
            var data = new SaveData();
            PetService s = Make(data);
            data.seaFightsWon = 25;

            double[] bonus = s.CombatBonus();
            foreach (double v in bonus) Assert.That(v, Is.Zero);
        }

        [Test]
        public void CombatBonusReflectsTheEquippedPetsRarityAndStar()
        {
            var data = new SaveData();
            PetService s = Make(data);
            data.seaFightsWon = 25;
            int species = 0;   // papağan -> Dodge
            data.pets.counts[Pets.CellIndex(species, RosterCardState.Rarity.Rare, 3)] = 1;
            s.Equip(0, species);

            double[] bonus = s.CombatBonus();
            double expected = Pets.Bonus(Pets.EffectKindOf(species), RosterCardState.Rarity.Rare, 3, T);
            Assert.That(bonus[(int)Pets.EffectKindOf(species)], Is.EqualTo(expected));
        }

        [Test]
        public void FusingAnEquippedPetIsWorthMoreOnTheVeryNextRead()
        {
            // CombatBonus resolves from the live grid rather than a cached star — see PetService's
            // class note — so a fusion made between two fights is felt on the next one with no
            // extra step and no risk of the two numbers drifting apart.
            var data = new SaveData();
            PetService s = Make(data);
            data.seaFightsWon = 25;
            int species = 4;   // yengeç -> Def
            int idx = Pets.CellIndex(species, RosterCardState.Rarity.Common, 1);
            data.pets.counts[idx] = 3;
            s.Equip(0, species);

            double before = s.CombatBonus()[(int)Pets.EffectKindOf(species)];
            Assert.That(s.Fuse(species, RosterCardState.Rarity.Common, 1), Is.True);
            double after = s.CombatBonus()[(int)Pets.EffectKindOf(species)];

            Assert.That(after, Is.GreaterThan(before));
        }

        [Test]
        public void UnequippingDropsItsBonusToZero()
        {
            var data = new SaveData();
            PetService s = Make(data);
            data.seaFightsWon = 25;
            int species = 2;
            data.pets.counts[Pets.CellIndex(species, RosterCardState.Rarity.Common, 1)] = 1;
            s.Equip(0, species);
            Assert.That(s.CombatBonus()[(int)Pets.EffectKindOf(species)], Is.GreaterThan(0d));

            s.Unequip(0);
            Assert.That(s.CombatBonus()[(int)Pets.EffectKindOf(species)], Is.Zero);
        }
    }
}
