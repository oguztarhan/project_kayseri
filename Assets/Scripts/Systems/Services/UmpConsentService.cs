using System;
using GoogleMobileAds.Ump.Api;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// Gerçek rıza akışı: Google UMP formu, ardından iOS'ta ATT izni (GDD §10, Apple Guideline
    /// 5.1.2(i)). Reklam SDK'sı bu bitmeden başlatılmaz — rıza alınmadan reklam istemek hem GDPR
    /// hem App Store tarafında ret sebebi.
    ///
    /// Sıra önemli: önce UMP, sonra ATT. AEA formu ATT'nin ne olduğunu anlatan metni taşıyabiliyor,
    /// ve Google'ın kendi rehberi de bu sırayı istiyor. ATT'nin <see cref="Tick"/> içinden
    /// çağrılmasının sebebi ayrı: iOS izni yalnız uygulama etkinken sorar, ilk kareden önce
    /// çağrılırsa diyaloğu hiç göstermeden reddeder ve bu karar kalıcı olur.
    ///
    /// Takılan bir form reklamları sonsuza kadar kapatmasın diye <see cref="TimeoutSeconds"/> var:
    /// süre dolduğunda akış kişiselleştirilmemiş reklamla kapanır, hiç reklamsız değil.
    /// </summary>
    public sealed class UmpConsentService : IConsent
    {
        private const float TimeoutSeconds = 8f;

        /// <summary>IAB TCF amaç onayları: 1 depolama, 3 basit profil, 4 kişiselleştirilmiş reklam.</summary>
        private const string TcfPurposeKey = "IABTCF_PurposeConsents";

        private readonly bool _forceEea;
        private Action _onDone;
        private float _elapsed;
        private bool _formDone;
        private bool _attAsked;

        public UmpConsentService(bool forceEeaForTesting) { _forceEea = forceEeaForTesting; }

        public bool AnalyticsAllowed { get; private set; }
        public bool PersonalizedAdsAllowed { get; private set; }

        /// <summary>Akış kapandı mı — bildirim izni bunun arkasında bekler.</summary>
        public bool Finished { get; private set; }

        /// <summary>GDPR bölgesinde Google kalıcı bir "gizlilik seçenekleri" girişi şart koşuyor.</summary>
        public bool PrivacyOptionsRequired
            => ConsentInformation.PrivacyOptionsRequirementStatus == PrivacyOptionsRequirementStatus.Required;

        public void Gather(Action onDone)
        {
            _onDone = onDone;

            var request = new ConsentRequestParameters { TagForUnderAgeOfConsent = false };
            if (_forceEea)
            {
                // Test cihazının hash'ini UMP ilk çalıştırmada logcat'e yazar; listeye eklenmeyen bir
                // cihazda coğrafya zorlaması yok sayılır.
                request.ConsentDebugSettings = new ConsentDebugSettings { DebugGeography = DebugGeography.EEA };
                Debug.Log("[Consent] EEA testi açık.");
            }

            ConsentInformation.Update(request, error =>
            {
                if (error != null) Debug.LogWarning("[Consent] bilgi güncellenemedi: " + error.Message);

                ConsentForm.LoadAndShowConsentFormIfRequired(formError =>
                {
                    if (formError != null) Debug.LogWarning("[Consent] form gösterilemedi: " + formError.Message);
                    _formDone = true;   // ATT buradan değil Tick'ten tetiklenir, yukarıdaki nota bak
                });
            });
        }

        /// <summary>GameBootstrap.Update'ten sürülür: ATT adımı ve zaman aşımı burada.</summary>
        public void Tick(float dt)
        {
            if (Finished) return;

            _elapsed += dt;

            if (_formDone && !_attAsked)
            {
                _attAsked = true;
                IOSTracking.Request(status =>
                {
                    Debug.Log("[Consent] ATT sonucu: " + status);
                    Finish();
                });
                return;
            }

            if (_elapsed < TimeoutSeconds) return;

            Debug.LogWarning("[Consent] akış zaman aşımına uğradı; kişiselleştirilmemiş reklamla devam.");
            Finish();
        }

        public void ShowPrivacyOptions()
        {
            ConsentForm.ShowPrivacyOptionsForm(error =>
            {
                if (error != null) { Debug.LogWarning("[Consent] gizlilik formu açılamadı: " + error.Message); return; }
                Resolve();
            });
        }

        private void Finish()
        {
            if (Finished) return;
            Finished = true;
            Resolve();

            Action done = _onDone;
            _onDone = null;
            if (done != null) done();
        }

        private void Resolve()
        {
            bool tcfOk;
            if (ConsentInformation.ConsentStatus == ConsentStatus.NotRequired)
            {
                tcfOk = true;   // GDPR bölgesi değil; TCF anahtarları hiç yazılmaz
            }
            else
            {
                string purposes = PurposeConsents();
                tcfOk = purposes.Length >= 4 && purposes[0] == '1' && purposes[2] == '1' && purposes[3] == '1';
            }

            bool ios = Application.platform == RuntimePlatform.IPhonePlayer;
            AnalyticsAllowed = tcfOk;
            PersonalizedAdsAllowed = tcfOk && (!ios || IOSTracking.Status == IOSTracking.Authorized);

            Debug.Log("[Consent] durum=" + ConsentInformation.ConsentStatus +
                      " reklam isteyebilir=" + ConsentInformation.CanRequestAds() +
                      " kişiselleştirilmiş=" + PersonalizedAdsAllowed);
        }

        /// <summary>
        /// UMP amaç onaylarını platformun kendi tercih deposuna yazar: iOS'ta NSUserDefaults,
        /// Android'de varsayılan SharedPreferences (PlayerPrefs de oraya bakar).
        /// </summary>
        private static string PurposeConsents()
        {
            if (Application.platform == RuntimePlatform.IPhonePlayer)
                return IOSTracking.UserDefaults(TcfPurposeKey) ?? string.Empty;
            return PlayerPrefs.GetString(TcfPurposeKey, string.Empty);
        }
    }
}
