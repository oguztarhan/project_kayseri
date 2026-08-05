"""Yukseltme (istasyon) ekraninin eksik parcalari.

Faz ilerleme cubugu, ayarlar ekraninin kaydiricilariyla ayni iki sprite'i kullaniyordu
(slider_yatak / slider_dolgu). O ikisi acik lavanta bir kapsul: ilerleme cubugu olarak
yikanmis duruyor ve dolgu zeminden ayirt edilmiyor. Ayarlar ekranini bozmamak icin
buradaki parcalar ayri isimlerle uretiliyor.

Ev uslubu (bkz. README): lacivert kontur, dikey govde gradyani, ust ucte beyaz parlaklik
bandi, <text> dugumu yok.

    python Tools/ui/rasterize_istasyon.py
"""
import math
import pathlib

OUT = pathlib.Path(__file__).parent / "svg"
OUT.mkdir(exist_ok=True)

NAVY = "#0B1220"
OYUK_HI, OYUK_LO = "#131E35", "#1B2A47"     # oyuk: KOYU ustte -- icine cokmus okunur
ALTIN_HI, ALTIN_LO = "#FFD75A", "#F09A18"
ALTIN_KONTUR = "#9A5F0C"


def grad(gid, top, bottom):
    return (f'<linearGradient id="{gid}" x1="0" y1="0" x2="0" y2="1">'
            f'<stop offset="0" stop-color="{top}"/>'
            f'<stop offset="1" stop-color="{bottom}"/></linearGradient>')


def svg(w, h, defs, body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
            f'viewBox="0 0 {w} {h}"><defs>{defs}</defs>{body}</svg>')


# ---------------------------------------------------------------- faz cubugu
# Yatak 9-dilim (kenar 40,0,40,0): yuvarlak uclar sabit, orta bant esniyor.
# Dolgu ise Image.Type.Filled ile kirpiliyor ve Filled 9-dilimi desteklemiyor,
# o yuzden ekrandaki boyuna yakin cizilir (888x50) -- yoksa uclar elips olur.

def faz_yatak():
    """Koyu, icine cokmus ray. Gradyan ters (koyu ustte) ve ustte ic golge var:
    ikisi birlikte 'oyuk' okutuyor, dolgunun uzerinde durdugu sey belli oluyor."""
    defs = (grad("y", OYUK_HI, OYUK_LO) +
            '<linearGradient id="ic" x1="0" y1="0" x2="0" y2="1">'
            '<stop offset="0" stop-color="#000000" stop-opacity="0.42"/>'
            '<stop offset="0.45" stop-color="#000000" stop-opacity="0.06"/>'
            '<stop offset="1" stop-color="#000000" stop-opacity="0"/></linearGradient>')
    body = ('<rect x="4" y="4" width="432" height="62" rx="31" fill="url(#y)" '
            f'stroke="{NAVY}" stroke-width="7"/>'
            '<rect x="12" y="12" width="416" height="30" rx="15" fill="url(#ic)"/>')
    return svg(440, 70, defs, body)


def faz_dolgu():
    """Altin dolgu. Zemin lacivert oldugu icin kontur de altinin koyusu -- lacivert
    bir kontur oyugun icinde kaybolurdu."""
    defs = grad("d", ALTIN_HI, ALTIN_LO)
    body = ('<rect x="4" y="4" width="880" height="42" rx="21" fill="url(#d)" '
            f'stroke="{ALTIN_KONTUR}" stroke-width="5"/>'
            '<rect x="22" y="12" width="844" height="12" rx="6" fill="#FFFFFF" opacity="0.45"/>')
    return svg(888, 50, defs, body)


# ---------------------------------------------------------------- faz bandi
# Faz atlayisinda ekrana inen serit, ayarlar ekraninin mavi seridini odunc aliyordu.
# Sette serit rengi panel ailesini gosterir (mavi = ayarlar/istasyon, yesil = odul),
# yani kutlama seridinin kendi rengi olmali: altin. Olculer aileyle ayni, 1960x460.

SERIT_KUYRUK = "#D08616"      # kuyrugun on yuzu
SERIT_KIVRIM = "#9A5F0C"      # kuyrugun katlanan koyu ucgeni
CERCEVE_DIS = "#E0A020"
CERCEVE_IC = "#FBDF8E"


def serit_faz():
    """Altin kutlama seridi. Aileyle ayni anatomi: iki yana tasan kirlangic kuyruklar,
    ortada cift cerceveli plaka, ustte parlaklik bandi, altta yumusak golge."""
    defs = (grad("p", ALTIN_HI, ALTIN_LO) +
            '<linearGradient id="parlak" x1="0" y1="0" x2="0" y2="1">'
            '<stop offset="0" stop-color="#FFFFFF" stop-opacity="0.55"/>'
            '<stop offset="1" stop-color="#FFFFFF" stop-opacity="0.10"/></linearGradient>')
    body = (
        # sol kuyruk
        f'<path d="M8 96 L120 96 L120 356 L8 356 L96 226 Z" fill="{SERIT_KIVRIM}"/>'
        f'<path d="M8 96 L360 96 L360 356 L8 356 L96 226 Z" fill="{SERIT_KUYRUK}"/>'
        # sag kuyruk
        f'<path d="M1952 96 L1840 96 L1840 356 L1952 356 L1864 226 Z" fill="{SERIT_KIVRIM}"/>'
        f'<path d="M1952 96 L1600 96 L1600 356 L1952 356 L1864 226 Z" fill="{SERIT_KUYRUK}"/>'
        # golge
        '<rect x="352" y="36" width="1256" height="400" rx="66" fill="#7A6038" opacity="0.35"/>'
        # plaka: dis cerceve, ic cerceve, govde
        f'<rect x="344" y="10" width="1272" height="410" rx="70" fill="{CERCEVE_DIS}"/>'
        f'<rect x="360" y="26" width="1240" height="378" rx="58" fill="{CERCEVE_IC}"/>'
        '<rect x="386" y="52" width="1188" height="326" rx="46" fill="url(#p)"/>'
        '<rect x="424" y="82" width="1112" height="86" rx="43" fill="url(#parlak)"/>')
    return svg(1960, 460, defs, body)


