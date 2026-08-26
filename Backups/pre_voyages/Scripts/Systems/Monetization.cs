using System;
using System.Collections.Generic;

namespace Game.Systems
{
    /// <summary>
    /// Monetization facades (GDD §10). Dev stubs now — the rewarded ad grants its reward instantly so
    /// the loop is testable; IAP reports "no store". Real SDKs (ad mediation, Unity IAP, mobile
    /// notifications) swap in at ship time and require package installs (user approval).
    /// </summary>
    public interface IAdService
    {
        bool Available { get; }
        void ShowRewarded(Action onReward);
    }

    public sealed class StubAdService : IAdService
    {
        public bool Available => true;
        public void ShowRewarded(Action onReward) => onReward?.Invoke();
    }

    public interface IIAPService
    {
        bool Ready { get; }
        IReadOnlyList<string> Entitlements { get; }
        event Action ProductsUpdated;
        event Action<IReadOnlyList<string>> EntitlementsUpdated;
        event Action<string, string> UnfinishedPurchase;
        string LocalizedPrice(string sku, string fallback);
        /// <summary>
        /// Completes with the platform transaction id. The reward must be saved against that id before
        /// this callback returns; only then may the platform order be confirmed.
        /// </summary>
        void Purchase(string sku, Action<bool, string> onDone);
        void RestorePurchases(Action<bool, string> onDone);
        /// <summary>Yarıda kalmış siparişleri yeniden teslim etmeyi dener.</summary>
        void RetryUnfinishedPurchases();
    }

    public sealed class StubIAPService : IIAPService
    {
        private static readonly string[] NoEntitlements = new string[0];

        public bool Ready => false;
        public IReadOnlyList<string> Entitlements => NoEntitlements;
        public event Action ProductsUpdated { add { } remove { } }
        public event Action<IReadOnlyList<string>> EntitlementsUpdated { add { } remove { } }
        public event Action<string, string> UnfinishedPurchase { add { } remove { } }
        public string LocalizedPrice(string sku, string fallback) => fallback;
        public void Purchase(string sku, Action<bool, string> onDone) => onDone?.Invoke(false, null);
        public void RestorePurchases(Action<bool, string> onDone)
            => onDone?.Invoke(false, "Mağaza bu platformda kullanılamıyor.");
        public void RetryUnfinishedPurchases() { }
    }

    /// <summary>
    /// Durable idempotency journal shared by every IAP reward path. A transaction is recorded in the
    /// same SaveData object as its reward, then both are written before the platform order is confirmed.
    /// </summary>
    public static class IapTransactionJournal
    {
        public static bool Contains(SaveData data, string transactionId)
        {
            return data != null && !string.IsNullOrEmpty(transactionId)
                   && data.processedIapTransactions != null
                   && data.processedIapTransactions.Contains(transactionId);
        }

        /// <summary>Returns true only the first time this transaction is recorded.</summary>
        public static bool Record(SaveData data, string transactionId)
        {
            if (data == null || string.IsNullOrEmpty(transactionId)) return false;
            if (data.processedIapTransactions == null)
                data.processedIapTransactions = new List<string>();
            if (data.processedIapTransactions.Contains(transactionId)) return false;
            data.processedIapTransactions.Add(transactionId);
            return true;
        }
    }

    public struct LocalNotificationRequest
    {
        public string Id;
        public string Title;
        public string Message;
        public string Target;
        public int AfterSeconds;
    }

    public interface INotifications
    {
        /// <summary>Queues one local notification. Android renders a title and a body as separate
        /// lines and collapses to the title alone on a locked screen, so both are required.</summary>
        void Schedule(LocalNotificationRequest request);

        /// <summary>Drops everything still queued. Called when the player opens the game: the queue is
        /// a prediction of an absence that has just ended, so none of it is true any more.</summary>
        void CancelAll();

        /// <summary>
        /// Asks for permission to post, once. Android 13+ requires it at runtime and stops asking
        /// after two refusals, so the caller picks the moment — see <see cref="Game.UI.WelcomeBackUI"/>,
        /// which asks as the player closes a screen that has just handed them offline money.
        /// </summary>
        void RequestPermission();

        /// <summary>Starts/refreshes the platform query that tells us which notification was tapped.</summary>
        void RefreshOpenedTarget();

        /// <summary>Returns a notification navigation target once, or null while none is available.</summary>
        string PollOpenedTarget();
    }

    public sealed class StubNotifications : INotifications
    {
        public void Schedule(LocalNotificationRequest request) { }
        public void CancelAll() { }
        public void RequestPermission() { }
        public void RefreshOpenedTarget() { }
        public string PollOpenedTarget() => null;
    }
}
