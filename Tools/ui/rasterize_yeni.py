"""SVG -> transparent PNG at 1x via headless Chrome.

No rasteriser library is installed, but Chrome is. A standalone .svg document picks up
Chrome's centring and bakes stray padding into the sprite, so each SVG is inlined into a
zero-margin HTML page sized exactly to the artboard.
"""
import pathlib
import subprocess
import sys

from yeni_parcalar import PIECES

HERE = pathlib.Path(__file__).parent
PNG = HERE / "png"
PNG.mkdir(exist_ok=True)
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"


def rasterize(name, w, h, markup):
    page = HERE / f"_{name}.html"
    page.write_text(
        "<html><head><meta charset='utf-8'><style>"
        "html,body{margin:0;padding:0;background:transparent;overflow:hidden}"
        "svg{display:block}</style></head><body>" + markup + "</body></html>",
        encoding="utf-8")
    out = PNG / f"{name}.png"
    subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                    "--default-background-color=00000000",
                    "--force-device-scale-factor=1",
                    f"--window-size={w},{h}",
                    f"--screenshot={out}", page.as_uri()],
                   check=True, capture_output=True)
    page.unlink()
    return out


if __name__ == "__main__":
    for name, (w, h, markup) in PIECES.items():
        out = rasterize(name, w, h, markup)
        print(f"{name:20s} {w:4d} x {h:4d}  {out.stat().st_size:6d} B")
    sys.exit(0)
