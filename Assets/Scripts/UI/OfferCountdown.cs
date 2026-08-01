using Game.Core;
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
    /// The clock is stamped by <see cref="PremiumStoreUI"/> the first time the store is opened rather
    /// than at install: a player who comes back on day three should still get the full window instead
    /// of an offer that quietly expired while they were away.
    /// </summary>
    public sealed class OfferCountdown : MonoBehaviour
    {
        [Tooltip("Rozetin içindeki süre yazısı.")]
        [SerializeField] private TMP_Text label;
        [Tooltip("Teklifin açık kaldığı süre, saat.")]
        [SerializeField] private float windowHours = 48f;
        [Tooltip("Kalan süre bunun altına inince rozet nabız gibi atar, saniye.")]
        [SerializeField] private float urgentSeconds = 3600f;
        [SerializeField] private float pulseScale = 1.09f;
        [Tooltip("Nabız hızı, saniyedeki vuruş.")]
        [SerializeField] private float pulseHz = 1.5f;

        private readonly char[] _clock = new char[8];   // "SS:DD:SN", rewritten in place once a second
        private RectTransform _labelRt;
        private SaveData _data;
        private TimeService _time;
        private int _painted = -1;

        private void Awake()
        {
            if (label != null) _labelRt = (RectTransform)label.transform;
        }

        private void OnEnable()
        {
            _data = ServiceLocator.Get<SaveData>();
            _time = ServiceLocator.Get<TimeService>();
            _painted = -1;
            Tick();
        }

        private void OnDisable()
        {
            if (_labelRt != null) _labelRt.localScale = Vector3.one;
        }

        private void Update() => Tick();

        private void Tick()
        {
            long left = SecondsLeft();
            if (left <= 0L) { gameObject.SetActive(false); return; }

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

        /// <summary>Full window until the store has been opened once — the stamp is what starts the clock.</summary>
        private long SecondsLeft()
        {
            long window = (long)(windowHours * 3600f);
            if (_data == null || _time == null || _data.starterOfferSeenUnix <= 0L) return window;
            long left = window - _time.ElapsedSince(_data.starterOfferSeenUnix);
            return left > 0L ? left : 0L;
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
