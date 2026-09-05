"""Portrait shipyard island, built to match design/attention-focused-concepts/
01-focus-ladder.png as closely as a generator can.

READ THIS BEFORE ADDING ANYTHING
The reference is CLEAN. Each terrace carries one building complex sitting in
open ground, trees ring the outer rim only, and one glowing route threads the
whole island. An earlier pass scattered crates, barrels and lamp posts over
every band chasing a "density" measurement; it read as junk and was removed.
Open ground is the composition, not space waiting to be filled.

Layout, top to bottom, exactly as the reference orders it:

    1  mountain, mine portal, yellow tipple, dump truck, coal train
    2  dark coal plant, twin yellow-banded stacks, conveyor, coal wagons
    3  HERO blue factory, focus halo, upgrade badge, container train  <- focus
    4  yellow refinery, banded tanks, pipe runs, tanker cars
    5  grey warehouse, blue containers, loader
    6  two further clean terraces (Navigation, Figurehead)
    7  port: yellow gantry crane, container ship, pier
    8  three customer islands with speech bubbles

SCREEN FRAME
    s  down-screen along the ladder (0 = mine)
    t  across, +t is screen right
    z  world up

W(s, t) maps onto the isomap camera (rot 42/0/45 ortho, the one shot.py
builds), which projects as

    screen_x = 0.7071 (x + y)
    screen_y = 0.5254 (y - x) + 0.669 z

so one unit of s or t is one unit on screen. Buildings stay world-axis aligned
- that is what gives them two visible faces - while the island is placed in
(s, t) and so runs straight up the portrait frame. A box sized (wx, wy) is
0.7071*(wx+wy) wide on screen: size against that, not against wx.

Run headless:  blender --background --python blockout.py
"""

import bpy
import math
import random

# ---------------------------------------------------------------- frame ----

K = 0.7071067811865476
S_STRETCH = 1.3458980337503153     # 1 unit of s == 1 unit down-screen


def W(s, t):
    a = s * S_STRETCH
    return ((t + a) * K, (t - a) * K)


ASPECT = 2340.0 / 1080.0
VIEW_W = 240.0
VIEW_H = VIEW_W * ASPECT           # 520
BAND = 67.0

# MEASURED OFF THE REFERENCE - do not deepen these.
#   terrace width : depth  = 3.1 : 1      (206 wide, ~66 deep)
#   row pitch             = 67            (240px of an 864px-wide frame)
#   island width in frame = 86%
# half_s was 54 here for several passes, giving 1.9:1 shelves, and that single
# number is what made the island read as a narrow caterpillar instead of a
# wide clean stack. Depth is the thing to protect.
# key            s      z   half_s half_t    t   sides
BANDS = [
    ("Mine",       50,  78,   42, 100,   -6,  19),
    ("Works",     117,  66,   41, 104,    6,  21),
    ("Cannon",    184,  55,   44, 108,   -4,  20),
    ("Hull",      251,  45,   41, 102,    8,  19),
    ("Rigging",   318,  36,   42, 100,   -5,  21),
    ("Navigation",385,  28,   41,  96,    7,  20),
    ("Figurehead",452,  20,   42,  94,   -4,  19),
    ("Dock",      519,  12,   44,  98,    4,  21),
]
BY_KEY = {b[0]: b for b in BANDS}


def band(key):
    b = BY_KEY[key]
    return b[1], b[2], b[5]


def zat(s):
    """Ground height anywhere down the ladder. Everything on the island
    derives its height from this; hard-coded z broke on every band move."""
    pts = [(b[1], b[2]) for b in BANDS]
    if s <= pts[0][0]:
        return pts[0][1]
    if s >= pts[-1][0]:
        return pts[-1][1]
    for (s0, z0), (s1, z1) in zip(pts, pts[1:]):
        if s0 <= s <= s1:
            return z0 + (z1 - z0) * (s - s0) / (s1 - s0)
    return pts[-1][1]


LOBES = [
    (84,   62,  21, 40, 13), (84,  -66,  18, 36, 11),
    (151, -68,  20, 38, 13), (151,  60,  17, 34, 11),
    (218,  66,  21, 40, 13), (218, -62,  18, 36, 11),
    (285, -66,  20, 38, 13), (285,  60,  17, 34, 11),
    (352,  62,  20, 38, 13), (352, -58,  17, 34, 11),
    (419, -62,  20, 38, 13), (419,  56,  17, 34, 11),
    (486,  58,  21, 40, 13), (486, -56,  18, 36, 11),
]

# Buildings sit centred on their terrace in the reference, not alternating
# left and right; the route weaves around them instead.
# key             t    colour
STATIONS = [
    ("Cannon",     -2, "factory"),
    ("Hull",        4, "mine"),
    ("Rigging",    -2, "storage"),
    ("Navigation",  4, "navigation"),
    ("Figurehead", -2, "figurehead"),
]

# -------------------------------------------------------------- palette ----

