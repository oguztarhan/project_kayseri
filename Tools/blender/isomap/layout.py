"""Shared layout constants - all in WORLD coordinates.

Screen mapping (ortho iso camera, yaw 45, elevation 48):
    screen_x = 0.7071*(x + y)          screen_y = 0.7071*(y - x)*0.743 + z*0.669
    world +X -> screen down-right      world +Y -> screen up-right
Visible frame at ortho_scale 380: screen_x in [-190, 190], screen_y in [-127, 127].

Districts sit on the world cardinal axes so they land in the four screen
quadrants:
    MINE (-X) top-left        DEPOT (+Y) top-right
    MARKET (-Y) bottom-left   REFINERY (+X) bottom-right
The ocean fills the screen-left / bottom-left, i.e. the world SW half-plane
(small x+y), with the port in a sheltered bay next to the market.
"""
from math import hypot

R = 128.0

MINE = (-R, 0.0)
DEPOT = (0.0, R)
REFINERY = (R, 0.0)
MARKET = (0.0, -R)
CENTER = (0.0, 0.0)
DISTRICTS = [MINE, DEPOT, REFINERY, MARKET]
PAD = 36.0

# Secondary, unlockable sites - the intercardinal screen positions.
# Screen top-centre is taken by the railway arc, so the quarry goes on the
# coastal strip between the mine and the port instead.
SITE_QUARRY = (-104.0, -62.0)   # screen left-centre    unlocks phase 2
SITE_STORE = (110.0, 110.0)     # screen right-centre   unlocks phase 2
SITE_PLANT = (102.0, -102.0)    # screen bottom-centre  unlocks phase 3
SITES = [("quarry", SITE_QUARRY, 2), ("store", SITE_STORE, 2),
         ("plant", SITE_PLANT, 3)]
SITE_PAD = 22.0

# ------------------------------------------------------------------- roads
ROAD_W = 14.0
ROAD_X = [(-158.0, 0.0), (196.0, 0.0)]
ROAD_Y = [(0.0, -196.0), (0.0, 196.0)]
LOOP = [(-73, -73), (-73, 73), (73, 73), (73, -73)]
LOOP_C = LOOP + [LOOP[0]]

SPURS = [
    ([(-73, 28), (-90, 16), (-102, 0)], "Spur.Mine"),
    ([(28, 73), (16, 90), (0, 102)], "Spur.Depot"),
    ([(73, -28), (90, -16), (102, 0)], "Spur.Refinery"),
    ([(-28, -73), (-16, -90), (0, -102)], "Spur.Market"),
    # links out to the unlockable sites
    ([(-73, -40), (-88, -48), (-102, -56)], "Spur.Quarry"),
    ([(64, 64), (88, 88), (110, 100)], "Spur.Store"),
    ([(64, -64), (84, -84), (102, -94)], "Spur.Plant"),
]

# market -> port haul road, running out to the quay
PORT_ROAD = [(-20, -126), (-38, -124), (-54, -120), (-66, -115)]

# ------------------------------------------------------------------- railway
# Tunnel in the massif, long arc through the world NW quadrant (top of screen),
# into the depot.  Phase 3 adds the branch down to the port.
RAIL = [(-186, -6), (-172, 12), (-156, 32), (-138, 52), (-118, 70),
        (-96, 88), (-72, 104), (-46, 116), (-20, 122), (4, 118), (18, 108)]
RAIL_PORT = [(6, -100), (-12, -106), (-34, -108), (-54, -110), (-66, -112)]

# ---------------------------------------------------------------------- water
# Rises in the massif, skirts SOUTH of the mine pad, and drains into the sea.
RIVER = [(-176, 20), (-170, -20), (-158, -52), (-142, -76), (-126, -92),
         (-112, -104)]
RIVER_W = 13.0
RIVER_CARVE = 24.0
FALLS = (0.30, 0.66)

# Shoreline, running screen top-left to bottom-left.  Ocean is the side with
# the smaller x+y.  The landward bulge in the middle is the harbour bay.
SHORE = [(-250, 40), (-224, 2), (-196, -36), (-160, -60), (-124, -80),
         (-96, -104), (-72, -132), (-48, -168), (-28, -210), (-12, -250)]
SEA_Z = -3.0
SEA_DEEP = -17.0

