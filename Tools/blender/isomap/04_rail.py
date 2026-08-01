"""Step 4: railway.  Upgrades with PHASE:
   1  short single track, timber sleepers, small loco + 5 wagons
   2  full line to the depot, ballasted, 11 wagons, signal posts
   3  double track + catenary masts, 17 wagons, plus a branch to the port
"""
import importlib
import layout
import parts
importlib.reload(layout)
importlib.reload(parts)
L = layout
P = parts

purge_collection("Rail")
CRail = coll("Rail")

RZ = 1.9
NCARS = PK(5, 11, 17)
TRACK_END = PK(0.55, 1.0, 1.0)      # phase 1 line only reaches partway
DOUBLE = PHASE >= 3

dg = bpy.context.evaluated_depsgraph_get()


def ground_z(x, y):
    hit, loc, nrm, idx, ob, _ = bpy.context.scene.ray_cast(dg, (x, y, 460.0),
                                                           (0, 0, -1))
    if hit and ob is not None and ob.name == "Ground":
        return loc.z
    return 0.0


FULL = [(p[0], p[1], 0.0) for p in L.RAIL]
SAMP_ALL = sample_bez(FULL, 520)

# where the line emerges from the massif
i0 = 0
for k, (pos, yaw) in enumerate(SAMP_ALL):
    if ground_z(pos.x, pos.y) < 4.5:
        i0 = k
        break
i1 = int(i0 + (len(SAMP_ALL) - i0) * TRACK_END)
SAMP = SAMP_ALL[i0:i1]
PATH = [(p.x, p.y, 0.0) for p, _ in SAMP[::18]]
if len(PATH) < 3:
    PATH = [(p.x, p.y, 0.0) for p, _ in SAMP]


def at(f):
    i = max(0, min(len(SAMP) - 1, int(f * (len(SAMP) - 1))))
    return SAMP[i]


def lay_track(path, samples, name, gauge=1.72, double=False):
    strip(path, PK(9.0, 11.5, 14.0), z=0.05, name=name + ".Ballast",
          material=mat("gravel"), collection=CRail)
    strip(path, PK(6.6, 8.2, 10.0), z=0.55, name=name + ".Bed",
          material=mat("rock_dark"), collection=CRail)
    bs = B().use(PK("wood", "wood", "concrete_dk"))
    for pos, yaw in scatter_along(path, PK(2.6, 2.35, 2.2)):
        bs.box((1.5, PK(5.4, 6.2, 6.8), 0.42), (pos.x, pos.y, 0.85), (0, 0, yaw))
    bs.make(name + ".Sleepers", collection=CRail)
    offs = [-gauge, gauge]
    if double:
        offs = [-gauge - 3.4, gauge - 3.4, -gauge + 3.4, gauge + 3.4]
    for s in offs:
        off = []
        for pos, yaw in samples:
            nx, ny = -sin(yaw), cos(yaw)
            off.append((pos.x + nx * s, pos.y + ny * s, 0.0))
        strip(off, 0.34, z=RZ - 0.28, name=name + ".Rail",
              material=mat("chrome"), collection=CRail, thickness=0.42)


lay_track(PATH, SAMP, "Rail", double=DOUBLE)

# ------------------------------------------------------------- tunnel portal
p0, yaw0 = at(0.006)
fx, fy = cos(yaw0), sin(yaw0)
bt = B().use(PK("wood", "concrete", "concrete"))
bt.box((3.4, PK(16.0, 22.0, 26.0), PK(11.0, 16.0, 19.0)),
       (p0.x, p0.y, PK(4.6, 6.5, 8.0)), (0, 0, yaw0))
bt.use("concrete_dk")
for i in range(13):
    a = radians(180) * i / 12.0
    bt.box((1.1, 1.5, 1.5),
           (p0.x + fx * 1.6 - sin(yaw0) * cos(a) * 5.0,
            p0.y + fy * 1.6 + cos(yaw0) * cos(a) * 5.0,
            1.2 + sin(a) * 5.6), (0, 0, yaw0))
bt.use("rock_dark")
bt.box((5.0, 8.4, 8.0), (p0.x - fx * 2.6, p0.y - fy * 2.6, 5.0), (0, 0, yaw0))
bt.cyl(4.2, 5.0, (p0.x - fx * 2.6, p0.y - fy * 2.6, 9.0),
       (0, radians(90), yaw0), 16)
bt.use("concrete_dk")
for s in (1, -1):
    bt.box((2.4, 7.0, 11.0),
           (p0.x + fx * 1.0 - sin(yaw0) * s * 8.0,
            p0.y + fy * 1.0 + cos(yaw0) * s * 8.0, 5.0), (0, 0, yaw0))
bt.make("Rail.Portal", collection=CRail)

# ------------------------------------------------ lineside masts and signals
if PHASE >= 2:
    bp = B().use("steel_dk")
    for pos, yaw in scatter_along(PATH, PK(40, 30, 22), offset=7.2):
        bp.boxz((0.4, 0.4, 8.5), (pos.x, pos.y, 0.6))
        bp.box((2.4, 0.25, 0.25), (pos.x, pos.y, 8.6), (0, 0, yaw))
        if DOUBLE:                                    # catenary wire + dropper
            bp.box((0.22, 8.0, 0.22), (pos.x, pos.y, 8.2), (0, 0, yaw + pi / 2))
    bp.make("Rail.Masts", collection=CRail)
if DOUBLE:
    bw = B().use("steel_dk")
    prev = None
    for pos, yaw in scatter_along(PATH, 22.0):
        if prev:
            bw.tube(0.09, [(prev.x, prev.y, 8.0), (pos.x, pos.y, 8.0)], 4)
        prev = pos
    bw.make("Rail.Catenary", collection=CRail)

# ---------------------------------------------------------------------- train
loco_src = P.locomotive("Loco.src", CRail)
wag_src = P.wagon("Wagon.src", True, CRail)
loco_src.hide_render = loco_src.hide_viewport = True
wag_src.hide_render = wag_src.hide_viewport = True
if PHASE == 1:                                        # small starter loco
    loco_src.scale = (0.72, 0.72, 0.72)
    wag_src.scale = (0.76, 0.76, 0.76)

GAP = PK(0.075, 0.056, 0.0455)
HEAD = PK(0.90, 0.955, 0.965)
CARS = [("loco", HEAD)] + [("wagon", HEAD - GAP - i * GAP) for i in range(NCARS)]
for kind, f in CARS:
    if not (-0.02 < f < 0.995):
        continue
    f = max(f, 0.0)
    pos, yaw = at(f)
    src = loco_src if kind == "loco" else wag_src
    off = -3.4 if DOUBLE else 0.0
    nx, ny = -sin(yaw), cos(yaw)
    dup(src, (pos.x + nx * off, pos.y + ny * off, RZ - 0.85), (0, 0, yaw),
        None, CRail, "Train." + kind)

# ------------------------------------------------------ phase 3: port branch
if PHASE >= 3:
    pp = [(p[0], p[1], 0.0) for p in L.RAIL_PORT]
    psamp = sample_bez(pp, 220)
    lay_track(pp, psamp, "RailPort")
    for f in (0.30, 0.46, 0.62):
        i = int(f * (len(psamp) - 1))
        pos, yaw = psamp[i]
        dup(wag_src, (pos.x, pos.y, RZ - 0.85), (0, 0, yaw), None, CRail,
            "PortTrain.wagon")

print("rail ok", stats(), "phase", PHASE, "cars", NCARS)
