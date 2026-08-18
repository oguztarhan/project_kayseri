"""Step 2: heightfield island - mountains, river gorge, ocean, beach, rocks."""
import importlib
import layout
importlib.reload(layout)
L = layout
import grade
importlib.reload(grade)

try:
    from mathutils import noise as mnoise
    def nz(x, y, s, seed=0.0):
        return mnoise.noise(Vector((x * s, y * s, seed)))
except Exception:                                    # pragma: no cover
    def nz(x, y, s, seed=0.0):
        v = sin(x * s * 12.9898 + y * s * 78.233 + seed) * 43758.5453
        return (v - math.floor(v)) * 2.0 - 1.0

purge_collection("Terrain")
CT = coll("Terrain")


def bed_z(t):
    """River bed level along normalised arc length, with two waterfall steps."""
    z = L.RIVER_Z0 - L.RIVER_FALL * t
    for thr, drop in zip(L.FALLS, L.FALL_DROPS):
        z -= drop * L.smoothstep(thr - 0.009, thr + 0.009, t)
    return z


# Measured against the loop itself rather than against a circle of radius
# LOOP_R. The iron island's ring wanders between 61 and 84, so a radial band
# would flatten a corridor that misses the tarmac by up to 23.
def loop_dist(x, y):
    if not L.LOOP_C:
        return 1e9
    d, _ = L.dist_to_path(x, y, L.LOOP_C)
    return d


def road_mask(x, y):
    """1 on the carriageway corridors. Kept separate from the pads so the
    tarmac corridor can be sunk slightly - see ROAD_CUT below."""
    # Measured against the arterial ITSELF, not against the axis it happens to
    # lie on. dist_to_path clamps to the ends, which is the same gating the two
    # hand-written smoothsteps used to do; the iron island's arterials have a
    # right-angle in each end, and an axis band flattened a corridor the road
    # never reaches.
    m = 0.0
    for _art in (L.ROAD_X, L.ROAD_Y):
        da, _ = L.dist_to_path(x, y, _art)
        m = max(m, L.band(da, L.ROAD_W * 0.9, L.ROAD_W * 2.1))
    m = max(m, L.band(loop_dist(x, y), L.ROAD_W * 0.85, L.ROAD_W * 1.9))
    for pts, _n in L.SPURS:
        ds, _ = L.dist_to_path(x, y, pts)
        m = max(m, L.band(ds, 8.0, 17.0))
    # Turning heads sit off the end of a spur, up to a bulb radius plus half a
    # carriageway clear of its centreline - past the band above, so without this
    # the far side of every bulb is laid on unflattened ground.
    for pts, _n in L.HEADS:
        dh, _ = L.dist_to_path(x, y, pts)
        m = max(m, L.band(dh, 7.0, 15.0))
    dp, _ = L.dist_to_path(x, y, L.PORT_ROAD)
    m = max(m, L.band(dp, 9.0, 18.0))
    # The crew's pavement circuit needs levelling too - laid on unflattened
    # ground it sat up to 0.41 under the noise it was crossing. Concentric with
    # the ring, so the same radial band works for it.
    dw, _ = L.dist_to_path(x, y, L.FOOTPATH)
    m = max(m, L.band(dw, 5.0, 13.0))
    return min(1.0, m)


def pad_mask(x, y):
    """1 on the YARDS - the ground a district, site, town block or the quay
    apron actually stands a slab on.

    Kept apart from flat_mask because the river carve has to be held off these
    and only these. Gating it by the whole flat mask would stop a river cutting
    across a carriageway, and the copper map's river crosses six of them.
    """
    m = 0.0
    for cx, cy in L.DISTRICTS:
        m = max(m, L.rect_mask(x - cx, y - cy, L.PAD, L.PAD, 7))
    for _n, (sx, sy), _need in L.SITES:
        m = max(m, L.rect_mask(x - sx, y - sy, L.SITE_PAD, L.SITE_PAD, 7))
    # Town-centre yards. grade.py pins their HEIGHT, but the ground only gets
    # levelled here - without this the power plant's slab had 254 vertices under
    # the terrain noise it was standing on.
    for tx, ty in L.TOWNS:
        m = max(m, L.rect_mask(x - tx, y - ty, L.TOWN_PAD + 2, L.TOWN_PAD + 2, 8))
    # port apron - offset landward of the quay, which way round depends on
    # which side of the island the sea is on
    m = max(m, L.rect_mask(x - L.PORT[0] - L.PORT_APRON[0],
                           y - L.PORT[1] - L.PORT_APRON[1], 38, 30, 8))
    return min(1.0, m)


