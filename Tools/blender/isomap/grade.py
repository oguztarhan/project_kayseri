"""Height field for the built environment - the one place ground level is decided.

Before this existed, 02_terrain.py flattened the ground to exactly z=0 under every
road, pad and site, and every district script hardcoded its props at z=0.3. That
made the whole island a pancake and left Unity assuming a single flat deck plane.

Two functions matter:

    pad_z(x, y)   the flat height of the district/site pad containing the point
    road_z(x, y)  the graded surface - equals pad_z inside a pad, ramps between

Callers must sample road_z at the SAME positions the mesh builder does, or the
exported centreline drifts off the visible tarmac - see strip(zfun=) in lib.py and
road_path() in 14_routes.py, which both sample at sample_bez() points.

Height is authored ALONG THE TWO ARTERIALS, as a profile against normalised arc
length, and everything else interpolates from them by distance. That ordering
matters: scattering control heights at the districts instead and hoping the roads
between them came out drivable capped the whole map's relief at the tightest
pinch on it - the mine, market, port and quarry pads all crowd the south-west
corner, and holding the loop there under 12% forced the total relief down to five
units, which is invisible at this camera. Driving the field from the roads puts
the grade under direct control, so the map can have 20 units of relief and still
have nothing steeper than a real hill road.

The loop, the spurs and the port road are deliberately NOT given profiles. They
adopt whatever the arterial field gives them, which is what keeps every junction
consistent without hand-matching heights at each crossing.
"""
from math import hypot

import layout as L

# Authored heights. High in the west against the massif, falling away east to
# the refinery. The spread is set by the ramp lengths below: a district pad is
# 80 units across and the centre flat 28, which leaves ~75 units of ramp on each
# arm, so roughly 7 units of height per arm is what keeps them under 10%.
# The spread is capped by the RING, not by the arterials. The ring crosses from
# the mine's arm to the market's in a quarter circle - 116 units - so whatever
# those two disagree by has to fit in that quarter. At the old 18/4 it was a 12.5
# unit drop, which put 24% on the south-west quarter of the ring road.
MINE_Z = 16.0
DEPOT_Z = 13.0
CENTER_Z = 10.0
REFINERY_Z = 5.0
# Also low enough that the harbour road's descent to the quay stays drivable:
# the market and port pads are only ~20 units apart, so every unit between them
# costs about 5% of gradient on that short ramp.
MARKET_Z = 5.0
# The port must sit low: its quay wall is built downward from the apron to
# SEA_Z, and letting it inherit the surrounding field put the apron at 11, so
# the harbour road climbed to reach the water and the wall was 14 units tall.
# Sea level is -3, so this is still a 6-unit quay wall, which is normal for a
# cargo berth. Lower values made the harbour road descend 4.5 units across the
# ~20 units between the market and port pads - a 30% ramp to the water.
PORT_Z = 3.0

# How fast the field falls away from a road, in units squared. Larger = the
# arterials' influence carries further before the two blend into each other.
_FALLOFF = 90.0


def _t_at(path, px, py):
    """Normalised arc length of the point on `path` nearest (px, py)."""
    return L.dist_to_path(px, py, path)[1]


def _profile(knots, t):
    """Smoothstep through (t, z) knots. Flat outside the first and last."""
    if t <= knots[0][0]:
        return knots[0][1]
    for i in range(len(knots) - 1):
        t0, z0 = knots[i]
        t1, z1 = knots[i + 1]
        if t <= t1:
            if t1 - t0 < 1e-9:
                return z1
            return z0 + (z1 - z0) * L.smoothstep(t0, t1, t)
    return knots[-1][1]


# Flat rectangles. Half-extents are 40, not layout.PAD's 36: the district ground
# slabs are box((72, 68)) and the mine's is offset CX+2, so it spans CX-34..CX+38,
# and fences sit at +/-35..36. At 36 those last few units hung over the slope.
PAD_HW = 40.0
PAD_HH = 37.0
PAD_FEATHER = 26.0
# Matches layout.SITE_PAD, so the height a site is pinned to covers the same
# ground the terrain flattens. The feather is tighter than a district's because
# the quarry sits only 121 out: at 22 its influence reached the ring road with
# 28% weight and put a step across it. 16 keeps the total reach at 42, where it
# was before the flat part was widened.
SITE_HW = 26.0
SITE_FEATHER = 16.0
CENTER_HW = 14.0

