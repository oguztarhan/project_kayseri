"""Reusable asset builders.  Each returns a single multi-material object that
can be cheaply linked-duplicated with lib.dup().  Scale is roughly 1 unit = 1 m.
"""
from lib import B, dup, mat, RNG, coll, rough_verts
from math import radians, sin, cos, pi, hypot, atan2

# Ore is whatever the current island mines, so the same stockpile and tipper
# geometry reads as coal on one map and as malachite on the other. Looked up
# through the module rather than copied into a constant: every step reloads
# layout before it reloads parts, so L always points at the live island.
import layout as L


# --------------------------------------------------------------------- roofs
# A roof is the largest single surface on a building and the one this camera
# angle sees most of, so a bare coloured prism is the loudest toy signal on the
# island. These give it what a real roof has: a profile, an edge, and clutter.

def pitched(b, sx, sy, rise, z, body="roof_grey", trim="steel_dk", ribs=True):
    """A gable roof with a ridge cap, fascia, barge boards and standing seams.

    Rotated a quarter turn about Z, so the ridge runs along X with length `sy`
    and the gable spans Y with width `sx`. Note the order: because of that turn
    a building `w` wide in X and `d` deep in Y wants `pitched(b, d, w, ...)`,
    NOT `(w, d)`. Getting it the wrong way round is what put a 30x18 shed's
    roof on sideways - the ridge across the short axis and the eaves hanging
    six metres out over open air at each end.
    """
    b.use(body)
    b.roof((sx, sy, rise), (0, 0, z), (0, 0, radians(90)))
    hx, hy = sy * 0.5, sx * 0.5          # after the quarter turn
    if ribs:
        # Standing seams down both slopes, about one every 0.9 m along the ridge.
        n = max(2, int(sy / 0.9))
        for i in range(n + 1):
            x = -hx + i * (sy / n)
            for sgn in (1, -1):
                for k in range(3):
                    t = 0.20 + k * 0.28
                    b.box((0.07, hy * 0.26, 0.07),
                          (x, sgn * hy * (1.0 - t), z + rise * t))
    b.use(trim)
    b.box((sy + 0.10, 0.34, 0.16), (0, 0, z + rise + 0.02))          # ridge cap
    for sgn in (1, -1):                                              # fascia
        b.box((sy, 0.14, 0.34), (0, sgn * hy, z - 0.12))
    for sgn in (1, -1):                                              # barge board
        b.box((0.14, sx, 0.20), (sgn * hx, 0, z - 0.06))
    return b


def flat_roof(b, w, d, z, deck="roof_grey", trim="steel_dk", plant=2):
    """A flat roof with a parapet upstand and plant standing on it."""
    b.use(deck)
    b.box((w, d, 0.22), (0, 0, z))
    b.use(trim)
    for sgn in (1, -1):
        b.box((0.20, d, 0.62), (sgn * (w * 0.5 - 0.10), 0, z + 0.30))
        b.box((w, 0.20, 0.62), (0, sgn * (d * 0.5 - 0.10), z + 0.30))
    # Rooftop plant. Nothing says "occupied building" faster than clutter up
    # there, and it breaks the dead flat plane this camera looks straight down on.
    for i in range(max(0, plant)):
        ux = RNG.uniform(-0.26, 0.26) * w
        uy = RNG.uniform(-0.26, 0.26) * d
        uw = min(w * 0.30, 2.2 + RNG.uniform(0.0, 0.9))
        b.use("steel_lt")
        b.box((uw, uw * 0.72, 0.85), (ux, uy, z + 0.55))
        b.use("steel_dk")
        for k in range(3):
            b.box((uw * 0.86, 0.07, 0.07), (ux, uy, z + 0.80 + k * 0.10))
    b.use("steel_dk")
    for i in range(2):
        b.cylz(0.22, 0.9, (w * (0.30 - 0.60 * i), -d * 0.30, z + 0.11), seg=10)
    return b


