using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Reports shop tips without an event per tip — at about one a minute that would flood the funnel. The first tip a
    /// shop ever leaves and every huge tip are logged as they happen; everything else is summed over the session and
    /// sent when the player leaves (<see cref="Flush"/>): how many tips, and what share of the shop's cash they were.
    /// </summary>
    public sealed class ShopTipAnalytics
    {
        public const string FirstEvent = "shop_tip_first";
        public const string HugeEvent = "shop_tip_huge";
        public const string SessionCountEvent = "shop_tips_session";
        public const string SessionShareEvent = "shop_tips_share";

        private readonly MarketService _market;
        private readonly IAnalytics _analytics;
        private int _tips;
        private double _saleCash, _tipCash;

        public ShopTipAnalytics(MarketService market, IAnalytics analytics)
        {
            _market = market ?? throw new ArgumentNullException(nameof(market));
            _analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
            _market.MiningShopBusinessSold += OnSold;
        }

        private void OnSold(MiningShopBusinessSimulation.Sale sale)
        {
            if (sale.Contract) return;
            _saleCash += sale.Cash;
            if (sale.TipTier == ShopTips.None) return;
            _tips++;
            _tipCash += sale.Tip;
            // The business counted this tip before the receipt went out, so the first one reads 1.
            MiningShopBusinessService shop = _market.MiningShopBusiness;
            if (shop != null && shop.View.TipCount == 1L) _analytics.Log(FirstEvent, "product", sale.ProductId);
            if (sale.TipTier == ShopTips.TierCount - 1) _analytics.Log(HugeEvent, "product", sale.ProductId);
        }

        /// <summary>Sends the session's totals and starts counting again. Nothing is sent for a session with no sales.</summary>
        public void Flush()
        {
            if (_saleCash <= 0d) return;
            _analytics.Log(SessionCountEvent, "tips", _tips);
            // Per cent of what the sales themselves paid, to one decimal: the figure balancing reads (planned ~3-4.4).
            _analytics.Log(SessionShareEvent, "percent", Math.Round(_tipCash / _saleCash * 100d, 1));
            _tips = 0;
            _saleCash = 0d;
            _tipCash = 0d;
        }
    }
}
