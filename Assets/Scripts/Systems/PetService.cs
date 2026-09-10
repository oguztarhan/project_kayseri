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
        /// <summary>Pearls one won fight pays when no <see cref="Game.Data.PetConfig"/> is wired —
        /// the same number that config's own default field carries, centralised here so the
        /// bootstrap's fallback and the asset's shipped value cannot drift apart.</summary>
        public const long DefaultPearlsPerWin = 2L;

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
        private Pets.Tuning _tuning;
        private PetChest.Tuning _chest;

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

        public PetService(SaveData data, Pets.Tuning tuning, PetChest.Tuning chest,
                          int[] slotUnlockFightsWon = null, Random random = null,
                          UnityEngine.Color[] rarityTint = null, SaveService save = null)
        {
            _data = data;
            _save = save;
            _tuning = tuning;
            _chest = chest;
            _random = random ?? new Random();
            _rarityTint = rarityTint != null && rarityTint.Length >= Pets.RarityCount
                ? rarityTint : DefaultRarityTint;
            _slotGates = Fit(slotUnlockFightsWon, Pets.SlotCount);
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
        /// <summary>
        /// Spend three copies at (<paramref name="species"/>, <paramref name="rarity"/>,
        /// <paramref name="star"/>) for one at the next rung. Refused with not enough copies, at the
        /// Mythic★5 ceiling, or for a cell that does not exist.
        /// </summary>
        public bool Fuse(int species, RosterCardState.Rarity rarity, int star)
        {
            if (_data == null || !Pets.Exists(species)) return false;
            if (CountAt(species, rarity, star) < Pets.FuseGroupSize) return false;
            if (!Pets.TryFuse(rarity, star, out var nextRarity, out int nextStar)) return false;

            int fromIdx = Pets.CellIndex(species, rarity, star);
            int toIdx = Pets.CellIndex(species, nextRarity, nextStar);
            if (fromIdx < 0 || toIdx < 0) return false;

            _data.pets.counts[fromIdx] -= Pets.FuseGroupSize;
            _data.pets.counts[toIdx] += 1;
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
        /// keeps, so a bulk open cannot be interrupted half-paid.
        /// </summary>
        public (int Species, RosterCardState.Rarity Rarity)[] TryOpenChests(int chests)
        {
            if (_data == null || chests <= 0) return null;
            long cost = PetChest.Cost(chests, _chest);
            if (_data.pearls < cost) return null;

            _data.pearls -= cost;

            var pulled = new (int Species, RosterCardState.Rarity Rarity)[chests];
            for (int i = 0; i < chests; i++)
            {
                var rarity = PetChest.RollRarity(_random.NextDouble(),
                                                 _data.pets.chestSinceEpic, _data.pets.chestSinceLegendary,
                                                 _chest);
                int species = PetChest.RollSpecies(_random.NextDouble());

                PetChest.Advance(rarity, ref _data.pets.chestSinceEpic, ref _data.pets.chestSinceLegendary);
                Grant(species, rarity);
                pulled[i] = (species, rarity);
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

        /// <summary>Hand one pet over, always at star 1 of the rarity it rolled.</summary>
        private void Grant(int species, RosterCardState.Rarity rarity)
        {
            if (_data == null || !Pets.Exists(species)) return;
            int idx = Pets.CellIndex(species, rarity, 1);
            if (idx < 0) return;
            _data.pets.counts[idx]++;
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