# ------------------------------------------------------------------- vehicles
def wheel(b, x, y, z, r=1.05, width=0.75, axis="y"):
    """Tyre, rim and hub as three separate solids.

    One flat cylinder is the single loudest toy tell on a vehicle: real wheels
    read as a dark tyre with a lighter dished rim inside it and a hub cap in the
    middle, and the eye picks that up long before it picks up the body shape.
    """
    rot = (radians(90), 0, 0) if axis == "y" else (0, radians(90), 0)
    b.use("rock_dark")                                   # tyre
    b.cyl(r, width, (x, y, z), rot, 14)
    b.use("steel_dk")                                    # rim, inset both sides
    b.cyl(r * 0.62, width * 1.06, (x, y, z), rot, 12)
    b.use("steel_lt")                                    # hub
    b.cyl(r * 0.26, width * 1.14, (x, y, z), rot, 10)
    return b

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
    # Ladder chassis: two rails and cross members, not a slab. Visible between
    # the wheels from every angle this game is played at.
    for s in (1, -1):
        b.box((12.6, 0.34, 0.62), (0, s * 0.95, 0.95))
    for i in range(5):
        b.box((0.30, 1.9, 0.34), (-5.4 + i * 2.8, 0, 0.95))
    # wheels
    AXLES = (4.6, 2.9, -2.4, -3.9, -5.3)
    for x in AXLES:
        for s in (1, -1):
            wheel(b, x, s * WB, 1.05, 1.05, 0.75)
    # mudguards over the rear bogie
    b.use(body)
    for x in (-2.4, -3.9, -5.3):
        for s in (1, -1):
            b.box((1.9, 1.0, 0.16), (x, s * WB, 2.20))
    # fuel tank and air bottles slung under the rails
    b.use("chrome")
    b.cyl(0.52, 2.6, (0.4, -1.30, 1.30), (0, radians(90), 0), 12)
    b.use("steel_lt")
    b.cyl(0.30, 1.3, (0.2, 1.30, 1.35), (0, radians(90), 0), 10)
    # cab
    b.use(body)
    b.box((3.5, 3.0, 2.6), (4.3, 0, 2.85))
    b.box((3.0, 3.05, 0.75), (4.2, 0, 4.5))
    b.use("glass")
    b.box((0.22, 2.6, 1.35), (5.95, 0, 3.55))
    for s in (1, -1):
        b.box((2.2, 0.2, 1.2), (4.3, s * 1.5, 3.6))
    # door shut lines and a step under each door
    b.use("steel_dk")
    for s in (1, -1):
        b.box((0.07, 0.07, 2.2), (3.1, s * 1.52, 2.9))
        b.box((0.07, 0.07, 2.2), (5.5, s * 1.52, 2.9))
        b.box((0.9, 0.5, 0.10), (4.0, s * 1.62, 1.55))
    # mirrors on arms, both sides
    for s in (1, -1):
        b.box((0.09, 0.55, 0.09), (5.7, s * 1.85, 4.15))
        b.box((0.14, 0.30, 0.80), (5.7, s * 2.10, 3.85))
    b.use("chrome")
    b.box((0.5, 2.9, 0.9), (6.2, 0, 1.75))
    # radiator grille slats behind the bumper
    b.use("steel_dk")
    for k in range(5):
        b.box((0.12, 2.4, 0.12), (6.12, 0, 2.35 + k * 0.32))
    b.use("chrome")
    b.cyl(0.24, 2.6, (2.7, 1.35, 3.6), (0, 0, 0), 8)
    b.use("headlight")
    for s in (1, -1):
        b.box((0.22, 0.7, 0.45), (6.4, s * 0.95, 1.95))
    # roof marker lamps
    for i in range(4):
        b.box((0.16, 0.20, 0.14), (4.2, -1.05 + i * 0.70, 4.95))

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
                b.use(L.ORE)
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
    for s in (1, -1):                       # cab side windows
        b.box((1.5, 0.16, 0.85), (2.6, s * 1.31, 2.55))
    b.use("steel_dk")                       # door shut lines and body crease
    for s in (1, -1):
        b.box((0.06, 0.06, 1.9), (1.6, s * 1.32, 2.35))
        b.box((5.4, 0.07, 0.07), (-0.4, s * 1.32, 1.90))
    b.box((0.10, 2.5, 1.9), (-3.15, 0, 2.35))       # rear door split
    for x in (2.3, -2.1):
        for s in (1, -1):
            wheel(b, x, s * 1.32, 0.78, 0.78, 0.55)
    b.use(body)                             # wheel arches
    for x in (2.3, -2.1):
        for s in (1, -1):
            b.box((2.0, 0.20, 0.14), (x, s * 1.32, 1.62))
    b.use("steel_dk")                       # bumpers
    b.box((0.30, 2.5, 0.42), (3.30, 0, 1.05))
    b.box((0.30, 2.5, 0.42), (-3.30, 0, 1.05))
    for s in (1, -1):                       # mirrors
        b.box((0.07, 0.34, 0.26), (3.05, s * 1.60, 2.70))
    b.use("headlight")
    for s in (1, -1):
        b.box((0.16, 0.50, 0.30), (3.35, s * 0.85, 1.55))
    b.use("taillight")
    for s in (1, -1):
        b.box((0.14, 0.30, 0.55), (-3.35, s * 1.05, 2.20))
    return b.make(name, collection=C, scale=(0.86, 0.86, 0.86))


