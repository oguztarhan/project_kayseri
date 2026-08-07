"""Copper island - second map.  Same buildings and vehicles, different land.

Screen mapping is the same as the coal island (ortho iso, yaw 45, elevation 48):
    screen_x = 0.7071*(x + y)          screen_y = 0.7071*(y - x)*0.743 + z*0.669
Visible frame at ortho_scale 380: screen_x in [-190, 190], screen_y in [-127, 127].

It is easiest to reason about this map in the ROTATED frame the camera actually
shows, so most of the geometry below is authored in (p, q) and converted:

    p = (x + y) / sqrt(2)      screen_x = p              (left  <-> right)
    q = (y - x) / sqrt(2)      screen_y = 0.743*q + ...  (down  <-> up)

What makes this island different from the coal map:

  * The OCEAN is on the opposite side - the world NE half-plane (large x+y),
    which reads as the screen RIGHT. On the coal island it is screen left.
  * A RIVER runs the full width of the map through the middle of the town,
    instead of hugging the far western edge. Four carriageways cross it, so
    this is the island with bridges (see ROAD_BRIDGES).
  * The MOUNTAINS moved to the west and south-west and the whole eastern side
    is open water, so the silhouette is a coastal shelf under a range rather
    than coal's bowl of peaks.
  * DEPOT and MARKET swap ends of the north-south arterial, because the market
    has to be the district on the coast - it is the one that feeds the port.
    Both districts are axis-aligned around their own centre and the arterial
    runs through them either way, so their models are untouched.

MINE and REFINERY keep their coal-island positions: the mine's rock face is
modelled facing -X, so the massif has to stay west of it.

    MINE (-X) top-left        MARKET (+Y) top-right, on the coast
    DEPOT (-Y) bottom-left    REFINERY (+X) bottom-right
"""
from math import exp, hypot

from geom import (SQ2, band, crossings, dist_to_path, fence_gaps, gate_point, gates, offset_open,
                  rect_mask, ring,
                  shore_fns, site_filters, smoothstep, straight, trim_arterial,
                  trim_zones)

NAME = "copper"
# Wider than the coal island's 380. The river runs the full width of the frame,
# which takes both horizontal intercardinal slots, so the two unlockable sites
# on the diagonals have to go top-centre and bottom-centre - and screen height
# is the compressed axis (0.5254 per unit against 0.7071). At 380 their pads
# reached +/-143 against a 127 frame and cropped. Preview framing only: the
# exported geometry is in world units and Unity drives its own camera.
ORTHO = 440.0

# Copper ore is malachite green shot through with rust, not black - see the
# island overrides in 01_setup.py. Everything that stockpiles ore asks the
# island for the material name rather than hardcoding "coal".
ORE = "ore_cu"
ORE_SHINY = "ore_cu_shiny"

R = 128.0


def _pq(p, q):
    """(p, q) in the camera-aligned frame -> world (x, y)."""
    return ((p - q) * SQ2, (p + q) * SQ2)


def _sd(s, d):
    """(x+y, y-x) -> world (x, y).  The same frame as _pq, unscaled.

    Handy where a number is easier to read as a raw sum: the shoreline is
    "everything past x + y = 240", not "past p = 170".
    """
    return ((s - d) * 0.5, (s + d) * 0.5)


MINE = (-R, 0.0)
DEPOT = (0.0, -R)
REFINERY = (R, 0.0)
MARKET = (0.0, R)
CENTER = (0.0, 0.0)
DISTRICTS = [MINE, DEPOT, REFINERY, MARKET]
PAD = 36.0

# Graded pad heights. The land tips down from the massif in the west to the
# harbour in the north-east. grade.py reads these off DISTRICT_Z and works out
# which district sits at each end of each arterial, so swapping DEPOT and
# MARKET needs nothing else changed.
#
# The binding constraint is the RING, not the arterials: it crosses from one
# district's arm to the next in a quarter circle (116 units), so no two
# ADJACENT districts may disagree by much more than 10 or that quarter of the
# ring road goes over 20%. Worst pair here is mine (15) against market (5).
MINE_Z = 15.0
DEPOT_Z = 9.0
REFINERY_Z = 6.0
MARKET_Z = 5.0
CENTER_Z = 10.0
PORT_Z = 2.5
DISTRICT_Z = [(MINE, MINE_Z), (DEPOT, DEPOT_Z),
              (REFINERY, REFINERY_Z), (MARKET, MARKET_Z)]

