# Pazar avlusunun ek esyalari. Blender'in kendi python'unda kosar:
#
#   /Applications/Blender.app/Contents/MacOS/Blender -b --python Tools/blender/market_props.py
#
# market_models.py'nin yardimcilarini kullanir (kutu, silindir, pah, eksen takasi) — ayni kurallar
# gecerli: Unity olculeriyle yazilir, Blender'da -Y ileri, disa aktarici Unity'nin +Z ilerisine cevirir.
#
# PARCA ISIMLERI MALZEME ROLUDUR. Her nesne "M_<rol>" diye adlandiriliyor ve Unity tarafinda
# MarketYardDressing bu ada bakip malzemeyi veriyor — FBX'ten malzeme ithal edilmiyor. Roller:
#
#   M_Ahsap    adanin kendi kereste rengi     M_Bez      cuval bezi (sabit)
#   M_Metal    adanin kendi metal rengi       M_Yesil    yaprak (sabit)
#   M_Tas      adanin kendi beton rengi       M_Kirmizi  yangin kirmizisi (sabit)
#                                             M_Turuncu  uyari turuncusu (sabit)
#                                             M_Beyaz    kadran, kagit (sabit)
#
# Ikisi adaya bagli, gerisi degil: bir yangin tupu her adada kirmizidir, ama bir palet komur
# avlusunda kurumlu, altin avlusunda acik olmali.
#
# IKI AILE, ve orijinleri farkli:
#   ZEMIN esyalari kendi tabanlarinda durur (Unity tarafi zaten olcup zemine oturtuyor).
#   DUVAR esyalari ARKA yuzlerinde durur ve +Z'ye bakar — tabelalarla ayni kural, cunku
#   duvara sifir yapistirilip yana dondurulecekler.

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

try:
    import bpy
    from mathutils import Vector
except ImportError:
    bpy = None

import market_models as M


OUT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                    "..", "..", "Assets", "Resources", "Market", "Props"))


# TEK PARCALI ESYA OLMAZ. Unity, icinde tek bir mesh olan bir FBX'i koke cokertiyor ve o kokun
# adi DOSYA adi oluyor — yani "M_Ahsap" gidip "pallet" geliyor, ve Unity tarafindaki rol eslemesi
# hicbir sey bulamiyor. Bu yuzden tek rolden olusan esyalar bile iki nesne halinde disa aktariliyor
# (Blender ikincisini "M_Ahsap.001" diye adlandirir, ve rol eslemesi one ekten okundugu icin tutar).


def part(name, objects):
    """Bir rolun parcalarini tek nesnede birlestirip M_<rol> diye adlandirir."""
    ob = M.join("M_" + name, objects) if len(objects) > 1 else objects[0]
    ob.name = "M_" + name
    ob.data.name = "M_" + name
    return ob


def box(size, at=(0, 0, 0), bevel=0.012, yaw=0.0, pitch=0.0):
    """
    Unity olculu, pahli, istege bagli dondurulmus bir kutu.

    HEM OLCU HEM YER Unity duzeninde: (en, YUKSEKLIK, derinlik). market_models.box olcuyu ceviriyor
    ama merkezi Blender koordinati bekliyor, ve ikisini karistirmak butun esyayi yan yatiriyor —
    ilk gecişte palet ust uste dizilmis kalaslar, el arabasi da havada iki cubuk olarak cikti.
    """
    at = (at[0], at[2], at[1])                           # Unity (x, yukari, derinlik) -> Blender
    ob = M.box("kutu", size, at)
    if bevel > 0:
        M.bevel(ob, bevel, 1)
    if yaw or pitch:
        ob.rotation_euler = (math.radians(pitch), 0.0, math.radians(yaw))
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return ob


# ------------------------------------------------------------------------------ zemin esyalari

def pallet():
    """Standart palet. Avlunun her yerinde tek basina ya da bir seyin altinda durabilir."""
    W, D = 1.30, 1.00
    tahta = []
    # Uc kat ve ortadaki kat digerlerine DIK. Hepsi ayni yone bakinca palet degil, ust uste
    # dizilmis bes kalas oluyor — capraz duzen paleti palet yapan tek sey.
    for i in range(3):                                   # alt tahtalar, en boyunca
        tahta.append(box((W, 0.06, 0.13), (0.0, 0.03, (i - 1) * (D / 2 - 0.07))))
    for i in range(3):                                   # boyuna kirisler, derinlik boyunca
        tahta.append(box((0.12, 0.10, D), ((i - 1) * (W / 2 - 0.06), 0.11, 0.0)))
    ust = []
    for i in range(5):                                   # ust tahtalar, yine en boyunca
        ust.append(box((W, 0.05, 0.15), (0.0, 0.185, (i - 2) * (D / 4.4))))
    return [part("Ahsap", tahta), part("Ahsap", ust)]


