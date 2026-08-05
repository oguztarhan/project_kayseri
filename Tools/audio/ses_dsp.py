"""Saf Python DSP kutusu -- numpy yok, sadece stdlib. 44.1 kHz.

Eski takim saf toplamsal sinus zillerinden kuruluydu; ince ve cansiz duruyordu.
Bu takimin tasarim kurallari:

  * Tonal her vurus IKI OPERATORLU FM'dir. Modulasyon indeksi genlikten hizli soner:
    ses parlak baslar, sicak biter. Sabit sinus yiginlarinin veremedigi sey budur.
  * Her vurusun onunde 2-5 ms YUKSEK GECIREN gurultu klik'i olur -- bant gecirenli
    "sss" degil, temiz bir "tk".
  * Perde atakta yarim ton yukaridan gelip yerine oturur (bend): parmakla cekilmis hissi.
  * Butun notalar TEK bir dizidendir (Do major pentatonik). Iki ses ust uste binerse
    hicbir zaman catismaz -- idle oyunda bu her dakika olur.
  * Kuyruk stereo, atak mono: iki kanala farkli tarak boylari verilir. Genis durur ama
    telefon hoparlorunde tek kanala dustugunde faz sorunu cikmaz.
"""
import array
import math
import os
import random
import wave

SR = 44100


# ---------------------------------------------------------------- temel


def buf(n):
    return [0.0] * n


def add(dst, src, at=0, g=1.0):
    n = len(dst)
    for i in range(len(src)):
        j = at + i
        if 0 <= j < n:
            dst[j] += src[i] * g


def noise(n, seed=0):
    r = random.Random(seed)
    return [r.uniform(-1.0, 1.0) for _ in range(n)]


def osc(n, f0, f1=None, sr=SR, ph=0.0):
    """Sinus; f1 verilirse f0'dan f1'e ustel kayar."""
    if f1 is None:
        f1 = f0
    out = [0.0] * n
    span = max(1, n - 1)
    ratio = (f1 / f0) if (f0 > 0 and f1 > 0) else 1.0
    for i in range(n):
        f = f0 * (ratio ** (i / span))
        ph += 2.0 * math.pi * f / sr
        out[i] = math.sin(ph)
    return out


def env(n, attack, tau, sr=SR):
    """Dogrusal atak + ustel sonum."""
    out = [0.0] * n
    an = max(1, int(attack * sr))
    dn = max(1.0, tau * sr)
    for i in range(n):
        out[i] = (i / an) if i < an else math.exp(-(i - an) / dn)
    return out


# ---------------------------------------------------------------- filtreler


def lp1(x, fc, sr=SR):
    a = 1.0 - math.exp(-2.0 * math.pi * fc / sr)
    y = 0.0
    out = [0.0] * len(x)
    for i in range(len(x)):
        y += a * (x[i] - y)
        out[i] = y
    return out


def hp1(x, fc, sr=SR):
    low = lp1(x, fc, sr)
    return [x[i] - low[i] for i in range(len(x))]


def svf(x, fc, damp=0.7, sr=SR, mode="lp"):
    """Chamberlin durum-degiskenli filtre. fc sabit sayi ya da t(saniye) alan fonksiyon."""
    n = len(x)
    out = [0.0] * n
    low = band = 0.0
    dyn = callable(fc)
    f = 0.0
    if not dyn:
        f = 2.0 * math.sin(math.pi * min(fc, sr * 0.45) / sr)
    for i in range(n):
        if dyn:
            c = fc(i / sr)
            f = 2.0 * math.sin(math.pi * min(max(c, 20.0), sr * 0.45) / sr)
        high = x[i] - low - damp * band
        band += f * high
        low += f * band
        out[i] = low if mode == "lp" else (band if mode == "bp" else high)
    return out


def verb(x, sr=SR, size=0.037, decay=0.62, mix=0.28):
    """Kucuk Schroeder tarak yankisi -- mekan hissi, ucuz."""
    n = len(x)
    out = list(x)
    for k, g in ((1.00, 0.84), (1.37, 0.76), (1.71, 0.69), (2.13, 0.61)):
        d = max(1, int(sr * size * k))
        fb = decay * g
        comb = [0.0] * n
        for i in range(n):
            comb[i] = x[i] + (comb[i - d] * fb if i >= d else 0.0)
        m = mix * 0.25
        for i in range(n):
            out[i] += comb[i] * m
    return out


def verb2(x, sr=SR, size=0.037, decay=0.62, mix=0.28):
    """Stereo yanki: iki kanal farkli tarak boyu alir.

    Atak iki kanalda da ayni oldugu icin ortada sabit durur; genisleyen sadece kuyruktur.
    Tek hoparlorde toplandiginda kuyruk biraz incelir, atak hic bozulmaz.
    """
    return [verb(x, sr, size, decay, mix), verb(x, sr, size * 1.21, decay, mix)]


# ---------------------------------------------------------------- yapi taslari

# Do major pentatonik. Butun efektler bu havuzdan nota secer.
NOTA = {
    "G3": 196.00, "C4": 261.63, "E4": 329.63, "G4": 392.00, "A4": 440.00,
    "C5": 523.25, "D5": 587.33, "E5": 659.25, "G5": 783.99, "A5": 880.00,
    "C6": 1046.50, "D6": 1174.66, "E6": 1318.51, "G6": 1567.98, "A6": 1760.00,
    "C7": 2093.00, "D7": 2349.32, "E7": 2637.02, "G7": 3135.96, "A7": 3520.00,
}

