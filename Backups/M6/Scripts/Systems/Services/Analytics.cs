using UnityEngine;

namespace Game.Systems
{
    /// <summary>Analytics facade. Real impl (Firebase/Unity Analytics) is swapped in at M5.</summary>
    public interface IAnalytics
    {
        void Log(string eventName);
        void Log(string eventName, string paramName, object value);
    }

    /// <summary>Dev stub — writes events to the Unity console so we can see the funnel while building.</summary>
    public sealed class DevAnalyticsService : IAnalytics
    {
        public void Log(string eventName) => Debug.Log($"[Analytics] {eventName}");

        public void Log(string eventName, string paramName, object value)
            => Debug.Log($"[Analytics] {eventName} {paramName}={value}");
    }
}
