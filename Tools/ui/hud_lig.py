"""HUD alt sirasinin dokuzuncu parcasi -> Assets/Resources/UI/Buttons/lig.png.

LIG ekraninin acicisi sanat gelmedigi icin duz mavi plakaya dusuyordu -- yedi
ikonun yaninda bir bos kare, oyuncunun once fark ettigi sey (ayni sorun BOLUM ve
KAPTAN icin hud_bolum_kaptan.py'de cozulmustu).

  lig.png      podyum: ortada altin birinci basamak, solda gumus, sagda lacivert.

NEDEN PODYUM. Alt sirada zaten bir kupa (gorev), bir baret (ustabasi), bir kitap
(bolum), bir kasket (kaptan) ve bir takvim (etkinlik) var. Kupa siralama icin en
dogal simge ama ALINMIS; podyum siralamayi kupayla karismadan anlatan tek sekil,
ve 150 pikselde ucuncu basamagin yuksekligi bile okunuyor.

    python3 Tools/ui/hud_lig.py

PLAKA CIZILMIYOR, SOKULUYOR -- plaka(), cizili() ve parlat() hud_bolum_kaptan'dan
aynen aliniyor. Ayni aileden gorunmesinin tek yolu ayni plakayi ve ayni kontur
dilini kullanmak; yeniden yazilan bir kopya kose yaricapinda kayardi.
"""
import pathlib

from PIL import Image

from hud_bolum_kaptan import (ALTIN_ALT, ALTIN_UST, KAGIT_ALT, KAGIT_UST,
                              LACIVERT, cizili, parlat, plaka)

HERE = pathlib.Path(__file__).resolve().parent
HEDEF = HERE.parents[1] / "Assets" / "Resources" / "UI" / "Buttons"

LACIVERT_ALT = (18, 30, 62, 255)


def podyum(boyut):
    """Uc basamak: ortada altin birinci, solda gumus ikinci, sagda lacivert ucuncu."""
    kat = Image.new("RGBA", boyut, (0, 0, 0, 0))
    cx, cy = boyut[0] // 2, boyut[1] // 2 + 10
    taban = cy + 96

    # Yan basamaklar once ciziliyor, ortadaki onlarin uzerine binsin: birincinin
    # one cikmasi derinligi veren tek sey.
    sol = lambda d: d.rectangle([cx - 172, cy - 4, cx - 58, taban], fill=255)
    kat.alpha_composite(cizili(boyut, sol, KAGIT_UST, KAGIT_ALT, kalinlik=11))

    sag = lambda d: d.rectangle([cx + 58, cy + 34, cx + 172, taban], fill=255)
    kat.alpha_composite(cizili(boyut, sag, LACIVERT, LACIVERT_ALT, kalinlik=11))

    orta = lambda d: d.rectangle([cx - 62, cy - 78, cx + 62, taban], fill=255)
    kat.alpha_composite(cizili(boyut, orta, ALTIN_UST, ALTIN_ALT, kalinlik=13))

    return parlat(kat, lambda dd: dd.ellipse([cx - 54, cy - 70, cx + 30, cy - 8], fill=255), boyut)


def main():
    im = plaka()
    im.alpha_composite(podyum(im.size))
    yol = HEDEF / "lig.png"
    im.save(yol)
    print("%-12s %dx%d  -> %s" % ("lig.png", im.width, im.height, yol))


if __name__ == "__main__":
    main()
