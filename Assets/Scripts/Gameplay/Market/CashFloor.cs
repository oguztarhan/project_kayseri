using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The money on the floor beside the counter, and the pressure valve of the whole yard.
    ///
    /// A customer pays where they stand, the note lands on the ground, and it is worth nothing until
    /// somebody walks over it. That is one more errand than banking the sale directly, and the errand
    /// is the point: it is the third job, it is the one an idle player will not do, and it is why
    /// hiring a collector is worth money.
    ///
    /// The floor has a CAP. When it is covered, the counter stops — see <see cref="IsFull"/>, which the
    /// queue checks before serving anyone. Without that the notes would be scenery: an ignored pile
    /// that costs nothing to ignore.
    /// </summary>
    public sealed class CashFloor : MonoBehaviour
    {
        [Tooltip("Yerde aynı anda durabilen deste sayısı. Dolduğunda tezgâh durur.")]
        [SerializeField, Min(1)] private int capacity = 14;

        [Tooltip("Destelerin saçıldığı alanın yarıçapı.")]
        [SerializeField, Min(0.5f)] private float scatterRadius = 2.6f;

        [Tooltip("Oyuncunun desteyi almak için yaklaşması gereken mesafe. Saçılma yarıçapından " +
                 "büyük olmalı — küçükse yığının kenarındaki desteler yerinde kalır.")]
        [SerializeField, Min(0.2f)] private float pickupRange = 3.2f;

        [Tooltip("Deste bu hızla oyuncuya uçar. Menzile giren para geri kaçmaz.")]
        [SerializeField, Min(1f)] private float flySpeed = 16f;

        [Tooltip("Uçarken çizdiği yayın yüksekliği. Sıfır yaparsan para düz bir çizgide süzülür.")]
        [SerializeField, Min(0f)] private float arcHeight = 1.8f;

        [Tooltip("Para sesi en sık bu aralıkla çalar. Bir yığını süpürürken kulak tırmalamasın diye.")]
        [SerializeField, Min(0f)] private float coinSoundSeconds = 0.12f;

        private sealed class Note
        {
            public Transform body;
            public double cash;
            public bool flying;
            public Vector3 from;      // where the flight started, so the arc has something to bend from
            public float flight;      // 0..1 along that arc
        }

        private readonly List<Note> _notes = new List<Note>();
        private readonly Stack<Note> _spare = new Stack<Note>();

        private MarketService _market;
        private string _yardKey;
        // Whoever may pick up: the player always, plus a hired collector once one is paid for. A list
        // rather than a field because "somebody else does this for you" is the entire point of the hire.
        private readonly List<Transform> _collectors = new List<Transform>();
        private Material _material;
        private MarketPrefabs _prefabs;
        private AudioService _audio;
        private HapticService _haptics;
        private float _coinCooldown;
        private int _scatter;             // spawn counter, so notes land in a spread instead of a stack

        /// <summary>True when the floor is covered and nothing more can be sold until it is cleared.</summary>
        public bool IsFull => _notes.Count >= capacity;

        /// <summary>Notes lying about right now, for the readout and for the "come and help" nudge.</summary>
        public int Lying => _notes.Count;

        public void Configure(MarketService market, string yardKey, Transform collector, MarketPrefabs prefabs)
        {
            _market = market;
            _yardKey = yardKey;
            _prefabs = prefabs;
            AddCollector(collector);
            _audio = ServiceLocator.Get<AudioService>();
            _haptics = ServiceLocator.Get<HapticService>();
            // The magnet has to out-reach the scatter or the rim of every drop is unreachable: notes
            // land up to scatterRadius from where they were paid, and a player standing in the middle
            // of their own takings would watch the outer ones sit there. Found by walking into a pile
            // and having it not clear.
            if (pickupRange < scatterRadius * 1.2f) pickupRange = scatterRadius * 1.2f;
            _material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _material.color = new Color(0.34f, 0.68f, 0.42f);
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", new Color(0.34f, 0.68f, 0.42f));
        }

        /// <summary>Drops what a sale was worth onto the floor, near where it was paid.</summary>
        public void Drop(double cash, Vector3 at)
        {
            if (cash <= 0d || IsFull) return;
            Note note = _spare.Count > 0 ? _spare.Pop() : NewNote();
            note.cash = cash;
            note.flying = false;
            note.body.gameObject.SetActive(true);

            // A deterministic spiral rather than a random scatter: the notes spread evenly, and the
            // yard looks the same on every run, which is what makes a layout problem reproducible.
            float angle = _scatter * 2.399963f;          // the golden angle, so points never line up
            float radius = scatterRadius * Mathf.Sqrt((_scatter % capacity + 1f) / capacity);
            _scatter++;
            note.body.position = at + new Vector3(Mathf.Cos(angle) * radius, 0.09f, Mathf.Sin(angle) * radius);
            note.body.rotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
            _notes.Add(note);
        }

        /// <summary>Adds somebody allowed to sweep the floor — the player, or a hired collector.</summary>
        public void AddCollector(Transform collector)
        {
            if (collector != null && !_collectors.Contains(collector)) _collectors.Add(collector);
        }

        /// <summary>Takes a collector back off the floor, so a parked yard's staff stop pulling notes.</summary>
        public void RemoveCollector(Transform collector) => _collectors.Remove(collector);

        /// <summary>
        /// Where the nearest un-flown note is, so a hired collector has somewhere to walk. False when
        /// the floor is clear, which is the collector's cue to go and wait by the counter.
        /// </summary>
        public bool TryNearestNote(Vector3 from, out Vector3 at)
        {
            at = Vector3.zero;
            float best = float.MaxValue;
            for (int i = 0; i < _notes.Count; i++)
            {
                if (_notes[i].flying) continue;
                float d = (_notes[i].body.position - from).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                at = _notes[i].body.position;
            }
            return best < float.MaxValue;
        }

        private void Update()
        {
            if (_market == null || _collectors.Count == 0) return;
            float dt = Time.deltaTime;
            _coinCooldown -= dt;

            float range = pickupRange * pickupRange;

            // Backwards, because banking a note removes it from the list.
            for (int i = _notes.Count - 1; i >= 0; i--)
            {
                Note note = _notes[i];
                Vector3 pocket = Pocket(note);

                // Once a note has started flying it never stops — walking past the edge of the range
                // mid-flight and leaving a note hovering is worse than a slightly generous magnet.
                if (!note.flying)
                {
                    if ((pocket - note.body.position).sqrMagnitude > range) continue;
                    note.flying = true;
                    note.from = note.body.position;
                    note.flight = 0f;
                }

                // Along a bowed path rather than a straight one. Money that leaps up and drops into
                // the player is the single clearest way to say "that was worth something" without a
                // number floating anywhere — and the arc is what separates it from a magnet.
                float span = Mathf.Max(0.6f, Vector3.Distance(note.from, pocket));
                note.flight += flySpeed * dt / span;

                if (note.flight >= 1f)
                {
                    Bank(note, i);
                    continue;
                }
                note.body.position = Vector3.Lerp(note.from, pocket, note.flight)
                                   + Vector3.up * (arcHeight * Mathf.Sin(Mathf.PI * note.flight));
                note.body.Rotate(0f, 480f * dt, 0f, Space.Self);
            }
        }

        /// <summary>
        /// Whose pocket a note is heading for: whoever is nearest. Resolved per note rather than once
        /// per frame so the player and a hired collector can be clearing opposite ends of the same
        /// floor without either of them pulling notes across the yard.
        /// </summary>
        private Vector3 Pocket(Note note)
        {
            Vector3 best = Vector3.zero;
            float nearest = float.MaxValue;
            for (int i = 0; i < _collectors.Count; i++)
            {
                Transform c = _collectors[i];
                if (c == null) continue;
                Vector3 pocket = c.position + Vector3.up * 0.8f;
                float d = (pocket - note.body.position).sqrMagnitude;
                if (d >= nearest) continue;
                nearest = d;
                best = pocket;
            }
            return best;
        }

        private void Bank(Note note, int index)
        {
            _market.Collect(_yardKey, note.cash);
            note.body.gameObject.SetActive(false);
            _notes.RemoveAt(index);
            _spare.Push(note);

            // Throttled by hand rather than trusted to the library: sweeping a full floor banks a
            // dozen notes inside a second, and twelve overlapping coin sounds is a noise, not a reward.
            if (_coinCooldown <= 0f)
            {
                _coinCooldown = coinSoundSeconds;
                _audio?.Play(SoundId.Coin);
                _haptics?.Light();
            }
        }

        private Note NewNote()
        {
            Transform body = MarketPrefabs.Spawn(_prefabs != null ? _prefabs.Cash : null, transform,
                                                 "Deste", PrimitiveType.Cube,
                                                 new Vector3(0.75f, 0.18f, 0.45f), _material);
            return new Note { body = body };
        }
    }
}
