"""Geometry helpers shared by every island.

Pure functions only - no island state lives here. Each isle_*.py imports these
and adds its own coordinates on top, so the two islands cannot drift apart on
the maths while differing on the map.
"""
from math import cos, hypot, pi, sin

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


def site_filters(sites):
    """(active_sites, locked_sites) closed over one island's SITES table."""
    def active(phase):
        return [(n, p) for (n, p, need) in sites if phase >= need]

    def locked(phase):
        return [(n, p) for (n, p, need) in sites if phase < need]

    return active, locked
