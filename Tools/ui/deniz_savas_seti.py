"""Deniz savasinin 2B sahne seti -> Assets/Resources/UI/Sea.

Dovus artik 3B denizde bordalasan kutular degil, kendi 2B sahnesinde oynanan bir
kart: yandan gorunen gemiler, gulle, patlama, kalkan, dalga seridi. "Sade ama
detayli" -- kit'in cikartma dili (kalin lacivert kontur, dikey gradyan, ust
parlaklik) buyuk sprite'lara tasiniyor.

    python3 Tools/ui/deniz_savas_seti.py

BUTUN GEMILER SAGA BAKAR. Tehdit ekranin saginda durur ve calisma aninda
localScale.x = -1 ile aynalanir; iki yon icin iki sprite cizmek, her duzeltmeyi
iki kez yapmak demektir.

dalga.png YATAYDA DONGULUDUR (64 px periyot, 1024 genislik) ve RawImage.uvRect
kaydirmasiyla akar -- tek nesne, sifir ek cizim. Importer'da wrap mode Repeat
olmali; rasterize eden betik degil, iceri alan kod ayarlar.
"""
import math
import pathlib

from PIL import Image, ImageDraw, ImageFilter

HEDEF = pathlib.Path(__file__).resolve().parents[2] / "Assets" / "Resources" / "UI" / "Sea"

KONTUR = (14, 20, 32, 255)

def dikey_gradyan(boyut, ust, alt):
    g = Image.new("RGBA", boyut)
    d = ImageDraw.Draw(g)
    for y in range(boyut[1]):
        t = y / max(1, boyut[1] - 1)
        d.line([(0, y), (boyut[0], y)],
               fill=tuple(int(ust[i] + (alt[i] - ust[i]) * t) for i in range(4)))
    return g

def cizili(boyut, ciz, ust, alt, kalinlik=10):
    """Sekli kontur + gradyan dolgu olarak basar -- kit'in cikartma dili."""
    maske = Image.new("L", boyut, 0)
    ciz(ImageDraw.Draw(maske))
    sisman = maske.filter(ImageFilter.MaxFilter(kalinlik * 2 + 1))
    kat = Image.new("RGBA", boyut, (0, 0, 0, 0))
    kat.paste(Image.new("RGBA", boyut, KONTUR), (0, 0), sisman)
    kat.paste(dikey_gradyan(boyut, ust, alt), (0, 0), maske)
    return kat

def parlat(kat, ciz, alfa=60):
    m = Image.new("L", kat.size, 0)
    ciz(ImageDraw.Draw(m))
    m = m.filter(ImageFilter.GaussianBlur(7))
    vurgu = Image.new("RGBA", kat.size, (255, 255, 255, alfa))
    kat.alpha_composite(Image.composite(vurgu, Image.new("RGBA", kat.size, (0, 0, 0, 0)), m))
    return kat


