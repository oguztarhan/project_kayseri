"""Step 14: export gameplay route paths and district anchors as JSON.

Must run AFTER build(phase) in the same session - the rail centreline is taken
from what 04_rail.py actually laid, which depends on a raycast against the built
terrain.

The FBX exporter in 13_export.py is mesh-only (`object_types={'MESH'}`, and it
skips anything without polygons), so empties authored here would be dropped
silently. Route data therefore travels beside the FBX as JSON instead of inside
it - same guarantee that it regenerates with the map, no invisible marker meshes
in the art.

Coordinates are written in UNITY space. The FBX export uses axis_up='Y' with
axis_forward='-Z', mapping Blender (bx, by, bz) to Unity (-bx, bz, -by).
Verified against the built map: Road.Loop sits at Blender (3.69, 3.69) and lands
in Unity at (-3.69, -3.69).

Paths are sampled through the same sample_bez() the mesh builder uses, so an
exported centreline lands on the visible road rather than on the straight line
between its control points - the smoothed loop bulges ~18 units past its corners.
"""
import json
import os
import importlib
import layout
importlib.reload(layout)
L = layout
import grade
importlib.reload(grade)

UNITY = "/Users/macbookair/Documents/GitHub/project_kayseri"
OUT = UNITY + "/Assets/Art/KayseriIsland/Routes"
os.makedirs(OUT, exist_ok=True)

ROAD_Y = 0.10   # Z_ROAD in 03_roads.py
# Where the ROLLING STOCK's origin sits, not the railhead. 04_rail.py parks the
# loco and wagons at RZ - 0.85, so exporting the railhead (RZ = 1.9) handed Unity
# a path 0.85 above the models' own datum and the whole train rode sunk into the
# ballast - by 0.85 at the depot and 3.9 up at the mine, once the grade was in.
RAIL_Y = 1.05

# Same widths 03_roads.py lays, so Unity can know the real drivable corridor.
# The old top-level roadWidth exported layout.ROAD_W unscaled for every phase,
# over-reporting the phase-1 carriageway by 61%.
MAIN_W = PK(L.ROAD_W * 0.62, L.ROAD_W * 0.86, L.ROAD_W)
LOOP_W = PK(8.0, 10.0, 12.0)
SPUR_W = PK(7.0, 9.0, 10.5)
PORT_W = PK(8.0, 10.0, 12.0)


def pt(x, y, h):
    """Blender (x, y) -> Unity (x, y, z). See module docstring."""
    return {"x": round(-float(x), 4), "y": round(float(h), 4),
            "z": round(-float(y), 4)}


def anchor(p, h=ROAD_Y):
    return pt(p[0], p[1], h + grade.road_z(p[0], p[1]))


def road_path(pts, h=ROAD_Y, graded=True):
    """Centreline as strip() lays it: same Catmull-Rom sampling, same count.

    Height is sampled at the SAME sample_bez() points the mesh builder uses, so
    an exported centreline lands on the visible road rather than beside it - the
    same guarantee the XY sampling already had.
    """
    p3 = [(p[0], p[1], 0.0) for p in pts]
    samples = sample_bez(p3, max(8, len(p3) * 10))
    if not graded:
        return [pt(pos.x, pos.y, h) for pos, _ in samples]
    return [pt(pos.x, pos.y, h + grade.road_z(pos.x, pos.y)) for pos, _ in samples]


# ------------------------------------------------------------------ anchors
# Flat name/pos lists rather than objects keyed by name: Unity's JsonUtility
# cannot deserialise dictionaries.
anchors = [
    {"name": "power", "pos": anchor(L.TOWN_POWER)},
    {"name": "haul", "pos": anchor(L.TOWN_HAUL)},
    {"name": "fleet", "pos": anchor(L.TOWN_FLEET)},
    {"name": "civic", "pos": anchor(L.TOWN_CIVIC)},
    # Where the ring road meets each arterial. Unity's truck routes turn here,
    # and with the ring a circle these are exact rather than nearest-vertex.
    {"name": "loopE", "pos": anchor((L.LOOP_R, 0.0))},
    {"name": "loopW", "pos": anchor((-L.LOOP_R, 0.0))},
    {"name": "loopN", "pos": anchor((0.0, L.LOOP_R))},
    {"name": "loopS", "pos": anchor((0.0, -L.LOOP_R))},
    {"name": "mine", "pos": anchor(L.MINE)},
    {"name": "depot", "pos": anchor(L.DEPOT)},
    {"name": "refinery", "pos": anchor(L.REFINERY)},
    {"name": "market", "pos": anchor(L.MARKET)},
    {"name": "center", "pos": anchor(L.CENTER)},
    {"name": "port", "pos": anchor(L.PORT)},
    {"name": "shipOut", "pos": anchor(L.SHIP_OUT)},
]
for name, p, need in L.SITES:
    anchors.append({"name": "site_" + name, "pos": anchor(p)})

active = [n for (n, p) in L.active_sites(PHASE)]

