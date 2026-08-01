"""Step 2: heightfield island - mountains, river gorge, ocean, beach, rocks."""
import importlib
import layout
importlib.reload(layout)
L = layout

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
    z = -3.0 - 8.0 * t
    for thr, drop in ((L.FALLS[0], 4.2), (L.FALLS[1], 4.8)):
        z -= drop * L.smoothstep(thr - 0.009, thr + 0.009, t)
    return z


def flat_mask(x, y):
    """1 where the ground must be a flat pad (districts, sites, roads)."""
    m = 0.0
    # Gate each arm to the road's ACTUAL extent, so the corridor stops at the
    # road end instead of flattening the massif beyond it.
    x0, x1 = L.ROAD_X[0][0], L.ROAD_X[1][0]
    gx = L.smoothstep(x0 - 20, x0 - 1, x) * (1.0 - L.smoothstep(x1 + 1, x1 + 20, x))
    y0, y1 = L.ROAD_Y[0][1], L.ROAD_Y[1][1]
    gy = L.smoothstep(y0 - 20, y0 - 1, y) * (1.0 - L.smoothstep(y1 + 1, y1 + 20, y))
    m = max(m, L.band(abs(y), L.ROAD_W * 0.9, L.ROAD_W * 2.1) * gx)
    m = max(m, L.band(abs(x), L.ROAD_W * 0.9, L.ROAD_W * 2.1) * gy)
    dl, _ = L.dist_to_path(x, y, L.LOOP_C)
    m = max(m, L.band(dl, L.ROAD_W * 0.85, L.ROAD_W * 1.9))
    for pts, _n in L.SPURS:
        ds, _ = L.dist_to_path(x, y, pts)
        m = max(m, L.band(ds, 8.0, 17.0))
    dp, _ = L.dist_to_path(x, y, L.PORT_ROAD)
    m = max(m, L.band(dp, 9.0, 18.0))
    # rail cutting, but not over the tunnelled first stretch
    dr, tr = L.dist_to_path(x, y, L.RAIL)
    m = max(m, L.band(dr, 9.0, 20.0) * L.smoothstep(0.035, 0.105, tr))
    for cx, cy in L.DISTRICTS:
        m = max(m, L.rect_mask(x - cx, y - cy, L.PAD, L.PAD, 7))
    for _n, (sx, sy), _need in L.SITES:
        m = max(m, L.rect_mask(x - sx, y - sy, L.SITE_PAD, L.SITE_PAD, 7))
    # port apron - offset landward of the quay along (+0.8, +0.6)
    m = max(m, L.rect_mask(x - L.PORT[0] - 12, y - L.PORT[1] - 9, 38, 30, 8))
    return min(1.0, m)


def land_height(x, y):
    h = nz(x, y, 0.0105, 0.0) * 2.6 + nz(x, y, 0.038, 4.0) * 0.9
    pk = 0.0
    for px, py, rad, ht in L.PEAKS:
        d = hypot(x - px, y - py)
        if d < rad * 1.06:
            f = L.smoothstep(rad, rad * 0.08, d)
            crag = 1.0 + nz(x, y, 0.050, px * 0.1) * 0.40
            pk = max(pk, ht * (f ** 1.45) * crag)
    h += pk
    h *= (1.0 - flat_mask(x, y))
    d, t = L.dist_to_path(x, y, L.RIVER)
    if d < L.RIVER_CARVE * 1.4:
        w = L.band(d, L.RIVER_W * 0.52, L.RIVER_CARVE)
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
        # beach shelf: flatten the last few metres down to the waterline
        f = L.smoothstep(-22.0, 0.0, d)
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
for j in range(N + 1):
    y = -half + j * step
    rows.append([bm.verts.new((-half + i * step, y, height(-half + i * step, y)))
                 for i in range(N + 1)])
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
    if c.z < L.SEA_Z - 0.4:
        p.material_index = 5                      # sea floor
    elif c.z < 2.4 and L.sea_depth(c.x, c.y) > -16.0:
        p.material_index = 3                      # beach
    elif n.z < 0.60:
        p.material_index = 2 if c.z > 14 else 1
    elif c.z > 22.0:
        p.material_index = 1
    elif c.z < -3.5:
        p.material_index = 3
    elif c.z > 7.0 or nz(c.x, c.y, 0.028, 9.0) > 0.42:
        p.material_index = 4
    else:
        p.material_index = 0

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
        # push seaward (towards smaller x+y)
        sgn = -1.0 if (nx + ny) > 0 else 1.0
        fx = pos.x + nx * off * sgn + RNG.uniform(-2, 2)
        fy = pos.y + ny * off * sgn + RNG.uniform(-2, 2)
        # only break in the shallows, never out over deep water
        if not (-1.0 < L.sea_depth(fx, fy) < 13.0):
            continue
        bf.sphere(RNG.uniform(1.5, 3.0), (fx, fy, L.SEA_Z + RNG.uniform(-0.2, 0.3)),
                  1, scale=(1.5, 1.5, 0.26))
bf.make("Surf", collection=CT, smooth=True)


# ---------------------------------------------------------------------- river
def water(path, width, zfun, name, lift=1.6):
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
        w = width * (0.5 + 0.12 * nz(x, y, 0.05, 2.0))
        z = zfun(t) + lift
        a = bmw.verts.new((x + nx * w, y + ny * w, z))
        b = bmw.verts.new((x - nx * w, y - ny * w, z))
        if prev:
            try:
                bmw.faces.new([prev[0], a, b, prev[1]])
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


river_ob, rpts = water(L.RIVER, L.RIVER_W, bed_z, "River")

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
    dr, _ = L.dist_to_path(x, y, L.RIVER)
    steep = abs(height(x + 2, y) - z) + abs(height(x, y + 2) - z)
    coastal = -10.0 < L.sea_depth(x, y) < 6.0
    if not (z > 7.0 or steep > 1.8 or dr < 22.0 or coastal):
        continue
    s = RNG.uniform(1.4, 5.0) * (1.6 if z > 20 else 1.0)
    dup(rock_src[RNG.randrange(5)], (x, y, z - s * 0.25),
        (0, 0, RNG.uniform(0, 6.28)), (s, s, s * RNG.uniform(0.6, 1.0)),
        CT, "Rock")
    placed += 1

print("terrain ok", stats(), "rocks", placed)
