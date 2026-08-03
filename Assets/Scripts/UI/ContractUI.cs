using Game.Core;
using Game.Data;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The contract screen (GDD §9, Figma "ekran_kontrat"): the rolling delivery order — hit the target
    /// before the clock runs out, claim the bonus, and a slightly bigger one starts. Editor-authored;
    /// the hierarchy lives in the UI_Kontrat prefab and every reference is wired in the Inspector.
    ///
    /// One card, two states. Running it shows the target, the clock and what the bonus pays; the moment
    /// the goal is met the same card turns green, the reward chips give way to the claim button and the
    /// clock stops — <see cref="ContractService"/> holds a won contract open indefinitely, so there is
    /// no way to lose a reward by not looking at this screen.
    ///
    /// The service was already ticking (from <see cref="HudUI"/>) with nothing on screen to show for it;
    /// this is the first thing that renders it.
    /// </summary>
    public sealed class ContractUI : MonoBehaviour
    {
        [Header("Panel (UI_Kontrat prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;

        [Header("Kontrat kartı")]
        [SerializeField] private Image cardImage;
        [Tooltip("Kontrat sürerken kullanılan kart görseli.")]
        [SerializeField] private Sprite cardRunning;
        [Tooltip("Hedef tutunca kullanılan yeşil kart görseli.")]
        [SerializeField] private Sprite cardDone;
        [SerializeField] private GameObject doneBadge;
        [Tooltip("Kazanılan / hedef.")]
        [SerializeField] private TMP_Text targetText;
        [SerializeField] private GameObject timerChip;
        [SerializeField] private TMP_Text timerText;

        [Header("İlerleme çubuğu")]
        [Tooltip("Dolgu alanı — genişliği ilerlemeye göre değişir.")]
        [SerializeField] private RectTransform barFillArea;

        [Header("Ödül satırı (kontrat sürerken)")]
        [SerializeField] private GameObject rewardRow;
        [SerializeField] private TMP_Text rewardCashText;
        [SerializeField] private TMP_Text rewardGemsText;

        [Header("Topla (kontrat bitince)")]
        [SerializeField] private Button claimButton;
        [SerializeField] private TMP_Text claimLabel;

        [Header("Sıradaki yuva")]
        [SerializeField] private TMP_Text streakText;

        [Tooltip("Sayaç akarken ekranın yenilenme aralığı (saniye).")]
        [SerializeField] private float refreshInterval = 0.1f;

        private ContractService _contract;
        private float _barFullWidth;
        private float _timer;

        private void Start()
        {
            _contract = ServiceLocator.Get<ContractService>();
            if (barFillArea != null) _barFullWidth = barFillArea.rect.width;

            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (claimButton != null) claimButton.onClick.AddListener(OnClaim);

            if (panelRoot != null) panelRoot.SetActive(false);
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        private void Update()
        {
            if (panelRoot == null || !panelRoot.activeSelf) return;
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;
            Refresh();
        }

        public void Toggle()
        {
            if (panelRoot == null) return;
            if (panelRoot.activeSelf) { Hide(); return; }
            Open();
        }

        public void Open()
        {
            if (_contract == null || panelRoot == null) return;
            Refresh();
            panelRoot.SetActive(true);
        }

        public void Hide()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Refresh()
        {
            if (_contract == null) return;
            bool done = _contract.Claimable;

            if (cardImage != null) cardImage.sprite = done ? cardDone : cardRunning;
            if (doneBadge != null) doneBadge.SetActive(done);
            if (timerChip != null) timerChip.SetActive(!done);
            if (rewardRow != null) rewardRow.SetActive(!done);
            if (claimButton != null) claimButton.gameObject.SetActive(done);

            if (targetText != null)
                targetText.text = done
                    ? Loc.T("kontrat.hedef_tuttu")
                    : "$" + NumberFormatter.Format(_contract.Earned) + " / $" + NumberFormatter.Format(_contract.Target);
            if (timerText != null) timerText.text = ClockText(_contract.SecondsLeft);

            if (barFillArea != null)
                barFillArea.sizeDelta = new Vector2(_barFullWidth * (float)_contract.Progress01, barFillArea.sizeDelta.y);

            if (rewardCashText != null) rewardCashText.text = "$" + NumberFormatter.Format(_contract.Reward);
            if (rewardGemsText != null) rewardGemsText.text = "+" + _contract.RewardGems;
            if (claimLabel != null) claimLabel.text = Loc.T("ortak.odulu_al");

            if (streakText != null)
                streakText.text = _contract.Streak > 0
                    ? string.Format(Loc.T("kontrat.seri"), _contract.Streak)
                    : Loc.T("kontrat.ilk");
        }

        private void OnClaim()
        {
            if (_contract == null || !_contract.Claimable) return;
            _contract.Claim();
            var audio = ServiceLocator.Get<AudioService>();
            if (audio != null) audio.Play(SoundId.Coin);
            Refresh();
        }

        /// <summary>"1:05" / "0:47" — seconds always two digits so the clock does not jitter in width.</summary>
        public static string ClockText(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = Mathf.CeilToInt(seconds);
            int m = total / 60;
            int s = total - m * 60;
            return m + ":" + (s < 10 ? "0" + s : s.ToString());
        }
    }
}
