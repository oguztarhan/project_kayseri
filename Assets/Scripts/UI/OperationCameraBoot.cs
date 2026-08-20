using Game.Gameplay;
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
        // The whole-operation fit is the "survey" distance, and the opening shot is a fraction of it.
        //
        // It used to open at about half, which framed a close, chunky view of the middle of a chain that
        // ran in a straight line. With the stations at the corners of a ring that same fraction cropped
        // two of them off the sides, so the player's first sight of the island was a road going nowhere.
        // Opening on very nearly the whole loop is what makes the layout legible; zooming in is a pinch.
        [SerializeField] private float defaultZoomFraction = 0.30f;

        [Header("HUD-safe area")]
        [SerializeField] private float hudTopFraction = 0.09f;    // screen height hidden by the top bar
        [SerializeField] private float hudBottomFraction = 0.17f; // screen height hidden by the bottom bar

        [Header("Limits")]
        // A deliberately narrow band around the opening shot: the playfield is composed to be
        // read at one distance, so zoom is for a closer look at a station rather than a way to
        // pull back to a map view. Opening sits at defaultZoomFraction, roughly mid-band.
        // Was 0.22, which put the closest dolly only 27% in from the opening shot — the whole band was
        // 2x and pinching felt like it did nothing. Getting right up to a single station is the point
        // of zooming in a game where the stations are the content, so the floor is now low enough to
        // fill the screen with one of them.
        [SerializeField] private float zoomInFactor = 0.07f;   // closest dolly, as a fraction of the whole-operation fit
        [SerializeField] private float zoomOutFactor = 0.44f;  // a step back, not a map view
        [SerializeField] private float panPadding = 40f;       // slack beyond the operation footprint

        // Children whose bounds must not influence the framing: locked expansions the player can't act on
        // for hours, the ground/water discs, scenery, and the decorative port out to sea.
        //
        // The authored scenery matters as much as the rest. Dead trees, loose miners and stray ore props
        // are scattered right across the island mesh, so counting them made the framing fit the whole
        // island however tightly the working site was composed — which is why the operation kept ending
        // up as a small knot in the middle of a large empty field.
        // "port_" is deliberately NOT in this list. The harbour is where the island's goods leave from, so
        // it has to be on screen; skipping it framed the market against a strip of grass with the pier
        // just off the edge.
        private static readonly string[] SkipPrefixes =
        {
            "ghost", "isle_", "lagoon_", "Dressing", "ship", "Tiles_",
            "dead", "miner", "orepile", "orecrystal", "bush", "tree", "cloud",
        };

        // District groups excluded from an AUTHORED island's framing only, so the generated
        // islands keep the framing they had. Terrain carries a 640-unit ground plane and the
        // sea, which framed the island as a speck in an ocean; Foliage is scenery; Rail runs
        // out to a tunnel in the massif far past the working site, and the train is meant to
        // vanish into it rather than drag the whole playfield back to keep it on screen.
        // Roads is excluded for the same reason as Rail: the two main arms run out to the
        // island edge at +/-196, three times past the ring road the operation sits on, so
        // counting them framed a playfield of mostly empty grass. The districts and the ring
        // they enclose are what the player watches.
        private static readonly string[] SkipDistricts = { "Terrain", "Foliage", "Rail", "Roads" };

        private bool _framed;
        private int _reframeChecks;
        private WorldIslands _world;

        // Component Start order is not deterministic. Authored operations finish resolving and
        // scaling their modelled geometry in CoalOperation.Start, so a camera solution produced by
        // this component's Start can have measured the pre-initialized island on some devices.
        // Two following frames cover both the other Start calls and one activation/layout frame.
        private const int BootVerificationFrames = 2;

        /// <summary>True once the opening shot has been solved — the tutorial waits for it before it
        /// takes the camera, so the two are never easing it to different places in the same frame.</summary>
        public bool Framed => _framed && _reframeChecks <= 0;

        private void Start()
        {
            _reframeChecks = BootVerificationFrames;
            Frame();
        }

        // Retry until it succeeds: at boot (Bootstrap → Main load) the CameraController can be unfindable
        // in the same frame as Start. Boot is rechecked after every operation's Start has completed,
        // while travel gets one verification frame behind the curtain: a
        // destination root can become active late in the frame on a device, and leaving the old camera
        // there makes the new island look like a speck that disappears when the player zooms in.
        private void Update()
        {
            if (!_framed) { Frame(); return; }
            if (_reframeChecks <= 0) return;

            _framed = false;
            Frame();
            if (_framed) _reframeChecks--;
        }

        /// <summary>Re-frame onto another island root (world-map travel).</summary>
        public void FrameOn(string rootName)
        {
            if (string.IsNullOrEmpty(rootName)) return;
            operationRootName = rootName;
            _framed = false;
            _reframeChecks = 1;
            Frame();
        }

        /// <summary>
        /// The island the game is actually standing on. The serialized name is only a fallback: it is
        /// whatever the scene was authored with, and a player who quit on their second island comes back
        /// to WorldIslands switching that island on and the coal one off — leaving this framed on hidden
        /// geometry, which is an empty sea where the operation should be.
        /// </summary>
        private string LiveRoot()
        {
            if (_world == null) _world = FindAnyObjectByType<WorldIslands>();
            if (_world == null) return operationRootName;
            string live = _world.RootName(_world.ActiveIndex);
            if (!string.IsNullOrEmpty(live)) operationRootName = live;
            return operationRootName;
        }

        private void Frame()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var cc = FindAnyObjectByType<CameraController>();
            var root = FindRoot(LiveRoot());
            if (root == null) return;   // not ready this frame — Update() retries

            if (!OperationBounds(root.transform, out Bounds b)) return;

            cam.orthographic = false;
            cam.fieldOfView = fieldOfView;

            // Each island's chain runs mine → market in its own world direction. Aiming the camera along
            // that axis makes every island read identically in portrait — mountains at the top, market at
            // the bottom, the road straight down the middle — instead of each one at a random diagonal.
            float useYaw = yaw;
            Transform mine = null, market = null;
            foreach (Transform ch in root.transform)
            {
                if (ch.name.StartsWith("mine_")) mine = ch;
                else if (ch.name == "market") market = ch;
            }
            if (mine != null && market != null)
            {
                Vector3 f = mine.position - market.position; f.y = 0f;   // screen-up points at the mountains
                if (f.sqrMagnitude > 1f) useYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            }

            Quaternion rot = Quaternion.Euler(pitch, useYaw, 0f);
            float surveyDist = FitDistance(b, rot, cam.aspect);
            float dist = surveyDist * defaultZoomFraction;
            Vector3 pos = b.center - rot * Vector3.forward * dist;

            // The HUD eats more screen at the bottom than the top, so the visual centre of the free area
            // sits above the screen centre. Slide the camera down its own up-axis to put the operation there.
            float vTan = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float centreOffset = (hudBottomFraction - hudTopFraction) * 0.5f;
            pos -= rot * Vector3.up * (centreOffset * 2f * dist * vTan);

            // CameraController measures its perspective zoom to the ground plane, while the fit above
            // is measured to the operation centre. Work in the controller's space for both the zoom
            // limits and the far plane. A fixed 20,000-unit far plane used to cover the whole archipelago
            // even though the camera travels with the active island; on mobile that near/far ratio throws
            // away enough depth precision for Copper's huge transparent sea to show through the land.
            float down = Mathf.Max(0.2f, -(rot * Vector3.forward).y);
            float groundToCentre = (b.center.y - b.min.y) / down;
            float safeCentreDistance = (b.extents.y + cam.nearClipPlane + 8f) / down;
            float minCentreDistance = Mathf.Max(surveyDist * zoomInFactor, safeCentreDistance);
            float minZoom = minCentreDistance + groundToCentre;
            float maxZoom = Mathf.Max(minZoom, surveyDist * zoomOutFactor + groundToCentre);
            cam.farClipPlane = RequiredFarClip(rot, fieldOfView, cam.aspect, maxZoom, b.size.y);

            if (cc != null)
            {
                cc.enabled = true;
                cc.SetGroundY(b.min.y);

                // CameraController expresses perspective zoom as distance to the GROUND plane, while
                // surveyDist is measured from the operation's CENTRE. Using surveyDist directly made
                // the closest Iron shot only ~24 units from the centre of a 353x54x362 island: the
                // camera entered the buildings and the island vanished behind/around it. Convert both
                // limits to the same ground-distance space and keep the camera above the tallest bound.
                // Pan far enough to walk the whole chain at the opening zoom, but no further.
                cc.SetBounds(new Vector2(pos.x - b.extents.x - panPadding, pos.x + b.extents.x + panPadding),
                             new Vector2(pos.z - b.extents.z - panPadding, pos.z + b.extents.z + panPadding));
                cc.FrameTo(pos, rot, dist);
                cc.SetZoomRange(minZoom, maxZoom);
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
        /// The farthest visible point is the ground/sea hit of a viewport corner at maximum zoom-out,
        /// not an inactive island thousands of units away. Solving those four rays keeps the whole screen
        /// filled at every allowed zoom while retaining far more depth-buffer precision on mobile GPUs.
        /// </summary>
        private static float RequiredFarClip(Quaternion rot, float fov, float aspect,
                                             float maxGroundDistance, float verticalSpan)
        {
            Vector3 forward = rot * Vector3.forward;
            float down = Mathf.Max(0.2f, -forward.y);
            float cameraHeight = down * Mathf.Max(1f, maxGroundDistance);
            float vTan = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float hTan = vTan * Mathf.Max(0.1f, aspect);
            float farthest = maxGroundDistance;

            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    Vector3 ray = rot * new Vector3(sx * hTan, sy * vTan, 1f).normalized;
                    if (ray.y < -0.02f)
                        farthest = Mathf.Max(farthest, cameraHeight / -ray.y);
                }

            // Tall peaks and water displacement get their own guard band. 250 also keeps fog, ships and
            // particles just beyond a tightly framed starter island from being cut off.
            return Mathf.Max(250f, farthest + Mathf.Max(0f, verticalSpan) + 50f);
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

                // An authored island keeps its districts one level down, under the active
                // phase root. Measuring that root whole would defeat every skip below it,
                // so step through it and judge each district on its own name.
                if (ch.name.StartsWith("Island_Phase"))
                {
                    if (!ch.gameObject.activeSelf) continue;
                    foreach (Transform district in ch)
                    {
                        if (Skip(district.name) || SkipDistrict(district.name)) continue;
                        Accumulate(district, ref b, ref have);
                    }
                    continue;
                }

                Accumulate(ch, ref b, ref have);
            }
            return have;
        }

        private static void Accumulate(Transform t, ref Bounds b, ref bool have)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rs.Length; i++)
            {
                if (!have) { b = rs[i].bounds; have = true; }
                else b.Encapsulate(rs[i].bounds);
            }
        }

        private static bool SkipDistrict(string n)
        {
            for (int i = 0; i < SkipDistricts.Length; i++)
                if (n.Equals(SkipDistricts[i], System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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
