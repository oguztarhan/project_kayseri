"""Anchor, pad and route manifest for the IndustrialReference map.

The map is FINAL. This script opens nothing, edits nothing and exports no
geometry - it reads map_geometry.json (the same file the Unity importer reads)
and writes anchors.json next to it. Codex binds gameplay to those names.

    python3 build_anchors.py

Coordinates are Unity space, world, metres - identical to what the importer
feeds IndustrialReference_Map.prefab, so a value drops straight in with no
conversion.

GROUND IS MEASURED, NOT ASSUMED
The first version of this file used a building's lowest vertex as the ground
height for every anchor on that plot. On a terraced island that is wrong: an
anchor 0.9m off the +z wall stands on the NEXT TERRACE UP, 1.3m above the
number it was given, so four station entrances were buried. Every anchor here
is now raycast down onto the actual walkable surface at its own x/z - terrace
meadows, cliffs, roads, the quay, the jetty, the bridges and the customer
islets - and the placement is rejected if the ground is missing (open water) or
off-level (a cliff face).

NOTHING THAT TOGGLES SHARES A BUILDING ANY MORE
The first version put two zones on one plot, which made progressive unlocking
impossible - hiding the Navigation Works also hid Storage, hiding the
Figurehead Atelier also hid the port the player sails from.

Only the five equipment stations ever toggle. Refinery and Storage are
logistics points: two anchors each, no building, no locked state, nothing to
hide. So the five buildings go to the five stations, and the two process zones
get their own always-on props:

    Cannon Foundry   -> Smelting_Plant        hideable on its own
    Hull Forge       -> Blue_Factory          hideable on its own
    Rigging Loft     -> Refinery              hideable on its own
    Navigation Works -> Warehouse             hideable on its own
    Figurehead Atl.  -> construction pad      NEEDS ART
    Refinery zone    -> Horizontal_Tank       always on, never hidden
    Storage zone     -> the container stack   always on, never hidden
    Mine, Port, Ship, Crane                   always on, never hidden

The island is full - his six plots are 4-6m across on 5m-deep terraces with
trees and props in every gap - so the fifth station gets the one clear patch of
level waterfront left, between the quay and the jetty. Its pad is found by
search: nothing of his overlaps it and the ground under it is flat. Locked
state is the bare pad; built state is whatever art lands there later. That is
an honest art gap, not a hidden one.

FACES
The terraces are only ~5m deep and his buildings sit hard against the uphill
retaining wall, so there is no standable ground on a plot's +z face at all.
Material therefore enters on the +x flank and leaves on the -z face:

    _Input   +x flank, uphill half      _Work    plot centre
    _Output  -z face, downhill          _Worker  +x flank, downhill half
    _Upgrade above the roof (no ground - it is a badge)

Offsets are not fixed: each face is scanned outward and the largest offset that
still stands on level ground is used.
"""

import json
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
GEOMETRY = os.path.join(HERE, "map_geometry.json")
OUT = os.path.join(HERE, "anchors.json")

# How far off a plot wall an anchor may stand. Scanned from FAR down to NEAR;
# the first offset that is on level ground wins.
FAR, NEAR, STEP = 1.0, 0.4, 0.1
# A surface is "the same level" as the plot within this. Terrace steps are
# 1.3m, so 0.25 separates a step from the slight camber of a road or slab.
LEVEL_TOL = 0.25
# Candidate construction pad footprints, largest first. The search takes the
# first that fits; the manifest reports what it actually got.
PADS = [(3.0, 2.4), (2.6, 2.0), (2.2, 1.6), (1.8, 1.2), (1.6, 0.9), (1.2, 0.7)]

# Anything the terrain raycast is allowed to hit. Everything else is a
# building, a prop or a tree and must not be stood on.
WALKABLE = {
    "02_Terrain/Terrace_01": None,
    "02_Terrain/Terrace_02": None,
    "02_Terrain/Terrace_03": None,
    "02_Terrain/Terrace_04": None,
    "02_Terrain/Terrace_05": None,
    "03_Parts/Main_road_ochre_foundation": None,
    "03_Parts/Main_road_luminous_golden_ribbon": None,
    "03_Parts/Main_road_bright_center": None,
    "03_Parts/Factory_circular_service_road": None,
    "03_Parts/Factory_glowing_lane": None,
    "03_Parts/Customer_connection": None,
    "03_Parts/Customer_connection_001": None,
    "03_Parts/Customer_connection_002": None,
    "08_Harbor/Port": ("quay", "jetty"),
    "09_Customers/Customer_Island_01": ("islet", "plaza"),
    "09_Customers/Customer_Island_02": ("islet", "plaza"),
    "09_Customers/Customer_Island_03": ("islet", "plaza"),
}