def gemi():
    """Oyuncunun gemisi: kirmizi govde, kaptan kosku, beyaz yelken, guverte topu."""
    B = (640, 460)
    kat = Image.new("RGBA", B, (0, 0, 0, 0))

    # Yelken once: govdenin arkasinda kalir. Hafif karinli, sol kenari direkte.
    yelken = lambda d: d.polygon([(330, 96), (560, 130), (520, 268), (330, 288)], fill=255)
    kat.alpha_composite(cizili(B, yelken, (255, 252, 244, 255), (208, 214, 228, 255), 9))
    d = ImageDraw.Draw(kat)
    d.line([(392, 108), (380, 282)], fill=(170, 178, 196, 255), width=7)   # yelken dikisi
    d.line([(468, 118), (450, 276)], fill=(170, 178, 196, 255), width=7)

    direk = lambda d2: d2.rectangle([320, 42, 342, 320], fill=255)
    kat.alpha_composite(cizili(B, direk, (122, 84, 48, 255), (86, 56, 30, 255), 8))
    flama = lambda d2: d2.polygon([(342, 46), (452, 62), (342, 92)], fill=255)
    kat.alpha_composite(cizili(B, flama, (250, 200, 60, 255), (226, 146, 8, 255), 7))

    # Govde: pruva sagda yukari kivrilir.
    govde = lambda d2: d2.polygon(
        [(58, 300), (612, 300), (596, 260), (560, 300), (58, 300),
         (58, 300)], fill=0) or d2.polygon(
        [(58, 300), (110, 402), (500, 402), (588, 322), (612, 262), (566, 296), (58, 296)], fill=255)
    kat.alpha_composite(cizili(B, govde, (198, 84, 52, 255), (128, 46, 32, 255), 11))
    d = ImageDraw.Draw(kat)
    d.line([(72, 330), (566, 330)], fill=(110, 40, 28, 255), width=8)      # kaplama derzi
    d.line([(84, 362), (540, 362)], fill=(110, 40, 28, 255), width=8)
    for x in (150, 240, 330, 420):                                          # lombarlar
        d.ellipse([x - 14, 336, x + 14, 364], fill=(246, 220, 150, 255), outline=KONTUR, width=5)

    # Kaptan kosku kicta.
    kosk = lambda d2: d2.rounded_rectangle([84, 216, 226, 300], 18, fill=255)
    kat.alpha_composite(cizili(B, kosk, (238, 232, 218, 255), (198, 192, 178, 255), 9))
    d = ImageDraw.Draw(kat)
    d.ellipse([128, 238, 176, 286], fill=(140, 208, 232, 255), outline=KONTUR, width=6)

    # Guverte topu, pruvaya donuk.
    top = lambda d2: (d2.rectangle([470, 246, 570, 276], fill=255),
                      d2.rectangle([452, 238, 492, 284], fill=255))
    kat.alpha_composite(cizili(B, top, (94, 100, 116, 255), (58, 62, 76, 255), 8))

    # Su kopugu.
    kopuk = lambda d2: d2.ellipse([44, 380, 620, 428], fill=255)
    kat.alpha_composite(cizili(B, kopuk, (240, 250, 255, 210), (196, 226, 244, 190), 6))
    return parlat(kat, lambda dd: dd.ellipse([110, 302, 430, 356], fill=255), 42)


def korsan():
    """Akinci teknesi: yirtik kizil yelken, koyu sivri govde, kara flama."""
    B = (600, 450)
    kat = Image.new("RGBA", B, (0, 0, 0, 0))

    # Yirtik yelken: sag kenari testere disli.
    def yelken(d):
        p = [(300, 84), (532, 120)]
        for i in range(5):
            p.append((532 - i * 14, 150 + i * 30))
            p.append((504 - i * 14, 168 + i * 30))
        p.append((300, 292))
        d.polygon(p, fill=255)
    kat.alpha_composite(cizili(B, yelken, (196, 52, 48, 255), (120, 26, 30, 255), 9))
    d = ImageDraw.Draw(kat)
    d.line([(356, 100), (346, 286)], fill=(96, 20, 24, 255), width=7)

    direk = lambda d2: d2.rectangle([290, 36, 310, 316], fill=255)
    kat.alpha_composite(cizili(B, direk, (66, 50, 40, 255), (40, 30, 24, 255), 8))
    flama = lambda d2: d2.polygon([(310, 40), (408, 54), (310, 82)], fill=255)
    kat.alpha_composite(cizili(B, flama, (52, 54, 66, 255), (22, 24, 32, 255), 7))

    govde = lambda d2: d2.polygon(
        [(48, 296), (96, 388), (452, 388), (556, 310), (584, 252), (532, 292), (48, 292)], fill=255)
    kat.alpha_composite(cizili(B, govde, (76, 42, 56, 255), (40, 20, 30, 255), 11))
    d = ImageDraw.Draw(kat)
    d.line([(64, 324), (532, 322)], fill=(30, 14, 22, 255), width=7)
    for x in (150, 230, 310, 390):                                          # kurek yariklari
        d.rectangle([x, 344, x + 42, 360], fill=(24, 12, 18, 255))

    kopuk = lambda d2: d2.ellipse([36, 368, 584, 414], fill=255)
    kat.alpha_composite(cizili(B, kopuk, (238, 248, 255, 190), (192, 222, 242, 170), 6))
    return parlat(kat, lambda dd: dd.ellipse([90, 300, 340, 356], fill=255), 34)


