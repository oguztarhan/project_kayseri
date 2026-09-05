"""Swap the blockout's placeholder geometry for real downloaded models.

blockout.py owns the island - terraces, cliffs, the route, the anchor contract.
This module owns what stands ON it. Nothing here moves an anchor.

Assets come from Sketchfab (the user's own account, CC-Attribution) and live in
an ASSETS collection, hidden from render; everything placed in the scene is a
linked duplicate sharing that mesh data, so several hundred trees cost one mesh.

Run after blockout.build():

    import blockout, assets
    blockout.build()
    assets.apply()

ATTRIBUTION (CC-BY, must ship in the game credits):
    Stylized_Industrial_Buildings_Pack - Teen-Wolf
    Stylized Pine Tree                 - Batuhan13
    Container Ship                     - lavawave
"""

import bpy
import math
import random
import mathutils

import blockout as B


ASSETS = "ASSETS"

# Which pack pieces stand in for which building. The pack is a modular kit of
# 23 industrial blocks; a station is two or three of them grouped, which is how
# the reference's complexes are massed.
STATION_KIT = {
    "Cannon":     ("factory2.4__0", "factory1.7__0", "factory3.2__0"),
    "Hull":       ("factory1.3__0", "factory2.6__0"),
    "Rigging":    ("factory3.5__0", "factory1.9__0"),
    "Navigation": ("factory2.2__0", "factory3.1__0"),
    "Figurehead": ("factory1.5__0", "factory2.8__0"),
}
MINE_KIT = ("factory3.4__0", "factory1.2__0")
PLANT_KIT = ("factory2.7__0", "factory3.6__0", "factory1.4__0")

TREE = "tree_low.001_StylizedTree_0"
SHIP = "Object_2"

# Per-building models, searched individually rather than taken from the pack.
# These are multi-object rigs placed with place_group().
#   root object name          what it is                    author (CC-BY)
GROUPS = {
    "crane":     "Sketchfab_model.004",  # Port crane "Sokol"   - Domender
    "tunnel":    "Sketchfab_model.005",  # Mine Shaft Kit       - dumokanart
    "plant":     "Sketchfab_model.006",  # Factory w/ towers    - assetfactory
    # Sketchfab_model.007 was the Red Alert ore refinery: it has an off-centre
    # pivot and read as a yellow spider on the terrace. Replaced.
    "refinery":  "Sketchfab_model.008",  # Stylized chem plant  - INGSOC1984
    "shop":      "Sketchfab_model.009",  # Isometric shop       - daviddickball
    "dumper":    "Sketchfab_model.010",  # Mine dump truck      - Arahan
    "wagons":    "Sketchfab_model.011",  # Train wagons pack    - gamico
    "warehouse": "Sketchfab_model.012",  # Warehouse Building   - rajibkc
}


# ------------------------------------------------------------------ util ----

def src(name):
    """A source mesh from the asset library, or None if it was not imported."""
    ob = bpy.data.objects.get(name)
    if ob is None or ob.type != 'MESH':
        return None
    return ob


_extent = {}


