"""Reusable asset builders.  Each returns a single multi-material object that
can be cheaply linked-duplicated with lib.dup().  Scale is roughly 1 unit = 1 m.
"""
from lib import B, dup, mat, RNG, coll, rough_verts
from math import radians, sin, cos, pi, hypot, atan2

# ------------------------------------------------------------------- vehicles
def truck(name, body="orange", load=None, C=None, trailer=True):
    """Articulated hauler ~13 long.

    load: 'coal' | 'skip' | 'cargo' | 'tank' | None

    'skip' is 'coal' without the coal - a high-sided tipper body that reads as an
    ore truck whether or not it is carrying anything. The gameplay layer parents
    its own load block to these and toggles it, so a body modelled with cargo
    baked in can never look empty.
    """
    b = B().use("steel_dk")
    WB = 1.55
    # chassis
    b.box((12.6, 2.5, 0.5), (0, 0, 0.95))
    # wheels
    b.use("rock_dark")
    for x, ys in ((4.6, (1,)), (2.9, (1,)), (-2.4, (1,)), (-3.9, (1,)),
                  (-5.3, (1,))):
        for s in (1, -1):
            b.cyl(1.05, 0.75, (x, s * WB, 1.05), (radians(90), 0, 0), 12)
    # cab
    b.use(body)
    b.box((3.5, 3.0, 2.6), (4.3, 0, 2.85))
    b.box((3.0, 3.05, 0.75), (4.2, 0, 4.5))
    b.use("glass")
    b.box((0.22, 2.6, 1.35), (5.95, 0, 3.55))
    for s in (1, -1):
        b.box((2.2, 0.2, 1.2), (4.3, s * 1.5, 3.6))
    b.use("chrome")
    b.box((0.5, 2.9, 0.9), (6.2, 0, 1.75))
    b.cyl(0.24, 2.6, (2.7, 1.35, 3.6), (0, 0, 0), 8)
    b.use("headlight")
    for s in (1, -1):
        b.box((0.22, 0.7, 0.45), (6.4, s * 0.95, 1.95))

    if trailer:
        if load in ("coal", "skip"):
            # Open-topped: floor and four walls, not a solid block. The gameplay
            # layer drops its ore block INTO this, and a closed box hid it - so a
            # loaded truck and an empty one looked identical.
            b.use("steel")
            b.box((8.6, 3.3, 0.4), (-2.2, 0, 1.95))
            for s in (1, -1):
                b.box((8.6, 0.3, 2.3), (-2.2, s * 1.5, 3.1))
                b.box((0.3, 3.3, 2.3), (-2.2 + s * 4.15, 0, 3.1))
            if load == "coal":
                b.use("coal")
                for i in range(9):
                    x = -6.0 + i * 0.95
                    b.sphere(1.35, (x, RNG.uniform(-0.35, 0.35), 4.35), 1,
                             scale=(1.0, 1.05, 0.42))
        elif load == "cargo":
            b.use("offwhite")
            b.box((9.2, 3.4, 3.6), (-2.4, 0, 3.7))
            b.use("steel_dk")
            b.box((0.25, 3.5, 3.5), (-7.05, 0, 3.7))
            for i in range(6):
                b.box((0.16, 3.5, 3.4), (-6.4 + i * 1.5, 0, 3.75))
        elif load == "tank":
            b.use("steel_lt")
            b.cyl(1.8, 9.0, (-2.3, 0, 3.6), (0, radians(90), 0), 14)
            b.use("steel_dk")
            b.box((9.0, 0.5, 0.4), (-2.3, 0, 5.5))
        else:
            b.use("steel")
            b.box((8.6, 3.3, 0.55), (-2.2, 0, 2.05))
    b.use("taillight")
    for s in (1, -1):
        b.box((0.18, 0.6, 0.4), (-6.9, s * 1.2, 2.1))
    # Built oversize for easy authoring, then scaled down so a hauler reads at a
    # believable size against a 13-wide carriageway.
    return b.make(name, collection=C, scale=(0.76, 0.76, 0.76))