PALETTE = {
    "sea":        (0.020, 0.105, 0.30),
    "sea_lt":     (0.045, 0.19, 0.42),
    "rock":       (0.33, 0.33, 0.36),
    "rock_dark":  (0.19, 0.19, 0.22),
    "grass":      (0.34, 0.42, 0.17),     # warm olive, as the reference
    "grass_dry":  (0.50, 0.50, 0.24),
    "sand":       (0.62, 0.55, 0.33),
    "tree":       (0.045, 0.17, 0.065),
    "tree_lt":    (0.09, 0.26, 0.10),
    "trunk":      (0.15, 0.095, 0.055),
    "road":       (0.46, 0.44, 0.39),
    "rail":       (0.22, 0.19, 0.15),
    "flow":       (1.00, 0.80, 0.06),
    "mine":       (0.94, 0.70, 0.06),     # industrial yellow
    "storage":    (0.42, 0.46, 0.51),
    "plant":      (0.20, 0.22, 0.26),     # dark coal plant
    "concrete":   (0.46, 0.46, 0.49),
    "roof":       (0.24, 0.26, 0.30),
    "crane":      (0.96, 0.74, 0.06),
    "ship":       (0.62, 0.15, 0.13),
    "ship_hull":  (0.14, 0.19, 0.31),
    "ship_deck":  (0.72, 0.71, 0.68),
    "container":  (0.14, 0.36, 0.62),
    "factory":    (0.13, 0.34, 0.68),     # the hero blue
    "factory_lt": (0.24, 0.50, 0.80),
    "navigation": (0.10, 0.50, 0.58),
    "figurehead": (0.44, 0.22, 0.58),
    "trim":       (0.66, 0.66, 0.63),
    "glass":      (0.30, 0.56, 0.72),
    "focus":      (0.66, 0.98, 0.18),
    "white":      (0.88, 0.89, 0.88),
    "coin":       (0.98, 0.76, 0.12),
    "gem":        (0.30, 0.85, 0.88),
    "shop_a":     (0.78, 0.22, 0.15),
    "shop_b":     (0.34, 0.46, 0.56),
    "shop_c":     (0.88, 0.44, 0.10),
}

_mats = {}


def dark(key, f=0.55):
    name = "%s_dk" % key
    if name not in PALETTE:
        r, g, b = PALETTE[key]
        PALETTE[name] = (r * f, g * f, b * f)
    return name


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
            bsdf.inputs["Roughness"].default_value = 0.8
        if key in ("flow", "focus") and "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = (r, g, b, 1.0)
            bsdf.inputs["Emission Strength"].default_value = 0.6
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


def box(name, coll, key, s, t, z, wx, wy, wz, ox=0.0, oy=0.0):
    """ox/oy nudge in WORLD xy - how a sub-part sits along its parent's own
    axis (a truck cab, a crane leg) rather than along the ladder."""
    cx, cy = W(s, t)
    cx += ox
    cy += oy
    hx, hy = wx * 0.5, wy * 0.5
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


def drum(name, coll, key, s, t, z, r_bot, r_top, h, n=12, ox=0.0, oy=0.0):
    cx, cy = W(s, t)
    v, f = _ngon(n, r_bot, r_top, h)
    v = [(x + cx + ox, y + cy + oy, zz + z) for x, y, zz in v]
    return _obj(name, v, f, key, coll)


def terrace(name, coll, key, s, t, z, half_s, half_t, taper=0.80, n=14,
            seed=0, jitter=0.11):
    """Plateau with cliffs sloping inward. The per-vertex jitter is what keeps
    the coastline irregular instead of a stack of clean ellipses."""
    depth = z + 40.0
    rng = random.Random(seed)
    jit = [rng.uniform(1.0 - jitter, 1.0 + jitter * 0.8) for _ in range(n)]
    v, f = [], []
    for scale, zz in ((taper, 0.0), (1.0, depth)):
        for i in range(n):
            a = 2.0 * math.pi * i / n
            r = scale * jit[i]
            v.append((math.cos(a) * r, math.sin(a) * r, zz))
    for i in range(n):
        j = (i + 1) % n
        f.append((i, j, n + j, n + i))
    f.append(tuple(range(n - 1, -1, -1)))
    f.append(tuple(range(n, 2 * n)))
    return _obj(name, [W(s + x * half_s, t + y * half_t) + (zz + z - depth,)
                       for x, y, zz in v], f, key, coll)


def patch(name, coll, key, s, t, z, hs, ht, seed=0, n=11):
    """Flat worn-ground disc. Used sparingly - a couple per terrace under the
    working area, never blanketing the band."""
    rng = random.Random(seed)
    verts = []
    for i in range(n):
        a = 2.0 * math.pi * i / n
        j = rng.uniform(0.82, 1.16)
        verts.append(W(s + math.cos(a) * hs * j, t + math.sin(a) * ht * j)
                     + (z,))
    return _obj(name, verts, [tuple(range(n))], key, coll)


def ribbon(name, coll, key, pts, width, lift=2.8, z=None):
    """Flat strip through (s, t) points. Height from zat() unless overridden."""
    world = [W(s, t) + ((zat(s) if z is None else z) + lift,) for s, t in pts]
    verts, faces = [], []
    for i, (x, y, zz) in enumerate(world):
        px, py, _ = world[max(i - 1, 0)]
        nx, ny, _ = world[min(i + 1, len(world) - 1)]
        dx, dy = nx - px, ny - py
        d = math.hypot(dx, dy) or 1.0
        ox, oy = -dy / d * width * 0.5, dx / d * width * 0.5
        verts += [(x + ox, y + oy, zz), (x - ox, y - oy, zz)]
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


