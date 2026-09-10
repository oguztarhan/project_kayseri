using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Owns the pet grid, the equipped slots and the chest: who has been drawn, how far each has
    /// fused, which three ride the ship, and how many pearls are in hand. The maths is in
    /// <see cref="Pets"/> and <see cref="PetChest"/>; this spends, grants and remembers — the same
    /// split <see cref="CaptainService"/> keeps for its own roster and crate.
    ///
    /// THE RANDOMNESS LIVES HERE AND NOWHERE ELSE, for the reason every crate in this project keeps
    /// it: <see cref="PetChest"/> takes a roll and returns a result, so the whole distribution can be
    /// asserted over tens of thousands of pulls in an EditMode test.
    ///
    /// A PULL IS ALWAYS A GRANT AT STAR 1. Unlike <see cref="CaptainService.Grant"/>, there is no
    /// "first copy is free, the rest are duplicates" split — every copy of every species is worth the
    /// same one count at the rarity it rolled, and <see cref="Fuse"/> is the only thing that ever
    /// turns three of them into something better. See <see cref="Pets"/>'s class note for why stars
    /// are fusion and not leveling.
    ///
    /// SLOTS UNLOCK THROUGH PROGRESSION, NOT ALL AT ONCE. <see cref="SlotsUnlocked"/> reads
    /// <c>SaveData.seaFightsWon</c> — the same ladder the route strip already climbs — against gates
    /// supplied by the bootstrap (<see cref="Game.Data.PetConfig.SlotUnlockFightsWon"/>). A new player
    /// is handed one slot and one ocean-themed choice to make, not a roster screen with three empty
    /// sockets and no idea which to fill first.
    /// </summary>
    public sealed class PetService
    {
        public const int SeaFightMilestoneCount = 3;
        public const int AchievementRewardTierCount = 3;
        public const int WeeklyMilestonePoints = 100;
        public static readonly int[] DefaultSeaFightMilestoneWins = { 10, 25, 50 };

        /// <summary>Slot gates used when no <see cref="Game.Data.PetConfig"/> is wired — the same
        /// numbers that config's own default field carries. Unlike most fallbacks in this project
        /// (which only cost the player art), these are gameplay: falling back to all-zero would hand
        /// over every slot on day one with no asset wired at all, which is the one regression "runs
        /// without a config" is never supposed to mean.</summary>
        public static readonly int[] DefaultSlotUnlockFightsWon = { 0, 10, 25 };

        private readonly SaveData _data;
        private readonly SaveService _save;   // null in tests that do not assert the disk
        private readonly Random _random;
        private readonly UnityEngine.Color[] _rarityTint;
        private readonly int[] _slotGates;
        private readonly Func<long> _nowUnix;
        private Pets.Tuning _tuning;
        private PetChest.Tuning _chest;
        private Pets.RewardTuning _rewards;

        private static readonly UnityEngine.Color[] DefaultRarityTint =
        {
            new UnityEngine.Color(0.48f, 0.54f, 0.62f, 1f),   // Common
            new UnityEngine.Color(0.26f, 0.60f, 0.92f, 1f),   // Rare
            new UnityEngine.Color(0.62f, 0.38f, 0.92f, 1f),   // Epic
            new UnityEngine.Color(0.96f, 0.66f, 0.18f, 1f),   // Legendary
            new UnityEngine.Color(0.94f, 0.28f, 0.42f, 1f),   // Mythic
        };

        /// <summary>Raised when anything the collection, the equip bar or the chest screen shows has
        /// moved.</summary>
        public event Action Changed;

        /// <summary>Raised once per pet handed over by a chest, in the order they came out.</summary>
        public event Action<int, RosterCardState.Rarity> Pulled;

        /// <summary>The complete, presentation-safe receipt for a fusion. UI can show the exact
        /// inputs consumed without inferring them from a grid that may already have refreshed.</summary>
        public readonly struct FusionResult
        {
            public readonly int Species;
            public readonly RosterCardState.Rarity SourceRarity;
            public readonly int SourceStar;
            public readonly int RealCopiesConsumed;
            public readonly long EssenceSpent;
            public readonly RosterCardState.Rarity ResultRarity;
            public readonly int ResultStar;

            public FusionResult(int species, RosterCardState.Rarity sourceRarity, int sourceStar,
                                int realCopiesConsumed, long essenceSpent,
                                RosterCardState.Rarity resultRarity, int resultStar)
            {
                Species = species;
                SourceRarity = sourceRarity;
                SourceStar = sourceStar;
                RealCopiesConsumed = realCopiesConsumed;
                EssenceSpent = essenceSpent;
                ResultRarity = resultRarity;
                ResultStar = resultStar;
            }

            public bool UsedEssence => EssenceSpent > 0L;
            public bool CrossedRarity => SourceRarity != ResultRarity;
        }

        public PetService(SaveData data, Pets.Tuning tuning, PetChest.Tuning chest,
                          int[] slotUnlockFightsWon = null, Random random = null,
                          UnityEngine.Color[] rarityTint = null, SaveService save = null,
                          Pets.RewardTuning? rewards = null, Func<long> nowUnix = null)
        {
            _data = data;
            _save = save;
            _tuning = tuning;
            _chest = chest;
            _random = random ?? new Random();
            _rarityTint = rarityTint != null && rarityTint.Length >= Pets.RarityCount
                ? rarityTint : DefaultRarityTint;
            _slotGates = Fit(slotUnlockFightsWon, Pets.SlotCount);
            _rewards = rewards ?? Pets.RewardTuning.Default;
            _nowUnix = nowUnix ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            Normalise();
        }

        private static int[] Fit(int[] src, int len)
        {
            var fitted = new int[len];
            if (src != null)
            {
                int n = src.Length < len ? src.Length : len;
                for (int i = 0; i < n; i++) fitted[i] = src[i] < 0 ? 0 : src[i];
            }
            return fitted;
        }

        private void Normalise()
        {
            if (_data == null) return;
            if (_data.pets == null) _data.pets = new PetSaveData();
            _data.pets.Normalise();
            if (_data.pearls < 0L) _data.pearls = 0L;
        }

        public UnityEngine.Color RarityTint(RosterCardState.Rarity rarity)
        {
            int i = (int)rarity;
            if (i < 0) i = 0;
            if (i >= _rarityTint.Length) i = _rarityTint.Length - 1;
            return _rarityTint[i];
        }

        public Pets.Tuning Tuning => _tuning;
        public PetChest.Tuning ChestTuning => _chest;

        // ------------------------------------------------------------------ read
        public long Pearls => _data != null ? _data.pearls : 0L;
        public long PetEssence => _data != null ? _data.pets.petEssence : 0L;
        public int ChestsOpened => _data != null ? _data.pets.chestsOpened : 0;
        public int SinceEpic => _data != null ? _data.pets.chestSinceEpic : 0;
        public int SinceLegendary => _data != null ? _data.pets.chestSinceLegendary : 0;

        /// <summary>
        /// How many of <see cref="Pets.SlotCount"/> are open for use right now. Gates are assumed
        /// authored in ascending order (each slot needs at least as many wins as the one before it);
        /// the walk stops at the first gate not yet met rather than trusting a later one that happens
        /// to be lower.
        /// </summary>
        public int SlotsUnlocked
        {
            get
            {
                int won = _data != null ? _data.seaFightsWon : 0;
                int n = 0;
                for (int i = 0; i < Pets.SlotCount; i++)
                {
                    if (won < _slotGates[i]) break;
                    n++;
                }
                return n;
            }
        }

        /// <summary>Won fights still needed before this slot opens. 0 for an already-open slot.</summary>
        public int FightsUntilSlot(int slot)
        {
            if (slot < 0 || slot >= Pets.SlotCount) return 0;
            int won = _data != null ? _data.seaFightsWon : 0;
            int need = _slotGates[slot] - won;
            return need < 0 ? 0 : need;
        }

        /// <summary>Copies owned at one cell.</summary>
        public int CountAt(int species, RosterCardState.Rarity rarity, int star)
            => _data != null ? Pets.CountAt(_data.pets.counts, species, rarity, star) : 0;

        /// <summary>The best cell a species is owned at, or false for one never drawn.</summary>
        public bool TryBestOwned(int species, out RosterCardState.Rarity rarity, out int star)
        {
            rarity = RosterCardState.Rarity.Common;
            star = 0;
            return _data != null && Pets.TryBestOwned(_data.pets.counts, species, out rarity, out star);
        }

        public bool Owned(int species) => TryBestOwned(species, out _, out _);

        /// <summary>Who is in slot <paramref name="slot"/>, or -1 for empty or for an unlocked-but-
        /// unfilled / not-yet-unlocked slot.</summary>
        public int EquippedAt(int slot)
        {
            if (_data == null || slot < 0 || slot >= Pets.SlotCount || slot >= SlotsUnlocked) return -1;
            return _data.pets.equippedSpecies[slot];
        }

        // ---------------------------------------------------------------- rewards
        /// <summary>The only production writer for the pet pearl wallet. A caller mutates its own
        /// eligibility state first, then calls this once so the flag and the balance reach disk as
        /// one SaveData snapshot.</summary>
        public bool GrantPearls(long amount)
        {
            if (_data == null || amount <= 0L) return false;
            long room = long.MaxValue - _data.pearls;
            if (room <= 0L) return false;
            _data.pearls += amount > room ? room : amount;
            _save?.Save(_data);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Pet Essence's only storage surface. Phase 2 will call this from maxed Mythic
        /// duplicates; keeping the mutation here now gives it the same save-before-acknowledge
        /// contract as pearls from the beginning.</summary>
        public bool GrantPetEssence(long amount)
        {
            if (!AddPetEssence(amount)) return false;
            _save?.Save(_data);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Standalone spend primitive for any future Essence-only action. Fusion changes
        /// Essence alongside its grid cells inside one transaction, so it does not call this method
        /// and accidentally create two save snapshots.</summary>
        public bool TrySpendPetEssence(long amount)
        {
            if (_data == null || amount <= 0L || _data.pets.petEssence < amount) return false;
            _data.pets.petEssence -= amount;
            _save?.Save(_data);
            Changed?.Invoke();
            return true;
        }

        private bool AddPetEssence(long amount)
        {
            if (_data == null || amount <= 0L) return false;
            long room = long.MaxValue - _data.pets.petEssence;
            if (room <= 0L) return false;
            _data.pets.petEssence += amount > room ? room : amount;
            return true;
        }

        /// <summary>Approved sea-win formula. Tier and enemy kind are clamped only for corrupted
        /// callers; EncounterController supplies their real resolved values on every win.</summary>
        public static long PearlsForSeaWin(int tier, int enemyKind, in Pets.RewardTuning rewards)
        {
            int safeTier = Clamp(tier, 0, Voyages.PayoutMult.Length - 1);
            int safeKind = Clamp(enemyKind, 0, SeaCombat.KindLoot.Length - 1);
            double baseValue = rewards.WinBase < 0d ? 0d : rewards.WinBase;
            double share = rewards.WinLootShare < 0d ? 0d : rewards.WinLootShare;
            double paid = baseValue * Voyages.PayoutMult[safeTier] * share * SeaCombat.KindLoot[safeKind];
            long rounded = (long)Math.Round(paid, MidpointRounding.AwayFromZero);
            return rounded < 1L ? 1L : rounded;
        }

        public long PearlsForSeaWin(int tier, int enemyKind) => PearlsForSeaWin(tier, enemyKind, _rewards);

        /// <summary>Called exactly once by ExpeditionService after a confirmed at-sea win. This is
        /// intentionally separate from RegisterKill so mid-fight plunder cannot earn pearls.</summary>
        public bool GrantSeaFightWin(int tier, int enemyKind) => GrantPearls(PearlsForSeaWin(tier, enemyKind));

        public bool TryGrantBootstrap()
        {
            if (_data == null || _data.pets.bootstrapGranted) return false;
            _data.pets.bootstrapGranted = true;
            CommitClaim(_rewards.BootstrapPearls);
            return true;
        }

        public bool CanClaimDaily => _data != null && _data.pets.dailyRewardDay != _nowUnix() / 86400L;

        public bool TryClaimDaily()
        {
            if (!CanClaimDaily) return false;
            _data.pets.dailyRewardDay = _nowUnix() / 86400L;
            CommitClaim(_rewards.DailyPearls);
            return true;
        }

        public bool CanClaimSeaFightMilestone(int index)
            => _data != null && index >= 0 && index < SeaFightMilestoneCount
               && _data.seaFightsWon >= DefaultSeaFightMilestoneWins[index]
               && !HasClaimed(SeaFightRewardId(index));

        public bool TryClaimSeaFightMilestone(int index)
        {
            if (!CanClaimSeaFightMilestone(index)) return false;
            return TryClaim(SeaFightRewardId(index), RewardAt(_rewards.SeaFightMilestonePearls, index));
        }

        /// <summary>Called by GoalService only after it records the existing 100-point weekly
        /// milestone as claimed. The stable pet receipt prevents retries from paying twice.</summary>
        public bool TryClaimWeeklyMilestone(int points)
            => points == WeeklyMilestonePoints
               && TryClaim("pet.weekly.100", _rewards.WeeklyMilestonePearls);

        /// <summary>
        /// The three collection tiers, read straight off the counts grid: a first pet, all six
        /// species, and a Legendary-or-better copy of anything. Nothing is counted separately, so
        /// there is no second number that could drift from what the player actually owns.
        /// </summary>
        public bool AchievementReached(int tier)
        {
            if (_data == null) return false;
            int owned = 0;
            bool legendary = false;
            for (int species = 0; species < Pets.SpeciesCount; species++)
            {
                if (!TryBestOwned(species, out var rarity, out _)) continue;
                owned++;
                if (rarity >= RosterCardState.Rarity.Legendary) legendary = true;
            }
            switch (tier)
            {
                case 0: return owned > 0;
                case 1: return owned >= Pets.SpeciesCount;
                case 2: return legendary;
                default: return false;
            }
        }

        public bool CanClaimAchievement(int tier)
            => AchievementReached(tier) && !HasClaimed(AchievementRewardId(tier));

        /// <summary>Pays one achievement tier once it is reached. Eligibility and the receipt both
        /// live here, so no screen can pay a tier the collection has not earned.</summary>
        public bool TryClaimAchievementReward(int tier)
        {
            if (!CanClaimAchievement(tier)) return false;
            return TryClaim(AchievementRewardId(tier), RewardAt(_rewards.AchievementPearls, tier));
        }

        /// <summary>Everything the panel's rewards button would pay right now.</summary>
        public int ClaimableRewardCount
        {
            get
            {
                int n = CanClaimDaily ? 1 : 0;
                for (int i = 0; i < SeaFightMilestoneCount; i++) if (CanClaimSeaFightMilestone(i)) n++;
                for (int i = 0; i < AchievementRewardTierCount; i++) if (CanClaimAchievement(i)) n++;
                return n;
            }
        }

        private static string AchievementRewardId(int tier) => "pet.achievement." + (tier + 1);

        private static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        private static long RewardAt(long[] rewards, int index)
        {
            if (rewards == null || index < 0 || index >= rewards.Length) return 0L;
            return rewards[index] < 0L ? 0L : rewards[index];
        }

        private static string SeaFightRewardId(int index) => "pet.sea-win." + (index + 1);

        private bool HasClaimed(string id)
        {
            if (_data == null || string.IsNullOrEmpty(id)) return false;
            string[] claimed = _data.pets.claimedRewardIds;
            for (int i = 0; i < claimed.Length; i++) if (claimed[i] == id) return true;
            return false;
        }

        private bool TryClaim(string id, long amount)
        {
            if (_data == null || string.IsNullOrEmpty(id) || HasClaimed(id)) return false;
            string[] previous = _data.pets.claimedRewardIds;
            var claimed = new string[previous.Length + 1];
            Array.Copy(previous, claimed, previous.Length);
            claimed[claimed.Length - 1] = id;
            _data.pets.claimedRewardIds = claimed;
            CommitClaim(amount);
            return true;
        }

        private void CommitClaim(long amount)
        {
            if (amount > 0L)
            {
                long room = long.MaxValue - _data.pearls;
                if (room > 0L) _data.pearls += amount > room ? room : amount;
            }
            _save?.Save(_data);
            Changed?.Invoke();
        }

        // ----------------------------------------------------------------- equip
        /// <summary>
        /// Put a species (or -1, empty) into a slot. Refused for a slot not yet unlocked, a species
        /// nobody owns, or a species already riding another slot — three pets doing the same job
        /// reads as a bug the table cannot actually produce (see <see cref="Pets"/>'s class note), so
        /// the service refuses it outright rather than letting the UI explain why it did nothing.
        /// </summary>
        public bool Equip(int slot, int species)
        {
            if (_data == null || slot < 0 || slot >= Pets.SlotCount || slot >= SlotsUnlocked) return false;
            if (species != -1)
            {
                if (!Owned(species)) return false;
                for (int i = 0; i < _data.pets.equippedSpecies.Length; i++)
                    if (i != slot && _data.pets.equippedSpecies[i] == species) return false;
            }
            _data.pets.equippedSpecies[slot] = species;
            _save?.Save(_data);
            Changed?.Invoke();
            return true;
        }

        public bool Unequip(int slot) => Equip(slot, -1);

        // ------------------------------------------------------------------ fuse
        /// <summary>The original exact-three-copy fusion path. It deliberately never spends Essence,
        /// preserving the normal rule for callers that do not offer the escape valve.</summary>
        public bool Fuse(int species, RosterCardState.Rarity rarity, int star)
            => Fuse(species, rarity, star, out _);

        public bool Fuse(int species, RosterCardState.Rarity rarity, int star, out FusionResult result)
            => CommitFusion(species, rarity, star, false, out result);

        /// <summary>Fuse with three real copies when available, or with exactly two identical copies
        /// plus one Essence. Essence can never replace more than one input.</summary>
        public bool FuseWithEssence(int species, RosterCardState.Rarity rarity, int star,
                                    out FusionResult result)
            => CommitFusion(species, rarity, star, true, out result);

        /// <summary>Builds the same receipt a fusion would return without changing the save. This is
        /// the UI's exact-material preview: it says whether the third input would be a copy or Essence.</summary>
        public bool TryGetFusionResult(int species, RosterCardState.Rarity rarity, int star,
                                       bool allowEssence, out FusionResult result)
        {
            result = default;
            if (_data == null || !Pets.Exists(species)) return false;
            if (!Pets.TryFuse(rarity, star, out var nextRarity, out int nextStar)) return false;

            int fromIdx = Pets.CellIndex(species, rarity, star);
            int toIdx = Pets.CellIndex(species, nextRarity, nextStar);
            if (fromIdx < 0 || toIdx < 0) return false;

            int copies = _data.pets.counts[fromIdx];
            int realCopies = Pets.FuseGroupSize;
            long essence = 0L;
            if (copies < Pets.FuseGroupSize)
            {
                if (!allowEssence || copies < Pets.FuseGroupSize - 1 || _data.pets.petEssence < 1L)
                    return false;
                realCopies = Pets.FuseGroupSize - 1;
                essence = 1L;
            }

            result = new FusionResult(species, rarity, star, realCopies, essence, nextRarity, nextStar);
            return true;
        }

        private bool CommitFusion(int species, RosterCardState.Rarity rarity, int star,
                                  bool allowEssence, out FusionResult result)
        {
            if (!TryGetFusionResult(species, rarity, star, allowEssence, out result)) return false;
            int fromIdx = Pets.CellIndex(species, rarity, star);
            int toIdx = Pets.CellIndex(species, result.ResultRarity, result.ResultStar);
            _data.pets.counts[fromIdx] -= result.RealCopiesConsumed;
            _data.pets.petEssence -= result.EssenceSpent;
            _data.pets.counts[toIdx] += 1;
            ReconcileEquipped();
            _save?.Save(_data);
            Changed?.Invoke();
            return true;
        }

        // ----------------------------------------------------------------- chest
        public long ChestCost(int chests) => PetChest.Cost(chests, _chest);

        public bool CanOpenChest(int chests)
            => _data != null && chests > 0 && _data.pearls >= ChestCost(chests);

        /// <summary>
        /// Open <paramref name="chests"/> at once. Returns who came out, in order, or null when there
        /// were not enough pearls. Pearls are taken BEFORE the first roll and the whole batch is
        /// rolled in one call, the same all-or-nothing shape <see cref="CaptainService.TryOpen"/>
        /// keeps, so a bulk open cannot be interrupted half-paid. <c>Essence</c> marks a pull that
        /// became one Pet Essence instead of a copy (see <see cref="Grant"/>), so a reveal can say so.
        /// </summary>
        public (int Species, RosterCardState.Rarity Rarity, bool Essence)[] TryOpenChests(int chests)
        {
            if (_data == null || chests <= 0) return null;
            long cost = PetChest.Cost(chests, _chest);
            if (_data.pearls < cost) return null;

            _data.pearls -= cost;

            var pulled = new (int Species, RosterCardState.Rarity Rarity, bool Essence)[chests];
            for (int i = 0; i < chests; i++)
            {
                var rarity = PetChest.RollRarity(_random.NextDouble(),
                                                 _data.pets.chestSinceEpic, _data.pets.chestSinceLegendary,
                                                 _chest);
                int species = PetChest.RollSpecies(_random.NextDouble());

                PetChest.Advance(rarity, ref _data.pets.chestSinceEpic, ref _data.pets.chestSinceLegendary);
                bool essence = Grant(species, rarity);
                pulled[i] = (species, rarity, essence);
            }

            _data.pets.chestsOpened += chests;
            // One write for the whole batch, before anything is told: the pearls, the pulls and the
            // pity counters reach the disk together, so a force-close after the reveal can neither
            // refund the pearls nor re-roll what came out.
            _save?.Save(_data);
            Changed?.Invoke();
            for (int i = 0; i < pulled.Length; i++) Pulled?.Invoke(pulled[i].Species, pulled[i].Rarity);
            return pulled;
        }

        /// <summary>Hand one pet over at star 1, except for a Mythic draw of a species already at
        /// Mythic 5★. That duplicate has no useful grid destination, so it becomes one Essence in
        /// this same chest transaction instead of becoming inert inventory. True when it did.</summary>
        private bool Grant(int species, RosterCardState.Rarity rarity)
        {
            if (_data == null || !Pets.Exists(species)) return false;
            if (rarity == RosterCardState.Rarity.Mythic
                && Pets.TryBestOwned(_data.pets.counts, species, out var bestRarity, out int bestStar)
                && Pets.IsMaxed(bestRarity, bestStar))
            {
                AddPetEssence(1L);
                return true;
            }
            int idx = Pets.CellIndex(species, rarity, 1);
            if (idx >= 0) _data.pets.counts[idx]++;
            return false;
        }

        private void ReconcileEquipped()
        {
            if (_data == null) return;
            for (int slot = 0; slot < _data.pets.equippedSpecies.Length; slot++)
            {
                int species = _data.pets.equippedSpecies[slot];
                if (Pets.Exists(species) && !Owned(species)) _data.pets.equippedSpecies[slot] = -1;
            }
        }

        // ---------------------------------------------------------------- combat
        /// <summary>
        /// The sum of every equipped, unlocked slot's <see cref="Pets.Bonus"/>, indexed by
        /// <see cref="Pets.EffectKind"/> — exactly what <see cref="Pets.ApplyCombatBonus"/> expects.
        /// Resolved from the live counts grid rather than from a cached star, so a pet fused away to
        /// a higher rung a moment ago is already worth more the next time a fight is built.
        /// </summary>
        public double[] CombatBonus()
        {
            var bonus = new double[Pets.EffectKindCount];
            if (_data == null) return bonus;

            int unlocked = SlotsUnlocked;
            for (int slot = 0; slot < unlocked; slot++)
            {
                int species = _data.pets.equippedSpecies[slot];
                if (!Pets.Exists(species)) continue;
                if (!Pets.TryBestOwned(_data.pets.counts, species, out var rarity, out int star)) continue;

                Pets.EffectKind kind = Pets.EffectKindOf(species);
                bonus[(int)kind] += Pets.Bonus(kind, rarity, star, _tuning);
            }
            return bonus;
        }
    }
}
