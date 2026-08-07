"""Iron island - third map, built to the reference town plan.

Screen mapping is the same as the other two (ortho iso, yaw 45, elevation 48):
    screen_x = 0.7071*(x + y)          screen_y = 0.7071*(y - x)*0.743 + z*0.669
Visible frame at ortho_scale 420: screen_x in [-210, 210], screen_y in [-140, 140],
i.e. x+y within +/-297 and 0.5254*(y-x) + 0.669*z within +/-140.

THE PLAN. A HEXAGONAL ring road in the middle of the island with SEVEN arms off
it, each running out to one works, and forest filling the blocks between them:

    ring corner W  -> MINE          ring corner 120 -> QUARRY
    ring corner E  -> REFINERY      ring corner  60 -> STORE
    ring edge   N  -> DEPOT         ring corner 300 -> PLANT
    ring edge   S  -> MARKET, and on past it to the harbour

Coal and copper are a CIRCLE with a cross through it. This is a HEXAGON with
seven radial arms, which is what makes it read as a laid-out industrial estate
rather than a roundabout: every arm meets the ring square on, at a corner or at
the middle of a straight side, and the blocks between them are proper wedges
rather than quarter-circles.

Nothing here needs the shared scripts changed. They stopped assuming the ring
was a circle when this island first tried a circuit(): the road is a PATH and
its four arterial junctions are MEASURED off it - see geom.axis_meets.

The island is bigger than coal (R 132 against 128, ortho 420 against 380), which
is what buys room for the three site arms and for the railway to run outside
everything on the east.
"""
from math import hypot

from geom import (SQ2, axis_meets, band, circuit, crossings, dist_to_path,
                  fence_gaps, gate_point, gates, island_fns, offset_closed,
                  offset_open, rect_mask, shore_fns, site_filters, smoothstep,
                  straight, trim_arterial, trim_zones)

NAME = "iron"
# 460, not coal's 380. This island carries three more roads and four more pads
# than coal does, and at 420 the mine's headframe, the depot's north end, the
# eastern massif and the outbound ship all clipped an edge. The frame is
# screen_x in +/-230 and screen_y in +/-153, i.e. x+y within +/-325 and
# 0.5254*(y-x) + 0.669*z within +/-153.
ORTHO = 460.0

ORE = "ore_fe"
ORE_SHINY = "ore_fe_shiny"

R = 132.0

# The four works sit on the world cardinal axes, so they land in the four screen
# quadrants and the two arterials are the world axes - same as coal. The screen
# is 2:1 in (x+y) against (y-x), so R is capped by the two districts that land
# on the screen VERTICAL: the mine's pad corner reaches y-x = R + 72, which at
# MINE_Z has to stay under 246. At R = 132 it lands at 204, with 42 to spare for
# the spoil heaps that stand outside the pad.
MINE = (-146.0, 0.0)
DEPOT = (0.0, 148.0)
REFINERY = (128.0, 0.0)
MARKET = (0.0, -128.0)
CENTER = (0.0, 0.0)
DISTRICTS = [MINE, DEPOT, REFINERY, MARKET]
PAD = 36.0

# Graded height of each district pad - read by grade.py, which builds the
# arterial profiles from whichever district sits at each end of each road.
MINE_Z = 16.0
DEPOT_Z = 13.0
REFINERY_Z = 5.0
MARKET_Z = 5.0
CENTER_Z = 10.0
PORT_Z = 3.0
DISTRICT_Z = [(MINE, MINE_Z), (DEPOT, DEPOT_Z),
              (REFINERY, REFINERY_Z), (MARKET, MARKET_Z)]

# ------------------------------------------------------------------- the ring
ROAD_W = 14.0

