"""Step 5: NW district - mine.  Phase 1 timber camp -> 3 automated pithead."""
import importlib
import layout
import parts
importlib.reload(layout)
importlib.reload(parts)
import grade
importlib.reload(grade)
import yard
importlib.reload(yard)
L = layout
P = parts

purge_collection("Mine")
C = coll("Mine")
CX, CY = L.MINE
F = yard.Frame("mine", L.MINE)

BODY = PK("wood_lt", "clad", "steel_lt")
ROOF = PK("roof_red", "roof_orange", "roof_teal")
NADITS = PK(1, 2, 3)
HEAD_H = PK(15.0, 22.0, 30.0)

# ------------------------------------------------------------------- ground
b = B().use("dirt")
b.box((72, 68, 0.3), (CX + 2, CY, 0.14))
if PHASE >= 2:
    b.use("gravel")
    b.box((34, 24, 0.34), (CX + 16, CY - 18, 0.18))
    b.box((26, 20, 0.34), (CX - 4, CY + 22, 0.18))
if PHASE >= 3:
    b.use("concrete")
    b.box((30, 22, 0.36), (CX + 14, CY + 6, 0.20))
b.make("Mine.Apron", collection=C)

# ---------------------------------------------------------------- rock face
rw = B().use("rock")
for i in range(34):
    dy = -36 + i * 2.2
    hgt = 16 + 8 * sin(i * 0.42) + RNG.uniform(-2, 2)
    rw.sphere(1.0, F.at(-34 + RNG.uniform(-3.0, 2.0), dy, hgt * 0.42), 2,
              scale=F.dim(RNG.uniform(4, 7), RNG.uniform(3, 5), hgt * 0.55))
rw.use("rock_dark")
for i in range(16):
    rw.sphere(1.0, F.at(-32 + RNG.uniform(-3, 2), -34 + i * 4.5,
                        RNG.uniform(2, 6)), 2,
              scale=F.dim(RNG.uniform(3, 5), RNG.uniform(2.5, 4.0),
                          RNG.uniform(3, 5)))
o = rw.make("Mine.Cliff", collection=C)
rough_verts(o, amount=0.9, scale=0.16, seed=4.0)

# ------------------------------------------------------------------- adits
# Slid along the cliff until each stands clear of the laid railway, which 04
# has already written to the scene. The third adit was authored at a spot the
# coal line happens to run straight through - the track came down off the
# massif and into the back of its timberwork.
_LINE = bpy.context.scene.get("rail_centreline") or []


def _rail_dist(wx, wy):
    best = 1e9
    for k in range(len(_LINE) - 1):
        ax, ay = _LINE[k]
        bx, by = _LINE[k + 1]
        vx, vy = bx - ax, by - ay
        d2 = vx * vx + vy * vy
        if d2 < 1e-9:
            continue
        t = max(0.0, min(1.0, ((wx - ax) * vx + (wy - ay) * vy) / d2))
        best = min(best, hypot(wx - (ax + vx * t), wy - (ay + vy * t)))
    return best


def _clear_of_rail(ady):
    for step in (0, -4, 4, -8, 8, -12, 12, -16, 16):
        cand = ady + step
        if abs(cand) > 34:
            continue
        wx, wy = F.xy(-25, cand)
        if _rail_dist(wx, wy) > 11.0:
            return cand
    return ady


ADY = tuple(_clear_of_rail(a) for a in (-20, 10, 30)[:NADITS])
for k, ady in enumerate(ADY):
    adx = -25
    ad = B().use(L.ORE)
    ad.box(F.dim(7.0, 7.6, 8.0), F.at(adx - 3.4, ady, 4.0))
    ad.use(PK("wood", "concrete_dk", "concrete_dk"))
    for s in (1, -1):
        ad.boxz(F.dim(2.2, 1.8, 9.0), F.at(adx, ady + s * 4.6, 0))
    ad.box(F.dim(2.2, 11.0, 1.8), F.at(adx, ady, 9.9))
    ad.use("wood")
    for s in (1, -1):
        ad.boxz(F.dim(1.3, 1.3, 8.4), F.at(adx + 1.6, ady + s * 3.6, 0))
    ad.box(F.dim(1.6, 8.6, 1.3), F.at(adx + 1.6, ady, 9.0))
    ad.use("wood_lt")
    for i in range(4):
        ad.box(F.dim(0.45, 7.6, 0.45), F.at(adx + 3.0 + i * 1.6, ady, 8.2))
    ad.make("Mine.Adit%d" % k, collection=C)
    strip([F.at(adx + 1, ady, 0), F.at(adx + 18, ady, 0),
           F.at(adx + 28, ady - 3, 0)], 3.4,
          z=0.36, name="Mine.Track%d" % k, material=mat("rock_dark"),
          collection=C)