def sacks():
    """Dort cuval, ikisi ustte. Yumusak birseyin yaninda durmasi butun kose sert bir odayi
    yumusatiyor — avludaki her sey kutu, sandik ya da varil."""
    govde = []
    for i, (x, z, y, s, yaw) in enumerate((
            (-0.30, -0.22, 0.00, 1.00, 12.0),
            (0.30, -0.18, 0.00, 0.96, -20.0),
            (0.02, 0.24, 0.00, 1.02, 34.0),
            (-0.02, -0.02, 0.42, 0.92, -8.0))):
        # Cuval bir kutu degil: alti genis, ustu dar, kenarlari fazlasiyla pahli.
        c = M.bevel(M.box("cuval%d" % i, (0.62 * s, 0.44 * s, 0.44 * s),
                          (x, z, y + 0.22 * s)), 0.13 * s, 3)
        c.rotation_euler = (0.0, 0.0, math.radians(yaw))
        bpy.context.view_layer.objects.active = c
        bpy.ops.object.select_all(action="DESELECT")
        c.select_set(True)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        govde.append(c)
    alt = part("Bez", govde[:2])
    ust = part("Bez", govde[2:])
    M.shade(alt, 30.0)
    M.shade(ust, 30.0)
    return [alt, ust]


def hand_truck():
    """
    Kantar arabasi, duvara dayali.

    Once DIK kuruluyor, egim en sonda birlesmis govdeye bir kerede veriliyor. Ilk gecişte her parca
    tek tek egilmisti ve hepsi kendi orijini etrafinda dondugu icin arabanin parcalari birbirinden
    ayrildi — tek parcaya tek donus, sekli bozamayacak olan.
    """
    metal = []
    for j in (-1, 1):                                    # iki kol
        metal.append(box((0.07, 1.30, 0.07), (j * 0.26, 0.65, 0.0)))
    metal.append(box((0.60, 0.07, 0.07), (0.0, 1.24, 0.0)))     # ust capraz
    metal.append(box((0.60, 0.07, 0.07), (0.0, 0.66, 0.0)))     # orta capraz
    for j in (-1, 1):                                    # tekerler, iskeletin arkasinda
        w = M.cylinder("teker%d" % j, 0.17, 0.17, 0.06, sides=12, base_z=-0.03)
        w.rotation_euler = (0.0, math.radians(90.0), 0.0)       # ekseni Unity X'e cevir
        w.location = (j * 0.34, 0.10, 0.17)                     # Blender +Y = arkada
        bpy.context.view_layer.objects.active = w
        bpy.ops.object.select_all(action="DESELECT")
        w.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)
        metal.append(w)

    ahsap = [box((0.62, 0.05, 0.34), (0.0, 0.04, -0.20))]       # ayak plakasi, one uzanir
    m = part("Metal", metal)
    M.shade(m, 40.0)
    a = part("Ahsap", ahsap)

    # Ve simdi hepsi birden geriye yaslanir.
    for ob in (m, a):
        ob.rotation_euler = (math.radians(-15.0), 0.0, 0.0)
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.select_all(action="DESELECT")
        ob.select_set(True)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return [m, a]


def scale():
    """Platform kantari. Bir pazarda tartilmadan hicbir sey el degistirmez, ve bu avluda
    tartiyi hatirlatan tek sey buydu — yoktu."""
    metal = [box((0.95, 0.10, 0.75), (0.0, 0.06, 0.0)),          # platform
             box((0.08, 1.05, 0.08), (0.0, 0.60, -0.30)),        # direk
             box((0.46, 0.34, 0.10), (0.0, 1.22, -0.30))]        # kadran kutusu
    beyaz = [box((0.34, 0.24, 0.03), (0.0, 1.22, -0.243), bevel=0.006)]
    return [part("Metal", metal), part("Beyaz", beyaz)]


def bench():
    """Sira bekleyenler icin bank. Kuyrugun yaninda degil — kuyruk zaten yurunen bir cizgi —
    ama ayni odada olmasi burasinin insanlarin bekledigi bir yer oldugunu soyluyor."""
    ahsap = []
    for i in range(3):
        ahsap.append(box((1.90, 0.075, 0.19), (0.0, 0.46, (i - 1) * 0.235)))
    for i in range(2):                                   # arkalik
        ahsap.append(box((1.90, 0.075, 0.17), (0.0, 0.72 + i * 0.22, 0.30), pitch=-8.0))
    metal = []
    for j in (-1, 1):
        metal.append(box((0.07, 0.46, 0.60), (j * 0.78, 0.23, 0.0)))
        metal.append(box((0.07, 0.52, 0.07), (j * 0.78, 0.72, 0.28)))
    return [part("Ahsap", ahsap), part("Metal", metal)]


