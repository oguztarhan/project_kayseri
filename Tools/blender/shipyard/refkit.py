"""Place the approved IndustrialReference art on the portrait Focus Ladder.

The friend's map (Tools/blender/IndustrialReference) is the art bible: its
buildings, palette and prop kit are what the game looks like. Its LAYOUT is a
single-screen isometric island, which is not what the plan needs - the plan
needs one 520-unit vertical ladder the player drags through.

So this module takes his buildings apart into a kit and re-lays them out on
blockout.py's ladder. His scene, his prefabs and his Unity import are never
touched; this reads map_geometry.json, the same file his Unity importer reads.

    blender --background --python refkit.py

Coordinates: map_geometry.json is Unity (Y up, left handed). Blender is Z up,
right handed, so vertices convert (x, y, z) -> (x, z, y) and every triangle
has to be reversed - that swap mirrors handedness and would otherwise turn
every mesh inside out.

The kit is loaded into blockout's ASSET_COLLECTION so blockout.wipe() spares
it and assets.place_group() can place it unchanged.
"""

import bpy
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:          # blender does not add the script's own dir
    sys.path.insert(0, HERE)

import blockout as B
import assets as A

GEOMETRY = os.path.join(HERE, "..", "IndustrialReference", "map_geometry.json")
OUT = os.path.join(HERE, "..", "..", "..", "design", "shipyard-refkit")

# One source group per KIND. The map ships 120 pine trees and 80 stones as
# separate groups; the kit needs one of each, place() instances the rest.
KIT = {
    "mountain":  "04_Parts/Mine_faceted_mountain",
    "mountain2": "04_Parts/Mine_faceted_mountain_001",
    "portal":    "04_Mine/Mine_Portal",
    "crusher":   "04_Mine/Crusher_Tower",
    "railway":   "04_Mine/Railway",
    "smelter":   "05_Smelter/Smelting_Plant",
    "factory":   "06_Factory/Blue_Factory",
    "refinery":  "07_Refinery/Refinery",
    "tank":      "07_Refinery/Horizontal_Tank",
    "warehouse": "08_Harbor/Warehouse",
    "port":      "08_Harbor/Port",
    "crane":     "08_Harbor/Crane",
    "ship":      "08_Harbor/Ship",
    "cust1":     "09_Customers/Customer_Island_01",
    "cust2":     "09_Customers/Customer_Island_02",
    "cust3":     "09_Customers/Customer_Island_03",
    "truck":     "04_Vehicles/Truck_chassis",
    "wagon":     "04_Vehicles/Ore_wagon_chassis",
    "crate":     "06_Crates_Wooden_freight_crate",
    "barrel":    "06_Barrels_Storage_barrel",
    "container": "08_Containers_Intermodal_container",
    "tree":      "10_Trees_Pine_trunk",
    "upgrade":   "12_Interface/Upgrade",
    "badge":     "12_Interface/Customer_Badges",
}

PREFIX = "KIT_"


# ------------------------------------------------------------------ load ----

def _material(entry):
    name = "ref_" + entry["name"]
    m = bpy.data.materials.get(name)
    if m:
        return m
    r, g, b = entry["color"][:3]
    m = bpy.data.materials.new(name)
    m.diffuse_color = (r, g, b, 1.0)
    m.use_nodes = True
    n = m.node_tree.nodes.get("Principled BSDF")
    if n:
        n.inputs["Base Color"].default_value = (r, g, b, 1.0)
        n.inputs["Metallic"].default_value = entry["metallic"]
        n.inputs["Roughness"].default_value = entry["roughness"]
        strength = entry.get("strength", 0.0)
        if strength and "Emission Color" in n.inputs:
            er, eg, eb = entry["emission"][:3]
            n.inputs["Emission Color"].default_value = (er, eg, eb, 1.0)
            n.inputs["Emission Strength"].default_value = strength
    return m


