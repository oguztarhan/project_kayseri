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

UNITY = "/Users/macbookair/Documents/GitHub/project_kayseri"
OUT = UNITY + "/Assets/Art/KayseriIsland/Routes"
os.makedirs(OUT, exist_ok=True)

ROAD_Y = 0.10   # Z_ROAD in 03_roads.py
RAIL_Y = 1.9    # RZ in 04_rail.py - railhead the train body rides on


def pt(x, y, h):
    """Blender (x, y) -> Unity (x, y, z). See module docstring."""
    return {"x": round(-float(x), 4), "y": round(float(h), 4),
            "z": round(-float(y), 4)}


def anchor(p, h=ROAD_Y):
    return pt(p[0], p[1], h)


def road_path(pts, h=ROAD_Y):
    """Centreline as strip() lays it: same Catmull-Rom sampling, same count."""
    p3 = [(p[0], p[1], 0.0) for p in pts]
    samples = sample_bez(p3, max(8, len(p3) * 10))
    return [pt(pos.x, pos.y, h) for pos, _ in samples]


# ------------------------------------------------------------------ anchors
# Flat name/pos lists rather than objects keyed by name: Unity's JsonUtility
# cannot deserialise dictionaries.
anchors = [
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
    rail = [pt(p[0], p[1], RAIL_Y) for p in laid]
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

rail_port = []
if PHASE >= 3:
    pp = [(p[0], p[1], 0.0) for p in L.RAIL_PORT]
    rail_port = [pt(pos.x, pos.y, RAIL_Y) for pos, _ in sample_bez(pp, 220)][::STEP]

# ------------------------------------------------------------------ roads
# Site spurs only exist once their site has unlocked, matching 03_roads.py.
SITE_SPUR = {"Spur.Quarry": "quarry", "Spur.Store": "store", "Spur.Plant": "plant"}
# One flat, name-keyed path list - Unity looks a route up by name. "loop" is the
# only closed one; everything else is an out-and-back run.
paths = [
    {"name": "loop", "closed": True, "points": road_path(L.LOOP_C)},
    {"name": "roadX", "closed": False, "points": road_path(L.ROAD_X)},
    {"name": "roadY", "closed": False, "points": road_path(L.ROAD_Y)},
    {"name": "portRoad", "closed": False, "points": road_path(L.PORT_ROAD)},
    {"name": "rail", "closed": False, "points": rail_thin},
    {"name": "shipLane", "closed": False, "points": road_path(L.SHIP_LANE, 0.0)},
]
if rail_port:
    paths.append({"name": "railPort", "closed": False, "points": rail_port})

for pts, name in L.SPURS:
    site = SITE_SPUR.get(name)
    if site is not None and site not in active:
        continue
    paths.append({"name": name, "closed": False, "points": road_path(pts)})

data = {
    "phase": PHASE,
    "roadHeight": ROAD_Y,
    "railHeight": RAIL_Y,
    "roadWidth": L.ROAD_W,
    "districtRadius": L.R,
    "activeSites": active,
    "anchors": anchors,
    "paths": paths,
}

dst = "%s/island_routes_P%d.json" % (OUT, PHASE)
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
