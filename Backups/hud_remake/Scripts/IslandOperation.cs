using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// A self-contained working mine on an island: a little train (engine + ore wagons) shuttles ore from the
    /// mine entrance to the depot and back, so an unlocked island reads as a <b>running operation</b> you can
    /// watch when you enter it. It's visual flavour driven by the island's own upgrade level (more wagons +
    /// faster as you upgrade) — the actual cash still comes from <see cref="Island.IncomePerSec"/>, so income
    /// stays smooth. Only runs while the island is unlocked; hidden (with the island) while it's ghosted.
    /// </summary>
    [RequireComponent(typeof(Island))]
    public sealed class IslandOperation : MonoBehaviour
    {
        [SerializeField] private GameObject enginePrefab;
        [SerializeField] private GameObject wagonPrefab;
        [SerializeField] private Vector3 minePoint = new Vector3(0f, 0f, 7f);    // local: at the mine entrance
        [SerializeField] private Vector3 depotPoint = new Vector3(0f, 0f, -10f); // local: at the market/depot
        [SerializeField] private float baseSpeed = 5.5f;
        [SerializeField] private float carScale = 1.1f;
        [SerializeField] private float carGap = 3.2f;
        [SerializeField] private int baseWagons = 2;
        [SerializeField] private int maxWagons = 5;
        [SerializeField] private Color oreColor = Color.white;
        [SerializeField] private float endPause = 0.5f;
        [SerializeField] private bool alwaysRun;   // run the train even while the island is locked (keeps the map lively)

        private Island _island;
        private Transform _train;
        private readonly List<Renderer> _oreRends = new List<Renderer>();
        private int _builtWagons = -1;
        private Vector3 _from, _to;
        private bool _loaded = true;   // heading to the depot with ore
        private float _dist;
        private float _pause;

        private void Awake() { _island = GetComponent<Island>(); }

        private void Start()
        {
            _from = minePoint; _to = depotPoint;
            Build(WantWagons());
        }

        private int WantWagons()
        {
            int lv = _island != null ? _island.Level : 0;
            return Mathf.Clamp(baseWagons + lv / 2, 1, maxWagons);   // +1 wagon every 2 upgrade levels
        }

        private void Update()
        {
            bool on = alwaysRun || (_island != null && _island.Unlocked);
            if (_train != null && _train.gameObject.activeSelf != on) _train.gameObject.SetActive(on);
            if (!on) return;

            int want = WantWagons();
            if (want != _builtWagons) Build(want);

            if (_pause > 0f) { _pause -= Time.deltaTime; return; }

            float speed = baseSpeed * (1f + 0.25f * _island.Level);
            Vector3 dir = _to - _from; dir.y = 0f;
            float seg = dir.magnitude;
            _dist += speed * Time.deltaTime;
            float f = seg > 0.01f ? Mathf.Clamp01(_dist / seg) : 1f;
            _train.localPosition = Vector3.Lerp(_from, _to, f);
            if (dir.sqrMagnitude > 0.0001f) _train.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            if (f >= 1f)
            {
                _dist = 0f; _pause = endPause;
                _loaded = !_loaded;                 // just delivered (or just loaded)
                SetOre(_loaded);                    // ore visible only while carrying to the depot
                Vector3 tmp = _from; _from = _to; _to = tmp;
            }
        }

        private void Build(int wagons)
        {
            if (_train != null) Destroy(_train.gameObject);
            _oreRends.Clear();
            _builtWagons = wagons;

            var go = new GameObject("Op_Train");
            _train = go.transform;
            _train.SetParent(transform, false);
            _train.localPosition = _from;

            if (enginePrefab != null) Car(enginePrefab, 0f, false);
            for (int i = 0; i < wagons; i++) Car(wagonPrefab != null ? wagonPrefab : enginePrefab, -(i + 1) * carGap, true);

            SetOre(_loaded);
            _train.gameObject.SetActive(_island != null && _island.Unlocked);
        }

        private void Car(GameObject prefab, float zOffset, bool withOre)
        {
            if (prefab == null) return;
            var car = Instantiate(prefab, _train);
            car.transform.localPosition = new Vector3(0f, 0f, zOffset);
            car.transform.localRotation = Quaternion.identity;
            car.transform.localScale = Vector3.one * carScale;
            if (withOre)
            {
                var ore = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = ore.GetComponent<Collider>(); if (col != null) Destroy(col);
                ore.transform.SetParent(car.transform, false);
                ore.transform.localPosition = new Vector3(0f, 1.1f, 0f);
                ore.transform.localScale = new Vector3(1.5f, 0.9f, 2.2f);
                var r = ore.GetComponent<Renderer>();
                r.sharedMaterial = new Material(r.sharedMaterial) { color = oreColor };
                _oreRends.Add(r);
            }
        }

        private void SetOre(bool visible) { for (int i = 0; i < _oreRends.Count; i++) if (_oreRends[i] != null) _oreRends[i].enabled = visible; }
    }
}
