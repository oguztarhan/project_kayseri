"""Geometry helpers shared by every island.

Pure functions only - no island state lives here. Each isle_*.py imports these
and adds its own coordinates on top, so the two islands cannot drift apart on
the maths while differing on the map.
"""
from math import atan2, cos, floor, hypot, pi, sin

SQ2 = 0.7071067811865476


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


def _unit(dx, dy):
    d = hypot(dx, dy) or 1.0
    return dx / d, dy / d


def circuit(corners, fillet=16.0, arc=4, step=20.0):
    """A closed road through STRAIGHT runs and rounded corners.

    ring() gives a circle, and a circle is why the first two islands read as the
    same map twice: no real road curves continuously for four hundred metres.
    A town's roads are straight lines that turn at junctions, so this takes the
    corners of a polygon and lays tarmac between them - straight where the road
    is straight, and rounded only at the turns.

    The corner is a quadratic Bezier off the two tangent points rather than a
    true arc: it needs no trigonometry, it cannot degenerate on a shallow or a
    reflex corner, and Catmull-Rom is going to smooth the result anyway. The
    straight runs are subdivided into COLLINEAR points for the same reason
    straight() exists - Catmull-Rom through collinear points is exactly the
    straight line, so the sides stay dead straight between the corners.

    `fillet` is the tangent length, clamped to 45% of the shorter adjoining side
    so two corners on one short side can never overrun each other.
    """
    n = len(corners)
    tan_pts = []
    for i in range(n):
        a, p, b = corners[i - 1], corners[i], corners[(i + 1) % n]
        la = hypot(a[0] - p[0], a[1] - p[1]) or 1.0
        lb = hypot(b[0] - p[0], b[1] - p[1]) or 1.0
        t = min(fillet, la * 0.45, lb * 0.45)
        ua, ub = _unit(a[0] - p[0], a[1] - p[1]), _unit(b[0] - p[0], b[1] - p[1])
        tan_pts.append(((p[0] + ua[0] * t, p[1] + ua[1] * t), p,
                        (p[0] + ub[0] * t, p[1] + ub[1] * t)))

    out = []
    for i in range(n):
        c1, p, c2 = tan_pts[i]
        out.append(c1)
        for k in range(1, arc + 1):
            s = k / (arc + 1.0)
            w0, w1, w2 = (1.0 - s) ** 2, 2.0 * (1.0 - s) * s, s * s
            out.append((w0 * c1[0] + w1 * p[0] + w2 * c2[0],
                        w0 * c1[1] + w1 * p[1] + w2 * c2[1]))
        out.append(c2)
        q = tan_pts[(i + 1) % n][0]
        d = hypot(q[0] - c2[0], q[1] - c2[1])
        for k in range(1, int(d / step)):
            s = k / float(int(d / step))
            out.append((c2[0] + (q[0] - c2[0]) * s, c2[1] + (q[1] - c2[1]) * s))
    return out + [out[0]]