def van(name, body="white", C=None):
    b = B().use(body)
    b.box((6.4, 2.6, 2.9), (0, 0, 2.35))
    b.box((2.2, 2.5, 1.5), (2.9, 0, 1.65))
    b.use("glass")
    b.box((0.2, 2.2, 1.0), (4.05, 0, 2.5))
    b.use("rock_dark")
    for x in (2.3, -2.1):
        for s in (1, -1):
            b.cyl(0.78, 0.55, (x, s * 1.32, 0.78), (radians(90), 0, 0), 10)
    return b.make(name, collection=C, scale=(0.86, 0.86, 0.86))


def loader(name, C=None):
    """Yellow wheel loader."""
    b = B().use("yellow")
    b.box((4.2, 3.0, 2.0), (-1.0, 0, 2.5))
    b.box((3.0, 2.8, 1.5), (1.6, 0, 2.3))
    b.use("glass")
    b.box((2.0, 2.4, 1.6), (-1.2, 0, 4.3))
    b.use("rock_dark")
    for x in (1.9, -2.0):
        for s in (1, -1):
            b.cyl(1.35, 0.9, (x, s * 1.6, 1.35), (radians(90), 0, 0), 12)
    b.use("steel_dk")
    for s in (1, -1):
        b.box((4.4, 0.3, 0.5), (3.4, s * 1.2, 2.6), (0, radians(-18), 0))
    b.use("steel")
    b.box((1.9, 3.4, 1.5), (5.6, 0, 1.05), (0, radians(12), 0))
    return b.make(name, collection=C)


def excavator(name, C=None):
    b = B().use("rock_dark")
    b.box((5.4, 3.4, 1.1), (0, 0, 0.75))
    for s in (1, -1):
        b.box((5.6, 0.9, 1.5), (0, s * 1.5, 0.9))
    b.use("yellow")
    b.box((4.0, 3.0, 2.2), (-0.6, 0, 2.5))
    b.use("glass")
    b.box((1.7, 1.9, 1.9), (1.0, 0.5, 3.6))
    b.use("yellow_lt")
    b.box((6.2, 0.8, 0.9), (3.4, 0, 4.6), (0, radians(-26), 0))
    b.use("steel_dk")
    b.box((4.6, 0.7, 0.75), (7.4, 0, 3.0), (0, radians(38), 0))
    b.use("steel")
    b.box((1.7, 1.9, 1.6), (9.0, 0, 1.1))
    return b.make(name, collection=C)


def forklift(name, C=None):
    b = B().use("yellow")
    b.box((2.6, 1.8, 1.5), (-0.3, 0, 1.15))
    b.use("steel_dk")
    b.box((0.25, 1.7, 3.2), (1.2, 0, 1.9))
    b.use("steel_lt")
    for s in (1, -1):
        b.box((1.1, 0.18, 0.14), (1.9, s * 0.55, 0.35))
    b.use("rock_dark")
    for x, r in ((0.7, 0.52), (-1.2, 0.4)):
        for s in (1, -1):
            b.cyl(r, 0.35, (x, s * 0.8, r), (radians(90), 0, 0), 8)
    return b.make(name, collection=C)


# ------------------------------------------------------------- rolling stock
def locomotive(name, C=None):
    b = B().use("red")
    b.box((13.0, 3.4, 3.4), (0, 0, 3.4))
    b.box((4.2, 3.5, 1.6), (-4.2, 0, 5.7))
    b.use("steel_dk")
    b.box((13.6, 3.6, 0.7), (0, 0, 1.5))
    b.use("glass")
    b.box((0.2, 2.9, 1.3), (6.55, 0, 4.5))
    b.box((3.6, 0.2, 1.2), (-4.2, 1.72, 5.75))
    b.box((3.6, 0.2, 1.2), (-4.2, -1.72, 5.75))
    b.use("rock_dark")
    for x in (5.0, 3.2, -3.2, -5.0):
        for s in (1, -1):
            b.cyl(0.85, 0.5, (x, s * 1.5, 0.95), (radians(90), 0, 0), 10)
    b.use("yellow_lt")
    b.box((0.3, 3.0, 0.5), (6.7, 0, 2.6))
    b.use("headlight")
    b.box((0.2, 0.8, 0.6), (6.75, 0, 5.2))
    return b.make(name, collection=C)


