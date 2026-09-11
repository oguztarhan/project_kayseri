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
        [SerializeField] private float rowHeight = 104f;
        [SerializeField] private float rowSpacing = 10f;
        [SerializeField] private float iconSize = 72f;
        [SerializeField] private Color scrim = new Color(0f, 0f, 0f, 0.66f);
        [SerializeField] private Color backdrop = new Color(0.92f, 0.94f, 0.99f, 0.98f);
        [SerializeField] private Color rowColor = new Color(0.17f, 0.21f, 0.31f, 1f);
        [SerializeField] private Color groupColor = new Color(0.10f, 0.14f, 0.23f, 1f);
        [SerializeField] private Color descriptionColor = new Color(0.72f, 0.78f, 0.88f, 1f);

        [Header("Simgeler")]
        [Tooltip("İsteğe bağlı elle atanan simgeler. Boş kalan para birimi önce oyunun zaten yüklediği " +
                 "sanatı (nakit, elmas, deniz kiti, atölye), o da yoksa harf rozetini kullanır. " +
                 "Savaş enerjisi, maden puanı, maden hurdası, inci ve pet özü için şu an çalışma " +
                 "zamanında yüklenebilir uygun sanat yok.")]
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
        private readonly Text[] _description = new Text[CurrencyCount];
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
        public string DisplayedDescription(CurrencyId id) => TextOf(_description, id);
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
            Sprite icon = Resources.Load<Sprite>("UI/Buttons/bilgi");
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
                                               new Vector2(0.035f, 0.035f), new Vector2(0.965f, 0.965f));
            // The sheet eats its own taps. Without a handler here a tap on any row walked up to the
            // scrim's dismiss button and closed the wallet — see MiningGearUI.BuildBackdrop.
            sheet.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            BuildHeader(sheet);
            BuildScroll(sheet);
            RefreshBadges();
            UiBuild.InsetContent(_root);
        }

        private void BuildHeader(RectTransform sheet)
        {
            RectTransform header = UiBuild.Flat(sheet, "Baslik", groupColor,
                                                new Vector2(0.025f, 0.900f), new Vector2(0.975f, 0.975f));
            _title = UiBuild.Label(header, "Baslik", Loc.T("wallet.title"), 34, TextAnchor.MiddleLeft);
            _title.color = Color.white;
            _title.rectTransform.offsetMin = new Vector2(28f, 0f);
            _title.rectTransform.offsetMax = new Vector2(-190f, 0f);

            Button close = UiBuild.Btn(header, "Kapat", Loc.T("wallet.close"), UiSkin.ButtonYellow,
                                       new Color(0.25f, 0.54f, 0.78f, 1f), 23, Hide);
            _closeLabel = close.GetComponentInChildren<Text>();
            Fit(_closeLabel, 14, 23);
            RectTransform closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 0.12f);
            closeRect.anchorMax = new Vector2(1f, 0.88f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(160f, 0f);
            closeRect.anchoredPosition = new Vector2(-20f, 0f);
        }

        private void BuildScroll(RectTransform sheet)
        {
            // RectMask2D, not Mask: a stencil Mask on a see-through image writes no stencil at all
            // (the UI shader alpha-clips it away), which left every row invisible in the running
            // game. RectMask2D clips by rect, costs no extra draw calls, and the clear Image stays
            // only as the raycast surface that lets a drag in the gaps scroll the list.
            var viewportGo = new GameObject("Gorunum", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportGo.transform.SetParent(sheet, false);
            RectTransform viewport = (RectTransform)viewportGo.transform;
            viewport.anchorMin = new Vector2(0.025f, 0.045f);
            viewport.anchorMax = new Vector2(0.975f, 0.885f);
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewportGo.GetComponent<Image>().color = Color.clear;

            var scrollGo = new GameObject("Kaydir", typeof(ScrollRect));
            scrollGo.transform.SetParent(viewport, false);
            RectTransform scrollRect = (RectTransform)scrollGo.transform;
            UiBuild.Anchor(scrollRect, Vector2.zero, Vector2.one);
            ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 55f;

            var contentGo = new GameObject("Icerik", typeof(RectTransform), typeof(VerticalLayoutGroup),
                                           typeof(ContentSizeFitter));
            contentGo.transform.SetParent(scrollRect, false);
            _content = (RectTransform)contentGo.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.sizeDelta = new Vector2(0f, 0f);
            VerticalLayoutGroup layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = rowSpacing;
            layout.padding = new RectOffset(0, 0, 0, 20);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _content;
            scroll.viewport = viewport;

            BuildGroups();
        }

        private void BuildGroups()
        {
            if (_registry == null || _content == null) return;
            _builtRows = 0;
            _builtGroups = 0;
            for (int groupIndex = 0; groupIndex < GroupOrder.Length; groupIndex++)
            {
                CurrencyCategory category = GroupOrder[groupIndex];
                bool hasEntry = false;
                for (int i = 0; i < _registry.Definitions.Count; i++)
                {
                    CurrencyDefinition definition = _registry.Definitions[i];
                    if (DisplayCategory(definition.Category) != category) continue;
                    hasEntry = true;
                    break;
                }
                if (!hasEntry) continue;

                Text header = UiBuild.Label(_content, "Grup", Loc.T(GroupKey(category)), 26,
                                            TextAnchor.MiddleLeft);
                header.color = new Color(0.11f, 0.17f, 0.27f, 1f);
                AddHeight(header.gameObject, 56f);
                _builtGroups++;

                for (int i = 0; i < _registry.Definitions.Count; i++)
                {
                    CurrencyDefinition definition = _registry.Definitions[i];
                    if (DisplayCategory(definition.Category) != category) continue;
                    BuildRow(definition);
                }
            }
        }

        private void BuildRow(CurrencyDefinition definition)
        {
            int index = (int)definition.Id;
            RectTransform row = UiBuild.Flat(_content, "Para_" + definition.Id,
                                             rowColor, Vector2.zero, Vector2.one);
            AddHeight(row.gameObject, rowHeight);
            float textLeft = iconSize + 32f;

            // The badge is always there; real art covers it when the currency has any. A tinted
            // square with the currency's mark keeps the column even when it does not.
            RectTransform badge = UiBuild.Flat(row, "Simge", CategoryTint(definition.Category),
                                               new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            badge.sizeDelta = new Vector2(iconSize, iconSize);
            badge.anchoredPosition = new Vector2(16f + iconSize * 0.5f, 0f);
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

            Text name = UiBuild.Label(row, "Ad", Loc.T(definition.LocalizedNameKey), 24, TextAnchor.LowerLeft);
            name.color = Color.white;
            name.rectTransform.anchorMin = new Vector2(0f, 0.52f);
            name.rectTransform.anchorMax = new Vector2(0.62f, 1f);
            name.rectTransform.offsetMin = new Vector2(textLeft, 0f);
            name.rectTransform.offsetMax = new Vector2(0f, -8f);
            Fit(name, 14, 24);

            // Regenerating rows keep the lower right for the countdown; the rest let the line run on.
            Text description = UiBuild.Label(row, "Aciklama", Loc.T(definition.LocalizedDescriptionKey), 17,
                                             TextAnchor.UpperLeft);
            description.color = descriptionColor;
            description.fontStyle = FontStyle.Normal;
            description.rectTransform.anchorMin = new Vector2(0f, 0f);
            description.rectTransform.anchorMax = new Vector2(definition.Regenerates ? 0.62f : 0.98f, 0.50f);
            description.rectTransform.offsetMin = new Vector2(textLeft, 6f);
            description.rectTransform.offsetMax = new Vector2(-8f, 0f);
            Fit(description, 11, 17);

            Text value = UiBuild.Label(row, "Deger", "0", 25, TextAnchor.LowerRight);
            value.color = Color.white;
            value.rectTransform.anchorMin = new Vector2(0.62f, 0.52f);
            value.rectTransform.anchorMax = new Vector2(0.98f, 1f);
            value.rectTransform.offsetMin = Vector2.zero;
            value.rectTransform.offsetMax = new Vector2(-18f, -8f);
            Fit(value, 14, 25);

            Text timer = UiBuild.Label(row, "Sure", string.Empty, 18, TextAnchor.UpperRight);
            timer.color = new Color(0.62f, 0.78f, 0.92f, 1f);
            timer.rectTransform.anchorMin = new Vector2(0.62f, 0f);
            timer.rectTransform.anchorMax = new Vector2(0.98f, 0.50f);
            timer.rectTransform.offsetMin = new Vector2(0f, 6f);
            timer.rectTransform.offsetMax = new Vector2(-18f, 0f);
            Fit(timer, 12, 18);

            _badge[index] = mark;
            _art[index] = art;
            _name[index] = name;
            _description[index] = description;
            _value[index] = value;
            _timer[index] = timer;
            _builtRows++;
        }

        /// <summary>
        /// Art already in the game, in this order: a hand-wired override, then what is loadable at
        /// runtime — the HUD skin's coin and gem, the sea kit's salvage and chart, the workshop's own
        /// opener art. Null means the letter badge stays.
        /// The sea kit is an atlas page (2048² ASTC); it is only touched when the wallet is first
        /// opened, never at boot.
        /// </summary>
        private Sprite ResolveIcon(CurrencyId id)
        {
            for (int i = 0; iconOverrides != null && i < iconOverrides.Length; i++)
                if (iconOverrides[i].currency == id && iconOverrides[i].icon != null)
                    return iconOverrides[i].icon;

            switch (id)
            {
                case CurrencyId.Cash: return UiSkin.Coin;
                case CurrencyId.Gems: return UiSkin.Gem;
                case CurrencyId.Salvage: return SeaKit.Get("hurda");
                case CurrencyId.Charts: return SeaKit.Get("harita");
                case CurrencyId.CraftPoints: return Resources.Load<Sprite>("UI/Buttons/atolye");
                // Not PetConfig.PearlIcon: it is wired to ikon_altin, the placeholder gold coin, and
                // in a list beside Cash it reads as a second cash row. The ◉ badge until pearl art lands.
                default: return null;
            }
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
                if (_description[index] != null) _description[index].text = Loc.T(definition.LocalizedDescriptionKey);
                if (!_registry.TryGetSnapshot(definition.Id, out CurrencySnapshot snapshot)) continue;
                if (_value[index] != null) _value[index].text = Format(snapshot);
                if (_timer[index] != null) _timer[index].text = RegenerationText(snapshot);
            }
            RefreshGroupLabels();
        }

        private void RefreshGroupLabels()
        {
            if (_content == null) return;
            int group = 0;
            for (int i = 0; i < _content.childCount && group < GroupOrder.Length; i++)
            {
                Transform child = _content.GetChild(i);
                if (child.name != "Grup") continue;
                Text label = child.GetComponentInChildren<Text>();
                if (label != null) label.text = Loc.T(GroupKey(GroupOrder[group++]));
            }
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

        private static void AddHeight(GameObject go, float height)
        {
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
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
