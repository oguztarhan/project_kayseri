"""Where the tarmac actually is, and what must not be standing on it.

The pavement, the kerbs and the markings are all laid as ribbons that follow
their own polylines, and every one of them is generated independently of the
carriageways.  So wherever two of those polylines cross - the ring footway over
the four arterials, an arterial footway over the ring, a kerb line running past
a junction - a raised strip of concrete ends up lying on the road surface.

Gapping the source polylines gets most of it and never gets all of it: a gap is
a guess at a radius, and the crossing angle, the road width and the ribbon width
all change per island and per phase.  This does it the only way that is exact.
It rebuilds the carriageway corridors as they are actually DRAWN - same trims,
same widths, same phase rules as 03_roads.py - and then deletes geometry face by
face against them.

Two directions, because the two mistakes are opposites:

    clip_out   pavement and kerbs: delete every face whose centroid falls on a
               carriageway.  A footway that meets a road now stops at the kerb
               line instead of running over it.
    clip_in    road markings: delete every face whose centroid falls OFF one.
               A dash that overshoots the end of a run, or a crosswalk laid a
               touch wide, no longer floats on the grass.

Crosswalk paint is exempt from clip_out on purpose: it is meant to be on the
road.  It is flat paint at marking height, not a raised kerb.
"""
import bmesh
import bpy
from math import hypot


def _seg_dist(px, py, ax, ay, bx, by):
    vx, vy = bx - ax, by - ay
    L2 = vx * vx + vy * vy
    if L2 < 1e-12:
        return hypot(px - ax, py - ay)
    t = ((px - ax) * vx + (py - ay) * vy) / L2
    t = 0.0 if t < 0.0 else (1.0 if t > 1.0 else t)
    return hypot(px - (ax + vx * t), py - (ay + vy * t))


class Mask(object):
    """The drawn carriageways, as corridors with a half width each."""

    def __init__(self, corridors):
        self.corridors = corridors
        # Bounding box per corridor, so the common case (a face nowhere near
        # this road) costs one compare instead of walking every segment.
        self.boxes = []
        for pts, hw in corridors:
            xs = [p[0] for p in pts]
            ys = [p[1] for p in pts]
            self.boxes.append((min(xs) - hw, max(xs) + hw,
                               min(ys) - hw, max(ys) + hw))

    def on_road(self, x, y, margin=0.0):
        for i, (pts, hw) in enumerate(self.corridors):
            x0, x1, y0, y1 = self.boxes[i]
            if x < x0 - margin or x > x1 + margin or y < y0 - margin or y > y1 + margin:
                continue
            r = hw + margin
            for k in range(len(pts) - 1):
                a, b = pts[k], pts[k + 1]
                if _seg_dist(x, y, a[0], a[1], b[0], b[1]) <= r:
                    return True
        return False


def build(L, phase, main_w, loop_w, spur_w, port_w):
    """The corridors 03_roads.py actually lays, for this island at this phase."""
    width = {"main": main_w, "loop": loop_w, "spur": spur_w}
    trim = {"arterial": lambda p: L.trim_arterial(p, L.GATES),
            "gated": lambda p: L.trim_zones(p, L.GATES),
            "none": lambda p: [p]}

    out = []
    for pts, wk, _name, mode in L.CARRIAGEWAYS:
        for run in trim[mode](pts):
            if len(run) >= 2:
                out.append(([(p[0], p[1]) for p in run], width[wk] * 0.5))

    need = {"Spur.Quarry": 2, "Spur.Store": 2, "Spur.Plant": 3}
    for pts, name in L.SPURS:
        if phase >= need.get(name, 1):
            for run in L.trim_zones(pts, L.GATES):
                if len(run) >= 2:
                    out.append(([(p[0], p[1]) for p in run], spur_w * 0.5))

    for run in L.trim_zones(L.PORT_ROAD, L.GATES):
        if len(run) >= 2:
            out.append(([(p[0], p[1]) for p in run], port_w * 0.5))

    heads = {"Head.Quarry": 2, "Head.Store": 2, "Head.Plant": 3}
    for pts, name in getattr(L, "HEADS", []):
        if phase >= heads.get(name, 1) and len(pts) >= 2:
            out.append(([(p[0], p[1]) for p in pts], spur_w * 0.5))

    for pts, name in getattr(L, "STREETS", []):
        if len(pts) >= 2:
            out.append(([(p[0], p[1]) for p in pts], spur_w * 0.5))

    return Mask(out)


def _cut(ob, mask, margin, keep_inside):
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    mw = ob.matrix_world
    doomed = []
    for f in bm.faces:
        c = mw @ f.calc_center_median()
        on = mask.on_road(c.x, c.y, margin)
        if on != keep_inside:
            doomed.append(f)
    n = len(doomed)
    if n:
        bmesh.ops.delete(bm, geom=doomed, context='FACES')
        bm.to_mesh(me)
        me.update()
    bm.free()
    return n


def clip_out(objects, mask, margin=0.05):
    """Delete the parts of these meshes that lie ON a carriageway."""
    cut = kept = 0
    for ob in objects:
        if ob.type != 'MESH' or ob.data is None or not len(ob.data.polygons):
            continue
        cut += _cut(ob, mask, margin, keep_inside=False)
        kept += len(ob.data.polygons)
    return cut, kept


def clip_in(objects, mask, margin=0.05):
    """Delete the parts of these meshes that lie OFF every carriageway."""
    cut = kept = 0
    for ob in objects:
        if ob.type != 'MESH' or ob.data is None or not len(ob.data.polygons):
            continue
        cut += _cut(ob, mask, margin, keep_inside=True)
        kept += len(ob.data.polygons)
    return cut, kept


def purge_empty(collection):
    """Drop meshes the clip emptied, so no zero-face objects reach the exporter."""
    gone = []
    for ob in list(collection.objects):
        if ob.type == 'MESH' and ob.data is not None and not len(ob.data.polygons):
            gone.append(ob.name)
            bpy.data.objects.remove(ob, do_unlink=True)
    return gone