# Unlockable sites. Screen position is the real constraint, not world distance:
# tall terrain and tall props project UPWARD (0.669 per unit of z), so anything
# much past y - x = 200 leaves the top of the frame. That rules out the whole
# world north-west, which is why there is no site on the screen upper-left.
#
# The quarry therefore goes down into the foothills below the range rather than
# up in it - close to the mine, exactly as the coal island's quarry is. It is
# only 104 from the mine, which overlaps feathers a little; that is fine and
# deliberate, and is what the coal map does too (its quarry is 66 out).
SITE_QUARRY = (-130.0, -104.0)  # foothills under the range   phase 2
SITE_STORE = (-110.0, 110.0)    # screen top-centre           phase 2
SITE_PLANT = (110.0, -110.0)    # screen bottom-centre        phase 3
SITES = [("quarry", SITE_QUARRY, 2), ("store", SITE_STORE, 2),
         ("plant", SITE_PLANT, 3)]
SITE_PAD = 26.0

# ------------------------------------------------------------------- roads
ROAD_W = 14.0
ROAD_X = straight((-158.0, 0.0), (196.0, 0.0))
ROAD_Y = straight((0.0, -196.0), (0.0, 196.0))

LOOP_R = 74.0
LOOP_C = ring(LOOP_R)
LOOP = LOOP_C[:-1]
# Where the ring meets each arterial: E, N, W, S. On this island it is just
# +-LOOP_R, but the iron map's loop is not a circle, so everything downstream
# reads these rather than assuming a radius. See geom.ring_meets.
LOOP_MEETS = [(LOOP_R, 0.0), (0.0, LOOP_R), (-LOOP_R, 0.0), (0.0, -LOOP_R)]

# Town centre. The river sweeps through the south-eastern quarter of the ring
# on this island, so the coal island's neat square of yards on the diagonals is
# not available - one of them would sit in the gorge. They are spread around
# the other three quarters instead, two of them sharing the roomy north-west.
#
# Four constraints, all of them binding somewhere:
#   r <= 45                 so the yard's grade FEATHER (pad + 14) stops short
#                           of the ring road - the feather is what matters, not
#                           the slab: at r = 47 it reached the tarmac and put
#                           19% on the ring's western quarter
#   >= 20 from each arterial (12 of yard + 8 of carriageway and shoulder)
#   >= 15 from the river     (12 of yard + margin, against the pinched gorge)
#   >= 28 apart
# At the coal island's yard size of 13 there is no arrangement of four that
# satisfies all of these, so the yards here are a touch smaller.
TOWN_POWER = (20.0, 20.0)     # NE  POWER PLANT
TOWN_HAUL = (-20.0, 40.0)     # N   ORE TRUCKS
TOWN_CIVIC = (-40.0, 20.0)    # W   the town itself
TOWN_FLEET = (-40.0, -20.0)   # SW  CARGO TRUCKS
TOWNS = [TOWN_POWER, TOWN_HAUL, TOWN_FLEET, TOWN_CIVIC]
TOWN_PAD = 12.0

WALK_R = LOOP_R + 9.0
FOOTPATH = ring(WALK_R)


def _spurhead(target):
    d = hypot(target[0], target[1])
    return (LOOP_R * target[0] / d, LOOP_R * target[1] / d)


SPURS = [
    # The quarry spur has to CROSS the river, not chase it. The river runs
    # almost due west along y = -58 through here, and a spur aimed straight at
    # the pit from the ring ran within 14 units of it for 90 units - a road in
    # the gorge. The dog-leg south turns the meeting into a 66-degree crossing,
    # which 03_roads.py then bridges like any other.
    ([_spurhead(SITE_QUARRY), (-66.0, -80.0), SITE_QUARRY], "Spur.Quarry"),
    ([_spurhead(SITE_STORE), (-82.0, 82.0), SITE_STORE], "Spur.Store"),
    ([_spurhead(SITE_PLANT), (82.0, -82.0), SITE_PLANT], "Spur.Plant"),
    # Town cross streets, each linking two yards' facing edges across an
    # arterial. Neither runs south-east: that quadrant of the ring is river.
    ([(-28.0, 20.0), (-10.0, 20.0), (8.0, 20.0)], "Street.TownN"),
    ([(-40.0, -8.0), (-40.0, 0.0), (-40.0, 8.0)], "Street.TownW"),
]

# market -> port haul road, north out of the market pad to the quay
PORT_ROAD = [(4, 166), (10, 178), (15, 190), (18, 198)]


