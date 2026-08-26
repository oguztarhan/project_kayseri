namespace Game.Systems
{
    /// <summary>Privacy-consent facade. Real UMP/COPPA flow is swapped in at M5; analytics and ads gate on this.</summary>
    public interface IConsent
    {
        bool AnalyticsAllowed { get; }
        bool PersonalizedAdsAllowed { get; }
    }

    /// <summary>Dev stub — grants consent during local development.</summary>
    public sealed class DevConsentService : IConsent
    {
        public bool AnalyticsAllowed => true;
        public bool PersonalizedAdsAllowed => true;
    }
}