# Groups that are ground, decoration or interface - they never block a pad or
# a route. Everything else does. The parked wagons and the scattered stones are
# in here on purpose: the wagons are dressing that gameplay vehicles will
# replace, and one decorative pebble sits square in the middle of the road ramp
# between terrace 4 and terrace 5, which is the map's only graded link there.
NON_BLOCKING = ("01_Parts/", "02_Terrain/", "03_Parts/", "10_Scenery/",
                "12_Interface/")
NON_BLOCKING_ANY = ("_Vehicles/", "/Scattered_stone")
# Pine canopies. His road ramps run straight through the needle cones of the
# trees planted on the terrace below them, so a canopy that blocked movement
# would cut terrace 4 off from terrace 5 entirely. Trunks still block.
NON_BLOCKING_PARTS = ("needles",)

# id, art group (None = needs a pad), pad seed x/z, pad ground level
STATIONS = [
    ("Cannon",     "05_Smelter/Smelting_Plant", None,          None),
    ("Hull",       "06_Factory/Blue_Factory",   None,          None),
    ("Rigging",    "07_Refinery/Refinery",      None,          None),
    ("Navigation", "08_Harbor/Warehouse",       None,          None),
    ("Figurehead", None,                        (-0.7, -11.8), 1.066),
]

# Logistics points. No building, no locked state, so sharing a terrace with a
# station costs nothing - but they still get their own always-on prop to stand
# on so the player can see where the step happens.
ZONES = [
    ("Refinery", ["07_Refinery/Horizontal_Tank"]),
    ("Storage",  ["08_Containers_Intermodal_container",
                  "08_Containers_Intermodal_container_001"]),
]

CUSTOMERS = ["09_Customers/Customer_Island_01",
             "09_Customers/Customer_Island_02",
             "09_Customers/Customer_Island_03"]

STOPS = [
    ("Camera_Stop_01", "04_Mine/Crusher_Tower"),
    ("Camera_Stop_02", "05_Smelter/Smelting_Plant"),
    ("Camera_Stop_03", "06_Factory/Blue_Factory"),
    ("Camera_Stop_04", "07_Refinery/Refinery"),
    ("Camera_Stop_05", "08_Harbor/Warehouse"),
    ("Camera_Stop_06", "08_Harbor/Port"),
    ("Camera_Stop_07", "09_Customers/Customer_Island_02"),
]

SEA = ("01_Parts/Endless_turquoise_sea", "10_Scenery/Sea_Ripples",
       "02_Terrain/Shore_Foam")


# --------------------------------------------------------------- geometry --

def load():
    with open(GEOMETRY) as fh:
        d = json.load(fh)
    return d, {g["name"]: g for g in d["groups"]}


D, GROUPS = load()
MESHES = D["meshes"]


def parts(gname, keys=None):
    """(name, world offset, mesh) for the parts of one group, optionally only
    those whose name contains one of `keys`."""
    g = GROUPS[gname]
    gp = g["position"]
    for p in g["parts"]:
        if keys and not any(k in p["name"] for k in keys):
            continue
        pp = p["position"]
        yield p["name"], [gp[k] + pp[k] for k in range(3)], MESHES[p["mesh"]]


def aabb(gname, keys=None):
    """World axis-aligned bounds. Parts carry no rotation or scale in this
    manifest, which is why summing the two offsets is enough."""
    lo, hi = [1e9] * 3, [-1e9] * 3
    for _, off, m in parts(gname, keys):
        v = m["vertices"]
        for i in range(0, len(v), 3):
            for k in range(3):
                w = off[k] + v[i + k]
                lo[k] = min(lo[k], w)
                hi[k] = max(hi[k], w)
    return lo, hi


def aabb_many(gnames):
    lo, hi = [1e9] * 3, [-1e9] * 3
    for g in gnames:
        a, b = aabb(g)
        for k in range(3):
            lo[k] = min(lo[k], a[k])
            hi[k] = max(hi[k], b[k])
    return lo, hi


def r3(v):
    return [round(float(x), 3) for x in v]


# ----------------------------------------------------------------- ground --

