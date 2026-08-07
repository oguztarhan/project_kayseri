using Game.Core;
using Game.Data;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The "while you were away" screen (Figma "ekran_hosgeldin"): the gold pile medallion, how long
    /// you were gone, what it earned, a rewarded-ad button that doubles it and a plain collect.
    /// Editor-authored — the hierarchy lives in the UI_HosGeldin prefab and every reference is wired
    /// in the Inspector, so medallion, rows and buttons are all tunable from the hierarchy.
    ///
    /// <see cref="GameBootstrap"/> already paid the base amount into the wallet at launch; this only
    /// reports it. The ad button pays a FRACTION of that amount on top. Clearing
    /// <see cref="OfflineReport.Pending"/> is what stops it reappearing when the player sails between
    /// islands and this screen is rebuilt.
    ///
    /// That fraction used to be a full second payment, uncapped and with no charge check — the single
    /// largest number in the whole ad economy. A twice-a-day player collects 5.6 income-hours without
    /// ads and this button alone was worth another 4.9, so an ad-watcher finished the island ladder in
    /// half the designed time. It is now charged out of the same daily table the ad screen's three
    /// slots use (<see cref="FreeRewardService"/>), and pays a fraction rather than a double.
    /// </summary>
    public sealed class WelcomeBackUI : MonoBehaviour
    {
        [Header("Panel (UI_HosGeldin prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button collectButton;
        [SerializeField] private Button adButton;

        [Header("Yazılar")]
        [SerializeField] private TMP_Text amountText;
        [SerializeField] private TMP_Text durationText;
        [Tooltip("Çevrimdışı kuralını (verim ve süre sınırı) açıkça yazar.")]
        [SerializeField] private TMP_Text capNoteText;
        [Tooltip("Kural satırındaki ayraç. Fontta yoksa otomatik olarak tireye düşer.")]
        [SerializeField] private string noteSeparator = " · ";

        [Tooltip("Ada kamerası yerine oturana kadar bekle, sonra aç.")]
        [SerializeField] private float appearDelay = 0.6f;

        [Header("Ödüllü reklam")]
        [Tooltip("Düğmenin günde kaç kez kullanılabileceği. Oyuncu günde iki kez döndüğü için 2, " +
                 "pratikte \"her dönüşte bir kez\" demektir; 1 yaparsan ikinci dönüşte düğme hiç görünmez.")]
        [SerializeField, Min(0)] private int adChargesPerDay = 2;
        [Tooltip("Reklamın çevrimdışı kazancın yüzde kaçını EK olarak ödediği. 0,5 = %50 fazlası, " +
                 "1 = ikiye katlar. Düğmenin yazısı bu değerden üretilir, ayrıca güncellemen gerekmez.")]
        [SerializeField, Range(0f, 2f)] private float adBonusFraction = 0.5f;

        /// <summary>
        /// Slot id in the shared daily table. Deliberately not one of the ad screen's three, so the
        /// "+1 hak" perk the store sells does not quietly hand out extra doubles here as well.
        /// </summary>
        private const string AdSlotId = "hosgeldin";

        private OfflineReport _report;
        private WalletService _wallet;
        private IAdService _ad;
        private FreeRewardService _free;
        private SaveService _save;
        private SaveData _data;
        private TMP_Text _adLabel;
        private float _delay;
        private bool _shown;

        private void Start()
        {
            _report = ServiceLocator.Get<OfflineReport>();
            _wallet = ServiceLocator.Get<WalletService>();
            _ad = ServiceLocator.Get<IAdService>();
            _free = ServiceLocator.Get<FreeRewardService>();
            _save = ServiceLocator.Get<SaveService>();
            _data = ServiceLocator.Get<SaveData>();
            // The caption is computed from adBonusFraction, so the label carries no LocalizedText —
            // the two would overwrite each other. Found once here rather than wired in the Inspector,
            // the way PremiumStoreUI finds the amount label on its offer cards.
            if (adButton != null) _adLabel = adButton.GetComponentInChildren<TMP_Text>(true);
            _delay = appearDelay;

            if (closeButton != null) closeButton.onClick.AddListener(Dismiss);
            if (collectButton != null) collectButton.onClick.AddListener(Dismiss);
            if (adButton != null) adButton.onClick.AddListener(OnDouble);

            if (panelRoot != null) panelRoot.SetActive(false);
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        private void Update()
        {
            if (_shown || _report == null || !_report.Pending) return;
            _delay -= Time.unscaledDeltaTime;
            if (_delay > 0f) return;
            Show();
        }

        public void Show()
        {
            if (_report == null || panelRoot == null) return;
            _shown = true;
            if (amountText != null) amountText.text = "$" + NumberFormatter.Format(_report.Amount);
            if (durationText != null) durationText.text = DurationText(_report.AwaySeconds);
            if (capNoteText != null) capNoteText.text = RuleText();
            if (adButton != null)
            {
                // Out of charges hides the button rather than dimming it: this screen is dismissed in
                // one tap and a dead control on it reads as the game being broken, not as a limit.
                bool offer = _ad != null && _ad.Available && CanDouble();
                adButton.gameObject.SetActive(offer);
                if (offer && _adLabel != null)
                    _adLabel.text = string.Format(Loc.T("hosgeldin.bonus"),
                                                  Mathf.RoundToInt(adBonusFraction * 100f));
            }
            panelRoot.SetActive(true);
            var audio = ServiceLocator.Get<AudioService>();
            if (audio != null) audio.Play(SoundId.Coin);
        }

        private void OnDouble()
        {
            if (_ad == null || !_ad.Available || !CanDouble()) return;
            _ad.ShowRewarded(() =>
            {
                if (_wallet != null && _report != null)
                    _wallet.AddCash(_report.Amount * new BigDouble(adBonusFraction));
                if (_free != null) _free.Consume(AdSlotId);
                // Charges are the thing a player would reload the app to get back; write them now.
                if (_save != null && _data != null) _save.Save(_data);
                Dismiss();
            });
        }

        /// <summary>
        /// No cooldown is passed: the panel only exists once per return, so the return itself is the
        /// spacing. Charges are what stops a player who backgrounds and reopens the app all evening
        /// from collecting the bonus every time.
        /// </summary>
        private bool CanDouble() => _free == null || _free.CanWatch(AdSlotId, adChargesPerDay, 0f);

        private void Dismiss()
        {
            if (_report != null) _report.Pending = false;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>"3 SA 12 DK" / "45 DK" — under a minute still reads as 1 DK, never "0".</summary>
        private static string DurationText(long seconds)
        {
            if (seconds <= 0L) return "";
            long minutes = seconds / 60L;
            if (minutes < 1L) minutes = 1L;
            long hours = minutes / 60L;
            minutes -= hours * 60L;
            if (hours <= 0L) return string.Format(Loc.T("ortak.sure_dk"), minutes);
            return minutes > 0L ? string.Format(Loc.T("ortak.sure_sa_dk"), hours, minutes)
                                : string.Format(Loc.T("ortak.sure_sa"), hours);
        }

        /// <summary>
        /// The offline rule, stated plainly. Efficiency is always shown — at 35%, an hour away pays
        /// about twenty minutes of production, and a player who does the arithmetic on an unlabelled
        /// number concludes the game shorted them. The cap half only appears when it actually bit.
        ///
        /// Deliberately the same two numbers the GECE VARDİYASI offer improves ("ÇEVRİMDIŞI 14 SAAT",
        /// "VERİM %35 → %60"), so the offer reads as an upgrade to a rule the player has already seen.
        /// </summary>
        private string RuleText()
        {
            string s = string.Format(Loc.T("hosgeldin.verim"), Mathf.RoundToInt((float)(_report.Efficiency * 100d)));
            if (_report.CreditedSeconds > 0L && _report.AwaySeconds > _report.CreditedSeconds)
                s += Separator() + string.Format(Loc.T("hosgeldin.en_fazla"), CapText(_report.CreditedSeconds));
            return s;
        }

        private string Separator()
        {
            if (string.IsNullOrEmpty(noteSeparator)) return " - ";
            TMP_FontAsset f = capNoteText != null ? capNoteText.font : null;
            for (int i = 0; i < noteSeparator.Length; i++)
                // searchFallbacks + tryAddCharacter: dinamik atlasta henüz olmayan ama fontta
                // (ya da yedek zincirinde) bulunan glifler de geçerli sayılsın.
                if (!char.IsWhiteSpace(noteSeparator[i]) && (f == null || !f.HasCharacter(noteSeparator[i], true, true)))
                    return " - ";
            return noteSeparator;
        }

        /// <summary>Cümle içinde geçtiği için kısaltma değil, tam kelime: "2 SAAT" / "45 DAKİKA".</summary>
        private static string CapText(long seconds)
        {
            long minutes = seconds / 60L;
            long hours = minutes / 60L;
            if (hours <= 0L) return string.Format(Loc.T("hosgeldin.dakika"), minutes);
            long rest = minutes - hours * 60L;
            return rest > 0L
                ? string.Format(Loc.T("hosgeldin.saat_dakika"), hours, rest)
                : string.Format(Loc.T("hosgeldin.saat"), hours);
        }
    }
}
