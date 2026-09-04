using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    public sealed class SeasonalIndustryPassService : IDisposable
    {
        private readonly LiveEventService _events;
        private readonly GoalService _goals;
        private readonly WalletService _wallet;
        private readonly ForemanService _foremen;
        private readonly CaptainService _captains;
        private readonly SaveData _data;
        private readonly SaveService _save;
        private readonly TimeService _time;
        private readonly IIAPService _iap;
        private readonly IAnalytics _analytics;
        private readonly SeasonalIndustryPass.Tuning _tuning;

        private LiveEvents.Definition _definition;
        private bool _hasDefinition;
        private bool _syncing;

        public event Action Changed;

        public SeasonalIndustryPassService(LiveEventService events, GoalService goals,
            WalletService wallet, SeasonalIndustryPass.Tuning tuning, ForemanService foremen = null,
            CaptainService captains = null, SaveData data = null, SaveService save = null,
            TimeService time = null, IIAPService iap = null, IAnalytics analytics = null)
        {
            _events = events;
            _goals = goals;
            _wallet = wallet;
            _foremen = foremen;
            _captains = captains;
            _data = data;
            _save = save;
            _time = time;
            _iap = iap;
            _analytics = analytics;
            _tuning = SeasonalIndustryPass.IsWellFormed(tuning)
                ? tuning : SeasonalIndustryPass.Tuning.Default;

            if (_iap != null)
            {
                _iap.EntitlementsUpdated += OnEntitlementsUpdated;
                _iap.UnfinishedPurchase += OnUnfinishedPurchase;
                OnEntitlementsUpdated(_iap.Entitlements);
            }
            Pick();
        }

        public void Dispose()
        {
            if (_iap == null) return;
            _iap.EntitlementsUpdated -= OnEntitlementsUpdated;
            _iap.UnfinishedPurchase -= OnUnfinishedPurchase;
        }

        private long NowUnix()
            => _time != null ? _time.NowUnix() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private bool Fits(in LiveEvents.Definition definition)
            => definition.Kind == SeasonalIndustryPass.Kind
               && definition.Slots >= SeasonalIndustryPass.Slots;

        private void Pick()
        {
            _hasDefinition = false;
            if (_events == null) return;

            long now = NowUnix();
            bool hasUpcoming = false;
            LiveEvents.Definition upcoming = default;
            for (int i = 0; i < _events.Count; i++)
            {
                LiveEvents.Definition candidate = _events.At(i);
                if (!Fits(candidate) || !_events.Visible(candidate.Id)) continue;
                LiveEvents.Phase phase = LiveEvents.PhaseAt(candidate, now);
                if (phase == LiveEvents.Phase.Active)
                {
                    _definition = candidate;
                    _hasDefinition = true;
                    return;
                }
                if (phase == LiveEvents.Phase.Closed && PendingFor(candidate) > 0)
                {
                    if (!_hasDefinition || candidate.StartUnix > _definition.StartUnix)
                    {
                        _definition = candidate;
                        _hasDefinition = true;
                    }
                }
                else if (phase == LiveEvents.Phase.Upcoming
                         && (!hasUpcoming || candidate.StartUnix < upcoming.StartUnix))
                {
                    upcoming = candidate;
                    hasUpcoming = true;
                }
            }
            if (!_hasDefinition && hasUpcoming)
            {
                _definition = upcoming;
                _hasDefinition = true;
            }
        }

        public void Sync()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                Pick();
                if (!_hasDefinition || _events == null || _goals == null
                    || !_events.Accruing(_definition.Id)) return;

                bool changed = false;
                for (int i = 0; i < SeasonalIndustryPass.SourceCount; i++)
                {
                    SeasonalIndustryPass.PointSource source = _tuning.Sources[i];
                    int cursorSlot = SeasonalIndustryPass.CursorSlot(i);
                    long stored = _events.Progress(_definition.Id, cursorSlot);
                    long lifetime = _goals.Lifetime(source.Metric);
                    if (stored <= 0L)
                    {
                        _events.Record(_definition.Id, cursorSlot, lifetime + 1L);
                        continue;
                    }

                    long delta = lifetime - (stored - 1L);
                    if (delta <= 0L) continue;
                    _events.Record(_definition.Id, SeasonalIndustryPass.ProgressSlot(i), delta);
                    _events.Record(_definition.Id, cursorSlot, delta);
                    changed = true;
                }
                if (changed) Changed?.Invoke();
            }
            finally { _syncing = false; }
        }

        public bool Available { get { Sync(); return _hasDefinition; } }
        public string SeasonId { get { Sync(); return _hasDefinition ? _definition.Id : null; } }
        public string PremiumSku => _tuning.PremiumSku;
        public string LocalizedPrice => _iap != null
            ? _iap.LocalizedPrice(_tuning.PremiumSku, _tuning.FallbackPrice)
            : _tuning.FallbackPrice;
        public LiveEvents.Phase Phase => Available
            ? LiveEvents.PhaseAt(_definition, NowUnix()) : LiveEvents.Phase.Closed;
        public bool Live => Phase == LiveEvents.Phase.Active;
        public long SecondsLeft => Available ? LiveEvents.SecondsLeft(_definition, NowUnix()) : 0L;
        public long SecondsUntilStart => Available
            ? LiveEvents.SecondsUntilStart(_definition, NowUnix()) : 0L;

        public long Points
        {
            get
            {
                Sync();
                if (!_hasDefinition) return 0L;
                return PointsFor(_definition);
            }
        }

        public SeasonalIndustryPass.Tier TierAt(int tier)
            => tier >= 0 && tier < SeasonalIndustryPass.TierCount ? _tuning.Tiers[tier] : default;

        public bool TierReached(int tier)
            => tier >= 0 && tier < SeasonalIndustryPass.TierCount
               && Points >= _tuning.Tiers[tier].Points;

        public bool HasPremium => OwnsSku(_tuning.PremiumSku);

        public bool FreeClaimed(int tier)
        {
            Sync();
            return _hasDefinition && ValidTier(tier)
                && _events.Claimed(_definition.Id, SeasonalIndustryPass.FreeClaimSlot(tier));
        }

        public bool PremiumClaimed(int tier)
        {
            Sync();
            return _hasDefinition && ValidTier(tier)
                && _events.Claimed(_definition.Id, SeasonalIndustryPass.PremiumClaimSlot(tier));
        }

        public bool CanClaimFree(int tier) => TierReached(tier) && !FreeClaimed(tier);
        public bool CanClaimPremium(int tier)
            => HasPremium && TierReached(tier) && !PremiumClaimed(tier);

        public bool ClaimFree(int tier)
        {
            if (!CanClaimFree(tier)) return false;
            if (!_events.MarkClaimed(_definition.Id, SeasonalIndustryPass.FreeClaimSlot(tier))) return false;
            Pay(_tuning.Tiers[tier].Free);
            Commit("pass_free_claim", tier);
            return true;
        }

        public bool ClaimPremium(int tier)
        {
            if (!CanClaimPremium(tier)) return false;
            if (!_events.MarkClaimed(_definition.Id, SeasonalIndustryPass.PremiumClaimSlot(tier))) return false;
            Pay(_tuning.Tiers[tier].Premium);
            Commit("pass_premium_claim", tier);
            return true;
        }

        public int PendingCount()
        {
            Sync();
            return _hasDefinition ? PendingFor(_definition) : 0;
        }

        public void PurchasePremium(Action<bool> onDone)
        {
            Sync();
            if (!_hasDefinition || !Live || HasPremium || _iap == null || !_iap.Ready)
            {
                onDone?.Invoke(false);
                return;
            }
            _iap.Purchase(_tuning.PremiumSku, (ok, transactionId) =>
            {
                if (!ok || string.IsNullOrEmpty(transactionId))
                {
                    onDone?.Invoke(false);
                    return;
                }
                ApplyEntitlement(transactionId);
                onDone?.Invoke(true);
            });
        }

        public void RestorePurchases(Action<bool, string> onDone)
        {
            if (_iap == null)
            {
                onDone?.Invoke(false, "Mağaza bu platformda kullanılamıyor.");
                return;
            }
            _iap.RestorePurchases((ok, message) =>
            {
                if (ok) OnEntitlementsUpdated(_iap.Entitlements);
                onDone?.Invoke(ok, message);
            });
        }

        private long PointsFor(in LiveEvents.Definition definition)
        {
            long points = 0L;
            for (int i = 0; i < SeasonalIndustryPass.SourceCount; i++)
                points = SeasonalIndustryPass.AddPoints(points,
                    _events.Progress(definition.Id, SeasonalIndustryPass.ProgressSlot(i)),
                    _tuning.Sources[i].PointsPerAction);
            return points;
        }

        private int PendingFor(in LiveEvents.Definition definition)
        {
            long points = PointsFor(definition);
            int pending = 0;
            bool premium = OwnsSku(_tuning.PremiumSku);
            for (int i = 0; i < SeasonalIndustryPass.TierCount; i++)
            {
                if (points < _tuning.Tiers[i].Points) break;
                if (!_events.Claimed(definition.Id, SeasonalIndustryPass.FreeClaimSlot(i))) pending++;
                if (premium && !_events.Claimed(definition.Id,
                    SeasonalIndustryPass.PremiumClaimSlot(i))) pending++;
            }
            return pending;
        }

        private static bool ValidTier(int tier)
            => tier >= 0 && tier < SeasonalIndustryPass.TierCount;

        private bool OwnsSku(string sku)
            => _data != null && _data.purchasedOffers != null && _data.purchasedOffers.Contains(sku);

        private void OnEntitlementsUpdated(IReadOnlyList<string> entitlements)
        {
            if (entitlements == null) return;
            for (int i = 0; i < entitlements.Count; i++)
            {
                if (!string.Equals(entitlements[i], _tuning.PremiumSku, StringComparison.Ordinal)) continue;
                ApplyEntitlement(null);
                return;
            }
        }

        private void OnUnfinishedPurchase(string sku, string transactionId)
        {
            if (!string.Equals(sku, _tuning.PremiumSku, StringComparison.Ordinal)) return;
            if (string.IsNullOrEmpty(transactionId))
                throw new InvalidOperationException("Sezon bileti işlem kimliği eksik.");
            ApplyEntitlement(transactionId);
        }

        private void ApplyEntitlement(string transactionId)
        {
            if (_data == null) throw new InvalidOperationException("Sezon bileti kaydı kullanılamıyor.");
            if (_data.purchasedOffers == null) _data.purchasedOffers = new List<string>();
            bool changed = false;
            if (!_data.purchasedOffers.Contains(_tuning.PremiumSku))
            {
                _data.purchasedOffers.Add(_tuning.PremiumSku);
                changed = true;
            }
            if (!string.IsNullOrEmpty(transactionId))
                changed |= IapTransactionJournal.Record(_data, transactionId);
            if (changed && _save != null) _save.Save(_data);
            _analytics?.Log("pass_premium_owned", "season", SeasonId ?? "none");
            Changed?.Invoke();
        }

        private void Pay(in SeasonalIndustryPass.Reward reward)
        {
            if (reward.Gems > 0L) _wallet?.AddGems(reward.Gems);
            if (reward.Cards > 0) _foremen?.GrantRandomDuplicates(reward.Cards);
            if (reward.Charts > 0L) _captains?.AddCharts(reward.Charts);
            if (reward.CashMinutes > 0d && _data != null)
                _wallet?.AddCash(new BigDouble(_data.incomeRatePerSec * reward.CashMinutes * 60d));
        }

        private void Commit(string eventName, int tier)
        {
            if (_save != null && _data != null) _save.Save(_data);
            _analytics?.Log(eventName, "season_tier", (_definition.Id ?? "none") + ":" + tier);
            Changed?.Invoke();
        }
    }
}
