# Pazar avlusunun kaplamalari. Blender'in kendi python'unda kosar:
#
#   /Applications/Blender.app/Contents/MacOS/Blender -b --python Tools/blender/market_textures.py
#
# NEDEN LUMINANS: avlunun renkleri MarketTheme'den geliyor ve her ada icin farkli. Kaplamalar
# _BaseColor ile CARPILDIGI icin, buradaki her doku ortalamasi 1.0'a yakin gri olmak zorunda —
# koyu bir doku kömür avlusunu tamamen karartir. Detay albedodan degil, normal haritasindan gelir.
#
# HEPSI TEKRARLANABILIR (seamless). Kutular dunya olcegine gore UV aliyor, yani ayni doku 40
# birimlik bir duvarda 10 kere yan yana diziliyor; ek yeri gorunen bir doku orada bir cizgi olur.

import os
import sys

import numpy as np

try:
    import bpy
except ImportError:                                     # duz python ile de calissin
    bpy = None

RES = 512
OUT = os.path.join(os.path.dirname(__file__), "..", "..",
                   "Assets", "Resources", "Market", "Textures")
OUT = os.path.normpath(OUT)


# --------------------------------------------------------------------------------------- gurultu

def _grid(res, cells, rng):
    """Bir hucre izgarasini sarmalayarak bilinear buyutur — kenarlari birbirine denk gelir."""
    g = rng.random((cells, cells)).astype(np.float32)
    t = (np.arange(res, dtype=np.float32) * cells / res)
    i0 = np.floor(t).astype(np.int32) % cells
    i1 = (i0 + 1) % cells
    f = t - np.floor(t)
    f = f * f * (3.0 - 2.0 * f)                          # smoothstep, kose kose gecis olmasin

    a = g[np.ix_(i0, i0)]
    b = g[np.ix_(i0, i1)]
    c = g[np.ix_(i1, i0)]
    d = g[np.ix_(i1, i1)]
    fx = f[None, :]
    fy = f[:, None]
    return (a * (1 - fx) * (1 - fy) + b * fx * (1 - fy) +
            c * (1 - fx) * fy + d * fx * fy)


def _grid_xy(res, cx, cy, rng):
    """_grid gibi, ama iki eksende ayri hucre sayisiyla — damar gibi uzamis desenler icin."""
    g = rng.random((cy, cx)).astype(np.float32)

    def axis(cells):
        t = np.arange(res, dtype=np.float32) * cells / res
        i0 = np.floor(t).astype(np.int32) % cells
        f = t - np.floor(t)
        return i0, (i0 + 1) % cells, (f * f * (3.0 - 2.0 * f))

    x0, x1, fx = axis(cx)
    y0, y1, fy = axis(cy)
    fx, fy = fx[None, :], fy[:, None]
    return (g[np.ix_(y0, x0)] * (1 - fx) * (1 - fy) + g[np.ix_(y0, x1)] * fx * (1 - fy) +
            g[np.ix_(y1, x0)] * (1 - fx) * fy + g[np.ix_(y1, x1)] * fx * fy)


