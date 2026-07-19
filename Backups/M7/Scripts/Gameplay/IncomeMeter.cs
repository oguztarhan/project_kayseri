using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Estimates the player's income rate (cash/second) as a smoothed average and writes it into the
    /// save each second, so offline earnings can pay out the recent rate (GDD §7).
    /// </summary>
    public sealed class IncomeMeter : MonoBehaviour
    {
        private WalletService _wallet;
        private SaveData _data;
        private BigDouble _lastCash;
        private float _accum;

        public double RatePerSec { get; private set; }

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            _data = ServiceLocator.Get<SaveData>();
            if (_wallet != null) _lastCash = _wallet.Cash;
            if (_data != null) RatePerSec = _data.incomeRatePerSec;
        }

        private void Update()
        {
            if (_wallet == null) return;
            _accum += Time.deltaTime;
            if (_accum < 1f) return;

            BigDouble delta = _wallet.Cash - _lastCash;
            double perSec = delta.Mantissa > 0d ? (delta / new BigDouble(_accum)).ToDouble() : 0d;
            RatePerSec = RatePerSec <= 0d ? perSec : RatePerSec * 0.7d + perSec * 0.3d;

            _lastCash = _wallet.Cash;
            _accum = 0f;
            if (_data != null) _data.incomeRatePerSec = RatePerSec;
        }
    }
}