def wander(corners, fillet=20.0, arc=4, step=20.0):
    """An OPEN road through straight runs and rounded corners.

    circuit()'s sibling for a network that branches instead of closing. Same
    quadratic-Bezier corner and the same collinear subdivision of the straights,
    so the road is dead straight between its turns and rounded only at them -
    which is what the reference photograph of a real estate road looks like.
    """
    n = len(corners)
    tan_pts = []
    for i in range(n):
        p = corners[i]
        a = corners[i - 1] if i > 0 else None
        b = corners[i + 1] if i < n - 1 else None
        if a is None or b is None:
            tan_pts.append((p, p, p))
            continue
        la = hypot(a[0] - p[0], a[1] - p[1]) or 1.0
        lb = hypot(b[0] - p[0], b[1] - p[1]) or 1.0
        t = min(fillet, la * 0.45, lb * 0.45)
        ua, ub = _unit(a[0] - p[0], a[1] - p[1]), _unit(b[0] - p[0], b[1] - p[1])
        tan_pts.append(((p[0] + ua[0] * t, p[1] + ua[1] * t), p,
                        (p[0] + ub[0] * t, p[1] + ub[1] * t)))

    out = []
    for i in range(n):
        c1, p, c2 = tan_pts[i]
        out.append(c1)
        if c1 != c2:
            for k in range(1, arc + 1):
                s = k / (arc + 1.0)
                w0, w1, w2 = (1.0 - s) ** 2, 2.0 * (1.0 - s) * s, s * s
                out.append((w0 * c1[0] + w1 * p[0] + w2 * c2[0],
                            w0 * c1[1] + w1 * p[1] + w2 * c2[1]))
            out.append(c2)
        if i == n - 1:
            break
        q = tan_pts[i + 1][0]
        d = hypot(q[0] - c2[0], q[1] - c2[1])
        for k in range(1, int(d / step)):
            f = k / float(int(d / step))
            out.append((c2[0] + (q[0] - c2[0]) * f, c2[1] + (q[1] - c2[1]) * f))
    return out


def axis_meets(loop):
    """Where a closed road crosses each arterial, as (E, N, W, S).

    ring() put these at known control points and blob() at known radii, both of
    which only worked because the loop was drawn about the origin by radius. A
    circuit() is drawn by its corners instead, so the crossings are measured -
    which also means an island is free to cross one arm at 58 and another at 84
    without any of the four being special.

    The FURTHEST crossing on each arm wins: a road that wanders back over an
    arterial twice still meets it, once, at the junction on the outside.
    """
    out = []
    for axis, sgn in ((0, 1), (1, 1), (0, -1), (1, -1)):
        other = 1 - axis
        best = 0.0
        for i in range(len(loop) - 1):
            p, q = loop[i], loop[i + 1]
            if (p[other] > 0.0) == (q[other] > 0.0):
                continue
            s = p[other] / (p[other] - q[other])
            v = p[axis] + (q[axis] - p[axis]) * s
            if v * sgn > 0.0 and abs(v) > abs(best):
                best = v
        out.append((best, 0.0) if axis == 0 else (0.0, best))
    return out


def ray_hit(loop, angle):
    """Where the ray from the origin on `angle` leaves a closed road.

    This is what a spur head is: the point on the loop a branch leaves from,
    on the bearing of whatever it is going to serve. The outermost crossing is
    the one taken, so a circuit that doubles back still hands out the junction
    on its outer side.
    """
    dx, dy = cos(angle), sin(angle)
    best, hit = 0.0, (0.0, 0.0)
    for i in range(len(loop) - 1):
        p, q = loop[i], loop[i + 1]
        ex, ey = q[0] - p[0], q[1] - p[1]
        den = ex * dy - ey * dx
        if abs(den) < 1e-9:
            continue
        s = (p[1] * dx - p[0] * dy) / den
        if s < 0.0 or s > 1.0:
            continue
        px, py = p[0] + ex * s, p[1] + ey * s
        u = px * dx + py * dy
        if u > best:
            best, hit = u, (px, py)
    return hit


def offset_closed(loop, d):
    """A closed road pushed d to its OUTSIDE - its pavement, or its verge.

    A true mitred normal offset, not the radial one a circle could get away
    with: on a circuit the sides are straight and the corners are not centred
    on the origin, so pushing every point away from (0, 0) would leave the
    pavement crossing the tarmac on one side and standing off it on the other.
    The miter is clamped so a tight corner makes a chamfer instead of a spike.

    Assumes the loop runs counter-clockwise, which ring(), blob() and circuit()
    all do.
    """
    pts = loop[:-1] if hypot(loop[0][0] - loop[-1][0],
                             loop[0][1] - loop[-1][1]) < 1e-6 else list(loop)
    n = len(pts)
    out = []
    for i in range(n):
        a, p, b = pts[i - 1], pts[i], pts[(i + 1) % n]
        n1 = _unit(p[1] - a[1], -(p[0] - a[0]))
        n2 = _unit(b[1] - p[1], -(b[0] - p[0]))
        mx, my = _unit(n1[0] + n2[0], n1[1] + n2[1])
        scale = d / max(0.4, mx * n1[0] + my * n1[1])
        out.append((p[0] + mx * scale, p[1] + my * scale))
    return out + [out[0]]


