using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Read-only index over balances that already belong to their domain services. It never writes
    /// save data and never becomes a new economy authority; it only gives presentation code one
    /// complete, live-updating query surface.
    /// </summary>
    public sealed class CurrencyRegistry : IDisposable
    {
        private static readonly CurrencyDefinition[] DefinitionsById =
        {
            new CurrencyDefinition(CurrencyId.Cash, "currency.cash", "currency.cash",
                                   CurrencyCategory.MainEconomy, CurrencyNumberFormat.Abbreviated),
            new CurrencyDefinition(CurrencyId.Gems, "currency.gems", "currency.gems",
                                   CurrencyCategory.Premium, CurrencyNumberFormat.WholeNumber),
            new CurrencyDefinition(CurrencyId.Salvage, "currency.salvage", "currency.salvage",
                                   CurrencyCategory.Sea, CurrencyNumberFormat.WholeNumber),
            new CurrencyDefinition(CurrencyId.Charts, "currency.charts", "currency.charts",
                                   CurrencyCategory.Sea, CurrencyNumberFormat.WholeNumber),
            new CurrencyDefinition(CurrencyId.CombatEnergy, "currency.combat_energy", "currency.combat_energy",
                                   CurrencyCategory.Sea, CurrencyNumberFormat.CurrentAndMaximum, true, true),
            new CurrencyDefinition(CurrencyId.CraftPoints, "currency.craft_points", "currency.craft_points",
                                   CurrencyCategory.Crafting, CurrencyNumberFormat.WholeNumber),
            new CurrencyDefinition(CurrencyId.MiningPoints, "currency.mining_points", "currency.mining_points",
                                   CurrencyCategory.MiningGear, CurrencyNumberFormat.CurrentAndMaximum, true, true),
            new CurrencyDefinition(CurrencyId.MiningScrap, "currency.mining_scrap", "currency.mining_scrap",
                                   CurrencyCategory.MiningGear, CurrencyNumberFormat.WholeNumber),
            new CurrencyDefinition(CurrencyId.Pearls, "currency.pearls", "currency.pearls",
                                   CurrencyCategory.Pets, CurrencyNumberFormat.WholeNumber),
            new CurrencyDefinition(CurrencyId.PetEssence, "currency.pet_essence", "currency.pet_essence",
                                   CurrencyCategory.Pets, CurrencyNumberFormat.WholeNumber)
        };

        private static readonly IReadOnlyList<CurrencyDefinition> ReadOnlyDefinitions =
            Array.AsReadOnly(DefinitionsById);

        private readonly TimeService _time;
        private readonly WalletService _wallet;
        private readonly ExpeditionService _expeditions;
        private readonly CraftingService _crafting;
        private readonly MiningGearService _miningGear;
        private readonly CaptainService _captains;
        private readonly PetService _pets;

        /// <summary>Raised when an existing balance-owning service reports a change.</summary>
        public event Action Changed;

        public IReadOnlyList<CurrencyDefinition> Definitions => ReadOnlyDefinitions;

        public CurrencyRegistry(TimeService time, WalletService wallet, ExpeditionService expeditions,
                                CraftingService crafting, MiningGearService miningGear,
                                CaptainService captains, PetService pets)
        {
            _time = time;
            _wallet = wallet;
            _expeditions = expeditions;
            _crafting = crafting;
            _miningGear = miningGear;
            _captains = captains;
            _pets = pets;

            if (_wallet != null)
            {
                _wallet.CashChanged += OnSourceChanged;
                _wallet.GemsChanged += OnSourceChanged;
            }
            if (_expeditions != null) _expeditions.Changed += OnSourceChanged;
            if (_crafting != null) _crafting.Changed += OnSourceChanged;
            if (_miningGear != null) _miningGear.Changed += OnSourceChanged;
            if (_captains != null) _captains.Changed += OnSourceChanged;
            if (_pets != null) _pets.Changed += OnSourceChanged;
        }

        public bool TryGetDefinition(CurrencyId id, out CurrencyDefinition definition)
            => TryDescribe(id, out definition);

        /// <summary>The same static metadata without a live registry, for text built where only the
        /// currency's identity is known (a spend receipt, a reward line).</summary>
        public static bool TryDescribe(CurrencyId id, out CurrencyDefinition definition)
        {
            int index = (int)id;
            if (index >= 0 && index < DefinitionsById.Length)
            {
                definition = DefinitionsById[index];
                return true;
            }

            definition = null;
            return false;
        }

        public bool TryGetSnapshot(CurrencyId id, out CurrencySnapshot snapshot)
        {
            if (!TryGetDefinition(id, out CurrencyDefinition definition))
            {
                snapshot = default;
                return false;
            }

            snapshot = BuildSnapshot(definition);
            return true;
        }

        public CurrencySnapshot GetSnapshot(CurrencyId id)
        {
            if (TryGetSnapshot(id, out CurrencySnapshot snapshot)) return snapshot;
            throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown currency identifier.");
        }

        /// <summary>Fills a caller-owned list so a repeatedly refreshed UI need not allocate.</summary>
        public void GetSnapshots(List<CurrencySnapshot> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            for (int i = 0; i < DefinitionsById.Length; i++)
                destination.Add(BuildSnapshot(DefinitionsById[i]));
        }

        public void Dispose()
        {
            if (_wallet != null)
            {
                _wallet.CashChanged -= OnSourceChanged;
                _wallet.GemsChanged -= OnSourceChanged;
            }
            if (_expeditions != null) _expeditions.Changed -= OnSourceChanged;
            if (_crafting != null) _crafting.Changed -= OnSourceChanged;
            if (_miningGear != null) _miningGear.Changed -= OnSourceChanged;
            if (_captains != null) _captains.Changed -= OnSourceChanged;
            if (_pets != null) _pets.Changed -= OnSourceChanged;
        }

        private CurrencySnapshot BuildSnapshot(CurrencyDefinition definition)
        {
            switch (definition.Id)
            {
                case CurrencyId.Cash:
                    return Snapshot(definition, _wallet != null ? _wallet.Cash : BigDouble.Zero);
                case CurrencyId.Gems:
                    return Snapshot(definition, _wallet != null ? _wallet.Gems : 0L);
                case CurrencyId.Salvage:
                    return Snapshot(definition, _expeditions != null ? _expeditions.Salvage : 0L);
                case CurrencyId.Charts:
                    return Snapshot(definition, _captains != null ? _captains.Charts : 0L);
                case CurrencyId.CombatEnergy:
                    return RegeneratingSnapshot(definition,
                        _expeditions != null ? _expeditions.Energy : 0L,
                        _expeditions != null ? _expeditions.EnergyMax : 0L,
                        _expeditions != null ? _expeditions.SecondsToNextEnergy : 0d);
                case CurrencyId.CraftPoints:
                    return Snapshot(definition, _crafting != null ? _crafting.Points : 0L);
                case CurrencyId.MiningPoints:
                    return RegeneratingSnapshot(definition,
                        _miningGear != null ? _miningGear.Points : 0L,
                        _miningGear != null ? _miningGear.PointCap : 0L,
                        _miningGear != null ? _miningGear.SecondsToNextPoint : 0d);
                case CurrencyId.MiningScrap:
                    return Snapshot(definition, _miningGear != null ? _miningGear.Scrap : 0L);
                case CurrencyId.Pearls:
                    return Snapshot(definition, _pets != null ? _pets.Pearls : 0L);
                case CurrencyId.PetEssence:
                    return Snapshot(definition, _pets != null ? _pets.PetEssence : 0L);
                default:
                    throw new ArgumentOutOfRangeException(nameof(definition));
            }
        }

        private static CurrencySnapshot Snapshot(CurrencyDefinition definition, BigDouble current)
            => new CurrencySnapshot(definition, current, 0L, 0L, 0d, 0L);

        private static CurrencySnapshot Snapshot(CurrencyDefinition definition, long wholeAmount)
            => new CurrencySnapshot(definition, new BigDouble(wholeAmount), wholeAmount, 0L, 0d, 0L);

        private CurrencySnapshot RegeneratingSnapshot(CurrencyDefinition definition, long wholeAmount,
                                                       long maximum, double secondsToNext)
        {
            if (maximum < 0L) maximum = 0L;
            if (secondsToNext < 0d) secondsToNext = 0d;
            long nextUnix = secondsToNext > 0d ? NowUnix() + (long)Math.Ceiling(secondsToNext) : 0L;
            return new CurrencySnapshot(definition, new BigDouble(wholeAmount), wholeAmount, maximum,
                                        secondsToNext, nextUnix);
        }

        private long NowUnix() => _time != null ? _time.NowUnix() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private void OnSourceChanged() => Changed?.Invoke();
    }
}
