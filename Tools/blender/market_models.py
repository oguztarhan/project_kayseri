# Pazar avlusunun modelleri. Blender'in kendi python'unda kosar:
#
#   /Applications/Blender.app/Contents/MacOS/Blender -b --python Tools/blender/market_models.py
#
# EKSENLER: Blender'da Z yukari, -Y ileri. FBX disa aktarici bunu Unity'nin Y-yukari/+Z-ileri
# duzenine ceviriyor, yani burada bir seyi -Y'ye bakacak sekilde kurarsan Unity'de +Z'ye bakar.
# Asagidaki her olcu once Unity'de istenen olcu olarak yaziliyor, sonra bu takasla kuruluyor —
# kodun icindeki blockSize gibi sayilarla yan yana okunabilsin diye.
#
# ORIJIN KURALI, ve modelin nereye oturdugunu bu belirliyor:
#   - kulce ve para destesi MERKEZLERINDE dururlar (kodda ilkel kutunun yerini aliyorlar, o da
#     merkezinde duruyor)
#   - tabela ARKA yuzunde durur, duvara sifir yapistirilabilsin diye
#   - raf ve fanin govdesi TABANINDA durur

import math
import os
import sys

try:
    import bpy
    import bmesh
    from mathutils import Vector
except ImportError:
    bpy = None

OUT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..",
                                    "Assets", "Resources", "Market", "Models"))


# ------------------------------------------------------------------------------------- yardimci

def clear():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.objects):
        for item in list(block):
            block.remove(item)


def unity(x, y, z):
    """Unity (x=en, y=boy, z=derinlik) olcusunu Blender (x, y, z) duzenine cevirir."""
    return Vector((x, z, y))


def mesh_from(name, verts, faces):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.validate()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    return ob


def box(name, size, centre=(0, 0, 0)):
    """Unity olcusuyle verilen bir kutu; merkezi Blender koordinatinda."""
    s = unity(*size) * 0.5
    c = Vector(centre)
    v = [(c.x + sx * s.x, c.y + sy * s.y, c.z + sz * s.z)
         for sx, sy, sz in ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1),
                            (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1))]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return mesh_from(name, v, f)


def bevel(ob, width, segments=2, angle=40.0):
    """
    Kenar pahi. Bu oyunun butun modellerinde ayni is: dogrudan kutu, telefon ekraninda oyuncak
    degil bitmemis is gibi duruyor — pahin yakaladigi ince isik cizgisi bir cismi kati gosteren
    tek sey. Acinin altindaki kenarlar atlaniyor, yoksa yuvarlak bir govdenin butun teseli pahlanir.
    """
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    edges = [e for e in bm.edges if e.calc_face_angle(0.0) > math.radians(angle)]
    if edges:
        bmesh.ops.bevel(bm, geom=edges, offset=width, offset_type="OFFSET",
                        segments=segments, profile=0.5, affect="EDGES", clamp_overlap=True)
    bm.to_mesh(me)
    bm.free()
    return ob


def cylinder(name, radius_bottom, radius_top, height, sides=16, base_z=0.0, cap=True):
    """Koni/silindir govdesi. base_z tabani nereye koyacagini soyler."""
    verts, faces = [], []
    for i in range(sides):
        a = 2 * math.pi * i / sides
        verts.append((math.cos(a) * radius_bottom, math.sin(a) * radius_bottom, base_z))
    for i in range(sides):
        a = 2 * math.pi * i / sides
        verts.append((math.cos(a) * radius_top, math.sin(a) * radius_top, base_z + height))
    for i in range(sides):
        j = (i + 1) % sides
        faces.append((i, j, sides + j, sides + i))
    if cap:
        faces.append(tuple(range(sides - 1, -1, -1)))
        faces.append(tuple(range(sides, sides * 2)))
    return mesh_from(name, verts, faces)


def join(name, parts):
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    ob = bpy.context.active_object
    ob.name = name
    ob.data.name = name
    return ob


def shade(ob, smooth_angle=35.0):
    """Duz golgeli parcalar plastik durur; aci esigi silindirleri yuvarlatip kutulari birakiyor."""
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    # 5.x'te aci esigi mesh uzerinde degil, "acidan yumusat" islecinde.
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle))