# Knot positions are measured off the paths rather than written by hand, so they
# stay correct if layout moves a district or extends a road.
#
# Each flat run spans the whole PAD, not just the district centre. Flattening
# only at the centre left the profile 2.6 units above what the pad insisted on
# by the time it reached the pad's edge, and the two fought across the feather.
_X = L.ROAD_X
_Y = L.ROAD_Y
PROFILES = [
    (_X, [(0.0, MINE_Z),
          (_t_at(_X, L.MINE[0] + PAD_HW, L.MINE[1]), MINE_Z),
          (_t_at(_X, L.CENTER[0] - CENTER_HW, L.CENTER[1]), CENTER_Z),
          (_t_at(_X, L.CENTER[0] + CENTER_HW, L.CENTER[1]), CENTER_Z),
          (_t_at(_X, L.REFINERY[0] - PAD_HW, L.REFINERY[1]), REFINERY_Z),
          (1.0, REFINERY_Z)]),
    (_Y, [(0.0, MARKET_Z),
          (_t_at(_Y, L.MARKET[0], L.MARKET[1] + PAD_HW), MARKET_Z),
          (_t_at(_Y, L.CENTER[0], L.CENTER[1] - CENTER_HW), CENTER_Z),
          (_t_at(_Y, L.CENTER[0], L.CENTER[1] + CENTER_HW), CENTER_Z),
          (_t_at(_Y, L.DEPOT[0], L.DEPOT[1] - PAD_HW), DEPOT_Z),
          (1.0, DEPOT_Z)]),
]


def _blend_profiles(x, y, profiles):
    num = 0.0
    den = 0.0
    for path, knots in profiles:
        d, t = L.dist_to_path(x, y, path)
        w = 1.0 / (d * d + _FALLOFF)
        num += w * _profile(knots, t)
        den += w
    return num / den if den else 0.0


def _arterial_z(x, y):
    return _blend_profiles(x, y, PROFILES)


# The loop needs a profile of its own. Left to interpolate between the two
# arterials it sat equidistant from both at its corners, where they disagree by
# up to 19 units (mine 22 against market 3), and the blend weight flipping over
# a few metres put a 59% slope on the south-west corner.
#
# Its knots are read off the arterial field at the four points where it crosses
# them - which, now that the ring is a circle, are exactly control points 0, 4, 8
# and 12 - so the junctions agree with the arterials without being hand-matched,
# and each quarter interpolates over 116 units, which is gentle.
#
# Four knots and not sixteen: a knot at every control point faithfully reproduced
# the arterial field's own variation, including the part where the mine's arm and
# the market's arm disagree, and that came out at 24% on the ring.
_CROSS = [(L.LOOP_R, 0.0), (0.0, L.LOOP_R), (-L.LOOP_R, 0.0), (0.0, -L.LOOP_R)]
LOOP_KNOTS = [(i * 0.25, _arterial_z(px, py)) for i, (px, py) in enumerate(_CROSS)]
LOOP_KNOTS.append((1.0, LOOP_KNOTS[0][1]))

PROFILES_MAIN = PROFILES + [(L.LOOP_C, LOOP_KNOTS)]


def _main_z(x, y):
    return _blend_profiles(x, y, PROFILES_MAIN)


def _spine_z(x, y):
    """Distance-weighted blend of every road profile."""
    return _blend_profiles(x, y, PROFILES_ALL)


# Each pad is pinned to the arterial height at its own centre, so the road
# running through it is already at the pad's level and the two cannot step
# against each other. The port and the sites inherit the same way.
PADS = [
    (L.MINE[0] + 2.0, L.MINE[1], PAD_HW, PAD_HH, PAD_FEATHER, MINE_Z),
    (L.DEPOT[0], L.DEPOT[1], PAD_HW, PAD_HW, PAD_FEATHER, DEPOT_Z),
    (L.REFINERY[0], L.REFINERY[1], PAD_HW, PAD_HW, PAD_FEATHER, REFINERY_Z),
    (L.MARKET[0], L.MARKET[1], PAD_HW, PAD_HW, PAD_FEATHER, MARKET_Z),
    # Half-extent plus feather has to stay under 40 in y: the loop's south side
    # runs at y = -73, exactly 40 from the quay, and a pad reaching past it put
    # a 4.5-unit step across the road and took the loop from 16% to 32%.
    (L.PORT[0], L.PORT[1], 22.0, 18.0, 20.0, PORT_Z),
] + [(sx, sy, SITE_HW, SITE_HW, SITE_FEATHER, _main_z(sx, sy))
     for _n, (sx, sy), _need in L.SITES
] + [(tx, ty, L.TOWN_PAD, L.TOWN_PAD, 14.0, _main_z(tx, ty))
     for tx, ty in L.TOWNS]

_PAD_SHARPNESS = 2.0


def _pinned_z(x, y):
    """Height a connector must meet at (x, y): the pad's if inside one."""
    for cx, cy, hw, hh, _f, h in PADS:
        if abs(x - cx) <= hw and abs(y - cy) <= hh:
            return h
    return _main_z(x, y)


# Spurs and the port road get two-knot profiles of their own, running between
# whatever the roads they join are already at. Without one they simply sampled
# the surrounding field, which meant they inherited its CROSS-slope: strip()
# builds a ribbon with both edges at the same z, so a road on a side-slope has
# one edge buried and the other floating. Spur.Store sat on a 22% cross-slope.
def _two_knot(pts):
    return (pts, [(0.0, _pinned_z(*pts[0])), (1.0, _pinned_z(*pts[-1]))])


