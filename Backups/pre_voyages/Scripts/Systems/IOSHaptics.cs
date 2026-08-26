using System.Runtime.InteropServices;

namespace Game.Systems
{
    /// <summary>
    /// iOS titreşim köprüsü. Android'in aksine süre ve şiddet ayarlanmaz; sistemin üç ağırlığından
    /// biri seçilir. Düşük Güç Modunda ve sistemin "Sistem Titreşimi" anahtarı kapalıyken iOS geri
    /// bildirimi sessizce yutar — ayarlardaki anahtar yine de dürüst kalır, çünkü oyunun kendi
    /// tercihi ayrı tutulur.
    /// </summary>
    public static class IOSHaptics
    {
        public const int Light = 0;
        public const int Medium = 1;
        public const int Heavy = 2;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void _IMTHapticPrepare(int style);
        [DllImport("__Internal")] private static extern void _IMTHapticImpact(int style);
        [DllImport("__Internal")] private static extern void _IMTHapticDouble();

        public static void Prepare(int style) => _IMTHapticPrepare(style);

        public static void Impact(int style) => _IMTHapticImpact(style);

        public static void Double() => _IMTHapticDouble();
#else
        public static void Prepare(int style) { }

        public static void Impact(int style) { }

        public static void Double() { }
#endif
    }
}
