"""Put each island's exported rail centreline back on the track that was laid.

Run it from the repo root with plain python - no Blender, no Unity:

    python3 Tools/blender/fit_rail_routes.py            # report only
    python3 Tools/blender/fit_rail_routes.py --write    # rewrite the route files

WHY THIS EXISTS
---------------
`isomap/14_routes.py` writes the rail centreline from the scene property
`rail_centreline`, which `isomap/04_rail.py` sets from the samples it lays the
track on. That is only true while the two are exported from the same Blender
session. They were not: the commit that added `_reach_yard`/`_trim_at_yard`/
`_straighten_tail` to 04_rail.py re-exported every island's FBX and left the
route files alone, so the line the trains drive is the pre-trim curve while the
rails under them are the trimmed one. On coal the two part company by 57 m -
the train runs off the rails, straight across the depot yard, and only vanishes
once it is well past the shed.

Rather than re-run the generator (which would regenerate the art too, and could
drift again), this measures the RAILS THEMSELVES out of the exported FBX and
writes the centreline back from them. The exported art is the thing the player
sees, so deriving the driving line from it cannot disagree with it.

WHAT IT REWRITES
----------------
* the "rail" path - the two running rails the locomotive uses, averaged, at the
  rolling stock's own datum (14_routes.RAIL_Y above the graded surface).
* the "railShed" anchor - the shed doorway, 04_rail.SHED_L back from the
  railhead, which is where CoalOperation stops drawing the train.

Nothing else in the file is touched, and the output is `json.dump(indent=1)`
exactly like the exporter's, so the diff is only the two things above.
"""
import json
import math
import os
import struct
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MODELS = os.path.join(ROOT, "Assets/Art/KayseriIsland/Models")
ROUTES = os.path.join(ROOT, "Assets/Art/KayseriIsland/Routes")

# All eight, in ladder order. Silver re-exports the copper map, ruby the iron, emerald
# the coal and diamond the gold - identical rail, so identical drift to fit.
ISLANDS = ("Coal", "Copper", "Iron", "Silver", "Gold", "Ruby", "Emerald", "Diamond")
PHASES = (1, 2, 3)

# Constants the generator lays the track with. Keep these in step with
# isomap/04_rail.py and isomap/14_routes.py.
RZ = 1.9                                    # 04_rail.RZ, the railhead datum
RAIL_SURFACE = RZ - 0.28                    # z the rail strips are placed at
RAIL_Y = 1.05                               # 14_routes.RAIL_Y, the loco's own datum
GAUGE = 1.72                                # half the distance between two rails
SHED_L = 12.0                               # 04_rail.SHED_L, depth of the engine shed
BED_W = {1: 6.6, 2: 8.2, 3: 10.0}           # 04_rail.lay_track's bed widths
TRAIN_OFF = {1: 0.0, 2: 0.0, 3: -3.4}       # 04_rail.TRAIN_OFF - the road the loco runs
STEP = 4.0                                  # spacing of the written waypoints
CHORD = 0.15                                # how far the thinned line may cut a curve


# --------------------------------------------------------------- binary FBX
# Only enough of the format to walk the node tree and pull Geometry/Vertices.

def _array(f, kind):
    length, encoding, clen = struct.unpack("<III", f.read(12))
    raw = f.read(clen)
    if encoding == 1:
        raw = zlib.decompress(raw)
    return struct.unpack("<%d%s" % (length, {b'f': 'f', b'd': 'd', b'l': 'q',
                                             b'i': 'i', b'b': 'b'}[kind]), raw)


def _prop(f):
    t = f.read(1)
    if t == b'Y':
        return struct.unpack("<h", f.read(2))[0]
    if t == b'C':
        return struct.unpack("<?", f.read(1))[0]
    if t == b'I':
        return struct.unpack("<i", f.read(4))[0]
    if t == b'F':
        return struct.unpack("<f", f.read(4))[0]
    if t == b'D':
        return struct.unpack("<d", f.read(8))[0]
    if t == b'L':
        return struct.unpack("<q", f.read(8))[0]
    if t in (b'f', b'd', b'l', b'i', b'b'):
        return _array(f, t)
    if t in (b'S', b'R'):
        return f.read(struct.unpack("<I", f.read(4))[0])
    raise ValueError("unknown FBX property type %r" % t)