def offset_open(path, d):
    """An OPEN path pushed d to its left - a pavement beside a road.

    offset_closed's sibling, for an island whose roads form a tree: there is no
    circuit to run a pavement round, so the crew's walk is a run beside one of
    the gates. Mitred the same way, and the end points take their own segment's
    normal rather than an average with a segment that is not there.
    """
    n = len(path)
    out = []
    for i in range(n):
        a = path[max(0, i - 1)]
        b = path[min(n - 1, i + 1)]
        p = path[i]
        n1 = _unit(p[1] - a[1], -(p[0] - a[0])) if i > 0 else None
        n2 = _unit(b[1] - p[1], -(b[0] - p[0])) if i < n - 1 else None
        if n1 is None:
            mx, my = n2
        elif n2 is None:
            mx, my = n1
        else:
            mx, my = _unit(n1[0] + n2[0], n1[1] + n2[1])
        ref = n1 or n2
        scale = d / max(0.4, mx * ref[0] + my * ref[1])
        out.append((p[0] - mx * scale, p[1] - my * scale))
    return out


def crossings(path, centre, r):
    """Normalised arc positions where `path` crosses a circle of radius r.

    grade.py used to find a district's pad edge by naming it - centre plus
    PAD_HW along whichever axis the road ran. That only worked while every
    arterial WAS an axis. Measured against a circle it is the same point on a
    straight road and still the right one on a road with corners in it.
    """
    lens = [hypot(path[i + 1][0] - path[i][0], path[i + 1][1] - path[i][1])
            for i in range(len(path) - 1)]
    total = sum(lens) or 1.0
    out, run = [], 0.0
    for i in range(len(path) - 1):
        a, b = path[i], path[i + 1]
        da = hypot(a[0] - centre[0], a[1] - centre[1])
        db = hypot(b[0] - centre[0], b[1] - centre[1])
        if (da > r) != (db > r):
            f = (da - r) / (da - db) if abs(da - db) > 1e-9 else 0.0
            out.append((run + lens[i] * f) / total)
        run += lens[i]
    return out


def turnhead(pts, radius=11.0, back=5.0, n=14):
    """The bulb a dead-end road ends in, so a truck can turn without reversing.

    Every road on this map that serves one building is a dead end, and until now
    they simply stopped - which is the one thing a real haul road never does,
    because an articulated truck cannot back out of a works. The bulb straddles
    the road's last few metres rather than sitting beyond it, so it widens the
    end of the road instead of pushing a disc of tarmac into the yard.
    """
    ex, ey = pts[-1][0], pts[-1][1]
    dx, dy = _unit(ex - pts[-2][0], ey - pts[-2][1])
    cx, cy = ex - dx * back, ey - dy * back
    ring_pts = [(cx + radius * cos(2.0 * pi * k / n),
                 cy + radius * sin(2.0 * pi * k / n)) for k in range(n)]
    return ring_pts + [ring_pts[0]]


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


def gates(districts, pad, sites, site_pad, extra=()):
    """The zones a public road stops at, as (x, y, radius).

    Every district is built centred ON its arterial - the depot's stacker tower
    sits dead on the north-south centreline, the mine's headframe on the
    east-west one - so a road that runs to the district centre runs through the
    middle of the works and out the far side. It cannot be routed around them
    without moving the districts; it has to END, which is what a road does when
    it reaches a works gate. Inside the gate the district's own yard slab is the
    surface, and the trucks keep driving on it: 14_routes.py exports the whole
    centreline, so only the TARMAC stops here, not the route.
    """
    z = [(x, y, pad) for x, y in districts]
    # +3 on the sites: a spur is 10.5 wide, so cutting its centreline at the
    # pad still left its shoulder inside the yard.
    z += [(p[0], p[1], site_pad + 3.0) for _n, p, _need in sites]
    return z + list(extra)


