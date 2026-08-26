using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The hull the player is standing on, placed on the lane from the voyage's own clock.
    ///
    /// SHE IS NOT DRIVEN AND HAS NO SPEED OF HER OWN. Position is a pure function of how far through
    /// her crossing the voyage is, which means the scene cannot drift out of step with the save
    /// however long it is left open, and closing the app mid-crossing loses nothing. It also means
    /// there is no input path that could shorten a route — the rule the whole layer rests on
    /// (Docs/FIVE_LAYERS.md §4) is held by the shape of the code rather than by a check.
    ///
    /// The bob and the heel are the only things here that are not the clock. They cost nothing, they
    /// run off <see cref="Time.time"/> rather than the voyage, and without them a boat sitting at a
    /// mathematically exact point on a flat plane reads as a model on a shelf.
    /// </summary>
    public sealed class PlayerShip : MonoBehaviour
    {
        [Tooltip("Dalga salinimi: govdenin dikey genligi ve periyodu.")]
        [SerializeField] private float bobHeight = 0.55f;
        [SerializeField, Min(0.1f)] private float bobSeconds = 3.1f;

        [Tooltip("Yalpalama: govdenin yana yatma acisi ve periyodu.")]
        [SerializeField] private float heelDegrees = 3.5f;
        [SerializeField, Min(0.1f)] private float heelSeconds = 4.7f;

        [Tooltip("Rotanin yonune donerken ne kadar yumusak donsun. Ani donus, u donusunde " +
                 "govdenin tek karede ters cevrilmesi demek.")]
        [SerializeField, Min(0.1f)] private float turnSharpness = 2.2f;

        private SeaLane _lane;
        private Transform _hull;
        private float _u;
        private bool _outbound = true;
        private Quaternion _facing = Quaternion.identity;

        /// <summary>The hull itself, so the camera has something to look at and S2 has a muzzle.</summary>
        public Transform Hull => _hull != null ? _hull : transform;

        public void Bind(SeaLane lane, Transform hull)
        {
            _lane = lane;
            _hull = hull;
            _facing = transform.rotation;
        }

        /// <summary>
        /// Put her where the clock says. Called every frame by the boot object rather than reading the
        /// service herself: the scene has one owner and it is not the boat.
        /// </summary>
        public void Place(float u, bool outbound, bool snap = false)
        {
            if (_lane == null) return;
            _u = Mathf.Clamp01(u);
            _outbound = outbound;

            Vector3 at = _lane.Point(_u);
            Vector3 forward = _lane.Heading(_u, _outbound);
            if (forward.sqrMagnitude > 0.0001f)
            {
                Quaternion want = Quaternion.LookRotation(forward, Vector3.up);
                _facing = snap ? want
                               : Quaternion.Slerp(_facing, want, 1f - Mathf.Exp(-turnSharpness * Time.deltaTime));
            }
            transform.SetPositionAndRotation(at, _facing);

            if (_hull == null || _hull == transform) return;
            float t = Time.time;
            _hull.localPosition = new Vector3(0f, Mathf.Sin(t * Mathf.PI * 2f / bobSeconds) * bobHeight, 0f);
            _hull.localRotation = Quaternion.Euler(0f, 0f,
                                    Mathf.Sin(t * Mathf.PI * 2f / heelSeconds) * heelDegrees);
        }

        public float LanePosition => _u;
        public bool Outbound => _outbound;
    }
}
