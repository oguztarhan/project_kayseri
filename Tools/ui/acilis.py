"""Loading-screen backdrops, one per island.

Every island shares ONE composition and differs only by palette. That is not laziness -- the
splash has to read as the same game eight times over, and the thing the player should notice
changing is the ore, not the layout. So the geometry lives in `scene()` and everything that
makes an island itself lives in `PALETTES`.

Coal's reading: the whole scene is cold -- slate, charcoal, deep blue night -- and every warm
pixel comes from a fire the player lit. The mine mouth, the lanterns, the embers. That contrast
is what makes charcoal read as COAL rather than as generic grey rock, and it gives the gold in
the logo something to belong to.

Drawn in the same flat low-poly language as the 3D island: two-tone faceted peaks, hard seams,
no shading inside a facet. Rendered by rasterize_logo.py.

Layout is portrait 1080x2400 against a 1080x2340 canvas reference, and everything that matters
sits inside the middle 900 px so a wide phone can crop the sides. The top third is deliberately
quiet: the logo goes there, and the bottom 300 px stay simple for the progress bar.
"""
import math
import pathlib
import random

OUT = pathlib.Path(__file__).parent / "svg"
OUT.mkdir(exist_ok=True)

W, H = 1080, 2400
HORIZON = 1180
CX = W // 2
SEED = 20260801

def _rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


def _hex(c):
    return "#" + "".join(f"{max(0, min(255, int(round(v)))):02X}" for v in c)


def mix(a, b, t):
    ra, rb = _rgb(a), _rgb(b)
    return _hex(tuple(ra[i] + (rb[i] - ra[i]) * t for i in range(3)))


def shade(a, k):
    return _hex(tuple(v * k for v in _rgb(a)))


# One ocean, so one sea colour -- only nudged toward each island's own sky so the horizon does
# not have a seam in it. Eight different seas would read as eight different games.
SEA = "#164067"

# The lamps stay warm on every island. They are oil lamps, not ore, and keeping one constant
# warm note is what stops the cold islands (silver, diamond) from going monochrome.
WARM, WARMLIT = "#FF9A2E", "#FFD57A"


def island(sky, halo, rock, ground, adit, ore, timber="#3A2818"):
    """Derives a full palette from six decisions.

    Every ridgeline, the cap and the rubble come off `rock` and `ore` by the same ratios, so all
    eight islands keep identical depth separation and the only thing that actually moves between
    them is hue. Hand-mixing sixteen colours per island would have drifted by the third one.

    `adit` is the light inside the mine mouth, and it is the strongest identity signal in the
    frame: coal and iron burn (fire orange), the gem islands glow with their own stone."""
    return dict(
        sky=sky,
        halo=halo,
        sea=(mix(SEA, sky[2], 0.28), shade(mix(SEA, sky[2], 0.28), 0.5)),
        far=(mix(rock, sky[2], 0.30), mix(rock, sky[2], 0.48)),
        mid=(rock, shade(rock, 0.68)),
        near=(shade(rock, 0.70), shade(rock, 0.42)),
        # coal's hand-mixed cap sits ~10% off its rock; at 28% the lit facets bleached
        # the whole massif and the mountains read as haze instead of stone
        cap=mix(rock, "#FFFFFF", 0.13),
        ground=ground,
        rock=shade(rock, 0.34),
        timber=timber,
        # the heaps are WASTE. Taken near the ore's own value they came out as bright coloured
        # pyramids -- gold tents, red tents -- reading as decoration rather than as slag
        vein=shade(ore, 0.26),
        warm=WARM, warmlit=WARMLIT,
        glow=adit[0], glowlit=adit[1],
        ore=shade(ore, 0.48),
        orelit=mix(ore, "#FFFFFF", 0.18),
    )