# NOT A SHAPE. Seven corners, none of them derived from an angle or a radius -
# no two sides the same length, no two turns the same, nothing symmetric about
# either axis. The first version of this island generated its corners off
# cos/sin at 60 degree steps and it came out reading as a hexagon dropped on the
# map, because that is exactly what it was.
#
# Written as corners rather than as a curve because that is how a road gets
# built: it runs straight until it has a reason to turn. circuit() lays the
# straights and rounds each turn, so what comes out is a road that happens to
# come back to itself, not a polygon.
#
# The only constraints are structural: it has to cross both arterials once on
# each side (axis_meets finds those four junctions), and its narrowest point has
# to clear the town yards inside it. Its sides run 66 to 94 long and sit 69 to
# 83 out from the middle.
LOOP_CORNERS = [(84.0, 10.0), (20.0, 70.0), (-66.0, 40.0), (-64.0, -34.0),
                (24.0, -62.0)]
LOOP_R = 74.0                    # nominal only - nothing downstream reads it
# FIVE corners and a SHORT fillet, both for the same reason. Seven corners with
# a 20 fillet left only 13 units of straight on a 53-unit side, so the curves
# joined up and the whole thing came out as a circle - the one shape this island
# is not allowed to be. Five sides of 74 to 94 with a 12 fillet are 74% straight,
# so what reads is a road that runs, turns, and runs again.
LOOP_C = circuit(LOOP_CORNERS, fillet=12.0)
LOOP = LOOP_C[:-1]
# MEASURED, not assumed. A filleted corner pulls the tarmac a quarter of the
# fillet back off the corner itself, so the east and west junctions are at 73
# and not 78, while the north and south ones sit on the flat at 67.6. Everything
# downstream - the crosswalks, the ring's height profile, the route anchors -
# reads these rather than a radius.
LOOP_MEETS = axis_meets(LOOP_C)

# Pedestrian circuit, concentric with the ring road and OUTSIDE it, so the
# pavement is the ring's outer footway rather than a second loop cutting across
# the middle of town.
# Offset off the road itself rather than drawn as a second, larger ring: an
# irregular circuit scaled up is not a constant distance from the original, so
# the pavement would crowd the road on the short sides and wander off it on the
# long ones.
WALK_R = LOOP_R + 9.0
FOOTPATH = offset_closed(LOOP_C, 9.0)

# ------------------------------------------------------------------- arterials
# The two through routes, on the world axes. trim_arterial cuts them at the
# works gates and keeps the run between, so the ends past each district are
# never drawn - they only have to reach far enough into each gate zone to be
# cut there.
ROAD_X = straight((-200.0, 0.0), (200.0, 0.0))
ROAD_Y = straight((0.0, -200.0), (0.0, 200.0))

# ------------------------------------------------------------------- the sites
# Placed in the three blocks the districts and the railway leave open, at
# whatever distance out that block allows - 88, 132 and 147 - rather than all
# three on one radius. Each is 62 or more clear of the district pads either side
# of it, which is the sum of the two half-extents.
SITE_QUARRY = (-124.0, 82.0)    # the block between the mine and the depot
SITE_STORE = (74.0, 88.0)       # between the depot and the refinery, inside the rail
SITE_PLANT = (104.0, -104.0)    # between the refinery and the market
SITES = [("quarry", SITE_QUARRY, 2), ("store", SITE_STORE, 2),
         ("plant", SITE_PLANT, 3)]
SITE_PAD = 26.0

# ------------------------------------------------------------------- the town
# Four yards on the diagonals, one per quadrant, each driven by its own station -
# see 15_town.py. Inside the ring, clear of both arterials and of the hexagon's
# nearest side.
TOWN_POWER = (30.0, 22.0)     # NE  POWER PLANT
TOWN_HAUL = (-24.0, 28.0)     # NW  ORE TRUCKS
TOWN_FLEET = (-26.0, -21.0)   # SW  CARGO TRUCKS
TOWN_CIVIC = (22.0, -24.0)    # SE  the town itself
TOWNS = [TOWN_POWER, TOWN_HAUL, TOWN_FLEET, TOWN_CIVIC]
TOWN_PAD = 13.0


