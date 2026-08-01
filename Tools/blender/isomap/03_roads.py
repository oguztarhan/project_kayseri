"""Step 3: road network.  Upgrades with PHASE:
   1  narrow dirt haul tracks, no markings
   2  paved two-lane with centre dashes and kerbs
   3  wide paved, full markings, crosswalks, kerbstones, lit junctions
"""
import importlib
import layout
importlib.reload(layout)
L = layout

purge_collection("Roads")
CR = coll("Roads")

SURF = PK("dirt", "asphalt", "asphalt")
SHOULDER = PK("dirt", "gravel", "gravel")
MAIN_W = PK(L.ROAD_W * 0.62, L.ROAD_W * 0.86, L.ROAD_W)
LOOP_W = PK(8.0, 10.0, 12.0)
SPUR_W = PK(7.0, 9.0, 10.5)
Z_ROAD = 0.10
Z_MARK = 0.22
MARKED = PHASE >= 2


def carriageway(pts, w, name, z=Z_ROAD):
    p3 = [(p[0], p[1], 0.0) for p in pts]
    strip(p3, w + PK(2.0, 3.0, 4.0), z=z - 0.06, name=name + ".shoulder",
          material=mat(SHOULDER), collection=CR)
    return strip(p3, w, z=z, name=name, material=mat(SURF), collection=CR)


roads = [(L.ROAD_X, MAIN_W, "Road.X"), (L.ROAD_Y, MAIN_W, "Road.Y"),
         (L.LOOP_C, LOOP_W, "Road.Loop")]
for pts, name in L.SPURS:
    # site spurs only exist once that site is unlocked
    need = {"Spur.Quarry": 2, "Spur.Store": 2, "Spur.Plant": 3}.get(name, 1)
    if PHASE >= need:
        roads.append((pts, SPUR_W, name))
roads.append((L.PORT_ROAD, PK(8.0, 10.0, 12.0), "Road.Port"))
for pts, w, name in roads:
    carriageway(pts, w, name)


def junction(cx, cy, size, name, m=None):
    b = B()
    b.use(m or SURF)
    b.box((size, size, 0.14), (cx, cy, Z_ROAD + 0.04))
    return b.make(name, collection=CR)


junction(0, 0, MAIN_W + PK(6, 9, 12), "Junc.Center",
         PK("dirt", "asphalt_lt", "asphalt_lt"))
for sx, sy in ((-1, -1), (-1, 1), (1, -1), (1, 1)):
    junction(73 * sx, 73 * sy, LOOP_W + 5, "Junc.LoopC")
for cx, cy in ((0, 73), (0, -73), (73, 0), (-73, 0)):
    junction(cx, cy, LOOP_W + 6, "Junc.LoopE")

# -------------------------------------------------------------------- markings
if MARKED:
    def dashes(pts, spacing=10.0, ln=4.0, wd=0.5, skip=None):
        b = B()
        for pos, yaw in scatter_along([(p[0], p[1], 0.0) for p in pts], spacing):
            if skip and any(hypot(pos.x - c[0], pos.y - c[1]) < c[2] for c in skip):
                continue
            b.box((ln, wd, 0.04), (pos.x, pos.y, Z_MARK), (0, 0, yaw))
        return b

    def edgelines(pts, w, name):
        p3 = [(p[0], p[1], 0.0) for p in pts]
        for s in (1, -1):
            off = []
            for pos, yaw in sample_bez(p3, max(8, len(p3) * 16)):
                nx, ny = -sin(yaw), cos(yaw)
                off.append((pos.x + nx * s * (w * 0.5 - 0.9),
                            pos.y + ny * s * (w * 0.5 - 0.9), 0.0))
            strip(off, 0.42, z=Z_MARK, name=name + ".edge",
                  material=mat("linepaint"), collection=CR)

    SKIP = [(0, 0, 19)] + [(73 * a, 73 * b, 13) for a in (-1, 1) for b in (-1, 1)] \
        + [(0, 73, 13), (0, -73, 13), (73, 0, 13), (-73, 0, 13)]

    dashes(L.ROAD_X, skip=SKIP).make("Mark.X", mat("linepaint"), CR)
    dashes(L.ROAD_Y, skip=SKIP).make("Mark.Y", mat("linepaint"), CR)
    dashes(L.LOOP_C, spacing=8.0, ln=3.0, skip=SKIP).make("Mark.Loop",
                                                          mat("linepaint"), CR)
    edgelines(L.ROAD_X, MAIN_W, "Road.X")
    edgelines(L.ROAD_Y, MAIN_W, "Road.Y")

if PHASE >= 3:
    bx = B().use("linepaint")
    for d in (1, -1):
        for i in range(8):
            o = -MAIN_W * 0.5 + 1.3 + i * 1.8
            bx.box((3.6, 0.95, 0.05), (d * (MAIN_W * 0.5 + 5.0), o, Z_MARK))
            bx.box((0.95, 3.6, 0.05), (o, d * (MAIN_W * 0.5 + 5.0), Z_MARK))
    bx.make("Crosswalk", collection=CR)
    # kerbstones along the main arterials
    kb = B().use("kerb")
    for path in (L.ROAD_X, L.ROAD_Y):
        p3 = [(p[0], p[1], 0.0) for p in path]
        for pos, yaw in scatter_along(p3, 5.0, offset=MAIN_W * 0.5 + 1.1,
                                      both=True):
            if hypot(pos.x, pos.y) < 24:
                continue
            kb.box((4.6, 0.7, 0.42), (pos.x, pos.y, 0.22), (0, 0, yaw))
    kb.make("Kerbs", collection=CR)

print("roads ok", stats(), "phase", PHASE)
