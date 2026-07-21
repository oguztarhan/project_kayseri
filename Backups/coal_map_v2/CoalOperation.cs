using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Drives the automated production cycle on the player-built <b>Coal</b> map. It reads the map's own
    /// labelled landmark objects <b>by name</b> and never moves the static layout — only the vehicles and
    /// the ore/bar piles animate. The cycle exactly follows the design:
    /// <list type="bullet">
    /// <item>A train shuttles between the coal mountain and the storage shed. It hides <i>inside</i> the
    /// mountain to load (emerges with full wagons), hauls to storage, hides <i>inside</i> the shed to drop
    /// its ore onto the storage pile, then returns empty.</item>
    /// <item>An ore truck drives to the storage pile (loads), hauls to the smelter (unloads); the smelter
    /// turns ore into bars.</item>
    /// <item>A cargo truck drives to the refined pile (loads bars), hauls to the market (unloads) which
    /// sells them for cash.</item>
    /// </list>
    /// Self-contained: cash lands in <see cref="WalletService"/>. The whole cycle is frame-driven through a
    /// private <see cref="Tick"/> fed Time.deltaTime, so it can also be stepped deterministically in tests.
    /// </summary>
    public sealed class CoalOperation : MonoBehaviour
    {
        [Header("Tuning (level-1 base rates)")]
        [SerializeField] private float trainSpeed = 18f;
        [SerializeField] private float truckSpeed = 20f;
        [SerializeField] private float trainOrePerTrip = 12f;
        [SerializeField] private float oreTruckCapacity = 6f;
        [SerializeField] private float cargoTruckCapacity = 4f;
        [SerializeField] private float smeltPerSecond = 3f;
        [SerializeField] private float barPrice = 45f;
        [SerializeField] private float dwellSeconds = 0.7f;   // pause hidden inside the mountain / shed
        [SerializeField] private float wagonGap = 2.2f;
        [SerializeField] private string islandRootName = "Island_Coal";

        // ---- landmarks (found by name under the island root) ----
        private Transform _mountain, _storage, _orePile, _refinery, _refinedPile, _market;
        private Transform _train; private Transform[] _wagons; private GameObject[] _wagonOre;
        private Transform _oreTruck, _cargoTruck; private GameObject _oreTruckLoad, _cargoTruckLoad;
        private Transform _oreHeap, _barHeap; private float _oreHeapY0, _barHeapY0;

        // ---- economy ----
        private double _storeOre, _refOre, _bars;
        private WalletService _wallet;
        private Material _oreMat, _barMat;

        // ---- upgrades (per-track levels, persisted in SaveData under "coal#<t>") ----
        private static readonly string[] TrackNames = { "Mountain", "Train", "Ore Truck", "Smelter", "Cargo Truck", "Storage", "Market" };
        private static readonly double[] BaseCost = { 60d, 80d, 70d, 120d, 90d, 100d, 150d };
        private readonly int[] _lv = new int[7];
        private SaveData _data;

        // ---- vehicle Y (kept flat; only XZ is driven) ----
        private float _trainY, _oreTruckY, _cargoTruckY; private float[] _wagonY;

        // ---- state machines ----
        private enum TR { LoadMountain, Haul, Deposit, Return }
        private TR _tr; private float _trTimer;
        private bool _oreToDrop; private double _oreCarry;
        private bool _cargoToDrop; private double _barCarry;
        private bool _ready;

        // exposed for the HUD
        public double StorageOre => _storeOre;
        public double Bars => _bars;
        public int TrackCount => TrackNames.Length;
        public string TrackName(int t) => TrackNames[t];
        public int Level(int t) => _lv[t];
        public BigDouble Cost(int t) => new BigDouble(BaseCost[t] * System.Math.Pow(1.6, _lv[t]));

        /// <summary>Buy one level on a track: spends cash, bumps the level (which scales the cycle), persists.</summary>
        public bool TryUpgrade(int t)
        {
            if (t < 0 || t >= TrackNames.Length || _wallet == null) return false;
            if (!_wallet.TrySpendCash(Cost(t))) return false;
            _lv[t]++; SaveLevel(t); return true;
        }

        // effective rates = base * per-track level bonus
        private float EffTrainOre => trainOrePerTrip * (1f + 0.30f * _lv[0]);
        private float EffTrainSpeed => trainSpeed * (1f + 0.15f * _lv[1]);
        private float EffOreCap => oreTruckCapacity * (1f + 0.30f * _lv[2]);
        private float EffSmelt => smeltPerSecond * (1f + 0.30f * _lv[3]);
        private float EffCargoCap => cargoTruckCapacity * (1f + 0.30f * _lv[4]);
        private float EffStorageFull => 60f * (1f + 0.5f * _lv[5]);
        private float EffBarPrice => barPrice * (1f + 0.40f * _lv[6]);

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _data = ServiceLocator.Get<SaveData>();
            LoadLevels();
            var root = GameObject.Find(islandRootName);
            if (root == null) { Debug.LogWarning("CoalOperation: '" + islandRootName + "' not found — disabled."); enabled = false; return; }
            var rt = root.transform;

            _mountain = Child(rt, "mine_Coal");
            _storage = Child(rt, "storage");
            _orePile = Child(rt, "storage ore pile here");
            _refinery = Child(rt, "refinery");
            _refinedPile = Child(rt, "refined ores pile here");
            _market = Child(rt, "market");
            _train = Child(rt, "train");
            _oreTruck = Child(rt, "truck_road.003");
            _cargoTruck = Child(rt, "truck_road.005");

            var w = new System.Collections.Generic.List<Transform>();
            var w0 = Child(rt, "wagon"); if (w0 != null) w.Add(w0);
            var w1 = Child(rt, "wagon.001"); if (w1 != null) w.Add(w1);
            var w2 = Child(rt, "wagon.002"); if (w2 != null) w.Add(w2);
            _wagons = w.ToArray();

            if (_mountain == null || _storage == null || _train == null || _orePile == null ||
                _refinery == null || _refinedPile == null || _market == null)
            { Debug.LogWarning("CoalOperation: missing a core landmark — disabled."); enabled = false; return; }

            _trainY = _train.position.y;
            _wagonY = new float[_wagons.Length];
            for (int i = 0; i < _wagons.Length; i++) _wagonY[i] = _wagons[i].position.y;
            if (_oreTruck != null) _oreTruckY = _oreTruck.position.y;
            if (_cargoTruck != null) _cargoTruckY = _cargoTruck.position.y;

            // ore/bar materials, cloned from a vehicle so they stay URP-correct
            Renderer refRend = _wagons.Length > 0 ? _wagons[0].GetComponentInChildren<Renderer>() : _train.GetComponentInChildren<Renderer>();
            Material src = refRend != null ? refRend.sharedMaterial : null;
            _oreMat = MakeMat(src, new Color(0.10f, 0.10f, 0.12f));   // coal ore (near-black)
            _barMat = MakeMat(src, new Color(0.88f, 0.55f, 0.18f));   // metal bars (amber)

            _wagonOre = new GameObject[_wagons.Length];
            for (int i = 0; i < _wagons.Length; i++)
                _wagonOre[i] = MakeChunk(_wagons[i], _oreMat, new Vector3(0f, 0.9f, 0f), new Vector3(1.6f, 0.8f, 2.0f));
            if (_oreTruck != null) _oreTruckLoad = MakeChunk(_oreTruck, _oreMat, new Vector3(0f, 1.0f, 0f), new Vector3(1.4f, 0.8f, 2.0f));
            if (_cargoTruck != null) _cargoTruckLoad = MakeChunk(_cargoTruck, _barMat, new Vector3(0f, 1.0f, 0f), new Vector3(1.4f, 0.8f, 2.0f));

            _oreHeap = MakeHeap(_orePile, _oreMat, out _oreHeapY0);
            _barHeap = MakeHeap(_refinedPile, _barMat, out _barHeapY0);

            SetWagonOre(false);
            Show(_oreTruckLoad, false); Show(_cargoTruckLoad, false);
            HideTrain(true);
            _tr = TR.LoadMountain; _trTimer = dwellSeconds;   // start hidden inside the mountain, loading
            _ready = true;
        }

        private void Update() { if (_ready) Tick(Time.deltaTime); }

        private void Tick(float dt)
        {
            if (dt <= 0f) return;
            TrainCycle(dt);
            OreTruckCycle(dt);
            Smelt(dt);
            CargoTruckCycle(dt);
            UpdateHeaps();
        }

        // ---------------- train ----------------
        private void TrainCycle(float dt)
        {
            switch (_tr)
            {
                case TR.LoadMountain:
                    _trTimer -= dt;
                    if (_trTimer <= 0f) { ShowTrainAt(_mountain.position, _storage.position); SetWagonOre(true); _tr = TR.Haul; }
                    break;
                case TR.Haul:
                    if (MoveConsist(_storage.position, dt)) { _storeOre += EffTrainOre; HideTrain(true); _trTimer = dwellSeconds; _tr = TR.Deposit; }
                    break;
                case TR.Deposit:
                    _trTimer -= dt;
                    if (_trTimer <= 0f) { ShowTrainAt(_storage.position, _mountain.position); SetWagonOre(false); _tr = TR.Return; }
                    break;
                case TR.Return:
                    if (MoveConsist(_mountain.position, dt)) { HideTrain(true); _trTimer = dwellSeconds; _tr = TR.LoadMountain; }
                    break;
            }
        }

        private bool MoveConsist(Vector3 target, float dt)
        {
            Vector3 p = _train.position; Vector3 to = new Vector3(target.x, _trainY, target.z);
            Vector3 d = to - p; d.y = 0f; float dist = d.magnitude;
            Vector3 dir = dist > 1e-4f ? d / dist : _train.forward;
            float sp = EffTrainSpeed;
            bool arrived;
            if (dist <= sp * dt) { _train.position = to; arrived = true; }
            else { _train.position = p + dir * (sp * dt); arrived = false; }
            _train.rotation = Quaternion.LookRotation(dir, Vector3.up);
            PlaceWagons(dir);
            return arrived;
        }

        private void PlaceWagons(Vector3 dir)
        {
            for (int i = 0; i < _wagons.Length; i++)
            {
                Vector3 wp = _train.position - dir * (wagonGap * (i + 1));
                wp.y = _wagonY[i];
                _wagons[i].position = wp;
                _wagons[i].rotation = _train.rotation;
            }
        }

        private void ShowTrainAt(Vector3 pos, Vector3 towards)
        {
            Vector3 d = towards - pos; d.y = 0f; if (d.sqrMagnitude < 1e-4f) d = _train.forward; d.Normalize();
            HideTrain(false);
            _train.position = new Vector3(pos.x, _trainY, pos.z);
            _train.rotation = Quaternion.LookRotation(d, Vector3.up);
            PlaceWagons(d);
        }

        private void HideTrain(bool hide)
        {
            _train.gameObject.SetActive(!hide);
            for (int i = 0; i < _wagons.Length; i++) _wagons[i].gameObject.SetActive(!hide);
        }

        private void SetWagonOre(bool on) { for (int i = 0; i < _wagonOre.Length; i++) Show(_wagonOre[i], on); }

        // ---------------- trucks ----------------
        private void OreTruckCycle(float dt)
        {
            if (_oreTruck == null) return;
            Vector3 target = _oreToDrop ? _refinery.position : _orePile.position;
            if (!MoveOnRoad(_oreTruck, _oreTruckY, target, dt)) return;
            if (_oreToDrop) { _refOre += _oreCarry; _oreCarry = 0d; Show(_oreTruckLoad, false); _oreToDrop = false; }
            else { double take = System.Math.Min(EffOreCap, _storeOre); _storeOre -= take; _oreCarry = take; Show(_oreTruckLoad, take > 0.01d); _oreToDrop = true; }
        }

        private void CargoTruckCycle(float dt)
        {
            if (_cargoTruck == null) return;
            Vector3 target = _cargoToDrop ? _market.position : _refinedPile.position;
            if (!MoveOnRoad(_cargoTruck, _cargoTruckY, target, dt)) return;
            if (_cargoToDrop) { if (_barCarry > 0.001d && _wallet != null) _wallet.AddCash(new BigDouble(_barCarry * EffBarPrice)); _barCarry = 0d; Show(_cargoTruckLoad, false); _cargoToDrop = false; }
            else { double take = System.Math.Min(EffCargoCap, _bars); _bars -= take; _barCarry = take; Show(_cargoTruckLoad, take > 0.01d); _cargoToDrop = true; }
        }

        private bool MoveOnRoad(Transform t, float y, Vector3 target, float dt)
        {
            Vector3 p = t.position; Vector3 to = new Vector3(target.x, y, target.z);
            Vector3 d = to - p; d.y = 0f; float dist = d.magnitude;
            if (dist <= truckSpeed * dt) { t.position = to; return true; }
            Vector3 dir = d / dist;
            t.position = p + dir * (truckSpeed * dt);
            t.rotation = Quaternion.LookRotation(dir, Vector3.up);
            return false;
        }

        private void Smelt(float dt)
        {
            if (_refOre <= 0d) return;
            double amt = System.Math.Min(_refOre, smeltPerSecond * dt);
            _refOre -= amt; _bars += amt;
        }

        // ---------------- pile visuals ----------------
        private void UpdateHeaps()
        {
            ScaleHeap(_oreHeap, _oreHeapY0, _storeOre, EffStorageFull);
            ScaleHeap(_barHeap, _barHeapY0, _bars, 40d);
        }

        // ---- upgrade persistence ----
        private void LoadLevels()
        {
            if (_data == null || _data.islandLevels == null) return;
            for (int t = 0; t < TrackNames.Length; t++)
            {
                StationLevel e = _data.islandLevels.Find(s => s.id == "coal#" + t);
                if (e != null) _lv[t] = e.level;
            }
        }

        private void SaveLevel(int t)
        {
            if (_data == null || _data.islandLevels == null) return;
            string id = "coal#" + t;
            StationLevel e = _data.islandLevels.Find(s => s.id == id);
            if (e == null) { e = new StationLevel { id = id }; _data.islandLevels.Add(e); }
            e.level = _lv[t];
        }

        private void ScaleHeap(Transform heap, float baseY, double amount, double full)
        {
            if (heap == null) return;
            float f = Mathf.Clamp01((float)(amount / full));
            bool on = f > 0.02f;
            if (heap.gameObject.activeSelf != on) heap.gameObject.SetActive(on);
            if (!on) return;
            Vector3 s = heap.localScale; s.y = Mathf.Lerp(0.4f, 4.5f, f); heap.localScale = s;
            Vector3 pp = heap.position; pp.y = baseY + s.y * 0.5f; heap.position = pp;
        }

        // ---------------- builders ----------------
        private static Transform Child(Transform root, string n)
        {
            foreach (Transform t in root) if (t.name == n) return t;
            return null;
        }

        private static void Show(GameObject go, bool on) { if (go != null && go.activeSelf != on) go.SetActive(on); }

        private static Material MakeMat(Material src, Color c)
        {
            Material m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            return m;
        }

        private GameObject MakeChunk(Transform parent, Material mat, Vector3 localPos, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "OpLoad";
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.SetActive(false);
            return go;
        }

        private Transform MakeHeap(Transform pile, Material mat, out float baseY)
        {
            var pr = pile.GetComponentInChildren<Renderer>();
            Vector3 size = pr != null ? pr.bounds.size : new Vector3(6f, 1f, 6f);
            baseY = (pr != null ? pr.bounds.max.y : pile.position.y);

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "OpHeap";
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.transform.SetParent(pile, true);
            go.transform.position = new Vector3(pile.position.x, baseY, pile.position.z);
            go.transform.localScale = new Vector3(Mathf.Max(2f, size.x * 0.65f), 1f, Mathf.Max(2f, size.z * 0.65f));
            go.transform.rotation = Quaternion.identity;
            go.SetActive(false);
            return go.transform;
        }
    }
}
