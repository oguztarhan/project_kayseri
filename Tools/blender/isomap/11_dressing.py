"""Step 11: forest, bushes, power lines, street lighting, roadside props.

Trees are raycast onto the assembled scene so they only land on bare land -
never on roads, pads, buildings, beach or water.  Infrastructure (pylons,
lighting) only appears once the island is developed enough to have it.
"""
import importlib
import layout
import parts
importlib.reload(layout)
importlib.reload(parts)
import grade
importlib.reload(grade)
L = layout
P = parts

purge_collection("Foliage")
purge_collection("Props")
CF = coll("Foliage")
CP = coll("Props")

scene = bpy.context.scene
dg = bpy.context.evaluated_depsgraph_get()


def ground_at(x, y):
    hit, loc, nrm, idx, obj, _ = scene.ray_cast(dg, (x, y, 460.0), (0, 0, -1))
    if not hit or obj is None or obj.name != "Ground":
        return None
    return loc.z, nrm.z


# Tree shape is per island, not just tree colour. A tall narrow 3-5 tier conifer
# is a wet northern coast; the iron map is dry ore country, so its trees are
# SHORTER, BROADER and only 2 tiers - a flat, spreading canopy that reads as
# scrub rather than forest even in silhouette. Recolouring alone left the two
# maps looking like the same wood in different light.
PINES = []
if L.DESIGN == "iron":
    for i in range(7):
        h = RNG.uniform(6.0, 10.5)
        PINES.append(P.pine("PineSrc%d" % i, h, h * RNG.uniform(0.42, 0.56), CF,
                            tiers=2, m="pine" if i % 2 else "pine_lt"))
    # and more of them, larger, so the ground between reads as scrub not lawn
    BUSHES = [P.bush("BushSrc%d" % i, RNG.uniform(2.0, 3.4), CF) for i in range(3)]
else:
    for i in range(7):
        h = RNG.uniform(9.0, 17.0)
        PINES.append(P.pine("PineSrc%d" % i, h, h * RNG.uniform(0.22, 0.30), CF,
                            tiers=RNG.randint(3, 5),
                            m="pine" if i % 2 else "pine_lt"))
    BUSHES = [P.bush("BushSrc%d" % i, RNG.uniform(1.3, 2.4), CF) for i in range(3)]
for o in PINES + BUSHES:
    o.hide_render = o.hide_viewport = True

half = L.GROUND_SIZE * 0.5 - 16

# Minimum spacing between scattered plants. Rejection sampling with no spacing
# rule puts trunks within a metre of each other often enough that the worst
# pairs read as one tree growing out of another - eleven units of canopy
# interpenetration at the top of the overlap audit. Bucketed by CELL so the
# check stays O(1) per candidate rather than O(n) over 760 pines.
CELL = 8.0


