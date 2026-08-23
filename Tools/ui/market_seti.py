"""Market avlusunun HUD parcalari -- grafit/turuncu setin temiz (v2) kesimi.

Avlu ekrani Assets/Resources/UI/MarketLiquid altindaki bes sprite'i Resources.Load ile
aliyor (bkz. MarketHudUI.Glass). Buradaki is o bes dosyanin uzerine yeni setin duzgun
kirpilmis hallerini yazmak -- .meta dosyalarina dokunulmadigi icin GUID'ler, dolayisiyla
her referans yerinde kaliyor. Alti da yeni: kolun basligi ilk kez ayri bir parca olarak
geliyor, o yuzden joystick_thumb yeni dosya.

Iki ozel is var:

* 02'de tek parca yok -- sayfada para hapinin yaninda kirpilmis ikinci bir sekil duruyor.
  Alfa sutun projeksiyonuyla bolup en genis parcayi aliyoruz.
* 03'un tepesine "DEMIR ADASI" basili. Avlu adasi degistikce o yazinin degismesi lazim,
  bu yuzden harfleri altlarindaki duz plaka rengiyle siliyoruz; adi ekran kendi yaziyor.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import png_io
import mavi_set as M

KOK = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
HEDEF = os.path.join(KOK, "Assets", "Resources", "UI", "MarketLiquid")
KAYNAK = os.path.join(os.path.expanduser("~"), "Downloads", "graphite-interior-hud-kit-clean-v2")


def yaz(ad, w, h, px):
    yol = os.path.join(HEDEF, ad + ".png")
    png_io.yaz(yol, w, h, px)
    print("  %-16s %4dx%-4d  en/boy=%.2f" % (ad, w, h, w / float(h)))


def olcek(w, h, px, uzun):
    k = uzun / float(max(w, h))
    return png_io.olcekle(w, h, px, max(1, int(round(w * k))), max(1, int(round(h * k))))


def hazirla(dosya):
    """Kaynagi oku, alfa sinirina kirp, kenar rengini seffaf tarafa tasir."""
    w, h, px = M.oku(os.path.join(KAYNAK, dosya))
    w, h, px = M.sikistir(w, h, px, 8)
    M.tasir(w, h, px, 6)
    return w, h, px


def en_genis_parca(w, h, px):
    """Sayfayi alfa sutun projeksiyonundan bolup en genis parcayi dondurur."""
    sutun = [0] * w
    for x in range(w):
        sutun[x] = sum(1 for y in range(h) if px[(y * w + x) * 4 + 3] > 24)
    parca = M.bantlar(sutun, max(1, h // 40))
    if not parca:
        return w, h, px
    x0, x1 = max(parca, key=lambda ab: ab[1] - ab[0])
    w, h, px = M.kirp(w, h, px, (x0, 0, x1, h - 1))
    return M.sikistir(w, h, px, 8)     # sutun kirpmasi yukseklige dokunmuyor, pay kaliyor


def metni_sil(w, h, px, kutu, tur):
    """Sete basili yaziyi altindaki plakaya boyar ve sildigi kutuyu dondurur.

    Oyun on bir dilde; sete Turkce yazi basili gelmis. Harfleri silmezsek Ingilizce
    oynayan oyuncu "GELIR" goruyor. Plaka duz degil, hafif dikey degrade tasiyor:
    tek renkle doldurmak yamayi gorunur birakiyor, o yuzden her satir kendi satirindan
    -- yazinin yanindaki bos seritten -- alinan ortanca renkle doluyor.

    `kutu` aramanin sinirlandigi (x0, y0, x1, y1) oranlari; `tur` harflerin rengi.
    """
    ix0, ix1 = int(w * kutu[0]), int(w * kutu[2])
    iy0, iy1 = int(h * kutu[1]), int(h * kutu[3])

    def harf(k):
        r, g, b, a = px[k], px[k + 1], px[k + 2], px[k + 3]
        if a < 128:
            return False
        if tur == "sari":
            return r > 150 and g > 110 and b < 100 and r - b > 70
        return min(r, g, b) > 150      # beyaz etiketler

    sx0, sy0, sx1, sy1 = w, h, -1, -1
    for y in range(iy0, iy1):
        for x in range(ix0, ix1):
            if harf((y * w + x) * 4):
                sx0 = min(sx0, x); sx1 = max(sx1, x)
                sy0 = min(sy0, y); sy1 = max(sy1, y)
    if sx1 < 0:
        return None

    pay = 5
    sx0, sy0 = max(0, sx0 - pay), max(0, sy0 - pay)
    sx1, sy1 = min(w - 1, sx1 + pay), min(h - 1, sy1 + pay)

    # Ornek serit: kutunun sagi bossa oradan, degilse solundan.
    ox0, ox1 = min(w - 1, sx1 + 18), min(w - 1, sx1 + 18 + 120)
    if ox1 - ox0 < 20:
        ox0, ox1 = max(0, sx0 - 18 - 120), max(0, sx0 - 18)
    for y in range(sy0, sy1 + 1):
        ornek = []
        for x in range(ox0, ox1):
            k = (y * w + x) * 4
            if px[k + 3] > 200:
                ornek.append((px[k], px[k + 1], px[k + 2]))
        if not ornek:
            continue
        ornek.sort(key=lambda c: c[0] + c[1] + c[2])
        r, g, b = ornek[len(ornek) // 2]
        for x in range(sx0, sx1 + 1):
            k = (y * w + x) * 4
            px[k], px[k + 1], px[k + 2], px[k + 3] = r, g, b, 255
    return (sx0, sy0, sx1, sy1)


def sil(w, h, px, ad, kutu, tur):
    kes = metni_sil(w, h, px, kutu, tur)
    print("     %-9s %s" % (ad, "x %d-%d  y %d-%d" % (kes[0], kes[2], kes[1], kes[3])
                                if kes else "bulunamadi"))


def main():
    print("market seti:")

    w, h, px = hazirla("01-back-button.png")
    sil(w, h, px, "ADAYA DON", (0.14, 0.24, 0.90, 0.80), "beyaz")
    yaz("back_button", *olcek(w, h, px, 640))

    w, h, px = hazirla("02-currency-pill-empty.png")
    w, h, px = en_genis_parca(w, h, px)
    yaz("currency_panel", *olcek(w, h, px, 420))

    w, h, px = hazirla("03-iron-island-panel-slots.png")
    # Basili ada adi ve dort yuva etiketi: hepsi Turkce, hepsi siliniyor -- ekran
    # kendi yerellestirilmis metnini ayni yerlere yaziyor.
    sil(w, h, px, "baslik",  (0.10, 0.08, 0.72, 0.26), "sari")
    sil(w, h, px, "GELIR",   (0.12, 0.255, 0.46, 0.348), "beyaz")
    sil(w, h, px, "MOD",     (0.54, 0.255, 0.90, 0.348), "beyaz")
    sil(w, h, px, "STOK",    (0.12, 0.510, 0.46, 0.605), "beyaz")
    sil(w, h, px, "DOLULUK", (0.54, 0.510, 0.92, 0.605), "beyaz")
    yaz("island_info_panel", *olcek(w, h, px, 900))

    w, h, px = hazirla("04-worker-counter-empty.png")
    yaz("objective_counter", *olcek(w, h, px, 560))

    w, h, px = hazirla("05-joystick-base.png")
    yaz("joystick", *olcek(w, h, px, 512))

    w, h, px = hazirla("06-joystick-thumb.png")
    yaz("joystick_thumb", *olcek(w, h, px, 400))


if __name__ == "__main__":
    main()
