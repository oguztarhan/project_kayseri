"""Step 17: check a district's property - the way in, the yard, and overlaps.

Run after a build to see what is standing where it should not be:

    run("17_audit")

Reports, per district:
  ENTRY   objects standing in the corridor between the gate and the yard
  YARD    objects standing in the open middle where the stockpile goes
  OVERLAP pairs of solid objects whose boxes intersect each other

Ground slabs, painted lines, fences and smoke are exempt: a lorry drives over
the first two, the third is meant to be crossed at its gaps, and the fourth is
not there.
"""
import importlib
import layout
importlib.reload(layout)
import yard
importlib.reload(yard)
L = layout

DISTRICTS = [("mine", "Mine", L.MINE), ("depot", "Depot", L.DEPOT),
             ("refinery", "Refinery", L.REFINERY), ("market", "Market", L.MARKET)]

# Flat ground, paint, fences and effects never block anything.
FLAT_Z = 1.5
# Ground, paint, effects and scenery block nothing; JOINED things are meant to
# touch what they serve, so they are exempt from the overlap test only.
EXEMPT = ("Pad", "Yard", "Apron", "Bays", "Fence", "Smoke", "Cliff", "Spoil",
          "Lamp", "Containers", "Goods", "Stalls", "Pile", "Clutter")

# How deep two boxes have to interpenetrate before it reads as a mistake rather
# than two props standing against each other.
BITE = 1.6


def _box(ob):
    """World-space AABB of an object, as (x0, x1, y0, y1, z0, z1)."""
    pts = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    return (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))


def _exempt(name):
    return any(k in name for k in EXEMPT)


def audit(verbose=True):
    lines = []
    for key, cname, centre in DISTRICTS:
        col = bpy.data.collections.get(cname)
        if col is None:
            continue
        F = yard.Frame(key, centre)
        u, v = F.u, F.v
        solid = []
        entry, inyard = [], []

        for ob in col.objects:
            if ob.type != 'MESH' or ob.data is None or not len(ob.data.polygons):
                continue
            x0, x1, y0, y1, z0, z1 = _box(ob)
            if (not _exempt(ob.name) and (z1 - z0) >= FLAT_Z
                    and not any(k in ob.name for k in yard.JOINED)):
                solid.append((ob.name, (x0, x1, y0, y1, z0, z1)))
            if _exempt(ob.name):
                continue
            if (z1 - z0) < FLAT_Z:
                continue
            # the box's own footprint in the district's (along, across) frame
            aa, bb = [], []
            for px, py in ((x0, y0), (x1, y0), (x0, y1), (x1, y1)):
                dx, dy = px - centre[0], py - centre[1]
                aa.append(dx * u[0] + dy * u[1])
                bb.append(dx * v[0] + dy * v[1])
            a, ra = (min(aa) + max(aa)) * 0.5, (max(aa) - min(aa)) * 0.5
            b, rb = (min(bb) + max(bb)) * 0.5, (max(bb) - min(bb)) * 0.5
            if yard.inside(yard.ENTRY, a, b, ra, rb):
                entry.append("%s (a=%.0f b=%.0f)" % (ob.name, a, b))
            if yard.inside(yard.YARD, a, b, ra, rb):
                inyard.append("%s (a=%.0f b=%.0f)" % (ob.name, a, b))

        pairs = []
        for i in range(len(solid)):
            ni, bi = solid[i]
            for j in range(i + 1, len(solid)):
                nj, bj = solid[j]
                ox = min(bi[1], bj[1]) - max(bi[0], bj[0])
                oy = min(bi[3], bj[3]) - max(bi[2], bj[2])
                oz = min(bi[5], bj[5]) - max(bi[4], bj[4])
                if ox > BITE and oy > BITE and oz > BITE:
                    pairs.append("%s x %s (%.0f/%.0f/%.0f)" % (ni, nj, ox, oy, oz))

        lines.append("%-9s ENTRY %d  YARD %d  OVERLAP %d"
                     % (cname, len(entry), len(inyard), len(pairs)))
        if verbose:
            for t in entry:
                lines.append("    ENTRY   " + t)
            for t in inyard:
                lines.append("    YARD    " + t)
            for t in pairs[:14]:
                lines.append("    OVERLAP " + t)
            if len(pairs) > 14:
                lines.append("    OVERLAP ... and %d more" % (len(pairs) - 14))

    out = "\n".join(lines)
    print(out)
    return out


AUDIT = audit(globals().get("VERBOSE", True))