# ------------------------------------------------------------------- railway
# Tunnel high in the western massif, then a long sweep south and east around
# the outside of everything into the depot. It runs entirely SOUTH of the
# river, so the line never needs a bridge - the gorge is the roads' problem.
#
# The tail has to finish at DEPOT + (18, -20): 06_depot.py hangs the intake
# conveyor off L.RAIL[-1] and its silos are placed around that.
RAIL = [(-248, 24), (-250, -12), (-244, -50), (-230, -88), (-210, -124),
        (-182, -156), (-148, -182), (-110, -200), (-70, -206), (-32, -196),
        (-2, -176), (18, -148)]

# ---------------------------------------------------------------------- water
# The river is the headline feature of this island: it rises on the southern
# flank of the massif, runs the full width of the frame, and empties into the
# sea on the right. Authored in (p, q) because in that frame it is simply
# "straight across the screen, with a sag in the middle".
#
#   * the sag (_RIV_BULGE) carries it clear of the central crossroads
#   * it still has to pass INSIDE the ring road, so it crosses the ring twice
#     and both arterials once - four road bridges, see ROAD_BRIDGES
#   * _RIV_HEAD lifts the source up into the mountains at the far left
#
# The bulge is 40 with a 62-wide falloff for a reason: those two numbers set
# where the river meets the arterials, at r = 44. Much less and the gorge eats
# the central crossroads (junction radius 19); much more and it surfaces within
# 20 units of the ring road's own junction with the same arterial.
# _RIV_HEAD and its ramp matter as much as the sag: they decide how much of the
# western half of the island the river sweeps through on its way down from the
# range. Ramped in over p = -60..-180 rather than -100..-215, so the upper
# river stays up on the massif's shoulder and leaves the foothills below it
# free - that strip is where the quarry site goes.
_RIV_P0, _RIV_P1 = -215.0, 196.0
_RIV_BULGE = 40.0
_RIV_FALLOFF = 62.0
_RIV_HEAD = 78.0
_RIV_HEAD_IN, _RIV_HEAD_OUT = -60.0, -180.0


def _river_q(p):
    sag = -_RIV_BULGE * exp(-(p / _RIV_FALLOFF) ** 2)
    head = _RIV_HEAD * smoothstep(_RIV_HEAD_IN, _RIV_HEAD_OUT, p)
    return sag + head


RIVER = [_pq(_RIV_P0 + (_RIV_P1 - _RIV_P0) * k / 25.0,
             _river_q(_RIV_P0 + (_RIV_P1 - _RIV_P0) * k / 25.0))
         for k in range(26)]

# Nominal width, kept for the callers that scatter props by it (11_dressing).
RIVER_W = 12.0
RIVER_CARVE = 24.0
FALLS = (0.16, 0.84)

# Both waterfalls are deliberately OUTSIDE the built middle, and the steady
# fall between them is gentle. With the drops at 0.34/0.68 the bed was 13 to 25
# below the town by the time it reached the bridges, and a 15-unit drop over
# the 6 units of bank the pinched gorge allows is a cliff - flat-shaded, it came
# out as a row of grey teeth down both sides. Now the middle section sits 8-12
# below the town: still a gorge worth bridging, with banks you can read.
RIVER_Z0 = 16.0
RIVER_FALL = 11.4
FALL_DROPS = (9.0, 8.0)

# Roads DO cross this river, so 03_roads.py carries them over it on bridges.
ROAD_BRIDGES = True


def _river_p(t):
    return _RIV_P0 + (_RIV_P1 - _RIV_P0) * max(0.0, min(1.0, t))


def river_w(t):
    """Surface half-width at normalised arc length t."""
    return 10.0 + 6.0 * smoothstep(85.0, 165.0, abs(_river_p(t)))


def river_carve(t):
    """Gorge half-width at t.

    Pinched to a rock notch where the river runs through the built middle of
    the island and opened out to a floodplain at either end. This is what makes
    a river through the middle possible at all: a constant 24-unit carve would
    take the central crossroads out at one end and the market pad at the other,
    and there is no straight line across this map that clears both.
    """
    return 11.0 + 19.0 * smoothstep(85.0, 165.0, abs(_river_p(t)))