class Ground(object):
    """Downward raycast against the walkable surfaces, bucketed on a 0.5m XZ
    grid. Returns the HIGHEST hit, which on this map is the top surface -
    the meadow above its own cliff, the road above the meadow."""

    CELL = 0.5

    def __init__(self):
        self.grid = {}
        for gname, keys in WALKABLE.items():
            for _, off, m in parts(gname, keys):
                v = m["vertices"]
                for sm in m["submeshes"]:
                    t = sm["triangles"]
                    for i in range(0, len(t), 3):
                        tri = tuple(
                            tuple(off[k] + v[3 * t[i + j] + k] for k in range(3))
                            for j in range(3))
                        self._insert(tri)

    def _insert(self, tri):
        xs = [p[0] for p in tri]
        zs = [p[2] for p in tri]
        c = self.CELL
        for ix in range(int(math.floor(min(xs) / c)), int(math.floor(max(xs) / c)) + 1):
            for iz in range(int(math.floor(min(zs) / c)), int(math.floor(max(zs) / c)) + 1):
                self.grid.setdefault((ix, iz), []).append(tri)

    def at(self, x, z):
        c = self.CELL
        best = None
        for tri in self.grid.get((int(math.floor(x / c)), int(math.floor(z / c))), ()):
            (ax, ay, az), (bx, by, bz), (cx, cy, cz) = tri
            det = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz)
            if abs(det) < 1e-12:
                continue
            u = ((bz - cz) * (x - cx) + (cx - bx) * (z - cz)) / det
            v = ((cz - az) * (x - cx) + (ax - cx) * (z - cz)) / det
            w = 1.0 - u - v
            if u < -1e-6 or v < -1e-6 or w < -1e-6:
                continue
            y = u * ay + v * by + w * cy
            if best is None or y > best:
                best = y
        return best

    def level(self, x, z, y, tol=LEVEL_TOL):
        g = self.at(x, z)
        return g is not None and abs(g - y) <= tol


GROUND = Ground()


# -------------------------------------------------------------- occupancy --

def blockers():
    """XZ footprint of everything that is not ground. Roads are ribbons whose
    bounding box spans the whole island, so they are handled by ROAD instead
    and left out here."""
    out = []
    for gname in GROUPS:
        if gname.startswith(NON_BLOCKING) or any(t in gname for t in NON_BLOCKING_ANY):
            continue
        skip = WALKABLE.get(gname)
        for pname, off, m in parts(gname):
            if skip and any(k in pname for k in skip):
                continue                      # the quay and the islet plazas
            if any(k in pname for k in NON_BLOCKING_PARTS):
                continue
            v = m["vertices"]
            x0 = y0 = z0 = 1e9
            x1 = y1 = z1 = -1e9
            for i in range(0, len(v), 3):
                x0 = min(x0, off[0] + v[i])
                x1 = max(x1, off[0] + v[i])
                y0 = min(y0, off[1] + v[i + 1])
                y1 = max(y1, off[1] + v[i + 1])
                z0 = min(z0, off[2] + v[i + 2])
                z1 = max(z1, off[2] + v[i + 2])
            out.append((gname, x0, x1, z0, z1, y0, y1))
    return out


BLOCKERS = blockers()


def occupied(x0, x1, z0, z1, ignore=(), y=None):
    """What stands in this footprint. With `y` - the ground height there - the
    test also ignores anything that is entirely below your feet or entirely
    over your head: the quay pilings hang under the deck and the crane boom
    passes 2m above it, and neither is something you walk into."""
    for gname, bx0, bx1, bz0, bz1, by0, by1 in BLOCKERS:
        if gname in ignore:
            continue
        if y is not None and (by1 < y + 0.05 or by0 > y + 2.0):
            continue
        if bx0 < x1 and bx1 > x0 and bz0 < z1 and bz1 > z0:
            return gname
    return None


# ------------------------------------------------------------------ road --

def centreline(gname, window=16):
    """The main road is a ribbon whose vertices run in strip order from the
    mine to the jetty, so averaging a sliding window of them is the
    centreline. window=16 is one ring; smaller windows jump between edges."""
    _, off, m = list(parts(gname))[0]
    v = m["vertices"]
    p = [(off[0] + v[i], off[1] + v[i + 1], off[2] + v[i + 2])
         for i in range(0, len(v), 3)]
    return [tuple(sum(q[k] for q in p[i:i + window]) / window for k in range(3))
            for i in range(0, len(p) - window + 1, window)]


ROAD = centreline("03_Parts/Main_road_bright_center")


def on_road(x, z, width=0.9):
    for rx, _, rz in ROAD:
        if (rx - x) ** 2 + (rz - z) ** 2 < width * width:
            return True
    return False


WALKER = None                 # built in build(), once Camera_Bounds is known


# ------------------------------------------------------------- placement --

def face(box, level, axis, sign, along=0.0, ignore=()):
    """A point off one face of `box`, as far out as level ground allows.
    `along` slides it along the face as a fraction of that face's length."""
    lo, hi = box
    cx = (lo[0] + hi[0]) * 0.5
    cz = (lo[2] + hi[2]) * 0.5
    d = FAR
    while d >= NEAR - 1e-9:
        if axis == "x":
            x = (hi[0] + d) if sign > 0 else (lo[0] - d)
            z = cz + along * (hi[2] - lo[2])
        else:
            z = (hi[2] + d) if sign > 0 else (lo[2] - d)
            x = cx + along * (hi[0] - lo[0])
        if GROUND.level(x, z, level) and not occupied(x - 0.2, x + 0.2, z - 0.2, z + 0.2,
                                                      ignore, GROUND.at(x, z)):
            return x, GROUND.at(x, z), z
        d -= STEP
    raise AssertionError("no level, unoccupied ground on the %s%s face of %r"
                         % ("+" if sign > 0 else "-", axis, box))


