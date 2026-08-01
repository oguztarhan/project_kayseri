"""Generates the sprites the full-screen island showcase needs.

Same anatomy as the shipped set: navy outline, vertical body gradient light-at-top, a gloss
band across the upper third. ASCII-only SVG, no <text> nodes, so localisation cannot reflow
anything. Two pieces are deliberately colourless -- the medallion disc and the aura are drawn
white so the code can tint them with each island's ore colour and one artboard serves all
eight islands.
"""
import pathlib

OUT = pathlib.Path(__file__).parent / "svg"
OUT.mkdir(exist_ok=True)

NAVY = "#182840"
GOLD_HI, GOLD_LO = "#F8E060", "#D89020"
BLUE_HI, BLUE_LO = "#58A8F0", "#2E7CD8"
PLATE_HI, PLATE_LO = "#2E4068", "#16203A"


def grad(gid, top, bottom):
    return (f'<linearGradient id="{gid}" x1="0" y1="0" x2="0" y2="1">'
            f'<stop offset="0" stop-color="{top}"/>'
            f'<stop offset="1" stop-color="{bottom}"/></linearGradient>')


def gloss(gid, strength=0.55):
    return (f'<linearGradient id="{gid}" x1="0" y1="0" x2="0" y2="1">'
            f'<stop offset="0" stop-color="#FFFFFF" stop-opacity="{strength}"/>'
            f'<stop offset="0.62" stop-color="#FFFFFF" stop-opacity="0.06"/>'
            f'<stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/></linearGradient>')


def svg(w, h, defs, body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
            f'viewBox="0 0 {w} {h}"><defs>{defs}</defs>{body}</svg>')


# ---------------------------------------------------------------- ore emblems
# Metals are an ingot pile, gemstones a cut brilliant, coal a heap of broken rock. Every
# emblem is drawn inside the same 300 box so they read at one size on the medallion.

def bar(cx, y, half, hi, lo, top_hi):
    """One ingot: a front face plus the foreshortened top the light catches."""
    return (
        f'<path d="M{cx - half} {y} L{cx + half} {y} L{cx + half - 13} {y + 44} '
        f'L{cx - half + 13} {y + 44} Z" fill="url(#{lo})" stroke="{NAVY}" '
        f'stroke-width="9" stroke-linejoin="round"/>'
        f'<path d="M{cx - half} {y} L{cx + half} {y} L{cx + half - 11} {y - 21} '
        f'L{cx - half + 11} {y - 21} Z" fill="url(#{top_hi})" stroke="{NAVY}" '
        f'stroke-width="9" stroke-linejoin="round"/>')


def ingots(hi, lo, top):
    defs = grad("f", lo, hi) + grad("t", top, hi) + gloss("g", 0.4)
    body = (bar(101, 214, 58, hi, "f", "t") +
            bar(199, 214, 58, hi, "f", "t") +
            bar(150, 142, 58, hi, "f", "t") +
            '<ellipse cx="150" cy="128" rx="46" ry="14" fill="url(#g)"/>')
    return svg(300, 300, defs, body)


def gem(hi, lo, mid):
    """A brilliant: table, crown facets, then the pavilion running to the point."""
    defs = grad("f", hi, lo) + gloss("g", 0.5)
    outline = "M104 62 L196 62 L242 116 L150 258 L58 116 Z"
    body = (
        f'<path d="{outline}" fill="url(#f)" stroke="{NAVY}" stroke-width="11" '
        f'stroke-linejoin="round"/>'
        f'<path d="M104 62 L196 62 L178 116 L122 116 Z" fill="{hi}" opacity="0.85"/>'
        f'<path d="M104 62 L122 116 L58 116 Z" fill="{mid}" opacity="0.7"/>'
        f'<path d="M196 62 L242 116 L178 116 Z" fill="{lo}" opacity="0.7"/>'
        f'<path d="M122 116 L178 116 L150 258 Z" fill="{hi}" opacity="0.55"/>'
        f'<path d="M178 116 L242 116 L150 258 Z" fill="{lo}" opacity="0.6"/>'
        f'<path d="{outline}" fill="none" stroke="{NAVY}" stroke-width="11" '
        f'stroke-linejoin="round"/>'
        f'<path d="M118 74 L182 74" stroke="#FFFFFF" stroke-opacity="0.7" '
        f'stroke-width="10" stroke-linecap="round"/>')
    return svg(300, 300, defs, body)


