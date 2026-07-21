using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Estimates the player's income rate (cash/second) and writes it into the save each second so offline
    /// earnings can pay out the recent rate (GDD §7). Measured off <see cref="WalletService.LifetimeCash"/>
    /// (earnings only — it never decreases when the player spends), so <b>buying an upgrade never makes the
    /// rate dip</b>; it's a trailing average over several seconds so bursty market sales read as a steady
    /// number that climbs as the player upgrades.
    /// </summary>
    public sealed class IncomeMeter : MonoBehaviour
    {
        [SerializeField] private int windowSeconds = 8;   // trailing-average window for a steady reading

        private WalletService _wallet;
        private SaveData _data;
        private BigDouble _lastLifetime;
        private float _accum;
        private BigDouble[] _buckets;
        private int _idx;
        private int _filled;

        public double RatePerSec { get; private set; }

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _data = ServiceLocator.Get<SaveData>();
            _buckets = new BigDouble[Mathf.Max(1, windowSeconds)];
            for (int i = 0; i < _buckets.Length; i++) _buckets[i] = new BigDouble(0d);
            if (_wallet != null) _lastLifetime = _wallet.LifetimeCash;
            if (_data != null) RatePerSec = _data.incomeRatePerSec;
        }

        private void Update()
        {
            if (_wallet == null) return;
            _accum += Time.deltaTime;
            if (_accum < 1f) return;

            // earnings this window (LifetimeCash only goes up on AddCash, never down on spend)
            BigDouble earned = _wallet.LifetimeCash - _lastLifetime;
            if (earned.Mantissa < 0d) earned = new BigDouble(0d);
            _lastLifetime = _wallet.LifetimeCash;

            _buckets[_idx] = earned / new BigDouble(_accum);   // per-second sample for this bucket
            _idx = (_idx + 1) % _buckets.Length;
            if (_filled < _buckets.Length) _filled++;

            BigDouble sum = new BigDouble(0d);
            for (int i = 0; i < _filled; i++) sum += _buckets[i];
            RatePerSec = (sum / new BigDouble(_filled)).ToDouble();

            _accum = 0f;
            if (_data != null) _data.incomeRatePerSec = RatePerSec;
        }
    }
}
