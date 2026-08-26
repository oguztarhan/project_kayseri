using System;
using System.Runtime.InteropServices;
#if UNITY_IOS && !UNITY_EDITOR
using AOT;
#endif
using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// App Tracking Transparency köprüsü. AdMob IDFA'ya uzandığı için Apple bu izni şart koşuyor
    /// (Guideline 5.1.2(i)); izin sorulmadan reklam SDK'sı çalıştırmak doğrudan ret sebebi.
    ///
    /// İstek yalnız uygulama ön planda ve etkinken sorulabilir: erken çağrılırsa iOS diyaloğu hiç
    /// göstermeden "reddedildi" döner ve durum kalıcı olur. Bu yüzden <see cref="UmpConsentService"/>
    /// çağrıyı ilk kareden sonra, Tick içinden yapar.
    ///
    /// iOS dışında her şey <see cref="Authorized"/> döner; Android'de TCF dizesi tek yetkidir.
    /// </summary>
    public static class IOSTracking
    {
        public const int NotDetermined = 0;
        public const int Restricted = 1;
        public const int Denied = 2;
        public const int Authorized = 3;

#if UNITY_IOS && !UNITY_EDITOR
        private delegate void StatusCallback(int status);

        [DllImport("__Internal")] private static extern int _IMTTrackingStatus();
        [DllImport("__Internal")] private static extern void _IMTRequestTracking(StatusCallback callback);
        [DllImport("__Internal")] private static extern string _IMTUserDefaultsString(string key);

        // Alan olarak tutulmazsa IL2CPP delegeyi toplar ve yerel taraf çöken bir işaretçiyi çağırır.
        private static readonly StatusCallback Bridge = OnStatus;
        private static Action<int> _pending;

        [MonoPInvokeCallback(typeof(StatusCallback))]
        private static void OnStatus(int status)
        {
            Debug.Log("[ATT] izin durumu: " + status);
            Action<int> done = _pending;
            _pending = null;
            if (done != null) done(status);
        }

        public static int Status => _IMTTrackingStatus();

        public static string UserDefaults(string key) => _IMTUserDefaultsString(key);

        public static void Request(Action<int> onDone)
        {
            int status = _IMTTrackingStatus();
            if (status != NotDetermined) { if (onDone != null) onDone(status); return; }
            _pending = onDone;
            _IMTRequestTracking(Bridge);
        }
#else
        public static int Status => Authorized;

        public static string UserDefaults(string key) => null;

        public static void Request(Action<int> onDone) { if (onDone != null) onDone(Authorized); }
#endif
    }
}
