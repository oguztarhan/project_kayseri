"""Gold island - fourth map: arid goldfield country, sea across the screen BOTTOM.

Screen mapping is the same as the other three (ortho iso, yaw 45, elevation 48):
    screen_x = 0.7071*(x + y)          screen_y = 0.7071*(y - x)*0.743 + z*0.669
Visible frame at ortho_scale 440: x+y within +/-311, 0.5254*(y-x) + 0.669*z
within +/-147.

THE PLAN, and how it differs from the other three at a glance:

  * The COAST RUNS ALONG THE BOTTOM of the screen. Coal and iron put the sea on
    the screen left, copper on the right; here the shore is a horizontal band
    (y - x roughly constant) with the harbour bay on it, so the whole map reads
    as high country running down to a south coast.
  * The CHAIN IS ROTATED: mine at world +y (screen top), depot at -x (upper
    left), refinery at -y (lower left), market at +x (lower right, ON the
    coast) with the port down-shore of it. On the other maps ore enters at the
    left and leaves at the bottom; here it pours from the top of the frame down
    to the sea.
  * The ring is a LONG IRREGULAR LOZENGE stretched across the screen, corners
    62-95 out, no two sides alike - not coal's circle, not iron's chunky
    pentagon.
  * The RAILWAY CROSSES THE WHOLE TOP OF THE FRAME behind the mine, through
    TWO mesas - a bore each - before dropping down the west side into the
    depot. Iron has one through-tunnel; this line is the island's skyline.
  * The RIVER rises in the north-west mesas, runs down the west edge and bends
    across the bottom-left corner into the south sea. It crosses no road -
    the sluice yard on its bank is the reason it exists.

Shared scripts untouched, same as iron: the ring is a measured PATH, the sea
side is data (SEA_AXIS), and the extra tunnel is just a second span in TUNNEL.
"""
from geom import (SQ2, axis_meets, band, circuit, crossings, dist_to_path,
                  fence_gaps, gate_point, gates, island_fns, offset_closed,
                  offset_open, rect_mask, shore_fns, site_filters, smoothstep,
                  straight, trim_arterial, trim_zones)

NAME = "gold"
# Between coal's 380 and iron's 460: the map is compact but the rail arc over
# the top and the harbour at the bottom both need headroom.
ORTHO = 440.0

ORE = "ore_au"
ORE_SHINY = "ore_au_shiny"

R = 128.0

# Rotated a quarter-turn against every other map - see the header. Same rule
# as always: the four works sit on the world cardinal axes so they land in the
# four screen quadrants and the arterials are the axes.
MINE = (0.0, 142.0)
DEPOT = (-138.0, 0.0)
REFINERY = (0.0, -128.0)
MARKET = (134.0, 0.0)
CENTER = (0.0, 0.0)
DISTRICTS = [MINE, DEPOT, REFINERY, MARKET]
PAD = 36.0

# The island tilts to its coast: high in the mined north, sea level at the
# market and port. grade.py builds the arterial profiles from these.
MINE_Z = 18.0
DEPOT_Z = 12.0
REFINERY_Z = 5.0
MARKET_Z = 4.0
CENTER_Z = 8.0
PORT_Z = 3.0
DISTRICT_Z = [(MINE, MINE_Z), (DEPOT, DEPOT_Z),
              (REFINERY, REFINERY_Z), (MARKET, MARKET_Z)]

# ------------------------------------------------------------------- the ring
ROAD_W = 14.0

# Six corners, none symmetric, stretched along the screen so the circuit reads
# as a long working loop rather than a roundabout. Crossings measured, not
# assumed: east and west arterial meets land near 81 and 89 out, north and
# south near 68 and 62 - four different numbers, like iron, unlike the circles.
LOOP_CORNERS = [(90.0, 18.0), (30.0, 70.0), (-58.0, 56.0),
                (-94.0, -8.0), (-34.0, -66.0), (56.0, -56.0)]
LOOP_R = 76.0                    # nominal only - nothing downstream reads it
LOOP_C = circuit(LOOP_CORNERS, fillet=12.0)
LOOP = LOOP_C[:-1]
LOOP_MEETS = axis_meets(LOOP_C)

