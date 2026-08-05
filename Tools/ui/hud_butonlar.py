"""HUD alt sirasinin iki eksik parcasi -> Assets/Art/UI/Butonlar.

Alt sirada bes buton yan yana duruyor ve hepsi ayni plakayi paylasmali:
btn_prestij_hud, btn_harita, btn_kontrat zaten lacivert. Eksik olan ikisi:

  btn_yukselt      yesildi. Plakasi sifirdan cizilmiyor, mevcut sprite'in yesil
                   pikselleri lacivert tona ceviriliyor -- boylece kazma ikonu,
                   parlaklik bandi, kontur ve golge birebir korunuyor.
  btn_boost_hud    yoktu. Sirann tek genis plakasi (420 artboard, otekiler 340):
                   ust yariya kucultulmus simsek ikonu, alt yari "×2 GELIR"
                   yazisina birakildi -- yazi TMP oldugu icin sprite'a girmiyor.

    python Tools/ui/hud_butonlar.py

Plaka anatomisi (btn_magaza.png'den olculdu): 340x390 artboard, govde x28..311
y46..329, kose yaricapi 80, kontur #101B33, dikey gradyan #2C4B95 -> #1E3369,
ust ucte beyaz parlaklik bandi, altinda yumusak golge. Genis plakada sadece
govde eni buyuyor; yukseklikler, yaricap ve konturun kalinligi ayni kaliyor ki
buton yan yana dizildiginde otekilerden ayri bir aileye ait gibi durmasin.
"""
import colorsys
import pathlib
import re
import subprocess
import tempfile

from png_io import bindir, oku, olcekle, yaz

HERE = pathlib.Path(__file__).resolve().parent
SANAT = HERE.parents[1] / "Assets" / "Art" / "UI"
BUTON = SANAT / "Butonlar"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"

NAVY = "#101B33"
GOVDE_HI, GOVDE_LO = "#2C4B95", "#1E3369"

# btn_magaza'nin plaka gradyani, HSV olarak: ust (h222.3 s0.70 v0.58), alt (h223.2 s0.71 v0.45).
# btn_yukselt'in yesili: ust (h143.7 s0.57 v0.82), alt (h144.3 s0.69 v0.69).
# Iki gradyanin v araligi da 0.13 oldugu icin donusum tek bir kaydirma: v -> v - 0.24.
HEDEF_TON = 222.5 / 360.0
HEDEF_DOY = 0.705
PARLAKLIK_KAY = -0.24


def yesili_lacivert_yap(kaynak, hedef):
    """Yesil govde piksellerini aileye uyan lacivert tona cevirir.

    Siniflandirma renk siralamasiyla yapiliyor, ton araligiyla degil: yesil plakada
    g > r ve g > b; altin kazmada r > g > b; beyaz sapta ucu birbirine yakin. Boylece
    kenar yumusatma pikselleri de dogru tarafa dusuyor.
    """
    w, h, px = oku(kaynak)
    cevrilen = kontur = 0
    # Kontur koyultmasi kendi basina tekrar edilebilir degil: v'yi 0.72 ile carpiyor ve
    # sonuc cogu zaman yine araligin icinde kaliyor, yani ikinci kosuda kontur bir kat
    # daha kararir. Iki gecis tek bir donusumun parcalari, o yuzden kapiyi yesil govde
    # tutuyor -- yesil kalmadiysa dosya zaten cevrilmis demektir, hic dokunulmuyor.
    if not any(px[i + 3] and px[i + 1] > px[i] and px[i + 1] > px[i + 2]
               for i in range(0, len(px), 4)):
        return "%-18s %dx%d  zaten cevrilmis" % (pathlib.Path(hedef).name, w, h)
    for i in range(0, len(px), 4):
        r, g, b, a = px[i], px[i + 1], px[i + 2], px[i + 3]
        if a == 0:
            continue
        if g > r and g > b:
            _, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
            nr, ng, nb = colorsys.hsv_to_rgb(HEDEF_TON, HEDEF_DOY, max(0.0, v + PARLAKLIK_KAY))
            px[i] = int(round(nr * 255)); px[i + 1] = int(round(ng * 255)); px[i + 2] = int(round(nb * 255))
            cevrilen += 1
        elif b > g > r:
            # Kontur ve golge zaten lacivert ama bu sprite'ta bir tik acikti (#1E2B47);
            # ailenin konturuna (#101B33) indiriliyor. Golge yari saydam oldugu icin
            # alfaya dokunulmuyor.
            _, s, v = colorsys.rgb_to_hsv(r / 255.0, g / 255.0, b / 255.0)
            if 0.24 < v <= 0.42:
                nr, ng, nb = colorsys.hsv_to_rgb(221.0 / 360.0, 0.69, v * 0.72)
                px[i] = int(round(nr * 255)); px[i + 1] = int(round(ng * 255)); px[i + 2] = int(round(nb * 255))
                kontur += 1
    yaz(hedef, w, h, px)
    return "%-18s %dx%d  govde=%d kontur=%d piksel" % (
        pathlib.Path(hedef).name, w, h, cevrilen, kontur)