def wagon(name, loaded=True, C=None):
    b = B().use("rust")
    b.box((10.0, 3.4, 2.6), (0, 0, 3.3))
    b.use("steel_dk")
    b.box((10.4, 3.6, 0.6), (0, 0, 1.6))
    for i in range(5):
        b.box((0.28, 3.7, 2.4), (-4.0 + i * 2.0, 0, 3.3))
    b.use("rock_dark")
    for x in (3.4, -3.4):
        for s in (1, -1):
            b.cyl(0.8, 0.45, (x, s * 1.5, 0.9), (radians(90), 0, 0), 10)
    if loaded:
        b.use("coal")
        for i in range(11):
            b.sphere(1.5, (-4.3 + i * 0.86, RNG.uniform(-0.4, 0.4), 4.7), 1,
                     scale=(1.0, 1.0, 0.40))
    return b.make(name, collection=C)


# ------------------------------------------------------------------- foliage
def pine(name, h=13.0, r=3.2, C=None, tiers=4, m="pine"):
    b = B().use("trunk")
    b.cylz(r * 0.13, h * 0.34, (0, 0, 0), seg=6)
    b.use(m)
    for i in range(tiers):
        f = i / max(1, tiers - 1.0)
        z = h * (0.22 + 0.60 * f)
        rr = r * (1.0 - 0.62 * f)
        b.conez(rr, rr * 0.16, h * 0.34, (0, 0, z), seg=9)
    return b.make(name, collection=C, smooth=False)


def bush(name, r=1.6, C=None):
    b = B().use("bush")
    for i in range(3):
        b.sphere(r * RNG.uniform(0.6, 1.0),
                 (RNG.uniform(-r, r) * 0.5, RNG.uniform(-r, r) * 0.5,
                  r * RNG.uniform(0.4, 0.7)), 1, scale=(1, 1, 0.75))
    return b.make(name, collection=C, smooth=True)


# --------------------------------------------------------------- street props
def streetlight(name, h=9.0, arm=3.0, C=None):
    b = B().use("steel")
    b.cylz(0.18, h, (0, 0, 0), seg=8)
    b.box((arm, 0.22, 0.22), (arm * 0.5, 0, h))
    b.use("lamp_glow")
    b.box((1.5, 0.6, 0.28), (arm * 0.92, 0, h - 0.25))
    return b.make(name, collection=C)


def pylon(name, h=26.0, C=None):
    b = B().use("steel")
    for s in (1, -1):
        for t in (1, -1):
            b.tube(0.22, [(s * 2.6, t * 2.6, 0), (s * 0.9, t * 0.9, h)], 5)
    for i in range(6):
        f = i / 5.0
        w = 2.6 - 1.7 * f
        z = h * f * 0.92
        for s in (1, -1):
            b.tube(0.16, [(s * w, -w, z), (s * w, w, z)], 4)
            b.tube(0.16, [(-w, s * w, z), (w, s * w, z)], 4)
    for i, (z, aw) in enumerate(((h * 0.72, 8.0), (h * 0.86, 6.6), (h, 5.0))):
        b.tube(0.18, [(-aw, 0, z), (aw, 0, z)], 5)
        for s in (1, -1):
            b.tube(0.12, [(s * aw, 0, z), (s * 1.2, 0, z - 3.0)], 4)
    return b.make(name, collection=C)


def fence_run(pts, name, C=None, h=2.4, post=2.6, m="steel_dk"):
    """Chain-link style fence following a list of (x,y,z) points."""
    from lib import sample_bez, scatter_along
    b = B().use(m)
    for pos, yaw in scatter_along(pts, post):
        b.boxz((0.18, 0.18, h), (pos.x, pos.y, pos.z))
    samples = sample_bez(pts, max(8, len(pts) * 14))
    for i in range(len(samples) - 1):
        a, c = samples[i][0], samples[i + 1][0]
        for zz in (h * 0.95, h * 0.55, h * 0.15):
            b.tube(0.055, [(a.x, a.y, a.z + zz), (c.x, c.y, c.z + zz)], 4)
    return b.make(name, collection=C)


