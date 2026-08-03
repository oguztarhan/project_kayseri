"""Step 3: road network.  Upgrades with PHASE:
   1  narrow dirt haul tracks, no markings
   2  paved two-lane with centre dashes and kerbs
   3  wide paved, full markings, crosswalks, kerbstones, lit junctions
"""
import importlib
import layout
importlib.reload(layout)
L = layout
import grade
importlib.reload(grade)

purge_collection("Roads")
CR = coll("Roads")

GZ = grade.road_z


def pitch(x, y, yaw):
    """Euler Y that lays a flat box along the road's slope.

    Markings and kerbs are boxes, not ribbons, so on a 15% grade a 4-unit dash
    left flat has one end 0.3 under the tarmac and the other 0.3 above it.
    lib._mx builds Euler(rot, 'XYZ') = Rz @ Ry @ Rx, and Ry(t) sends local +X to
    z = -sin(t), so the sign is negative to lift the leading end.
    """
    gx = (GZ(x + 2.0, y) - GZ(x - 2.0, y)) * 0.25
    gy = (GZ(x, y + 2.0) - GZ(x, y - 2.0)) * 0.25
    return -atan2(gx * cos(yaw) + gy * sin(yaw), 1.0)

SURF = PK("dirt", "asphalt", "asphalt")
SHOULDER = PK("dirt", "gravel", "gravel")
MAIN_W = PK(L.ROAD_W * 0.62, L.ROAD_W * 0.86, L.ROAD_W)
LOOP_W = PK(8.0, 10.0, 12.0)
SPUR_W = PK(7.0, 9.0, 10.5)
# Sits the tarmac clearly proud of the ground. At 0.10 the built roads cleared
# the terrain by as little as 0.01 and the loop poked through in two places, so
# stretches of road simply vanished. Together with 02_terrain's ROAD_CUT this
# gives ~0.38 of clearance, which also reads as a built-up carriageway edge.
Z_ROAD = 0.22
Z_MARK = 0.34
MARKED = PHASE >= 2


# Seams across the carriageway, as fractions of its width: edge, verge, wheel
# rut, crown, and back out. The surface used to be a two-vertex ribbon, which is
# one flat colour end to end - there was nowhere to hang a wheel track.
LANES = (0.5, 0.34, 0.22, 0.08, -0.08, -0.22, -0.34, -0.5)

# Linear, like Blender's ramps. The island shader shows vertex colour and nothing
# else, so these ARE the road.
DIRT_RUT = (0.072, 0.044, 0.024)     # packed and damp where the wheels run
DIRT_MID = (0.185, 0.122, 0.064)
DIRT_DRY = (0.330, 0.242, 0.140)     # dust thrown up onto the crown
DIRT_EDGE = (0.118, 0.090, 0.044)
ASPH_MID = (0.046, 0.048, 0.054)
ASPH_WEAR = (0.088, 0.091, 0.098)    # tyres polish the wheel tracks lighter
ASPH_EDGE = (0.032, 0.033, 0.038)
VERGE = PK((0.145, 0.102, 0.050), (0.120, 0.114, 0.100), (0.120, 0.114, 0.100))


def grain(x, y):
    """0..1 patchiness, at scales a road ribbon has the vertices to hold.

    Amplitudes kept under 0.5 between them so this never clamps: a clamped
    plateau is a flat-topped patch with a hard rim, and on the ring road that
    read as a spill of pale mud with an outline.
    """
    return max(0.0, min(1.0, 0.5 + 0.22 * nz1(x, y, 1.0 / 9.0, 5.0)
                        + 0.12 * nz1(x, y, 1.0 / 3.2, 12.0)))


def dirt_colour(x, y, z, vi, fi):
    u = abs(LANES[vi % len(LANES)])
    c = mix(DIRT_MID, DIRT_DRY, L.smoothstep(0.20, 0.02, u) * 0.75)
    c = mix(c, DIRT_RUT, exp(-((u - 0.22) / 0.080) ** 2) * 0.85)
    c = mix(c, DIRT_EDGE, L.smoothstep(0.33, 0.5, u) * 0.8)
    f = 0.80 + 0.38 * grain(x, y)
    return (c[0] * f, c[1] * f, c[2] * f)


