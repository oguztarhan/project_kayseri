"""SVG -> transparent PNG via headless Chrome, straight into Assets/Art/UI/Magaza.

    python Tools/ui/rasterize_elmas.py

Same trick as rasterize_harita.py: no rasteriser library is installed but Chrome is, and a
standalone .svg document picks up Chrome's centring and bakes stray padding into the sprite,
so each piece is inlined into a zero-margin HTML page sized exactly to its artboard.

These land beside the pack cards they have to match. Import settings live on the .meta Unity
generates, so a brand new card needs its sprite mode set once -- the pack cards are
Sprite/Single, ppu 100, no 9-slice.
"""
import pathlib
import re
import subprocess
import tempfile

from magaza_elmas import PIECES

HERE = pathlib.Path(__file__).resolve().parent
DEST = HERE.parents[1] / "Assets" / "Art" / "UI" / "Magaza"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"

DEST.mkdir(parents=True, exist_ok=True)
work = pathlib.Path(tempfile.mkdtemp(prefix="kayseri_elmas_"))
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
                    f"--screenshot={out}", page.as_uri()],
                   check=True, capture_output=True)
    print(f"{name:20s} {w:4d} x {h:4d}  {out.stat().st_size:7d} B")
print(f"\n-> {DEST}")