# ------------------------------------------------------------------- rail
# Taken from the laid track, not from layout.RAIL - see 04_rail.py.
laid = bpy.context.scene.get("rail_centreline")
if laid:
    rail = [pt(p[0], p[1], RAIL_Y + grade.road_z(p[0], p[1])) for p in laid]
else:
    print("   WARNING: no rail_centreline on the scene - run build(phase) first;"
          " falling back to the untrimmed layout path")
    rail = road_path(L.RAIL, RAIL_Y)

# Thin the 500-odd samples down to something a path follower can walk cheaply
# while still tracing the arc. Endpoints are always kept.
STEP = 6
rail_thin = rail[::STEP]
if rail and rail[-1] != rail_thin[-1]:
    rail_thin.append(rail[-1])

# The shed doorway at the storage end, on the road the train runs on. Unity
# turns it into a distance along the rail and hides each car as it passes, so
# the train is swallowed a wagon at a time instead of blinking out.
_door = bpy.context.scene.get("rail_shed_door")
if _door:
    anchors.append({"name": "railShed",
                    "pos": pt(_door[0], _door[1],
                              RAIL_Y + grade.road_z(_door[0], _door[1]))})
else:
    print("   WARNING: no rail_shed_door on the scene - run build(phase) first")

rail_port = []
if PHASE >= 3:
    pp = [(p[0], p[1], 0.0) for p in L.RAIL_PORT]
    rail_port = [pt(pos.x, pos.y, RAIL_Y + grade.road_z(pos.x, pos.y))
                 for pos, _ in sample_bez(pp, 220)][::STEP]

# ------------------------------------------------------------------ roads
# Site spurs only exist once their site has unlocked, matching 03_roads.py.
SITE_SPUR = {"Spur.Quarry": "quarry", "Spur.Store": "store", "Spur.Plant": "plant"}
# One flat, name-keyed path list - Unity looks a route up by name. "loop" is the
# only closed one; everything else is an out-and-back run.
paths = [
    {"name": "loop", "closed": True, "width": LOOP_W, "points": road_path(L.LOOP_C)},
    {"name": "roadX", "closed": False, "width": MAIN_W, "points": road_path(L.ROAD_X)},
    {"name": "roadY", "closed": False, "width": MAIN_W, "points": road_path(L.ROAD_Y)},
    {"name": "portRoad", "closed": False, "width": PORT_W,
     "points": road_path(L.PORT_ROAD)},
    {"name": "rail", "closed": False, "width": 0.0, "points": rail_thin},
    # Pavement circuit the site crew walks. Width 0: it is not drivable.
    {"name": "footpath", "closed": True, "width": 0.0,
     "points": road_path(L.FOOTPATH, 0.16)},
    # Ships ride the waterline, not the road grade.
    {"name": "shipLane", "closed": False, "width": 0.0,
     "points": road_path(L.SHIP_LANE, L.SEA_Z + 0.15, graded=False)},
]
if rail_port:
    paths.append({"name": "railPort", "closed": False, "width": 0.0,
                  "points": rail_port})

for pts, name in L.SPURS:
    site = SITE_SPUR.get(name)
    if site is not None and site not in active:
        continue
    paths.append({"name": name, "closed": False, "width": SPUR_W,
                  "points": road_path(pts)})

data = {
    "phase": PHASE,
    "roadHeight": ROAD_Y,
    "railHeight": RAIL_Y,
    "roadWidth": MAIN_W,          # phase-scaled, matching MAIN_W in 03_roads.py
    "districtRadius": L.R,
    "activeSites": active,
    "anchors": anchors,
    "paths": paths,
}

dst = "%s/%s_routes_P%d.json" % (OUT, L.NAME, PHASE)
with open(dst, "w") as f:
    json.dump(data, f, indent=1)

lp = next(p for p in paths if p["name"] == "loop")["points"]
print("phase %d routes -> %s" % (PHASE, dst))
print("   anchors %d  paths %d  rail %d pts (of %d laid)  sites %s"
      % (len(anchors), len(paths), len(rail_thin), len(rail), active or "none"))
print("   names: %s" % ", ".join(p["name"] for p in paths))
print("   loop x %.1f..%.1f  centre %.2f   rail x %.1f..%.1f"
      % (min(p["x"] for p in lp), max(p["x"] for p in lp),
         (min(p["x"] for p in lp) + max(p["x"] for p in lp)) / 2.0,
         min(p["x"] for p in rail_thin), max(p["x"] for p in rail_thin)))
# Heights must VARY now. A flat 0.1 everywhere means grade.road_z never reached
# the export and every vehicle would drive through the hills.
_ys = [q["y"] for p in paths for q in p["points"]]
print("   height y %.2f..%.2f across %d points; anchors %.2f..%.2f"
      % (min(_ys), max(_ys), len(_ys),
         min(a["pos"]["y"] for a in anchors), max(a["pos"]["y"] for a in anchors)))