def flat_mask(x, y):
    """1 where the ground must be flat (districts, sites, roads, rail)."""
    m = road_mask(x, y)
    # rail cutting, but not over the tunnelled first stretch
    # The rail cutting - but NOT over the tunnelled stretch at the start, or the
    # corridor levels the very hill the portal is bored into. How much of the
    # line is underground is the island's business: coal and copper bore into a
    # massif a few units in, the iron line runs 60 units inside its hill.
    # The rail cutting - but NOT where the line is underground. TUNNEL is a LIST
    # of spans now: the first is the bore it starts in, and any after it are
    # through-tunnels partway along, which is the only way to build the thing a
    # player actually reads as a tunnel - track, mountain, track. With one span
    # the loop below never runs, so coal and copper are untouched.
    dr, tr = L.dist_to_path(x, y, L.RAIL)
    cut = L.smoothstep(L.TUNNEL[0][0], L.TUNNEL[0][1], tr)
    for _a, _b in L.TUNNEL[1:]:
        _c, _h = (_a + _b) * 0.5, (_b - _a) * 0.5
        cut *= 1.0 - L.band(abs(tr - _c), _h * 0.65, _h)
    m = max(m, L.band(dr, 9.0, 20.0) * cut)
    return min(1.0, max(m, pad_mask(x, y)))


# How far the ground is dished below the tarmac along a carriageway. Measured
# against the built meshes, roads were clearing the terrain by as little as 0.01
# and the loop was poking through in places; the corridor is 25 units wide, so
# this reads as a 1% dish rather than a trench, and it gives the ribbon room to
# sit proud instead of fighting the ground it is laid on.
#
# 0.22 rather than 0.16 because the ground is a smooth curve and the road is a
# chord across it: with the old 20-segment arterials the sag mid-segment was
# 0.29 against 0.38 of clearance. layout.straight() cut the sag to near nothing;
# this is the belt to its braces.
ROAD_CUT = 0.22


def land_height(x, y):
    # The graded surface is the LANDFORM, not merely a pad height. Adding it only
    # under the flat mask left every district a mesa standing 11-18 units out of
    # a plain still sitting at zero, and made "height above the built level"
    # negative everywhere else - which stripped the island of its trees.
    base = grade.road_z(x, y)
    # It has to fall away to the waterline though, or the whole coast is a cliff.
    # Gated by the flat mask below so the quay keeps its graded apron.
    base *= L.smoothstep(-6.0, 45.0, -L.sea_depth(x, y))
    h = nz(x, y, 0.0105, 0.0) * 2.6 + nz(x, y, 0.038, 4.0) * 0.9
    pk = 0.0
    for px, py, rad, ht in L.PEAKS:
        d = hypot(x - px, y - py)
        if d < rad * 1.06:
            f = L.smoothstep(rad, rad * 0.08, d)
            crag = 1.0 + nz(x, y, 0.050, px * 0.1) * 0.40
            pk = max(pk, ht * (f ** 1.45) * crag)
    h += pk
    # Flatten TO the graded surface, not to zero. This is what stopped the island
    # being a pancake: the noise and peaks above were being multiplied away
    # everywhere the map is built on.
    m = flat_mask(x, y)
    h = (base + h) * (1.0 - m) + grade.road_z(x, y) * m
    h -= ROAD_CUT * road_mask(x, y)

    # Gorge width varies ALONG the river - see river_carve() in the island
    # module. On the copper map it pinches to a rock notch through the built
    # middle of the island and opens to a floodplain at either end, which is
    # the only way a river gets across that map without eating the central
    # crossroads at one end or the market pad at the other.
    # An island need not have a river at all - see isle_coal.RIVER.
    d, t = (L.dist_to_path(x, y, L.RIVER) if L.RIVER else (1e9, 0.0))
    carve = L.river_carve(t)
    if L.RIVER and d < carve * 1.4:
        # ...but never through a yard. The carve is applied AFTER the flatten,
        # so ungated it simply overwrote whatever the pad had just laid: the
        # coal river runs 11 units off the mine pad's west edge with a 24-unit
        # gorge, and it ate the western third of the mine - 28 units deep at the
        # south-west corner - leaving the apron slab, the rock face and three
        # adits hanging over open air. The pad mask feathers over 7 units, so
        # what the gorge gets instead of the yard is a bank down off the terrace,
        # which is what a works cut into a river valley looks like.
        w = L.band(d, L.river_w(t) * 0.52, carve) * (1.0 - pad_mask(x, y))
        h = h * (1.0 - w) + (bed_z(t) - 1.5) * w
    # open-pit bowl once the quarry site is unlocked
    if PHASE >= 2:
        qx, qy = L.SITE_QUARRY
        dq = hypot(x - qx + 4, y - qy + 2)
        if dq < 24.0:
            h -= 15.0 * (L.band(dq, 4.0, 23.0) ** 1.3)
    return h


