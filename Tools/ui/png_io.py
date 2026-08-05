"""Kucuk 8-bit RGBA PNG okuyucu/yazici -- stdlib disinda bagimlilik yok.

Makinede PIL kurulu degil (bkz. README: rasterizer da yok, Chrome kullaniliyor). Sprite
uzerinde piksel isi gerektiginde -- ton cevirme, ustune ikon bindirme -- bu kadari yetiyor.
Sadece 8-bit RGBA (renk tipi 6) okur; pipeline'in urettigi butun PNG'ler oyle.
"""
import struct
import zlib


def oku(yol):
    """PNG -> (genislik, yukseklik, bytearray RGBA)."""
    ham = open(yol, "rb").read()
    i = 8
    w = h = bit = renk = 0
    veri = b""
    while i < len(ham):
        boy = struct.unpack(">I", ham[i:i + 4])[0]
        tip = ham[i + 4:i + 8]
        govde = ham[i + 8:i + 8 + boy]
        if tip == b"IHDR":
            w, h, bit, renk = struct.unpack(">IIBB", govde[:10])
        elif tip == b"IDAT":
            veri += govde
        i += 12 + boy
    if bit != 8 or renk != 6:
        raise ValueError("sadece 8-bit RGBA: %s (bit=%d renk=%d)" % (yol, bit, renk))

    duz = zlib.decompress(veri)
    bpp = 4
    satir = w * bpp
    px = bytearray(w * h * bpp)
    onceki = bytearray(satir)
    p = 0
    for y in range(h):
        f = duz[p]
        p += 1
        cur = bytearray(duz[p:p + satir])
        p += satir
        if f == 1:
            for x in range(bpp, satir):
                cur[x] = (cur[x] + cur[x - bpp]) & 255
        elif f == 2:
            for x in range(satir):
                cur[x] = (cur[x] + onceki[x]) & 255
        elif f == 3:
            for x in range(satir):
                a = cur[x - bpp] if x >= bpp else 0
                cur[x] = (cur[x] + (a + onceki[x]) // 2) & 255
        elif f == 4:
            for x in range(satir):
                a = cur[x - bpp] if x >= bpp else 0
                b = onceki[x]
                c = onceki[x - bpp] if x >= bpp else 0
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                cur[x] = (cur[x] + pr) & 255
        px[y * satir:(y + 1) * satir] = cur
        onceki = cur
    return w, h, px


def yaz(yol, w, h, px):
    """bytearray RGBA -> PNG (filtre 0, en yuksek sikistirma)."""
    satir = w * 4
    ham = bytearray()
    for y in range(h):
        ham.append(0)
        ham += px[y * satir:(y + 1) * satir]

    def parca(tip, govde):
        c = struct.pack(">I", len(govde)) + tip + govde
        return c + struct.pack(">I", zlib.crc32(tip + govde) & 0xFFFFFFFF)

    out = (b"\x89PNG\r\n\x1a\n"
           + parca(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
           + parca(b"IDAT", zlib.compress(bytes(ham), 9))
           + parca(b"IEND", b""))
    open(yol, "wb").write(out)
    return len(out)


def olcekle(w, h, px, nw, nh):
    """Kutu filtresiyle kucultur -> (nw, nh, bytearray RGBA).

    Renkler alfayla carpilarak ortalaniyor; yoksa saydam piksellerin (cogu zaman siyah)
    rengi kenarlara sizip ikonun etrafinda koyu bir hale birakiyor.
    """
    yeni = bytearray(nw * nh * 4)
    for y in range(nh):
        y0, y1 = y * h // nh, max(y * h // nh + 1, (y + 1) * h // nh)
        for x in range(nw):
            x0, x1 = x * w // nw, max(x * w // nw + 1, (x + 1) * w // nw)
            sr = sg = sb = sa = 0.0
            n = 0
            for sy in range(y0, y1):
                for sx in range(x0, x1):
                    i = (sy * w + sx) * 4
                    a = px[i + 3] / 255.0
                    sr += px[i] * a
                    sg += px[i + 1] * a
                    sb += px[i + 2] * a
                    sa += a
                    n += 1
            o = (y * nw + x) * 4
            if sa > 0.0:
                yeni[o] = min(255, int(round(sr / sa)))
                yeni[o + 1] = min(255, int(round(sg / sa)))
                yeni[o + 2] = min(255, int(round(sb / sa)))
            yeni[o + 3] = int(round(sa / n * 255.0))
    return nw, nh, yeni


def bindir(alt_w, alt_h, alt, ust_w, ust_h, ust, x0, y0):
    """ust'u alt'in uzerine (x0, y0) noktasindan alfa-over ile bindirir. alt yerinde degisir."""
    for y in range(ust_h):
        ay = y0 + y
        if not (0 <= ay < alt_h):
            continue
        for x in range(ust_w):
            ax = x0 + x
            if not (0 <= ax < alt_w):
                continue
            u = (y * ust_w + x) * 4
            ua = ust[u + 3] / 255.0
            if ua <= 0.0:
                continue
            a = (ay * alt_w + ax) * 4
            aa = alt[a + 3] / 255.0
            ya = ua + aa * (1.0 - ua)
            for k in range(3):
                alt[a + k] = int(round((ust[u + k] * ua + alt[a + k] * aa * (1.0 - ua)) / ya))
            alt[a + 3] = int(round(ya * 255.0))
