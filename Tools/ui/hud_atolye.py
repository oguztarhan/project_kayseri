"""HUD alt sirasinin ATOLYE acicisi -> Assets/Resources/UI/Buttons/atolye.png.

hud_bolum_kaptan.py'nin devami ve ayni yontem: plaka gorev.png'den sokuluyor,
uzerine yeni simge kontur + gradyan olarak basiliyor. O dosyanin acilis notu
neden sifirdan plaka cizilmedigini anlatiyor; burada tekrarlanmiyor.

  atolye.png   ORS. Alt sirada kupa (gorev), baret (ustabasi), kitap (bolum),
               kasket (kaptan) ve flama dizisi (etkinlik) var. Orsun silueti
               hicbirine benzemiyor cunku ALT AGIR: solda sivri boynuz, ince
               bir bel, yayilan bir taban. Digerleri ust agir ya da simetrik.

CEKIC CIZILMEDI. Ors + cekic daha acik anlatirdi ama cekic ancak orsun USTUNE
sigiyor ve bileske 258x270'e dusuyordu -- kardeslerinin hepsi ~324 genis ve en
cok 252 yuksek. Simgeyi kuculterek anlam eklemek, alt sirada tek basina kucuk
duran bir dugme uretiyordu. Tek nesne ayrica ailenin kurali: coklu olan yalniz
etkinlik, ve o bilerek.

TABLA ALTIN, GOVDE KAGIT. Ailedeki HER ikonda altin var (kupa, baret, kitabin
ayraci, kasketin siperligi, flamalar); butun beyaz bir ors o zincirin disina
duserdi. Ustelik kasketten ayrilmasi buradan geliyor: kaskette acik kubbe USTTE
ve altin ALTTA, orsta tam tersi.

BOY ELLE VERILMIYOR, OLCULUYOR. Geometri yerel koordinatta duruyor; asagidaki
iki gecis once cizip sinir kutusunu okuyor, sonra oleceklendirip ortaliyor.
Elle ayarlanan bir olcek, cokgenin bir noktasi degistigi anda sessizce bozulur.

    python Tools/ui/hud_atolye.py
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

# Kardeslerin olculeri: bolum 319x217, kaptan 329x252, etkinlik 323x199.
HEDEF_EN = 324
HEDEF_MERKEZ = (256, 268)

# Yerel geometri (olcek 1, merkez 0,0). Tabla boynuzu tasiyor: boynuz govdeden
# degil tabladan cikar, ve ikisi ayri cizildigi icin aradaki kontur kasketin
# kubbe/bant ayrimiyla ayni isi goruyor.
TABLA = [(-96, -78), (96, -78), (96, -36), (-84, -36), (-130, -62)]
GOVDE = [(-54, -36), (54, -36), (30, 14), (34, 40), (92, 52), (92, 76),
         (-92, 76), (-92, 52), (-34, 40), (-30, 14)]


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


def yerlestir(nokta, cx, cy, s):
    return [(cx + x * s, cy + y * s) for x, y in nokta]


def ors(boyut, cx, cy, s):
    """Govde once, tabla sonra: tabla govdenin ust kenarini ortuyor."""
    kalin = max(4, int(round(12 * s)))
    kat = Image.new("RGBA", boyut, (0, 0, 0, 0))
    kat.alpha_composite(cizili(boyut, lambda d: d.polygon(yerlestir(GOVDE, cx, cy, s), fill=255),
                               KAGIT_UST, KAGIT_ALT, kalinlik=kalin))
    kat.alpha_composite(cizili(boyut, lambda d: d.polygon(yerlestir(TABLA, cx, cy, s), fill=255),
                               ALTIN_UST, ALTIN_ALT, kalinlik=kalin))
    return kat


def simge(boyut):
    # 1. gecis: olcegi bul. 2. gecis: kaymayi bul. 3. gecis: ciz.
    bb = ors(boyut, 256, 256, 1.0).getbbox()
    s = HEDEF_EN / float(bb[2] - bb[0])

    bb = ors(boyut, 256, 256, s).getbbox()
    cx = 256 + HEDEF_MERKEZ[0] - (bb[0] + bb[2]) // 2
    cy = 256 + HEDEF_MERKEZ[1] - (bb[1] + bb[3]) // 2

    kat = ors(boyut, cx, cy, s)
    # Vurgu tablanin sol ucunde: orsun isik alan yeri, ve boynuzu okutan sey.
    return parlat(kat, lambda dd: dd.ellipse(
        [cx - 118 * s, cy - 82 * s, cx + 10 * s, cy - 30 * s], fill=255), boyut)


def main():
    im = plaka()
    im.alpha_composite(simge(im.size))
    yol = HEDEF / "atolye.png"
    im.save(yol)
    print("%-14s %dx%d  -> %s" % ("atolye.png", im.width, im.height, yol))


if __name__ == "__main__":
    main()
