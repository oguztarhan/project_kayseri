"""Beyaz panelin duzgun kesimi, mavi baslik seridi ve sekiz ustabasi portresi.

mavi_set.py deseni surduruyor: hepsi stdlib, PIL yok (bkz. README). Uc is var:

1. panel_beyaz -- eski kesim sol kenardan tirasliydi, yuvarlak kose duzlesip
   9-dilim kenar cizgisini ikiye katliyordu. Kullanicinin hazir kestigi PNG
   temiz; buradan sadece alfa sinirina kirpip olcekliyoruz.
2. serit_mavi -- uc seritli sayfadan (mavi/kirmizi/mor) ustteki mavi olan.
   Sayfa zaten alfali; sadece seffaf taraftaki koyu RGB'yi tasirmak gerekiyor,
   yoksa bilineer ornekleme kenara siyah hale birakiyor.
3. usta_1..8 -- portreler alfali geliyor ama genis seffaf pay ve siyah cerceve
   RGB'siyle; kirp + tasir + olcekle.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import png_io
import mavi_set as M

KOK = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
HEDEF = os.path.join(KOK, "Assets", "Art", "UI", "MaviSet")
PORTRE = os.path.join(KOK, "Assets", "Art", "UI", "Ustabasilar")

# Kaynaklar kullanicinin kendi klasorlerinde ve adlari Turkce. Python UTF-8 yollarla
# sorun yasamiyor -- ayni yollari kabuktan gecirmek yasiyor, o yuzden kopyalanmiyorlar.
EV = os.path.expanduser("~")
PANEL_KAYNAK = os.path.join(EV, "Downloads", "kesilmiş panel hazır")
SERIT_KAYNAK = os.path.join(EV, "OneDrive", "Desktop", "Uİ packages",
                            "Codex Görseli 21 Ağu 2026 14_54_40.png")
PORTRE_KLASOR = os.path.join(EV, "OneDrive", "Desktop", "ustabaşıları")

PORTRELER = [
    ("usta_1", "01-maden-ustabasi.png"),
    ("usta_2", "02-tren-ustabasi.png"),
    ("usta_3", "03-depo-ustabasi.png"),
    ("usta_4", "04-cevher-kamyonu-ustabasi.png"),
    ("usta_5", "05-fabrika-ustabasi.png"),
    ("usta_6", "06-yuk-kamyonu-ustabasi.png"),
    ("usta_7", "07-pazar-ustabasi.png"),
    ("usta_8", "08-enerji-santrali-ustabasi.png"),
]


def yaz(klasor, ad, w, h, px):
    os.makedirs(klasor, exist_ok=True)
    yol = os.path.join(klasor, ad + ".png")
    png_io.yaz(yol, w, h, px)
    print("  %-10s %4dx%-4d  %s" % (ad, w, h, yol))


def olcek(w, h, px, uzun):
    """En uzun kenari `uzun` olacak sekilde kucultur."""
    k = uzun / float(max(w, h))
    return png_io.olcekle(w, h, px, max(1, int(round(w * k))), max(1, int(round(h * k))))


def panel():
    w, h, px = M.oku(PANEL_KAYNAK)
    w, h, px = M.sikistir(w, h, px, 8)
    # Kose yaricapi kaynakta ~100 piksel; olcek sonrasi kenar payini oradan tureterek
    # 9-dilim sinirini veriyoruz, yoksa egri kenar dilimine tasip cizgi haline geliyor.
    hedef_h = 286
    k = hedef_h / float(h)
    nw, nh, kucuk = png_io.olcekle(w, h, px, int(round(w * k)), hedef_h)
    yaz(HEDEF, "panel_beyaz", nw, nh, kucuk)
    print("     onerilen spriteBorder = %d" % int(round(100 * k)) + " (+pay)")


def serit():
    w, h, px = M.oku(SERIT_KAYNAK)
    # Sayfadaki uc seridi alfa projeksiyonundan ayiriyoruz; ilki mavi olan.
    satir = [0] * h
    for y in range(h):
        b = y * w
        satir[y] = sum(1 for x in range(w) if px[(b + x) * 4 + 3] > 24)
    bant = M.bantlar(satir, w // 200)
    y0, y1 = bant[0]
    x0, x1 = w, -1
    for y in range(y0, y1 + 1):
        b = y * w
        for x in range(w):
            if px[(b + x) * 4 + 3] > 24:
                x0 = min(x0, x)
                x1 = max(x1, x)
    cw, ch, cpx = M.kirp(w, h, px, (x0, y0, x1, y1))
    M.tasir(cw, ch, cpx, 6)
    nw, nh, kucuk = olcek(cw, ch, cpx, 1024)
    yaz(HEDEF, "serit_mavi", nw, nh, kucuk)


def portreler():
    for ad, dosya in PORTRELER:
        w, h, px = M.oku(os.path.join(PORTRE_KLASOR, dosya))
        w, h, px = M.sikistir(w, h, px, 8)
        M.tasir(w, h, px, 6)
        nw, nh, kucuk = olcek(w, h, px, 512)
        yaz(PORTRE, ad, nw, nh, kucuk)


def main():
    print("panel:")
    panel()
    print("serit:")
    serit()
    print("portreler:")
    portreler()


if __name__ == "__main__":
    main()
