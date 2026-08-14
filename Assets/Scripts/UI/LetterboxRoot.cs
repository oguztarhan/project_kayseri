using System.Collections.Generic;
using UnityEngine;

namespace Game.UI
{
    /// <summary>How a portrait-authored sheet is made to use a landscape screen.</summary>
    public enum LandscapeFit
    {
        /// <summary>Tries each strategy and keeps whichever draws the screen biggest.</summary>
        Auto = 0,
        /// <summary>Scales the whole design rect to fit. What every panel did before landscape mattered.</summary>
        Uniform = 1,
        /// <summary>Scales the union of the content's own rects, ignoring the empty sheet around it.</summary>
        FitContent = 2,
        /// <summary>Folds the sheet's top half and bottom half into two side-by-side columns.</summary>
        TwoColumn = 3,
        /// <summary>Drops the letterbox: the sheet becomes the screen, for content anchored to its edges.</summary>
        Stretch = 4,
        /// <summary>Folds the rows <i>inside</i> the card into two columns and widens the card to suit.</summary>
        InnerColumns = 5,
    }

    /// <summary>
    /// Re-fits a portrait-authored screen onto a landscape one, so the game can run landscape without
    /// every panel being redrawn in Figma first.
    ///
    /// The panels came out corner-anchored with absolute pixel offsets — a button 1400px down a
    /// 2340-tall sheet is 1400px down whatever it is parented to. Stretch that parent across a 1080-tall
    /// landscape canvas and the button is simply off the screen. So the sheet is pinned to its design
    /// size and scaled as one piece, which keeps every one of those offsets pointing where it was drawn.
    ///
    /// Scaled whole, though, a 1080x2340 sheet lands at <b>0.46</b> on a 2340x1080 phone: a 498px strip
    /// using a fifth of the width, unreadable at arm's length. Getting that back means using the width,
    /// and the width is only there if the content stops being a single tall column:
    ///
    /// <list type="bullet">
    /// <item><b>FitContent</b> — cheapest. Most sheets are one card floating in mostly empty space;
    /// measuring what is actually drawn and fitting that instead is free scale, and nothing moves
    /// relative to anything else.</item>
    /// <item><b>TwoColumn</b> — the blocks in the sheet's top half slide left, the bottom half slides
    /// right. For the upgrade screen that puts the model and its title beside the upgrade tray.</item>
    /// <item><b>InnerColumns</b> — the same fold one level down, on the rows inside a card, with the card
    /// itself widened and shortened to hold them. This is what turns a 976x1520 settings card into a
    /// ~1900x870 landscape one; fitting the card without folding it only ever buys back the margins.</item>
    /// <item><b>Stretch</b> — for a screen that is one edge-anchored scroll view. Nothing to letterbox:
    /// the sheet is resized to the screen at 1:1 and the content adapts on its own.</item>
    /// </list>
    ///
    /// <see cref="LandscapeFit.Auto"/> costs one arithmetic pass over the blocks and then picks whichever
    /// wins, which is nearly always the most aggressive one that applies. Set a mode explicitly to
    /// overrule it.
    ///
    /// Goes on the content, never on the dimmer — <c>Karartma</c> stays stretched to the full screen so
    /// the darkening still covers everything behind the card.
    /// </summary>
    [ExecuteAlways]
    // Ahead of the screen scripts: StationScreenUI caches its tray's home position in Awake, and the
    // fold has to have moved the tray by then or the open animation slides it back to the portrait spot.
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(RectTransform))]
    public sealed class LetterboxRoot : MonoBehaviour
    {
        [Tooltip("The resolution this screen's children were laid out against — the CanvasScaler " +
                 "reference the Figma export used. Offsets inside stay true as long as this matches.")]
        [SerializeField] private Vector2 designSize = new Vector2(1080f, 2340f);

        [Tooltip("How to use the extra width when the screen is landscape. Portrait always uses Uniform.")]
        [SerializeField] private LandscapeFit landscapeFit = LandscapeFit.Auto;

        [Tooltip("The node whose direct children are the screen's blocks. Empty picks the SafeArea child, " +
                 "or this rect itself if there isn't one.")]
        [SerializeField] private RectTransform content;

        [Tooltip("InnerColumns: the card whose rows get folded. Empty picks the block with the most children.")]
        [SerializeField] private RectTransform card;

        [Tooltip("Screen pixels kept clear on every side so nothing sits hard against a bezel.")]
        [SerializeField] private float edgePadding = 24f;

        [Tooltip("Scale ceiling. 1 means art is never drawn above the resolution it was authored at.")]
        [SerializeField] private float maxScale = 1f;

        [Tooltip("Design units between two folded columns.")]
        [SerializeField] private float columnGap = 72f;

        /// <summary>
        /// How many design units tall the screen currently is — the height a child can grow to and still
        /// be on screen. Not the same as the frame's own height once the sheet is scaled down, which is
        /// what a screen sizing a list against its parent would otherwise get.
        /// </summary>
        public float VisibleHeight { get; private set; }

        private RectTransform _rect;
        private RectTransform _parent;
        private RectTransform _frame;
        private Vector2 _applied = new Vector2(-1f, -1f);
        private Rect _appliedFrame = new Rect(-1f, -1f, -1f, -1f);

        // Folding writes anchoredPosition and sizeDelta on nodes that live in the prefab, so their
        // authored values have to be kept to fold from — a second Apply must not fold a folded sheet.
        private readonly List<RectTransform> _moved = new List<RectTransform>();
        private readonly List<Vector2> _movedPos = new List<Vector2>();
        private readonly List<Vector2> _movedSize = new List<Vector2>();
        private readonly List<bool> _movedResized = new List<bool>();

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _parent = _rect.parent as RectTransform;
            Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

        private void Update()
        {
            // Size compare only — the same trick SafeArea uses, and it catches rotation and resolution
            // changes without needing a callback from the OS.
            if (_parent == null || _rect == null) return;
            if (_parent.rect.size != _applied) { Apply(); return; }

            // The frame is usually a SafeArea, which anchors itself off Screen.safeArea a frame or more
            // after this first runs — and does it again on rotation. Its insets move the content inside
            // the sheet, so a box measured before that lands off-centre by however deep the notch is.
            if (FrameRect(Frame()) != _appliedFrame) Apply();
        }

        private void Apply()
        {
            if (_rect == null) _rect = (RectTransform)transform;
            if (_parent == null) _parent = _rect.parent as RectTransform;
            if (_parent == null || designSize.x <= 0f || designSize.y <= 0f) return;

            Vector2 avail = _parent.rect.size;
            if (avail.x <= 0f || avail.y <= 0f) return;
            _applied = avail;
            _appliedFrame = FrameRect(Frame());

            _rect.anchorMin = Centre;
            _rect.anchorMax = Centre;
            _rect.pivot = Centre;

            Restore();

            LandscapeFit fit = Resolve(avail);

            if (fit == LandscapeFit.Stretch)
            {
                // Never narrower than the design width, or the offsets inside would start overlapping.
                float s = Mathf.Min(maxScale, avail.x / designSize.x);
                if (s <= 0f) return;
                _rect.sizeDelta = avail / s;
                _rect.anchoredPosition = Vector2.zero;
                _rect.localScale = new Vector3(s, s, 1f);
                VisibleHeight = avail.y / s;
                return;
            }

            _rect.sizeDelta = designSize;

            Rect box;
            switch (fit)
            {
                case LandscapeFit.FitContent: box = ContentBox(); break;
                case LandscapeFit.TwoColumn: box = FoldBlocks(avail, true); break;
                case LandscapeFit.InnerColumns: box = FoldCard(avail, true); break;
                default: box = new Rect(designSize * -0.5f, designSize); break;
            }

            // The smaller of the two ratios — whatever box we settled on arrives whole, and whichever way
            // it is the long way up decides the scale. Uniform takes no padding: it is what a portrait
            // screen falls back to, and there the sheet is meant to land at exactly 1:1.
            float pad = fit == LandscapeFit.Uniform ? 0f : edgePadding * 2f;
            float scale = ScaleFor(box, avail, pad);
            _rect.localScale = new Vector3(scale, scale, 1f);
            _rect.anchoredPosition = -box.center * scale;   // puts the box's middle on the screen's middle
            VisibleHeight = avail.y / scale;
        }

        private float ScaleFor(Rect box, Vector2 avail, float pad)
        {
            if (box.width <= 0f || box.height <= 0f) return 1f;
            float s = Mathf.Min((avail.x - pad) / box.width, (avail.y - pad) / box.height);
            return Mathf.Clamp(Mathf.Min(s, maxScale), 0.01f, 100f);
        }

        private LandscapeFit Resolve(Vector2 avail)
        {
            // Portrait is what the sheets were drawn for; there is nothing to win and plenty to break.
            if (avail.x <= avail.y) return LandscapeFit.Uniform;

            // Folding writes to nodes that live in the prefab. Doing that outside Play mode would have
            // the editor serialise the folded layout over the authored one the next time the prefab is
            // saved, and the original would be gone.
            bool mayFold = Application.isPlaying;

            if (landscapeFit != LandscapeFit.Auto)
            {
                bool folds = landscapeFit == LandscapeFit.TwoColumn || landscapeFit == LandscapeFit.InnerColumns;
                return folds && !mayFold ? LandscapeFit.FitContent : landscapeFit;
            }

            // A screen whose blocks barely cover the sheet is a screen that *is* its stretched child — a
            // full-bleed scroll view with a close button floating over it. Measuring the close button and
            // fitting the sheet to that would centre the screen on the button and hang the scroll off
            // both ends, so these go to Stretch and let the scroll size itself.
            Rect fitBox = ContentBox();
            if (fitBox.width * fitBox.height < designSize.x * designSize.y * 0.4f) return LandscapeFit.Stretch;

            if (!mayFold) return LandscapeFit.FitContent;

            // Measure all three and keep the biggest. Every candidate here is a legitimate rendering of
            // the same screen, so there is nothing to weigh against scale.
            float pad = edgePadding * 2f;
            LandscapeFit best = LandscapeFit.FitContent;
            float bestScale = ScaleFor(fitBox, avail, pad);

            float inner = ScaleFor(FoldCard(avail, false), avail, pad);
            if (inner > bestScale) { bestScale = inner; best = LandscapeFit.InnerColumns; }

            float two = ScaleFor(FoldBlocks(avail, false), avail, pad);
            if (two > bestScale) best = LandscapeFit.TwoColumn;

            return best;
        }

        // ---------- geometry ----------

        /// <summary>
        /// The node holding the screen's blocks — the SafeArea child if there is one. Resolved once:
        /// Update asks for the frame's rect every frame to catch the safe area landing, and that must
        /// not turn into a GetComponent sweep per frame.
        /// </summary>
        private RectTransform Frame()
        {
            if (_frame != null) return _frame;
            if (content != null) return _frame = content;
            for (int i = 0; i < _rect.childCount; i++)
            {
                var child = _rect.GetChild(i) as RectTransform;
                if (child != null && child.GetComponent<SafeArea>() != null) return _frame = child;
            }
            return _frame = _rect;
        }

        /// <summary>
        /// The card to fold the rows of — the block carrying the most children, which on every one of
        /// these screens is the card and not the title strip, the glow or the close button.
        /// </summary>
        private RectTransform Card(RectTransform frame)
        {
            if (card != null) return card;
            RectTransform best = null;
            int most = 1;
            for (int i = 0; i < frame.childCount; i++)
            {
                var child = frame.GetChild(i) as RectTransform;
                if (!IsBlock(child) || child.childCount <= most) continue;
                most = child.childCount;
                best = child;
            }
            return best;
        }

        /// <summary>
        /// Where a block sits, in its parent's coordinates. Read off the anchors rather than <c>rect</c>
        /// so it is right on the first frame, before uGUI has resolved any layout.
        /// </summary>
        private static Rect BlockRect(RectTransform block, Vector2 frame)
        {
            Vector2 size = new Vector2(
                (block.anchorMax.x - block.anchorMin.x) * frame.x + block.sizeDelta.x,
                (block.anchorMax.y - block.anchorMin.y) * frame.y + block.sizeDelta.y);

            Vector2 anchor = new Vector2(
                ((block.anchorMin.x + block.anchorMax.x) * 0.5f - 0.5f) * frame.x,
                ((block.anchorMin.y + block.anchorMax.y) * 0.5f - 0.5f) * frame.y);

            // anchoredPosition places the pivot, not the middle, so step back to the middle.
            Vector2 centre = anchor + block.anchoredPosition
                             + new Vector2((0.5f - block.pivot.x) * size.x, (0.5f - block.pivot.y) * size.y);
            return new Rect(centre - size * 0.5f, size);
        }

        /// <summary>The inverse of <see cref="BlockRect"/>: put a block at a centre, at a size.</summary>
        private void Place(RectTransform block, Vector2 frame, Vector2 centre, Vector2 size)
        {
            Remember(block, true);
            block.sizeDelta = new Vector2(
                size.x - (block.anchorMax.x - block.anchorMin.x) * frame.x,
                size.y - (block.anchorMax.y - block.anchorMin.y) * frame.y);

            Vector2 anchor = new Vector2(
                ((block.anchorMin.x + block.anchorMax.x) * 0.5f - 0.5f) * frame.x,
                ((block.anchorMin.y + block.anchorMax.y) * 0.5f - 0.5f) * frame.y);

            block.anchoredPosition = centre - anchor
                                     - new Vector2((0.5f - block.pivot.x) * size.x, (0.5f - block.pivot.y) * size.y);
        }

        /// <summary>
        /// The blocks worth measuring. A child stretched to both edges is a backdrop, a dimmer or a
        /// confetti layer — it fills whatever box we pick, so letting it vote would always return the
        /// whole sheet and nothing would ever be gained.
        ///
        /// Hidden blocks still count. Screens like the upgrade tray switch pages by disabling the model
        /// and the phase bar, and a layout that reshuffled itself under the player on every page change
        /// would be worse than one that is slightly generous on the pages showing less.
        /// </summary>
        private static bool IsBlock(RectTransform child)
        {
            if (child == null) return false;
            return !(child.anchorMin == Vector2.zero && child.anchorMax == Vector2.one);
        }

        /// <summary>
        /// Where the block frame sits inside the sheet. Derived from its anchors rather than read from
        /// <c>rect</c>, which on the first frame still holds whatever the last screen size resolved to.
        /// </summary>
        private Rect FrameRect(RectTransform frame)
        {
            if (frame == _rect) return new Rect(designSize * -0.5f, designSize);
            return BlockRect(frame, designSize);
        }

        private static Rect Union(Rect a, Rect b)
        {
            float xMin = Mathf.Min(a.xMin, b.xMin), xMax = Mathf.Max(a.xMax, b.xMax);
            float yMin = Mathf.Min(a.yMin, b.yMin), yMax = Mathf.Max(a.yMax, b.yMax);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        /// <summary>The union of a node's block children, in that node's own coordinates.</summary>
        private static bool ChildBox(RectTransform parent, Vector2 frame, out Rect box)
        {
            box = new Rect();
            bool any = false;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i) as RectTransform;
                if (!IsBlock(child)) continue;
                Rect r = BlockRect(child, frame);
                box = any ? Union(box, r) : r;
                any = true;
            }
            return any;
        }

        private Rect ContentBox()
        {
            RectTransform frame = Frame();
            Rect frameRect = FrameRect(frame);
            if (!ChildBox(frame, frameRect.size, out Rect box)) return new Rect(designSize * -0.5f, designSize);
            box.position += frameRect.center;
            return box;
        }

        // ---------- folding ----------

        /// <summary>
        /// Splits blocks into an upper group and a lower group and reports the shift that puts the upper
        /// group in a left column and the lower group in a right column, side by side and each centred.
        ///
        /// Every split the blocks allow is measured and the one that ends up biggest on this screen
        /// wins, rather than always cutting at the sheet's middle — where the middle falls has nothing
        /// to do with where the content is, and a card sitting entirely in the top third of its sheet
        /// would not be cut at all. Blocks sharing a centre share a group, so a row of day tiles or a
        /// stack of concentric glows is never split down the middle.
        /// </summary>
        private bool PlanFold(List<Rect> rects, Vector2 avail,
                              out Vector2 topShift, out Vector2 bottomShift, out Rect folded, out float splitY)
        {
            topShift = bottomShift = Vector2.zero;
            folded = new Rect();
            splitY = 0f;
            if (rects.Count < 2) return false;

            // Descending by centre, so a split index means "this many blocks in the left column".
            var order = new List<int>(rects.Count);
            for (int i = 0; i < rects.Count; i++) order.Add(i);
            order.Sort((a, b) => rects[b].center.y.CompareTo(rects[a].center.y));

            float pad = edgePadding * 2f;
            float bestScale = -1f;
            float bestBalance = float.MaxValue;
            for (int cut = 1; cut < order.Count; cut++)
            {
                // Never inside a group of blocks that share a centre — they are one row.
                if (Mathf.Approximately(rects[order[cut - 1]].center.y, rects[order[cut]].center.y)) continue;

                Rect top = rects[order[0]], bottom = rects[order[cut]];
                for (int i = 1; i < cut; i++) top = Union(top, rects[order[i]]);
                for (int i = cut + 1; i < order.Count; i++) bottom = Union(bottom, rects[order[i]]);

                float width = top.width + columnGap + bottom.width;
                float height = Mathf.Max(top.height, bottom.height);
                var candidate = new Rect(width * -0.5f, height * -0.5f, width, height);

                // Scored uncapped, on purpose. Clamping to maxScale here would tie every split that
                // already fits and hand the win to whichever came first, which is the most lopsided one.
                float scale = Mathf.Min((avail.x - pad) / width, (avail.y - pad) / height);
                float balance = Mathf.Abs(top.height - bottom.height);

                bool better = scale > bestScale * 1.001f
                              || (scale > bestScale * 0.999f && balance < bestBalance);
                if (!better) continue;

                bestScale = Mathf.Max(scale, bestScale);
                bestBalance = balance;
                splitY = rects[order[cut]].center.y;
                folded = candidate;
                topShift = new Vector2(-width * 0.5f + top.width * 0.5f - top.center.x, -top.center.y);
                bottomShift = new Vector2(width * 0.5f - bottom.width * 0.5f - bottom.center.x, -bottom.center.y);
            }

            return bestScale > 0f;
        }

        /// <summary>Folds the frame's own blocks into two columns. The upgrade screen's shape.</summary>
        private Rect FoldBlocks(Vector2 avail, bool commit)
        {
            RectTransform frame = Frame();
            Rect frameRect = FrameRect(frame);

            var blocks = new List<RectTransform>();
            var rects = new List<Rect>();
            for (int i = 0; i < frame.childCount; i++)
            {
                var child = frame.GetChild(i) as RectTransform;
                if (!IsBlock(child)) continue;
                blocks.Add(child);
                rects.Add(BlockRect(child, frameRect.size));
            }

            if (!PlanFold(rects, avail, out Vector2 topShift, out Vector2 bottomShift, out Rect folded, out float splitY))
                return ContentBox();

            if (commit)
                for (int i = 0; i < blocks.Count; i++)
                {
                    Remember(blocks[i], false);   // position only — the tray resizes itself per page
                    blocks[i].anchoredPosition += rects[i].center.y > splitY ? topShift : bottomShift;
                }

            folded.position += frameRect.center;
            return folded;
        }

        /// <summary>
        /// Folds the rows inside the card into two columns, then rebuilds the card around them: same
        /// margins as it was drawn with, but wide and short instead of narrow and tall. The card's top
        /// edge stays put so whatever title strip sits above it still lines up, and anything out at the
        /// card's edge — a close button in the corner — keeps its gap to that edge.
        /// </summary>
        private Rect FoldCard(Vector2 avail, bool commit)
        {
            RectTransform frame = Frame();
            Rect frameRect = FrameRect(frame);
            RectTransform target = Card(frame);
            if (target == null) return ContentBox();

            Rect cardRect = BlockRect(target, frameRect.size);
            if (!ChildBox(target, cardRect.size, out Rect rowsBox)) return ContentBox();

            var rows = new List<RectTransform>();
            var rects = new List<Rect>();
            for (int i = 0; i < target.childCount; i++)
            {
                var child = target.GetChild(i) as RectTransform;
                if (!IsBlock(child)) continue;
                rows.Add(child);
                rects.Add(BlockRect(child, cardRect.size));
            }

            if (!PlanFold(rects, avail, out Vector2 topShift, out Vector2 bottomShift, out Rect folded, out float splitY))
                return ContentBox();

            // The card is its rows plus the margins it was drawn with. Keeping them keeps the art's
            // corners and padding looking deliberate rather than cropped.
            Vector2 margin = new Vector2(cardRect.width - rowsBox.width, cardRect.height - rowsBox.height);
            Vector2 cardSize = new Vector2(folded.width + margin.x, folded.height + margin.y);
            Vector2 cardCentre = new Vector2(cardRect.center.x, cardRect.yMax - cardSize.y * 0.5f);

            if (!commit)
            {
                Rect planned = SiblingBox(frame, frameRect, target, cardRect, cardCentre, cardSize);
                planned.position += frameRect.center;
                return planned;
            }

            // Rows first, in the card's new coordinates: the fold is planned around the old centre, so
            // recentre it on the new card before placing anything.
            for (int i = 0; i < rows.Count; i++)
            {
                Vector2 shift = rects[i].center.y > splitY ? topShift : bottomShift;
                Place(rows[i], cardSize, rects[i].center + shift, rects[i].size);
            }

            MoveSiblings(frame, frameRect, target, cardRect, cardCentre, cardSize, true);
            Place(target, frameRect.size, cardCentre, cardSize);

            if (!ChildBox(frame, frameRect.size, out Rect box)) return ContentBox();
            box.position += frameRect.center;
            return box;
        }

        /// <summary>What the frame's blocks would cover once the card is resized, without moving any.</summary>
        private Rect SiblingBox(RectTransform frame, Rect frameRect, RectTransform target,
                                Rect cardRect, Vector2 cardCentre, Vector2 cardSize)
        {
            return MoveSiblings(frame, frameRect, target, cardRect, cardCentre, cardSize, false);
        }

        /// <summary>
        /// Keeps the card's siblings reading the same way against a card that has just changed shape.
        /// A block out near the card's edge — a close button in the corner, a title strip sitting above
        /// it — keeps its gap to that edge; a block near the middle — a glow behind the card — keeps its
        /// offset from the middle and so follows the card.
        /// </summary>
        private Rect MoveSiblings(RectTransform frame, Rect frameRect, RectTransform target,
                                  Rect cardRect, Vector2 cardCentre, Vector2 cardSize, bool commit)
        {
            Rect box = new Rect(cardCentre - cardSize * 0.5f, cardSize);
            for (int i = 0; i < frame.childCount; i++)
            {
                var child = frame.GetChild(i) as RectTransform;
                if (!IsBlock(child) || child == target) continue;

                Rect r = BlockRect(child, frameRect.size);
                var moved = new Vector2(
                    Track(r.center.x, cardRect.center.x, cardRect.width, cardCentre.x, cardSize.x),
                    Track(r.center.y, cardRect.center.y, cardRect.height, cardCentre.y, cardSize.y));

                if (commit && (moved - r.center).sqrMagnitude > 0.01f) Place(child, frameRect.size, moved, r.size);
                box = Union(box, new Rect(moved - r.size * 0.5f, r.size));
            }
            return box;
        }

        /// <summary>One axis of <see cref="MoveSiblings"/>: edge-pinned out past a third, else centred.</summary>
        private static float Track(float value, float wasCentre, float wasSize, float centre, float size)
        {
            float offset = value - wasCentre;
            if (Mathf.Abs(offset) <= wasSize / 3f) return centre + offset;
            float edgeGap = Mathf.Abs(offset) - wasSize * 0.5f;
            return centre + Mathf.Sign(offset) * (size * 0.5f + edgeGap);
        }

        // ---------- undo ----------

        /// <summary>
        /// Keeps what a node looked like before the fold touched it. <paramref name="resized"/> only when
        /// the fold changes its size: a screen may set its own list's height between two Applies —
        /// the upgrade tray does exactly that — and putting the authored height back would undo it.
        /// </summary>
        private void Remember(RectTransform rt, bool resized)
        {
            for (int i = 0; i < _moved.Count; i++) if (_moved[i] == rt) return;
            _moved.Add(rt);
            _movedPos.Add(rt.anchoredPosition);
            _movedSize.Add(rt.sizeDelta);
            _movedResized.Add(resized);
        }

        private void Restore()
        {
            for (int i = 0; i < _moved.Count; i++)
            {
                if (_moved[i] == null) continue;
                _moved[i].anchoredPosition = _movedPos[i];
                if (_movedResized[i]) _moved[i].sizeDelta = _movedSize[i];
            }
            _moved.Clear();
            _movedPos.Clear();
            _movedSize.Clear();
            _movedResized.Clear();
        }

        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
    }
}
