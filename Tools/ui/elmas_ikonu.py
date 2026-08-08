"""The store's gem, on its own.

    python Tools/ui/elmas_ikonu.py

The ELMAS ILE AL cards price things in gems and put a gem beside the number. That slot used to
hold ikon_elmas -- the navy-outlined pale blue one from the HUD -- while every card behind it
carries the store's own gem: black outline, saturated cyan, brighter crown. Two different gems
for the same currency, side by side in one panel.

The store gem existed only baked into the gems_* pack cards, which are 280x360 sprites with a
green background and an amount slot, so there was nothing to point the icon field at. This
redraws it standalone. Geometry and colour are not invented: both were sampled straight out of
gems_80.png -- silhouette 144x134 with a 44-wide table on top and the girdle 48 rows down,
body #00C5C7, crown #38E7E9, the cooler strip under the table #07C4D5, outline pure black.

Scaled 1.343x into a 240x240 artboard so it matches the other Ikonlar sprites.
"""
import pathlib
import re
import subprocess
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
DEST = HERE.parents[1] / "Assets" / "Art" / "UI" / "Ikonlar"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"

GOVDE = "#00C5C7"      # pavilion and lower body
KRON = "#38E7E9"       # the bright band across the crown
TEPE = "#07C4D5"       # cooler strip directly under the table
FACET = "#00A7BE"      # facet edges, sampled from the same card
PARLAK = "#B8F4F2"     # gloss

# Five points, not eleven. The sampled silhouette curves gently through the shoulders, and the
# first pass followed it with four short steps a side -- but a 12px round join on vertices that
# close together swallows every corner, and the stone came out as a blob. At the size this is
# actually seen (48px beside a price) the cut has to be stated in as few straight edges as
# possible: table across the top, one crown edge down to the girdle, one pavilion edge to the
# point. Round joins on five corners give the chunky finish the rest of the UI has.
SILUET = "M 92,30 L 148,30 L 214,96 L 120,210 L 26,96 Z"

ELMAS = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="240" height="240" viewBox="0 0 240 240">'
    '<defs>'
    f'<clipPath id="tas"><path d="{SILUET}"/></clipPath>'
    '</defs>'
    # Body first, then the two brighter bands clipped to the stone.
    f'<path d="{SILUET}" fill="{GOVDE}"/>'
    f'<g clip-path="url(#tas)">'
    f'<rect x="0" y="0" width="240" height="100" fill="{KRON}"/>'
    f'<rect x="0" y="0" width="240" height="62" fill="{TEPE}"/>'
    # The girdle: the widest line of the stone, and the one edge that has to read at 48px.
    f'<rect x="0" y="94" width="240" height="7" fill="{FACET}" opacity="0.55"/>'
    # The table edge, so the flat top reads as a facet rather than as a cropped stone.
    f'<rect x="0" y="59" width="240" height="5" fill="{FACET}" opacity="0.38"/>'
    # Two facet edges running from the table corners down to the point, which is what makes it
    # a cut stone rather than a coloured lozenge.
    f'<path d="M 92,30 L 124,210 L 116,210 Z" fill="{FACET}" opacity="0.32"/>'
    f'<path d="M 148,30 L 124,210 L 116,210 Z" fill="{FACET}" opacity="0.32"/>'
    # Gloss, upper left, the house highlight.
    f'<path d="M 62,86 L 100,44 L 122,44 L 78,92 Z" fill="{PARLAK}" opacity="0.82"/>'
    f'</g>'
    f'<path d="{SILUET}" fill="none" stroke="#000000" stroke-width="12" '
    f'stroke-linejoin="round" stroke-linecap="round"/>'
    '</svg>')

PIECES = {"ikon_elmas_magaza": ELMAS}

if __name__ == "__main__":
    DEST.mkdir(parents=True, exist_ok=True)
    work = pathlib.Path(tempfile.mkdtemp(prefix="kayseri_elmas_ikon_"))
    for name, markup in PIECES.items():
        w = int(re.search(r'width="(\d+)"', markup).group(1))
        h = int(re.search(r'height="(\d+)"', markup).group(1))
        page = work / f"{name}.html"
        page.write_text(
            "<html><head><meta charset='utf-8'><style>"
            "html,body{margin:0;padding:0;background:transparent;overflow:hidden}"
            "svg{display:block}</style></head><body>" + markup + "</body></html>",
            encoding="utf-8")
        out = DEST / f"{name}.png"
        subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                        "--default-background-color=00000000",
                        "--force-device-scale-factor=1",
                        f"--window-size={w},{h}",
                        f"--screenshot={out}", page.as_uri()], check=True)
        print(f"{name}.png  {w}x{h}  ->  {out}")