# Outer footway, offset off the road itself - same reasoning as iron.
WALK_R = LOOP_R + 9.0
FOOTPATH = offset_closed(LOOP_C, 9.0)

# ------------------------------------------------------------------- arterials
ROAD_X = straight((-200.0, 0.0), (200.0, 0.0))
ROAD_Y = straight((0.0, -200.0), (0.0, 200.0))

# ------------------------------------------------------------------- the sites
# Three of the four blocks: NW (between depot and mine, under the rail's
# descent), NE (between mine and market, inside the empty high block), SW
# (between depot and refinery). The SE block is left open on purpose - that is
# harbour country, and the stamp mill stands there instead.
SITE_QUARRY = (-88.0, 74.0)
SITE_STORE = (96.0, 78.0)
SITE_PLANT = (-92.0, -78.0)
SITES = [("quarry", SITE_QUARRY, 2), ("store", SITE_STORE, 2),
         ("plant", SITE_PLANT, 3)]
SITE_PAD = 26.0

# ------------------------------------------------------------------- the town
# Four yards on the diagonals inside the ring, clear of both arterials and of
# the lozenge's nearest side.
TOWN_POWER = (26.0, 24.0)     # NE  POWER PLANT
TOWN_HAUL = (-30.0, 26.0)     # NW  ORE TRUCKS
TOWN_FLEET = (-27.0, -22.0)   # SW  CARGO TRUCKS
TOWN_CIVIC = (24.0, -26.0)    # SE  the town itself
TOWNS = [TOWN_POWER, TOWN_HAUL, TOWN_FLEET, TOWN_CIVIC]
TOWN_PAD = 13.0


def _yard_arm(junction, elbow, site):
    """A site arm: out of the ring, one bend, then square into the yard.
    Same shape and same reasoning as iron's - the last leg is axis-aligned so
    the arm never runs under the yard slab's corners."""
    return straight(junction, elbow, 5) + straight(elbow, site, 4)[1:]


SPURS = [
    # Site arms. Each leaves part-way along a ring side and bends once.
    (_yard_arm((-70.0, 32.0), (-88.0, 46.0), SITE_QUARRY), "Spur.Quarry"),
    (_yard_arm((58.0, 44.0), (96.0, 52.0), SITE_STORE), "Spur.Store"),
    (_yard_arm((-62.0, -40.0), (-92.0, -54.0), SITE_PLANT), "Spur.Plant"),
    # Town cross streets, middle point on the arterial - 03_roads reads p[1].
    ([(-30.0, 26.0), (0.0, 27.0), (26.0, 24.0)], "Street.TownN"),
    ([(-27.0, -22.0), (0.0, -25.0), (24.0, -26.0)], "Street.TownS"),
]

# Market -> port haul road: a coastal run EAST along the shore to the harbour.
# The first port sat 59 from the market - two gate zones deep into each other,
# the exact collision iron fixed by going to 141 - so the harbour moved
# up-shore instead, and the haul road became a real stretch of coast road.
PORT_ROAD = [(172.0, 2.0), (188.0, 10.0), (204.0, 26.0), (214.0, 38.0)]

# ------------------------------------------------------------------- railway
# THE SKYLINE. From a railhead buried in the eastern canyon, the line sweeps
# across the whole top of the frame BEHIND the mine - through two mesas, a
# bore each - then turns down the west side and comes into the depot from the
# north. Every other map hides most of its rail behind a works; this one is
# watched for its entire run.
RAIL = [(200, 120), (180, 140), (156, 158), (128, 176), (98, 190), (66, 200),
        (34, 204), (2, 202), (-30, 194), (-60, 182), (-88, 166), (-114, 148),
        (-138, 126), (-156, 100), (-166, 72), (-166, 52), (-158, 42)]

# Three spans: the buried railhead, then one bore through each mesa. Wider
# than the first cut so more of each crossing is under rock, and both mouths
# stand in the mesa's face rather than at its skirt.
TUNNEL = [(0.0, 0.07), (0.11, 0.27), (0.42, 0.58)]

