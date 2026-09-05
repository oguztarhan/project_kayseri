"""Authored-route export for the IndustrialReference map.

CoalOperation has an authored-island path: when `authoredRoutes` is assigned it
skips its whole runtime layout pass and instead reads this file, creating the
landmark objects it needs from the anchors here (see
CoalOperation.PrepareAuthoredIsland). So this file, not scene wiring, is what
makes his map playable.

    python3 build_routes.py

Reads  map_geometry.json (his map, untouched) and anchors.json (measured on it).
Writes Assets/Art/KayseriIsland/Routes/industrial_routes_P1.json.

SCALE
His map is 19 x 34 metres. Island_Coal is 2827 x 2862, and every tuned constant
in CoalOperation is in those metres - trainSpeed 18, tunnelMouthGap 70,
maxRailLift 8. At 1:1 a train crosses his whole island in under two seconds, so
everything here is written at SCALE metres.

The island root stays at scale 1 and the map art is a SCALE-sized child of it.
IslandRoutes.Rebase runs every coordinate through islandRoot.TransformPoint, so
a scaled root would scale these too and double the factor; and the file's scalar
fields (roadWidth, railHeight, districtRadius) are not transformed at all, so a
scaled root would leave them in the wrong units. Baking the scale here keeps one
consistent space.
"""

import json
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
GEOMETRY = os.path.join(HERE, "map_geometry.json")
ANCHORS = os.path.join(HERE, "anchors.json")
OUT = os.path.join(HERE, "..", "..", "..", "Assets", "Art", "KayseriIsland",
                   "Routes", "industrial_routes_P1.json")

SCALE = 85.0

# Route-anchor name -> the anchor in anchors.json that locates it. The left-hand
# names are CoalOperation's vocabulary and cannot change; the right-hand ones are
# the contract manifest measured on his buildings.
ANCHOR_MAP = {
    "mine":      "Mine_Output",
    "depot":     "Station_Navigation_Work",   # the Warehouse plot, which is Storage
    "refinery":  "Station_Cannon_Work",       # the Smelting_Plant plot, which is Refinery
    "market":    "Station_Figurehead_Work",   # the Port plot
    "port":      "Player_Outfitting",
    "shipOut":   "Set_Sail",
    "railShed":  "Train_Load",
    "center":    "Station_Hull_Work",         # Blue_Factory, the middle of the spine
    "power":     "Station_Rigging_Work",
    "haul":      "Train_Unload",
    "civic":     "Customer_Berth_02",
    "fleet":     "Customer_Berth_01",
}

# Road and rail geometry to trace, as (route path name, group name, closed).
# CoalOperation reads TWO arterials, Arteries = { "roadX", "roadY" }, and picks whichever
# passes nearer a district (see CoalOperation.Artery). His island has only one: the three
# Main_road_* groups are one layered ribbon - ochre foundation, golden ribbon and bright
# centre stacked on the same spine - and both Factory_* groups are the same service loop.
# So both arterial names are given that spine. That is not a fudge, it is what the map is:
# every district stands on the one road, so "whichever passes nearer" is the same answer
# either way. Omitting roadX made AuthoredCircuit fail and every truck fall back to
# straight-line runs across open ground.
ROADS = [
    ("loop",     "03_Parts/Factory_circular_service_road",    True),
    ("roadY",    "03_Parts/Main_road_ochre_foundation",       False),
    ("roadX",    "03_Parts/Main_road_bright_center",          False),
    ("footpath", "03_Parts/Main_road_luminous_golden_ribbon", True),
    ("portRoad", "03_Parts/Customer_connection",              False),
]


def load():
    with open(GEOMETRY) as fh:
        geo = json.load(fh)
    with open(ANCHORS) as fh:
        anc = json.load(fh)
    return geo, anc, {g["name"]: g for g in geo["groups"]}


def world_points(geo, group):
    """Every vertex of a group in map world space."""
    gp = group["position"]
    out = []
    for p in group["parts"]:
        pp = p["position"]
        v = geo["meshes"][p["mesh"]]["vertices"]
        for i in range(0, len(v), 3):
            out.append((gp[0] + pp[0] + v[i],
                        gp[1] + pp[1] + v[i + 1],
                        gp[2] + pp[2] + v[i + 2]))
    return out


def centreline(pts, closed, samples=48):
    """Trace a ribbon mesh's centreline.

    A road here is a flat ribbon, so slicing it across its own long axis and
    averaging each slice recovers the line it was drawn along. A closed loop has
    no long axis - every slice would fold two opposite sides of the ring onto one
    another - so those are traced by angle about the centroid instead.
    """
    if not pts:
        return []

    cx = sum(p[0] for p in pts) / len(pts)
    cz = sum(p[2] for p in pts) / len(pts)

    if closed:
        buckets = [[] for _ in range(samples)]
        for p in pts:
            a = math.atan2(p[2] - cz, p[0] - cx)
            buckets[int((a + math.pi) / (2 * math.pi) * samples) % samples].append(p)
        line = []
        for b in buckets:
            if b:
                line.append((sum(q[0] for q in b) / len(b),
                             max(q[1] for q in b),
                             sum(q[2] for q in b) / len(b)))
        return line

    xs = [p[0] for p in pts]
    zs = [p[2] for p in pts]
    along = 0 if (max(xs) - min(xs)) >= (max(zs) - min(zs)) else 2
    lo, hi = min(p[along] for p in pts), max(p[along] for p in pts)
    if hi - lo < 1e-6:
        return []

    buckets = [[] for _ in range(samples)]
    for p in pts:
        k = int((p[along] - lo) / (hi - lo) * (samples - 1))
        buckets[k].append(p)
    line = []
    for b in buckets:
        if b:
            line.append((sum(q[0] for q in b) / len(b),
                         max(q[1] for q in b),          # ride the top face, not the underside
                         sum(q[2] for q in b) / len(b)))
    return line