def canavar():
    """Deniz canavari: iki hortum, dikenli sirt, acik agizli kafa."""
    B = (640, 440)
    kat = Image.new("RGBA", B, (0, 0, 0, 0))
    UST, ALT = (52, 130, 96, 255), (24, 74, 58, 255)

    def hortumlar(d):
        d.pieslice([40, 210, 260, 430], 180, 360, fill=255)     # kuyruk hortumu
        d.pieslice([190, 150, 450, 420], 180, 360, fill=255)    # ana hortum
        d.rectangle([420, 190, 512, 400], fill=255)             # boyun
        d.rounded_rectangle([432, 96, 616, 218], 52, fill=255)  # kafa
        d.polygon([(596, 190), (640, 232), (560, 246), (596, 190)], fill=255)  # ust cene ucu
        d.polygon([(468, 218), (612, 258), (472, 292)], fill=255)              # alt cene
    kat.alpha_composite(cizili(B, hortumlar, UST, ALT, 11))

    # Sirt dikenleri.
    def dikenler(d):
        for i, (x, y) in enumerate([(96, 232), (156, 200), (250, 158), (318, 142), (388, 158)]):
            d.polygon([(x, y), (x + 34, y - 44 - (i % 2) * 10), (x + 62, y)], fill=255)
    kat.alpha_composite(cizili(B, dikenler, (86, 180, 130, 255), (44, 110, 82, 255), 8))

    d = ImageDraw.Draw(kat)
    # Dis ve goz.
    for x in (500, 540, 578):
        d.polygon([(x, 226), (x + 14, 254), (x + 26, 226)], fill=(250, 250, 240, 255),
                  outline=KONTUR, width=3)
    d.ellipse([516, 128, 566, 178], fill=(244, 208, 60, 255), outline=KONTUR, width=6)
    d.ellipse([536, 138, 552, 170], fill=(20, 24, 30, 255))
    return parlat(kat, lambda dd: dd.ellipse([230, 190, 400, 300], fill=255), 36)


def enkaz():
    """Sahipsiz enkaz: yana yatmis govde, kirik direk, sarkan yelken parcasi."""
    B = (620, 440)
    kat = Image.new("RGBA", B, (0, 0, 0, 0))

    kirik = lambda d: d.rectangle([296, 96, 318, 300], fill=255)
    kat.alpha_composite(cizili(B, kirik, (96, 82, 62, 255), (62, 52, 40, 255), 8))
    seren = lambda d: d.polygon([(306, 108), (462, 196), (448, 222), (300, 138)], fill=255)
    kat.alpha_composite(cizili(B, seren, (96, 82, 62, 255), (58, 48, 38, 255), 8))
    parca = lambda d: d.polygon([(380, 160), (452, 204), (430, 300), (398, 262), (376, 306), (362, 200)], fill=255)
    kat.alpha_composite(cizili(B, parca, (206, 198, 180, 255), (150, 144, 130, 255), 7))

    govde = lambda d: d.polygon(
        [(52, 292), (108, 392), (470, 392), (556, 316), (580, 262), (532, 292), (52, 288)], fill=255)
    kat.alpha_composite(cizili(B, govde, (128, 104, 74, 255), (78, 62, 44, 255), 11))
    d = ImageDraw.Draw(kat)
    d.line([(70, 322), (528, 318)], fill=(60, 48, 34, 255), width=7)
    d.ellipse([170, 330, 240, 380], fill=(28, 22, 18, 255), outline=KONTUR, width=6)   # yara
    d.ellipse([330, 340, 382, 382], fill=(28, 22, 18, 255), outline=KONTUR, width=6)

    kopuk = lambda d2: d2.ellipse([40, 372, 580, 416], fill=255)
    kat.alpha_composite(cizili(B, kopuk, (236, 246, 252, 180), (190, 218, 238, 160), 6))
    # Butun kati hafifce yatir: enkaz dik durmaz.
    kat = kat.rotate(-7, resample=Image.BICUBIC, center=(310, 330))
    return kat


def gulle():
    B = (96, 96)
    kat = Image.new("RGBA", B, (0, 0, 0, 0))
    top = lambda d: d.ellipse([10, 10, 86, 86], fill=255)
    kat.alpha_composite(cizili(B, top, (96, 102, 118, 255), (44, 48, 60, 255), 6))
    ImageDraw.Draw(kat).ellipse([26, 22, 46, 42], fill=(210, 218, 232, 200))
    return kat


