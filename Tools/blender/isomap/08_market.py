"""Step 8: SW district - market / distribution centre.  Phase 1 dirt trading
post -> 3 full logistics hub feeding the port."""
import importlib
import layout
import parts
importlib.reload(layout)
importlib.reload(parts)
import grade
importlib.reload(grade)
L = layout
P = parts

purge_collection("Market")
C = coll("Market")
CX, CY = L.MARKET

# --------------------------------------------------------------------- pads
b = B().use(PK("dirt", "asphalt_lt", "asphalt_lt"))
# 74 along the arterial - see the note on Depot.Yard: the road now stops at the
# pad edge and this slab is the surface from there in.
b.box((72, 74, 0.3), (CX, CY, 0.14))
b.use("concrete")
b.box(PK((28, 16, 0.34), (42, 22, 0.34), (48, 26, 0.34)), (CX - 4, CY + 20, 0.18))
b.make("Market.Pad", collection=C)

if PHASE >= 2:
    pk = B().use("linepaint")
    for i in range(14):
        pk.box((0.35, 7.0, 0.05), (CX - 28 + i * 4.2, CY - 8, 0.34))
    for i in range(14):
        pk.box((0.35, 7.0, 0.05), (CX - 28 + i * 4.2, CY - 22, 0.34))
    for i in range(9):
        pk.box((6.6, 0.35, 0.05), (CX + 26, CY - 28 + i * 4.2, 0.34))
    pk.make("Market.Bays", collection=C)

# ---------------------------------------------------------------- warehouses
wh = P.warehouse("Market.WH1", PK(22, 28, 30), PK(14, 19, 22), PK(8, 11, 13), C,
                 PK("wood_lt", "cream", "cream"),
                 PK("roof_red", "roof_teal", "roof_teal"),
                 curved=(PHASE >= 2), doors=PK(2, 3, 4))
# Its south wall now lands at CY+3, which is where the loading dock stands in
# FRONT of it - at 38 long it reached CY+41, four over the yard slab, and
# swallowed both the dock and the crate yard.
wh.location = (CX - 4, CY + 20, 0.3)
if PHASE >= 2:
    wh2 = P.warehouse("Market.WH2", PK(0, 20, 24), PK(0, 15, 17), PK(0, 8, 10),
                      C, "clad", "roof_blue", doors=3)
    wh2.location = (CX + 23, CY + 24, 0.3)

# loading dock
if PHASE >= 2:
    dk = B().use("concrete_dk")
    dk.boxz((38, 5.0, 1.3), (CX - 8, CY + 0, 0.3))
    dk.use("steel_dk")
    for i in range(4):
        dk.boxz((5.0, 1.6, 0.5), (CX - 23 + i * 9.6, CY - 2.4, 0.9))
    dk.use("roof_grey")
    dk.box((40, 4.0, 0.4), (CX - 8, CY - 1, 6.4))
    for i in range(5):
        dk.boxz((0.4, 0.4, 6.2), (CX - 26 + i * 9.0, CY - 2.4, 1.6))
    dk.make("Market.Dock", collection=C)

# ------------------------------------------------------------------- shops
SHOPS = PK([(-22, -2, 11, 9, 5, "wood_lt", "roof_red", "red"),
            (-8, -2, 10, 9, 5, "cream", "roof_grey", "teal")],
           [(-26, -2, 13, 10, 6, "cream", "roof_red", "red"),
            (-11, -2, 12, 10, 6, "white", "roof_teal", "teal"),
            (3, -2, 12, 10, 7, "cream", "roof_orange", "orange")],
           [(-28, -2, 13, 10, 6, "cream", "roof_red", "red"),
            (-13, -2, 12, 10, 6, "white", "roof_teal", "teal"),
            (1, -2, 12, 10, 7, "cream", "roof_orange", "orange"),
            (15, -2, 13, 10, 6, "white", "roof_blue", "blue_lt")])
for i, (dx, dy, w, d, h, body, roof, aw) in enumerate(SHOPS):
    P.shop("Market.Shop%d" % i, w, d, h, C, body, roof, aw).location = (
        CX + dx, CY + dy, 0.3)

# market stalls
st = B().use("steel_lt")
NST = PK(4, 6, 8)
for i in range(NST):
    sx, sy = CX - 28 + i * 6.2, CY - 34
    for a in ((-1, -1), (-1, 1), (1, -1), (1, 1)):
        st.boxz((0.22, 0.22, 3.0), (sx + a[0] * 2.2, sy + a[1] * 1.7, 0.3))
for i in range(0, NST, 2):
    st.use("red")
    st.box((5.4, 4.4, 0.3), (CX - 28 + i * 6.2, CY - 34, 3.5))
