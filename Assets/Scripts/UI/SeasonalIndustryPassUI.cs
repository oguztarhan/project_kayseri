using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>Scrollable free/premium reward track for the Seasonal Industry Pass.</summary>
    public sealed class SeasonalIndustryPassUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 114;
        [SerializeField] private Sprite cardPanel;
        [SerializeField] private Sprite ribbon;
        [SerializeField] private Sprite actionButton;
        [SerializeField] private Sprite closeIcon;
        [SerializeField] private Sprite gemIcon;

        private const float RowHeight = 150f;
        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color Soft = new Color(0.34f, 0.40f, 0.50f, 1f);
        private static readonly Color Blue = new Color(0.18f, 0.58f, 0.88f, 1f);
        private static readonly Color Gold = new Color(0.95f, 0.67f, 0.18f, 1f);
        private static readonly Color Green = new Color(0.25f, 0.72f, 0.42f, 1f);
        private static readonly Color Disabled = new Color(0.48f, 0.52f, 0.58f, 1f);

        private SeasonalIndustryPassService _pass;
        private LocalizationService _loc;
        private RectTransform _root;
        private Text _title;
        private Text _clock;
        private Text _points;
        private Text _status;
        private Text _freeHeader;
        private Text _premiumHeader;
        private Button _buyButton;
        private Text _buyLabel;
        private Button _restoreButton;
        private Text _restoreLabel;
        private readonly Text[] _tierLabels = new Text[SeasonalIndustryPass.TierCount];
        private readonly Text[] _freeRewards = new Text[SeasonalIndustryPass.TierCount];
        private readonly Text[] _premiumRewards = new Text[SeasonalIndustryPass.TierCount];
        private readonly Button[] _freeButtons = new Button[SeasonalIndustryPass.TierCount];
        private readonly Button[] _premiumButtons = new Button[SeasonalIndustryPass.TierCount];
        private readonly Text[] _freeButtonLabels = new Text[SeasonalIndustryPass.TierCount];
        private readonly Text[] _premiumButtonLabels = new Text[SeasonalIndustryPass.TierCount];
        private RewardRevealUI _reveal;
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

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "SezonBiletiKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", new Color(0.03f, 0.04f, 0.09f, 0.93f),
                Vector2.zero, Vector2.one);
            Button dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            RectTransform sheet = Art(_root, "Zemin", cardPanel,
                new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.86f));
            sheet.GetComponent<Image>().color = new Color(0.92f, 0.95f, 0.99f, 1f);
            sheet.GetComponent<Image>().raycastTarget = true;
            sheet.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;

            RectTransform band = Art(_root, "Serit", ribbon,
                new Vector2(0.25f, 0.87f), new Vector2(0.75f, 0.985f));
            _title = UiBuild.Label(Slot(band, "Yazi", new Vector2(0.05f, 0.18f), new Vector2(0.95f, 0.86f)),
                "Text", Loc.T("pass.baslik"), 34, TextAnchor.MiddleCenter);

            Button close = UiBuild.Btn(_root, "Kapat", closeIcon != null ? string.Empty : "×",
                closeIcon != null ? closeIcon : UiSkin.ButtonGrey, Color.white, 32, Hide);
            UiBuild.Anchor((RectTransform)close.transform,
                new Vector2(0.88f, 0.89f), new Vector2(0.95f, 0.97f));

            _clock = UiBuild.Label(Slot(sheet, "Saat", new Vector2(0.04f, 0.91f), new Vector2(0.58f, 0.98f)),
                "Text", string.Empty, 23, TextAnchor.MiddleLeft);
            _clock.color = Soft;
            _points = UiBuild.Label(Slot(sheet, "Puan", new Vector2(0.58f, 0.91f), new Vector2(0.96f, 0.98f)),
                "Text", string.Empty, 26, TextAnchor.MiddleRight);
            _points.color = Ink;

            _buyButton = UiBuild.Btn(sheet, "PremiumAl", string.Empty,
                actionButton != null ? actionButton : UiSkin.ButtonGreen, Gold, 22, BuyPremium);
            UiBuild.Anchor((RectTransform)_buyButton.transform,
                new Vector2(0.51f, 0.825f), new Vector2(0.96f, 0.90f));
            _buyLabel = _buyButton.GetComponentInChildren<Text>();

            _restoreButton = UiBuild.Btn(sheet, "GeriYukle", string.Empty,
                UiSkin.ButtonBlue, Blue, 18, Restore);
            UiBuild.Anchor((RectTransform)_restoreButton.transform,
                new Vector2(0.04f, 0.825f), new Vector2(0.48f, 0.90f));
            _restoreLabel = _restoreButton.GetComponentInChildren<Text>();

            _status = UiBuild.Label(Slot(sheet, "Durum", new Vector2(0.04f, 0.785f), new Vector2(0.96f, 0.825f)),
                "Text", string.Empty, 18, TextAnchor.MiddleCenter);
            _status.color = Soft;

            _freeHeader = UiBuild.Label(Slot(sheet, "UcretsizBaslik", new Vector2(0.18f, 0.735f), new Vector2(0.55f, 0.785f)),
                "Text", Loc.T("pass.ucretsiz"), 22, TextAnchor.MiddleCenter);
            _freeHeader.color = Blue;
            _premiumHeader = UiBuild.Label(Slot(sheet, "PremiumBaslik", new Vector2(0.55f, 0.735f), new Vector2(0.96f, 0.785f)),
                "Text", Loc.T("pass.premium"), 22, TextAnchor.MiddleCenter);
            _premiumHeader.color = Gold;

            BuildTrack(sheet);
            _reveal = RewardRevealUI.Create(_root, cardPanel, gemIcon);
        }

        private void BuildTrack(RectTransform sheet)
        {
            var scrollGo = new GameObject("KademeListesi", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(sheet, false);
            RectTransform scrollRoot = UiBuild.Anchor((RectTransform)scrollGo.transform,
                new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.735f));

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollRoot, false);
            RectTransform viewport = UiBuild.Anchor((RectTransform)viewportGo.transform, Vector2.zero, Vector2.one);
            Image viewportImage = viewportGo.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            RectTransform content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, SeasonalIndustryPass.TierCount * RowHeight);

            ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 45f;

            for (int i = 0; i < SeasonalIndustryPass.TierCount; i++) BuildRow(content, i);
        }

        private void BuildRow(RectTransform content, int index)
        {
            RectTransform row = Art(content, "Kademe" + index, cardPanel, Vector2.zero, Vector2.one);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(-10f, RowHeight - 8f);
            row.anchoredPosition = new Vector2(0f, -index * RowHeight - 4f);
            row.GetComponent<Image>().color = Color.white;

            _tierLabels[index] = UiBuild.Label(
                Slot(row, "KademeNo", new Vector2(0.01f, 0.08f), new Vector2(0.17f, 0.92f)),
                "Text", string.Empty, 21, TextAnchor.MiddleCenter);
            _tierLabels[index].color = Ink;
            _freeRewards[index] = UiBuild.Label(
                Slot(row, "UcretsizOdul", new Vector2(0.18f, 0.48f), new Vector2(0.54f, 0.91f)),
                "Text", string.Empty, 18, TextAnchor.MiddleCenter);
            _freeRewards[index].color = Ink;
            _premiumRewards[index] = UiBuild.Label(
                Slot(row, "PremiumOdul", new Vector2(0.55f, 0.48f), new Vector2(0.98f, 0.91f)),
                "Text", string.Empty, 18, TextAnchor.MiddleCenter);
            _premiumRewards[index].color = Ink;

            int captured = index;
            _freeButtons[index] = UiBuild.Btn(row, "UcretsizAl", string.Empty,
                actionButton != null ? actionButton : UiSkin.ButtonGreen, Green, 17,
                () => ClaimFree(captured));
            UiBuild.Anchor((RectTransform)_freeButtons[index].transform,
                new Vector2(0.20f, 0.08f), new Vector2(0.52f, 0.45f));
            _freeButtonLabels[index] = _freeButtons[index].GetComponentInChildren<Text>();

            _premiumButtons[index] = UiBuild.Btn(row, "PremiumAl", string.Empty,
                actionButton != null ? actionButton : UiSkin.ButtonGreen, Gold, 17,
                () => ClaimPremium(captured));
            UiBuild.Anchor((RectTransform)_premiumButtons[index].transform,
                new Vector2(0.59f, 0.08f), new Vector2(0.94f, 0.45f));
            _premiumButtonLabels[index] = _premiumButtons[index].GetComponentInChildren<Text>();
        }

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
            if (_pass.ClaimFree(tier)) _reveal?.Present(RewardText(reward));
            Refresh();
        }

        private void ClaimPremium(int tier)
        {
            if (_pass == null) return;
            SeasonalIndustryPass.Reward reward = _pass.TierAt(tier).Premium;
            if (_pass.ClaimPremium(tier)) _reveal?.Present(RewardText(reward));
            Refresh();
        }

        private void Refresh()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;
            _title.text = Loc.T("pass.baslik");
            _freeHeader.text = Loc.T("pass.ucretsiz");
            _premiumHeader.text = Loc.T("pass.premium");
            _restoreLabel.text = Loc.T("ayarlar.geri_yukle");

            bool available = _pass != null && _pass.Available;
            long points = available ? _pass.Points : 0L;
            _points.text = points + " " + Loc.T("pass.puan");
            if (!available) _clock.text = Loc.T("pass.yok");
            else if (_pass.Phase == LiveEvents.Phase.Active)
                _clock.text = Loc.T("etkinlik.kalan") + " " + HudUI.LongClock(_pass.SecondsLeft);
            else if (_pass.Phase == LiveEvents.Phase.Upcoming)
                _clock.text = Loc.T("etkinlik.basliyor") + " " + HudUI.LongClock(_pass.SecondsUntilStart);
            else _clock.text = Loc.T("pass.bitti");

            bool owned = available && _pass.HasPremium;
            _buyLabel.text = owned ? Loc.T("pass.premium_aktif")
                : Loc.T("ortak.satin_al") + " · " + (_pass != null ? _pass.LocalizedPrice : string.Empty);
            _buyButton.interactable = available && _pass.Live && !owned;
            _buyButton.GetComponent<Image>().color = _buyButton.interactable ? Gold : Disabled;
            _restoreButton.interactable = _pass != null && !owned;

            for (int i = 0; i < SeasonalIndustryPass.TierCount; i++)
            {
                SeasonalIndustryPass.Tier tier = _pass != null ? _pass.TierAt(i) : default;
                bool reached = available && points >= tier.Points;
                _tierLabels[i].text = Loc.T("pass.kademe") + " " + (i + 1) + "\n" + tier.Points;
                _freeRewards[i].text = RewardText(tier.Free);
                _premiumRewards[i].text = RewardText(tier.Premium);
                RefreshClaim(_freeButtons[i], _freeButtonLabels[i], reached,
                    available && _pass.FreeClaimed(i), available && _pass.CanClaimFree(i), true);
                RefreshClaim(_premiumButtons[i], _premiumButtonLabels[i], reached,
                    available && _pass.PremiumClaimed(i), available && _pass.CanClaimPremium(i), owned);
            }
        }

        private static void RefreshClaim(Button button, Text label, bool reached, bool claimed,
            bool canClaim, bool laneOwned)
        {
            button.interactable = canClaim;
            button.GetComponent<Image>().color = canClaim ? Green : Disabled;
            label.text = claimed ? Loc.T("gorev.alindi")
                : !laneOwned ? Loc.T("gorev.kilitli")
                : reached ? Loc.T("gorev.al") : Loc.T("gorev.kilitli");
        }

        private static string RewardText(in SeasonalIndustryPass.Reward reward)
        {
            string text = string.Empty;
            if (reward.Gems > 0L) text += "+" + reward.Gems + " ◆";
            if (reward.Cards > 0) text += Space(text) + "+" + reward.Cards + " " + Loc.T("ustabasi.kart");
            if (reward.Charts > 0L) text += Space(text) + "+" + reward.Charts + " " + Loc.T("kaptan.harita");
            if (reward.CashMinutes > 0d)
                text += Space(text) + "+" + reward.CashMinutes.ToString("0.#") + " " + Loc.T("sprint.nakit_dakika");
            return text;
        }

        private static string Space(string text) => text.Length > 0 ? "   " : string.Empty;

        private static RectTransform Art(RectTransform parent, string name, Sprite sprite,
            Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite != null ? sprite : UiSkin.Panel;
            image.type = image.sprite != null && image.sprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return UiBuild.Anchor((RectTransform)go.transform, min, max);
        }

        private static RectTransform Slot(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, min, max);
        }
    }
}