def _mesh(entry, mats):
    """One Blender mesh from a manifest entry, converted out of Unity space."""
    v = entry["vertices"]
    # (x, y, z) Unity -> (x, z, y) Blender.
    verts = [(v[i], v[i + 2], v[i + 1]) for i in range(0, len(v), 3)]

    faces, per_face, slots, index = [], [], [], {}
    for sub in entry["submeshes"]:
        mat = mats[sub["material"]]
        if mat.name not in index:
            index[mat.name] = len(slots)
            slots.append(mat)
        mi = index[mat.name]
        tris = sub["triangles"]
        for i in range(0, len(tris), 3):
            # Reversed winding: the axis swap above mirrors handedness, so the
            # original order would leave every face pointing inwards.
            faces.append((tris[i + 2], tris[i + 1], tris[i]))
            per_face.append(mi)

    me = bpy.data.meshes.new(entry["name"])
    me.from_pydata(verts, [], faces)
    me.update()
    for m in slots:
        me.materials.append(m)
    if len(slots) > 1:
        for poly, mi in zip(me.polygons, per_face):
            poly.material_index = mi
    return me


def load():
    """Build the kit into blockout's spared ASSETS collection."""
    if bpy.data.objects.get(PREFIX + "factory"):
        print("refkit: already loaded")
        return

    with open(os.path.abspath(GEOMETRY)) as fh:
        man = json.load(fh)

    mats = {e["name"]: _material(e) for e in man["materials"]}
    groups = {g["name"]: g for g in man["groups"]}
    lib = A.library()
    cache = {}

    built = 0
    for key, gname in KIT.items():
        g = groups.get(gname)
        if g is None:
            print("refkit: MISSING group %s" % gname)
            continue

        root = bpy.data.objects.new(PREFIX + key, None)
        root.empty_display_size = 0.4
        lib.objects.link(root)
        gx, gy, gz = g["position"]
        root.location = (gx, gz, gy)

        for part in g["parts"]:
            mi = part["mesh"]
            me = cache.get(mi)
            if me is None:
                me = cache[mi] = _mesh(man["meshes"][mi], mats)
            ob = bpy.data.objects.new(part["name"], me)
            px, py, pz = part["position"]
            ob.location = (px, pz, py)
            ob.parent = root
            lib.objects.link(ob)
            built += 1

    bpy.context.view_layer.update()
    A.stow()
    print("refkit: %d kit groups, %d parts" % (len(KIT), built))


def K(key):
    return PREFIX + key


# ----------------------------------------------------------------- place ----

def _z(s):
    return B.zat(s)


def put(key, name, coll, s, t, size, yaw=0.0, tint=None, dz=0.0):
    return A.place_group(K(key), name, coll, s, t, _z(s) + dz,
                         size=size, yaw=yaw, tint=tint)


# Which of his buildings becomes which station. Five distinct silhouettes,
# in his own colours - no recolouring except where a shape is reused.
# station        kit        size  yaw
STATION_KIT = {
    # Cannon is the tutorial focus and was reading smaller than Hull's factory.
    "Cannon":     ("smelter",   92, 0.0,  None),
    "Hull":       ("factory",   86, 0.0,  None),
    "Rigging":    ("warehouse", 72, 0.0,  None),
    # The refinery also stands on the Works band; teal separates the two.
    "Navigation": ("refinery",  74, 0.0,  "navigation"),
    # The pier read as a flat grey slab - no silhouette to close the ladder on.
    # The crusher tower also stands at the mine, 400 units up; purple separates.
    "Figurehead": ("crusher",   76, 0.0,  "figurehead"),
}


def build_source(root):
    c = B.coll("Ref_Source", root)
    s, _, t = 50, 0, 0
    put("mountain",  "Ref_Mountain_A", c,  22, -46, 150)
    put("mountain2", "Ref_Mountain_B", c,  26,  44, 130)
    put("portal",    "Ref_Mine_Portal", c, 52, -18, 62)
    put("crusher",   "Ref_Crusher", c,     56,  36, 66)
    put("railway",   "Ref_Railway", c,     78,   4, 96)
    put("wagon",     "Ref_Wagon_A", c,     92, -14, 26)
    put("truck",     "Ref_Truck_A", c,     70,  46, 24)
    return c


def build_works(root):
    c = B.coll("Ref_Works", root)
    put("refinery",  "Ref_Refinery", c,  118,  44, 74)
    put("tank",      "Ref_Tank_A", c,    138,  20, 30)
    put("tank",      "Ref_Tank_B", c,    144,  40, 30)
    for i, (s, t) in enumerate(((104, -52), (118, -44), (106, -30),
                                (128, -56), (120, -22))):
        put("container", "Ref_Yard_%d" % i, c, s, t, 24)
    put("truck",     "Ref_Truck_B", c,   136, -12, 24)
    return c


