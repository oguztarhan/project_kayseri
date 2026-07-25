using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Frames the pan/zoom <see cref="CameraController"/> onto an island's working operation so it fills
    /// the portrait screen, the way a mobile idle tycoon composes its playfield.
    ///
    /// Two things this deliberately does NOT do, because the old version did and both were wrong:
    /// it does not measure the island's full renderer bounds (locked ghost expansions sit far outside
    /// the active area and dragged the framing back), and it does not use a hand-tuned distance
    /// constant (the fit is solved against the real FOV and the real viewport aspect).
    /// </summary>
    public sealed class OperationCameraBoot : MonoBehaviour
    {
        [SerializeField] private string operationRootName = "Island_Coal";

        [Header("Framing")]
        [SerializeField] private float pitch = 46f;            // downward tilt
        [SerializeField] private float yaw = 90f;              // aligned with the mine→market axis so the chain runs down-screen
        [SerializeField] private float fieldOfView = 30f;      // narrow: less perspective distortion, reads closer
        [SerializeField] private float edgeMargin = 0.06f;     // breathing room as a fraction of the fitted span
        // The whole-operation fit is the "survey" distance. Idle tycoons open much closer than that and
        // let you pull back, so the opening shot is a fraction of it. Measured by screenshot, not guessed.
        [SerializeField] private float defaultZoomFraction = 0.52f;

        [Header("HUD-safe area")]
        [SerializeField] private float hudTopFraction = 0.09f;    // screen height hidden by the top bar
        [SerializeField] private float hudBottomFraction = 0.17f; // screen height hidden by the bottom bar

        [Header("Limits")]
        [SerializeField] private float zoomInFactor = 0.30f;   // closest dolly, as a fraction of the whole-operation fit
        [SerializeField] private float zoomOutFactor = 1.15f;  // just past "see the whole island"
        [SerializeField] private float panPadding = 40f;       // slack beyond the operation footprint

        // Children whose bounds must not influence the framing: locked expansions the player can't act on
        // for hours, the ground/water discs, scenery, and the decorative port out to sea.
        private static readonly string[] SkipPrefixes = { "ghost", "isle_", "lagoon_", "Dressing", "port_", "ship", "Tiles_" };

        private bool _framed;

        private void Start() { Frame(); }

        // Retry until it succeeds: at boot (Bootstrap → Main load) the CameraController can be unfindable
        // in the same frame as Start. Once framed we stop so we never fight the player's own pan/zoom.
        private void Update() { if (!_framed) Frame(); }

        /// <summary>Re-frame onto another island root (world-map travel).</summary>
        public void FrameOn(string rootName)
        {
            operationRootName = rootName;
            _framed = false;
        }

        private void Frame()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var cc = FindAnyObjectByType<CameraController>();
            var root = FindRoot(operationRootName);
            if (root == null) return;   // not ready this frame — Update() retries

            if (!OperationBounds(root.transform, out Bounds b)) return;

            cam.orthographic = false;
            cam.fieldOfView = fieldOfView;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 20000f);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            float surveyDist = FitDistance(b, rot, cam.aspect);
            float dist = surveyDist * defaultZoomFraction;
            Vector3 pos = b.center - rot * Vector3.forward * dist;

            // The HUD eats more screen at the bottom than the top, so the visual centre of the free area
            // sits above the screen centre. Slide the camera down its own up-axis to put the operation there.
            float vTan = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float centreOffset = (hudBottomFraction - hudTopFraction) * 0.5f;
            pos -= rot * Vector3.up * (centreOffset * 2f * dist * vTan);

            if (cc != null)
            {
                cc.enabled = true;
                cc.SetGroundY(b.min.y);
                cc.SetZoomRange(surveyDist * zoomInFactor, surveyDist * zoomOutFactor);
                // Pan far enough to walk the whole chain at the opening zoom, but no further.
                cc.SetBounds(new Vector2(pos.x - b.extents.x - panPadding, pos.x + b.extents.x + panPadding),
                             new Vector2(pos.z - b.extents.z - panPadding, pos.z + b.extents.z + panPadding));
                cc.FrameTo(pos, rot, dist);
            }
            else
            {
                cam.transform.SetPositionAndRotation(pos, rot);
            }

            _framed = true;
            Debug.Log("OperationCameraBoot: framed '" + operationRootName + "' span " +
                      b.size.x.ToString("F0") + "x" + b.size.z.ToString("F0") +
                      " open " + dist.ToString("F0") + " survey " + surveyDist.ToString("F0") +
                      " aspect " + cam.aspect.ToString("F2"));
        }

        /// <summary>
        /// Smallest dolly distance along <paramref name="rot"/> that keeps every corner of
        /// <paramref name="b"/> inside both the vertical and the horizontal frustum, after reserving the
        /// HUD bands and the edge margin.
        /// </summary>
        private float FitDistance(Bounds b, Quaternion rot, float aspect)
        {
            float vTan = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float hTan = vTan * Mathf.Max(0.1f, aspect);

            float usable = Mathf.Clamp(1f - hudTopFraction - hudBottomFraction, 0.25f, 1f);
            float keep = Mathf.Clamp01(1f - edgeMargin);
            float vSafe = Mathf.Max(0.01f, vTan * usable * keep);
            float hSafe = Mathf.Max(0.01f, hTan * keep);

            Quaternion inv = Quaternion.Inverse(rot);
            Vector3 e = b.extents;
            float dist = 1f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        // Corner offset from the bounds centre, expressed in camera space.
                        Vector3 local = inv * new Vector3(e.x * sx, e.y * sy, e.z * sz);
                        float needH = Mathf.Abs(local.x) / hSafe - local.z;
                        float needV = Mathf.Abs(local.y) / vSafe - local.z;
                        if (needH > dist) dist = needH;
                        if (needV > dist) dist = needV;
                    }
            return dist;
        }

        /// <summary>Bounds of the parts of the island the player actually watches and acts on.</summary>
        private static bool OperationBounds(Transform root, out Bounds b)
        {
            b = new Bounds();
            bool have = false;
            foreach (Transform ch in root)
            {
                if (Skip(ch.name)) continue;
                var rs = ch.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < rs.Length; i++)
                {
                    if (!have) { b = rs[i].bounds; have = true; }
                    else b.Encapsulate(rs[i].bounds);
                }
            }
            return have;
        }

        private static bool Skip(string n)
        {
            for (int i = 0; i < SkipPrefixes.Length; i++)
                if (n.StartsWith(SkipPrefixes[i], System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Scene-root scan rather than GameObject.Find so a just-activated island resolves the same frame.
        private static GameObject FindRoot(string n)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                if (roots[i].name == n) return roots[i];
            return null;
        }
    }
}