def guardrail(pts, name, C=None, h=1.3):
    from lib import sample_bez, scatter_along
    b = B().use("steel_lt")
    for pos, yaw in scatter_along(pts, 4.0):
        b.boxz((0.2, 0.2, h), (pos.x, pos.y, pos.z))
    samples = sample_bez(pts, max(8, len(pts) * 12))
    for i in range(len(samples) - 1):
        a, c = samples[i][0], samples[i + 1][0]
        b.tube(0.16, [(a.x, a.y, a.z + h), (c.x, c.y, c.z + h)], 4)
    return b.make(name, collection=C)


# -------------------------------------------------------------- industry bits
def silo(name, r=4.0, h=18.0, C=None, m="steel_lt", cone_top=True):
    b = B().use("concrete_dk")
    b.cylz(r * 1.12, 1.0, (0, 0, 0), seg=20)
    b.use(m)
    b.cylz(r, h, (0, 0, 1.0), seg=20)
    for i in range(3):
        b.cyl(r * 1.03, 0.3, (0, 0, 1.0 + h * (0.25 + i * 0.25)), seg=20)
    if cone_top:
        b.use("steel_dk")
        b.conez(r * 1.05, r * 0.18, r * 0.75, (0, 0, 1.0 + h), seg=20)
    b.use("steel")
    for i in range(int(h / 1.6)):
        b.box((0.5, 0.09, 0.09), (r + 0.3, 0, 1.6 + i * 1.6))
    return b.make(name, collection=C, smooth=False)


def tank(name, r=6.0, h=7.0, C=None, m="steel_lt", band="red"):
    b = B().use("concrete_dk")
    b.cylz(r * 1.15, 0.8, (0, 0, 0), seg=24)
    b.use(m)
    b.cylz(r, h, (0, 0, 0.8), seg=24)
    b.use(band)
    b.cyl(r * 1.02, h * 0.14, (0, 0, 0.8 + h * 0.62), seg=24)
    b.use("steel_dk")
    b.conez(r * 1.04, r * 0.3, r * 0.34, (0, 0, 0.8 + h), seg=24)
    b.use("steel")
    b.tube(0.13, [(r, 0, 0.9), (r + 0.9, 0, 0.9), (r + 0.9, 0, h * 0.9)], 5)
    return b.make(name, collection=C, smooth=False)


def column(name, r=3.0, h=26.0, C=None, m="steel_lt"):
    """Refinery distillation column."""
    b = B().use("concrete_dk")
    b.cylz(r * 1.3, 1.2, (0, 0, 0), seg=18)
    b.use(m)
    b.cylz(r, h, (0, 0, 1.2), seg=18)
    b.use("steel_dk")
    b.sphere(r, (0, 0, 1.2 + h), 2, scale=(1, 1, 0.45))
    for i in range(4):
        b.cyl(r * 1.06, 0.22, (0, 0, 2.0 + h * (0.18 + i * 0.21)), seg=18)
    b.use("steel")
    # external stair spiral
    for i in range(int(h / 1.3)):
        a = i * 0.55
        b.box((1.5, 0.5, 0.1),
              ((r + 0.75) * cos(a), (r + 0.75) * sin(a), 1.8 + i * 1.3),
              (0, 0, a))
    return b.make(name, collection=C, smooth=False)


def stack(name, r=2.2, h=32.0, C=None, banded=True):
    b = B().use("concrete_dk")
    b.cylz(r * 1.5, 1.6, (0, 0, 0), seg=16)
    b.use("white")
    b.conez(r * 1.15, r * 0.82, h, (0, 0, 1.6), seg=16)
    if banded:
        b.use("red")
        for i in range(3):
            f = 0.30 + i * 0.22
            rr = r * (1.15 - 0.33 * f)
            b.cyl(rr * 1.03, h * 0.085, (0, 0, 1.6 + h * f), seg=16)
    b.use("steel_dk")
    b.cyl(r * 0.88, 0.6, (0, 0, 1.6 + h), seg=16)
    return b.make(name, collection=C, smooth=False)


