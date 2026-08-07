"""Step 7: SE district - refinery.  Phase 1 small kiln yard -> 3 full
petrochemical plant with columns, spheres and flare."""
import importlib
import layout
import parts
importlib.reload(layout)
importlib.reload(parts)
import grade
importlib.reload(grade)
L = layout
P = parts

purge_collection("Refinery")
C = coll("Refinery")
CX, CY = L.REFINERY

# --------------------------------------------------------------------- pads
b = B().use(PK("dirt", "concrete", "concrete"))
b.box((72, 68, 0.3), (CX, CY, 0.14))
b.use("concrete_dk")
b.box(PK((26, 18, 0.34), (40, 26, 0.34), (48, 32, 0.34)), (CX - 2, CY + 6, 0.18))
if PHASE >= 2:
    b.use("asphalt")
    b.box((62, 14, 0.36), (CX - 2, CY - 26, 0.19))
b.make("Refinery.Pad", collection=C)

# ------------------------------------------------------------------ columns
COLS = PK([(-14, 10, 2.4, 15, "cream")],
          [(-16, 12, 3.0, 24, "cream"), (-6, 16, 2.6, 20, "steel_lt"),
           (4, 13, 3.2, 27, "white")],
          [(-18, 12, 3.4, 32, "cream"), (-9, 17, 2.8, 26, "steel_lt"),
           (2, 15, 3.8, 36, "white"), (13, 11, 3.0, 28, "cream"),
           (-20, 0, 2.6, 22, "steel_lt")])
for i, (dx, dy, rr, hh, cm) in enumerate(COLS):
    P.column("Refinery.Col%d" % i, rr, hh, C, m=cm).location = (
        CX + dx, CY + dy, 0.3)

# ------------------------------------------------------------------- stacks
STACKS = PK([(20, 16, 1.8, 20)], [(22, 20, 2.2, 30), (28, 12, 1.9, 25)],
            [(24, 22, 2.4, 38), (30, 13, 2.0, 32), (18, 28, 1.9, 28)])
for i, (dx, dy, rr, hh) in enumerate(STACKS):
    P.stack("Refinery.Stack%d" % i, rr, hh, C).location = (CX + dx, CY + dy, 0.3)
    sm = P.smoke_plume("Refinery.Smoke%d" % i, C, PK(1.8, 2.4, 2.9),
                       PK(5, 7, 9), PK(16.0, 22.0, 28.0), PK(7.0, 10.0, 13.0))
    sm.location = (CX + dx, CY + dy, hh + 3.0)

if PHASE >= 3:
    fl = B().use("steel")
    fl.cylz(1.1, 30.0, (0, 0, 0), seg=12)
    for i in range(9):
        fl.box((3.0, 0.2, 0.2), (0, 0, 3.0 + i * 3.0))
    fl.use("headlight")
    fl.conez(1.4, 0.2, 5.0, (0, 0, 30.0), seg=10)
    fl.make("Refinery.Flare", collection=C).location = (CX + 28, CY - 6, 0.3)

# -------------------------------------------------------------------- tanks
TANKS = PK([(-24, -8, 5.0, 6.0, "white", "red"),
            (-24, 8, 4.4, 5.5, "cream", "blue_lt")],
           [(-26, -10, 6.5, 7.5, "white", "red"),
            (-26, 20, 5.6, 6.5, "teal", "white"),
            (-6, -6, 5.2, 7.0, "cream", "blue_lt"),
            (8, -4, 6.0, 8.0, "white", "orange")],
           [(-28, -10, 7.2, 8.5, "white", "red"),
            (-28, 22, 6.2, 7.2, "teal", "white"),
            (-7, -7, 5.8, 7.8, "cream", "blue_lt"),
            (9, -5, 6.8, 8.8, "white", "orange"),
            (22, -14, 5.4, 6.8, "blue_lt", "white"),
            (-16, -20, 5.0, 6.2, "teal", "orange")])
for i, (dx, dy, rr, hh, body, band) in enumerate(TANKS):
    P.tank("Refinery.Tank%d" % i, rr, hh, C, m=body, band=band).location = (
        CX + dx, CY + dy, 0.3)

if PHASE >= 3:
    sp = B().use("steel_lt")
    for dx, dy in ((-32, 6), (-32, -2)):
        sp.sphere(4.0, (CX + dx, CY + dy, 6.4), 3)
        sp.use("steel_dk")
        for a in range(4):
            an = radians(45 + a * 90)
            sp.tube(0.3, [(CX + dx + cos(an) * 3.0, CY + dy + sin(an) * 3.0, 0.3),
                          (CX + dx + cos(an) * 3.2, CY + dy + sin(an) * 3.2, 5.5)],
                    5)
        sp.use("steel_lt")
    sp.make("Refinery.Spheres", collection=C)

