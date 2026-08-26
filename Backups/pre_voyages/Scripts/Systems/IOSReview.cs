using System.Runtime.InteropServices;

namespace Game.Systems
{
    /// <summary>
    /// Apple'ın kendi değerlendirme sayfası. İşletim sistemi yılda kabaca üç gösterimle sınırlar ve
    /// hiçbir sonuç döndürmez — sayfa hiç açılmamış olabilir. Bu yüzden çağıran taraf kendi
    /// sayacını tutar (bkz. <see cref="RatingPromptService"/>) ve sonuca bağlı bir UI kurmaz.
    ///
    /// Kendi "beğendin mi?" kartımızı Apple'ın sayfasının önüne koymak HIG ihlali ve bilinen bir ret
    /// sebebi; iOS'ta kart tamamen atlanır.
    /// </summary>
    public static class IOSReview
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void _IMTRequestReview();

        public static bool Available => true;

        public static void Request() => _IMTRequestReview();
#else
        public static bool Available => false;

        public static void Request() { }
#endif
    }
}
