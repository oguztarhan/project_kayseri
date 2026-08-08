"""Step 9: ship port in the harbour bay beside the market.

Phase 1  timber jetty, one small boat, crates on the sand
Phase 2  concrete quay, one crane, container yard, coaster alongside
Phase 3  full terminal - two quays, three cranes, big ships, one under way
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

purge_collection("Port")
C = coll("Port")

# quay frame: QD runs along the shore, QN points out to sea
QX, QY = L.PORT
QYAW = L.PORT_YAW
QD = (cos(QYAW), sin(QYAW))
QN = (-QD[1] * 1.0, QD[0] * 1.0)
# Make QN point seaward. This used to test QN[0] + QN[1] > 0, i.e. it assumed
# the ocean is always the smaller-x+y half-plane. The copper island's sea is
# the OTHER half-plane, so the entire port came out mirrored about its own
# quay: wall and cranes facing inland, container yard and sheds standing in
# the water. Asking the island which way the sea lies is right for both maps.
if QN[0] * L.SEA_AXIS[0] + QN[1] * L.SEA_AXIS[1] < 0:
    QN = (-QN[0], -QN[1])
# The whole collection is lifted onto the graded apron at the end of this file,
# like the other districts. Marine geometry must NOT come with it - the ships
# and pier piles belong to the water, not the quay - so SEA is pre-compensated
# here and lands back on the real waterline once the lift is applied.
Z0 = grade.pad_z(QX, QY)
SEA = L.SEA_Z - Z0
APRON_TOP = 0.9                   # top face of the apron slab, in local terms


def q(a, n, z=0.0):
    """Quay-local (along, seaward) -> world."""
    return (QX + QD[0] * a + QN[0] * n, QY + QD[1] * a + QN[1] * n, z)


# ------------------------------------------------------------------- apron
QL = PK(30.0, 50.0, 64.0)
QW = PK(12.0, 18.0, 22.0)
b = B().use(PK("dirt", "concrete", "concrete"))
p = q(0, -QW * 0.5 + 2)
b.box((QL, QW, 1.2), (p[0], p[1], 0.3), (0, 0, QYAW))
b.use(PK("dirt", "concrete_dk", "concrete_dk"))
# back apron kept short: it must not run into the market pad (x >= -36)
p2 = q(0, -QW * 0.5 - 8)
b.box((QL * 0.72, 12.0, 0.6), (p2[0], p2[1], 0.28), (0, 0, QYAW))
b.make("Port.Apron", collection=C)

# ------------------------------------------------------------- quay wall
qw = B().use(PK("wood", "concrete", "concrete"))
for i in range(int(QL / 4.0)):
    a = -QL * 0.5 + 2.0 + i * 4.0
    p = q(a, 2.0)
    # Spans the full face from its footing up to the apron. A fixed 4.6 stopped
    # 1.2 below the waterline, which was invisible when the apron sat at 0 but
    # leaves it floating once the quay is graded up out of the water.
    qw.boxz((4.2, 5.0, APRON_TOP - (SEA - 3.4)), (p[0], p[1], SEA - 3.4),
            (0, 0, QYAW))
qw.use("steel_dk")
for i in range(int(QL / 7.0)):
    a = -QL * 0.5 + 3.5 + i * 7.0
    p = q(a, 4.2)
    qw.cyl(0.9, 2.4, (p[0], p[1], SEA + 0.4), (radians(90), 0, QYAW), 10)
qw.make("Port.QuayWall", collection=C)

bol = P.bollard("Port.BollardSrc", C)
bol.hide_render = bol.hide_viewport = True
for i in range(int(QL / 9.0)):
    a = -QL * 0.5 + 5.0 + i * 9.0
    p = q(a, 1.0)
    dup(bol, (p[0], p[1], 1.5), (0, 0, QYAW), None, C, "Port.Bollard")

# --------------------------------------------------------------- piers
NPIER = PK(1, 2, 3)
PLEN = PK(26.0, 40.0, 52.0)
for k in range(NPIER):
    a = (-QL * 0.35 + k * (QL * 0.7 / max(1, NPIER - 1))) if NPIER > 1 else 0.0
    pb = B().use(PK("wood", "concrete", "concrete"))
    mid = q(a, PLEN * 0.5 + 3.0)
    pb.box((PK(7.0, 10.0, 12.0), PLEN, 1.2), (mid[0], mid[1], 1.4), (0, 0, QYAW))
    pb.use(PK("wood", "concrete_dk", "concrete_dk"))
    for i in range(int(PLEN / 5.0)):
        n = 5.0 + i * 5.0
        for s in (1, -1):
            pp = q(a + s * PK(2.6, 3.8, 4.6), n)
            pb.boxz((1.1, 1.1, 6.0), (pp[0], pp[1], SEA - 4.5), (0, 0, QYAW))
    pb.make("Port.Pier%d" % k, collection=C)

# --------------------------------------------------------------- cranes
NCRANE = PK(0, 1, 3)
# Spaced by the crane's own width, not by a fraction of the quay. Three 25-wide
# cranes spread over 0.68 of a short quay end up 20 apart, which stands each one
# inside the next; the gantries then read as one tangled machine.
_CW = PK(0.0, 21.0, 25.0)
# And no more of them than the quay can actually hold. Spacing alone was not
# enough: three 25-wide cranes need 59 metres of quay, and where the quay is
# shorter than that they were simply pushed off the ends and bunched.
if _CW > 0.0:
    NCRANE = max(1, min(NCRANE, int(QL * 0.92 / (_CW * 1.18))))
_STEP = max(QL * 0.68 / max(1, NCRANE - 1), _CW * 1.18) if NCRANE > 1 else 0.0
for k in range(NCRANE):
    a = (-_STEP * (NCRANE - 1) * 0.5 + k * _STEP) if NCRANE > 1 else 0.0
    p = q(a, 5.0)
    cr = P.port_crane("Port.Crane%d" % k, PK(0, 21.0, 25.0),
                      PK(0, 24.0, 29.0), C, PK("orange", "orange", "yellow_lt"))
    cr.location = (p[0], p[1], 1.0)
    # boom must reach SEAWARD over the berths, not inland over the market
    cr.rotation_euler = (0, 0, QYAW - radians(90))

if PHASE == 1:                       # simple timber derrick instead
    p = q(4, 8.0)
    dv = B().use("wood")
    dv.cylz(0.6, 12.0, (p[0], p[1], 1.4), seg=8)
    dv.box((11.0, 0.7, 0.7), (p[0] + QN[0] * 4.5, p[1] + QN[1] * 4.5, 12.6),
           (0, radians(-16), QYAW + radians(90)))
    dv.use("steel_dk")
    dv.tube(0.1, [(p[0] + QN[0] * 9, p[1] + QN[1] * 9, 12.0),
                  (p[0] + QN[0] * 9, p[1] + QN[1] * 9, 3.0)], 4)
    dv.make("Port.Derrick", collection=C)

# ---------------------------------------------------------- container yard
COLS = ("blue", "red", "teal", "orange", "green_ind", "yellow_lt", "blue_lt")
NC = PK(0, 18, 36)
cy = B() if NC else None
# Down-shore half of the quay only. The yard used to run the whole length at
# a = -27..14, which is exactly where the shed and the control office stand -
# 292 triangles of container inside the shed and 242 inside the office.
ROWS, DEPTH = 3, 3          # shallow yard - keeps the port off the market pad
for i in range(NC):
    row = i % ROWS
    col = (i // ROWS) % DEPTH
    lay = i // (ROWS * DEPTH)
    a = -QL * 0.08 + row * 6.4
    n = -QW * 0.5 - 5.0 - col * 3.6
    pos = q(a, n)
    cy.use(COLS[(i * 3) % len(COLS)])
    cy.boxz((7.0, 3.0, 3.0), (pos[0], pos[1], 0.9 + lay * 3.0), (0, 0, QYAW))
if cy is not None:
    cy.make("Port.Containers", collection=C)

if PHASE == 1:                       # crates and barrels on the sand instead
    cs = B().use("wood_lt")
    for i in range(14):
        s = RNG.uniform(1.5, 2.4)
        pos = q(RNG.uniform(-14, 14), RNG.uniform(-20, -8))
        cs.boxz((s, s, s * 0.85), (pos[0], pos[1], 0.9), (0, 0, QYAW))
    cs.use("rust")
    for i in range(8):
        pos = q(RNG.uniform(-14, 14), RNG.uniform(-22, -10))
        cs.cylz(1.0, 2.0, (pos[0], pos[1], 0.9), seg=10)
    cs.make("Port.Crates", collection=C)

# ------------------------------------------------------------- buildings
# Everything on the quay is ordered along `a` so the haul road's approach stays
# clear: sheds up-shore, container yard in the middle, and the down-shore third
# left empty because that is the side the road comes in from. Putting the sheds
# down-shore instead - which is where the containers used to be - simply swapped
# which of them the road ran through.
_ps = q(-QL * 0.33, -QW * 0.5 - 10)
_shed = P.warehouse("Port.Shed", PK(14, 18, 22), PK(11, 14, 16), PK(6, 9, 11), C,
                    PK("wood_lt", "clad", "clad"),
                    PK("roof_red", "roof_blue", "roof_blue"))
_shed.location = (_ps[0], _ps[1], 0.6)
# Square to the quay, like everything else here. Left at world zero these two
# stood at an angle to the wharf they belong to, which is what made the
# container stacks read as stabbing into the shed rather than sitting beside it.
_shed.rotation_euler = (0, 0, QYAW)
if PHASE >= 2:
    _pc = q(-QL * 0.42, -QW * 0.5 + 2)
    _ctl = P.office("Port.Control", 11, 9, PK(1, 2, 3), C)
    _ctl.location = (_pc[0], _pc[1], 0.6)
    _ctl.rotation_euler = (0, 0, QYAW)
    # harbour light at the pier head
    hl = B().use("white")
    ph = q(QL * 0.5 + 4, PLEN + 6)
    hl.conez(2.0, 1.4, 7.0, (ph[0], ph[1], SEA - 1.0), seg=12)
    hl.use("red")
    hl.cylz(1.5, 2.0, (ph[0], ph[1], SEA + 6.0), seg=12)
    hl.use("lamp_glow")
    hl.cylz(1.1, 1.6, (ph[0], ph[1], SEA + 8.0), seg=10)
    hl.make("Port.Beacon", collection=C)

# ------------------------------------------------------------------ ships
# Ships berth ALONGSIDE the finger piers, so their length runs seaward (along
# n) and they are offset sideways (along a) clear of the pier decking.
PIER_A = [(-QL * 0.35 + k * (QL * 0.7 / max(1, NPIER - 1))) if NPIER > 1 else 0.0
          for k in range(NPIER)]
PIER_W = PK(7.0, 10.0, 12.0)
SHIPS = PK([("boat", 16.0)], [("coaster", 44.0)],
           [("cargo", 60.0), ("cargo", 52.0)])
for i, (kind, ln) in enumerate(SHIPS):
    beam = ln * 0.20
    # berth successive ships at opposite ends of the quay so they never
    # occupy the same slot
    base = PIER_A[0] if i == 0 else PIER_A[NPIER - 1]
    side = 1 if i == 0 else -1
    a = base + side * (PIER_W * 0.5 + beam * 0.5 + 1.6)
    # sit far enough out that the stern clears the quay wall
    pos = q(a, ln * 0.5 + 7.0)
    if kind == "boat":
        s = P.tug("Port.Boat", ln, C, "blue_lt")
    else:
        s = P.ship("Port.Ship%d" % i, ln, C,
                   hull=("red", "blue", "teal")[i % 3],
                   funnel=("orange", "yellow_lt", "red")[i % 3],
                   crates=(PHASE >= 2))
    s.location = (pos[0], pos[1], SEA + 0.15)
    # Head along QN, the seaward normal - NOT QYAW - 90, which is only the
    # seaward side on an island whose ocean happens to lie the way the coal
    # map's does. Where QN is flipped, that put both hulls 180 degrees out and
    # lying across each other in mid-harbour. On the coal island the two are
    # the same number, so its berths are unchanged.
    s.rotation_euler = (0, 0, atan2(QN[1], QN[0]))

# a ship under way, heading off-screen
if PHASE >= 2:
    ln = PK(0, 44.0, 66.0)
    sail = P.ship("Port.ShipOut", ln, C, hull="teal", funnel="red",
                  crates=True)
    sx, sy = L.SHIP_OUT
    syaw = atan2(L.SHIP_LANE[-1][1] - sy, L.SHIP_LANE[-1][0] - sx)
    sail.location = (sx, sy, SEA + 0.15)
    sail.rotation_euler = (0, 0, syaw)
    wk = P.wake("Port.Wake", C, ln * 0.85, ln * 0.22, 24)
    wk.location = (sx, sy, SEA + 0.25)
    wk.rotation_euler = (0, 0, syaw)
    sm = P.smoke_plume("Port.ShipSmoke", C, 1.8, 6, 14.0, 10.0)
    # Aft of the funnel, measured along the ship's own heading rather than a
    # fixed offset - the two islands' shipping lanes run opposite ways.
    sm.location = (sx - cos(syaw) * ln * 0.26, sy - sin(syaw) * ln * 0.26,
                   SEA + ln * 0.20)

if PHASE >= 3:
    tg = P.tug("Port.Tug", 20.0, C, "orange")
    tg.location = (q(QL * 0.46, PLEN * 0.5)[0], q(QL * 0.46, PLEN * 0.5)[1],
                   SEA + 0.6)
    tg.rotation_euler = (0, 0, QYAW + radians(30))

# ------------------------------------------------------------- berth check
# Hull length is phase-scaled here while SHIP_OUT is authored per island, so the
# two drift apart silently: at phase 3 the outbound ship used to sail straight
# through the outer berth, 15 units deep, on BOTH maps. World AABBs prove
# nothing about it - every hull lies on a diagonal - so compare oriented boxes.
bpy.context.view_layer.update()


def hull_box(o):
    """(cx, cy, along, across, half length, half beam) in world XY."""
    xs = [c[0] for c in o.bound_box]
    ys = [c[1] for c in o.bound_box]
    c = o.matrix_world @ Vector(((min(xs) + max(xs)) * 0.5,
                                 (min(ys) + max(ys)) * 0.5, 0.0))
    yw = o.rotation_euler.z
    return (c.x, c.y, (cos(yw), sin(yw)), (-sin(yw), cos(yw)),
            (max(xs) - min(xs)) * 0.5, (max(ys) - min(ys)) * 0.5)


def hull_gap(A, B):
    """Separating-axis gap between two oriented boxes; negative = interpenetrating."""
    worst = -1e9
    for X in (A, B):
        for ax in (X[2], X[3]):
            s = []
            for Z in (A, B):
                m = Z[0] * ax[0] + Z[1] * ax[1]
                r = (abs(Z[2][0] * ax[0] + Z[2][1] * ax[1]) * Z[4]
                     + abs(Z[3][0] * ax[0] + Z[3][1] * ax[1]) * Z[5])
                s.append((m - r, m + r))
            worst = max(worst, max(s[1][0] - s[0][1], s[0][0] - s[1][1]))
    return worst


hulls = [o for o in C.objects
         if any(k in o.name for k in ("Ship", "Boat", "Tug"))
         and not any(k in o.name for k in ("Smoke", "Wake"))]
tight, gap = None, 1e9
for i in range(len(hulls)):
    for j in range(i + 1, len(hulls)):
        g = hull_gap(hull_box(hulls[i]), hull_box(hulls[j]))
        if g < gap:
            gap, tight = g, (hulls[i].name, hulls[j].name)
if tight:
    print("   port: %d hulls, closest %s / %s at %.1f%s"
          % (len(hulls), tight[0], tight[1], gap,
             "   <-- OVERLAPPING" if gap < 0 else ""))

# ------------------------------------------------------------- port traffic
# Parked hard against the quay edge rather than out on the apron, which is
# where the haul road from the market arrives.
tk = P.truck("Port.Truck", "white", "cargo", C)
p = q(-QL * 0.2, -QW * 0.5 + 8)
tk.location = (p[0], p[1], 1.5)
tk.rotation_euler = (0, 0, QYAW)
for i, a in enumerate(PK((), (10.0,), (10.0, 24.0))):
    pp = q(a, -QW * 0.5 + 8)
    dup(tk, (pp[0], pp[1], 1.5), (0, 0, QYAW), None, C, "Port.Truck%d" % i)

if PHASE >= 2:
    fk = P.forklift("Port.Fork", C)
    pf = q(-QL * 0.36, -QW * 0.5 - 8)
    fk.location = (pf[0], pf[1], 0.9)
    fk.rotation_euler = (0, 0, QYAW + radians(50))
if PHASE >= 3:
    ld = P.loader("Port.Reach", C)
    pl = q(-QL * 0.16, -QW * 0.5 - 8)    # beside the container yard, not in it
    ld.location = (pl[0], pl[1], 0.9)
    ld.rotation_euler = (0, 0, QYAW + radians(-40))

for i, a in enumerate((-QL * 0.4, 0.0, QL * 0.4)[:PK(1, 2, 3)]):
    pp = q(a, -QW * 0.5 + 1)
    P.streetlight("Port.Lamp%d" % i, 9.0, 3.0, C).location = (pp[0], pp[1], 1.5)

lift_collection("Port", Z0)

print("port ok", stats(), "phase", PHASE, "apron z", round(Z0, 2))