def _yard_arm(junction, elbow, site):
    """A site arm: out of a ring junction, one bend, then square into the yard.

    The last leg is deliberately axis-aligned. gates() cuts a road at a RADIUS
    but a site pad is a SQUARE of the same half-extent, so its corners reach 37
    where the gate cuts at 26 - an arm that arrives diagonally runs its last 11
    units underneath the yard slab, which the audit reports as a blocked road.

    Built from straight() runs rather than three loose points, because a bare
    corner is bowed up to 18 units wide by the Catmull-Rom in sample_bez.
    """
    return straight(junction, elbow, 5) + straight(elbow, site, 4)[1:]


SPURS = [
    # The three site arms. Each leaves the ring at a different kind of place -
    # one at a corner, two part-way along a side - and each bends once, the way
    # a road does when it has to get round something.
    # Each leaves part-way along a side, not at a corner - a works road joins the
    # trunk where the works is, not where the trunk happens to turn.
    (_yard_arm((-40.0, 49.0), (-124.0, 60.0), SITE_QUARRY), "Spur.Quarry"),
    (_yard_arm((55.0, 37.0), (74.0, 54.0), SITE_STORE), "Spur.Store"),
    (_yard_arm((36.0, -48.0), (104.0, -70.0), SITE_PLANT), "Spur.Plant"),
    # Town-centre cross streets. Each runs yard to yard across the north-south
    # arterial - trimmed at both yard gates, so what gets drawn is the stretch
    # between them - and each dips slightly on its way, which is what keeps
    # Street.TownN off the haul yard's hopper on its way out of the gate.
    # Their MIDDLE point has to be the one on the arterial: 03_roads reads
    # p[1] to decide where to break the centre line for the junction.
    ([(-24.0, 28.0), (0.0, 28.0), (30.0, 22.0)], "Street.TownN"),
    ([(-28.0, -22.0), (0.0, -26.0), (22.0, -24.0)], "Street.TownS"),
]

# market -> port haul road, running out to the quay.
#
# It approaches the port from the LANDWARD side, north-east of it, rather than
# straight along the shore. PORT_YAW puts the quay on the (-1, 1) axis, so the
# port's structures spread north-west AND south-east of its centre - and the
# south-east arm is exactly where a road coming along the coast from the market
# wants to be. The first version ran under Port.Crane0, a 29.5-unit gantry.
PORT_ROAD = [(-20.0, -146.0), (-56.0, -136.0), (-90.0, -118.0), (-116.0, -100.0)]

# ------------------------------------------------------------------- railway
# Down the EAST side of the island, outside every works, from a railhead buried
# in the hills at the screen bottom-right up into the depot at the top.
#
# The reason it is here and not anywhere else: this is the only stretch of the
# map wide enough for a mountain the train can go THROUGH and still be watched
# doing it. Everything else is either behind a works or off the frame edge.
#
# Eleven control points, not five. A long curve described by few points comes
# out of Catmull-Rom as a series of kinks; this reads as surveyed.
# The line stops OUTSIDE the depot yard, not in the middle of it. 04_rail builds
# the engine shed on the last few units of track, and with the terminus at
# (10, 164) - 20 units inside a pad that reaches y = 184 - that shed was built
# straight through Depot.Shed1, its two stock piles and the main conveyor. The
# track now ends at (44, 173), 8 clear of the pad's east edge, so the shed stands
# beside the yard and the siding runs in to it.
RAIL = [(230, 6), (216, 30), (200, 54), (184, 78), (168, 102), (150, 124),
        (130, 144), (110, 158), (90, 168), (70, 174), (62, 174), (54, 172)]

