"""Step 6: storage depot.  Phase 1 open stockyard -> 3 full stacker/reclaimer
terminal with silos and gantry.

Three things are reserved before anything is placed: the way in from the road
gate, the way in for the RAIL, and the open middle the heaps stand on.  The
stacker tower used to stand dead in that middle - which is where the arterial
centreline runs and where every truck drives - and the fence had a gap only for
the road, so the train came in through a solid wall.  Coordinates are authored
in the coal map's orientation and rotated onto whichever island is being built;
see yard.Frame.
"""
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

purge_collection("Depot")
C = coll("Depot")
CX, CY = L.DEPOT
F = yard.Frame("depot", L.DEPOT)

# Where the line comes through the property boundary. Derived, not named per
# island: the rail reaches the depot from a different quarter on every map.
RAIL_IN = yard.crosses(L.RAIL, L.DEPOT, L.PAD)

# --------------------------------------------------------------------- yard
b = B().use(PK("dirt", "concrete_dk", "concrete"))
b.box(F.dim(72, 74, 0.3), F.at(0, 0, 0.14))
# The stockyard floor - the open middle, where the heaps go and where there is
# room for a lorry to turn round.
b.use(L.ORE_SHINY)
b.box(F.dim(PK(24, 28, 28), PK(24, 26, 26), 0.34), F.at(0, 2, 0.18))
# The way in: from the gate to the yard, down the middle, empty.
b.use("asphalt")
b.box(F.dim(16, 26, 0.36), F.at(0, -26, 0.19))
if PHASE >= 2:
    # The lorry lane down the east flank, clear of the yard and of the way in.
    b.box(F.dim(6, 54, 0.36), F.at(25, 2, 0.19))
b.make("Depot.Yard", collection=C)

# ---------------------------------------------------------------- ore piles
# In the middle, on the yard floor, arranged either side of the line the train
# runs in on so it can be tipped straight onto them.
PILES = PK([(-6, 2, 5.0, 4.0), (6, 2, 5.0, 4.0)],
           [(-7, -3, 5.8, 5.0), (7, -3, 5.8, 5.0), (0, 8, 5.8, 5.0)],
           [(-7, -5, 6.2, 6.0), (7, -5, 6.2, 6.0),
            (-7, 9, 6.2, 6.0), (7, 9, 6.2, 6.0)])
for i, (dx, dy, rr, hh) in enumerate(PILES):
    o = P.coal_pile("Depot.Pile%d" % i, rr, hh, C, seed=i * 3.7)
    o.location = F.at(dx, dy, 0.3)

# ------------------------------------------- stacker / conveyor bridge
# Moved off the middle onto the back edge: at (-4, 4) the tower stood on the
# yard, on the heaps and squarely on the arterial centreline the trucks drive.
#
# WHICH back corner is chosen per island rather than fixed, because the line
# reaches the depot from a different quarter on every map and the engine shed at
# the railhead is 15 wide. On copper the fixed position put the tower inside that
# shed - the "train shed in the middle of the storage building". The tower cannot
# simply be shoved clear afterwards either: three conveyors are built from its
# coordinates, so moving it later would leave them hanging in mid-air. Picked
# here, before anything is built from it.
_HEAD = (L.RAIL[-1][0], L.RAIL[-1][1])
_CANDIDATES = [(-2, 28), (-22, 26), (18, 26), (-28, 12), (28, 12)]


def _rail_clear(dx, dy):
    """How far an authored spot sits from the railhead and from the line."""
    wx, wy, _ = F.at(dx, dy)
    near = hypot(wx - _HEAD[0], wy - _HEAD[1])
    for i in range(len(L.RAIL) - 1):
        ax, ay = L.RAIL[i]
        bx, by = L.RAIL[i + 1]
        vx, vy = bx - ax, by - ay
        d2 = vx * vx + vy * vy
        if d2 < 1e-9:
            continue
        t = max(0.0, min(1.0, ((wx - ax) * vx + (wy - ay) * vy) / d2))
        near = min(near, hypot(wx - (ax + vx * t), wy - (ay + vy * t)))
    return near