def loader(name, C=None):
    """Yellow wheel loader."""
    b = B().use("yellow")
    b.box((4.2, 3.0, 2.0), (-1.0, 0, 2.5))
    b.box((3.0, 2.8, 1.5), (1.6, 0, 2.3))
    b.use("glass")
    b.box((2.0, 2.4, 1.6), (-1.2, 0, 4.3))
    for x in (1.9, -2.0):
        for s in (1, -1):
            wheel(b, x, s * 1.6, 1.35, 1.35, 0.9)
    b.use("yellow")                       # mudguards over both axles
    for x in (1.9, -2.0):
        for s in (1, -1):
            b.box((3.0, 1.05, 0.18), (x, s * 1.6, 2.75))
    b.use("steel_dk")                     # articulation joint between the halves
    b.cylz(0.55, 1.3, (0.3, 0, 1.6), seg=10)
    for s in (1, -1):                     # lift arms
        b.box((4.4, 0.3, 0.5), (3.4, s * 1.2, 2.6), (0, radians(-18), 0))
    b.use("chrome")                       # hydraulic rams to the arms and bucket
    for s in (1, -1):
        b.cyl(0.16, 2.6, (2.6, s * 1.2, 2.05), (0, radians(74), 0), 8)
        b.cyl(0.13, 1.7, (4.6, s * 1.2, 1.75), (0, radians(58), 0), 8)
    b.use("steel_dk")                     # cab frame posts
    for sx in (1, -1):
        for sy in (1, -1):
            b.box((0.14, 0.14, 1.8), (-1.2 + sx * 0.95, sy * 1.15, 4.3))
    b.use("steel")
    b.box((1.9, 3.4, 1.5), (5.6, 0, 1.05), (0, radians(12), 0))
    b.box((2.0, 3.4, 0.16), (6.35, 0, 0.42), (0, radians(12), 0))   # cutting edge
    b.use("headlight")
    for s in (1, -1):
        b.box((0.14, 0.34, 0.24), (0.9, s * 1.35, 3.35))
    b.use("taillight")
    b.cylz(0.16, 0.30, (-2.8, 0, 3.60), seg=8)                      # beacon
    return b.make(name, collection=C)


