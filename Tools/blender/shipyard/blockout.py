"""Portrait shipyard - Milestone 1 greybox blockout.

Coloured blocks only. Its job is to prove the vertical ladder fits and to
freeze the anchor contract; final art replaces the collections at Milestone 6
without moving a single anchor.

SCREEN FRAME
    s  down-screen along the ladder (0 = mine, 1120 = dock)
    t  across, +t is screen right
    z  world up

`W(s, t)` maps that onto the isomap camera (rot 42/0/45 ortho, the same one
shot.py builds), which projects as

    screen_x = 0.7071 (x + y)
    screen_y = 0.5254 (y - x) + 0.669 z

so one unit of s or t is exactly one unit on screen. Buildings stay world-axis
aligned - that is what gives them the two-visible-faces iso read - while the
island itself is placed in (s, t) and so runs straight up the portrait frame.

Run headless:
    blender --background --python blockout.py
or from the Blender MCP:
    exec(compile(open(P).read(), P, "exec"), {"__name__": "__main__"})
"""

import bpy
import math

# ---------------------------------------------------------------- frame ----

K = 0.7071067811865476
S_STRETCH = 1.3458980337503153     # 1 unit of s == 1 unit down-screen


def W(s, t):
    """Screen-frame (s, t) -> world (x, y)."""
    a = s * S_STRETCH
    return ((t + a) * K, (t - a) * K)


BAND = 140.0                       # one rung of the ladder, in screen units
VIEW_W = 200.0                     # play camera sees this much across
ASPECT = 2340.0 / 1080.0
VIEW_H = VIEW_W * ASPECT           # 433 -> a little over three bands

# The lateral offset and the uneven radii are what stop the ladder reading as
# a stack of identical coins - the bands have to interlock into one landmass.
# key            s      z   half_s half_t    t   sides
BANDS = [
    ("Mine",       70,  62,   96,  80,   -8,  11),
    ("Works",     210,  48,   90,  86,   10,  13),
    ("Cannon",    350,  36,   94,  80,  -10,  12),
    ("Hull",      490,  26,   88,  84,   12,  11),
    ("Rigging",   630,  18,   92,  78,   -9,  13),
    ("Navigation",770,  11,   88,  84,   10,  12),
    ("Figurehead",910,   6,   90,  76,   -8,  11),
    ("Dock",     1050,   2,  100,  90,    4,  13),
]
BY_KEY = {b[0]: b for b in BANDS}

# Filler lobes between the bands: they weld the terraces together and give the
# coastline bays instead of a scalloped edge of circles.
LOBES = [
    (140,  38, 55,  44, 40, 9), (140, -44, 55,  36, 32, 8),
    (280, -46, 42,  42, 36, 9), (280,  40, 42,  34, 30, 8),
    (420,  44, 31,  40, 36, 9), (420, -40, 31,  32, 30, 8),
    (560, -48, 22,  42, 34, 9), (560,  38, 22,  34, 32, 8),
    (700,  44, 15,  40, 34, 9), (700, -38, 15,  32, 30, 8),
    (840, -46,  9,  42, 34, 9), (840,  40,  9,  34, 30, 8),
    (980,  42,  4,  44, 36, 9), (980, -40,  4,  36, 32, 8),
]

# key           t     colour key
STATIONS = [
    ("Cannon",    -38, "cannon"),
    ("Hull",       40, "hull"),
    ("Rigging",   -40, "rigging"),
    ("Navigation", 40, "navigation"),
    ("Figurehead",-38, "figurehead"),
]

# -------------------------------------------------------------- palette ----

