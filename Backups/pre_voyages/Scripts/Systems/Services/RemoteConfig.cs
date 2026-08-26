namespace Game.Systems
{
    /// <summary>Remote-config facade. Real impl (Firebase/Unity Remote Config) is swapped in at M5.</summary>
    public interface IRemoteConfig
    {
        double GetDouble(string key, double fallback);
        bool GetBool(string key, bool fallback);
        string GetString(string key, string fallback);
    }

    /// <summary>Dev stub — always returns the local fallback (no server), so tuning uses the SO defaults.</summary>
    public sealed class LocalRemoteConfigService : IRemoteConfig
    {
        public double GetDouble(string key, double fallback) => fallback;
        public bool GetBool(string key, bool fallback) => fallback;
        public string GetString(string key, string fallback) => fallback;
    }
}
