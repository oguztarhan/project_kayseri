using System.Collections.Generic;
using Game.Core;
using Game.Data;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// A haul route with a POOL of trucks (GDD §3 fleet size — the anti-jam mechanic). Upgrade tracks:
    /// <b>Trucks</b> (activate another pooled truck → more parallel throughput), <b>Speed</b>, <b>Capacity</b>,
    /// and <b>Roads</b> (spread the fleet across parallel lanes so they don't queue on one path). Trucks are
    /// pre-instantiated once and toggled — no runtime Instantiate/Destroy (GDD §14.5).
    /// </summary>
    public sealed class TruckFleet : UpgradableStation
    {
        public enum Mode { OreToRefinery, ProductToMarket }

        [SerializeField] private StationConfig config;
        [SerializeField] private Mode mode;
        [SerializeField] private GameObject truckModel;
        [SerializeField] private GameObject loadModel;
        [SerializeField] private Vector3 loadLocalPos = new Vector3(0f, 0.6f, -0.2f);
        [SerializeField] private Vector3 loadScale = Vector3.one;
        [SerializeField] private Transform sourcePoint;
        [SerializeField] private Transform destPoint;
        [SerializeField] private Transform[] path;
        [SerializeField] private StorageYard storage;
        [SerializeField] private Refinery refinery;
        [SerializeField] private Market market;
        [SerializeField] private int maxTrucks = 8;
        [SerializeField] private float baseSpeed = 9f;
        [SerializeField] private float turnSpeed = 170f;
        [SerializeField] private float accel = 14f;
        [SerializeField] private float laneSpacing = 2.4f;

        private const int TrucksT = 0, SpeedT = 1, CapT = 2, RoadsT = 3;
        private static readonly string[] Names = { "Trucks", "Speed", "Capacity", "Roads" };

        private List<FleetTruck> _pool;

        protected override string StationLabel => config != null ? config.DisplayName : name;
        protected override string[] TrackNames => Names;
        // fleet-size & new-road unlocks cost more than the flat speed/capacity tracks
        protected override double TrackBaseCost(int t) => config.BaseUpgradeCost * (t == TrucksT ? 5d : t == RoadsT ? 15d : 1d);

        public Inventory<ResourceDef> Source => mode == Mode.OreToRefinery ? (storage != null ? storage.Ore : null) : (refinery != null ? refinery.Output : null);
        public Inventory<ResourceDef> Dest => mode == Mode.OreToRefinery ? (refinery != null ? refinery.Input : null) : (market != null ? market.Products : null);
        public float Speed => baseSpeed * (1f + 0.15f * TrackLevel(SpeedT));
        public double Capacity
        {
            get
            {
                double c = config != null ? config.CapacityAtLevel(TrackLevel(CapT)) : 10d;
                if (mode == Mode.OreToRefinery && storage != null) c *= storage.LoadMultiplier;
                return c;
            }
        }
        public float TurnSpeed => turnSpeed;
        public float Accel => accel;
        public Transform SourcePoint => sourcePoint;
        public Transform DestPoint => destPoint;
        public Transform[] Path => path;

        private int ActiveTrucks => Mathf.Clamp(1 + TrackLevel(TrucksT), 1, maxTrucks);
        private int LaneCount => Mathf.Max(1, 1 + TrackLevel(RoadsT));

        private void Awake() { BuildPool(); OnUpgraded(); }

        private void BuildPool()
        {
            _pool = new List<FleetTruck>(maxTrucks);
            if (truckModel == null || transform.parent == null) return;
            Vector3 start = sourcePoint != null ? sourcePoint.position : transform.position;
            for (int i = 0; i < maxTrucks; i++)
            {
                GameObject t = Instantiate(truckModel, transform.parent);
                t.name = name + "_truck" + i;
                t.transform.position = start;
                FleetTruck ft = t.GetComponent<FleetTruck>();
                if (ft == null) ft = t.AddComponent<FleetTruck>();
                if (t.GetComponent<DustTrail>() == null) t.AddComponent<DustTrail>();
                GameObject load = null;
                if (loadModel != null)
                {
                    load = Instantiate(loadModel, t.transform);
                    load.name = "Load";
                    load.transform.localPosition = loadLocalPos;
                    load.transform.localScale = loadScale;
                    load.SetActive(false);
                }
                ft.Init(this, load);
                t.SetActive(false);
                _pool.Add(ft);
            }
        }

        protected override void OnUpgraded()
        {
            if (_pool == null) return;
            int active = ActiveTrucks, lanes = LaneCount;
            Vector3 dir = (destPoint != null && sourcePoint != null) ? (destPoint.position - sourcePoint.position) : Vector3.forward;
            dir.y = 0f; if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward; dir.Normalize();
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
            Vector3 start = sourcePoint != null ? sourcePoint.position : transform.position;
            for (int i = 0; i < _pool.Count; i++)
            {
                FleetTruck ft = _pool[i];
                bool on = i < active;
                if (on)
                {
                    int lane = i % lanes;
                    Vector3 offset = perp * (laneSpacing * (lane - (lanes - 1) * 0.5f));
                    ft.SetLane(offset);
                    if (!ft.gameObject.activeSelf)
                    {
                        ft.transform.position = start + offset + dir * (i * 1.5f);
                        ft.gameObject.SetActive(true);
                    }
                }
                else if (ft.gameObject.activeSelf) ft.gameObject.SetActive(false);
            }
        }
    }
}
