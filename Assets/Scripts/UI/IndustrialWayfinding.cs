using System.Collections.Generic;
using Game.Core;
using Game.Systems;
using Game.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.UI
{
    /// <summary>Painted route arrows, workplace corners and roadside destination boards.</summary>
    public sealed class IndustrialWayfinding : MonoBehaviour
    {
        [SerializeField, Min(20f)] private float _arrowSpacing = 140f;
        [SerializeField, Min(1f)] private float _arrowWidth = 12f;
        [SerializeField, Min(1f)] private float _lineWidth = 3f;
        [SerializeField, Min(1f)] private float _cornerLength = 35f;
        [SerializeField] private Color _paintColor = new Color(0.98f, 0.84f, 0.36f);
        [SerializeField] private Color _boardColor = new Color(0.045f, 0.10f, 0.16f);
        [SerializeField] private Vector2 _boardSize = new Vector2(125f, 30f);
        [SerializeField, Min(1f)] private float _boardHeight = 40f;
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<int> _indices = new List<int>();
        private readonly List<Transform> _boards = new List<Transform>(5);
        private readonly List<TMP_Text> _labels = new List<TMP_Text>(5);
        private readonly List<string> _keys = new List<string>(5);
        private readonly List<bool> _left = new List<bool>(5);
        private readonly List<Mesh> _meshes = new List<Mesh>(6);
        private Material _paint, _board;
        private CoalOperation _operation;
        private Camera _camera;
        private Transform _visuals;
        private LocalizationService _localization;

        public void Initialize(CoalOperation operation, Transform island)
        {
            _operation = operation;
            _camera = Camera.main;
            if (!operation.TryGetShopHaulRoads(out Vector3[] portRoad, out Vector3[] road,
                out _, out _)) { enabled = false; return; }
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) { enabled = false; return; }
            _paint = new Material(shader) { name = "Tesis yol isaretleri" };
            _paint.SetColor("_BaseColor", _paintColor);
            _paint.SetFloat("_Cull", (float)CullMode.Off);
            _board = new Material(shader) { name = "Tesis yon tabelasi" };
            _board.SetColor("_BaseColor", _boardColor);
            _board.SetFloat("_Cull", (float)CullMode.Off);
            _visuals = new GameObject("YollarVeTabelalar").transform;
            _visuals.SetParent(transform, false);
            // The exported road runs from the harbour uphill. Arrows show the downhill route.
            Arrows(road, true);
            Arrows(portRoad, false);
            Renderer[] art = island.GetComponentsInChildren<Renderer>(true);
            Zone(art, "Mine | pitch black entrance", "shipyard.mine", road, true);
            Zone(art, "Smelter | main furnace", "shipyard.smelter", road, false);
            Zone(art, "Factory | foundation", "shipyard.factory", road, false);
            Zone(art, "Refinery | equipment slab", "shipyard.refinery", road, false);
            Zone(art, "Warehouse | concrete base", "shipyard.deposit", road, false);
            MakeMesh("BoyalıYolVeBolgeSinirlari", _visuals, _paint, _vertices, _indices);
            _localization = ServiceLocator.Get<LocalizationService>();
            if (_localization != null) _localization.Changed += RefreshText;
            RefreshText();
        }

        private void Arrows(Vector3[] path, bool reverse)
        {
            float next = _arrowSpacing * 0.5f;
            for (int i = 1; i < path.Length; i++)
            {
                Vector3 delta = path[i] - path[i - 1];
                float length = delta.magnitude;
                if (length < 0.001f) continue;
                while (next <= length)
                {
                    Vector3 point = Vector3.Lerp(path[i - 1], path[i], next / length) + Vector3.up * 3f;
                    Vector3 forward = delta.normalized * (reverse ? -1f : 1f);
                    Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
                    Stroke(point - forward * _arrowWidth + side * _arrowWidth, point, _lineWidth);
                    Stroke(point - forward * _arrowWidth - side * _arrowWidth, point, _lineWidth);
                    next += _arrowSpacing;
                }
                next -= length;
            }
        }

        private void Zone(Renderer[] art, string name, string key, Vector3[] road, bool mine)
        {
            Renderer source = null;
            for (int i = 0; i < art.Length; i++) if (art[i].name == name) { source = art[i]; break; }
            if (source == null) return;
            Bounds b = source.bounds;
            float ground = name.Contains("base") || name.Contains("foundation") || name.Contains("slab")
                ? b.max.y + 2f : b.min.y + 2f;
            float x0 = b.min.x - 20f, x1 = b.max.x + 20f;
            float z0 = b.min.z - (mine ? 155f : 20f), z1 = mine ? b.min.z - 30f : b.max.z + 20f;
            for (int c = 0; c < 4; c++)
            {
                bool right = c % 2 == 1, back = c >= 2;
                Vector3 p = new Vector3(right ? x1 : x0, ground, back ? z1 : z0);
                Stroke(p, p + Vector3.right * (right ? -_cornerLength : _cornerLength), _lineWidth);
                Stroke(p, p + Vector3.forward * (back ? -_cornerLength : _cornerLength), _lineWidth);
            }
            Vector3 target = new Vector3(b.center.x, ground, z0);
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < road.Length; i++)
            {
                Vector3 d = road[i] - target; d.y = 0f;
                if (d.sqrMagnitude >= best) continue;
                best = d.sqrMagnitude; nearest = i;
            }
            Vector3 origin = road[nearest] + Vector3.left * 40f;
            bool left = target.x < origin.x;
            // Post belongs to the road surface, even when the destination is on a higher terrace.
            Stroke(origin, origin + Vector3.forward * 7f, 9f);
            var post = new GameObject("Yon_" + name).transform;
            post.SetParent(_visuals, false);
            post.position = origin + Vector3.up * _boardHeight;
            var verts = new List<Vector3> { new Vector3(-_boardSize.x / 2, -_boardSize.y / 2),
                new Vector3(-_boardSize.x / 2, _boardSize.y / 2), new Vector3(_boardSize.x / 2, _boardSize.y / 2),
                new Vector3(_boardSize.x / 2, -_boardSize.y / 2) };
            MakeMesh("Levha", post, _board, verts, new List<int> { 0, 1, 2, 0, 2, 3 });
            var label = new GameObject("Yazi").AddComponent<TextMeshPro>();
            label.transform.SetParent(post, false);
            label.transform.localPosition = new Vector3(0, 0, -0.1f);
            label.rectTransform.sizeDelta = _boardSize - new Vector2(8f, 4f);
            label.font = TMP_Settings.defaultFontAsset;
            // World-space TMP uses one tenth of its nominal font size in world units.
            label.fontSize = 220f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 100f; label.fontSizeMax = 220f;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = Color.white;
            label.raycastTarget = false;
            _boards.Add(post); _labels.Add(label); _keys.Add(key); _left.Add(left);
        }

        private void Stroke(Vector3 a, Vector3 b, float width)
        {
            Vector3 side = Vector3.Cross(Vector3.up, b - a).normalized * (width * 0.5f);
            int n = _vertices.Count;
            _vertices.Add(transform.InverseTransformPoint(a - side));
            _vertices.Add(transform.InverseTransformPoint(a + side));
            _vertices.Add(transform.InverseTransformPoint(b + side));
            _vertices.Add(transform.InverseTransformPoint(b - side));
            _indices.Add(n); _indices.Add(n + 1); _indices.Add(n + 2);
            _indices.Add(n); _indices.Add(n + 2); _indices.Add(n + 3);
        }

        private void MakeMesh(string name, Transform parent, Material material, List<Vector3> vertices, List<int> indices)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
            _meshes.Add(mesh);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        }

        private void RefreshText()
        {
            for (int i = 0; i < _labels.Count; i++)
                _labels[i].text = _left[i] ? "< " + Loc.T(_keys[i]) : Loc.T(_keys[i]) + " >";
        }

        private void LateUpdate()
        {
            if (_visuals == null) return;
            bool visible = _operation != null && _operation.isActiveAndEnabled;
            if (_visuals.gameObject.activeSelf != visible) _visuals.gameObject.SetActive(visible);
            if (!visible || _camera == null) return;
            for (int i = 0; i < _boards.Count; i++) _boards[i].rotation = _camera.transform.rotation;
        }

        private void OnDestroy()
        {
            if (_localization != null) _localization.Changed -= RefreshText;
            foreach (Mesh mesh in _meshes) Destroy(mesh);
            if (_paint != null) Destroy(_paint);
            if (_board != null) Destroy(_board);
        }
    }
}
