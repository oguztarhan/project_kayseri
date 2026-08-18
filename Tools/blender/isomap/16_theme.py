"""Step 16: the props that say WHICH island this is.

The two maps share every building, vehicle, road and material bar the ore, so
without this they read as one island in two colours. Each island names a THEME
and a couple of open-ground spots in its isle_ module; the builder for that
theme fills them.

Everything here stands on open ground BETWEEN the ring road and the districts,
on its own levelled slab, rather than inside a district pad. The four yards are
already full - the overlap audit had to be driven to zero to get them that way -
and a signature piece reads better as its own landmark anyway.

Adding a third island means a THEME string, two spots, and one builder below.
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

purge_collection("Theme")
C = coll("Theme")

scene = bpy.context.scene
dg = bpy.context.evaluated_depsgraph_get()


def ground_z(x, y):
    """Terrain height at (x, y) - the Ground mesh only, like 11_dressing."""
    hit, loc, _n, _i, obj, _m = scene.ray_cast(dg, (x, y, 460.0), (0, 0, -1))
    return loc.z if hit and obj is not None and obj.name == "Ground" else 0.0


def slab(b, cx, cy, w, d, z, thick=1.6, yaw=0.0):
    """A levelled pad under a signature piece.

    Thick on purpose: out here the ground still carries the districts' grade
    feather, so a thin slab would show daylight under its downhill edge.
    """
    b.box((w, d, thick), (cx, cy, z - thick * 0.5 + 0.3), (0, 0, yaw))


# ══════════════════════════════════════════════════════════════════ coal
def coal_theme():
    wx, wy = L.THEME_SPOTS["works"]
    wz = ground_z(wx, wy)

    # ---- coke-oven battery: the piece that reads as "coal" from the play camera
    b = B().use("concrete_dk")
    slab(b, wx, wy, 36.0, 17.0, wz)
    N = PK(3, 5, 6)
    for i in range(N):
        ox = wx - (N - 1) * 2.6 + i * 5.2
        b.use("brick")
        b.conez(3.1, 1.5, 3.4, (ox, wy - 1.0, wz + 0.3), seg=12)   # beehive
        b.boxz((5.0, 1.6, 1.1), (ox, wy - 4.6, wz + 0.3))          # oven mouth
        b.use("steel_dk")
        b.cylz(0.62, PK(4.0, 5.5, 7.0), (ox, wy - 1.0, wz + 3.6), seg=8)
    b.use("steel_dk")                                              # charging deck
    b.boxz((N * 5.2 + 2.0, 3.4, 3.2), (wx, wy + 5.4, wz + 0.3))
    b.use("hazard")
    b.boxz((N * 5.2 + 2.0, 0.3, 0.9), (wx, wy + 7.0, wz + 3.5))    # deck handrail
    b.make("Theme.CokeOvens", collection=C)

    if PHASE >= 2:                                                 # ovens venting
        for i in range(0, N, 2):
            ox = wx - (N - 1) * 2.6 + i * 5.2
            sm = P.smoke_plume("Theme.CokeSteam%d" % i, C, 1.5, 5, 11.0, 7.0)
            sm.location = (ox, wy - 1.0, wz + PK(7.0, 8.5, 10.0))

    o = P.coal_pile("Theme.CokeStock", PK(5.0, 6.5, 8.0), PK(3.0, 4.0, 5.0), C,
                    seed=13.0)                                     # feed for the ovens
    o.location = (wx - 24.0, wy + 1.0, ground_z(wx - 24.0, wy + 1.0) + 0.2)

    # ---- screening plant and the pit-prop stack beside it
    # Timber alone read as a lumber yard, which is the wrong island entirely.
    # The black screened stock beside it is what names the place.
    yx, yy = L.THEME_SPOTS["yard"]
    yz = ground_z(yx, yy)
    t = B().use("soot")
    slab(t, yx, yy, 26.0, 18.0, yz, thick=1.2)
    t.use("clad")                                                  # screening shed
    t.boxz((9.0, 7.0, PK(6.0, 7.5, 9.0)), (yx + 7.0, yy + 3.5, yz + 0.3))
    t.use("soot")
    t.roof((8.0, 10.2, 2.0), (yx + 7.0, yy + 3.5, yz + 0.3 + PK(6.0, 7.5, 9.0)),
           (0, 0, radians(90)))
    t.use("steel_dk")                                              # screen chute
    t.tube(0.5, [(yx + 3.0, yy + 3.5, yz + PK(4.5, 5.5, 6.5)),
                 (yx - 3.0, yy - 1.0, yz + 1.6)], 6)
    t.use("wood")                                                  # round props
    for s in range(PK(1, 2, 2)):
        bx = yx - 8.0 + s * 6.0
        for row in range(3):
            for k in range(3 - row):
                t.cyl(0.52, 8.0,
                      (bx, yy + 6.0 + row * 0.5 + k * 1.06 - (2 - row) * 0.53,
                       yz + 0.85 + row * 0.92),
                      (0, radians(90), radians(90)), 7)
    t.make("Theme.Screens", collection=C)

    for i, (dx, dy, rr, hh) in enumerate(PK([(-4, -5, 5.0, 3.2)],
                                            [(-4, -5, 6.0, 3.8), (6, -6, 4.6, 3.0)],
                                            [(-4, -5, 7.0, 4.4), (7, -6, 5.4, 3.4)])):
        o = P.coal_pile("Theme.Screened%d" % i, rr, hh, C, seed=31.0 + i * 4.1)
        o.location = (yx + dx, yy + dy, yz + 0.3)

    # ---- slack heaps: small spills of coal on the open ground, which is what
    # the ground round a colliery actually looks like
    for i, (hx, hy) in enumerate(L.THEME_SPILLS):
        hz = ground_z(hx, hy)
        if hz <= L.SEA_Z + 1.0:
            continue
        o = P.coal_pile("Theme.Slack%d" % i, 3.4 + (i % 3) * 1.1,
                        2.0 + (i % 2) * 0.9, C, seed=21.0 + i * 3.3)
        o.location = (hx, hy, hz - 0.3)


# ══════════════════════════════════════════════════════════════ copper
def copper_theme():
    wx, wy = L.THEME_SPOTS["works"]
    wz = ground_z(wx, wy)

    # The three pieces of this theme that are about the ORE rather than the map,
    # asked of the island the same way parts.py asks for L.ORE. The copper
    # island itself sets none of them and gets these defaults; the silver
    # island, which re-exports this map, sets all three - a silver works is
    # cyanide-leached and casts silver cathode, and verdigris is copper's
    # corrosion product, not silver's.
    LIQUOR = getattr(L, "LIQUOR", "leach")
    PATINA = getattr(L, "PATINA", "verdigris")
    PLATE = getattr(L, "PLATE", "copper_plate")

    # ---- leach ponds: the green liquor is the loudest colour on the island
    b = B().use("concrete")
    slab(b, wx, wy, 38.0, 25.0, wz)
    for i in range(PK(1, 2, 3)):
        px = wx - 12.0 + i * 12.0
        # The rim is four bunds, not a slab under the liquid: as a slab its top
        # face landed at exactly the pond's surface height and won the depth
        # test, so all three ponds rendered as flat grey rectangles.
        b.use(LIQUOR)
        b.boxz((9.6, 7.0, 0.75), (px, wy - 1.0, wz + 0.3))
        b.use("crust")
        for sx, sy, w, d in ((0, 3.9, 11.0, 1.2), (0, -3.9, 11.0, 1.2),
                             (5.4, 0, 1.2, 8.8), (-5.4, 0, 1.2, 8.8)):
            b.boxz((w, d, 1.15), (px + sx, wy - 1.0 + sy, wz + 0.3))
        b.use("steel_lt")                                  # launder into the pond
        b.tube(0.34, [(px, wy + 4.2, wz + 2.2), (px, wy + 1.6, wz + 1.6)], 6)
    b.use("steel_lt")                                      # header main
    b.tube(0.44, [(wx - 15.0, wy + 4.2, wz + 2.2),
                  (wx + 15.0, wy + 4.2, wz + 2.2)], 8)
    for i in range(4):
        b.cylz(0.22, 2.2, (wx - 13.0 + i * 8.6, wy + 4.2, wz + 0.3), seg=6)
    b.make("Theme.LeachPonds", collection=C)

    # pump house, the one verdigris roof on the map
    ph = B().use("clad")
    ph.boxz((8.0, 6.5, 4.6), (wx - 14.0, wy + 8.5, wz + 0.3))
    ph.use(PATINA)
    ph.roof((7.2, 8.8, 1.9), (wx - 14.0, wy + 8.5, wz + 4.9), (0, 0, radians(90)))
    ph.make("Theme.PumpHouse", collection=C)

    # ---- cathode plate: the metal itself, stacked where it is cast
    yx, yy = L.THEME_SPOTS["yard"]
    yz = ground_z(yx, yy)
    t = B().use("concrete_dk")
    slab(t, yx, yy, 22.0, 16.0, yz, thick=1.2)
    for s in range(PK(2, 3, 4)):
        bx = yx - 7.5 + (s % 2) * 9.0
        by = yy - 4.0 + (s // 2) * 8.0
        t.use("wood")                                      # pallet
        t.boxz((5.2, 3.6, 0.5), (bx, by, yz + 0.3))
        t.use(PLATE)
        for k in range(PK(6, 9, 12)):                      # plate bundle
            t.boxz((4.6, 3.0, 0.14), (bx, by, yz + 0.8 + k * 0.22))
    t.use("steel_dk")                                      # lifting frame
    for sx in (-1, 1):
        t.boxz((0.4, 0.4, 6.2), (yx + sx * 9.5, yy - 6.5, yz + 0.3))
        t.boxz((0.4, 0.4, 6.2), (yx + sx * 9.5, yy + 6.5, yz + 0.3))
    t.box((0.5, 13.6, 0.5), (yx - 9.5, yy, yz + 6.7))
    t.box((0.5, 13.6, 0.5), (yx + 9.5, yy, yz + 6.7))
    t.box((19.4, 0.6, 0.6), (yx, yy, yz + 6.9))
    t.make("Theme.CathodeYard", collection=C)

    # slag pots, tipped out beside the yard
    sp = B()
    for i in range(PK(1, 2, 3)):
        sx = yx - 6.0 + i * 6.0
        sp.use("rust")
        sp.conez(2.1, 1.5, 2.8, (sx, yy + 10.5, yz + 0.6), seg=10)
        sp.use("steel_dk")
        sp.boxz((5.0, 1.0, 0.7), (sx, yy + 10.5, yz + 0.3))
    sp.make("Theme.SlagPots", collection=C)

    # Raw malachite waiting to be leached. L.ORE is the island's own ore, so this
    # is green here and would be black on the coal map - the cheapest thematic
    # signal there is, and the one the eye reads first.
    for i, (dx, dy, rr, hh) in enumerate(PK([(-4, 6, 5.0, 3.2)],
                                            [(-4, 6, 6.0, 3.8), (7, 7, 4.4, 2.8)],
                                            [(-4, 6, 6.6, 4.2), (8, 7, 5.0, 3.2)])):
        o = P.coal_pile("Theme.OreStock%d" % i, rr, hh, C, seed=41.0 + i * 4.1)
        o.location = (yx + dx, yy + dy, yz + 0.3)

    # ---- malachite-stained boulders, out where the ore body outcrops
    for i, (hx, hy) in enumerate(L.THEME_SPILLS):
        hz = ground_z(hx, hy)
        if hz <= L.SEA_Z + 1.0:
            continue
        o = P.coal_pile("Theme.Outcrop%d" % i, 3.0 + (i % 3) * 1.0,
                        1.8 + (i % 2) * 0.8, C, seed=21.0 + i * 3.3)
        o.location = (hx, hy, hz - 0.3)


# ══════════════════════════════════════════════════════════════════ iron
def iron_theme():
    wx, wy = L.THEME_SPOTS["works"]
    wz = ground_z(wx, wy)

    # ---- blast furnace: the tallest thing on the island bar the scarp itself
    b = B().use("concrete_dk")
    slab(b, wx, wy, 34.0, 20.0, wz)
    FH = PK(16.0, 24.0, 31.0)
    b.use("steel_dk")                                  # furnace stack and bosh
    b.conez(4.6, 3.4, FH * 0.62, (wx, wy, wz + 0.3), seg=14)
    b.conez(3.4, 4.4, FH * 0.16, (wx, wy, wz + 0.3 + FH * 0.62), seg=14)
    b.cylz(4.4, FH * 0.22, (wx, wy, wz + 0.3 + FH * 0.78), seg=14)
    b.use("rust")                                      # bell top and downcomer
    b.conez(4.4, 1.2, 3.0, (wx, wy, wz + 0.3 + FH), seg=14)
    b.tube(1.1, [(wx + 4.0, wy, wz + FH * 0.9), (wx + 11.0, wy, wz + FH * 0.4),
                 (wx + 11.0, wy, wz + 2.0)], 8)
    b.use("steel")                                     # charging hoist
    for s in (1, -1):
        b.tube(0.34, [(wx - 7.4, wy + s * 3.2, wz + 0.3),
                      (wx - 5.2, wy + s * 1.6, wz + 0.3 + FH * 0.95)], 5)
    # hot-blast stoves: the row of tall drums that says "blast furnace" and not
    # "silo", because there are always three or four of them in a line
    b.use("brick")
    for i in range(PK(2, 3, 4)):
        sx = wx + 9.0 + i * 5.6
        b.cylz(2.4, FH * 0.72, (sx, wy + 5.0, wz + 0.3), seg=12)
        b.use("steel_dk")
        b.conez(2.4, 1.4, 2.0, (sx, wy + 5.0, wz + 0.3 + FH * 0.72), seg=12)
        b.use("brick")
    b.use("clad")                                      # cast house
    b.boxz((13.0, 8.0, PK(6.0, 7.5, 9.0)), (wx - 11.0, wy - 5.0, wz + 0.3))
    b.use("rust")
    b.roof((9.2, 14.4, 2.2), (wx - 11.0, wy - 5.0, wz + 0.3 + PK(6.0, 7.5, 9.0)),
           (0, 0, radians(90)))
    b.make("Theme.BlastFurnace", collection=C)

    if PHASE >= 2:
        sm = P.smoke_plume("Theme.FurnaceSmoke", C, 2.2, 7, 20.0, 12.0)
        sm.location = (wx, wy, wz + FH + 4.0)

    o = P.coal_pile("Theme.BurdenStock", PK(5.0, 6.5, 8.0), PK(3.0, 4.0, 5.0), C,
                    seed=17.0)                          # ore waiting to be charged
    o.location = (wx - 23.0, wy + 2.0, ground_z(wx - 23.0, wy + 2.0) + 0.2)

    # ---- ingot stacks and the slag bank
    yx, yy = L.THEME_SPOTS["yard"]
    yz = ground_z(yx, yy)
    t = B().use("concrete_dk")
    slab(t, yx, yy, 24.0, 17.0, yz, thick=1.2)
    for s in range(PK(2, 3, 4)):
        bx = yx - 7.0 + (s % 2) * 9.0
        by = yy - 4.0 + (s // 2) * 8.0
        t.use("steel_dk")                               # pig iron, stacked
        for k in range(PK(4, 6, 8)):
            t.boxz((5.0, 3.2, 0.5), (bx, by, yz + 0.3 + k * 0.62))
    t.use("rust")                                       # torpedo ladle on a bogie
    t.cyl(2.0, 9.0, (yx + 6.0, yy + 6.5, yz + 3.0), (0, radians(90), 0), 12)
    t.use("steel_dk")
    for s in (1, -1):
        t.boxz((2.4, 3.0, 1.2), (yx + 6.0 + s * 3.4, yy + 6.5, yz + 0.3))
    t.make("Theme.IngotYard", collection=C)

    # The slag bank: tipped over the edge of the yard and running downhill, the
    # way a real one does. Red-orange rather than the ore's dark haematite.
    sb = B().use("brick")
    for i in range(PK(2, 3, 5)):
        sb.conez(7.0 - i * 0.5, 0.0, 3.4 + (i % 2) * 1.0,
                 (yx - 4.0 + i * 5.5, yy - 13.0 - (i % 2) * 3.0,
                  ground_z(yx - 4.0 + i * 5.5, yy - 13.0) - 0.4), seg=11)
    sb.make("Theme.SlagBank", collection=C)

    # ---- haematite outcrops on the bare ground
    for i, (hx, hy) in enumerate(L.THEME_SPILLS):
        hz = ground_z(hx, hy)
        if hz <= L.SEA_Z + 1.0:
            continue
        o = P.coal_pile("Theme.Outcrop%d" % i, 3.2 + (i % 3) * 1.0,
                        1.9 + (i % 2) * 0.8, C, seed=21.0 + i * 3.3)
        o.location = (hx, hy, hz - 0.3)


# ══════════════════════════════════════════════════════════════════ gold
def gold_theme():
    wx, wy = L.THEME_SPOTS["works"]
    wz = ground_z(wx, wy)

    # ---- stamp mill: the tall timber battery house that crushed the quartz.
    # Reads as "gold rush" the way the blast furnace reads as "iron". The
    # battery row stands PROUD of the front wall, facing the camera, with the
    # ore bin high on its trestle behind - a mill works top-down, and showing
    # the drop is what makes it read as machinery rather than sheds.
    b = B().use("concrete_dk")
    slab(b, wx, wy, 36.0, 22.0, wz)
    N = PK(3, 5, 7)                                    # stamps in the battery
    BW = N * 2.4 + 3.0
    MH = PK(9.0, 11.0, 13.0)                           # battery house height
    b.use("clad")                                      # the house itself
    b.boxz((BW, 8.0, MH), (wx - 2.0, wy + 1.0, wz + 0.3))
    b.use("sluice_wood")
    b.roof((7.4, BW + 1.6, 2.4), (wx - 2.0, wy + 1.0, wz + 0.3 + MH),
           (0, 0, radians(90)))
    b.use("wood")                                      # ore bin, up on its trestle
    b.boxz((BW * 0.55, 5.4, 4.2), (wx - 2.0, wy + 8.4, wz + MH * 0.62))
    for sx in (-1, 1):
        for sy in (0.0, 3.4):
            b.boxz((0.8, 0.8, MH * 0.62), (wx - 2.0 + sx * BW * 0.24,
                                           wy + 6.8 + sy, wz + 0.3))
    b.use("sluice_wood")                               # feed chute into the house top
    b.tube(0.6, [(wx - 2.0, wy + 6.6, wz + MH * 0.62 + 1.2),
                 (wx - 2.0, wy + 3.6, wz + MH * 0.8)], 6)
    b.use("steel_dk")                                  # the stamp battery, out front
    for i in range(N):
        sx = wx - 2.0 - (N - 1) * 1.2 + i * 2.4
        b.cylz(0.34, MH * 0.66, (sx, wy - 3.9, wz + 1.6), seg=6)
        b.boxz((1.1, 1.1, 1.4), (sx, wy - 3.9, wz + 1.0 + MH * 0.66))  # stamp head
    b.box((BW - 1.0, 1.0, 1.0), (wx - 2.0, wy - 3.9, wz + 2.6 + MH * 0.66))
    b.use("wood")                                      # mortar box under the stamps
    b.boxz((BW - 0.6, 2.6, 1.5), (wx - 2.0, wy - 3.9, wz + 0.3))
    b.use("sluice_wood")                               # amalgam tables running down
    for i in range(N // 2 + 1):
        tx = wx - 2.0 - (N // 2) * 2.4 * 0.9 + i * 4.4
        b.box((3.6, 4.8, 0.5), (tx, wy - 7.6, wz + 1.5), (radians(-12), 0, 0))
    b.use("brick")                                     # boiler chimney at the gable
    b.cylz(1.0, MH + 4.0, (wx + BW * 0.5 + 1.8, wy + 1.0, wz + 0.3), seg=10)
    b.make("Theme.StampMill", collection=C)

    if PHASE >= 2:                                     # boiler working
        sm = P.smoke_plume("Theme.MillSmoke", C, 1.6, 5, 12.0, 8.0)
        sm.location = (wx + BW * 0.5 + 1.8, wy + 1.0, wz + MH + 5.0)

    o = P.coal_pile("Theme.QuartzFeed", PK(5.0, 6.5, 8.0), PK(3.0, 4.0, 5.0), C,
                    seed=23.0)                         # pay dirt waiting its turn
    o.location = (wx - 22.0, wy + 3.0, ground_z(wx - 22.0, wy + 3.0) + 0.2)

    # ---- sluice runs on the river bank, and the bullion cage beside them:
    # the gold itself on show, the way copper shows its cathode plate.
    yx, yy = L.THEME_SPOTS["yard"]
    yz = ground_z(yx, yy)
    t = B().use("concrete_dk")
    slab(t, yx, yy, 26.0, 18.0, yz, thick=1.2)
    t.use("sluice_wood")
    for r in range(PK(2, 3, 4)):                       # the sluice boxes, stepped
        ry = yy + 6.0 - r * 4.2
        t.box((16.0, 1.4, 0.9), (yx - 3.0, ry, yz + 2.2 - r * 0.4))
        for k in range(5):                             # riffle bars
            t.box((0.25, 1.3, 0.35), (yx - 9.5 + k * 3.3, ry, yz + 2.7 - r * 0.4))
        for lx in (-9.0, -3.0, 3.0):                   # trestle legs
            t.boxz((0.5, 0.5, 2.0 - r * 0.4), (yx + lx, ry, yz + 0.3))
    t.use("steel_dk")                                  # header flume off the hill
    t.tube(0.4, [(yx + 9.0, yy + 9.5, yz + 4.6), (yx + 5.0, yy + 6.0, yz + 2.8)], 6)
    t.make("Theme.Sluices", collection=C)

    v = B().use("steel_dk")                            # the strong cage
    for sx in (-1, 1):
        v.boxz((0.4, 0.4, 4.4), (yx + 8.0 + sx * 3.4, yy - 5.5, yz + 0.3))
        v.boxz((0.4, 0.4, 4.4), (yx + 8.0 + sx * 3.4, yy - 0.5, yz + 0.3))
    v.box((7.6, 5.8, 0.5), (yx + 8.0, yy - 3.0, yz + 4.8))
    v.use("wood")                                      # pallet
    v.boxz((5.2, 3.6, 0.5), (yx + 8.0, yy - 3.0, yz + 0.3))
    # Same hook as the copper theme's PLATE: what this island's refinery turns
    # out. Gold pours bars; the diamond island, which re-exports this map, has
    # nothing to pour and stacks sorted parcels of its own ore in the cage
    # instead.
    v.use(getattr(L, "PLATE", "bullion"))              # the poured bars
    for k in range(PK(4, 7, 10)):
        v.boxz((3.2 - (k % 3) * 0.3, 2.2, 0.5),
               (yx + 8.0, yy - 3.0, yz + 0.85 + k * 0.55))
    v.make("Theme.BullionCage", collection=C)

    # tailings fans running off the low ends of the sluices toward the river
    tf = B().use("sand")
    for i in range(PK(2, 3, 4)):
        tf.conez(4.6 - i * 0.6, 0.0, 2.2 + (i % 2) * 0.7,
                 (yx - 14.0 - i * 4.0, yy - 2.0 - (i % 2) * 5.0,
                  ground_z(yx - 14.0 - i * 4.0, yy - 2.0) - 0.4), seg=11)
    tf.make("Theme.Tailings", collection=C)

    # ---- ore carts on a stub of narrow gauge at the mill door: the scale of
    # working nothing else on any island has
    oc = B().use("steel_dk")
    for rx in (-0.55, 0.55):                            # the narrow-gauge stub
        oc.box((14.0, 0.22, 0.25), (wx - 8.0, wy - 10.5 + rx, wz + 0.35))
    for k in range(PK(1, 2, 3)):
        cx = wx - 4.0 - k * 5.0
        oc.use("rust")
        oc.boxz((2.4, 1.5, 1.1), (cx, wy - 10.5, wz + 0.9))
        oc.use("steel_dk")
        for sx in (-0.8, 0.8):
            for sy in (-0.6, 0.6):
                oc.cyl(0.32, 0.2, (cx + sx, wy - 10.5 + sy, wz + 0.65),
                       (radians(90), 0, 0), 8)
        if k == 0:                                      # the lead cart is loaded
            oc.use(L.ORE)
            oc.conez(1.0, 0.3, 0.8, (cx, wy - 10.5, wz + 2.0), seg=8)
    oc.make("Theme.OreCarts", collection=C)

    # ---- wind pumps: the dry-country waterworks, one tower at a time
    for i, (px, py) in enumerate(getattr(L, "THEME_WINDPUMPS", [])):
        pz = ground_z(px, py)
        if pz <= L.SEA_Z + 1.0:
            continue
        w = B().use("steel")
        WH = 7.5 + (i % 2) * 1.5
        for sx, sy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):   # lattice legs
            w.tube(0.14, [(px + sx * 1.3, py + sy * 1.3, pz),
                          (px + sx * 0.35, py + sy * 0.35, pz + WH)], 4)
        w.box((2.0, 2.0, 0.15), (px, py, pz + WH * 0.55))     # brace ring
        w.use("steel_lt")                               # rotor, aimed at the camera
        w.cyl(2.1, 0.3, (px + 0.9, py - 0.9, pz + WH + 0.9),
              (radians(90), 0, radians(45)), 18)
        w.use("steel_dk")
        w.box((0.2, 2.6, 0.7), (px - 1.4, py + 1.4, pz + WH + 0.9),
              (0, 0, radians(-45)))                     # tail vane
        w.use("clad")                                   # the tank it fills
        w.cylz(2.2, 2.4, (px + 4.2, py + 3.0, pz + 0.1), seg=12)
        w.make("Theme.Windpump%d" % i, collection=C)

    # ---- the old diggings: spoil mounds and abandoned timber on the slope
    # where the rush started. This is the island's backstory as set dressing.
    dg = getattr(L, "THEME_DIGGINGS", None)
    if dg is not None:
        dx0, dy0 = dg
        d = B()
        for k in range(7):                              # the mound field
            mx = dx0 - 12.0 + (k % 4) * 7.5 + (k // 4) * 3.0
            my = dy0 - 8.0 + (k // 4) * 9.0 + (k % 3) * 2.5
            d.use("gravel")
            d.conez(3.4 + (k % 3) * 0.8, 0.0, 1.8 + (k % 2) * 0.7,
                    (mx, my, ground_z(mx, my) - 0.3), seg=9)
        d.use("sluice_wood")                            # leaning props, a fallen one
        d.cyl(0.28, 5.0, (dx0 - 6.0, dy0 + 3.0, ground_z(dx0 - 6.0, dy0 + 3.0) + 2.2),
              (radians(14), radians(8), 0), 6)
        d.cyl(0.28, 5.0, (dx0 + 8.0, dy0 - 4.0, ground_z(dx0 + 8.0, dy0 - 4.0) + 0.5),
              (radians(88), 0, radians(30)), 6)
        # the gallows frame over the first shaft
        gz = ground_z(dx0, dy0)
        d.use("wood")
        for sx in (-1.6, 1.6):
            d.boxz((0.5, 0.5, 6.0), (dx0 + sx, dy0, gz))
        d.box((4.4, 0.6, 0.6), (dx0, dy0, gz + 6.0))
        d.use("steel_dk")
        d.cyl(0.9, 0.4, (dx0, dy0, gz + 5.6), (radians(90), 0, 0), 10)  # sheave
        d.make("Theme.Diggings", collection=C)

    # ---- the water flume that feeds the sluices, downhill out of the mesas
    fl = getattr(L, "THEME_FLUME", None)
    if fl:
        f = B().use("sluice_wood")
        pts = [(x, y, ground_z(x, y) + 3.4 - i * 0.9) for i, (x, y) in enumerate(fl)]
        f.tube(0.5, pts, 6)
        for i, (x, y, z) in enumerate(pts):             # trestle posts
            f.boxz((0.45, 0.45, max(0.8, z - ground_z(x, y))),
                   (x, y, ground_z(x, y)))
        f.make("Theme.Flume", collection=C)

    # ---- quartz outcrops on the open ground - the colour that says pay dirt
    for i, (hx, hy) in enumerate(L.THEME_SPILLS):
        hz = ground_z(hx, hy)
        if hz <= L.SEA_Z + 1.0:
            continue
        o = P.coal_pile("Theme.Outcrop%d" % i, 3.2 + (i % 3) * 1.0,
                        1.9 + (i % 2) * 0.8, C, seed=21.0 + i * 3.3)
        o.location = (hx, hy, hz - 0.3)


BUILDERS = {"coal": coal_theme, "copper": copper_theme, "iron": iron_theme,
            "gold": gold_theme}
BUILDERS[L.THEME]()

print("theme ok", stats(), L.THEME, "phase", PHASE)