def annulus(name, coll, key, s, t, z, r_s, r_t, w, n=44):
    verts, faces = [], []
    for i in range(n):
        a = 2.0 * math.pi * i / n
        c_, s_ = math.cos(a), math.sin(a)
        verts.append(W(s + c_ * r_s, t + s_ * r_t) + (z,))
        verts.append(W(s + c_ * (r_s + w), t + s_ * (r_t + w)) + (z,))
    for i in range(n):
        j = (i + 1) % n
        faces.append((i * 2, i * 2 + 1, j * 2 + 1, j * 2))
    return _obj(name, verts, faces, key, coll)


# ------------------------------------------------------------ vegetation ----

def _cone_into(verts, faces, cx, cy, cz, r, h, n=6):
    base = len(verts)
    for i in range(n):
        a = 2.0 * math.pi * i / n
        verts.append((cx + r * math.cos(a), cy + r * math.sin(a), cz))
    verts.append((cx, cy, cz + h))
    for i in range(n):
        faces.append((base + i, base + (i + 1) % n, base + n))
    faces.append(tuple(range(base + n - 1, base - 1, -1)))


def trees(name, coll, ring, rng, key="tree"):
    """Three-tier conifers, merged into two meshes (foliage, trunks)."""
    verts, faces, tv, tf = [], [], [], []
    for s, t, z, sc in ring:
        cx, cy = W(s, t)
        h = rng.uniform(20.0, 30.0) * sc
        r = rng.uniform(3.4, 4.8) * sc
        base = len(tv)
        bv, bf = _ngon(5, r * 0.30, r * 0.24, h * 0.26)
        tv += [(x + cx, y + cy, zz + z) for x, y, zz in bv]
        tf += [tuple(i + base for i in ff) for ff in bf]
        for rr, hh, zo in ((1.00, 0.44, 0.14), (0.76, 0.38, 0.40),
                           (0.50, 0.34, 0.66)):
            _cone_into(verts, faces, cx, cy, z + h * zo, r * rr, h * hh, n=7)
    if not verts:
        return None
    _obj(name + "_Trunks", tv, tf, "trunk", coll)
    return _obj(name, verts, faces, key, coll)


def rim_cluster(s, t, hs, ht, rng, n, side, z, spread=0.55):
    """Points in an arc on one flank of a terrace. The reference keeps trees
    banked on the left and right edges and the working ground clear."""
    out = []
    mid = 0.0 if side > 0 else math.pi
    for _ in range(n):
        a = mid + rng.uniform(-spread, spread) * math.pi
        rr = rng.uniform(0.80, 1.02)
        out.append((s + math.sin(a) * hs * rr * 0.85,
                    t + math.cos(a) * ht * rr * side,
                    z, rng.uniform(0.8, 1.25)))
    return out


def rocks(name, coll, ring, rng):
    verts, faces = [], []
    for s, t, z, sc in ring:
        cx, cy = W(s, t)
        r = rng.uniform(3.0, 6.5) * sc
        v, f = _ngon(6, r, r * 0.45, r * rng.uniform(0.8, 1.5))
        base = len(verts)
        verts += [(x + cx, y + cy, zz + z) for x, y, zz in v]
        faces += [tuple(i + base for i in ff) for ff in f]
    if not verts:
        return None
    return _obj(name, verts, faces, "rock", coll)


# ----------------------------------------------------------------- parts ----

def crag(name, coll, key, s, t, z, r, h, seed=0, n=6, taper=0.14):
    rng = random.Random(seed)
    jit = [rng.uniform(0.68, 1.28) for _ in range(n)]
    off = [rng.uniform(-0.12, 0.12) for _ in range(n)]
    v, f = [], []
    for scale, zz in ((1.0, 0.0), (taper, h)):
        for i in range(n):
            a = 2.0 * math.pi * i / n + off[i]
            rr = r * scale * jit[i]
            v.append((math.cos(a) * rr, math.sin(a) * rr, zz))
    for i in range(n):
        j = (i + 1) % n
        f.append((i, j, n + j, n + i))
    f.append(tuple(range(n - 1, -1, -1)))
    f.append(tuple(range(n, 2 * n)))
    cx, cy = W(s, t)
    return _obj(name, [(x + cx, y + cy, zz + z) for x, y, zz in v], f, key,
                coll)


def truck(name, coll, s, t, z, key, l=24.0, w=15.0, h=11.0):
    box(name + "_Chassis", coll, "rock_dark", s, t, z, l, w * 0.74, 3.5)
    box(name + "_Bed", coll, key, s, t, z + 3.5, l * 0.64, w, h, ox=l * 0.17)
    box(name + "_Cab", coll, "trim", s, t, z + 3.5, l * 0.30, w * 0.88,
        h * 1.30, ox=-l * 0.35)


def wagon(name, coll, s, t, z, key, load=None, l=21.0, w=13.0, h=9.0):
    box(name + "_Chassis", coll, "rock_dark", s, t, z, l, w * 0.8, 3.0)
    box(name + "_Body", coll, key, s, t, z + 3.0, l, w, h)
    if load:
        box(name + "_Load", coll, load, s, t, z + 3.0 + h, l * 0.88, w * 0.86,
            4.0)


def tanker(name, coll, s, t, z, l=22.0):
    box(name + "_Chassis", coll, "rock_dark", s, t, z, l, 10, 3.0)
    drum(name + "_Tank", coll, "trim", s, t, z + 3.0, 6.5, 6.5, l, n=10)