def patlama():
    B = (240, 240)
    kat = Image.new("RGBA", B, (0, 0, 0, 0))
    def yildiz(d, cx, cy, dis, ic, n=8, doldur=255):
        p = []
        for i in range(n * 2):
            r = dis if i % 2 == 0 else ic
            a = math.pi * i / n - math.pi / 2
            p.append((cx + r * math.cos(a), cy + r * math.sin(a)))
        d.polygon(p, fill=doldur)
    kat.alpha_composite(cizili(B, lambda d: yildiz(d, 120, 120, 108, 44),
                               (255, 214, 74, 255), (232, 120, 24, 255), 7))
    ic = Image.new("RGBA", B, (0, 0, 0, 0))
    yildiz(ImageDraw.Draw(ic), 120, 120, 58, 26, doldur=(255, 252, 235, 255))
    kat.alpha_composite(ic)
    return kat


def kalkan():
    """SIPER'in gorseli: govdeye asilan tahta kalkan perdesi."""
    B = (300, 320)
    kat = Image.new("RGBA", B, (0, 0, 0, 0))
    govde = lambda d: d.rounded_rectangle([36, 30, 264, 290], 60, fill=255)
    kat.alpha_composite(cizili(B, govde, (176, 132, 78, 255), (118, 84, 48, 255), 10))
    d = ImageDraw.Draw(kat)
    for x in (110, 186):
        d.line([(x, 44), (x, 278)], fill=(104, 72, 40, 255), width=8)
    cember = lambda d2: d2.ellipse([106, 116, 194, 204], fill=255)
    kat.alpha_composite(cizili(B, cember, (206, 210, 222, 255), (140, 146, 162, 255), 8))
    return kat


def kanca():
    B = (140, 140)
    kat = Image.new("RGBA", B, (0, 0, 0, 0))
    def ciz(d):
        d.rectangle([62, 16, 80, 66], fill=255)
        d.arc([28, 40, 112, 124], 300, 200, fill=255, width=18)
        d.polygon([(28, 82), (14, 118), (52, 102)], fill=255)
    kat.alpha_composite(cizili(B, ciz, (208, 214, 228, 255), (120, 128, 146, 255), 7))
    return kat


def dalga():
    """Yatay dongulu dalga seridi: 64 px periyot, iki ton."""
    B = (1024, 150)
    kat = Image.new("RGBA", B, (0, 0, 0, 0))
    d = ImageDraw.Draw(kat)
    d.rectangle([0, 84, 1024, 150], fill=(24, 82, 122, 255))
    for k in range(-1, 17):
        cx = 32 + k * 64
        d.ellipse([cx - 44, 44, cx + 44, 132], fill=(24, 82, 122, 255))
    for k in range(-1, 17):
        cx = 32 + k * 64
        d.arc([cx - 44, 44, cx + 44, 132], 200, 340, fill=(150, 208, 236, 255), width=10)
    return kat


def bulut():
    B = (360, 190)
    kat = Image.new("RGBA", B, (0, 0, 0, 0))
    def ciz(d):
        d.ellipse([20, 80, 160, 170], fill=255)
        d.ellipse([90, 34, 250, 160], fill=255)
        d.ellipse([190, 70, 336, 168], fill=255)
        d.rectangle([50, 120, 310, 168], fill=255)
    kat.alpha_composite(cizili(B, ciz, (255, 255, 255, 235), (214, 226, 240, 225), 7))
    return kat


