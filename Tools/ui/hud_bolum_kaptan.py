"""HUD alt sirasinin iki yeni parcasi -> Assets/Resources/UI/Buttons.

Alt sirada artik sekiz buton var. Altisi kendi ikonunu tasiyor; BOLUMLER ve
KAPTANLAR ekranlarinin acicilari sanat gelmedigi icin duz renkli plakaya
dusuyordu -- alti ikonun yaninda iki bos kare, oyuncunun once fark ettigi sey.

  bolum.png    acik kitap: bir bolum bir hikaye, ve kitap hicbir seyle
               karismiyor (kupa gorev, baret ustabasi).
  kaptan.png   kaptan kasketi: beyaz kubbe, lacivert bant, altin siperlik ve
               capa. Baret SARI oldugu icin kasket BEYAZ -- iki insan ikonunun
               150 pikselde birbirine benzememesi renkten geliyor, bicimden degil.

    python3 Tools/ui/hud_bolum_kaptan.py

PLAKA CIZILMIYOR, SOKULUYOR. gorev.png'nin plakasi aliniyor ve ortasindaki kupa
siliniyor: govde yatayda duz, dikeyde yumusak bir gradyan (x=88 sutunu bunun
tamami), yani her satir kendi renginde bir seride donusuyor. Sifirdan cizilen bir
plaka konturu, kose yaricapi ve ust parlaklik bandiyla asla birebir tutmazdi ve
yan yana dizildiginde baska bir aileden gorunurdu.
"""
import pathlib

from PIL import Image, ImageDraw, ImageFilter

HERE = pathlib.Path(__file__).resolve().parent
HEDEF = HERE.parents[1] / "Assets" / "Resources" / "UI" / "Buttons"
KAYNAK = HEDEF / "gorev.png"

# Kupanin kapladigi alan. Ust parlaklik bandi y<118'de kaliyor, o yuzden ona
# dokunulmuyor; plakanin ic konturu de x<85 ve x>427'de.
SIL = (85, 122, 427, 458)
SUTUN = 88          # govde gradyaninin okundugu, simgeden uzak sutun

KONTUR = (14, 20, 32, 255)
ALTIN_UST, ALTIN_ALT = (255, 214, 74, 255), (226, 146, 8, 255)
KAGIT_UST, KAGIT_ALT = (255, 253, 246, 255), (216, 220, 232, 255)
LACIVERT = (32, 52, 104, 255)


def plaka():
    """gorev.png'nin plakasi, ortasi silinmis."""
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
    """Bir sekli kontur + gradyan dolgu olarak cizer -- kit'in cikartma dili.

    Kontur, maskeyi buyutup koyu renkle basarak elde ediliyor; boylece kose
    birlesimlerinde kalinlik degismiyor. ImageDraw'in width parametresi cokgende
    ic koseleri inceltiyor ve 150 pikselde bu gorunuyor.
    """
    maske = Image.new("L", boyut, 0)
    ciz(ImageDraw.Draw(maske))

    sisman = maske.filter(ImageFilter.MaxFilter(kalinlik * 2 + 1))
    kat = Image.new("RGBA", boyut, (0, 0, 0, 0))
    kat.paste(Image.new("RGBA", boyut, KONTUR), (0, 0), sisman)
    kat.paste(dikey_gradyan(boyut, ust, alt), (0, 0), maske)
    return kat


def parlat(kat, maske_ciz, boyut):
    """Ust yariya yumusak bir beyaz vurgu -- her ikonda ayni yerden geliyor."""
    m = Image.new("L", boyut, 0)
    maske_ciz(ImageDraw.Draw(m))
    m = m.filter(ImageFilter.GaussianBlur(6))
    vurgu = Image.new("RGBA", boyut, (255, 255, 255, 70))
    kat.alpha_composite(Image.composite(vurgu, Image.new("RGBA", boyut, (0, 0, 0, 0)), m))
    return kat