# Port sits in the bay up-shore of the market.  Kept well clear of the market
# pad (x >= -36): the quay's landward edge stops around x = -53.
PORT = (-76.0, -113.0)
PORT_YAW = -0.9272952            # quay runs parallel to this stretch of shore
SHIP_OUT = (-108.0, -136.0)      # ship under way, heading off-screen
SHIP_LANE = [(-108, -136), (-142, -162), (-176, -188), (-210, -214)]

GROUND_SIZE = 640.0
GROUND_SEGS = 250

PEAKS = [
    # Massif the mine is cut into - screen top-left.  Peaks sit clear of the
    # mine pad (x < -164) and landward of the shoreline.  The range runs
    # south-west so its summits stay inside the frame rather than cropping.
    (-170, 2, 26, 54), (-182, -12, 27, 58), (-172, -34, 25, 52),
    (-192, -26, 26, 56), (-166, -58, 24, 46), (-176, -46, 24, 50),
    (-196, 6, 26, 58), (-184, 24, 25, 54), (-204, -14, 25, 54),
    (-176, 44, 24, 48),
    # ridge behind the railway arc - crops off the frame top
    (-176, 62, 28, 52), (-150, 88, 28, 50), (-120, 116, 28, 48),
    (-88, 140, 28, 46), (-52, 160, 27, 42), (-14, 176, 26, 38),
    # world NE quadrant = screen right edge
    (30, 172, 26, 36), (72, 154, 27, 38), (112, 130, 28, 40),
    (150, 102, 28, 40), (180, 68, 26, 34), (196, 30, 25, 30),
    # world SE quadrant = screen bottom edge
    (192, -22, 25, 30), (172, -66, 26, 34), (142, -108, 26, 34),
    (106, -146, 26, 32), (66, -178, 25, 30),
    # world S / SW - headland framing the harbour
    (20, -206, 25, 30), (-16, -166, 22, 26), (-52, -206, 24, 28),
]


def dist_to_path(x, y, path):
    """Distance from (x,y) to a polyline, plus normalised position along it."""
    best, best_t, acc, total = 1e9, 0.0, 0.0, 0.0
    lens = []
    for i in range(len(path) - 1):
        ax, ay = path[i][0], path[i][1]
        bx, by = path[i + 1][0], path[i + 1][1]
        Lx = hypot(bx - ax, by - ay)
        lens.append(Lx)
        total += Lx
    for i in range(len(path) - 1):
        ax, ay = path[i][0], path[i][1]
        bx, by = path[i + 1][0], path[i + 1][1]
        dx, dy = bx - ax, by - ay
        L2 = dx * dx + dy * dy
        t = 0.0 if L2 < 1e-9 else ((x - ax) * dx + (y - ay) * dy) / L2
        t = max(0.0, min(1.0, t))
        d = hypot(x - (ax + dx * t), y - (ay + dy * t))
        if d < best:
            best = d
            best_t = (acc + lens[i] * t) / max(total, 1e-6)
        acc += lens[i]
    return best, best_t


def shore_sum(x, y):
    """(x+y) of the nearest shoreline point - land is above it, sea below."""
    best, bs = 1e9, -200.0
    for i in range(len(SHORE) - 1):
        ax, ay = SHORE[i]
        bx, by = SHORE[i + 1]
        dx, dy = bx - ax, by - ay
        L2 = dx * dx + dy * dy
        t = 0.0 if L2 < 1e-9 else ((x - ax) * dx + (y - ay) * dy) / L2
        t = max(0.0, min(1.0, t))
        px, py = ax + dx * t, ay + dy * t
        d = hypot(x - px, y - py)
        if d < best:
            best, bs = d, px + py
    return bs


def sea_depth(x, y):
    """>0 = metres seaward of the waterline (0 on the beach)."""
    return (shore_sum(x, y) - (x + y)) * 0.7071


def smoothstep(e0, e1, x):
    if e1 == e0:
        return 0.0
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3.0 - 2.0 * t)


def band(d, inner, outer):
    return 1.0 - smoothstep(inner, outer, d)


def rect_mask(x, y, hw, hh, feather):
    return min(band(abs(x), hw, hw + feather), band(abs(y), hh, hh + feather))


def active_sites(phase):
    return [(n, p) for (n, p, need) in SITES if phase >= need]


def locked_sites(phase):
    return [(n, p) for (n, p, need) in SITES if phase < need]
