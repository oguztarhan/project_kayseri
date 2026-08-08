using System;

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
        void Purchase(string sku, Action<bool> onDone);
    }

    public sealed class StubIAPService : IIAPService
    {
        public void Purchase(string sku, Action<bool> onDone) => onDone?.Invoke(false);
    }

    public interface INotifications
    {
        /// <summary>Queues one local notification. Android renders a title and a body as separate
        /// lines and collapses to the title alone on a locked screen, so both are required.</summary>
        void Schedule(string title, string message, int afterSeconds);

        /// <summary>Drops everything still queued. Called when the player opens the game: the queue is
        /// a prediction of an absence that has just ended, so none of it is true any more.</summary>
        void CancelAll();

        /// <summary>
        /// Asks for permission to post, once. Android 13+ requires it at runtime and stops asking
        /// after two refusals, so the caller picks the moment — see <see cref="Game.UI.WelcomeBackUI"/>,
        /// which asks as the player closes a screen that has just handed them offline money.
        /// </summary>
        void RequestPermission();
    }

    public sealed class StubNotifications : INotifications
    {
        public void Schedule(string title, string message, int afterSeconds) { }
        public void CancelAll() { }
        public void RequestPermission() { }
    }
}
