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
        void Schedule(string id, string message, int afterSeconds);
    }

    public sealed class StubNotifications : INotifications
    {
        public void Schedule(string id, string message, int afterSeconds) { }
    }
}