# Two bores. The first buries the railhead so the line starts inside rock rather
# than in the middle of a field. The SECOND is the one the island is built
# around: a through-tunnel under the eastern massif, entering at screen
# (181, -37) and leaving at (178, -4) - both well inside the frame, with 100
# units of open track before it and 105 after. Every earlier attempt put a mouth
# at a frame edge, where the player could not see it happen.
# The second span's ends are SOLVED, not guessed. The massif is 42 tall over a
# 44 radius, so its surface stands 12 above the track at 30 units from the
# summit; the line is 30 out at t = 0.39 going in and t = 0.62 coming out.
# These are arc-length fractions, so shortening the tail at the depot end moved
# both - they are re-derived whenever RAIL changes, never carried over.
# 02_terrain cuts the corridor flat right up to those points and leaves the rock
# alone past them, so each mouth gets a face to stand in instead of a slope.
TUNNEL = [(0.0, 0.08), (0.39, 0.62)]

# HAND-PLACED PORTAL OFFSETS, in world units, applied on top of what bore()
# computes. The generator puts each mouth on the line at its own tunnel-span
# boundary, which is right to a few units - but it cannot know where the rock
# face actually lands once every peak in PEAKS has been summed and the rail
# cutting has taken its bite. These are the nudges that seat each portal in its
# hillside, measured off positions set by hand in Blender.
#
# They are DATA, not a one-off fix in the scene. A hand-moved object lives only
# in the .blend and the next build silently reverts it - which is exactly what
# happened once already. Kept here, all three phases get the same portals.
#
# Order matches the mouths bore() builds: railhead first, then entrance and exit
# for each through-bore in TUNNEL[1:].
# X AND Y ONLY. The z values these were first written with (1.11 and 1.22) were
# an artefact of measuring against grade.road_z, which is the ROAD surface;
# bore() sets its height from the rail's own two-knot profile, and that was
# already correct. Adding a z on top lifted both mouths clear of the track.
PORTAL_NUDGE = [(2.97, -5.17, 0.0),       # railhead
                (15.69, -21.70, 0.0),     # through-tunnel, entrance
                (25.93, -23.83, 0.0)]     # through-tunnel, exit

# ---------------------------------------------------------------------- water
# Rises in the massif behind the mine, runs down the west side and drains into
# the sea. It crosses no carriageway, so nothing has to be carried over it.
#
# The bed profile has to sit BELOW the ground it crosses. 02_terrain BLENDS
# terrain towards it, so a bed above the land lifts a channel of water into the
# air instead of cutting a gorge - which is what put a sheet of water in the sky
# on two earlier versions of this island. These are coal's numbers, on a river
# with coal's shape: high ground at the source, sea level at the mouth.
RIVER = [(-212, 26), (-218, -6), (-220, -40), (-218, -74), (-210, -108),
         (-198, -142)]
RIVER_W = 13.0
RIVER_CARVE = 24.0
FALLS = (0.30, 0.66)

RIVER_Z0 = -3.0
RIVER_FALL = 8.0
FALL_DROPS = (4.2, 4.8)

ROAD_BRIDGES = False


def river_w(t):
    """Surface half-width at normalised arc length t."""
    return RIVER_W


def river_carve(t):
    """How far either side of the centreline the gorge is cut at t."""
    return RIVER_CARVE


def _sd(s, d):
    """(x+y, y-x) -> world (x, y).  The screen frame: s is right, d is up."""
    return ((s - d) * 0.5, (s + d) * 0.5)


# Shoreline down the screen LEFT: a band of near-constant s, so the coast runs
# vertically on screen. The landward bulge at d = -44 is the harbour bay, which
# is where the port sits.
SHORE = [_sd(s, d) for s, d in (
    (-274, 330), (-278, 250), (-282, 170), (-272, 100),
    (-266, 30), (-272, -40), (-278, -120), (-276, -200),
    (-272, -270), (-268, -340))]
SEA_AXIS = (-SQ2, -SQ2)          # unit vector pointing out to sea
SEA_Z = -3.0
SEA_DEEP = -17.0

# One coast, down the screen left, same as the other two maps. geom.island_fns
# and a closed SHORE will make this water go all the way round if it is ever
# wanted again - the ring version of this coastline is a straight swap.
sea_depth = shore_fns(SHORE, SEA_AXIS[0], SEA_AXIS[1])