def alev():
    """Alev gemisi: komurlesmis govde, guvertede harlayan alev sutunu."""
    B = (600, 470)
    kat = Image.new("RGBA", B, (0, 0, 0, 0))

    direk = lambda d: d.rectangle([286, 120, 306, 330], fill=255)
    kat.alpha_composite(cizili(B, direk, (54, 44, 40, 255), (32, 26, 24, 255), 8))

    govde = lambda d: d.polygon(
        [(52, 316), (104, 408), (456, 408), (552, 330), (578, 274), (528, 310), (52, 312)], fill=255)
    kat.alpha_composite(cizili(B, govde, (70, 58, 54, 255), (36, 28, 26, 255), 11))
    d = ImageDraw.Draw(kat)
    d.line([(68, 344), (528, 340)], fill=(24, 18, 16, 255), width=7)
    for x in (150, 240, 330, 420):                                          # koz agizlari
        d.ellipse([x - 12, 352, x + 12, 376], fill=(255, 120, 30, 255), outline=KONTUR, width=5)

    # Alev sutunu: uc katman yalim.
    def yalim(d, cx, taban, tepe, gen):
        d.polygon([(cx - gen, taban), (cx - gen * 0.35, tepe + 40), (cx, tepe),
                   (cx + gen * 0.45, tepe + 52), (cx + gen, taban)], fill=255)
    kat.alpha_composite(cizili(B, lambda d: yalim(d, 296, 322, 96, 92),
                               (255, 150, 40, 255), (214, 62, 18, 255), 9))
    kat.alpha_composite(cizili(B, lambda d: yalim(d, 296, 318, 150, 56),
                               (255, 214, 80, 255), (240, 130, 30, 255), 6))
    ic = Image.new("RGBA", B, (0, 0, 0, 0))
    yalim(ImageDraw.Draw(ic), 296, 312, 196, 30)
    kat.alpha_composite(cizili(B, lambda d: yalim(d, 296, 312, 196, 30),
                               (255, 250, 220, 255), (255, 210, 90, 255), 4))
    # Kivilcimlar.
    d = ImageDraw.Draw(kat)
    for cx, cy, r in ((222, 150, 9), (368, 122, 8), (330, 78, 7), (252, 96, 6)):
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(255, 170, 60, 235), outline=KONTUR, width=3)

    kopuk = lambda d2: d2.ellipse([40, 388, 578, 432], fill=255)
    kat.alpha_composite(cizili(B, kopuk, (238, 248, 255, 190), (192, 222, 242, 170), 6))
    return kat


def hayalet():
    """Hayalet gemi: soluk camgobegi govde, lime lime yelken, yari saydam."""
    B = (600, 460)
    kat = Image.new("RGBA", B, (0, 0, 0, 0))
    UST, ALT = (196, 244, 240, 255), (108, 168, 176, 255)

    def yelken(d):
        p = [(300, 70), (520, 104)]
        for i in range(5):
            p.append((520 - i * 12, 136 + i * 30))
            p.append((492 - i * 12, 154 + i * 30))
        p.append((300, 282))
        d.polygon(p, fill=255)
    kat.alpha_composite(cizili(B, yelken, (222, 250, 248, 255), (150, 200, 206, 255), 8))
    d = ImageDraw.Draw(kat)
    d.line([(352, 88), (344, 276)], fill=(128, 178, 184, 255), width=6)

    direk = lambda d2: d2.rectangle([288, 30, 308, 306], fill=255)
    kat.alpha_composite(cizili(B, direk, (150, 198, 200, 255), (100, 148, 154, 255), 8))

    govde = lambda d2: d2.polygon(
        [(50, 292), (100, 384), (452, 384), (552, 306), (580, 250), (530, 288), (50, 288)], fill=255)
    kat.alpha_composite(cizili(B, govde, UST, ALT, 11))
    d = ImageDraw.Draw(kat)
    d.line([(66, 320), (528, 316)], fill=(96, 146, 154, 255), width=7)
    for x in (170, 270, 370):                                              # bos lombarlar
        d.ellipse([x - 13, 330, x + 13, 356], fill=(46, 84, 96, 255), outline=KONTUR, width=5)

    # Govdeden sizan parilti.
    kat = parlat(kat, lambda dd: dd.ellipse([120, 280, 460, 360], fill=255), 52)

    # Yari saydamlik: hayalet gorunur ama arkasi da gorunur.
    a = kat.getchannel("A").point(lambda v: int(v * 0.82))
    kat.putalpha(a)
    return kat


def _ikon_zemin(B):
    return Image.new("RGBA", B, (0, 0, 0, 0))


