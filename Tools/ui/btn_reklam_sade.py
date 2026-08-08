"""btn_x2_reklam.png -> btn_bonus_reklam.png, with the baked-in x2 taken off.

    python Tools/ui/btn_reklam_sade.py

The welcome-back screen's orange button used to promise double, and the promise was painted
into the sprite: a play icon on the left, a big white "x2" on the right. The rewarded ad now
pays adBonusFraction, which is 30%, and the caption is written from that number at runtime --
so the one part of the button that could not follow the tuning was the part baked into the art.

Rather than redraw the button, this rebuilds the patch under the x2. The body is a strictly
vertical gradient: sampling two clean columns 200px apart across the glyph's rows agrees to
within 2/255, so every row can be refilled from a single clean column and the seam is invisible.
The source column is taken just left of the glyph, close enough that it is inside the same gloss
band, and the script refuses to run if it turns out the gloss does not cover the whole patch --
in that case the fill would paint highlight onto rows that never had any.

The play icon is deliberately left alone. It is still a rewarded ad, and the icon is what says so.
"""
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import png_io

BUTONLAR = HERE.parents[1] / "Assets" / "Art" / "UI" / "Butonlar"
KAYNAK = BUTONLAR / "btn_x2_reklam.png"
# NOT btn_reklam.png -- that name was already taken by the blue monitor icon the HUD's rewarded-ad
# button wears, and writing this wide orange bar over it replaces that icon everywhere it is used.
HEDEF = BUTONLAR / "btn_bonus_reklam.png"

PAY = 6          # pixels of margin added around the glyph box before filling
KAYNAK_UZAKLIK = 40   # how far left of the glyph the clean source column sits


def main():
    w, h, px = png_io.oku(str(KAYNAK))

    def at(x, y):
        i = (y * w + x) * 4
        return px[i:i + 4]

    def beyaz(c):
        return c[3] > 120 and c[0] > 225 and c[1] > 225 and c[2] > 225

    x0 = w; x1 = 0; y0 = h; y1 = 0
    for y in range(h):
        for x in range(w // 2, w):
            if beyaz(at(x, y)):
                x0 = min(x0, x); x1 = max(x1, x)
                y0 = min(y0, y); y1 = max(y1, y)
    if x1 < x0:
        sys.exit("x2 bulunamadi -- sprite zaten sade mi?")
    print("x2 kutusu: x %d..%d  y %d..%d" % (x0, x1, y0, y1))

    x0 = max(0, x0 - PAY); x1 = min(w - 1, x1 + PAY)
    y0 = max(0, y0 - PAY); y1 = min(h - 1, y1 + PAY)
    src = x0 - KAYNAK_UZAKLIK
    if src < 0:
        sys.exit("kaynak sutun tuvalin disinda")

    # The gloss is the one horizontal feature; if it stops inside the patch, a single source
    # column is the wrong tool and this must not quietly produce a smeared button.
    for y in range(y0, y1 + 1):
        a = at(src, y)
        b = at(x1 + 4 if x1 + 4 < w else w - 1, y)
        if max(abs(a[k] - b[k]) for k in range(4)) > 12:
            sys.exit("y=%d: yamanin iki yani farkli (#%02X%02X%02X vs #%02X%02X%02X) -- "
                     "tek sutundan doldurmak yanlis olur" % (y, a[0], a[1], a[2], b[0], b[1], b[2]))

    for y in range(y0, y1 + 1):
        kaynak = at(src, y)
        for x in range(x0, x1 + 1):
            i = (y * w + x) * 4
            px[i:i + 4] = kaynak

    png_io.yaz(str(HEDEF), w, h, px)
    print("yazildi: %s  (%dx%d)" % (HEDEF, w, h))


if __name__ == "__main__":
    main()