def conveyor(a, b_, name, C=None, w=2.6, legs=True, m="steel"):
    """Enclosed belt bridge from a to b_ (3D points), with support legs."""
    from mathutils import Vector
    A, Bv = Vector(a), Vector(b_)
    d = Bv - A
    ln = d.length
    yaw = atan2(d.y, d.x)
    pitch = -atan2(d.z, hypot(d.x, d.y))
    mid = (A + Bv) * 0.5
    b = B().use(m)
    b.box((ln, w, 0.5), tuple(mid), (0, pitch, yaw))
    b.use("steel_dk")
    b.box((ln, w * 0.82, 1.5), (mid.x, mid.y, mid.z + 1.1), (0, pitch, yaw))
    b.use(m)
    n = max(2, int(ln / 3.2))
    for i in range(n + 1):
        p = A + d * (i / n)
        b.box((0.22, w + 0.5, 0.22), tuple(p), (0, pitch, yaw))
    if legs:
        b.use("steel_dk")
        nl = max(1, int(ln / 11.0))
        for i in range(1, nl + 1):
            p = A + d * (i / (nl + 1.0))
            if p.z < 1.5:
                continue
            for s in (1, -1):
                b.tube(0.24, [(p.x + sin(yaw) * s * w * 0.5,
                               p.y - cos(yaw) * s * w * 0.5, 0.0),
                              (p.x, p.y, p.z - 0.3)], 5)
    return b.make(name, collection=C)


def gantry(name, span=26.0, h=13.0, C=None, m="yellow"):
    """Portal gantry crane straddling a yard."""
    b = B().use(m)
    for s in (1, -1):
        for t in (1, -1):
            b.tube(0.34, [(t * 1.6, s * span * 0.5, 0),
                          (t * 0.8, s * span * 0.5 * 0.88, h)], 6)
        b.tube(0.3, [(0, s * span * 0.5, h * 0.45),
                     (0, s * span * 0.5 * 0.94, h * 0.45)], 5)
    b.box((2.4, span, 1.5), (0, 0, h + 0.4))
    b.use("steel_dk")
    b.box((3.0, 4.0, 1.8), (0, -span * 0.14, h - 0.9))
    b.use("steel")
    b.tube(0.09, [(0, -span * 0.14, h - 1.7), (0, -span * 0.14, 3.0)], 4)
    b.use("steel_dk")
    b.box((3.4, 3.0, 1.2), (0, -span * 0.14, 2.4))
    return b.make(name, collection=C)


def tower_crane(name, h=34.0, jib=26.0, C=None):
    b = B().use("yellow")
    for s in (1, -1):
        for t in (1, -1):
            b.tube(0.2, [(s * 1.5, t * 1.5, 0), (s * 1.5, t * 1.5, h)], 5)
    for i in range(int(h / 3.0)):
        z = i * 3.0
        for s in (1, -1):
            b.tube(0.13, [(s * 1.5, -1.5, z), (s * 1.5, 1.5, z)], 4)
            b.tube(0.13, [(-1.5, s * 1.5, z), (1.5, s * 1.5, z)], 4)
    b.box((3.6, 3.6, 2.2), (0, 0, h + 1.1))
    b.use("yellow_lt")
    b.box((jib, 1.6, 1.4), (jib * 0.35, 0, h + 3.0))
    b.box((jib * 0.32, 1.6, 1.4), (-jib * 0.22, 0, h + 3.0))
    b.use("steel_dk")
    b.tube(0.08, [(jib * 0.62, 0, h + 2.4), (jib * 0.62, 0, h - 7.0)], 4)
    b.box((1.6, 1.6, 1.2), (jib * 0.62, 0, h - 7.6))
    return b.make(name, collection=C)


def hopper(name, r=5.0, h=9.0, C=None):
    """Elevated load-out hopper on legs."""
    b = B().use("steel")
    for a in range(4):
        an = radians(45 + a * 90)
        b.tube(0.3, [(cos(an) * r * 0.8, sin(an) * r * 0.8, 0),
                     (cos(an) * r * 0.55, sin(an) * r * 0.55, h * 0.55)], 5)
    b.use("steel_dk")
    b.conez(r * 0.22, r, h * 0.5, (0, 0, h * 0.5), seg=14)
    b.use("steel_lt")
    b.cylz(r, h * 0.42, (0, 0, h), seg=14)
    return b.make(name, collection=C)


