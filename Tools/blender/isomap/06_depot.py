"""Step 6: NE district - coal storage depot.  Phase 1 open stockyard ->
3 full stacker/reclaimer terminal with silos and gantry."""
import importlib
import layout
import parts
importlib.reload(layout)
importlib.reload(parts)
L = layout
P = parts

purge_collection("Depot")
C = coll("Depot")
CX, CY = L.DEPOT

# --------------------------------------------------------------------- yard
b = B().use(PK("dirt", "concrete_dk", "concrete"))
b.box((72, 68, 0.3), (CX, CY, 0.14))
b.use("coal_shiny")
b.box(PK((36, 26, 0.34), (46, 32, 0.34), (54, 38, 0.34)), (CX - 2, CY + 2, 0.18))
if PHASE >= 2:
    b.use("asphalt")
    b.box((16, 60, 0.36), (CX + 27, CY - 2, 0.19))
b.make("Depot.Yard", collection=C)

# --------------------------------------------------------------- coal piles
PILES = PK([(-14, 6, 12, 8), (6, 10, 10, 7)],
           [(-18, 8, 16, 11), (2, 15, 14, 10), (20, 6, 12, 9),
            (-6, -8, 11, 7.5)],
           [(-20, 8, 18, 12), (2, 17, 16, 11), (22, 7, 14, 10),
            (-8, -9, 13, 9), (16, -14, 11, 7.5), (-24, -14, 10, 7)])
for i, (dx, dy, rr, hh) in enumerate(PILES):
    o = P.coal_pile("Depot.Pile%d" % i, rr, hh, C, seed=i * 3.7)
    o.location = (CX + dx, CY + dy, 0.3)

if PHASE >= 2:
    w = B().use("concrete")
    for i in range(5):
        w.boxz((3.0, 16.0, 4.0), (CX - 33, CY - 14 + i * 8.0, 0.3))
    w.boxz((48.0, 3.0, 4.0), (CX - 4, CY + 31, 0.3))
    w.make("Depot.Walls", collection=C)

# ------------------------------------------------- stacker / conveyor bridge
if PHASE >= 2:
    P.conveyor((18, 108, 3.0), (CX - 4, CY + 4, PK(0, 24, 28)),
               "Depot.ConvMain", C, 3.2)
    P.conveyor((CX - 4, CY + 4, PK(0, 23, 27)), (CX + 18, CY + 8, 20.0),
               "Depot.ConvA", C, 3.0)
    P.conveyor((CX - 4, CY + 4, PK(0, 23, 27)), (CX - 22, CY + 12, 20.0),
               "Depot.ConvB", C, 3.0)
    tt = B().use("steel")
    TH = PK(0, 26, 31)
    for s in (1, -1):
        for t in (1, -1):
            tt.tube(0.42, [(CX - 4 + s * 6.5, CY + 4 + t * 6.5, 0),
                           (CX - 4 + s * 3.2, CY + 4 + t * 3.2, TH)], 6)
    for i in range(int(TH / 2.9)):
        z = 2.5 + i * 2.9
        wq = 6.5 - 3.3 * (z / TH)
        for s in (1, -1):
            tt.tube(0.22, [(CX - 4 - wq, CY + 4 + s * wq, z),
                           (CX - 4 + wq, CY + 4 + s * wq, z)], 4)
            tt.tube(0.22, [(CX - 4 + s * wq, CY + 4 - wq, z),
                           (CX - 4 + s * wq, CY + 4 + wq, z)], 4)
    tt.use(PK("yellow", "yellow", "yellow_lt"))
    tt.box((10, 10, 3.0), (CX - 4, CY + 4, TH + 1.5))
    tt.use("steel_dk")
    tt.box((11, 11, 0.8), (CX - 4, CY + 4, TH + 3.2))
    tt.make("Depot.Tower", collection=C)

if PHASE >= 2:
    g = P.gantry("Depot.Gantry", PK(0, 34.0, 44.0), PK(0, 15.0, 19.0), C)
    g.location = (CX - 18, CY + 6, 0.3)

# ------------------------------------------------------------------- silos
SILOS = PK([], [(-26, "cream"), (-17, "white")],
           [(-28, "cream"), (-19, "white"), (-10, "teal"), (-1, "cream")])
for i, (dx, cm) in enumerate(SILOS):
    P.silo("Depot.Silo%d" % i, 4.4, PK(0, 17.0, 22.0), C, m=cm).location = (
        CX + dx, CY + 26, 0.3)

# --------------------------------------------------------------- warehouses
P.warehouse("Depot.Shed1", PK(18, 26, 30), PK(12, 15, 17), PK(7, 9, 11), C,
            PK("wood_lt", "clad", "clad"),
            PK("roof_red", "roof_red", "roof_red")).location = (
    CX + 22, CY + 26, 0.3)
if PHASE >= 2:
    P.warehouse("Depot.Shed2", 18, 12, 7, C, "cream", "roof_blue").location = (
        CX + 26, CY - 24, 0.3)
    P.office("Depot.Office", 12, 10, PK(1, 2, 3), C).location = (
        CX + 28, CY - 9, 0.3)

if PHASE >= 2:
    P.hopper("Depot.Hopper", 6.0, 12.0, C).location = (CX + 26, CY + 9, 0.3)
if PHASE >= 3:
    P.hopper("Depot.Hopper2", 5.4, 11.0, C).location = (CX + 26, CY - 2, 0.3)

# ------------------------------------------------------------------ vehicles
tk = P.truck("Depot.Truck", "orange", "coal", C)
tk.location = (CX + 27, CY + 14, 0.3)
tk.rotation_euler = (0, 0, radians(-90))
for i, dy in enumerate(PK((2,), (2, -10, -22), (2, -10, -22, -34))):
    dup(tk, (CX + 27, CY + dy, 0.3), (0, 0, radians(-90)), None, C,
        "Depot.Truck%d" % i)

ld = P.loader("Depot.Loader", C)
ld.location = (CX - 22, CY - 10, 0.3)
ld.rotation_euler = (0, 0, radians(40))
if PHASE >= 2:
    dup(ld, (CX + 8, CY - 20, 0.3), (0, 0, radians(160)), None, C,
        "Depot.Loader2")
    ex = P.excavator("Depot.Excav", C)
    ex.location = (CX - 28, CY + 14, 0.3)
    ex.rotation_euler = (0, 0, radians(-25))

# -------------------------------------------------------------------- detail
if PHASE >= 2:
    P.fence_run([(CX - 35, CY - 32, 0.3), (CX + 35, CY - 32, 0.3),
                 (CX + 35, CY + 32, 0.3)], "Depot.Fence", C)
for i, (lx, ly) in enumerate((((CX + 35, CY + 18), (CX + 35, CY - 16),
                               (CX - 32, CY - 26), (CX + 6, CY - 30))
                              [:PK(1, 3, 4)])):
    P.streetlight("Depot.Lamp%d" % i, 10.0, 3.2, C).location = (lx, ly, 0.3)

cc = B().use("blue")
for i in range(PK(2, 4, 6)):
    cc.boxz((7.0, 3.0, 3.0), (CX - 30 + i * 0.6, CY - 24 + i * 3.6, 0.3))
cc.make("Depot.Containers", collection=C)

print("depot ok", stats(), "phase", PHASE)