def _node(f, wide):
    end, nprops, _plen = struct.unpack("<QQQ" if wide else "<III",
                                       f.read(24 if wide else 12))
    namelen = struct.unpack("<B", f.read(1))[0]
    if end == 0:
        return None
    name = f.read(namelen).decode("utf-8", "replace")
    props = [_prop(f) for _ in range(nprops)]
    kids = []
    while f.tell() < end:
        k = _node(f, wide)
        if k is None:
            break
        kids.append(k)
    f.seek(end)
    return (name, props, kids)


def fbx_meshes(path):
    """{ object name: [ (x, y, z), ... ] } for every mesh in an FBX, Blender space."""
    with open(path, "rb") as f:
        f.seek(23)
        wide = struct.unpack("<I", f.read(4))[0] >= 7500
        size = f.seek(0, 2)
        f.seek(27)
        roots = []
        while f.tell() < size - 160:
            n = _node(f, wide)
            if n is None:
                break
            roots.append(n)

    out = {}
    for name, _props, kids in roots:
        if name != "Objects":
            continue
        for gname, gprops, gkids in kids:
            if gname != "Geometry":
                continue
            label = gprops[1].decode("utf-8", "replace").split("\x00")[0]
            for vname, vprops, _ in gkids:
                if vname != "Vertices":
                    continue
                a = vprops[0]
                out.setdefault(label, []).append(
                    [(a[i], a[i + 1], a[i + 2]) for i in range(0, len(a), 3)])
    return out


# ------------------------------------------------------------------ geometry

def parts(meshes, want):
    """Every mesh whose name is "<group>.<want>[.NNN]", in name order."""
    got = []
    for name in sorted(meshes):
        bits = name.split(".")
        if len(bits) >= 2 and bits[1] == want:
            for vs in meshes[name]:
                got.append(vs)
    return got


