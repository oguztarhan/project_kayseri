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


PINES = []
for i in range(7):
    h = RNG.uniform(9.0, 17.0)
    PINES.append(P.pine("PineSrc%d" % i, h, h * RNG.uniform(0.22, 0.30), CF,
                        tiers=RNG.randint(3, 5),
                        m="pine" if i % 2 else "pine_lt"))
BUSHES = [P.bush("BushSrc%d" % i, RNG.uniform(1.3, 2.4), CF) for i in range(3)]
for o in PINES + BUSHES:
    o.hide_render = o.hide_viewport = True

half = L.GROUND_SIZE * 0.5 - 16

# --------------------------------------------------------------- pine forest
placed, tries = 0, 0
TARGET = PK(760, 620, 520)          # island gets progressively cleared
while placed < TARGET and tries < 30000:
    tries += 1
    x = RNG.uniform(-half, half)
    y = RNG.uniform(-half, half)
    g = ground_at(x, y)
    if g is None:
        continue
    z, nz_ = g
    if nz_ < 0.80 or z < 0.15 or z > 42.0:
        continue
    if L.sea_depth(x, y) > -7.0:            # keep off the beach
        continue
    dr, _ = L.dist_to_path(x, y, L.RIVER)
    if dr < L.RIVER_W * 0.9:
        continue
    if abs(y) < 12.5 or abs(x) < 12.5:
        continue
    dl, _ = L.dist_to_path(x, y, L.LOOP_C)
    if dl < 10.0:
        continue
    dq, _ = L.dist_to_path(x, y, L.RAIL)
    if dq < 9.0:
        continue
    dens = 0.10 + 0.44 * min(1.0, max(0.0, z / 16.0)) + \
        0.36 * min(1.0, max(0.0, (hypot(x, y) - 125.0) / 110.0))
    if RNG.random() > dens:
        continue
    s = RNG.uniform(0.75, 1.35)
    dup(PINES[RNG.randrange(len(PINES))], (x, y, z - 0.3),
        (0, 0, RNG.uniform(0, 6.28)), (s, s, s * RNG.uniform(0.85, 1.2)),
        CF, "Pine")
    placed += 1

bplaced, tries = 0, 0
while bplaced < 300 and tries < 8000:
    tries += 1
    x = RNG.uniform(-half, half)
    y = RNG.uniform(-half, half)
    g = ground_at(x, y)
    if g is None:
        continue
    z, nz_ = g
    if nz_ < 0.72 or z < 0.8 or z > 30.0:
        continue
    s = RNG.uniform(0.7, 1.6)
    dup(BUSHES[RNG.randrange(3)], (x, y, z - 0.2),
        (0, 0, RNG.uniform(0, 6.28)), (s, s, s), CF, "Bush")
    bplaced += 1

# -------------------------------------------------------------- power lines
if PHASE >= 2:
    PYLON_RUNS = PK([], [[(196, -30), (162, -66), (128, -102), (94, -138)]],
                    [[(196, -30), (162, -66), (128, -102), (94, -138)],
                     [(52, 178), (86, 146), (120, 114), (154, 82), (188, 50)]])
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
    for path, off in ((L.ROAD_X, 10.0), (L.ROAD_Y, 10.0), (L.LOOP_C, 8.6)):
        pts = [(p[0], p[1], 0.0) for p in path]
        for pos, yaw in scatter_along(pts, sp, offset=off, both=True):
            if hypot(pos.x, pos.y) < 26:
                continue
            g = ground_at(pos.x, pos.y)
            if g is None:
                continue
            z, nz_ = g
            if nz_ < 0.9 or abs(z) > 3.0:
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