def build_stations(root):
    made = {}
    for key, t, _ in B.STATIONS:
        kit, size, yaw, tint = STATION_KIT[key]
        c = B.coll("Ref_Station_%s" % key, root)
        s = B.BY_KEY[key][1]
        put(kit, "Ref_%s" % key, c, s, t, size, yaw=yaw, tint=tint)
        put("crate",  "Ref_%s_In" % key, c, s - 26, t - 40, 20)
        put("crate",  "Ref_%s_Out" % key, c, s + 26, t + 40, 20)
        put("barrel", "Ref_%s_Barrel" % key, c, s - 20, t + 44, 16)
        made[key] = c
    return made


def build_dock(root):
    c = B.coll("Ref_Dock", root)
    put("crane", "Ref_Crane", c,  508, -46, 92)
    put("port",  "Ref_Pier", c,   520,  30, 78)
    put("ship",  "Ref_Ship", c,   556,  -8, 120, dz=-10)
    put("cust1", "Ref_Cust_01", c, 588, -74, 64, dz=-11)
    put("cust2", "Ref_Cust_02", c, 604,   6, 60, dz=-11)
    put("cust3", "Ref_Cust_03", c, 588,  76, 64, dz=-11)
    for i, (s, t) in enumerate(((504, 8), (512, 20), (498, 22))):
        put("container", "Ref_Dock_Cont_%d" % i, c, s, t, 22)
    return c


def apply():
    root = bpy.data.collections.get("SHIPYARD")
    load()

    # His art replaces the blockout's placeholder buildings; the blockout keeps
    # owning terrain, route, anchors and cameras.
    A.clear("Source", "Dock", "Trees", "Rocks")
    for key, _, _ in B.STATIONS:
        A.clear("Station_%s_Built" % key)

    build_source(root)
    build_works(root)
    stations = build_stations(root)
    build_dock(root)
    return stations


# ---------------------------------------------------------------- render ----

def locked(on=True):
    """Start state: Cannon standing in his art, the other four still pads."""
    for key, _, _ in B.STATIONS:
        built = (key == "Cannon") or not on
        for cname, want in (("Ref_Station_%s" % key, built),
                            ("Station_%s_Pad" % key, not built)):
            cl = bpy.data.collections.get(cname)
            if not cl:
                continue
            # list(): all_objects is a live property, and changing hide_* while
            # iterating it invalidates the iterator - only the first couple of
            # objects ever got hidden, so every station rendered as built.
            for ob in list(cl.all_objects):
                ob.hide_viewport = not want
                ob.hide_render = not want


def shot(camera, path, ortho=None):
    sc = bpy.context.scene
    sc.camera = bpy.data.objects[camera]
    if ortho:
        sc.camera.data.ortho_scale = ortho
    sc.render.filepath = path
    sc.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)
    print("wrote %s" % path)


def check():
    """The lock is the whole point of the start state - assert it actually
    hides. It silently did not: see the list() note in locked()."""
    locked(True)
    for key, _, _ in B.STATIONS:
        cl = bpy.data.collections.get("Ref_Station_%s" % key)
        lit = sum(1 for o in cl.all_objects if not o.hide_render)
        if key == "Cannon":
            assert lit == len(cl.all_objects), "Cannon must be fully built"
        else:
            assert lit == 0, "%s still renders %d objects when locked" % (key, lit)
    print("check: lock state ok")


if __name__ == "__main__":
    B.build()
    apply()
    check()
    out = os.path.abspath(OUT)
    os.makedirs(out, exist_ok=True)

    locked(True)
    shot("Cam_Overview", os.path.join(out, "01-locked-start.png"))
    shot("Cam_Play_Cannon", os.path.join(out, "03-play-locked.png"))
    # Cam_Play_Cannon is ortho VIEW_H (520), which is the WHOLE ladder - the
    # player would see all eight bands at once and have nothing to drag through.
    # 330 is the plan's framing: active station plus part of the neighbours.
    shot("Cam_Play_Cannon", os.path.join(out, "04-play-framed.png"), ortho=330)
    locked(False)
    shot("Cam_Overview", os.path.join(out, "02-built-end.png"))