PALETTE = {
    "sea":        (0.04, 0.17, 0.36),
    "rock":       (0.30, 0.30, 0.34),
    "rock_dark":  (0.21, 0.21, 0.25),
    "grass":      (0.24, 0.47, 0.17),
    "grass_dry":  (0.45, 0.48, 0.22),
    "sand":       (0.68, 0.58, 0.37),
    "snow":       (0.86, 0.89, 0.93),
    "road":       (0.55, 0.53, 0.48),
    "rail":       (0.26, 0.21, 0.17),
    "flow":       (0.97, 0.76, 0.10),
    "mine":       (0.83, 0.58, 0.09),
    "storage":    (0.52, 0.55, 0.58),
    "refinery":   (0.27, 0.31, 0.38),
    "concrete":   (0.58, 0.58, 0.60),
    "pad":        (0.38, 0.36, 0.33),
    "pad_mark":   (0.62, 0.60, 0.30),
    "crane":      (0.91, 0.70, 0.09),
    "ship":       (0.72, 0.19, 0.16),
    "ship_deck":  (0.80, 0.78, 0.74),
    "cannon":     (0.76, 0.21, 0.13),
    "hull":       (0.15, 0.35, 0.62),
    "rigging":    (0.16, 0.53, 0.33),
    "navigation": (0.10, 0.53, 0.60),
    "figurehead": (0.46, 0.23, 0.60),
    "trim":       (0.90, 0.88, 0.83),
    "glass":      (0.35, 0.62, 0.72),
}

_mats = {}


def mat(key):
    if key in _mats:
        return _mats[key]
    r, g, b = PALETTE[key]
    m = bpy.data.materials.new("sy_" + key)
    m.diffuse_color = (r, g, b, 1.0)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (r, g, b, 1.0)
        if "Roughness" in bsdf.inputs:
            bsdf.inputs["Roughness"].default_value = 0.75
    _mats[key] = m
    return m


# ------------------------------------------------------------ primitives ----

def _obj(name, verts, faces, key, coll):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    me.materials.append(mat(key))
    coll.objects.link(ob)
    return ob


def box(name, coll, key, s, t, z, wx, wy, wz):
    """Axis-aligned box, centred on (s, t) at height z, sized in world units."""
    cx, cy = W(s, t)
    hx, hy, hz = wx * 0.5, wy * 0.5, wz * 0.5
    v = [(cx - hx, cy - hy, z), (cx + hx, cy - hy, z),
         (cx + hx, cy + hy, z), (cx - hx, cy + hy, z),
         (cx - hx, cy - hy, z + wz), (cx + hx, cy - hy, z + wz),
         (cx + hx, cy + hy, z + wz), (cx - hx, cy + hy, z + wz)]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
         (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return _obj(name, v, f, key, coll)


def _ngon(n, r_bot, r_top, h):
    v, f = [], []
    for r, zz in ((r_bot, 0.0), (r_top, h)):
        for i in range(n):
            a = 2.0 * math.pi * i / n
            v.append((r * math.cos(a), r * math.sin(a), zz))
    for i in range(n):
        j = (i + 1) % n
        f.append((i, j, n + j, n + i))
    f.append(tuple(range(n - 1, -1, -1)))
    f.append(tuple(range(n, 2 * n)))
    return v, f


def drum(name, coll, key, s, t, z, r_bot, r_top, h, n=12):
    """Cone / cylinder standing on z. Radii in world units."""
    cx, cy = W(s, t)
    v, f = _ngon(n, r_bot, r_top, h)
    v = [(x + cx, y + cy, zz + z) for x, y, zz in v]
    return _obj(name, v, f, key, coll)


def terrace(name, coll, key, s, t, z, half_s, half_t, taper=0.78, n=14):
    """Plateau: an ellipse elongated down the ladder, cliffs sloping inward.

    Authored in screen units, so half_s/half_t are what you read off the
    portrait frame, not world extents.
    """
    depth = z + 34.0
    v, f = _ngon(n, taper, 1.0, depth)
    out = []
    for x, y, zz in v:
        out.append(W(s + x * half_s, t + y * half_t) + (zz + z - depth,))
    return _obj(name, out, f, key, coll)


def ribbon(name, coll, key, pts, width, lift=0.6):
    """Flat strip through (s, t, z) points - roads, rail, the flow line."""
    world = [W(s, t) + (z + lift,) for s, t, z in pts]
    verts, faces = [], []
    for i, (x, y, z) in enumerate(world):
        px, py, _ = world[max(i - 1, 0)]
        nx, ny, _ = world[min(i + 1, len(world) - 1)]
        dx, dy = nx - px, ny - py
        d = math.hypot(dx, dy) or 1.0
        ox, oy = -dy / d * width * 0.5, dx / d * width * 0.5
        verts += [(x + ox, y + oy, z), (x - ox, y - oy, z)]
    for i in range(len(world) - 1):
        a = i * 2
        faces.append((a, a + 1, a + 3, a + 2))
    return _obj(name, verts, faces, key, coll)


def anchor(name, coll, s, t, z, size=6.0, kind='PLAIN_AXES'):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = kind
    e.empty_display_size = size
    x, y = W(s, t)
    e.location = (x, y, z)
    coll.objects.link(e)
    return e


# ----------------------------------------------------------- collections ----

def coll(name, parent):
    c = bpy.data.collections.new(name)
    parent.children.link(c)
    return c


def wipe():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)
    for blk in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras,
                bpy.data.lights):
        for d in list(blk):
            if d.users == 0:
                blk.remove(d)
    _mats.clear()