# Hand-seating nudges for the five portal mouths (railhead, then in/out per
# bore), measured off the built terrain once it exists. Start at zero.
PORTAL_NUDGE = [(0.0, 0.0, 0.0),
                (0.0, 0.0, 0.0), (0.0, 0.0, 0.0),
                (0.0, 0.0, 0.0), (0.0, 0.0, 0.0)]

# ---------------------------------------------------------------------- water
# Rises off the southern end of the north-west mesas, runs down the far west,
# and bends across the bottom-left corner into the south sea. It crosses no
# carriageway; the sluice yard on its east bank is what it is FOR - this is
# placer country, and the river is where the gold came from.
#
# The bed starts at 4 and only ever goes DOWN: the first cut of this river
# started at 14, above the western plain, and 02_terrain obligingly lifted a
# sheet of water into the air - the exact failure the iron header warns about.
RIVER = [(-234, 8), (-238, -32), (-234, -72), (-224, -112), (-208, -152),
         (-186, -190), (-156, -222), (-118, -246), (-76, -260), (-46, -268)]
RIVER_W = 13.0
RIVER_CARVE = 24.0
FALLS = (0.18, 0.42)

RIVER_Z0 = 4.0
RIVER_FALL = 6.0
FALL_DROPS = (2.5, 2.5)

ROAD_BRIDGES = False


def river_w(t):
    """Surface half-width: a mountain stream at the source, a river by the
    time it reaches the sluice ground."""
    return 8.0 + 5.0 * smoothstep(0.05, 0.35, t)


def river_carve(t):
    """Gorge half-width: pinched at the source so the cut does not bite into
    the mesa the river rises under, full width past the first fall."""
    return 13.0 + 11.0 * smoothstep(0.08, 0.30, t)


def _sd(s, d):
    """(x+y, y-x) -> world (x, y).  The screen frame: s is right, d is up."""
    return ((s - d) * 0.5, (s + d) * 0.5)


# THE SOUTH COAST: a band of near-constant d, so the shore runs horizontally
# across the screen - the one orientation no other island uses. The landward
# bulge on the right, past the market, is the harbour bay; its three middle
# points are collinear in s so the quay gets a straight face (the copper
# lesson). The notch at the far left is the river mouth.
SHORE = [_sd(s, d) for s, d in (
    (-330, -216), (-260, -204), (-190, -196), (-120, -194),
    (-52, -196), (10, -192), (70, -196), (130, -200), (190, -198),
    (250, -176), (270, -176), (290, -176),     # straight harbour face
    (315, -192), (345, -204))]
SEA_AXIS = (SQ2, -SQ2)           # unit vector pointing out to sea: south-east
SEA_Z = -3.0
SEA_DEEP = -17.0

sea_depth = shore_fns(SHORE, SEA_AXIS[0], SEA_AXIS[1])

# Port on the harbour face, 100 up-shore of the market: both gate zones plus
# 22 units of real haul road between them. The quay runs along the shore's own
# direction, world (1, 1).
PORT = (220.0, 50.0)
PORT_GATE = 42.0
PORT_YAW = 0.7853982             # quay parallel to the coast band
PORT_APRON = (-9.0, 9.0)         # landward along the quay's normal
# Ship under way, standing out into the southern sea off the harbour mouth.
# Kept well clear of the moored hulls: a phase-3 berth reaches 67 out from the
# quay, and the first spot here read -10 on the audit - inside one.
SHIP_OUT = (284.0, -52.0)
SHIP_LANE = [(284, -52), (300, -92), (314, -134), (326, -176)]

GROUND_SIZE = 640.0
GROUND_SEGS = 250


def _ridge(x0, y0, x1, y1, n, rad, ht):
    """A line of peaks - spaced closer than their radius they read as a range."""
    return [(x0 + (x1 - x0) * k / (n - 1.0), y0 + (y1 - y0) * k / (n - 1.0),
             rad, ht) for k in range(n)]