def plant():
    """Saksi. Bir depoya konan tek canli sey, ve tam da bu yuzden ise yariyor: etrafindaki her
    seyin ne kadar sert oldugunu gosteriyor."""
    pot = M.cylinder("saksi", 0.30, 0.38, 0.44, sides=14, base_z=0.0)
    rim = M.cylinder("agiz", 0.40, 0.39, 0.07, sides=14, base_z=0.40)
    tas = part("Tas", [pot, rim])
    M.shade(tas, 45.0)

    yaprak = []
    for i in range(7):
        a = math.radians(i * 51.4)
        lean = 26.0 + (i % 3) * 9.0
        leaf = M.box("yaprak%d" % i, (0.13, 0.62, 0.30), (0.0, 0.0, 0.31))
        M.bevel(leaf, 0.05, 2)
        leaf.rotation_euler = (math.radians(lean), 0.0, a)
        leaf.location = (math.cos(a) * 0.13, math.sin(a) * 0.13, 0.44)
        bpy.context.view_layer.objects.active = leaf
        bpy.ops.object.select_all(action="DESELECT")
        leaf.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)
        yaprak.append(leaf)
    y = part("Yesil", yaprak)
    M.shade(y, 40.0)
    return [tas, y]


def cone():
    """Trafik konisi. Rampanin agzinda duracak — orasi kamyonlarin bosalttigi yer ve bir uyari
    seridinin yaninda duran koni o seridin ne dedigini tekrar ediyor."""
    taban = box((0.44, 0.05, 0.44), (0.0, 0.025, 0.0), bevel=0.015)
    govde = M.cylinder("govde", 0.19, 0.045, 0.62, sides=12, base_z=0.04)
    t = part("Turuncu", [taban, govde])
    M.shade(t, 40.0)
    band = M.cylinder("serit", 0.135, 0.115, 0.10, sides=12, base_z=0.30, cap=False)
    b = part("Beyaz", [band])
    M.shade(b, 40.0)
    return [t, b]


def toolchest():
    """Takim dolabi. Cekmeceleri olan tek esya — yatay cizgiler avlunun her yerindeki dikey
    kaburgalara karsi duruyor."""
    kirmizi = [box((0.90, 0.86, 0.52), (0.0, 0.51, 0.0))]
    # Cekmece araliklari govdenin ON yuzunun ONUNDE. Ilk gecişte 0.265'teydiler, yani 0.52
    # derinlikli bir govdenin yuzeyiyle bes milim farkla es duzlemde — hicbir sey gorunmuyordu.
    for i in range(3):
        kirmizi.append(box((0.80, 0.055, 0.035), (0.0, 0.24 + i * 0.24, 0.275)))
    metal = [box((0.86, 0.16, 0.48), (0.0, 0.08, 0.0))]           # kaide
    for i in range(3):
        metal.append(box((0.34, 0.055, 0.075), (0.0, 0.31 + i * 0.24, 0.30)))    # kollar
    return [part("Kirmizi", kirmizi), part("Metal", metal)]


# ------------------------------------------------------------------------------ duvar esyalari
#
# Hepsinin orijini ARKA yuzunde ve hepsi +Z'ye bakar. Unity tarafi bunlari duvarin ic yuzune
# sifir koyup yana donduruyor, yani buradaki her sey +Z yonunde buyumeli.

def extinguisher():
    """Yangin tupu ve askisi."""
    metal = [box((0.16, 0.30, 0.05), (0.0, 0.02, 0.0)),            # duvar plakasi
             box((0.20, 0.05, 0.13), (0.0, 0.20, 0.11)),           # ust kelepce
             box((0.20, 0.05, 0.13), (0.0, -0.20, 0.11))]          # alt kelepce
    # Dik, ve one kaydirilmis. Ilk gecişte X ekseninde 90 derece dondurulmustu ve tup duvarda
    # yan yatiyordu — silindirin ekseni zaten Blender Z, yani yukari; dondurmeye gerek yok.
    body = M.cylinder("tup", 0.115, 0.115, 0.52, sides=12, base_z=-0.26)
    body.location = (0.0, -0.17, 0.0)                    # Blender -Y = Unity +Z, yani one
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    k = part("Kirmizi", [body])
    M.shade(k, 40.0)
    m = part("Metal", metal + [box((0.05, 0.14, 0.05), (0.06, 0.30, 0.15))])   # hortum agzi
    return [k, m]