for i in range(1, NST, 2):
    st.use("white")
    st.box((5.4, 4.4, 0.3), (CX - 28 + i * 6.2, CY - 34, 3.5))
st.use("wood_lt")
for i in range(NST):
    st.boxz((4.4, 2.0, 1.0), (CX - 28 + i * 6.2, CY - 30, 0.3))
st.make("Market.Stalls", collection=C)

# ------------------------------------------------------------- fuel station
if PHASE >= 2:
    fs = B().use("steel_lt")
    for a in ((-1, -1), (-1, 1), (1, -1), (1, 1)):
        fs.boxz((0.7, 0.7, 6.0), (CX + 26 + a[0] * 6.0, CY - 5 + a[1] * 4.0, 0.3))
    fs.use("white")
    fs.box((16.0, 11.0, 1.1), (CX + 26, CY - 5, 6.9))
    fs.use("red")
    fs.box((16.4, 11.4, 0.5), (CX + 26, CY - 5, 7.6))
    fs.use("steel_dk")
    for s in (-1, 1):
        fs.boxz((1.2, 2.4, 2.0), (CX + 26 + s * 3.5, CY - 5, 0.6))
    fs.use("winlight")
    fs.box((15.0, 10.0, 0.25), (CX + 26, CY - 5, 6.3))
    fs.make("Market.Fuel", collection=C)
    P.shop("Market.FuelShop", 10, 8, 5, C, "white", "red").location = (
        CX + 26, CY + 8, 0.3)

if PHASE >= 2:
    P.office("Market.Office", 14, 12, PK(1, 2, 3), C).location = (
        CX - 27, CY + 27, 0.3)

# ---------------------------------------------------------------- vehicles
tk = P.truck("Market.Truck", "white", "cargo", C)
tk.location = (CX - 22, CY + 2, 0.3)
tk.rotation_euler = (0, 0, radians(90))
for i, dx in enumerate(PK((), (-11,), (-11, 1))):
    dup(tk, (CX + dx, CY + 2, 0.3), (0, 0, radians(90)), None, C, "Market.T%d" % i)

VANS = PK((("white", -18),), (("blue_lt", -18), ("white", -8), ("red", 4)),
          (("blue_lt", -20), ("white", -10), ("red", 2), ("offwhite", 12)))
for i, (col, dx) in enumerate(VANS):
    v = P.van("Market.Van%d" % i, col, C)
    v.location = (CX + dx, CY - 11, 0.3)
    if PHASE >= 2:
        v2 = P.van("Market.VanB%d" % i, col, C)
        v2.location = (CX + dx + 3, CY - 25, 0.3)

fk = P.forklift("Market.Fork", C)
fk.location = (CX - 20, CY + 13, 0.3)
fk.rotation_euler = (0, 0, radians(-60))
for i, (dx, dy, a) in enumerate(PK((), ((-2, 13, 120),),
                                   ((-2, 13, 120), (18, 15, 20)))):
    dup(fk, (CX + dx, CY + dy, 0.3), (0, 0, radians(a)), None, C,
        "Market.Fork%d" % i)

cr = B().use("wood_lt")
for i in range(PK(6, 12, 16)):
    s = RNG.uniform(1.4, 2.1)
    cr.boxz((s, s, s * 0.8), (CX + 6 + RNG.uniform(0, 13),
                              CY + 5 + RNG.uniform(-2, 3), 0.3))
cr.use("blue")
for i in range(PK(1, 3, 4)):
    cr.boxz((7.0, 3.0, 3.0), (CX + 16 + i * 1.0, CY - 16 + i * 3.0, 0.3))
cr.make("Market.Goods", collection=C)

for i, (lx, ly) in enumerate((((CX - 32, CY - 16), (CX + 8, CY - 16),
                               (CX - 32, CY + 4), (CX + 32, CY + 16),
                               (CX - 4, CY - 30))[:PK(1, 3, 5)])):
    P.streetlight("Market.Lamp%d" % i, 9.0, 3.0, C).location = (lx, ly, 0.3)

if PHASE >= 2:
    # A gate for the arterial and one for every other road that reaches here -
    # the quay road on all three islands, and on the iron island the mine's own
    # haul road as well, which comes in through the west fence.
    P.fence_run([(CX - 35, CY + 32, 0.3), (CX - 35, CY - 33, 0.3),
                 (CX + 35, CY - 33, 0.3)], "Market.Fence", C, 2.0,
                gaps=L.fence_gaps(L.MARKET, L.PAD, L.APPROACHES))


# Built in local terms against a flat z=0, then moved onto the graded
# pad in one go - see lib.lift_collection.
lift_collection("Market", grade.pad_z(CX, CY))

print("market ok", stats(), "phase", PHASE)
