using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Presentation-only reward toast. It accepts an already committed receipt and has no references
    /// to the wallet, save data, or claim APIs.
    /// </summary>
    public sealed class RewardRevealUI : MonoBehaviour
    {
        private const float HoldSeconds = 1.6f;
        private CanvasGroup _group;
        private RectTransform _card;
        private Text _title;
        private Text _value;
        private Image _icon;
        private float _shownAt;

        public static RewardRevealUI Create(RectTransform parent, Sprite cardSprite, Sprite gemIcon)
        {
            var go = new GameObject("OdulSunum", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(CanvasGroup), typeof(RewardRevealUI));
            go.transform.SetParent(parent, false);
            RectTransform root = UiBuild.Anchor((RectTransform)go.transform, Vector2.zero, Vector2.one);
            Image scrim = go.GetComponent<Image>();
            scrim.sprite = UiSkin.Flat;
            scrim.type = Image.Type.Sliced;
            scrim.color = new Color(0.02f, 0.03f, 0.06f, 0.66f);

            RewardRevealUI reveal = go.GetComponent<RewardRevealUI>();
            reveal._group = go.GetComponent<CanvasGroup>();
            go.GetComponent<Button>().onClick.AddListener(reveal.Hide);

            var cardGo = new GameObject("Kart", typeof(RectTransform), typeof(Image));
            cardGo.transform.SetParent(root, false);
            reveal._card = UiBuild.Anchor((RectTransform)cardGo.transform,
                new Vector2(0.25f, 0.37f), new Vector2(0.75f, 0.63f));
            Image panel = cardGo.GetComponent<Image>();
            panel.sprite = cardSprite != null ? cardSprite : UiSkin.Panel;
            panel.type = Image.Type.Sliced;
            panel.color = cardSprite != null || UiSkin.HasArt ? Color.white
                : new Color(0.15f, 0.20f, 0.31f, 1f);
            panel.raycastTarget = false;

            reveal._title = UiBuild.Label(reveal._card, "Baslik", Loc.T("gorev.odul_alindi"), 34,
                TextAnchor.MiddleCenter);
            UiBuild.Anchor((RectTransform)reveal._title.transform,
                new Vector2(0.08f, 0.57f), new Vector2(0.92f, 0.91f));
            reveal._title.color = new Color(0.09f, 0.14f, 0.24f, 1f);

            reveal._value = UiBuild.Label(reveal._card, "Deger", string.Empty, 31,
                TextAnchor.MiddleCenter);
            UiBuild.Anchor((RectTransform)reveal._value.transform,
                new Vector2(0.08f, 0.12f), new Vector2(0.92f, 0.56f));
            reveal._value.color = new Color(0.12f, 0.38f, 0.70f, 1f);

            var iconGo = new GameObject("Elmas", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(reveal._card, false);
            Image icon = iconGo.GetComponent<Image>();
            icon.sprite = gemIcon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = gemIcon != null;
            reveal._icon = icon;
            UiBuild.Anchor((RectTransform)iconGo.transform,
                new Vector2(0.10f, 0.20f), new Vector2(0.22f, 0.48f));
            go.SetActive(false);
            return reveal;
        }

        public void Present(GoalService.ClaimReceipt receipt)
        {
            if (!receipt.Any) return;
            _title.text = receipt.Items > 1
                ? string.Format("{0} ×{1}", Loc.T("gorev.odul_alindi"), receipt.Items)
                : Loc.T("gorev.odul_alindi");
            string value = receipt.Gems > 0L ? CurrencyText.Gain(CurrencyId.Gems, receipt.Gems) : string.Empty;
            if (receipt.Cards > 0)
                value = Spaced(value, string.Format("+{0} {1}", receipt.Cards, Loc.T("ustabasi.kart")));
            // Packs are banked unopened on the collection screen; saying so here is the only way the
            // player learns a claim paid one (Docs/PLAN_14, slice 7).
            if (receipt.Packs > 0)
                value = Spaced(value, string.Format(Loc.T("koleksiyon.paket_x"), receipt.Packs));
            _value.text = value;
            ShowIcon(receipt.Gems > 0L);
            _shownAt = Time.unscaledTime;
            _group.alpha = 0f;
            _card.localScale = Vector3.one * 0.82f;
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
            ServiceLocator.Get<HapticService>()?.Medium();
        }

        /// <summary>Shows an already committed reward rendered by another reward-owning service.
        /// <paramref name="showIcon"/> is false when the reward holds none of the currency the card's
        /// icon was built with — a charts-only pass tier must not wear the gem.</summary>
        public void Present(string value, bool showIcon = true)
        {
            if (string.IsNullOrEmpty(value)) return;
            _title.text = Loc.T("gorev.odul_alindi");
            _value.text = value;
            ShowIcon(showIcon);
            _shownAt = Time.unscaledTime;
            _group.alpha = 0f;
            _card.localScale = Vector3.one * 0.82f;
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            ServiceLocator.Get<AudioService>()?.Play(SoundId.Reward);
            ServiceLocator.Get<HapticService>()?.Medium();
        }

        private void Update()
        {
            float elapsed = Time.unscaledTime - _shownAt;
            float enter = Mathf.Clamp01(elapsed / 0.18f);
            _group.alpha = elapsed < HoldSeconds - 0.35f
                ? enter : Mathf.Clamp01((HoldSeconds - elapsed) / 0.35f);
            float overshoot = 1f + Mathf.Sin(enter * Mathf.PI) * 0.08f;
            _card.localScale = Vector3.one * Mathf.Lerp(0.82f, overshoot, enter);
            if (elapsed >= HoldSeconds) Hide();
        }

        private void Hide() => gameObject.SetActive(false);

        private void ShowIcon(bool show)
        {
            if (_icon != null) _icon.enabled = show && _icon.sprite != null;
        }

        private static string Spaced(string head, string tail) => head.Length > 0 ? head + "    " + tail : tail;
    }
}
