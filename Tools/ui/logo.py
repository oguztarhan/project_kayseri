"""The game's wordmark: ISLAND MINING TYCOON.

Built to the casual-store logo language the reference set uses, which is four things and not
one of them optional:

  1. the words sit on an ARCH, glyphs rotating with the tangent -- a straight baseline reads
     as a caption no matter how well the letters are drawn
  2. a thick CREAM KEYLINE wraps the whole extruded silhouette, so the mark survives on any
     background without a plate behind it
  3. the EXTRUSION is deep and solid, stacked from the bottom copy up
  4. the stack GROWS: ISLAND narrow, MINING wide, TYCOON small on a banner -- three words the
     same size are a list, and a list is what killed the first attempt

Every layer is one <text> reused by <use>, so a pass costs one line instead of thirteen.
paint-order="stroke" is what puts each outline OUTSIDE the glyph; without it a 40-wide stroke
eats twenty pixels into the letter and the face goes spindly.

Unlike every other piece in this set the logo DOES use <text>. The no-text rule exists so a
localisation cannot reflow a sprite; a wordmark is one baked image with nothing to reflow, and
outlining 18 glyphs by hand would only make it worse. The face is Baloo2-ExtraBold, the game's
own font, loaded from the project by rasterize_logo.py.
"""
import pathlib

OUT = pathlib.Path(__file__).parent / "svg"
OUT.mkdir(exist_ok=True)

NAVY = "#182840"
INK = "#101A2E"          # the outline the whole mark is cut from
CREAM = "#FFF4DC"        # the keyline that lets it sit on anything
# The extruded side of the letters, one per face colour. A near-black chunk vanishes against a
# dark screen and the mark goes flat; the reference logos all cut their extrusion from a darker
# shade of the FACE, so the depth stays legible on any background.
BRONZE = "#9A4E06"
MOSS = "#0A5C2E"
RIM = "#E0A028"          # the plated band between keyline and outline
STEEL_HI, STEEL_LO = "#F0F4FC", "#A4B4CE"
WOOD_HI, WOOD_LO = "#A0682E", "#6A3E18"

W, H = 1160, 880
CX = W / 2

# ---------------------------------------------------------------- defs

def metal(gid, *stops):
    """A face ramp. What separates a premium metal from a coloured letter is not the hue, it
    is the number of events on the way down: near-white crown, a HARD step where the polish
    line falls, a long dark body, then a bright BOUNCE just above the bottom edge -- light the
    surface throws back up off whatever it is standing on. Drop the bounce and the same colours
    read as flat paint."""
    body = "".join(f'<stop offset="{o}" stop-color="{c}"/>' for o, c in stops)
    return f'<linearGradient id="{gid}" x1="0" y1="0" x2="0" y2="1">{body}</linearGradient>'


GRADS = (
    metal("gold",
          (0, "#FFFDF2"), (0.13, "#FFF0AC"), (0.29, "#FFDA62"),
          (0.31, "#E9A417"), (0.56, "#D98A0C"), (0.79, "#C57405"),
          (0.90, "#F2B534"), (1, "#B0620A"))
    # ISLAND's face. The reference logos never run two words in one colour -- the second word
    # is always a different hue at the same treatment, and that contrast is most of their
    # punch. Emerald against gold also happens to be two of the game's own ores.
    + metal("jade",
            (0, "#F2FFF8"), (0.13, "#C6FADD"), (0.29, "#68E39C"),
            (0.31, "#22B463"), (0.56, "#149A52"), (0.79, "#0C8244"),
            (0.90, "#3FCC80"), (1, "#08652F"))
    + '<linearGradient id="steel" x1="0" y1="0" x2="0" y2="1">'
    + f'<stop offset="0" stop-color="{STEEL_HI}"/>'
    + f'<stop offset="0.46" stop-color="#DCE6F4"/>'
    + f'<stop offset="0.48" stop-color="#B4C4DC"/>'
    + f'<stop offset="1" stop-color="{STEEL_LO}"/>'
    + '</linearGradient>'
    + '<linearGradient id="wood" x1="0" y1="0" x2="0" y2="1">'
    + f'<stop offset="0" stop-color="{WOOD_HI}"/>'
    + f'<stop offset="1" stop-color="{WOOD_LO}"/>'
    + '</linearGradient>'
    # the mark sits above the screen rather than printed on it
    + '<filter id="lift" x="-14%" y="-12%" width="128%" height="128%">'
    + '<feDropShadow dx="0" dy="15" stdDeviation="17" flood-color="#020609" '
      'flood-opacity="0.58"/></filter>'
)


