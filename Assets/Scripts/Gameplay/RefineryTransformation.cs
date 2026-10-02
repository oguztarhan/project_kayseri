using Game.Core;
using Game.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    /// <summary>Bounded stock displays driven only by the furnace's real buffers and throughput.</summary>
    public sealed class RefineryTransformation : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _unitsPerPiece = 2f;
        [SerializeField, Min(1f)] private float _pieceSize = 22f;
        [SerializeField, Min(1f)] private float _spacing = 30f;
        [SerializeField, Min(0f)] private float _frontClearance = 65f;
        [SerializeField, Min(0.05f)] private float _flashSeconds = 0.45f;
        [SerializeField] private Color _oreColor = new Color(0.20f, 0.24f, 0.29f);
        [SerializeField] private Color _barColor = new Color(0.77f, 0.86f, 0.9f);
        private const int Pieces = 12;
        private readonly Transform[] _ore = new Transform[Pieces], _bars = new Transform[Pieces];
        private readonly Material[] _materials = new Material[3];
        private CoalOperation _operation;
        private AccessibilityConfig _accessibility;
        private Transform _root, _feeding, _leaving, _flash;
        private Vector3 _input, _output, _mouth;
        private double _lastTotal;
        private float _flashLeft;
        private double _inputScale;

        private void Awake() => _accessibility = ServiceLocator.Get<AccessibilityConfig>();

        public void Initialize(CoalOperation operation, Transform island)
        {
            Renderer furnace = null;
            foreach (var r in island.GetComponentsInChildren<Renderer>(true))
                if (r.name == "Smelter | main furnace") { furnace = r; break; }
            if (furnace == null) { enabled = false; return; }
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) { enabled = false; return; }
            _operation = operation;
            _lastTotal = operation.TotalRefined;
            _root = new GameObject("GirisVeCikis").transform;
            _root.SetParent(transform, false);
            Bounds b = furnace.bounds;
            float y = b.min.y + _pieceSize * 0.5f;
            _input = new Vector3(b.center.x - b.size.x * 0.22f, y, b.min.z - _frontClearance);
            _output = new Vector3(b.center.x + b.size.x * 0.22f, y, b.min.z - _frontClearance);
            _mouth = new Vector3(b.center.x, y, b.min.z - _pieceSize * 0.4f);
            Color[] colors = { _oreColor, _barColor, new Color(1f, 0.48f, 0.08f) };
            for (int i = 0; i < 3; i++)
            {
                _materials[i] = new Material(shader) { name = "Rafineri donusum " + i };
                _materials[i].SetColor("_BaseColor", colors[i]);
            }
            for (int i = 0; i < Pieces; i++)
            {
                _ore[i] = Piece("HamCevher" + i, PrimitiveType.Sphere, _materials[0]);
                _bars[i] = Piece("Kulce" + i, PrimitiveType.Cube, _materials[1]);
                _ore[i].position = _input + Slot(i, -1);
                _bars[i].position = _output + Slot(i, 1);
                _ore[i].rotation = Quaternion.Euler(i * 31f, i * 47f, i * 19f);
            }
            _feeding = Piece("FirinBesleme", PrimitiveType.Sphere, _materials[0]);
            _leaving = Piece("YeniKulce", PrimitiveType.Cube, _materials[1]);
            _flash = Piece("IslemeIsisi", PrimitiveType.Cube, _materials[2]);
            _flash.position = _mouth;
        }

        private Vector3 Slot(int index, int side) => new Vector3(side * (index % 3) * _spacing,
            (index / 6) * _pieceSize * 0.65f, -(index / 3 % 2) * _spacing);

        private Transform Piece(string name, PrimitiveType shape, Material material)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name; go.transform.SetParent(_root, false);
            Destroy(go.GetComponent<Collider>());
            Renderer r = go.GetComponent<Renderer>();
            r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off;
            go.SetActive(false);
            return go.transform;
        }

        private void LateUpdate()
        {
            if (_root == null) return;
            bool active = _operation != null && _operation.isActiveAndEnabled;
            if (_root.gameObject.activeSelf != active) _root.gameObject.SetActive(active);
            if (!active) return;
            bool reduce = _accessibility != null && _accessibility.ReduceMotion;
            // A large shipment still visibly drains instead of staying capped until its final units.
            _inputScale = System.Math.Max(_inputScale, _operation.RefineQueue / Pieces);
            if (_operation.RefineQueue <= 0d) _inputScale = _unitsPerPiece;
            Stock(_ore, _operation.RefineQueue, false, System.Math.Max(_unitsPerPiece, _inputScale));
            Stock(_bars, _operation.Bars, true, System.Math.Max(_unitsPerPiece, _operation.Economy.BarCap / Pieces));
            double total = _operation.TotalRefined;
            bool processing = total > _lastTotal;
            _lastTotal = total;
            if (processing) _flashLeft = _flashSeconds;
            else _flashLeft = Mathf.Max(0f, _flashLeft - Time.deltaTime);
            float phase = (float)((total / _unitsPerPiece) % 1d);
            bool move = processing && !reduce;
            _feeding.gameObject.SetActive(move && phase < 0.5f);
            _leaving.gameObject.SetActive(move && phase >= 0.5f);
            if (move)
            {
                _feeding.position = Vector3.Lerp(_input, _mouth, phase * 2f);
                _feeding.localScale = Vector3.one * _pieceSize * (1f - phase * 0.7f);
                _leaving.position = Vector3.Lerp(_mouth, _output, (phase - 0.5f) * 2f);
                _leaving.localScale = new Vector3(1.1f, 0.45f, 0.65f) * _pieceSize;
            }
            _flash.gameObject.SetActive(!reduce && _flashLeft > 0f);
            _flash.localScale = new Vector3(1.5f, 0.18f, 0.3f) * (_pieceSize * _flashLeft / _flashSeconds);
        }

        private void Stock(Transform[] pieces, double amount, bool bars, double units)
        {
            double visible = System.Math.Min(Pieces, System.Math.Max(0d, amount / units));
            for (int i = 0; i < pieces.Length; i++)
            {
                float fill = (float)System.Math.Min(1d, System.Math.Max(0d, visible - i));
                bool show = fill > 0f;
                if (pieces[i].gameObject.activeSelf != show) pieces[i].gameObject.SetActive(show);
                if (show) pieces[i].localScale = (bars ? new Vector3(1.1f, 0.45f, 0.65f) : new Vector3(0.9f, 0.75f, 1f))
                    * (_pieceSize * Mathf.Pow(fill, 1f / 3f));
            }
        }

        private void OnDisable() { if (_root != null) _root.gameObject.SetActive(false); }
        private void OnDestroy()
        {
            for (int i = 0; i < _materials.Length; i++) if (_materials[i] != null) Destroy(_materials[i]);
        }
    }
}
