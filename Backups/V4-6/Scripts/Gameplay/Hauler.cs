using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Base truck: hauls resources source → dest along an optional bendy path, turning to face its
    /// direction of travel and accelerating/braking like a real vehicle. Shows a visible cargo prop while
    /// carrying. Upgrade tracks: <b>Speed</b> and <b>Capacity</b> (GDD §3). Subclasses bind inventories.
    /// </summary>
    public abstract class Hauler : UpgradableStation
    {
        [SerializeField] protected StationConfig config;
        [SerializeField] private Transform sourcePoint;
        [SerializeField] private Transform destPoint;
        [SerializeField] private Transform[] toDestPath;   // optional bend waypoints between source and dest
        [SerializeField] private float speed = 9f;         // base speed (Speed track scales it)
        [SerializeField] private float turnSpeed = 170f;
        [SerializeField] private float accel = 12f;
        [SerializeField] private GameObject loadVisual;    // visible cargo (ore / crates) while carrying

        private const int SpeedTrack = 0, CapTrack = 1;
        private static readonly string[] TrackList = { "Speed", "Capacity" };

        protected readonly Inventory<ResourceDef> Cargo = new Inventory<ResourceDef>(BigDouble.Zero);

        private enum State { AtSource, ToDest, AtDest, ToSource }
        private State _state = State.AtSource;
        private int _wp;
        private float _cur;
        private float _speed;
        private double _cap;

        protected override string StationLabel => config != null ? config.DisplayName : name;
        protected override string[] TrackNames => TrackList;
        protected override double TrackBaseCost(int track) => config.BaseUpgradeCost;

        protected virtual double LoadBonus => 1d;             // storage "Loading" upgrade hooks in here
        private double Capacity => _cap * LoadBonus;

        protected abstract Inventory<ResourceDef> Source { get; }
        protected abstract Inventory<ResourceDef> Dest { get; }

        private void Awake() => OnUpgraded();

        protected override void OnUpgraded()
        {
            _speed = speed * (1f + 0.15f * TrackLevel(SpeedTrack));
            _cap = config != null ? config.CapacityAtLevel(TrackLevel(CapTrack)) : 10d;
        }

        private void Update()
        {
            Inventory<ResourceDef> src = Source, dst = Dest;
            if (src == null || dst == null) return;
            if (loadVisual != null) loadVisual.SetActive(Cargo.Total.Mantissa > 0d);

            switch (_state)
            {
                case State.AtSource:
                    if (src.Total.Mantissa <= 0d) { Brake(); return; }
                    double cap = Capacity;
                    Cargo.Capacity = new BigDouble(cap);
                    if (Inventories.Transfer(src, Cargo, new BigDouble(cap)).Mantissa <= 0d) { Brake(); return; }
                    _wp = 0; _state = State.ToDest;
                    break;
                case State.ToDest:
                    if (Follow(true)) _state = State.AtDest;
                    break;
                case State.AtDest:
                    Inventories.Transfer(Cargo, dst, Cargo.Total);
                    if (Cargo.Total.Mantissa > 0d) { Brake(); return; }
                    _wp = 0; _state = State.ToSource;
                    break;
                case State.ToSource:
                    if (Follow(false)) _state = State.AtSource;
                    break;
            }
        }

        private bool Follow(bool forward)
        {
            Vector3 target = Waypoint(forward, _wp, out bool last);
            if (MoveTo(target, last))
            {
                if (last) return true;
                _wp++;
            }
            return false;
        }

        private Vector3 Waypoint(bool forward, int wp, out bool last)
        {
            int n = toDestPath != null ? toDestPath.Length : 0;
            if (forward)
            {
                if (wp < n && toDestPath[wp] != null) { last = false; return toDestPath[wp].position; }
                last = true; return destPoint != null ? destPoint.position : transform.position;
            }
            if (wp < n && toDestPath[n - 1 - wp] != null) { last = false; return toDestPath[n - 1 - wp].position; }
            last = true; return sourcePoint != null ? sourcePoint.position : transform.position;
        }

        private bool MoveTo(Vector3 target, bool isFinal)
        {
            Vector3 to = target - transform.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist > 0.05f)
            {
                Quaternion look = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
            }
            float targetSpeed = (isFinal && dist < 3f) ? Mathf.Lerp(1.5f, _speed, dist / 3f) : _speed;
            _cur = Mathf.MoveTowards(_cur, targetSpeed, accel * Time.deltaTime);
            transform.position = Vector3.MoveTowards(transform.position, target, _cur * Time.deltaTime);
            return (transform.position - target).sqrMagnitude < 0.05f;
        }

        private void Brake() => _cur = Mathf.MoveTowards(_cur, 0f, accel * 2f * Time.deltaTime);
    }
}
