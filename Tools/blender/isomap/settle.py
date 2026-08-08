"""Settling the whole island: off the rails, out of each other, onto the ground.

17_tidy works one district at a time, so it can only ever see a fault that lives
inside a single collection.  Most of what a player actually notices spans two:
a coal heap from Theme standing in a Depot shed, the rail shed from Rail sitting
inside the storage building, a Mine conveyor laid across the running line.  This
pass takes every built collection at once.

Three things, in order, because they constrain each other:

1. RAIL   nothing but the line's own furniture may stand on the running line.
          A train that drives through a stockpile is the loudest fault on the
          map, and unlike a road there is no steering round it.
2. CLASH  solids pushed apart pairwise, same relaxation as yard.separate but
          across collection boundaries.
3. GROUND anything whose top has ended up below the terrain is lifted so it
          sits on it.  Sinking is only ever an accident here - the build steps
          place against a road profile, and where that profile and the real
          ground disagree a shed ends up underground.

Deliberately NOT done: dropping "floating" objects.  Cranes stand on a quay over
water, tunnel mouths sit up on the massif, ghost tiers hover on purpose - a
blanket drop would bury all of them.
"""
import bpy
from mathutils import Vector
from math import hypot

import survey

# Never moved: the ground, the line, the pads everything is placed against, and
# anything whose position IS its meaning - a tunnel mouth is on the line because
# that is what a tunnel mouth is.
PINNED = ("Pad", "Yard", "Apron", "Bays", "Fence", "Ground", "Water", "Foam",
          "Cliff", "Rail.", "Track", "Ballast", "Sleeper", "Bed", "Adit",
          "Portal", "Tunnel", "Headframe", "Tower", "Deck", "Quay", "Pier",
          "Jetty", "Conv", "Rack", "Gantry", "Dock", "Bay", "Pipe", "Trestle",
          "Bridge", "Catenary", "Mast", "Loco", "Wagon", "Train", "Ship",
          "Boat", "Tug", "Beacon", "Tier", "Steam", "Smoke", "Plume", "Bench")

RAIL_HW = 5.0        # keep-clear each side of the running line
GAP = 0.8
MAX_STEP = 1.2
# How far an object may end up from where its build step put it. THIS IS THE
# WHOLE POINT OF THE PASS: it is a nudge, not a re-layout. Without it the
# relaxation happily drags a coal heap 33 metres - half the width of the entire
# district pad - until it stops overlapping anything, which it achieves by
# standing on the grass, the road and a stand of trees instead. The clash
# numbers looked perfect and the map looked far worse than before.
#
# 8 m: two movable objects can therefore open a 16 m gap between them, which
# closes almost every real overlap on the island, while staying well inside a
# yard (half-width 12-18) and nowhere near the pad edge at 36. The failure this
# guards against is a heap walking 33 m onto the grass, not a shed shuffling 6.
#
# When a clash cannot be resolved inside this budget the object stays put and
# the survey reports it. A fault that is visible is worth more than a fault that
# has been hidden by scattering the scene.
MAX_DRIFT = 8.0


def _movable(name):
    return not any(k in name for k in PINNED)


def _seg_push(cx, cy, rx, ry, path, hw):
    """How far, and which way, to shove a BOX clear of a polyline.

    Measured from the box's edge, not its centre, and returning only the
    shortfall. Pushing the centre out to a fixed radius instead - which is what
    this did - re-ran every round and dragged everything back onto a ring around
    the line, undoing the clash pass each time it fired. That is why objects
    near the railway would not come apart however many rounds it was given.
    """
    best = None
    for i in range(len(path) - 1):
        ax, ay = path[i]
        bx, by = path[i + 1]
        vx, vy = bx - ax, by - ay
        L2 = vx * vx + vy * vy
        if L2 < 1e-9:
            continue
        t = ((cx - ax) * vx + (cy - ay) * vy) / L2
        t = 0.0 if t < 0.0 else (1.0 if t > 1.0 else t)
        px, py = ax + vx * t, ay + vy * t
        gx = max(0.0, abs(cx - px) - rx)
        gy = max(0.0, abs(cy - py) - ry)
        d = hypot(gx, gy)
        if best is None or d < best[0]:
            best = (d, px, py)
    if best is None:
        return None
    d, px, py = best
    if d >= hw:
        return None
    need = hw - d
    ex, ey = cx - px, cy - py
    L = hypot(ex, ey)
    if L < 1e-4:
        return (need, 0.0)
    return (ex / L * need, ey / L * need)