def side_ramp(gid, top, bottom, y1, y2):
    """The extruded flank, graded in USER space so one ramp spans the whole chunk. Filled per
    copy in object space instead, every slice would carry an identical ramp and the extrusion
    would come out as a flat slab."""
    return (f'<linearGradient id="{gid}" gradientUnits="userSpaceOnUse" x1="0" y1="{y1}" '
            f'x2="0" y2="{y2}"><stop offset="0" stop-color="{top}"/>'
            f'<stop offset="1" stop-color="{bottom}"/></linearGradient>')


# ---------------------------------------------------------------- arched words

def arch(wid, text, baseline, size, half_w, rise, length):
    """Defines the arc and the single <text> every layer of one word reuses.

    A quadratic with its control point `rise` above the baseline puts the apex exactly ON the
    baseline and drops both ends by `rise` -- so `rise` is literally the height of the arch,
    and the two words stay comparable by construction."""
    return (f'<path id="p{wid}" d="M{CX - half_w} {baseline + rise} '
            f'Q{CX} {baseline - rise} {CX + half_w} {baseline + rise}"/>'
            f'<text id="t{wid}" font-family="Baloo2" font-weight="800" font-size="{size}" '
            f'text-anchor="middle" paint-order="stroke" stroke-linejoin="round">'
            f'<textPath href="#p{wid}" startOffset="50%" textLength="{length}" '
            f'lengthAdjust="spacingAndGlyphs">{text}</textPath></text>')


def stack(wid, face, side, extrude=28, step=2, keyline=62, rim=0, edge=42, seam=6):
    """Four passes over the same glyphs: cream silhouette, ink outline, bronze chunk, gold face.

    Each pass repeats at every extrusion offset, so the strokes union into ONE outline around
    the whole chunk instead of thirteen stacked rings. The cream survives only where it is
    wider than the ink -- (keyline - edge) / 2 px of it, all the way round.

    The bronze carries almost no stroke on purpose. Widen it and the chunk bleeds out past the
    face as well as below it, the face's own outline then has to cover that bleed, and the
    extrusion ends up divorced from the letter by a fat ink band -- brown crescents floating
    under gold. Stroke it at ~0 and its union is exactly the glyph swept downward, which is
    what an extrusion actually is. `seam` is the hairline that still separates face from side."""
    out = []
    for dy in range(extrude, -1, -step):
        out.append(f'<use href="#t{wid}" y="{dy}" fill="{CREAM}" stroke="{CREAM}" '
                   f'stroke-width="{keyline}"/>')
    # An optional gold band between cream and ink. Tried at full width it muddied the coloured
    # word -- three concentric rings around one letter is a lot of edge and the face stops being
    # the loudest thing. Off by default; premium is coming from the ramp, not from more rings.
    if rim > edge:
        for dy in range(extrude, -1, -step):
            out.append(f'<use href="#t{wid}" y="{dy}" fill="{RIM}" stroke="{RIM}" '
                       f'stroke-width="{rim}"/>')
    for dy in range(extrude, -1, -step):
        out.append(f'<use href="#t{wid}" y="{dy}" fill="{INK}" stroke="{INK}" '
                   f'stroke-width="{edge}"/>')
    for dy in range(extrude, -1, -step):
        out.append(f'<use href="#t{wid}" y="{dy}" fill="url(#{side})" stroke="url(#{side})" '
                   f'stroke-width="2"/>')
    out.append(f'<use href="#t{wid}" fill="url(#{face})" stroke="{INK}" stroke-width="{seam}"/>')
    return "".join(out)


# ---------------------------------------------------------------- ornament

