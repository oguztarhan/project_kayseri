"""Tek atislik efektler -> Assets/Audio/Arayuz + Ekonomi + Ilerleme (44.1 kHz stereo).

Ses ailesi tek bir fikir uzerine kurulu: her sey Do major pentatonikten, her tonal vurus
FM, her vurusun onunde temiz bir klik. Sesler birbirinin akrabasi gibi duysun diye ayni
malzemeler (TAHTA / CAM / CAN / GOVDE) tekrar tekrar kullanilir.

Yukselen jest = iyi bir sey oldu (panel acildi, yukseltme, odul).
Alcalan jest    = bir sey kapandi ya da olmadi (panel kapandi, geri, yetersiz).
"""
import os

from ses_dsp import (CAM, CAN, GOVDE, NOTA, SR, TAHTA, add, buf, click, env,
                     fade2, fm, hava, lp1, norm2, noise, osc, pirilti, soft,
                     svf, verb2, write)

KOK = r"A:\Github\project_kayseri\Assets\Audio"


def sn(x):
    return int(x * SR)


def N(ad):
    return NOTA[ad]


# ------------------------------------------------------------------ arayuz

def dokunus():
    """Her butonun sesi. Yumusak tahta 'tok'. Gunde yuzlerce kez duyulacak:
    kisa, pes ve yorucu olmayan bir seye ihtiyaci var, cinlamaya degil."""
    n = sn(0.075)
    o = buf(n)
    add(o, click(n, 5400.0, 0.0022, 11), g=0.22)
    add(o, fm(n, N("A5"), TAHTA, tau=0.030, itau=0.0075, bend=0.55), g=0.90)
    add(o, fm(n, N("A6"), TAHTA, tau=0.014, itau=0.004), g=0.16)
    o = lp1(o, 12000.0)
    return fade2(norm2(verb2(o, size=0.019, mix=0.10), 0.56), 0.4, 8.0)


def geri():
    """Kapat / geri. Ayni tahta ama bir dortlu pes ve acikca dusen bir perde --
    'bitti' jesti. Dokunusla ayni malzeme oldugu icin kardes duyuluyor."""
    n = sn(0.095)
    o = buf(n)
    add(o, click(n, 3400.0, 0.0028, 12), g=0.18)
    add(o, fm(n, N("E5"), TAHTA, tau=0.040, itau=0.010, bend=2.2, bend_t=0.022), g=0.90)
    o = lp1(o, 8500.0)
    return fade2(norm2(verb2(o, size=0.023, mix=0.13), 0.52), 0.4, 10.0)


def panel_ac():
    """Panel yukari suzuluyor: hava yukari kayiyor, ustune yukselen iki nota."""
    n = sn(0.34)
    o = buf(n)
    add(o, hava(n, 480.0, 4200.0, attack=0.075, tau=0.075, seed=21, damp=0.45), g=0.40)

    gov = osc(n, 165.0, 300.0)
    ge = env(n, 0.014, 0.075)
    for i in range(n):
        o[i] += gov[i] * ge[i] * 0.20

    add(o, fm(sn(0.20), N("G5"), TAHTA, tau=0.085, bend=0.4), sn(0.095), 0.50)
    add(o, fm(sn(0.22), N("C6"), CAM, tau=0.095, itau=0.024, bend=0.4), sn(0.165), 0.44)

    o = lp1(o, 13000.0)
    return fade2(norm2(verb2(o, size=0.040, mix=0.26), 0.68), 0.8, 16.0)


def panel_kapat():
    """Panel iniyor: hava asagi, iki nota asagi. Acilisin aynadaki hali."""
    n = sn(0.27)
    o = buf(n)
    add(o, hava(n, 3800.0, 520.0, attack=0.012, tau=0.065, seed=22, damp=0.45), g=0.38)

    gov = osc(n, 280.0, 150.0)
    ge = env(n, 0.008, 0.055)
    for i in range(n):
        o[i] += gov[i] * ge[i] * 0.20

    add(o, fm(sn(0.17), N("C6"), TAHTA, tau=0.070, bend=0.3), sn(0.020), 0.42)
    add(o, fm(sn(0.19), N("G5"), TAHTA, tau=0.080, bend=0.3), sn(0.088), 0.40)

    o = lp1(o, 9500.0)
    return fade2(norm2(verb2(o, size=0.034, mix=0.20), 0.60), 0.8, 16.0)


