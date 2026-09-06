using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Thin Main-scene bridge for the Cannon slice. It keeps the portrait island's existing
    /// CoalOperation as the source of truth for Mine/Deposit/Refinery stock and exposes stable
    /// button-facing methods for the future world interaction layer.
    /// </summary>
    public sealed class CannonProductionWorldBridge : MonoBehaviour
    {
        [SerializeField] private CoalOperation operation;

        private CannonProductionService _production;
        private bool _bound;

        public CannonProductionService Production => _production;
        public bool IsBound => _bound;
        public bool IsRunning => _production != null && _production.IsRunning;
        public bool HasFinishedOutput => _production != null && _production.HasFinishedOutput;

        private void Awake()
        {
            if (!ShipyardFeatureSwitch.IsEnabled(ServiceLocator.Get<SaveData>()))
            {
                enabled = false;
                return;
            }
            if (operation == null) operation = GetComponent<CoalOperation>();
            TryBind();
        }

        private void Update()
        {
            if (!TryBind()) return;
            _production.PullMaterialsFromSource();
            _production.Poll();
        }

        public bool TryStartCannon() => _production != null && _production.TryStartCannon();
        public bool TryStart(string machineId, string recipeId)
            => _production != null && _production.TryStart(machineId, recipeId);
        public double PullMaterials() => _production != null ? _production.PullMaterialsFromSource() : 0d;
        public double PullMaterials(string machineId, string recipeId)
            => _production != null ? _production.PullMaterialsFromSource(machineId, recipeId) : 0d;
        public double MaterialQuantity(string resourceId)
            => _production != null ? _production.MaterialQuantity(resourceId) : 0d;
        public ShipyardFinishedItemState FinishedOutputAt(int index)
            => _production != null ? _production.FinishedOutputAt(index) : null;
        public ShipyardFinishedItemState FinishedOutputAt(string machineId, int index)
            => _production != null ? _production.FinishedOutputAt(machineId, index) : null;
        public bool SellOutput(string itemId) => _production != null && _production.SellOutput(itemId);
        public bool SellOutput(string machineId, string itemId)
            => _production != null && _production.SellOutput(machineId, itemId);
        public bool EquipOutput(string itemId) => _production != null && _production.EquipOutput(itemId);
        public bool EquipOutput(string machineId, string itemId)
            => _production != null && _production.EquipOutput(machineId, itemId);
        public bool StoreOutput(string itemId) => _production != null && _production.StoreOutput(itemId);
        public bool StoreOutput(string machineId, string itemId)
            => _production != null && _production.StoreOutput(machineId, itemId);
        public long SalvageOutput(string itemId) => _production != null ? _production.SalvageOutput(itemId) : 0L;
        public long SalvageOutput(string machineId, string itemId)
            => _production != null ? _production.SalvageOutput(machineId, itemId) : 0L;
        public bool FulfillActiveCannonOrder(string itemId)
            => _production != null && _production.FulfillActiveCannonOrder(itemId);
        public bool FulfillOrder(string machineId, string itemId)
            => _production != null && _production.FulfillOrder(machineId, itemId);

        private bool TryBind()
        {
            if (_bound) return true;
            if (!ServiceLocator.TryGet(out _production) || _production == null || operation == null)
                return false;

            _production.BindMaterialSource(operation.ShipyardMaterialAvailable,
                                           operation.TakeShipyardMaterial);
            _bound = true;
            return true;
        }
    }
}