def gate_point(district, pad, centre=(0.0, 0.0)):
    """Where the arterial meets a district - the pad edge on the town side.

    Derived rather than named per district, so it follows the two islands
    swapping which end of the north-south arterial the depot and the market sit
    at. Districts pass it to fence_run() as the gate to leave open; a fence that
    does not reach it is simply unaffected.
    """
    dx, dy = centre[0] - district[0], centre[1] - district[1]
    d = hypot(dx, dy)
    if d < 1e-6:
        return district
    return (district[0] + dx / d * pad, district[1] + dy / d * pad)


def fence_gaps(district, pad, approaches, radius=10.0):
    """Where roads cross a district's boundary - the gaps its fence needs.

    The arterial's gate is always one of them. The rest are MEASURED off the
    roads that reach the district, so an island is free to serve one of its
    works by a second road - the iron island's mine haul road comes into the
    market from the west - without any district script having to know that road
    exists. It used to be one hand-written special case for the harbour road.

    Measured against this district's zone alone, not against every gate: on the
    copper island the port gate swallows the haul road completely, and the
    trucks still drive out that way, so the gap has to be there whether or not
    any tarmac is drawn on it.
    """
    zone = (district[0], district[1], pad)
    out = [gate_point(district, pad) + (11.0,)]
    for pts in approaches:
        for run in trim_zones(pts, [zone]):
            for p in (run[0], run[-1]):
                if abs(hypot(p[0] - district[0], p[1] - district[1]) - pad) < 3.0:
                    out.append((p[0], p[1], radius))
    return out


def _span_in_circle(p, q, c, r):
    """[t0, t1] of the segment p->q that lies inside circle (c, r), or None."""
    dx, dy = q[0] - p[0], q[1] - p[1]
    fx, fy = p[0] - c[0], p[1] - c[1]
    a = dx * dx + dy * dy
    if a < 1e-12:
        return (0.0, 1.0) if fx * fx + fy * fy <= r * r else None
    b = 2.0 * (fx * dx + fy * dy)
    cc = fx * fx + fy * fy - r * r
    disc = b * b - 4.0 * a * cc
    if disc <= 0.0:
        return None
    s = disc ** 0.5
    t0 = max(0.0, (-b - s) / (2.0 * a))
    t1 = min(1.0, (-b + s) / (2.0 * a))
    return (t0, t1) if t1 > t0 else None


def trim_zones(path, zones, min_len=6.0):
    """The parts of `path` outside every zone, as a list of separate runs.

    Solved per segment against each circle rather than by walking samples, so
    the cut lands exactly on the gate rather than on whichever sample happened
    to fall nearest it - the arterials are 12 collinear control points, and a
    sampled cut would leave up to 30 units of road inside the yard.
    """
    runs, cur = [], []
    for i in range(len(path) - 1):
        p, q = path[i], path[i + 1]
        spans = [s for s in (_span_in_circle(p, q, (c[0], c[1]), c[2])
                             for c in zones) if s]
        spans.sort()
        free, t = [], 0.0
        for a, b in spans:                       # complement of the union
            if a > t:
                free.append((t, a))
            t = max(t, b)
        if t < 1.0:
            free.append((t, 1.0))
        for a, b in free:
            pa = (p[0] + (q[0] - p[0]) * a, p[1] + (q[1] - p[1]) * a)
            pb = (p[0] + (q[0] - p[0]) * b, p[1] + (q[1] - p[1]) * b)
            if cur and hypot(cur[-1][0] - pa[0], cur[-1][1] - pa[1]) < 1e-6:
                cur.append(pb)
            else:
                if len(cur) >= 2:
                    runs.append(cur)
                cur = [pa, pb]
    if len(cur) >= 2:
        runs.append(cur)
    # A stub shorter than a truck is not a road; it is what is left when a zone
    # clips a corner, and drawing it leaves a scrap of tarmac in the grass.
    return [_declutter(r) for r in runs if _run_len(r) >= min_len]