def first_face(box, level, options, ignore=()):
    """First of several faces that has usable ground. Logistics props are not
    plots - the horizontal tank is wedged against the refinery, so its +x side
    is inside a building and the point belongs on the free flank instead."""
    for axis, sign, along in options:
        try:
            return face(box, level, axis, sign, along, ignore)
        except AssertionError:
            continue
    raise AssertionError("no usable face on %r" % (box,))


def snap(x, z, level, radius=1.5):
    """Nearest point to x/z that stands on level ground. Used where a plot's
    own bounding box centre falls in open water - the Port group's box spans
    two disjoint quays with the harbour between them."""
    if GROUND.level(x, z, level):
        return x, GROUND.at(x, z), z
    r = 0.1
    while r <= radius:
        n = max(8, int(r * 40))
        for k in range(n):
            a = 2 * math.pi * k / n
            px, pz = x + r * math.cos(a), z + r * math.sin(a)
            if GROUND.level(px, pz, level):
                return px, GROUND.at(px, pz), pz
        r += 0.1
    raise AssertionError("no ground within %.1fm of (%.2f, %.2f) at y=%.2f"
                         % (radius, x, z, level))


def find_pad(seed, level, reach=3.0, clear=0.1):
    """Largest clear, level, unoccupied rectangle nearest the seed. Sizes are
    tried biggest first and the spiral keeps the pad as close to the seed as
    the map allows, so a bigger pad never wins by landing somewhere silly."""
    for w, d in PADS:
        r = 0.0
        while r <= reach:
            n = 1 if r == 0 else max(8, int(r * 40))
            for k in range(n):
                a = 2 * math.pi * k / n
                cx, cz = seed[0] + r * math.cos(a), seed[1] + r * math.sin(a)
                if occupied(cx - w / 2 - clear, cx + w / 2 + clear,
                            cz - d / 2 - clear, cz + d / 2 + clear, (), level):
                    continue
                grid = [(cx - w / 2 + w * i / 3.0, cz - d / 2 + d * j / 3.0)
                        for i in range(4) for j in range(4)]
                if any(on_road(px, pz) for px, pz in grid):
                    continue
                if all(GROUND.level(px, pz, level, 0.12) for px, pz in grid):
                    return cx, level, cz, w, d
            r += 0.1
    raise AssertionError("no clear pad within %.1fm of %r" % (reach, seed))


# -------------------------------------------------------------- building --

