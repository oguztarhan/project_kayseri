using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Runtime-built overview of every wallet currency. It reads the CurrencyRegistry only; grants,
    /// spending, and regeneration remain owned by the domain services. The screen is built lazily so
    /// a player who never opens it pays no cost for its rows.
    /// </summary>
    public sealed class WalletUI : MonoBehaviour
    {
        /// <summary>One hand-wired icon. Only needed where the game has no runtime-loadable art for
        /// a currency; see <see cref="ResolveIcon"/> for the order icons are looked up in.</summary>
        [Serializable]
        private struct CurrencyIcon
        {
            public CurrencyId currency;
            public Sprite icon;
        }

        [Header("Wallet layout")]
        [SerializeField] private int sortingOrder = 120;
        [Tooltip("Only used where there is no HUD rail (the portrait Shipyard scene). Below every " +
                 "screen's sheet, so an open screen covers the button.")]
        [SerializeField] private int standaloneOpenerSortingOrder = 99;
        [SerializeField, Range(0f, 0.1f)] private float columnGap = 0.035f;
        [SerializeField] private Color scrim = new Color(0f, 0f, 0f, 0.66f);
        [SerializeField] private Color backdrop = new Color(0.025f, 0.10f, 0.24f, 1f);
        [SerializeField] private Color rowColor = new Color(0.17f, 0.21f, 0.31f, 1f);
        [SerializeField] private Color groupColor = new Color(0.56f, 0.86f, 1f, 1f);
        [SerializeField] private Color balanceColor = new Color(1f, 0.88f, 0.40f, 1f);

        [Header("Simgeler")]
        [Tooltip("İsteğe bağlı elle atanan simgeler. Boş kalanlar UI/Wallet altındaki currency-icons " +
                 "görsellerini kullanır. Görsel bulunamazsa harf rozeti gösterilir.")]
        [SerializeField] private CurrencyIcon[] iconOverrides = new CurrencyIcon[0];

        private static readonly CurrencyCategory[] GroupOrder =
        {
            CurrencyCategory.MainEconomy,
            CurrencyCategory.Sea,
            CurrencyCategory.Crafting,
            CurrencyCategory.MiningGear,
            CurrencyCategory.Pets
        };

        private const int CurrencyCount = 10;

        public const string OpenerButtonName = "BtnCuzdan";

        private readonly Text[] _name = new Text[CurrencyCount];
        private readonly Text[] _groupLabels = new Text[GroupOrder.Length];
        private readonly Text[] _value = new Text[CurrencyCount];
        private readonly Text[] _timer = new Text[CurrencyCount];
        private readonly Text[] _badge = new Text[CurrencyCount];
        private readonly Image[] _art = new Image[CurrencyCount];

        private HudUI _hud;
        private CurrencyRegistry _registry;
        private MiningGearService _mining;
        private LocalizationService _loc;
        private RectTransform _root;
        private RectTransform _content;
        private Sprite _cardSprite;
        private Text _title;
        private Text _closeLabel;
        private Text _openerLabel;
        private float _pollTimer;
        private bool _initialized;
        private int _builtRows;
        private int _builtGroups;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public int BuiltRowCount => _builtRows;
        public int BuiltGroupCount => _builtGroups;

        public string DisplayedValue(CurrencyId id) => TextOf(_value, id);
        public string DisplayedName(CurrencyId id) => TextOf(_name, id);
        public string DisplayedTimer(CurrencyId id) => TextOf(_timer, id);

        /// <summary>True when the row shows real art rather than the fallback letter badge.</summary>
        public bool HasArtIcon(CurrencyId id)
        {
            int index = (int)id;
            return index >= 0 && index < _art.Length && _art[index] != null && _art[index].enabled;
        }

        private static string TextOf(Text[] labels, CurrencyId id)
        {
            int index = (int)id;
            return index >= 0 && index < labels.Length && labels[index] != null
                ? labels[index].text
                : string.Empty;
        }

        private void Start()
        {
            if (!_initialized) Initialize(FindAnyObjectByType<HudUI>(FindObjectsInactive.Include));
        }

        /// <summary>Called by HudUI after its authored rail is available.</summary>
        public void Initialize(HudUI hud)
        {
            if (_initialized) return;
            _initialized = true;
            _hud = hud;
            _registry = ServiceLocator.Get<CurrencyRegistry>();
            _mining = ServiceLocator.Get<MiningGearService>();
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_registry != null) _registry.Changed += OnSourceChanged;
            if (_loc != null) _loc.Changed += OnLanguageChanged;
            BuildOpener();
            Hide();
        }

        private void OnDestroy()
        {
            if (_registry != null) _registry.Changed -= OnSourceChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        /// <summary>
        /// The once-a-second pulse. Mining Points are banked only when someone polls the pool (sea
        /// energy is worked out on read, mining points are not), so the wallet polls it the way
        /// MiningGearUI does. Without that, the row's countdown ran to zero and started over while the
        /// balance beside it never moved.
        /// </summary>
        private void Update()
        {
            if (!IsOpen) return;
            _pollTimer -= Time.unscaledDeltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = 1f;
            _mining?.Poll();
            Refresh();
        }

        public void Show()
        {
            if (!_initialized) Initialize(FindAnyObjectByType<HudUI>(FindObjectsInactive.Include));
            if (_root == null) Build();
            if (_root != null) _root.gameObject.SetActive(true);
            _mining?.Poll();
            Refresh();
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void BuildOpener()
        {
            if (_hud == null)
            {
                // The portrait Shipyard scene has no HudUI rail, and it is where a fresh save boots.
                // Same answer as PetRosterUI's opener there: a small canvas of its own, bottom-left
                // opposite the pet button, below every sheet so an open screen covers it.
                RectTransform canvas = UiBuild.Canvas(transform, "CuzdanAciciKanvas", standaloneOpenerSortingOrder);
                Button standalone = UiBuild.Btn(canvas, OpenerButtonName, Loc.T("wallet.title"),
                    UiSkin.ButtonYellow, new Color(0.94f, 0.68f, 0.20f, 1f), 22, Show);
                UiBuild.Anchor((RectTransform)standalone.transform,
                    new Vector2(0.040f, 0.035f), new Vector2(0.350f, 0.100f));
                _openerLabel = standalone.GetComponentInChildren<Text>();
                Fit(_openerLabel, 14, 22);
                return;
            }
            Sprite icon = PortraitUiArt.Get("general-wallet-icon");
            _hud.AttachBottomButton(16, OpenerButtonName, icon != null ? icon : UiSkin.ButtonYellow, Show);
        }

        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "CuzdanKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", UiBuild.Opaque(scrim), Vector2.zero, Vector2.one);
            Button dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            RectTransform sheet = UiBuild.Flat(_root, "Zemin", backdrop,
                                               new Vector2(0.025f, 0.025f), new Vector2(0.975f, 0.975f));
            // Fit the whole sheet, including its contents, to the original panel's proportions.
            // Stretching only the background would leave text outside the painted frame.
            Sprite panel = PortraitUiArt.Get("general-large-panel");
            if (panel != null)
            {
                PortraitUiArt.Apply(sheet.GetComponent<Image>(), panel);
                RectTransform bounds = new GameObject("PanelAlani", typeof(RectTransform)).GetComponent<RectTransform>();
                bounds.SetParent(_root, false);
                UiBuild.Anchor(bounds, new Vector2(0.025f, 0.025f), new Vector2(0.975f, 0.975f));
                sheet.SetParent(bounds, false);
                UiBuild.Anchor(sheet, Vector2.zero, Vector2.one);
                AspectRatioFitter fit = sheet.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = panel.rect.width / panel.rect.height;
            }
            _cardSprite = PortraitUiArt.Get("general-small-card-panel-transparent");
            // The sheet eats its own taps. Without a handler here a tap on any row walked up to the
            // scrim's dismiss button and closed the wallet — see MiningGearUI.BuildBackdrop.
            sheet.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            BuildHeader(sheet);
            BuildContent(sheet);
            RefreshBadges();
            UiBuild.InsetContent(_root);
        }

        private void BuildHeader(RectTransform sheet)
        {
            RectTransform header = UiBuild.Flat(sheet, "Baslik", Color.clear,
                                                new Vector2(0.14f, 0.83f), new Vector2(0.86f, 0.96f));
            PortraitUiArt.Apply(header.GetComponent<Image>(), "general-title-plate");
            header.GetComponent<Image>().raycastTarget = false;
            _title = UiBuild.Label(header, "Baslik", Loc.T("wallet.title"), 46, TextAnchor.MiddleCenter);
            _title.color = Color.white;
            UiBuild.Anchor(_title.rectTransform, new Vector2(0.20f, 0.15f), new Vector2(0.80f, 0.85f));
            Fit(_title, 24, 46);
            Shadow shadow = _title.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.25f, 0.10f, 0.025f, 0.85f);
            shadow.effectDistance = new Vector2(0f, -3f);

            Button close = UiBuild.Btn(sheet, "Kapat", Loc.T("wallet.close"), UiSkin.ButtonYellow,
                                       new Color(0.25f, 0.54f, 0.78f, 1f), 23, Hide);
            _closeLabel = close.GetComponentInChildren<Text>();
            Fit(_closeLabel, 14, 23);
            RectTransform closeRect = (RectTransform)close.transform;
            UiBuild.Anchor(closeRect, new Vector2(0.87f, 0.925f), new Vector2(0.99f, 1.01f));
            Sprite closeSprite = PortraitUiArt.Get("general-close-button");
            if (closeSprite != null)
            {
                PortraitUiArt.Apply(close.GetComponent<Image>(), closeSprite);
                _closeLabel.enabled = false;
            }
        }

        private void BuildContent(RectTransform sheet)
        {
            // A fixed two-column overview: all ten balances are visible without scrolling.
            // RectMask2D keeps the contents inside the frame without requiring an opaque stencil.
            var viewportGo = new GameObject("Gorunum", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportGo.transform.SetParent(sheet, false);
            RectTransform viewport = (RectTransform)viewportGo.transform;
            // Inside the frame's inlay with a margin: at 0.07-0.93 x 0.065 the cards' rims sat on the
            // frame at both sides and the last row leaned on its bottom edge.
            viewport.anchorMin = new Vector2(0.09f, 0.090f);
            viewport.anchorMax = new Vector2(0.91f, 0.815f);
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewportGo.GetComponent<Image>().color = Color.clear;

            var contentGo = new GameObject("Icerik", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            _content = (RectTransform)contentGo.transform;
            UiBuild.Anchor(_content, Vector2.zero, Vector2.one);
            BuildGroups();
        }

        private void BuildGroups()
        {
            if (_registry == null || _content == null) return;
            _builtRows = 0;
            _builtGroups = 0;
            // Main economy + sea on the left, crafting + mining + pets on the right.
            // Each column contains exactly five cards; category headings use the gap above a card.
            int leftRow = 0;
            int rightRow = 0;
            for (int groupIndex = 0; groupIndex < GroupOrder.Length; groupIndex++)
            {
                CurrencyCategory category = GroupOrder[groupIndex];
                bool left = category == CurrencyCategory.MainEconomy || category == CurrencyCategory.Sea;
                bool first = true;
                for (int i = 0; i < _registry.Definitions.Count; i++)
                {
                    CurrencyDefinition definition = _registry.Definitions[i];
                    if (DisplayCategory(definition.Category) != category) continue;
                    int rowIndex = left ? leftRow++ : rightRow++;
                    var cell = new GameObject("Hucre_" + definition.Id, typeof(RectTransform)).GetComponent<RectTransform>();
                    cell.SetParent(_content, false);
                    float xMin = left ? 0f : 0.5f + columnGap * 0.5f;
                    float xMax = left ? 0.5f - columnGap * 0.5f : 1f;
                    UiBuild.Anchor(cell, new Vector2(xMin, 1f - (rowIndex + 1) * 0.2f),
                                        new Vector2(xMax, 1f - rowIndex * 0.2f));
                    if (first)
                    {
                        Text header = UiBuild.Label(cell, "Grup", Loc.T(GroupKey(category)), 23, TextAnchor.MiddleLeft);
                        header.color = groupColor;
                        UiBuild.Anchor(header.rectTransform, new Vector2(0.05f, 0.80f), new Vector2(0.975f, 1f));
                        Fit(header, 18, 23);
                        _groupLabels[groupIndex] = header;
                        _builtGroups++;
                        first = false;
                    }
                    BuildRow(definition, cell);
                }
            }
        }

        private void BuildRow(CurrencyDefinition definition, RectTransform cell)
        {
            int index = (int)definition.Id;
            var bounds = new GameObject("KartAlani", typeof(RectTransform)).GetComponent<RectTransform>();
            bounds.SetParent(cell, false);
            UiBuild.Anchor(bounds, new Vector2(0f, 0.025f), new Vector2(1f, 0.79f));
            RectTransform row = UiBuild.Flat(bounds, "Para_" + definition.Id,
                                             rowColor, Vector2.zero, Vector2.one);
            if (_cardSprite != null)
            {
                PortraitUiArt.Apply(row.GetComponent<Image>(), _cardSprite);
                AspectRatioFitter fit = row.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = _cardSprite.rect.width / _cardSprite.rect.height;
            }

            // The badge is always there; real art covers it when the currency has any. A tinted
            // square with the currency's mark keeps the column even when it does not.
            RectTransform badge = UiBuild.Flat(row, "Simge", CategoryTint(definition.Category),
                                               new Vector2(0.04f, 0.19f), new Vector2(0.29f, 0.81f));
            // Up to 40: the fallback font draws ◉ and ✦ small for their size, letters stop at the width.
            Text mark = UiBuild.Label(badge, "Harf", string.Empty, 40, TextAnchor.MiddleCenter);
            mark.color = Color.white;
            Fit(mark, 14, 40);

            var artGo = new GameObject("Resim", typeof(RectTransform), typeof(Image));
            artGo.transform.SetParent(badge, false);
            Image art = artGo.GetComponent<Image>();
            art.preserveAspect = true;
            art.raycastTarget = false;
            UiBuild.Anchor((RectTransform)artGo.transform, Vector2.zero, Vector2.one);
            Sprite sprite = ResolveIcon(definition.Id);
            art.sprite = sprite;
            art.enabled = sprite != null;
            mark.enabled = sprite == null;
            if (sprite != null) badge.GetComponent<Image>().color = Color.clear;

            Text name = UiBuild.Label(row, "Ad", Loc.T(definition.LocalizedNameKey), 22, TextAnchor.MiddleLeft);
            name.color = Color.white;
            // Text block centred on the card's inlay, off the top rim, and stopping short of the right one:
            // it used to start at 0.64 and run to 0.93, on the rim at both ends.
            UiBuild.Anchor(name.rectTransform, new Vector2(0.32f, definition.Regenerates ? 0.60f : 0.56f),
                                               new Vector2(0.90f, 0.82f));
            Fit(name, 17, 22);

            Text value = UiBuild.Label(row, "Deger", "0", 36, TextAnchor.MiddleLeft);
            value.color = balanceColor;
            UiBuild.Anchor(value.rectTransform, new Vector2(0.32f, definition.Regenerates ? 0.36f : 0.14f),
                                               new Vector2(0.90f, definition.Regenerates ? 0.60f : 0.56f));
            Fit(value, 20, 36);

            Text timer = UiBuild.Label(row, "Sure", string.Empty, 17, TextAnchor.MiddleLeft);
            timer.color = groupColor;
            UiBuild.Anchor(timer.rectTransform, new Vector2(0.32f, 0.12f), new Vector2(0.90f, 0.36f));
            Fit(timer, 14, 17);

            _badge[index] = mark;
            _art[index] = art;
            _name[index] = name;
            _value[index] = value;
            _timer[index] = timer;
            _builtRows++;
        }

        /// <summary>
        /// Uses an Inspector override first, then the original currency-icons art. The imported
        /// sprites reference the source textures and trim only transparent padding; no copied PNGs.
        /// </summary>
        private Sprite ResolveIcon(CurrencyId id)
        {
            for (int i = 0; iconOverrides != null && i < iconOverrides.Length; i++)
                if (iconOverrides[i].currency == id && iconOverrides[i].icon != null)
                    return iconOverrides[i].icon;

            return Resources.Load<Sprite>("UI/Wallet/" + id);
        }

        private void Refresh()
        {
            if (_title != null) _title.text = Loc.T("wallet.title");
            if (_closeLabel != null) _closeLabel.text = Loc.T("wallet.close");
            if (_registry == null) return;
            for (int i = 0; i < _registry.Definitions.Count; i++)
            {
                CurrencyDefinition definition = _registry.Definitions[i];
                int index = (int)definition.Id;
                if (_name[index] != null) _name[index].text = Loc.T(definition.LocalizedNameKey);
                if (!_registry.TryGetSnapshot(definition.Id, out CurrencySnapshot snapshot)) continue;
                if (_value[index] != null) _value[index].text = Format(snapshot);
                if (_timer[index] != null) _timer[index].text = RegenerationText(snapshot);
            }
            RefreshGroupLabels();
        }

        private void RefreshGroupLabels()
        {
            for (int i = 0; i < _groupLabels.Length; i++)
                if (_groupLabels[i] != null) _groupLabels[i].text = Loc.T(GroupKey(GroupOrder[i]));
        }

        private string Format(CurrencySnapshot snapshot)
        {
            if (snapshot.Id == CurrencyId.Cash)
                return NumberFormatter.Format(snapshot.Current);
            string amount = snapshot.WholeAmount.ToString(CultureInfo.InvariantCulture);
            return snapshot.HasMaximum
                ? amount + "/" + snapshot.Maximum.ToString(CultureInfo.InvariantCulture)
                : amount;
        }

        private static string RegenerationText(CurrencySnapshot snapshot)
        {
            if (!snapshot.IsRegenerating) return string.Empty;
            if (snapshot.SecondsToNextRegeneration <= 0d)
                return Loc.T("wallet.full");
            return string.Format(Loc.T("wallet.next_regen"),
                                 UiBuild.Clock((float)snapshot.SecondsToNextRegeneration));
        }

        private static CurrencyCategory DisplayCategory(CurrencyCategory category)
            => category == CurrencyCategory.Premium ? CurrencyCategory.MainEconomy : category;

        private static string GroupKey(CurrencyCategory category)
        {
            switch (category)
            {
                case CurrencyCategory.MainEconomy: return "wallet.group.main";
                case CurrencyCategory.Sea: return "wallet.group.sea";
                case CurrencyCategory.Crafting: return "wallet.group.crafting";
                case CurrencyCategory.MiningGear: return "wallet.group.mining";
                default: return "wallet.group.pets";
            }
        }

        /// <summary>
        /// The fallback badge's mark: the glyph the rest of the game already uses for that currency
        /// where there is one (◆ gems on every reward line, ◉ and ✦ on the pet screen), otherwise the
        /// localized name's initials — "MP"/"MS" in English, "MP"/"MH" in Turkish — so no two rows
        /// share a mark the way the old single letters did (two "P", two "E").
        /// </summary>
        private static string BadgeMark(CurrencyDefinition definition)
        {
            switch (definition.Id)
            {
                case CurrencyId.Cash: return "$";
                case CurrencyId.Gems: return "◆";
                case CurrencyId.Pearls: return "◉";
                case CurrencyId.PetEssence: return "✦";
                default: return Initials(Loc.T(definition.LocalizedNameKey));
            }
        }

        private static string Initials(string name)
        {
            string marks = string.Empty;
            bool atWordStart = true;
            for (int i = 0; i < name.Length && marks.Length < 2; i++)
            {
                char c = name[i];
                if (char.IsWhiteSpace(c) || c == '-') { atWordStart = true; continue; }
                if (atWordStart && char.IsLetter(c)) marks += char.ToUpperInvariant(c);
                atWordStart = false;
            }
            return marks;
        }

        private void RefreshBadges()
        {
            if (_registry == null) return;
            for (int i = 0; i < _registry.Definitions.Count; i++)
            {
                CurrencyDefinition definition = _registry.Definitions[i];
                Text mark = _badge[(int)definition.Id];
                if (mark != null) mark.text = BadgeMark(definition);
            }
        }

        private static Color CategoryTint(CurrencyCategory category)
        {
            switch (category)
            {
                case CurrencyCategory.MainEconomy: return new Color(0.20f, 0.62f, 0.34f, 1f);
                case CurrencyCategory.Premium: return new Color(0.26f, 0.60f, 0.92f, 1f);
                case CurrencyCategory.Sea: return new Color(0.12f, 0.46f, 0.62f, 1f);
                case CurrencyCategory.Crafting: return new Color(0.70f, 0.48f, 0.16f, 1f);
                case CurrencyCategory.MiningGear: return new Color(0.52f, 0.40f, 0.30f, 1f);
                default: return new Color(0.62f, 0.38f, 0.92f, 1f);
            }
        }

        /// <summary>Shrink-to-fit so a long translation stays inside its cell at every width.</summary>
        private static void Fit(Text label, int min, int max)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private void OnSourceChanged()
        {
            if (IsOpen) Refresh();
        }

        private void OnLanguageChanged()
        {
            if (_openerLabel != null) _openerLabel.text = Loc.T("wallet.title");
            RefreshBadges();
            if (IsOpen) Refresh();
        }
    }
}