def rubble(hi, lo, mid):
    """Broken rock: three chunks, the light one on top so the heap reads as a heap."""
    defs = grad("a", hi, lo) + grad("b", mid, lo) + gloss("g", 0.45)
    body = (
        f'<path d="M148 118 L212 104 L248 158 L206 212 L156 186 Z" fill="url(#b)" '
        f'stroke="{NAVY}" stroke-width="11" stroke-linejoin="round"/>'
        f'<path d="M58 176 L102 122 L164 142 L176 204 L112 232 Z" fill="url(#a)" '
        f'stroke="{NAVY}" stroke-width="11" stroke-linejoin="round"/>'
        f'<path d="M102 122 L164 142 L128 168 Z" fill="{hi}" opacity="0.55"/>'
        f'<path d="M96 68 L146 56 L162 100 L114 116 Z" fill="url(#a)" '
        f'stroke="{NAVY}" stroke-width="11" stroke-linejoin="round"/>'
        f'<ellipse cx="126" cy="80" rx="22" ry="10" fill="url(#g)"/>')
    return svg(300, 300, defs, body)


PIECES = {
    "cevher_komur":  rubble("#6A7080", "#242A38", "#4A5060"),
    "cevher_bakir":  ingots("#F0A050", "#B85820", "#FFCE90"),
    "cevher_demir":  ingots("#B8C6D6", "#68788C", "#E0E8F0"),
    "cevher_gumus":  ingots("#EAF2FA", "#98A8BC", "#FFFFFF"),
    "cevher_altin":  ingots("#F8DC50", "#D08810", "#FFF4A0"),
    "cevher_yakut":  gem("#FF7488", "#A81830", "#E04058"),
    "cevher_zumrut": gem("#68E898", "#0E8850", "#28B870"),
    "cevher_elmas":  gem("#E8FBFF", "#68B8E0", "#A8E0F4"),
}

# ---------------------------------------------------------------- showcase furniture
# Disc and frame share one 620 artboard so a single rect size lines them up in Unity: the
# frame's inner edge lands exactly on the disc's rim, whatever the medallion is scaled to.
PIECES["harita_disk"] = svg(620, 620,
    grad("d", "#FFFFFF", "#C8D6E6") + gloss("g", 0.6),
    '<circle cx="310" cy="310" r="248" fill="url(#d)"/>'
    '<ellipse cx="310" cy="228" rx="180" ry="116" fill="url(#g)"/>')

_studs = "".join(
    f'<circle cx="{310 + round(272 * __import__("math").cos(a * 3.14159265 / 180), 1)}" '
    f'cy="{310 + round(272 * __import__("math").sin(a * 3.14159265 / 180), 1)}" r="15" '
    f'fill="#FFF0B0" stroke="{NAVY}" stroke-width="8"/>'
    for a in range(-90, 270, 45))

PIECES["harita_cerceve"] = svg(620, 620,
    grad("r", GOLD_HI, GOLD_LO),
    f'<circle cx="310" cy="310" r="272" fill="none" stroke="{NAVY}" stroke-width="54"/>'
    f'<circle cx="310" cy="310" r="272" fill="none" stroke="url(#r)" stroke-width="34"/>'
    f'<circle cx="310" cy="310" r="262" fill="none" stroke="#FFFFFF" stroke-opacity="0.35" '
    f'stroke-width="8"/>' + _studs)

# Tinted and pulsed in code; white so one artboard serves all eight ore colours.
PIECES["harita_aura"] = svg(640, 640,
    '<radialGradient id="a"><stop offset="0" stop-color="#FFFFFF" stop-opacity="0.95"/>'
    '<stop offset="0.38" stop-color="#FFFFFF" stop-opacity="0.5"/>'
    '<stop offset="0.66" stop-color="#FFFFFF" stop-opacity="0.15"/>'
    '<stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/></radialGradient>',
    '<circle cx="320" cy="320" r="320" fill="url(#a)"/>')