# Malzeme reçeteleri: (oran, indeks) -- oran modulator/tasiyici, indeks parlaklik.
TAHTA = (3.00, 2.6)      # marimba / tokmak: tok, sicak
CAM = (3.51, 4.6)        # cinlayan metal: para, pirilti
CAN = (1.41, 3.2)        # inharmonik can: buyuk anlar
GOVDE = (1.00, 1.4)      # harmonik, kalin: darbe ve govde


def fm(n, f, malzeme=TAHTA, tau=0.12, itau=None, sr=SR, bend=0.0, bend_t=0.013):
    """Iki operatorlu FM vurus.

    malzeme : (oran, indeks) ikilisi -- yukaridaki TAHTA / CAM / CAN / GOVDE.
    tau     : genlik sonum sabiti (sn).
    itau    : indeks sonumu; verilmezse tau'nun ucte biri. Genlikten KISA olmali,
              yoksa ses bastan sona ayni parlaklikta kalir ve sentetik duyulur.
    bend    : atakta perdenin kac yarim ton yukaridan gelecegi. 0.4-0.8 "cekilmis"
              hissi verir, 2+ acik bir dusus jesti olur.
    """
    if itau is None:
        itau = tau * 0.32
    out = [0.0] * n
    k = 2.0 * math.pi / sr
    pc = pm = 0.0
    oran, indeks = malzeme
    bukum = (2.0 ** (bend / 12.0)) - 1.0
    for i in range(n):
        t = i / sr
        fc = f * (1.0 + bukum * math.exp(-t / bend_t)) if bend else f
        pm += k * fc * oran
        pc += k * fc
        ix = indeks * math.exp(-t / itau)
        out[i] = math.sin(pc + ix * math.sin(pm)) * math.exp(-t / tau)
    return out


def click(n, fc=4200.0, dur=0.0035, seed=1, sr=SR):
    """Kisa yuksek geciren gurultu vurusu -- tonal vurusun onune konan 'tk'."""
    m = min(n, max(2, int(dur * sr)))
    x = noise(m, seed)
    for i in range(m):
        x[i] *= math.exp(-i / (m * 0.22))
    x = hp1(x, fc, sr)
    out = [0.0] * n
    add(out, x)
    return out


def hava(n, f0, f1, sr=SR, attack=0.06, tau=0.08, seed=7, damp=0.5):
    """Suzulen hava: bant gecirenin merkezi f0'dan f1'e kayar. Panel gecisleri icin."""
    x = noise(n, seed)
    e = env(n, attack, tau, sr)
    for i in range(n):
        x[i] *= e[i]
    sure = max(1e-4, n / sr * 0.72)
    return svf(x, lambda t: f0 * ((f1 / f0) ** min(1.0, t / sure)), damp, sr, "bp")


def pirilti(n, sr=SR, fc=6500.0, attack=0.09, tau=0.28, seed=71):
    """Yuksek, havada asili duran toz -- odul ve faz kuyruklarinda."""
    x = noise(n, seed)
    e = env(n, attack, tau, sr)
    for i in range(n):
        x[i] *= e[i]
    return hp1(x, fc, sr)


# ---------------------------------------------------------------- cikis


def norm(x, peak=0.86):
    m = 0.0
    for v in x:
        a = abs(v)
        if a > m:
            m = a
    if m < 1e-9:
        return x
    k = peak / m
    return [v * k for v in x]


def norm2(chans, peak=0.86):
    """Iki kanali ORTAK tepeye gore olceklendirir -- yoksa stereo goruntu kayar."""
    m = 0.0
    for c in chans:
        for v in c:
            a = abs(v)
            if a > m:
                m = a
    if m < 1e-9:
        return chans
    k = peak / m
    return [[v * k for v in c] for c in chans]


def soft(x, drive=1.35):
    k = math.tanh(drive)
    return [math.tanh(v * drive) / k for v in x]


def fade(x, ms_in=1.5, ms_out=10.0, sr=SR):
    n = len(x)
    ni = max(1, int(sr * ms_in / 1000.0))
    no = max(1, int(sr * ms_out / 1000.0))
    for i in range(min(ni, n)):
        x[i] *= i / ni
    for i in range(min(no, n)):
        x[n - 1 - i] *= i / no
    return x


def fade2(chans, ms_in=1.5, ms_out=12.0, sr=SR):
    return [fade(c, ms_in, ms_out, sr) for c in chans]


def loopify(x, xf, sr):
    """Kuyrugu basa capraz gecisle karistirip dikissiz dongu yapar."""
    m = max(1, int(xf * sr))
    n = len(x)
    out = x[:n - m]
    for i in range(m):
        k = i / m
        out[i] = x[n - m + i] * (1.0 - k) + x[i] * k
    return out


def write(path, chans, sr=SR):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    n = len(chans[0])
    data = array.array("h")
    for i in range(n):
        for c in chans:
            v = c[i]
            if v > 1.0:
                v = 1.0
            elif v < -1.0:
                v = -1.0
            data.append(int(v * 32767.0))
    w = wave.open(path, "wb")
    try:
        w.setnchannels(len(chans))
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(data.tobytes())
    finally:
        w.close()
    rms = math.sqrt(sum(v * v for v in chans[0]) / max(1, n))
    pk = max(abs(v) for v in chans[0])
    return "%-30s %5.2f sn  %dch @%d  tepe %.2f  rms %.3f  %d KB" % (
        os.path.basename(path), n / sr, len(chans), sr, pk, rms,
        os.path.getsize(path) // 1024)
