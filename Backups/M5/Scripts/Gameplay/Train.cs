using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Shuttles its mine's ore to storage. Waits at the mine when empty and at storage when full, so
    /// both bottleneck directions are visible (GDD §2). Deposits under the mine's ore key.
    /// </summary>
    public sealed class Train : MonoBehaviour, IUpgradable
    {
        [SerializeField] private StationConfig config;
        [SerializeField] private MineStation mine;
        [SerializeField] private StorageYard storage;
        [SerializeField] private Transform minePoint;
        [SerializeField] private Transform storagePoint;
        [SerializeField] private float speed = 5f;
        [SerializeField] private GameObject loadVisual;

        public int Level { get; private set; }

        private enum State { AtMine, ToStorage, AtStorage, ToMine }
        private State _state = State.AtMine;
        private BigDouble _carrying;
        private double Capacity => config.CapacityAtLevel(Level);

        private void Update()
        {
            switch (_state)
            {
                case State.AtMine:
                    if (mine == null || mine.Output.IsEmpty) return;
                    _carrying = mine.Output.Remove(new BigDouble(Capacity));
                    SetLoad(true);
                    _state = State.ToStorage;
                    break;
                case State.ToStorage:
                    if (MoveTo(storagePoint.position)) _state = State.AtStorage;
                    break;
                case State.AtStorage:
                    if (storage != null && mine != null && mine.Ore != null)
                    {
                        BigDouble accepted = storage.Ore.Add(mine.Ore, _carrying);
                        _carrying -= accepted;
                    }
                    if (_carrying.Mantissa > 0d) return;
                    SetLoad(false);
                    _state = State.ToMine;
                    break;
                case State.ToMine:
                    if (MoveTo(minePoint.position)) _state = State.AtMine;
                    break;
            }
        }

        private bool MoveTo(Vector3 target) { transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime); return (transform.position - target).sqrMagnitude < 0.01f; }
        private void SetLoad(bool on) { if (loadVisual != null) loadVisual.SetActive(on); }

        public string Label => config.DisplayName;
        public BigDouble UpgradeCost(EconomyService economy) => economy.UpgradeCost(config.BaseUpgradeCost, Level);
        public void ApplyLevel(int level) => Level = level;
    }
}
