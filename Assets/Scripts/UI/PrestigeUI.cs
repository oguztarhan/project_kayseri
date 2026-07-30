using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The prestige screen (GDD §8): cash the run in for Investors, a permanent global income multiplier,
    /// and start the upgrades again from zero. <see cref="PrestigeService"/> has always been able to do
    /// this; nothing in the game could ask it to, so the entire end-game loop was unreachable.
    ///
    /// Opened from the chip <see cref="MetaHud"/> shows once the run is worth cashing in. Confirming is
    /// deliberately two taps — it wipes every upgrade the player has bought.
    ///
    /// The islands themselves are NOT taken away. Buying them back every run would turn the world map
    /// into busywork rather than progress, so a prestige resets what you built on the islands and keeps
    /// the archipelago you unlocked.
    /// </summary>
    public sealed class PrestigeUI : MonoBehaviour
    {
        [SerializeField] private string mainSceneName = "Main";

        private PrestigeService _prestige;
        private SaveData _data;
        private GameObject _modal;
        private Text _headline, _detail, _confirmText;
        private Button _confirm;
        private Image _confirmBg;
        private bool _armed;

        private static readonly Color Scrim = new Color(0.04f, 0.02f, 0.07f, 0.86f);
        private static readonly Color Card = new Color(0.17f, 0.13f, 0.26f, 1f);
        private static readonly Color Violet = new Color(0.50f, 0.32f, 0.72f, 1f);
        private static readonly Color Red = new Color(0.72f, 0.26f, 0.26f, 1f);
        private static readonly Color Grey = new Color(0.24f, 0.27f, 0.32f, 1f);
        private static readonly Color Dim = new Color(0.72f, 0.68f, 0.85f, 1f);

        private void Start()
        {
            _prestige = ServiceLocator.Get<PrestigeService>();
            _data = ServiceLocator.Get<SaveData>();
            Build();
        }

        /// <summary>Shows the screen with the current payout filled in.</summary>
        public void Open()
        {
            if (_prestige == null || _modal == null) return;
            _armed = false;
            _confirmText.text = "PRESTIGE";
            _confirmBg.sprite = UiSkin.ButtonYellow;
            if (!UiSkin.HasArt) _confirmBg.color = Violet;

            BigDouble pending = _prestige.PendingInvestors();
            double after = _prestige.Investors + pending.ToDouble();
            _headline.text = "+" + NumberFormatter.Format(pending) + "  INVESTORS";
            _detail.text =
                "you hold " + NumberFormatter.Format(new BigDouble(_prestige.Investors)) + " investors" +
                // Invariant: on a Turkish-locale machine the default gives "×1,00", which reads as a
                // thousands separator next to every other number on screen.
                "   (×" + _prestige.IncomeMultiplier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " income)\n" +
                "after prestige: " + NumberFormatter.Format(new BigDouble(after)) + " investors\n\n" +
                "every station upgrade resets to level 1 and your cash goes to zero.\n" +
                "the islands you own stay yours.";
            _modal.SetActive(true);
        }

        private void Build()
        {
            RectTransform root = UiBuild.Canvas(transform, "PrestigeCanvas", 225);
            RectTransform scrim = UiBuild.Flat(root, "Scrim", Scrim, Vector2.zero, Vector2.one);
            _modal = scrim.gameObject;

            RectTransform card = UiBuild.Flat(scrim, "Card", Card, new Vector2(0.07f, 0.28f), new Vector2(0.93f, 0.72f));

            Text title = UiBuild.Label(card, "Title", "PRESTIGE", 46, TextAnchor.MiddleCenter);
            UiBuild.Anchor(title.rectTransform, new Vector2(0f, 0.86f), new Vector2(1f, 0.97f));
            title.color = new Color(1f, 0.88f, 0.55f);

            _headline = UiBuild.Label(card, "Headline", "", 54, TextAnchor.MiddleCenter);
            UiBuild.Anchor(_headline.rectTransform, new Vector2(0f, 0.68f), new Vector2(1f, 0.85f));
            _headline.color = new Color(0.78f, 0.62f, 1f);

            _detail = UiBuild.Label(card, "Detail", "", 24, TextAnchor.UpperCenter);
            UiBuild.Anchor(_detail.rectTransform, new Vector2(0.06f, 0.26f), new Vector2(0.94f, 0.66f));
            _detail.color = Dim;

            _confirm = UiBuild.Btn(card, "Confirm", "PRESTIGE", UiSkin.ButtonYellow, Violet, 32, OnConfirm);
            UiBuild.Anchor(_confirm.GetComponent<RectTransform>(), new Vector2(0.10f, 0.13f), new Vector2(0.62f, 0.24f));
            _confirmText = _confirm.GetComponentInChildren<Text>();
            _confirmBg = _confirm.GetComponent<Image>();

            Button close = UiBuild.Btn(card, "Close", "NOT YET", UiSkin.ButtonGrey, Grey, 30,
                                       () => _modal.SetActive(false));
            UiBuild.Anchor(close.GetComponent<RectTransform>(), new Vector2(0.66f, 0.13f), new Vector2(0.90f, 0.24f));

            _modal.SetActive(false);
        }

        private void OnConfirm()
        {
            if (_prestige == null) return;
            if (!_armed)
            {
                // Two taps: this throws away every upgrade the player has bought.
                _armed = true;
                _confirmText.text = "TAP AGAIN TO CONFIRM";
                _confirmBg.sprite = UiSkin.ButtonGrey;
                if (!UiSkin.HasArt) _confirmBg.color = Red;
                return;
            }

            _prestige.DoPrestige();

            // PrestigeService clears stationLevels, which is the single-mountain schema. The archipelago
            // keeps its upgrades in islandLevels, so without this the reset would take the player's cash
            // and leave every island fully upgraded.
            if (_data != null)
            {
                _data.islandLevels.Clear();
                ServiceLocator.Get<SaveService>()?.Save(_data);
            }

            // Reloading is the reset: each CoalOperation reads its levels in Start, so re-running Start on
            // all eight is both simpler and safer than trying to walk them back in place.
            _modal.SetActive(false);
            SceneManager.LoadScene(mainSceneName, LoadSceneMode.Single);
        }
    }
}
