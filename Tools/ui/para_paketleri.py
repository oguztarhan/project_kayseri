# -*- coding: utf-8 -*-
"""Magazadaki nakit paketlerinin kart gorselleri.

Oyundan altin kalktigi icin gold_2500..gold_1000000 kartlarindaki sikke yiginlari
yerine kullanicinin verdigi alti kademeli para gorseli geciyor. Kartin kendisi
degismiyor: bos mavi kart (elmas_nakit) zemin olarak alinip uzerine ikon
bindiriliyor, yani cerceve, nokta deseni, adet hapi ve fiyat seridi eskisiyle
birebir ayni yerde kaliyor.

Ikonlar yumusak 3B render; kartlardaki her sey kalin siyah konturlu. Kontursuz
birakilsa ayni ailenin parcasi gibi durmazdi, o yuzden mesafe donusumuyle
disariya siyah bir kontur cikariliyor. Boyutlar da altin kartlardaki gibi
kademe kademe buyuyor -- paket buyudukce gorsel de buyusun.

mavi_set.py deseni: hepsi stdlib, PIL yok (bkz. README).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import png_io
import mavi_set as M

KOK = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MAGAZA = os.path.join(KOK, "Assets", "Art", "UI", "Magaza")
ZEMIN = os.path.join(MAGAZA, "elmas_nakit.png")          # bos mavi kart
KAYNAK = os.path.join(os.path.expanduser("~"), "Downloads",
                      "para-paketleri-6-kademe-tek-dosya.png")

SUTUN, SATIR = 3, 2                 # kaynak sayfa 3x2
KONTUR = 7                          # siyah kontur kalinligi, piksel
KART_ORTA_X = 140

# Kademe basina (dis kutu genisligi, dis kutu yuksekligi, dikey merkez).
# Altin kartlarda gorsel 134px'ten 213px'e buyuyordu; ayni rampa korunuyor.
# Kutu konturu da kapsiyor: hapin ustune tasmasin diye alt sinir 232.
KADEMELER = [
    ("nakit_2500",    176, 132, 118),
    ("nakit_8000",    190, 144, 120),
    ("nakit_25000",   198, 160, 122),
    ("nakit_75000",   212, 178, 124),
    ("nakit_250000",  200, 198, 126),
    ("nakit_1000000", 214, 206, 128),
]

UZAK = 1 << 20


def hucre(w, h, px, indeks):
    """Sayfadan bir kademeyi kesip saydam paya kadar kirpar."""
    cw, ch = w // SUTUN, h // SATIR
    x0, y0 = (indeks % SUTUN) * cw, (indeks // SUTUN) * ch
    kw, kh, kpx = M.kirp(w, h, px, (x0, y0, x0 + cw - 1, y0 + ch - 1))
    kw, kh, kpx = M.sikistir(kw, kh, kpx, 8)
    M.tasir(kw, kh, kpx, 6)
    return kw, kh, kpx


def konturla(w, h, px, r):
    """Ikonun etrafina r piksel siyah kontur cikarir -> (nw, nh, RGBA).

    Chamfer 3-4 mesafe donusumu: disk taramasi 280x360'ta bile saniyeler
    suruyordu, bu iki gecis dogrusal.
    """
    nw, nh = w + 2 * r, h + 2 * r
    d = [UZAK] * (nw * nh)
    for y in range(h):
        for x in range(w):
            if px[(y * w + x) * 4 + 3] >= 96:
                d[(y + r) * nw + (x + r)] = 0

    for y in range(nh):
        for x in range(nw):
            i = y * nw + x
            en = d[i]
            if y > 0:
                if d[i - nw] + 3 < en: en = d[i - nw] + 3
                if x > 0 and d[i - nw - 1] + 4 < en: en = d[i - nw - 1] + 4
                if x < nw - 1 and d[i - nw + 1] + 4 < en: en = d[i - nw + 1] + 4
            if x > 0 and d[i - 1] + 3 < en: en = d[i - 1] + 3
            d[i] = en
    for y in range(nh - 1, -1, -1):
        for x in range(nw - 1, -1, -1):
            i = y * nw + x
            en = d[i]
            if y < nh - 1:
                if d[i + nw] + 3 < en: en = d[i + nw] + 3
                if x > 0 and d[i + nw - 1] + 4 < en: en = d[i + nw - 1] + 4
                if x < nw - 1 and d[i + nw + 1] + 4 < en: en = d[i + nw + 1] + 4
            if x < nw - 1 and d[i + 1] + 3 < en: en = d[i + 1] + 3
            d[i] = en

    dis = r * 3
    out = bytearray(nw * nh * 4)
    for i in range(nw * nh):
        mesafe = d[i]
        if mesafe >= dis + 3:
            continue
        a = 255 if mesafe <= dis else int(round((dis + 3 - mesafe) / 3.0 * 255))
        out[i * 4 + 3] = a                      # RGB zaten siyah
    png_io.bindir(nw, nh, out, w, h, px, r, r)
    return nw, nh, out


def main():
    kw, kh, kpx = M.oku(KAYNAK)
    print("kaynak %dx%d" % (kw, kh))
    zw, zh, zpx = M.oku(ZEMIN)

    for i in range(len(KADEMELER)):
        ad, kutu_w, kutu_h, merkez_y = KADEMELER[i]
        iw, ih, ipx = hucre(kw, kh, kpx, i)

        ic_w, ic_h = kutu_w - 2 * KONTUR, kutu_h - 2 * KONTUR
        k = min(ic_w / float(iw), ic_h / float(ih))
        iw, ih, ipx = png_io.olcekle(iw, ih, ipx, int(round(iw * k)), int(round(ih * k)))
        iw, ih, ipx = konturla(iw, ih, ipx, KONTUR)

        kart = bytearray(zpx)
        png_io.bindir(zw, zh, kart, iw, ih, ipx,
                      KART_ORTA_X - iw // 2, merkez_y - ih // 2)
        png_io.yaz(os.path.join(MAGAZA, ad + ".png"), zw, zh, kart)
        print("  %-16s ikon %3dx%-3d" % (ad, iw, ih))


if __name__ == "__main__":
    main()