PALETTES = {
    # Coal keeps its hand-mixed values: it was tuned by eye across four passes and approved, and
    # re-deriving it from the factory would move colours nobody asked to move.
    "komur": dict(
        sky=("#08111F", "#102340", "#1B3C60"),
        halo="#3C6D9E",
        sea=("#164067", "#0A2038"),
        far=("#36455C", "#29364C"),
        mid=("#39404E", "#262C38"),
        near=("#282C36", "#161920"),
        cap="#4A566E",
        # olive-slate, not meadow: a coal island's ground has coal ground into it
        ground=("#17271D", "#09110C"),
        rock="#12151B",
        timber="#3A2818",
        vein="#0A0C11",
        warm=WARM, warmlit=WARMLIT,
        glow="#FF9A2E", glowlit="#FFD57A",
        ore="#1B1F26",
        orelit="#3A455C",
    ),
    "bakir": island(("#0C0B14", "#221A22", "#40302C"), "#8A6248", "#4A382C",
                    ("#2A2418", "#12100A"), ("#F08830", "#FFD8A0"), "#B87333"),
    "demir": island(("#0A101C", "#131F32", "#22364E"), "#4A6480", "#3E434C",
                    ("#20241E", "#0C0F0B"), ("#E0621C", "#FFCE96"), "#7A4A32"),
    "gumus": island(("#0A1220", "#152438", "#2A3F58"), "#5C7C9E", "#464C58",
                    ("#1C2228", "#0A0E12"), ("#8CC8E8", "#EAFBFF"), "#B6BECC"),
    "altin": island(("#0E0B14", "#221925", "#3A2C30"), "#9A7048", "#4C4436",
                    ("#2C2616", "#12100A"), ("#F0A818", "#FFF0B4"), "#C8962A"),
    "yakut": island(("#10080F", "#240D1C", "#3C1628"), "#84364E", "#443036",
                    ("#241820", "#0E0A0E"), ("#E0243C", "#FFB0B8"), "#A0202E"),
    "zumrut": island(("#06120E", "#0D241E", "#153A30"), "#367C64", "#2E4038",
                     ("#16281C", "#080F0A"), ("#22B058", "#BCFFD2"), "#178A3E"),
    "elmas": island(("#080F1E", "#122438", "#1F4054"), "#5A8CB4", "#404A58",
                    ("#18222A", "#080D12"), ("#7ADCF4", "#EEFDFF"), "#8FC8D8"),
}


# ---------------------------------------------------------------- gradients

def lin(gid, top, bottom, y1, y2):
    return (f'<linearGradient id="{gid}" gradientUnits="userSpaceOnUse" x1="0" y1="{y1}" '
            f'x2="0" y2="{y2}"><stop offset="0" stop-color="{top}"/>'
            f'<stop offset="1" stop-color="{bottom}"/></linearGradient>')


def lin3(gid, a, b, c, y1, y2):
    return (f'<linearGradient id="{gid}" gradientUnits="userSpaceOnUse" x1="0" y1="{y1}" '
            f'x2="0" y2="{y2}"><stop offset="0" stop-color="{a}"/>'
            f'<stop offset="0.58" stop-color="{b}"/>'
            f'<stop offset="1" stop-color="{c}"/></linearGradient>')


def glow(gid, cx, cy, r, inner, outer):
    return (f'<radialGradient id="{gid}" gradientUnits="userSpaceOnUse" cx="{cx}" cy="{cy}" '
            f'r="{r}"><stop offset="0" stop-color="{inner}" stop-opacity="0.95"/>'
            f'<stop offset="0.42" stop-color="{outer}" stop-opacity="0.42"/>'
            f'<stop offset="1" stop-color="{outer}" stop-opacity="0"/></radialGradient>')


# ---------------------------------------------------------------- terrain

def peak(cx, base, half, top, lit, shade, cap=None, tilt=0.25):
    """One faceted mountain: a lit face and a shade face meeting at the ridge, plus an optional
    lighter cap. Two flat tones and a hard seam is the whole low-poly trick -- shade the faces
    smoothly and it stops matching the 3D island it is standing in for."""
    ax = cx + half * tilt
    out = (f'<path d="M{cx - half:.0f} {base} L{ax:.0f} {top:.0f} L{cx:.0f} {base} Z" '
           f'fill="{lit}"/>'
           f'<path d="M{cx:.0f} {base} L{ax:.0f} {top:.0f} L{cx + half:.0f} {base} Z" '
           f'fill="{shade}"/>')
    if cap:
        k = 0.32
        bl = (cx - half) + (ax - (cx - half)) * (1 - k)
        br = (cx + half) + (ax - (cx + half)) * (1 - k)
        by = base + (top - base) * (1 - k)
        out += (f'<path d="M{bl:.0f} {by:.0f} L{ax:.0f} {top:.0f} L{ax:.0f} {by:.0f} Z" '
                f'fill="{cap}"/>'
                f'<path d="M{ax:.0f} {by:.0f} L{ax:.0f} {top:.0f} L{br:.0f} {by:.0f} Z" '
                f'fill="{cap}" opacity="0.5"/>')
    return out