# ----------------------------------------------------------------- pipe work
if PHASE >= 2:
    P.pipe_rack([(CX - 34, CY + 6, 0.3), (CX - 4, CY + 6, 0.3),
                 (CX + 20, CY + 4, 0.3), (CX + 32, CY + 4, 0.3)],
                "Refinery.Rack1", C, PK(0, 4, 5), 0.34, 5.0)
    P.pipe_rack([(CX + 0, CY + 22, 0.3), (CX + 0, CY - 18, 0.3)],
                "Refinery.Rack2", C, PK(0, 3, 4), 0.30, 3.6)
if PHASE >= 3:
    P.pipe_rack([(CX + 15, CY + 24, 0.3), (CX + 15, CY - 20, 0.3)],
                "Refinery.Rack3", C, 3, 0.28, 4.4)

# ------------------------------------------------------- coal intake
P.hopper("Refinery.Intake", PK(4.5, 6.0, 6.5), PK(8.0, 11.0, 13.0), C).location = (
    CX - 32, CY + 18, 0.3)
if PHASE >= 2:
    P.conveyor((CX - 32, CY + 18, PK(0, 11.0, 13.0)), (CX - 18, CY + 13, 22.0),
               "Refinery.ConvIn", C, 2.8)
o = P.coal_pile("Refinery.CoalIn", PK(6.0, 8.0, 9.5), PK(4.0, 5.0, 6.0), C,
                seed=9.0)
o.location = (CX - 32, CY + 28, 0.3)

# ------------------------------------------------------------ process blocks
P.warehouse("Refinery.Hall", PK(16, 24, 28), PK(11, 15, 17), PK(8, 11, 13), C,
            PK("wood_lt", "clad", "clad"), PK("roof_red", "roof_teal", "roof_teal")
            ).location = (CX + 4, CY + 23, 0.3)
if PHASE >= 2:
    P.warehouse("Refinery.Shed", 18, 12, 8, C, "cream", "roof_orange").location = (
        CX + 28, CY + 25, 0.3)
    P.office("Refinery.Office", 13, 11, PK(1, 2, 3), C).location = (
        CX - 28, CY - 28, 0.3)

u = B()
for i, (bd, rf) in enumerate((("cream", "roof_blue"), ("white", "roof_red"),
                              ("cream", "roof_teal"), ("white", "roof_orange"),
                              ("cream", "roof_blue"))[:PK(2, 4, 5)]):
    u.use(bd)
    u.boxz((6.0, 5.0, 4.0), (CX - 18 + i * 8.0, CY - 16, 0.3))
    u.use(rf)
    u.boxz((6.6, 5.6, 0.5), (CX - 18 + i * 8.0, CY - 16, 4.3))
u.make("Refinery.Utils", collection=C)

# ------------------------------------------------------------- loading bays
if PHASE >= 2:
    bay = B().use("concrete_dk")
    bay.boxz((48, 4.0, 1.4), (CX - 2, CY - 22, 0.3))
    bay.use("steel")
    for i in range(7):
        bay.boxz((0.4, 0.4, 6.0), (CX - 22 + i * 7.6, CY - 22, 1.7))
    bay.box((48, 6.0, 0.4), (CX - 2, CY - 22, 7.9))
    bay.make("Refinery.Bay", collection=C)

tk = P.truck("Refinery.Truck", "white", "cargo", C)
tk.location = (CX - 20, CY - 28, 0.3)
tk.rotation_euler = (0, 0, radians(180))
for i, dx in enumerate(PK((), (-4, 12), (-4, 12, 26))):
    dup(tk, (CX + dx, CY - 28, 0.3), (0, 0, radians(180)), None, C,
        "Refinery.Truck%d" % i)
if PHASE >= 3:
    tkt = P.truck("Refinery.Tanker", "steel_lt", "tank", C)
    tkt.location = (CX + 32, CY - 16, 0.3)
    tkt.rotation_euler = (0, 0, radians(90))

if PHASE >= 2:
    P.fence_run([(CX - 35, CY + 33, 0.3), (CX + 35, CY + 33, 0.3),
                 (CX + 35, CY - 33, 0.3), (CX - 35, CY - 33, 0.3)],
                "Refinery.Fence", C,
                gaps=[L.gate_point(L.REFINERY, L.PAD) + (11.0,)])
for i, (lx, ly) in enumerate((((CX - 32, CY - 22), (CX + 32, CY - 22),
                               (CX + 0, CY - 32), (CX - 34, CY + 26))
                              [:PK(1, 3, 4)])):
    P.streetlight("Refinery.Lamp%d" % i, 10.0, 3.2, C).location = (lx, ly, 0.3)


# Built in local terms against a flat z=0, then moved onto the graded
# pad in one go - see lib.lift_collection.
lift_collection("Refinery", grade.pad_z(CX, CY))

print("refinery ok", stats(), "phase", PHASE)
