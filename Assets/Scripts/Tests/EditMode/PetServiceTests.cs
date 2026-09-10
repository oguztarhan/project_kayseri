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

        private static PetService MakeRewards(SaveData data, Pets.RewardTuning rewards,
                                              System.Func<long> nowUnix = null, SaveService save = null)
            => new PetService(data, T, C, Gates, new System.Random(12345), null, save, rewards, nowUnix);

        private sealed class FixedRandom : System.Random
        {
            private readonly double[] _values;
            private int _next;

            public FixedRandom(params double[] values) => _values = values;

            public override double NextDouble()
            {
                int index = _next < _values.Length ? _next++ : _values.Length - 1;
                return _values[index];
            }
        }

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
        public void NewRewardFieldsNormaliseWithoutResettingThePetGrid()
        {
            var data = new SaveData();
            data.pets.counts = new[] { 4 };
            data.pets.petEssence = -3L;
            data.pets.dailyRewardDay = -9L;
            data.pets.claimedRewardIds = new[] { "pet.weekly.100", "", "pet.weekly.100", null };

            Make(data);

            Assert.That(data.pets.counts[0], Is.EqualTo(4));
            Assert.That(data.pets.petEssence, Is.Zero);
            Assert.That(data.pets.dailyRewardDay, Is.EqualTo(-1L));
            Assert.That(data.pets.claimedRewardIds, Is.EqualTo(new[] { "pet.weekly.100" }));
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

            (int species, RosterCardState.Rarity rarity, bool essence) = pulled[0];
            Assert.That(essence, Is.False);
            Assert.That(s.CountAt(species, rarity, 1), Is.EqualTo(1));
            Assert.That(s.Owned(species), Is.True);
        }

        // ---- reward economy ----------------------------------------------------------------------

        [Test]
        public void SeaWinFormulaUsesTierKindAndTheOnePearlFloor()
        {
            Pets.RewardTuning rewards = Pets.RewardTuning.Default;

            Assert.That(PetService.PearlsForSeaWin(0, 0, rewards), Is.EqualTo(1L));
            Assert.That(PetService.PearlsForSeaWin(2, 1, rewards), Is.EqualTo(3L));
            Assert.That(PetService.PearlsForSeaWin(3, 1, rewards), Is.EqualTo(7L));
        }

        [Test]
        public void BootstrapPaysExactlyOnceAcrossReload()
        {
            var data = new SaveData();
            PetService first = MakeRewards(data, Pets.RewardTuning.Default);

            Assert.That(first.TryGrantBootstrap(), Is.True);
            Assert.That(data.pearls, Is.EqualTo(100L));

            SaveData reloaded = UnityEngine.JsonUtility.FromJson<SaveData>(UnityEngine.JsonUtility.ToJson(data));
            PetService second = MakeRewards(reloaded, Pets.RewardTuning.Default);
            Assert.That(second.TryGrantBootstrap(), Is.False);
            Assert.That(reloaded.pearls, Is.EqualTo(100L));
        }

        [Test]
        public void DailyRewardUsesUtcDaysAndCannotBeClaimedTwice()
        {
            long now = 172800L;
            var data = new SaveData();
            PetService s = MakeRewards(data, Pets.RewardTuning.Default, () => now);

            Assert.That(s.TryClaimDaily(), Is.True);
            Assert.That(s.TryClaimDaily(), Is.False);
            Assert.That(data.pearls, Is.EqualTo(20L));

            now += 86400L;
            Assert.That(s.TryClaimDaily(), Is.True);
            Assert.That(data.pearls, Is.EqualTo(40L));
        }

        [Test]
        public void SeaMilestonesAndAchievementTiersHaveStableOneShotReceipts()
        {
            var data = new SaveData();
            PetService s = MakeRewards(data, Pets.RewardTuning.Default);
            data.seaFightsWon = 50;
            // Every species owned, one of them Legendary — all three collection tiers reached.
            for (int species = 0; species < Pets.SpeciesCount; species++)
                data.pets.counts[Pets.CellIndex(species, RosterCardState.Rarity.Common, 1)] = 1;
            data.pets.counts[Pets.CellIndex(0, RosterCardState.Rarity.Legendary, 1)] = 1;

            Assert.That(s.TryClaimSeaFightMilestone(0), Is.True);
            Assert.That(s.TryClaimSeaFightMilestone(1), Is.True);
            Assert.That(s.TryClaimSeaFightMilestone(2), Is.True);
            Assert.That(s.TryClaimSeaFightMilestone(2), Is.False);
            Assert.That(s.TryClaimAchievementReward(0), Is.True);
            Assert.That(s.TryClaimAchievementReward(1), Is.True);
            Assert.That(s.TryClaimAchievementReward(2), Is.True);
            Assert.That(s.TryClaimAchievementReward(1), Is.False);
            Assert.That(data.pearls, Is.EqualTo(350L));
        }

        [Test]
        public void EachAchievementTierIsRefusedUntilItsCollectionConditionHolds()
        {
            var data = new SaveData();
            PetService s = MakeRewards(data, Pets.RewardTuning.Default);

            for (int tier = 0; tier < PetService.AchievementRewardTierCount; tier++)
                Assert.That(s.TryClaimAchievementReward(tier), Is.False, "nothing owned yet: tier " + tier);
            Assert.That(data.pearls, Is.Zero);

            data.pets.counts[Pets.CellIndex(2, RosterCardState.Rarity.Common, 1)] = 1;
            Assert.That(s.CanClaimAchievement(0), Is.True, "a first pet reaches tier 1");
            Assert.That(s.CanClaimAchievement(1), Is.False, "one species is not all six");
            Assert.That(s.CanClaimAchievement(2), Is.False, "a Common is not Legendary");
            Assert.That(s.TryClaimAchievementReward(0), Is.True);
            Assert.That(s.TryClaimAchievementReward(0), Is.False);

            for (int species = 0; species < Pets.SpeciesCount; species++)
                data.pets.counts[Pets.CellIndex(species, RosterCardState.Rarity.Common, 1)] = 1;
            Assert.That(s.TryClaimAchievementReward(1), Is.True);
            Assert.That(s.TryClaimAchievementReward(2), Is.False);

            data.pets.counts[Pets.CellIndex(4, RosterCardState.Rarity.Mythic, 1)] = 1;
            Assert.That(s.TryClaimAchievementReward(2), Is.True, "Mythic counts as Legendary-or-better");
            Assert.That(data.pearls, Is.EqualTo(25L + 50L + 100L));
        }

        [Test]
        public void DailyReadinessFlipsWithTheUtcDay()
        {
            long now = 172800L + 3600L;
            var data = new SaveData();
            PetService s = MakeRewards(data, Pets.RewardTuning.Default, () => now);

            Assert.That(s.CanClaimDaily, Is.True);
            s.TryClaimDaily();
            Assert.That(s.CanClaimDaily, Is.False);
            now += 86400L - 3600L;   // midnight UTC, not 24h after the claim
            Assert.That(s.CanClaimDaily, Is.True);
        }

        [Test]
        public void ClaimableRewardCountAddsDailyMilestonesAndAchievements()
        {
            var data = new SaveData();
            PetService s = MakeRewards(data, Pets.RewardTuning.Default, () => 172800L);
            Assert.That(s.ClaimableRewardCount, Is.EqualTo(1), "only the daily on a fresh save");

            data.seaFightsWon = 25;
            data.pets.counts[Pets.CellIndex(0, RosterCardState.Rarity.Common, 1)] = 1;
            Assert.That(s.ClaimableRewardCount, Is.EqualTo(1 + 2 + 1));

            s.TryClaimDaily();
            s.TryClaimSeaFightMilestone(0);
            Assert.That(s.ClaimableRewardCount, Is.EqualTo(2));
        }

        [Test]
        public void TheExistingWeeklyHundredPointClaimPaysPetPearlsOnce()
        {
            var data = new SaveData();
            var goals = new GoalService(data, new WalletService(data.wallet), null, new TimeService());
            PetService pets = MakeRewards(data, Pets.RewardTuning.Default);
            goals.Pets = pets;

            for (int i = 0; i < Goals.WeeklyTasks.Length; i++)
                goals.Record(Goals.WeeklyTasks[i].Metric, Goals.WeeklyTasks[i].Target);

            int weeklyHundred = -1;
            for (int i = 0; i < Goals.WeeklyMilestones.Length; i++)
                if (Goals.WeeklyMilestones[i].Points == PetService.WeeklyMilestonePoints) weeklyHundred = i;

            Assert.That(weeklyHundred, Is.GreaterThanOrEqualTo(0));
            Assert.That(goals.ClaimWeeklyMilestone(weeklyHundred), Is.True);
            Assert.That(data.pearls, Is.EqualTo(100L));
            Assert.That(goals.ClaimWeeklyMilestone(weeklyHundred), Is.False);
            Assert.That(data.pearls, Is.EqualTo(100L));
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

        [Test]
        public void EveryValidFusionRungProducesTheExactExpectedReceipt()
        {
            int transitions = 0;
            for (int rarityIndex = 0; rarityIndex < Pets.RarityCount; rarityIndex++)
            {
                var rarity = (RosterCardState.Rarity)rarityIndex;
                for (int star = 1; star <= Pets.MaxStars; star++)
                {
                    if (!Pets.TryFuse(rarity, star, out var expectedRarity, out int expectedStar)) continue;

                    var data = new SaveData();
                    PetService s = Make(data);
                    int from = Pets.CellIndex(0, rarity, star);
                    data.pets.counts[from] = Pets.FuseGroupSize;

                    Assert.That(s.Fuse(0, rarity, star, out PetService.FusionResult result), Is.True,
                                rarity + " " + star + " should be a valid rung");
                    Assert.That(result.RealCopiesConsumed, Is.EqualTo(Pets.FuseGroupSize));
                    Assert.That(result.EssenceSpent, Is.Zero);
                    Assert.That(result.ResultRarity, Is.EqualTo(expectedRarity));
                    Assert.That(result.ResultStar, Is.EqualTo(expectedStar));
                    Assert.That(result.CrossedRarity, Is.EqualTo(star == Pets.MaxStars));
                    Assert.That(data.pets.counts[from], Is.Zero);
                    Assert.That(data.pets.counts[Pets.CellIndex(0, expectedRarity, expectedStar)], Is.EqualTo(1));
                    transitions++;
                }
            }

            Assert.That(transitions, Is.EqualTo(24));
        }

        [Test]
        public void EssenceSubstitutionConsumesExactlyTwoCopiesAndOneEssence()
        {
            var data = new SaveData();
            PetService s = Make(data);
            int from = Pets.CellIndex(2, RosterCardState.Rarity.Rare, 5);
            int to = Pets.CellIndex(2, RosterCardState.Rarity.Epic, 1);
            data.pets.counts[from] = 2;
            data.pets.petEssence = 1L;

            Assert.That(s.TryGetFusionResult(2, RosterCardState.Rarity.Rare, 5, true,
                                              out PetService.FusionResult preview), Is.True);
            Assert.That(preview.RealCopiesConsumed, Is.EqualTo(2));
            Assert.That(preview.EssenceSpent, Is.EqualTo(1L));
            Assert.That(preview.CrossedRarity, Is.True);

            Assert.That(s.FuseWithEssence(2, RosterCardState.Rarity.Rare, 5,
                                           out PetService.FusionResult result), Is.True);
            Assert.That(result.RealCopiesConsumed, Is.EqualTo(2));
            Assert.That(result.EssenceSpent, Is.EqualTo(1L));
            Assert.That(data.pets.counts[from], Is.Zero);
            Assert.That(data.pets.counts[to], Is.EqualTo(1));
            Assert.That(s.PetEssence, Is.Zero);
        }

        [Test]
        public void InvalidEssenceFusionInputsFailWithoutMutatingTheSave()
        {
            var data = new SaveData();
            PetService s = Make(data);
            int valid = Pets.CellIndex(0, RosterCardState.Rarity.Common, 1);
            int maxed = Pets.CellIndex(0, RosterCardState.Rarity.Mythic, Pets.MaxStars);
            data.pets.counts[valid] = 1;
            data.pets.counts[maxed] = 3;
            data.pets.petEssence = 1L;
            int[] beforeCounts = (int[])data.pets.counts.Clone();

            Assert.That(s.FuseWithEssence(-1, RosterCardState.Rarity.Common, 1, out _), Is.False);
            Assert.That(s.FuseWithEssence(0, (RosterCardState.Rarity)99, 1, out _), Is.False);
            Assert.That(s.FuseWithEssence(0, RosterCardState.Rarity.Common, 0, out _), Is.False);
            Assert.That(s.FuseWithEssence(0, RosterCardState.Rarity.Common, 1, out _), Is.False,
                        "one real copy plus Essence is not a three-input fusion");
            Assert.That(s.FuseWithEssence(0, RosterCardState.Rarity.Mythic, Pets.MaxStars, out _), Is.False);
            Assert.That(data.pets.counts, Is.EqualTo(beforeCounts));
            Assert.That(data.pets.petEssence, Is.EqualTo(1L));
        }

        [Test]
        public void AMythicFiveStarChestDuplicateBecomesEssenceInsteadOfAnInertCopy()
        {
            var data = new SaveData();
            data.pearls = C.PearlCost;
            data.pets.counts[Pets.CellIndex(0, RosterCardState.Rarity.Mythic, Pets.MaxStars)] = 1;
            var s = new PetService(data, T, C, Gates, new FixedRandom(0.999d, 0d));

            var pulled = s.TryOpenChests(1);

            Assert.That(pulled, Is.Not.Null);
            Assert.That(pulled[0].Rarity, Is.EqualTo(RosterCardState.Rarity.Mythic));
            Assert.That(pulled[0].Essence, Is.True, "the receipt says the pull became Essence");
            Assert.That(s.PetEssence, Is.EqualTo(1L));
            Assert.That(s.CountAt(0, RosterCardState.Rarity.Mythic, 1), Is.Zero);
            Assert.That(s.CountAt(0, RosterCardState.Rarity.Mythic, Pets.MaxStars), Is.EqualTo(1));
            Assert.That(s.Pearls, Is.Zero);
        }

        [Test]
        public void AMythicPullOfASpeciesNotYetMaxedIsACopyNotEssence()
        {
            var data = new SaveData();
            data.pearls = C.PearlCost;
            var s = new PetService(data, T, C, Gates, new FixedRandom(0.999d, 0d));

            var pulled = s.TryOpenChests(1);

            Assert.That(pulled[0].Rarity, Is.EqualTo(RosterCardState.Rarity.Mythic));
            Assert.That(pulled[0].Essence, Is.False);
            Assert.That(s.CountAt(0, RosterCardState.Rarity.Mythic, 1), Is.EqualTo(1));
            Assert.That(s.PetEssence, Is.Zero);
        }

        [Test]
        public void MaxedPetDuplicateCannotCreateAChestRefundOrFurtherFusionLoop()
        {
            var data = new SaveData();
            data.pearls = C.PearlCost;
            data.pets.counts[Pets.CellIndex(0, RosterCardState.Rarity.Mythic, Pets.MaxStars)] = 1;
            var s = new PetService(data, T, C, Gates, new FixedRandom(0.999d, 0d));

            Assert.That(s.TryOpenChests(1), Is.Not.Null);
            Assert.That(s.Pearls, Is.Zero, "the duplicate conversion does not refund the chest");
            Assert.That(s.PetEssence, Is.EqualTo(1L));
            Assert.That(s.FuseWithEssence(0, RosterCardState.Rarity.Mythic, Pets.MaxStars, out _), Is.False);
            Assert.That(s.PetEssence, Is.EqualTo(1L), "a capped rung cannot spend or turn Essence into more pets");
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
        public void EssenceGrantsAndSpendsReachTheDiskAtomically()
        {
            var save = new SaveService("pets-test.dat");
            var data = new SaveData();
            PetService s = MakeRewards(data, Pets.RewardTuning.Default, null, save);

            Assert.That(s.GrantPetEssence(3L), Is.True);
            Assert.That(save.TryLoad(out SaveData granted), Is.True);
            Assert.That(granted.pets.petEssence, Is.EqualTo(3L));

            Assert.That(s.TrySpendPetEssence(1L), Is.True);
            Assert.That(save.TryLoad(out SaveData spent), Is.True);
            Assert.That(spent.pets.petEssence, Is.EqualTo(2L));
        }

        [Test]
        public void EssenceFusionWritesCopiesResultAndEssenceInOneSave()
        {
            var save = new SaveService("pets-test.dat");
            var data = new SaveData();
            PetService s = MakeSaving(data, save);
            int from = Pets.CellIndex(3, RosterCardState.Rarity.Legendary, 5);
            int to = Pets.CellIndex(3, RosterCardState.Rarity.Mythic, 1);
            data.pets.counts[from] = 2;
            data.pets.petEssence = 1L;

            Assert.That(s.FuseWithEssence(3, RosterCardState.Rarity.Legendary, 5, out _), Is.True);
            Assert.That(save.TryLoad(out SaveData onDisk), Is.True);
            Assert.That(onDisk.pets.counts[from], Is.Zero);
            Assert.That(onDisk.pets.counts[to], Is.EqualTo(1));
            Assert.That(onDisk.pets.petEssence, Is.Zero);
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
            Assert.That(s.EquippedAt(0), Is.EqualTo(species));
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