def headframe(name, coll, s, t, z, key, w=20.0, h=44.0):
    for i, (ox, oy) in enumerate(((-w * .5, -w * .5), (w * .5, -w * .5),
                                  (-w * .5, w * .5), (w * .5, w * .5))):
        box("%s_Leg_%02d" % (name, i + 1), coll, key, s, t, z, 3.5, 3.5, h,
            ox=ox, oy=oy)
    for lvl in (0.42, 0.78):
        box("%s_Brace_%02d" % (name, int(lvl * 100)), coll, key, s, t,
            z + h * lvl, w + 3.5, w + 3.5, 3.0)
    box(name + "_Head", coll, key, s, t, z + h, w + 4, w + 4, 6.0)
    drum(name + "_Sheave", coll, "rock_dark", s, t, z + h + 6, 7, 7, 3.0, n=10)


def gantry(name, coll, s, t, z, key, span=62.0, height=46.0, jib=58.0,
           leg=7.0):
    for i, (ox, oy) in enumerate(((-span * .5, -15), (span * .5, -15),
                                  (-span * .5, 15), (span * .5, 15))):
        box("%s_Leg_%02d" % (name, i + 1), coll, key, s, t, z, leg, leg,
            height, ox=ox, oy=oy)
    for nm, oy in (("A", -15.0), ("B", 15.0)):
        box("%s_Beam_%s" % (name, nm), coll, key, s, t, z + height,
            span + leg, leg, leg, oy=oy)
    box(name + "_Portal", coll, key, s, t, z + height, leg, 30 + leg, leg)
    box(name + "_Jib", coll, key, s, t, z + height + leg * 0.4, leg * 0.85,
        jib, leg * 0.85, oy=-jib * 0.5 - 14)
    box(name + "_Trolley", coll, "rock_dark", s, t, z + height - 5, leg,
        leg * 1.5, 5.0, oy=-jib * 0.42)
    box(name + "_Hoist", coll, "rock_dark", s, t, z + height - 24, 2.0, 2.0,
        19.0, oy=-jib * 0.42)


def ship_hull(name, coll, key, s, t, z, length, width, height, bow=0.30):
    cx, cy = W(s, t)
    hl, hw = length * 0.5, width * 0.5
    b = bow * hw
    bot = ((-hw, -hl), (hw, -hl), (b, hl), (-b, hl))
    top = ((-hw * 1.06, -hl), (hw * 1.06, -hl), (b * 1.15, hl * 1.04),
           (-b * 1.15, hl * 1.04))
    v = [(cx + x, cy + y, z) for x, y in bot]
    v += [(cx + x, cy + y, z + height) for x, y in top]
    f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5),
         (2, 3, 7, 6), (3, 0, 4, 7)]
    return _obj(name, v, f, key, coll)


def containers(name, coll, s, t, z, rng, cols=3, rows=2, cw=11.0, cl=22.0,
               ch=8.0, ox=0.0, oy=0.0):
    keys = ("container", "ship", "mine", "navigation", "trim")
    for i in range(cols):
        for j in range(rows):
            box("%s_%02d_%02d" % (name, i + 1, j + 1), coll,
                rng.choice(keys), s, t, z + j * ch, cl, cw, ch,
                ox=ox, oy=oy + (i - (cols - 1) * 0.5) * (cw + 1.5))


def bubble(name, coll, s, t, z, icon):
    """White speech bubble with a coloured token, as over each customer."""
    box(name + "_Body", coll, "white", s, t, z, 26, 22, 17)
    box(name + "_Tail", coll, "white", s, t, z - 5, 7, 6, 6)
    box(name + "_Icon", coll, icon, s, t, z + 4, 13, 3, 11, oy=-11.5)


def shop(name, coll, s, t, z, key):
    box(name + "_Body", coll, key, s, t, z, 34, 28, 19)
    box(name + "_Window", coll, "glass", s, t, z + 6, 35, 29, 7)
    box(name + "_Roof", coll, dark(key), s, t, z + 19, 39, 33, 3)
    box(name + "_Awning", coll, "white", s, t, z + 15, 11, 30, 2, ox=20)
    box(name + "_Sign", coll, "white", s, t, z + 22, 13, 3, 9)


# ----------------------------------------------------------- collections ----

def coll(name, parent):
    c = bpy.data.collections.new(name)
    parent.children.link(c)
    return c


ASSET_COLLECTION = "ASSETS"


def wipe():
    """Clear the generated island, sparing the downloaded asset library.

    Assets are expensive to re-download and are pure source data - they are
    never placed directly, only linked-duplicated by assets.py.
    """
    lib = bpy.data.collections.get(ASSET_COLLECTION)
    spared = {ob.name for ob in lib.all_objects} if lib else set()

    for ob in list(bpy.data.objects):
        if ob.name not in spared:
            bpy.data.objects.remove(ob, do_unlink=True)
    for c in list(bpy.data.collections):
        if c is not lib:
            bpy.data.collections.remove(c)
    for blk in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras,
                bpy.data.lights):
        for d in list(blk):
            if d.users == 0 and not d.use_fake_user:
                blk.remove(d)
    _mats.clear()


# ---------------------------------------------------------------- island ----

def _weave(s0, s1, amp, step=14.0, phase=0.0):
    """Smooth sine weave down the ladder, one crossing per band.

    Generated rather than hand-listed: the route has to be sampled densely
    enough to ride the cliff drop between terraces, otherwise the ribbon
    spans the gap and the road reads as broken stubs.
    """
    pts, s = [], s0
    while s <= s1:
        pts.append((s, amp * math.sin(s / BAND * math.pi + phase)))
        s += step
    return pts