# ---------------------------------------------------------------- island ----

def build_terrain(root):
    c = coll("Terrain", root)
    x, y = W(560, 0)
    _obj("Sea", [(x - 1400, y - 1400, 0), (x + 1400, y - 1400, 0),
                 (x + 1400, y + 1400, 0), (x - 1400, y + 1400, 0)],
         [(0, 1, 2, 3)], "sea", c)

    for key, s, z, hs, ht in BANDS:
        terrace("Land_%s_Rock" % key, c, "rock", s, 0, z, hs, ht)
        cap = "sand" if key == "Dock" else ("grass_dry" if key == "Mine"
                                            else "grass")
        terrace("Land_%s_Cap" % key, c, cap, s, 0, z + 2.0,
                hs * 0.965, ht * 0.965, taper=0.985)

    # the massif the mine is cut into, plus two shoulders framing the top
    drum("Mountain_Peak", c, "rock", -32, -6, 58, 78, 5, 96, n=10)
    drum("Mountain_Snow", c, "snow", -32, -6, 138, 20, 3, 17, n=10)
    drum("Mountain_Shoulder_L", c, "rock_dark", 24, -66, 60, 34, 8, 52, n=8)
    drum("Mountain_Shoulder_R", c, "rock_dark", 10, 62, 60, 30, 7, 44, n=8)
    return c


SPINE = [
    (-10, -12, 64), (70, 8, 62), (150, 28, 54), (210, 10, 48),
    (280, 28, 41), (350, 32, 36), (420, 4, 30), (490, -32, 26),
    (560, -6, 21), (630, 34, 18), (700, 6, 14), (770, -34, 11),
    (840, -4, 8), (910, 32, 6), (980, 8, 4), (1052, 0, 2),
]

RAIL = [(14, -36, 63), (60, -32, 61), (120, -22, 55),
        (176, -14, 50), (216, -20, 48)]


def build_logistics(root):
    c = coll("Logistics", root)
    ribbon("Road_Spine", c, "road", SPINE, 15.0)
    ribbon("Flow_Line", c, "flow", SPINE, 4.0, lift=1.6)
    ribbon("Rail_Mine_To_Works", c, "rail", RAIL, 9.0, lift=1.0)
    return c