def export(name, objects):
    os.makedirs(OUT, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objects:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    path = os.path.join(OUT, name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True, global_scale=1.0,
        axis_forward="-Z", axis_up="Y", bake_space_transform=False,
        object_types={"MESH"}, use_mesh_modifiers=True, mesh_smooth_type="FACE",
        add_leaf_bones=False, path_mode="COPY")
    print("YAZILDI", path)


# -------------------------------------------------------------------------------------- modeller

def ingot():
    """
    Tasinan/satilan kulce. Kodda bunun yerini 1.05 x 0.32 x 0.62'lik duz bir kup tutuyordu —
    oyuncunun sirtindaki, tezgahtaki ve yigindaki her sey oydu, yani oyunun butun dongusunun
    konusu olan nesne bir kutuydu. Kulce, yani ust yuzu alt yuzden dar bir kesik piramit.
    """
    w, h, d = 1.05, 0.32, 0.62
    tw, td = w * 0.78, d * 0.68
    z0, z1 = -h / 2, h / 2
    v = [(-w / 2, -d / 2, z0), (w / 2, -d / 2, z0), (w / 2, d / 2, z0), (-w / 2, d / 2, z0),
         (-tw / 2, -td / 2, z1), (tw / 2, -td / 2, z1), (tw / 2, td / 2, z1), (-tw / 2, td / 2, z1)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    ob = bevel(mesh_from("SM_Market_Ingot", v, f), 0.028, segments=2, angle=25.0)
    return [ob]


def cash():
    """
    Yerdeki para destesi: uc kat banknot ve uzerlerinden gecen iki kagit bant. Kodda 0.75 x 0.18
    x 0.45'lik yesil bir kutuydu ve yerde on tane yan yana durunca yesil kutu yigini oluyordu —
    katlar arasindaki bosluk deste oldugunu bir bakista soyleyen tek sey.
    """
    parts = []
    layers = 3
    gap = 0.012
    lay_h = (0.18 - gap * (layers - 1)) / layers
    for i in range(layers):
        y = -0.09 + lay_h / 2 + i * (lay_h + gap)
        # Her kat bir tik kaymis. Ustuste tam hizali uc dilim tek bir kutuya geri doner.
        dx = (i - 1) * 0.018
        parts.append(bevel(box("kat%d" % i, (0.75, lay_h, 0.45), (dx, 0.0, y)), 0.008, 1))
    for k, sx in enumerate((-0.20, 0.20)):
        parts.append(box("bant%d" % k, (0.10, 0.20, 0.47), (sx, 0.0, 0.0)))
    ob = join("SM_Market_CashBundle", parts)
    return [ob]


def sign_frame():
    """
    Duvar tabelasi: arka pano, cevresinde cerceve, altinda iki konsol. Yazi Unity tarafinda TMP
    olarak onune konuyor, cunku tabelanin uzerindekiler on bir dilde degisiyor.

    Orijin ARKA yuzunde: duvarin yuzeyine sifir konumlanip +Z'ye baksin diye. Kalinligin tamami
    one dogru buyuyor.
    """
    W, H = 3.2, 1.15
    t = 0.09                                            # panonun kalinligi
    r = 0.11                                            # cerceve cubugunun eni
    parts = [box("pano", (W - r, H - r, t), (0, -t / 2, 0))]
    # Cerceve panodan biraz one tasar, yoksa tek parca bir levha olur.
    for name, size, at in (
            ("cerceve_ust", (W, r, t * 1.7), (0, -t * 0.85, (H - r) / 2)),
            ("cerceve_alt", (W, r, t * 1.7), (0, -t * 0.85, -(H - r) / 2)),
            ("cerceve_sol", (r, H, t * 1.7), (-(W - r) / 2, -t * 0.85, 0)),
            ("cerceve_sag", (r, H, t * 1.7), ((W - r) / 2, -t * 0.85, 0))):
        parts.append(bevel(box(name, size, at), 0.022, 1))
    # Konsollar: duvardan tabelaya giden iki kisa dirsek, tabelanin arkasinda kaliyor.
    for k, sx in enumerate((-W * 0.30, W * 0.30)):
        parts.append(bevel(box("konsol%d" % k, (0.13, 0.13, 0.22), (sx, -0.11, -H / 2 - 0.06)), 0.02, 1))
    ob = join("SM_Market_SignFrame", parts)
    return [ob]


def shelf():
    """
    Tezgahin arkasindaki urun rafi. Stok arttikca uzerine kulce diziliyor, yani raf sadece
    dekor degil bir gosterge — Unity tarafi rafin yuzeylerini oradan olcuyor, bu yuzden uc
    tabla esit araliklarla ve on kenari acik.

    Orijin TABANINDA ve on yuzu +Z'ye bakar.
    """
    parts = []
    W, H, D = 4.2, 2.3, 0.9
    for k, sx in enumerate((-W / 2 + 0.07, W / 2 - 0.07)):
        parts.append(bevel(box("dikme%d" % k, (0.14, H, D), (sx, 0.0, H / 2)), 0.025, 1))
    for i in range(3):
        z = 0.34 + i * 0.78
        parts.append(bevel(box("tabla%d" % i, (W - 0.28, 0.09, D), (0.0, 0.0, z)), 0.02, 1))
    # Arka pano: raf duvara dayali durmali, arkasindan avlunun geri kalani gorunmemeli.
    parts.append(box("arka", (W - 0.28, H - 0.1, 0.05), (0.0, D / 2 - 0.03, H / 2)))
    ob = join("SM_Market_Shelf", parts)
    return [ob]


BUILDS = [
    ("SM_Market_Ingot", ingot),
    ("SM_Market_CashBundle", cash),
    ("SM_Market_SignFrame", sign_frame),
    ("SM_Market_Shelf", shelf),
]


def main():
    for name, maker in BUILDS:
        clear()
        export(name, maker())


if __name__ == "__main__":
    if bpy is None:
        sys.exit("Blender'in python'unda kosmali: Blender -b --python market_models.py")
    main()