def _declutter(run, min_seg=4.0):
    """Drop control points that crowd the cut.

    A gate usually lands close to an existing control point, leaving a 2-unit
    segment followed by a 16-unit one. Catmull-Rom through that swings wide
    inside the first segment - the port road bowed 5 units back into the market
    yard it had just been cut out of. Endpoints are never dropped: they are the
    gate.
    """
    out = [run[0]]
    for p in run[1:-1]:
        if hypot(p[0] - out[-1][0], p[1] - out[-1][1]) >= min_seg:
            out.append(p)
    if len(out) > 1 and hypot(run[-1][0] - out[-1][0], run[-1][1] - out[-1][1]) < min_seg:
        out.pop()
    out.append(run[-1])
    return out


def trim_arterial(path, zones):
    """The gated run of an arterial that is part of the network.

    An arterial is cut at BOTH districts it serves, and the layout runs the line
    on past each of them so grade.py has room to land its profile - so trimming
    leaves the useful middle plus a stranded stub beyond each district. The stub
    is the road-out-the-far-side the gates exist to remove. The run through the
    crossroads is the one that is actually a road.
    """
    runs = trim_zones(path, zones)
    if len(runs) <= 1:
        return runs
    return [min(runs, key=lambda r: min(hypot(p[0], p[1]) for p in r))]


def _run_len(pts):
    return sum(hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1])
               for i in range(len(pts) - 1))


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


def smoothstep(e0, e1, x):
    if e1 == e0:
        return 0.0
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3.0 - 2.0 * t)


def band(d, inner, outer):
    return 1.0 - smoothstep(inner, outer, d)


def rect_mask(x, y, hw, hh, feather):
    return min(band(abs(x), hw, hw + feather), band(abs(y), hh, hh + feather))


def shore_fns(shore, ax, ay):
    """Build sea_depth() for a coastline polyline.

    (ax, ay) is a UNIT vector pointing out to sea, so which side of the
    coastline is water is data rather than a hardcoded sign. The coal island
    faces the smaller x+y (screen left) and the copper island the larger
    (screen right); nothing else in the generator has to know which.

    Returns sea_depth(x, y): metres seaward of the waterline, 0 on the beach,
    negative inland.
    """
    def proj(x, y):
        return x * ax + y * ay

    def shore_edge(x, y):
        """proj() of the nearest point ON the coastline."""
        best, be = 1e9, -1e9
        for i in range(len(shore) - 1):
            sx, sy = shore[i]
            tx, ty = shore[i + 1]
            dx, dy = tx - sx, ty - sy
            L2 = dx * dx + dy * dy
            t = 0.0 if L2 < 1e-9 else ((x - sx) * dx + (y - sy) * dy) / L2
            t = max(0.0, min(1.0, t))
            px, py = sx + dx * t, sy + dy * t
            d = hypot(x - px, y - py)
            if d < best:
                best, be = d, proj(px, py)
        return be

    def sea_depth(x, y):
        return proj(x, y) - shore_edge(x, y)

    return sea_depth


