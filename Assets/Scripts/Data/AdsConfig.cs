using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// AdMob ödüllü reklam ayarları (GDD §10). Oyunda yalnız ödüllü reklam var — geçiş (interstitial)
    /// ve banner yok, o yüzden tek bir reklam birimi dört yuvanın hepsine yetiyor.
    ///
    /// <see cref="UseTestAds"/> varsayılan olarak AÇIK ve gerçek kimlikler AdMob konsolunda hazır
    /// olana kadar açık kalmalı: geliştirme sürümünde gerçek reklam göstermek hesabın askıya
    /// alınmasıyla sonuçlanır, dolayısıyla güvenli değer bir build'in hatırlaması gereken şey değil,
    /// varsayılanın kendisi olmalı.
    /// </summary>
    [CreateAssetMenu(fileName = "AdsConfig", menuName = "Ore Empire/Ads Config", order = 17)]
    public sealed class AdsConfig : ScriptableObject
    {
        // Google'ın herkese açık demo birimleri (developers.google.com/admob/unity/test-ads). Her
        // projede aynıdırlar ve değiştirilmemeleri gerekir; bu yüzden alan değil sabit.
        private const string TestRewardedAndroid = "ca-app-pub-3940256099942544/5224354917";
        private const string TestRewardedIos = "ca-app-pub-3940256099942544/1712485313";

        [Tooltip("AÇIK: Google'ın test reklamları oynar, AdMob hesabına hiç dokunmaz. Gerçek reklam " +
                 "kimlikleri hazır olana kadar AÇIK bırak.")]
        [SerializeField] private bool useTestAds = true;

        [Header("Gerçek ödüllü reklam kimlikleri (AdMob konsolundan)")]
        [SerializeField] private string androidRewardedId = "";
        [SerializeField] private string iosRewardedId = "";

        [Header("Davranış")]
        [Tooltip("Reklam henüz yüklenmemişken oyuncu düğmeye basarsa kaç saniye beklenip " +
                 "gösterilmeye çalışılacağı. Süre dolarsa ödül verilmez ve hak harcanmaz.")]
        [SerializeField, Range(1f, 15f)] private float showTimeoutSeconds = 5f;

        public bool UseTestAds => useTestAds;
        public float ShowTimeoutSeconds => showTimeoutSeconds;

        /// <summary>
        /// Bu build'in isteyeceği reklam birimi. Gerçek alan boşsa test birimine düşer: yarım
        /// doldurulmuş bir asset hiç reklam göstermemektense test reklamı göstersin.
        /// </summary>
        public string RewardedUnitId
        {
            get
            {
                bool ios = Application.platform == RuntimePlatform.IPhonePlayer;
                string live = ios ? iosRewardedId : androidRewardedId;
                string test = ios ? TestRewardedIos : TestRewardedAndroid;
                return useTestAds || string.IsNullOrEmpty(live) ? test : live;
            }
        }
    }
}
