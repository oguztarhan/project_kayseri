using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// A rolling contract (GDD §9): sell a target number of product units before the timer runs out for
    /// a cash + gem reward. Listens to the market's units-sold signal; auto-rerolls on complete or expiry.
    /// </summary>
    public sealed class ContractManager : MonoBehaviour
    {
        [SerializeField] private ContractConfig config;

        private WalletService _wallet;
        private BigDouble _target;
        private BigDouble _progress;
        private float _deadline;

        public BigDouble Target => _target;
        public BigDouble Progress => _progress;
        public float TimeLeft => Mathf.Max(0f, _deadline - Time.time);
        public long RewardGems => config != null ? config.RewardGems : 0L;

        private void OnEnable() => Market.AnyUnitsSold += OnUnitsSold;
        private void OnDisable() => Market.AnyUnitsSold -= OnUnitsSold;

        private void Start()
        {
            _wallet = ServiceLocator.Get<WalletService>();
            NewContract();
        }

        private void OnUnitsSold(BigDouble units) => _progress += units;

        private void Update()
        {
            if (config == null) return;
            if (_progress >= _target) Complete();
            else if (Time.time >= _deadline) NewContract();
        }

        private void NewContract()
        {
            _target = new BigDouble(config.TargetUnits);
            _progress = BigDouble.Zero;
            _deadline = Time.time + config.TimeLimitSeconds;
        }

        private void Complete()
        {
            if (_wallet != null)
            {
                _wallet.AddCash(new BigDouble(config.RewardCash));
                _wallet.AddGems(config.RewardGems);
            }
            NewContract();
        }
    }
}