def coal_pile(name, r=13.0, h=8.0, C=None, seed=1.0):
    b = B().use("coal")
    b.conez(r, r * 0.10, h, (0, 0, 0), seg=22)
    o = b.make(name, collection=C, smooth=False)
    rough_verts(o, amount=r * 0.075, scale=0.09, seed=seed)
    return o


def warehouse(name, w=30.0, d=18.0, h=9.0, C=None, body="offwhite",
              roof="roof_grey", curved=False, doors=3):
    b = B().use("concrete_dk")
    b.boxz((w + 1.0, d + 1.0, 0.5), (0, 0, 0))
    b.use(body)
    b.boxz((w, d, h), (0, 0, 0.5))
    if curved:
        # Barrel roof: full-width cylinder squashed in Z, its lower half buried
        # inside the walls so only the arc shows.
        b.use(roof)
        b.cyl(d * 0.5, w + 0.6, (0, 0, h + 0.5), (0, radians(90), 0), 20,
              scale=(0.60, 1, 1))
        b.use(body)                     # gable end walls, else the barrel reads
        for s in (1, -1):               # as a big blank disc facing camera
            b.cyl(d * 0.49, 0.5, (s * (w * 0.5 + 0.32), 0, h + 0.5),
                  (0, radians(90), 0), 20, scale=(0.60, 1, 1))
    else:
        b.use(roof)
        b.roof((w + 1.2, d + 1.2, d * 0.22), (0, 0, h + 0.5), (0, 0, radians(90)))
    b.use("steel_dk")
    for i in range(doors):
        x = -w * 0.5 + w * (i + 0.5) / doors
        b.box((w / doors * 0.55, 0.25, h * 0.62), (x, d * 0.5, 0.5 + h * 0.31))
    b.use("winlight")
    for i in range(int(w / 5.0)):
        b.box((2.2, 0.2, 1.1), (-w * 0.5 + 3.0 + i * 5.0, -d * 0.5, h * 0.72))
    return b.make(name, collection=C)


def shop(name, w=13.0, d=10.0, h=6.0, C=None, body="offwhite", roof="roof_red",
         awning=None):
    b = B().use(body)
    b.boxz((w, d, h), (0, 0, 0))
    b.use(roof)
    b.boxz((w + 1.0, d + 1.0, 0.7), (0, 0, h))
    b.use("glass")
    b.box((w * 0.74, 0.2, h * 0.42), (0, d * 0.5, h * 0.4))
    if awning:
        b.use(awning)
        b.box((w * 0.86, 2.6, 0.22), (0, d * 0.5 + 1.3, h * 0.66),
              (radians(-12), 0, 0))
    b.use("winlight")
    b.box((w * 0.7, 0.1, h * 0.34), (0, d * 0.5 - 0.15, h * 0.4))
    return b.make(name, collection=C)


def office(name, w=14.0, d=12.0, floors=3, C=None, body="offwhite"):
    fh = 3.4
    b = B().use(body)
    b.boxz((w, d, fh * floors), (0, 0, 0))
    b.use("glass")
    for f in range(floors):
        z = fh * (f + 0.55)
        b.box((w * 0.82, d + 0.18, fh * 0.44), (0, 0, z))
        b.box((w + 0.18, d * 0.82, fh * 0.44), (0, 0, z))
    b.use("roof_grey")
    b.boxz((w + 0.8, d + 0.8, 0.5), (0, 0, fh * floors))
    b.use("steel_dk")
    b.boxz((3.0, 2.4, 1.6), (w * 0.2, 0, fh * floors + 0.5))
    return b.make(name, collection=C)


def pipe_rack(pts, name, C=None, n=4, r=0.36, z0=2.6, dz=0.9):
    """Parallel pipe run with support bents."""
    from mathutils import Vector
    b = B().use("steel_lt")
    P = [Vector(p) for p in pts]
    for i in range(n):
        off = (i - (n - 1) * 0.5) * 1.05
        run = []
        for p in P:
            d = None
            run.append((p.x, p.y + off, p.z + z0 + (i % 2) * dz * 0.35))
        b.tube(r, run, 8)
    b.use("steel_dk")
    for k in range(0, len(P) - 1):
        a, c = P[k], P[k + 1]
        steps = max(1, int((c - a).length / 7.0))
        for s in range(steps):
            p = a + (c - a) * (s / steps)
            b.box((0.4, n * 1.2, 0.35), (p.x, p.y, p.z + z0 + 0.6))
            for sgn in (1, -1):
                b.boxz((0.4, 0.4, z0 + 0.6),
                       (p.x, p.y + sgn * n * 0.55, p.z))
    return b.make(name, collection=C)


