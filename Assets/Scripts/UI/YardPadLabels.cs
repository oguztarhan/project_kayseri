using Game.Core;
using Game.Gameplay;
using Game.Systems;
using TMPro;
using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// The price painted on each floor pad: what it sells, how far along it is, and what the next step
    /// costs — lying flat on the ground, in the yard, as part of it.
    ///
    /// On the floor rather than floating above it, which was the first attempt and was wrong. A tag
    /// hanging in the air belongs to the interface; paint it on the slab and it belongs to the market.
    /// It also fixes what floating labels always do in a crowded room — six of them over six pads
    /// jostle, overlap and hide each other, while six painted floors never can, because the floor is
    /// where the pads already are and they do not overlap either.
    ///
    /// One <see cref="TextMeshPro"/> per pad, parented to it and turned face-up. They are world
    /// geometry, so they take the yard's own light and shrink honestly with distance — no
    /// screen-space projection, no canvas, nothing to keep in step with the camera.
    /// </summary>
    public sealed class YardPadLabels : MonoBehaviour
    {
        [Tooltip("Yazının pedin yüzeyinden yüksekliği. Sıfıra çok yaklaşırsan zeminle çakışıp titrer.")]
        [SerializeField] private float lift = 0.22f;

        [Tooltip("Zemindeki yazının punto büyüklüğü. Telefonda, kameranın normal uzaklığından " +
                 "okunabilmesi gerek — masaüstünde büyük görünmesi bir şey ifade etmiyor.")]
        [SerializeField, Min(0.5f)] private float fontSize = 6.5f;

        [Tooltip("Yazının kapladığı alan, dünya birimi. Pedin dışına taşabilir; zemine boyandığı için " +
                 "taşan kısım komşu bir şeyin üstünü örtmüyor.")]
        [SerializeField] private Vector2 area = new Vector2(9f, 6f);

        [Tooltip("Yazıların bu kadar seyrek tazelenmesi yeter — fiyat ancak bir şey alınınca değişir.")]
        [SerializeField, Min(0.05f)] private float refreshSeconds = 0.2f;

        [Tooltip("Boş bırakılırsa TMP'nin öntanımlı yazı tipi kullanılır.")]
        [SerializeField] private TMP_FontAsset font;

        private static readonly Color Affordable = new Color(0.86f, 0.98f, 0.88f, 1f);
        private static readonly Color TooDear = new Color(0.78f, 0.80f, 0.86f, 0.75f);
        private static readonly Color Finished = new Color(1f, 0.91f, 0.66f, 1f);

        private sealed class Tag
        {
            public UpgradePad pad;
            public TextMeshPro text;
        }

        private readonly System.Collections.Generic.List<Tag> _tags =
            new System.Collections.Generic.List<Tag>();

        private MarketService _market;
        private WalletService _wallet;
        private float _refreshIn;

        /// <summary>Paints one pad. Called once per pad, for every yard the hall built.</summary>
        public void Build(MarketService market, UpgradePad[] pads)
        {
            _market = market;
            _wallet = ServiceLocator.Get<WalletService>();

            for (int i = 0; i < pads.Length; i++)
            {
                if (pads[i] == null) continue;
                _tags.Add(new Tag { pad = pads[i], text = Paint(pads[i]) });
            }
            Refresh();
        }

        private TextMeshPro Paint(UpgradePad pad)
        {
            var go = new GameObject("Yazi_" + pad.Kind);
            // Parented to the pad, so a pad that ever moves takes its price with it.
            go.transform.SetParent(pad.transform, false);
            // Face up. The camera looks down the yard from the south, so the text's own up-axis points
            // that way too and the words read the right way round from where the player actually is.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localPosition = new Vector3(0f, lift, 0f);

            var text = go.AddComponent<TextMeshPro>();
            if (font != null) text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = area;
            // An outline, because the pads come in three colours and white-on-gold is the one pairing
            // that stops being text at a glance.
            text.fontStyle = FontStyles.Bold;
            text.outlineWidth = 0.18f;
            text.outlineColor = new Color32(12, 14, 20, 230);
            return text;
        }

        private void Update()
        {
            if (_market == null) return;
            _refreshIn -= Time.deltaTime;
            if (_refreshIn > 0f) return;
            _refreshIn = refreshSeconds;
            Refresh();
        }

        private void Refresh()
        {
            for (int i = 0; i < _tags.Count; i++) Write(_tags[i]);
        }

        private void Write(Tag tag)
        {
            YardUpgrade kind = tag.pad.Kind;
            string yard = tag.pad.YardKey;
            string name = Loc.T(PadKey(kind));

            if (_market.IsTrackMaxed(yard, kind))
            {
                tag.text.text = name + "\n" + Loc.T("market.maks");
                tag.text.color = Finished;
                return;
            }

            double cost = _market.Cost(yard, kind);
            tag.text.text = name + "\n$" + NumberFormatter.Format(new BigDouble(cost)) +
                            "\n" + _market.Level(yard, kind) + " / " + MarketPrices.MaxLevel(kind);
            bool affordable = _wallet != null && cost > 0d && _wallet.CanAfford(new BigDouble(cost));
            tag.text.color = affordable ? Affordable : TooDear;
        }

        /// <summary>The localisation key naming each track. Shared with the HUD's underfoot readout.</summary>
        public static string PadKey(YardUpgrade kind)
        {
            switch (kind)
            {
                case YardUpgrade.DepositSlot: return "market.ped.stok";
                case YardUpgrade.QueueSlot: return "market.ped.sira";
                case YardUpgrade.HireCarry: return "market.ped.tasiyici";
                case YardUpgrade.HireServe: return "market.ped.tezgahtar";
                case YardUpgrade.HireCollect: return "market.ped.toplayici";
                default: return "market.ped.sirt";
            }
        }
    }
}
