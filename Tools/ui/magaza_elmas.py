"""Cards for the store's ELMAS ILE AL section -- the things gems are spent on.

These deliberately do NOT follow the harita set's house style. The shipped pack cards
(gold_*, gems_*) are an older, flat generation: one solid body colour, a same-hue darker
outline, a darker band across the foot, and a recessed near-black slot the layout fills
with TMP. Sampled out of the PNGs rather than guessed:

    gems_80    body #46C42B  band #34A01C  outline #2A8214  slot #151515
    gold_2500  body #2F92DA  band #2477B8  outline #175F98

A new card in that grid has to match THEM or it reads as a different product. So: flat.

The heroes are the game's OWN icons, embedded from Assets/Art/UI/Ikonlar rather than drawn
here. That set already speaks the right language -- navy outline, one bright fill, a pale
highlight -- and a hand-drawn near-copy would be a second, drifting version of art the game
already ships. Hand-drawing is for pieces that do not exist yet; every hero this section
needs already did.

Body colour is chosen for CONTRAST against its own hero, which is why the boost card is not
amber: ikon_hizlandirici is an amber bolt, and an amber bolt on an amber card disappears.

One card serves a whole family. gems_80 and gems_12000 are the same artwork with a different
number over them, so three durations of the same boost need one sprite, not three -- the
duration is the layout's job, not the sprite's.

No <text> nodes: localisation must never be able to reflow a sprite.
"""
import base64
import pathlib

HERE = pathlib.Path(__file__).resolve().parent
OUT = HERE / "svg"
ICONS = HERE.parents[1] / "Assets" / "Art" / "UI" / "Ikonlar"
OUT.mkdir(exist_ok=True)

W, H = 280, 360
SLOT = "#151515"         # the recessed amount slot

# body, outline, foot band -- one row per product
FAMILIES = {
    "hiz":        ("#7059C8", "#46379A", "#5C48B0"),   # amber bolt needs a cool ground
    "nakit":      ("#2F92DA", "#175F98", "#2477B8"),   # the gold pack's own blue, under coins
    "cevrimdisi": ("#1F8A8A", "#116060", "#187474"),   # teal: the hourglass sand reads warm on it
    "reklam":     ("#F0902A", "#B8621A", "#D07A22"),   # the video icon is blue, so: amber
    "gunluk":     ("#D64550", "#9A2A36", "#B93844"),   # gold chest on red
    "yatirimci":  ("#E0A828", "#A87418", "#C89020"),   # gold, because it is the expensive one
}

# Scattered specks, as on the shipped cards. Fixed positions rather than random so a re-run
# produces a byte-identical sprite and Unity does not re-import for nothing.
SPECKS = [(46, 74, 11), (232, 96, 9), (56, 160, 8), (238, 186, 12), (40, 226, 9),
          (228, 250, 8), (86, 40, 7), (196, 52, 10)]


def icon(name):
    """The shipped 240x240 icon, inlined so the SVG stays a single portable file."""
    raw = (ICONS / f"{name}.png").read_bytes()
    return "data:image/png;base64," + base64.b64encode(raw).decode("ascii")


def place(name, cx, cy, size):
    return (f'<image href="{icon(name)}" x="{cx - size / 2:.0f}" y="{cy - size / 2:.0f}" '
            f'width="{size:.0f}" height="{size:.0f}"/>')


def card(family, hero=""):
    body, edge, band = FAMILIES[family]
    specks = "".join(f'<circle cx="{x}" cy="{y}" r="{r}" fill="#000000" opacity="0.07"/>'
                     for x, y, r in SPECKS)
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">'
        f'<rect x="4" y="4" width="272" height="352" rx="26" fill="{edge}"/>'
        f'<rect x="10" y="10" width="260" height="340" rx="21" fill="{body}"/>'
        f'<g clip-path="url(#govde)">{specks}'
        f'<rect x="10" y="296" width="260" height="54" fill="{band}"/></g>'
        f'<defs><clipPath id="govde">'
        f'<rect x="10" y="10" width="260" height="340" rx="21"/>'
        f'</clipPath></defs>'
        f'{hero}'
        f'<rect x="74" y="233" width="132" height="50" rx="25" fill="{SLOT}"/>'
        f'</svg>')


# The cards ship WITHOUT their hero. The hero is a separate Image the layout drops on top,
# because each product gets its own idle motion -- the hourglass turns over, the chest
# rattles, the bolt flickers -- and a hero baked into the background cannot move. Every hero
# but one is a shipped icon used as-is; only the coin cluster has to be composed.

# Three of the one shipped coin rather than one: a single coin on a blue card is exactly
# gold_2500, and two cards that look identical in the same scroll view is a worse problem
# than a slightly busier hero.
COINS = ('<svg xmlns="http://www.w3.org/2000/svg" width="240" height="240" viewBox="0 0 240 240">'
         + place("ikon_altin", 78, 148, 116)
         + place("ikon_altin", 162, 148, 116)
         + place("ikon_altin", 120, 88, 132)
         + '</svg>')

PIECES = {
    "elmas_hiz":        card("hiz"),
    "elmas_nakit":      card("nakit"),
    "elmas_cevrimdisi": card("cevrimdisi"),
    "elmas_reklam":     card("reklam"),
    "elmas_gunluk":     card("gunluk"),
    "elmas_yatirimci":  card("yatirimci"),
    "ikon_sikke_yigin": COINS,
}

if __name__ == "__main__":
    for name, markup in PIECES.items():
        (OUT / f"{name}.svg").write_text(markup, encoding="ascii")
        print(f"{name:20s} {len(markup):7d} B")
    print(f"\n-> {OUT}")
