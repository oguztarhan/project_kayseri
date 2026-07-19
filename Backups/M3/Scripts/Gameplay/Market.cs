using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Sells finished products out of its inventory each tick and pays the player (GDD §3, §4). Its
    /// product inventory is filled by cargo trucks; a manager multiplies its sell rate (GDD §6).
    /// </summary>
    public sealed class Market : MonoBehaviour, IUpgradable, IProducer
    {
        [SerializeField] private StationConfig config;

        public int Level { get; private set; }
        public Inventory<ResourceDef> Products { get; private set; }
        public double RateMultiplier { get; set; } = 1d;

        public event System.Action<BigDouble> Sold;

        private GameClock _clock;
        private WalletService _wallet;
        private readonly List<ResourceDef> _keys = new List<ResourceDef>();

        private void Awake() => Products = new Inventory<ResourceDef>(new BigDouble(config.CapacityAtLevel(Level)));
        private void Start() { _clock = ServiceLocator.Get<GameClock>(); _wallet = ServiceLocator.Get<WalletService>(); if (_clock != null) _clock.OnTick += OnTick; }
        private void OnDestroy() { if (_clock != null) _clock.OnTick -= OnTick; }

        private void OnTick()
        {
            if (_wallet == null || Products.Total.Mantissa <= 0d) return;

            BigDouble budget = new BigDouble(config.RateAtLevel(Level) * RateMultiplier * _clock.TickInterval);
            BigDouble revenue = BigDouble.Zero;

            _keys.Clear();
            _keys.AddRange(Products.Amounts.Keys);
            for (int i = 0; i < _keys.Count; i++)
            {
                if (budget.Mantissa <= 0d) break;
                ResourceDef p = _keys[i];
                BigDouble sell = Products.Remove(p, budget);
                if (sell.Mantissa <= 0d) continue;
                revenue += sell * p.BaseValue;
                budget -= sell;
            }

            if (revenue.Mantissa > 0d) { _wallet.AddCash(revenue); Sold?.Invoke(revenue); }
        }

        public string Label => config.DisplayName;
        public BigDouble UpgradeCost(EconomyService economy) => economy.UpgradeCost(config.BaseUpgradeCost, Level);
        public void ApplyLevel(int level) => Level = level;
    }
}
