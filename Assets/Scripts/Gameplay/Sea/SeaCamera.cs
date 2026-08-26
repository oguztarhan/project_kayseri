using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The camera over the sea: the same fixed-angle rig the market yard uses, following the hull.
    ///
    /// FIXED PITCH AND YAW, like <see cref="Game.Gameplay.MarketCamera"/> and for the same
    /// reason — a camera that turns with the boat would swing the whole horizon through ninety degrees
    /// at the halfway turn, which is a way to make a player put the phone down. The ship turns; the
    /// world does not.
    ///
    /// It follows on a damped spring rather than rigidly. The hull's own bob would otherwise be
    /// transmitted straight into the camera, cancelling itself out on screen and leaving a boat that
    /// is perfectly still in a sea that is heaving.
    /// </summary>
    public sealed class SeaCamera : MonoBehaviour
    {
        // SEVENTEEN DEGREES, AND THE NUMBER MATTERS. The camera's field of view is 40, so it sees
        // from pitch-20 to pitch+20: at anything above 20 the top ray still points at the water and
        // the horizon is never in frame. The first pass sat at 44 and the sea rendered as a flat blue
        // field with no sky in it — no horizon, no scale, no sense of a world the boat is crossing.
        [SerializeField, Range(6f, 80f)] private float pitch = 17f;
        [SerializeField] private float yaw = 28f;
        [SerializeField, Min(10f)] private float distance = 118f;

        [Tooltip("Govdenin ne kadar onune bakilsin. Gemi ekranin ortasinda degil, biraz gerisinde " +
                 "durmali — oyuncunun gormek istedigi sey onunde ne oldugu.")]
        [SerializeField] private float lead = 52f;

        [Tooltip("Takibin yumusakligi. Kucuk deger, govdenin dalga salinimini kameraya tasir.")]
        [SerializeField, Min(0.01f)] private float smoothing = 0.55f;

        private Transform _target;
        private Vector3 _aim;
        private Vector3 _velocity;

        public void Follow(Transform target)
        {
            _target = target;
            if (target == null) return;
            _aim = AimFor(target);
            Apply();
        }

        private Vector3 AimFor(Transform t)
        {
            // Level with the water rather than with the hull: the bob belongs to the boat.
            Vector3 ahead = t.forward * lead;
            return new Vector3(t.position.x + ahead.x, 0f, t.position.z + ahead.z);
        }

        private void LateUpdate()
        {
            if (_target == null) return;
            _aim = Vector3.SmoothDamp(_aim, AimFor(_target), ref _velocity, smoothing);
            Apply();
        }

        private void Apply()
        {
            Quaternion angle = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(_aim - angle * Vector3.forward * distance, angle);
        }
    }
}