def container(name, C=None, col="blue"):
    b = B().use(col)
    b.boxz((7.0, 3.0, 3.0), (0, 0, 0))
    b.use("steel_dk")
    for i in range(9):
        b.box((0.12, 3.06, 2.8), (-3.0 + i * 0.75, 0, 1.5))
    return b.make(name, collection=C)


def crate_stack(name, C=None):
    b = B().use("wood_lt")
    n = RNG.randint(2, 4)
    for i in range(n):
        s = RNG.uniform(1.5, 2.2)
        b.boxz((s, s, s * 0.85),
               (RNG.uniform(-0.5, 0.5), RNG.uniform(-0.5, 0.5), i * s * 0.85))
    return b.make(name, collection=C)


# --------------------------------------------------------------------- marine
def ship(name, L_=60.0, C=None, hull="red", house="white", funnel="orange",
         crates=True, kind="cargo"):
    """Cargo ship, bow at local +X, waterline at local z=0."""
    W = L_ * 0.20
    H = L_ * 0.125
    b = B()
    # tapered hull, built from slices so bow and stern narrow off
    SLICES = [(-0.50, 0.30), (-0.44, 0.62), (-0.34, 0.86), (-0.18, 0.99),
              (0.02, 1.00), (0.20, 0.96), (0.33, 0.82), (0.42, 0.58),
              (0.48, 0.28)]
    b.use(hull)
    for i in range(len(SLICES) - 1):
        f0, w0 = SLICES[i]
        f1, w1 = SLICES[i + 1]
        fm = (f0 + f1) * 0.5
        wm = (w0 + w1) * 0.5
        # centred so roughly a third of the hull sits above the waterline
        b.box((L_ * (f1 - f0) * 1.06, W * wm, H), (L_ * fm, 0, -H * 0.17))
    b.use("steel_dk")                       # boot-top stripe at the waterline
    for i in range(len(SLICES) - 1):
        f0, w0 = SLICES[i]
        f1, w1 = SLICES[i + 1]
        fm, wm = (f0 + f1) * 0.5, (w0 + w1) * 0.5
        b.box((L_ * (f1 - f0) * 1.07, W * wm * 1.02, H * 0.16),
              (L_ * fm, 0, -H * 0.04))
    b.use("offwhite")                       # main deck
    for i in range(len(SLICES) - 1):
        f0, w0 = SLICES[i]
        f1, w1 = SLICES[i + 1]
        fm, wm = (f0 + f1) * 0.5, (w0 + w1) * 0.5
        b.box((L_ * (f1 - f0) * 1.04, W * wm * 0.96, 0.5),
              (L_ * fm, 0, H * 0.33))
    # superstructure aft
    sx = -L_ * 0.34
    b.use(house)
    b.boxz((L_ * 0.15, W * 0.72, H * 0.62), (sx, 0, H * 0.24))
    b.boxz((L_ * 0.11, W * 0.60, H * 0.42), (sx, 0, H * 0.86))
    b.use("glass")
    b.box((L_ * 0.115, W * 0.62, H * 0.20), (sx, 0, H * 1.10))
    b.use(funnel)
    b.cylz(W * 0.16, H * 0.46, (sx - L_ * 0.045, 0, H * 1.28), seg=12)
    b.use("steel_dk")
    b.cyl(W * 0.17, H * 0.10, (sx - L_ * 0.045, 0, H * 1.70), seg=12)
    # masts + rails
    b.use("steel_lt")
    b.cylz(0.32, H * 0.9, (L_ * 0.30, 0, H * 0.24), seg=8)
    b.cylz(0.32, H * 0.7, (sx + L_ * 0.10, 0, H * 0.24), seg=8)
    for s in (1, -1):
        b.box((L_ * 0.62, 0.16, 0.16), (L_ * 0.05, s * W * 0.46, H * 0.62))
    if crates:
        cols = ("blue", "red", "teal", "orange", "green_ind", "yellow_lt")
        n = 0
        for i in range(6):
            for j in range(3):
                for k in range(2):
                    if RNG.random() < 0.22:
                        continue
                    b.use(cols[(i + j + k) % len(cols)])
                    b.boxz((L_ * 0.085, W * 0.22, H * 0.26),
                           (L_ * (0.24 - i * 0.095), (j - 1) * W * 0.25,
                            H * 0.26 + k * H * 0.26))
                    n += 1
    return b.make(name, collection=C)


