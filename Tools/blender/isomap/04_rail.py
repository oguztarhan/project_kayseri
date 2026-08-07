"""Step 4: railway.  Upgrades with PHASE:
   1  short single track, timber sleepers, small loco + 5 wagons
   2  full line to the depot, ballasted, 11 wagons, signal posts
   3  double track + catenary masts, 17 wagons
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

GZ = grade.road_z


def pitch(x, y, yaw):
    """Euler Y that lays a flat box along the track's slope - see 03_roads."""
    gx = (GZ(x + 2.0, y) - GZ(x - 2.0, y)) * 0.25
    gy = (GZ(x, y + 2.0) - GZ(x, y - 2.0)) * 0.25
    return -atan2(gx * cos(yaw) + gy * sin(yaw), 1.0)

purge_collection("Rail")
CRail = coll("Rail")

RZ = 1.9
# The rake the player can count: 3 wagons, then 5, then 7. CoalOperation clones
# the scene rake up to MaxWagons and shows as many as the TRAIN station's phase
# allows, so this is the phase-1 count that has to match its BaseWagons.
NCARS = PK(3, 5, 7)
# Full line at every phase: the train hauls mine -> depot from level 0, so the
# track has to connect. Phase 1 still reads as the early game through its lighter
# furniture - timber sleepers, no ballast profile, 5 wagons instead of 17.
TRACK_END = PK(1.0, 1.0, 1.0)
DOUBLE = PHASE >= 3
# Which of the two roads the train runs on once the line is doubled. The rails
# are laid at +-3.4 either side of the centreline, so a train driving the plain
# centreline at phase 3 runs down the six-foot between them, off both tracks.
TRAIN_OFF = -3.4 if DOUBLE else 0.0

_GROUND = bpy.data.objects.get("Ground")


def ground_z(x, y):
    """Terrain height at (x, y) - measured against the Ground mesh only.

    A scene-wide ray_cast returns whatever is highest and fell back to 0.0 when
    that was not the terrain, so anywhere the line passes under a road it read a
    15-unit drop - which would have grown a viaduct pier in the middle of a level
    crossing.
    """
    if _GROUND is None:
        return 0.0
    hit, loc, _n, _i = _GROUND.ray_cast(Vector((x, y, 460.0)),
                                        Vector((0.0, 0.0, -1.0)))
    return loc.z if hit else 0.0


FULL = [(p[0], p[1], 0.0) for p in L.RAIL]
SAMP_ALL = sample_bez(FULL, 520)

# where the line emerges from the massif
i0 = 0
for k, (pos, yaw) in enumerate(SAMP_ALL):
    # Height ABOVE the graded surface. A plain z test never tripped once the
    # land rose to 10-18, so i0 stayed at 0 and the track started inside the
    # massif with its portal 45 units under the rock.
    if ground_z(pos.x, pos.y) - GZ(pos.x, pos.y) < 4.5:
        i0 = k
        break
i1 = int(i0 + (len(SAMP_ALL) - i0) * TRACK_END)
SAMP = SAMP_ALL[i0:i1]
PATH = [(p.x, p.y, 0.0) for p, _ in SAMP[::18]]
if len(PATH) < 3:
    PATH = [(p.x, p.y, 0.0) for p, _ in SAMP]


def offset_line(samples, s):
    """Samples shifted s sideways - the road a train actually occupies."""
    if not s:
        return [(p.x, p.y) for p, _ in samples]
    return [(p.x - sin(yaw) * s, p.y + cos(yaw) * s) for p, yaw in samples]


# Hand the finished centreline to 14_routes. It cannot be re-derived from
# layout.RAIL alone: the head is trimmed by a raycast against the built massif
# and the tail by TRACK_END, so at phase 1 the track stops well short of the
# depot. The train has to run on exactly the rail that was laid - and on one
# road of it, not between the two.
bpy.context.scene["rail_centreline"] = [[float(x), float(y)]
                                        for x, y in offset_line(SAMP, TRAIN_OFF)]


def at(f):
    i = max(0, min(len(SAMP) - 1, int(f * (len(SAMP) - 1))))
    return SAMP[i]


def lay_track(path, samples, name, gauge=1.72, double=False):
    strip(path, PK(9.0, 11.5, 14.0), z=0.05, name=name + ".Ballast",
          material=mat("gravel"), collection=CRail, zfun=GZ)
    strip(path, PK(6.6, 8.2, 10.0), z=0.55, name=name + ".Bed",
          material=mat("rock_dark"), collection=CRail, zfun=GZ)
    bs = B().use(PK("wood", "wood", "concrete_dk"))
    for pos, yaw in scatter_along(path, PK(2.6, 2.35, 2.2)):
        bs.box((1.5, PK(5.4, 6.2, 6.8), 0.42),
               (pos.x, pos.y, 0.85 + GZ(pos.x, pos.y)),
               (0, pitch(pos.x, pos.y, yaw), yaw))
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
              material=mat("chrome"), collection=CRail, thickness=0.42, zfun=GZ)


