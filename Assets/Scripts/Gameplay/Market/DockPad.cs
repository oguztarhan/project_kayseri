using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The dock: where the player puts bars into a waiting ship, and where he takes what she brought
    /// back off her.
    ///
    /// WHY THERE IS A PAD AT ALL, when the hold already fills by itself. Because a voyage was, until
    /// now, a thing that happened inside a panel — the player pressed a button and a number moved
    /// somewhere he could not see. Everything else in this yard is done with his hands: he walks to
    /// the heap, he carries, he sets down at the counter, he steps on a square to spend money. A
    /// second destination for a bar (Docs/VOYAGES.md §1) that could not be reached by carrying one
    /// would be the only thing in the room that is not a place.
    ///
    /// It does not START voyages. Which route, and who is aboard, are decisions with odds attached,
    /// and a decision made by standing somewhere by accident is not a decision. The panel opens them;
    /// this loads them and unloads them.
    ///
    /// It is a sibling of <see cref="StockPad"/> and <see cref="SellCounter"/> and it borrows both:
    /// the contact grace from one, the cadence from the other. Bars leave the back one at a time
    /// because the rhythm is most of what the loop feels like — see StockPad's note.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class DockPad : MonoBehaviour
    {
        /// <summary>See <see cref="StockPad"/> — a contact that has to be renewed cannot get stuck on.</summary>
        private const float ContactGrace = 0.15f;

        [Tooltip("Sırttan ambara bir külçenin inme süresi. Tezgâhtakiyle aynı tempoda.")]
        [SerializeField, Min(0.02f)] private float loadSeconds = 0.11f;

        [Tooltip("Gemi döndüğünde işaretin ne kadar yükseğe zıpladığı.")]
        [SerializeField, Min(0f)] private float readyBob = 0.55f;

        [Tooltip("Zıplama hızı.")]
        [SerializeField, Min(0.1f)] private float bobSpeed = 2.2f;

        private VoyageService _voyages;
        private string _yardKey;
        private CarryStack _carry;
        private AudioService _audio;
        private HapticService _haptics;
        private Transform _marker, _hull;
        private Vector3 _markerHome, _markerScale;

        private float _timer;
        private float _lastTouch = float.NegativeInfinity;
        private float _punch;

        public void Configure(VoyageService voyages, string yardKey, CarryStack carry,
                              Transform marker, Transform hull)
        {
            _hull = hull;
            _voyages = voyages;
            _yardKey = yardKey;
            _carry = carry;
            _marker = marker;
            if (_marker != null)
            {
                _markerHome = _marker.localPosition;
                _markerScale = _marker.localScale;    // whatever it was built at, not an assumed 1
            }
            _audio = ServiceLocator.Get<AudioService>();
            _haptics = ServiceLocator.Get<HapticService>();
            GetComponent<BoxCollider>().isTrigger = true;
        }

        private void OnTriggerStay(Collider other)
        {
            // The carry stack is what identifies the player — customers and hires have none, and a
            // dock that loaded off a passing shopper would be shipping the shop. Same test the other
            // two pads use.
            if (other.GetComponentInChildren<CarryStack>() == null) return;
            // Stepping on starts the cadence over, so the first bar goes aboard straight away.
            if (Time.time - _lastTouch > ContactGrace) _timer = 0f;
            _lastTouch = Time.time;
        }

        private bool Standing => Time.time - _lastTouch <= ContactGrace;

        private void Update()
        {
            if (_voyages == null) return;

            Marker();

            if (!Standing) { _timer = 0f; return; }

            // Unloading her comes first. A player who walks over to a ship that is home wants what is
            // on it, and taking the cards is one touch rather than a cadence — there is one payout on
            // a voyage, not a stream of them.
            int settled = _voyages.SettledBerthOn(_yardKey);
            if (settled >= 0)
            {
                if (_voyages.TryClaim(settled) > 0)
                {
                    _audio?.Play(SoundId.Reward);
                    _haptics?.Medium();
                    _punch = 1f;
                }
                return;
            }

            // Then loading, off his back, one bar at a time.
            if (_carry == null || _carry.IsEmpty) { _timer = 0f; return; }
            if (_voyages.LoadingBerthOn(_yardKey) < 0) { _timer = 0f; return; }

            _timer += Time.deltaTime;
            if (_timer < loadSeconds) return;
            _timer = 0f;

            // Ask the hold first and only then take it off his back: the other order drops a bar into
            // a full ship and it is gone. Same rule the counter follows.
            if (_voyages.DepositByHand(_yardKey, 1d) <= 0d) return;
            if (!_carry.TryRemove()) return;
            _audio?.Play(SoundId.Tick);
            _punch = 0.45f;
        }

        /// <summary>
        /// The one thing on the floor that says something is waiting. It bobs when a ship is home and
        /// sits still otherwise — a signal the player reads from across the room without a HUD, which
        /// is the whole reason the dock is a place rather than a button.
        /// </summary>
        private void Marker()
        {
            bool home = _voyages.SettledBerthOn(_yardKey) >= 0;
            bool loading = _voyages.LoadingBerthOn(_yardKey) >= 0;

            // She is alongside while there is nothing at sea, and gone while there is. The one piece of
            // this feature the player can see without reading anything.
            if (_hull != null)
            {
                bool alongside = home || loading || _voyages.At(0) == null;
                if (_hull.gameObject.activeSelf != alongside) _hull.gameObject.SetActive(alongside);
            }

            if (_marker == null) return;

            float lift = home ? Mathf.Abs(Mathf.Sin(Time.time * bobSpeed)) * readyBob : 0f;

            if (_punch > 0f) _punch = Mathf.Max(0f, _punch - Time.deltaTime * 3.2f);
            float scale = 1f + _punch * 0.35f;

            _marker.localPosition = _markerHome + new Vector3(0f, lift, 0f);
            _marker.localScale = _markerScale * scale;
        }
    }
}
