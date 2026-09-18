using System;
using System.Globalization;
using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The odds sheet, shared by the master chest, the captain crate and the card pack. Both stores require the
    /// chance of a paid randomised pull to be readable BEFORE the purchase, and the master chest is
    /// paid for in gems, which are sold for money — so the ⓘ that opens this is not decoration.
    ///
    /// Every number printed here comes out of <see cref="Odds"/>, which derives it from the same
    /// tuning struct the roll reads. Nothing on this screen is typed in by hand, which is the only
    /// way an odds sheet stays true across a balance pass.
    ///
    /// A PLAIN CLASS, not a MonoBehaviour, and one per roster screen — the same shape as
    /// <see cref="RosterInspectPanel"/>, for the same reason: it is a layer inside a screen that is
    /// already built in code, and giving it its own component would mean giving it its own canvas.
    ///
    /// ONE LAYOUT, THREE COATS. The rows, the note and the button are the same on every screen; the
    /// frame, ribbon, close and button art come from the screen that opened it (<see cref="Skin"/>),
    /// so the sheet reads as part of that screen rather than as a system dialog over it.
    ///
    /// SIZED TO WHAT IT SHOWS. The sheet is laid out in canvas units on every open and its height is
    /// the sum of its rows — a three-row card pack and a six-row master chest both come out full, with
    /// no empty band where the other crate's rows would have gone.
    /// </summary>
    public sealed class OddsSheetUI
    {
        /// <summary>
        /// A screen's coat for the sheet. Every position and size is in the SHEET ART'S OWN PIXELS,
        /// measured on the sprite, so the layout can convert it at whatever scale the sheet is drawn and
        /// the ribbon and close stay pinned to the same spot on the frame.
        /// </summary>
        public struct Skin
        {
            public Sprite Sheet;
            /// <summary>
            /// The sheet's pixelsPerUnitMultiplier. Zero draws the art at exactly the sheet's width, for a
            /// frame whose top or bottom border carries an ornament — the league board's crest, the masters
            /// panel's tab — that a nine-slice would otherwise stretch sideways.
            /// </summary>
            public float SheetScale;
            /// <summary>The inner (white) area's edges, in sheet pixels: left, top, right, bottom.</summary>
            public Vector4 Inner;
            /// <summary>Where the frame turns opaque, from its top. The sheet is centred on what shows.</summary>
            public float OpaqueTop;

            public Sprite Ribbon;
            /// <summary>A capsule ribbon, nine-sliced with <see cref="PillFit"/>, rather than a picture.</summary>
            public bool RibbonSliced;
            /// <summary>The ribbon's width, as a share of the sheet's.</summary>
            public float RibbonWidth;
            /// <summary>A sliced ribbon's height in sheet pixels. A picture ribbon keeps its own aspect.</summary>
            public float RibbonHeight;
            /// <summary>Where the ribbon's flat band sits on the frame, in sheet pixels down from its top.</summary>
            public float RibbonCentre;
            /// <summary>The flat band's middle on the ribbon art, 0 at its foot, 1 at its head.</summary>
            public float RibbonBand;
            /// <summary>The empty share of the ribbon art under its tails; content starts where they end.</summary>
            public float RibbonFoot;
            public Vector2 TitleMin, TitleMax;

            public Sprite Close;
            public float CloseSize;
            /// <summary>The close's centre in sheet pixels: x in from the RIGHT edge, y down from the top.</summary>
            public Vector2 CloseCentre;

            public Sprite Button;
            public Color ButtonInk;
            public Vector2 ButtonTextMin, ButtonTextMax;

            /// <summary>
            /// The masters screen: <c>UstaPanel/panel_ana</c> with its blue bar across the top (measured
            /// 94–275 px), the <c>serit_mavi</c> ribbon laid on that bar, the kit's square close at the
            /// bar's right end, and the blue capsule. Drawn at the sheet's width, because the bottom tab
            /// rides the bottom border's middle slice.
            /// </summary>
            public static Skin Masters(Sprite sheet, Sprite ribbon, Sprite button, Sprite close) => new Skin
            {
                Sheet = sheet, SheetScale = 0f, Inner = new Vector4(60f, 275f, 60f, 125f), OpaqueTop = 94f,
                Ribbon = ribbon, RibbonWidth = 0.60f, RibbonCentre = 184f, RibbonBand = 0.677f,
                // Legacy Text draws its capitals high in the line box — the masters header's own correction.
                TitleMin = new Vector2(0.20f, 0.677f - 0.30f), TitleMax = new Vector2(0.80f, 0.677f + 0.06f),
                Close = close, CloseSize = 118f, CloseCentre = new Vector2(135f, 184f),
                Button = button, ButtonInk = Paper,
                ButtonTextMin = new Vector2(0.14f, 0.14f), ButtonTextMax = new Vector2(0.86f, 0.86f),
            };

            /// <summary>
            /// The captain screen: <c>captain_card_panel</c> at the screen's own border scale of 2, its gold-
            /// clasped ribbon straddling the frame's top edge (78 px), and the kit's close over the corner.
            /// </summary>
            public static Skin Captains(Sprite sheet, Sprite ribbon, Sprite button, Sprite close) => new Skin
            {
                Sheet = sheet, SheetScale = 2f, Inner = new Vector4(104f, 130f, 103f, 144f), OpaqueTop = 78f,
                Ribbon = ribbon, RibbonWidth = 0.70f, RibbonCentre = 84f, RibbonBand = 0.56f, RibbonFoot = 0.19f,
                TitleMin = new Vector2(0.20f, 0.56f - 0.18f), TitleMax = new Vector2(0.80f, 0.56f + 0.18f),
                Close = close, CloseSize = 140f, CloseCentre = new Vector2(84f, 108f),
                Button = button, ButtonInk = Paper,
                ButtonTextMin = new Vector2(0.14f, 0.16f), ButtonTextMax = new Vector2(0.86f, 0.84f),
            };

            /// <summary>
            /// The card collection: the league board and ribbon, the round close and the orange capsule —
            /// the kit the events family is built from, and the collection's own blue, gold and orange.
            /// Loaded from the kits' atlases, because the collection screen wires no sheet art of its own.
            /// The ribbon sits under the crest where <see cref="EtkinlikKit.Header"/> puts it.
            /// </summary>
            public static Skin CardPack() => new Skin
            {
                Sheet = LigKit.Board, SheetScale = 0f, Inner = new Vector4(100f, 287f, 100f, 90f), OpaqueTop = 3f,
                Ribbon = LigKit.Get("serit"), RibbonSliced = true, RibbonWidth = 0.70f, RibbonHeight = 170f,
                RibbonCentre = 240f, RibbonBand = 0.5f,
                TitleMin = new Vector2(0.25f, 0.18f), TitleMax = new Vector2(0.75f, 0.82f),
                Close = EkranKit.Get("kapat"), CloseSize = 123f, CloseCentre = new Vector2(81f, 70f),
                Button = EkranKit.Get("btn_turuncu"), ButtonInk = EkranKit.Ink,
                ButtonTextMin = EkranKit.InlayMin, ButtonTextMax = EkranKit.InlayMax,
            };
        }

        /// <summary>
        /// Rows in the pool, derived from the widest table either caller can ask for rather than
        /// pinned at a number. The master chest states three counts plus one line per rarity, and the
        /// captain crate one line per grade.
        ///
        /// It was a hardcoded 5, which was right until the master chest grew a rarity row: Row() drops
        /// anything past the pool SILENTLY, so the Legendary line — the one figure players actually
        /// open this sheet for — simply never drew. A cap that can be outgrown without saying so is
        /// worse than one that cannot be outgrown at all.
        /// </summary>
        private static readonly int MaxRows =
            Mathf.Max(Captains.GradeCount, MasterChestFixedRows + Foremen.RarityCount);

        /// <summary>Cards per chest, directed, rolled — the three counts above the rarity table.</summary>
        private const int MasterChestFixedRows = 3;

        private static readonly Color Ink = new Color(0.09f, 0.14f, 0.24f, 1f);
        private static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);
        private static readonly Color Paper = new Color(0.96f, 0.97f, 1f, 1f);
        /// <summary>The note's card: the row card washed a shade bluer, so it reads as an aside.</summary>
        private static readonly Color NoteWash = new Color(0.89f, 0.93f, 1f, 1f);

        // Canvas units (1080×1920 reference). The rows and note are the same size on every coat.
        private const float MaxSheetWidth = 960f;
        private const float SideMargin = 36f, EndMargin = 48f;
        private const float Pad = 26f;
        private const float RowHeight = 92f, RowGap = 12f, GroupGap = 30f, SectionGap = 26f;
        private const float MarkWidth = 48f, MarkHeight = 46f;
        /// <summary>The value chip's inset from the row's right edge, clear of the card's rim.</summary>
        private const float ChipInset = 20f;
        private const float ChipWidth = 196f, ChipHeight = 62f;
        private const float NotePadX = 34f, NotePadY = 26f, NoteMin = 84f;
        private const float ButtonHeight = 108f, ButtonWidth = 0.46f;
        /// <summary>The row card's border drawn at 1/2.2 — at full size its rim would eat a 92-unit row, and at
        /// 1/1.6 it read as a second frame inside the sheet's own.</summary>
        private const float RowCardScale = 2.2f;

        /// <summary>
        /// The game's decimal separator, not the handset's — the same rule the roster screens follow.
        /// A Turkish phone would otherwise draw "10,5%" here while the card beside it draws "10.5%".
        /// </summary>
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        private readonly Skin _skin;
        private readonly RectTransform _overlay;
        private readonly RectTransform _sheet;
        private readonly Image _sheetImage;
        private readonly RectTransform _ribbon;
        private readonly Text _title;
        private readonly RectTransform _close;
        private readonly RectTransform[] _row = new RectTransform[MaxRows];
        private readonly Image[] _rowMark = new Image[MaxRows];
        private readonly Text[] _rowLabel = new Text[MaxRows];
        private readonly Text[] _rowValue = new Text[MaxRows];
        private readonly bool[] _rowGroupStart = new bool[MaxRows];
        private readonly RectTransform _noteCard;
        private readonly Text _note;
        private readonly RectTransform _button;
        private readonly Text _buttonLabel;
        private int _used;
        private bool _nextStartsGroup;

        public OddsSheetUI(RectTransform parent) : this(parent, Skin.CardPack()) { }

        public OddsSheetUI(RectTransform parent, Skin skin)
        {
            _skin = skin;
            _overlay = UiBuild.Flat(parent, "OranKarartma", new Color(0.02f, 0.03f, 0.06f, 0.88f),
                                    Vector2.zero, Vector2.one);
            var dismiss = _overlay.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(Hide);

            _sheetImage = Sliced(_overlay, "OranSayfasi", skin.Sheet, false);
            if (skin.Sheet == null) _sheetImage.color = Paper;
            _sheetImage.raycastTarget = true;
            _sheet = _sheetImage.rectTransform;
            // Stops a tap inside the sheet from reaching the dismiss layer under it.
            var blocker = _sheet.gameObject.AddComponent<Button>();
            blocker.transition = Selectable.Transition.None;

            // The ribbon is a picture unless the coat says it is a capsule: a picture keeps its own
            // aspect because Layout sizes its box to it, so preserveAspect is only a guard.
            Image ribbon = skin.RibbonSliced ? Sliced(_sheet, "Serit", skin.Ribbon, true)
                                             : Picture(_sheet, "Serit", skin.Ribbon);
            _ribbon = ribbon.rectTransform;
            _title = Label(_ribbon, "Baslik", 40, TextAnchor.MiddleCenter, skin.TitleMin, skin.TitleMax);
            _title.color = skin.Ribbon != null ? Paper : Ink;
            Fit(_title, 20, 40);

            Sprite rowCard = AtolyeKit.Get("gorev_kart");
            Sprite mark = EkranKit.Get("btn_bos");
            Sprite chip = AtolyeKit.Get("hap_cip");
            for (int i = 0; i < MaxRows; i++)
            {
                Image card = Sliced(_sheet, "Satir" + i, rowCard, false);
                card.pixelsPerUnitMultiplier = RowCardScale;
                _row[i] = card.rectTransform;

                // A gem of the rarity's own colour at the row's head. The kit's pale capsule is the one
                // piece light enough to take a tint and keep its rim, and PillFit keeps it round.
                _rowMark[i] = Sliced(_row[i], "Isaret", mark, true);
                PinLeft(_rowMark[i].rectTransform, Pad - 4f, MarkWidth, MarkHeight);

                _rowLabel[i] = Label(_row[i], "Ad", 30, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
                Fit(_rowLabel[i], 16, 30);

                Image well = Sliced(_row[i], "Deger", chip, true);
                if (chip == null) well.color = Ink;
                PinRight(well.rectTransform, ChipInset, ChipWidth, ChipHeight);
                _rowValue[i] = Label(well.rectTransform, "Yazi", 30, TextAnchor.MiddleCenter,
                                     new Vector2(0.14f, 0.10f), new Vector2(0.86f, 0.90f));
                _rowValue[i].color = Paper;
                Fit(_rowValue[i], 16, 30);
            }

            Image noteCard = Sliced(_sheet, "NotKarti", rowCard, false);
            noteCard.pixelsPerUnitMultiplier = RowCardScale;
            noteCard.color = rowCard != null ? NoteWash : new Color(0.90f, 0.93f, 0.98f, 1f);
            _noteCard = noteCard.rectTransform;
            _note = Label(_noteCard, "Not", 24, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            _note.color = InkSoft;
            // Not best fit: Layout measures the note and grows its card to it, so it never has to shrink.
            float scale = TextScale();
            _note.fontSize = Mathf.RoundToInt(24 * scale);
            _note.horizontalOverflow = HorizontalWrapMode.Wrap;
            _note.verticalOverflow = VerticalWrapMode.Overflow;
            _note.resizeTextForBestFit = false;
            _note.lineSpacing = 1.1f;
            var noteRect = (RectTransform)_note.transform.parent;
            noteRect.offsetMin = new Vector2(NotePadX, NotePadY);
            noteRect.offsetMax = new Vector2(-NotePadX, -NotePadY);

            Image face = Sliced(_sheet, "Kapat", skin.Button, true);
            if (skin.Button == null) face.color = new Color(0.45f, 0.49f, 0.56f, 1f);
            face.raycastTarget = true;
            var close = face.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.targetGraphic = face;
            close.onClick.AddListener(Hide);
            _button = face.rectTransform;
            _buttonLabel = Label(_button, "Yazi", 32, TextAnchor.MiddleCenter, skin.ButtonTextMin, skin.ButtonTextMax);
            _buttonLabel.color = skin.Button != null ? skin.ButtonInk : Paper;
            Fit(_buttonLabel, 16, 32);

            // Drawn last, over the ribbon's tail and the frame's corner.
            Image cross = Picture(_sheet, "Carpi", skin.Close);
            if (skin.Close == null)
            {
                cross.sprite = UiSkin.Flat;
                cross.enabled = true;
                cross.color = new Color(0.80f, 0.24f, 0.24f, 1f);
            }
            cross.raycastTarget = true;
            var shut = cross.gameObject.AddComponent<Button>();
            shut.transition = Selectable.Transition.None;
            shut.targetGraphic = cross;
            shut.onClick.AddListener(Hide);
            _close = cross.rectTransform;

            _overlay.gameObject.SetActive(false);
        }

        public bool Visible => _overlay != null && _overlay.gameObject.activeSelf;

        // ------------------------------------------------------------------ master chest
        /// <summary>
        /// What a master chest actually does. The directed card is listed apart from the rolled ones
        /// and never folded into a percentage: it is not chance, it always goes to whoever is furthest
        /// behind, and presenting it as a probability would overstate the randomness by a third.
        /// </summary>
        public void ShowMasterChest(in MasterChest.Tuning tuning, Func<int, Color> tint = null)
        {
            Begin();
            int rolled = Odds.MasterRolledCards(tuning);
            Row(Loc.T("oran.usta.kart"), MasterChest.CardsFor(1, tuning).ToString(Culture), tint, -1);
            Row(Loc.T("oran.usta.yonlendirilen"), MasterChest.DirectedIn(tuning).ToString(Culture), tint, -1);
            Row(Loc.T("oran.usta.rastgele"), rolled.ToString(Culture), tint, -1);
            // One row per rarity rather than one flat "any master" figure: rarity is drawn now, so a
            // single number would hide the only odds anybody actually wants to know. Its own group, so
            // the counts and the chances do not read as one list.
            _nextStartsGroup = true;
            for (int rank = 0; rank < Foremen.RarityCount; rank++)
                Row(Loc.T("usta.nadirlik." + rank.ToString(Culture)),
                    Percent(Odds.MasterCardChance((Foremen.Rarity)rank, tuning)), tint, rank);

            _note.text = Loc.T("oran.usta.not") + "\n" + Loc.T("oran.not");
            Finish();
        }

        // ------------------------------------------------------------------ captain crate
        /// <summary>
        /// The crate's base table plus its three guarantees in words. Base means "with nothing owed" —
        /// the pity rules bend the numbers, so folding them in would print a percentage that is true
        /// of no particular pull.
        /// </summary>
        public void ShowCaptainCrate(in CaptainCrate.Tuning tuning, Func<int, Color> tint = null)
        {
            Begin();
            for (int grade = 0; grade < Captains.GradeCount; grade++)
            {
                double chance = Odds.CaptainGradeChance(grade, tuning);
                // A grade nobody in the roster carries cannot be rolled, so it is not listed. Printing
                // it at 0% would read as a rate we are hiding rather than a rank that does not exist.
                if (chance <= 0d) continue;
                Row(Loc.T("kaptan.derece." + grade.ToString(Culture)), Percent(chance), tint, grade);
            }

            string note = string.Empty;
            if (tuning.EpicPity > 0)
                note += string.Format(Culture, Loc.T("oran.kaptan.garanti"),
                                      Loc.T("kaptan.derece.2"), tuning.EpicPity) + "\n";
            if (tuning.LegendaryPity > 0)
                note += string.Format(Culture, Loc.T("oran.kaptan.garanti"),
                                      Loc.T("kaptan.derece.3"), tuning.LegendaryPity) + "\n";
            if (tuning.SoftPityStart > 0 && tuning.SoftPityStep > 0d)
                note += string.Format(Culture, Loc.T("oran.kaptan.yumusak"),
                                      Loc.T("kaptan.derece.3"), tuning.SoftPityStart,
                                      (tuning.SoftPityStep * 100d).ToString("0.##", Culture)) + "\n";
            _note.text = note + Loc.T("oran.not");
            Finish();
        }

        // ------------------------------------------------------------------ card pack
        /// <summary>
        /// The collection pack's base table, one row per rarity the catalogue actually carries, plus
        /// its guarantees in words — the captain crate's grammar, because the two are the same shape.
        /// <paramref name="census"/> is what drops Mythic: no card carries it at launch, and a 0% row
        /// reads as a hidden rate rather than a rank that does not exist yet (Docs/PLAN_14).
        /// </summary>
        public void ShowCardPack(in CardCollectionPack.Tuning tuning, int[] census, Func<int, Color> tint = null)
        {
            Begin();
            for (int rarity = 0; rarity < CardCollection.RarityCount; rarity++)
            {
                double chance = CardCollectionPack.ChanceOf((RosterCardState.Rarity)rarity, census, tuning);
                if (chance <= 0d) continue;
                Row(Loc.T("kaptan.derece." + rarity.ToString(Culture)), Percent(chance), tint, rarity);
            }

            string note = string.Empty;
            if (tuning.EpicPity > 0)
                note += string.Format(Culture, Loc.T("oran.kaptan.garanti"),
                                      Loc.T("kaptan.derece.2"), tuning.EpicPity) + "\n";
            if (tuning.LegendaryPity > 0)
                note += string.Format(Culture, Loc.T("oran.kaptan.garanti"),
                                      Loc.T("kaptan.derece.3"), tuning.LegendaryPity) + "\n";
            if (tuning.SoftPityStart > 0 && tuning.SoftPityStep > 0d)
                note += string.Format(Culture, Loc.T("oran.kaptan.yumusak"),
                                      Loc.T("kaptan.derece.3"), tuning.SoftPityStart,
                                      (tuning.SoftPityStep * 100d).ToString("0.##", Culture)) + "\n";
            _note.text = note + Loc.T("koleksiyon.oran.not") + "\n" + Loc.T("oran.not");
            Finish();
        }

        public void Hide()
        {
            if (_overlay != null) _overlay.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ plumbing
        private void Begin()
        {
            _used = 0;
            _nextStartsGroup = false;
            // One line, shrinking to fit: the league ribbon is a capsule, and a wrapped title spills onto its clasps.
            _title.text = EtkinlikKit.OneLine(Loc.T("oran.baslik"));
            _buttonLabel.text = EtkinlikKit.OneLine(Loc.T("lig.kapat"));
        }

        /// <summary>
        /// One line. <paramref name="rank"/> is the rarity it states, or -1 for a count; a rarity line
        /// carries a gem of that rarity's colour, when the caller can say what the colour is.
        /// </summary>
        private void Row(string label, string value, Func<int, Color> tint, int rank)
        {
            if (_used >= MaxRows) return;
            int i = _used;
            _rowLabel[i].text = label;
            _rowValue[i].text = value;
            _rowGroupStart[i] = _nextStartsGroup && i > 0;
            _nextStartsGroup = false;

            bool marked = rank >= 0 && tint != null;
            _rowMark[i].gameObject.SetActive(marked);
            if (marked) _rowMark[i].color = tint(rank);
            // The label starts after the gem when there is one, and at the gem's place when there is not,
            // so each group lines up with itself.
            var slot = (RectTransform)_rowLabel[i].transform.parent;
            slot.offsetMin = new Vector2(marked ? Pad + MarkWidth + 14f : Pad, 0f);
            slot.offsetMax = new Vector2(-(ChipInset + ChipWidth + 16f), 0f);

            _row[i].gameObject.SetActive(true);
            _used++;
        }

        private void Finish()
        {
            for (int i = _used; i < MaxRows; i++) _row[i].gameObject.SetActive(false);
            _overlay.gameObject.SetActive(true);
            _overlay.SetAsLastSibling();
            Layout();
        }

        /// <summary>
        /// Places everything in canvas units, top down, and sizes the sheet to what it holds. Run on
        /// every open: the row count differs by crate, and the canvas is narrower on a tall phone.
        /// </summary>
        private void Layout()
        {
            Rect area = _overlay.rect;
            float areaWidth = area.width > 1f ? area.width : 1080f;
            float areaHeight = area.height > 1f ? area.height : 1920f;
            float width = Mathf.Min(MaxSheetWidth, areaWidth - 2f * SideMargin);

            // Units per sheet pixel. Image.pixelsPerUnit is the sprite's own over the canvas reference
            // of 100, and the multiplier divides the border on top of that.
            Sprite art = _skin.Sheet;
            float spritePpu = art != null ? art.pixelsPerUnit : 100f;
            float multiplier = _skin.SheetScale > 0f ? _skin.SheetScale
                             : art != null ? art.rect.width * 100f / (spritePpu * width) : 1f;
            float unit = 100f / (spritePpu * multiplier);
            _sheetImage.pixelsPerUnitMultiplier = multiplier;

            float left = _skin.Inner.x * unit + Pad;
            float right = _skin.Inner.z * unit + Pad;
            float inner = width - left - right;

            // The ribbon: its flat band on the frame line the coat names, its box sized to the art.
            float ribbonWidth = width * _skin.RibbonWidth;
            float ribbonHeight = _skin.RibbonSliced || _skin.Ribbon == null
                ? _skin.RibbonHeight * unit
                : ribbonWidth * _skin.Ribbon.rect.height / _skin.Ribbon.rect.width;
            float ribbonTop = _skin.RibbonCentre * unit - (1f - _skin.RibbonBand) * ribbonHeight;
            Place(_ribbon, (width - ribbonWidth) * 0.5f, ribbonTop, ribbonWidth, ribbonHeight);
            float ribbonFoot = ribbonTop + ribbonHeight * (1f - _skin.RibbonFoot);

            float y = Mathf.Max(_skin.Inner.y * unit, ribbonFoot) + Pad;
            for (int i = 0; i < _used; i++)
            {
                if (_rowGroupStart[i]) y += GroupGap - RowGap;
                Place(_row[i], left, y, inner, RowHeight);
                y += RowHeight + RowGap;
            }
            y += SectionGap - RowGap;

            float noteHeight = Mathf.Max(NoteMin, NoteTextHeight(inner - 2f * NotePadX) + 2f * NotePadY);
            Place(_noteCard, left, y, inner, noteHeight);
            y += noteHeight + SectionGap;

            float buttonWidth = width * ButtonWidth;
            Place(_button, (width - buttonWidth) * 0.5f, y, buttonWidth, ButtonHeight);
            y += ButtonHeight;

            float height = y + Pad + _skin.Inner.w * unit;
            float closeSize = _skin.CloseSize * unit;
            Place(_close, width - _skin.CloseCentre.x * unit - closeSize * 0.5f,
                  _skin.CloseCentre.y * unit - closeSize * 0.5f, closeSize, closeSize);

            // Centred on what shows, which starts at the ribbon or the frame, whichever is higher; shrunk
            // as one piece if a short screen cannot hold it, never squeezed.
            float shown = Mathf.Min(_skin.OpaqueTop * unit, ribbonTop);
            float scale = Mathf.Min(1f, (areaHeight - 2f * EndMargin) / (height - shown));
            _sheet.anchorMin = _sheet.anchorMax = new Vector2(0.5f, 0.5f);
            _sheet.pivot = new Vector2(0.5f, 1f);
            _sheet.sizeDelta = new Vector2(width, height);
            _sheet.localScale = new Vector3(scale, scale, 1f);
            _sheet.anchoredPosition = new Vector2(0f, scale * (height + shown) * 0.5f);
        }

        /// <summary>The note's wrapped height at <paramref name="width"/> — what Text.preferredHeight
        /// would say if the box were already that wide.</summary>
        private float NoteTextHeight(float width)
        {
            TextGenerationSettings settings = _note.GetGenerationSettings(new Vector2(Mathf.Max(1f, width), 0f));
            float pixels = _note.cachedTextGeneratorForLayout.GetPreferredHeight(_note.text, settings);
            float perUnit = _note.pixelsPerUnit > 0f ? _note.pixelsPerUnit : 1f;
            return pixels / perUnit;
        }

        private static string Percent(double chance)
            => string.Format(Culture, "{0:0.##}%", chance * 100d);

        /// <summary>Top-left placement in the sheet, in canvas units down from its top edge.</summary>
        private static void Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        private static void PinLeft(RectTransform rt, float inset, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(inset, 0f);
        }

        private static void PinRight(RectTransform rt, float inset, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(-inset, 0f);
        }

        /// <summary>A nine-sliced piece; <paramref name="capsule"/> keeps its round caps with PillFit.</summary>
        private static Image Sliced(RectTransform parent, string name, Sprite sprite, bool capsule)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite != null ? sprite : UiSkin.Flat;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            if (capsule && sprite != null) PillFit.Wrap(image);
            return image;
        }

        /// <summary>A picture drawn whole at its own aspect — a ribbon, a close.</summary>
        private static Image Picture(RectTransform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.enabled = sprite != null;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private static Text Label(RectTransform parent, string name, int size, TextAnchor anchor,
                                  Vector2 min, Vector2 max)
        {
            Text label = UiBuild.Label(Slot(parent, name, min, max), "Text", string.Empty, size, anchor);
            label.color = Ink;
            Fit(label, Mathf.Max(11, size / 2), size);
            return label;
        }

        private static float TextScale()
        {
            AccessibilityConfig accessibility = ServiceLocator.Get<AccessibilityConfig>();
            return accessibility != null ? accessibility.TextScale : 1f;
        }

        private static void Fit(Text label, int min, int max)
        {
            float scale = TextScale();
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(1, Mathf.RoundToInt(min * scale));
            label.resizeTextMaxSize = Mathf.Max(label.resizeTextMinSize, Mathf.RoundToInt(max * scale));
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static RectTransform Slot(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, min, max);
        }
    }
}
