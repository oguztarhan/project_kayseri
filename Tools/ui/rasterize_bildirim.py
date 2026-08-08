"""SVG -> saydam PNG, dogrudan Assets/Art/UI/Bildirim icine.

rasterize_harita.py ile ayni yol: makinede rasterizer kutuphanesi yok ama Chrome var.

Tek fark en sonda: kucuk ikon 24 pikselde ISE YARAMAK zorunda, ve bunu ancak kucultup
bakarak anlarsin. Betik o yuzden `ocak_kucuk`u 24'e indirip ASCII bir onizleme basar.
Onizlemede vagon secilmiyorsa cihazda da secilmez -- sekli kalinlastir, yeniden calistir.

    python Tools/ui/rasterize_bildirim.py
"""
import pathlib
import re
import subprocess
import tempfile

from bildirim_ikonu import PIECES
from png_io import oku

HERE = pathlib.Path(__file__).resolve().parent
DEST = HERE.parents[1] / "Assets" / "Art" / "UI" / "Bildirim"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"

RAMPA = " .:-=+*#%@"   # koyudan aciga degil, SEFFAFTAN OPAGA


def onizle(yol, kenar=24):
    """PNG'yi kenar x kenar'a kutu-ortalamasiyla indirip alfayi ASCII basar."""
    w, h, px = oku(yol)
    satirlar = []
    for gy in range(kenar):
        s = []
        for gx in range(kenar):
            x0, x1 = gx * w // kenar, max(gx * w // kenar + 1, (gx + 1) * w // kenar)
            y0, y1 = gy * h // kenar, max(gy * h // kenar + 1, (gy + 1) * h // kenar)
            top = n = 0
            for y in range(y0, y1):
                satir = (y * w) * 4
                for x in range(x0, x1):
                    top += px[satir + x * 4 + 3]
                    n += 1
            a = top / n / 255.0
            s.append(RAMPA[min(len(RAMPA) - 1, int(a * len(RAMPA)))])
        satirlar.append("".join(s))
    return satirlar


DEST.mkdir(parents=True, exist_ok=True)
work = pathlib.Path(tempfile.mkdtemp(prefix="kayseri_bildirim_"))
uretilen = {}
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
    uretilen[name] = out
    print(f"{name:14s} {w:4d} x {h:4d}  {out.stat().st_size:7d} B")

if "ocak_kucuk" in uretilen:
    print("\ndurum cubugunda (24 px, alfa):")
    for satir in onizle(uretilen["ocak_kucuk"]):
        print("   |" + satir + "|")

print(f"\n-> {DEST}")