def kitap(boyut):
    """Acik kitap: iki sayfa, ortada sirt, altta lacivert kapak."""
    kat = Image.new("RGBA", boyut, (0, 0, 0, 0))
    cx, cy = boyut[0] // 2, boyut[1] // 2 + 6

    kapak = lambda d: d.polygon(
        [(cx - 138, cy - 64), (cx, cy - 43), (cx + 138, cy - 64),
         (cx + 146, cy + 95), (cx, cy + 113), (cx - 146, cy + 95)], fill=255)
    kat.alpha_composite(cizili(boyut, kapak, LACIVERT, (18, 30, 62, 255), kalinlik=13))

    sol = lambda d: d.polygon(
        [(cx - 123, cy - 79), (cx - 7, cy - 51), (cx - 7, cy + 85), (cx - 130, cy + 62)], fill=255)
    sag = lambda d: d.polygon(
        [(cx + 123, cy - 79), (cx + 7, cy - 51), (cx + 7, cy + 85), (cx + 130, cy + 62)], fill=255)
    for sayfa in (sol, sag):
        kat.alpha_composite(cizili(boyut, sayfa, KAGIT_UST, KAGIT_ALT, kalinlik=11))

    # Satirlar: sayfanin kendi egimini takip ediyor, yoksa kitap duz duruyor.
    d = ImageDraw.Draw(kat)
    for i in range(4):
        y = cy - 28 + i * 25
        d.line([(cx - 105, y - 10), (cx - 25, y)], fill=(120, 132, 158, 255), width=8)
        d.line([(cx + 25, y), (cx + 105, y - 10)], fill=(120, 132, 158, 255), width=8)

    # Ayrac: kitabin altin lekesi, sirtin sagindan sarkiyor.
    ayrac = lambda dd: dd.polygon(
        [(cx + 38, cy - 48), (cx + 68, cy - 42), (cx + 68, cy + 61), (cx + 53, cy + 44), (cx + 38, cy + 58)],
        fill=255)
    kat.alpha_composite(cizili(boyut, ayrac, ALTIN_UST, ALTIN_ALT, kalinlik=9))
    return parlat(kat, lambda dd: dd.ellipse([cx - 124, cy - 92, cx + 24, cy - 10], fill=255), boyut)


def capa_ciz(d, cx, cy, k):
    """Gercek bir capa: halka, gonder, cubuk, ve altta kanca gibi kivrilan kollar.

    Kollar ARC ile ciziliyor, doldurulmus bir yarim daireden delik cikararak
    degil -- o yontem 150 pikselde capa degil, kase gibi okunuyordu.
    """
    d.ellipse([cx - 13 * k, cy - 46 * k, cx + 13 * k, cy - 20 * k], outline=255, width=int(8 * k))
    d.rectangle([cx - 6 * k, cy - 34 * k, cx + 6 * k, cy + 46 * k], fill=255)
    d.rectangle([cx - 34 * k, cy - 16 * k, cx + 34 * k, cy - 4 * k], fill=255)
    d.arc([cx - 42 * k, cy - 6 * k, cx + 42 * k, cy + 54 * k], 20, 160, fill=255, width=int(12 * k))
    for yon in (-1, 1):
        ux = cx + yon * 42 * k
        d.polygon([(ux - 11 * k, cy + 16 * k), (ux + 11 * k, cy + 16 * k), (ux + yon * 2 * k, cy + 40 * k)],
                  fill=255)


def kasket(boyut):
    """Kaptan kasketi: beyaz kubbe, lacivert bant, altin siperlik ve capa."""
    kat = Image.new("RGBA", boyut, (0, 0, 0, 0))
    cx, cy = boyut[0] // 2, boyut[1] // 2 + 14

    siperlik = lambda d: d.polygon(
        [(cx - 152, cy + 52), (cx + 152, cy + 52), (cx + 116, cy + 98), (cx - 116, cy + 98)], fill=255)
    kat.alpha_composite(cizili(boyut, siperlik, ALTIN_UST, ALTIN_ALT, kalinlik=12))

    kubbe = lambda d: d.pieslice([cx - 128, cy - 128, cx + 128, cy + 56], 180, 360, fill=255)
    kat.alpha_composite(cizili(boyut, kubbe, KAGIT_UST, KAGIT_ALT, kalinlik=13))

    bant = lambda d: d.rectangle([cx - 131, cy + 4, cx + 131, cy + 58], fill=255)
    kat.alpha_composite(cizili(boyut, bant, LACIVERT, (18, 30, 62, 255), kalinlik=11))

    kat.alpha_composite(cizili(boyut, lambda d: capa_ciz(d, cx, cy + 30, 0.62),
                               ALTIN_UST, ALTIN_ALT, kalinlik=7))
    return parlat(kat, lambda dd: dd.ellipse([cx - 104, cy - 104, cx + 12, cy - 24], fill=255), boyut)


def main():
    taban = plaka()
    boyut = taban.size
    for ad, ciz in (("bolum", kitap), ("kaptan", kasket)):
        im = taban.copy()
        im.alpha_composite(ciz(boyut))
        yol = HEDEF / (ad + ".png")
        im.save(yol)
        print("%-12s %dx%d  -> %s" % (ad + ".png", im.width, im.height, yol))


if __name__ == "__main__":
    main()
