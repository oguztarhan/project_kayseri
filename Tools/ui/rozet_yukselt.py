"""The "this one can be upgraded" badge that floats over a station.

    python Tools/ui/rozet_yukselt.py

A blue rounded square with a white arrow in it, per the reference. Dressed in the house style so
it sits with the rest of the UI rather than on top of it: the navy #182840 outline every button
and pill in this game carries, a vertical gradient rather than a flat fill, and the white gloss
band across the upper third.

The arrow is drawn as one filled path instead of a triangle plus a rectangle. Two shapes leave a
hairline seam where they meet once the sprite is scaled down to the ~90px it actually occupies
on a phone, and at that size the seam is the only thing you notice.
"""
import pathlib
import re
import subprocess
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
DEST = HERE.parents[1] / "Assets" / "Art" / "UI" / "Ikonlar"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"

KENAR = "#182840"      # the house outline
UST = "#63CDF7"        # gradient top
ALT = "#2A8FD6"        # gradient bottom

# Up arrow: head corners, then down the stem and back. One closed path.
OK = ("M 120,58 L 182,126 L 150,126 L 150,182 L 90,182 L 90,126 L 58,126 Z")

ROZET = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="240" height="240" viewBox="0 0 240 240">'
    '<defs>'
    f'<linearGradient id="govde" x1="0" y1="0" x2="0" y2="1">'
    f'<stop offset="0" stop-color="{UST}"/><stop offset="1" stop-color="{ALT}"/>'
    f'</linearGradient>'
    '<clipPath id="kutu"><rect x="22" y="22" width="196" height="196" rx="48"/></clipPath>'
    '</defs>'
    f'<rect x="22" y="22" width="196" height="196" rx="48" fill="url(#govde)"/>'
    # Gloss, clipped to the badge so it cannot spill over the rounded corners.
    '<g clip-path="url(#kutu)">'
    '<rect x="38" y="38" width="164" height="42" rx="21" fill="#FFFFFF" opacity="0.22"/>'
    '</g>'
    # Arrow: a dark under-copy one pixel down gives it an edge against the lighter top of the
    # gradient without an outline of its own, which at this size would close up the arrow's notch.
    f'<path d="{OK}" fill="{KENAR}" opacity="0.28" transform="translate(0,5)"/>'
    f'<path d="{OK}" fill="#FFFFFF"/>'
    f'<rect x="22" y="22" width="196" height="196" rx="48" fill="none" '
    f'stroke="{KENAR}" stroke-width="13"/>'
    '</svg>')

PIECES = {"rozet_yukselt": ROZET}

if __name__ == "__main__":
    DEST.mkdir(parents=True, exist_ok=True)
    work = pathlib.Path(tempfile.mkdtemp(prefix="kayseri_rozet_"))
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