# Rays that turn behind the medallion. Masked to fade at both the hub and the rim, so the
# square artboard never shows a corner however far it has rotated.
_rays = "".join(
    '<path d="M360 360 L{:.1f} {:.1f} L{:.1f} {:.1f} Z" fill="#FFFFFF"/>'.format(
        360 + 520 * __import__("math").cos((a) * 3.14159265 / 180),
        360 + 520 * __import__("math").sin((a) * 3.14159265 / 180),
        360 + 520 * __import__("math").cos((a + 11) * 3.14159265 / 180),
        360 + 520 * __import__("math").sin((a + 11) * 3.14159265 / 180))
    for a in range(0, 360, 20))

PIECES["harita_isin"] = svg(720, 720,
    '<radialGradient id="m"><stop offset="0" stop-color="#000000"/>'
    '<stop offset="0.24" stop-color="#FFFFFF"/>'
    '<stop offset="0.60" stop-color="#FFFFFF" stop-opacity="0.8"/>'
    '<stop offset="1" stop-color="#000000"/></radialGradient>'
    '<mask id="k"><rect width="720" height="720" fill="url(#m)"/></mask>',
    f'<g mask="url(#k)">{_rays}</g>')

PIECES["parilti"] = svg(96, 96, "",
    '<path d="M48 3 C52 31 65 44 93 48 C65 52 52 65 48 93 C44 65 31 52 3 48 '
    'C31 44 44 31 48 3 Z" fill="#FFFFFF"/>')

# Dark glass plate the name and numbers sit on, over the lit backdrop. 9-sliced in Unity.
PIECES["harita_tabela"] = svg(720, 340,
    grad("p", PLATE_HI, PLATE_LO),
    f'<rect x="12" y="12" width="696" height="316" rx="70" fill="url(#p)" '
    f'stroke="{NAVY}" stroke-width="14"/>'
    f'<rect x="46" y="44" width="628" height="54" rx="27" fill="#FFFFFF" opacity="0.10"/>')

# The set ships a green buy pill but no neutral "sail there" CTA, so this is its blue twin.
PIECES["btn_git"] = svg(700, 216,
    grad("b", BLUE_HI, BLUE_LO),
    '<rect x="10" y="26" width="680" height="180" rx="90" fill="#C4CCDC"/>'
    f'<rect x="10" y="10" width="680" height="180" rx="90" fill="url(#b)" '
    f'stroke="{NAVY}" stroke-width="14"/>'
    '<rect x="58" y="36" width="584" height="44" rx="22" fill="#FFFFFF" opacity="0.24"/>')


# The shipped waiting pill carries an hourglass, which is wrong under "BURADASIN" or a locked
# island, so this is the neutral grey twin of btn_git.
PIECES["btn_pasif"] = svg(700, 216,
    grad("b", "#8494AC", "#5C6C88"),
    '<rect x="10" y="26" width="680" height="180" rx="90" fill="#B8C0D0"/>'
    f'<rect x="10" y="10" width="680" height="180" rx="90" fill="url(#b)" '
    f'stroke="{NAVY}" stroke-width="14"/>'
    '<rect x="58" y="36" width="584" height="44" rx="22" fill="#FFFFFF" opacity="0.16"/>')


# HUD boost shortcut. Shaped to btn_yukselt's shell so the two bottom buttons read as a pair,
# violet so it is not mistaken for the green upgrade or the navy prestige button. The multiplier
# is drawn by the label, not baked in -- the set stays text-free.
PIECES["btn_boost"] = svg(460, 396,
    grad("b", "#B48CF4", "#7B45D8") + grad("s", "#FFE870", "#E0A020"),
    '<rect x="28" y="68" width="404" height="286" rx="78" fill="#C8D0E0"/>'
    f'<rect x="28" y="44" width="404" height="286" rx="78" fill="url(#b)" '
    f'stroke="{NAVY}" stroke-width="18"/>'
    '<rect x="89" y="82" width="283" height="55" rx="28" fill="#FFFFFF" opacity="0.26"/>'
    f'<path d="M242 72 L199 120 L225 120 L216 162 L261 111 L234 111 Z" fill="url(#s)" '
    f'stroke="{NAVY}" stroke-width="16" stroke-linejoin="round"/>'
    # a sunken band across the foot, so the state label reads against the body instead of the bolt
    f'<rect x="60" y="254" width="341" height="62" rx="31" fill="{NAVY}" opacity="0.34"/>')


if __name__ == "__main__":
    for name, markup in PIECES.items():
        (OUT / f"{name}.svg").write_text(markup, encoding="ascii")
        print(f"{name:18s} {len(markup):5d} B")
