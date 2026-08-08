"""The working frame of a district: which way it faces, and what must stay clear.

Every district is built centred ON its arterial, and the arterial runs from the
island centre out to the district and stops at its gate (PAD units short - see
geom.gates).  So a district has a FRONT: the side its road arrives on, the side
its trucks come in from, and the side the camera looks at it from.

Two things follow from that, and neither was true before this module existed.

**The front is not the same direction on every island.**  06_depot.py and its
siblings were written against the coal map, where the depot stands north of the
centre and its front therefore faces -Y.  The copper map puts the depot SOUTH,
so every one of those literals pointed out the back of the works; the gold map
turns the whole chain a quarter turn, so they pointed out the side.  `Frame`
takes the coordinates exactly as they were authored against coal and rotates
them into whichever frame the current island's district actually stands in.
Nothing in a district script has to know which island it is building.

**The way in has to be empty.**  A truck drives up the arterial, through the
gate and into the yard; the exported centreline runs all the way to the district
centre.  Anything standing on that line is something a lorry drives through.
ENTRY is that corridor and YARD is the open ground it opens onto - the middle of
the property, where the stockpile stands and where there is room to turn.  Both
are expressed in the same authored (coal) space as everything else, so a script
can test its own placements against them with `Frame.clear`.
"""
from math import hypot

from mathutils import Vector

# The frame each district script's literals were authored in - the coal map's
# own orientation.  (u = out towards the island centre, v = u turned left.)
REF = {
    "mine":     ((1.0, 0.0), (0.0, 1.0)),     # coal MINE     = (-R, 0), front +X
    "depot":    ((0.0, -1.0), (1.0, 0.0)),    # coal DEPOT    = (0, +R), front -Y
    "refinery": ((-1.0, 0.0), (0.0, -1.0)),   # coal REFINERY = (+R, 0), front -X
    "market":   ((0.0, 1.0), (-1.0, 0.0)),    # coal MARKET   = (0, -R), front +Y
}

# Keep-clear zones, in authored (coal) space, as (a0, a1, b0, b1) along the
# district's own axes: a runs out towards the gate, b across it.
#
# ENTRY reaches from outside the gate (PAD = 36) in to the yard, and is wider
# than the 14-unit carriageway so a lorry has somewhere to be when it meets one
# coming out.  YARD is the open middle: the stockpile, and the room to turn.
# Sized to what the job actually needs, not to what looks generous on paper: a
# 72x68 property also has to hold the plant, and reserving a third of it pushed
# so much onto the flanks that the flanks then overlapped each other.
ENTRY = (14.0, 42.0, -8.0, 8.0)
YARD = (-9.0, 14.0, -12.0, 12.0)


def axis_frame(district, centre=(0.0, 0.0)):
    """A district's own axes: out towards the island centre, and across that.

    Snapped to the nearest world axis. Every island stands its districts on the
    two arterials, so the true direction is already axial to within rounding -
    and the art is built from axis-aligned boxes, which a diagonal frame would
    turn into diamonds.
    """
    dx, dy = centre[0] - district[0], centre[1] - district[1]
    d = hypot(dx, dy)
    if d < 1e-6:
        return (0.0, 1.0), (-1.0, 0.0)
    ux, uy = dx / d, dy / d
    if abs(ux) >= abs(uy):
        ux, uy = (1.0 if ux > 0 else -1.0), 0.0
    else:
        ux, uy = 0.0, (1.0 if uy > 0 else -1.0)
    return (ux, uy), (-uy, ux)


def inside(zone, a, b, ra=0.0, rb=0.0):
    """Whether a footprint of half-extents (ra, rb) at (a, b) touches a zone."""
    a0, a1, b0, b1 = zone
    return (a + ra > a0 and a - ra < a1 and b + rb > b0 and b - rb < b1)