def excavator(name, C=None):
    b = B().use("rock_dark")
    b.box((5.4, 3.4, 1.1), (0, 0, 0.75))
    for s in (1, -1):
        b.box((5.6, 0.9, 1.5), (0, s * 1.5, 0.9))
        # track links round the frame, and the drive sprocket and idler
        for i in range(11):
            b.box((0.34, 1.0, 0.16), (-2.5 + i * 0.5, s * 1.5, 0.20))
            b.box((0.34, 1.0, 0.16), (-2.5 + i * 0.5, s * 1.5, 1.62))
        b.use("steel_dk")
        for x in (2.55, -2.55):
            b.cyl(0.72, 1.02, (x, s * 1.5, 0.90), (radians(90), 0, 0), 12)
        for i in range(3):
            b.cyl(0.30, 1.06, (-1.2 + i * 1.2, s * 1.5, 0.42),
                  (radians(90), 0, 0), 8)
        b.use("rock_dark")
    b.use("steel_dk")
    b.cylz(1.35, 0.35, (0, 0, 1.30), seg=16)          # slew ring
    b.use("yellow")
    b.box((4.0, 3.0, 2.2), (-0.6, 0, 2.5))
    b.box((1.5, 2.9, 0.9), (-2.6, 0, 2.0))            # counterweight
    b.use("glass")
    b.box((1.7, 1.9, 1.9), (1.0, 0.5, 3.6))
    b.use("steel_dk")                                  # cab frame posts
    for sx in (1, -1):
        for sy in (1, -1):
            b.box((0.13, 0.13, 2.0), (1.0 + sx * 0.85, 0.5 + sy * 0.95, 3.6))
    b.cylz(0.14, 1.1, (-1.6, -1.0, 3.6), seg=8)        # exhaust
    b.use("yellow_lt")
    b.box((6.2, 0.8, 0.9), (3.4, 0, 4.6), (0, radians(-26), 0))
    b.use("chrome")                                    # boom and stick rams
    b.cyl(0.19, 3.4, (2.4, 0, 3.55), (0, radians(-38), 0), 8)
    b.cyl(0.17, 2.6, (5.6, 0, 4.15), (0, radians(52), 0), 8)
    b.use("steel_dk")
    b.box((4.6, 0.7, 0.75), (7.4, 0, 3.0), (0, radians(38), 0))
    b.use("steel")
    b.box((1.7, 1.9, 1.6), (9.0, 0, 1.1))
    for i in range(4):                                 # bucket teeth
        b.box((0.5, 0.26, 0.22), (9.85, -0.62 + i * 0.42, 0.55))
    return b.make(name, collection=C)


def forklift(name, C=None):
    b = B().use("yellow")
    b.box((2.6, 1.8, 1.5), (-0.3, 0, 1.15))
    b.box((0.7, 1.7, 0.9), (-1.5, 0, 1.30))                 # counterweight
    b.use("steel_dk")
    for s in (1, -1):                                        # twin mast channels
        b.box((0.22, 0.22, 3.2), (1.2, s * 0.62, 1.9))
    b.box((0.16, 1.5, 0.20), (1.2, 0, 3.45))                 # mast crown
    b.box((0.16, 1.6, 0.9), (1.35, 0, 0.75))                 # carriage
    b.use("steel_lt")
    for s in (1, -1):
        b.box((1.1, 0.18, 0.14), (1.9, s * 0.55, 0.35))
    b.use("chrome")
    b.cyl(0.09, 2.6, (1.05, 0, 1.9), (0, 0, 0), 8)           # lift ram
    b.use("steel_dk")                                        # overhead guard
    for sx in (1, -1):
        for sy in (1, -1):
            b.box((0.10, 0.10, 1.5), (-0.3 + sx * 0.85, sy * 0.72, 2.65))
    b.box((2.0, 1.7, 0.10), (-0.3, 0, 3.40))
    b.use("glass")
    b.box((1.1, 1.3, 0.9), (-0.4, 0, 2.35))                  # operator seat area
    for x, r in ((0.7, 0.52), (-1.2, 0.4)):
        for s in (1, -1):
            wheel(b, x, s * 0.8, r, r, 0.35)
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
        b.use(L.ORE)
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