def _spacer():
    grid = {}

    def clear(x, y, gap):
        cx, cy = int(x // CELL), int(y // CELL)
        for i in (-1, 0, 1):
            for j in (-1, 0, 1):
                for px, py in grid.get((cx + i, cy + j), ()):
                    if hypot(x - px, y - py) < gap:
                        return False
        return True

    def keep(x, y):
        grid.setdefault((int(x // CELL), int(y // CELL)), []).append((x, y))

    return clear, keep


pine_clear, pine_keep = _spacer()

# Declared up here rather than beside the pylons themselves, which are built
# further down this same file: the forest is planted first, so without the
# positions in hand a tower lands on top of a tree that is already there.
PYLON_RUNS = PK([], L.PYLONS[:1], L.PYLONS)
PYLON_FEET = [p for run in PYLON_RUNS for p in run]
bush_clear, bush_keep = _spacer()

# --------------------------------------------------------------- pine forest
placed, tries = 0, 0
TARGET = PK(760, 620, 520)          # island gets progressively cleared
while placed < TARGET and tries < 90000:
    tries += 1
    x = RNG.uniform(-half, half)
    y = RNG.uniform(-half, half)
    g = ground_at(x, y)
    if g is None:
        continue
    z, nz_ = g
    # Height ABOVE the graded surface. The plain z test used to double as the
    # "don't plant on the yards" rule, because every pad was flat at z=0; once
    # the districts sit at 4-18 it passes and pines sprout across the tarmac.
    rel = z - grade.road_z(x, y)
    if nz_ < 0.80 or rel < 0.15 or rel > 42.0:
        continue
    if L.sea_depth(x, y) > -7.0:            # keep off the beach
        continue
    if L.RIVER and L.dist_to_path(x, y, L.RIVER)[0] < L.RIVER_W * 0.9:
        continue
    if any(L.dist_to_path(x, y, p)[0] < 12.5 for p in (L.ROAD_X, L.ROAD_Y)):
        continue
    if any(L.dist_to_path(x, y, p)[0] < 10.0 for p, _n in L.SPURS + L.HEADS):
        continue
    # Wide enough to clear the ring road AND its outer footway (9 units out,
    # 1.8 half-width, kerb on top), so no pine ends up standing in the pavement.
    # Against the loop itself: on the iron island it is not a circle.
    if L.dist_to_path(x, y, L.LOOP_C)[0] < 12.5:
        continue
    dq, _ = L.dist_to_path(x, y, L.RAIL)
    if dq < 9.0:
        continue
    # Out of every works yard, site, town and the quay. L.GATES is exactly that
    # list of (x, y, radius) - it is what the roads are already trimmed against,
    # so a pine can no longer sprout inside a shed. The `rel` height test above
    # used to serve as this rule back when every pad was flat at z=0; once the
    # districts sat up on graded pads it stopped rejecting anything, and nothing
    # replaced it.
    if any(hypot(x - gx, y - gy) < gr for gx, gy, gr in L.GATES):
        continue
    dens = 0.10 + 0.44 * min(1.0, max(0.0, rel / 16.0)) + \
        0.36 * min(1.0, max(0.0, (hypot(x, y) - 125.0) / 110.0))
    if RNG.random() > dens:
        continue
    if not pine_clear(x, y, 6.0):
        continue
    if any(hypot(x - px, y - py) < 13.0 for px, py in PYLON_FEET):
        continue
    s = RNG.uniform(0.75, 1.35)
    dup(PINES[RNG.randrange(len(PINES))], (x, y, z - 0.3),
        (0, 0, RNG.uniform(0, 6.28)), (s, s, s * RNG.uniform(0.85, 1.2)),
        CF, "Pine")
    pine_keep(x, y)
    placed += 1

bplaced, tries = 0, 0
while bplaced < 300 and tries < 24000:
    tries += 1
    x = RNG.uniform(-half, half)
    y = RNG.uniform(-half, half)
    g = ground_at(x, y)
    if g is None:
        continue
    z, nz_ = g
    rel = z - grade.road_z(x, y)
    if nz_ < 0.72 or rel < 0.8 or rel > 30.0:
        continue
    # Out of every works yard, site, town and the quay. L.GATES is exactly that
    # list of (x, y, radius) - it is what the roads are already trimmed against,
    # so a pine can no longer sprout inside a shed. The `rel` height test above
    # used to serve as this rule back when every pad was flat at z=0; once the
    # districts sat up on graded pads it stopped rejecting anything, and nothing
    # replaced it.
    if any(hypot(x - gx, y - gy) < gr for gx, gy, gr in L.GATES):
        continue
    if not bush_clear(x, y, 4.0):
        continue
    s = RNG.uniform(0.7, 1.6)
    dup(BUSHES[RNG.randrange(3)], (x, y, z - 0.2),
        (0, 0, RNG.uniform(0, 6.28)), (s, s, s), CF, "Bush")
    bush_keep(x, y)
    bplaced += 1

# ------------------------------------------------------------ ground clutter
# Pebbles, fallen branches and grass tufts, so the open ground has something on
# it between the trees. All batched into one mesh per kind rather than scattered
# as objects: this is a thousand pieces, and a thousand renderers on a phone is a
# thousand draw calls for something the player reads as texture.
def clutter_spot():
    """A point on open, walkable grass - or None. Same exclusions the pines use,
    plus the pavement ring, which they do not have to care about."""
    x = RNG.uniform(-half, half)
    y = RNG.uniform(-half, half)
    g = ground_at(x, y)
    if g is None:
        return None
    z, nz_ = g
    rel = z - grade.road_z(x, y)
    if nz_ < 0.86 or rel < 0.4 or rel > 34.0:
        return None
    if L.sea_depth(x, y) > -6.0:
        return None
    if L.RIVER and L.dist_to_path(x, y, L.RIVER)[0] < L.RIVER_W * 0.8:
        return None
    if any(L.dist_to_path(x, y, p)[0] < 11.0 for p in (L.ROAD_X, L.ROAD_Y)):
        return None
    if L.dist_to_path(x, y, L.LOOP_C)[0] < 11.0 or \
            L.dist_to_path(x, y, L.FOOTPATH)[0] < 5.0:
        return None
    dq, _ = L.dist_to_path(x, y, L.RAIL)
    if dq < 8.0:
        return None
    return x, y, z


def clumps(count, tries_max, place):
    """Scatter in clumps, not evenly. Ground litter gathers - and a clump reads
    at a glance where the same pieces spread thinly read as nothing at all."""
    n, tries = 0, 0
    while n < count and tries < tries_max:
        tries += 1
        s = clutter_spot()
        if s is None:
            continue
        place(s[0], s[1], s[2], n)
        n += 1
    return n


pb = B()


def _pebbles(x, y, z, k):
    pb.use("rock" if k % 3 else "rock_dark")
    for _i in range(RNG.randint(2, 5)):
        r = RNG.uniform(0.30, 0.85)
        dx, dy = RNG.uniform(-2.6, 2.6), RNG.uniform(-2.6, 2.6)
        g = ground_at(x + dx, y + dy)
        if g is None:
            continue
        pb.sphere(r, (x + dx, y + dy, g[0] + r * 0.20), 0,
                  scale=(1.0, RNG.uniform(0.7, 1.25), RNG.uniform(0.45, 0.75)))


npb = clumps(PK(180, 155, 130), 9000, _pebbles)
pb.make("Ground.Pebbles", collection=CF)

br = B().use("wood")


def _branches(x, y, z, k):
    for _i in range(RNG.randint(1, 2)):
        dx, dy = RNG.uniform(-2.0, 2.0), RNG.uniform(-2.0, 2.0)
        g = ground_at(x + dx, y + dy)
        if g is None:
            continue
        bz = g[0]
        ln = RNG.uniform(1.8, 4.0)
        a = RNG.uniform(0, 6.28)
        br.box((ln, 0.26, 0.24), (x + dx, y + dy, bz + 0.12), (0, 0, a))
        if RNG.random() < 0.6:                   # a fork off the main stick
            f = a + RNG.uniform(0.5, 1.1) * (1 if RNG.random() < 0.5 else -1)
            br.box((ln * 0.42, 0.18, 0.17),
                   (x + dx + cos(a) * ln * 0.28, y + dy + sin(a) * ln * 0.28, bz + 0.11),
                   (0, 0, f))


nbr = clumps(PK(150, 130, 110), 8000, _branches)
br.make("Ground.Branches", collection=CF)

tf = B()


def _tufts(x, y, z, k):
    tf.use("bush" if k % 4 else "pine_lt")
    for _i in range(RNG.randint(4, 7)):
        dx, dy = RNG.uniform(-2.2, 2.2), RNG.uniform(-2.2, 2.2)
        g = ground_at(x + dx, y + dy)
        if g is None:
            continue
        h = RNG.uniform(0.7, 1.5)
        tf.conez(RNG.uniform(0.20, 0.32), 0.02, h, (x + dx, y + dy, g[0] - 0.05),
                 (RNG.uniform(-0.3, 0.3), RNG.uniform(-0.3, 0.3), 0), 3)


ntf = clumps(PK(230, 200, 170), 10000, _tufts)
tf.make("Ground.Tufts", collection=CF)

print("   clutter: %d pebble, %d branch, %d tuft clumps in 3 meshes"
      % (npb, nbr, ntf))

# -------------------------------------------------------------- power lines
if PHASE >= 2:
    py_src = P.pylon("PylonSrc", 28.0, CP)
    py_src.hide_render = py_src.hide_viewport = True
    for ri, run in enumerate(PYLON_RUNS):
        tops = []
        for (x, y) in run:
            g = ground_at(x, y)
            if g is None:
                continue
            z, nz_ = g
            if nz_ < 0.7 or z < 0.5:
                continue
            dxr, _ = L.dist_to_path(x, y, L.RAIL)
            if dxr < 16:
                continue
            if any(abs(x - cx) < L.PAD + 6 and abs(y - cy) < L.PAD + 6
                   for cx, cy in L.DISTRICTS):
                continue
            if abs(y) < 18 or abs(x) < 18:
                continue
            a = atan2(run[-1][1] - run[0][1], run[-1][0] - run[0][0])
            dup(py_src, (x, y, z - 0.4), (0, 0, a + pi / 2), None, CP, "Pylon")
            tops.append((x, y, z + 28.0))
        if len(tops) > 1:
            bw = B().use("steel_dk")
            for i in range(len(tops) - 1):
                ax, ay, az = tops[i]
                bx, by, bz = tops[i + 1]
                span = hypot(bx - ax, by - ay)
                if span > 70:
                    continue
                for arm, dz in ((-8.0, 0.0), (0.0, -3.0), (8.0, 0.0)):
                    pts = []
                    for k in range(9):
                        f = k / 8.0
                        sag = -sin(f * pi) * span * 0.035
                        nx = -(by - ay) / span
                        ny = (bx - ax) / span
                        pts.append((ax + (bx - ax) * f + nx * arm,
                                    ay + (by - ay) * f + ny * arm,
                                    az + (bz - az) * f + dz + sag))
                    bw.tube(0.10, pts, 4)
            bw.make("Wires%d" % ri, collection=CP)

# ------------------------------------------------- street lighting on roads
if PHASE >= 2:
    lamp = P.streetlight("LampSrc", 8.0, 3.0, CP)
    lamp.hide_render = lamp.hide_viewport = True
    sp = PK(0, 56.0, 40.0)
    _lit = [(L.ROAD_X, 10.0), (L.ROAD_Y, 10.0)]
    if L.LOOP_C:
        _lit.append((L.LOOP_C, 8.6))
    for path, off in _lit:
        pts = [(p[0], p[1], 0.0) for p in path]
        for pos, yaw in scatter_along(pts, sp, offset=off, both=True):
            if hypot(pos.x, pos.y) < 26:
                continue
            # A lamp offset INWARD from the ring lands on an arterial whenever
            # the two happen to line up - it is only luck that keeps it off the
            # tarmac on the circular islands. Half the carriageway plus the
            # lamp arm is what it has to clear.
            if any(L.dist_to_path(pos.x, pos.y, p)[0] < L.ROAD_W * 0.5 + 3.4
                   for p in (L.ROAD_X, L.ROAD_Y)):
                continue
            # Same problem one step out: a turning head is a bulb of tarmac
            # hanging off a road, so a lamp spaced along that road can land in
            # the middle of it.
            if any(L.dist_to_path(pos.x, pos.y, h)[0] < 9.0 for h, _ in L.HEADS):
                continue
            g = ground_at(pos.x, pos.y)
            if g is None:
                continue
            z, nz_ = g
            # Height above the GRADED surface, not absolute z. The flat "within
            # 3 of zero" test only ever worked while the whole map sat at z=0;
            # with the island now standing 4-18 high it rejected every lamp
            # position on the built area and lit nothing but the coast road.
            if nz_ < 0.9 or abs(z - grade.road_z(pos.x, pos.y)) > 3.0:
                continue
            facing = yaw + (pi if (pos.x * -sin(yaw) + pos.y * cos(yaw)) > 0
                            else 0)
            dup(lamp, (pos.x, pos.y, z), (0, 0, facing + pi), None, CP, "Lamp")

# --------------------------------------------------------- roadside details
if PHASE >= 2:
    sg = B().use("steel")
    for path in (L.ROAD_X, L.ROAD_Y):
        pts = [(p[0], p[1], 0.0) for p in path]
        for pos, yaw in scatter_along(pts, 78.0, offset=9.2, both=True):
            if hypot(pos.x, pos.y) < 34:
                continue
            sg.boxz((0.24, 0.24, 3.4), (pos.x, pos.y, 0.0))
            sg.use("white")
            sg.box((0.15, 2.6, 1.7), (pos.x, pos.y, 4.2), (0, 0, yaw))
            sg.use("steel")
    sg.make("RoadSigns", collection=CP)

print("dressing ok", stats(), "pines", placed, "bushes", bplaced,
      "phase", PHASE)