def build_source(root):
    """Mine + train + storage + refinery - the three bands above Cannon."""
    c = coll("Source", root)

    box("Mine_Headhouse", c, "mine", 40, -34, 64, 30, 22, 26)
    box("Mine_Portal", c, "rock_dark", 16, -40, 64, 20, 16, 18)
    drum("Mine_Silo", c, "storage", 58, -14, 64, 9, 9, 30)
    box("Mine_Conveyor", c, "concrete", 68, -26, 66, 34, 8, 5)
    drum("Mine_OrePile", c, "rock_dark", 84, -46, 64, 14, 3, 9, n=8)

    box("Train_Engine", c, "hull", 150, -20, 55, 16, 11, 11)
    for i, ss in enumerate((168, 184, 200)):
        box("Train_Wagon_%02d" % (i + 1), c, "rail", ss, -17, 55, 13, 10, 8)

    for i, (ss, tt) in enumerate(((188, 34), (206, 52), (224, 34))):
        drum("Storage_Silo_%02d" % (i + 1), c, "storage", ss, tt, 50,
             11, 11, 34)
    box("Storage_Shed", c, "concrete", 224, 58, 50, 30, 24, 14)

    box("Refinery_Hall", c, "refinery", 214, -30, 50, 40, 30, 22)
    drum("Refinery_Stack_L", c, "concrete", 200, -44, 72, 5, 4, 34)
    drum("Refinery_Stack_R", c, "concrete", 212, -52, 72, 5, 4, 28)
    box("Refinery_Pipes", c, "storage", 236, -20, 50, 26, 10, 8)
    return c


def build_station(root, key, t, ckey):
    """One station family: a locked pad state and a built state, siblings."""
    _, s, z, _, _ = BY_KEY[key]
    fam = coll("Station_%s" % key, root)
    pad = coll("Station_%s_Pad" % key, fam)
    built = coll("Station_%s_Built" % key, fam)

    # ---- locked: a levelled pad, a marker post, a stack of timber
    box("Pad_%s_Slab" % key, pad, "pad", s, t, z + 2.0, 54, 46, 2.5)
    box("Pad_%s_Kerb" % key, pad, "pad_mark", s, t, z + 4.5, 58, 50, 1.2)
    drum("Pad_%s_Post" % key, pad, "pad_mark", s - 20, t - 18, z + 4, 2, 2, 16)
    box("Pad_%s_Timber" % key, pad, "rail", s + 18, t + 14, z + 4.5, 16, 10, 5)

    # ---- built: hall + roof trim + the family's identifying silhouette
    box("%s_Hall" % key, built, ckey, s, t, z + 2.0, 44, 34, 24)
    box("%s_Roof" % key, built, "trim", s, t, z + 26.0, 48, 38, 3)
    box("%s_Annex" % key, built, "concrete", s + 24, t + 8, z + 2.0, 20, 18, 13)
    box("%s_InBin" % key, built, "storage", s - 26, t - 12, z + 2.0, 14, 14, 9)
    box("%s_OutRack" % key, built, "trim", s + 26, t - 16, z + 2.0, 16, 12, 7)

    if key == "Cannon":
        drum("%s_Furnace" % key, built, "concrete", s - 8, t + 22, z + 2, 9, 7, 46)
        drum("%s_Barrel" % key, built, ckey, s + 12, t - 24, z + 8, 4, 3, 22, n=8)
        box("%s_Barrel_Rack" % key, built, "rail", s + 12, t - 24, z + 2, 12, 10, 6)
    elif key == "Hull":
        box("%s_Gantry" % key, built, "crane", s, t - 26, z + 2, 8, 40, 26)
        box("%s_PlateStack" % key, built, "storage", s + 20, t - 24, z + 2, 18, 14, 6)
        box("%s_Anvil" % key, built, "rock_dark", s - 20, t + 20, z + 2, 12, 12, 9)
    elif key == "Rigging":
        drum("%s_Mast" % key, built, "trim", s - 4, t + 20, z + 2, 3, 2, 56, n=8)
        box("%s_Yard" % key, built, "trim", s - 4, t + 20, z + 50, 30, 3, 2)
        drum("%s_RopeDrum" % key, built, "rail", s + 22, t + 18, z + 2, 7, 7, 10)
    elif key == "Navigation":
        drum("%s_Dome" % key, built, "glass", s - 6, t + 20, z + 26, 12, 4, 14)
        drum("%s_Tower" % key, built, "concrete", s - 6, t + 20, z + 2, 12, 12, 24)
        drum("%s_Scope" % key, built, "trim", s + 20, t - 22, z + 12, 3, 5, 20, n=8)
    elif key == "Figurehead":
        box("%s_Plinth" % key, built, "concrete", s - 20, t + 20, z + 2, 16, 16, 12)
        drum("%s_Figure" % key, built, "trim", s - 20, t + 20, z + 14, 6, 3, 24, n=8)
        box("%s_Kiln" % key, built, ckey, s + 22, t + 20, z + 2, 16, 14, 15)
    return fam


