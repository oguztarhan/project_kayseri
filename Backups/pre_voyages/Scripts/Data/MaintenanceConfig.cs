using Game.Core;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Bakım tuning: how fast an unplayed island wears, how far it can fall, and what putting it
    /// right costs. Designer-editable.
    ///
    /// Every field here feeds <see cref="Maintenance.Tuning"/> and nothing else — the maths lives in
    /// <see cref="Maintenance"/> so it can be tested without a scene, and this is only the dial panel
    /// in front of it.
    /// </summary>
    [CreateAssetMenu(fileName = "MaintenanceConfig", menuName = "Ore Empire/Maintenance Config", order = 11)]
    public sealed class MaintenanceConfig : ScriptableObject
    {
        [Tooltip("Kapalıysa hiçbir şey yıpranmaz ve onarım arayüzü hiç görünmez. Özelliği tek " +
                 "anahtarla kapatmak için.")]
        [SerializeField] private bool enabled = true;

        [Header("Yıpranma")]
        [Tooltip("Her ayrılığın ilk bu kadar saati bedava. Günde bir açan oyuncu hiç kir görmez, " +
                 "ve oyun açıkken hiçbir şey eskimez.")]
        [SerializeField, Min(0f)] private float graceHours = 8f;

        [Tooltip("Bir istasyonu tamamdan tabana indiren ayrılık süresi, oyuncunun çıktığı andan " +
                 "sayılır. Yıpranma bunun kendisi kadar değil, bunun eksi tolerans kadarı boyunca " +
                 "yayılır.")]
        [SerializeField, Min(1f)] private float decayHours = 72f;

        [Tooltip("Bir istasyonun düşebileceği en kötü durum. Yokluk bir yerde durur, dipsiz değildir. " +
                 "0,55 = en kötü ihtimalle ada %55 hızında çalışır.")]
        [SerializeField, Range(0.1f, 1f)] private float floor = 0.55f;

        [Header("Onarım")]
        [Tooltip("Tek istasyonun tam onarımı, adanın KENDİ dakikalık kazancı cinsinden. Sekiz " +
                 "istasyon x 1,25 = tamamen bakımsız bir adayı toparlamak on dakikalık üretim eder.")]
        [SerializeField, Min(0f)] private float repairIncomeMinutes = 1.25f;

        [Tooltip("Ekibin sahada geçirdiği süre: küçük bir çizik ve tam harabe. Gerçek hasar ikisinin " +
                 "arasına düşer.")]
        [SerializeField, Min(1f)] private float repairSecondsMin = 20f;
        [SerializeField, Min(1f)] private float repairSecondsMax = 180f;

        [Header("Bakım primi")]
        [Tooltip("Adanın TAMAMI onarıldığında verilen çarpan. 1,10 = %10 hızlı. Aynı sayılar, ters " +
                 "çevrilmiş çerçeve: oyuncu ceza değil ödül görsün.")]
        [SerializeField, Range(1f, 2f)] private float bonusMultiplier = 1.10f;

        [Tooltip("Primin süresi, dakika.")]
        [SerializeField, Min(0f)] private float bonusMinutes = 10f;

        public bool Enabled => enabled;

        /// <summary>These dials as the plain struct the maths and the service both take.</summary>
        public Maintenance.Tuning Tuning => new Maintenance.Tuning
        {
            GraceHours = graceHours,
            // A decay window shorter than the grace one would wear an island out before the free
            // hours were up, which the maths reads as a divide by a negative span. Held apart here,
            // where a designer can see it, rather than defended in the formula.
            DecayHours = decayHours > graceHours ? decayHours : graceHours + 1f,
            Floor = floor,
            RepairIncomeMinutes = repairIncomeMinutes,
            RepairSecondsMin = repairSecondsMin,
            RepairSecondsMax = repairSecondsMax > repairSecondsMin ? repairSecondsMax : repairSecondsMin,
            BonusMultiplier = bonusMultiplier,
            BonusMinutes = bonusMinutes,
        };
    }
}
