using Game.Gameplay;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// Boots the single-island view: frames the shared pan/zoom <see cref="CameraController"/> onto the
    /// player's operation island (found by name — the layout is never moved) and shows the game HUD with its
    /// upgrade tab pointed at the coal (home) island. This replaces the framing/HUD boot that the removed
    /// WorldMapUI used to do, so pressing Play drops the player straight onto their map.
    /// </summary>
    public sealed class OperationCameraBoot : MonoBehaviour
    {
        [SerializeField] private string operationRootName = "Island_Coal";
        [SerializeField] private Vector3 rotEuler = new Vector3(52f, 45f, 0f);
        [SerializeField] private float back = 170f;
        [SerializeField] private float viewPad = 0.62f;   // ortho size = operation span * this
        [SerializeField] private float pan = 130f;        // pan half-extent around the operation

        private bool _framed;

        private void Start() { Frame(); }

        // Retry every frame until it succeeds: at boot (Bootstrap → Main load) the CameraController can be
        // unfindable in the same frame as Start, so a one-shot in Start silently no-ops. Once framed we stop
        // so we never fight the player's own pan/zoom.
        private void Update() { if (!_framed) Frame(); }

        private void Frame()
        {
            var cam = Camera.main;
            if (cam != null) cam.farClipPlane = Mathf.Max(cam.farClipPlane, 20000f);
            var cc = FindAnyObjectByType<CameraController>();
            var root = GameObject.Find(operationRootName);
            if (root == null || (cc == null && cam == null)) return;   // not ready this frame — Update() retries

            bool have = false; Bounds b = new Bounds();
            foreach (Transform ch in root.transform)
            {
                string n = ch.name;
                if (n == "isle_Coal" || n == "lagoon_Coal" || n == "Water" || n == "Ground") continue;   // skip big ground/water planes
                var rs = ch.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < rs.Length; i++) { if (!have) { b = rs[i].bounds; have = true; } else b.Encapsulate(rs[i].bounds); }
            }
            Vector3 c = have ? b.center : root.transform.position;
            float span = have ? Mathf.Max(b.size.x, b.size.z) : 120f;

            Quaternion rot = Quaternion.Euler(rotEuler);
            float size = Mathf.Max(30f, span * viewPad);
            Vector3 pos = c + rot * Vector3.forward * -back;
            if (cc != null)
            {
                cc.enabled = true;
                cc.SetZoomRange(Mathf.Max(12f, size * 0.35f), size * 1.9f);
                cc.SetBounds(new Vector2(pos.x - pan, pos.x + pan), new Vector2(pos.z - pan, pos.z + pan));
                cc.FrameTo(pos, rot, size);
            }
            else { cam.transform.SetPositionAndRotation(pos, rot); if (cam.orthographic) cam.orthographicSize = size; }
            _framed = true;
            Debug.Log("OperationCameraBoot: framed on '" + operationRootName + "' at " + pos.ToString("F0") + " ortho " + size.ToString("F0"));

            // Show the HUD and point its upgrade tab at the coal (home) island.
            var hud = FindAnyObjectByType<HudUGUI>();
            if (hud != null)
            {
                var canvas = hud.GetComponentInChildren<Canvas>(true);
                if (canvas != null) canvas.enabled = true;
                var im = FindAnyObjectByType<IslandManager>();
                if (im != null && im.Islands != null)
                {
                    Island home = null;
                    for (int i = 0; i < im.Islands.Length; i++)
                    {
                        Island isl = im.Islands[i];
                        if (isl != null && isl.Def != null && isl.Def.HomeIsland) { home = isl; break; }
                    }
                    if (home == null && im.Islands.Length > 0) home = im.Islands[0];
                    hud.SetCurrentIsland(home);
                }
            }
        }
    }
}
