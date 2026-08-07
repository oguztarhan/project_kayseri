"""Step 15: the four town-centre yards inside the ring road.

    Power   POWER PLANT   NE      Haul    ORE TRUCKS    NW
    Fleet   CARGO TRUCKS  SW      Civic   (the town)    SE

Power, Haul and Fleet all belong to stations that existed in the economy with no
building anywhere on the map, so upgrading them showed the player nothing. Civic
has no station of its own and follows the furthest-advanced one, so the town
itself visibly grows as the island does.

Each gets its own collection rather than one shared "Town" so
IslandPhaseController can advance them separately, which is the whole point of
putting them here.

Phase 1  a shed and a yard          2  proper plant / garage      3  full works
Built in local terms against a flat z=0 and moved onto the graded pad at the
end, exactly like the four outer districts.
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

# Yard half-extents. The pad is L.TOWN_PAD square; the slab is drawn a touch
# inside it so its edge never hangs over the feather.
HW, HH = L.TOWN_PAD, L.TOWN_PAD - 1.0

for name in ("Power", "Haul", "Fleet", "Civic"):
    purge_collection(name)
CP, CH, CF, CV = (coll(n) for n in ("Power", "Haul", "Fleet", "Civic"))

YARD = PK("dirt", "concrete", "concrete")
BODY = PK("wood_lt", "clad", "steel_lt")
ROOF = PK("roof_grey", "roof_blue", "roof_teal")


def yard(cx, cy, name, C, m):
    b = B().use(m)
    b.box((HW * 2, HH * 2, 0.3), (cx, cy, 0.14))
    return b.make(name, collection=C)


def perimeter(cx, cy, name, C):
    # On the slab edge, not a metre inside it: the workshop and the garage are
    # sized to the yard, so their walls and roof overhang met the fence line.
    P.fence_run([(cx - HW + 0.4, cy - HH + 0.4, 0.3), (cx + HW - 0.4, cy - HH + 0.4, 0.3),
                 (cx + HW - 0.4, cy + HH - 0.4, 0.3), (cx - HW + 0.4, cy + HH - 0.4, 0.3),
                 (cx - HW + 0.4, cy - HH + 0.4, 0.3)], name, C, 2.4)


# ------------------------------------------------------------- power plant
PX, PY = L.TOWN_POWER
yard(PX, PY, "Power.Yard", CP, YARD)

# turbine hall
P.warehouse("Power.Hall", PK(11.0, 13.0, 14.0), PK(8.0, 9.0, 10.0),
            PK(7.0, 8.5, 10.0), CP, BODY, ROOF).location = (PX - 4, PY + 3.5, 0.3)

# Chimneys - the silhouette that reads as "power" from the play camera. Two,
# not three: at a 3.4 pitch and 2.1 radius they grew through each other, and
# there is no pitch that fits three of them beside the turbine hall in a yard
# this size.
for i in range(PK(1, 2, 2)):
    P.stack("Power.Stack%d" % i, PK(1.6, 1.9, 2.1), PK(20.0, 27.0, 34.0),
            CP).location = (PX + 4 + i * 5.0, PY + 8, 0.3)

# Fuel / feedwater tanks, on a pitch wider than they are - 4.4 against a 6.8
# diameter buried each one in the next.
for i in range(PK(1, 2, 3)):
    P.tank("Power.Tank%d" % i, PK(2.6, 3.0, 3.4), PK(4.0, 5.0, 6.0), CP,
           "steel_lt", "red").location = (PX - 9 + i * 7.6, PY - 9, 0.3)

# cooling towers once it is a real station
if PHASE >= 2:
    ct = B().use("concrete")
    for i in range(PK(0, 1, 1)):
        cx, cy = PX + 8, PY - 1
        ct.conez(PK(0, 4.2, 4.6), PK(0, 2.8, 3.1), PK(0, 12.0, 15.0),
                 (cx, cy, 0.3), seg=18)
    ct.make("Power.CoolingTowers", collection=CP, smooth=True)

# switchyard: the transformer bank and its gantry of busbars
sw = B().use("steel_dk")
for i in range(PK(2, 3, 4)):
    tx, ty = PX - 9, PY + 7 - i * 3.6
    sw.boxz((2.8, 3.0, 3.2), (tx, ty, 0.3))
    sw.cylz(0.45, 4.0, (tx + 0.9, ty, 3.5), seg=8)
sw.use("metal_gal")
for i in range(PK(2, 3, 4)):
    ty = PY + 7 - i * 3.6
    sw.tube(0.22, [(PX - 11.0, ty, 0.3), (PX - 11.0, ty, 8.4)], 6)
    sw.tube(0.16, [(PX - 11.0, ty, 8.0), (PX - 6.0, ty, 8.0)], 6)
sw.make("Power.Switchyard", collection=CP)

if PHASE >= 2:
    P.pipe_rack([(PX - 6, PY - 2, 0.3), (PX + 9, PY - 2, 0.3)],
                "Power.Pipes", CP, n=PK(0, 3, 4))

perimeter(PX, PY, "Power.Fence", CP)
for i, (lx, ly) in enumerate(((PX - 7, PY - 9), (PX + 7, PY + 9))):
    P.streetlight("Power.Lamp%d" % i, 9.0, 3.0, CP).location = (lx, ly, 0.3)

# --------------------------------------------------------------- haul yard
# ORE TRUCKS drove the road surface and nothing else. This is where the fleet
# that runs mine -> storage -> refinery is kept and loaded.
HX, HY = L.TOWN_HAUL
yard(HX, HY, "Haul.Yard", CH, PK("dirt", "gravel", "asphalt_lt"))

# The yard is split across its middle: buildings north of HY, parked vehicles
# south of it. A hauler is 13 long and the yard is 24 deep, so anything else is
# a collision waiting to happen - the old layout parked five of them on a 5.6
# pitch in two rows and each pair overlapped by 7.4.
P.warehouse("Haul.Workshop", PK(9.0, 10.0, 11.0), PK(8.0, 9.5, 11.0),
            PK(6.0, 7.0, 8.0), CH, BODY, ROOF).location = (HX - 4, HY + 4.5, 0.3)

# loading hopper over a drive-through bay, so the yard reads as ore handling
P.hopper("Haul.Hopper", PK(3.6, 4.4, 5.0), PK(8.0, 10.0, 11.0),
         CH).location = (HX + 8, HY + 5, 0.3)
if PHASE >= 2:
    P.conveyor((HX + 1, HY + 5, 0.6), (HX + 8, HY + 5, 8.0), "Haul.Conv", CH, 2.2)

# weighbridge on the way in
wb = B().use("concrete_dk")
wb.box((7.0, 4.0, 0.4), (HX + 8, HY - 8, 0.45))
wb.use("steel_lt")
wb.boxz((1.2, 1.2, 2.6), (HX + 11.5, HY - 8, 0.5))
wb.make("Haul.Weighbridge", collection=CH)

# The ore fleet, parked. Scenery: CoalOperation drives its own trucks. Laid
# along X in ranks 5 apart, which is a truck's width and a door - the only way
# two 13-long bodies fit in a yard this size without touching.
ore_src = P.truck("Haul.TruckSrc", "yellow_lt", "coal", CH)
ore_src.hide_render = ore_src.hide_viewport = True
for i in range(PK(1, 2, 2)):
    dup(ore_src, (HX - 3, HY - 3.0 - i * 5.0, 0.3),
        (0, 0, 0), None, CH, "Haul.Parked%d" % i)

if PHASE >= 3:
    P.loader("Haul.Loader", CH).location = (HX + 7, HY - 9, 0.3)

perimeter(HX, HY, "Haul.Fence", CH)
for i, (lx, ly) in enumerate(((HX + 7, HY - 9), (HX - 7, HY + 9))):
    P.streetlight("Haul.Lamp%d" % i, 9.0, 3.0, CH).location = (lx, ly, 0.3)

# ------------------------------------------------------------- fleet depot
FX, FY = L.TOWN_FLEET
yard(FX, FY, "Fleet.Yard", CF, PK("dirt", "asphalt_lt", "asphalt_lt"))

# Same split as the haul yard: garage across the north half, parked vehicles
# across the south. The garage was 18 deep in a 24-deep yard sitting 6 north of
# centre, which put its north wall out on the arterial on the copper island.
P.warehouse("Fleet.Garage", PK(9.0, 10.0, 11.0), PK(8.0, 9.5, 11.0),
            PK(6.0, 7.5, 8.5), CF, BODY, ROOF).location = (FX - 4, FY + 4.5, 0.3)

# roller doors facing the yard, so the shed reads as a garage not a warehouse
gd = B().use("steel_dk")
for i in range(PK(2, 3, 4)):
    gx = FX - 4 - PK(3.0, 4.0, 4.5) + i * PK(6.0, 4.0, 3.0)
    gd.boxz((PK(3.0, 2.8, 2.6), 0.4, PK(4.0, 4.6, 5.2)), (gx, FY + 0.4, 0.35))
gd.make("Fleet.Doors", collection=CF)

# fuel island
fu = B().use("concrete_dk")
fu.box((7.0, 4.0, 0.35), (FX + 8, FY + 5, 0.42))
fu.use("orange")
for s in (-1, 1):
    fu.boxz((0.8, 1.2, 3.0), (FX + 8 + s * 2.2, FY + 5, 0.6))
fu.use("steel_lt")
fu.box((8.0, 5.0, 0.5), (FX + 8, FY + 5, 5.4))
fu.use("steel_dk")
for s in (-1, 1):
    fu.tube(0.3, [(FX + 8 + s * 3.2, FY + 5, 0.6), (FX + 8 + s * 3.2, FY + 5, 5.2)], 6)
fu.make("Fleet.FuelIsland", collection=CF)

P.tank("Fleet.FuelTank", 2.2, 3.6, CF, "steel_lt", "red").location = (FX + 9, FY - 2, 0.3)

# Two ranks 4.5 apart - a body's width and a door. See Haul.Parked.
park = P.truck("Fleet.TruckSrc", "white", "cargo", CF)
park.hide_render = park.hide_viewport = True
for i in range(PK(1, 2, 2)):
    dup(park, (FX - 3, FY - 2.5 - i * 4.5, 0.3),
        (0, 0, 0), None, CF, "Fleet.Parked%d" % i)

if PHASE >= 2:
    P.crate_stack("Fleet.Crates", CF).location = (FX + 10, FY + 10, 0.3)
if PHASE >= 3:
    # Across the yard mouth, which is what a wash arch spans.
    P.gantry("Fleet.WashGantry", 12.0, 6.5, CF).location = (FX - 3, FY - 11.5, 0.3)

# painted parking bays, between the two ranks rather than under them
if PHASE >= 2:
    pb = B().use("linepaint")
    for i in range(PK(0, 4, 6)):
        pb.box((0.35, 4.0, 0.05), (FX - 10 + i * 3.4, FY - 4.75, 0.36))
    pb.make("Fleet.Bays", collection=CF)

perimeter(FX, FY, "Fleet.Fence", CF)
for i, (lx, ly) in enumerate(((FX - 7, FY + 9), (FX + 7, FY - 9))):
    P.streetlight("Fleet.Lamp%d" % i, 9.0, 3.0, CF).location = (lx, ly, 0.3)

# ------------------------------------------------------------- civic block
# No station drives this one, so it follows the furthest-advanced station: the
# town grows as the whole island does. A paved square rather than a fenced yard,
# because it is the one place on the map that is not industrial.
VX, VY = L.TOWN_CIVIC
yard(VX, VY, "Civic.Plaza", CV, PK("gravel", "concrete", "concrete"))

if PHASE >= 2:
    pv = B().use("concrete_dk")
    for i in range(7):
        pv.box((HW * 2 - 2, 0.3, 0.05), (VX, VY - HH + 3 + i * 3.0, 0.32))
    pv.make("Civic.Paving", collection=CV)

# town hall - the one building that gets taller with the island
P.office("Civic.Hall", PK(10.0, 12.0, 13.0), PK(9.0, 10.0, 11.0), PK(2, 3, 5),
         CV, PK("wood_lt", "offwhite", "offwhite")).location = (VX - 5, VY + 6, 0.3)

# clock tower
ct = B().use(PK("wood_lt", "offwhite", "offwhite"))
ct.boxz((3.2, 3.2, PK(10.0, 15.0, 20.0)), (VX + 5, VY + 8, 0.3))
ct.use("winlight")
for s in (-1, 1):
    ct.box((2.0, 0.2, 2.0), (VX + 5 + s * 1.7, VY + 8, PK(8.6, 13.6, 18.6)))
    ct.box((0.2, 2.0, 2.0), (VX + 5, VY + 8 + s * 1.7, PK(8.6, 13.6, 18.6)))
ct.use(PK("roof_grey", "roof_red", "roof_red"))
ct.conez(2.6, 0.0, 3.4, (VX + 5, VY + 8, PK(10.3, 15.3, 20.3)), seg=8)
ct.make("Civic.Tower", collection=CV)

# a row of shops along the south side
for i in range(PK(1, 2, 2)):
    P.shop("Civic.Shop%d" % i, 7.0, 6.0, PK(5.0, 6.0, 7.0), CV,
           PK("wood_lt", "offwhite", "clad"),
           ("roof_red", "roof_blue", "roof_teal")[i % 3],
           awning="orange" if PHASE >= 2 else None).location = (
        VX - 8 + i * 8.0, VY - 5, 0.3)

# water tower: the island's only civic infrastructure, and a good silhouette
if PHASE >= 2:
    wt = B().use("metal_gal")
    for a in range(4):
        ax, ay = (2.2 if a % 2 else -2.2), (2.2 if a // 2 else -2.2)
        wt.tube(0.28, [(VX + 9 + ax, VY - 1 + ay, 0.3), (VX + 9, VY - 1, 9.0)], 6)
    wt.use("steel_lt")
    wt.cylz(3.6, 4.4, (VX + 9, VY - 1, 9.0), seg=14)
    wt.use("roof_grey")
    wt.conez(3.8, 0.0, 1.6, (VX + 9, VY - 1, 13.4), seg=14)
    wt.make("Civic.WaterTower", collection=CV, smooth=False)

# planting and benches - the thing that says "people live here"
tree = P.pine("Civic.TreeSrc", 7.0, 2.0, CV, tiers=3, m="pine_lt")
tree.hide_render = tree.hide_viewport = True
# In the corners of the plaza, not on top of the town hall - the old row ran
# straight through it.
for i, (tx, ty) in enumerate(((10, 10), (10, -10), (-10, -10))):
    dup(tree, (VX + tx, VY + ty, 0.3), (0, 0, i * 1.1), (1, 1, 1), CV,
        "Civic.Tree%d" % i)
bn = B().use("wood_lt")
for i in range(PK(2, 4, 6)):
    bx, by = VX - 9 + (i % 3) * 9.0, VY + (2.0 if i < 3 else -4.0)
    bn.box((2.6, 0.7, 0.25), (bx, by, 0.95))
    bn.box((2.6, 0.25, 0.9), (bx, by - 0.35, 1.4))
bn.make("Civic.Benches", collection=CV)

for i, (lx, ly) in enumerate(((VX - 7, VY - 9), (VX + 7, VY + 9),
                              (VX - 7, VY + 9))):
    P.streetlight("Civic.Lamp%d" % i, 8.0, 2.6, CV).location = (lx, ly, 0.3)

for name, (cx, cy) in (("Power", L.TOWN_POWER), ("Haul", L.TOWN_HAUL),
                       ("Fleet", L.TOWN_FLEET), ("Civic", L.TOWN_CIVIC)):
    lift_collection(name, grade.pad_z(cx, cy))

print("town ok", stats(), "phase", PHASE, "pad z",
      ["%.1f" % grade.pad_z(cx, cy) for cx, cy in L.TOWNS])
