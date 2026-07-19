using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Shuttles ore mine → storage. Waits at the mine when it's empty and at storage when it's full,
    /// so both directions of bottleneck are visible (GDD §2). Movement is frame-based (presentation);
    /// the ore transfer is deterministic on arrival. Carry capacity scales with level.
    /// </summary>
    public sealed class Train : MonoBehaviour, IUpgradable
    {
        [SerializeField] private StationConfig config;
        [SerializeField] private MineStation mine;
        [SerializeField] private StorageYard storage;
        [SerializeField] private Transform minePoint;
        [SerializeField] private Transform storagePoint;
        [SerializeField] private float speed = 5f;
        [SerializeField] private GameObject loadVisual; // shown while carrying

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
                    if (mine == null || mine.Output.IsEmpty) return; // wait for ore (mine bottleneck)
                    _carrying = mine.Output.Remove(new BigDouble(Capacity));
                    SetLoadVisual(true);
                    _state = State.ToStorage;
                    break;

                case State.ToStorage:
                    if (MoveTo(storagePoint.position)) _state = State.AtStorage;
                    break;

                case State.AtStorage:
                    if (storage != null)
                    {
                        BigDouble accepted = storage.Buffer.Add(_carrying);
                        _carrying -= accepted;
                    }
                    if (_carrying.Mantissa > 0d) return; // storage full, wait (storage bottleneck)
                    SetLoadVisual(false);
                    _state = State.ToMine;
                    break;

                case State.ToMine:
                    if (MoveTo(minePoint.position)) _state = State.AtMine;
                    break;
            }
        }

        private bool MoveTo(Vector3 target)
        {
            transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);
            return (transform.position - target).sqrMagnitude < 0.01f;
        }

        private void SetLoadVisual(bool on)
        {
            if (loadVisual != null) loadVisual.SetActive(on);
        }

        public string Label => config.DisplayName;

        public BigDouble UpgradeCost(EconomyService economy) => economy.UpgradeCost(config.BaseUpgradeCost, Level);

        public void ApplyLevel(int level) => Level = level;
    }
}