def ridgeline(rng, base, y_lo, y_hi, count, half_lo, half_hi, lit, shade, cap=None, pad=120):
    """A row of peaks marching across the frame. Halves overlap on purpose: spaced apart they
    read as a row of tents rather than as one massif."""
    span = (W + pad * 2) / count
    return "".join(
        peak(-pad + span * (i + 0.5) + rng.uniform(-span * 0.2, span * 0.2), base,
             rng.uniform(half_lo, half_hi), rng.uniform(y_lo, y_hi),
             lit, shade, cap, rng.uniform(-0.34, 0.34))
        for i in range(count))


def islets(rng, p):
    """Three humps on the horizon. The game is an archipelago and the splash should say so
    before the player has ever opened the map."""
    out = []
    for x, w, h in ((150, 190, 46), (872, 150, 34), (640, 96, 22)):
        out.append(f'<path d="M{x - w} {HORIZON} Q{x - w * 0.45} {HORIZON - h * 1.7} {x} '
                   f'{HORIZON - h} Q{x + w * 0.5} {HORIZON - h * 1.5} {x + w} {HORIZON} Z" '
                   f'fill="{p["far"][1]}" opacity="0.62"/>')
    return "".join(out)


def land(p):
    """The island shelf the whole foreground stands on. Runs off both edges so a crop never
    exposes an end of it."""
    return (f'<path d="M-40 2400 L-40 1742 C160 1690 300 1664 {CX} 1660 '
            f'C790 1664 940 1692 1120 1748 L1120 2400 Z" fill="url(#ground)"/>'
            f'<path d="M-40 1742 C160 1690 300 1664 {CX} 1660 C790 1664 940 1692 1120 1748 '
            f'L1120 1790 C940 1734 790 1706 {CX} 1702 C300 1706 160 1732 -40 1784 Z" '
            f'fill="{p["cap"]}" opacity="0.20"/>')


# ---------------------------------------------------------------- props

def mine(cx, base, p):
    """The mine mouth: a black arch with fire behind it, a timber frame, and a shaft of light
    thrown down the slope. It is the only thing in the frame the eye is meant to go to after
    the logo, so it gets the warmest pixel in the image."""
    half, rise = 118, 150
    arch = (f'M{cx - half} {base} L{cx - half} {base - rise * 0.55:.0f} '
            f'Q{cx} {base - rise * 1.5:.0f} {cx + half} {base - rise * 0.55:.0f} '
            f'L{cx + half} {base} Z')
    inner = (f'M{cx - half + 26} {base} L{cx - half + 26} {base - rise * 0.5:.0f} '
             f'Q{cx} {base - rise * 1.28:.0f} {cx + half - 26} {base - rise * 0.5:.0f} '
             f'L{cx + half - 26} {base} Z')
    return (
        # the light on the ground in front of the mouth, laid down before the frame
        f'<path d="M{cx - half - 10} {base} L{cx + half + 10} {base} L{cx + 300} {base + 330} '
        f'L{cx - 300} {base + 330} Z" fill="url(#mouth)" opacity="0.17"/>'
        f'<path d="{arch}" fill="{p["rock"]}"/>'
        f'<path d="{inner}" fill="url(#mouth)"/>'
        # timber frame
        f'<rect x="{cx - half - 20}" y="{base - 178}" width="30" height="178" '
        f'fill="{p["timber"]}"/>'
        f'<rect x="{cx + half - 10}" y="{base - 178}" width="30" height="178" '
        f'fill="{p["timber"]}"/>'
        f'<rect x="{cx - half - 40}" y="{base - 206}" width="{half * 2 + 80}" height="34" '
        f'rx="6" fill="{p["timber"]}"/>'
        f'<rect x="{cx - half - 40}" y="{base - 206}" width="{half * 2 + 80}" height="11" '
        f'rx="5" fill="#6A4C2E"/>')