SPINE = _weave(-8.0, 552.0, 24.0)
RAIL = [(22, -34), (52, -28), (82, -18), (111, -8), (139, -18), (166, -28)]


def build_terrain(root):
    c = coll("Terrain", root)
    x, y = W(340, 0)
    _obj("Sea", [(x - 1600, y - 1600, 0), (x + 1600, y - 1600, 0),
                 (x + 1600, y + 1600, 0), (x - 1600, y + 1600, 0)],
         [(0, 1, 2, 3)], "sea", c)

    for bi, (key, s, z, hs, ht, t, n) in enumerate(BANDS):
        sd = 100 + bi
        terrace("Land_%s_Rock" % key, c, "rock", s, t, z, hs, ht, n=n, seed=sd)
        cap = "sand" if key == "Dock" else ("grass_dry" if key == "Mine"
                                            else "grass")
        terrace("Land_%s_Cap" % key, c, cap, s, t, z + 2.0, hs * 0.93,
                ht * 0.93, taper=0.985, n=n, seed=sd)
        # one soft worn area under the working middle, nothing more
        patch("Ground_%s" % key, c, "grass_dry" if key != "Dock" else "sand",
              s, t, z + 2.3, hs * 0.52, ht * 0.44, seed=500 + bi)

    for i, (s, t, hs, ht, n) in enumerate(LOBES):
        z = zat(s)
        sd = 200 + i
        terrace("Land_Lobe_%02d_Rock" % (i + 1), c, "rock", s, t, z, hs, ht,
                n=n, seed=sd)
        terrace("Land_Lobe_%02d_Cap" % (i + 1), c, "grass", s, t, z + 2.0,
                hs * 0.92, ht * 0.92, taper=0.985, n=n, seed=sd)

    zm = zat(60)
    for i, (cs, ct, cr, ch, ck) in enumerate((
            (2, -14, 46, 46, "rock"), (-6, 16, 38, 38, "rock"),
            (18, -40, 30, 30, "rock_dark"), (14, 34, 27, 26, "rock_dark"),
            (-14, -4, 30, 54, "rock"), (30, 6, 24, 22, "rock_dark"),
            (26, -60, 22, 20, "rock_dark"), (16, 54, 20, 17, "rock_dark"))):
        crag("Mountain_Crag_%02d" % (i + 1), c, ck, cs, ct, zm - 4, cr, ch,
             seed=300 + i, n=6 if i % 2 else 7)
    return c


def build_vegetation(root):
    """Trees banked on the left and right flanks only. The reference never
    puts a tree in the working middle of a terrace."""
    c = coll("Vegetation", root)
    rng = random.Random(19)
    for key, s, z, hs, ht, t, _n in BANDS:
        per = 15 if key not in ("Mine", "Dock") else 11
        ring = (rim_cluster(s, t, hs, ht, rng, per, 1, z + 2.0)
                + rim_cluster(s, t, hs, ht, rng, per, -1, z + 2.0))
        trees("Trees_%s" % key, c, ring, rng,
              "tree_lt" if key == "Mine" else "tree")
        rocks("Rocks_%s" % key, c,
              rim_cluster(s, t, hs * 1.02, ht * 1.03, rng, 8, 1, z + 1.0)
              + rim_cluster(s, t, hs * 1.02, ht * 1.03, rng, 8, -1, z + 1.0),
              rng)

    for i, (s, t, hs, ht, _n) in enumerate(LOBES):
        z = zat(s)
        side = 1 if t > 0 else -1
        trees("Trees_Lobe_%02d" % (i + 1), c,
              rim_cluster(s, t, hs, ht, rng, 7, side, z + 2.0, spread=0.7),
              rng)
    return c


def build_logistics(root):
    c = coll("Logistics", root)
    ribbon("Road_Spine", c, "road", SPINE, 26.0)
    ribbon("Flow_Line", c, "flow", SPINE, 13.0, lift=3.8)
    ribbon("Rail_Mine_To_Works", c, "rail", RAIL, 9.0, lift=3.2)
    return c


