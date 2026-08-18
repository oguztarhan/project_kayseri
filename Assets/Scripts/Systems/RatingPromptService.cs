using System;
using Game.Core;
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// Device-local scheduling for the store-rating card. The first request is tied to a positive
    /// contract moment; postponing creates a strict 48-hour cooldown which survives restarts.
    /// </summary>
    public sealed class RatingPromptService
    {
        public const int RequiredContractSuccesses = 3;
        public const int RequiredSessions = 3;
        public const long RequiredInstallAgeSeconds = 2L * 24L * 60L * 60L;
        public const long PostponeSeconds = 2L * 24L * 60L * 60L;

        private const string FirstSeenKey = "rating_prompt_first_seen";
        private const string SessionsKey = "rating_prompt_sessions";
        private const string SuccessesKey = "rating_prompt_contract_successes";
        private const string NextRequestKey = "rating_prompt_next_request";
        private const string CompletedKey = "rating_prompt_completed";

        private readonly Func<long> _now;
        private readonly IRatingPromptStore _store;
        private readonly IAnalytics _analytics;
        private bool _requestOutstanding;

        public event Action Requested;

        public RatingPromptService(TimeService time, IAnalytics analytics)
            : this(time != null ? (Func<long>)time.NowUnix : UtcNow,
                   new PlayerPrefsRatingPromptStore(), analytics) { }

        public RatingPromptService(Func<long> now, IRatingPromptStore store, IAnalytics analytics = null)
        {
            _now = now ?? UtcNow;
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _analytics = analytics;

            long current = _now();
            if (_store.GetLong(FirstSeenKey, 0L) <= 0L)
                _store.SetLong(FirstSeenKey, current);
            _store.SetInt(SessionsKey, _store.GetInt(SessionsKey, 0) + 1);
            _store.Save();
        }

        public bool IsCompleted => _store.GetInt(CompletedKey, 0) != 0;

        /// <summary>Called only after a contract reward has actually been claimed.</summary>
        public bool RecordContractSuccess()
        {
            if (IsCompleted) return false;
            _store.SetInt(SuccessesKey, _store.GetInt(SuccessesKey, 0) + 1);
            _store.Save();
            return TryRequest(false);
        }

        /// <summary>
        /// Lets the UI restore a postponed card once its 48-hour cooldown is over. It deliberately
        /// cannot create the first request: that one must follow a successful contract.
        /// </summary>
        public bool TryRequestPostponed()
        {
            if (_store.GetLong(NextRequestKey, 0L) <= 0L) return false;
            return TryRequest(true);
        }

        public void Postpone()
        {
            _store.SetLong(NextRequestKey, _now() + PostponeSeconds);
            _store.Save();
            _requestOutstanding = false;
            _analytics?.Log("rating_prompt_later", "cooldown_days", 2);
        }

        public void Complete()
        {
            _store.SetInt(CompletedKey, 1);
            _store.SetLong(NextRequestKey, 0L);
            _store.Save();
            _requestOutstanding = false;
            _analytics?.Log("rating_prompt_rate");
        }

        private bool TryRequest(bool postponedOnly)
        {
            if (_requestOutstanding || IsCompleted) return false;
            long now = _now();
            long firstSeen = _store.GetLong(FirstSeenKey, now);
            long next = _store.GetLong(NextRequestKey, 0L);
            if (postponedOnly && next <= 0L) return false;
            if (_store.GetInt(SuccessesKey, 0) < RequiredContractSuccesses
                || _store.GetInt(SessionsKey, 0) < RequiredSessions
                || now - firstSeen < RequiredInstallAgeSeconds
                || now < next)
                return false;

            _requestOutstanding = true;
            _analytics?.Log("rating_prompt_requested");
            Requested?.Invoke();
            return true;
        }

        private static long UtcNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public interface IRatingPromptStore
    {
        int GetInt(string key, int fallback);
        long GetLong(string key, long fallback);
        void SetInt(string key, int value);
        void SetLong(string key, long value);
        void Save();
    }

    public sealed class PlayerPrefsRatingPromptStore : IRatingPromptStore
    {
        public int GetInt(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);

        public long GetLong(string key, long fallback)
        {
            string value = PlayerPrefs.GetString(key, null);
            return long.TryParse(value, out long parsed) ? parsed : fallback;
        }

        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void SetLong(string key, long value) => PlayerPrefs.SetString(key, value.ToString());
        public void Save() => PlayerPrefs.Save();
    }
}
