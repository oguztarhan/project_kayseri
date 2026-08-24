# -*- coding: utf-8 -*-
"""Nakit madalyonlari: gunluk odul karolari ve "tekrar hos geldin" kahramani.

Karolarin ve odul ekranlarinin ikonu bos degil, madalyonun icine oturtulmus bir
ikon: madalyon_elmas ve madalyon_altin boyle uretilmis. Oyundan altin kalkinca
altin madalyon yanlis parayi anlatir oldu; ayni cerceveye nakit ikonu koyuyoruz
ki karolar elmas gunlerininkiyle ayni parcadan yapilmis gibi dursun.

Cerceve madalyon.png (bos madalyon, 301px). Ikonun kaplayacagi kutu
madalyon_elmas'taki elmasin kutusundan olculdu: 195x180, merkez (229, 230) --
yani 460'lik madalyonda %45. Kahraman madalyonu ayni orani buyutulmus halde
kullaniyor.

mavi_set.py deseni: hepsi stdlib, PIL yok (bkz. README).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import png_io
import mavi_set as M

KOK = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
IKONLAR = os.path.join(KOK, "Assets", "Art", "UI", "Ikonlar")

# (cikti adi, madalyon boyu, ikon adi, ikon kutusunun madalyona orani)
MADALYONLAR = [
    ("madalyon_nakit",        460, "ikon_nakit_1", 0.446, 0.413),
    ("madalyon_kazanc_nakit", 909, "ikon_nakit_3", 0.560, 0.520),
]

# Bos madalyonun ic diski tam ortasinda degil: alt kenarindaki golge yuzunden
# merkez madalyon_elmas'ta (229, 230) yani %49,8 / %50,0.
MERKEZ = (0.498, 0.500)


def main():
    for ad, boy, ikon, oran_w, oran_h in MADALYONLAR:
        cw, ch, cpx = png_io.oku(os.path.join(IKONLAR, "madalyon.png"))
        cw, ch, cpx = png_io.olcekle(cw, ch, cpx, boy, boy)

        iw, ih, ipx = png_io.oku(os.path.join(IKONLAR, ikon + ".png"))
        iw, ih, ipx = M.sikistir(iw, ih, ipx, 4)      # tuvalin saydam payi olcegi bozmasin
        k = min(boy * oran_w / float(iw), boy * oran_h / float(ih))
        iw, ih, ipx = png_io.olcekle(iw, ih, ipx, int(round(iw * k)), int(round(ih * k)))

        kart = bytearray(cpx)
        png_io.bindir(boy, boy, kart, iw, ih, ipx,
                      int(boy * MERKEZ[0]) - iw // 2, int(boy * MERKEZ[1]) - ih // 2)
        png_io.yaz(os.path.join(IKONLAR, ad + ".png"), boy, boy, kart)
        print("%-22s %3dx%-3d  ikon %3dx%-3d" % (ad, boy, boy, iw, ih))


if __name__ == "__main__":
    main()
