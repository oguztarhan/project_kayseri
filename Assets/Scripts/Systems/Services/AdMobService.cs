using System;
using Game.Data;
using GoogleMobileAds.Api;
using GoogleMobileAds.Common;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// The real rewarded-ad service (GDD §10), replacing <see cref="StubAdService"/> on device. All four
    /// placements — the ad screen's three rows, the HUD boost shortcut and the welcome-back bonus — go
    /// through one ad unit, so exactly one ad is kept warm and the next is loaded the moment it closes.
    ///
    /// <see cref="Available"/> is deliberately optimistic: it answers "can this build show an ad", not
    /// "is one in memory right now". <see cref="Game.UI.WelcomeBackUI"/> decides whether to offer its
    /// bonus 0.6s after boot, well before any ad has finished loading, and a strict answer would hide
    /// the best-paying placement in the game on every cold start. A tap that arrives early waits the
    /// load out instead.
    ///
    /// Nothing here spends a charge. Both callers consume one only inside the callback this class
    /// invokes, so every failure path below — no fill, no network, presentation error, timeout, or a
    /// player who closes the ad early — costs the player nothing.
    /// </summary>
    public sealed class AdMobService : IAdService
    {
        private const float FirstRetrySeconds = 2f;
        private const float MaxRetrySeconds = 64f;

        private readonly string _unitId;
        private readonly float _showTimeout;

        private RewardedAd _ad;
        private Action _pendingReward;
        private float _waitLeft;
        private float _retryLeft;
        private float _retryDelay = FirstRetrySeconds;
        private bool _initialized;
        private bool _loading;
        private bool _showing;

        public AdMobService(AdsConfig config)
        {
            _unitId = config != null ? config.RewardedUnitId : null;
            _showTimeout = config != null ? config.ShowTimeoutSeconds : 5f;

            // The plugin raises every ad callback on a background thread, and all of them below end up
            // touching the wallet, the save file and the UI. ExecuteInUpdate marshals each one back to
            // the main thread; creating the executor here rather than waiting for the SDK to do it
            // means even the initialization callback has somewhere to land. The old
            // MobileAds.RaiseAdEventsOnUnityMainThread switch that used to cover this is deprecated.
            MobileAdsEventExecutor.Initialize();

            if (string.IsNullOrEmpty(_unitId))
            {
                Debug.LogError("[Ads] AdsConfig yok ya da reklam birimi boş; ödüllü reklam kapalı.");
                return;
            }

            MobileAds.Initialize(status => MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                _initialized = true;
                // After Initialize, not before: it reaches straight through to the platform client,
                // which has nothing behind it until the SDK has come up.
                MobileAds.SetiOSAppPauseOnBackground(true);
                Load();
            }));
        }

        public bool Available
            => _initialized && !_showing && _pendingReward == null && !string.IsNullOrEmpty(_unitId);

        public void ShowRewarded(Action onReward)
        {
            if (!Available) return;

            if (_ad != null && _ad.CanShowAd())
            {
                Show(onReward);
                return;
            }

            // Nothing in memory yet. Park the callback and let the load hand it straight to the ad the
            // moment it lands; the alternative is a dead button for the first seconds of every session.
            _pendingReward = onReward;
            _waitLeft = _showTimeout;
            _retryLeft = 0f;
            Load();
        }

        /// <summary>Drives the load backoff and the timeout on a waiting tap. Called from GameBootstrap.</summary>
        public void Tick(float dt)
        {
            if (_retryLeft > 0f)
            {
                _retryLeft -= dt;
                if (_retryLeft <= 0f) Load();
            }

            if (_pendingReward == null) return;

            _waitLeft -= dt;
            if (_waitLeft > 0f) return;

            _pendingReward = null;
            Debug.LogWarning("[Ads] ödüllü reklam zamanında hazır olmadı; ödül verilmedi, hak harcanmadı.");
        }

        private void Load()
        {
            if (_loading || _showing || _ad != null || !_initialized) return;
            if (string.IsNullOrEmpty(_unitId)) return;

            _loading = true;
            RewardedAd.Load(_unitId, new AdRequest(),
                (ad, error) => MobileAdsEventExecutor.ExecuteInUpdate(() => OnLoaded(ad, error)));
        }

        private void OnLoaded(RewardedAd ad, LoadAdError error)
        {
            _loading = false;

            if (error != null || ad == null)
            {
                // No fill and no network look identical from here. Back off, so a session spent in
                // airplane mode does not hammer the SDK for its whole length.
                _retryLeft = _retryDelay;
                _retryDelay = Mathf.Min(_retryDelay * 2f, MaxRetrySeconds);
                Debug.LogWarning("[Ads] ödüllü reklam yüklenemedi: " +
                                 (error != null ? error.GetMessage() : "reklam dönmedi"));
                return;
            }

            _retryDelay = FirstRetrySeconds;
            _ad = ad;

            if (_pendingReward == null) return;

            Action waiting = _pendingReward;
            _pendingReward = null;
            Show(waiting);
        }

        private void Show(Action onReward)
        {
            RewardedAd ad = _ad;
            _ad = null;                 // one show per ad; the next is loaded once this one closes
            _showing = true;
            _pendingReward = null;

            // Unity keeps running behind an Android fullscreen ad, so without this the soundtrack
            // plays over it. AudioListener rather than AudioService: the mix is the player's saved
            // setting and has to come back exactly as they left it.
            ad.OnAdFullScreenContentOpened += () =>
                MobileAdsEventExecutor.ExecuteInUpdate(() => AudioListener.pause = true);

            ad.OnAdFullScreenContentClosed += () =>
                MobileAdsEventExecutor.ExecuteInUpdate(() => Finish(ad, null));

            ad.OnAdFullScreenContentFailed += error =>
                MobileAdsEventExecutor.ExecuteInUpdate(() => Finish(ad, error));

            ad.Show(reward => MobileAdsEventExecutor.ExecuteInUpdate(() =>
            {
                if (onReward != null) onReward();
            }));
        }

        /// <summary>Tears the shown ad down and lines the next one up. Reached whether it closed
        /// normally or never managed to present.</summary>
        private void Finish(RewardedAd ad, AdError error)
        {
            AudioListener.pause = false;
            _showing = false;
            if (error != null) Debug.LogWarning("[Ads] ödüllü reklam gösterilemedi: " + error.GetMessage());
            ad.Destroy();
            Load();
        }
    }
}
