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
    /// reports it. The 2× button pays the SAME amount a second time, so the total is double. Clearing
    /// <see cref="OfflineReport.Pending"/> is what stops it reappearing when the player sails between
    /// islands and this screen is rebuilt.
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

        private OfflineReport _report;
        private WalletService _wallet;
        private IAdService _ad;
        private float _delay;
        private bool _shown;

        private void Start()
        {
            _report = ServiceLocator.Get<OfflineReport>();
            _wallet = ServiceLocator.Get<WalletService>();
            _ad = ServiceLocator.Get<IAdService>();
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
            if (adButton != null) adButton.gameObject.SetActive(_ad != null && _ad.Available);
            panelRoot.SetActive(true);
            var audio = ServiceLocator.Get<AudioService>();
            if (audio != null) audio.Play(SoundId.Coin);
        }

        private void OnDouble()
        {
            if (_ad == null || !_ad.Available) return;
            _ad.ShowRewarded(() =>
            {
                if (_wallet != null && _report != null) _wallet.AddCash(_report.Amount);
                Dismiss();
            });
        }

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
        /// The offline rule, stated plainly. Efficiency is always shown — at 50%, an hour away pays
        /// half an hour of production, and a player who does the arithmetic on an unlabelled number
        /// concludes the game shorted them. The cap half only appears when it actually bit.
        ///
        /// Deliberately the same two numbers the GECE VARDİYASI offer improves ("ÇEVRİMDIŞI 8 SAAT",
        /// "VERİM %50 → %75"), so the offer reads as an upgrade to a rule the player has already seen.
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
