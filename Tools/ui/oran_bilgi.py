"""Sans orani rozeti -> Assets/Resources/UI/Buttons/bilgi.png.

Usta sandigi elmasla aciliyor ve elmas gercek parayla satiliyor: yani sandik
ucretli bir sans mekanigi ve her iki magaza da oranin ALIMDAN ONCE okunabilir
olmasini sart kosuyor. Bu rozet o sayfayi acan dugme -- referans ekranlarda da
her sandik kartinin sag ustunde ayni ⓘ duruyor.

    python3 Tools/ui/oran_bilgi.py

Cizim dili hud_bolum_kaptan.py'den: kontur + dikey gradyan + ust parlaklik.
PLAKA YOK -- bu bir alt sira ikonu degil, kartin kosesine oturan serbest bir
rozet; arkasinda kendi kartinin beyaz zemini var.

Harf FONTLA DEGIL SEKILLE ciziliyor. Bir yazi tipi her makinede ayni yerde
degil, olmayinca PIL sessizce bitmap default'a duser ve rozet 128 pikselde
bulanik cikar. Nokta + govde iki dikdortgen; her boyutta ayni.
"""
import pathlib

from PIL import Image, ImageDraw

from hud_bolum_kaptan import KONTUR, cizili, parlat

HERE = pathlib.Path(__file__).resolve().parent
HEDEF = HERE.parents[1] / "Assets" / "Resources" / "UI" / "Buttons"

BOYUT = (128, 128)

# Grafit: HUD'un sayac hapiyla ayni aile, kartin beyazi uzerinde geri cekiliyor.
GRAFIT_UST, GRAFIT_ALT = (108, 118, 140, 255), (58, 66, 86, 255)
KAGIT = (252, 253, 255, 255)


def rozet():
    kat = Image.new("RGBA", BOYUT, (0, 0, 0, 0))
    w, h = BOYUT
    cx, cy = w // 2, h // 2

    daire = lambda d: d.ellipse([10, 10, w - 10, h - 10], fill=255)
    kat.alpha_composite(cizili(BOYUT, daire, GRAFIT_UST, GRAFIT_ALT, kalinlik=7))

    d = ImageDraw.Draw(kat)
    # Nokta ve govde: konturun bir tik disina tasan koyu bir golge, uzerine kagit.
    d.ellipse([cx - 9, cy - 34, cx + 9, cy - 16], fill=KONTUR)
    d.rounded_rectangle([cx - 9, cy - 8, cx + 9, cy + 34], radius=5, fill=KONTUR)
    d.ellipse([cx - 7, cy - 32, cx + 7, cy - 18], fill=KAGIT)
    d.rounded_rectangle([cx - 7, cy - 6, cx + 7, cy + 32], radius=4, fill=KAGIT)

    return parlat(kat, lambda dd: dd.ellipse([24, 20, 74, 54], fill=255), BOYUT)


def main():
    HEDEF.mkdir(parents=True, exist_ok=True)
    yol = HEDEF / "bilgi.png"
    rozet().save(yol)
    print("%-12s %dx%d  -> %s" % ("bilgi.png", BOYUT[0], BOYUT[1], yol))


if __name__ == "__main__":
    main()