# ----------------------------------------------------------------- headframe
hf = B().use(PK("wood", "steel_dk", "steel_dk"))
hx, hy = F.xy(-4, -18)   # back of the pad, clear of the yard and the winder
H = HEAD_H
for s in (1, -1):
    for t in (1, -1):
        hf.tube(0.5, [(hx + s * 4.5, hy + t * 4.5, 0),
                      (hx + s * 2.4, hy + t * 2.4, H)], 6)
for i in range(int(H / 3.2)):
    z = i * 3.2
    w = 4.5 - 2.1 * (z / H)
    for s in (1, -1):
        hf.tube(0.3, [(hx + s * w, hy - w, z), (hx + s * w, hy + w, z)], 4)
        hf.tube(0.3, [(hx - w, hy + s * w, z), (hx + w, hy + s * w, z)], 4)
hf.use(PK("orange", "orange", "yellow_lt"))
hf.box((7.0, 8.0, 2.2), (hx, hy, H + 1.2))
hf.use("steel")
hf.cyl(3.0, 1.2, (hx, hy, H + 2.8), (radians(90), 0, 0), 14)
hf.use(PK("wood", "steel_dk", "steel_dk"))
for t in (1, -1):
    hf.tube(0.42, [(hx + 13, hy + t * 3.5, 0), (hx + 1.5, hy + t * 2.2, H)], 5)
hf.make("Mine.Headframe", collection=C)

P.warehouse("Mine.Winder", PK(12, 16, 20), PK(9, 12, 14), PK(6, 8, 9), C,
            BODY, ROOF).location = F.at(6, -26)

# ------------------------------------------------------------- process plant
if PHASE >= 2:
    pl = B().use("clad")
    px_, py_ = F.xy(6, 18)
    ph_ = PK(0, 13, 17)
    pl.boxz((PK(0, 18, 22), PK(0, 14, 17), ph_), (px_, py_, 0.3))
    pl.use(PK("roof_red", "roof_orange", "roof_teal"))
    pl.boxz((PK(0, 18.6, 22.6), PK(0, 14.6, 17.6), 1.2), (px_, py_, ph_ + 0.3))
    pl.use("rust")
    pl.boxz((5.0, 5.0, 7.0), (px_ - 5, py_, ph_ + 1.5))
    pl.use("cream")
    for s in (1, -1):
        pl.cylz(2.4, 9.0, (px_ + 6, py_ + s * 3, ph_ + 1.5), seg=14)
    pl.make("Mine.Plant", collection=C)
    P.warehouse("Mine.Crusher", PK(0, 20, 24), PK(0, 13, 15), PK(0, 9, 11), C,
                "clad", "roof_teal").location = F.at(24, 26)
    # conveyors from the adits into the plant
    for k, ady in enumerate(ADY):
        P.conveyor(F.at(-22, ady, 8.6), (px_ - 7, py_ - 6 + k * 5, ph_ + 0.5),
                   "Mine.Conv%d" % k, C, 2.4)
    P.conveyor((px_ + 9, py_, ph_), F.at(26, 26, 11.0), "Mine.ConvOut",
               C, 2.4)

# ------------------------------------------- loading gantry over the railway
# Snapped onto the laid line rather than built at an authored spot: the gantry
# exists to straddle the track, and when the route moved it stayed behind,
# straddling grass.
gx, gy = -152.0, 40.0
if _LINE:
    _gi = min(range(len(_LINE)), key=lambda k: (_LINE[k][0] - gx) ** 2
              + (_LINE[k][1] - gy) ** 2)
    # Only when the line actually passes the mine. On the islands whose route
    # runs elsewhere the nearest point is up the coast, and snapping to it
    # would stand the gantry a hundred metres out in the wilds.
    if hypot(_LINE[_gi][0] - gx, _LINE[_gi][1] - gy) < 25.0:
        gx, gy = _LINE[_gi][0], _LINE[_gi][1]