# Port in the bay down-shore of the market, 92 clear of the market pad so the
# haul road is a real road and not a shared fence line.
# 141 down-shore of the market, not 91. The market's gate is 36 and the port's
# is 44, so at 91 apart the two zones left 11 units of haul road between them -
# and the phase-3 shed and market shop, which stand right at their own gates,
# met in the middle of it.
PORT = (-131.0, -101.0)
# Where the haul road hands over to the quay apron. 44, not coal's 26: this
# port's shed, container stacks and reach-stacker stand up to 30 out from its
# centre on the landward side, and the road is 12 wide, so anything under 42 put
# tarmac beneath a 12-unit shed. The apron takes over from here in.
PORT_GATE = 44.0
PORT_YAW = 2.3561944902          # quay runs along the shore, i.e. (-1, 1)
PORT_APRON = (12.0, 9.0)
# Ship under way, heading off-screen. Kept 76 off the moored hull: a phase-3 one
# reaches 67 out from the quay, and at anything closer the two sail through each
# other.
SHIP_OUT = (-206.0, -86.0)
SHIP_LANE = [(-206, -86), (-236, -116), (-266, -146), (-296, -176)]

GROUND_SIZE = 640.0
GROUND_SEGS = 250


def _ridge(x0, y0, x1, y1, n, rad, ht):
    """A line of peaks. Spaced closer than their own radius they combine with
    max() into a continuous rise with a scarp on each side - see the README."""
    return [(x0 + (x1 - x0) * k / (n - 1.0), y0 + (y1 - y0) * k / (n - 1.0),
             rad, ht) for k in range(n)]


# Every peak stands clear of every pad by its own radius plus the pad's half
# extent plus 15, which is what stops a works being built into solid rock. The
# ranges fill the four corners the road plan leaves empty and nothing sits
# between the works, so the eye reads the gates without competition.
PEAKS = (
    # THE EASTERN MASSIF, sitting astride the railway at t = 0.5. This is the
    # landmark the line exists to go through: its summit lands at screen
    # (178, 8), dead centre-right, so the train runs in one side and out the
    # other in full view. Radius 40 rather than 66 - the refinery is 96 away and
    # the storage yard 88, and a bigger hill would be built into one of them.
    # Its summit sits 14 off the line, so the bore runs through the middle of the
    # rock and not along a flank. TUNNEL[1] is measured off this radius.
    # Height 34 over radius 44, not 56 over 42. A peak that rises 46 units in 42
    # is a spike, and a spike has no FACE: the rock climbed away from the track
    # so fast that the portal - which is set at rail height - ended up buried
    # inside the hillside instead of standing in it. At this slope the bore has
    # a face to open onto, and 22 units of rock still sit over the train.
    # ONE peak on the line and the rest off it. The flankers used to sit at
    # (180, 88) and (144, 140), 7 and 17 from the track - which put 30 units of
    # ground exactly where the entrance portal stands, and a portal is 24 tall,
    # so it was buried. These two are 42 and 45 out, past their own radius, so
    # they give the massif its bulk without lifting the ground at either mouth.
    [(159, 113, 44, 42), (204, 124, 40, 34), (188, 148, 38, 30)]
    # THE RAILHEAD HILL, centred 24 units BEYOND RAIL[0] along the line's own
    # bearing rather than parked near it. That is what puts the portal properly
    # inside a hillside instead of standing in front of one.
    + [(242, -15, 44, 52), (258, -6, 38, 44)]
    # the rise between the plant and the south coast
    + [(112, -172, 36, 40), (166, -146, 36, 38)]
    # The massif behind the mine, on the screen upper-left. It used to run down
    # to y = -36, where the river carved straight through it: RIVER_CARVE cuts
    # 24 either side of the bed down to bed level, which took a 40-unit peak to
    # -11.7 - a hole in the coast where a headland should be. It now stops where
    # the river rises, and the river runs off its southern end instead.
    + _ridge(-206, 16, -226, 72, 4, 34, 44)
    # the range across the screen top, behind the quarry and the depot
    + _ridge(-152, 138, -56, 194, 3, 40, 46)
    # the headland framing the harbour, screen bottom-left
    + _ridge(-40, -210, 46, -232, 3, 34, 38)
)