def build():
    anchors = {}
    zones = []

    def put(name, pos, on, note):
        anchors[name] = {"pos": r3(pos), "on": on, "note": note}

    def station_anchors(key, box, level, roof, on, ignore=()):
        lo, hi = box
        cx, cz = (lo[0] + hi[0]) * 0.5, (lo[2] + hi[2]) * 0.5
        put("Station_%s_Work" % key, snap(cx, cz, level), on, "plot centre")
        put("Station_%s_Input" % key, face(box, level, "x", +1, +0.25, ignore), on,
            "+x flank, uphill half - materials arrive here")
        put("Station_%s_Output" % key, face(box, level, "z", -1, 0.0, ignore), on,
            "-z face, downhill - finished equipment leaves here")
        put("Station_%s_Worker" % key, face(box, level, "x", +1, -0.25, ignore), on,
            "+x flank, downhill half - worker idle spot")
        put("Station_%s_Upgrade" % key, (cx, roof + 0.4, cz), on,
            "badge, clear of the roof - not a ground anchor")

    # -------------------------------------------------------- the stations --
    for key, art, seed, seed_y in STATIONS:
        if art:
            lo, hi = aabb(art)
            level = GROUND.at((lo[0] + hi[0]) * 0.5, (lo[2] + hi[2]) * 0.5)
            station_anchors(key, (lo, hi), level, hi[1], art, ignore=(art,))
            zones.append({
                "id": "Station_" + key,
                "art_group": art,
                "hide_when_locked": [art],
                "needs_art": False,
                "pad": {"centre": r3([(lo[0] + hi[0]) * 0.5, level,
                                      (lo[2] + hi[2]) * 0.5]),
                        "size": [round(hi[0] - lo[0], 3), round(hi[2] - lo[2], 3)],
                        "note": "footprint of his building - show the pad while "
                                "the group above is hidden"},
            })
        else:
            px, py, pz, w, d = find_pad(seed, seed_y)
            # No building stands here yet, so these sit ON the pad's own edges
            # rather than in an apron around it - the waterfront gap is not
            # wide enough for an apron, and an empty pad is walkable.
            inset = 0.15
            put("Station_%s_Work" % key, (px, py, pz),
                "IndustrialReference_Map", "pad centre")
            put("Station_%s_Input" % key, (px + w / 2 - inset, py, pz + d / 4),
                "IndustrialReference_Map",
                "pad +x edge, uphill half - materials arrive here")
            put("Station_%s_Output" % key, (px, py, pz - d / 2 + inset),
                "IndustrialReference_Map",
                "pad -z edge, downhill - finished equipment leaves here")
            put("Station_%s_Worker" % key, (px + w / 2 - inset, py, pz - d / 4),
                "IndustrialReference_Map",
                "pad +x edge, downhill half - worker idle spot")
            put("Station_%s_Upgrade" % key, (px, py + 2.2, pz),
                "IndustrialReference_Map",
                "badge, above the future roof - not a ground anchor")
            zones.append({
                "id": "Station_" + key,
                "art_group": None,
                "hide_when_locked": [],
                "needs_art": True,
                "pad": {"centre": r3([px, py, pz]),
                        "size": [round(w, 3), round(d, 3)],
                        "note": "clear level ground - NO ART EXISTS for this "
                                "station, the pad is all there is"},
            })

    # ----------------------------------------------- always-on process zones --
    for zname, gnames in ZONES:
        lo, hi = aabb_many(gnames)
        level = GROUND.at((lo[0] + hi[0]) * 0.5, (lo[2] + hi[2]) * 0.5)
        put("%s_Input" % zname,
            first_face((lo, hi), level,
                       [("x", +1, +0.25), ("x", -1, +0.25), ("z", +1, 0.0)], gnames),
            gnames[0], "uphill half of the first free flank")
        put("%s_Output" % zname,
            first_face((lo, hi), level,
                       [("z", -1, 0.0), ("x", -1, -0.25), ("x", +1, -0.25)], gnames),
            gnames[0], "downhill face")
        zones.append({"id": zname, "art_group": gnames[0],
                      "always_on": gnames, "hide_when_locked": [],
                      "needs_art": False, "pad": None})

    # --------------------------------------------------------------- source --
    lo, hi = aabb("04_Mine/Mine_Portal")
    # The rail runs right past the portal mouth, which is the point - ore
    # leaves the portal onto the wagons - so the rail does not count as an
    # obstruction here.
    put("Mine_Output",
        first_face((lo, hi), GROUND.at((lo[0] + hi[0]) * 0.5, lo[2] - 1.0),
                   [("z", -1, 0.0), ("x", -1, 0.0), ("x", +1, 0.0)],
                   ("04_Mine/Mine_Portal", "04_Mine/Railway")),
        "04_Mine/Mine_Portal", "ore leaves the portal onto the rail")

    lo, hi = aabb("04_Mine/Railway")
    cz = (lo[2] + hi[2]) * 0.5
    put("Train_Load", snap(hi[0] - 0.4, cz, GROUND.at(hi[0] - 0.4, cz)),
        "04_Mine/Railway", "uphill end of the existing rail")
    put("Train_Unload", snap(lo[0] + 0.4, cz, GROUND.at(lo[0] + 0.4, cz)),
        "04_Mine/Railway", "downhill end of the existing rail")

    # ----------------------------------------------------------------- dock --
    # The Port group's bounding box spans the concrete quay AND the road jetty
    # with open water between them, so its centre is at sea. Both dock anchors
    # are measured on the quay deck itself.
    qlo, qhi = aabb("08_Harbor/Port", ("quay",))
    deck = qhi[1]
    lo, hi = aabb("08_Harbor/Crane")
    put("Player_Outfitting", snap((lo[0] + hi[0]) * 0.5, qlo[2] + 0.9, deck),
        "08_Harbor/Port", "quay deck under the crane boom")

    lo, hi = aabb("08_Harbor/Ship")
    put("Set_Sail", snap((lo[0] + hi[0]) * 0.5, qlo[2] + 0.25, deck),
        "08_Harbor/Port", "quay lip alongside the moored ship")

    for i, gname in enumerate(CUSTOMERS, 1):
        lo, hi = aabb(gname, ("plaza",))
        cx = (lo[0] + hi[0]) * 0.5
        put("Customer_Berth_%02d" % i, snap(cx, hi[2] - 0.45, hi[1]), gname,
            "customer plaza at the bridge landing")

    # --------------------------------------------------------------- camera --
    for name, gname in STOPS:
        lo, hi = aabb(gname)
        cx, cz = (lo[0] + hi[0]) * 0.5, (lo[2] + hi[2]) * 0.5
        g = GROUND.at(cx, cz)
        if g is None:                       # Port again - centre is over water
            cx, g, cz = snap(cx, cz, qhi[1], 3.0)
        put(name, (cx, g, cz), gname, "centred on " + gname)

    gl, gh = [1e9] * 3, [-1e9] * 3
    for gname in GROUPS:
        if gname in SEA:
            continue
        lo, hi = aabb(gname)
        for k in range(3):
            gl[k] = min(gl[k], lo[k])
            gh[k] = max(gh[k], hi[k])
    anchors["Camera_Bounds"] = {
        "min": r3(gl), "max": r3(gh), "on": "IndustrialReference_Map",
        "note": "island and customer islands, sea planes excluded",
    }

    global WALKER
    WALKER = Walk((gl, gh))

    out = {
        "space": "unity-world-metres",
        "source": "Tools/blender/IndustrialReference/map_geometry.json",
        "map_prefab": ("Assets/Prefabs/Island/IndustrialReference/"
                       "IndustrialReference_Map.prefab"),
        "generated_by": "Tools/blender/IndustrialReference/build_anchors.py",
        "zones": zones,
        "anchors": anchors,
        "routes": routes(anchors),
    }
    with open(OUT, "w") as fh:
        json.dump(out, fh, indent=2, sort_keys=True)
    return out


