namespace Game.Data
{
    /// <summary>
    /// Every sound the game can ask for. Call sites name the moment, not the file — which clip plays,
    /// how loud, and how often it is allowed to repeat all live in <see cref="AudioLibrary"/>.
    /// </summary>
    public enum SoundId
    {
        None = 0,

        // arayüz
        Tap,            // her buton
        Back,           // kapat / geri
        PanelOpen,
        PanelClose,
        Denied,         // para yetmedi

        // ekonomi
        Coin,           // toplu para girişi (çevrimdışı, sözleşme)
        Sale,           // her satış — sık çalar, kısıtlıdır
        Upgrade,
        Purchase,       // mağaza / elmas
        Reward,         // günlük ödül, reklam ödülü

        // ilerleme
        PhaseUp,        // bir bölge yeniden inşa oldu
        Tick,           // seviye pipi

        // pazar avlusu
        MarketDoor,     // müşteri kapıdan girdi — sık çalar, kısıtlıdır
        MarketVip,      // VIP müşteri girdi
    }
}
