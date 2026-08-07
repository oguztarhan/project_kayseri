"""Step 5: NW district - mine.  Phase 1 timber camp -> 3 automated pithead."""
import importlib
import layout
import parts
importlib.reload(layout)
importlib.reload(parts)
import grade
importlib.reload(grade)
L = layout
P = parts

purge_collection("Mine")
C = coll("Mine")
CX, CY = L.MINE

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
    y = CY - 36 + i * 2.2
    hgt = 16 + 8 * sin(i * 0.42) + RNG.uniform(-2, 2)
    rw.sphere(1.0, (CX - 34 + RNG.uniform(-3.0, 2.0), y, hgt * 0.42), 2,
              scale=(RNG.uniform(4, 7), RNG.uniform(3, 5), hgt * 0.55))
rw.use("rock_dark")
for i in range(16):
    rw.sphere(1.0, (CX - 32 + RNG.uniform(-3, 2), CY - 34 + i * 4.5,
                    RNG.uniform(2, 6)), 2,
              scale=(RNG.uniform(3, 5), RNG.uniform(2.5, 4.0),
                     RNG.uniform(3, 5)))
o = rw.make("Mine.Cliff", collection=C)
rough_verts(o, amount=0.9, scale=0.16, seed=4.0)

# ------------------------------------------------------------------- adits
ADY = (CY - 20, CY + 10, CY + 30)[:NADITS]
for k, ay in enumerate(ADY):
    ax = CX - 25
    ad = B().use(L.ORE)
    ad.box((7.0, 7.6, 8.0), (ax - 3.4, ay, 4.0))
    ad.use(PK("wood", "concrete_dk", "concrete_dk"))
    for s in (1, -1):
        ad.boxz((2.2, 1.8, 9.0), (ax, ay + s * 4.6, 0))
    ad.box((2.2, 11.0, 1.8), (ax, ay, 9.9))
    ad.use("wood")
    for s in (1, -1):
        ad.boxz((1.3, 1.3, 8.4), (ax + 1.6, ay + s * 3.6, 0))
    ad.box((1.6, 8.6, 1.3), (ax + 1.6, ay, 9.0))
    ad.use("wood_lt")
    for i in range(4):
        ad.box((0.45, 7.6, 0.45), (ax + 3.0 + i * 1.6, ay, 8.2))
    ad.make("Mine.Adit%d" % k, collection=C)
    strip([(ax + 1, ay, 0), (ax + 18, ay, 0), (ax + 28, ay - 3, 0)], 3.4,
          z=0.36, name="Mine.Track%d" % k, material=mat("rock_dark"),
          collection=C)

# ----------------------------------------------------------------- headframe
hf = B().use(PK("wood", "steel_dk", "steel_dk"))
hx, hy = CX + 2, CY - 4
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
            BODY, ROOF).location = (CX + 18, CY - 8, 0.3)

# ------------------------------------------------------------- process plant
if PHASE >= 2:
    pl = B().use("clad")
    px_, py_ = CX + 6, CY + 18
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
                "clad", "roof_teal").location = (CX + 21, CY + 20, 0.3)
    # conveyors from the adits into the plant
    for k, ay in enumerate(ADY):
        P.conveyor((CX - 22, ay, 8.6), (px_ - 7, py_ - 6 + k * 5, ph_ + 0.5),
                   "Mine.Conv%d" % k, C, 2.4)
    P.conveyor((px_ + 9, py_, ph_), (CX + 26, CY + 22, 11.0), "Mine.ConvOut",
               C, 2.4)

# ------------------------------------------- loading gantry over the railway
gx, gy = -152.0, 40.0
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
HEAPS = PK([(24, -26, 9, 6)],
           [(26, -26, 11, 7), (10, -30, 8, 5), (-8, 30, 9, 6)],
           [(28, -26, 13, 8), (12, -32, 10, 6), (-8, 30, 11, 7),
            (24, 20, 9, 6)])   # clear of the works gate at CX+36
for i, (sx, sy, rr, hh) in enumerate(HEAPS):
    o = P.coal_pile("Mine.Spoil%d" % i, rr, hh, C, seed=i * 5.1)
    o.location = (CX + sx, CY + sy, 0.3)
    if i == 2:
        o.data.materials[0] = mat("rock_dark")

# -------------------------------------------------------------- yard plant
ex = P.excavator("Mine.Excav", C)
ex.location = (CX + 22, CY - 30, 0.3)
ex.rotation_euler = (0, 0, radians(200))
if PHASE >= 2:
    dup(ex, (CX - 12, CY + 30, 0.3), (0, 0, radians(35)), None, C, "Mine.Excav2")
if PHASE >= 3:
    dup(ex, (CX + 30, CY + 26, 0.3), (0, 0, radians(-70)), None, C, "Mine.Excav3")
    P.tower_crane("Mine.Crane", 30.0, 22.0, C).location = (CX - 16, CY - 30, 0.3)

ld = P.loader("Mine.Loader", C)
ld.location = (CX + 12, CY - 24, 0.3)
ld.rotation_euler = (0, 0, radians(-40))
if PHASE >= 3:
    dup(ld, (CX + 4, CY + 30, 0.3), (0, 0, radians(120)), None, C, "Mine.Loader2")

# Both stand back from the gate at CX+36: a 13-long body reaches 5.6 along its
# own axis, so parking one at CX+32 put its nose out on the arterial.
tk = P.truck("Mine.Truck", PK("rust", "yellow_lt", "yellow_lt"), "coal", C)
tk.location = (CX + 20, CY - 20, 0.3)
tk.rotation_euler = (0, 0, radians(150))
if PHASE >= 2:
    dup(tk, (CX + 28, CY + 4, 0.3), (0, 0, radians(-30)), None, C, "Mine.Truck2")

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
