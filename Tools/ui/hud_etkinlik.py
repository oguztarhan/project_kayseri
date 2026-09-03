"""HUD alt sirasinin ETKINLIKLER acicisi -> Assets/Resources/UI/Buttons/etkinlik.png.

hud_bolum_kaptan.py'nin devami ve ayni yontem: plaka gorev.png'den sokuluyor,
uzerine yeni simge kontur + gradyan olarak basiliyor. O dosyanin acilis notu
neden sifirdan plaka cizilmedigini anlatiyor; burada tekrarlanmiyor.

  etkinlik.png   ipe asili uc flama. Silueti seciliyor, rengi degil: alt sirada
                 zaten kupa (gorev), baret (ustabasi), kitap (bolum) ve kasket
                 (kaptan) var -- dordu de TEK, DOLU bir kutle. Ucgen dizisi 150
                 pikselde bunlarin hicbiriyle karismiyor, cunku tek bir sekil
                 degil, bir RITIM.

RENK KITIN DISINA CIKMIYOR. Flamalar altin / kagit / lacivert -- setin uc rengi.
Yeni bir festival kirmizisi eklemek 150 pikselde daha okunakli olurdu ama alt
sirayi iki aileye bolerdi; ayirt etmeyi bicime yaptirmak, o bedeli odemekten iyi.

    python Tools/ui/hud_etkinlik.py
"""
import pathlib

from PIL import Image, ImageDraw, ImageFilter

HERE = pathlib.Path(__file__).resolve().parent
HEDEF = HERE.parents[1] / "Assets" / "Resources" / "UI" / "Buttons"
KAYNAK = HEDEF / "gorev.png"

# hud_bolum_kaptan.py ile ayni: kupanin alani ve govde gradyaninin okundugu sutun.
SIL = (85, 122, 427, 458)
SUTUN = 88

KONTUR = (14, 20, 32, 255)
ALTIN_UST, ALTIN_ALT = (255, 214, 74, 255), (226, 146, 8, 255)
KAGIT_UST, KAGIT_ALT = (255, 253, 246, 255), (216, 220, 232, 255)
LACIVERT = (32, 52, 104, 255)
LACIVERT_ALT = (18, 30, 62, 255)

# Ipin gectigi yay: uclari yukarida, ortasi SARKIK. Duz bir ip cubuk gibi
# duruyor; sarkma, dizinin asili oldugunu tek basina anlatan sey.
IP_YARI = 150       # ortadan uca yatay mesafe
IP_UST = -104       # uclarin merkeze gore yuksekligi
IP_SARKMA = 58      # ortanin uclardan ne kadar asagida oldugu


def plaka():
    """gorev.png'nin plakasi, ortasi silinmis. hud_bolum_kaptan.plaka ile ayni."""
    im = Image.open(KAYNAK).convert("RGBA")
    px = im.load()
    x0, y0, x1, y1 = SIL
    for y in range(y0, y1):
        satir = px[SUTUN, y]
        for x in range(x0, x1):
            px[x, y] = satir
    return im


def dikey_gradyan(boyut, ust, alt):
    g = Image.new("RGBA", boyut)
    d = ImageDraw.Draw(g)
    for y in range(boyut[1]):
        t = y / max(1, boyut[1] - 1)
        d.line([(0, y), (boyut[0], y)],
               fill=tuple(int(ust[i] + (alt[i] - ust[i]) * t) for i in range(4)))
    return g


def cizili(boyut, ciz, ust, alt, kalinlik=16):
    """Kontur + gradyan dolgu. hud_bolum_kaptan.cizili ile ayni gerekce."""
    maske = Image.new("L", boyut, 0)
    ciz(ImageDraw.Draw(maske))

    sisman = maske.filter(ImageFilter.MaxFilter(kalinlik * 2 + 1))
    kat = Image.new("RGBA", boyut, (0, 0, 0, 0))
    kat.paste(Image.new("RGBA", boyut, KONTUR), (0, 0), sisman)
    kat.paste(dikey_gradyan(boyut, ust, alt), (0, 0), maske)
    return kat


def parlat(kat, maske_ciz, boyut):
    """Ust yariya yumusak beyaz vurgu -- her ikonda ayni yerden geliyor."""
    m = Image.new("L", boyut, 0)
    maske_ciz(ImageDraw.Draw(m))
    m = m.filter(ImageFilter.GaussianBlur(6))
    vurgu = Image.new("RGBA", boyut, (255, 255, 255, 70))
    kat.alpha_composite(Image.composite(vurgu, Image.new("RGBA", boyut, (0, 0, 0, 0)), m))
    return kat


def ip_y(cy, t):
    """Yayin t=0..1 boyunca yuksekligi. Uclarda IP_UST, ortada IP_UST+IP_SARKMA."""
    return cy + IP_UST + IP_SARKMA * (1.0 - (2.0 * t - 1.0) ** 2)


def flamalar(boyut):
    """Ipe asili uc ucgen flama."""
    kat = Image.new("RGBA", boyut, (0, 0, 0, 0))
    cx, cy = boyut[0] // 2, boyut[1] // 2 + 10

    # Ip once ciziliyor ki flamalar onun uzerine otursun ve baglanti yeri
    # gorunmesin -- ipin ustunde duran bir ucgen asili degil, yapisik durur.
    def ip(d):
        nokta = [(cx - IP_YARI + 2 * IP_YARI * (i / 40.0), ip_y(cy, i / 40.0))
                 for i in range(41)]
        d.line(nokta, fill=255, width=13, joint="curve")
    kat.alpha_composite(cizili(boyut, ip, LACIVERT, LACIVERT_ALT, kalinlik=8))

    # Uc flama: setin uc rengi, soldan saga. Ortadaki KAGIT cunku en genis
    # yuzey ortada ve en acik renk orada en cok isik topluyor.
    renkler = ((ALTIN_UST, ALTIN_ALT), (KAGIT_UST, KAGIT_ALT), (LACIVERT, LACIVERT_ALT))
    for i, (ust, alt) in enumerate(renkler):
        t = 0.20 + 0.30 * i
        x = cx - IP_YARI + 2 * IP_YARI * t
        y = ip_y(cy, t)
        # Ucgenin ust kenari ipin egimini takip ediyor: yatay bir kenar,
        # sarkan bir ipte yamuk duruyor.
        eg = (ip_y(cy, t + 0.06) - ip_y(cy, t - 0.06)) * 0.5
        ucgen = (lambda d, x=x, y=y, eg=eg: d.polygon(
            [(x - 46, y - eg), (x + 46, y + eg), (x, y + 116)], fill=255))
        kat.alpha_composite(cizili(boyut, ucgen, ust, alt, kalinlik=11))

    return parlat(kat, lambda dd: dd.ellipse([cx - 130, cy - 118, cx + 10, cy - 34], fill=255), boyut)


def main():
    im = plaka()
    im.alpha_composite(flamalar(im.size))
    yol = HEDEF / "etkinlik.png"
    im.save(yol)
    print("%-14s %dx%d  -> %s" % ("etkinlik.png", im.width, im.height, yol))


if __name__ == "__main__":
    main()
