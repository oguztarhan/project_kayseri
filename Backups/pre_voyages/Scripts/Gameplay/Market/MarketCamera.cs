using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The yard camera: a fixed isometric angle that follows the player and never rotates.
    ///
    /// Fixed on purpose. The yard is a small walled space meant to be read at a glance, and a camera
    /// the player can spin is a camera the player can get lost in — they end up steering the view
    /// instead of the character. It also keeps the stick honest: with the angle constant, up on the
    /// stick is the same direction in the yard every time, which is what lets muscle memory form.
    ///
    /// The follow is deliberately soft and only horizontal. Height stays put, so walking behind the
    /// counter does not lift the frame off the queue.
    /// </summary>
    public sealed class MarketCamera : MonoBehaviour
    {
        [Header("Açı")]
        [Tooltip("Kameranın yere bakma açısı. Adadaki görünümle aynı tutulursa avlu oranın parçası gibi durur.")]
        [SerializeField, Range(20f, 80f)] private float pitch = 52f;

        [Tooltip("Avlunun etrafındaki dönüş açısı. Kuzeyi hangi köşeye koyduğunu bu belirler.")]
        [SerializeField] private float yaw = 45f;

        [Tooltip("Hedefe olan uzaklık. Büyüdükçe avlunun daha çoğu görünür.")]
        [SerializeField, Min(5f)] private float distance = 34f;

        [Header("Takip")]
        [Tooltip("Baktığı noktanın oyuncunun ayağından ne kadar yukarıda olduğu. Sıfır yaparsan " +
                 "ekranın alt yarısı hep zemin olur.")]
        [SerializeField] private float lookHeight = 1.6f;

        [Tooltip("Yumuşama süresi. Sıfır yaparsan kamera oyuncuya yapışır ve her adım sarsıntı olur.")]
        [SerializeField, Min(0f)] private float followSeconds = 0.22f;

        [Tooltip("Kameranın yetişebileceği en yüksek hız. Oyuncudan hızlı olmalı, yoksa geride kalır.")]
        [SerializeField, Min(1f)] private float maxCatchUpSpeed = 28f;

        private Transform _target;
        private Vector3 _focus;
        private Vector3 _drift;      // SmoothDamp's own state

        /// <summary>Who to follow. Null parks the camera wherever it was last aimed.</summary>
        public void Follow(Transform target)
        {
            _target = target;
            if (target == null) return;
            _focus = target.position;   // snap on assignment, or the first frame is a swoop across the yard
            Place();
        }

        private void LateUpdate()
        {
            if (_target == null) return;
            // Capped so a long sprint down the hall cannot leave the camera trailing further and
            // further behind — SmoothDamp on its own accelerates without limit to catch up, and the
            // frame ends up lurching when the player finally stops.
            _focus = followSeconds > 0f
                ? Vector3.SmoothDamp(_focus, _target.position, ref _drift, followSeconds, maxCatchUpSpeed)
                : _target.position;
            Place();
        }

        private void Place()
        {
            Quaternion angle = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 aim = new Vector3(_focus.x, _focus.y + lookHeight, _focus.z);
            transform.SetPositionAndRotation(aim - angle * Vector3.forward * distance, angle);
        }
    }
}
