"""Radyal hale ve isaret eden el -- ikisi de kodla cizilir, dosya elle duzenlenmez.

Projede yumusak bir hale yoktu: Efekt/parilti.png duz beyaz bir dikdortgen, isik_hare.png
yatay bir serit. Ikisi de bir seyin ARKASINA konunca kenari belli olan bir levha birakiyor --
madalyonun arkasinda tam olarak oyle oldu. Buradaki hale merkezden disa dogru sifira inen bir
alfa ile ciziliyor, yani kenari yok: nerede bitecegi yok ki gorunsun.

    python Tools/ui/efekt_hale.py
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import png_io

KOK = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Art", "UI")


def hale(boy=512, cekirdek=0.10, kuvvet=2.2):
    """Merkezi dolu, kenari sifir. cekirdek: tam opak yaricap orani."""
    px = bytearray(boy * boy * 4)
    orta = (boy - 1) / 2.0
    for y in range(boy):
        for x in range(boy):
            d = math.hypot(x - orta, y - orta) / orta
            if d >= 1.0:
                a = 0.0
            elif d <= cekirdek:
                a = 1.0
            else:
                # kenara dogru ussel sonum: yarim yolda hala %25, kenarda tam sifir
                t = (d - cekirdek) / (1.0 - cekirdek)
                a = (1.0 - t) ** kuvvet
            i = (y * boy + x) * 4
            px[i] = px[i + 1] = px[i + 2] = 255
            px[i + 3] = int(a * 255.0 + 0.5)
    return boy, boy, px


def isin(boy=512, kol=12, kalinlik=0.16):
    """Halenin uzerine binen isin cemberi -- donerken yildiz patlamasi gibi okunur."""
    px = bytearray(boy * boy * 4)
    orta = (boy - 1) / 2.0
    for y in range(boy):
        for x in range(boy):
            dx, dy = x - orta, y - orta
            d = math.hypot(dx, dy) / orta
            if d >= 1.0 or d <= 0.06:
                a = 0.0
            else:
                aci = (math.atan2(dy, dx) + math.pi) / (2.0 * math.pi) * kol
                pay = abs((aci % 1.0) - 0.5) * 2.0        # 0 = isinin ortasi
                genis = max(0.0, 1.0 - pay / kalinlik)
                a = genis * (1.0 - d) ** 1.4
            i = (y * boy + x) * 4
            px[i] = px[i + 1] = px[i + 2] = 255
            px[i + 3] = int(min(1.0, a) * 255.0 + 0.5)
    return boy, boy, px


def el(w=256, h=340):
    """Dokunan el: yumruk + isaret parmagi. Egitimde halkanin ortasinda oynar."""
    px = bytearray(w * h * 4)

    def nokta(cx, cy, r, renk):
        # Renk, alfa artmasa da yaziliyor: kontur once cizilip icine ten rengi basiliyor ve
        # ikinci gecis birincinin tam opak pikselleri uzerine gelmek zorunda.
        for y in range(max(0, int(cy - r) - 2), min(h, int(cy + r) + 3)):
            for x in range(max(0, int(cx - r) - 2), min(w, int(cx + r) + 3)):
                d = math.hypot(x - cx, y - cy)
                if d > r + 1.0:
                    continue
                a = 1.0 if d <= r - 1.0 else (r + 1.0 - d) / 2.0
                i = (y * w + x) * 4
                if a > 0.5:
                    px[i], px[i + 1], px[i + 2] = renk
                if a * 255.0 > px[i + 3]:
                    px[i + 3] = int(a * 255.0 + 0.5)

    def kapsul(x0, y0, x1, y1, r, renk):
        n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
        for k in range(n + 1):
            t = k / float(n)
            nokta(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, r, renk)

    koyu = (36, 44, 68)
    ten = (255, 226, 190)

    def cizim(ek, renk):
        """Ayni el, yariçaplari `ek` kadar sismis halde. Once koyu, sonra ten: kontur boyle cikiyor."""
        # yumruk yatay bir kapsul -- daire cizince el degil termometre okunuyordu
        kapsul(w * 0.40, h * 0.76, w * 0.63, h * 0.76, w * 0.225 + ek, renk)
        # isaret parmagi yumrugun ortasinda degil solunda: ortada duran tek parmak
        # baska bir el hareketi gibi okunuyor, sagindaki bogumlar da onu duzeltmiyor
        kapsul(w * 0.40, h * 0.24, w * 0.40, h * 0.58, w * 0.100 + ek, renk)
        kapsul(w * 0.30, h * 0.65, w * 0.19, h * 0.75, w * 0.085 + ek, renk)   # basparmak
        nokta(w * 0.585, h * 0.595, w * 0.080 + ek, renk)                      # bukulu parmak bogumlari
        nokta(w * 0.700, h * 0.650, w * 0.075 + ek, renk)

    cizim(w * 0.045, koyu)
    cizim(0.0, ten)
    return w, h, px


def yaz(klasor, ad, uc):
    yol = os.path.join(KOK, klasor, ad)
    png_io.yaz(yol, uc[0], uc[1], uc[2])
    print("%s  %dx%d" % (os.path.relpath(yol, KOK), uc[0], uc[1]))


if __name__ == "__main__":
    yaz("Efekt", "isik_hale.png", hale())
    yaz("Efekt", "isik_isin.png", isin())
    yaz("Efekt", "el_dokun.png", el())
