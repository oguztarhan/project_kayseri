using UnityEngine;

namespace Game.Systems
{
    /// <summary>
    /// Haptics (GDD §13.5). Three weights: a tap you barely notice, a purchase you do, and a double
    /// beat for the moments the island changes shape.
    ///
    /// <see cref="Handheld.Vibrate"/> alone cannot do this — it is a fixed ~250 ms buzz, far too heavy
    /// to put under a button. Android 8 (API 26) added <c>VibrationEffect</c>, which takes a duration
    /// and an amplitude, so that is what runs on anything modern. Older devices fall back to
    /// <see cref="Handheld.Vibrate"/>; that call also has to stay in the source, because it is what
    /// makes Unity write the VIBRATE permission into the generated manifest.
    ///
    /// <see cref="Enabled"/> is mutable so the settings screen can flip it at runtime. Silent in the
    /// editor and on any device that refuses to hand over a vibrator.
    /// </summary>
    public sealed class HapticService
    {
        public bool Enabled { get; set; }

        public HapticService(bool enabled) { Enabled = enabled; }

        /// <summary>Buttons, taps, ticks.</summary>
        public void Light() => OneShot(12L, 90);

        /// <summary>Something was bought or claimed.</summary>
        public void Medium() => OneShot(22L, 160);

        /// <summary>Double beat — a district rebuilt, an island maxed.</summary>
        public void Heavy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Enabled || !Resolve()) return;
            if (_api < 26 || _effects == null) { Handheld.Vibrate(); return; }

            // bekle, vur, bekle, daha sert vur — tekrar yok
            long[] timings = { 0L, 26L, 45L, 34L };
            int[] amplitudes = { 0, 200, 0, 255 };
            using (var effect = _effects.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1))
                _vibrator.Call("vibrate", effect);
#endif
        }

        private void OneShot(long milliseconds, int amplitude)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Enabled || !Resolve()) return;
            if (_api < 26 || _effects == null) { Handheld.Vibrate(); return; }

            using (var effect = _effects.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, amplitude))
                _vibrator.Call("vibrate", effect);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject _vibrator;
        private static AndroidJavaClass _effects;
        private static int _api;
        private static bool _resolved;

        private static bool Resolve()
        {
            if (_resolved) return _vibrator != null;
            _resolved = true;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    _api = version.GetStatic<int>("SDK_INT");

                if (_api >= 26) _effects = new AndroidJavaClass("android.os.VibrationEffect");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Haptic] Titreşim servisi alınamadı: " + e.Message);
                _vibrator = null;
            }
            return _vibrator != null;
        }
#endif
    }
}