def fence_run(pts, name, C=None, h=2.4, post=2.6, m="steel_dk", gaps=()):
    """Chain-link style fence following a list of (x,y,z) points.

    Walked as STRAIGHT segments rather than through sample_bez. A yard
    perimeter is a rectangle, and Catmull-Rom through its corners bows every
    side outward - 2.7 units at the town yards, which is what put three of the
    four fences out on the cross street they are meant to stand back from, and
    the depot's and market's out on the arterial.

    gaps = [(x, y, r), ...]: the works gates, left open. Every district is
    fenced and the arterial now runs up to the yard rather than through it, so
    without this the road ends against a continuous fence and the trucks drive
    through it. A fence that never comes within r of a gate is unaffected, which
    is why all four districts can pass their own gate whether or not their fence
    reaches it - the two islands disagree about that, see geom.gate_point. The
    market needs two: the arterial and the haul road out to the quay.
    """
    def blocked(x, y):
        return any(hypot(x - g[0], y - g[1]) < g[2] for g in gaps)

    b = B().use(m)
    for a, c in zip(pts, pts[1:]):
        seg = hypot(c[0] - a[0], c[1] - a[1])
        if seg < 1e-6:
            continue
        n = max(1, int(round(seg / post)))
        for i in range(n):
            t0, t1 = i / float(n), (i + 1) / float(n)
            p = [(a[k] + (c[k] - a[k]) * t0) for k in range(3)]
            q = [(a[k] + (c[k] - a[k]) * t1) for k in range(3)]
            if not blocked(p[0], p[1]):         # the run's last post is added
                b.boxz((0.18, 0.18, h), p)      # below, so corners do not get two
            if blocked((p[0] + q[0]) * 0.5, (p[1] + q[1]) * 0.5):
                continue
            for zz in (h * 0.95, h * 0.55, h * 0.15):
                b.tube(0.055, [(p[0], p[1], p[2] + zz), (q[0], q[1], q[2] + zz)], 4)
    if not blocked(pts[-1][0], pts[-1][1]):
        b.boxz((0.18, 0.18, h), (pts[-1][0], pts[-1][1], pts[-1][2]))
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
    # Ring stiffeners between the existing bands, and a caged ladder with side
    # rails rather than rungs floating in mid-air.
    b.use(m)
    for i in range(int(h / 2.2)):
        b.cyl(r * 1.012, 0.09, (0, 0, 1.0 + 1.1 + i * 2.2), seg=20)
    b.use("steel")
    for i in range(int(h / 0.44)):
        b.box((0.56, 0.07, 0.07), (r + 0.32, 0, 1.4 + i * 0.44))
    for sy in (1, -1):
        b.box((0.10, 0.10, h - 0.8), (r + 0.60, sy * 0.28, 1.0 + h * 0.5))
    for i in range(int(h / 1.3)):
        b.box((0.10, 0.62, 0.08), (r + 0.46, 0, 1.4 + i * 1.3))
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
    # Course seams: a welded tank is built from rolled plate in 1.8 m courses
    # and the weld line round each one is its most recognisable feature.
    b.use(m)
    _cs = max(1.8, h / 4.0)
    _i = 1
    while _cs * _i < h - 0.4:
        b.cyl(r * 1.008, 0.10, (0, 0, 0.8 + _cs * _i), seg=24)
        _i += 1
    b.use("steel")
    b.tube(0.13, [(r, 0, 0.9), (r + 0.9, 0, 0.9), (r + 0.9, 0, h * 0.9)], 5)
    # Roof handrail, and a caged ladder up the side.
    b.use("steel_dk")
    for k in range(16):
        _a = k * (6.28318 / 16)
        b.box((0.09, 0.09, 1.05), (cos(_a) * r * 0.94, sin(_a) * r * 0.94,
                                   0.8 + h + r * 0.10))
    b.cyl(r * 0.95, 0.09, (0, 0, 0.8 + h + r * 0.10 + 0.52), seg=24)
    for k in range(int(h / 0.42)):
        b.box((0.62, 0.06, 0.06), (-r - 0.34, 0, 1.0 + k * 0.42))
    for sy in (1, -1):
        b.box((0.09, 0.09, h - 0.6), (-r - 0.62, sy * 0.30, 0.8 + h * 0.5))
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
    b = B().use(L.ORE)
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
        pitched(b, d + 1.2, w + 1.2, d * 0.22, h + 0.5, body=roof)
    # Vertical sheet ribs down both long walls. A 12 cm proud rib every 1.6 m is
    # what profiled cladding actually is, and it is the difference between a
    # painted box and a building at any distance the game is played from.
    b.use(body)
    _rib = 1.6
    for i in range(int(w / _rib) + 1):
        x = -w * 0.5 + i * _rib
        if abs(x) > w * 0.5:
            continue
        for sy in (1, -1):
            b.box((0.12, 0.12, h * 0.94), (x, sy * (d * 0.5 + 0.06), 0.5 + h * 0.47))
    # Gutter along both eaves, and a downpipe at each corner.
    b.use("steel_dk")
    for sy in (1, -1):
        b.box((w + 0.9, 0.30, 0.30), (0, sy * (d * 0.5 + 0.20), 0.5 + h + 0.05))
        for sx in (1, -1):
            b.box((0.22, 0.22, h), (sx * (w * 0.5 - 0.2), sy * (d * 0.5 + 0.22),
                                    0.5 + h * 0.5))
    # Roller doors, each in a steel frame set proud of the wall.
    for i in range(doors):
        x = -w * 0.5 + w * (i + 0.5) / doors
        dw = w / doors * 0.55
        b.use("steel_dk")
        b.box((dw, 0.25, h * 0.62), (x, d * 0.5, 0.5 + h * 0.31))
        b.use("steel")
        b.box((dw + 0.5, 0.16, 0.30), (x, d * 0.5 + 0.16, 0.5 + h * 0.62 + 0.15))
        for sx in (1, -1):
            b.box((0.30, 0.16, h * 0.62), (x + sx * (dw * 0.5 + 0.15),
                                           d * 0.5 + 0.16, 0.5 + h * 0.31))
        # slats
        b.use(body)
        for k in range(4):
            b.box((dw * 0.94, 0.10, 0.07),
                  (x, d * 0.5 + 0.19, 0.5 + h * 0.10 + k * h * 0.14))
    # Strip glazing in a recessed reveal rather than a decal on the wall.
    for i in range(int(w / 5.0)):
        wx = -w * 0.5 + 3.0 + i * 5.0
        b.use("steel_dk")
        b.box((2.6, 0.18, 1.5), (wx, -(d * 0.5 + 0.05), h * 0.72))
        b.use("winlight")
        b.box((2.2, 0.2, 1.1), (wx, -d * 0.5, h * 0.72))
    return b.make(name, collection=C)


