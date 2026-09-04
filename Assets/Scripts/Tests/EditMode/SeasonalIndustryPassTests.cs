using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Systems;
using NUnit.Framework;
using UnityEditor;

namespace Game.Tests
{
    public sealed class SeasonalIndustryPassTests
    {
        private const long Day = 86400L;

        private sealed class FakeIap : IIAPService
        {
            public bool Ready { get; set; } = true;
            public readonly List<string> Owned = new List<string>();
            public IReadOnlyList<string> Entitlements => Owned;
            public event Action ProductsUpdated { add { } remove { } }
            public event Action<IReadOnlyList<string>> EntitlementsUpdated;
            public event Action<string, string> UnfinishedPurchase;
            public string LocalizedPrice(string sku, string fallback) => fallback;
            public void Purchase(string sku, Action<bool, string> onDone) => onDone(true, "tx-buy");
            public void RestorePurchases(Action<bool, string> onDone)
            {
                EntitlementsUpdated?.Invoke(Owned);
                onDone(true, null);
            }
            public void RetryUnfinishedPurchases() { }
            public void Interrupt(string sku, string transactionId)
                => UnfinishedPurchase?.Invoke(sku, transactionId);
        }

        private sealed class Rig
        {
            public SaveData Data;
            public GoalService Goals;
            public LiveEventService Events;
            public SeasonalIndustryPassService Pass;
            public FakeIap Iap;
        }

        private static LiveEvents.Definition Definition(string id, long start, long end)
            => new LiveEvents.Definition
            {
                Id = id,
                Kind = SeasonalIndustryPass.Kind,
                StartUnix = start,
                EndUnix = end,
                ConfigVersion = 1,
                Slots = SeasonalIndustryPass.Slots,
                MinIslands = 0,
            };