class Frame(object):
    """One district's placement frame.

        F = yard.Frame("refinery", L.REFINERY)
        b.box(F.dim(26, 18, 0.34), F.at(-2, 6, 0.18))

    `at` and `dim` take the coordinates exactly as they were authored against
    the coal map and return world ones for the island being built.
    """

    def __init__(self, name, district, centre=(0.0, 0.0)):
        self.name = name
        self.cx, self.cy = district
        self.u, self.v = axis_frame(district, centre)
        self.ru, self.rv = REF[name]
        # A quarter turn between the authored frame and this one means a box's
        # width and depth swap: the art is axis-aligned, so it has to be
        # re-measured rather than rotated.
        self.swap = abs(self.u[0] * self.ru[0] + self.u[1] * self.ru[1]) < 0.5

    # ------------------------------------------------------------- placement
    def ab(self, dx, dy):
        """Authored (dx, dy) as this district's own (along, across)."""
        return (dx * self.ru[0] + dy * self.ru[1],
                dx * self.rv[0] + dy * self.rv[1])

    def at(self, dx, dy, z=0.3):
        a, b = self.ab(dx, dy)
        return (self.cx + self.u[0] * a + self.v[0] * b,
                self.cy + self.u[1] * a + self.v[1] * b, z)

    def xy(self, dx, dy):
        p = self.at(dx, dy)
        return (p[0], p[1])

    def dim(self, w, d, *rest):
        """A box's world dimensions, swapped if this island turns the district."""
        wd = (d, w) if self.swap else (w, d)
        return wd + tuple(rest)

    def yaw(self, deg):
        """An authored heading, turned into this island's frame (radians in)."""
        from math import atan2, degrees, radians
        ra = degrees(atan2(self.ru[1], self.ru[0]))
        ua = degrees(atan2(self.u[1], self.u[0]))
        return radians(deg + ua - ra)

    # ------------------------------------------------------------ keep-clear
    def clear(self, dx, dy, ra=0.0, rb=0.0, zones=(ENTRY, YARD)):
        """Whether an authored footprint stays out of the way in and the yard."""
        a, b = self.ab(dx, dy)
        for z in zones:
            if inside(z, a, b, ra, rb):
                return False
        return True

    def report(self, items, zones=(ENTRY, YARD)):
        """Names of authored placements that stand in a keep-clear zone."""
        bad = []
        for name, dx, dy, ra, rb in items:
            if not self.clear(dx, dy, ra, rb, zones):
                a, b = self.ab(dx, dy)
                bad.append("%s at a=%.0f b=%.0f" % (name, a, b))
        return bad


def gate(district, pad, centre=(0.0, 0.0)):
    """The point on the arterial where the tarmac stops and the yard begins."""
    u, _v = axis_frame(district, centre)
    return (district[0] + u[0] * pad, district[1] + u[1] * pad)


def crosses(path, district, radius):
    """Where a polyline first enters a district's property, as (x, y).

    Used for the rail: the line has to get through the fence, so the fence needs
    a gap exactly where it comes in rather than a guess per island.
    """
    cx, cy = district
    inside_prev = None
    for i in range(len(path)):
        px, py = path[i][0], path[i][1]
        d = hypot(px - cx, py - cy)
        here = d <= radius
        if inside_prev is None:
            inside_prev = here
            continue
        if here != inside_prev:
            qx, qy = path[i - 1][0], path[i - 1][1]
            # bisect the crossing segment - close enough for a fence gap
            for _ in range(12):
                mx, my = (px + qx) * 0.5, (py + qy) * 0.5
                if (hypot(mx - cx, my - cy) <= radius) == here:
                    px, py = mx, my
                else:
                    qx, qy = mx, my
            return ((px + qx) * 0.5, (py + qy) * 0.5)
        inside_prev = here
    return None


# ---------------------------------------------------------------- tidy pass
# Things that are MEANT to touch what they serve. A conveyor lands on a tower, a
# pipe rack runs into a column, a gantry straddles a stockpile, a loading bay is
# a platform lorries back onto. Reporting those as mistakes buries the real ones,
# and shoving them apart would disconnect the plant.
JOINED = ("Conv", "Rack", "Gantry", "Dock", "Bay", "Pipe", "Spheres", "Walls")

# Never moved: ground, the works' own fixed plant, and anything a script placed
# deliberately against something else.
# Tower and Headframe carry baked conveyor endpoints, so moving them would
# disconnect the plant they feed; everything else is free to shuffle.
PINNED = ("Pad", "Yard", "Apron", "Bays", "Fence", "Smoke", "Cliff", "Adit",
          "Headframe", "Tower") + JOINED


def _flat(name, keys):
    return any(k in name for k in keys)


MAX_DRIFT = 8.0     # how far separate() may carry a prop from its authored spot