def build_dock(root):
    c = coll("Dock", root)
    _, s, z, _, _ = BY_KEY["Dock"]

    box("Dock_Quay", c, "concrete", s + 46, 0, 0.0, 210, 40, 5)
    box("Dock_Warehouse", c, "storage", s - 14, 40, z + 2, 46, 30, 18)
    box("Outfitting_Shed", c, "concrete", s + 8, -40, z + 2, 40, 28, 16)
    box("Dock_Crane_Leg", c, "crane", s + 44, -30, 5, 8, 8, 40)
    box("Dock_Crane_Jib", c, "crane", s + 44, -46, 42, 6, 44, 5)

    for i, tt in enumerate((-62, 0, 62)):
        ss = s + 84
        box("Customer_Ship_%02d_Hull" % (i + 1), c, "ship", ss, tt, 1.0, 34, 24, 12)
        box("Customer_Ship_%02d_Deck" % (i + 1), c, "ship_deck", ss, tt, 13.0,
            26, 18, 4)
        drum("Customer_Ship_%02d_Mast" % (i + 1), c, "trim", ss, tt, 17.0,
             1.6, 1.2, 26, n=6)

    box("Player_Ship_Hull", c, "hull", s + 30, -74, 1.0, 30, 22, 12)
    box("Player_Ship_Deck", c, "ship_deck", s + 30, -74, 13.0, 22, 16, 4)
    drum("Player_Ship_Mast", c, "trim", s + 30, -74, 17.0, 1.6, 1.2, 30, n=6)
    return c


# --------------------------------------------------------------- anchors ----

def build_anchors(root):
    """The frozen contract. Codex binds gameplay to these names; nothing here
    may be renamed or moved once art integration starts."""
    c = coll("Anchors", root)

    anchor("Mine_Output", c, 72, -20, 66)
    anchor("Train_Load", c, 150, -20, 62)
    anchor("Train_Unload", c, 206, -18, 55)
    anchor("Storage_Input", c, 190, 26, 52)
    anchor("Storage_Output", c, 222, 40, 52)
    anchor("Refinery_Input", c, 200, -22, 52)
    anchor("Refinery_Output", c, 232, -28, 52)

    for key, t, _ in STATIONS:
        _, s, z, _, _ = BY_KEY[key]
        anchor("Station_%s_Input" % key, c, s - 26, t - 12, z + 11)
        anchor("Station_%s_Work" % key, c, s, t, z + 4)
        anchor("Station_%s_Output" % key, c, s + 26, t - 16, z + 9)
        anchor("Station_%s_Upgrade" % key, c, s, t, z + 34, kind='SPHERE')
        anchor("Station_%s_Worker" % key, c, s - 4, t + 28, z + 4)

    _, sd, zd, _, _ = BY_KEY["Dock"]
    for i, tt in enumerate((-62, 0, 62)):
        anchor("Customer_Berth_%02d" % (i + 1), c, sd + 84, tt, 2, kind='ARROWS')
    anchor("Player_Outfitting", c, sd + 30, -74, 2, kind='ARROWS')
    anchor("Set_Sail", c, sd + 96, -74, 2, size=10, kind='SPHERE')

    stops = coll("Camera_Stops", root)
    for i, (key, s, z, _, _) in enumerate(BANDS):
        anchor("Camera_Stop_%02d" % (i + 1), stops, s, 0, z + 10,
               size=12, kind='CUBE').name = "Camera_Stop_%02d" % (i + 1)

    b = anchor("Camera_Bounds", c, 560, 0, 40, kind='CUBE')
    b.empty_display_size = 1.0
    # half-extents in screen units: the full scrollable ladder
    b.scale = (VIEW_W * 0.5, 600.0 * S_STRETCH, 120.0)
    b.rotation_euler = (0.0, 0.0, math.radians(-45.0))
    return c