# Town streets are left out. dist_to_path clamps t, so a connector throws its
# height sideways for CONN_R + CONN_FEATHER - 33 units - and the two 34-unit
# streets at y = +/-30 sit close enough to the crossroads to do that to the
# north-south arterial, compressing its climb into half the road and putting 29%
# on it. They are short, they run across the flattest part of the map, and
# strip() banks each ribbon per edge vertex, so they lose nothing by simply
# taking the surrounding field.
CONNECTORS = [_two_knot(pts) for pts, name in L.SPURS
              if not name.startswith("Street.")]
CONNECTORS.append(_two_knot(L.PORT_ROAD))
# The railway gets one too. It is inside 02_terrain's flat_mask, so without a
# profile it simply took the surrounding field and came out at 10.5% - fine for
# a lorry, impossible for a train. Its own two-knot ramp runs the length of the
# arc, which puts it under 1%.
CONNECTORS.append(_two_knot(L.RAIL))
CONNECTORS.append(_two_knot(L.RAIL_PORT))

PROFILES_ALL = PROFILES_MAIN + CONNECTORS

# How close to a connector its own profile takes over completely, and over what
# distance it hands back to the surrounding terrain. The hand-off IS the cutting
# or embankment - the road holds its grade and the ground steps up or down to
# meet it, which is what a road crossing a terrace looks like.
CONN_R = 11.0
CONN_FEATHER = 22.0


def _rect(dx, dy, hw, hh, f):
    """Smooth rectangular mask.

    layout.rect_mask uses min(), which creases along the diagonals; once the
    weight is raised to a power those creases sharpen into 25%+ slopes right
    where the loop road passes the refinery and depot pads. A product of the
    two bands is C1 everywhere.
    """
    return L.band(abs(dx), hw, hw + f) * L.band(abs(dy), hh, hh + f)


def road_z(x, y):
    """Ground level of the built environment at (x, y)."""
    z = _blend_profiles(x, y, PROFILES_MAIN)

    # Weights are raised to a power before averaging so a pad at full strength
    # swamps a neighbour's feather reaching into it; the blend-in factor stays
    # linear so the hand-off to open ground is still smooth.
    num = 0.0
    den = 0.0
    strongest = 0.0
    for cx, cy, hw, hh, f, h in PADS:
        w = _rect(x - cx, y - cy, hw, hh, f)
        if w <= 0.0:
            continue
        p = w ** _PAD_SHARPNESS
        num += p * h
        den += p
        if w > strongest:
            strongest = w
    if den > 0.0:
        z = z * (1.0 - strongest) + (num / den) * strongest

    # Connectors are applied AFTER the pads, so a spur or the harbour road keeps
    # its own grade where it crosses a terrace edge. With the pads applied last
    # the market pad's feather reached 66 units west and squeezed the port
    # road's 4.5-unit descent into 20 units of road - a 31% ramp to the quay.
    # ...but not inside a pad's flat core. Spur.Quarry runs past the mine pad's
    # south-east corner and was cutting a 4.4-unit trench through it, which would
    # have left the yard slab floating. A road crossing a flat yard is flat.
    core = 0.0
    for cx, cy, hw, hh, _f, _h in PADS:
        core = max(core, _rect(x - cx, y - cy, hw, hh, CONN_FEATHER))
    if core >= 1.0:
        return z

    cnum = 0.0
    cden = 0.0
    cstrong = 0.0
    for path, knots in CONNECTORS:
        d, t = L.dist_to_path(x, y, path)
        w = L.band(d, CONN_R, CONN_R + CONN_FEATHER)
        if w <= 0.0:
            continue
        # Fade the influence out at both ends. dist_to_path clamps t, so without
        # this a connector throws a blob of its endpoint height sideways past
        # where the road actually is - Spur.Store's start was doing that to the
        # loop 24 units away, at 26%. The knots are read off the surrounding
        # field at the endpoints anyway, so there is nothing to lose here.
        w *= L.smoothstep(0.0, 0.18, t) * (1.0 - L.smoothstep(0.82, 1.0, t))
        w *= 1.0 - core
        if w <= 0.0:
            continue
        p = w ** _PAD_SHARPNESS
        cnum += p * _profile(knots, t)
        cden += p
        if w > cstrong:
            cstrong = w
    if cden > 0.0:
        z = z * (1.0 - cstrong) + (cnum / cden) * cstrong
    return z


def pad_z(x, y):
    """Flat height of the pad containing (x, y), else the graded surface.

    District scripts use this once, for their own centre, and translate their
    whole collection by it - cheaper and far less error-prone than editing the
    ~150 hardcoded z literals spread across them.
    """
    for cx, cy, hw, hh, _f, h in PADS:
        if abs(x - cx) <= hw and abs(y - cy) <= hh:
            return h
    return road_z(x, y)


def grade_at(x, y, step=6.0):
    """Local slope as a fraction (0.08 = 8%). For checking, not for building."""
    dx = (road_z(x + step, y) - road_z(x - step, y)) / (2.0 * step)
    dy = (road_z(x, y + step) - road_z(x, y - step)) / (2.0 * step)
    return hypot(dx, dy)