def asphalt_colour(x, y, z, vi, fi):
    u = abs(LANES[vi % len(LANES)])
    c = mix(ASPH_MID, ASPH_WEAR, exp(-((u - 0.24) / 0.085) ** 2) * 0.8)
    c = mix(c, ASPH_EDGE, L.smoothstep(0.34, 0.5, u) * 0.7)
    f = 0.88 + 0.26 * grain(x, y)
    return (c[0] * f, c[1] * f, c[2] * f)


def verge_colour(x, y, z, vi, fi):
    f = 0.78 + 0.44 * grain(x, y)
    return (VERGE[0] * f, VERGE[1] * f, VERGE[2] * f)


def carriageway(pts, w, name, z=Z_ROAD):
    # One narrow verge rather than a wide separate band. The old shoulder stood out as
    # far as 4 units either side in its own gravel colour, so the road read as three
    # parallel stripes; trimmed back it just softens the edge and the carriageway reads
    # as a single solid surface.
    p3 = [(p[0], p[1], 0.0) for p in pts]
    # Same seams and 0.14 under, not two vertices at 0.03. A two-vertex ribbon
    # spans its whole width in one straight line, so wherever the grade is convex
    # across the road that line rode up through the tarmac - a pale wedge of
    # verge sitting in the middle of the carriageway.
    paint(strip(p3, w + PK(0.9, 1.2, 1.6), z=z - 0.14, name=name + ".shoulder",
                material=mat(SHOULDER), collection=CR, zfun=GZ, cols=LANES),
          verge_colour)
    return paint(strip(p3, w, z=z, name=name, material=mat(SURF), collection=CR,
                       zfun=GZ, cols=LANES),
                 dirt_colour if PHASE == 1 else asphalt_colour)


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


# No fillet discs at the crossings. They were a flat disc of surface laid over
# the junction to round its corners, and once the carriageway grew wheel ruts
# they read as exactly what they were: a pale plate dropped on the road, with no
# ruts running through it. Two roads crossing already overlap, so the tarmac is
# continuous without them.

# -------------------------------------------------------------------- markings
if MARKED:
    def dashes(pts, spacing=10.0, ln=4.0, wd=0.5, skip=None):
        b = B()
        for pos, yaw in scatter_along([(p[0], p[1], 0.0) for p in pts], spacing):
            if skip and any(hypot(pos.x - c[0], pos.y - c[1]) < c[2] for c in skip):
                continue
            b.box((ln, wd, 0.04), (pos.x, pos.y, Z_MARK + GZ(pos.x, pos.y)),
                  (0, pitch(pos.x, pos.y, yaw), yaw))
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
                  material=mat("linepaint"), collection=CR, zfun=GZ)

    # No centre line through a junction.
    SKIP = [(0, 0, 19)] \
        + [(L.LOOP_R * a, L.LOOP_R * b, 15) for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1))] \
        + [(p[0][0], p[0][1], 12) for p, n in L.SPURS if not n.startswith("Street.")] \
        + [(0, p[1][1], 12) for p, n in L.SPURS if n.startswith("Street.")]

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
            cx = d * (MAIN_W * 0.5 + 5.0)
            bx.box((3.6, 0.95, 0.05), (cx, o, Z_MARK + GZ(cx, o)))
            bx.box((0.95, 3.6, 0.05), (o, cx, Z_MARK + GZ(o, cx)))
    bx.make("Crosswalk", collection=CR)
    # kerbstones along the main arterials
    kb = B().use("kerb")
    for path in (L.ROAD_X, L.ROAD_Y):
        p3 = [(p[0], p[1], 0.0) for p in path]
        for pos, yaw in scatter_along(p3, 5.0, offset=MAIN_W * 0.5 + 1.1,
                                      both=True):
            if hypot(pos.x, pos.y) < 24:
                continue
            kb.box((4.6, 0.7, 0.42), (pos.x, pos.y, 0.22 + GZ(pos.x, pos.y)),
                   (0, pitch(pos.x, pos.y, yaw), yaw))
    kb.make("Kerbs", collection=CR)

