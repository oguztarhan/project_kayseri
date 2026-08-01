"""Step 11: tycoon "upgrade" layer - buildable plots and works-in-progress.

Each district gets a marked-out expansion plot so the map reads as a game
board with room to grow: surveyed pads with corner markers, one site actively
under construction with a tower crane and scaffolding, and tier badges on the
main producers.
"""
import importlib
import layout
import parts
importlib.reload(layout)
importlib.reload(parts)
L = layout
P = parts

purge_collection("Plots")
C = coll("Plots")


def plot(cx, cy, w=26.0, d=20.0, name="Plot", marker="yellow"):
    """An empty, surveyed building pad - the 'available upgrade slot' look."""
    b = B().use("concrete")
    b.box((w, d, 0.30), (cx, cy, 0.42))
    b.use("concrete_dk")
    b.box((w - 2.2, d - 2.2, 0.14), (cx, cy, 0.60))
    b.use(marker)
    for sx in (-1, 1):
        for sy in (-1, 1):
            # corner survey markers
            b.boxz((2.6, 0.55, 0.55), (cx + sx * (w * 0.5 - 1.3),
                                       cy + sy * (d * 0.5 - 0.28), 0.57))
            b.boxz((0.55, 2.6, 0.55), (cx + sx * (w * 0.5 - 0.28),
                                       cy + sy * (d * 0.5 - 1.3), 0.57))
            b.boxz((0.5, 0.5, 2.4), (cx + sx * (w * 0.5 - 0.6),
                                     cy + sy * (d * 0.5 - 0.6), 0.57))
    return b.make(name, collection=C)


def site(cx, cy, w=24.0, d=18.0, h=11.0, name="Site"):
    """A plot mid-upgrade: footings, steel frame, scaffold, crane, materials."""
    b = B().use("concrete_dk")
    b.box((w, d, 0.35), (cx, cy, 0.45))
    b.use("steel")
    nx, ny = 4, 3
    for i in range(nx):
        for j in range(ny):
            x = cx - w * 0.42 + i * (w * 0.84 / (nx - 1))
            y = cy - d * 0.40 + j * (d * 0.80 / (ny - 1))
            b.boxz((0.55, 0.55, h), (x, y, 0.6))
    for j in range(ny):
        y = cy - d * 0.40 + j * (d * 0.80 / (ny - 1))
        for z in (h * 0.45, h * 0.9):
            b.box((w * 0.86, 0.4, 0.4), (cx, y, 0.6 + z))
    for i in range(nx):
        x = cx - w * 0.42 + i * (w * 0.84 / (nx - 1))
        for z in (h * 0.45, h * 0.9):
            b.box((0.4, d * 0.82, 0.4), (x, cy, 0.6 + z))
    # part-clad first floor
    b.use("clad")
    b.box((w * 0.86, 0.3, h * 0.42), (cx, cy - d * 0.40, 0.6 + h * 0.21))
    b.box((0.3, d * 0.5, h * 0.42), (cx - w * 0.42, cy - d * 0.15,
                                     0.6 + h * 0.21))
    # scaffolding
    b.use("yellow_lt")
    for i in range(7):
        x = cx - w * 0.5 + i * (w / 6.0)
        b.boxz((0.28, 0.28, h * 0.75), (x, cy + d * 0.54, 0.6))
    for z in (h * 0.28, h * 0.55, h * 0.75):
        b.box((w + 1.0, 0.26, 0.26), (cx, cy + d * 0.54, 0.6 + z))
        b.box((w + 1.0, 1.6, 0.12), (cx, cy + d * 0.54 - 0.7, 0.6 + z + 0.2))
    return b.make(name, collection=C)


def tier_badge(x, y, z, n, name="Tier"):
    """Small stacked chevrons marking a producer's upgrade tier."""
    b = B().use("yellow_lt")
    for i in range(n):
        b.box((2.6, 0.7, 0.35), (x, y, z + i * 1.0), (0, 0, radians(45)))
        b.box((2.6, 0.7, 0.35), (x, y, z + i * 1.0), (0, 0, radians(-45)))
    return b.make(name, collection=C)


