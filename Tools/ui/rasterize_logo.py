"""Logo/splash SVG -> transparent PNG, via headless Chrome with the project's own font.

Separate from rasterize_harita.py for one reason: these pieces carry <text>, so the page has to
declare an @font-face pointing at Baloo2-ExtraBold.ttf inside the repo. Chrome will not read a
font across a file:// origin without --allow-file-access-from-files.

    python Tools/ui/rasterize_logo.py            # everything
    python Tools/ui/rasterize_logo.py logo_oyun  # one piece

Output lands in Assets/Art/UI/Acilis/. Import settings are not written here -- a brand new piece
needs its sprite mode set once in Unity.
"""
import pathlib
import re
import subprocess
import sys
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
DEST = ROOT / "Assets" / "Art" / "UI" / "Acilis"
FONT = ROOT / "Assets" / "Art" / "Fonts" / "Baloo2-ExtraBold.ttf"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"


def collect():
    pieces = {}
    from logo import PIECES as logo_pieces
    pieces.update(logo_pieces)
    try:
        from acilis import PIECES as splash_pieces
    except ImportError:
        splash_pieces = {}
    pieces.update(splash_pieces)
    return pieces


def render(name, markup, work):
    w = int(re.search(r'width="(\d+)"', markup).group(1))
    h = int(re.search(r'height="(\d+)"', markup).group(1))
    page = work / f"{name}.html"
    page.write_text(
        "<html><head><meta charset='utf-8'><style>"
        f"@font-face{{font-family:'Baloo2';src:url('{FONT.as_uri()}') format('truetype');"
        "font-weight:100 900;font-display:block}"
        "html,body{margin:0;padding:0;background:transparent;overflow:hidden}"
        "svg{display:block}</style></head><body>" + markup + "</body></html>",
        encoding="utf-8")
    out = DEST / f"{name}.png"
    subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                    "--allow-file-access-from-files",
                    "--default-background-color=00000000",
                    "--force-device-scale-factor=1",
                    "--virtual-time-budget=4000",
                    f"--window-size={w},{h}",
                    f"--screenshot={out}", page.as_uri()],
                   check=True, capture_output=True)
    print(f"{name:18s} {w:5d} x {h:4d}  {out.stat().st_size:8d} B")


if __name__ == "__main__":
    DEST.mkdir(parents=True, exist_ok=True)
    work = pathlib.Path(tempfile.mkdtemp(prefix="kayseri_logo_"))
    wanted = sys.argv[1:]
    for name, markup in collect().items():
        if wanted and name not in wanted:
            continue
        render(name, markup, work)
    print(f"\n-> {DEST}")
