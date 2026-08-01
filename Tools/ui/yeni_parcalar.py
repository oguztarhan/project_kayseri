"""Generates the six sprites the world map and the gem shop are missing.

Palette and anatomy are lifted from the shipped set (sampled out of the PNGs in Unity):
every shape carries a navy outline, a vertical body gradient light-at-top, and a gloss
band across the upper third. ASCII-only SVG, no <text> nodes -- the whole set is
text-free so localisation cannot reflow it.
"""
import pathlib

OUT = pathlib.Path(__file__).parent / "svg"
OUT.mkdir(exist_ok=True)

NAVY = "#182840"          # the outline every sprite in the set shares
NAVY_DEEP = "#101830"
NAVY_BODY = "#203878"     # dark disc behind an icon (pill_elmas body)
NAVY_INK = "#405078"      # ruled lines, hat body

GREEN_HI, GREEN_LO = "#50B878", "#30A860"
BLUE_HI, BLUE_LO = "#50A0E8", "#3890E0"
BLUE_RIM = "#184888"
PALE_HI, PALE_LO = "#D0E0F0", "#C8D8E8"
PALE_SHADOW = "#A8B8D8"
CYAN_HI, CYAN_LO = "#D0F0F8", "#A0E0F0"
GOLD_HI, GOLD_LO = "#F8E830", "#F0A030"
WHITE = "#F8F8F8"


def grad(gid, top, bottom):
    return (f'<linearGradient id="{gid}" x1="0" y1="0" x2="0" y2="1">'
            f'<stop offset="0" stop-color="{top}"/>'
            f'<stop offset="1" stop-color="{bottom}"/></linearGradient>')


def gloss(gid, strength=0.55):
    """The white sheen every body in the set carries across its upper third."""
    return (f'<linearGradient id="{gid}" x1="0" y1="0" x2="0" y2="1">'
            f'<stop offset="0" stop-color="#FFFFFF" stop-opacity="{strength}"/>'
            f'<stop offset="0.62" stop-color="#FFFFFF" stop-opacity="0.06"/>'
            f'<stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/></linearGradient>')


def svg(w, h, defs, body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
            f'viewBox="0 0 {w} {h}"><defs>{defs}</defs>{body}</svg>')


# ---------------------------------------------------------------- 1. halat
# The route between two island nodes. Dots, not capsules: capsules read as three separate
# controls stacked up, while dots read as one dashed path. One sprite for both states --
# a locked leg is the same art tinted grey.
def halat():
    w, h = 48, 200
    defs = grad("b", PALE_HI, PALE_LO) + gloss("g")
    parts = []
    for cy in (45, 100, 155):
        parts.append(f'<circle cx="24" cy="{cy}" r="16" fill="url(#b)" '
                     f'stroke="{NAVY}" stroke-width="7"/>')
        parts.append(f'<ellipse cx="24" cy="{cy - 5}" rx="10" ry="6" fill="url(#g)"/>')
    return w, h, svg(w, h, defs, "".join(parts))


# ------------------------------------------------------- 2. rozet_buradasin
# Same chassis as rozet_tamam (navy ring, green body, white glyph) so the map's
# "you are here" marker reads as part of the badge family rather than a new object.
def rozet_buradasin():
    w = h = 200
    defs = grad("b", GREEN_HI, GREEN_LO) + gloss("g") + grad("p", WHITE, "#E0E8F0")
    body = (
        f'<circle cx="100" cy="100" r="86" fill="url(#b)" stroke="{NAVY}" stroke-width="14"/>'
        f'<ellipse cx="100" cy="72" rx="62" ry="40" fill="url(#g)"/>'
        # map pin: round head, tapered tip
        f'<path d="M100 50 c-21 0 -38 17 -38 38 c0 27 38 62 38 62 s38 -35 38 -62 '
        f'c0 -21 -17 -38 -38 -38 z" fill="url(#p)" stroke="{NAVY}" stroke-width="10" '
        f'stroke-linejoin="round"/>'
        f'<circle cx="100" cy="88" r="14" fill="{GREEN_LO}"/>'
    )
    return w, h, svg(w, h, defs, body)


