"""Finding placement faults across the whole island, not one district at a time.

Every check here is against real evaluated geometry rather than against the
numbers a build step meant to use, because the faults that survive are exactly
the ones where the intent and the result disagree.
"""
import bpy
from mathutils import Vector
from math import hypot

# Everything built out of parts. Terrain and Foliage are the ground and the
# scenery: they are what other things are measured AGAINST, not against each
# other.
BUILT = ("Roads", "Rail", "Mine", "Depot", "Refinery", "Market", "Port",
         "Sites", "Props", "Power", "Haul", "Fleet", "Civic", "Theme",
         "Foliage")

# Flat ground, paint, effects and things that are meant to touch what they serve.
FLAT_Z = 1.6
SKIP = ("Pad", "Yard", "Apron", "Bays", "Fence", "Smoke", "Cliff", "Walk",
        "Mark", "Kerb", "Crosswalk", "Road", "Clutter", "Ground", "Water",
        "Foam", "Track", "Ballast", "Sleeper", "Rail.Line", "Bed", "Bench")
# Joined by design: a conveyor lands on a tower, a rack runs into a column.
JOINED = ("Conv", "Rack", "Gantry", "Dock", "Bay", "Pipe", "Spheres", "Walls",
          "Trestle", "Bridge", "Pier", "Jetty", "Quay", "Portal", "Tunnel")
# Rolling stock and road vehicles move; they are allowed to be on their own line.
MOVING = ("Wagon", "Loco", "Train", "Truck", "Van", "V.ore", "V.cargo",
          "V.tank", "V.van", "Ship", "Boat", "Tug", "Barge")
# Elevated on purpose: quay cranes stand over water, ghost tiers hover, plumes
# rise, a bridge deck spans a gap, an adit is cut into a hillside.
ELEVATED = ("Crane", "Beacon", "Tier", "Steam", "Smoke", "Plume", "Deck",
            "Adit", "Winder", "Bridge", "Catenary", "Mast", "Portal")

# Pairs that are SUPPOSED to be inside each other. An engine shed exists to hold
# an engine; a loader with its bucket in a spoil heap is a loader doing its job.
# Reporting these as faults is what buries the handful that are real.
LEGIT = (("Shed", ("Loco", "Train", "Wagon")),
         ("Pine", ("Tree", "Bush", "Pine")),
         ("Bush", ("Tree", "Bush", "Pine")),
         ("Crane", ("Ship", "Container", "Crate", "Barge")),
         ("Gantry", ("Ship", "Container", "Crate", "Barge")),
         ("Construction", ("Crane", "Crate")))


def _legit_pair(a, b):
    for key, mates in LEGIT:
        if key in a and any(m in b for m in mates):
            return True
        if key in b and any(m in a for m in mates):
            return True
    return False


BITE = 1.8          # how deep two boxes must interpenetrate to count
RAIL_HW = 4.2       # half width of the running line's keep-clear
BURIED = 1.5        # BASE this far below local ground = buried
# Things that are meant to be cut into the ground or run down to the seabed.
FOOTED = ("Quay", "Wall", "Pier", "Jetty", "Deck", "Pile", "Foot", "Bed",
          "Ramp", "Slip", "Adit", "Portal", "Tunnel", "Cliff", "Bench")
FLOAT = 3.0         # base this far above local ground = floating
# A ribbon - a road, a rail line, a run of catenary - is ONE mesh spanning the
# whole island, so its bounding box covers half the map and every box test
# against it is meaningless. They are excluded from the clash pass; the rail
# pass tests against the centreline itself, which is the right shape for it.
RIBBON = 46.0


def _has(name, keys):
    return any(k in name for k in keys)


def _box(ob):
    pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    return [min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)]


def solids(names=BUILT):
    """(object, world box) for everything that can be stood in or on."""
    out = []
    for n in names:
        col = bpy.data.collections.get(n)
        if col is None:
            continue
        for ob in col.objects:
            if ob.type != 'MESH' or ob.data is None or not len(ob.data.polygons):
                continue
            if _has(ob.name, SKIP):
                continue
            if ob.hide_render or ob.hide_viewport:
                continue          # template meshes: parked, hidden, never drawn
            if ob.name.endswith(("Src", "src", ".src")):
                continue
            b = _box(ob)
            if (b[5] - b[4]) < FLAT_Z:
                continue
            span = max(b[1] - b[0], b[3] - b[2])
            out.append((ob, b, n, span <= RIBBON))
    return out


def ground_z(x, y):
    """True terrain height under a point, by dropping a ray onto the ground.

    grade.grade_at is the road/pad PROFILE the build steps lay against, not the
    terrain: on the rail embankment it reads 0 where the ground is 15, which
    made every carriage on the island look like it was floating.
    """
    col = bpy.data.collections.get("Terrain")
    if col is None:
        return None
    best = None
    for ob in col.objects:
        if ob.type != 'MESH' or not ob.data or not len(ob.data.polygons):
            continue
        if not ob.name.startswith(("Ground", "Isle", "isle", "Land", "Pad")):
            continue
        try:
            inv = ob.matrix_world.inverted()
        except Exception:
            continue
        o = inv @ Vector((x, y, 900.0))
        d = (inv @ Vector((x, y, -900.0))) - o
        hit, loc, _n, _i = ob.ray_cast(o, d.normalized(), distance=d.length)
        if hit:
            z = (ob.matrix_world @ loc).z
            if best is None or z > best:
                best = z
    return best


