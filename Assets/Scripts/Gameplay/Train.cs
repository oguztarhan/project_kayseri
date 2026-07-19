using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Simple M1 shuttle (superseded by <see cref="TrainConvoy"/>): carries a mine's ore to storage. Kept
    /// for the greybox scene. Upgrade tracks: <b>Speed</b> and <b>Capacity</b>.
    /// </summary>
    public sealed class Train : UpgradableStation
    {
        [SerializeField] private StationConfig config;
        [SerializeField] private MineStation mine;
        [SerializeField] private StorageYard storage;
        [SerializeField] private Transform minePoint;
        [SerializeField] private Transform storagePoint;
        [SerializeField] private float speed = 5f;
        [SerializeField] private GameObject loadVisual;

        private const int SpeedTrack = 0, CapTrack = 1;
        private static readonly string[] TrackList = { "Speed", "Capacity" };

        private enum State { AtMine, ToStorage, AtStorage, ToMine }
        private State _state = State.AtMine;
        private BigDouble _carrying;
        private float _speed;

        protected override string StationLabel => config.DisplayName;
        protected override string[] TrackNames => TrackList;
        protected override double TrackBaseCost(int track) => config.BaseUpgradeCost;

        private double Capacity => config.CapacityAtLevel(TrackLevel(CapTrack));

        private void Awake() => OnUpgraded();
        protected override void OnUpgraded() => _speed = speed * (1f + 0.15f * TrackLevel(SpeedTrack));

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

        private bool MoveTo(Vector3 target) { transform.position = Vector3.MoveTowards(transform.position, target, _speed * Time.deltaTime); return (transform.position - target).sqrMagnitude < 0.01f; }
        private void SetLoad(bool on) { if (loadVisual != null) loadVisual.SetActive(on); }
    }
}