def settle(L, rounds=60):
    bpy.context.view_layer.update()
    items = survey.solids()
    boxes = {}
    solid = []
    for ob, b, _col, compact in items:
        if not compact:
            continue
        boxes[ob.name] = list(b)
        solid.append(ob)
    movable = [ob for ob in solid if _movable(ob.name)]
    start = {ob.name: tuple(ob.location) for ob in movable}
    home = {ob.name: (ob.location[0], ob.location[1]) for ob in movable}

    def shift(ob, dx, dy, dz=0.0):
        hx, hy = home.get(ob.name, (ob.location[0], ob.location[1]))
        nx, ny = ob.location[0] + dx, ob.location[1] + dy
        # clamp the RESULT back inside the drift budget, not the step: a run of
        # small legal steps in one direction is exactly how it travelled 33 m
        off = hypot(nx - hx, ny - hy)
        if off > MAX_DRIFT:
            k = MAX_DRIFT / off
            nx, ny = hx + (nx - hx) * k, hy + (ny - hy) * k
        dx, dy = nx - ob.location[0], ny - ob.location[1]
        if dx == 0.0 and dy == 0.0 and dz == 0.0:
            return
        ob.location = (nx, ny, ob.location[2] + dz)
        b = boxes[ob.name]
        b[0] += dx; b[1] += dx; b[2] += dy; b[3] += dy; b[4] += dz; b[5] += dz

    path = [(p[0], p[1]) for p in getattr(L, "RAIL", [])]
    railed = set()

    def clear_rail():
        if not path:
            return 0.0
        worst = 0.0
        for ob in movable:
            b = boxes[ob.name]
            cx, cy = (b[0] + b[1]) * 0.5, (b[2] + b[3]) * 0.5
            rx, ry = (b[1] - b[0]) * 0.5, (b[3] - b[2]) * 0.5
            p = _seg_push(cx, cy, rx, ry, path, RAIL_HW)
            if p is None:
                continue
            dx = max(-14.0, min(14.0, p[0]))
            dy = max(-14.0, min(14.0, p[1]))
            shift(ob, dx, dy)
            railed.add(ob.name)
            worst = max(worst, hypot(dx, dy))
        return worst

    # ------------------------------------------------- 1+2 rail and clash
    movable_set = set(id(ob) for ob in movable)
    for _ in range(rounds):
        worst = clear_rail()
        # Re-sorted each round because the boxes move; still far cheaper than
        # the full n-squared sweep it replaces.
        order = sorted(solid, key=lambda ob: boxes[ob.name][0])
        for i in range(len(order)):
            oi = order[i]
            bi = boxes[oi.name]
            for j in range(i + 1, len(order)):
                oj = order[j]
                bj = boxes[oj.name]
                if bj[0] - bi[1] >= GAP:
                    break
                ox = min(bi[1], bj[1]) - max(bi[0], bj[0]) + GAP
                oy = min(bi[3], bj[3]) - max(bi[2], bj[2]) + GAP
                oz = min(bi[5], bj[5]) - max(bi[4], bj[4])
                if ox <= 0 or oy <= 0 or oz <= 0:
                    continue
                mi, mj = id(oi) in movable_set, id(oj) in movable_set
                if not mi and not mj:
                    continue
                if survey._legit_pair(oi.name, oj.name):
                    continue
                worst = max(worst, min(ox, oy))
                if ox <= oy:
                    s = 1.0 if (bi[0] + bi[1]) >= (bj[0] + bj[1]) else -1.0
                    di, dj = (min(ox, MAX_STEP) * s, 0.0), (-min(ox, MAX_STEP) * s, 0.0)
                else:
                    s = 1.0 if (bi[2] + bi[3]) >= (bj[2] + bj[3]) else -1.0
                    di, dj = (0.0, min(oy, MAX_STEP) * s), (0.0, -min(oy, MAX_STEP) * s)
                if mi and mj:
                    di = (di[0] * 0.5, di[1] * 0.5)
                    dj = (dj[0] * 0.5, dj[1] * 0.5)
                elif mi:
                    dj = (0.0, 0.0)
                else:
                    di = (0.0, 0.0)
                if di != (0.0, 0.0):
                    shift(oi, di[0], di[1])
                if dj != (0.0, 0.0):
                    shift(oj, dj[0], dj[1])
        if worst < 0.2:
            break
    clear_rail()          # last word: the line wins over a tight fit

    # -------------------------------------------------------------- 3 ground
    lifted = 0
    for ob in movable:
        b = boxes[ob.name]
        cx, cy = (b[0] + b[1]) * 0.5, (b[2] + b[3]) * 0.5
        gz = survey.ground_z(cx, cy)
        if gz is None:
            continue
        # Stand it ON the ground. Foundations, quay walls and pier piles are
        # meant to go down into it and are left alone.
        if not survey._has(ob.name, survey.FOOTED) and b[4] < gz - survey.BURIED:
            shift(ob, 0.0, 0.0, (gz + 0.10) - b[4])
            lifted += 1

    bpy.context.view_layer.update()
    moved = sum(1 for ob in movable
                if hypot(ob.location[0] - start[ob.name][0],
                         ob.location[1] - start[ob.name][1]) > 0.2
                or abs(ob.location[2] - start[ob.name][2]) > 0.2)
    return {"solids": len(solid), "movable": len(movable),
            "off_rail": len(railed), "lifted": lifted, "moved": moved}
