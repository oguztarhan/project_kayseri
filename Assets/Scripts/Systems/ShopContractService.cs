using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Shop contracts: one offer per 30-minute slot of wall-clock time, a contract customer who joins the shop when
    /// the player accepts, and one payment — cash, gems and foreman cards together — when the last item is delivered.
    /// Accepting pays nothing; a cancelled contract pays nothing.
    ///
    /// The offer is not stored as numbers. Its job (which bench, which size) comes from the slot's seed and is frozen
    /// the first time the slot is looked at, so building a bench mid-slot cannot swap the product under the player;
    /// its quantity and price follow the bench until accept, when the simulation takes a copy that nothing changes
    /// again. The contract itself — terms, progress, whose turn it is — lives on the business
    /// (<see cref="ShopContractState"/>), so it is saved with the shop it belongs to.
    ///
    /// The payment rides the completing receipt. <see cref="MarketService"/> raises it from inside the shop's clock,
    /// and this pays, records the goal, clears the contract and writes the "Contract Completed!" summary in one save:
    /// a kill at any point either finds the contract still owed or finds it paid with its screen waiting.
    /// </summary>
    public sealed class ShopContractService
    {
        /// <summary>The current slot's job, priced against the bench as it is now.</summary>
        public struct Offer
        {
            public long Slot;
            public int ProductIndex;
            public int SizeIndex;
            public ShopContract.Terms Terms;
        }

        /// <summary>What a completed contract paid, for the celebration screen.</summary>
        public struct Completion
        {
            public int ProductIndex;
            public int SizeIndex;
            public int Quantity;
            public double Cash;
            /// <summary>What the same items would have sold for normally; Cash minus this is the premium.</summary>
            public double NormalCash;
            public long Gems;
            public int ForemanCards;
            /// <summary>The foreman the cards went to, or -1.</summary>
            public int Foreman;
        }

        private readonly MarketService _market;
        private readonly WalletService _wallet;
        private readonly ForemanService _foremen;
        private readonly GoalService _goals;
        private readonly SaveService _save;
        private readonly SaveData _data;
        private readonly Func<long> _now;
        private readonly IAnalytics _analytics;
        private readonly ShopContract.Tuning _tuning;
        private readonly ShopContractSaveData _store;
        private readonly bool[] _built = new bool[MiningShopCampaign.ProductCount];

        /// <summary>A contract was accepted or cancelled.</summary>
        public event Action Changed;

        /// <summary>A contract was completed and paid. Raised after the save that holds the payment.</summary>
        public event Action<Completion> Completed;

        public ShopContractService(MarketService market, WalletService wallet, ShopContract.Tuning tuning,
            SaveData data, Func<long> nowUnix = null, ForemanService foremen = null, GoalService goals = null,
            SaveService save = null, IAnalytics analytics = null)
        {
            _market = market ?? throw new ArgumentNullException(nameof(market));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _data = data ?? throw new ArgumentNullException(nameof(data));
            tuning.Validate();
            _tuning = tuning;
            _now = nowUnix;
            _foremen = foremen;
            _goals = goals;
            _save = save;
            _analytics = analytics;
            if (_data.shopContract == null) _data.shopContract = new ShopContractSaveData();
            _store = _data.shopContract;
            _market.MiningShopBusinessSold += OnSold;
            CancelLeftBehind();
        }

        private MiningShopBusinessService Shop => _market.MiningShopBusiness;

        /// <summary>Contracts are offered once a shop is open and the opening tutorial is behind the player.</summary>
        public bool Unlocked => Shop != null && _data.tutorialStep >= TutorialProgress.StepDone;

        public long CurrentSlot => ShopContract.SlotAt(NowUnix(), _tuning);

        /// <summary>Seconds until the next slot's offer replaces this one.</summary>
        public double SecondsToNextOffer => ShopContract.SecondsToNextSlot(NowUnix(), _tuning);

        public bool HasActive => Shop != null && Shop.View.ContractActive;

        /// <summary>The expected share of each size, for callers that report on the reward budget.</summary>
        public ShopContract.Tuning Tuning => _tuning;

        /// <summary>
        /// This slot's offer. False when contracts are locked, no bench is built, or this slot's offer was already
        /// taken (or the clock was set back behind the last one taken). An offer is returned while a contract runs,
        /// so the screen can show what is next; <see cref="CanAccept"/> says whether it can be taken.
        /// </summary>
        public bool TryGetOffer(out Offer offer)
        {
            offer = default;
            if (!Unlocked) return false;
            MiningShopBusinessService shop = Shop;
            MiningShopBusinessSimulation.Snapshot view = shop.View;
            long slot = CurrentSlot;
            if (slot <= view.ContractSlot) return false;
            if (!TryFrozenPick(view, slot, out int product, out int size)) return false;

            offer = new Offer
            {
                Slot = slot,
                ProductIndex = product,
                SizeIndex = size,
                Terms = ShopContract.TermsFor(size, shop.CraftSeconds(product), NormalUnitPrice(product), _tuning)
            };
            return true;
        }

        /// <summary>There is an offer the player can take right now — the HUD chip's NEW and the tutorial's cue.</summary>
        public bool OfferReady => TryGetOffer(out Offer offer) && CanAccept(offer);

        /// <summary>Whether <paramref name="offer"/> is this slot's and nothing is running.</summary>
        public bool CanAccept(in Offer offer)
            => Unlocked && !Shop.View.ContractActive && offer.Slot == CurrentSlot && offer.Slot > Shop.View.ContractSlot;

        /// <summary>
        /// Takes the offer of <paramref name="slot"/> at today's price and saves. Refused for any other slot, so a
        /// tap that lands after the offer turned over cannot accept the new one unseen. Pays nothing.
        /// </summary>
        public bool Accept(long slot)
        {
            if (!TryGetOffer(out Offer offer) || offer.Slot != slot || !CanAccept(offer)) return false;
            if (!Shop.StartContract(offer.Slot, offer.ProductIndex, offer.SizeIndex, offer.Terms)) return false;
            Commit();
            _analytics?.Log("shop_contract_accept", "size", offer.SizeIndex);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// The player is leaving this island: the running contract is cancelled and nothing is paid, including for
        /// items already handed over. Call before the shop switches business. False when nothing was running.
        /// </summary>
        public bool CancelForIslandChange()
        {
            MiningShopBusinessService shop = Shop;
            if (shop == null || !shop.CancelContract()) return false;
            Commit();
            _analytics?.Log("shop_contract_cancelled", "reason", "island");
            Changed?.Invoke();
            return true;
        }

        public bool CelebrationPending => _store.celebrationPending;

        /// <summary>The completion the celebration screen still owes the player. Only meaningful while pending.</summary>
        public Completion PendingCelebration => new Completion
        {
            ProductIndex = _store.celebrationProduct,
            SizeIndex = _store.celebrationSize,
            Quantity = _store.celebrationQuantity,
            Cash = _store.celebrationCash,
            NormalCash = _store.celebrationNormalCash,
            Gems = _store.celebrationGems,
            ForemanCards = _store.celebrationCards,
            Foreman = _store.celebrationForeman
        };

        /// <summary>The celebration screen was shown; it is not shown again.</summary>
        public void MarkCelebrationShown()
        {
            if (!_store.celebrationPending) return;
            _store.celebrationPending = false;
            Commit();
        }

        /// <summary>What one item of this bench earns selling normally, the figure contracts are priced against.</summary>
        public double NormalUnitPrice(int productIndex)
        {
            MiningShopBusinessService shop = Shop;
            return ShopContract.NormalUnitPrice(shop.UnitPrice(productIndex),
                BenchMastery.StarsAt(shop.View.ProductAt(productIndex).Level), _market.MiningShopStandingMultiplier,
                shop.Mastery);
        }

        private bool TryFrozenPick(in MiningShopBusinessSimulation.Snapshot view, long slot, out int product, out int size)
        {
            if (_store.pickSlot == slot && _store.pickBusinessId == view.BusinessId &&
                _store.pickProduct >= 0 && _store.pickProduct < view.AvailableProductCount &&
                view.ProductAt(_store.pickProduct).TableBuilt &&
                _store.pickSize >= 0 && _store.pickSize < ShopContract.SizeCount)
            {
                product = _store.pickProduct;
                size = _store.pickSize;
                return true;
            }

            for (int i = 0; i < _built.Length; i++)
                _built[i] = i < view.AvailableProductCount && view.ProductAt(i).TableBuilt;
            if (!ShopContract.TryPick(view.BusinessId, slot, _built, _tuning, out ShopContract.Pick pick))
            {
                product = -1;
                size = 0;
                return false;
            }

            // Written into the save object now and to disk with the next save of anything.
            _store.pickBusinessId = view.BusinessId;
            _store.pickSlot = slot;
            _store.pickProduct = pick.ProductIndex;
            _store.pickSize = pick.SizeIndex;
            product = pick.ProductIndex;
            size = pick.SizeIndex;
            return true;
        }

        /// <summary>The last item was just handed over: pay everything, record it, clear it and save, together.</summary>
        private void OnSold(MiningShopBusinessSimulation.Sale sale)
        {
            if (!sale.CompletesContract) return;
            MiningShopBusinessService shop = Shop;
            if (shop == null) return;
            MiningShopBusinessSimulation.Snapshot view = shop.View;

            var done = new Completion
            {
                ProductIndex = view.ContractProductIndex,
                SizeIndex = view.ContractSizeIndex,
                Quantity = view.ContractQuantity,
                Cash = view.ContractCash,
                NormalCash = view.ContractCash / (1d + _tuning.Sizes[view.ContractSizeIndex].Premium),
                Gems = view.ContractGems,
                ForemanCards = view.ContractForemanCards,
                Foreman = -1
            };

            if (done.Cash > 0d) _wallet.AddCash(new BigDouble(done.Cash));
            if (done.Gems > 0L) _wallet.AddGems(done.Gems);
            if (done.ForemanCards > 0 && _foremen != null) done.Foreman = _foremen.GrantDirectedDuplicates(done.ForemanCards);
            _goals?.Record(Goals.Contracts);

            _store.celebrationPending = true;
            _store.celebrationProduct = done.ProductIndex;
            _store.celebrationSize = done.SizeIndex;
            _store.celebrationQuantity = done.Quantity;
            _store.celebrationCash = done.Cash;
            _store.celebrationNormalCash = done.NormalCash;
            _store.celebrationGems = done.Gems;
            _store.celebrationCards = done.ForemanCards;
            _store.celebrationForeman = done.Foreman;
            shop.ClearCompletedContract();
            Commit();

            _analytics?.Log("shop_contract_complete", "size", done.SizeIndex);
            Completed?.Invoke(done);
        }

        /// <summary>
        /// A contract running on a business other than the open one was left behind by an island change and is
        /// cancelled unpaid. This is the net under <see cref="CancelForIslandChange"/>: whatever moved the player,
        /// a contract never survives on an island they are no longer on.
        /// </summary>
        private void CancelLeftBehind()
        {
            MiningShopBusinessService shop = Shop;
            if (shop == null || _data.miningShopBusinesses == null) return;
            string open = shop.View.BusinessId;
            bool cancelled = false;
            for (int i = 0; i < _data.miningShopBusinesses.Count; i++)
            {
                MiningShopState record = _data.miningShopBusinesses[i];
                if (record == null || record.BusinessId == open || record.Business == null ||
                    record.Business.Contract == null || !record.Business.Contract.Active) continue;
                record.Business.Contract.Clear();
                cancelled = true;
                _analytics?.Log("shop_contract_cancelled", "reason", "left_behind");
            }
            if (cancelled) Commit();
        }

        private void Commit() => _save?.Save(_data);

        private long NowUnix() => _now != null ? _now() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