def build_source(root):
    """Terraces 1 and 2 of the reference: mine, then the coal plant."""
    c = coll("Source", root)

    def g(s):
        return zat(s) + 2.0

    # ---- 1: portal in the massif, yellow tipple, dump truck, coal train
    box("Mine_Portal_Face", c, "rock_dark", 5, -40, g(5), 30, 26, 26)
    box("Mine_Portal_Mouth", c, "rail", 5, -40, g(5), 15, 13, 16, ox=11)
    box("Mine_Headhouse", c, "mine", 30, -26, g(30), 40, 30, 28)
    box("Mine_Headhouse_Roof", c, dark("mine"), 30, -26, g(30) + 28, 44, 34, 3)
    headframe("Mine_Headframe", c, 30, -26, g(30) + 31, "rail", w=17, h=32)
    box("Mine_Conveyor", c, "concrete", 52, -14, g(52) + 4, 46, 8, 5)
    truck("Mine_Truck", c, 20, 22, g(20), "mine", l=30, w=18, h=15)
    drum("Mine_OrePile", c, "rock_dark", 60, -46, g(60), 16, 3, 10, n=8)

    box("Train_Engine", c, "mine", 84, -10, g(84), 24, 15, 13)
    box("Train_Engine_Cab", c, dark("mine"), 84, -10, g(84) + 13, 11, 14, 9,
        ox=-6)
    for i, ss in enumerate((104, 124, 144)):
        wagon("Train_Wagon_%02d" % (i + 1), c, ss, -6, g(ss), "rail",
              load="rock_dark")

    # ---- 2: dark coal plant, twin yellow-banded stacks, conveyor, silos
    box("Plant_Hall", c, "plant", 119, 8, g(119), 62, 46, 34)
    box("Plant_Hall_Roof", c, dark("plant"), 119, 8, g(119) + 34, 66, 50, 3)
    box("Plant_Annex", c, "plant", 119, 8, g(119), 30, 26, 22, ox=-40)
    for nm, oy, h in (("L", -14.0, 52.0), ("R", 4.0, 44.0)):
        drum("Plant_Stack_%s" % nm, c, "rock_dark", 109, 20, g(109) + 20,
             7, 6, h, oy=oy)
        drum("Plant_Stack_%s_Band" % nm, c, "mine", 109, 20,
             g(109) + 20 + h * 0.74, 7.5, 6.5, h * 0.14, oy=oy)
    box("Plant_Conveyor", c, "storage", 132, -34, g(132) + 6, 44, 10, 6)
    box("Plant_Crusher", c, "storage", 142, -48, g(142), 26, 22, 16)
    for i, (ss, tt) in enumerate(((99, 52), (117, 66), (137, 52))):
        drum("Storage_Silo_%02d" % (i + 1), c, "storage", ss, tt, g(ss),
             12, 12, 34)
        box("Storage_Silo_%02d_Cap" % (i + 1), c, dark("storage"), ss, tt,
            g(ss) + 34, 26, 26, 4)
    return c


def build_station(root, key, t, ckey):
    """The reference's hero: a layered factory block with a glazed wing, a
    tall stack, and an emblem panel - centred on its terrace."""
    s, z, bt = band(key)
    t += bt
    fam = coll("Station_%s" % key, root)
    pad = coll("Station_%s_Pad" % key, fam)
    built = coll("Station_%s_Built" % key, fam)
    g = z + 2.0

    # ---- locked: a quiet levelled pad. Deliberately sparse - the reference
    # gives future space no visual weight at all.
    patch("Pad_%s_Ground" % key, pad, "sand", s, t, z + 2.3, 40, 34,
          seed=600 + len(key))
    box("Pad_%s_Slab" % key, pad, "concrete", s, t, z + 2.4, 56, 46, 2.5)
    # Perimeter bars. As a filled 60x50 slab this rendered as a solid yellow
    # blob and was the loudest thing on the island.
    for nm, wx, wy, ox, oy in (("N", 60, 2.5, 0, -25), ("S", 60, 2.5, 0, 25),
                               ("W", 2.5, 50, -30, 0), ("E", 2.5, 50, 30, 0)):
        box("Pad_%s_Kerb_%s" % (key, nm), pad, "mine", s, t, z + 4.9,
            wx, wy, 1.2, ox=ox, oy=oy)
    for i, (ds, dt) in enumerate(((-18, -14), (-18, 14), (18, -14), (18, 14))):
        box("Pad_%s_Footing_%02d" % (key, i + 1), pad, "concrete", s + ds,
            t + dt, z + 4.9, 12, 10, 6)

    # ---- built
    box("%s_Hall" % key, built, ckey, s, t, g, 66, 48, 34)
    box("%s_Hall_Roof" % key, built, dark(ckey), s, t, g + 34, 70, 52, 4)
    box("%s_Wing" % key, built, ckey, s, t, g, 34, 30, 46, ox=-30, oy=8)
    box("%s_Wing_Roof" % key, built, dark(ckey), s, t, g + 46, 38, 34, 4,
        ox=-30, oy=8)
    box("%s_Glazing" % key, built, "glass", s, t, g + 10, 67, 49, 9)
    box("%s_Wing_Glazing" % key, built, "glass", s, t, g + 14, 35, 31, 20,
        ox=-30, oy=8)
    box("%s_Emblem" % key, built, "%s_lt" % ckey if "%s_lt" % ckey in PALETTE
        else "trim", s, t, g + 14, 3, 20, 20, ox=34)
    drum("%s_Stack" % key, built, ckey, s, t, g + 20, 9, 8, 54, ox=6, oy=-26)
    drum("%s_Stack_Cap" % key, built, dark(ckey), s, t, g + 74, 10, 9, 4,
        ox=6, oy=-26)
    box("%s_Duct" % key, built, dark(ckey), s, t, g + 8, 12, 34, 12,
        ox=30, oy=-30)
    box("%s_Annex" % key, built, "concrete", s + 28, t + 20, g, 26, 22, 16)
    box("%s_InBin" % key, built, "storage", s - 30, t - 22, g, 18, 18, 11)
    box("%s_OutRack" % key, built, "trim", s + 30, t - 24, g, 20, 16, 9)
    for i, (ds, dt) in enumerate(((-16, -30), (-4, -34))):
        box("%s_Crate_%02d" % (key, i + 1), built, "mine", s + ds, t + dt, g,
            12, 10, 9)
    truck("%s_Truck" % key, built, s + 12, t - 42, g, "container",
          l=24, w=15, h=11)
    return fam