def separate(objects, centre, key, rounds=400, gap=0.6):
    """Nudge overlapping props apart, and push them out of the way in.

    A relaxation rather than a layout: every district grows over three phases and
    on four differently-turned islands, so the placements cannot all be solved by
    hand and the ones that are wrong are wrong only on some of the twelve
    combinations. Each round moves every unpinned prop directly away from what it
    is inside, a fraction of the overlap at a time, so a knot of three or four
    settles instead of ping-ponging.

    Returns the number of props that ended up somewhere else.
    """
    u, v = axis_frame(centre)
    movable, boxes = [], {}

    def box(ob):
        pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
        xs = [p.x for p in pts]
        ys = [p.y for p in pts]
        zs = [p.z for p in pts]
        return [min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)]

    import bpy
    bpy.context.view_layer.update()

    solid = []
    for ob in objects:
        if ob.type != 'MESH' or ob.data is None or not len(ob.data.polygons):
            continue
        b = box(ob)
        if (b[5] - b[4]) < 1.5:
            continue
        boxes[ob.name] = b
        solid.append(ob)
        if not _flat(ob.name, PINNED):
            movable.append(ob)

    start = {ob.name: tuple(ob.location) for ob in movable}

    # The same drift clamp settle.py has. Without it this pass resolved a knot
    # by walking a machine metre by metre across the yard until it stood in the
    # silo row - every "how did THAT get THERE" contact traced back here.
    def _clamp(ob, nx, ny):
        s0 = start.get(ob.name)
        if s0 is None:
            return nx, ny
        off = hypot(nx - s0[0], ny - s0[1])
        if off <= MAX_DRIFT:
            return nx, ny
        k = MAX_DRIFT / off
        return s0[0] + (nx - s0[0]) * k, s0[1] + (ny - s0[1]) * k

    movable_set = set(id(ob) for ob in movable)
    half = 35.0          # the property's half-width; nothing is shoved onto the grass

    for _ in range(rounds):
        worst = 0.0
        # Resolved pair by pair rather than by summing every neighbour's push at
        # once: a prop wedged between two others gets equal and opposite shoves
        # that cancel, so the sum never moves it and the knot never comes apart.
        for i in range(len(solid)):
            oi = solid[i]
            bi = boxes[oi.name]
            for j in range(i + 1, len(solid)):
                oj = solid[j]
                bj = boxes[oj.name]
                ox = min(bi[1], bj[1]) - max(bi[0], bj[0]) + gap
                oy = min(bi[3], bj[3]) - max(bi[2], bj[2]) + gap
                oz = min(bi[5], bj[5]) - max(bi[4], bj[4])
                if ox <= 0 or oy <= 0 or oz <= 0:
                    continue
                mi = id(oi) in movable_set
                mj = id(oj) in movable_set
                if not mi and not mj:
                    continue
                worst = max(worst, min(ox, oy))
                # out along whichever axis needs the shorter shove
                if ox <= oy:
                    ci = (bi[0] + bi[1]) * 0.5
                    cj = (bj[0] + bj[1]) * 0.5
                    s_i = 1.0 if ci >= cj else -1.0
                    dxi, dyi, dxj, dyj = ox * s_i, 0.0, -ox * s_i, 0.0
                else:
                    ci = (bi[2] + bi[3]) * 0.5
                    cj = (bj[2] + bj[3]) * 0.5
                    s_i = 1.0 if ci >= cj else -1.0
                    dxi, dyi, dxj, dyj = 0.0, oy * s_i, 0.0, -oy * s_i
                # whichever of the two can move takes the whole step; if both
                # can, they share it
                if mi and mj:
                    dxi *= 0.5; dyi *= 0.5; dxj *= 0.5; dyj *= 0.5
                elif mi:
                    dxj = dyj = 0.0
                else:
                    dxi = dyi = 0.0
                for ob, b, dx, dy in ((oi, bi, dxi, dyi), (oj, bj, dxj, dyj)):
                    if dx == 0.0 and dy == 0.0:
                        continue
                    cx = (b[0] + b[1]) * 0.5 + dx - centre[0]
                    cy = (b[2] + b[3]) * 0.5 + dy - centre[1]
                    if abs(cx) > half or abs(cy) > half:
                        continue          # would end up off the property
                    nx, ny = _clamp(ob, ob.location[0] + dx, ob.location[1] + dy)
                    dx, dy = nx - ob.location[0], ny - ob.location[1]
                    if abs(dx) < 1e-6 and abs(dy) < 1e-6:
                        continue
                    ob.location = (nx, ny, ob.location[2])
                    b[0] += dx; b[1] += dx; b[2] += dy; b[3] += dy

        # and out of the corridor the lorries come in through
        for ob in movable:
            bi = boxes[ob.name]
            ax = (bi[0] + bi[1]) * 0.5 - centre[0]
            ay = (bi[2] + bi[3]) * 0.5 - centre[1]
            aa = ax * u[0] + ay * u[1]
            bb = ax * v[0] + ay * v[1]
            ra = (abs(bi[1] - bi[0]) * abs(u[0]) + abs(bi[3] - bi[2]) * abs(u[1])) * 0.5
            rb = (abs(bi[1] - bi[0]) * abs(v[0]) + abs(bi[3] - bi[2]) * abs(v[1])) * 0.5
            if not inside(ENTRY, aa, bb, ra, rb):
                continue
            push = (ENTRY[3] + rb + gap) - bb if bb >= 0 else (ENTRY[2] - rb - gap) - bb
            dx, dy = v[0] * push, v[1] * push
            nx, ny = _clamp(ob, ob.location[0] + dx, ob.location[1] + dy)
            dx, dy = nx - ob.location[0], ny - ob.location[1]
            ob.location = (nx, ny, ob.location[2])
            bi[0] += dx; bi[1] += dx; bi[2] += dy; bi[3] += dy
            worst = max(worst, abs(push))

        if worst < 0.15:
            break

    bpy.context.view_layer.update()

    n = 0
    for ob in movable:
        sx, sy, _sz = start[ob.name]
        if abs(ob.location[0] - sx) > 0.2 or abs(ob.location[1] - sy) > 0.2:
            n += 1
    return n