def rails(cx, y_top, half_top, y_bot, half_bot, p, n=13):
    """Sleepers bunched toward the top and rails converging with them. Even spacing would read
    as a ladder painted on the hill instead of as track running away from the viewer."""
    out = []
    for i in range(n):
        t = (i / (n - 1)) ** 1.5
        y = y_top + (y_bot - y_top) * t
        half = half_top + (half_bot - half_top) * t
        h = 7 + 17 * t
        out.append(f'<rect x="{cx - half * 1.18:.0f}" y="{y:.0f}" '
                   f'width="{half * 2.36:.0f}" height="{h:.0f}" rx="{h / 2:.0f}" '
                   f'fill="{p["timber"]}"/>')
    for s in (-1, 1):
        w_top, w_bot = 9, 22
        out.append(
            f'<path d="M{cx + s * half_top - w_top:.0f} {y_top} '
            f'L{cx + s * half_top + w_top:.0f} {y_top} '
            f'L{cx + s * half_bot + w_bot:.0f} {y_bot} '
            f'L{cx + s * half_bot - w_bot:.0f} {y_bot} Z" fill="{p["cap"]}" opacity="0.55"/>')
    return "".join(out)


def headframe(x, y, p, scale=1.0):
    """A pithead winding tower. One silhouette does more to say COAL MINE than any amount of
    grey rock does, and it breaks the symmetry the centred adit would otherwise leave."""
    g = (f'<path d="M-78 0 L-30 -250 L-14 -250 L-52 0 Z" fill="{p["vein"]}"/>'
         f'<path d="M78 0 L30 -250 L14 -250 L52 0 Z" fill="{p["vein"]}"/>'
         f'<path d="M-58 -74 L58 -74 L58 -60 L-58 -60 Z" fill="{p["vein"]}"/>'
         f'<path d="M-44 -150 L44 -150 L44 -137 L-44 -137 Z" fill="{p["vein"]}"/>'
         f'<path d="M-56 -70 L44 -145 L50 -134 L-50 -59 Z" fill="{p["vein"]}" opacity="0.85"/>'
         # the sheave wheel, the one shape everyone recognises
         f'<circle cx="0" cy="-284" r="46" fill="none" stroke="{p["vein"]}" stroke-width="17"/>'
         f'<circle cx="0" cy="-284" r="11" fill="{p["vein"]}"/>'
         f'<path d="M-30 -250 L30 -250 L22 -276 L-22 -276 Z" fill="{p["vein"]}"/>'
         # hoist rope running down into the shaft house
         f'<rect x="-2" y="-284" width="4" height="230" fill="{p["vein"]}"/>'
         f'<path d="M-104 0 L-104 -66 L-16 -66 L-16 0 Z" fill="{p["vein"]}"/>'
         f'<path d="M-104 -66 L-16 -66 L-24 -84 L-96 -84 Z" fill="{p["near"][0]}"/>'
         f'<rect x="-84" y="-52" width="34" height="30" rx="4" fill="{p["warm"]}"/>'
         f'<circle cx="0" cy="-318" r="9" fill="{p["warmlit"]}"/>')
    return f'<g transform="translate({x} {y}) scale({scale})">{g}</g>'


def spoil(x, base, half, top, p, flip=1):
    """A waste tip. Black seams painted across the slopes were the first attempt and they read
    as loose planks lying on the mountain, not as coal in it. A spoil heap is the shape a coal
    mine actually leaves on a hillside, and being the darkest object in frame it lands at once."""
    apex = x + half * 0.22 * flip
    return (f'<path d="M{x - half:.0f} {base} L{apex:.0f} {top} L{x:.0f} {base} Z" '
            f'fill="{p["ore"]}"/>'
            f'<path d="M{x:.0f} {base} L{apex:.0f} {top} L{x + half:.0f} {base} Z" '
            f'fill="{p["vein"]}"/>'
            f'<path d="M{x - half * 0.36:.0f} {base} L{apex:.0f} {top} '
            f'L{x - half * 0.08:.0f} {base} Z" fill="{p["orelit"]}" opacity="0.14"/>')


