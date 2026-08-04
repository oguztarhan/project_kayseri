"""Step 12: unlockable expansion sites + the upgrade-tier furniture.

Each secondary site is either LOCKED - a hazard-striped pad with survey stakes,
a padlock sign and a translucent ghost of what will be built there - or
UNLOCKED, in which case the real facility stands on it.

    quarry  (screen top-centre)     unlocks at phase 2
    store   (screen right-centre)   unlocks at phase 2
    plant   (screen bottom-centre)  unlocks at phase 3
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

purge_collection("Sites")
C = coll("Sites")


# ------------------------------------------------------------------- locked
def lock_sign(x, y, z, name):
    b = B().use("steel_dk")
    b.boxz((0.7, 0.7, 7.0), (x, y, z))
    b.use("yellow_lt")
    b.box((0.4, 7.0, 5.0), (x, y, z + 9.0), (0, 0, radians(45)))
    b.use("steel_dk")                      # padlock body
    b.box((0.5, 2.4, 2.0), (x, y, z + 8.4), (0, 0, radians(45)))
    for i in range(7):                     # shackle arc
        a = radians(180) * i / 6.0
        b.box((0.4, 0.42, 0.42),
              (x - sin(radians(45)) * cos(a) * 0.95,
               y + cos(radians(45)) * cos(a) * 0.95,
               z + 9.7 + sin(a) * 0.95), (0, 0, radians(45)))
    return b.make(name, collection=C)


def locked_plot(cx, cy, name, ghost=(22, 16, 12)):
    hw = L.SITE_PAD * 0.8
    b = B().use("dirt")
    b.box((hw * 2, hw * 2, 0.30), (cx, cy, 0.30))
    b.use("hazard")                        # striped kerb round the boundary
    for s in (1, -1):
        b.box((hw * 2 + 2.4, 1.8, 0.5), (cx, cy + s * hw, 0.52))
        b.box((1.8, hw * 2 + 2.4, 0.5), (cx + s * hw, cy, 0.52))
    b.use("yellow_lt")                     # survey stakes
    for sx in (-1, 1):
        for sy in (-1, 1):
            b.boxz((0.5, 0.5, 2.6), (cx + sx * (hw - 2), cy + sy * (hw - 2), 0.5))
    b.make(name + ".Pad", collection=C)

    # Wireframe preview of what gets built here.  Edge beams read far better
    # than a solid translucent block, which just looks like a glass crate.
    g = B().use("ghost")
    gw, gd, gh = ghost
    t = 0.5
    z0 = 0.7
    for sx in (-1, 1):
        for sy in (-1, 1):
            g.boxz((t, t, gh), (cx + sx * gw * 0.5, cy + sy * gd * 0.5, z0))
    for zz in (z0, z0 + gh):
        for sy in (-1, 1):
            g.box((gw + t, t, t), (cx, cy + sy * gd * 0.5, zz))
        for sx in (-1, 1):
            g.box((t, gd + t, t), (cx + sx * gw * 0.5, cy, zz))
    # roof ridge, so the silhouette reads as a building
    g.box((t, gd + t, t), (cx, cy, z0 + gh * 1.28))
    for sy in (-1, 1):
        g.box((t, gd * 0.72, t), (cx - gw * 0.25, cy + sy * gd * 0.25,
                                  z0 + gh * 1.14), (radians(0), 0, 0))
    g.make(name + ".Ghost", collection=C)
    lock_sign(cx, cy - hw - 4.0, 0.5, name + ".Lock")


# ----------------------------------------------------------------- unlocked
def build_quarry(cx, cy):
    # the bowl itself is carved into the heightfield in 02_terrain; here we
    # just add the working benches and plant around it
    b = B().use("rock_dark")
    for i in range(5):                     # terraced benches down the pit wall
        r = 20.0 - i * 3.4
        b.cyl(r, 0.8, (cx - 4, cy - 2, -1.6 - i * 2.7), seg=16)
    b.make("Site.Quarry.Benches", collection=C)
    # Kept beside the pit rather than 40 units up-slope of it: out at (+20,+17)
    # the shed's near corner reached r=82.7, which is inside the ring road's
    # outer footway, so the pavement ran straight through it.
    P.warehouse("Site.Quarry.Shed", 20, 13, 9, C, "clad",
                "roof_orange").location = (cx + 15, cy + 4, 0.3)
    P.hopper("Site.Quarry.Hopper", 5.4, 11.0, C).location = (cx + 17, cy - 14, 0.3)
    P.conveyor((cx - 6, cy - 4, -8.0), (cx + 17, cy - 14, 11.0),
               "Site.Quarry.Conv", C, 2.4)
    ex = P.excavator("Site.Quarry.Excav", C)
    ex.location = (cx - 10, cy - 8, -10.5)
    ex.rotation_euler = (0, 0, radians(120))
    tk = P.truck("Site.Quarry.Truck", "yellow_lt", "coal", C)
    tk.location = (cx + 4, cy + 15, 0.3)
    tk.rotation_euler = (0, 0, radians(-40))
    o = P.coal_pile("Site.Quarry.Heap", 9.0, 6.0, C, seed=31.0)
    o.location = (cx - 18, cy + 14, 0.3)
    o.data.materials[0] = mat("rock_dark")


def build_store(cx, cy):
    b = B().use("concrete")
    b.box((52, 52, 0.30), (cx, cy, 0.15))
    b.use(L.ORE_SHINY)
    b.box((32, 24, 0.34), (cx - 2, cy, 0.19))
    b.make("Site.Store.Yard", collection=C)
    for i, (dx, cm) in enumerate(((-14, "cream"), (-5, "white"), (4, "teal"))):
        P.silo("Site.Store.Silo%d" % i, 4.2, 18.0, C, m=cm).location = (
            cx + dx, cy + 16, 0.3)
    for i, (dx, dy, rr, hh) in enumerate(((-10, -4, 11, 7), (8, -6, 9, 6))):
        P.coal_pile("Site.Store.Pile%d" % i, rr, hh, C,
                    seed=i * 7.0).location = (cx + dx, cy + dy, 0.3)
    P.warehouse("Site.Store.Shed", 22, 14, 9, C, "clad", "roof_blue").location = (
        cx + 16, cy + 14, 0.3)
    P.gantry("Site.Store.Gantry", 30.0, 13.0, C).location = (cx - 2, cy - 2, 0.3)
    ld = P.loader("Site.Store.Loader", C)
    ld.location = (cx + 14, cy - 12, 0.3)
    ld.rotation_euler = (0, 0, radians(150))
    tk = P.truck("Site.Store.Truck", "orange", "coal", C)
    tk.location = (cx + 20, cy - 4, 0.3)
    tk.rotation_euler = (0, 0, radians(-90))


def build_plant(cx, cy):
    b = B().use("concrete")
    b.box((52, 52, 0.30), (cx, cy, 0.15))
    b.use("concrete_dk")
    b.box((30, 20, 0.34), (cx, cy + 4, 0.19))
    b.make("Site.Plant.Pad", collection=C)
    for i, (dx, dy, rr, hh, cm) in enumerate((( -10, 8, 2.8, 24, "cream"),
                                              (0, 10, 3.2, 28, "white"))):
        P.column("Site.Plant.Col%d" % i, rr, hh, C, m=cm).location = (
            cx + dx, cy + dy, 0.3)
    P.stack("Site.Plant.Stack", 2.1, 28.0, C).location = (cx + 14, cy + 12, 0.3)
    sm = P.smoke_plume("Site.Plant.Smoke", C, 2.2, 6, 18.0, 9.0)
    sm.location = (cx + 14, cy + 12, 31.0)
    for i, (dx, dy, rr, hh, bd, bn) in enumerate((
            (-16, -6, 5.4, 6.8, "white", "red"),
            (-4, -10, 5.0, 6.4, "teal", "white"),
            (8, -6, 5.6, 7.0, "cream", "orange"))):
        P.tank("Site.Plant.Tank%d" % i, rr, hh, C, m=bd, band=bn).location = (
            cx + dx, cy + dy, 0.3)
    P.pipe_rack([(cx - 20, cy + 2, 0.3), (cx + 18, cy + 2, 0.3)],
                "Site.Plant.Rack", C, 4, 0.30, 4.4)
    P.warehouse("Site.Plant.Hall", 20, 13, 10, C, "clad", "roof_teal").location = (
        cx + 16, cy - 12, 0.3)
    tk = P.truck("Site.Plant.Truck", "white", "cargo", C)
    tk.location = (cx - 16, cy - 18, 0.3)
    tk.rotation_euler = (0, 0, radians(180))


BUILDERS = {"quarry": build_quarry, "store": build_store, "plant": build_plant}
GHOSTS = {"quarry": (24, 18, 12), "store": (26, 18, 14), "plant": (22, 16, 16)}

for nm, (sx, sy), need in L.SITES:
    if PHASE >= need:
        BUILDERS[nm](sx, sy)
    else:
        locked_plot(sx, sy, "Site." + nm, GHOSTS[nm])


# --------------------------------------------------- tier badges + spare plots
def tier_badge(x, y, z, n, name):
    b = B().use("yellow_lt")
    for i in range(n):
        b.box((3.0, 0.8, 0.4), (x, y, z + i * 1.2), (0, 0, radians(45)))
        b.box((3.0, 0.8, 0.4), (x, y, z + i * 1.2), (0, 0, radians(-45)))
    return b.make(name, collection=C)


for nm, (dx, dy) in (("Mine", L.MINE), ("Depot", L.DEPOT),
                     ("Refinery", L.REFINERY), ("Market", L.MARKET)):
    off = (36.0, 34.0)
    tier_badge(dx + off[0] * (1 if dx >= 0 else -1) * 0.0 + 34,
               dy - 34, 13.0, PHASE, "Tier." + nm)

# active construction site while the island is still growing
if PHASE == 2:
    cx, cy = L.DEPOT[0] - 26, L.DEPOT[1] - 26
    b = B().use("concrete_dk")
    b.box((24, 18, 0.35), (cx, cy, 0.45))
    b.use("steel")
    for i in range(4):
        for j in range(3):
            b.boxz((0.55, 0.55, 12.0), (cx - 10 + i * 6.6, cy - 7 + j * 7.0, 0.6))
    for j in range(3):
        for z in (5.5, 11.0):
            b.box((21, 0.4, 0.4), (cx, cy - 7 + j * 7.0, 0.6 + z))
    b.use("clad")
    b.box((21, 0.3, 5.0), (cx, cy - 7.4, 3.2))
    b.use("yellow_lt")
    for i in range(7):
        b.boxz((0.28, 0.28, 9.0), (cx - 12 + i * 4.0, cy + 8.0, 0.6))
    for z in (3.2, 6.4, 9.0):
        b.box((26, 0.26, 0.26), (cx, cy + 8.0, 0.6 + z))
    b.make("Site.Construction", collection=C)
    cr = P.tower_crane("Site.Crane", 34.0, 26.0, C)
    cr.location = (cx + 16, cy - 4, 0.3)
    cr.rotation_euler = (0, 0, radians(150))


# The three sites sit on three different pads, so each object is raised by
# the pad under its own position rather than by one shared offset.
lift_by_pad("Sites", grade.pad_z)

print("sites ok", stats(), "phase", PHASE,
      "unlocked", [n for n, _ in L.active_sites(PHASE)])