def shop(name, w=13.0, d=10.0, h=6.0, C=None, body="offwhite", roof="roof_red",
         awning=None):
    b = B().use(body)
    b.boxz((w, d, h), (0, 0, 0))
    pitched(b, d + 1.0, w + 1.0, d * 0.20, h, body=roof)
    # shopfront: a stall riser under the glass, mullions in it, and a fascia
    # board over the top - the three things every real shopfront has
    b.use("glass")
    b.box((w * 0.74, 0.2, h * 0.42), (0, d * 0.5, h * 0.4))
    b.use("steel_dk")
    for i in range(3):
        b.box((0.09, 0.26, h * 0.42), (-w * 0.24 + i * w * 0.24, d * 0.5, h * 0.4))
    b.use(body)
    b.box((w * 0.80, 0.26, h * 0.16), (0, d * 0.5 + 0.03, h * 0.14))
    b.box((w * 0.92, 0.24, h * 0.14), (0, d * 0.5 + 0.04, h * 0.68))
    b.use("wood")
    b.box((w * 0.20, 0.18, h * 0.54), (w * 0.30, d * 0.5 + 0.04, h * 0.30))
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
    for f in range(floors):
        z = fh * (f + 0.55)
        # glazing set back behind the wall line, so the reveal casts a shadow
        b.use("glass")
        b.box((w * 0.82, d + 0.02, fh * 0.44), (0, 0, z))
        b.box((w + 0.02, d * 0.82, fh * 0.44), (0, 0, z))
        # mullions every 1.5 m, and the spandrel band at each floor line
        b.use("steel_dk")
        for i in range(int(w * 0.82 / 1.5) + 1):
            x = -w * 0.41 + i * 1.5
            if abs(x) <= w * 0.41:
                b.box((0.10, d + 0.20, fh * 0.44), (x, 0, z))
        for i in range(int(d * 0.82 / 1.5) + 1):
            y = -d * 0.41 + i * 1.5
            if abs(y) <= d * 0.41:
                b.box((w + 0.20, 0.10, fh * 0.44), (0, y, z))
        b.use(body)
        b.box((w + 0.22, d + 0.22, 0.28), (0, 0, fh * (f + 1) - 0.10))
    # entrance canopy over the door
    b.use("steel_dk")
    b.box((w * 0.34, 1.6, 0.16), (0, d * 0.5 + 0.7, fh * 0.92))
    b.use("glass")
    b.box((w * 0.26, 0.16, fh * 0.72), (0, d * 0.5, fh * 0.38))
    flat_roof(b, w + 0.8, d + 0.8, fh * floors, deck="roof_grey", plant=1)
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
