"""SVG -> saydam PNG (headless Chrome) -> Assets/Art/UI/Gostergeler.

rasterize_harita.py ile ayni yontem: makinede rasterize kutuphanesi yok ama Chrome var.
Tek basina bir .svg belgesi Chrome'un ortalamasini yiyip sprite'a bosluk katiyor, o yuzden
her parca sifir kenar bosluklu, artboard boyunda bir HTML sayfasina gomuluyor.

    python Tools/ui/rasterize_istasyon.py
"""
import pathlib
import re
import subprocess
import tempfile

from istasyon_parcalari import KENAR, KLASOR, PIECES

HERE = pathlib.Path(__file__).resolve().parent
SANAT = HERE.parents[1] / "Assets" / "Art" / "UI"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"

work = pathlib.Path(tempfile.mkdtemp(prefix="kayseri_ist_"))
for name, markup in PIECES.items():
    DEST = SANAT / KLASOR.get(name, "Gostergeler")
    DEST.mkdir(parents=True, exist_ok=True)
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
    print(f"{name:16s} {w:4d} x {h:3d}  9-dilim {KENAR[name]}  {out.stat().st_size:7d} B  -> {DEST.name}/")
print(f"\n-> {SANAT}")