# Coastline down the screen right-hand side. Ocean is the side with the LARGER
# x+y - the opposite half-plane to the coal island. The inland bulge up near
# the top is the harbour the port sits in.
# The three points of the harbour face are exactly collinear in world space
# (each is +9,-13 on from the last). That is not tidiness - the port is built
# as a straight quay along PORT_YAW, and on the previous shore it landed on a
# kink in the bay, so the quay came out as a spit with the crane on the grass
# behind it and the container stacks hanging over the water.
SHORE = [_sd(s, d) for d, s in (
    (300, 256), (262, 250), (226, 240),
    (204, 230), (182, 226), (160, 222),      # straight quay face - the harbour
    (136, 230), (110, 240), (80, 248), (40, 252), (0, 254),
    (-45, 252), (-92, 248), (-140, 244), (-190, 240), (-240, 238),
    (-292, 240))]
SEA_AXIS = (SQ2, SQ2)            # unit vector pointing out to sea
SEA_Z = -3.0
SEA_DEEP = -17.0

sea_depth = shore_fns(SHORE, SEA_AXIS[0], SEA_AXIS[1])

# Port in the harbour bay, up-shore of the market. Its pad reaches y = 184 and
# the market's stops at 165, so there is 19 units of clear ground between them
# - the coal island had this at 4.8 and it read as one continuous industrial
# smear.
PORT = (18.0, 200.0)             # mid-quay, set 6 back off the waterline
# Further out than coal's 26: this quay runs down-shore across the road's
# approach, so the gate has to clear the end crane and the container yard as
# well as the apron. At 33 the haul road is gone entirely on this island, which
# is right - the market pad and the quay apron are only 66 apart here, so what
# was left between them was a four-metre stub of tarmac between two paved
# surfaces. The trucks still drive the exported centreline across both.
PORT_GATE = 33.0
PORT_YAW = -0.9651               # atan2(-13, 9) - the straight face above
PORT_APRON = (-13.0, -9.0)       # landward along the quay's normal
# Ship under way, standing out to sea. It has to leave the harbour PAST the
# berths, not through them: ships moor bow-out along the finger piers, so at
# phase 3 a 60-unit hull reaches 67 units off the quay, and this used to sit
# 47 out on the same line - a 15-unit interpenetration with the outer berth.
# Set 42 along the quay instead, which clears the outermost hull by 15 and
# still lands inside the frame (screen_x 213 against a 220 half-width).
SHIP_OUT = (37.0, 264.0)
SHIP_LANE = [(37, 264), (71, 298), (105, 332), (139, 366)]

GROUND_SIZE = 640.0
GROUND_SEGS = 250

# Peaks combine with max(), not sum - see 02_terrain. Screen height is
# 0.5254*(y - x) + 0.669*z, so anything above y - x = 190 with a 50-unit summit
# runs off the top of the frame; those entries are deliberate backdrop.
PEAKS = [
    # Massif the mine is cut into. Clear of the mine pad, which flattens the
    # ground out to x = -171.
    (-198, -6, 26, 52), (-214, -30, 26, 56), (-196, -46, 25, 50),
    (-220, -62, 25, 52), (-198, -80, 25, 48), (-210, -96, 24, 46),
    (-176, -66, 24, 44), (-232, -14, 25, 50), (-186, -104, 24, 42),
    # South-west arm, inside the sweep of the railway - frames the depot and
    # stands behind the quarry pit without crowding it.
    (-178, -128, 24, 42), (-152, -160, 24, 38), (-92, -158, 23, 34),
    # Beyond the railway, running off the bottom-left of the frame.
    (-120, -252, 24, 34), (-58, -264, 24, 32), (4, -266, 24, 32),
    (66, -254, 24, 30),
    # Headland along the screen bottom, south of the refinery.
    (86, -176, 24, 32), (176, -84, 24, 32), (208, -50, 23, 30),
    (232, -14, 23, 26),
    # Low ridge along the screen top, between the mine and the market. Mostly
    # backdrop - it is above the frame line and only its flanks show.
    (-166, 116, 24, 30), (-146, 176, 23, 26), (-84, 196, 23, 24),
    (-46, 210, 23, 22),
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
THEME = "copper"

# See the note in isle_coal.py: open ground between the ring and a district,
# clear of the roads, the rail, the river and the shore.
THEME_SPOTS = {
    "works": (96.0, -52.0),     # leach ponds, between refinery and plant
    "yard": (-106.0, 48.0),     # cathode plate and slag pots, up by the mine
}
THEME_SPILLS = [(-91, -101), (53, -133), (51, 140), (48, -76), (-26, 84),
                (117, 54)]

active_sites, locked_sites = site_filters(SITES)