# ----------------------------------------------------------------- routes --

def drape(a, b, step=0.35):
    """Straight line from a to b, resampled and dropped onto the ground."""
    n = max(1, int(math.dist((a[0], a[2]), (b[0], b[2])) / step))
    pts = []
    for i in range(n + 1):
        t = i / float(n)
        x = a[0] + (b[0] - a[0]) * t
        z = a[2] + (b[2] - a[2]) * t
        g = GROUND.at(x, z)
        assert g is not None, "route leaves the ground at (%.2f, %.2f)" % (x, z)
        pts.append((x, g + 0.05, z))
    return pts


class Walk(object):
    """Coarse walkable grid over the island, and A* on it.

    A cell is walkable when there is ground under it and nothing of his stands
    on it; a step between neighbours is allowed only when the ground rises less
    than MAX_RISE. That single rule is what makes the routes usable: the
    terrace faces are near vertical and get rejected, so every path between two
    terraces is forced onto his own switchback road, which is the only graded
    connection on the map."""

    CELL = 0.25
    # Rise allowed in one cell step. Off-road that is 0.55, which clears the
    # quay kerb (0.42) and the jetty kerb (0.49) but rejects a terrace face
    # (1.3). On his road it is 1.1: the ramps are stylised and near-vertical in
    # places - the terrace 2 descent drops 0.9m in one cell - and the road is
    # the only graded link between terraces, so refusing to walk it strands
    # every terrace on its own.
    MAX_RISE = 0.55
    ROAD_RISE = 1.1

    def __init__(self, bounds):
        (x0, _, z0), (x1, _, z1) = bounds
        self.x0, self.z0 = x0 - 1.0, z0 - 1.0
        self.nx = int((x1 - x0 + 2.0) / self.CELL) + 1
        self.nz = int((z1 - z0 + 2.0) / self.CELL) + 1
        self.y = {}
        self.road = set()
        for ix in range(self.nx):
            for iz in range(self.nz):
                x, z = self.world(ix, iz)
                g = GROUND.at(x, z)
                if g is None:
                    continue
                # His road is the designed circulation and is never blocked.
                # Bounding boxes overlap it constantly - the refinery slab's
                # box swallows the terrace 3 ramp, pine trunks stand in the
                # terrace 5 ramp - and honouring those boxes severs the island
                # into four unreachable terraces.
                road = on_road(x, z, 0.6)
                if not road and occupied(x - 0.1, x + 0.1, z - 0.1, z + 0.1, (), g):
                    continue
                self.y[(ix, iz)] = g
                if road:
                    self.road.add((ix, iz))

    def world(self, ix, iz):
        return self.x0 + ix * self.CELL, self.z0 + iz * self.CELL

    def cell(self, x, z):
        return (int(round((x - self.x0) / self.CELL)),
                int(round((z - self.z0) / self.CELL)))

    def nearest(self, x, z, reach=2.0):
        c = self.cell(x, z)
        if c in self.y:
            return c
        n = int(reach / self.CELL)
        best, bd = None, 1e9
        for dx in range(-n, n + 1):
            for dz in range(-n, n + 1):
                k = (c[0] + dx, c[1] + dz)
                if k not in self.y:
                    continue
                d = dx * dx + dz * dz
                if d < bd:
                    best, bd = k, d
        assert best, "no walkable cell within %.1fm of (%.2f, %.2f)" % (reach, x, z)
        return best

    def path(self, a, b):
        import heapq
        start, goal = self.nearest(a[0], a[2]), self.nearest(b[0], b[2])
        if start == goal:
            return [start]
        gx, gz = goal

        def hcost(c):
            return (abs(c[0] - gx) + abs(c[1] - gz)) * self.CELL

        open_q = [(hcost(start), 0.0, start)]
        came, cost = {start: None}, {start: 0.0}
        while open_q:
            _, g, cur = heapq.heappop(open_q)
            if cur == goal:
                break
            if g > cost.get(cur, 1e9):
                continue
            cy = self.y[cur]
            for dx, dz in ((1, 0), (-1, 0), (0, 1), (0, -1),
                           (1, 1), (1, -1), (-1, 1), (-1, -1)):
                nxt = (cur[0] + dx, cur[1] + dz)
                ny = self.y.get(nxt)
                if ny is None:
                    continue
                rise = self.MAX_RISE
                if cur in self.road and nxt in self.road:
                    rise = self.ROAD_RISE
                if abs(ny - cy) > rise:
                    continue
                step = self.CELL * (1.4142 if dx and dz else 1.0)
                ng = g + step
                if ng < cost.get(nxt, 1e9):
                    cost[nxt] = ng
                    came[nxt] = cur
                    heapq.heappush(open_q, (ng + hcost(nxt), ng, nxt))
        assert goal in came, "no walkable path between %r and %r" % (a, b)
        out, c = [], goal
        while c is not None:
            out.append(c)
            c = came[c]
        return out[::-1]


