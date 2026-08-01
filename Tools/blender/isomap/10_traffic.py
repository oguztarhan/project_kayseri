"""Step 10: vehicles in transit - the whole chain moving, deliberately sparse.

    coal train    mine -> depot        (rail)
    ore trucks    depot -> refinery
    cargo trucks  refinery -> market
    port trucks   market -> quay -> ships
"""
import importlib
import layout
import parts
importlib.reload(layout)
importlib.reload(parts)
L = layout
P = parts

purge_collection("Vehicles")
C = coll("Vehicles")

LANE = PK(2.6, 3.0, 3.6)

SRC = {
    "ore": P.truck("V.ore", "orange", "coal", C),
    "ore2": P.truck("V.ore2", "yellow_lt", "coal", C),
    "cargo": P.truck("V.cargo", "white", "cargo", C),
    "cargo2": P.truck("V.cargo2", "blue_lt", "cargo", C),
    "empty": P.truck("V.empty", "orange", None, C),
    "tank": P.truck("V.tank", "steel_lt", "tank", C),
    "van": P.van("V.van", "white", C),
    "van2": P.van("V.van2", "red", C),
}
if PHASE == 1:                       # smaller, scrappier starter fleet
    for k in ("ore", "ore2", "cargo", "cargo2", "empty"):
        SRC[k].scale = (0.62, 0.62, 0.62)
for o in SRC.values():
    o.hide_render = o.hide_viewport = True


def place(path, fracs, kind, side=1, z=0.30, flip=False, lane=None):
    lane = LANE if lane is None else lane
    pts = [(p[0], p[1], 0.0) for p in path]
    S = sample_bez(pts, 400)
    for f in fracs:
        i = max(0, min(len(S) - 1, int(f * (len(S) - 1))))
        pos, yaw = S[i]
        nx, ny = -sin(yaw), cos(yaw)
        dup(SRC[kind], (pos.x + nx * side * lane, pos.y + ny * side * lane, z),
            (0, 0, yaw + (pi if flip else 0.0)), None, C, "V." + kind)


ORE_ROUTE = [(0, 102), (0, 84), (0, 73), (24, 70), (50, 58), (66, 40),
             (73, 18), (73, 0), (92, 0), (112, 0)]
CARGO_ROUTE = [(112, 0), (92, 0), (73, 0), (70, -26), (58, -50), (40, -66),
               (18, -73), (0, -73), (0, -92), (0, -108)]
PORT_ROUTE = [(0, -134), (-14, -140), (-30, -146), (-46, -150), (-56, -154)]

place(ORE_ROUTE, PK([0.30], [0.14, 0.52], [0.10, 0.40, 0.72]), "ore", side=-1)
place(ORE_ROUTE, PK([], [0.78], [0.26, 0.58]), "ore2", side=-1)
place(ORE_ROUTE, PK([], [0.36], [0.30, 0.66]), "empty", side=1, flip=True)

place(CARGO_ROUTE, PK([0.42], [0.20, 0.60], [0.14, 0.44, 0.76]), "cargo", side=-1)
place(CARGO_ROUTE, PK([], [0.84], [0.30, 0.62]), "cargo2", side=-1)
place(CARGO_ROUTE, PK([], [0.48], [0.36, 0.70]), "empty", side=1, flip=True)

place(PORT_ROUTE, PK([0.45], [0.28, 0.72], [0.20, 0.52, 0.84]), "cargo", side=-1)
place(PORT_ROUTE, PK([], [0.55], [0.40, 0.76]), "empty", side=1, flip=True)

# light background traffic on the arterials
place(L.ROAD_X, PK([0.30], [0.22, 0.74], [0.18, 0.50, 0.82]), "cargo2", side=-1)
place(L.ROAD_Y, PK([0.62], [0.34, 0.80], [0.26, 0.58, 0.86]), "ore", side=-1)
place(L.ROAD_X, PK([], [0.58], [0.36, 0.68]), "van", side=1, flip=True)
place(L.ROAD_Y, PK([], [0.46], [0.42, 0.74]), "van2", side=1, flip=True)
if PHASE >= 3:
    place(L.ROAD_X, [0.44], "tank", side=-1)
    place(L.LOOP_C, [0.12, 0.38, 0.64, 0.88], "van", side=-1, lane=LANE - 0.6)
elif PHASE == 2:
    place(L.LOOP_C, [0.22, 0.72], "van", side=-1, lane=LANE - 0.6)

# haul road out of the pit
place([(-102, 0), (-90, 0), (-78, 4), (-73, 22)],
      PK([0.40], [0.25, 0.70], [0.18, 0.52, 0.84]), "ore2", side=-1)

print("traffic ok", stats(), "phase", PHASE)