# --------------------------------------------------------------- mine: tier 2
mx, my = L.MINE
tier_badge(mx + 33, my - 30, 12.0, 2, "Tier.Mine")
plot(mx + 26, my + 30, 20, 16, "Plot.Mine", "yellow")

# --------------------------------------- depot: tier 3 + active construction
dx, dy = L.DEPOT
tier_badge(dx + 32, dy + 32, 12.0, 3, "Tier.Depot")
site(dx - 24, dy - 24, 22, 16, 12.0, "Site.Depot")
cr = P.tower_crane("Crane.Depot", 34.0, 26.0, C)
cr.location = (dx - 8, dy - 26, 0.3)
cr.rotation_euler = (0, 0, radians(154))
mt = B().use("wood_lt")
for i in range(6):
    mt.boxz((3.4, 2.2, 1.1), (dx - 30 + (i % 3) * 4.2, dy - 12 + (i // 3) * 3.0,
                              0.3))
mt.use("steel_lt")
for i in range(5):
    mt.cylz(0.35, 9.0, (dx - 14 + i * 0.9, dy - 13, 0.6),
            (0, radians(90), radians(20)), 8)
mt.make("Site.Materials", collection=C)

# ------------------------------------------------------------ refinery: tier 3
rx, ry = L.REFINERY
tier_badge(rx - 32, ry + 30, 12.0, 3, "Tier.Refinery")
plot(rx + 24, ry - 4, 18, 22, "Plot.Refinery", "orange")

# -------------------------------------------------------------- market: tier 2
kx, ky = L.MARKET
tier_badge(kx - 31, ky - 28, 10.0, 2, "Tier.Market")
plot(kx + 6, ky + 30, 24, 14, "Plot.Market", "teal")

# ------------------------------------------------- painted yard markings
# Hazard chevrons and walkways break up the big grey aprons with colour.
mk = B().use("linepaint_y")
for i in range(11):                       # refinery: hazard edge to the bays
    mk.box((2.6, 0.9, 0.05), (rx - 24 + i * 5.0, ry - 17.4, 0.52),
           (0, 0, radians(38)))
mk.use("linepaint")
for i in range(2):                        # refinery walkway
    mk.box((52, 0.55, 0.05), (rx - 2, ry + 1.4 + i * 2.6, 0.52))
mk.use("linepaint_y")
for i in range(9):                        # depot: loader route chevrons
    mk.box((2.4, 0.85, 0.05), (dx + 18.5, dy + 14 - i * 5.4, 0.52),
           (0, 0, radians(-38)))
mk.use("linepaint")
mk.box((0.55, 44, 0.05), (dx + 21.5, dy - 6, 0.52))
mk.use("linepaint_y")
for i in range(8):                        # mine: pit edge warning line
    mk.box((2.4, 0.85, 0.05), (mx + 30, my - 26 + i * 5.0, 0.52),
           (0, 0, radians(38)))
mk.make("Yard.Markings", collection=C)

# a few bright skips / bins scattered round the yards for colour
bn = B()
for i, (bx_, by_, cm) in enumerate((
        (mx + 30, my + 16, "orange"), (mx + 26, my + 10, "teal"),
        (dx - 30, dy + 14, "orange"), (dx - 30, dy + 8, "red"),
        (rx - 30, ry - 12, "teal"), (rx - 30, ry - 18, "yellow_lt"),
        (kx + 30, ky + 4, "red"), (kx + 30, ky - 2, "teal"))):
    bn.use(cm)
    bn.boxz((4.4, 2.6, 2.2), (bx_, by_, 0.32))
    bn.use("steel_dk")
    bn.box((4.6, 2.8, 0.25), (bx_, by_, 2.6))
bn.make("Yard.Skips", collection=C)

print("plots ok", stats())