# -------------------------------------------------------------- footways
# Sidewalks for the site crew. The ring pavement runs OUTSIDE the ring road, as
# its outer footway, and passes within a few metres of all four district gates -
# so the crew can walk mine to storage to refinery to market without ever
# leaving the pavement. It used to be a separate square inside the loop, which
# read as a second road cutting through the middle of town and went nowhere near
# the districts.
#
# Laid as ribbons with the same zfun as the carriageways, and a touch higher so
# they read as raised kerb rather than paint on the road.
PAVE = PK("gravel", "concrete", "concrete")
PAVE_W = PK(2.6, 3.2, 3.6)
Z_PAVE = Z_ROAD + 0.06

strip([(p[0], p[1], 0.0) for p in L.FOOTPATH], PAVE_W, z=Z_PAVE,
      name="Walk.Ring", material=mat(PAVE), collection=CR, zfun=GZ)

# Both sides of each arterial. Inside the ring they run from the centre junction
# out to the ring pavement; outside it they carry on to each district's gate.
# Both stop short of the ring road itself - that crossing is a crosswalk, below.
_off = MAIN_W * 0.5 + PAVE_W * 0.5 + 1.0
_in0, _in1 = 20.0, L.LOOP_R - 8.0
_out0, _out1 = L.LOOP_R + 8.0, 92.0
for s in (1, -1):
    for r0, r1, tag in ((_in0, _in1, "I"), (_out0, _out1, "O")):
        strip([(r0, s * _off, 0.0), (r1, s * _off, 0.0)], PAVE_W, z=Z_PAVE,
              name="Walk.XE" + tag, material=mat(PAVE), collection=CR, zfun=GZ)
        strip([(-r1, s * _off, 0.0), (-r0, s * _off, 0.0)], PAVE_W, z=Z_PAVE,
              name="Walk.XW" + tag, material=mat(PAVE), collection=CR, zfun=GZ)
        strip([(s * _off, r0, 0.0), (s * _off, r1, 0.0)], PAVE_W, z=Z_PAVE,
              name="Walk.YN" + tag, material=mat(PAVE), collection=CR, zfun=GZ)
        strip([(s * _off, -r1, 0.0), (s * _off, -r0, 0.0)], PAVE_W, z=Z_PAVE,
              name="Walk.YS" + tag, material=mat(PAVE), collection=CR, zfun=GZ)

# Crossings where the footways meet the ring road, so the pavement visibly
# continues over the tarmac instead of just stopping either side of it.
if MARKED:
    cw = B().use("linepaint")
    for s in (1, -1):
        for k in range(6):
            o = s * _off - PAVE_W * 0.5 + 0.4 + k * (PAVE_W - 0.8) / 5.0
            for a in (1, -1):
                cw.box((LOOP_W + 3.0, 0.6, 0.05),
                       (a * L.LOOP_R, o, Z_MARK + GZ(a * L.LOOP_R, o)),
                       (0, pitch(a * L.LOOP_R, o, 0.0), 0.0))
                cw.box((0.6, LOOP_W + 3.0, 0.05),
                       (o, a * L.LOOP_R, Z_MARK + GZ(o, a * L.LOOP_R)),
                       (0, pitch(o, a * L.LOOP_R, 1.5708), 1.5708))
    cw.make("Walk.Crossings", collection=CR)

if PHASE >= 2:
    kb = B().use("kerb")
    for pos, yaw in scatter_along([(p[0], p[1], 0.0) for p in L.FOOTPATH], 6.0,
                                  offset=PAVE_W * 0.5 + 0.25, both=True):
        # Short blocks: a 5.4-long kerbstone laid flat on a curve stands its ends
        # proud of the pavement it edges, which is what buried four of them in
        # the ground at the old footpath's corners.
        kb.box((3.0, 0.4, 0.3), (pos.x, pos.y, Z_PAVE + 0.12 + GZ(pos.x, pos.y)),
               (0, pitch(pos.x, pos.y, yaw), yaw))
    kb.make("Walk.Kerbs", collection=CR)

print("roads ok", stats(), "phase", PHASE)