def raw_extent(me):
    """Largest dimension of a mesh's own vertex data, ignoring every transform
    on it or its parents. Cached - it is asked once per placed instance."""
    if me.name in _extent:
        return _extent[me.name]
    if not me.vertices:
        _extent[me.name] = 0.0
        return 0.0
    xs = [v.co.x for v in me.vertices]
    ys = [v.co.y for v in me.vertices]
    zs = [v.co.z for v in me.vertices]
    e = max(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    _extent[me.name] = e
    return e


def library():
    col = bpy.data.collections.get(ASSETS)
    if col is None:
        col = bpy.data.collections.new(ASSETS)
        bpy.context.scene.collection.children.link(col)
    return col


def gather():
    """Move any freshly imported Sketchfab hierarchy into ASSETS.

    A download lands in the scene collection; unless it is filed here,
    blockout.wipe() deletes it on the next rebuild and it has to be
    re-downloaded.
    """
    col = library()
    held = {o.name for o in col.all_objects}
    moved = 0
    for ob in list(bpy.data.objects):
        if ob.parent is not None or ob.name in held:
            continue
        if not (ob.name.startswith("Sketchfab_model") or ob.name == "root"):
            continue
        for o in [ob] + list(ob.children_recursive):
            for c in list(o.users_collection):
                c.objects.unlink(o)
            col.objects.link(o)
            moved += 1
    if moved:
        print("  gathered %d newly imported objects into ASSETS" % moved)


def stow():
    """Hide the whole asset library. It is source data only - every asset in
    the scene is a linked duplicate placed by place().

    The hide goes on the COLLECTION as well as the objects: per-object
    hide_render alone did not stop the library rendering, and 170 stray source
    models showed up floating in the sea.
    """
    col = library()
    col.hide_render = True
    col.hide_viewport = True
    for ob in col.all_objects:
        ob.hide_render = True
        ob.hide_viewport = True

    vl = bpy.context.view_layer.layer_collection.children.get(ASSETS)
    if vl:
        vl.hide_viewport = True


def place(source, name, coll, s, t, z, size=None, yaw=0.0, tint=None,
          ox=0.0, oy=0.0):
    """Linked duplicate of `source`, dropped on the island at (s, t, z).

    Sits the model's own base on z rather than its origin - the packs have
    origins all over the place, and an unbased model floats or sinks.
    """
    if source is None:
        return None
    ob = bpy.data.objects.new(name, source.data)
    coll.objects.link(ob)

    # Inherit the source's WORLD rotation. Sketchfab imports carry the
    # Y-up -> Z-up conversion on the Sketchfab_model root, and a duplicate
    # built from raw mesh data drops it - every model lands on its side.
    base = source.matrix_world.to_quaternion()
    ob.rotation_mode = 'QUATERNION'
    ob.rotation_quaternion = (mathutils.Quaternion((0.0, 0.0, 1.0), yaw)
                              @ base)

    if size:
        # Measure the RAW mesh, which is what this duplicate carries. Neither
        # source.dimensions nor source.scale is usable here: the imported
        # meshes are children of a scaled Sketchfab root, so the import factor
        # lives on the parent and both of those read straight past it.
        k = (size / raw_extent(source.data)) if raw_extent(source.data) else 1.0
        ob.scale = (k, k, k)
    else:
        ob.scale = source.matrix_world.to_scale()

    bpy.context.view_layer.update()
    bb = [ob.matrix_world @ mathutils.Vector(v) for v in ob.bound_box]
    cx = sum(v.x for v in bb) / 8.0
    cy = sum(v.y for v in bb) / 8.0
    zmin = min(v.z for v in bb)

    x, y = B.W(s, t)
    ob.location = (x + ox - cx, y + oy - cy, z - zmin)
    ob.hide_render = False
    ob.hide_viewport = False
    if tint:
        paint(ob, tint)
    return ob


def place_group(root_name, name, coll, s, t, z, size=None, yaw=0.0,
                tint=None, ox=0.0, oy=0.0):
    """Copy a whole imported hierarchy onto the island.

    The per-building models are multi-object rigs (a crane is ~110 parts), so
    place() - which duplicates one mesh - cannot carry them. Mesh data stays
    linked; only the transforms are new.
    """
    root = bpy.data.objects.get(root_name)
    if root is None:
        return None

    made = []

    def rec(o, parent):
        c = o.copy()
        coll.objects.link(c)
        c.hide_render = False
        c.hide_viewport = False
        if parent is not None:
            c.parent = parent
            c.matrix_parent_inverse = o.matrix_parent_inverse.copy()
        made.append(c)
        for ch in o.children:
            rec(ch, c)
        return c

    top = rec(root, None)
    top.name = name
    bpy.context.view_layer.update()

    def world_bounds():
        pts = []
        for o in made:
            if o.type != 'MESH':
                continue
            pts += [o.matrix_world @ mathutils.Vector(v) for v in o.bound_box]
        return pts

    pts = world_bounds()
    if not pts:
        return top

    if size:
        ext = max(max(p[i] for p in pts) - min(p[i] for p in pts)
                  for i in range(3))
        if ext:
            k = size / ext
            top.scale = tuple(v * k for v in top.scale)

    top.rotation_mode = 'QUATERNION'
    top.rotation_quaternion = (mathutils.Quaternion((0.0, 0.0, 1.0), yaw)
                               @ top.rotation_quaternion)
    bpy.context.view_layer.update()

    pts = world_bounds()
    cx = (max(p.x for p in pts) + min(p.x for p in pts)) * 0.5
    cy = (max(p.y for p in pts) + min(p.y for p in pts)) * 0.5
    zmin = min(p.z for p in pts)
    x, y = B.W(s, t)
    top.location = (top.location.x + (x + ox - cx),
                    top.location.y + (y + oy - cy),
                    top.location.z + (z - zmin))

    if tint:
        for o in made:
            if o.type == 'MESH':
                paint(o, tint)
    return top


_tinted = {}


def paint(ob, key):
    """Recolour a downloaded model to the reference palette.

    The packs ship neutral grey; the reference is saturated blue / yellow /
    near-black per building, and that colour coding is most of how a player
    tells the stations apart.
    """
    sig = (ob.data.name, key)
    if sig in _tinted:
        ob.data = _tinted[sig]
        return
    me = ob.data.copy()
    ob.data = me
    r, g, b = B.PALETTE[key]
    me.materials.clear()
    me.materials.append(B.mat(key))
    _tinted[sig] = me


def clear(*collection_names):
    """Hide the placeholder geometry a real asset now replaces."""
    for cn in collection_names:
        c = bpy.data.collections.get(cn)
        if not c:
            continue
        for ob in c.objects:
            ob.hide_render = True
            ob.hide_viewport = True


# ----------------------------------------------------------------- build ----

def real_trees(root):
    """Replace the cone trees with the real modelled pine, instanced."""
    tree = src(TREE)
    if tree is None:
        return None
    c = B.coll("Trees_Real", root)
    rng = random.Random(19)

    old = bpy.data.collections.get("Vegetation")
    if old:
        for ob in list(old.objects):
            if "Trees" in ob.name:
                ob.hide_render = True
                ob.hide_viewport = True

    n = 0
    for key, s, z, hs, ht, t, _sides in B.BANDS:
        per = 15 if key not in ("Mine", "Dock") else 11
        ring = (B.rim_cluster(s, t, hs, ht, rng, per, 1, z + 2.0)
                + B.rim_cluster(s, t, hs, ht, rng, per, -1, z + 2.0))
        for ps, pt, pz, sc in ring:
            place(tree, "Pine_%s_%03d" % (key, n), c, ps, pt, pz,
                  size=rng.uniform(20.0, 30.0) * sc,
                  yaw=rng.uniform(0.0, 6.28))
            n += 1

    for i, (s, t, hs, ht, _sides) in enumerate(B.LOBES):
        z = B.zat(s)
        side = 1 if t > 0 else -1
        for ps, pt, pz, sc in B.rim_cluster(s, t, hs, ht, rng, 7, side,
                                            z + 2.0, spread=0.7):
            place(tree, "Pine_Lobe_%03d" % n, c, ps, pt, pz,
                  size=rng.uniform(20.0, 28.0) * sc,
                  yaw=rng.uniform(0.0, 6.28))
            n += 1
    print("  real trees: %d" % n)
    return c


def real_buildings(root):
    """Real industrial blocks on the stations, the mine and the coal plant."""
    c = B.coll("Buildings_Real", root)

    # Sizes are the model's LARGEST dimension. A terrace is ~66 deep, so a
    # complex has to stay near 40 or it overhangs the cliff.
    # mine
    zm = B.zat(50) + 2.0
    for i, nm in enumerate(MINE_KIT):
        place(src(nm), "Mine_Block_%02d" % (i + 1), c, 30 + i * 20,
              -26 + i * 20, zm, size=38 - i * 8, yaw=math.radians(20 * i),
              tint="mine" if i == 0 else "concrete")

    # coal plant - near-black, as the reference paints it
    zp = B.zat(117) + 2.0
    for i, nm in enumerate(PLANT_KIT):
        place(src(nm), "Plant_Block_%02d" % (i + 1), c, 117 + (i - 1) * 18,
              8 + (i - 1) * 22, zp, size=42 - i * 7,
              yaw=math.radians(15 * i), tint="plant" if i < 2 else "storage")

    # the five stations
    for key, t, ckey in B.STATIONS:
        s, z, bt = B.band(key)
        tt = t + bt
        kit = STATION_KIT.get(key, ())
        for i, nm in enumerate(kit):
            place(src(nm), "%s_Block_%02d" % (key, i + 1), c,
                  s + (i - 1) * 16, tt + (i - 1) * 22, z + 2.0,
                  size=46 - i * 9, yaw=math.radians(18 * i),
                  tint=ckey if i == 0 else ("trim" if i == 2 else
                                            B.dark(ckey)))
    print("  real buildings placed")
    return c


def real_landmarks(root):
    """The buildings searched for individually - these carry the map's
    character, so they get the right model rather than a pack stand-in."""
    c = B.coll("Landmarks_Real", root)

    # mine: tunnel mouth driven into the massif
    place_group(GROUPS["tunnel"], "Mine_Tunnel_Real", c, 16, -36,
                B.zat(50) + 2.0, size=44, yaw=math.radians(210),
                tint="rock_dark")

    # coal plant on the Works band - near-black with its smoke towers
    place_group(GROUPS["plant"], "Plant_Real", c, 119, 6,
                B.zat(117) + 2.0, size=76, yaw=math.radians(35), tint="plant")

    # refinery on the Hull band
    s, z, bt = B.band("Hull")
    place_group(GROUPS["refinery"], "Refinery_Real", c, s, bt, z + 2.0,
                size=64, yaw=math.radians(-20), tint="mine")

    # warehouse on the Rigging band
    s, z, bt = B.band("Rigging")
    place_group(GROUPS["warehouse"], "Warehouse_Real", c, s, bt, z + 2.0,
                size=58, yaw=math.radians(30), tint="storage")

    # dump truck up at the mine, as the reference has it
    place_group(GROUPS["dumper"], "Mine_Dumper_Real", c, 22, 24,
                B.zat(50) + 2.0, size=26, yaw=math.radians(140), tint="mine")

    # coal train running down from the mine to the plant
    place_group(GROUPS["wagons"], "Train_Real", c, 96, -14, B.zat(96) + 2.0,
                size=78, yaw=math.radians(-42))

    # the port crane. Sits on the seaward edge of the quay (z=7, the quay top)
    # with the jib reaching over the water, not back-to-front on the sand.
    sd, zd, bdt = B.band("Dock")
    place_group(GROUPS["crane"], "Dock_Crane_Real", c, sd + 46, bdt - 74, 7.0,
                size=78, yaw=math.radians(215), tint="crane")

    # the three customer shops
    for i, (tt, ck) in enumerate(((-74, "shop_a"), (0, "shop_b"),
                                  (74, "shop_c"))):
        place_group(GROUPS["shop"], "Cust_%02d_Shop_Real" % (i + 1), c,
                    sd + 132, tt, 8.0, size=40, yaw=math.radians(30 + i * 40),
                    tint=ck)
    return c


def real_ship(root):
    ship = src(SHIP)
    if ship is None:
        return None
    c = B.coll("Ships_Real", root)
    s, z, bt = B.band("Dock")
    place(ship, "Cargo_Ship_Real", c, s + 40, bt - 124, 1.0, size=118,
          yaw=math.radians(4), tint="ship_hull")
    place(ship, "Player_Ship_Real", c, s + 86, bt - 96, 1.0, size=48,
          yaw=math.radians(-14), tint="ship")
    print("  real ships placed")
    return c


def apply():
    """Swap placeholders for real geometry. Call after blockout.build()."""
    gather()
    stow()
    root = bpy.data.collections.get("SHIPYARD")
    if root is None:
        raise RuntimeError("run blockout.build() first")

    missing = [n for n in (TREE, SHIP) if src(n) is None]
    if missing:
        print("  MISSING assets, keeping placeholders for: %s" % missing)

    real_trees(root)
    real_buildings(root)
    real_landmarks(root)
    real_ship(root)

    # retire the primitives the real models now stand in for
    clear("Source", "Station_Cannon_Built")
    for ob in bpy.data.collections["Dock"].objects:
        if ob.name.startswith(("Cargo_Ship", "Player_Ship", "Dock_Crane")):
            ob.hide_render = True
            ob.hide_viewport = True
    # real landmarks now stand where these pack blocks and primitives were
    for ob in bpy.data.collections["Buildings_Real"].objects:
        if ob.name.startswith(("Hull_Block", "Plant_Block", "Rigging_Block")):
            ob.hide_render = True
            ob.hide_viewport = True
    for cn in ("Customers", "Logistics"):
        col = bpy.data.collections.get(cn)
        if not col:
            continue
        for ob in col.objects:
            if ob.name.startswith(("Cust_01_", "Cust_02_", "Cust_03_",
                                   "Truck_")) and "Trees" not in ob.name:
                ob.hide_render = True
                ob.hide_viewport = True

    n = sum(len(c.objects) for c in bpy.data.collections)
    print("assets applied: %d objects in scene" % n)
    return root