def simplify(pts, tol=0.12):
    """Drop points that sit on the straight line between their neighbours, so a
    120-cell path becomes a dozen waypoints. The test is in 3D on purpose: a
    run that is straight in plan can still step off a kerb, and dropping that
    point would let the line cut through the kerb."""
    if len(pts) < 3:
        return list(pts)
    out = [pts[0]]
    for i in range(1, len(pts) - 1):
        a, b, p = out[-1], pts[i + 1], pts[i]
        ab = [b[k] - a[k] for k in range(3)]
        ap = [p[k] - a[k] for k in range(3)]
        n = math.sqrt(sum(v * v for v in ab))
        if n < 1e-9:
            out.append(p)
            continue
        cr = (ab[1] * ap[2] - ab[2] * ap[1],
              ab[2] * ap[0] - ab[0] * ap[2],
              ab[0] * ap[1] - ab[1] * ap[0])
        if math.sqrt(sum(v * v for v in cr)) / n > tol:
            out.append(p)
    out.append(pts[-1])
    return out


def route_between(a, b):
    """Ground path from a to b: A* on the walkable grid, thinned to waypoints,
    with the two real anchor positions as the actual ends."""
    pts = [tuple(a)]
    for ix, iz in WALKER.path(a, b)[1:-1]:
        x, z = WALKER.world(ix, iz)
        pts.append((x, WALKER.y[(ix, iz)] + 0.05, z))
    pts.append(tuple(b))
    return simplify(pts)


def dedupe(pts, eps=0.02):
    out = []
    for p in pts:
        if not out or math.dist(out[-1], p) > eps:
            out.append(p)
    return [r3(p) for p in out]


def routes(a):
    """Basic connected paths. Deliveries run station to station along his own
    road; worker loops circle one plot on its own terrace. Nothing here is
    pathfinding - it is the shortest sensible line, resampled onto the ground,
    for a first playable."""
    r = {}

    def delivery(name, src, dst):
        r["Delivery_" + name] = {
            "from": src, "to": dst, "kind": "delivery",
            "points": dedupe(route_between(a[src]["pos"], a[dst]["pos"])),
        }

    chain = [("Mine_to_Cannon", "Mine_Output", "Station_Cannon_Input"),
             ("Cannon_to_Hull", "Station_Cannon_Output", "Station_Hull_Input"),
             ("Hull_to_Refinery", "Station_Hull_Output", "Refinery_Input"),
             ("Refinery_to_Rigging", "Refinery_Output", "Station_Rigging_Input"),
             ("Rigging_to_Storage", "Station_Rigging_Output", "Storage_Input"),
             ("Storage_to_Navigation", "Storage_Output", "Station_Navigation_Input"),
             ("Navigation_to_Figurehead", "Station_Navigation_Output",
              "Station_Figurehead_Input"),
             ("Figurehead_to_Sail", "Station_Figurehead_Output", "Set_Sail")]
    for name, src, dst in chain:
        delivery(name, src, dst)

    # Customers are reached by sea. His bridges from the jetty to the islands
    # exist, but the moored ship lies across the western one, and a delivery
    # boat is the fiction anyway - so these are open lanes at sea level, not
    # ground paths.
    sea = aabb("01_Parts/Endless_turquoise_sea")[1][1]
    for i in (1, 2, 3):
        src, dst = a["Set_Sail"]["pos"], a["Customer_Berth_%02d" % i]["pos"]
        lane = [tuple(src)]
        for t in (0.25, 0.5, 0.75):
            lane.append((src[0] + (dst[0] - src[0]) * t, sea,
                         src[2] + (dst[2] - src[2]) * t))
        lane.append(tuple(dst))
        r["Sail_to_Berth_%02d" % i] = {
            "from": "Set_Sail", "to": "Customer_Berth_%02d" % i,
            "kind": "sea", "points": dedupe(lane)}

    rail = dedupe(drape(a["Train_Load"]["pos"], a["Train_Unload"]["pos"]))
    rail[0], rail[-1] = a["Train_Load"]["pos"], a["Train_Unload"]["pos"]
    r["Rail_Mine"] = {"from": "Train_Load", "to": "Train_Unload",
                      "kind": "rail", "points": rail}

    # Worker loop: idle spot -> input -> output -> back, pathed round the
    # building rather than through it.
    for key, _, _, _ in STATIONS:
        w = a["Station_%s_Worker" % key]["pos"]
        i = a["Station_%s_Input" % key]["pos"]
        o = a["Station_%s_Output" % key]["pos"]
        pts = (route_between(w, i) + route_between(i, o)[1:]
               + route_between(o, w)[1:])
        r["Worker_" + key] = {"from": "Station_%s_Worker" % key,
                              "to": "Station_%s_Worker" % key, "kind": "worker",
                              "points": dedupe(pts)}
    return r


