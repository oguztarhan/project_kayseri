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
        [Header("Wallet layout")]
        [SerializeField] private int sortingOrder = 120;
        [SerializeField] private float rowHeight = 82f;
        [SerializeField] private float rowSpacing = 10f;
        [SerializeField] private Color scrim = new Color(0f, 0f, 0f, 0.66f);
        [SerializeField] private Color backdrop = new Color(0.92f, 0.94f, 0.99f, 0.98f);
        [SerializeField] private Color rowColor = new Color(0.17f, 0.21f, 0.31f, 1f);
        [SerializeField] private Color groupColor = new Color(0.10f, 0.14f, 0.23f, 1f);

        private static readonly CurrencyCategory[] GroupOrder =
        {
            CurrencyCategory.MainEconomy,
            CurrencyCategory.Sea,
            CurrencyCategory.Crafting,
            CurrencyCategory.MiningGear,
            CurrencyCategory.Pets
        };

        private readonly Text[] _name = new Text[10];
        private readonly Text[] _value = new Text[10];
        private readonly Text[] _timer = new Text[10];
        private readonly Text[] _icon = new Text[10];

        private HudUI _hud;
        private CurrencyRegistry _registry;
        private LocalizationService _loc;
        private RectTransform _root;
        private RectTransform _content;
        private float _pollTimer;
        private bool _initialized;
        private int _builtRows;
        private int _builtGroups;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public int BuiltRowCount => _builtRows;
        public int BuiltGroupCount => _builtGroups;

        public string DisplayedValue(CurrencyId id)
        {
            int index = (int)id;
            return index >= 0 && index < _value.Length && _value[index] != null
                ? _value[index].text
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

        private void Update()
        {
            if (!IsOpen) return;
            _pollTimer -= Time.unscaledDeltaTime;
            if (_pollTimer > 0f) return;
            _pollTimer = 1f;
            Refresh();
        }

        public void Show()
        {
            if (!_initialized) Initialize(FindAnyObjectByType<HudUI>(FindObjectsInactive.Include));
            if (_root == null) Build();
            if (_root != null) _root.gameObject.SetActive(true);
            Refresh();
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void BuildOpener()
        {
            if (_hud == null) return;
            Sprite icon = Resources.Load<Sprite>("UI/Buttons/bilgi");
            _hud.AttachBottomButton(16, "BtnCuzdan", icon != null ? icon : UiSkin.ButtonYellow, Show);
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
            BuildHeader(sheet);
            BuildScroll(sheet);
            UiBuild.InsetContent(_root);
        }

        private void BuildHeader(RectTransform sheet)
        {
            RectTransform header = UiBuild.Flat(sheet, "Baslik", groupColor,
                                                new Vector2(0.025f, 0.900f), new Vector2(0.975f, 0.975f));
            Text title = UiBuild.Label(header, "Baslik", Loc.T("wallet.title"), 34, TextAnchor.MiddleLeft);
            title.color = Color.white;
            title.rectTransform.offsetMin = new Vector2(28f, 0f);
            title.rectTransform.offsetMax = new Vector2(-190f, 0f);

            Button close = UiBuild.Btn(header, "Kapat", Loc.T("wallet.close"), UiSkin.ButtonYellow,
                                       new Color(0.25f, 0.54f, 0.78f, 1f), 23, Hide);
            RectTransform closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = new Vector2(1f, 0.12f);
            closeRect.anchorMax = new Vector2(1f, 0.88f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(160f, 0f);
            closeRect.anchoredPosition = new Vector2(-20f, 0f);
        }

        private void BuildScroll(RectTransform sheet)
        {
            var viewportGo = new GameObject("Gorunum", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(sheet, false);
            RectTransform viewport = (RectTransform)viewportGo.transform;
            viewport.anchorMin = new Vector2(0.025f, 0.045f);
            viewport.anchorMax = new Vector2(0.975f, 0.885f);
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

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

            Text icon = UiBuild.Label(row, "Simge", IconFor(definition.Id), 30, TextAnchor.MiddleCenter);
            icon.color = Color.white;
            icon.rectTransform.anchorMin = new Vector2(0f, 0f);
            icon.rectTransform.anchorMax = new Vector2(0f, 1f);
            icon.rectTransform.sizeDelta = new Vector2(80f, 0f);
            icon.rectTransform.anchoredPosition = new Vector2(40f, 0f);

            Text name = UiBuild.Label(row, "Ad", Loc.T(definition.LocalizedNameKey), 25, TextAnchor.MiddleLeft);
            name.color = Color.white;
            name.rectTransform.anchorMin = new Vector2(0f, 0f);
            name.rectTransform.anchorMax = new Vector2(0.58f, 1f);
            name.rectTransform.offsetMin = new Vector2(92f, 0f);
            name.rectTransform.offsetMax = Vector2.zero;

            Text value = UiBuild.Label(row, "Deger", "0", 25, TextAnchor.MiddleRight);
            value.color = Color.white;
            value.rectTransform.anchorMin = new Vector2(0.58f, 0.50f);
            value.rectTransform.anchorMax = new Vector2(0.98f, 1f);
            value.rectTransform.offsetMin = Vector2.zero;
            value.rectTransform.offsetMax = new Vector2(-18f, 0f);

            Text timer = UiBuild.Label(row, "Sure", "", 18, TextAnchor.MiddleRight);
            timer.color = new Color(0.62f, 0.78f, 0.92f, 1f);
            timer.rectTransform.anchorMin = new Vector2(0.58f, 0f);
            timer.rectTransform.anchorMax = new Vector2(0.98f, 0.52f);
            timer.rectTransform.offsetMin = Vector2.zero;
            timer.rectTransform.offsetMax = new Vector2(-18f, 0f);

            _icon[index] = icon;
            _name[index] = name;
            _value[index] = value;
            _timer[index] = timer;
            _builtRows++;
        }

        private void Refresh()
        {
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

        private static string IconFor(CurrencyId id)
        {
            switch (id)
            {
                case CurrencyId.Cash: return "$";
                case CurrencyId.Gems: return "◆";
                case CurrencyId.Salvage: return "S";
                case CurrencyId.Charts: return "C";
                case CurrencyId.CombatEnergy: return "E";
                case CurrencyId.CraftPoints: return "P";
                case CurrencyId.MiningPoints: return "M";
                case CurrencyId.MiningScrap: return "R";
                case CurrencyId.Pearls: return "P";
                default: return "E";
            }
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
            if (IsOpen) Refresh();
        }
    }
}