def height(x, y):
    """Land above the waterline, sea floor below it."""
    depth = L.sea_depth(x, y)          # >0 seaward of the shoreline
    h = land_height(x, y)
    if depth <= -26.0:
        return h
    wob = nz(x, y, 0.020, 11.0) * 5.0  # ragged, non-straight coastline
    d = depth + wob
    if d <= 0.0:
        # beach shelf: flatten the last few metres down to the waterline, but
        # not where the map is built on. The quay sits right at the water, and
        # ungated this dragged the port apron from its graded height down to 0.
        f = L.smoothstep(-22.0, 0.0, d) * (1.0 - flat_mask(x, y))
        return h * (1.0 - f) + (0.9 * (1.0 - f)) * f
    # underwater: shelve off, then drop to the deep
    prof = L.smoothstep(0.0, 58.0, d)
    floor = L.SEA_Z - 1.2 - (abs(L.SEA_DEEP) - 1.2) * prof
    floor += nz(x, y, 0.035, 21.0) * (1.2 + 2.4 * prof)
    return floor


# --------------------------------------------------------------------- ground
bm = bmesh.new()
N = L.GROUND_SEGS
half = L.GROUND_SIZE * 0.5
step = L.GROUND_SIZE / N
rows = []
H = []                       # kept for the colour pass: slope comes from the grid
for j in range(N + 1):
    y = -half + j * step
    hs = [height(-half + i * step, y) for i in range(N + 1)]
    H.append(hs)
    rows.append([bm.verts.new((-half + i * step, y, hs[i])) for i in range(N + 1)])
bm.verts.ensure_lookup_table()
for j in range(N):
    for i in range(N):
        bm.faces.new([rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i]])

me = bpy.data.meshes.new("Ground")
bm.to_mesh(me)
bm.free()
for mn in ("grass", "rock", "cliff", "sand", "grass_dry", "seabed"):
    me.materials.append(mat(mn))
ground = bpy.data.objects.new("Ground", me)
CT.objects.link(ground)

for p in me.polygons:
    c, n = p.center, p.normal
    # Vegetation and rock go by height ABOVE THE GRADED SURFACE, not absolute z.
    # The town now sits at 4-18, so absolute thresholds ("dry grass above 7",
    # "rock above 22") turned the whole inhabited half of the island brown.
    rel = c.z - grade.road_z(c.x, c.y)
    if c.z < L.SEA_Z - 0.4:
        p.material_index = 5                      # sea floor
    elif c.z < 2.4 and L.sea_depth(c.x, c.y) > -16.0:
        p.material_index = 3                      # beach
    elif n.z < 0.60:
        p.material_index = 2 if rel > 14 else 1
    elif rel > 22.0:
        p.material_index = 1
    elif c.z < -3.5:
        p.material_index = 3
    elif rel > 7.0 or nz(c.x, c.y, 0.028, 9.0) > 0.42:
        p.material_index = 4
    else:
        p.material_index = 0

# ------------------------------------------------------------- ground colours
# The island shader reads vertex colour and nothing else, and the generic bake in
# 13_export can only turn position into colour - which is why the grass came out
# as one flat green with a few pale blotches where the material happened to
# change. Ground colour is a landscape, not a texture: it follows the slope, the
# height above the valley floor, the water and the sun, so it is painted here.
# All six stops stay green: an island whose high ground goes khaki reads as bare
# earth, not as grass in the sun. The range is tone, with a little yellow lift at
# the top and a cool shade at the bottom.
GRASS = [(0.020, 0.070, 0.028),      # deep shade, hollows and the river banks
         (0.040, 0.122, 0.038),
         (0.070, 0.185, 0.050),
         (0.112, 0.245, 0.064),
         (0.168, 0.300, 0.080),
         (0.245, 0.350, 0.108)]      # sun on the exposed tops