def build_dock(root):
    """Port on the left flank, then the three customer islands below."""
    c = coll("Dock", root)
    s, z, bt = band("Dock")
    rng = random.Random(53)

    box("Dock_Warehouse", c, "storage", s - 12, bt + 20, z + 2, 60, 44, 24)
    box("Dock_Warehouse_Roof", c, dark("storage"), s - 12, bt + 20, z + 26,
        64, 48, 3)
    for i in range(3):
        box("Dock_Awning_%02d" % (i + 1), c, "mine", s - 12, bt + 20, z + 14,
            4, 12, 8, ox=31, oy=-14 + i * 14)
    containers("Dock_Stack", c, s + 10, bt + 60, z + 2, rng, cols=3, rows=2)
    truck("Dock_Truck", c, s - 34, bt + 6, z + 2, "mine", l=24, w=15, h=11)

    box("Dock_Quay", c, "concrete", s + 44, bt - 62, 0.0, 62, 76, 7)
    for i in range(4):
        drum("Dock_Bollard_%02d" % (i + 1), c, "rock_dark", s + 44, bt - 62,
             7.0, 2.0, 1.8, 4.5, n=6, ox=-22 + i * 15, oy=-33)
    gantry("Dock_Crane", c, s + 44, bt - 66, 7.0, "crane", span=40, height=34,
           jib=36, leg=5)

    ship_hull("Cargo_Ship_Hull", c, "ship", s + 40, bt - 124, 1.0, 106, 34, 9)
    ship_hull("Cargo_Ship_Upper", c, "ship_hull", s + 40, bt - 124, 9.0, 104,
              33, 6)
    box("Cargo_Ship_Super", c, "ship_deck", s + 40, bt - 124, 15.0, 24, 20, 16,
        oy=-36)
    box("Cargo_Ship_Bridge", c, "trim", s + 40, bt - 124, 31.0, 19, 14, 4,
        oy=-36)
    containers("Cargo_Ship_Cargo", c, s + 40, bt - 124, 15.0, rng, cols=3,
               rows=2, cl=17, cw=8, ch=7, oy=12)

    ship_hull("Player_Ship_Hull", c, "ship_hull", s + 86, bt - 96, 1.0, 42, 22,
              11)
    box("Player_Ship_Deck", c, "ship_deck", s + 86, bt - 96, 12.0, 15, 13, 6)
    drum("Player_Ship_Mast", c, "trim", s + 86, bt - 96, 18.0, 1.7, 1.1, 30,
         n=6)

    cust = coll("Customers", root)
    for i, (tt, ckey, icon) in enumerate(((-74, "shop_a", "coin"),
                                          (0, "shop_b", "gem"),
                                          (74, "shop_c", "coin"))):
        ss = s + 132
        terrace("Cust_%02d_Rock" % (i + 1), cust, "rock", ss, tt, 6, 30, 32,
                n=11, seed=400 + i)
        terrace("Cust_%02d_Cap" % (i + 1), cust, "sand", ss, tt, 8, 27, 29,
                taper=0.985, n=11, seed=400 + i)
        shop("Cust_%02d" % (i + 1), cust, ss, tt, 8, ckey)
        bubble("Cust_%02d_Bubble" % (i + 1), cust, ss - 30, tt, 44, icon)
        trees("Cust_%02d_Trees" % (i + 1), cust,
              rim_cluster(ss, tt, 30, 32, rng, 5, 1 if i != 2 else -1, 8.0,
                          spread=0.5), rng)

    ribbon("Flow_To_Customers", cust, "flow",
           [(s + 96, 0), (s + 116, 0)], 9.0, lift=3.8, z=8.0)
    ribbon("Flow_Customer_Row", cust, "flow",
           [(s + 116, -74), (s + 116, 0), (s + 116, 74)], 9.0, lift=3.8, z=8.0)
    return c


def build_focus(root):
    """The halo and the upgrade badge the reference puts on the active
    station."""
    c = coll("Focus", root)
    s, z, bt = band("Cannon")
    annulus("Focus_Ring", c, "focus", s, bt - 2, z + 3.2, 52, 74, 6)
    box("Upgrade_Badge", c, "focus", s - 4, bt + 62, z + 54, 13, 13, 13)
    box("Upgrade_Badge_Arrow", c, "white", s - 4, bt + 62, z + 61, 6, 6, 7)
    return c


# --------------------------------------------------------------- anchors ----