# ------------------------------------------------------------------- ground
# THIS ISLAND IS RED. 02_terrain paints the ground from a five-stop ramp, shade
# in the hollows to sun on the tops, and coal and copper both run it in green.
# Iron country is ferruginous - ore-bearing laterite weathers to rust and ochre -
# so the same ramp is rebuilt in reds, which is the one change that makes the
# map read as somewhere else at a glance rather than as the green island again.
#
# Same structure, same ordering, only the hue moves: 02_terrain's slope and
# height shading is untouched.
GROUND_RAMP = [(0.062, 0.026, 0.018),    # deep shade, hollows and gully floors
               (0.108, 0.048, 0.028),
               (0.170, 0.078, 0.040),
               (0.245, 0.118, 0.056),
               (0.322, 0.168, 0.078),
               (0.395, 0.232, 0.112)]    # sun on the exposed tops
GROUND_EARTH = (0.230, 0.110, 0.050)     # bare cut and spoil
GROUND_ROCK = (0.330, 0.268, 0.238)      # country rock, warmer than the others
GROUND_SAND = (0.430, 0.318, 0.196)      # the beaches, now all the way round

# ------------------------------------------------------------------- theme
THEME = "iron"

THEME_SPOTS = {
    # Blast furnace, in the block east of the ring. 48 off the ring road and 34
    # off the storage yard's arm: the outcrops that come with it scatter up to 28
    # out, and at (100, 34) one of them landed on the kerb.
    "works": (118.0, 44.0),
    "yard": (-96.0, -58.0),     # ingot stacks and the slag bank, above the port
}
THEME_SPILLS = [(-100, 18), (40, -100), (72, 76), (-40, -166), (150, 40),
                (-30, 190)]

# Every road that reaches a district, so its fence knows where to leave a
# gate - see geom.fence_gaps. The arterials are not listed: they end at the
# gate point every district script already opens for.
APPROACHES = [PORT_ROAD]

# Transmission lines, as runs of pylon feet. Per island because they have to
# thread between the works, and no two islands put those in the same place.
PYLONS = [[(120, -40), (150, -70), (180, -100)],
          [(-70, 128), (-44, 162), (-18, 196)]]

# THE TARMAC, as opposed to the truck ROUTES.
#
#   (points, width key, name, trim mode)
#   trim "arterial"  cut at the works gates, keep the run through the middle
#        "gated"     cut at the works gates, keep every run
#        "none"      lay it whole
CARRIAGEWAYS = [(ROAD_X, "main", "Road.X", "arterial"),
                (ROAD_Y, "main", "Road.Y", "arterial"),
                (LOOP_C, "loop", "Road.Ring", "none")]

# Where the tarmac stops - see geom.gates. Town yards get 2 units of clearance
# on top of their pad so a cross street ends at the kerb rather than against the
# gate itself; the districts and sites are cut exactly at the pad, which is also
# the edge of the yard slab that takes over inside.
GATES = gates(DISTRICTS, PAD, SITES, SITE_PAD,
              extra=[(x, y, TOWN_PAD + 5.0) for x, y in TOWNS] + [PORT + (PORT_GATE,)])

# No turning heads: a bulb of tarmac in front of every works read as a
# roundabout dropped in a field. Each arm runs into the gate and the yard slab
# takes over, and the trucks turn inside the yard - which is where they were
# going anyway.
HEADS = []

# No one-way runs: every road on this island carries both directions.
ONEWAY = []

active_sites, locked_sites = site_filters(SITES)