lay_track(PATH, SAMP, "Rail", double=DOUBLE)

# ------------------------------------------------------- viaduct over the gorge
# Out of the tunnel the line crosses the river's canyon: for the first fifth of
# its run the ground falls to -5 while the track holds 17.8, so the track was
# simply hanging in the air. Everywhere else it rides 1.9 above the ground, which
# is the ballast, so a plain drop test finds exactly the spans that need carrying.
DECK_MIN = 3.5


def viaduct(samples, name, w=PK(9.0, 11.5, 14.0)):
    runs, cur = [], []
    for pos, yaw in samples:
        if GZ(pos.x, pos.y) - ground_z(pos.x, pos.y) > DECK_MIN:
            cur.append((pos, yaw))
        elif cur:
            runs.append(cur)
            cur = []
    if cur:
        runs.append(cur)

    pier = B().use("concrete")
    for r, run in enumerate(runs):
        if len(run) < 6:
            continue
        # Deck: a girder box under the ballast, carried right to the abutments.
        strip([(p.x, p.y, 0.0) for p, _ in run], w * 0.86, z=-1.5,
              name="%s.Deck%d" % (name, r), material=mat("concrete"),
              collection=CRail, thickness=1.4, zfun=GZ)
        step = max(1, int(len(run) / max(1, int(_run_len(run) / 24.0))))
        for k in range(step // 2, len(run), step):
            pos, yaw = run[k]
            g = ground_z(pos.x, pos.y)
            h = GZ(pos.x, pos.y) - 1.5 - g
            if h < 2.0:
                continue
            # Tapered so it reads as a masonry pier rather than a post.
            pier.conez(2.6, 1.7, h, (pos.x, pos.y, g), (0, 0, yaw), seg=8)
            pier.box((7.2, 2.2, 1.1), (pos.x, pos.y, g + h - 0.55), (0, 0, yaw))
    pier.make(name + ".Piers", collection=CRail)


def _run_len(run):
    t = 0.0
    for i in range(len(run) - 1):
        t += hypot(run[i + 1][0].x - run[i][0].x, run[i + 1][0].y - run[i][0].y)
    return max(t, 1.0)


viaduct(SAMP, "Rail")

# ------------------------------------------------------------- tunnel portal
_NUDGE = getattr(L, "PORTAL_NUDGE", [])


def bore(t, into, name, idx):
    """A tunnel mouth on the line at arc position t.

    `into` is the direction the rock lies in: +1 when the hill is ahead of the
    train at t (the mouth it drives INTO) and -1 when it is behind (the mouth it
    comes OUT of). The whole structure is simply turned round, because a portal
    is a face on a hillside and which way it looks is the only thing that
    changes.

    This used to be inline and built exactly once, at the railhead. The iron
    island's line runs THROUGH a massif in the middle of the frame, and a bore
    with no mouths is just track disappearing into a slope.
    """
    p0, yaw0 = at(t)
    if into > 0:
        yaw0 += radians(180)
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
    # THE HOLE. Everything above is the surround; without this the mouth is a
    # concrete arch laid flat against a hillside, which is what it looked like -
    # an entrance that is not open. A near-black slab set just inside the arch
    # and slightly narrower than it reads as depth: the eye takes an unlit
    # opening as a tunnel going in, and no amount of arch detail does that on
    # its own. Unlit rather than pure black so it still catches a little bounce.
    bt.use("tunnel_void")
    bt.box((1.2, 8.6, 8.2), (p0.x + fx * 1.15, p0.y + fy * 1.15, 5.0), (0, 0, yaw0))
    bt.cyl(4.3, 1.2, (p0.x + fx * 1.15, p0.y + fy * 1.15, 9.1),
           (0, radians(90), yaw0), 16)
    bt.use("concrete_dk")
    for s in (1, -1):
        bt.box((2.4, 7.0, 11.0),
               (p0.x + fx * 1.0 - sin(yaw0) * s * 8.0,
                p0.y + fy * 1.0 + cos(yaw0) * s * 8.0, 5.0), (0, 0, yaw0))
    ob = bt.make(name, collection=CRail)
    # A single offset is right here, unlike the track: the portal is one compact
    # structure at the tunnel mouth rather than something spanning the whole fall.
    ob.location.z += GZ(p0.x, p0.y)
    # The island's own seating offset, if it has one - see PORTAL_NUDGE in
    # isle_iron. Applied last, so it reads as a nudge off the computed position
    # rather than replacing it: move the line or the peaks and the portal still
    # follows, with the same offset held.
    if idx < len(_NUDGE):
        _dx, _dy, _dz = _NUDGE[idx]
        ob.location.x += _dx
        ob.location.y += _dy
        ob.location.z += _dz
    return ob


# The railhead. Its bore starts at t = 0, so there is no mouth to build at the
# near end - the line simply begins inside the rock.
bore(0.006, -1, "Rail.Portal", 0)

# Every bore AFTER the first is a through-tunnel: the train has to be seen going
# in one side and coming out the other, so both ends get a mouth.
for _i, (_a, _b) in enumerate(L.TUNNEL[1:]):
    bore(_a, 1, "Rail.PortalIn%d" % _i, 1 + _i * 2)
    bore(_b, -1, "Rail.PortalOut%d" % _i, 2 + _i * 2)

# --------------------------------------------------------- storage rail shed
# The line ends inside the depot yard, where the train used to blink out of
# existence with its rake left standing on the open slab. It runs into a shed
# instead: the doorway is exported as an anchor and CoalOperation hides each car
# as it passes, so the train is swallowed a wagon at a time and drawn back out
# the same way.
# Depth along the track. Longer than a locomotive, so a car is fully under the
# roof before it stops being drawn - but not so long that the doorway reaches
# the north-south arterial, which runs up the middle of the yard under its slab
# and is the road the ore trucks drive to the storage gate.
SHED_L = 12.0
SHED_W = PK(11.0, 12.0, 15.0)      # phase 3 is double track: both roads covered
SHED_H = PK(7.5, 8.5, 10.0)


def back_along(dist):
    """Index of the sample `dist` metres back from the end of the laid track."""
    t, i = 0.0, len(SAMP) - 1
    while i > 1 and t < dist:
        t += hypot(SAMP[i][0].x - SAMP[i - 1][0].x,
                   SAMP[i][0].y - SAMP[i - 1][0].y)
        i -= 1
    return i


door_i = back_along(SHED_L)
run_ = SAMP[door_i:]
pd, yd = SAMP[door_i]
pe, ye = SAMP[-1]
# A straight hall on the chord of the last stretch, not a ribbon bent along it:
# the track still turns a few degrees in here, so the walls are pushed out by
# however far it wanders off that chord rather than by a guessed margin.
alen = hypot(pe.x - pd.x, pe.y - pd.y)
ux, uy = (pe.x - pd.x) / alen, (pe.y - pd.y) / alen
sx, sy = -uy, ux
_off = [(p.x - pd.x) * sx + (p.y - pd.y) * sy for p, _ in run_]
mid = (max(_off) + min(_off)) * 0.5
W = SHED_W + (max(_off) - min(_off))
yaw = atan2(uy, ux)
cx = (pd.x + pe.x) * 0.5 + sx * mid
cy = (pd.y + pe.y) * 0.5 + sy * mid
gz = GZ(cx, cy)
RISE = W * 0.24

bh = B().use("concrete_dk")
bh.boxz((alen + 1.4, W + 1.4, 0.5), (cx, cy, gz), (0, 0, yaw))
bh.use("clad")
for s in (1, -1):                                          # side walls
    bh.boxz((alen, 0.8, SHED_H), (cx + sx * s * W * 0.5, cy + sy * s * W * 0.5,
                                  gz + 0.5), (0, 0, yaw))
bh.boxz((0.9, W, SHED_H), (pe.x + ux * 1.0, pe.y + uy * 1.0, gz + 0.5),
        (0, 0, yaw))                                       # back wall
bh.use("roof_blue")
bh.roof((W + 1.4, alen + 1.4, RISE), (cx, cy, gz + SHED_H + 0.5),
        (0, 0, yaw - pi * 0.5))
# The island is seen from the south-east, so the back wall - not the doorway -
# is the face the player reads the building by. Give it a shutter to match.
bh.use("orange")
bh.box((0.5, W * 0.6, SHED_H * 0.62),
       (pe.x + ux * 1.7, pe.y + uy * 1.7, gz + 0.5 + SHED_H * 0.31), (0, 0, yaw))
bh.use("winlight")
for i in range(int(alen / 4.5)):
    for s in (1, -1):
        bh.box((2.0, 0.3, 1.0),
               (cx + ux * (-alen * 0.5 + 3.0 + i * 4.5) + sx * s * (W * 0.5 + 0.3),
                cy + uy * (-alen * 0.5 + 3.0 + i * 4.5) + sy * s * (W * 0.5 + 0.3),
                gz + 0.5 + SHED_H * 0.74), (0, 0, yaw))
bh.use("concrete_dk")                                      # doorway frame
bh.box((1.6, W + 2.8, 1.8), (pd.x, pd.y, gz + SHED_H + 1.2), (0, 0, yaw))
for s in (1, -1):
    bh.boxz((1.6, 1.6, SHED_H + 2.1),
            (pd.x + sx * s * (W * 0.5 + 0.8), pd.y + sy * s * (W * 0.5 + 0.8), gz),
            (0, 0, yaw))
bh.use(PK("wood", "steel_dk", "steel_dk"))                 # buffer stop
bh.box((1.0, 4.4, 1.6), (pe.x - ux * 0.6, pe.y - uy * 0.6, gz + 1.4), (0, 0, yaw))
bh.make("Rail.Shed", collection=CRail)
if PHASE >= 2:
    P.streetlight("Rail.ShedLamp", 9.0, 3.0, CRail).location = (
        pd.x + sx * (W * 0.5 + 3.2), pd.y + sy * (W * 0.5 + 3.2), gz)

# The doorway, on the road the train runs on - 14_routes exports it as an
# anchor so Unity can turn it into a distance along the rail.
_dx, _dy = offset_line([(pd, yd)], TRAIN_OFF)[0]
bpy.context.scene["rail_shed_door"] = [float(_dx), float(_dy)]

# ------------------------------------------------ lineside masts and signals
if PHASE >= 2:
    bp = B().use("steel_dk")
    for pos, yaw in scatter_along(PATH, PK(40, 30, 22), offset=7.2):
        gz = GZ(pos.x, pos.y)
        bp.boxz((0.4, 0.4, 8.5), (pos.x, pos.y, 0.6 + gz))
        bp.box((2.4, 0.25, 0.25), (pos.x, pos.y, 8.6 + gz), (0, 0, yaw))
        if DOUBLE:                                    # catenary wire + dropper
            bp.box((0.22, 8.0, 0.22), (pos.x, pos.y, 8.2 + gz), (0, 0, yaw + pi / 2))
    bp.make("Rail.Masts", collection=CRail)
if DOUBLE:
    bw = B().use("steel_dk")
    prev = None
    for pos, yaw in scatter_along(PATH, 22.0):
        if prev:
            bw.tube(0.09, [(prev.x, prev.y, 8.0 + GZ(prev.x, prev.y)),
                           (pos.x, pos.y, 8.0 + GZ(pos.x, pos.y))], 4)
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

# Car spacing in METRES along the rail, not as a fraction of the sample count.
# SAMP is evenly spaced in CURVE PARAMETER, so a fixed step through it covers
# different distances on the straights than through the curves - which is why a
# 0.0455 fractional pitch put 5.2 of every car inside the one in front. The
# pitch here is the longest body (the loco, 13.6) plus a coupling.
CAR_PITCH = PK(11.0, 13.0, 15.5)
_CUM = [0.0]
for _i in range(1, len(SAMP)):
    _CUM.append(_CUM[-1] + hypot(SAMP[_i][0].x - SAMP[_i - 1][0].x,
                                 SAMP[_i][0].y - SAMP[_i - 1][0].y))
_TRACK = _CUM[-1] or 1.0


def at_dist(d):
    """The sample `d` metres along the laid track."""
    lo, hi = 0, len(_CUM) - 1
    while lo < hi:
        mid = (lo + hi) // 2
        if _CUM[mid] < d:
            lo = mid + 1
        else:
            hi = mid
    return SAMP[lo]


HEAD = PK(0.90, 0.955, 0.965)
for _k, kind in enumerate(["loco"] + ["wagon"] * NCARS):
    d = HEAD * _TRACK - _k * CAR_PITCH
    if d < 0.0:
        continue
    pos, yaw = at_dist(d)
    src = loco_src if kind == "loco" else wag_src
    nx, ny = -sin(yaw), cos(yaw)
    tx, ty = pos.x + nx * TRAIN_OFF, pos.y + ny * TRAIN_OFF
    dup(src, (tx, ty, RZ - 0.85 + GZ(tx, ty)),
        (0, pitch(tx, ty, yaw), yaw), None, CRail, "Train." + kind)

print("rail ok", stats(), "phase", PHASE, "cars", NCARS)
