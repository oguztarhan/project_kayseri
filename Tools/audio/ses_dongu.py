"""Donguler -> Assets/Audio/Ortam + Dunya (22.05 kHz).

Dikissizlik kurali: yatak once uretilip capraz gecisle donguye cevrilir, olaylar
(marti, cekic, cinlama) DONGUDEN SONRA ustune eklenir. Boylece bir olay dikise
denk gelip yarim kalmaz.

Bunlar saatlerce dinlenecek. Kural: hicbir sey on plana cikmayacak. Tekrar eden
her olay ya cok seyrek ya cok alcak; yatakta ani genlik ya da tiz enerji yok.
"""
import math
import os

from ses_dsp import (CAM, GOVDE, NOTA, add, buf, click, env, fm, hp1, lp1,
                     loopify, noise, norm, osc, soft, svf, verb, write)

KOK = r"A:\Github\project_kayseri\Assets\Audio"
LSR = 22050


def sn(x):
    return int(x * LSR)


def lfo(n, hz, lo, hi, faz=0.0):
    out = [0.0] * n
    for i in range(n):
        s = 0.5 + 0.5 * math.sin(2.0 * math.pi * hz * i / LSR + faz)
        out[i] = lo + (hi - lo) * s
    return out


# ------------------------------------------------------------------ ortam

def ruzgar(n, seed, faz):
    """Agac tepesi ruzgari. Eskisinden pes ve daha yumusak sardirmali: tiz bant
    kulakta 'hisirti' olarak birikiyor, alcak bant 'hava' olarak kaliyor."""
    x = noise(n, seed)
    x = svf(x, lambda t: 240.0 + 380.0 * (0.5 + 0.5 * math.sin(2.0 * math.pi * 0.052 * t + faz)),
            0.92, LSR, "bp")
    g = lfo(n, 0.073, 0.38, 1.0, faz * 1.7)
    for i in range(n):
        x[i] *= g[i]
    return lp1(x, 1800.0, LSR)


def dalga(n, seed, faz):
    """Uzak kiyi. Uc farkli periyodun carpimi -- hicbiri digerine tam bolunmedigi
    icin kulak tekrari yakalayamiyor."""
    x = lp1(noise(n, seed), 420.0, LSR)
    for i in range(n):
        t = i / LSR
        s = (0.5 + 0.5 * math.sin(2.0 * math.pi * t / 3.4 + faz)) * 0.45 \
            + (0.5 + 0.5 * math.sin(2.0 * math.pi * t / 4.7 + faz * 2.1)) * 0.35 \
            + (0.5 + 0.5 * math.sin(2.0 * math.pi * t / 6.1 + faz * 0.6)) * 0.20
        x[i] *= s * s
    return x


def pedal(n):
    """Cok alcak Do pedali: iki oktav ve bir beslik, yavas nefes alan bir yigin.
    Yatagi bir tona baglar -- efektler pentatonikten geldigi icin ne calarsa
    calsin ortamla uyusur. Ozellikle 'muzik' gibi duyulmayacak kadar sessiz."""
    o = buf(n)
    for f, g in ((NOTA["C4"] * 0.25, 1.00), (NOTA["C4"] * 0.5, 0.42), (NOTA["G4"] * 0.5, 0.20)):
        t = osc(n, f, sr=LSR)
        for i in range(n):
            o[i] += t[i] * g
    am = lfo(n, 0.037, 0.45, 1.0)
    for i in range(n):
        o[i] *= am[i]
    return lp1(o, 400.0, LSR)


def kus(sure=0.62, seed=91):
    """Uzakta bir kus. Eski marti keskin ve tekrar tekrar duyulunca sinir bozucuydu;
    bu daha yumusak, uc kisa yukselen nota ve agir bir kuyruk."""
    n = sn(sure)
    o = buf(n)
    for k, at in enumerate((0.0, 0.155, 0.295)):
        m = sn(0.11)
        v = buf(m)
        f0 = 1180.0 + k * 120.0
        t = osc(m, f0, f0 * 1.22, sr=LSR)
        e = env(m, 0.018, 0.030, sr=LSR)
        for i in range(m):
            vib = 1.0 + 0.035 * math.sin(2.0 * math.pi * 17.0 * i / LSR)
            v[i] = t[i] * e[i] * vib
        add(o, v, sn(at), 0.85 - k * 0.24)
    o = lp1(o, 2600.0, LSR)
    o = verb(o, LSR, size=0.095, decay=0.74, mix=0.60)
    return norm(o, 0.5)