def rubble(rng, p, n=9):
    """Loose coal along the trackside. Small, dark, and the reason the ground does not read as
    a lawn with rails on it."""
    out = []
    for _ in range(n):
        x = rng.choice((rng.uniform(30, 300), rng.uniform(780, 1050)))
        y = rng.uniform(1830, 2110)
        r = rng.uniform(9, 22)
        out.append(f'<path d="M{x - r:.0f} {y + r * 0.5:.0f} L{x - r * 0.4:.0f} {y - r:.0f} '
                   f'L{x + r * 0.7:.0f} {y - r * 0.7:.0f} L{x + r:.0f} {y + r * 0.4:.0f} Z" '
                   f'fill="{p["ore"]}"/>'
                   f'<path d="M{x - r * 0.4:.0f} {y - r:.0f} L{x + r * 0.7:.0f} '
                   f'{y - r * 0.7:.0f} L{x:.0f} {y - r * 0.1:.0f} Z" fill="{p["near"][0]}" '
                   f'opacity="0.7"/>')
    return "".join(out)


def lantern(x, y, p, scale=1.0):
    """A post lamp. Its glow is the reason the foreground has any warmth at all."""
    g = (f'<rect x="-8" y="0" width="16" height="168" rx="8" fill="{p["rock"]}"/>'
         f'<rect x="-40" y="-4" width="80" height="14" rx="7" fill="{p["rock"]}"/>'
         f'<path d="M-32 -4 L32 -4 L23 -78 L-23 -78 Z" fill="{p["warm"]}"/>'
         f'<path d="M-23 -78 L23 -78 L17 -102 L-17 -102 Z" fill="{p["rock"]}"/>'
         f'<circle cx="0" cy="-44" r="14" fill="{p["warmlit"]}"/>')
    return (f'<g transform="translate({x} {y}) scale({scale})">'
            f'<circle cx="0" cy="-44" r="150" fill="url(#lamp)" opacity="0.55"/>{g}</g>')


# (depth, half-gauge). The foreground gauge started at 330 -- a 660 px track on a 1080 px
# screen -- and anything sitting honestly on it then had to be a third of the frame wide.
RAIL_TOP, RAIL_BOT = (1664, 62), (2180, 208)


def foreground(p):
    """A dark ledge across the bottom. It frames the scene, hides where the rails stop, and
    gives the progress bar something solid to sit on."""
    return (f'<path d="M-40 2400 L-40 2172 L110 2118 L268 2166 L430 2104 L560 2148 '
            f'L720 2096 L880 2150 L1010 2110 L1120 2158 L1120 2400 Z" fill="{p["rock"]}"/>'
            f'<path d="M-40 2172 L110 2118 L268 2166 L430 2104 L560 2148 L720 2096 '
            f'L880 2150 L1010 2110 L1120 2158 L1120 2186 L1010 2138 L880 2178 L720 2124 '
            f'L560 2176 L430 2132 L268 2194 L110 2146 L-40 2200 Z" fill="{p["orelit"]}" '
            f'opacity="0.24"/>')


def stars(rng, n, y_lo, y_hi):
    out = []
    for _ in range(n):
        x, y = rng.uniform(20, W - 20), rng.uniform(y_lo, y_hi)
        fade = 1.0 - (y - y_lo) / max(1.0, y_hi - y_lo) * 0.72
        out.append(f'<circle cx="{x:.0f}" cy="{y:.0f}" r="{rng.choice((1.6, 2.0, 2.5, 3.1))}" '
                   f'fill="#DCEAFF" opacity="{rng.uniform(0.3, 0.9) * fade:.2f}"/>')
    return "".join(out)