def build_anchors(root):
    """The frozen contract. Codex binds gameplay to these names."""
    c = coll("Anchors", root)

    anchor("Mine_Output", c, 49, -10, 82)
    anchor("Train_Load", c, 84, -10, 74)
    anchor("Train_Unload", c, 144, -6, 66)
    anchor("Storage_Input", c, 99, 40, 68)
    anchor("Storage_Output", c, 134, 58, 68)
    anchor("Refinery_Input", c, 109, -6, 68)
    anchor("Refinery_Output", c, 141, -34, 68)

    for key, t, _ in STATIONS:
        s, z, bt = band(key)
        t += bt
        anchor("Station_%s_Input" % key, c, s - 30, t - 22, z + 13)
        anchor("Station_%s_Work" % key, c, s, t, z + 4)
        anchor("Station_%s_Output" % key, c, s + 30, t - 24, z + 11)
        anchor("Station_%s_Upgrade" % key, c, s, t, z + 44, kind='SPHERE')
        anchor("Station_%s_Worker" % key, c, s - 6, t + 34, z + 4)

    sd, zd, bt = band("Dock")
    for i, tt in enumerate((-74, 0, 74)):
        anchor("Customer_Berth_%02d" % (i + 1), c, sd + 132, tt, 8,
               kind='ARROWS')
    anchor("Player_Outfitting", c, sd + 86, bt - 96, 2, kind='ARROWS')
    anchor("Set_Sail", c, sd + 86, bt - 134, 2, size=10, kind='SPHERE')

    stops = coll("Camera_Stops", root)
    for i, (key, s, z, _hs, _ht, t, _n) in enumerate(BANDS):
        anchor("Camera_Stop_%02d" % (i + 1), stops, s, t, z + 10, size=12,
               kind='CUBE')

    b = anchor("Camera_Bounds", c, 340, 0, 40, kind='CUBE')
    b.empty_display_size = 1.0
    b.scale = (VIEW_W * 0.5, 420.0 * S_STRETCH, 120.0)
    b.rotation_euler = (0.0, 0.0, math.radians(-45.0))
    return c


# --------------------------------------------------------- cameras / lit ----

def _place(cam, s, t, z, scale):
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
    for name, s, z, scale in (("Cam_Overview", 293, 40, 850.0),
                              ("Cam_Concept", 285, 36, 800.0),
                              ("Cam_Play_Cannon", 159, 46, VIEW_H)):
        cd = bpy.data.cameras.new(name)
        ob = bpy.data.objects.new(name, cd)
        c.objects.link(ob)
        _place(ob, s, 0, z, scale)

    sun = bpy.data.lights.new("Sun", 'SUN')
    sun.energy = 3.1
    sun.angle = math.radians(3.0)
    so = bpy.data.objects.new("Sun", sun)
    so.rotation_euler = (math.radians(52.0), math.radians(4.0),
                         math.radians(128.0))
    c.objects.link(so)

    sc = bpy.context.scene
    sc.camera = bpy.data.objects["Cam_Play_Cannon"]
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
        bg.inputs[0].default_value = (0.09, 0.20, 0.38, 1.0)
        bg.inputs[1].default_value = 0.30
    sc.world = world

    ev = getattr(sc, "eevee", None)
    if ev:
        for attr, val in (("use_raytracing", True), ("use_gtao", True),
                          ("gtao_distance", 24.0), ("use_shadows", True),
                          ("shadow_ray_count", 2)):
            if hasattr(ev, attr):
                setattr(ev, attr, val)
    try:
        sc.view_settings.view_transform = 'Standard'
        sc.view_settings.look = 'None'
    except TypeError:
        pass
    return c


# ------------------------------------------------------------------ main ----

def set_locked(locked=True):
    """Start state: Cannon built, the other four still pads."""
    for key, _, _ in STATIONS:
        built = key == "Cannon" or not locked
        for suffix, on in (("_Built", built), ("_Pad", not built)):
            cl = bpy.data.collections.get("Station_%s%s" % (key, suffix))
            if not cl:
                continue
            for ob in cl.objects:
                ob.hide_viewport = not on
                ob.hide_render = not on


MANIFEST = ("/Users/macbookair/Documents/GitHub/project_kayseri/"
            "Tools/blender/shipyard/anchors.json")


def export_manifest(path=MANIFEST):
    import json
    out = {
        "frame": {
            "k": K, "s_stretch": S_STRETCH, "band": BAND,
            "view_w": VIEW_W, "view_h": VIEW_H,
            "camera_euler_deg": [42.0, 0.0, 45.0], "camera_ortho": True,
            "screen_x": "0.7071*(x+y)",
            "screen_y": "0.5254*(y-x) + 0.669*z",
        },
        "bands": [{"key": b[0], "s": b[1], "z": b[2], "t": b[5]}
                  for b in BANDS],
        "stations": [{"key": k, "t": t, "colour": c} for k, t, c in STATIONS],
        "anchors": {},
    }
    for cname in ("Anchors", "Camera_Stops"):
        cl = bpy.data.collections.get(cname)
        if not cl:
            continue
        for ob in cl.objects:
            # ob.location, not matrix_world: matrix_world is stale until the
            # depsgraph re-evaluates, and these empties are unparented.
            loc = ob.location
            out["anchors"][ob.name] = {
                "pos": [round(loc.x, 4), round(loc.y, 4), round(loc.z, 4)],
                "collection": cname,
            }
    with open(path, "w") as fh:
        json.dump(out, fh, indent=2, sort_keys=True)
    print("anchor manifest: %d anchors -> %s" % (len(out["anchors"]), path))
    return path


def build():
    wipe()
    root = bpy.data.collections.new("SHIPYARD")
    bpy.context.scene.collection.children.link(root)

    build_terrain(root)
    build_vegetation(root)
    build_logistics(root)
    build_source(root)
    for key, t, ckey in STATIONS:
        build_station(root, key, t, ckey)
    build_dock(root)
    build_focus(root)
    build_anchors(root)
    build_cameras(root)
    set_locked(True)

    n = sum(len(c.objects) for c in bpy.data.collections)
    print("shipyard: %d objects, %d bands of %d, view %dx%d"
          % (n, len(BANDS), int(BAND), int(VIEW_W), int(VIEW_H)))
    return root


if __name__ == "__main__":
    build()
