"""Coal island - the original map.  All coordinates are WORLD coordinates.

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

from geom import (SQ2, band, crossings, dist_to_path, fence_gaps, gate_point, gates, offset_open,
                  rect_mask, ring,
                  shore_fns, site_filters, smoothstep, straight, trim_arterial,
                  trim_zones)

NAME = "coal"
ORTHO = 380.0

# Material names for the ore this island mines - see 01_setup.py. Parts and
# district scripts ask the island rather than naming a material, so the same
# stockpile geometry reads as coal here and as malachite ore on the copper map.
ORE = "coal"
ORE_SHINY = "coal_shiny"

R = 128.0

MINE = (-R, 0.0)
DEPOT = (0.0, R)
REFINERY = (R, 0.0)
MARKET = (0.0, -R)
CENTER = (0.0, 0.0)
DISTRICTS = [MINE, DEPOT, REFINERY, MARKET]
PAD = 36.0

# Graded height of each district pad - read by grade.py, which builds the
# arterial profiles from whichever district sits at each end of each road.
# High in the west against the massif, falling away east to the refinery.
MINE_Z = 16.0
DEPOT_Z = 13.0
REFINERY_Z = 5.0
MARKET_Z = 5.0
CENTER_Z = 10.0
PORT_Z = 3.0
DISTRICT_Z = [(MINE, MINE_Z), (DEPOT, DEPOT_Z),
              (REFINERY, REFINERY_Z), (MARKET, MARKET_Z)]

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
# Where the ring meets each arterial: E, N, W, S. On this island it is just
# +-LOOP_R, but the iron map's loop is not a circle, so everything downstream
# reads these rather than assuming a radius. See geom.ring_meets.
LOOP_MEETS = [(LOOP_R, 0.0), (0.0, LOOP_R), (-LOOP_R, 0.0), (0.0, -LOOP_R)]

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
# into the depot.  One line only: the quayside siding that used to run from the
# market to the port is gone - it crossed the market yard, parked a wagon inside
# Market.Office, and duplicated a haul the port road already makes.
RAIL = [(-186, -6), (-172, 12), (-156, 32), (-138, 52), (-118, 70),
        (-96, 88), (-72, 104), (-46, 116), (-20, 122), (4, 118), (18, 108)]

# ---------------------------------------------------------------------- water
# Rises in the massif, skirts SOUTH of the mine pad, and drains into the sea.
RIVER = [(-176, 20), (-170, -20), (-158, -52), (-142, -76), (-126, -92),
         (-112, -104)]
RIVER_W = 13.0
RIVER_CARVE = 24.0
FALLS = (0.30, 0.66)

# Bed profile, as a function of normalised arc length - see bed_z in
# 02_terrain.py. The coal river runs the far western edge at a constant slope.
RIVER_Z0 = -3.0
RIVER_FALL = 8.0
FALL_DROPS = (4.2, 4.8)

# This river never crosses a carriageway, so no road needs carrying over it.
ROAD_BRIDGES = False


def river_w(t):
    """Surface half-width at normalised arc length t."""
    return RIVER_W


def river_carve(t):
    """How far either side of the centreline the gorge is cut at t."""
    return RIVER_CARVE


# Shoreline, running screen top-left to bottom-left.  Ocean is the side with
# the smaller x+y.  The landward bulge in the middle is the harbour bay.
SHORE = [(-250, 40), (-224, 2), (-196, -36), (-160, -60), (-124, -80),
         (-96, -104), (-72, -132), (-48, -168), (-28, -210), (-12, -250)]
SEA_AXIS = (-SQ2, -SQ2)          # unit vector pointing out to sea
SEA_Z = -3.0
SEA_DEEP = -17.0

sea_depth = shore_fns(SHORE, SEA_AXIS[0], SEA_AXIS[1])

# Port sits in the bay up-shore of the market.  Kept well clear of the market
# pad (x >= -36): the quay's landward edge stops around x = -53.
PORT = (-76.0, -113.0)
PORT_GATE = 26.0        # where the haul road hands over to the quay apron
PORT_YAW = -0.9272952            # quay runs parallel to this stretch of shore
PORT_APRON = (12.0, 9.0)         # apron centre, offset landward of the quay
# Ship under way, heading off-screen. Same correction as the copper map: it was
# almost dead in line with the berths (0.8 along the quay, 39 out) while a
# phase-3 hull reaches 67 out, so it sailed through the moored one. Moved 42
# along the quay, past the harbour mouth.
SHIP_OUT = (-143.0, -111.0)
SHIP_LANE = [(-143, -111), (-177, -137), (-211, -163), (-245, -189)]

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

# No turning heads. The iron island ends every dead-end road in a bulb so a
# loaded truck can turn; these two predate it and their roads simply stop.
HEADS = []
# Nor any one-way run - see isle_iron.ONEWAY.
ONEWAY = []

# Every road that reaches a district, so its fence knows where to leave a
# gate - see geom.fence_gaps. The arterials are not listed: they end at the
# gate point every district script already opens for.
APPROACHES = [PORT_ROAD]

# Transmission lines, as runs of pylon feet. Per island because they have to
# thread between the works, and no two islands put those in the same place.
PYLONS = [[(196, -30), (162, -66), (128, -102), (94, -138)],
          [(52, 178), (86, 146), (120, 114), (154, 82), (188, 50)]]

# THE TARMAC, as opposed to the truck ROUTES.
#
# These were the same data until now, which is the single reason every attempt
# at this island came back as a cross: ROAD_X and ROAD_Y are what Unity drives,
# and they were also what got drawn, so the moment two routes wanted to share a
# stretch of trunk the drawing put two ribbons of asphalt on the same line.
#
# Split, a route may overlap another freely - nothing draws it - and the tarmac
# is free to be a branching tree with T-junctions instead of a crossroads.
#
#   (points, width key, name, trim mode)
#   trim "arterial"  cut at the works gates, keep the run through the middle
#        "gated"     cut at the works gates, keep every run
#        "none"      lay it whole
CARRIAGEWAYS = [(ROAD_X, "main", "Road.X", "arterial"),
                (ROAD_Y, "main", "Road.Y", "arterial"),
                (LOOP_C, "loop", "Road.Loop", "none")]

# How much of the railway is inside the tunnel, as a span of its arc length.
# 02_terrain leaves this stretch unflattened so the rock stays solid over the
# bore; past it the cutting takes over.
TUNNEL = [(0.035, 0.105)]

# Where the tarmac stops - see geom.gates. Town yards get 2 units of clearance
# on top of their pad so a cross street ends at the kerb rather than against the
# gate itself; the districts and sites are cut exactly at the pad, which is also
# the edge of the yard slab that takes over inside. The port is in the list for
# the same reason a district is: the quay apron is the driving surface from
# there in, and the haul road used to run on under it.
GATES = gates(DISTRICTS, PAD, SITES, SITE_PAD,
              extra=[(x, y, TOWN_PAD + 2.0) for x, y in TOWNS] + [PORT + (PORT_GATE,)])

# ------------------------------------------------------------------- theme
# Which set of signature props this island gets - see 16_theme.py. The two maps
# share every building, so without these they are the same island in two
# colours; these are the pieces that say at a glance which ore is mined here.
THEME = "coal"

# Where the two signature pieces stand: open ground between the ring road and a
# district, clear of every road, the rail arc, the river and the shore. The
# district yards are full - the overlap audit had to be driven to zero to get
# them that way - so a landmark of its own is both the only room there is and
# the better read.
THEME_SPOTS = {
    "works": (98.0, 52.0),      # coke-oven battery, between refinery and store
    "yard": (-106.0, 48.0),     # pit props and sawn timber, up by the mine
}
# Spills of the stuff this island handles, out on the bare ground. Cheap, and
# they carry the theme right out to the edges of the frame where there is
# otherwise nothing but grass.
THEME_SPILLS = [(53, -133), (51, 140), (48, -76), (-26, 84), (-52, -71),
                (116, -59)]

active_sites, locked_sites = site_filters(SITES)