EARTH = (0.175, 0.115, 0.058)
ROCKC = (0.315, 0.300, 0.272)
SANDC = (0.395, 0.330, 0.212)
SEABD = (0.120, 0.150, 0.115)

# The ground is painted here, not by a material, so the copper island's warmer
# country rock has to be repeated in these constants or the outcrops in
# 01_setup would be rusty while the hillsides they sit on stayed grey.
if L.DESIGN == "copper":
    EARTH = (0.205, 0.122, 0.052)
    ROCKC = (0.345, 0.272, 0.202)
    SANDC = (0.420, 0.340, 0.222)

# An island may bring its own palette. The ramp above is the green country the
# first two maps are in; the iron island is ferruginous ground and reads red.
# Same six-stop shade-to-sun structure whatever the hue, so the slope and
# height shading below is unchanged.
#
# This comes SECOND on purpose. The derived islands (isle_silver and friends)
# re-export a base map and set these to shift its ground off the original's;
# read the other way round, the copper block above would clobber the silver
# island's grey back to copper's rust.
GRASS = getattr(L, "GROUND_RAMP", GRASS)
EARTH = getattr(L, "GROUND_EARTH", EARTH)
ROCKC = getattr(L, "GROUND_ROCK", ROCKC)
SANDC = getattr(L, "GROUND_SAND", SANDC)


def slope_at(i, j):
    """Gradient magnitude from the built grid - cheaper and truer than
    re-sampling height(), which costs four noise fields per call."""
    i0, i1 = max(0, i - 1), min(N, i + 1)
    j0, j1 = max(0, j - 1), min(N, j + 1)
    dx = (H[j][i1] - H[j][i0]) / ((i1 - i0) * step)
    dy = (H[j1][i] - H[j0][i]) / ((j1 - j0) * step)
    return hypot(dx, dy)


def curv_at(i, j):
    """Height above the mean of the four neighbours: + on ridges, - in hollows.

    This is what gives flat-shaded ground its shape. Lighting alone only sees
    the facet normal, so a rolling meadow and a flat field light identically;
    darkening the hollows and catching the ridges is the terrain's own shading.
    """
    i0, i1 = max(0, i - 1), min(N, i + 1)
    j0, j1 = max(0, j - 1), min(N, j + 1)
    return H[j][i] - (H[j][i0] + H[j][i1] + H[j0][i] + H[j1][i]) * 0.25


def grass_at(x, y, z, i, j):
    sl = slope_at(i, j)
    rel = z - grade.road_z(x, y)
    # Three scales, none finer than a couple of quads: meadows, patches, grain.
    # Anything below the 2.6-unit vertex pitch cannot be resolved and comes out
    # as speckle, which is what the material bake's fifth octave was doing.
    dry = (0.46 + 0.30 * nz1(x, y, 1.0 / 52.0, 3.0)
           + 0.21 * nz1(x, y, 1.0 / 16.0, 8.0)
           + 0.10 * nz1(x, y, 1.0 / 6.0, 15.0))
    dry += 0.008 * max(0.0, rel)                       # bleached up the hillsides
    dry += 0.18 * L.smoothstep(0.12, 0.55, sl)         # and on the sunny faces
    if L.RIVER:
        dr, _t = L.dist_to_path(x, y, L.RIVER)
        dry -= 0.45 * L.band(dr, 7.0, 36.0)            # green follows the water
    c = ramp(GRASS, dry)
    c = mix(c, EARTH, L.smoothstep(0.48, 1.05, sl) * 0.55)  # soil through the turf
    c = mix(c, ROCKC, max(L.smoothstep(0.85, 1.60, sl),
                          L.smoothstep(19.0, 33.0, rel) * 0.85))
    if L.sea_depth(x, y) > -20.0:
        c = mix(c, SANDC, L.smoothstep(3.4, 0.5, z))        # up out of the beach
    if z < L.SEA_Z - 0.3:
        c = mix(c, SEABD, L.smoothstep(L.SEA_Z - 0.3, L.SEA_Z - 4.0, z))
    # Ridges catch the light, hollows hold shade.
    s = max(-0.17, min(0.17, curv_at(i, j) * 0.85))
    return (c[0] * (1.0 + s), c[1] * (1.0 + s), c[2] * (1.0 + s))


