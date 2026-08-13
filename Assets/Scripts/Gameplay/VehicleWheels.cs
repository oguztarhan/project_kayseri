using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Rolls a vehicle's wheels by how far it has actually travelled.
    ///
    /// Nothing here affects the simulation. It exists because a lorry crossing the island on frozen
    /// wheels reads as a sliding model rather than a moving vehicle — the wheels are the one part of it
    /// the eye checks without being asked. Driving them off measured distance rather than off a speed
    /// setting means they stay honest through dwells, queueing and the follow-gap slowdown: a truck
    /// stopped at the pile has still wheels, and one crawling behind another turns them slowly.
    ///
    /// The Kenney bodies ship each wheel as its own transform, which is what makes this possible at all;
    /// the authored meshes have theirs modelled in and are simply not registered.
    ///
    /// Pooled at registration and only ever rotated, so a running island allocates nothing. A plain
    /// class rather than a MonoBehaviour, for the same reason as <see cref="SiteLife"/>: CoalOperation
    /// already owns the per-island update order and this has no independent lifecycle.
    /// </summary>
    public sealed class VehicleWheels
    {
        private const string WheelPrefix = "wheel";

        private readonly List<Transform> _model = new List<Transform>();
        private readonly List<Transform[]> _wheels = new List<Transform[]>();
        private readonly List<float> _radius = new List<float>();
        private readonly List<Vector3> _last = new List<Vector3>();

        /// <summary>
        /// Registers a vehicle, if its art has wheels that turn independently. Safe to call on anything.
        /// </summary>
        public void Add(Transform body, string modelChild)
        {
            if (body == null) return;
            Transform model = body.Find(modelChild);
            if (model == null) return;

            var found = new List<Transform>();
            float radius = 0f;
            var all = model.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (!all[i].name.StartsWith(WheelPrefix, System.StringComparison.Ordinal)) continue;
                var mf = all[i].GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                // A wheel is a disc: thin across the axle, round on the other two. The larger of those
                // two is the rolling diameter. A bogie — the trains' paired assemblies — is not shaped
                // like that and is left out rather than spun as one lump.
                Vector3 s = mf.sharedMesh.bounds.size;
                float thin = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
                float wide = Mathf.Max(s.y, s.z);
                if (thin != s.x) continue;                  // axle is not the model's X — not a wheel
                if (Mathf.Abs(s.y - s.z) > 0.25f * wide) continue;   // not round enough to be one

                found.Add(all[i]);
                radius = Mathf.Max(radius, wide * 0.5f * all[i].lossyScale.y);
            }
            if (found.Count == 0 || radius < 1e-4f) return;

            _model.Add(model);
            _wheels.Add(found.ToArray());
            _radius.Add(radius);
            _last.Add(body.position);
        }

        /// <summary>Rolls every registered vehicle's wheels for this frame's movement.</summary>
        public void Tick()
        {
            for (int i = 0; i < _model.Count; i++)
            {
                Transform model = _model[i];
                if (model == null) continue;

                Vector3 now = model.parent != null ? model.parent.position : model.position;
                Vector3 delta = now - _last[i];
                _last[i] = now;
                if (delta.sqrMagnitude < 1e-8f) continue;

                // Signed along the nose, so reversing rolls them backwards and a sideways shove — a
                // respawn, a teleport into a bay — does not spin them at all.
                float travelled = Vector3.Dot(delta, model.forward);
                if (Mathf.Abs(travelled) < 1e-5f) continue;

                // Rotation about the model's +X takes the top of the wheel toward +Z, which is the
                // nose: rolling forward, not backwards.
                float degrees = travelled / _radius[i] * Mathf.Rad2Deg;
                var wheels = _wheels[i];
                for (int w = 0; w < wheels.Length; w++)
                {
                    if (wheels[w] == null) continue;
                    wheels[w].Rotate(Vector3.right, degrees, Space.Self);
                }
            }
        }
    }
}