TOWER = max(_CANDIDATES, key=lambda c: _rail_clear(c[0], c[1]))
if PHASE >= 2:
    P.conveyor((L.RAIL[-1][0], L.RAIL[-1][1], 3.0),
               F.at(TOWER[0], TOWER[1], PK(0, 24, 28)), "Depot.ConvMain", C, 3.2)
    P.conveyor(F.at(TOWER[0], TOWER[1], PK(0, 23, 27)), F.at(22, 24, 20.0),
               "Depot.ConvA", C, 3.0)
    P.conveyor(F.at(TOWER[0], TOWER[1], PK(0, 23, 27)), F.at(-24, 24, 20.0),
               "Depot.ConvB", C, 3.0)
    tt = B().use("steel")
    TH = PK(0, 26, 31)
    for s in (1, -1):
        for t in (1, -1):
            tt.tube(0.42, [F.at(TOWER[0] + s * 6.5, TOWER[1] + t * 6.5, 0),
                           F.at(TOWER[0] + s * 3.2, TOWER[1] + t * 3.2, TH)], 6)
    for i in range(int(TH / 2.9)):
        z = 2.5 + i * 2.9
        wq = 6.5 - 3.3 * (z / TH)
        for s in (1, -1):
            tt.tube(0.22, [F.at(TOWER[0] - wq, TOWER[1] + s * wq, z),
                           F.at(TOWER[0] + wq, TOWER[1] + s * wq, z)], 4)
            tt.tube(0.22, [F.at(TOWER[0] + s * wq, TOWER[1] - wq, z),
                           F.at(TOWER[0] + s * wq, TOWER[1] + wq, z)], 4)
    tt.use(PK("yellow", "yellow", "yellow_lt"))
    tt.box(F.dim(10, 10, 3.0), F.at(TOWER[0], TOWER[1], TH + 1.5))
    tt.use("steel_dk")
    tt.box(F.dim(11, 11, 0.8), F.at(TOWER[0], TOWER[1], TH + 3.2))
    tt.make("Depot.Tower", collection=C)

if PHASE >= 2:
    g = P.gantry("Depot.Gantry", PK(0, 34.0, 44.0), PK(0, 15.0, 19.0), C)
    g.location = F.at(-24, 22)

# ------------------------------------------------------------------- silos
SILOS = PK([], [(-29, "cream"), (-19, "white")],
           [(-31, "cream"), (-21.4, "white"), (-11.8, "teal"), (-2.2, "cream")])
for i, (dx, cm) in enumerate(SILOS):
    P.silo("Depot.Silo%d" % i, 4.4, PK(0, 17.0, 22.0), C, m=cm).location = \
        F.at(dx, 32)

# --------------------------------------------------------------- warehouses
P.warehouse("Depot.Shed1", *F.dim(PK(14, 16, 18), PK(12, 15, 17)),
            PK(7, 9, 11), C, PK("wood_lt", "clad", "clad"),
            PK("roof_red", "roof_red", "roof_red")).location = F.at(14, 30)
# East of the lorry lane, all of them - see the note on Depot.Yard.
if PHASE >= 2:
    P.warehouse("Depot.Shed2", *F.dim(13, 10), 7, C, "cream",
                "roof_blue").location = F.at(31, -22)
    P.office("Depot.Office", *F.dim(12, 10), PK(1, 2, 3), C).location = \
        F.at(32, -8)

if PHASE >= 2:
    P.hopper("Depot.Hopper", 5.0, 12.0, C).location = F.at(32, 8)

# ------------------------------------------------------------------ vehicles
tk = P.truck("Depot.Truck", "orange", "coal", C)
tk.location = F.at(25, 14)
tk.rotation_euler = (0, 0, F.yaw(-90))
tk.hide_render = tk.hide_viewport = True     # template only; the dups below are the fleet
for i, dy in enumerate(PK((2,), (2, 20), (2, 20))):
    dup(tk, F.at(25, dy), (0, 0, F.yaw(-90)), None, C, "Depot.Truck%d" % i)

ld = P.loader("Depot.Loader", C)
ld.location = F.at(-26, 14)              # on the yard edge, off the heaps
ld.rotation_euler = (0, 0, F.yaw(40))
if PHASE >= 2:
    dup(ld, F.at(26, -6), (0, 0, F.yaw(160)), None, C, "Depot.Loader2")
    ex = P.excavator("Depot.Excav", C)
    ex.location = F.at(-26, 6)
    ex.rotation_euler = (0, 0, F.yaw(-25))

# -------------------------------------------------------------------- detail
if PHASE >= 2:
    # One gap for the arterial and one for the rail. Without the second the
    # fence ran straight across the running line and the train came in
    # through a wall.
    gaps = [L.gate_point(L.DEPOT, L.PAD) + (12.0,)]
    if RAIL_IN is not None:
        gaps.append(RAIL_IN + (13.0,))
    P.fence_run([F.at(-35, 32), F.at(-35, -32), F.at(35, -32), F.at(35, 32)],
                "Depot.Fence", C, gaps=gaps)
for i, (dx, dy) in enumerate((((35, 18), (35, -16), (-32, -26), (14, -30))
                              [:PK(1, 3, 4)])):
    P.streetlight("Depot.Lamp%d" % i, 10.0, 3.2, C).location = F.at(dx, dy)

cc = B().use("blue")
for i in range(PK(2, 4, 6)):
    cc.boxz(F.dim(7.0, 3.0, 3.0), F.at(-30 + i * 0.6, -20 + i * 2.6))
cc.make("Depot.Containers", collection=C)


# Built in local terms against a flat z=0, then moved onto the graded
# pad in one go - see lib.lift_collection.
lift_collection("Depot", grade.pad_z(CX, CY))

print("depot ok", stats(), "phase", PHASE)
