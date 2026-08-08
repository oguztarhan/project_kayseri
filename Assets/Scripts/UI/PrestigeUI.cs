using Game.Core;
using Game.Data;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The prestige screen (GDD §8, Figma "ekran_prestij"): cash the run in for Investors — a permanent
    /// global income multiplier — and start the upgrades again from zero. Editor-authored; the whole
    /// hierarchy lives in the UI_Prestij prefab and every reference is wired in the Inspector.
    ///
    /// The screen is built as one argument top to bottom: what you become (hero medallion), how many
    /// you get (gain chip), the only number that matters (multiplier now → after), what you lose vs
    /// what you keep (the trade tray), how close you are (threshold bar), then the irreversible button.
    ///
    /// Confirming is deliberately two taps — it wipes every upgrade the player has bought. The islands
    /// themselves are NOT taken away: re-buying the archipelago every run would be busywork, so a
    /// prestige resets what you built on the islands and keeps the islands.
    /// </summary>
    public sealed class PrestigeUI : MonoBehaviour
    {
        [Header("Panel (UI_Prestij prefabında bağlı)")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;

        [Header("Değerler")]
        [Tooltip("Bu prestijde kazanılacak yatırımcı sayısı.")]
        [SerializeField] private TMP_Text gainText;
        [SerializeField] private TMP_Text multiplierNowText;
        [SerializeField] private TMP_Text multiplierAfterText;

        [Header("Eşik çubuğu")]
        [Tooltip("Dolgu alanı — genişliği ilerlemeye göre değişir.")]
        [SerializeField] private RectTransform barFillArea;
        [SerializeField] private TMP_Text barNoteText;

        [Header("Prestij butonu")]
        [SerializeField] private Button prestigeButton;
        [SerializeField] private Image prestigeButtonImage;
        [SerializeField] private Sprite ctaLive;     // btn_prestij
        [SerializeField] private Sprite ctaLocked;   // btn_prestij_pasif
        [SerializeField] private TMP_Text ctaLabel;

        [SerializeField] private string mainSceneName = "Main";

        private PrestigeService _prestige;
        private SaveData _data;
        private float _barFullWidth;
        private bool _armed;

        private void Start()
        {
            _prestige = ServiceLocator.Get<PrestigeService>();
            _data = ServiceLocator.Get<SaveData>();
            if (barFillArea != null) _barFullWidth = barFillArea.rect.width;

            if (closeButton != null) closeButton.onClick.AddListener(Hide);
            if (prestigeButton != null) prestigeButton.onClick.AddListener(OnConfirm);

            if (panelRoot != null) panelRoot.SetActive(false);
            UiPanelSound.Attach(panelRoot);   // panel kapatıldıktan SONRA — açılış sesi boot'ta çalmasın
        }

        public void Toggle()
        {
            if (panelRoot == null) return;
            if (panelRoot.activeSelf) { Hide(); return; }
            Open();
        }

        /// <summary>Shows the screen with the current payout filled in.</summary>
        public void Open()
        {
            if (_prestige == null || panelRoot == null) return;
            _armed = false;
            Refresh();
            panelRoot.SetActive(true);
        }

        public void Hide()
        {
            _armed = false;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Refresh()
        {
            BigDouble pending = _prestige.PendingInvestors();
            bool ready = _prestige.CanPrestige();

            if (gainText != null) gainText.text = string.Format(Loc.T("prestij.yatirimci_kazanc"), NumberFormatter.Format(pending));
            if (multiplierNowText != null) multiplierNowText.text = Multiplier(_prestige.IncomeMultiplier);
            if (multiplierAfterText != null) multiplierAfterText.text = Multiplier(_prestige.MultiplierAfterPrestige());

            double lifetime = _prestige.LifetimeCash.ToDouble();
            double threshold = _prestige.Threshold;
            int islandsLeft = _prestige.IslandsStillNeeded;
            if (barFillArea != null)
            {
                float p = Mathf.Clamp01((float)_prestige.UnlockProgress01);
                barFillArea.sizeDelta = new Vector2(_barFullWidth * p, barFillArea.sizeDelta.y);
            }
            // Which gate is shut decides what the line says. Cash is met long before the islands are,
            // so asking for money the player already has would print a negative number under a full bar.
            if (barNoteText != null)
                barNoteText.text = ready ? Loc.T("prestij.hazir")
                    : islandsLeft > 0 ? string.Format(Loc.T("prestij.ada_gerek"), islandsLeft)
                    : string.Format(Loc.T("prestij.esik"), NumberFormatter.Format(new BigDouble(threshold - lifetime)));

            if (prestigeButton != null) prestigeButton.interactable = ready;
            if (prestigeButtonImage != null)
            {
                prestigeButtonImage.sprite = ready ? ctaLive : ctaLocked;
                // Disabled tint takılı kalabiliyor; doğru rengi anında bas.
                prestigeButtonImage.CrossFadeColor(Color.white, 0f, true, true);
            }
            if (ctaLabel != null) ctaLabel.text = Loc.T(ready ? "prestij.yap" : "prestij.henuz_degil");
        }

        /// <summary>
        /// Invariant culture on purpose: a Turkish-locale machine renders "×1,35", which reads as a
        /// thousands separator next to every other number on the screen.
        /// </summary>
        private static string Multiplier(double value)
            => "×" + value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        private void OnConfirm()
        {
            if (_prestige == null || !_prestige.CanPrestige()) return;
            if (!_armed)
            {
                // Two taps: this throws away every upgrade the player has bought.
                _armed = true;
                if (ctaLabel != null) ctaLabel.text = Loc.T("prestij.onayla");
                return;
            }

            _prestige.DoPrestige();

            // Sahne yeniden yükleniyor ama ses sunucusu sahneler arası yaşıyor, o yüzden kesilmez.
            var audio = ServiceLocator.Get<AudioService>();
            if (audio != null) audio.Play(SoundId.PhaseUp);
            var haptic = ServiceLocator.Get<HapticService>();
            if (haptic != null) haptic.Heavy();

            // Retire the live operation before touching the save. Scene teardown fires CoalOperation's
            // OnDisable, which persists that island's measured rate — so clearing the rates and *then*
            // reloading let the pre-prestige number write itself straight back into the list that had
            // just been emptied. Disabling here puts that write before the wipe instead of after it.
            var ops = FindObjectsByType<CoalOperation>(FindObjectsSortMode.None);
            for (int i = 0; i < ops.Length; i++) ops[i].enabled = false;

            // PrestigeService clears stationLevels, which is the single-mountain schema. The archipelago
            // keeps its upgrades in islandLevels, so without this the reset would take the player's cash
            // and leave every island fully upgraded.
            if (_data != null)
            {
                _data.islandLevels.Clear();
                // The idle islands' measured rates go with the levels that produced them; leaving them
                // would have every island you are not standing on keep paying its pre-prestige number.
                _data.islandRates.Clear();
                ServiceLocator.Get<SaveService>()?.Save(_data);
            }

            // Reloading is the reset: each CoalOperation reads its levels in Start, so re-running Start on
            // all eight is both simpler and safer than trying to walk them back in place.
            Hide();
            SceneManager.LoadScene(mainSceneName, LoadSceneMode.Single);
        }
    }
}