def island_fns(coast):
    """sea_depth() for a CLOSED coastline - land with water all the way round.

    Same contract as shore_fns, so it drops straight into 02_terrain, the port
    and the dressing pass: metres seaward of the waterline, 0 on the beach,
    negative inland.

    What differs is how the sign is decided. shore_fns projects onto a single
    sea axis, which is what makes it a COAST: everything past that line is water
    however far round you walk, so the map has one shore and three edges that
    simply run off the frame. Here the sign is which side of the ring the point
    falls on, so the sea closes behind you and the land is an island.

    Distance is to the nearest point on the ring; inside/outside is the usual
    crossing count. O(len(coast)) per sample, same as shore_fns.
    """
    pts = list(coast)
    if pts[0] != pts[-1]:
        pts.append(pts[0])

    def sea_depth(x, y):
        best = 1e9
        inside = False
        for i in range(len(pts) - 1):
            sx, sy = pts[i]
            tx, ty = pts[i + 1]
            dx, dy = tx - sx, ty - sy
            L2 = dx * dx + dy * dy
            t = 0.0 if L2 < 1e-9 else ((x - sx) * dx + (y - sy) * dy) / L2
            t = max(0.0, min(1.0, t))
            d = hypot(x - sx - dx * t, y - sy - dy * t)
            if d < best:
                best = d
            if (sy > y) != (ty > y) and x < sx + (y - sy) * dx / (ty - sy):
                inside = not inside
        return -best if inside else best

    return sea_depth


# ---------------------------------------------------------------- coastlines
# A coastline is a distance field, not a polyline: everything downstream - the
# landform falloff, the beach material, the tree and boulder scatter - asks
# sea_depth(x, y) how far out to sea it is. So the way to make a coast look
# like a coast is to bend that field, once, here. Bending it in 02_terrain
# instead is what left the LANDFORM running dead straight behind a waterline
# that wobbled: the ground fell away to the sea along a ruled line and only the
# last two metres of beach were ragged.


def _lattice(xi, yi, seed):
    """Deterministic value in [-1, 1] at an integer lattice point.

    Written out rather than taken from mathutils.noise because geom is imported
    outside Blender (gen.py's argument checks, and any bare-python measuring of
    a map), and because a hash that lives here cannot drift between the eight
    islands the way a Blender version bump could.
    """
    h = (xi * 374761393 + yi * 668265263 + seed * 2147483647) & 0xFFFFFFFF
    h = (h ^ (h >> 13)) * 1274126177 & 0xFFFFFFFF
    h ^= h >> 16
    return (h & 0xFFFFFF) / 8388607.5 - 1.0


def _value(x, y, seed):
    xi, yi = int(floor(x)), int(floor(y))
    fx, fy = x - xi, y - yi
    ux = fx * fx * (3.0 - 2.0 * fx)
    uy = fy * fy * (3.0 - 2.0 * fy)
    a = _lattice(xi, yi, seed)
    b = _lattice(xi + 1, yi, seed)
    c = _lattice(xi, yi + 1, seed)
    d = _lattice(xi + 1, yi + 1, seed)
    lo = a + (b - a) * ux
    return lo + ((c + (d - c) * ux) - lo) * uy


def fbm(x, y, wavelength, seed=0, octaves=4, gain=0.6, lac=2.3, spread=2.5):
    """Fractal value noise on [-1, 1], coarsest octave at `wavelength`.

    Wavelength rather than frequency because every caller here is thinking in
    metres of coastline: "bays about 300 across" is the shape being asked for.

    `spread` is what makes the callers' amplitudes mean something. Summed
    octaves divided by the sum of their weights is mathematically on [-1, 1],
    but it does not go there: measured over 32k samples the mean magnitude was
    0.16 and the 95th percentile 0.38. A coast asked for at 28 metres of relief
    got four, which is why the gold island's south shore still read as a ruled
    line after the first pass. Scaled so the typical excursion is around 0.4 of
    the amplitude asked for and the 95th percentile near all of it; the few per
    cent past that clamp, which costs a short flat stretch of coast now and
    then and is no worse than what a cliff line looks like anyway.
    """
    amp, freq, total, norm = 1.0, 1.0 / wavelength, 0.0, 0.0
    for k in range(octaves):
        total += amp * _value(x * freq, y * freq, seed + k * 101)
        norm += amp
        amp *= gain
        freq *= lac
    return max(-1.0, min(1.0, total / norm * spread))