# Per vertex and per face, not per corner: the ground has 250k corners over 62k
# quads, and grade.road_z is not something to call a quarter of a million times.
VCOL = [None] * ((N + 1) * (N + 1))
for j in range(N + 1):
    yv = -half + j * step
    for i in range(N + 1):
        VCOL[j * (N + 1) + i] = grass_at(-half + i * step, yv, H[j][i], i, j)
# Per-face jitter, in tone AND in hue. Flat-shaded ground under one smooth colour
# field reads as a plastic sheet; facet-to-facet variation is the only texture a
# 2.6-unit quad can carry, and one facet greener than the next next to it does
# more for the look than any amount of finer noise, which just aliases.
# Sampled at each face's own centre, not at its index: laying the lattice out by
# index means every 701st face wraps a row, and 250 faces to a row put two or
# three rows on the same lattice line - which came out as diagonal banding.
# The tone is sampled at a scale a little coarser than one quad, so facets fall
# into clusters of two or three rather than a perfectly random quilt; the hue is
# sampled finer, for grain on top of that.
FJIT = [(1.0 + 0.105 * nz1(p.center.x, p.center.y, 0.9, 27.0),
         0.055 * nz1(p.center.x, p.center.y, 2.7, 61.0))
        for p in me.polygons]


def ground_colour(x, y, z, vi, fi):
    c = VCOL[vi]
    f, h = FJIT[fi]
    return (c[0] * (f + h), c[1] * f, c[2] * (f - h * 1.4))


paint(ground, ground_colour)

# ------------------------------------------------------------------------ sea
bs = B().use("sea")
bs.box((L.GROUND_SIZE * 1.15, L.GROUND_SIZE * 1.15, 0.4),
       (0, 0, L.SEA_Z - 0.2))
sea = bs.make("Sea", collection=CT)

# surf line along the shore
bf = B().use("foam")
for pos, yaw in scatter_along([(p[0], p[1], 0.0) for p in L.SHORE], 6.0):
    for k in range(3):
        off = -2.0 - k * 3.4
        nx, ny = -sin(yaw), cos(yaw)
        # push seaward - which of the two shore normals that is depends on
        # which half-plane the island's ocean occupies
        sgn = 1.0 if (nx * L.SEA_AXIS[0] + ny * L.SEA_AXIS[1]) > 0 else -1.0
        fx = pos.x + nx * off * sgn + RNG.uniform(-2, 2)
        fy = pos.y + ny * off * sgn + RNG.uniform(-2, 2)
        # only break in the shallows, never out over deep water
        if not (-1.0 < L.sea_depth(fx, fy) < 13.0):
            continue
        bf.sphere(RNG.uniform(1.5, 3.0), (fx, fy, L.SEA_Z + RNG.uniform(-0.2, 0.3)),
                  1, scale=(1.5, 1.5, 0.26))
bf.make("Surf", collection=CT, smooth=True)


# ---------------------------------------------------------------------- river
def reach(x, y, nx, ny, z, lo, hi, over=0.8):
    """How far off the centreline the bed is still under the surface at z.

    The water surface used to be a constant ribbon, `RIVER_W * 0.5` wide, which
    is narrower than the channel the terrain carves EVERYWHERE: 1 unit of dry
    river bed either side through the pinched middle and 8 at the floodplain
    ends, so the river read as a stream in a trench rather than a full river.
    Following the ground instead fills whatever the carve left, and goes on
    doing so if river_carve() is ever retuned.

    Sampled per side, so an asymmetric gorge gets an asymmetric river. `over`
    carries the edge a little way into the bank, where the discretised ground
    mesh would otherwise leave a seam of bare bed showing along the waterline.
    """
    d = lo
    while d < hi and height(x + nx * d, y + ny * d) <= z:
        d += 0.5
    return min(hi, d + over)


