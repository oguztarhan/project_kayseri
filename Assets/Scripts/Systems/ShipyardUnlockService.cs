using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Persistent unlock ladder for ship-item machine families. Unlocking is deliberately a
    /// construction job, not an instant cash purchase: orders and reputation prove the player is
    /// ready, art readiness is checked before any currency leaves the wallet, and completion is
    /// settled from wall-clock timestamps after a restart.
    /// </summary>
    public sealed class ShipyardUnlockService
    {
        public struct UnlockRule
        {
            public string MachineId;
            public int RequiredOrders;
            public int RequiredReputation;
            public double ConstructionCost;
            public long ConstructionSeconds;
        }

        private static readonly UnlockRule[] Rules =
        {
            new UnlockRule { MachineId = ShipyardProgression.CannonMachineId, RequiredOrders = 0, RequiredReputation = 0, ConstructionCost = 0d, ConstructionSeconds = 0L },
            new UnlockRule { MachineId = ShipyardProgression.HullMachineId, RequiredOrders = 1, RequiredReputation = 1, ConstructionCost = 300d, ConstructionSeconds = 5L },
            new UnlockRule { MachineId = ShipyardProgression.RiggingMachineId, RequiredOrders = 3, RequiredReputation = 3, ConstructionCost = 750d, ConstructionSeconds = 8L },
            new UnlockRule { MachineId = ShipyardProgression.NavigationMachineId, RequiredOrders = 6, RequiredReputation = 6, ConstructionCost = 1500d, ConstructionSeconds = 10L },
            new UnlockRule { MachineId = ShipyardProgression.FigureheadMachineId, RequiredOrders = 10, RequiredReputation = 10, ConstructionCost = 3000d, ConstructionSeconds = 15L }
        };

        private readonly SaveData _data;
        private readonly SaveService _save;
        private readonly TimeService _time;
        private readonly WalletService _wallet;
        private readonly Func<string, bool> _artReady;
        private readonly Func<long> _now;
        private string _focusTarget = "";

        public event Action<string> FocusRequested;

        public ShipyardUnlockService(SaveData data, SaveService save, TimeService time,
                                     WalletService wallet,
                                     Func<string, bool> artReady = null,
                                     Func<long> now = null)
        {
            _data = data ?? new SaveData();
            _save = save;
            _time = time;
            _wallet = wallet;
            _artReady = artReady;
            _now = now;
            _data.shipyard.Normalize();
            Poll();
        }

        public ShipyardProgression Progress => _data.shipyard;
        public string NextMachine => _data.shipyard.NextMachine;
        public string FocusTargetMachineId => _focusTarget;

        public ShipyardMachineState CurrentConstruction
        {
            get
            {
                var machines = _data.shipyard.machines;
                for (int i = 0; i < machines.Count; i++)
                    if (machines[i] != null && machines[i].constructionState == ShipyardMachineState.Constructing)
                        return machines[i];
                return null;
            }
        }

        public double ConstructionSecondsLeft
        {
            get
            {
                ShipyardMachineState machine = CurrentConstruction;
                if (machine == null) return 0d;
                long left = machine.constructionFinishUnix - NowUnix();
                return left > 0L ? left : 0d;
            }
        }

        public UnlockRule RuleFor(string machineId)
        {
            for (int i = 0; i < Rules.Length; i++)
                if (Rules[i].MachineId == machineId) return Rules[i];
            return default;
        }

        public bool TryBeginNextConstruction()
        {
            Poll();
            if (CurrentConstruction != null) return false;
            string id = NextMachine;
            if (string.IsNullOrEmpty(id)) return false;
            UnlockRule rule = RuleFor(id);
            if (_data.shipyard.completedOrders < rule.RequiredOrders
                || _data.shipyard.reputation < rule.RequiredReputation
                || !IsArtReady(id)) return false;
            if (_wallet == null || !_wallet.TrySpendCash(new BigDouble(Math.Max(0d, rule.ConstructionCost))))
                return false;

            long started = NowUnix();
            ShipyardMachineState machine = FindMachine(id);
            if (machine == null)
            {
                machine = new ShipyardMachineState { machineId = id };
                _data.shipyard.machines.Add(machine);
            }
            if (!_data.shipyard.unlockedStations.Contains(id)) _data.shipyard.unlockedStations.Add(id);
            machine.constructionState = ShipyardMachineState.Constructing;
            machine.constructionStartedUnix = started;
            machine.constructionFinishUnix = started + Math.Max(1L, rule.ConstructionSeconds);
            machine.activeRecipeId = "";
            Commit();
            return true;
        }

        public bool Poll()
        {
            ShipyardMachineState machine = CurrentConstruction;
            if (machine == null || NowUnix() < machine.constructionFinishUnix) return false;

            string id = machine.machineId;
            machine.constructionState = ShipyardMachineState.Built;
            machine.constructionStartedUnix = 0L;
            machine.constructionFinishUnix = 0L;
            _focusTarget = id;
            Commit();
            FocusRequested?.Invoke(id);
            return true;
        }

        public bool TryConsumeFocusTarget(out string machineId)
        {
            machineId = _focusTarget;
            if (string.IsNullOrEmpty(machineId)) return false;
            _focusTarget = "";
            return true;
        }

        public static bool DefaultArtReady(string machineId)
            => machineId != ShipyardProgression.FigureheadMachineId;

        private bool IsArtReady(string machineId)
            => _artReady != null ? _artReady(machineId) : DefaultArtReady(machineId);

        private ShipyardMachineState FindMachine(string id)
        {
            var machines = _data.shipyard.machines;
            for (int i = 0; i < machines.Count; i++)
                if (machines[i] != null && machines[i].machineId == id) return machines[i];
            return null;
        }

        private long NowUnix() => _now != null ? _now() : (_time != null ? _time.NowUnix() : DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        private void Commit() => _save?.Save(_data);
    }
}