PEAKS = (
    # The eastern canyon the railhead is buried in, top-right of frame. Three
    # peaks, not two: the extra one sits square behind the mouth so the line
    # starts inside a hillside rather than in front of one.
    [(216, 108, 44, 50), (236, 128, 38, 42), (238, 92, 36, 40)]
    # MESA A, astride the rail at t ~ 0.19: the summit peak sits ON the line so
    # the bore runs through the middle of real rock, and the flankers stand
    # past their own radius from both mouths - lifted ground AT a mouth is what
    # buries a portal, iron's exact lesson.
    + [(142, 167, 42, 40), (178, 196, 36, 32), (108, 214, 34, 30)]
    # MESA B, astride the rail at t ~ 0.5, dead top-centre of the frame.
    + [(3, 203, 40, 38), (60, 236, 32, 28), (-56, 224, 32, 28)]
    # The north-west mesa country the river rises under, behind the depot.
    # It stops north of the river's source - the carve would cut any peak the
    # bed runs through, iron's exact lesson.
    + _ridge(-188, 108, -228, 52, 3, 38, 40)
    # Dry hills filling the south-west corner, past the plant site.
    + _ridge(-150, -140, -84, -184, 3, 34, 30)
    # Sea stacks off the south coast, screen bottom-right - drowned hills
    # where the old shoreline ran, breaking up the open water.
    + [(196, -102, 36, 38), (224, -76, 34, 42)]
)

# ------------------------------------------------------------------- ground
# THIS ISLAND IS DRY. Coal and copper are green, iron is ferruginous red;
# gold country is straw, ochre and quartz - sun-cured grass over pale rock,
# which is what says "different island" before a single building is read.
GROUND_RAMP = [(0.072, 0.054, 0.028),    # deep shade, gully floors
               (0.128, 0.096, 0.048),
               (0.196, 0.150, 0.072),
               (0.268, 0.208, 0.100),
               (0.342, 0.270, 0.134),
               (0.420, 0.340, 0.180)]    # sun-cured tops
GROUND_EARTH = (0.290, 0.190, 0.085)     # tawny cut ground and spoil
GROUND_ROCK = (0.410, 0.385, 0.345)      # pale quartz-bearing country rock
GROUND_SAND = (0.500, 0.430, 0.290)      # the south beaches

# ------------------------------------------------------------------- theme
THEME = "gold"

THEME_SPOTS = {
    # Stamp mill on the rise above the harbour, in the block the sites leave
    # open - the batteries that crushed the quartz, and the reason the port
    # below it exists. Pulled up off the beach: at (64,-92) it stood on sand.
    "works": (56.0, -84.0),
    # Sluice runs on the river's east bank, with the bullion cage beside
    # them - the gold itself, on show the way copper shows its cathode plate.
    "yard": (-204.0, -58.0),
}
THEME_SPILLS = [(-60, 80), (60, 60), (-120, -40), (30, -100), (150, 26),
                (-40, -150)]

# Wind pumps, the dry-country waterworks - lattice tower, rotor and tank.
# One by the town, one out at the store site's block, one above the coast.
THEME_WINDPUMPS = [(48.0, 8.0), (124.0, 98.0), (16.0, -74.0)]
# The old diggings: the field of spoil mounds and abandoned timber on the
# slope between the mine and the quarry - where the rush started before the
# deep mine went in.
THEME_DIGGINGS = (-58.0, 118.0)
# The water flume feeding the sluices, run downhill out of the mesa country.
THEME_FLUME = [(-186.0, -30.0), (-196.0, -44.0), (-202.0, -54.0)]

# Roads that reach a district from a non-arterial direction, for the fences.
APPROACHES = [PORT_ROAD]

# Transmission runs, threaded through the two gaps the works leave.
PYLONS = [[(-56, -96), (-26, -126), (4, -156)],
          [(152, 64), (174, 94), (196, 124)]]

# The tarmac, same grammar as iron: two arterials cut at the gates, the ring
# laid whole.
CARRIAGEWAYS = [(ROAD_X, "main", "Road.X", "arterial"),
                (ROAD_Y, "main", "Road.Y", "arterial"),
                (LOOP_C, "loop", "Road.Ring", "none")]

GATES = gates(DISTRICTS, PAD, SITES, SITE_PAD,
              extra=[(x, y, TOWN_PAD + 5.0) for x, y in TOWNS] + [PORT + (PORT_GATE,)])

# No turning heads, no one-way runs - same as iron, for the same reasons.
HEADS = []
ONEWAY = []

active_sites, locked_sites = site_filters(SITES)