def ortam_ada():
    """Global ortam yatagi: ruzgar + uzak dalga + alcak pedal + iki kus."""
    ham = sn(20.0)
    kanal = []
    for c, (sr_w, sr_d, faz) in enumerate(((101, 111, 0.0), (202, 212, 1.9))):
        x = buf(ham)
        add(x, ruzgar(ham, sr_w, faz), g=0.44)
        add(x, dalga(ham, sr_d, faz), g=0.56)
        kanal.append(x)

    ped = pedal(ham)
    for x in kanal:
        add(x, ped, g=0.085)

    kanal = [loopify(x, 2.0, LSR) for x in kanal]

    k = kus()
    add(kanal[0], k, sn(5.80), 0.085)
    add(kanal[1], k, sn(5.80), 0.052)
    add(kanal[0], k, sn(13.35), 0.046)
    add(kanal[1], k, sn(13.35), 0.076)

    return [norm(x, 0.56) for x in kanal]


# ------------------------------------------------------------------ dunya

def dunya_izabe():
    """Izabe: derin firin ugultusu + yavas kukreme + ara ara metal cinlamasi.
    Kukreme artik iki ayri hizda nefes aliyor, tek LFO'nun mekanik tekrari gitti."""
    ham = sn(9.0)
    o = buf(ham)
    for f, g in ((55.0, 1.00), (82.3, 0.58), (110.7, 0.30), (165.2, 0.11)):
        t = osc(ham, f, sr=LSR)
        for i in range(ham):
            o[i] += t[i] * g * 0.23
    kuk = lp1(noise(ham, 301), 760.0, LSR)
    a1 = lfo(ham, 0.29, 0.42, 1.0)
    a2 = lfo(ham, 0.11, 0.60, 1.0, 2.2)
    for i in range(ham):
        o[i] += kuk[i] * a1[i] * a2[i] * 0.44
    o = loopify(o, 1.0, LSR)

    for at, g, f in ((2.05, 0.13, 615.0), (5.60, 0.09, 784.0)):
        c = fm(sn(0.40), f, CAM, tau=0.10, itau=0.028, sr=LSR)
        c = lp1(c, 4600.0, LSR)
        add(o, c, int(at * LSR), g)
    return [norm(o, 0.60)]


def dunya_deniz():
    """Liman: art arda binen dalga kabarmalari + kopuk hisirtisi."""
    ham = sn(11.0)
    o = lp1(noise(ham, 401), 640.0, LSR)
    kop = hp1(noise(ham, 402), 2400.0, LSR)
    for i in range(ham):
        t = i / LSR
        s = (0.5 + 0.5 * math.sin(2.0 * math.pi * t / 3.667)) * 0.42 \
            + (0.5 + 0.5 * math.sin(2.0 * math.pi * t / 5.500 + 1.3)) * 0.34 \
            + (0.5 + 0.5 * math.sin(2.0 * math.pi * t / 2.750 + 2.6)) * 0.24
        s *= s
        o[i] = o[i] * s + kop[i] * (s ** 3.5) * 0.26
    o = loopify(o, 1.5, LSR)
    return [norm(o, 0.58)]


def dunya_maden():
    """Maden: bant takirtisi + duzenli kazma darbeleri (donguye tam bolunur).
    Darbeler artik FM govde: eskisi ciplak sinus oldugu icin 'bip' gibi duyuluyordu."""
    vurus = 1.45
    ham = sn(vurus * 4.0 + 1.0)
    o = lp1(noise(ham, 501), 340.0, LSR)
    for i in range(ham):
        o[i] *= 0.55 + 0.45 * math.sin(2.0 * math.pi * 6.5 * i / LSR)
    bant = osc(ham, 208.0, sr=LSR)
    ba = lfo(ham, 0.9, 0.25, 0.7)
    for i in range(ham):
        o[i] = o[i] * 0.55 + bant[i] * ba[i] * 0.045
    o = loopify(o, 1.0, LSR)

    for k in range(4):
        m = sn(0.32)
        v = buf(m)
        add(v, fm(m, 148.0 - k * 5.0, GOVDE, tau=0.045, itau=0.016, sr=LSR, bend=2.6), g=0.95)
        add(v, click(m, 1100.0, 0.010, 511 + k, LSR), g=0.30)
        add(o, v, int(k * vurus * LSR), 0.58 - (k % 2) * 0.12)
    return [soft(norm(o, 0.64), 1.2)]


ISLER = (
    ("Ortam/ada.wav", ortam_ada),
    ("Dunya/izabe.wav", dunya_izabe),
    ("Dunya/deniz.wav", dunya_deniz),
    ("Dunya/maden.wav", dunya_maden),
)

if __name__ == "__main__":
    for yol, fn in ISLER:
        print(write(os.path.join(KOK, yol.replace("/", os.sep)), fn(), LSR), flush=True)
