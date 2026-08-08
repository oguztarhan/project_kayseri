"""Android bildirim ikonlari: bir kucuk siluet, bir renkli rozet.

Android iki ayri gorsel kullanir ve kurallari birbirinin zitti.

KUCUK IKON durum cubugunda, saatin yaninda, 24 pikselde durur. Android onun RENGINI ATAR --
yalnizca alfa kanalini alip kalan sekli beyaza boyar. Yani renkli bir logo oraya konursa
zemini de opak oldugu icin kare komple beyaza boyanir ve kullanici anlamsiz bir kare gorur.
Bu yuzden `ocak_kucuk` saydam zemin ustunde tek parca beyaz siluettir, ve sekil kasitli olarak
tombuldur: 24 pikselde ince her sey kaybolur.

BUYUK IKON bildirimi asagi cekince kartin icinde gorunur ve renkli olabilir. `ocak_buyuk`
oyunun kendi paletini kullanir (bkz. acilis.py: SEA, WARM, WARMLIT) ve DAIREDIR -- bircok
launcher buyuk ikonu daire icine kirpar, kare bir rozetin koseleri gider.

Ikisi de ayni vagon geometrisini paylasir, cunku bildirime iki farkli sey cizmenin anlami yok.

    python Tools/ui/rasterize_bildirim.py
"""

# ── Ev tarzi (Tools/ui/README.md) ─────────────────────────────────────────────
CIZGI = "#182840"     # her govdenin lacivert konturu
GOK_UST = "#1B3C60"   # komur adasinin gokyuzu ust tonu
GOK_ALT = "#0A2038"   # deniz alt tonu
ISIK = "#FF9A2E"      # WARM  -- ocak isigi
CEVHER = "#FFD57A"    # WARMLIT -- yuklu cevher

# ── Vagon, 100x100 birimlik kutuda ───────────────────────────────────────────
# Uc parca ust uste biner ve tek bir siluet olur: cevher yigini govdenin ustune
# oturur, tekerlekler govdenin altina degir. Tekerlekler arasinda kalan bosluk
# kasitli -- siluetin vagon olarak okunmasini saglayan sey o.
#
# Yigin ILK HALINDE uc ince tumsekti ve 24 pikselde uc tumsek uc piksel eder:
# vagonun ustunde ne oldugu belirsiz bir tirtik. Simdi iki genis tepe ve
# aralarinda tek bir cukur var -- o boyutta "yuklu" olarak okunan sey bu.
YIGIN = "M18 44 C26 26 40 24 50 33 C60 24 74 28 82 44 Z"
GOVDE = "M16 44 L84 44 L74 73 L26 73 Z"
# Tekerlekler kasitli olarak kalin ve birbirinden uzak: aralarindaki bosluk
# siluetin vagon olarak okunmasini saglayan tek ipucu, ve kapanirsa sekil
# anlamsiz bir yumruya donusuyor.
TEKER = ((34, 81, 10), (66, 81, 10))

# Sekil kutusu x 16..84, y 25..91 -- merkezi (50, 58). Artboard'un ortasina
# oturtmak icin her iki ikon da bu noktadan olcekleniyor.
MERKEZ = "translate(50,50) scale({0}) translate(-50,-58)"


def _vagon(dolgu, kontur=None, kalinlik=0.0):
    """Vagonu tek bir grup olarak dondurur. Kontur yoksa duz siluet."""
    k = ""
    if kontur:
        k = ' stroke="%s" stroke-width="%.1f" stroke-linejoin="round"' % (kontur, kalinlik)
    p = ['<g fill="%s"%s>' % (dolgu, k)]
    p.append('<path d="%s"/>' % YIGIN)
    p.append('<path d="%s"/>' % GOVDE)
    for cx, cy, r in TEKER:
        p.append('<circle cx="%d" cy="%d" r="%d"/>' % (cx, cy, r))
    p.append('</g>')
    return "".join(p)


# ── Kucuk ikon: saydam zemin, duz beyaz, baska hicbir sey ────────────────────
# Olcek 1.28: sekil artboard'un yaklasik %88'ini kaplar. Android durum cubugu
# ikonunun etrafina kendi ic boslugunu zaten koyuyor, o yuzden burada fazladan
# pay birakmak sadece ikonu kucultur -- ilk denemede %62'ye sigmisti ve saatin
# yaninda bir toz zerresi gibi duruyordu.
KUCUK = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="192" height="192" viewBox="0 0 100 100">'
    '<g transform="%s">%s</g>'
    '</svg>'
) % (MERKEZ.format(1.28), _vagon("#FFFFFF"))


# ── Buyuk ikon: komur adasinin rengi, daire ──────────────────────────────────
BUYUK = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="384" height="384" viewBox="0 0 100 100">'
    '<defs>'
    '<linearGradient id="gok" x1="0" y1="0" x2="0" y2="1">'
    '<stop offset="0" stop-color="%s"/><stop offset="1" stop-color="%s"/>'
    '</linearGradient>'
    # Ocak isigi: vagonun arkasindan gelen sicak hale. Disa dogru tamamen soner,
    # yoksa lacivert konturun icinde turuncu bir halka gorunur.
    '<radialGradient id="hale" cx="0.5" cy="0.54" r="0.40">'
    '<stop offset="0" stop-color="%s" stop-opacity="0.95"/>'
    '<stop offset="0.55" stop-color="%s" stop-opacity="0.34"/>'
    '<stop offset="1" stop-color="%s" stop-opacity="0"/>'
    '</radialGradient>'
    '</defs>'
    '<circle cx="50" cy="50" r="49" fill="%s"/>'
    '<circle cx="50" cy="50" r="45.5" fill="url(#gok)"/>'
    '<circle cx="50" cy="50" r="45.5" fill="url(#hale)"/>'
    # Ust ucteki beyaz parlama bandi -- setin butun govdelerinde var.
    '<ellipse cx="50" cy="19" rx="34" ry="14" fill="#FFFFFF" opacity="0.13"/>'
    '<g transform="%s">%s</g>'
    '</svg>'
) % (GOK_UST, GOK_ALT, ISIK, ISIK, ISIK, CIZGI, MERKEZ.format(0.72),
     _vagon(CEVHER, CIZGI, 4.0))


PIECES = {
    "ocak_kucuk": KUCUK,
    "ocak_buyuk": BUYUK,
}
