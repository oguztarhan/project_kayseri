using Game.Core;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The depo: one shell over the two things the player owns that are not money — the ship's
    /// gear, and the production chain the islands run.
    ///
    /// TWO TABS, TWO MODELS, DELIBERATELY NOT MERGED. Ore and refined goods flow through
    /// <see cref="Refining"/> in tonnes and are sold; ship gear is four worn stat blocks and a shelf
    /// of kept ones. They are not the same kind of thing and one grid holding both would have to lie
    /// about at least one of them, so DONANIM draws <see cref="ExpeditionService"/>'s slots and shelf
    /// and KATALOG draws <see cref="Catalogue"/>'s chain, and the tab is the honest seam between them.
    ///
    /// THIS SCREEN IS A READER. Every button below calls a service and then redraws whatever came
    /// back: the shelf's capacity, the swap, the hurda and the bench's lesson are all
    /// <see cref="ExpeditionService"/>'s to decide, and none of them is worked out here. The
    /// catalogue tab mutates nothing at all.
    ///
    /// A CARD IS TAPPED BY ID, NEVER BY CELL. The shelf re-orders itself whenever something leaves
    /// the middle of it, so every action carries the item's id and the service refuses one it cannot
    /// find — see <see cref="GearStash"/>. Holding the cell index instead would scrap the item that
    /// slid into the row under the player's finger.
    ///
    /// BUILT IN CODE, and hung off the workshop rather than the HUD's bottom row. That row is six
    /// openers wide already and it re-centres itself on every attach, so a seventh would start
    /// pushing the ends off a narrow screen; the workshop's bench is also simply where a depo
    /// belongs. <see cref="CraftingUI"/> makes this object if the scene has none and hands over its
    /// own sprites, so the depo wears the workshop's art without a scene edit.
    /// </summary>
    public sealed class InventoryUI : MonoBehaviour
    {
        [SerializeField] private int sortingOrder = 115;

        [Header("Görseller")]
        [Tooltip("Kart gövdesi — MaviSet/panel_beyaz.")]
        [SerializeField] private Sprite cardPanel;
        [Tooltip("Başlık şeridi — MaviSet/serit_mavi.")]
        [SerializeField] private Sprite ribbon;
        [Tooltip("Düğmeler — MaviSet/btn_hap_kalin.")]
        [SerializeField] private Sprite actionButton;
        [Tooltip("Kapat düğmesi — MaviSet/btn_kapat_yeni.")]
        [SerializeField] private Sprite closeIcon;
        [Tooltip("Sayaç hapı — MaviSet/gosterge_grafit.")]
        [SerializeField] private Sprite chipPill;

        [Header("Renkler")]
        [SerializeField] private Color scrim = new Color(0f, 0f, 0f, 0.62f);
        [SerializeField] private Color backdrop = new Color(0.92f, 0.94f, 0.99f, 0.98f);

        /// <summary>The grid's width. Five across by four down is the depo's default twenty.</summary>
        private const int Columns = 5;

        /// <summary>
        /// The most cells the grid will ever draw. It sizes itself to the tuned capacity at build
        /// time (see <see cref="_cells"/>) and stops here: a capacity raised past this draws its
        /// first thirty, and the rest of the shelf is still reachable through PARÇALA, which
        /// empties all of it. Thirty is six rows, which is as deep as the card is tall.
        /// </summary>
        private const int MaxCells = 30;

        /// <summary>How many cells this build actually drew — the tuned capacity, or the shelf's
        /// own count when a lowered capacity has left it over-full.</summary>
        private int _cells = MaxCells;

        /// <summary>The grade ladder's ink — the same five the workshop, the captains and the sea wear.</summary>
        private static readonly Color[] GradeTint =
        {
            new Color(0.48f, 0.54f, 0.62f, 1f),
            new Color(0.26f, 0.60f, 0.92f, 1f),
            new Color(0.62f, 0.38f, 0.92f, 1f),
            new Color(0.96f, 0.66f, 0.18f, 1f),
            new Color(0.94f, 0.28f, 0.42f, 1f),
        };

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color InkFaint = new Color(0.58f, 0.63f, 0.71f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);
        private static readonly Color Good = new Color(0.24f, 0.68f, 0.36f, 1f);
        private static readonly Color Warn = new Color(0.94f, 0.68f, 0.20f, 1f);
        private static readonly Color Empty = new Color(0.86f, 0.88f, 0.92f, 1f);
        private static readonly Color Selected = new Color(0.12f, 0.72f, 0.98f, 1f);
        private const float RibbonBand = 0.677f;

        private ExpeditionService _sea;
        private CraftingService _crafting;
        private MarketService _market;
        private LocalizationService _loc;

        private RectTransform _root, _gearPage, _cataloguePage;
        private Text _titleLabel, _gearTabLabel, _catalogueTabLabel;
        private Image _gearTabArt, _catalogueTabArt;

        // ---- donanım
        private Text _powerLabel, _shelfLabel, _hintLabel, _scrapAllLabel;
        private Button _scrapAllBtn;
        private readonly Image[] _wornArt = new Image[SeaCombat.SlotCount];
        private readonly Text[] _wornSlot = new Text[SeaCombat.SlotCount];
        private readonly Text[] _wornGrade = new Text[SeaCombat.SlotCount];
        private readonly Text[] _wornScore = new Text[SeaCombat.SlotCount];
        private readonly Image[] _cellArt = new Image[MaxCells];
        private readonly Text[] _cellSlot = new Text[MaxCells];
        private readonly Text[] _cellScore = new Text[MaxCells];
        private readonly Text[] _cellArrow = new Text[MaxCells];
        private readonly GameObject[] _cellGo = new GameObject[MaxCells];

        private RectTransform _actionStrip;
        private Button _primaryBtn, _secondaryBtn;
        private Text _primaryLabel, _secondaryLabel;

        // ---- katalog
        private readonly Text[] _entryName = new Text[64];
        private readonly Text[] _entryState = new Text[64];
        private readonly Image[] _entryStripe = new Image[64];
        private Text _oresTitle, _goodsTitle;

        /// <summary>What the action strip is pointed at: a shelf id, or a worn slot, or nothing.
        /// Never a cell index — see the class header.</summary>
        private long _pickedId = GearStash.NoId;
        private int _pickedSlot = -1;

        private bool _showCatalogue;

        /// <summary>Scratch for <see cref="Catalogue.IsDiscovered"/>, refilled on every catalogue
        /// draw. One array rather than one per row: eighteen rows ask the same question.</summary>
        private readonly bool[] _owned = new bool[8];

        /// <summary>
        /// Services and subscriptions only. THE SCREEN IS BUILT ON FIRST OPEN, not here, for two
        /// reasons: <see cref="Adopt"/> hands over the workshop's sprites and cannot run before
        /// AddComponent has already returned from this method, and a player who never opens the
        /// depo should not pay for sixty of its objects.
        /// </summary>
        private void Awake()
        {
            _sea = ServiceLocator.Get<ExpeditionService>();
            _crafting = ServiceLocator.Get<CraftingService>();
            _market = ServiceLocator.Get<MarketService>();
            if (_sea != null) _sea.Changed += OnChanged;
            if (_crafting != null) _crafting.Changed += OnChanged;
            _loc = ServiceLocator.Get<LocalizationService>();
            if (_loc != null) _loc.Changed += OnLanguageChanged;
        }

        private void OnDestroy()
        {
            if (_sea != null) _sea.Changed -= OnChanged;
            if (_crafting != null) _crafting.Changed -= OnChanged;
            if (_loc != null) _loc.Changed -= OnLanguageChanged;
        }

        /// <summary>
        /// Borrow the workshop's own sprites. Called by <see cref="CraftingUI"/> right after it
        /// makes this object, and only ever fills a slot the Inspector left empty — a scene that
        /// authors this screen properly keeps whatever it authored.
        /// </summary>
        public void Adopt(Sprite panel, Sprite band, Sprite button, Sprite close, Sprite chip)
        {
            if (cardPanel == null) cardPanel = panel;
            if (ribbon == null) ribbon = band;
            if (actionButton == null) actionButton = button;
            if (closeIcon == null) closeIcon = close;
            if (chipPill == null) chipPill = chip;
        }

        private void OnChanged()
        {
            if (_root != null && _root.gameObject.activeSelf) Refresh();
        }

        private void OnLanguageChanged()
        {
            if (_root != null && _root.gameObject.activeSelf) Refresh();
        }

        public void Show()
        {
            if (_root == null) Build();
            if (_root != null) _root.gameObject.SetActive(true);
            Refresh();
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
            ClearPick();
        }

        // ------------------------------------------------------------------ build
        private void Build()
        {
            RectTransform canvas = UiBuild.Canvas(transform, "DepoKanvas", sortingOrder);
            _root = UiBuild.Flat(canvas, "Karartma", scrim, Vector2.zero, Vector2.one);
            var dismiss = _root.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            BuildBackdrop();
            BuildHeader();
            BuildTabs();

            _gearPage = Zone(_root, "Donanim", new Vector2(0.020f, 0.020f), new Vector2(0.980f, 0.740f));
            _cataloguePage = Zone(_root, "Katalog", new Vector2(0.020f, 0.020f), new Vector2(0.980f, 0.740f));
            BuildGearPage();
            BuildCataloguePage();
        }

        /// <summary>One opaque sheet behind everything — see CraftingUI.BuildBackdrop for why.</summary>
        private void BuildBackdrop()
        {
            RectTransform sheet = Art(_root, "Zemin", cardPanel,
                                      new Vector2(0.020f, 0.020f), new Vector2(0.980f, 0.842f));
            var image = sheet.GetComponent<Image>();
            image.color = backdrop;
            image.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
        }

        private void BuildHeader()
        {
            RectTransform band = Art(_root, "Serit", ribbon,
                                     new Vector2(0.360f, 0.850f), new Vector2(0.640f, 0.992f));
            _titleLabel = UiBuild.Label(Zone(band, "Yazi", new Vector2(0.13f, RibbonBand - 0.13f),
                                             new Vector2(0.87f, RibbonBand + 0.13f)),
                                        "Text", Loc.T("depo.baslik"), 38, TextAnchor.MiddleCenter);

            Button close = UiBuild.Btn(_root, "Kapat", string.Empty,
                                       closeIcon != null ? closeIcon : UiSkin.ButtonGrey,
                                       new Color(0.30f, 0.34f, 0.42f, 1f), 34, Hide);
            var closeImage = close.GetComponent<Image>();
            closeImage.type = Image.Type.Simple;
            closeImage.preserveAspect = true;
            UiBuild.Anchor((RectTransform)close.transform,
                           new Vector2(0.878f, 0.873f), new Vector2(0.938f, 0.970f));
        }

        /// <summary>The two tabs. The unselected one is grey art, so which page is up reads at a
        /// glance rather than from the text alone.</summary>
        private void BuildTabs()
        {
            Button gear = UiBuild.Btn(_root, "SekmeDonanim", string.Empty,
                                      actionButton != null ? actionButton : UiSkin.ButtonBlue,
                                      Selected, 26, () => SetTab(false));
            UiBuild.Anchor((RectTransform)gear.transform,
                           new Vector2(0.055f, 0.762f), new Vector2(0.495f, 0.838f));
            PillFit.Wrap(gear.GetComponent<Image>());
            _gearTabArt = gear.GetComponent<Image>();
            _gearTabLabel = gear.GetComponentInChildren<Text>();
            Fit(_gearTabLabel, 14, 26);

            Button cat = UiBuild.Btn(_root, "SekmeKatalog", string.Empty,
                                     actionButton != null ? actionButton : UiSkin.ButtonBlue,
                                     Selected, 26, () => SetTab(true));
            UiBuild.Anchor((RectTransform)cat.transform,
                           new Vector2(0.505f, 0.762f), new Vector2(0.945f, 0.838f));
            PillFit.Wrap(cat.GetComponent<Image>());
            _catalogueTabArt = cat.GetComponent<Image>();
            _catalogueTabLabel = cat.GetComponentInChildren<Text>();
            Fit(_catalogueTabLabel, 14, 26);
        }

        // ------------------------------------------------------------- donanım
        private void BuildGearPage()
        {
            // The worn four, across the top: what the ship is actually wearing, and the headline
            // the depo is here to move.
            RectTransform worn = Art(_gearPage, "Takili", cardPanel,
                                     new Vector2(0f, 0.640f), new Vector2(1f, 1f));

            _powerLabel = UiBuild.Label(Zone(worn, "Guc", new Vector2(0.05f, 0.780f), new Vector2(0.95f, 0.940f)),
                                        "Text", string.Empty, 32, TextAnchor.MiddleCenter);
            _powerLabel.color = Ink;

            const float left = 0.035f, right = 0.965f;
            float w = (right - left) / SeaCombat.SlotCount;
            for (int slot = 0; slot < SeaCombat.SlotCount; slot++)
            {
                int captured = slot;
                RectTransform cell = Art(worn, "Slot" + slot, cardPanel,
                                         new Vector2(left + captured * w + 0.008f, 0.090f),
                                         new Vector2(left + (captured + 1) * w - 0.008f, 0.740f));
                _wornArt[slot] = cell.GetComponent<Image>();
                _wornArt[slot].raycastTarget = true;
                var tap = cell.gameObject.AddComponent<Button>();
                tap.transition = Selectable.Transition.None;
                tap.onClick.AddListener(() => PickWorn(captured));

                _wornSlot[slot] = UiBuild.Label(Zone(cell, "Ad", new Vector2(0.05f, 0.660f), new Vector2(0.95f, 0.940f)),
                                                "Text", string.Empty, 20, TextAnchor.MiddleCenter);
                Fit(_wornSlot[slot], 11, 20);
                _wornGrade[slot] = UiBuild.Label(Zone(cell, "Derece", new Vector2(0.05f, 0.340f), new Vector2(0.95f, 0.640f)),
                                                 "Text", string.Empty, 20, TextAnchor.MiddleCenter);
                Fit(_wornGrade[slot], 10, 20);
                _wornScore[slot] = UiBuild.Label(Zone(cell, "Guc", new Vector2(0.05f, 0.060f), new Vector2(0.95f, 0.330f)),
                                                 "Text", string.Empty, 22, TextAnchor.MiddleCenter);
                Fit(_wornScore[slot], 11, 22);
            }

            // The shelf.
            RectTransform shelf = Art(_gearPage, "Raf", cardPanel,
                                      new Vector2(0f, 0.130f), new Vector2(1f, 0.625f));

            _shelfLabel = UiBuild.Label(Zone(shelf, "Sayac", new Vector2(0.05f, 0.900f), new Vector2(0.62f, 0.985f)),
                                        "Text", string.Empty, 26, TextAnchor.MiddleLeft);
            _shelfLabel.color = Ink;

            _scrapAllBtn = UiBuild.Btn(shelf, "Parcala", string.Empty,
                                       actionButton != null ? actionButton : UiSkin.ButtonYellow,
                                       Warn, 20, OnScrapAll);
            UiBuild.Anchor((RectTransform)_scrapAllBtn.transform,
                           new Vector2(0.560f, 0.892f), new Vector2(0.960f, 0.992f));
            PillFit.Wrap(_scrapAllBtn.GetComponent<Image>());
            _scrapAllLabel = _scrapAllBtn.GetComponentInChildren<Text>();
            Fit(_scrapAllLabel, 11, 20);

            // The grid is exactly as deep as the depo it draws, so a shelf tuned to twenty is four
            // rows rather than four rows and an empty band. Capacity cannot move inside a session,
            // and a shelf left over-full by a lowered capacity brings its own count along so the
            // overflow is visible and can be dealt with one card at a time.
            const float top = 0.860f, bottom = 0.040f;
            _cells = _sea != null ? _sea.StashCapacity : GearStash.DefaultCapacity;
            if (_sea != null && _sea.StashCount > _cells) _cells = _sea.StashCount;
            if (_cells < Columns) _cells = Columns;
            if (_cells > MaxCells) _cells = MaxCells;
            int rows = (_cells + Columns - 1) / Columns;
            float ch = (top - bottom) / rows;
            float cw = 0.92f / Columns;
            for (int i = 0; i < _cells; i++)
            {
                int captured = i;
                int row = i / Columns, col = i % Columns;
                RectTransform cell = Art(shelf, "Goz" + i, cardPanel,
                                         new Vector2(0.04f + col * cw + 0.006f, top - (row + 1) * ch + 0.010f),
                                         new Vector2(0.04f + (col + 1) * cw - 0.006f, top - row * ch - 0.010f));
                _cellGo[i] = cell.gameObject;
                _cellArt[i] = cell.GetComponent<Image>();
                _cellArt[i].raycastTarget = true;
                var tap = cell.gameObject.AddComponent<Button>();
                tap.transition = Selectable.Transition.None;
                tap.onClick.AddListener(() => PickCell(captured));

                _cellSlot[i] = UiBuild.Label(Zone(cell, "Ad", new Vector2(0.06f, 0.520f), new Vector2(0.94f, 0.920f)),
                                             "Text", string.Empty, 17, TextAnchor.MiddleCenter);
                Fit(_cellSlot[i], 9, 17);
                _cellScore[i] = UiBuild.Label(Zone(cell, "Guc", new Vector2(0.06f, 0.100f), new Vector2(0.94f, 0.500f)),
                                              "Text", string.Empty, 19, TextAnchor.MiddleCenter);
                Fit(_cellScore[i], 9, 19);
                _cellArrow[i] = UiBuild.Label(Zone(cell, "Ok", new Vector2(0.62f, 0.600f), new Vector2(0.98f, 0.980f)),
                                              "Text", string.Empty, 20, TextAnchor.MiddleRight);
                _cellArrow[i].color = Good;
            }

            // What the tap does. Hidden until something is picked, so the strip is never two
            // buttons pointing at nothing.
            _actionStrip = Zone(_gearPage, "Islem", new Vector2(0f, 0.010f), new Vector2(1f, 0.120f));

            _hintLabel = UiBuild.Label(Zone(_gearPage, "Ipucu", new Vector2(0.04f, 0.010f), new Vector2(0.96f, 0.120f)),
                                       "Text", string.Empty, 20, TextAnchor.MiddleCenter);
            _hintLabel.color = InkFaint;
            Fit(_hintLabel, 12, 20);

            _primaryBtn = UiBuild.Btn(_actionStrip, "Birincil", string.Empty,
                                      actionButton != null ? actionButton : UiSkin.ButtonGreen,
                                      Good, 24, OnPrimary);
            UiBuild.Anchor((RectTransform)_primaryBtn.transform,
                           new Vector2(0.040f, 0.10f), new Vector2(0.485f, 0.92f));
            PillFit.Wrap(_primaryBtn.GetComponent<Image>());
            _primaryLabel = _primaryBtn.GetComponentInChildren<Text>();
            Fit(_primaryLabel, 12, 24);

            _secondaryBtn = UiBuild.Btn(_actionStrip, "Ikincil", string.Empty,
                                        actionButton != null ? actionButton : UiSkin.ButtonYellow,
                                        Warn, 24, OnSecondary);
            UiBuild.Anchor((RectTransform)_secondaryBtn.transform,
                           new Vector2(0.515f, 0.10f), new Vector2(0.960f, 0.92f));
            PillFit.Wrap(_secondaryBtn.GetComponent<Image>());
            _secondaryLabel = _secondaryBtn.GetComponentInChildren<Text>();
            Fit(_secondaryLabel, 12, 24);
        }

        // -------------------------------------------------------------- katalog
        /// <summary>
        /// The chain, in two columns: the eight ores on the left, the ten refined goods on the
        /// right. Two columns rather than one scrolling list because eighteen rows fit, and a list
        /// that fits is a list nobody has to drag to find the bottom of.
        /// </summary>
        private void BuildCataloguePage()
        {
            RectTransform ores = Art(_cataloguePage, "Cevherler", cardPanel,
                                     new Vector2(0f, 0f), new Vector2(0.487f, 1f));
            RectTransform goods = Art(_cataloguePage, "Urunler", cardPanel,
                                      new Vector2(0.513f, 0f), new Vector2(1f, 1f));

            _oresTitle = UiBuild.Label(Zone(ores, "Baslik", new Vector2(0.06f, 0.930f), new Vector2(0.94f, 0.990f)),
                                       "Text", string.Empty, 24, TextAnchor.MiddleCenter);
            _oresTitle.color = Ink;
            Fit(_oresTitle, 12, 24);
            _goodsTitle = UiBuild.Label(Zone(goods, "Baslik", new Vector2(0.06f, 0.930f), new Vector2(0.94f, 0.990f)),
                                        "Text", string.Empty, 24, TextAnchor.MiddleCenter);
            _goodsTitle.color = Ink;
            Fit(_goodsTitle, 12, 24);

            for (int e = 0; e < Catalogue.EntryCount && e < _entryName.Length; e++)
            {
                bool ore = Catalogue.IsOre(e);
                RectTransform column = ore ? ores : goods;
                int index = ore ? e : e - Catalogue.OreCount;
                int count = ore ? Catalogue.OreCount : Catalogue.ProductCount;

                const float top = 0.910f, bottom = 0.020f;
                float rh = (top - bottom) / count;
                RectTransform row = Zone(column, "Sira" + e,
                                         new Vector2(0.05f, top - (index + 1) * rh + 0.006f),
                                         new Vector2(0.95f, top - index * rh - 0.006f));

                _entryStripe[e] = Stripe(row, new Vector2(0f, 0.14f), new Vector2(0.030f, 0.86f));
                _entryName[e] = UiBuild.Label(Zone(row, "Ad", new Vector2(0.08f, 0.46f), new Vector2(1f, 1f)),
                                              "Text", string.Empty, 20, TextAnchor.LowerLeft);
                Fit(_entryName[e], 10, 20);
                _entryState[e] = UiBuild.Label(Zone(row, "Durum", new Vector2(0.08f, 0f), new Vector2(1f, 0.46f)),
                                               "Text", string.Empty, 17, TextAnchor.UpperLeft);
                Fit(_entryState[e], 9, 17);
            }
        }

        // -------------------------------------------------------------- actions
        private void SetTab(bool catalogue)
        {
            if (_showCatalogue == catalogue && _root != null && _root.gameObject.activeSelf) return;
            _showCatalogue = catalogue;
            ClearPick();
            Refresh();
        }

        private void PickWorn(int slot)
        {
            SeaCombat.Item worn = _sea != null ? _sea.GearItem(slot) : new SeaCombat.Item { Grade = -1 };
            if (worn.Grade < 0) { ClearPick(); Refresh(); return; }
            _pickedSlot = slot;
            _pickedId = GearStash.NoId;
            Refresh();
        }

        private void PickCell(int cell)
        {
            long id = _sea != null ? _sea.StashIdAt(cell) : GearStash.NoId;
            if (id <= GearStash.NoId) { ClearPick(); Refresh(); return; }
            _pickedId = id;
            _pickedSlot = -1;
            Refresh();
        }

        private void ClearPick()
        {
            _pickedId = GearStash.NoId;
            _pickedSlot = -1;
        }

        /// <summary>GİYDİR on a shelved item, DEPOYA on a worn one — in both cases the move that
        /// keeps everything and pays nothing.</summary>
        private void OnPrimary()
        {
            if (_sea == null) return;
            if (_pickedId > GearStash.NoId)
            {
                if (_sea.EquipFromStash(_pickedId)) { ClearPick(); Tap(); }
            }
            else if (_pickedSlot >= 0)
            {
                if (_sea.StowWorn(_pickedSlot)) { ClearPick(); Tap(); }
            }
            Refresh();
        }

        /// <summary>SÖK, from either side. The item is gone for hurda and a lesson.</summary>
        private void OnSecondary()
        {
            if (_sea == null) return;
            if (_pickedId > GearStash.NoId)
            {
                if (_sea.ScrapFromStash(_pickedId, out _) > 0L) { ClearPick(); Tap(); }
            }
            else if (_pickedSlot >= 0)
            {
                if (_sea.ScrapWorn(_pickedSlot) > 0L) { ClearPick(); Tap(); }
            }
            Refresh();
        }

        private void OnScrapAll()
        {
            if (_sea == null) return;
            if (_sea.ScrapAllStash(out _) > 0L) { ClearPick(); Tap(); }
            Refresh();
        }

        private static void Tap() => ServiceLocator.Get<HapticService>()?.Medium();

        // -------------------------------------------------------------- refresh
        private void Refresh()
        {
            if (_titleLabel != null) _titleLabel.text = Loc.T("depo.baslik");
            if (_gearTabLabel != null) _gearTabLabel.text = Loc.T("depo.donanim");
            if (_catalogueTabLabel != null) _catalogueTabLabel.text = Loc.T("depo.katalog");
            if (_gearTabArt != null)
                _gearTabArt.color = _showCatalogue ? new Color(0.62f, 0.67f, 0.75f, 1f) : Selected;
            if (_catalogueTabArt != null)
                _catalogueTabArt.color = _showCatalogue ? Selected : new Color(0.62f, 0.67f, 0.75f, 1f);

            if (_gearPage != null && _gearPage.gameObject.activeSelf == _showCatalogue)
                _gearPage.gameObject.SetActive(!_showCatalogue);
            if (_cataloguePage != null && _cataloguePage.gameObject.activeSelf != _showCatalogue)
                _cataloguePage.gameObject.SetActive(_showCatalogue);

            if (_showCatalogue) RefreshCatalogue();
            else RefreshGear();
        }

        private void RefreshGear()
        {
            if (_sea == null) return;
            SeaCombat.Tuning t = _sea.Combat;

            _powerLabel.text = Loc.T("deniz.guc") + "  " + N(_sea.ShipPower());

            for (int slot = 0; slot < SeaCombat.SlotCount; slot++)
            {
                SeaCombat.Item worn = _sea.GearItem(slot);
                bool has = worn.Grade >= 0;
                _wornSlot[slot].text = Loc.T("deniz.slot." + slot);
                _wornSlot[slot].color = InkSoft;
                _wornGrade[slot].text = has ? Loc.T("kaptan.derece." + worn.Grade) : Loc.T("deniz.bos");
                _wornGrade[slot].color = has ? GradeTint[Mathf.Clamp(worn.Grade, 0, GradeTint.Length - 1)]
                                             : InkFaint;
                _wornScore[slot].text = has ? SeaCombat.ItemScore(worn, t).ToString() : "—";
                _wornScore[slot].color = has ? Ink : InkFaint;
                _wornArt[slot].color = _pickedSlot == slot ? Selected : (has ? Color.white : Empty);
            }

            int count = _sea.StashCount;
            int capacity = _sea.StashCapacity;
            _shelfLabel.text = string.Format(Loc.T("depo.raf"), count, capacity);
            _shelfLabel.color = _sea.StashHasRoom ? Ink : Warn;

            long scrapAll = _sea.ScrapAllValue(out long xpAll);
            _scrapAllLabel.text = string.Format(Loc.T("depo.parcala"), scrapAll, xpAll);
            _scrapAllBtn.interactable = count > 0;

            // A cell is drawn while it is inside the depo — which is the tuned capacity, or the
            // count when the shelf is over-full. Cells past that are hidden rather than drawn empty.
            int shown = capacity < _cells ? capacity : _cells;
            if (count > shown) shown = count < _cells ? count : _cells;
            for (int i = 0; i < _cells; i++)
            {
                bool draw = i < shown;
                if (_cellGo[i] == null) continue;
                if (_cellGo[i].activeSelf != draw) _cellGo[i].SetActive(draw);
                if (!draw) continue;

                SeaCombat.Item item = _sea.StashItemAt(i);
                if (item.Grade < 0)
                {
                    _cellSlot[i].text = string.Empty;
                    _cellScore[i].text = string.Empty;
                    _cellArrow[i].text = string.Empty;
                    _cellArt[i].color = Empty;
                    continue;
                }

                _cellSlot[i].text = Loc.T("deniz.slot." + item.Slot);
                _cellSlot[i].color = GradeTint[Mathf.Clamp(item.Grade, 0, GradeTint.Length - 1)];
                _cellScore[i].text = SeaCombat.ItemScore(item, t).ToString();
                _cellScore[i].color = Ink;
                _cellArrow[i].text = GearStash.IsUpgrade(item, _sea.GearItem(item.Slot), t) ? "▲" : string.Empty;
                _cellArt[i].color = _sea.StashIdAt(i) == _pickedId && _pickedId > GearStash.NoId
                                  ? Selected : Color.white;
            }

            RefreshActionStrip();
        }

        /// <summary>
        /// The strip under the grid: two buttons when something is picked, a line of text when
        /// nothing is. The labels carry what the press pays, because a SÖK that does not say what
        /// it hands back is a button nobody presses twice.
        /// </summary>
        private void RefreshActionStrip()
        {
            bool picked = _pickedId > GearStash.NoId || _pickedSlot >= 0;
            if (_actionStrip.gameObject.activeSelf != picked) _actionStrip.gameObject.SetActive(picked);
            if (_hintLabel.gameObject.activeSelf == picked) _hintLabel.gameObject.SetActive(!picked);

            if (!picked)
            {
                _hintLabel.text = _sea.StashCount > 0 ? Loc.T("depo.sec") : Loc.T("depo.bos");
                return;
            }

            SeaCombat.Item item = _pickedId > GearStash.NoId ? Shelved(_pickedId) : _sea.GearItem(_pickedSlot);
            if (item.Grade < 0) { ClearPick(); RefreshActionStrip(); return; }

            int grade = Mathf.Clamp(item.Grade, 0, GradeTint.Length - 1);
            if (_pickedId > GearStash.NoId)
            {
                _primaryLabel.text = Loc.T("deniz.giydir") + "  ·  " + Loc.T("deniz.slot." + item.Slot);
                _primaryBtn.interactable = true;
            }
            else
            {
                _primaryLabel.text = Loc.T("depo.depoya");
                _primaryBtn.interactable = _sea.StashHasRoom;
            }
            _secondaryLabel.text = string.Format(Loc.T("atolye.sok"),
                                                 SeaCombat.ScrapFor(grade),
                                                 Crafting.SalvageXpFor(grade));
        }

        private SeaCombat.Item Shelved(long id)
        {
            int n = _sea.StashCount;
            for (int i = 0; i < n; i++)
                if (_sea.StashIdAt(i) == id) return _sea.StashItemAt(i);
            return new SeaCombat.Item { Grade = -1 };
        }

        /// <summary>
        /// The chain's rows. Nothing here can be pressed: a catalogue is a reference, and the way
        /// to unlock a row is to go and buy the island it names.
        /// </summary>
        private void RefreshCatalogue()
        {
            _oresTitle.text = Loc.T("depo.cevherler");
            _goodsTitle.text = Loc.T("depo.urunler");

            for (int i = 0; i < _owned.Length && i < Catalogue.OreCount; i++)
                _owned[i] = _market == null || _market.IsOwned(Catalogue.OreKeys[i]);

            for (int e = 0; e < Catalogue.EntryCount && e < _entryName.Length; e++)
            {
                if (_entryName[e] == null) continue;
                bool ore = Catalogue.IsOre(e);
                bool open = Catalogue.IsDiscovered(e, _owned);

                _entryName[e].text = NameOf(e);
                _entryName[e].color = open ? Ink : InkFaint;
                _entryStripe[e].color = open
                    ? OreInk(Catalogue.IslandOf(e))
                    : new Color(0.72f, 0.75f, 0.80f, 1f);

                if (!open)
                {
                    int missing = Catalogue.MissingIsland(e, _owned);
                    _entryState[e].text = missing >= 0
                        ? string.Format(Loc.T("depo.ada_gerek"), Loc.Id("ada", Catalogue.OreKeys[missing]))
                        : Loc.T("senlik.kilitli");
                    _entryState[e].color = InkFaint;
                    continue;
                }

                _entryState[e].text = ore
                    ? Loc.Id("ada", Catalogue.OreKeys[e])
                    : Inputs(e) + "   ·   " + string.Format(Loc.T("depo.saniye"), Sec(e));
                _entryState[e].color = InkSoft;
            }
        }

        /// <summary>
        /// An entry's own name: the ore table for an ore, the goods table for a product.
        ///
        /// The two lookups are deliberately different calls. <see cref="Loc.Id"/> answers from the
        /// ACTIVE language only and hands back the raw id when that cell is blank, which is fine for
        /// <c>cevher.*</c> and <c>ada.*</c> — old rows, long since filled in every column. The
        /// <c>urun.*</c> rows are new, so they go through <see cref="Loc.T"/>, which falls back to
        /// English: the worst a missing cell can do here is print GOLD BAR in German rather than
        /// <c>gold_bar</c> on the card.
        /// </summary>
        private static string NameOf(int entry)
            => Catalogue.IsOre(entry)
             ? Loc.Id("cevher", Catalogue.OreKeys[entry])
             : Loc.T("urun." + Catalogue.KeyOf(entry));

        /// <summary>What a product is made of, in the player's language.</summary>
        private static string Inputs(int entry)
        {
            int n = Catalogue.InputCount(entry);
            if (n <= 0) return string.Empty;
            string text = NameOf(Catalogue.InputAt(entry, 0));
            for (int i = 1; i < n; i++) text += " + " + NameOf(Catalogue.InputAt(entry, i));
            return text;
        }

        private static string Sec(int entry)
        {
            double s = Catalogue.SecondsOf(entry);
            return (System.Math.Round(s * 10d) / 10d).ToString("0.#");
        }

        /// <summary>The ore ladder's ink, so a row is recognisable as its island's colour. Held
        /// here rather than read off <c>WorldIslands</c> — a UI assembly cannot see the gameplay
        /// one, and these are the same eight brand colours the map draws.</summary>
        private static Color OreInk(int rung)
        {
            switch (rung)
            {
                case 0:  return new Color(0.35f, 0.38f, 0.44f, 1f);   // kömür
                case 1:  return new Color(0.85f, 0.51f, 0.28f, 1f);   // bakır
                case 2:  return new Color(0.55f, 0.60f, 0.68f, 1f);   // demir
                case 3:  return new Color(0.72f, 0.78f, 0.85f, 1f);   // gümüş
                case 4:  return new Color(0.95f, 0.75f, 0.22f, 1f);   // altın
                case 5:  return new Color(0.88f, 0.26f, 0.34f, 1f);   // yakut
                case 6:  return new Color(0.22f, 0.72f, 0.48f, 1f);   // zümrüt
                default: return new Color(0.42f, 0.78f, 0.94f, 1f);   // elmas
            }
        }

        // --------------------------------------------------------------- pieces
        private static RectTransform Zone(RectTransform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        private static RectTransform Art(RectTransform parent, string name, Sprite sprite,
                                         Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite != null ? sprite : UiSkin.Panel;
            img.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = false;
            img.color = Color.white;
            img.raycastTarget = false;
            return UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
        }

        private static Image Stripe(RectTransform parent, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject("Cizgi", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = UiSkin.Flat;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            UiBuild.Anchor((RectTransform)go.transform, aMin, aMax);
            return img;
        }

        /// <summary>Shrink-to-fit so a long translation stays on its row.</summary>
        private static void Fit(Text label, int min, int max)
        {
            if (label == null) return;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private static string N(double v)
        {
            if (v < 999.5d) return Mathf.RoundToInt((float)v).ToString();
            double k = v / 1000d;
            return k < 99.95d ? k.ToString("0.0") + "k" : Mathf.RoundToInt((float)k) + "k";
        }
    }
}
