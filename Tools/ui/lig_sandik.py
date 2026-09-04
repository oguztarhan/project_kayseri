"""LIG ekraninin odul sandiklari -> Assets/Resources/UI/Lig.

Referans ekranda her siranin sagi bir sandik: birinci altin, ikinci gumus,
ucuncu bronz, geri kalan sirali kademe icin duz bir sandik. Oyuncu sandiga
basinca icinde ne oldugunu goruyor -- yani sandik dekor degil, odul tablosunun
kendisi.

    python3 Tools/ui/lig_sandik.py

Kit'in cizim dili hud_bolum_kaptan.py'den aliniyor (kontur + dikey gradyan + ust
parlaklik), boylece sandiklar alt siranin ikonlariyla ayni aileden cikiyor.
Plaka YOK: bunlar satirin icine gomulen serbest ikonlar, arkalarinda kendi
satirlarinin zemini var.
"""
import pathlib

from PIL import Image, ImageDraw

from hud_bolum_kaptan import ALTIN_ALT, ALTIN_UST, KONTUR, cizili, parlat

HERE = pathlib.Path(__file__).resolve().parent
HEDEF = HERE.parents[1] / "Assets" / "Resources" / "UI" / "Lig"

BOYUT = (256, 256)

# Govde ve kapak icin ust/alt gradyan ciftleri.
KADEME = {
    "altin":  ((255, 214, 74, 255),  (204, 130, 6, 255)),
    "gumus":  ((240, 245, 255, 255), (150, 163, 190, 255)),
    "bronz":  ((226, 158, 96, 255),  (150, 84, 38, 255)),
    "sade":   ((150, 214, 138, 255), (58, 132, 68, 255)),
}


def sandik(ust, alt):
    """Kapagi kavisli bir sandik: govde, kapak, orta bant ve altin kilit."""
    kat = Image.new("RGBA", BOYUT, (0, 0, 0, 0))
    w, h = BOYUT
    cx = w // 2

    govde = lambda d: d.rounded_rectangle([34, 126, w - 34, 214], radius=14, fill=255)
    kat.alpha_composite(cizili(BOYUT, govde, ust, alt, kalinlik=9))

    # Kapak: ust yarisi kesilmis bir elips -- duz bir dikdortgen sandik degil,
    # kutu gibi okunuyor.
    kapak = lambda d: d.pieslice([34, 62, w - 34, 178], 180, 360, fill=255)
    kat.alpha_composite(cizili(BOYUT, kapak, ust, alt, kalinlik=9))

    # Kapagi govdeden ayiran seri.
    d = ImageDraw.Draw(kat)
    d.rectangle([40, 120, w - 40, 134], fill=KONTUR)

    # Orta bant: kapaktan govdeye inen dikey serit.
    bant = lambda dd: dd.rectangle([cx - 20, 74, cx + 20, 210], fill=255)
    kat.alpha_composite(cizili(BOYUT, bant, ALTIN_UST, ALTIN_ALT, kalinlik=7))

    # Kilit.
    kilit = lambda dd: dd.rounded_rectangle([cx - 26, 132, cx + 26, 178], radius=10, fill=255)
    kat.alpha_composite(cizili(BOYUT, kilit, ALTIN_UST, ALTIN_ALT, kalinlik=7))
    ImageDraw.Draw(kat).ellipse([cx - 8, 146, cx + 8, 162], fill=KONTUR)

    return parlat(kat, lambda dd: dd.ellipse([54, 76, 150, 126], fill=255), BOYUT)


def main():
    HEDEF.mkdir(parents=True, exist_ok=True)
    for ad, (ust, alt) in KADEME.items():
        yol = HEDEF / ("sandik_" + ad + ".png")
        sandik(ust, alt).save(yol)
        print("%-18s %dx%d  -> %s" % (yol.name, BOYUT[0], BOYUT[1], yol))


if __name__ == "__main__":
    main()