def water(path, zfun, name, lift=1.6):
    pts, segl, total = [], [], 0.0
    for i in range(len(path) - 1):
        Lx = hypot(path[i + 1][0] - path[i][0], path[i + 1][1] - path[i][1])
        segl.append(Lx)
        total += Lx
    acc = 0.0
    for i in range(len(path) - 1):
        ax, ay = path[i]
        bx, by = path[i + 1]
        n = max(6, int(segl[i] / 2.0))
        for k in range(n):
            f = k / n
            pts.append((ax + (bx - ax) * f, ay + (by - ay) * f,
                        (acc + segl[i] * f) / total))
        acc += segl[i]
    pts.append((path[-1][0], path[-1][1], 1.0))

    bmw = bmesh.new()
    prev = None
    for i, (x, y, t) in enumerate(pts):
        j = min(i + 1, len(pts) - 1)
        k = max(i - 1, 0)
        dx, dy = pts[j][0] - pts[k][0], pts[j][1] - pts[k][1]
        ln = hypot(dx, dy) or 1.0
        nx, ny = -dy / ln, dx / ln
        z = zfun(t) + lift
        # Never narrower than the flat bed the carve laid, never wider than the
        # gorge - river_carve() is what keeps the river off the central
        # crossroads at one end and the market pad at the other.
        lo, hi = L.river_w(t) * 0.52, L.river_carve(t)
        wl = reach(x, y, nx, ny, z, lo, hi)
        wr = reach(x, y, -nx, -ny, z, lo, hi)
        a = bmw.verts.new((x + nx * wl, y + ny * wl, z))
        b = bmw.verts.new((x - nx * wr, y - ny * wr, z))
        if prev:
            try:
                # Wound left-bank -> right-bank -> downstream, which puts the
                # normal UP. It used to be [prev_l, l, r, prev_r], and that is
                # the other way round: every one of the surface's 219 faces
                # pointed at the river bed. Lit from underneath, the water came
                # out a flat dark slab the same value as the rock around it, so
                # the river read as a dry channel however wide it was drawn.
                bmw.faces.new([prev[0], prev[1], b, a])
            except ValueError:
                pass
        prev = (a, b)
    mw = bpy.data.meshes.new(name)
    bmw.to_mesh(mw)
    bmw.free()
    mw.materials.append(mat("water"))
    ob = bpy.data.objects.new(name, mw)
    CT.objects.link(ob)
    return ob, pts


if L.RIVER:
    river_ob, rpts = water(L.RIVER, bed_z, "River")

    bff = B().use("foam")
    for thr in L.FALLS:
        x, y, t = min(rpts, key=lambda d: abs(d[2] - thr - 0.016))
        for k in range(22):
            a = RNG.uniform(0, 6.28)
            rr = RNG.uniform(0, L.RIVER_W * 0.6)
            bff.sphere(RNG.uniform(0.9, 2.3),
                       (x + cos(a) * rr, y + sin(a) * rr,
                        bed_z(t) + 1.7 + RNG.uniform(-0.4, 1.8)), 1)
    bff.make("Foam", mat("foam"), CT, smooth=True)

# ------------------------------------------------------------------- boulders
rock_src = []
for i in range(5):
    b = B()
    b.sphere(1.0, (0, 0, 0), 2, scale=(1.0, RNG.uniform(0.7, 1.3),
                                       RNG.uniform(0.55, 0.95)))
    o = b.make("RockSrc%d" % i, mat("rock" if i % 2 else "rock_dark"), CT)
    rough_verts(o, amount=0.34, scale=1.7, seed=i * 3.3)
    o.hide_render = True
    o.hide_viewport = True
    rock_src.append(o)

placed, tries = 0, 0
while placed < 470 and tries < 9000:
    tries += 1
    x = RNG.uniform(-half + 18, half - 18)
    y = RNG.uniform(-half + 18, half - 18)
    if flat_mask(x, y) > 0.12:
        continue
    z = height(x, y)
    if z < L.SEA_Z - 0.5:
        continue
    dr = L.dist_to_path(x, y, L.RIVER)[0] if L.RIVER else 1e9
    steep = abs(height(x + 2, y) - z) + abs(height(x, y + 2) - z)
    coastal = -10.0 < L.sea_depth(x, y) < 6.0
    rel = z - grade.road_z(x, y)          # above the graded surface, as above
    if not (rel > 7.0 or steep > 1.8 or dr < 22.0 or coastal):
        continue
    s = RNG.uniform(1.4, 5.0) * (1.6 if rel > 20 else 1.0)
    dup(rock_src[RNG.randrange(5)], (x, y, z - s * 0.25),
        (0, 0, RNG.uniform(0, 6.28)), (s, s, s * RNG.uniform(0.6, 1.0)),
        CT, "Rock")
    placed += 1

print("terrain ok", stats(), "rocks", placed)