def plaka_svg(en):
    """en piksel genisliginde bos plaka. Kenar boslugu 28, golge 11 piksel asagida."""
    govde = en - 56
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{en}" height="390" viewBox="0 0 {en} 390">
<defs>
  <linearGradient id="g" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="{GOVDE_HI}"/><stop offset="1" stop-color="{GOVDE_LO}"/>
  </linearGradient>
</defs>
<rect x="28" y="60" width="{govde}" height="284" rx="80" fill="#050912" opacity="0.22"/>
<rect x="31.5" y="49.5" width="{govde - 7}" height="277" rx="76.5" fill="url(#g)"
      stroke="{NAVY}" stroke-width="7"/>
<rect x="72" y="76" width="{govde - 88}" height="52" rx="26" fill="#FFFFFF" opacity="0.12"/>
</svg>'''


def plaka_ciz(hedef, markup):
    """Bos lacivert plakayi Chrome ile rasterize eder (README'deki yontem)."""
    w = int(re.search(r'width="(\d+)"', markup).group(1))
    h = int(re.search(r'height="(\d+)"', markup).group(1))
    calisma = pathlib.Path(tempfile.mkdtemp(prefix="kayseri_hud_"))
    sayfa = calisma / "plaka.html"
    sayfa.write_text(
        "<html><head><meta charset='utf-8'><style>"
        "html,body{margin:0;padding:0;background:transparent;overflow:hidden}"
        "svg{display:block}</style></head><body>" + markup + "</body></html>",
        encoding="utf-8")
    subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                    "--default-background-color=00000000", "--force-device-scale-factor=1",
                    f"--window-size={w},{h}", f"--screenshot={hedef}", sayfa.as_uri()],
                   check=True, capture_output=True)
    return w, h


# Otekiler 340x390 ve ekranda 190.4x218.4 duruyor, yani olcek 0.56. Genis plaka
# 408 cizilince ekranda 228.5 oluyor: bes buton + dort bosluk toplam 1042 birim,
# yani sira 21:9 bir telefonda bile (mantiksal en 1041) kenara dayanmiyor.
BOOST_EN = 408
IKON_BOY = 166      # 240'tan kucultuluyor, alt yari yaziya kaliyor
IKON_MERKEZ = 140


def boost_butonu(hedef):
    """Genis lacivert plaka + ust yarida simsek.

    Ikon artik plakanin merkezinde degil: gorsel govde y49..326 arasinda, ikon 146'ya
    oturunca altinda 232..312 bandi aciliyor ve "×2 GELIR" yazisi oraya biniyor. Yazi
    sprite'a cizilmiyor -- on bir dile cevriliyor, TMP'de kalmasi gerek.
    """
    w, h = plaka_ciz(hedef, plaka_svg(BOOST_EN))
    pw, ph, plaka = oku(hedef)
    iw, ih, ikon = olcekle(*oku(str(SANAT / "Ikonlar" / "ikon_hizlandirici.png")),
                           IKON_BOY, IKON_BOY)
    bindir(pw, ph, plaka, iw, ih, ikon, (pw - iw) // 2, IKON_MERKEZ - ih // 2)
    yaz(hedef, pw, ph, plaka)
    return "%-18s %dx%d  ikon %dx%d y=%d" % (
        pathlib.Path(hedef).name, pw, ph, iw, ih, IKON_MERKEZ - ih // 2)


if __name__ == "__main__":
    print(yesili_lacivert_yap(str(BUTON / "btn_yukselt.png"), str(BUTON / "btn_yukselt.png")))
    print(boost_butonu(str(BUTON / "btn_boost_hud.png")))
    print("->", BUTON)