# ----------------------------------------------------------------- checks --

REQUIRED = (
    ["Mine_Output", "Train_Load", "Train_Unload",
     "Storage_Input", "Storage_Output", "Refinery_Input", "Refinery_Output",
     "Player_Outfitting", "Set_Sail", "Camera_Bounds"]
    + ["Station_%s_%s" % (k, part)
       for k, _, _, _ in STATIONS
       for part in ("Input", "Work", "Output", "Upgrade", "Worker")]
    + ["Customer_Berth_%02d" % i for i in (1, 2, 3)]
    + [n for n, _ in STOPS]
)


def check(out):
    a = out["anchors"]
    missing = [n for n in REQUIRED if n not in a]
    assert not missing, "missing contract anchors: %s" % missing

    # Every ground anchor stands on real, level ground. _Upgrade is a badge
    # floating above a roof and is exempt.
    bad = []
    for name, rec in a.items():
        if "pos" not in rec or name.endswith("_Upgrade"):
            continue
        x, y, z = rec["pos"]
        g = GROUND.at(x, z)
        if g is None:
            bad.append("%s is over water" % name)
        elif abs(g - y) > 0.06:
            bad.append("%s floats/sinks %+.2fm" % (name, g - y))
    assert not bad, "anchors off the ground: %s" % bad

    # No ground anchor inside a building. Camera stops are deliberately on
    # plot centres, and the three rail anchors are deliberately on the rail.
    rail = ("Mine_Output", "Train_Load", "Train_Unload")
    inside = []
    for name, rec in a.items():
        if "pos" not in rec or name.endswith(("_Upgrade", "_Work")):
            continue
        if name.startswith("Camera_Stop") or name in rail:
            continue
        x, y, z = rec["pos"]
        hit = occupied(x - 0.15, x + 0.15, z - 0.15, z + 0.15, (), y)
        if hit:
            inside.append("%s in %s" % (name, hit))
    assert not inside, "anchors inside geometry: %s" % inside

    seen, clash = {}, []
    for name, rec in a.items():
        if "pos" not in rec:
            continue
        k = tuple(rec["pos"])
        if k in seen and not (name.startswith("Camera_Stop")
                              or seen[k].startswith("Camera_Stop")):
            clash.append("%s == %s" % (name, seen[k]))
        seen[k] = name
    assert not clash, "anchors sharing one coordinate: %s" % clash

    # Every land route stays on the ground, ends on the anchors it names, and
    # its interior does not cut through geometry. The road is exempt (his
    # buildings overhang it), and so is the rail route, which is meant to run
    # along the rail and through the crusher that straddles it.
    for name, rec in out["routes"].items():
        if rec["kind"] == "sea":
            continue
        pts = rec["points"]
        assert pts[0] == a[rec["from"]]["pos"], "%s does not start on %s" % (name, rec["from"])
        assert pts[-1] == a[rec["to"]]["pos"], "%s does not end on %s" % (name, rec["to"])
        for i, (x, y, z) in enumerate(pts):
            g = GROUND.at(x, z)
            assert g is not None, "%s leaves the ground at (%.2f, %.2f)" % (name, x, z)
            if rec["kind"] == "rail" or i in (0, len(pts) - 1) or on_road(x, z, 0.6):
                continue
            hit = occupied(x - 0.05, x + 0.05, z - 0.05, z + 0.05, (), y - 0.05)
            assert not hit, "%s runs through %s at (%.2f, %.2f)" % (name, hit, x, z)

    pads = [z for z in out["zones"] if z["needs_art"]]
    print("check: %d anchors on measured ground, %d routes, %d contract names, "
          "%d station(s) with no art" % (len(a), len(out["routes"]),
                                         len(REQUIRED), len(pads)))


if __name__ == "__main__":
    out = build()
    check(out)
    print("wrote %s" % OUT)
