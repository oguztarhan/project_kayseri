"""Pazar avlusunun sesleri -> Assets/Audio/Ortam + Dunya.

    python3 Tools/audio/ses_pazar.py

Uc parca:
  Ortam/pazar.wav      avlunun oda tonu (dongu, 22.05 kHz stereo)
  Dunya/kapi_zili.wav  musteri kapidan girdiginde
  Dunya/vip_gelis.wav  VIP musteri girdiginde

AILEYE UYUM: butun malzemeler ses_dsp'den, butun tonal vuruslar Do major pentatonikten
ve FM. Pazar adanin akrabasi olmali — ayni oyunun icinde bir oda, baska bir oyun degil.
Ortam yataginin kurali da ada ile ayni: hicbir sey on plana cikmaz, tekrar eden her sey
ya cok seyrek ya cok alcak. Saatlerce dinlenecek.

YOL: ses_dongu.py ve ses_efekt.py'deki KOK sabiti Windows makinesinin diskini gosteriyor.
Burada dosyanin kendi yerinden hesaplaniyor, yani iki makinede de kosar.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from ses_dsp import (CAM, GOVDE, TAHTA, NOTA, SR, add, buf, click, fm, lp1, loopify,
                     noise, norm, osc, pirilti, soft, svf, write)

KOK = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                    "..", "..", "Assets", "Audio"))
LSR = 22050


def sn(x, sr=LSR):
    return int(x * sr)


def lfo(n, hz, lo, hi, faz=0.0, sr=LSR):
    out = [0.0] * n
    for i in range(n):
        s = 0.5 + 0.5 * math.sin(2.0 * math.pi * hz * i / sr + faz)
        out[i] = lo + (hi - lo) * s
    return out


# ------------------------------------------------------------------------------ ortam

def _oda(n, seed):
    """Kapali bir hacmin kendi ugultusu: cok pes, hemen hemen sabit, sadece var."""
    o = buf(n)
    for f, g in ((41.0, 1.00), (61.5, 0.52), (82.0, 0.24)):
        t = osc(n, f, sr=LSR)
        for i in range(n):
            o[i] += t[i] * g * 0.16
    kaba = lp1(noise(n, seed), 210.0, LSR)
    for i in range(n):
        o[i] += kaba[i] * 0.30
    return o


def _havalandirma(n, seed, faz):
    """
    Binanin havasi: yavas nefes alan alcak bir hava bandi.

    ONCE PERVANEYDI, saniyede 1.33 kanat vuran. O ses tavandaki pervanenin sesiydi ve pervane
    kaldirildi — gorunurde donen hicbir sey yokken ritmik bir vuvu duymak, sesin nereden geldigini
    arattiran turden bir yanlislik. Vurus gitti, hava kaldi: iki cok yavas dalgalanma, ikisi de
    birbirine bolunmuyor, yani kulak tekrari yakalayamiyor.
    """
    x = svf(noise(n, seed),
            lambda t: 300.0 + 150.0 * math.sin(2.0 * math.pi * 0.07 * t + faz),
            0.85, LSR, "bp")
    slow = lfo(n, 0.041, 0.70, 1.0, faz * 2.3)
    slower = lfo(n, 0.017, 0.80, 1.0, faz * 0.9)
    for i in range(n):
        x[i] *= slow[i] * slower[i]
    return lp1(x, 900.0, LSR)


def _ugultu(n, seed, faz):
    """Uzaktaki kalabalik: konusmanin sozleri degil, sadece bandi ve kabarmasi.

    300-900 Hz arasi, cok yavas dalgalanan. Daha tizi 'kalabalik' degil 'hisirti' oluyor,
    daha pesi ugultudan cikip ugultunun altindaki odaya karisiyor."""
    x = svf(noise(n, seed), lambda t: 430.0 + 260.0 * math.sin(2.0 * math.pi * 0.038 * t + faz),
            0.90, LSR, "bp")
    a1 = lfo(n, 0.083, 0.30, 1.0, faz)
    a2 = lfo(n, 0.031, 0.55, 1.0, faz * 1.7)
    for i in range(n):
        x[i] *= a1[i] * a2[i]
    return lp1(x, 1500.0, LSR)


def ortam_pazar():
    """Avlunun oda tonu: hacim ugultusu + havalandirma + uzak kalabalik, uzerinde iki uzak tak.

    Olaylar DONGUDEN SONRA ekleniyor, ada yataginda oldugu gibi — dikise denk gelen bir
    olay yarim kalir ve her dongude ayni yerde tiklar.
    """
    ham = sn(18.0)
    kanal = []
    for c, (s1, s2, s3, faz) in enumerate(((811, 821, 831, 0.0), (812, 822, 832, 2.1))):
        x = buf(ham)
        add(x, _oda(ham, s1), g=0.52)
        add(x, _havalandirma(ham, s2, faz), g=0.26)
        add(x, _ugultu(ham, s3, faz), g=0.34)
        kanal.append(x)

    kanal = [loopify(x, 2.5, LSR) for x in kanal]

    # Uzakta bir yerde birinin bir sey indirmesi. Iki kere, on sekiz saniyede — yani
    # neredeyse hic. Sag ve sol farkli anlarda, boylece oda genis duyuluyor.
    for at, g, f, kanal_i in ((4.35, 0.055, 132.0, 0), (11.80, 0.042, 118.0, 1)):
        v = buf(sn(0.5))
        add(v, fm(sn(0.5), f, GOVDE, tau=0.075, itau=0.02, sr=LSR, bend=1.8), g=0.9)
        add(v, click(sn(0.5), 900.0, 0.008, int(f), LSR), g=0.22)
        v = lp1(v, 2200.0, LSR)
        add(kanal[kanal_i], v, sn(at), g)
        add(kanal[1 - kanal_i], v, sn(at + 0.011), g * 0.6)

    return [norm(x, 0.52) for x in kanal]


# ------------------------------------------------------------------------------ efektler

def kapi_zili():
    """Musteri kapidan girdi. Iki notali, alcalan degil YUKSELEN kucuk bir zil.

    Duvarci degil dukkanci sesi: CAM malzemesi, kisa kuyruk, tepesi yok. Bu ses sik
    calacak — kutuphanedeki minInterval onu seyrek tutuyor ama yine de gunde yuzlerce
    kez duyulacak, yani hicbir zaman one cikmamali. Zirve 0.5'te tutuluyor.
    """
    n = sn(0.75, SR)
    o = buf(n)
    for at, nota, g in ((0.000, "G5", 1.00), (0.085, "C6", 0.78)):
        v = fm(n, NOTA[nota], CAM, tau=0.20, itau=0.045, sr=SR)
        add(o, v, sn(at, SR), g * 0.42)
    add(o, click(n, 5200.0, 0.0030, 91, SR), g=0.16)
    o = lp1(o, 8200.0, SR)
    return [norm(o, 0.50)]


def vip_gelis():
    """VIP geldi. Ayni aileden ama uc notali ve yukari acilan — kapi zilinin buyugu.

    Bunun one cikmasi GEREKIYOR: rozet oyuncunun bakmadigi yerde de olabilir, ses ise
    her yerde. Yine de bir odul sesi degil bir haber sesi — pirilti ince tutuluyor,
    yoksa her dokuz musteride bir kutlama yapiyor gibi olur.
    """
    n = sn(1.10, SR)
    o = buf(n)
    for at, nota, g in ((0.000, "E5", 0.95), (0.075, "G5", 0.88), (0.150, "C6", 1.00)):
        v = fm(n, NOTA[nota], CAM, tau=0.28, itau=0.055, sr=SR)
        add(o, v, sn(at, SR), g * 0.44)
    # Altina ince bir govde, yoksa ses ekranda degil kulakta duruyor.
    add(o, fm(n, NOTA["C4"], TAHTA, tau=0.16, itau=0.04, sr=SR), g=0.16)
    add(o, pirilti(n, SR, fc=7200.0, attack=0.10, tau=0.30, seed=93), g=0.13)
    add(o, click(n, 5600.0, 0.0032, 94, SR), g=0.14)
    o = lp1(o, 11000.0, SR)
    return [soft(norm(o, 0.62), 1.15)]


ISLER = (
    ("Ortam/pazar.wav", ortam_pazar, LSR),
    ("Dunya/kapi_zili.wav", kapi_zili, SR),
    ("Dunya/vip_gelis.wav", vip_gelis, SR),
)

if __name__ == "__main__":
    for yol, fn, sr in ISLER:
        print(write(os.path.join(KOK, yol.replace("/", os.sep)), fn(), sr), flush=True)
