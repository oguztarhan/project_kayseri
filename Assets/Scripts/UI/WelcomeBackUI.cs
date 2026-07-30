using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The "while you were away" popup. <see cref="GameBootstrap"/> already computed offline earnings and
    /// paid them into the wallet at launch, but nothing ever told the player — the money simply appeared,
    /// which is the one moment an idle game gets to show that it kept working without you.
    ///
    /// This only reads <see cref="OfflineReport"/>; it does not pay out again. Clearing
    /// <c>Pending</c> on collect is what stops it reappearing when the player sails between islands and
    /// this screen is rebuilt.
    /// </summary>
    public sealed class WelcomeBackUI : MonoBehaviour
    {
        [SerializeField] private float appearDelay = 0.6f;   // let the island finish framing before covering it

        private OfflineReport _report;
        private GameObject _modal;
        private Text _amount;
        private float _delay;
        private bool _shown;

        private static readonly Color Scrim = new Color(0.02f, 0.04f, 0.07f, 0.82f);
        private static readonly Color Card = new Color(0.12f, 0.18f, 0.26f, 1f);
        private static readonly Color Green = new Color(0.22f, 0.66f, 0.36f, 1f);
        private static readonly Color Dim = new Color(0.62f, 0.72f, 0.82f, 1f);

        private void Start()
        {
            _report = ServiceLocator.Get<OfflineReport>();
            _delay = appearDelay;
            Build();
        }

        private void Update()
        {
            if (_shown || _report == null || !_report.Pending) return;
            _delay -= Time.unscaledDeltaTime;
            if (_delay > 0f) return;
            _amount.text = "$" + NumberFormatter.Format(_report.Amount);
            _modal.SetActive(true);
            _shown = true;
        }

        private void Build()
        {
            RectTransform root = UiBuild.Canvas(transform, "WelcomeBackCanvas", 220);
            RectTransform scrim = UiBuild.Flat(root, "Scrim", Scrim, Vector2.zero, Vector2.one);
            _modal = scrim.gameObject;

            RectTransform card = UiBuild.Flat(scrim, "Card", Card, new Vector2(0.10f, 0.36f), new Vector2(0.90f, 0.64f));

            Text title = UiBuild.Label(card, "Title", "WELCOME BACK", 46, TextAnchor.MiddleCenter);
            UiBuild.Anchor(title.rectTransform, new Vector2(0f, 0.76f), new Vector2(1f, 0.95f));
            title.color = new Color(1f, 0.92f, 0.5f);

            Text sub = UiBuild.Label(card, "Sub", "your islands kept working while you were away", 24, TextAnchor.MiddleCenter);
            UiBuild.Anchor(sub.rectTransform, new Vector2(0f, 0.62f), new Vector2(1f, 0.75f));
            sub.color = Dim;

            _amount = UiBuild.Label(card, "Amount", "", 64, TextAnchor.MiddleCenter);
            UiBuild.Anchor(_amount.rectTransform, new Vector2(0f, 0.34f), new Vector2(1f, 0.60f));
            _amount.color = new Color(0.55f, 0.95f, 0.6f);

            Button collect = UiBuild.Btn(card, "Collect", "COLLECT", UiSkin.ButtonGreen, Green, 34, Dismiss);
            UiBuild.Anchor(collect.GetComponent<RectTransform>(), new Vector2(0.18f, 0.09f), new Vector2(0.82f, 0.29f));

            _modal.SetActive(false);
        }

        private void Dismiss()
        {
            if (_report != null) _report.Pending = false;
            _modal.SetActive(false);
        }
    }
}
