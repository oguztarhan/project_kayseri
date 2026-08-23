"""Mavi/beyaz UI setini kaynak PNG'lerden kesip Assets/Art/UI/MaviSet altina yazar.

Kaynaklar tek tip degil, bu yuzden uc ayri sorun var:

1. Bir kismi duzgun alfa tasiyor (panel, carpi butonlari, ayar ikonlari, dolar) -- sadece
   kirpilip olceklenmeleri yetiyor.
2. Bir kisminda alfa hic yok; seffaflik dama deseni piksel olarak gomulu (ustabasi butonu,
   beyaz kutu panel). Dama notr gri -- #FEFEFE ile #F3F3F3 arasi, kanal farki 1'i gecmiyor --
   asil gorselin kenari ise ya mavimsi (227,233,246) ya lacivert (5,8,16). Kenardan flood-fill
   ile ayrilabiliyor; icerideki beyaz govdeye sizmiyor cunku cerceve halkasi yolu kesiyor.
3. Kirpilan her sprite'in seffaf piksellerinde kaynagin arka plan rengi duruyor. Unity bunu
   bilinear orneklerken kenara sizdiriyor -- ikonun etrafinda beyaz ya da koyu bir hale.
   Opak renkleri seffaf tarafa tasirmak bunu kokten bitiriyor.

Calistirma:  python Tools/ui/mavi_set.py
"""
import glob
import os
import struct
import sys
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import png_io

KOK = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
INDIR = r"C:\Users\kenan\Downloads"
HEDEF = os.path.join(KOK, "Assets", "Art", "UI", "MaviSet")


# ---------------------------------------------------------------- okuma

