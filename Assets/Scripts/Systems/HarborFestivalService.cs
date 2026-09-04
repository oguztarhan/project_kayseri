using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Joins Harbor Festival tuning to the existing live-event, goal, reward, save, and entitlement
    /// services. Tokens are derived from completed tasks minus claimed catalogue costs, so the event
    /// does not introduce a second mutable wallet.
    /// </summary>
    public sealed class HarborFestivalService
    {
        private readonly LiveEventService _events;
        private readonly GoalService _goals;
        private readonly WalletService _wallet;
        private readonly ForemanService _foremen;
        private readonly CaptainService _captains;
        private readonly BoostService _boost;
        private readonly SaveData _data;
        private readonly SaveService _save;
        private readonly TimeService _time;
        private readonly IIAPService _iap;
        private readonly IAnalytics _analytics;
        private readonly HarborFestival.Tuning _tuning;

        private LiveEvents.Definition _def;
        private bool _has;
        private bool _syncing;
        private long _pickedUnix = long.MinValue;

        public event Action Changed;

        public HarborFestivalService(LiveEventService events, GoalService goals, WalletService wallet,
            HarborFestival.Tuning tuning, ForemanService foremen = null, CaptainService captains = null,
            BoostService boost = null, SaveData data = null, SaveService save = null,
            TimeService time = null, IIAPService iap = null, IAnalytics analytics = null)
        {
            _events = events;
            _goals = goals;
            _wallet = wallet;
            _foremen = foremen;
            _captains = captains;
            _boost = boost;
            _data = data;
            _save = save;
            _time = time;
            _iap = iap;
            _analytics = analytics;
            _tuning = HarborFestival.IsWellFormed(tuning) ? tuning : HarborFestival.Tuning.Default;
        }

        private long NowUnix()
            => _time != null ? _time.NowUnix() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private bool Fits(in LiveEvents.Definition definition)
            => definition.Kind == HarborFestival.Kind && definition.Slots >= HarborFestival.Slots;

        private void Pick(long now)
        {
            _has = false;
            if (_events == null) return;

            LiveEvents.Definition owed = default, soon = default, past = default;
            bool hasOwed = false, hasSoon = false, hasPast = false;
            for (int i = 0; i < _events.Count; i++)
            {
                LiveEvents.Definition definition = _events.At(i);
                if (!Fits(definition) || !_events.Visible(definition.Id)) continue;

                LiveEvents.Phase phase = LiveEvents.PhaseAt(definition, now);
                if (phase == LiveEvents.Phase.Active)
                {
                    _def = definition;
                    _has = true;
                    return;
                }
                if (phase == LiveEvents.Phase.Upcoming)
                {
                    if (!hasSoon || definition.StartUnix < soon.StartUnix)
                    {
                        soon = definition;
                        hasSoon = true;
                    }
                    continue;
                }

                if (!hasPast || definition.StartUnix > past.StartUnix)
                {
                    past = definition;
                    hasPast = true;
                }
                if (Pending(definition) > 0 && (!hasOwed || definition.StartUnix > owed.StartUnix))
                {
                    owed = definition;
                    hasOwed = true;
                }
            }

            if (hasOwed) { _def = owed; _has = true; }
            else if (hasSoon) { _def = soon; _has = true; }
            else if (hasPast) { _def = past; _has = true; }
        }

        public void Sync()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                long now = NowUnix();
                if (now != _pickedUnix)
                {
                    _pickedUnix = now;
                    Pick(now);
                }
                Accrue();
            }
            finally { _syncing = false; }
        }

        private void Accrue()
        {
            if (!_has || _goals == null || _events == null || !_events.Accruing(_def.Id)) return;
            bool moved = false;
            for (int metric = 0; metric < Goals.MetricCount; metric++)
            {
                int cursorSlot = HarborFestival.CursorSlot(metric);
                long stored = _events.Progress(_def.Id, cursorSlot);
                long lifetime = _goals.Lifetime(metric);
                if (stored <= 0L)
                {
                    _events.Record(_def.Id, cursorSlot, lifetime + 1L);
                    continue;
                }

                long delta = lifetime - (stored - 1L);
                if (delta <= 0L) continue;
                for (int task = 0; task < HarborFestival.TaskCount; task++)
                {
                    if (_tuning.Tasks[task].Metric != metric) continue;
                    if (_events.Progress(_def.Id, HarborFestival.TaskSlot(task)) >= _tuning.Tasks[task].Target) continue;
                    _events.Record(_def.Id, HarborFestival.TaskSlot(task), delta);
                    moved = true;
                }
                _events.Record(_def.Id, cursorSlot, delta);
            }
            if (moved) Changed?.Invoke();
        }

        public bool Available { get { Sync(); return _has; } }
        public string Id { get { Sync(); return _has ? _def.Id : null; } }
        public LiveEvents.Phase Phase { get { Sync(); return _has ? LiveEvents.PhaseAt(_def, NowUnix()) : LiveEvents.Phase.Closed; } }
        public long SecondsLeft { get { Sync(); return _has ? LiveEvents.SecondsLeft(_def, NowUnix()) : 0L; } }
        public HarborFestival.Task TaskAt(int index) => index >= 0 && index < HarborFestival.TaskCount ? _tuning.Tasks[index] : default;
        public HarborFestival.Tier TierAt(int index) => index >= 0 && index < HarborFestival.TierCount ? _tuning.Tiers[index] : default;
        public HarborFestival.CatalogueItem CatalogueAt(int index) => index >= 0 && index < HarborFestival.CatalogueCount ? _tuning.Catalogue[index] : default;

        public long TaskProgress(int index)
        {
            Sync();
            if (!_has || index < 0 || index >= HarborFestival.TaskCount) return 0L;
            long progress = _events.Progress(_def.Id, HarborFestival.TaskSlot(index));
            return progress > _tuning.Tasks[index].Target ? _tuning.Tasks[index].Target : progress;
        }

        public bool TaskDone(int index)
            => index >= 0 && index < HarborFestival.TaskCount && TaskProgress(index) >= _tuning.Tasks[index].Target;

        public bool TaskClaimed(int index)
        {
            Sync();
            return _has && index >= 0 && index < HarborFestival.TaskCount
                && _events.Claimed(_def.Id, HarborFestival.TaskSlot(index));
        }

        public int TokensEarned
        {
            get
            {
                Sync();
                if (!_has) return 0;
                return Earned(_def);
            }
        }

        public int TokensSpent
        {
            get
            {
                Sync();
                if (!_has) return 0;
                return Spent(_def);
            }
        }

        public int TokenBalance
        {
            get
            {
                int balance = TokensEarned - TokensSpent;
                return balance > 0 ? balance : 0;
            }
        }

        private int Earned(in LiveEvents.Definition definition)
        {
            int tokens = 0;
            for (int i = 0; i < HarborFestival.TaskCount; i++)
                if (_events.Progress(definition.Id, HarborFestival.TaskSlot(i)) >= _tuning.Tasks[i].Target)
                    tokens += _tuning.Tasks[i].Tokens;
            return tokens;
        }

        private int Spent(in LiveEvents.Definition definition)
        {
            int tokens = 0;
            for (int i = 0; i < HarborFestival.CatalogueCount; i++)
                if (_events.Claimed(definition.Id, HarborFestival.CatalogueSlot(i)))
                    tokens += _tuning.Catalogue[i].Cost;
            return tokens;
        }

        public bool PremiumOwned
        {
            get
            {
                string sku = _tuning.PremiumSku;
                if (string.IsNullOrEmpty(sku)) return false;
                if (_data != null && _data.purchasedOffers != null)
                    for (int i = 0; i < _data.purchasedOffers.Count; i++)
                        if (_data.purchasedOffers[i] == sku) return true;
                IReadOnlyList<string> entitlements = _iap != null ? _iap.Entitlements : null;
                if (entitlements != null)
                    for (int i = 0; i < entitlements.Count; i++) if (entitlements[i] == sku) return true;
                return false;
            }
        }

        public bool FreeTierClaimed(int index) => TierClaimed(index, false);
        public bool PremiumTierClaimed(int index) => TierClaimed(index, true);

        private bool TierClaimed(int index, bool premium)
        {
            Sync();
            if (!_has || index < 0 || index >= HarborFestival.TierCount) return false;
            int slot = premium ? HarborFestival.PremiumTierSlot(index) : HarborFestival.FreeTierSlot(index);
            return _events.Claimed(_def.Id, slot);
        }

        public bool CanClaimFreeTier(int index)
            => index >= 0 && index < HarborFestival.TierCount
                && TokensEarned >= _tuning.Tiers[index].Tokens && !FreeTierClaimed(index);

        public bool CanClaimPremiumTier(int index)
            => PremiumOwned && index >= 0 && index < HarborFestival.TierCount
                && TokensEarned >= _tuning.Tiers[index].Tokens && !PremiumTierClaimed(index);

        public bool CatalogueClaimed(int index)
        {
            Sync();
            return _has && index >= 0 && index < HarborFestival.CatalogueCount
                && _events.Claimed(_def.Id, HarborFestival.CatalogueSlot(index));
        }

        public bool CanRedeem(int index)
            => Phase == LiveEvents.Phase.Active && index >= 0 && index < HarborFestival.CatalogueCount
                && !CatalogueClaimed(index) && TokenBalance >= _tuning.Catalogue[index].Cost;

        public bool ClaimTask(int index)
        {
            if (!TaskDone(index) || TaskClaimed(index)) return false;
            if (!_events.MarkClaimed(_def.Id, HarborFestival.TaskSlot(index))) return false;
            Pay(_tuning.Tasks[index].Reward);
            Commit("harbor_task_claim", index);
            return true;
        }

        public bool ClaimFreeTier(int index) => ClaimTier(index, false);
        public bool ClaimPremiumTier(int index) => ClaimTier(index, true);

        private bool ClaimTier(int index, bool premium)
        {
            if (premium ? !CanClaimPremiumTier(index) : !CanClaimFreeTier(index)) return false;
            int slot = premium ? HarborFestival.PremiumTierSlot(index) : HarborFestival.FreeTierSlot(index);
            if (!_events.MarkClaimed(_def.Id, slot)) return false;
            Pay(premium ? _tuning.Tiers[index].Premium : _tuning.Tiers[index].Free);
            Commit("harbor_tier_claim", (premium ? "premium:" : "free:") + index);
            return true;
        }

        public bool Redeem(int index)
        {
            if (!CanRedeem(index)) return false;
            if (!_events.MarkClaimed(_def.Id, HarborFestival.CatalogueSlot(index))) return false;
            Pay(_tuning.Catalogue[index].Reward);
            Commit("harbor_redeem", index);
            return true;
        }

        public int ExpiryGems
        {
            get
            {
                if (Phase != LiveEvents.Phase.Closed || !_has || _events.Claimed(_def.Id, HarborFestival.ExpirySlot)) return 0;
                return TokenBalance / _tuning.TokensPerExpiryGem;
            }
        }

        public bool ClaimExpiryConversion()
        {
            int gems = ExpiryGems;
            if (gems <= 0 || !_events.MarkClaimed(_def.Id, HarborFestival.ExpirySlot)) return false;
            _wallet?.AddGems(gems);
            Commit("harbor_expiry_conversion", gems);
            return true;
        }

        public int PendingCount()
        {
            Sync();
            return _has ? Pending(_def) : 0;
        }

        private int Pending(in LiveEvents.Definition definition)
        {
            int pending = 0;
            int earned = Earned(definition);
            for (int i = 0; i < HarborFestival.TaskCount; i++)
                if (_events.Progress(definition.Id, HarborFestival.TaskSlot(i)) >= _tuning.Tasks[i].Target
                    && !_events.Claimed(definition.Id, HarborFestival.TaskSlot(i))) pending++;
            for (int i = 0; i < HarborFestival.TierCount; i++)
            {
                if (earned < _tuning.Tiers[i].Tokens) continue;
                if (!_events.Claimed(definition.Id, HarborFestival.FreeTierSlot(i))) pending++;
                if (PremiumOwned && !_events.Claimed(definition.Id, HarborFestival.PremiumTierSlot(i))) pending++;
            }
            if (LiveEvents.PhaseAt(definition, NowUnix()) == LiveEvents.Phase.Closed
                && !_events.Claimed(definition.Id, HarborFestival.ExpirySlot)
                && (earned - Spent(definition)) / _tuning.TokensPerExpiryGem > 0) pending++;
            return pending;
        }

        private void Pay(in HarborFestival.Reward reward)
        {
            if (reward.Gems > 0L) _wallet?.AddGems(reward.Gems);
            if (reward.Cards > 0) _foremen?.GrantRandomDuplicates(reward.Cards);
            if (reward.Charts > 0L) _captains?.AddCharts(reward.Charts);
            if (reward.BoostMult > 1d && reward.BoostSeconds > 0d)
                _boost?.AddBoost(reward.BoostMult, reward.BoostSeconds);
        }

        private void Commit(string eventName, object value)
        {
            if (_save != null && _data != null) _save.Save(_data);
            _analytics?.Log(eventName, "event", _def.Id + ":" + value);
            Changed?.Invoke();
        }
    }
}