def ragged(sea_depth, amp=28.0, wavelength=150.0, seed=31, land_bias=0.22,
           calm=(), calm_r=88.0):
    """Break a straight coast into headlands, spits and coves.

    The authored SHORE of every map is a near-constant band - gold's is a
    horizontal line across the screen - because it is placed to put the quay
    and the districts where the frame wants them, not to look like geology.
    This is what turns it back into a coast, at four scales down to the 2.6
    metre pitch of the ground grid.

    Pushed SEAWARD at full amplitude and landward at `land_bias` of it, which is
    what makes this safe to drop under an already-built map. Every district and
    site was placed against the straight line, and three of them sit within five
    metres of it (gold's market and refinery, coal's quarry); eroding the coast
    the same distance it is allowed to grow would put the waterline inside their
    pads. Growing is free - the sea it takes is empty.

    `calm` are the points that must keep the straight shore they were built
    against: the port, above all. A quay is a dredged face, so a smooth coast
    there is also the truer picture. The taper has to clear the apron, not just
    the quay - it is a 76-wide rectangle offset landward of PORT - so it holds
    the coast dead straight inside 0.55 * calm_r and is only at full amplitude
    a whole calm_r out.

    Wrap this INSIDE enclose, not outside it. The ring is what keeps the coast
    clear of the edge of the ground grid, and it can only do that if it has the
    last word: run the other way round, a spit growing seaward off the back
    coast lands in the 22 units of shelf the ring leaves and the island is cut
    square again in patches.
    """
    def ragged_depth(x, y):
        a = amp
        for cx, cy in calm:
            a *= smoothstep(calm_r * 0.55, calm_r, hypot(x - cx, y - cy))
        if a <= 0.0:
            return sea_depth(x, y)
        f = fbm(x, y, wavelength, seed, 4, 0.6, 2.35)
        return sea_depth(x, y) + a * (f if f < 0.0 else f * land_bias)

    return ragged_depth


def enclose(sea_depth, r0=279.0, amp=17.0, wavelength=300.0, seed=7,
            keep=258.0):
    """Close a one-sided coast into an island.

    shore_fns gives a HALF-PLANE: sea on one side of the authored line, land
    forever on the other. Three of a map's four sides therefore never meet
    water at all - the ground simply runs to the edge of the GROUND_SIZE grid
    and is cut off square, which is what read as a square island.

    This intersects that half-plane with the inside of an irregular ring, so
    the land closes and the sea goes all the way round. Being an INTERSECTION
    it can only ever take land away, never add it: no district, rail corridor,
    quay or shipping lane can find itself on new ground because of this. What
    it does eat is the outer skirt of the backdrop mountain rings, which is the
    point - a range that walks into the sea is what tells you it is an island.

    `keep` is the radius the ring is never allowed inside. The furthest built
    thing on any of the four maps is the copper island's rail loop at 250.
    """
    def enclosed(x, y):
        # Five octaves, and weighted towards the fine end (gain 0.72). At four
        # and 0.65 the finest was +/-2.2 over a 22-metre wavelength, which on a
        # 2.6-metre ground grid is smooth: close up the back coast came out as
        # a clean arc with an even sand band, and read as a cut rather than a
        # shore. The last octave here is under the grid pitch on purpose - it
        # aliases into per-quad ragged, which is what a flat-shaded coastline
        # has instead of a curve.
        r = max(keep, r0 + amp * fbm(x, y, wavelength, seed, 5, 0.72, 2.5))
        return max(sea_depth(x, y), hypot(x, y) - r)

    return enclosed


def site_filters(sites):
    """(active_sites, locked_sites) closed over one island's SITES table."""
    def active(phase):
        return [(n, p) for (n, p, need) in sites if phase >= need]

    def locked(phase):
        return [(n, p) for (n, p, need) in sites if phase < need]

    return active, locked