def ikon_top():
    """Yuva ikonu: tekerlekli guverte topu."""
    B = (220, 220)
    kat = _ikon_zemin(B)
    def ciz(d):
        d.rectangle([44, 84, 196, 128], fill=255)          # namlu
        d.rectangle([24, 74, 70, 138], fill=255)           # kama
        d.ellipse([64, 122, 140, 198], fill=255)           # tekerlek
    kat.alpha_composite(cizili(B, ciz, (110, 118, 134, 255), (58, 62, 76, 255), 8))
    d = ImageDraw.Draw(kat)
    d.ellipse([88, 146, 116, 174], fill=(200, 168, 96, 255), outline=KONTUR, width=5)
    return kat


def ikon_zirh():
    """Yuva ikonu: percinli zirh plakasi."""
    B = (220, 220)
    kat = _ikon_zemin(B)
    plaka = lambda d: d.rounded_rectangle([34, 30, 186, 190], 30, fill=255)
    kat.alpha_composite(cizili(B, plaka, (150, 158, 174, 255), (88, 94, 110, 255), 9))
    d = ImageDraw.Draw(kat)
    d.line([(110, 44), (110, 178)], fill=(70, 76, 92, 255), width=7)
    for x, y in ((58, 54), (162, 54), (58, 166), (162, 166)):
        d.ellipse([x - 9, y - 9, x + 9, y + 9], fill=(214, 220, 232, 255), outline=KONTUR, width=4)
    return kat


def ikon_durbun():
    """Yuva ikonu: capraz duran durbun."""
    B = (220, 220)
    kat = _ikon_zemin(B)
    def ciz(d):
        d.polygon([(38, 158), (150, 46), (182, 78), (70, 190)], fill=255)  # govde
        d.polygon([(146, 34), (186, 74), (200, 60), (160, 20)], fill=255)  # agiz
    kat.alpha_composite(cizili(B, ciz, (188, 148, 84, 255), (120, 88, 48, 255), 8))
    d = ImageDraw.Draw(kat)
    d.line([(96, 100), (128, 132)], fill=(230, 206, 160, 255), width=8)
    d.ellipse([34, 154, 78, 198], fill=(140, 208, 232, 255), outline=KONTUR, width=6)
    return kat


def ikon_tilsim():
    """Yuva ikonu: kordonlu tilsim tasi."""
    B = (220, 220)
    kat = _ikon_zemin(B)
    d0 = ImageDraw.Draw(kat)
    kordon = lambda d: d.arc([54, 10, 166, 110], 200, 340, fill=255, width=14)
    kat.alpha_composite(cizili(B, kordon, (150, 110, 62, 255), (104, 74, 42, 255), 5))
    tas = lambda d: d.polygon([(110, 62), (176, 128), (110, 202), (44, 128)], fill=255)
    kat.alpha_composite(cizili(B, tas, (120, 210, 200, 255), (36, 130, 128, 255), 9))
    d = ImageDraw.Draw(kat)
    d.polygon([(110, 88), (150, 128), (110, 128)], fill=(226, 250, 246, 210))
    return kat


def yildiz():
    """Derece yildizi: kucuk, dolgun, altin."""
    B = (96, 96)
    kat = _ikon_zemin(B)
    def ciz(d):
        p = []
        for i in range(10):
            r = 40 if i % 2 == 0 else 17
            a = math.pi * i / 5 - math.pi / 2
            p.append((48 + r * math.cos(a), 48 + r * math.sin(a)))
        d.polygon(p, fill=255)
    kat.alpha_composite(cizili(B, ciz, (255, 216, 84, 255), (232, 148, 16, 255), 6))
    return kat


def main():
    HEDEF.mkdir(parents=True, exist_ok=True)
    for ad, uret in (("gemi", gemi), ("korsan", korsan), ("canavar", canavar), ("enkaz", enkaz),
                     ("alev", alev), ("hayalet", hayalet),
                     ("gulle", gulle), ("patlama", patlama), ("kalkan", kalkan), ("kanca", kanca),
                     ("dalga", dalga), ("bulut", bulut),
                     ("ikon_top", ikon_top), ("ikon_zirh", ikon_zirh),
                     ("ikon_durbun", ikon_durbun), ("ikon_tilsim", ikon_tilsim),
                     ("yildiz", yildiz)):
        im = uret()
        yol = HEDEF / (ad + ".png")
        im.save(yol)
        print("%-10s %dx%-4d -> %s" % (ad + ".png", im.width, im.height, yol.name))


if __name__ == "__main__":
    main()