def fbm_xy(res, cx, cy, octaves, rng, gain=0.5):
    out = np.zeros((res, res), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        out += _grid_xy(res, cx * (2 ** o), cy * (2 ** o), rng) * amp
        total += amp
        amp *= gain
    return out / total


def fbm(res, cells, octaves, rng, gain=0.5):
    out = np.zeros((res, res), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        out += _grid(res, cells * (2 ** o), rng) * amp
        total += amp
        amp *= gain
    return out / total


def uv(res):
    """0..1 arasi u (yatay) ve v (dikey) koordinat duzlemleri."""
    a = (np.arange(res, dtype=np.float32) + 0.5) / res
    return np.meshgrid(a, a)                             # u, v


def band(x, centre, half, soft):
    """Sarmalanan bir serit: |x - centre| periyodik olarak olculur, kenarlari yumusatilir."""
    d = np.abs(((x - centre + 0.5) % 1.0) - 0.5)
    return 1.0 - np.clip((d - half) / max(soft, 1e-5), 0.0, 1.0)


def dots(res, xs, ys, radius, soft=0.35):
    """Verilen (u,v) noktalarina sarmalanan yuvarlak kabartilar."""
    u, v = uv(res)
    out = np.zeros((res, res), np.float32)
    for cx, cy in zip(xs, ys):
        du = ((u - cx + 0.5) % 1.0) - 0.5
        dv = ((v - cy + 0.5) % 1.0) - 0.5
        d = np.sqrt(du * du + dv * dv) / radius
        out = np.maximum(out, np.clip((1.0 - d) / soft, 0.0, 1.0) ** 0.6)
    return np.minimum(out, 1.0)


# ------------------------------------------------------------------------------------ kaplamalar

def wall_panel(rng):
    """Dikey nervurlu sanayi duvar paneli: kaburgalar, civata siralari, bir panel eki."""
    u, v = uv(RES)
    ribs = 6.0
    # Trapez kaburga: sinusun tabanini kirp, tepesi duz kalsin — yuvarlak bir dalga plastik durur.
    wave = np.clip(np.cos(2 * np.pi * u * ribs) * 1.9, -1.0, 1.0) * 0.5 + 0.5
    height = wave * 0.55

    # Kaburgalarin arasindaki oluk. Kaburga tepesinden ayri bir cizgi, yoksa yuzey tek dalga olur.
    groove = band(u, 0.5 / ribs, 0.006, 0.008)
    for k in range(1, int(ribs)):
        groove = np.maximum(groove, band(u, (k + 0.5) / ribs, 0.006, 0.008))
    height -= groove * 0.22

    # Panel eki: karo basina bir yatay derz. v=0'da, yani karo sinirinda — iki karo arasinda tek
    # cizgi olarak birlesir.
    seam = band(v, 0.0, 0.010, 0.006)
    height -= seam * 0.42

    # Civatalar, ekin iki yaninda ve kaburga tepelerinde.
    heads = (dots(RES, [(k + 0.0) / ribs for k in range(int(ribs))], [0.045] * int(ribs), 0.016) +
             dots(RES, [(k + 0.0) / ribs for k in range(int(ribs))], [0.955] * int(ribs), 0.016))
    height += np.minimum(heads, 1.0) * 0.30

    grime = fbm(RES, 4, 4, rng)
    albedo = 0.90 + (height - 0.28) * 0.16 + (grime - 0.5) * 0.10
    albedo -= seam * 0.10
    return albedo, height + (grime - 0.5) * 0.05


def floor_concrete(rng):
    """Dokme beton: derz izgarasi, mala izleri, agrega benekleri, lekeler."""
    u, v = uv(RES)
    coarse = fbm(RES, 3, 5, rng)
    swirl = fbm(RES, 6, 3, rng)
    speck = fbm(RES, 64, 2, rng)

    # Karo basina bir plaka: derzler karo sinirinda, u=0 ve v=0.
    joint = np.maximum(band(u, 0.0, 0.007, 0.004), band(v, 0.0, 0.007, 0.004))

    height = coarse * 0.25 + swirl * 0.10 + speck * 0.06
    height -= joint * 0.70

    albedo = 0.95 + (coarse - 0.5) * 0.26 + (speck - 0.5) * 0.09
    albedo -= joint * 0.38
    # Birkac koyu leke. Temiz beton yeni beton demek; burasi bir depo zemini.
    stain = np.clip((fbm(RES, 3, 4, rng) - 0.58) * 2.6, 0.0, 1.0)
    albedo -= stain * 0.20
    return albedo, height


def roof_corrugated(rng):
    """Oluklu sac: sinus dalgasi, bindirme eki ve boyuna pas cizgileri."""
    u, v = uv(RES)
    waves = 8.0
    height = (np.cos(2 * np.pi * u * waves) * 0.5 + 0.5) * 0.65
    lap = band(u, 0.0, 0.014, 0.008)
    height += lap * 0.18
    streak = fbm(RES, 2, 3, rng) * 0.6 + fbm(RES, 16, 2, rng) * 0.4
    streak = np.repeat(streak[:1, :], RES, axis=0) * 0.5 + streak * 0.5   # boyuna uzat
    albedo = 0.92 + (height - 0.32) * 0.12 + (streak - 0.5) * 0.09
    return albedo, height


def wood_plank(rng):
    """Yatay kalaslar: damar, kalas basi araliklari ve civi izleri."""
    u, v = uv(RES)
    planks = 4.0
    idx = np.floor(v * planks)
    gap = np.zeros((RES, RES), np.float32)
    for k in range(int(planks)):
        gap = np.maximum(gap, band(v, k / planks, 0.004, 0.005))

    # Damar KALASIN BOYUNCA uzar. Izotropik gurultu burada bulut gibi lekeler veriyordu — damar
    # cizgi olmali, leke degil. Bu yuzden u ekseninde 2, v ekseninde 24 hucre: desen yatayda genis,
    # dikeyde sik. Uzerine binen sinus da sadece v'ye bakiyor, yani cizgiler yatay kaliyor.
    warp = fbm_xy(RES, 2, 24, 3, rng)
    grain = np.sin((v * 96.0 + warp * 5.0) * np.pi) * 0.5 + 0.5
    grain = grain * 0.75 + fbm_xy(RES, 3, 40, 2, rng) * 0.25

    # Her kalas biraz farkli tonda. Ayni tonda dort kalas tek bir tahta gibi okunur.
    tone = np.take(np.linspace(-0.042, 0.042, int(planks)), idx.astype(np.int32) % int(planks))

    nails = dots(RES, [0.10, 0.10, 0.60, 0.60], [0.12, 0.62, 0.37, 0.87], 0.011)

    height = grain * 0.14 - gap * 0.85 - nails * 0.45
    albedo = 0.94 + (grain - 0.5) * 0.15 + tone
    albedo -= gap * 0.45
    albedo -= nails * 0.18
    return albedo, height


def metal_plate(rng):
    """Baklava desenli sac: siralar halinde capraz kabartilar ve kose percinleri."""
    u, v = uv(RES)
    n = 6.0

    # Kabartilar HUCRE HUCRE ciziliyor, iki capraz dalganin kesisimiyle degil. Dalga carpimi
    # denendi ve baklava vermiyor: iki cizgi ailesinin ust uste binmesi bir haç izgarasi cikariyor.
    # Bir hucre + icinde bir cubuk, ve cubugun yonu satir satir degisiyor — gercek gozyasi sacinin
    # yapisi bu, ve kenari da net cikiyor.
    cu, cv = u * n, v * n
    ix, iy = np.floor(cu), np.floor(cv)
    su = (cu - ix) * 2.0 - 1.0                           # hucre icinde -1..1
    sv = (cv - iy) * 2.0 - 1.0

    k = np.sqrt(0.5)
    flip = np.where((iy.astype(np.int32) % 2) == 0, 1.0, -1.0)
    along = (su + sv * flip) * k                         # cubugun boyu yonunde
    across = (su * flip - sv) * k                        # enine

    half_len, half_wide, soft = 0.66, 0.17, 0.10
    lo = np.clip((half_len - np.abs(along)) / soft, 0.0, 1.0)
    hi = np.clip((half_wide - np.abs(across)) / soft, 0.0, 1.0)
    tread = np.minimum(lo, hi)
    tread = tread * tread * (3.0 - 2.0 * tread)          # kenarina pah

    rivets = dots(RES, [0.04, 0.96, 0.04, 0.96], [0.04, 0.04, 0.96, 0.96], 0.030)
    scuff = fbm(RES, 5, 4, rng)

    height = tread * 0.55 + rivets * 0.45 + (scuff - 0.5) * 0.06
    albedo = 0.90 + tread * 0.11 + rivets * 0.07 + (scuff - 0.5) * 0.15
    return albedo, height


def hazard(rng):
    """Capraz uyari seritleri. Sari MarketYardBuild'den tint olarak gelir, doku sadece desen."""
    u, v = uv(RES)
    n = 4.0
    s = (u + v) * n
    stripe = ((s % 1.0) < 0.5).astype(np.float32)
    # Kenarlarini biraz yumusat: keskin kenar telefonda titrer.
    edge = np.abs(((s % 1.0) - 0.5)) * 2.0
    soft = np.clip((edge - 0.90) / 0.10, 0.0, 1.0)
    stripe = stripe * (1.0 - soft) + 0.5 * soft

    wear = fbm(RES, 6, 4, rng)
    chip = np.clip((wear - 0.58) * 3.2, 0.0, 1.0)
    albedo = 1.0 - stripe * 0.62
    albedo = albedo * (1.0 - chip * 0.35) + chip * 0.30
    height = (wear - 0.5) * 0.10 - chip * 0.15
    return albedo, height


def banner(rng):
    """Bez afis: dokuma dokusu, dikis kenarlari ve bir sevron seridi."""
    u, v = uv(RES)
    weave = (np.sin(2 * np.pi * u * 128) * np.sin(2 * np.pi * v * 128)) * 0.5 + 0.5
    fold = fbm(RES, 3, 3, rng)

    hem = np.maximum(band(v, 0.06, 0.006, 0.004), band(v, 0.94, 0.006, 0.004))
    chevron = np.abs(((u * 6.0 + np.abs(v - 0.5) * 3.0) % 1.0) - 0.5) * 2.0
    chevron = np.clip((chevron - 0.45) / 0.12, 0.0, 1.0)
    inside = band(v, 0.5, 0.20, 0.02)

    albedo = 0.96 + (weave - 0.5) * 0.05 + (fold - 0.5) * 0.12
    albedo -= chevron * inside * 0.30
    albedo -= hem * 0.15
    height = (weave - 0.5) * 0.05 + (fold - 0.5) * 0.30 + hem * 0.20
    return albedo, height


# ------------------------------------------------------------------------------------- yazma

def to_normal(height, strength=1.0):
    """Yukseklikten teget uzayda normal haritasi. Sarmalanarak turevlenir, yoksa kenarda dikis olur."""
    h = height.astype(np.float32)
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5
    dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5
    nx = -dx * strength * 8.0
    ny = -dy * strength * 8.0
    nz = np.ones_like(h)
    inv = 1.0 / np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.dstack((nx * inv * 0.5 + 0.5, ny * inv * 0.5 + 0.5, nz * inv * 0.5 + 0.5))


def save(name, rgb, srgb, alpha=None):
    """
    Blender'in resim tamponuna yazip PNG olarak kaydeder.

    Tampon HER ZAMAN dogrusal. sRGB isaretli bir resmi kaydederken Blender tampondan dosyaya
    giderken sRGB'ye kodluyor — yani dosyada istedigimiz degerin cikmasi icin tampona o degerin
    dogrusal karsiligini koymak gerekiyor. Normal haritasi Non-Color, oraya deger oldugu gibi gider.
    """
    path = os.path.join(OUT, name + ".png")
    h, w = rgb.shape[0], rgb.shape[1]
    rgb = np.clip(rgb, 0.0, 1.0).astype(np.float32)
    if srgb:
        rgb = np.where(rgb <= 0.04045, rgb / 12.92,
                       np.power((rgb + 0.055) / 1.055, 2.4)).astype(np.float32)

    a = np.ones((h, w, 1), np.float32) if alpha is None else np.clip(alpha, 0.0, 1.0).reshape(h, w, 1)
    rgba = np.dstack((rgb, a.astype(np.float32)))
    rgba = np.flipud(rgba)                               # Blender satirlari alttan sayar

    img = bpy.data.images.new(name, width=w, height=h, alpha=alpha is not None,
                              float_buffer=True, is_data=not srgb)
    img.colorspace_settings.name = "sRGB" if srgb else "Non-Color"
    img.pixels.foreach_set(np.ascontiguousarray(rgba).ravel())
    img.file_format = "PNG"
    img.filepath_raw = path
    img.save()
    bpy.data.images.remove(img)
    print("YAZILDI", path)



# ------------------------------------------------------------------------------------- rozetler
#
# Kuyruktaki musterinin kafasinin ustunde duran kucuk isaret. Dort tane: memnun, bekliyor,
# sabri tukendi, ve VIP. Yazi degil sekil, cunku bunlar on bir dilde ayni kalmali ve bir
# telefonda bu kadar kucuk bir alanda okunabilecek tek sey silüet.

BADGE = 128


def _badge_base(colour):
    """Dolu daire + beyaz cerceve. Disi saydam, yani rozet gercekten yuvarlak gorunur."""
    u, v = uv(BADGE)
    # y YUKARI dogru buyur. save() tamponu ters cevirdigi icin v=0 dosyada UST satir oluyor —
    # duz alinca gulen yuz ters cikiyordu, agiz yukarida.
    x, y = (u - 0.5) * 2.0, (0.5 - v) * 2.0
    r = np.sqrt(x * x + y * y)
    soft = 2.0 / BADGE * 3.0
    disc = 1.0 - np.clip((r - 0.92) / soft, 0.0, 1.0)
    rim = np.clip((r - 0.74) / soft, 0.0, 1.0) * disc
    rgb = np.dstack([np.full((BADGE, BADGE), c, np.float32) for c in colour])
    rgb = rgb * (1.0 - rim[..., None]) + np.float32(0.98) * rim[..., None]
    return x, y, rgb, disc


def _stamp(rgb, mask, colour=0.99):
    return rgb * (1.0 - mask[..., None]) + np.float32(colour) * mask[..., None]


def badge_happy():
    """Yesil, gulumseyen yuz."""
    x, y, rgb, a = _badge_base((0.24, 0.72, 0.36))
    r = np.sqrt(x * x + y * y)
    soft = 6.0 / BADGE
    # Agiz: bir halkanin alt yarisi.
    mouth = (np.abs(r - 0.42) < 0.10) & (y < -0.06)
    eyes = (((x + 0.26) ** 2 + (y - 0.30) ** 2) < 0.017) | \
           (((x - 0.26) ** 2 + (y - 0.30) ** 2) < 0.017)
    return _stamp(rgb, (mouth | eyes).astype(np.float32)), a


def badge_wait():
    """Kehribar, saat: iki akrep."""
    x, y, rgb, a = _badge_base((0.92, 0.68, 0.18))
    ring = np.abs(np.sqrt(x * x + y * y) - 0.52) < 0.075
    up = (np.abs(x) < 0.075) & (y > 0.0) & (y < 0.46)
    side = (np.abs(y) < 0.075) & (x > 0.0) & (x < 0.34)
    return _stamp(rgb, (ring | up | side).astype(np.float32)), a


def badge_cross():
    """Kirmizi, unlem."""
    x, y, rgb, a = _badge_base((0.86, 0.26, 0.22))
    bar = (np.abs(x) < 0.11) & (y > -0.12) & (y < 0.56)
    dot = (x * x + (y + 0.38) ** 2) < 0.022
    return _stamp(rgb, (bar | dot).astype(np.float32)), a


def badge_vip():
    """Altin, alti kollu yildiz — ust uste iki ucgen, cizmesi ve okunmasi en kolay yildiz."""
    x, y, rgb, a = _badge_base((0.98, 0.78, 0.16))

    def triangle(flip):
        yy = y * flip
        # Uc yari duzlemin kesisimi: tabani asagida eskenar ucgen.
        return (yy > -0.34) & (yy < 0.86 - 1.5 * np.abs(x)) & (np.abs(x) < 0.62)

    star = triangle(1.0) | triangle(-1.0)
    return _stamp(rgb, star.astype(np.float32)), a


BADGES = [
    ("T_Mood_Happy", badge_happy),
    ("T_Mood_Wait", badge_wait),
    ("T_Mood_Cross", badge_cross),
    ("T_Mood_Vip", badge_vip),
]


MAKERS = [
    ("T_Market_Wall",   wall_panel,      101),
    ("T_Market_Floor",  floor_concrete,  202),
    ("T_Market_Roof",   roof_corrugated, 303),
    ("T_Market_Wood",   wood_plank,      404),
    ("T_Market_Metal",  metal_plate,     505),
    ("T_Market_Hazard", hazard,          606),
    ("T_Market_Banner", banner,          707),
]


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, maker, seed in MAKERS:
        rng = np.random.default_rng(seed)                # ayni tohum, ayni doku — her kosuda
        albedo, height = maker(rng)
        albedo = np.clip(albedo, 0.0, 1.0)
        save(name, np.dstack((albedo, albedo, albedo)), srgb=True)
        save(name + "_N", to_normal(height), srgb=False)
    for name, maker in BADGES:
        rgb, alpha = maker()
        save(name, rgb, srgb=True, alpha=alpha)


if __name__ == "__main__":
    if bpy is None:
        sys.exit("Blender'in python'unda kosmali: Blender -b --python market_textures.py")
    main()