# --------------------------------------------------------- cameras / lit ----

def _place(cam, s, t, z, scale):
    """Aim an ortho camera at a screen-frame point."""
    x, y = W(s, t)
    fwd = (-0.4731, 0.4731, -0.7431)
    d = 2200.0
    cam.location = (x - fwd[0] * d, y - fwd[1] * d, z - fwd[2] * d)
    cam.rotation_euler = (math.radians(42.0), 0.0, math.radians(45.0))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = scale
    cam.data.clip_start = 1.0
    cam.data.clip_end = 6000.0


def build_cameras(root):
    c = coll("Cameras", root)

    for name, s, z, scale in (("Cam_Overview", 560, 30, 1460.0),
                              ("Cam_Play_Cannon", 350, 36, VIEW_H)):
        cd = bpy.data.cameras.new(name)
        ob = bpy.data.objects.new(name, cd)
        c.objects.link(ob)
        _place(ob, s, 0, z, scale)

    sun = bpy.data.lights.new("Sun", 'SUN')
    sun.energy = 4.2
    sun.angle = math.radians(6.0)
    so = bpy.data.objects.new("Sun", sun)
    so.rotation_euler = (math.radians(48.0), math.radians(6.0),
                         math.radians(118.0))
    c.objects.link(so)

    sc = bpy.context.scene
    sc.camera = bpy.data.objects["Cam_Overview"]
    sc.render.resolution_x = 1080
    sc.render.resolution_y = 2340
    engines = {e.identifier for e in
               bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items}
    sc.render.engine = ('BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines
                        else 'BLENDER_EEVEE')

    world = bpy.data.worlds.get("World") or bpy.data.worlds.new("World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.16, 0.30, 0.48, 1.0)
        bg.inputs[1].default_value = 0.9
    sc.world = world
    return c


# ------------------------------------------------------------------ main ----

def set_locked(locked=True):
    """Milestone-1 start state: Cannon built, the other four still pads."""
    for key, _, _ in STATIONS:
        built = key == "Cannon" or not locked
        for suffix, on in (("_Built", built), ("_Pad", not built)):
            c = bpy.data.collections.get("Station_%s%s" % (key, suffix))
            if not c:
                continue
            for ob in c.objects:
                ob.hide_viewport = not on
                ob.hide_render = not on


def build():
    wipe()
    root = bpy.data.collections.new("SHIPYARD")
    bpy.context.scene.collection.children.link(root)

    build_terrain(root)
    build_logistics(root)
    build_source(root)
    for key, t, ckey in STATIONS:
        build_station(root, key, t, ckey)
    build_dock(root)
    build_anchors(root)
    build_cameras(root)
    set_locked(True)

    n = sum(len(c.objects) for c in bpy.data.collections)
    print("shipyard blockout: %d objects, ladder %d screen units, "
          "%d bands of %d" % (n, int(BANDS[-1][1] + BAND * 0.5),
                              len(BANDS), int(BAND)))
    return root


if __name__ == "__main__":
    build()
