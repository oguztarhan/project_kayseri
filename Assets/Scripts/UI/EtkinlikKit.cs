using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// The pieces every screen that opens from the events board is built from — the harbor and foundry
    /// festivals, the production sprint, the industry pass and their reward card — so the five read as
    /// one family with <see cref="LiveEventsUI"/> itself.
    ///
    /// NOTHING NEW IS PACKED HERE. The sheet, ribbon, green capsule, chests, gem and card icon come
    /// from <see cref="LigKit"/>; the navy card, orange and pale capsules, chest, lock and close disc
    /// from <see cref="EkranKit"/>; the tabs, bars and chip from <see cref="AtolyeKit"/>; the chart
    /// icon from <see cref="SeaKit"/>. This file only decides how they sit together.
    ///
    /// THE ART IS PRE-COLOURED. A button or tab says what it is by swapping its face, never by tint,
    /// and every builder turns the Selectable transition off so Unity's disabled ColorTint cannot wash
    /// the swapped face as well.
    ///
    /// Every getter can come back null (no atlas, no such piece); the builders then fall back to the
    /// flat quad, which is ugly but never a missing screen.
    /// </summary>
    public static class EtkinlikKit
    {
        /// <summary>
        /// The sheet's box on the scrim. Across 0.03–0.97 for the reason <see cref="LadderUI"/> records:
        /// that width keeps the crest in the sheet's top-centre slice unstretched.
        /// </summary>
        public static readonly Vector2 SheetMin = new Vector2(0.030f, 0.050f);
        public static readonly Vector2 SheetMax = new Vector2(0.970f, 0.870f);

        /// <summary>Second-rank ink on the white sheet — clocks, captions, the empty line.</summary>
        public static readonly Color InkSoft = new Color(0.36f, 0.42f, 0.52f, 1f);

        /// <summary>A row whose reward is gone or out of reach sits its icon back this far.</summary>
        public static readonly Color Faded = new Color(1f, 1f, 1f, 0.45f);

        /// <summary>
        /// A label's band on the green and pale capsules: clear of the round caps, and short enough
        /// that with <see cref="Fit"/>'s Truncate a second line only fits once the whole word already
        /// fits on one — so a long word shrinks onto its line rather than breaking over the rim.
        /// </summary>
        private static readonly Vector2 CapsBandMin = new Vector2(0.18f, 0.24f);
        private static readonly Vector2 CapsBandMax = new Vector2(0.82f, 0.76f);

        /// <summary>What a capsule button is saying: take this, do this, or not now.</summary>
        public enum Face { Claim, Primary, Dead }

        public static Sprite Gem => LigKit.Get("elmas");
        public static Sprite MasterCard => LigKit.Get("usta_kart");
        public static Sprite Chart => SeaKit.Get("harita");

        /// <summary>The league's four chests, plainest first — a ladder of rewards reads richer as it climbs.</summary>
        public static Sprite Chest(int rank)
        {
            switch (rank)
            {
                case 0: return LigKit.Get("sandik_sade");
                case 1: return LigKit.Get("sandik_bronz");
                case 2: return LigKit.Get("sandik_gumus");
                default: return LigKit.Get("sandik_altin");
            }
        }

        // ------------------------------------------------------------------ frame
        /// <summary>The league sheet as <c>Zemin</c>, eating its own taps so the scrim's dismiss cannot fire through it.</summary>
        public static Image Sheet(RectTransform root)
        {
            Image sheet = EkranKit.Sliced(root, "Zemin", LigKit.Board, SheetMin, SheetMax, false);
            sheet.raycastTarget = true;
            var eat = sheet.gameObject.AddComponent<Button>();
            eat.transition = Selectable.Transition.None;
            return sheet;
        }

        /// <summary>
        /// The ribbon across the rail under the crest and the close disc on the sheet's corner, at the
        /// league board's offsets from its top edge — the header the contract, mining gear and events
        /// screens wear.
        /// </summary>
        public static Text Header(RectTransform root, string title, UnityAction hide)
        {
            Image band = EkranKit.Sliced(root, "Serit", LigKit.Get("serit"),
                                         new Vector2(0.215f, 0.698f), new Vector2(0.785f, 0.790f), true);
            // 0.25–0.75, not the league's 0.20–0.80: the ribbon's clasps ride its caps, PillFit scales the
            // caps with the ribbon's height, and on a tall phone that height is a bigger share of a
            // narrower canvas — the wider band put "LỄ HỘI BẾN CẢNG" on the clasps at 1080×2340.
            Text label = Label(band.rectTransform, "Yazi", new Vector2(0.25f, 0.18f), new Vector2(0.75f, 0.82f),
                               title, 36, TextAnchor.MiddleCenter, EkranKit.Paper, 18);
            EkranKit.Close(root, new Vector2(0.838f, 0.789f), new Vector2(0.952f, 0.877f), hide);
            return label;
        }

        /// <summary>The line and icon that stand in for a module with nothing scheduled.</summary>
        public static RectTransform Empty(RectTransform sheet, string text, out Text label)
        {
            RectTransform empty = Slot(sheet, "Bos", new Vector2(0.10f, 0.30f), new Vector2(0.90f, 0.62f));
            EkranKit.Icon(empty, "Simge", EkranKit.Get("etkinlik_ikon"), new Vector2(0.30f, 0.40f), new Vector2(0.70f, 1f));
            label = Label(empty, "Yazi", Vector2.zero, new Vector2(1f, 0.34f), text, 32, TextAnchor.MiddleCenter, InkSoft, 16);
            return empty;
        }

        // ------------------------------------------------------------------ text
        public static RectTransform Slot(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return UiBuild.Anchor((RectTransform)go.transform, min, max);
        }

        /// <summary>A label in its own band, shrinking to fit — the band is <paramref name="name"/>, the Text its child.</summary>
        public static Text Label(RectTransform parent, string name, Vector2 min, Vector2 max, string text,
                                 int size, TextAnchor anchor, Color color, int minSize)
        {
            Text label = UiBuild.Label(Slot(parent, name, min, max), "Text", text, size, anchor);
            label.color = color;
            Fit(label, minSize, size);
            return label;
        }

        /// <summary>
        /// Shrink-to-fit. TRUNCATE, NOT <see cref="UiBuild.Label"/>'s OVERFLOW: best fit only shrinks
        /// against a rect the text may not spill out of vertically, and left on Overflow it keeps the
        /// largest size, wraps, and draws its second line over whatever is under the band.
        /// </summary>
        public static void Fit(Text label, int min, int max)
        {
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // ---------------------------------------------------------------- controls
        /// <summary>A tab on the workshop's pale capsule; <see cref="SetTab"/> puts the chosen one on gold.</summary>
        public static Button Tab(RectTransform parent, string name, Vector2 min, Vector2 max, UnityAction onClick,
                                 out Text label)
        {
            Button tab = EkranKit.Capsule(parent, name, AtolyeKit.Get("sekme"), min, max, onClick);
            // 0.22–0.78 across: the selected tab carries a gold stud in each cap, and once OneLine kept
            // "PHẦN THƯỞNG" on a single line its ends ran onto them at 1080×2340. The height is kept
            // short too, but it is OneLine, not the band, that stops the wrap — see there.
            label = Label((RectTransform)tab.transform, "Yazi", new Vector2(0.22f, 0.28f), new Vector2(0.78f, 0.72f),
                          string.Empty, 26, TextAnchor.MiddleCenter, EkranKit.Ink, 12);
            return tab;
        }

        public static void SetTab(Button tab, bool selected)
        {
            if (tab == null) return;
            Sprite want = AtolyeKit.Get(selected ? "sekme_secili" : "sekme");
            var image = (Image)tab.targetGraphic;
            if (want == null || image.sprite == want) return;
            image.sprite = want;
            var fit = image.GetComponent<PillFit>();
            if (fit != null) fit.Fit();
        }

        /// <summary>A capsule button built on its pale face; <see cref="SetFace"/> gives it its state.</summary>
        public static Button Capsule(RectTransform parent, string name, Vector2 min, Vector2 max, UnityAction onClick,
                                     out Text label)
        {
            Button button = EkranKit.Capsule(parent, name, EkranKit.Get("btn_bos"), min, max, onClick);
            label = Label((RectTransform)button.transform, "Yazi", CapsBandMin, CapsBandMax,
                          string.Empty, 24, TextAnchor.MiddleCenter, EkranKit.Ink, 12);
            return button;
        }

        /// <summary>
        /// Green to take, orange for the screen's main act, pale for not now — and the label's band and
        /// ink move with the face, because the orange capsule's writing space is its cream inlay while
        /// the other two are writable to their caps. The sprite is only written when it changes: this
        /// runs once a second on every row.
        /// </summary>
        public static void SetFace(Button button, Text label, Face face, bool canPress)
        {
            if (button == null) return;
            button.interactable = canPress;
            if (label != null) label.color = face == Face.Claim ? EkranKit.Paper : EkranKit.Ink;

            Sprite want = face == Face.Claim ? LigKit.Get("al_butonu")
                        : face == Face.Primary ? EkranKit.Get("btn_turuncu")
                        : EkranKit.Get("btn_bos");
            var image = (Image)button.targetGraphic;
            if (want == null || image.sprite == want) return;
            image.sprite = want;
            var fit = image.GetComponent<PillFit>();
            if (fit != null) fit.Fit();

            if (label == null) return;
            bool inlay = face == Face.Primary;
            UiBuild.Anchor((RectTransform)label.transform.parent,
                           inlay ? EkranKit.InlayMin : CapsBandMin,
                           inlay ? EkranKit.InlayMax : CapsBandMax);
        }

        /// <summary>The workshop's blue chip with a white label — a balance the screen is counting in.</summary>
        public static Text Chip(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            Image chip = EkranKit.Sliced(parent, name, AtolyeKit.Get("hap_cip"), min, max, true);
            return Label(chip.rectTransform, "Yazi", new Vector2(0.14f, 0.20f), new Vector2(0.86f, 0.80f),
                         string.Empty, 26, TextAnchor.MiddleCenter, EkranKit.Paper, 13);
        }

        /// <summary>
        /// A capsule bar on the workshop's dark track, and the fill inside it whose WIDTH is driven —
        /// see GoalsUI.Bar for why not <see cref="Image.Type.Filled"/>. <paramref name="fill"/> names
        /// one of the kit's three fills: cubuk_mavi, cubuk_yesil, cubuk_altin.
        /// </summary>
        public static Image Bar(RectTransform parent, string name, Vector2 min, Vector2 max, string fill)
        {
            Image track = EkranKit.Sliced(parent, name, AtolyeKit.Get("cubuk_yatak"), min, max, true);
            RectTransform area = Slot(track.rectTransform, "DolguAlani", Vector2.zero, Vector2.one);
            area.offsetMin = new Vector2(3f, 3f);
            area.offsetMax = new Vector2(-3f, -3f);
            return EkranKit.Sliced(area, "Dolgu", AtolyeKit.Get(fill), Vector2.zero, new Vector2(0f, 1f), true);
        }

        public static void Progress(Image fill, float value)
        {
            if (fill == null) return;
            float t = Mathf.Clamp01(value);
            var rt = (RectTransform)fill.transform;
            if (rt.anchorMax.x != t) rt.anchorMax = new Vector2(t, 1f);
            // An empty fill is not drawn at all: at zero width its round caps would still show as a dot.
            if (fill.enabled != t > 0f) fill.enabled = t > 0f;
        }

        // -------------------------------------------------------------------- rows
        /// <summary>
        /// The navy card. <paramref name="borderScale"/> is its <see cref="Image.pixelsPerUnitMultiplier"/>:
        /// the art is 240 px tall with 151 of them border, so a row shorter than that needs the frame
        /// drawn finer or its well becomes a sliver. A nine-slice scales its corners evenly — finer, never squashed.
        /// </summary>
        public static Image Card(RectTransform parent, string name, Vector2 min, Vector2 max, float borderScale)
        {
            Image card = EkranKit.Sliced(parent, name, EkranKit.Get("kart_lacivert"), min, max, false);
            card.pixelsPerUnitMultiplier = borderScale;
            return card;
        }

        /// <summary>
        /// A vertical list: <paramref name="name"/>/Viewport/Content, masked, dragged by anything in it that
        /// is not a button. Returns the content; its root is <c>content.parent.parent</c>.
        /// </summary>
        public static RectTransform Scroll(RectTransform parent, string name, Vector2 min, Vector2 max,
                                           int rows, float pitch)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(ScrollRect));
            go.transform.SetParent(parent, false);
            RectTransform root = UiBuild.Anchor((RectTransform)go.transform, min, max);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportGo.transform.SetParent(root, false);
            RectTransform viewport = UiBuild.Anchor((RectTransform)viewportGo.transform, Vector2.zero, Vector2.one);
            // Catches the drag in the gaps between rows; invisible, but a raycast target.
            Image catcher = viewportGo.GetComponent<Image>();
            catcher.sprite = UiSkin.Flat;
            catcher.color = new Color(1f, 1f, 1f, 0.001f);
            catcher.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, rows * pitch);

            var scroll = go.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 45f;
            return content;
        }

        /// <summary>Resizes a list to the rows it is actually showing, so it never scrolls past the last one.</summary>
        public static void SetRows(RectTransform content, int rows, float pitch)
        {
            float height = rows * pitch;
            if (content.sizeDelta.y != height) content.sizeDelta = new Vector2(0f, height);
        }

        /// <summary>A navy card seated at row <paramref name="index"/> of a <see cref="Scroll"/> list.</summary>
        public static Image Row(RectTransform content, string name, int index, float pitch, float height,
                                float borderScale)
        {
            Image card = Card(content, name, Vector2.zero, Vector2.one, borderScale);
            RectTransform rt = card.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, -index * pitch);
            return card;
        }

        /// <summary>
        /// A one-line label — a tab, a capsule button — with its spaces made non-breaking.
        ///
        /// A BAND ALONE CANNOT STOP THE WRAP. Legacy best fit takes the largest size at which the text
        /// fits its rect WITH wrapping, and a tab or capsule grows taller on a tall phone: "PHẦN THƯỞNG"
        /// broke in two at 1080×2340 however short its band was cut. With no break opportunity the only
        /// way to fit is to shrink, which is what a one-line label should do. Measured in Play: two
        /// lines at 24 became one line at 21, and LegacyRuntime carries the glyph.
        /// </summary>
        public static string OneLine(string text)
            => string.IsNullOrEmpty(text) ? text : text.Replace(' ', (char)0xA0);

        public static void SetActive(Component c, bool on)
        {
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        /// <summary>A timed income boost as a word — "×2 30 MIN" — for the reward rows and card.</summary>
        public static string Boost(double multiplier, double seconds)
            => multiplier > 1d ? "×" + multiplier.ToString("0.#") + " " + HudUI.LongClock((float)seconds) : null;
    }
}
