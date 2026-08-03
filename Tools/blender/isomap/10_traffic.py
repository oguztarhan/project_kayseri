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
import grade
importlib.reload(grade)
L = layout
P = parts

purge_collection("Vehicles")
C = coll("Vehicles")

LANE = PK(2.6, 3.0, 3.6)

# These are the bodies the GAMEPLAY layer adopts: 13_export.py renames anything
# named V.ore*/V.cargo* to truck_road_ore<N>/truck_road_cargo<N>, and
# CoalOperation picks its fleet by that tag - an ore truck has to be a tipper and
# a cargo truck a flatbed, whichever road they happen to be parked on.
#
# Both are modelled EMPTY. The gameplay layer parents its own load block to each
# truck and shows it only while the truck is carrying, so a body with coal or a
# box trailer baked in could never look empty - which is what made an ore truck
# still look full on the way back from the refinery.
SRC = {
    "ore": P.truck("V.ore", "orange", "skip", C),
    "ore2": P.truck("V.ore2", "yellow_lt", "skip", C),
    "cargo": P.truck("V.cargo", "white", None, C),
    "cargo2": P.truck("V.cargo2", "blue_lt", None, C),
    "tank": P.truck("V.tank", "steel_lt", "tank", C),
    "van": P.van("V.van", "white", C),
    "van2": P.van("V.van2", "red", C),
}
if PHASE == 1:                       # smaller, scrappier starter fleet
    for k in ("ore", "ore2", "cargo", "cargo2"):
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
        vx = pos.x + nx * side * lane
        vy = pos.y + ny * side * lane
        # Sit on the road and lean with it. A truck left flat on the 14% climb
        # out of the mine reads as parked in mid-air.
        heading = yaw + (pi if flip else 0.0)
        gx = (grade.road_z(vx + 2.0, vy) - grade.road_z(vx - 2.0, vy)) * 0.25
        gy = (grade.road_z(vx, vy + 2.0) - grade.road_z(vx, vy - 2.0)) * 0.25
        tilt = -atan2(gx * cos(heading) + gy * sin(heading), 1.0)
        dup(SRC[kind], (vx, vy, z + grade.road_z(vx, vy)),
            (0, tilt, heading), None, C, "V." + kind)


# Fractions along the two arterials. The old placement used its own hardcoded
# polylines through the loop's former corners at (73, 0) and (0, 73); with the
# ring a circle at 74 and the district spurs gone, every one of those trucks was
# standing in the grass. Derived from the roads themselves now, so they cannot
# drift apart again.
def frac_x(x):
    return (x - L.ROAD_X[0][0]) / (L.ROAD_X[-1][0] - L.ROAD_X[0][0])


def frac_y(y):
    return (y - L.ROAD_Y[0][1]) / (L.ROAD_Y[-1][1] - L.ROAD_Y[0][1])


def spread(a, b, n):
    return [a + (b - a) * (i + 0.5) / n for i in range(n)]


# Counts match CoalOperation's fleet caps exactly - 4 ore (base 2 + 2 upgrade
# levels) and 3 cargo (base 1 + 2) - so every body on the map gets adopted and
# driven. One more of either would stand on the road forever.
# ORE: storage -> crossroads -> refinery, laden south down roadY then east.
for f in spread(frac_y(118), frac_y(20), 2):
    place(L.ROAD_Y, [f], "ore", side=-1, flip=True)
for f in spread(frac_x(20), frac_x(118), 2):
    place(L.ROAD_X, [f], "ore2", side=-1)
# CARGO: refinery -> crossroads -> market, laden west then south.
for f in spread(frac_x(112), frac_x(16), 2):
    place(L.ROAD_X, [f], "cargo", side=1, flip=True)
place(L.ROAD_Y, [frac_y(-64)], "cargo2", side=1, flip=True)

# light background traffic - Blender renders only, 13_export drops these
place(L.ROAD_X, PK([0.14], [0.14, 0.30], [0.10, 0.22, 0.34]), "van", side=1,
      flip=True)
place(L.ROAD_Y, PK([0.90], [0.88, 0.94], [0.86, 0.92, 0.96]), "van2", side=-1)
if PHASE >= 3:
    place(L.ROAD_X, [frac_x(-60)], "tank", side=-1)
    place(L.LOOP_C, [0.12, 0.38, 0.64, 0.88], "van", side=-1, lane=LANE - 0.6)
elif PHASE == 2:
    place(L.LOOP_C, [0.22, 0.72], "van", side=-1, lane=LANE - 0.6)

print("traffic ok", stats(), "phase", PHASE)
