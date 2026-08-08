"""Step 8: market / distribution centre.  Phase 1 dirt trading post -> 3 full
logistics hub feeding the port.

Same discipline as the depot and the refinery: the arterial comes through the
gate into an empty corridor, the corridor opens onto a yard the lorries turn and
sell in, and the sheds, shops and dock stand round the outside of it.  The big
warehouse used to sit squarely in that corridor and the loading dock across the
middle of the yard.  Coordinates are authored in the coal map's orientation and
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

purge_collection("Market")
C = coll("Market")
CX, CY = L.MARKET
F = yard.Frame("market", L.MARKET)

# --------------------------------------------------------------------- pads
b = B().use(PK("dirt", "asphalt_lt", "asphalt_lt"))
b.box(F.dim(72, 74, 0.3), F.at(0, 0, 0.14))
# The selling yard: the open middle.
b.use("concrete")
b.box(F.dim(PK(26, 30, 32), PK(24, 26, 28), 0.34), F.at(0, -2, 0.18))
# The way in, from the gate to the yard.
b.use("asphalt")
b.box(F.dim(16, 26, 0.36), F.at(0, 26, 0.19))
b.make("Market.Pad", collection=C)

if PHASE >= 2:
    # Parking bays down both flanks of the yard, not across its mouth.
    pk = B().use("linepaint")
    for i in range(9):
        pk.box(F.dim(7.0, 0.35, 0.05), F.at(-24, 12 - i * 4.2, 0.34))
    for i in range(9):
        pk.box(F.dim(7.0, 0.35, 0.05), F.at(24, 12 - i * 4.2, 0.34))
    for i in range(9):
        pk.box(F.dim(0.35, 6.6, 0.05), F.at(-28 + i * 4.2, -26, 0.34))
    pk.make("Market.Bays", collection=C)

# ---------------------------------------------------------------- warehouses
# Across the back. It stood at dy +20, which is the middle of the way in.
wh = P.warehouse("Market.WH1", *F.dim(PK(22, 28, 30), PK(14, 19, 22)),
                 PK(8, 11, 13), C, PK("wood_lt", "cream", "cream"),
                 PK("roof_red", "roof_teal", "roof_teal"),
                 curved=(PHASE >= 2), doors=PK(2, 3, 4))
wh.location = F.at(-6, -28)
if PHASE >= 2:
    wh2 = P.warehouse("Market.WH2", *F.dim(PK(0, 20, 24), PK(0, 15, 17)),
                      PK(0, 8, 10), C, "clad", "roof_blue", doors=3)
    wh2.location = F.at(26, -6)

# loading dock - along the west flank of the yard, facing in
if PHASE >= 2:
    dk = B().use("concrete_dk")
    dk.boxz(F.dim(5.0, 34, 1.3), F.at(25, -2))
    dk.use("steel_dk")
    for i in range(4):
        dk.boxz(F.dim(1.6, 5.0, 0.5), F.at(22.6, -14 + i * 8.6, 0.9))
    dk.use("roof_grey")
    dk.box(F.dim(4.0, 36, 0.4), F.at(24, -2, 6.4))
    for i in range(5):
        dk.boxz(F.dim(0.4, 0.4, 6.2), F.at(22.6, -16 + i * 8.0, 1.6))
    dk.make("Market.Dock", collection=C)

# ------------------------------------------------------------------- shops
# Down the east flank, outside the yard.
SHOPS = PK([(-27, 12, 11, 9, 5, "wood_lt", "roof_red", "red"),
            (-27, 0, 10, 9, 5, "cream", "roof_grey", "teal")],
           [(-28, 14, 13, 10, 6, "cream", "roof_red", "red"),
            (-28, 1, 12, 10, 6, "white", "roof_teal", "teal"),
            (-28, -12, 12, 10, 7, "cream", "roof_orange", "orange")],
           [(-29, 15, 13, 10, 6, "cream", "roof_red", "red"),
            (-29, 2, 12, 10, 6, "white", "roof_teal", "teal"),
            (-29, -11, 12, 10, 7, "cream", "roof_orange", "orange"),
            (-29, -24, 13, 10, 6, "white", "roof_blue", "blue_lt")])
for i, (dx, dy, w, d, h, body, roof, aw) in enumerate(SHOPS):
    P.shop("Market.Shop%d" % i, *F.dim(w, d), h, C, body, roof,
           aw).location = F.at(dx, dy)

# market stalls - across the back corner, clear of everything
st = B().use("steel_lt")
NST = PK(4, 6, 8)
for i in range(NST):
    sx, sy = -28 + i * 6.2, -34
    for a in ((-1, -1), (-1, 1), (1, -1), (1, 1)):
        st.boxz(F.dim(0.22, 0.22, 3.0), F.at(sx + a[0] * 2.2, sy + a[1] * 1.7))
for i in range(0, NST, 2):
    st.use("red")
    st.box(F.dim(5.4, 4.4, 0.3), F.at(-28 + i * 6.2, -34, 3.5))
for i in range(1, NST, 2):
    st.use("white")
    st.box(F.dim(5.4, 4.4, 0.3), F.at(-28 + i * 6.2, -34, 3.5))
st.use("wood_lt")
for i in range(NST):
    st.boxz(F.dim(4.4, 2.0, 1.0), F.at(-28 + i * 6.2, -30))
st.make("Market.Stalls", collection=C)

# ------------------------------------------------------------- fuel station
if PHASE >= 2:
    fs = B().use("steel_lt")
    for a in ((-1, -1), (-1, 1), (1, -1), (1, 1)):
        fs.boxz(F.dim(0.7, 0.7, 6.0), F.at(-27 + a[0] * 6.0, 26 + a[1] * 4.0))
    fs.use("white")
    fs.box(F.dim(16.0, 11.0, 1.1), F.at(-27, 26, 6.9))
    fs.use("red")
    fs.box(F.dim(16.4, 11.4, 0.5), F.at(-27, 26, 7.6))
    fs.use("steel_dk")
    for s in (-1, 1):
        fs.boxz(F.dim(1.2, 2.4, 2.0), F.at(-27 + s * 3.5, 26, 0.6))
    fs.use("winlight")
    fs.box(F.dim(15.0, 10.0, 0.25), F.at(-27, 26, 6.3))
    fs.make("Market.Fuel", collection=C)
    P.shop("Market.FuelShop", *F.dim(10, 8), 5, C, "white",
           "red").location = F.at(-30, 12)

if PHASE >= 2:
    P.office("Market.Office", *F.dim(14, 12), PK(1, 2, 3), C).location = \
        F.at(28, 26)

# ---------------------------------------------------------------- vehicles
# Parked on the bays down the flanks, nose in - never across the yard mouth.
tk = P.truck("Market.Truck", "white", "cargo", C)
tk.location = F.at(-15, 8)
tk.rotation_euler = (0, 0, F.yaw(90))
for i, dy in enumerate(PK((), (-2,), (-2, -14))):
    dup(tk, F.at(-15, dy), (0, 0, F.yaw(90)), None, C, "Market.T%d" % i)

VANS = PK((("white", 10),), (("blue_lt", 10), ("white", 0), ("red", -10)),
          (("blue_lt", 12), ("white", 2), ("red", -8), ("offwhite", -18)))
for i, (col, dy) in enumerate(VANS):
    v = P.van("Market.Van%d" % i, col, C)
    v.location = F.at(15, dy - 6)
    if PHASE >= 2:
        v2 = P.van("Market.VanB%d" % i, col, C)
        v2.location = F.at(-16, dy - 8)

fk = P.forklift("Market.Fork", C)
fk.location = F.at(-15, -22)
fk.rotation_euler = (0, 0, F.yaw(-60))
for i, (dx, dy, a) in enumerate(PK((), ((14, -22, 120),),
                                   ((14, -22, 120), (0, -24, 20)))):
    dup(fk, F.at(dx, dy), (0, 0, F.yaw(a)), None, C, "Market.Fork%d" % i)

cr = B().use("wood_lt")
for i in range(PK(6, 12, 16)):
    s = RNG.uniform(1.4, 2.1)
    cr.boxz(F.dim(s, s, s * 0.8), F.at(-30 + RNG.uniform(0, 5),
                                       -14 + RNG.uniform(-6, 6)))
cr.use("blue")
for i in range(PK(1, 3, 4)):
    cr.boxz(F.dim(7.0, 3.0, 3.0), F.at(31, -20 + i * 3.4))
cr.make("Market.Goods", collection=C)

for i, (dx, dy) in enumerate((((-32, -16), (32, -16), (-32, 12), (32, 12),
                               (0, -32))[:PK(1, 3, 5)])):
    P.streetlight("Market.Lamp%d" % i, 9.0, 3.0, C).location = F.at(dx, dy)

if PHASE >= 2:
    # A gate for the arterial and one for every other road that reaches here -
    # the quay road on all three islands, and on the iron island the mine's own
    # haul road as well, which comes in through the west fence.
    P.fence_run([F.at(-35, 32), F.at(-35, -33), F.at(35, -33), F.at(35, 32)],
                "Market.Fence", C, 2.0,
                gaps=L.fence_gaps(L.MARKET, L.PAD, L.APPROACHES))


# Built in local terms against a flat z=0, then moved onto the graded
# pad in one go - see lib.lift_collection.
lift_collection("Market", grade.pad_z(CX, CY))

print("market ok", stats(), "phase", PHASE)