def vec(p):
    return {"x": round(p[0] * SCALE, 4),
            "y": round(p[1] * SCALE, 4),
            "z": round(p[2] * SCALE, 4)}


def build():
    geo, anc, groups = load()
    A = anc["anchors"]

    anchors = []
    for route_name, contract_name in ANCHOR_MAP.items():
        rec = A.get(contract_name)
        if rec is None or "pos" not in rec:
            print("  missing contract anchor %s for %s" % (contract_name, route_name))
            continue
        anchors.append({"name": route_name, "pos": vec(rec["pos"])})

    paths = []
    for name, gname, closed in ROADS:
        g = groups.get(gname)
        if g is None:
            print("  missing road group " + gname)
            continue
        line = centreline(world_points(geo, g), closed)
        if len(line) < 2:
            print("  could not trace " + gname)
            continue
        paths.append({"name": name, "closed": closed,
                      "width": 0.0 if name == "footpath" else round(8.0, 2),
                      "points": [vec(p) for p in line]})

    # Rail is 20 separate sleeper/track pieces rather than one ribbon, so it is
    # traced from the pieces' own centres along the run instead of by slicing.
    rail = groups.get("04_Mine/Railway")
    if rail is not None:
        pts = world_points(geo, rail)
        line = centreline(pts, False, samples=32)
        if len(line) >= 2:
            paths.append({"name": "rail", "closed": False, "width": 0.0,
                          "points": [vec(p) for p in line]})

    # The four points where the ring road meets an arterial. CoalOperation.RingMeet picks the
    # nearest of these as the junction a district turns off at, and returns false when none of
    # them exist - which made AuthoredCircuit fail and every truck fall back to straight-line
    # runs across open ground. They are the compass extremes of the traced ring itself, so they
    # sit on real tarmac rather than being guessed.
    ring = next((p for p in paths if p["name"] == "loop"), None)
    if ring is not None and len(ring["points"]) >= 4:
        pts = ring["points"]
        for nm, key in (("loopN", lambda q: -q["z"]), ("loopS", lambda q: q["z"]),
                        ("loopE", lambda q: -q["x"]), ("loopW", lambda q: q["x"])):
            anchors.append({"name": nm, "pos": dict(min(pts, key=key))})

    # railShed must sit ON the traced rail, not merely near it: CoalOperation measures the
    # distance and warns that the route file and the track are from different builds. Snap
    # it to the nearest point of the rail we just traced.
    railpath = next((p for p in paths if p["name"] == "rail"), None)
    if railpath is not None:
        shed = next((a for a in anchors if a["name"] == "railShed"), None)
        if shed is not None:
            sx, sz = shed["pos"]["x"], shed["pos"]["z"]
            best = min(railpath["points"],
                       key=lambda q: (q["x"] - sx) ** 2 + (q["z"] - sz) ** 2)
            shed["pos"] = dict(best)

    # shipLane: the moored ship out to open water, straight.
    ship = A.get("Set_Sail")
    if ship:
        p = ship["pos"]
        paths.append({"name": "shipLane", "closed": False, "width": 0.0,
                      "points": [vec(p), vec((p[0], p[1], p[2] - 6.0))]})

    out = {
        "phase": 1,
        "roadHeight": round(0.1 * SCALE, 3),
        "railHeight": round(0.06 * SCALE, 3),
        "roadWidth": round(0.10 * SCALE, 3),
        "districtRadius": round(1.5 * SCALE, 2),
        "activeSites": [],
        "anchors": anchors,
        "paths": paths,
    }
    os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
    with open(os.path.abspath(OUT), "w") as fh:
        json.dump(out, fh, indent=1)
    return out


REQUIRED_ANCHORS = ("mine", "depot", "refinery", "market", "center", "port", "railShed",
                    "loopN", "loopE", "loopS", "loopW")
REQUIRED_PATHS = ("rail", "portRoad", "loop", "footpath")


def check(out):
    have_a = {a["name"] for a in out["anchors"]}
    have_p = {p["name"] for p in out["paths"]}
    miss_a = [n for n in REQUIRED_ANCHORS if n not in have_a]
    miss_p = [n for n in REQUIRED_PATHS if n not in have_p]
    assert not miss_a, "CoalOperation reads these anchors: missing %s" % miss_a
    assert not miss_p, "CoalOperation reads these paths: missing %s" % miss_p

    # A landmark dropped at the origin means the mapping silently failed; the
    # operation would disable itself at runtime with only a vague warning.
    for a in out["anchors"]:
        p = a["pos"]
        assert abs(p["x"]) + abs(p["z"]) > 1e-3, "anchor %s sits at the origin" % a["name"]

    for p in out["paths"]:
        assert len(p["points"]) >= 2, "path %s has %d points" % (p["name"], len(p["points"]))
    print("check: %d anchors, %d paths, all required names present"
          % (len(out["anchors"]), len(out["paths"])))


if __name__ == "__main__":
    o = build()
    check(o)
    print("wrote %s" % os.path.normpath(os.path.abspath(OUT)))
    for p in o["paths"]:
        print("  path %-10s %3d pts" % (p["name"], len(p["points"])))