gt = B().use("steel")
gh = PK(8.0, 10.5, 13.0)
for s in (1, -1):
    for t in (1, -1):
        gt.tube(0.34, [(gx + s * 6.0, gy + t * 6.0, 0),
                       (gx + s * 3.6, gy + t * 3.6, gh)], 5)
gt.use(PK("wood_lt", "yellow", "yellow_lt"))
gt.box((13.0, 7.0, 1.3), (gx, gy, gh + 0.7), (0, 0, radians(48)))
gt.use("steel_dk")
gt.conez(3.0, 1.1, 3.6, (gx, gy, gh - 3.5), seg=12)
gt.make("Mine.LoadGantry", collection=C)

# ------------------------------------------------------------- spoil heaps
HEAPS = PK([(26, -27, 5.0, 4.0)],
           [(27, -27, 5.6, 4.5), (13, -31, 4.6, 3.6), (2, 32, 5.0, 4.0)],
           [(28, -27, 6.2, 5.0), (14, -32, 5.2, 4.0), (-15, 35, 5.6, 4.5),
            (32, 10, 5.0, 4.0)])   # clear of the works gate at CX+36
for i, (sx, sy, rr, hh) in enumerate(HEAPS):
    o = P.coal_pile("Mine.Spoil%d" % i, rr, hh, C, seed=i * 5.1)
    o.location = F.at(sx, sy, 0.3)
    if i == 2:
        o.data.materials[0] = mat("rock_dark")

# -------------------------------------------------------------- yard plant
ex = P.excavator("Mine.Excav", C)
ex.location = F.at(-2, -28)
ex.rotation_euler = (0, 0, radians(200))
if PHASE >= 2:
    dup(ex, F.at(-16, 24), (0, 0, F.yaw(35)), None, C, "Mine.Excav2")
if PHASE >= 3:
    dup(ex, (CX + 30, CY + 26, 0.3), (0, 0, radians(-70)), None, C, "Mine.Excav3")
    P.tower_crane("Mine.Crane", 30.0, 22.0, C).location = (CX - 16, CY - 30, 0.3)

ld = P.loader("Mine.Loader", C)
ld.location = F.at(24, -33)
ld.rotation_euler = (0, 0, radians(-40))
if PHASE >= 3:
    dup(ld, F.at(-2, 36), (0, 0, F.yaw(120)), None, C, "Mine.Loader2")

# Both stand back from the gate at CX+36: a 13-long body reaches 5.6 along its
# own axis, so parking one at CX+32 put its nose out on the arterial.
tk = P.truck("Mine.Truck", PK("rust", "yellow_lt", "yellow_lt"), "coal", C)
tk.location = F.at(18, -20)
tk.rotation_euler = (0, 0, radians(150))
if PHASE >= 2:
    dup(tk, F.at(24, 18), (0, 0, F.yaw(-30)), None, C, "Mine.Truck2")

# ------------------------------------------------------------------ details
d = B().use("steel_dk")
for i in range(PK(3, 6, 8)):
    d.boxz((2.6, 2.6, 3.2), (CX - 14 + i * 5.5, CY - 33, 0.3))
d.use("wood_lt")
for i in range(PK(4, 8, 10)):
    d.boxz((1.8, 1.8, 1.6), (CX + 30 + RNG.uniform(-2, 2),
                             CY - 30 + i * 2.6, 0.3))
d.make("Mine.Clutter", collection=C)

if PHASE >= 2:
    # Open at the works gate: the arterial now stops at the pad edge instead of
    # running through the yard, so it ends against this fence - and the trucks,
    # which still drive the whole centreline, would drive through it.
    P.fence_run([(CX + 36, CY - 34, 0.3), (CX + 36, CY + 32, 0.3),
                 (CX + 6, CY + 35, 0.3)], "Mine.Fence", C,
                gaps=[L.gate_point(L.MINE, L.PAD) + (11.0,)])
for i, (lx, ly) in enumerate((((CX + 34, CY - 22), (CX + 34, CY + 14),
                               (CX + 4, CY - 32))[:PK(1, 2, 3)])):
    P.streetlight("Mine.Lamp%d" % i, 9.0, 3.0, C).location = (lx, ly, 0.3)


# Built in local terms against a flat z=0, then moved onto the graded
# pad in one go - see lib.lift_collection.
lift_collection("Mine", grade.pad_z(CX, CY))

print("mine ok", stats(), "phase", PHASE)