def tug(name, L_=18.0, C=None, hull="red"):
    b = B().use(hull)
    b.box((L_ * 0.86, L_ * 0.30, L_ * 0.20), (0, 0, -L_ * 0.05))
    b.box((L_ * 0.30, L_ * 0.24, L_ * 0.18), (L_ * 0.42, 0, -L_ * 0.04))
    b.use("offwhite")
    b.boxz((L_ * 0.34, L_ * 0.26, L_ * 0.22), (-L_ * 0.06, 0, L_ * 0.06))
    b.use("glass")
    b.box((L_ * 0.35, L_ * 0.27, L_ * 0.09), (-L_ * 0.06, 0, L_ * 0.20))
    b.use("steel_dk")
    b.cylz(L_ * 0.05, L_ * 0.18, (-L_ * 0.20, 0, L_ * 0.26), seg=8)
    return b.make(name, collection=C)


def port_crane(name, h=26.0, reach=30.0, C=None, m="orange"):
    """Rail-mounted quayside container crane; boom runs along local +X."""
    b = B().use(m)
    for s in (1, -1):
        for t in (1, -1):
            b.tube(0.55, [(s * 5.0, t * 7.0, 0), (s * 3.0, t * 5.0, h)], 6)
    for i in range(int(h / 4.0)):
        z = 3.0 + i * 4.0
        for s in (1, -1):
            b.tube(0.3, [(s * 4.2, -6.0, z), (s * 4.2, 6.0, z)], 4)
    b.box((10.0, 14.0, 1.8), (0, 0, h + 0.9))
    # boom out over the water, plus counter-jib
    b.box((reach, 3.0, 1.6), (reach * 0.42, 0, h + 3.0))
    b.box((reach * 0.34, 3.0, 1.6), (-reach * 0.26, 0, h + 3.0))
    b.use("steel_dk")
    for f in (0.55, 0.30):
        b.tube(0.16, [(reach * f, 0, h + 2.2), (reach * f, 0, h - 6.0)], 4)
    b.box((3.4, 6.0, 1.6), (reach * 0.55, 0, h - 6.8))
    b.use("glass")
    b.box((3.0, 3.4, 2.0), (2.0, -5.0, h - 1.0))
    b.use("steel_lt")
    for s in (1, -1):
        b.box((12.0, 1.4, 0.7), (0, s * 7.0, 0.35))
    return b.make(name, collection=C)


def bollard(name, C=None):
    b = B().use("steel_dk")
    b.cylz(0.55, 1.4, (0, 0, 0), seg=10)
    b.sphere(0.62, (0, 0, 1.5), 1, scale=(1, 1, 0.6))
    return b.make(name, collection=C)


def wake(name, C=None, length=44.0, width=7.0, n=26):
    """Foam trail behind a moving ship (local -X is astern)."""
    b = B().use("foam")
    for i in range(n):
        f = i / (n - 1.0)
        sp = width * (0.35 + 1.5 * f)
        for s in (1, -1):
            b.sphere(1.1 + 2.2 * f,
                     (-length * f - 4.0, s * sp * 0.5 + RNG.uniform(-1, 1),
                      RNG.uniform(-0.3, 0.3)), 1, scale=(1.4, 1.0, 0.22))
    return b.make(name, collection=C, smooth=True)


def smoke_plume(name, C=None, r=2.2, n=7, rise=15.0, drift=5.0):
    b = B().use("smoke")
    for i in range(n):
        f = i / (n - 1.0)
        b.sphere(r * (0.55 + f * 1.5),
                 (drift * f * f, drift * 0.4 * f, rise * f), 1,
                 scale=(1.0, 1.0, 0.78))
    return b.make(name, collection=C, smooth=True)