def embers(rng, n, cx, cy, spread, p):
    """Sparks drifting off the mine mouth -- the only thing in a still image that suggests
    something is happening in there."""
    out = []
    for _ in range(n):
        a = rng.uniform(0, math.tau)
        d = rng.uniform(0.12, 1.0) ** 0.6 * spread
        x = cx + math.cos(a) * d * 1.3
        y = cy - abs(math.sin(a)) * d - rng.uniform(0, 150)
        out.append(f'<circle cx="{x:.0f}" cy="{y:.0f}" r="{rng.uniform(2.2, 6.0):.1f}" '
                   f'fill="{p["warmlit"]}" opacity="{rng.uniform(0.2, 0.75):.2f}"/>')
    return "".join(out)


# ---------------------------------------------------------------- assembly

def build(key):
    p = PALETTES[key]
    rng = random.Random(SEED)

    defs = (
        lin3("sky", p["sky"][0], p["sky"][1], p["sky"][2], 0, HORIZON)
        + lin("sea", p["sea"][0], p["sea"][1], HORIZON, 1760)
        + lin("ground", p["ground"][0], p["ground"][1], 1660, 2280)
        # painted into a full-width rect, never into a shape of its own: an ellipse filled with
        # a circular ramp shows its own rim wherever the ramp has not yet reached zero, and that
        # arc across the sky was the loudest artefact in the first pass
        + glow("halo", CX, HORIZON - 30, 880, p["halo"], p["halo"])
        + glow("mouth", CX, 1596, 330, p["glowlit"], p["glow"])
        + glow("lamp", 0, -44, 150, p["warmlit"], p["warm"])
        + f'<radialGradient id="vignette" gradientUnits="userSpaceOnUse" cx="{CX}" cy="1240" '
          f'r="1460"><stop offset="0.42" stop-color="#00060E" stop-opacity="0"/>'
          f'<stop offset="1" stop-color="#00060E" stop-opacity="0.80"/></radialGradient>'
    )

    sea_glints = "".join(
        f'<rect x="{rng.uniform(-40, W):.0f}" y="{rng.uniform(HORIZON + 40, 1640):.0f}" '
        f'width="{rng.uniform(60, 200):.0f}" height="7" rx="3.5" fill="#8FC0E4" '
        f'opacity="{rng.uniform(0.05, 0.15):.2f}"/>' for _ in range(24))

    body = (
        f'<rect width="{W}" height="{HORIZON}" fill="url(#sky)"/>'
        + stars(rng, 95, 40, HORIZON - 130)
        + f'<rect y="240" width="{W}" height="{HORIZON - 240}" fill="url(#halo)" '
          f'opacity="0.55"/>'
        + f'<rect y="{HORIZON}" width="{W}" height="{1760 - HORIZON}" fill="url(#sea)"/>'
        + sea_glints
        + islets(rng, p)
        + ridgeline(rng, 1258, 984, 1108, 7, 150, 262, p["far"][0], p["far"][1])
        + ridgeline(rng, 1444, 1030, 1196, 6, 196, 322, p["mid"][0], p["mid"][1], p["cap"])
        + ridgeline(rng, 1668, 1146, 1338, 5, 236, 384, p["near"][0], p["near"][1], p["cap"])
        + land(p)
        + spoil(214, 1706, 176, 1508, p, -1)
        + spoil(944, 1720, 130, 1580, p, 1)
        + headframe(806, 1700, p, 0.92)
        + mine(CX, 1664, p)
        # tight to the mouth: scattered wide they read as gold dust over the mountains rather
        # than as sparks coming out of the adit
        + embers(rng, 20, CX, 1560, 110, p)
        + rails(CX, RAIL_TOP[0], RAIL_TOP[1], RAIL_BOT[0], RAIL_BOT[1], p)
        + rubble(rng, p)
        + lantern(196, 1868, p, 1.05)
        + lantern(884, 1900, p, 0.92)
        + foreground(p)
    )
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" '
            f'viewBox="0 0 {W} {H}"><defs>{defs}</defs>{body}'
            f'<rect width="{W}" height="{H}" fill="url(#vignette)"/></svg>')


PIECES = {f"acilis_{k}": build(k) for k in PALETTES}

if __name__ == "__main__":
    for name, markup in PIECES.items():
        (OUT / f"{name}.svg").write_text(markup, encoding="utf-8")
        print(f"{name:18s} {len(markup):7d} B")
