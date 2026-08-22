using Game.Core;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// The starter offer's window. The card art carries a red badge with an empty slot; this fills it
    /// with the time left and switches the whole card off once the window closes, so the store stops
    /// advertising something that can no longer be bought.
    ///
    /// The clock is stamped by <see cref="WorldIslands"/> when an island is entered. Every island owns
    /// a separate window, so an expired coal offer cannot hide a fresh copper offer.
    /// </summary>
    public sealed class OfferCountdown : MonoBehaviour
    {
        [Tooltip("Rozetin içindeki süre yazısı.")]
        [SerializeField] private TMP_Text label;
        [Tooltip("Kalan süre bunun altına inince rozet nabız gibi atar, saniye.")]
        [SerializeField] private float urgentSeconds = 3600f;
        [SerializeField] private float pulseScale = 1.09f;
        [Tooltip("Nabız hızı, saniyedeki vuruş.")]
        [SerializeField] private float pulseHz = 1.5f;

        private readonly char[] _clock = new char[8];   // "SS:DD:SN", rewritten in place once a second
        private RectTransform _labelRt;
        private SaveData _data;
        private TimeService _time;
        private WorldIslands _world;
        private int _painted = -1;

        private void Awake()
        {
            if (label != null) _labelRt = (RectTransform)label.transform;
        }

        private void OnEnable()
        {
            Resolve();
            _painted = -1;
            Tick();
        }

        /// <summary>
        /// Picks up whatever is available now. Re-run from Tick while anything is still missing,
        /// because OnEnable can land before the services are registered and before WorldIslands has
        /// woken — and a card that resolved nothing on its first frame used to stay broken for good.
        /// </summary>
        private void Resolve()
        {
            if (_data == null) _data = ServiceLocator.Get<SaveData>();
            if (_time == null) _time = ServiceLocator.Get<TimeService>();
            if (_world == null) _world = FindAnyObjectByType<WorldIslands>();
        }

        private void OnDisable()
        {
            if (_labelRt != null) _labelRt.localScale = Vector3.one;
        }

        private void Update() => Tick();

        private void Tick()
        {
            if (_data == null || _time == null || _world == null) Resolve();

            long left = SecondsLeft();

            // NOT READY is not the same as EXPIRED, and conflating them is a one-way trip: the branch
            // below switches this object off, and a disabled object never gets another OnEnable to
            // re-ask. One early frame where the world had not finished waking would have hidden a live
            // offer for the rest of the session. Wait instead.
            if (left < 0L) return;

            if (left == 0L) { gameObject.SetActive(false); return; }

            int whole = (int)left;
            if (whole != _painted) { _painted = whole; Paint(whole); }

            if (_labelRt == null) return;
            // the last stretch beats, so a player scrolling past reads that the offer is about to go
            float k = 1f;
            if (left <= (long)urgentSeconds)
                k = 1f + (pulseScale - 1f) * 0.5f *
                    (1f + Mathf.Sin(Time.unscaledTime * pulseHz * 2f * Mathf.PI));
            _labelRt.localScale = new Vector3(k, k, 1f);
        }

        /// <summary>
        /// The active island's own window. Zero means the offer is over — bought, expired or never
        /// stamped. NEGATIVE means the question cannot be answered yet, which is a different thing:
        /// WorldIslands does not build its ladder until its own Awake, and Unity interleaves Awake and
        /// OnEnable per object during a scene load, so this can be asked first.
        /// </summary>
        private long SecondsLeft()
        {
            if (_data == null || _time == null || _world == null) return -1L;

            string island = _world.IslandKey(_world.ActiveIndex);
            if (string.IsNullOrEmpty(island)) return -1L;   // ladder not populated yet

            return StarterOfferState.SecondsLeft(_data, island, _time.NowUnix());
        }

        /// <summary>Writes SS:DD:SN into the reused buffer; building a string every second would allocate.</summary>
        private void Paint(int left)
        {
            if (label == null) return;
            int h = left / 3600;
            if (h > 99) h = 99;
            int m = left / 60 % 60;
            int s = left % 60;
            _clock[0] = (char)('0' + h / 10); _clock[1] = (char)('0' + h % 10); _clock[2] = ':';
            _clock[3] = (char)('0' + m / 10); _clock[4] = (char)('0' + m % 10); _clock[5] = ':';
            _clock[6] = (char)('0' + s / 10); _clock[7] = (char)('0' + s % 10);
            label.SetCharArray(_clock, 0, 8);
        }
    }
}
