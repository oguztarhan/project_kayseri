using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The reward card the events screens show after a claim: the league's crowned reward panel, what
    /// was paid as icons and amounts, and a close button.
    ///
    /// ITS OWN CARD, NOT <see cref="RewardRevealUI"/>. That one is shared with the goals, pets and card
    /// collection screens, and restyling it would have restyled them too; the events family keeps this
    /// one so its reward moment matches the screens it pops over.
    ///
    /// PRESENTATION ONLY. It is handed a reward already paid — it has no reference to a wallet, a save
    /// or any claim — and it stays up until tapped, so a claim made mid-scroll is not missed.
    /// </summary>
    public sealed class EtkinlikOdulUI : MonoBehaviour
    {
        private const float EnterSeconds = 0.22f;

        private CanvasGroup _group;
        private RectTransform _card;
        private Text _title, _closeLabel;
        private EtkinlikOdulSatiri _reward;
        private float _shownAt;
        private bool _entering;

        public static EtkinlikOdulUI Create(RectTransform parent)
        {
            var go = new GameObject("EtkinlikOdulu", typeof(RectTransform), typeof(Image), typeof(Button),
                                    typeof(CanvasGroup), typeof(EtkinlikOdulUI));
            go.transform.SetParent(parent, false);
            RectTransform root = UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);

            Image scrim = go.GetComponent<Image>();
            scrim.sprite = UiSkin.Flat;
            scrim.type = Image.Type.Sliced;
            scrim.color = new Color(0.02f, 0.03f, 0.06f, 0.72f);

            EtkinlikOdulUI popup = go.GetComponent<EtkinlikOdulUI>();
            popup._group = go.GetComponent<CanvasGroup>();
            var dismiss = go.GetComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(popup.Hide);

            // About 670 units across, over 660 px of art: the crown rides the panel's stretching
            // top-centre slice, and at that width it is drawn at its own size — see LadderUI's reward card.
            Image card = EkranKit.Sliced(root, "Kart", LigKit.Get("odul_pano"),
                                         new Vector2(0.19f, 0.33f), new Vector2(0.81f, 0.64f), false);
            popup._card = card.rectTransform;

            popup._title = EtkinlikKit.Label(card.rectTransform, "Baslik", new Vector2(0.10f, 0.50f), new Vector2(0.90f, 0.68f),
                                             Loc.T("gorev.odul_alindi"), 40, TextAnchor.MiddleCenter, EkranKit.Ink, 20);
            popup._reward = EtkinlikOdulSatiri.Create(card.rectTransform, "Odul", new Vector2(0.08f, 0.27f),
                                                     new Vector2(0.92f, 0.49f), 42,
                                                     new Color(0.12f, 0.38f, 0.70f, 1f), TextAnchor.MiddleCenter);

            Button close = EtkinlikKit.Capsule(card.rectTransform, "Tamam", new Vector2(0.29f, 0.03f),
                                               new Vector2(0.71f, 0.20f), popup.Hide, out popup._closeLabel);
            EtkinlikKit.SetFace(close, popup._closeLabel, EtkinlikKit.Face.Claim, true);
            popup._closeLabel.text = Loc.T("lig.kapat");

            go.SetActive(false);
            return popup;
        }

        public bool Showing => gameObject.activeSelf;

        /// <summary>Shows what a claim just paid. A reward with nothing in it shows nothing.</summary>
        public void Present(long gems, int cards, long charts, string extra)
        {
            if (gems <= 0L && cards <= 0 && charts <= 0L && string.IsNullOrEmpty(extra)) return;

            _title.text = Loc.T("gorev.odul_alindi");
            _closeLabel.text = Loc.T("lig.kapat");
            _reward.Set(null, gems, cards, charts, extra);

            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            _shownAt = Time.unscaledTime;
            _entering = true;
            _group.alpha = 0f;
            _card.localScale = Vector3.one * 0.82f;

            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
            ServiceLocator.Get<HapticService>()?.Medium();
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>Only the pop-in animates; once it lands the card holds still and Update does nothing.</summary>
        private void Update()
        {
            if (!_entering) return;
            float t = Mathf.Clamp01((Time.unscaledTime - _shownAt) / EnterSeconds);
            _group.alpha = t;
            float overshoot = 1f + Mathf.Sin(t * Mathf.PI) * 0.08f;
            _card.localScale = Vector3.one * Mathf.Lerp(0.82f, overshoot, t);
            if (t < 1f) return;
            _entering = false;
            _card.localScale = Vector3.one;
        }
    }
}