def _seg_dist(px, py, ax, ay, bx, by):
    vx, vy = bx - ax, by - ay
    L2 = vx * vx + vy * vy
    if L2 < 1e-12:
        return hypot(px - ax, py - ay)
    t = ((px - ax) * vx + (py - ay) * vy) / L2
    t = 0.0 if t < 0.0 else (1.0 if t > 1.0 else t)
    return hypot(px - (ax + vx * t), py - (ay + vy * t))


def on_rail(b, path, hw=RAIL_HW):
    """Whether a world box overlaps the running line in plan."""
    cx, cy = (b[0] + b[1]) * 0.5, (b[2] + b[3]) * 0.5
    rx, ry = (b[1] - b[0]) * 0.5, (b[3] - b[2]) * 0.5
    reach = hw + max(rx, ry)
    for i in range(len(path) - 1):
        a, c = path[i], path[i + 1]
        if _seg_dist(cx, cy, a[0], a[1], c[0], c[1]) > reach:
            continue
        # tighter: nearest point on the segment against the box itself
        for k in range(9):
            t = k / 8.0
            px = a[0] + (c[0] - a[0]) * t
            py = a[1] + (c[1] - a[1]) * t
            if (b[0] - hw < px < b[1] + hw) and (b[2] - hw < py < b[3] + hw):
                return True
    return False


def run(L, grade, verbose=True, phase=0):
    items = solids()
    lines = []
    rail_hits, buried, floating, clashes = [], [], [], []

    path = [(p[0], p[1]) for p in getattr(L, "RAIL", [])]
    for ob, b, col, compact in items:
        if _has(ob.name, MOVING) or _has(ob.name, JOINED):
            continue
        if col == "Rail":
            continue                     # the line's own furniture belongs there
        if path and on_rail(b, path):
            rail_hits.append("%-28s [%s]" % (ob.name, col))

    for ob, b, col, compact in items:
        if not compact:
            continue                     # a ribbon has no meaningful "under it"
        cx, cy = (b[0] + b[1]) * 0.5, (b[2] + b[3]) * 0.5
        gz = ground_z(cx, cy)
        if gz is None:
            continue
        # A portal is cut INTO a hillside and a wagon in the bore is under the
        # massif: both read as buried against a ray dropped from the sky, and
        # both are right. Only things that should be standing in daylight count.
        if _has(ob.name, ELEVATED) or _has(ob.name, MOVING):
            continue
        if not _has(ob.name, FOOTED) and b[4] < gz - BURIED:
            buried.append("%-28s [%s] base %.1f, ground %.1f, sunk %.1f"
                          % (ob.name, col, b[4], gz, gz - b[4]))
        elif b[4] > gz + FLOAT and not _has(ob.name, ELEVATED):
            floating.append("%-28s [%s] base %.1f, ground %.1f"
                            % (ob.name, col, b[4], gz))

    # Sweep and prune. Sorted by the left edge, the inner loop stops the moment
    # a box starts beyond the current one's right edge - so a thousand trees
    # spread over 400 metres cost a few thousand compares instead of half a
    # million. The full pass was O(n^2) and took longer than the build.
    compact_items = sorted([it for it in items if it[3]], key=lambda it: it[1][0])
    n = len(compact_items)
    for i in range(n):
        oi, bi, ci, _ = compact_items[i]
        if _has(oi.name, JOINED):
            continue
        for j in range(i + 1, n):
            oj, bj, cj, _ = compact_items[j]
            if bj[0] - bi[1] >= 0:
                break
            if _has(oj.name, JOINED):
                continue
            if _has(oi.name, MOVING) and _has(oj.name, MOVING):
                continue
            if _legit_pair(oi.name, oj.name):
                continue
            ox = min(bi[1], bj[1]) - max(bi[0], bj[0])
            oy = min(bi[3], bj[3]) - max(bi[2], bj[2])
            oz = min(bi[5], bj[5]) - max(bi[4], bj[4])
            if ox > BITE and oy > BITE and oz > BITE:
                clashes.append("%s [%s] x %s [%s]  %.0f/%.0f/%.0f"
                               % (oi.name, ci, oj.name, cj, ox, oy, oz))

    lines.append("SURVEY phase %d: %d solids (%d compact) | RAIL %d  BURIED %d  "
                 "FLOATING %d  CLASH %d"
                 % (phase, len(items), n, len(rail_hits), len(buried),
                    len(floating), len(clashes)))
    if verbose:
        for tag, rows, cap in (("RAIL", rail_hits, 20), ("BURIED", buried, 20),
                               ("FLOATING", floating, 20), ("CLASH", clashes, 30)):
            for r in rows[:cap]:
                lines.append("   %-8s %s" % (tag, r))
            if len(rows) > cap:
                lines.append("   %-8s ... and %d more" % (tag, len(rows) - cap))

    out = "\n".join(lines)
    print(out)
    return {"rail": rail_hits, "buried": buried, "floating": floating,
            "clash": clashes}