def vent():
    """Duvar menfezi. Panjurlari egik, yani isik onlarda kirilir — duz bir izgara sadece bir desen."""
    cerceve = [box((0.90, 0.66, 0.06), (0.0, 0.0, 0.03)),
               box((0.90, 0.07, 0.10), (0.0, 0.295, 0.06)),
               box((0.90, 0.07, 0.10), (0.0, -0.295, 0.06))]
    # Panjurlar cercevenin ON yuzunden acikca disari cikmali; es duzlemde bir izgara desen olur,
    # kabartma olmaz — ve kabartma olmayan bir menfez sadece duvara cizilmis bir dikdortgen.
    panjur = []
    for i in range(5):
        panjur.append(box((0.80, 0.075, 0.13), (0.0, 0.20 - i * 0.10, 0.10), pitch=34.0))
    return [part("Metal", cerceve), part("Metal", panjur)]


def pipes():
    """Duvar boyunca giden boru demeti ve iki kelepce. Yatay, cunku duvarin kendi kaburgalari
    dikey — ustunden gecen yatay bir cizgi duvari duz bir yuzey olmaktan cikariyor."""
    metal = []
    for i, (r, lift) in enumerate(((0.10, 0.20), (0.075, 0.0), (0.06, -0.17))):
        p = M.cylinder("boru%d" % i, r, r, 3.60, sides=10, base_z=-1.80)
        p.rotation_euler = (0.0, math.radians(90.0), 0.0)   # ekseni Unity X'e cevir
        p.location = (0.0, -(0.10 + r), lift)               # Blender -Y = one, Z = yukari
        bpy.context.view_layer.objects.active = p
        bpy.ops.object.select_all(action="DESELECT")
        p.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)
        metal.append(p)
    kelepce = [box((0.11, 0.52, 0.20), (j * 1.35, 0.0, 0.07)) for j in (-1, 1)]
    m = part("Metal", metal)
    M.shade(m, 40.0)
    return [m, part("Metal", kelepce)]


def notice():
    """Ilan panosu. Uzerindeki kagitlar okunmuyor ve okunmasi da gerekmiyor — bir duvarda asili
    kagit, orada calisan birileri oldugunu soyleyen en kisa cumle."""
    ahsap = [box((1.30, 0.90, 0.05), (0.0, 0.0, 0.0))]
    for name, size, at in (("ust", (1.34, 0.09, 0.09), (0.0, 0.45, 0.02)),
                           ("alt", (1.34, 0.09, 0.09), (0.0, -0.45, 0.02)),
                           ("sol", (0.09, 0.90, 0.09), (-0.62, 0.0, 0.02)),
                           ("sag", (0.09, 0.90, 0.09), (0.62, 0.0, 0.02))):
        ahsap.append(box(size, at))
    kagit = []
    for x, y, w, h, yaw in ((-0.32, 0.16, 0.36, 0.46, 3.0), (0.14, 0.20, 0.30, 0.38, -5.0),
                            (-0.20, -0.24, 0.28, 0.30, -2.0), (0.30, -0.22, 0.34, 0.26, 6.0)):
        kagit.append(box((w, h, 0.014), (x, y, 0.042), bevel=0.0))
    return [part("Ahsap", ahsap), part("Beyaz", kagit)]


def clock():
    """Duvar saati. Akrepleri sabit — donen bir saat, oyunun kendi saatiyle uyusmadigi anda
    yanlis bir saat olur, ve durmus bir saat kimsenin dikkatini cekmez."""
    ring = M.cylinder("kasa", 0.34, 0.34, 0.09, sides=18, base_z=0.0)
    ring.rotation_euler = (math.radians(-90.0), 0.0, 0.0)
    bpy.context.view_layer.objects.active = ring
    bpy.ops.object.select_all(action="DESELECT")
    ring.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    m = part("Metal", [ring])
    M.shade(m, 45.0)

    face = M.cylinder("kadran", 0.29, 0.29, 0.02, sides=18, base_z=0.0)
    face.rotation_euler = (math.radians(-90.0), 0.0, 0.0)
    face.location = (0.0, 0.0, 0.075)
    bpy.context.view_layer.objects.active = face
    bpy.ops.object.select_all(action="DESELECT")
    face.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=False)
    beyaz = part("Beyaz", [face])
    M.shade(beyaz, 45.0)

    akrep = [box((0.035, 0.20, 0.012), (0.0, 0.08, 0.10)),
             box((0.16, 0.032, 0.012), (0.06, 0.0, 0.10))]
    return [m, beyaz, part("Kirmizi", akrep)]


BUILDS = [
    ("pallet", pallet), ("sacks", sacks), ("hand_truck", hand_truck), ("scale", scale),
    ("bench", bench), ("plant", plant), ("cone", cone), ("toolchest", toolchest),
    ("extinguisher", extinguisher), ("vent", vent), ("pipes", pipes),
    ("notice", notice), ("clock", clock),
]


def main():
    M.OUT = OUT                                          # market_models.export bunu kullaniyor
    for name, maker in BUILDS:
        M.clear()
        M.export(name, maker())


if __name__ == "__main__":
    if bpy is None:
        sys.exit("Blender'in python'unda kosmali: Blender -b --python market_props.py")
    main()
