"""Cubuk dolgusu ve grafit gosterge.

Iki is var:

1. bar_dolgu -- eski dolgu (slider_dolgu) kalin lacivert konturlu ayri bir kapsuldu:
   yatagin icine oturmuyor, uzerinde duruyordu; %10 dolulukta da kenarda lacivert bir
   topak birakiyordu. Yeni dolgu yatagin KENDI siluetinden uretiliyor -- ayni kapsul,
   ayni kose yaricapi, sadece rengi canli mavi. Dolunca yatakla birebir ortusuyor,
   yani cubuk "doluyor" gibi duruyor, uzerine ikinci bir sey binmiyor.

2. gosterge_grafit -- kaynak PNG 1911x823 ama kapsul icinde 1688x324'luk bir alanda;
   geri kalani saydam pay. Oldugu gibi kullanilinca kutunun icinde kucucuk kaliyor.
   Kirpip 9-dilim kenar payi veriyoruz.
"""
import colorsys
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import png_io
import mavi_set as M

KOK = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
GOSTERGE = os.path.join(KOK, "Assets", "Art", "UI", "Gostergeler")
MAVISET = os.path.join(KOK, "Assets", "Art", "UI", "MaviSet")


def yaz(klasor, ad, w, h, px, not_=""):
    yol = os.path.join(klasor, ad + ".png")
    png_io.yaz(yol, w, h, px)
    print("  %-20s %4dx%-4d %s" % (ad, w, h, not_))


def dolgu(ton, doygunluk):
    """Yatagi verilen tona boyar. Kenar cizgisi govdeden bir tik koyu kaliyor."""
    w, h, px = M.oku(os.path.join(GOSTERGE, "slider_yatak.png"))
    out = bytearray(px)
    for i in range(0, len(px), 4):
        if px[i + 3] == 0:
            continue
        r, g, b = px[i] / 255.0, px[i + 1] / 255.0, px[i + 2] / 255.0
        hh, s, v = colorsys.rgb_to_hsv(r, g, b)
        # Yatak acik gri-mavi: v 0.70-0.92 arasi. Araligi acip canli maviye tasiyoruz.
        nv = 0.42 + (v - 0.62) * 1.55
        nv = max(0.30, min(1.0, nv))
        nr, ng, nb = colorsys.hsv_to_rgb(ton, doygunluk, nv)
        out[i] = int(round(nr * 255))
        out[i + 1] = int(round(ng * 255))
        out[i + 2] = int(round(nb * 255))
    return w, h, out


def grafit(yukseklik=150):
    w, h, px = M.oku(os.path.join(GOSTERGE, "graphite-empty-pill-indicator.png"))
    w, h, px = M.sikistir(w, h, px, 8)
    M.tasir(w, h, px, 6)
    k = yukseklik / float(h)
    return png_io.olcekle(w, h, px, int(round(w * k)), yukseklik)


def main():
    print("cubuk:")
    w, h, px = dolgu(0.575, 0.82)
    yaz(GOSTERGE, "bar_dolgu", w, h, px, "kenar payi = 44/40, yatakla ayni")

    print("gosterge:")
    w, h, px = grafit()
    yaz(MAVISET, "gosterge_grafit", w, h, px, "kenar payi = %d" % (h // 2))


if __name__ == "__main__":
    main()
