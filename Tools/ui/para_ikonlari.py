# -*- coding: utf-8 -*-
"""Nakit ikonlarinin ucu: magaza hizlandirici kartlari, bedava odul ve gunluk odul icin.

Oyundan altin kalkinca sikke yigini ikonu (ikon_sikke_yigin, ikon_altin) her yerde
yanlis parayi anlatmaya basladi. Kart gorsellerini ureten para_paketleri.py ile ayni
kaynaktan, ayni kontur boruhattiyla, ama kart zemini olmadan tek basina ikon uretir --
cunku bu uc yer 240x240'lik bir yuvaya ikon basiyor, kart degil.

Uc kademe yeter: kaynaktaki ilk uc gorsel (tek banknot, bantli deste, deste + sikke).
Kazanc buyudukce ikon da buyusun diye sirayla kullanilirlar.

mavi_set.py deseni: hepsi stdlib, PIL yok (bkz. README).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import png_io
import para_paketleri as P

KOK = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
IKONLAR = os.path.join(KOK, "Assets", "Art", "UI", "Ikonlar")

TUVAL = 240             # oyundaki oteki ikonlarla ayni kare
KONTUR = 9              # 240'lik tuvalde kartlardakiyle ayni kalinlikta durur
PAY = 10                # tuvalin kenariyla kontur arasinda kalan bosluk

KADEMELER = ["ikon_nakit_1", "ikon_nakit_2", "ikon_nakit_3"]


def main():
    kw, kh, kpx = P.M.oku(P.KAYNAK)
    print("kaynak %dx%d" % (kw, kh))

    for i in range(len(KADEMELER)):
        ad = KADEMELER[i]
        iw, ih, ipx = P.hucre(kw, kh, kpx, i)

        # Kontur disariya tastigi icin olcek hesabi ondan once yapilir.
        ic = TUVAL - 2 * (PAY + KONTUR)
        k = min(ic / float(iw), ic / float(ih))
        iw, ih, ipx = png_io.olcekle(iw, ih, ipx, int(round(iw * k)), int(round(ih * k)))
        iw, ih, ipx = P.konturla(iw, ih, ipx, KONTUR)

        tuval = bytearray(TUVAL * TUVAL * 4)
        png_io.bindir(TUVAL, TUVAL, tuval, iw, ih, ipx,
                      (TUVAL - iw) // 2, (TUVAL - ih) // 2)
        png_io.yaz(os.path.join(IKONLAR, ad + ".png"), TUVAL, TUVAL, tuval)
        print("  %-16s %3dx%-3d" % (ad, iw, ih))


if __name__ == "__main__":
    main()
