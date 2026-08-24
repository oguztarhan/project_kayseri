"""Kullanicinin verdigi hap butonlari, renk varyantlari ve yeni kapat dugmesi.

mavi_set.py deseni: hepsi stdlib, PIL yok (bkz. README).

Kaynakta iki mavi hap var (bttn1 ince, bttn2 kalin). Kirmizi ve sari olanlar dosya
olarak gelmedi, sadece gorsel olarak gosterildi; ikisi de ayni uretimden ciktigi icin
kalin hapin tonunu dondurup uretiyoruz. Boylece uc buton tek aile: ayni siluet, ayni
lacivert cerceve, ayni parlama -- sadece yuz rengi degisiyor. Ayri ayri cizilmis uc
dosya olsaydi kenar kalinliklari tutmazdi.
"""
import colorsys
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import png_io
import mavi_set as M

KOK = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
HEDEF = os.path.join(KOK, "Assets", "Art", "UI", "MaviSet")

EV = os.path.expanduser("~")
HAP_INCE = os.path.join(EV, "Downloads", "bttn1")
HAP_KALIN = os.path.join(EV, "Downloads", "bttn2")
KAPAT_SAYFA = os.path.join(EV, "OneDrive", "Desktop", "Uİ packages",
                           "Codex Görseli 21 Ağu 2026 14_32_11.png")

# Krem ic halka: kirmizi ve sari butonlarda gumus degil krem bir cizgi var.
KREM = (255, 244, 214)


def yaz(ad, w, h, px, not_=""):
    os.makedirs(HEDEF, exist_ok=True)
    yol = os.path.join(HEDEF, ad + ".png")
    png_io.yaz(yol, w, h, px)
    print("  %-20s %4dx%-4d %s" % (ad, w, h, not_))


def hazirla(kaynak, yukseklik):
    """Alfa sinirina kirpar, saydam taraftaki koyu RGB'yi tasirir, olcekler."""
    w, h, px = M.oku(kaynak)
    w, h, px = M.sikistir(w, h, px, 8)
    M.tasir(w, h, px, 6)
    k = yukseklik / float(h)
    return png_io.olcekle(w, h, px, int(round(w * k)), yukseklik)


def boya(w, h, px, ton, doygunluk=1.0, halka=None):
    """Yuz rengini `ton`a cevirir; lacivert cerceveye ve saydam paya dokunmaz.

    Cerceve V<0.30 oldugu icin disarida kaliyor -- kirmizi ve sari orneklerde de
    ayni lacivert cerceve var, onu dondurmek aileyi bozardi.
    """
    out = bytearray(px)
    for i in range(0, len(px), 4):
        a = px[i + 3]
        if a == 0:
            continue
        r, g, b = px[i] / 255.0, px[i + 1] / 255.0, px[i + 2] / 255.0
        hh, s, v = colorsys.rgb_to_hsv(r, g, b)
        if v < 0.30:
            continue                      # lacivert cerceve ve golge
        if s < 0.22:
            if halka is None:
                continue                  # gumus halka ve parlama oldugu gibi kalir
            # Krem halka: notr piksel ne kadar parlaksa o kadar krem.
            out[i] = int(round(halka[0] * v))
            out[i + 1] = int(round(halka[1] * v))
            out[i + 2] = int(round(halka[2] * v))
            continue
        nr, ng, nb = colorsys.hsv_to_rgb(ton, min(1.0, s * doygunluk), v)
        out[i] = int(round(nr * 255))
        out[i + 1] = int(round(ng * 255))
        out[i + 2] = int(round(nb * 255))
    return w, h, out


def kapat(indeks, boy=320):
    """Uc kapat dugmesi tek sayfada; alfa sutun projeksiyonundan ayirip birini alir."""
    w, h, px = M.oku(KAPAT_SAYFA)
    sutun = [0] * w
    for x in range(w):
        sutun[x] = sum(1 for y in range(h) if px[(y * w + x) * 4 + 3] > 24)
    bant = M.bantlar(sutun, h // 40)
    x0, x1 = bant[indeks]
    cw, ch, cpx = M.kirp(w, h, px, (x0, 0, x1, h - 1))
    cw, ch, cpx = M.sikistir(cw, ch, cpx, 8)
    M.tasir(cw, ch, cpx, 6)
    k = boy / float(max(cw, ch))
    return png_io.olcekle(cw, ch, cpx, int(round(cw * k)), int(round(ch * k)))


def main():
    print("haplar:")
    w, h, px = hazirla(HAP_INCE, 160)
    yaz("btn_hap_mavi", w, h, px, "kenar payi = %d" % (h // 2))

    w, h, px = hazirla(HAP_KALIN, 180)
    yaz("btn_hap_kalin", w, h, px, "kenar payi = %d" % (h // 2))

    rw, rh, rpx = boya(w, h, px, 0.005, 1.15, KREM)
    yaz("btn_hap_kirmizi", rw, rh, rpx)

    sw, sh, spx = boya(w, h, px, 0.115, 1.20, KREM)
    yaz("btn_hap_sari", sw, sh, spx)

    print("kapat:")
    w, h, px = kapat(2)
    yaz("btn_kapat_v3", w, h, px)


if __name__ == "__main__":
    main()
