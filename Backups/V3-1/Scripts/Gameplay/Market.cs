using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Sells finished products out of its inventory each tick and pays the player (GDD §3, §4). Upgrade
    /// tracks: <b>Sell Speed</b> (throughput) and <b>Price</b> (revenue multiplier). A manager multiplies
    /// the sell rate (§6); prestige (§8) and a temporary ad boost (§10) also multiply revenue.
    /// </summary>
    public sealed class Market : UpgradableStation, IProducer
    {
        [SerializeField] private StationConfig config;

        private const int Speed = 0, Price = 1;
        private static readonly string[] TrackList = { "Sell Speed", "Price" };

        public Inventory<ResourceDef> Products { get; private set; }
        public double RateMultiplier { get; set; } = 1d;

        public event System.Action<BigDouble> Sold;
        public static event System.Action<BigDouble> AnyUnitsSold;   // units, for contracts

        private GameClock _clock;
        private WalletService _wallet;
        private PrestigeService _prestige;
        private BoostService _boost;
        private readonly List<ResourceDef> _keys = new List<ResourceDef>();
        private double _rate, _priceMult = 1d;

        protected override string StationLabel => config.DisplayName;
        protected override string[] TrackNames => TrackList;
        protected override double TrackBaseCost(int track) => config.BaseUpgradeCost;

        private void Awake() { Products = new Inventory<ResourceDef>(new BigDouble(config.CapacityAtLevel(0))); OnUpgraded(); }

        private void Start()
        {
            _clock = ServiceLocator.Get<GameClock>();
            _wallet = ServiceLocator.Get<WalletService>();
            _prestige = ServiceLocator.Get<PrestigeService>();
            _boost = ServiceLocator.Get<BoostService>();
            if (_clock != null) _clock.OnTick += OnTick;
        }

        private void OnDestroy() { if (_clock != null) _clock.OnTick -= OnTick; }

        protected override void OnUpgraded()
        {
            _rate = config.RateAtLevel(TrackLevel(Speed));
            _priceMult = 1d + 0.15d * TrackLevel(Price);
        }

        private void OnTick()
        {
            if (_wallet == null || Products.Total.Mantissa <= 0d) return;

            BigDouble budget = new BigDouble(_rate * RateMultiplier * _clock.TickInterval);
            BigDouble revenue = BigDouble.Zero;
            BigDouble units = BigDouble.Zero;

            _keys.Clear();
            _keys.AddRange(Products.Amounts.Keys);
            for (int i = 0; i < _keys.Count; i++)
            {
                if (budget.Mantissa <= 0d) break;
                ResourceDef p = _keys[i];
                BigDouble sell = Products.Remove(p, budget);
                if (sell.Mantissa <= 0d) continue;
                revenue += sell * p.BaseValue;
                units += sell;
                budget -= sell;
            }

            if (revenue.Mantissa <= 0d) return;

            double mult = _priceMult;
            if (_prestige != null) mult *= _prestige.IncomeMultiplier;
            if (_boost != null) mult *= _boost.ActiveMultiplier;
            revenue = revenue * mult;

            _wallet.AddCash(revenue);
            Sold?.Invoke(revenue);
            AnyUnitsSold?.Invoke(units);
        }
    }
}