        private static Rig Running()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet);
            var events = new LiveEventService(data, new List<LiveEvents.Definition>
            {
                Definition("industry-pass-2026-09", now - Day, now + Day),
            });
            var iap = new FakeIap();
            return new Rig
            {
                Data = data,
                Goals = goals,
                Events = events,
                Iap = iap,
                Pass = new SeasonalIndustryPassService(events, goals, wallet,
                    SeasonalIndustryPass.Tuning.Default, data: data, iap: iap),
            };
        }

        [Test]
        public void DefaultTuningAndSlotMapAreValid()
        {
            SeasonalIndustryPass.Tuning tuning = SeasonalIndustryPass.Tuning.Default;
            Assert.That(SeasonalIndustryPass.IsWellFormed(tuning), Is.True);
            Assert.That(tuning.Tiers.Length, Is.EqualTo(SeasonalIndustryPass.TierCount));
            Assert.That(SeasonalIndustryPass.PremiumClaimSlot(14), Is.LessThan(SeasonalIndustryPass.Slots));
            Assert.That(SeasonalIndustryPass.CursorSlot(3), Is.LessThan(SeasonalIndustryPass.Slots));
        }

        [Test]
        public void ExistingLifetimeDoesNotAwardSeasonPoints()
        {
            Rig rig = Running();
            rig.Goals.Record(Goals.Upgrades, 1000);
            rig.Pass.Sync();
            Assert.That(rig.Pass.Points, Is.Zero);

            rig.Goals.Record(Goals.Upgrades, 10);
            Assert.That(rig.Pass.Points, Is.EqualTo(30));
        }

        [Test]
        public void FreeAndPremiumTiersCanBeClaimedInAnyOrderOnlyOnce()
        {
            Rig rig = Running();
            rig.Pass.Sync();
            rig.Goals.Record(Goals.Upgrades, 24);
            Assert.That(rig.Pass.Points, Is.EqualTo(72));
            Assert.That(rig.Pass.ClaimFree(1), Is.True);
            Assert.That(rig.Pass.ClaimFree(0), Is.True);
            Assert.That(rig.Pass.ClaimFree(1), Is.False);
            Assert.That(rig.Pass.ClaimPremium(1), Is.False);

            bool purchased = false;
            rig.Pass.PurchasePremium(ok => purchased = ok);
            Assert.That(purchased, Is.True);
            Assert.That(rig.Pass.ClaimPremium(1), Is.True);
            Assert.That(rig.Pass.ClaimPremium(0), Is.True);
            Assert.That(rig.Pass.ClaimPremium(0), Is.False);
            Assert.That(rig.Data.wallet.gems, Is.EqualTo(105));
        }

        [Test]
        public void InterruptedPurchaseRestoresEntitlementIdempotently()
        {
            Rig rig = Running();
            rig.Iap.Interrupt(rig.Pass.PremiumSku, "tx-interrupted");
            rig.Iap.Interrupt(rig.Pass.PremiumSku, "tx-interrupted");

            Assert.That(rig.Pass.HasPremium, Is.True);
            Assert.That(rig.Data.purchasedOffers.FindAll(s => s == rig.Pass.PremiumSku).Count, Is.EqualTo(1));
            Assert.That(rig.Data.processedIapTransactions.FindAll(s => s == "tx-interrupted").Count,
                Is.EqualTo(1));
        }

        [Test]
        public void StoreRestorationPersistsPremiumOwnership()
        {
            Rig rig = Running();
            rig.Iap.Owned.Add(rig.Pass.PremiumSku);
            bool restored = false;
            rig.Pass.RestorePurchases((ok, message) => restored = ok);

            Assert.That(restored, Is.True);
            Assert.That(rig.Pass.HasPremium, Is.True);
        }

        [Test]
        public void NewSeasonDoesNotInheritProgressClaimsOrPremium()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var data = new SaveData();
            var wallet = new WalletService(data.wallet);
            var goals = new GoalService(data, wallet);
            var oldDefinition = Definition("industry-pass-old", now - 3 * Day, now - 2 * Day);
            var newDefinition = Definition("industry-pass-new", now - Day, now + Day);
            data.liveEvents.Add(new LiveEventState
            {
                id = oldDefinition.Id,
                configVersion = 1,
                progress = new long[SeasonalIndustryPass.Slots],
                claimed = new bool[SeasonalIndustryPass.Slots],
            });
            data.liveEvents[0].progress[0] = 1000;
            data.liveEvents[0].claimed[0] = true;
            data.purchasedOffers.Add("industry_pass_old");
            var events = new LiveEventService(data,
                new List<LiveEvents.Definition> { oldDefinition, newDefinition });
            var pass = new SeasonalIndustryPassService(events, goals, wallet,
                SeasonalIndustryPass.Tuning.Default, data: data);

            Assert.That(pass.SeasonId, Is.EqualTo(newDefinition.Id));
            Assert.That(pass.Points, Is.Zero);
            Assert.That(pass.FreeClaimed(0), Is.False);
            Assert.That(pass.HasPremium, Is.False);
        }

        [Test]
        public void AuthoredConfigAndSeptemberScheduleAreValid()
        {
            SeasonalIndustryPassConfig passConfig =
                AssetDatabase.LoadAssetAtPath<SeasonalIndustryPassConfig>(
                    "Assets/Data/SeasonalIndustryPassConfig.asset");
            Assert.That(passConfig, Is.Not.Null);
            Assert.That(SeasonalIndustryPass.IsWellFormed(passConfig.ToTuning()), Is.True);

            LiveEventConfig liveConfig = AssetDatabase.LoadAssetAtPath<LiveEventConfig>(
                "Assets/Data/LiveEventConfig.asset");
            Assert.That(liveConfig, Is.Not.Null);
            List<LiveEvents.Definition> definitions = liveConfig.Definitions();
            LiveEvents.Definition pass = definitions.Find(d => d.Kind == SeasonalIndustryPass.Kind);
            long expectedStart = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)
                .ToUnixTimeSeconds();

            Assert.That(pass.Id, Is.EqualTo("industry_pass_2026_09"));
            Assert.That(pass.StartUnix, Is.EqualTo(expectedStart));
            Assert.That(pass.EndUnix, Is.EqualTo(expectedStart + 30L * Day));
            Assert.That(pass.Slots, Is.EqualTo(SeasonalIndustryPass.Slots));
            Assert.That(pass.MinCompletedChapters, Is.EqualTo(1));
        }
    }
}