def oku_rgb(yol):
    """Alfasiz (renk tipi 2) PNG -> (w, h, RGBA bytearray, alfa hepsi 255)."""
    ham = open(yol, "rb").read()
    i = 8
    w = h = bit = renk = 0
    veri = b""
    while i < len(ham):
        boy = struct.unpack(">I", ham[i:i + 4])[0]
        tip = ham[i + 4:i + 8]
        govde = ham[i + 8:i + 8 + boy]
        if tip == b"IHDR":
            w, h, bit, renk = struct.unpack(">IIBB", govde[:10])
        elif tip == b"IDAT":
            veri += govde
        i += 12 + boy
    if bit != 8 or renk != 2:
        raise ValueError("sadece 8-bit RGB: %s (bit=%d renk=%d)" % (yol, bit, renk))

    duz = zlib.decompress(veri)
    bpp = 3
    satir = w * bpp
    ham3 = bytearray(w * h * bpp)
    onceki = bytearray(satir)
    p = 0
    for y in range(h):
        f = duz[p]
        p += 1
        cur = bytearray(duz[p:p + satir])
        p += satir
        if f == 1:
            for x in range(bpp, satir):
                cur[x] = (cur[x] + cur[x - bpp]) & 255
        elif f == 2:
            for x in range(satir):
                cur[x] = (cur[x] + onceki[x]) & 255
        elif f == 3:
            for x in range(satir):
                a = cur[x - bpp] if x >= bpp else 0
                cur[x] = (cur[x] + (a + onceki[x]) // 2) & 255
        elif f == 4:
            for x in range(satir):
                a = cur[x - bpp] if x >= bpp else 0
                b = onceki[x]
                c = onceki[x - bpp] if x >= bpp else 0
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                cur[x] = (cur[x] + pr) & 255
        ham3[y * satir:(y + 1) * satir] = cur
        onceki = cur

    px = bytearray(w * h * 4)
    for i in range(w * h):
        px[i * 4] = ham3[i * 3]
        px[i * 4 + 1] = ham3[i * 3 + 1]
        px[i * 4 + 2] = ham3[i * 3 + 2]
        px[i * 4 + 3] = 255
    return w, h, px


def oku(yol):
    """Renk tipi ne olursa olsun RGBA dondurur."""
    try:
        return png_io.oku(yol)
    except ValueError:
        return oku_rgb(yol)


# ---------------------------------------------------------------- dama keyleme

def _integral(w, h, mask):
    """(w+1)x(h+1) toplam tablosu -- pencere sayimini O(1) yapar."""
    it = [0] * ((w + 1) * (h + 1))
    for y in range(h):
        satir = 0
        ust = (y) * (w + 1)
        alt = (y + 1) * (w + 1)
        for x in range(w):
            satir += mask[y * w + x]
            it[alt + x + 1] = it[ust + x + 1] + satir
    return it


def _pencere(it, w, x0, y0, x1, y1):
    a = (y1 + 1) * (w + 1)
    b = y0 * (w + 1)
    return it[a + x1 + 1] - it[a + x0] - it[b + x1 + 1] + it[b + x0]


def maske_duzelt(w, h, mask, r=5):
    """Ikili maskeye cogunluk filtresi -- benekleri siler, sinirini duzeltir.

    Yumusak dis parlamanin uzerinde dama deseni oldugu icin flood-fill sinirda tirtikli
    birakiyor: parlak kareler arka plan sayiliyor, koyu kareler sayilmiyor. Pencerenin
    cogunlugunu almak o tirtigi tek gecise siliyor.
    """
    it = _integral(w, h, mask)
    yeni = bytearray(w * h)
    yarim = ((2 * r + 1) ** 2) // 2
    for y in range(h):
        y0, y1 = max(0, y - r), min(h - 1, y + r)
        for x in range(w):
            x0, x1 = max(0, x - r), min(w - 1, x + r)
            alan = (x1 - x0 + 1) * (y1 - y0 + 1)
            sayi = _pencere(it, w, x0, y0, x1, y1)
            esik = yarim if alan == (2 * r + 1) ** 2 else alan // 2
            yeni[y * w + x] = 1 if sayi > esik else 0
    return yeni


def maske_bulanik(w, h, mask, r=2):
    """Ikili maskeden 0..255 arasi yumusak alfa -- kesim kenarini kirilgan olmaktan cikarir."""
    it = _integral(w, h, mask)
    out = bytearray(w * h)
    for y in range(h):
        y0, y1 = max(0, y - r), min(h - 1, y + r)
        for x in range(w):
            x0, x1 = max(0, x - r), min(w - 1, x + r)
            alan = (x1 - x0 + 1) * (y1 - y0 + 1)
            out[y * w + x] = (_pencere(it, w, x0, y0, x1, y1) * 255) // alan
    return out


def dama_kes(w, h, px, alt=232, fark=8, duzelt=5, yumusak=2):
    """Kenardan flood-fill ile gomulu dama desenini seffaf yapar. px yerinde degisir.

    Notr ve parlak olan her piksel arka plan adayi; ama sadece disaridan yuruyerek
    ulasilabilenler siliniyor, boylece gorselin icindeki beyaz alanlar korunuyor.
    """
    def arka_mi(i):
        r, g, b = px[i], px[i + 1], px[i + 2]
        return min(r, g, b) >= alt and max(r, g, b) - min(r, g, b) <= fark

    gorulen = bytearray(w * h)
    yigin = []
    for x in range(w):
        for y in (0, h - 1):
            k = y * w + x
            if not gorulen[k] and arka_mi(k * 4):
                gorulen[k] = 1
                yigin.append(k)
    for y in range(h):
        for x in (0, w - 1):
            k = y * w + x
            if not gorulen[k] and arka_mi(k * 4):
                gorulen[k] = 1
                yigin.append(k)

    while yigin:
        k = yigin.pop()
        x = k % w
        y = k // w
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            if 0 <= nx < w and 0 <= ny < h:
                nk = ny * w + nx
                if not gorulen[nk] and arka_mi(nk * 4):
                    gorulen[nk] = 1
                    yigin.append(nk)

    if duzelt:
        gorulen = maske_duzelt(w, h, gorulen, duzelt)
    alfa = maske_bulanik(w, h, gorulen, yumusak) if yumusak else None
    for k in range(w * h):
        px[k * 4 + 3] = 255 - alfa[k] if alfa is not None else (0 if gorulen[k] else 255)
    return px


# ---------------------------------------------------------------- kirpma / tuval

def sinirlar(w, h, px, esik=8):
    """Alfasi esigin ustundeki piksellerin sinir kutusu -> (x0, y0, x1, y1)."""
    x0, y0, x1, y1 = w, h, -1, -1
    for y in range(h):
        satir = y * w
        for x in range(w):
            if px[(satir + x) * 4 + 3] > esik:
                if x < x0:
                    x0 = x
                if x > x1:
                    x1 = x
                if y < y0:
                    y0 = y
                if y > y1:
                    y1 = y
    if x1 < 0:
        raise ValueError("tamamen seffaf")
    return x0, y0, x1, y1


def kirp(w, h, px, kutu):
    x0, y0, x1, y1 = kutu
    nw, nh = x1 - x0 + 1, y1 - y0 + 1
    yeni = bytearray(nw * nh * 4)
    for y in range(nh):
        k = ((y + y0) * w + x0) * 4
        yeni[y * nw * 4:(y + 1) * nw * 4] = px[k:k + nw * 4]
    return nw, nh, yeni


def sikistir(w, h, px, esik=8):
    """Seffaf kenar payini atar."""
    return kirp(w, h, px, sinirlar(w, h, px, esik))


def tuval(w, h, px, tw, th, oran=0.93):
    """Icerigi (tw x th) tuvale, en-boyu bozmadan, ortalayarak oturtur.

    oran, icerigin tuvalin ne kadarini kaplayacagi. Mevcut buton sprite'lari 0,93 civari
    dolduruyor; yeni set aynisini tutmazsa ayni rect icinde daha buyuk gorunur.
    """
    hedef_w, hedef_h = tw * oran, th * oran
    olcek = min(hedef_w / w, hedef_h / h)
    iw, ih = max(1, int(round(w * olcek))), max(1, int(round(h * olcek)))
    _, _, kucuk = png_io.olcekle(w, h, px, iw, ih)

    yeni = bytearray(tw * th * 4)
    png_io.bindir(tw, th, yeni, iw, ih, kucuk, (tw - iw) // 2, (th - ih) // 2)
    return tw, th, yeni


# ---------------------------------------------------------------- kenar tasirma

def tasir(w, h, px, adim=4):
    """Opak piksellerin rengini seffaf tarafa yayar (renk hale onleme).

    Alfaya dokunmaz; sadece alfasi dusuk piksellerin RGB'sini komsudan doldurur. Cephe
    listesiyle yuruyor, tum goruntuyu adim sayisi kadar taramiyor.
    """
    dolu = bytearray(w * h)
    cephe = []
    for k in range(w * h):
        if px[k * 4 + 3] > 16:
            dolu[k] = 1
            cephe.append(k)

    for _ in range(adim):
        yeni_cephe = []
        for k in cephe:
            x = k % w
            y = k // w
            for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                if not (0 <= nx < w and 0 <= ny < h):
                    continue
                nk = ny * w + nx
                if dolu[nk]:
                    continue
                dolu[nk] = 2
                px[nk * 4] = px[k * 4]
                px[nk * 4 + 1] = px[k * 4 + 1]
                px[nk * 4 + 2] = px[k * 4 + 2]
                yeni_cephe.append(nk)
        for k in yeni_cephe:
            dolu[k] = 1
        cephe = yeni_cephe
        if not cephe:
            break
    return px


# ---------------------------------------------------------------- sprite sayfasi bolme

def bantlar(proj, esik):
    """Projeksiyonda esigi asan araliklari dondurur -> [(bas, son), ...]."""
    out = []
    bas = None
    for i, v in enumerate(proj):
        if v > esik and bas is None:
            bas = i
        elif v <= esik and bas is not None:
            out.append((bas, i - 1))
            bas = None
    if bas is not None:
        out.append((bas, len(proj) - 1))
    return out


def sayfayi_bol(w, h, px, esik=8):
    """Sprite sayfasini satir/sutun bosluklarindan ayirir -> [(x0, y0, x1, y1), ...].

    Sayfada ikonlar iki satir halinde ve aralarinda tam seffaf koridorlar var; alfa
    projeksiyonu bunu dogrudan veriyor, elle koordinat girmeye gerek kalmiyor.
    """
    satir_proj = [0] * h
    for y in range(h):
        s = 0
        for x in range(w):
            if px[(y * w + x) * 4 + 3] > esik:
                s += 1
        satir_proj[y] = s

    kutular = []
    for y0, y1 in bantlar(satir_proj, 0):
        sutun_proj = [0] * w
        for y in range(y0, y1 + 1):
            for x in range(w):
                if px[(y * w + x) * 4 + 3] > esik:
                    sutun_proj[x] += 1
        for x0, x1 in bantlar(sutun_proj, 0):
            # bant icindeki gercek dikey sinirlari daralt
            ky0, ky1 = y1, y0
            for y in range(y0, y1 + 1):
                for x in range(x0, x1 + 1):
                    if px[(y * w + x) * 4 + 3] > esik:
                        if y < ky0:
                            ky0 = y
                        if y > ky1:
                            ky1 = y
                        break
            kutular.append((x0, ky0, x1, ky1))
    return kutular


# ---------------------------------------------------------------- boru hatti

def yaz(ad, w, h, px, klasor=None):
    yol = os.path.join(klasor or HEDEF, ad)
    os.makedirs(os.path.dirname(yol), exist_ok=True)
    boy = png_io.yaz(yol, w, h, px)
    print("  %-26s %4dx%-4d %6d B" % (ad, w, h, boy))


def isle(kaynak, dama=False, hedef_boy=None, oran=0.93, en_boy=None):
    """Oku -> (gerekirse) dama kes -> kirp -> tasir -> olcek/tuval."""
    w, h, px = oku(kaynak)
    if dama:
        dama_kes(w, h, px)
    w, h, px = sikistir(w, h, px)
    tasir(w, h, px)
    if en_boy is not None:
        return tuval(w, h, px, en_boy[0], en_boy[1], oran)
    if hedef_boy is not None:
        return tuval(w, h, px, hedef_boy, hedef_boy, oran)
    return w, h, px


def olcekli(kaynak, uzun_kenar, dama=False):
    """Kirpilmis icerigi tuvalsiz, en-boyu koruyarak uzun kenara indirir."""
    w, h, px = oku(kaynak)
    if dama:
        dama_kes(w, h, px)
    w, h, px = sikistir(w, h, px)
    tasir(w, h, px)
    olcek = uzun_kenar / float(max(w, h))
    if olcek >= 1.0:
        return w, h, px
    return png_io.olcekle(w, h, px, max(1, int(round(w * olcek))), max(1, int(round(h * olcek))))


# Harita vitrini: ada sirasi komur, bakir, demir, gumus, altin, yakut, zumrut, elmas.
ADA_GORSELLERI = [
    ("komur", "coal-original-selected"),
    ("bakir", "copper-original-selected"),
    ("demir", "iron-foundry-yard-selected"),
    ("gumus", "silver-original-selected"),
    ("altin", "gold-premium-selected"),
    ("yakut", "ruby-centered-bridge-selected"),
    ("zumrut", "emerald-tree-root-selected"),
    ("elmas", "diamond-terraced-mine-selected-alt"),
]

# Alt seritteki cevher rozetleri. Bakirin sade surumu sette yok; vitrin gorseli kucultulerek
# kullaniliyor -- zincirdeki 64 pikselde ikisi de ayni okunuyor.
CEVHER_ROZETLERI = [
    ("komur", "coal"),
    ("bakir", "copper-original-selected"),
    ("demir", "iron"),
    ("gumus", "silver"),
    ("altin", "gold"),
    ("yakut", "ruby"),
    ("zumrut", "emerald"),
    ("elmas", "diamond"),
]

AYAR_IKON_ADLARI = [
    # sayfadaki gorsel sira: ust satir soldan saga, sonra alt satir
    "ikon_muzik", "ikon_titresim", "ikon_ses", "ikon_dil",
    "ikon_yildiz", "ikon_gizlilik", "ikon_geriyukle",
]


def main():
    p = lambda *a: os.path.join(INDIR, *a)

    def tek(desen):
        """Dosya adlarinda Turkce karakter var; saat damgasiyla eslestiriyoruz."""
        bulunan = glob.glob(os.path.join(INDIR, "panel ve butonlar", "*" + desen + "*.png"))
        if len(bulunan) != 1:
            raise SystemExit("'%s' icin %d dosya: %s" % (desen, len(bulunan), bulunan))
        return bulunan[0]

    print("MaviSet ->", HEDEF)

    # --- paneller ve butonlar
    yaz("panel_mavi.png", *olcekli(p("panel ve butonlar", "ui-reference-1-universal-panel-v2.png"), 660))
    yaz("panel_beyaz.png", *olcekli(tek("14_13_26"), 620, dama=True))
    yaz("btn_mavi.png", *olcekli(p("panel ve butonlar", "button..png"), 640))
    yaz("madalyon_mavi.png", *olcekli(p("panel ve butonlar", "ui-reference-1-white-center-medallion.png"), 300))

    # carpi butonlarindan ilki: kirmizi disk, beyaz X
    w, h, px = oku(tek("14_32_00"))
    kutular = sayfayi_bol(w, h, px)
    print("  carpi sayfasi:", kutular)
    kw, kh, kpx = kirp(w, h, px, kutular[0])
    tasir(kw, kh, kpx)
    yaz("btn_kapat_yeni.png", *tuval(kw, kh, kpx, 300, 300, 0.96))

    # --- dolar ikonu (HUD altin gostergesi)
    # Kare tuvale oturtulmuyor: deste 1,54 en/boy oraninda, kareye sigdirilinca ustunde
    # altinda seffaf bant kaliyor ve ayni rect'te altin ikonunun ucte ikisi kadar gorunuyor.
    yaz("ikon_dolar.png", *olcekli(p("dollarssss"), 240))

    # --- ayar ikonlari
    w, h, px = oku(p("panel ve butonlar", "settings-icons-blue-white-sprite-sheet.png"))
    kutular = sayfayi_bol(w, h, px)
    print("  ayar sayfasi: %d parca" % len(kutular), kutular)
    if len(kutular) != len(AYAR_IKON_ADLARI):
        raise SystemExit("beklenen %d ikon, bulunan %d" % (len(AYAR_IKON_ADLARI), len(kutular)))
    for ad, kutu in zip(AYAR_IKON_ADLARI, kutular):
        kw, kh, kpx = kirp(w, h, px, kutu)
        tasir(kw, kh, kpx)
        yaz(ad + ".png", *tuval(kw, kh, kpx, 240, 240, 0.94))

    # --- harita: ada vitrin gorselleri ve alt serit rozetleri
    cev = os.path.join(INDIR, "mineral-icons-transparent")
    for ad, dosya in ADA_GORSELLERI:
        yaz("ada_" + ad + ".png", *olcekli(os.path.join(cev, dosya + ".png"), 512), klasor=os.path.join(HEDEF, "Adalar"))
    for ad, dosya in CEVHER_ROZETLERI:
        yaz("cevher_" + ad + ".png", *isle(os.path.join(cev, dosya + ".png"), hedef_boy=192, oran=0.94),
            klasor=os.path.join(HEDEF, "Adalar"))

    # --- Resources'taki iki ray butonu: dosyanin uzerine yaziliyor, .meta ve GUID duruyor
    res = os.path.join(KOK, "Assets", "Resources", "UI", "Buttons")
    # Elle temizlenmis surumler: ikisi de duzgun alfa tasiyor, dama keylemeye gerek yok.
    yaz("gorev.png", *isle(p("achievement-button-clean.png"), hedef_boy=512, oran=0.93), klasor=res)
    yaz("ustabasi.png", *isle(p("mining-foreman-hardhat-button-clean.png"), hedef_boy=512, oran=0.93), klasor=res)


if __name__ == "__main__":
    main()