def faz_isin():
    """Seridin arkasina konan isin celengi. Beyaz cizilir ki kod istedigi tonu versin.
    Radyal maske disa dogru soldurur -- kenari kesik bir carkin ucuz duracagi yerde
    isik gibi dagilir."""
    n = 18
    r = 470
    cx = cy = 480
    wedges = []
    for i in range(n):
        a0 = (360.0 / n) * i
        a1 = a0 + (360.0 / n) * 0.46
        x0 = cx + r * math.cos(math.radians(a0))
        y0 = cy + r * math.sin(math.radians(a0))
        x1 = cx + r * math.cos(math.radians(a1))
        y1 = cy + r * math.sin(math.radians(a1))
        wedges.append(f'<path d="M{cx} {cy} L{x0:.1f} {y0:.1f} L{x1:.1f} {y1:.1f} Z" fill="#FFFFFF"/>')
    defs = ('<radialGradient id="sol" cx="0.5" cy="0.5" r="0.5">'
            '<stop offset="0.08" stop-color="#FFFFFF" stop-opacity="1"/>'
            '<stop offset="0.55" stop-color="#FFFFFF" stop-opacity="0.72"/>'
            '<stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/></radialGradient>'
            '<mask id="m"><rect width="960" height="960" fill="url(#sol)"/></mask>')
    return svg(960, 960, defs, '<g mask="url(#m)">' + "".join(wedges) + '</g>')


# ---------------------------------------------------------------- genisletme ikonu
# Seridin son yuvasi (ve tek seferlik satin alim kartlari) altin kulce yiginini
# kullaniyordu: setin "para" ikonu, genisletmeyle ilgisi yok ve seritteki digerlerinin
# hepsi bir yapiyi gosterirken bu bir kaynagi gosteriyordu.
#
# Yerine gecen sey oyunun kendi diliyle konusuyor: kilitli arsalar haritada sari kesik
# cizgiyle isaretli, o yuzden ikon da kesik cizgili bir arsa ve ustunde disa acilan dort
# ok. Yuvada 70 piksele iniyor, bu yuzden her sey kalin: ince cizgi o boyda kayboluyor.

ARSA_HI, ARSA_LO = "#3E5A86", "#25395C"


def ikon_genisletme():
    """Kesik cizgili arsa + disa acilan dort kose oku."""
    defs = grad("a", ARSA_HI, ARSA_LO) + grad("o", ALTIN_HI, ALTIN_LO)
    # merkezdeki arsa
    body = ('<rect x="74" y="74" width="92" height="92" rx="18" fill="url(#a)" '
            f'stroke="{NAVY}" stroke-width="10"/>'
            '<rect x="88" y="88" width="64" height="64" rx="10" fill="none" '
            f'stroke="{ALTIN_HI}" stroke-width="8" stroke-dasharray="17 13" stroke-linecap="round"/>')
    # dort kose oku: biri cizilir, digerleri merkez etrafinda dondurulur
    ok = ('M0 0 L0 54 L19 35 L45 61 L61 45 L35 19 L54 19 Z')
    for aci in (0, 90, 180, 270):
        body += (f'<g transform="rotate({aci} 120 120)">'
                 f'<path d="{ok}" transform="translate(22 22)" fill="url(#o)" '
                 f'stroke="{NAVY}" stroke-width="10" stroke-linejoin="round"/></g>')
    return svg(240, 240, defs, body)


PIECES = {
    "faz_yatak": faz_yatak(),
    "faz_dolgu": faz_dolgu(),
    "serit_faz": serit_faz(),
    "faz_isin": faz_isin(),
    "ikon_genisletme": ikon_genisletme(),
}

# Rasterize edildikten sonra Unity'de bir kez ayarlanmasi gerekenler.
# (sprite modu Single; 9-dilim kenari rasterize_istasyon.py tarafindan yaziliyor.)
KENAR = {
    "faz_yatak": (40, 0, 40, 0),
    "faz_dolgu": (0, 0, 0, 0),      # Filled -- dilimlenmez
    "serit_faz": (0, 0, 0, 0),      # aile gibi tek parca gerilir
    "faz_isin": (0, 0, 0, 0),
    "ikon_genisletme": (0, 0, 0, 0),
}

# Ikonlar Gostergeler/ yerine Ikonlar/ altina gider.
KLASOR = {"ikon_genisletme": "Ikonlar"}

if __name__ == "__main__":
    for ad, markup in PIECES.items():
        (OUT / f"{ad}.svg").write_text(markup, encoding="utf-8")
        print(f"{ad:14s} -> svg/{ad}.svg")
