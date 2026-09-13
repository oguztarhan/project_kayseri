using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The Seasonal Industry Pass: a scrolling track of fifteen tiers, each with a free and a premium
    /// reward, plus the premium purchase and restore.
    ///
    /// DRAWN FROM <see cref="EtkinlikKit"/>, the events family's shared pieces — sheet, ribbon, chip,
    /// navy rows, capsules — and a claim shows <see cref="EtkinlikOdulUI"/>, the events family's own
    /// reward card, not the shared <see cref="RewardRevealUI"/>. The purchase is the screen's main act,
    /// so it is the orange capsule; restore is the pale one beside it.
    ///
    /// THE PURCHASE BUTTONS SIT STRAIGHT ON THE SHEET (<c>Zemin/PremiumAl</c>, <c>Zemin/GeriYukle</c>)
    /// and the track keeps its <c>KademeListesi/Viewport/Content/Kademe{n}</c> shape: that is what the
    /// UI smoke test walks.
    /// </summary>
    public sealed class SeasonalIndustryPassUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 114;
        [SerializeField] private Color scrim = new Color(0.03f, 0.04f, 0.09f, 1f);

        /// <summary>Row pitch in canvas units; each card is 14 shorter, the gap between rows.</summary>
        private const float RowPitch = 206f, RowGap = 14f;

        /// <summary>The navy card at 1/1.5 border — a tier row carries two reward columns.</summary>
        private const float BorderScale = 1.5f;

        /// <summary>The premium lane's heading ink: the orange capsule's colour, deep enough for a white sheet.</summary>
        private static readonly Color PremiumInk = new Color(0.86f, 0.42f, 0.08f, 1f);

        private SeasonalIndustryPassService _pass;
        private LocalizationService _loc;
        private RectTransform _root, _empty, _track;
        private Text _title, _clock, _points, _status, _freeHeader, _premiumHeader, _emptyLabel;
        private Button _buyButton, _restoreButton;
        private Text _buyLabel, _restoreLabel;
        private EtkinlikOdulUI _popup;

        private readonly Image[] _tierIcons = new Image[SeasonalIndustryPass.TierCount];
        private readonly Text[] _tierLabels = new Text[SeasonalIndustryPass.TierCount];
        private readonly Text[] _tierPoints = new Text[SeasonalIndustryPass.TierCount];
        private readonly EtkinlikOdulSatiri[] _freeRewards = new EtkinlikOdulSatiri[SeasonalIndustryPass.TierCount];
        private readonly EtkinlikOdulSatiri[] _premiumRewards = new EtkinlikOdulSatiri[SeasonalIndustryPass.TierCount];
        private readonly Button[] _freeButtons = new Button[SeasonalIndustryPass.TierCount];
        private readonly Button[] _premiumButtons = new Button[SeasonalIndustryPass.TierCount];
        private readonly Text[] _freeButtonLabels = new Text[SeasonalIndustryPass.TierCount];
        private readonly Text[] _premiumButtonLabels = new Text[SeasonalIndustryPass.TierCount];
        private float _tick;

        private void Awake()
        {
            _pass = ServiceLocator.Get<SeasonalIndustryPassService>();
            _loc = ServiceLocator.Get<LocalizationService>();
            Build();
            if (_pass != null) _pass.Changed += Refresh;
            if (_loc != null) _loc.Changed += Refresh;
            Hide();
        }

        private void OnDestroy()
        {
            if (_pass != null) _pass.Changed -= Refresh;
            if (_loc != null) _loc.Changed -= Refresh;
        }

        public void Show()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            _tick = 0f;
            Refresh();
        }

        public void Hide()
        {
            if (_popup != null) _popup.Hide();
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            _tick += Time.unscaledDeltaTime;
            if (_tick < 1f) return;
            _tick = 0f;
            Refresh();
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "SezonBiletiKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            Button dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            RectTransform sheet = EtkinlikKit.Sheet(_root).rectTransform;
            _title = EtkinlikKit.Header(_root, Loc.T("pass.baslik"), Hide);

            _clock = EtkinlikKit.Label(sheet, "Saat", new Vector2(0.090f, 0.715f), new Vector2(0.570f, 0.767f),
                                       string.Empty, 24, TextAnchor.MiddleLeft, EtkinlikKit.InkSoft, 12);
            _points = EtkinlikKit.Chip(sheet, "Puan", new Vector2(0.600f, 0.713f), new Vector2(0.910f, 0.767f));

            _restoreButton = EtkinlikKit.Capsule(sheet, "GeriYukle", new Vector2(0.085f, 0.650f), new Vector2(0.445f, 0.706f),
                                                 Restore, out _restoreLabel);
            _buyButton = EtkinlikKit.Capsule(sheet, "PremiumAl", new Vector2(0.460f, 0.648f), new Vector2(0.915f, 0.708f),
                                             BuyPremium, out _buyLabel);

            _status = EtkinlikKit.Label(sheet, "Durum", new Vector2(0.090f, 0.618f), new Vector2(0.910f, 0.646f),
                                        string.Empty, 20, TextAnchor.MiddleCenter, EtkinlikKit.InkSoft, 11);

            _freeHeader = EtkinlikKit.Label(sheet, "UcretsizBaslik", new Vector2(0.405f, 0.584f), new Vector2(0.650f, 0.616f),
                                            Loc.T("pass.ucretsiz"), 22, TextAnchor.MiddleCenter, EkranKit.Ink, 11);
            _premiumHeader = EtkinlikKit.Label(sheet, "PremiumBaslik", new Vector2(0.660f, 0.584f), new Vector2(0.915f, 0.616f),
                                               Loc.T("pass.premium"), 22, TextAnchor.MiddleCenter, PremiumInk, 11);

            RectTransform content = EtkinlikKit.Scroll(sheet, "KademeListesi", new Vector2(0.085f, 0.075f), new Vector2(0.915f, 0.580f),
                                                       SeasonalIndustryPass.TierCount, RowPitch);
            _track = (RectTransform)content.parent.parent;
            for (int i = 0; i < SeasonalIndustryPass.TierCount; i++) BuildRow(content, i);

            _empty = EtkinlikKit.Empty(sheet, Loc.T("pass.yok"), out _emptyLabel);
            _popup = EtkinlikOdulUI.Create(_root);
            // Content into the safe area; the scrim above it keeps covering the notch.
            UiBuild.InsetContent(_root);
        }

        /// <summary>A chest richer up the track, the tier and the points it needs, then the free column and the premium one.</summary>
        private void BuildRow(RectTransform content, int index)
        {
            RectTransform row = EtkinlikKit.Row(content, "Kademe" + index, index, RowPitch, RowPitch - RowGap, BorderScale).rectTransform;

            _tierIcons[index] = EkranKit.Icon(row, "Simge", EtkinlikKit.Chest(index * 4 / SeasonalIndustryPass.TierCount),
                                              new Vector2(0.025f, 0.24f), new Vector2(0.135f, 0.76f));
            _tierLabels[index] = EtkinlikKit.Label(row, "KademeNo", new Vector2(0.150f, 0.50f), new Vector2(0.390f, 0.78f),
                                                   string.Empty, 24, TextAnchor.MiddleLeft, EkranKit.Paper, 12);
            _tierPoints[index] = EtkinlikKit.Label(row, "Puan", new Vector2(0.150f, 0.22f), new Vector2(0.390f, 0.50f),
                                                   string.Empty, 20, TextAnchor.MiddleLeft, EkranKit.PaperSoft, 11);

            int captured = index;
            _freeRewards[index] = EtkinlikOdulSatiri.Create(row, "UcretsizOdul", new Vector2(0.400f, 0.52f), new Vector2(0.650f, 0.78f),
                                                           22, EkranKit.Paper, TextAnchor.MiddleCenter);
            _freeButtons[index] = EtkinlikKit.Capsule(row, "UcretsizAl", new Vector2(0.405f, 0.21f), new Vector2(0.645f, 0.51f),
                                                      () => ClaimFree(captured), out _freeButtonLabels[index]);

            _premiumRewards[index] = EtkinlikOdulSatiri.Create(row, "PremiumOdul", new Vector2(0.660f, 0.52f), new Vector2(0.960f, 0.78f),
                                                              22, EkranKit.Paper, TextAnchor.MiddleCenter);
            _premiumButtons[index] = EtkinlikKit.Capsule(row, "PremiumAl", new Vector2(0.690f, 0.21f), new Vector2(0.930f, 0.51f),
                                                         () => ClaimPremium(captured), out _premiumButtonLabels[index]);
        }

        // ------------------------------------------------------------------ act
        private void BuyPremium()
        {
            if (_pass == null) return;
            _status.text = string.Empty;
            _buyButton.interactable = false;
            _pass.PurchasePremium(ok =>
            {
                _status.text = ok ? Loc.T("pass.satin_basarili") : Loc.T("pass.satin_basarisiz");
                Refresh();
            });
        }

        private void Restore()
        {
            if (_pass == null) return;
            _status.text = Loc.T("ayarlar.geri_yukleniyor");
            _restoreButton.interactable = false;
            _pass.RestorePurchases((ok, message) =>
            {
                _status.text = Loc.T(ok ? "ayarlar.geri_basarili" : "ayarlar.geri_basarisiz");
                Refresh();
            });
        }

        private void ClaimFree(int tier)
        {
            if (_pass == null) return;
            SeasonalIndustryPass.Reward reward = _pass.TierAt(tier).Free;
            if (_pass.ClaimFree(tier)) Present(reward);
            Refresh();
        }

        private void ClaimPremium(int tier)
        {
            if (_pass == null) return;
            SeasonalIndustryPass.Reward reward = _pass.TierAt(tier).Premium;
            if (_pass.ClaimPremium(tier)) Present(reward);
            Refresh();
        }

        private void Present(in SeasonalIndustryPass.Reward reward)
            => _popup.Present(reward.Gems, reward.Cards, reward.Charts, CashText(reward.CashMinutes));

        // ---------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            _title.text = Loc.T("pass.baslik");

            bool available = _pass != null && _pass.Available;
            EtkinlikKit.SetActive(_empty, !available);
            EtkinlikKit.SetActive(_track, available);
            EtkinlikKit.SetActive(_clock.transform.parent, available);
            EtkinlikKit.SetActive(_points.transform.parent.parent, available);
            EtkinlikKit.SetActive(_status.transform.parent, available);
            EtkinlikKit.SetActive(_freeHeader.transform.parent, available);
            EtkinlikKit.SetActive(_premiumHeader.transform.parent, available);
            EtkinlikKit.SetActive(_buyButton, available);
            EtkinlikKit.SetActive(_restoreButton, available);
            if (!available)
            {
                _emptyLabel.text = Loc.T("pass.yok");
                return;
            }

            _freeHeader.text = Loc.T("pass.ucretsiz");
            _premiumHeader.text = Loc.T("pass.premium");

            long points = _pass.Points;
            _points.text = points + " " + Loc.T("pass.puan");
            if (_pass.Phase == LiveEvents.Phase.Active)
                _clock.text = Loc.T("etkinlik.kalan") + " " + HudUI.LongClock(_pass.SecondsLeft);
            else if (_pass.Phase == LiveEvents.Phase.Upcoming)
                _clock.text = Loc.T("etkinlik.basliyor") + " " + HudUI.LongClock(_pass.SecondsUntilStart);
            else
                _clock.text = Loc.T("pass.bitti");

            bool owned = _pass.HasPremium;
            bool canBuy = _pass.Live && !owned;
            _buyLabel.text = EtkinlikKit.OneLine(owned ? Loc.T("pass.premium_aktif")
                : Loc.T("ortak.satin_al") + " · " + _pass.LocalizedPrice);
            EtkinlikKit.SetFace(_buyButton, _buyLabel, canBuy ? EtkinlikKit.Face.Primary : EtkinlikKit.Face.Dead, canBuy);

            _restoreLabel.text = EtkinlikKit.OneLine(Loc.T("ayarlar.geri_yukle"));
            EtkinlikKit.SetFace(_restoreButton, _restoreLabel, EtkinlikKit.Face.Dead, !owned);

            for (int i = 0; i < SeasonalIndustryPass.TierCount; i++)
            {
                SeasonalIndustryPass.Tier tier = _pass.TierAt(i);
                bool reached = points >= tier.Points;

                _tierIcons[i].color = reached ? Color.white : EtkinlikKit.Faded;
                _tierLabels[i].text = Loc.T("pass.kademe") + " " + (i + 1);
                _tierPoints[i].text = tier.Points + " " + Loc.T("pass.puan");

                _freeRewards[i].Set(null, tier.Free.Gems, tier.Free.Cards, tier.Free.Charts, CashText(tier.Free.CashMinutes));
                _premiumRewards[i].Set(null, tier.Premium.Gems, tier.Premium.Cards, tier.Premium.Charts, CashText(tier.Premium.CashMinutes));

                RefreshClaim(_freeButtons[i], _freeButtonLabels[i], reached, _pass.FreeClaimed(i), _pass.CanClaimFree(i),
                             true, EtkinlikKit.Face.Claim);
                RefreshClaim(_premiumButtons[i], _premiumButtonLabels[i], reached, _pass.PremiumClaimed(i),
                             _pass.CanClaimPremium(i), owned, EtkinlikKit.Face.Primary);
            }
        }

        /// <summary>Claimed, locked (not reached, or a premium lane not owned), or ready — ready wears the lane's own face.</summary>
        private static void RefreshClaim(Button button, Text label, bool reached, bool claimed, bool canClaim,
                                         bool laneOwned, EtkinlikKit.Face readyFace)
        {
            label.text = EtkinlikKit.OneLine(claimed ? Loc.T("gorev.alindi")
                : !laneOwned || !reached ? Loc.T("gorev.kilitli")
                : Loc.T("gorev.al"));
            EtkinlikKit.SetFace(button, label, canClaim ? readyFace : EtkinlikKit.Face.Dead, canClaim);
        }

        private static string CashText(double minutes)
            => minutes > 0d ? "+" + minutes.ToString("0.#") + " " + Loc.T("sprint.nakit_dakika") : null;
    }
}
