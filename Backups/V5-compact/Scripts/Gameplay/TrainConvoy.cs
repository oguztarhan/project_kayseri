using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// A train that drives INTO the mountain, waits while it loads, then emerges pulling a line of full ore
    /// wagons and delivers to storage (GDD §3). Wagons are independent followers that trail the engine along
    /// the rail — a real coupled train that articulates through the curves. Upgrade tracks: <b>Wagons</b>
    /// (count), <b>Speed</b>, and <b>Capacity</b> (ore per wagon). Wagons show ore when full, empty when dumped.
    /// </summary>
    public sealed class TrainConvoy : UpgradableStation
    {
        [SerializeField] private StationConfig config;
        [SerializeField] private MineStation mine;
        [SerializeField] private StorageYard storage;
        [SerializeField] private Transform insidePoint;    // deep inside the mountain (hidden)
        [SerializeField] private Transform bendPoint;       // curve on the run so the train visibly turns
        [SerializeField] private Transform storagePoint;    // where it dumps at storage
        [SerializeField] private GameObject wagonPrefab;
        [SerializeField] private GameObject wagonLoadPrefab;
        [SerializeField] private int startWagons = 3;
        [SerializeField] private int maxWagons = 8;         // cap the *visible* wagons so the train never gets absurdly long
        [SerializeField] private float wagonSpacing = 1.9f;
        [SerializeField] private float speed = 6f;          // base speed (Speed track scales it)
        [SerializeField] private float turnSpeed = 220f;
        [SerializeField] private float loadSeconds = 2.5f;

        private const int WagonsTrack = 0, SpeedTrack = 1, CapTrack = 2;
        private static readonly string[] TrackList = { "Wagons", "Speed", "Capacity" };

        private readonly List<Transform> _wagons = new List<Transform>();
        private readonly List<GameObject> _loads = new List<GameObject>();
        private BigDouble _carrying;
        private Vector3 _home;
        private float _timer;
        private float _speed;
        private int _leg;                                   // 0 = heading to the bend, 1 = past it

        private enum State { GoIn, Loading, GoOut, Unload, GoHome }
        private State _state = State.GoIn;

        protected override string StationLabel => config.DisplayName;
        protected override string[] TrackNames => TrackList;
        protected override double TrackBaseCost(int track) => config.BaseUpgradeCost;

        private int WagonCount => Mathf.Clamp(startWagons + TrackLevel(WagonsTrack), 1, maxWagons);   // visible wagons (capped)
        private double Capacity => config.CapacityAtLevel(TrackLevel(CapTrack)) * Mathf.Max(1, startWagons + TrackLevel(WagonsTrack));   // throughput uses the full count

        private void Awake() { _home = transform.position; OnUpgraded(); }

        protected override void OnUpgraded()
        {
            _speed = speed * (1f + 0.15f * TrackLevel(SpeedTrack));
            RebuildWagons();
        }

        private void RebuildWagons()
        {
            if (wagonPrefab == null || transform.parent == null) return;
            int want = WagonCount;
            while (_wagons.Count < want)
            {
                GameObject w = Instantiate(wagonPrefab, transform.parent);
                w.name = name + "_wagon" + _wagons.Count;
                w.transform.position = transform.position - transform.forward * (wagonSpacing * (_wagons.Count + 1));
                w.transform.rotation = transform.rotation;
                GameObject load = null;
                if (wagonLoadPrefab != null)
                {
                    load = Instantiate(wagonLoadPrefab, w.transform);
                    load.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                    load.transform.localScale = new Vector3(2.3f, 2.8f, 3.0f);   // fill the wagon bed with ore
                    load.SetActive(_carrying.Mantissa > 0d);
                }
                _wagons.Add(w.transform);
                _loads.Add(load);
            }
            while (_wagons.Count > want)
            {
                int last = _wagons.Count - 1;
                if (_wagons[last] != null) Destroy(_wagons[last].gameObject);
                _wagons.RemoveAt(last);
                _loads.RemoveAt(last);
            }
        }

        private void Update()
        {
            switch (_state)
            {
                case State.GoIn:
                    if (insidePoint != null && MoveEngine(insidePoint.position)) { _timer = loadSeconds; _state = State.Loading; }
                    break;
                case State.Loading:
                    _timer -= Time.deltaTime;
                    if (_timer <= 0f)
                    {
                        _carrying = mine != null ? mine.Output.Remove(new BigDouble(Capacity)) : BigDouble.Zero;
                        SetLoads(_carrying.Mantissa > 0d);
                        _state = State.GoOut;
                    }
                    break;
                case State.GoOut:
                {
                    if (storagePoint == null) break;
                    Vector3 target = (_leg == 0 && bendPoint != null) ? bendPoint.position : storagePoint.position;
                    if (MoveEngine(target))
                    {
                        if (_leg == 0 && bendPoint != null) _leg = 1;
                        else { _leg = 0; _state = State.Unload; }
                    }
                    break;
                }
                case State.Unload:
                    if (storage != null && mine != null && mine.Ore != null)
                    {
                        BigDouble accepted = storage.Ore.Add(mine.Ore, _carrying);
                        _carrying -= accepted;
                    }
                    if (_carrying.Mantissa > 0d) { UpdateChain(); return; } // storage full — wait
                    SetLoads(false);
                    _state = State.GoHome;
                    break;
                case State.GoHome:
                {
                    Vector3 target = (_leg == 0 && bendPoint != null) ? bendPoint.position : _home;
                    if (MoveEngine(target))
                    {
                        if (_leg == 0 && bendPoint != null) _leg = 1;
                        else { _leg = 0; _state = State.GoIn; }
                    }
                    break;
                }
            }
            UpdateChain();
        }

        private bool MoveEngine(Vector3 target)
        {
            Vector3 to = target - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.02f)
            {
                Quaternion look = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
            }
            transform.position = Vector3.MoveTowards(transform.position, target, _speed * Time.deltaTime);
            return (transform.position - target).sqrMagnitude < 0.03f;
        }

        // Each wagon rides exactly one spacing behind the car ahead, facing it — the chain bends through curves.
        private void UpdateChain()
        {
            for (int i = 0; i < _wagons.Count; i++)
            {
                Transform w = _wagons[i];
                if (w == null) continue;
                Vector3 leader = i == 0 ? transform.position : _wagons[i - 1].position;
                Vector3 d = leader - w.position; d.y = 0f;
                float dist = d.magnitude;
                if (dist > 0.0001f)
                {
                    Vector3 dir = d / dist;
                    w.position = leader - dir * wagonSpacing;
                    w.rotation = Quaternion.LookRotation(dir, Vector3.up);
                }
            }
        }

        private void SetLoads(bool on)
        {
            for (int i = 0; i < _loads.Count; i++) if (_loads[i] != null) _loads[i].SetActive(on);
        }
    }
}