def yetersiz():
    """Para yetmedi. Iki tok, kapali vurus -- yanki YOK, tepesi kesik. Sert bir
    hata bip'i degil; oyuncuyu azarlamadan 'hayir' demesi gerekiyor."""
    n = sn(0.30)
    o = buf(n)
    for k, at in enumerate((0.0, 0.098)):
        m = sn(0.13)
        v = buf(m)
        add(v, click(m, 620.0, 0.009, 31 + k), g=0.16)
        add(v, fm(m, N("G3") * (1.0 - 0.06 * k), GOVDE, tau=0.048, itau=0.016, bend=1.4), g=0.95)
        add(o, v, sn(at), 1.0 - k * 0.26)
    o = lp1(o, 900.0)
    o = soft(o, 1.7)
    return fade2(norm2([o, list(o)], 0.62), 0.8, 14.0)


# ------------------------------------------------------------------ ekonomi

def para():
    """Toplu para girisi. Klasik cam para: parlak, cinlayan, yukari bir dortlu."""
    n = sn(0.46)
    o = buf(n)
    add(o, click(n, 7600.0, 0.0022, 41), g=0.16)
    add(o, fm(sn(0.40), N("E6"), CAM, tau=0.105, itau=0.024, bend=0.35), 0, 0.85)
    add(o, fm(sn(0.35), N("A6"), CAM, tau=0.088, itau=0.019), sn(0.052), 0.62)
    add(o, pirilti(n, fc=7000.0, attack=0.06, tau=0.14, seed=43), g=0.055)
    o = lp1(o, 15000.0)
    return fade2(norm2(verb2(o, size=0.044, mix=0.28), 0.80), 0.6, 20.0)


def satis():
    """Her satista calar. Tek kucuk plink. Sik duyuldugu icin kisa, sakin ve
    diger seslerin ustune binmeyecek kadar alcak."""
    n = sn(0.21)
    o = buf(n)
    add(o, click(n, 6800.0, 0.0018, 42), g=0.11)
    add(o, fm(sn(0.18), N("G6"), CAM, tau=0.052, itau=0.013, bend=0.3), 0, 0.85)
    o = lp1(o, 13500.0)
    return fade2(norm2(verb2(o, size=0.030, mix=0.15), 0.46), 0.5, 14.0)


def yukseltme():
    """Yukseltme alindi: Do-Mi-Sol yukari, altinda bir govde vurusu. Ucuncu nota
    ile govde ayni anda geliyor -- 'oturdu' hissini veren sey bu."""
    n = sn(0.62)
    o = buf(n)
    add(o, click(n, 5200.0, 0.0025, 51), g=0.14)
    for k, ad in enumerate(("C6", "E6", "G6")):
        add(o, fm(sn(0.40), N(ad), TAHTA, tau=0.145, itau=0.040, bend=0.5),
            sn(0.062 * k), 0.80 - k * 0.06)
    add(o, fm(sn(0.24), N("C4") * 0.5, GOVDE, tau=0.075, itau=0.028, bend=2.0),
        sn(0.124), 0.40)
    add(o, pirilti(n, fc=7500.0, attack=0.16, tau=0.13, seed=53), g=0.045)
    o = lp1(o, 14000.0)
    return fade2(norm2(verb2(o, size=0.046, mix=0.28), 0.82), 0.7, 22.0)


def satin_alma():
    """Magaza satin almasi. Yukseltmenin buyugu: dort nota, gercek bir bas ve
    daha uzun bir kuyruk. Parayla alinan sey daha buyuk duyulmali."""
    n = sn(0.88)
    o = buf(n)
    add(o, click(n, 4600.0, 0.004, 61), g=0.16)
    for k, ad in enumerate(("C6", "E6", "G6", "C7")):
        add(o, fm(sn(0.60), N(ad), CAN if k == 3 else TAHTA, tau=0.185, itau=0.050, bend=0.45),
            sn(0.052 * k), 0.74 - k * 0.05)
    add(o, fm(sn(0.34), 98.0, GOVDE, tau=0.110, itau=0.040, bend=2.4), sn(0.010), 0.42)
    add(o, pirilti(n, fc=6800.0, attack=0.22, tau=0.24, seed=63), g=0.055)
    o = lp1(o, 14500.0)
    return fade2(norm2(verb2(o, size=0.056, mix=0.36), 0.86), 0.7, 28.0)