# ------------------------------------------------------- 3. btn_fiyat_elmas
# btn_fiyat_yesil's pill, in the diamond blue, with the gold coin swapped for a gem --
# so a gem price is never mistaken for a cash price at a glance.
def btn_fiyat_elmas():
    w, h = 332, 156
    defs = (grad("b", BLUE_HI, BLUE_LO) + gloss("g") + grad("d", CYAN_HI, CYAN_LO))
    body = (
        f'<rect x="16" y="16" width="300" height="124" rx="62" fill="url(#b)" '
        f'stroke="{NAVY}" stroke-width="12"/>'
        f'<rect x="34" y="30" width="264" height="46" rx="23" fill="url(#g)"/>'
        # gem badge, overlapping the left end exactly as the coin does. The gem fills the
        # socket the way the coin's face does -- a small gem on a dark disc sinks into the
        # outline instead of reading as the price currency.
        f'<circle cx="70" cy="78" r="48" fill="{BLUE_RIM}" stroke="{NAVY}" stroke-width="12"/>'
        f'<path d="M70 36 l38 26 l-38 56 l-38 -56 z" fill="url(#d)" stroke="{NAVY_DEEP}" '
        f'stroke-width="8" stroke-linejoin="round"/>'
        f'<path d="M32 62 h76" stroke="{NAVY_DEEP}" stroke-width="7" stroke-linecap="round"/>'
    )
    return w, h, svg(w, h, defs, body)


# ------------------------------------------------------ 4/5. kademe pipleri
# Discrete levels want discrete marks: five pips beat a bar because "3 of 5 bought"
# is the thing the player is reading.
def pip(filled):
    w = h = 56
    if filled:
        defs = grad("b", BLUE_HI, BLUE_LO) + gloss("g")
        body = (f'<circle cx="28" cy="28" r="20" fill="url(#b)" stroke="{NAVY}" stroke-width="8"/>'
                f'<ellipse cx="28" cy="21" rx="13" ry="8" fill="url(#g)"/>')
    else:
        defs = grad("b", PALE_LO, PALE_SHADOW)
        body = (f'<circle cx="28" cy="28" r="20" fill="url(#b)" stroke="{NAVY}" stroke-width="8"/>'
                f'<ellipse cx="28" cy="34" rx="12" ry="7" fill="{PALE_SHADOW}" opacity="0.55"/>')
    return w, h, svg(w, h, defs, body)


# ------------------------------------------------------------ 6. ikon_kontrat
# Object-plus-gold-accent, the same recipe as ikon_nakit (green note + gold coin).
def ikon_kontrat():
    w = h = 240
    defs = (grad("p", WHITE, "#DDE4F0") + gloss("g") + grad("s", GOLD_HI, GOLD_LO))
    body = (
        f'<path d="M56 34 h96 l32 32 v140 a10 10 0 0 1 -10 10 h-118 a10 10 0 0 1 -10 -10 '
        f'v-162 a10 10 0 0 1 10 -10 z" fill="url(#p)" stroke="{NAVY}" stroke-width="12" '
        f'stroke-linejoin="round"/>'
        f'<path d="M152 34 v32 h32" fill="none" stroke="{NAVY}" stroke-width="12" '
        f'stroke-linejoin="round" stroke-linecap="round"/>'
        f'<path d="M78 96 h84 M78 126 h84 M78 156 h50" stroke="{NAVY_INK}" stroke-width="13" '
        f'stroke-linecap="round"/>'
        # wax seal, overlapping the sheet's lower-right corner
        f'<circle cx="168" cy="176" r="38" fill="url(#s)" stroke="{NAVY}" stroke-width="12"/>'
        f'<path d="M168 156 l6 13 l14 2 l-10 10 l3 14 l-13 -7 l-13 7 l3 -14 l-10 -10 l14 -2 z" '
        f'fill="{NAVY}"/>'
    )
    return w, h, svg(w, h, defs, body)


PIECES = {
    "halat": halat(),
    "rozet_buradasin": rozet_buradasin(),
    "btn_fiyat_elmas": btn_fiyat_elmas(),
    "pip_dolu": pip(True),
    "pip_bos": pip(False),
    "ikon_kontrat": ikon_kontrat(),
}

if __name__ == "__main__":
    for name, (w, h, markup) in PIECES.items():
        (OUT / f"{name}.svg").write_text(markup, encoding="ascii")
        print(f"{name:20s} {w:4d} x {h:4d}")
