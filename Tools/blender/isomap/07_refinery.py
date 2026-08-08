"""Step 7: refinery.  Phase 1 small kiln yard -> 3 full petrochemical plant
with columns, spheres and flare.

Laid out around a way in and an open middle, not scattered over the whole pad:
the arterial comes through the gate into an empty corridor, the corridor opens
onto the yard where the ore lands, and the plant stands on the two flanks and
across the back.  Coordinates are authored in the coal map's orientation and
rotated onto whichever island is being built - see yard.Frame.
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

purge_collection("Refinery")
C = coll("Refinery")
CX, CY = L.REFINERY
F = yard.Frame("refinery", L.REFINERY)

# --------------------------------------------------------------------- pads
b = B().use(PK("dirt", "concrete", "concrete"))
b.box(F.dim(72, 68, 0.3), F.at(0, 0, 0.14))
# The yard floor: the open middle the corridor opens onto. Concrete on every
# phase now - it used to be a small slab off to one side, so the ore heap sat
# on bare dirt beside the plant instead of on a working surface.
b.use("concrete_dk")
b.box(F.dim(PK(24, 28, 30), PK(22, 26, 26), 0.34), F.at(-2, 0, 0.18))
# The way in: tarmac from the gate to the yard, down the middle and empty.
b.use("asphalt")
b.box(F.dim(26, 16, 0.36), F.at(-26, 0, 0.19))
b.make("Refinery.Pad", collection=C)

# ------------------------------------------------------------------ columns
# Back-left, clear of the yard: dx <= -18 is behind the middle, dy >= 18 is off
# the corridor. Every one of these used to stand between the gate and the heap.
COLS = PK([(-12, 15, 2.4, 15, "cream")],
          [(-14, 15, 3.0, 24, "cream"), (-4, 15, 2.6, 20, "steel_lt"),
           (6, 15, 3.2, 27, "white")],
          [(-16, 15, 3.4, 32, "cream"), (-5, 15, 2.8, 26, "steel_lt"),
           (6, 15, 3.8, 36, "white"), (17, 15, 3.0, 28, "cream")])
for i, (dx, dy, rr, hh, cm) in enumerate(COLS):
    P.column("Refinery.Col%d" % i, rr, hh, C, m=cm).location = F.at(dx, dy)

# ------------------------------------------------------------------- stacks
STACKS = PK([(20, 16, 1.8, 20)], [(22, 20, 2.2, 30), (28, 12, 1.9, 25)],
            [(30, 20, 2.4, 38), (32, 8, 2.0, 32), (31, 32, 1.9, 28)])
for i, (dx, dy, rr, hh) in enumerate(STACKS):
    P.stack("Refinery.Stack%d" % i, rr, hh, C).location = F.at(dx, dy)
    sm = P.smoke_plume("Refinery.Smoke%d" % i, C, PK(1.8, 2.4, 2.9),
                       PK(5, 7, 9), PK(16.0, 22.0, 28.0), PK(7.0, 10.0, 13.0))
    sm.location = F.at(dx, dy, hh + 3.0)

if PHASE >= 3:
    fl = B().use("steel")
    fl.cylz(1.1, 30.0, (0, 0, 0), seg=12)
    for i in range(9):
        fl.box((3.0, 0.2, 0.2), (0, 0, 3.0 + i * 3.0))
    fl.use("headlight")
    fl.conez(1.4, 0.2, 5.0, (0, 0, 30.0), seg=10)
    fl.make("Refinery.Flare", collection=C).location = F.at(28, -6)

# -------------------------------------------------------------------- tanks
# Both flanks, outside the yard's 17-unit half-width.
TANKS = PK([(-24, -18, 5.0, 6.0, "white", "red"),
            (-24, 18, 4.4, 5.5, "cream", "blue_lt")],
           [(-26, -18, 6.5, 7.5, "white", "red"),
            (-26, 22, 5.6, 6.5, "teal", "white"),
            (-8, 20, 5.2, 7.0, "cream", "blue_lt"),
            (8, 20, 6.0, 8.0, "white", "orange")],
           [(-28, -19, 7.2, 8.5, "white", "red"),
            (-28, 25, 6.2, 7.2, "teal", "white"),
            (-15, 28, 5.8, 7.8, "cream", "blue_lt"),
            (24, -19, 6.8, 8.8, "white", "orange")])
for i, (dx, dy, rr, hh, body, band) in enumerate(TANKS):
    P.tank("Refinery.Tank%d" % i, rr, hh, C, m=body, band=band).location = \
        F.at(dx, dy)

if PHASE >= 3:
    sp = B().use("steel_lt")
    for dx, dy in ((-30, 20), (-30, -20)):
        p = F.at(dx, dy)
        sp.sphere(4.0, (p[0], p[1], 6.4), 3)
        sp.use("steel_dk")
        for a in range(4):
            an = radians(45 + a * 90)
            sp.tube(0.3, [(p[0] + cos(an) * 3.0, p[1] + sin(an) * 3.0, 0.3),
                          (p[0] + cos(an) * 3.2, p[1] + sin(an) * 3.2, 5.5)],
                    5)
        sp.use("steel_lt")
    sp.make("Refinery.Spheres", collection=C)

# ----------------------------------------------------------------- pipe work
# Along the back and down the flanks - never across the yard or the way in.
if PHASE >= 2:
    P.pipe_rack([F.at(-34, 16), F.at(-4, 16), F.at(20, 16), F.at(32, 16)],
                "Refinery.Rack1", C, PK(0, 4, 5), 0.34, 5.0)
    P.pipe_rack([F.at(20, 22), F.at(20, -18)],
                "Refinery.Rack2", C, PK(0, 3, 4), 0.30, 3.6)
if PHASE >= 3:
    P.pipe_rack([F.at(15, 24), F.at(15, -20)], "Refinery.Rack3", C, 3, 0.28, 4.4)

# ------------------------------------------------------- ore intake
# On the yard's edge facing the way in, so the tipper reaches it without
# crossing the plant, and the feed heap sits beside it rather than in the lane.
P.hopper("Refinery.Intake", PK(4.5, 6.0, 6.5), PK(8.0, 11.0, 13.0), C).location \
    = F.at(-32, 18)
if PHASE >= 2:
    P.conveyor(F.at(-32, 18, PK(0, 11.0, 13.0)), F.at(-18, 13, 22.0),
               "Refinery.ConvIn", C, 2.8)
o = P.coal_pile("Refinery.CoalIn", PK(4.0, 5.0, 5.8), PK(3.0, 3.6, 4.2), C,
                seed=9.0)
o.location = F.at(-32, 28)

# ------------------------------------------------------------ process blocks
P.warehouse("Refinery.Hall", *F.dim(PK(16, 24, 28), PK(11, 15, 17)),
            PK(8, 11, 13), C, PK("wood_lt", "clad", "clad"),
            PK("roof_red", "roof_teal", "roof_teal")).location = F.at(6, 28)
if PHASE >= 2:
    P.warehouse("Refinery.Shed", *F.dim(18, 12), 8, C, "cream",
                "roof_orange").location = F.at(27, -31)
    P.office("Refinery.Office", *F.dim(13, 11), PK(1, 2, 3), C).location = \
        F.at(-28, -28)

u = B()
for i, (bd, rf) in enumerate((("cream", "roof_blue"), ("white", "roof_red"),
                              ("cream", "roof_teal"), ("white", "roof_orange"),
                              ("cream", "roof_blue"))[:PK(2, 4, 5)]):
    u.use(bd)
    u.boxz(F.dim(6.0, 5.0, 4.0), F.at(-18 + i * 8.0, -14))
    u.use(rf)
    u.boxz(F.dim(6.6, 5.6, 0.5), F.at(-18 + i * 8.0, -14, 4.3))
u.make("Refinery.Utils", collection=C)

# ------------------------------------------------------------- loading bays
# Down one flank of the yard rather than across the mouth of it, which is where
# the bay used to sit - four metres in front of the gate.
if PHASE >= 2:
    bay = B().use("concrete_dk")
    bay.boxz(F.dim(44, 4.0, 1.4), F.at(-2, -22))
    bay.use("steel")
    for i in range(7):
        bay.boxz(F.dim(0.4, 0.4, 6.0), F.at(-22 + i * 7.6, -22, 1.7))
    bay.box(F.dim(44, 6.0, 0.4), F.at(-2, -22, 7.9))
    bay.make("Refinery.Bay", collection=C)

tk = P.truck("Refinery.Truck", "white", "cargo", C)
tk.location = F.at(-20, -28)
tk.rotation_euler = (0, 0, F.yaw(180))
for i, da in enumerate(PK((), (-4, 12), (-4, 12))):
    dup(tk, F.at(da, -28), (0, 0, F.yaw(90)), None, C, "Refinery.Truck%d" % i)
if PHASE >= 3:
    tkt = P.truck("Refinery.Tanker", "steel_lt", "tank", C)
    tkt.location = F.at(32, -16)
    tkt.rotation_euler = (0, 0, F.yaw(90))

if PHASE >= 2:
    P.fence_run([F.at(-35, 33), F.at(35, 33), F.at(35, -33), F.at(-35, -33)],
                "Refinery.Fence", C,
                gaps=[L.gate_point(L.REFINERY, L.PAD) + (12.0,)])
for i, (dx, dy) in enumerate((((-32, -22), (32, -22), (0, -32), (-34, 26))
                              [:PK(1, 3, 4)])):
    P.streetlight("Refinery.Lamp%d" % i, 10.0, 3.2, C).location = F.at(dx, dy)


# Built in local terms against a flat z=0, then moved onto the graded
# pad in one go - see lib.lift_collection.
lift_collection("Refinery", grade.pad_z(CX, CY))

print("refinery ok", stats(), "phase", PHASE)
