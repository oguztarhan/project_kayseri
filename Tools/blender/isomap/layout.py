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
from math import cos, hypot, pi, sin

R = 128.0


def ring(radius, n=16, close=True):
    """n points evenly around a circle, first one on +X.

    Catmull-Rom through 16 points deviates from the true circle by under 1%, so
    a ring built this way IS the circle - which is what lets everything else be
    placed by radius alone. The four axis crossings land exactly on control
    points 0, n/4, n/2 and 3n/4, so junctions, spur heads and the grade profile
    all agree on where the ring meets the arterials without measuring for it.
    """
    pts = [(radius * cos(2.0 * pi * k / n), radius * sin(2.0 * pi * k / n))
           for k in range(n)]
    return pts + [pts[0]] if close else pts


def straight(a, b, n=12):
    """A straight run as n collinear control points.

    Not cosmetic: strip() and road_path() both sample a path at len(pts) * 10,
    so a two-point arterial became a 20-segment ribbon - 17-unit chords over a
    smoothstep grade profile, whose sagitta (~0.29) ate almost all of the 0.38
    the road sits proud of the ground. That is what made stretches of road sink
    into the hillside between their vertices. Catmull-Rom through collinear
    points is exactly the straight line, so the geometry is unchanged.
    """
    return [(a[0] + (b[0] - a[0]) * k / (n - 1.0),
             a[1] + (b[1] - a[1]) * k / (n - 1.0)) for k in range(n)]

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
# Half-extent of the flat ground a site stands on. It has to cover the yard slab
# 12_sites.py actually draws, which is 52 square: at 22 the store's north edge
# hung 4 units over the feather and buried itself 25 deep in the peak behind it.
SITE_PAD = 26.0

# ------------------------------------------------------------------- roads
ROAD_W = 14.0
ROAD_X = straight((-158.0, 0.0), (196.0, 0.0))
ROAD_Y = straight((0.0, -196.0), (0.0, 196.0))

# The ring road is a true circle. It used to be a Catmull-Rom through four
# corners at +/-73, which bulged to 103 on the diagonals and - because
# sample_bez reflects its end tangents rather than wrapping - came out lopsided:
# 91.2 out on the east side against 81.0 on the west. Nothing could be placed
# against it by radius, the four "junctions" on the arterials sat 18 units short
# of where the tarmac actually crossed, and its corners reached far enough out
# to brush the district yards.
LOOP_R = 74.0
LOOP_C = ring(LOOP_R)
LOOP = LOOP_C[:-1]

# Town centre, inside the ring road. Four yards on the diagonals, one per
# quadrant, each driven by its own station - see 15_town.py. Sized and placed
# so the outermost corner sits at r=60, comfortably inside the ring road's
# inner kerb at r=67.2.
TOWN_POWER = (30.0, 30.0)     # NE  POWER PLANT
TOWN_HAUL = (-30.0, 30.0)     # NW  ORE TRUCKS
TOWN_FLEET = (-30.0, -30.0)   # SW  CARGO TRUCKS
TOWN_CIVIC = (30.0, -30.0)    # SE  the town itself
TOWNS = [TOWN_POWER, TOWN_HAUL, TOWN_FLEET, TOWN_CIVIC]
TOWN_PAD = 13.0

# Pedestrian circuit, concentric with the ring road and OUTSIDE it, so the
# pavement is the ring's outer footway rather than a second loop cutting across
# the middle of town. 9 units out clears the phase-3 carriageway and its
# shoulder (6.8) with room for a verge; the outer kerb then lands at 85.2,
# which is 4.1 clear of the nearest district geometry (the mine spoil at 89.3).
WALK_R = LOOP_R + 9.0
FOOTPATH = ring(WALK_R)

# Spurs leave the ring RADIALLY, from a point on the circle. They used to start
# 10-16 units inside it, so each one crossed the ring road and carried on -
# reading as a stub of tarmac stranded in the grass rather than a turning off
# the main road. The four district spurs are gone entirely: both arterials
# already run the length of their districts, so the spurs only ever duplicated
# them with a diagonal shortcut.
def _spurhead(target):
    d = hypot(target[0], target[1])
    return (LOOP_R * target[0] / d, LOOP_R * target[1] / d)


SPURS = [
    # links out to the unlockable sites
    ([_spurhead(SITE_QUARRY), (-84.0, -50.0), SITE_QUARRY], "Spur.Quarry"),
    ([_spurhead(SITE_STORE), (81.0, 81.0), SITE_STORE], "Spur.Store"),
    ([_spurhead(SITE_PLANT), (77.0, -77.0), SITE_PLANT], "Spur.Plant"),
    # town-centre cross streets, each meeting the north-south arterial square on
    ([(-17.0, 30.0), (0.0, 30.0), (17.0, 30.0)], "Street.TownN"),
    ([(-17.0, -30.0), (0.0, -30.0), (17.0, -30.0)], "Street.TownS"),
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
