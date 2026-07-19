using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Base truck: shuttles resources from a source inventory to a dest inventory, carrying up to
    /// capacity. Waits at the source when empty and at the dest when full, surfacing both bottleneck
    /// directions (GDD §2). Subclasses bind the specific inventories (ore truck vs cargo truck).
    /// </summary>
    public abstract class Hauler : MonoBehaviour, IUpgradable
    {
        [SerializeField] protected StationConfig config;
        [SerializeField] private Transform sourcePoint;
        [SerializeField] private Transform destPoint;
        [SerializeField] private float speed = 5f;
        [SerializeField] private GameObject loadVisual;

        public int Level { get; private set; }
        protected readonly Inventory<ResourceDef> Cargo = new Inventory<ResourceDef>(BigDouble.Zero);

        private enum State { AtSource, ToDest, AtDest, ToSource }
        private State _state = State.AtSource;
        protected double Capacity => config != null ? config.CapacityAtLevel(Level) : 10d;

        protected abstract Inventory<ResourceDef> Source { get; }
        protected abstract Inventory<ResourceDef> Dest { get; }

        private void Update()
        {
            Inventory<ResourceDef> src = Source, dst = Dest;
            if (src == null || dst == null) return;

            switch (_state)
            {
                case State.AtSource:
                    if (src.Total.Mantissa <= 0d) return; // nothing to haul (source bottleneck)
                    Cargo.Capacity = new BigDouble(Capacity);
                    if (Inventories.Transfer(src, Cargo, new BigDouble(Capacity)).Mantissa <= 0d) return;
                    SetLoad(true);
                    _state = State.ToDest;
                    break;
                case State.ToDest:
                    if (MoveTo(destPoint.position)) _state = State.AtDest;
                    break;
                case State.AtDest:
                    Inventories.Transfer(Cargo, dst, Cargo.Total);
                    if (Cargo.Total.Mantissa > 0d) return; // dest full (dest bottleneck)
                    SetLoad(false);
                    _state = State.ToSource;
                    break;
                case State.ToSource:
                    if (MoveTo(sourcePoint.position)) _state = State.AtSource;
                    break;
            }
        }

        private bool MoveTo(Vector3 target) { transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime); return (transform.position - target).sqrMagnitude < 0.01f; }
        private void SetLoad(bool on) { if (loadVisual != null) loadVisual.SetActive(on); }

        public string Label => config != null ? config.DisplayName : name;
        public BigDouble UpgradeCost(EconomyService economy) => economy.UpgradeCost(config.BaseUpgradeCost, Level);
        public void ApplyLevel(int level) => Level = level;
    }
}