def odul():
    """Gunluk odul / reklam odulu. Pentatonik selale -- yedi nota yukari, ustune
    havada asili duran toz. Oyunun en cok 'hediye' duyulan sesi."""
    n = sn(1.10)
    o = buf(n)
    diz = ("G6", "A6", "C7", "D7", "E7", "G7", "A7")
    for k, ad in enumerate(diz):
        add(o, fm(sn(0.36), N(ad), CAM, tau=0.078, itau=0.020, bend=0.3),
            sn(0.049 * k), 0.62 - k * 0.058)
    add(o, fm(sn(0.50), N("C5"), CAN, tau=0.20, itau=0.06), sn(0.010), 0.22)
    add(o, pirilti(n, fc=7200.0, attack=0.13, tau=0.34, seed=71), g=0.075)
    o = lp1(o, 15500.0)
    return fade2(norm2(verb2(o, size=0.058, mix=0.40), 0.84), 0.8, 34.0)


# ------------------------------------------------------------------ ilerleme

def faz_yukseldi():
    """Faz gecisi -- oyunun en buyuk ani. Yukselis, darbe, akor, pirilti kuyrugu.
    Akor Do major: bina yikilip yenisi kalkarken calan sey bu."""
    n = sn(1.95)
    o = buf(n)

    # yukselis: gurultu bandi yukari kayar, gucu kareden hizli buyur
    yn = sn(0.72)
    yuk = noise(yn, 81)
    for i in range(yn):
        yuk[i] *= (i / yn) ** 2.4
    yuk = svf(yuk, lambda t: 240.0 * ((6000.0 / 240.0) ** min(1.0, t / 0.70)), 0.40, SR, "bp")
    add(o, yuk, 0, 0.30)

    # darbe
    dn = sn(0.48)
    add(o, click(dn, 900.0, 0.014, 82), sn(0.700), 0.28)
    add(o, fm(dn, 62.0, GOVDE, tau=0.145, itau=0.055, bend=3.0), sn(0.700), 0.66)

    # akor -- Do major + oktav
    for k, ad in enumerate(("C5", "E5", "G5", "C6", "E6")):
        add(o, fm(sn(1.15), N(ad), CAN, tau=0.46, itau=0.10, bend=0.35),
            sn(0.715 + 0.017 * k), 0.52 - k * 0.062)

    # pirilti kuyrugu
    for k, ad in enumerate(("C7", "E7", "G7")):
        add(o, fm(sn(0.55), N(ad), CAM, tau=0.19, itau=0.05), sn(0.90 + 0.125 * k), 0.14 - k * 0.032)
    add(o, pirilti(n, fc=7000.0, attack=0.85, tau=0.42, seed=83), g=0.06)

    o = lp1(o, 15000.0)
    return fade2(norm2(verb2(o, size=0.074, mix=0.44, decay=0.70), 0.90), 0.8, 45.0)


def tik():
    """Seviye pipi. Minik, yuksek, neredeyse sadece klik."""
    n = sn(0.038)
    o = buf(n)
    add(o, click(n, 6200.0, 0.0018, 91), g=0.30)
    add(o, fm(n, N("A7"), TAHTA, tau=0.0085, itau=0.0028), g=0.55)
    o = lp1(o, 13000.0)
    return fade2(norm2([o, list(o)], 0.44), 0.3, 5.0)


ISLER = (
    ("Arayuz/dokunus.wav", dokunus),
    ("Arayuz/geri.wav", geri),
    ("Arayuz/panel_ac.wav", panel_ac),
    ("Arayuz/panel_kapat.wav", panel_kapat),
    ("Arayuz/yetersiz.wav", yetersiz),
    ("Ekonomi/para.wav", para),
    ("Ekonomi/satis.wav", satis),
    ("Ekonomi/yukseltme.wav", yukseltme),
    ("Ekonomi/satin_alma.wav", satin_alma),
    ("Ekonomi/odul.wav", odul),
    ("Ilerleme/faz.wav", faz_yukseldi),
    ("Ilerleme/tik.wav", tik),
)

if __name__ == "__main__":
    for yol, fn in ISLER:
        print(write(os.path.join(KOK, yol.replace("/", os.sep)), fn()), flush=True)