def ribbon(vs):
    """The centre of one strip() ribbon, row by row along the path.

    strip() lays two vertices per sample, row-major, and the SOLIDIFY modifier on
    the rails doubles that into a second shell. The lower of the two shells is the
    surface strip() actually placed, so the height comes from there - taking the
    upper one puts the whole line 0.42 above the railhead.
    """
    half = len(vs) // 2
    out = []
    for i in range(0, half, 2):
        row = sorted((vs[i], vs[i + 1], vs[half + i], vs[half + i + 1]),
                     key=lambda p: p[2])
        a, b = row[0], row[1]
        out.append(((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5, (a[2] + b[2]) * 0.5))
    return out


def flat_ribbon(vs):
    """The centre of an un-solidified strip() ribbon - the track bed."""
    return [((vs[i][0] + vs[i + 1][0]) * 0.5,
             (vs[i][1] + vs[i + 1][1]) * 0.5,
             (vs[i][2] + vs[i + 1][2]) * 0.5) for i in range(0, len(vs), 2)]


def offset(line, s):
    """Lateral shift, matching 04_rail.offset_line."""
    if not s:
        return list(line)
    out = []
    for i, p in enumerate(line):
        a, b = line[max(0, i - 1)], line[min(len(line) - 1, i + 1)]
        yaw = math.atan2(b[1] - a[1], b[0] - a[0])
        out.append((p[0] - math.sin(yaw) * s, p[1] + math.cos(yaw) * s, p[2]))
    return out


def train_road(meshes, phase):
    """The centre of the pair of rails the locomotive runs on, in Blender space.

    Phase 3 lays four rails and the loco takes the pair TRAIN_OFF to one side, and
    all three adjacent pairs are the same 3.4 apart - so the pair cannot be picked
    by spacing. It is picked by which one lies over the bed's centreline shifted by
    TRAIN_OFF, and those three candidates are a clear 3.4 apart.
    """
    rails = [ribbon(vs) for vs in parts(meshes, "Rail")]
    if len(rails) < 2:
        raise ValueError("expected at least 2 running rails, found %d" % len(rails))
    n = len(rails[0])
    for r in rails:
        if len(r) != n:
            raise ValueError("the running rails disagree on row count")

    bed = parts(meshes, "Bed")
    if len(bed) != 1:
        raise ValueError("expected 1 track bed, found %d" % len(bed))
    seam = sum(math.dist(bed[0][i][:2], bed[0][i + 1][:2])
               for i in range(0, len(bed[0]), 2)) / (len(bed[0]) // 2)
    if abs(seam - BED_W[phase]) > 0.35:
        raise ValueError("bed seam spacing %.2f is not the phase-%d width %.2f"
                         % (seam, phase, BED_W[phase]))
    want = offset(flat_ribbon(bed[0]), TRAIN_OFF[phase])

    probe = range(0, n, max(1, n // 40))
    best, pair = None, None
    for i in range(len(rails)):
        for k in range(i + 1, len(rails)):
            sep = [math.dist(rails[i][j][:2], rails[k][j][:2]) for j in probe]
            if abs(sum(sep) / len(sep) - 2 * GAUGE) > 0.4:
                continue                       # not two rails of one road
            err = 0.0
            for j in probe:
                q = ((rails[i][j][0] + rails[k][j][0]) * 0.5,
                     (rails[i][j][1] + rails[k][j][1]) * 0.5)
                err += min(math.dist(q, w[:2]) for w in want)
            if best is None or err < best:
                best, pair = err, (rails[i], rails[k])
    if pair is None:
        raise ValueError("no pair of rails a gauge apart")

    a, b = pair
    return [((a[i][0] + b[i][0]) * 0.5, (a[i][1] + b[i][1]) * 0.5,
             (a[i][2] + b[i][2]) * 0.5) for i in range(n)]


def resample(line, step, chord):
    """Thin the laid line to waypoints, keeping curves honest.

    A flat every-N-metres thinning is wrong here: the rake is placed by arc length
    along this polyline, so wherever the thinned line cuts a corner the wagons are
    put beside the track. A segment is extended while the rails stay within `chord`
    of it, and is committed at `step` regardless - straight runs cost few points,
    the tight curve off the mine keeps as many as it needs.
    """
    out = [line[0]]
    anchor = 0
    for i in range(2, len(line)):
        a, b = line[anchor], line[i]
        run = math.dist(a[:2], b[:2])
        dx, dy = b[0] - a[0], b[1] - a[1]
        l2 = dx * dx + dy * dy
        worst = 0.0
        for k in range(anchor + 1, i):
            p = line[k]
            t = max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / l2)) if l2 > 1e-9 else 0.0
            worst = max(worst, math.hypot(p[0] - (a[0] + dx * t), p[1] - (a[1] + dy * t)))
            if worst > chord:
                break
        if worst > chord or run >= step:
            out.append(line[i - 1])
            anchor = i - 1
    if out[-1] != line[-1]:
        out.append(line[-1])
    return out


def back_along(line, dist):
    """The point `dist` metres back from the railhead - 04_rail.back_along."""
    run, i = 0.0, len(line) - 1
    while i > 1 and run < dist:
        run += math.dist(line[i][:2], line[i - 1][:2])
        i -= 1
    return line[i]


def off_line(q, pts):
    """How far a Unity point lies from a Unity polyline, ignoring height."""
    best = float("inf")
    for a, b in zip(pts, pts[1:]):
        dx, dz = b["x"] - a["x"], b["z"] - a["z"]
        l2 = dx * dx + dz * dz
        t = max(0.0, min(1.0, ((q[0] - a["x"]) * dx + (q[2] - a["z"]) * dz) / l2)) if l2 > 1e-9 else 0.0
        best = min(best, math.hypot(q[0] - (a["x"] + dx * t), q[2] - (a["z"] + dz * t)))
    return best


def to_unity(p):
    """Blender railhead point -> Unity point at the rolling stock's datum.

    The axis map is 14_routes.pt's: (bx, by, bz) -> (-bx, bz, -by).
    """
    return {"x": round(-p[0], 4),
            "y": round(p[2] - RAIL_SURFACE + RAIL_Y, 4),
            "z": round(-p[1], 4)}


def from_unity(a):
    return (-a["x"], -a["z"])


# ---------------------------------------------------------------------- main

def fit(island, phase, write):
    route_path = os.path.join(ROUTES, "%s_routes_P%d.json" % (island.lower(), phase))
    fbx_path = os.path.join(MODELS, island, "Phase%d" % phase, "Rail_P%d.fbx" % phase)
    if not (os.path.exists(route_path) and os.path.exists(fbx_path)):
        print("%-7s P%d  skipped - no route file or no Rail FBX" % (island, phase))
        return False

    with open(route_path) as f:
        routes = json.load(f)
    meshes = fbx_meshes(fbx_path)

    line = train_road(meshes, phase)

    anchors = {a["name"]: a for a in routes["anchors"]}
    depot = from_unity(anchors["depot"]["pos"])
    if math.dist(line[0][:2], depot) < math.dist(line[-1][:2], depot):
        line.reverse()                         # written tunnel mouth -> depot

    door = back_along(line, SHED_L)
    thinned = resample(line, STEP, CHORD)

    # Thinning must not straighten a curve the rails actually bend through - the
    # rake is placed by arc length along this polyline, so a chord cut here puts
    # the wagons beside the track rather than on it.
    cut = max(off_line((-p[0], 0.0, -p[1]), [to_unity(q) for q in thinned]) for p in line)
    if cut > CHORD:
        raise ValueError("thinning at %.1f m cuts the curve by %.2f m" % (STEP, cut))

    # The train has to end up under the shed roof, not short of it or past it.
    shed = parts(meshes, "Shed")
    if shed:
        lo = (min(v[0] for v in shed[0]), min(v[1] for v in shed[0]))
        hi = (max(v[0] for v in shed[0]), max(v[1] for v in shed[0]))
        for label, p in (("doorway", door), ("railhead", line[-1])):
            if not (lo[0] <= p[0] <= hi[0] and lo[1] <= p[1] <= hi[1]):
                raise ValueError("the %s does not land inside the engine shed" % label)

    rail = next(p for p in routes["paths"] if p["name"] == "rail")
    old_pts = [(q["x"], q["y"], q["z"]) for q in rail["points"]]
    new_pts = [to_unity(p) for p in thinned]
    drift = max(off_line(q, new_pts) for q in old_pts) if old_pts else 0.0
    old_len = sum(math.dist(a, b) for a, b in zip(old_pts, old_pts[1:]))

    rail["points"] = new_pts
    moved = 0.0
    if "railShed" in anchors:
        old = anchors["railShed"]["pos"]
        new = to_unity(door)
        moved = math.hypot(old["x"] - new["x"], old["z"] - new["z"])
        anchors["railShed"]["pos"] = new
    else:
        routes["anchors"].append({"name": "railShed", "pos": to_unity(door)})

    total = sum(math.dist((a["x"], a["y"], a["z"]), (b["x"], b["y"], b["z"]))
                for a, b in zip(new_pts, new_pts[1:]))
    print("%-7s P%d  line %.1f m -> %.1f m (%d pts -> %d); it ran up to %5.1f m off "
          "the rails; shed door moved %5.1f m%s"
          % (island, phase, old_len, total, len(old_pts), len(new_pts), drift, moved,
             "" if write else "   (dry run)"))

    if write:
        with open(route_path, "w") as f:
            json.dump(routes, f, indent=1)
    return True


def main():
    write = "--write" in sys.argv[1:]
    for island in ISLANDS:
        for phase in PHASES:
            fit(island, phase, write)
    if not write:
        print("\nNothing written. Re-run with --write to update the route files.")
    else:
        print("\nRoute files updated. Rebuild the phase prefabs so the terrain cut "
              "in RailCorridorFlattener follows the corrected line.")


if __name__ == "__main__":
    main()