def pick(x, y, angle, scale=1.0, silhouette=False):
    """One pickaxe, drawn head-up with the handle hanging down, then rotated about its own
    middle so a pair of them crosses cleanly. White-with-gold-accent on an ink outline, the
    rule the HUD icons follow. Spikes are near-straight wedges: curve them much and the head
    stops reading as a pick and starts reading as a pair of horns.

    `silhouette` flattens every part to cream and fattens every stroke -- drawn underneath the
    real pair, that copy IS the keyline. Swapping colours without also swapping widths would
    hide it exactly behind the original, which is what the first attempt did."""
    ink = CREAM if silhouette else INK
    steel = CREAM if silhouette else "url(#steel)"
    wood = CREAM if silhouette else "url(#wood)"
    gold = CREAM if silhouette else "#F5C63C"
    w = 40 if silhouette else 13
    g = (
        f'<rect x="-23" y="-40" width="46" height="344" rx="23" fill="{wood}" '
        f'stroke="{ink}" stroke-width="{w}"/>'
        + ('' if silhouette else
           '<rect x="-11" y="0" width="12" height="286" rx="6" fill="#FFFFFF" opacity="0.20"/>')
        + f'<path d="M-44 -70 C-118 -50 -172 -22 -212 6 L-196 44 C-152 24 -102 12 -44 8 Z" '
          f'fill="{steel}" stroke="{ink}" stroke-width="{w}" stroke-linejoin="round"/>'
          f'<path d="M44 -70 C118 -50 172 -22 212 6 L196 44 C152 24 102 12 44 8 Z" '
          f'fill="{steel}" stroke="{ink}" stroke-width="{w}" stroke-linejoin="round"/>'
        # collar kept small: enlarged, it stops reading as a collar and starts reading as a
        # second white blob sitting on the head
          f'<rect x="-42" y="-68" width="84" height="92" rx="20" fill="{steel}" '
          f'stroke="{ink}" stroke-width="{w}"/>'
        # the one gold accent the icon rule allows -- a ferrule on the shaft, where a band on
        # the collar just looked like a pill stuck to the metal
          f'<rect x="-31" y="34" width="62" height="30" rx="15" fill="{gold}" '
          f'stroke="{ink}" stroke-width="{w if silhouette else 11}"/>'
    )
    return (f'<g transform="translate({x} {y}) rotate({angle}) scale({scale}) '
            f'translate(0 -118)">{g}</g>')


def crest(x, y, scale):
    """The crossed pair over its own cream keyline, so the ornament and the wordmark read as
    one cut-out sticker rather than two pasted pieces."""
    return (pick(x, y, -38, scale, True) + pick(x, y, 38, scale, True)
            + pick(x, y, -38, scale) + pick(x, y, 38, scale))


def gem(cx, cy, r):
    """A tiny cut stone to flank the small word. Same cut-out anatomy as everything else --
    cream keyline, ink edge, gold face -- because the moment one element gets its own
    treatment the mark stops being one object."""
    pts = f'M{cx} {cy - r} L{cx + r * 0.74} {cy - r * 0.16} L{cx} {cy + r} L{cx - r * 0.74} {cy - r * 0.16} Z'
    return (f'<path d="{pts}" fill="{CREAM}" stroke="{CREAM}" stroke-width="30" '
            f'stroke-linejoin="round"/>'
            f'<path d="{pts}" fill="{INK}" stroke="{INK}" stroke-width="16" '
            f'stroke-linejoin="round"/>'
            f'<path d="{pts}" fill="url(#gold)"/>')


# ---------------------------------------------------------------- assembly

DEFS = (GRADS
        + arch("A", "ISLAND", 424, 150, 322, 36, 610)
        + arch("B", "MINING", 618, 188, 414, 48, 816)
        + arch("C", "TYCOON", 764, 70, 168, 9, 336)
        + side_ramp("sB", "#C96A0A", "#7A3A03", 604, 700)
        + side_ramp("sC", "#B25C08", "#7A3A03", 754, 796)
        + side_ramp("sJade", "#1A9350", "#0A5E2E", 410, 496)
)


def build(face, side):
    body = (
        crest(CX, 208, 0.70)
        # the keyline scales with the word: one fixed width would puff up around the smaller
        # letters and read as two different marks stacked
        + stack("A", face, side, extrude=30, keyline=54, edge=36)
        + stack("B", "gold", "sB", extrude=36, keyline=62, edge=42)
        # TYCOON gets the SAME build, only smaller. On a plate it read as a different product
        # bolted to the bottom -- no reference logo puts one of its words on a plaque.
        + gem(CX - 248, 742, 32) + gem(CX + 248, 742, 32)
        + stack("C", "gold", "sC", extrude=16, keyline=30, edge=20, seam=4)
    )
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" '
            f'viewBox="0 0 {W} {H}"><defs>{DEFS}</defs>'
            f'<g filter="url(#lift)">{body}</g></svg>')


PIECES = {"logo_oyun": build("jade", "sJade")}

if __name__ == "__main__":
    for name, markup in PIECES.items():
        (OUT / f"{name}.svg").write_text(markup, encoding="utf-8")
        print(f"{name:18s} {len(markup):6d} B")
