"""Turning the built geometry from toy into material.

Nothing here moves anything or changes a dimension - the map, the district
layouts and every size stay exactly as the build steps made them.  What it
changes is how light meets the surfaces.

The single biggest reason the island reads as a toy is that every edge on it is
a perfect 90 degrees.  Real concrete, steel and timber have a chamfer, a weld
fillet or a worn arris a few centimetres across, and that sliver is what catches
a highlight and tells the eye how big something is.  Without it a 20-metre
warehouse and a 2-metre crate shade identically, which is exactly the look of
moulded plastic.

Done as an unapplied modifier stack on purpose:

* the viewport and the render show it immediately,
* 13_export.py already exports with `use_mesh_modifiers=True`, so Unity gets the
  same geometry without a second bake step,
* and it can be turned off wholesale to compare, or dropped for a low-end build,
  without rebuilding the island.

Terrain and foliage are deliberately excluded.  The ground is already an organic
displaced mesh with no hard edges to soften, and it carries two thirds of the
island's faces - bevelling it would cost more than everything else put together
and show nothing.
"""
import bpy
from math import radians

# The groups worth bevelling: everything built out of boxes and cylinders.
BUILT = ("Roads", "Rail", "Mine", "Depot", "Refinery", "Market", "Port",
         "Sites", "Props", "Power", "Haul", "Fleet", "Civic", "Theme",
         "Vehicles")

BEVEL = "Kayseri.Bevel"


def _meshes(names):
    for n in names:
        col = bpy.data.collections.get(n)
        if col is None:
            continue
        for ob in col.objects:
            if ob.type == 'MESH' and ob.data is not None and len(ob.data.polygons):
                yield ob


def clear(names=BUILT):
    """Drop the pass, so the raw build can be compared against it."""
    n = 0
    for ob in _meshes(names):
        for m in list(ob.modifiers):
            if m.name.startswith(BEVEL) or m.type == 'NODES':
                ob.modifiers.remove(m)
                n += 1
    return n


def realism(names=BUILT, width=0.05, segments=1, angle=44.0,
            smooth=35.0, harden=True):
    """Chamfer every hard edge and shade the result by angle.

    `width` is in metres and deliberately small.  At 5-6 cm it reads as a cast
    edge on concrete and a folded one on sheet steel at the distance the game is
    played from; much above 10 cm and the buildings start to look inflated,
    which is the same toy problem from the other direction.

    `segments` of 2 is the cheap sweet spot - one segment reads as a bevel only
    from straight on, three is invisible against two at this scale.
    """
    done = 0
    seen = set()
    for ob in _meshes(names):
        if ob.data.name in seen:
            continue          # linked duplicates share a mesh; modifiers are per-object
        seen.add(ob.data.name)

        for m in list(ob.modifiers):
            if m.name.startswith(BEVEL):
                ob.modifiers.remove(m)

        b = ob.modifiers.new(BEVEL, 'BEVEL')
        b.width = width
        b.segments = segments
        b.limit_method = 'ANGLE'
        b.angle_limit = radians(angle)
        b.miter_outer = 'MITER_ARC'
        # Clamped, or a bevel wider than the thinnest plate in the mesh turns
        # handrails and window mullions inside out.
        b.use_clamp_overlap = True
        if harden:
            try:
                b.harden_normals = True
            except Exception:
                pass
        done += 1

    # Smooth by angle AFTER the bevel, so the chamfer faces blend into the flats
    # they came from while the flats themselves stay crisp against each other.
    _smooth_by_angle(names, smooth)
    return done


def shading(names=BUILT, angle=32.0):
    """Smooth-by-angle only - the realism that costs nothing.

    The bevel pass this module was written for tripled the face count, which a
    60fps mid-range Android target cannot pay for, so it is not in the build.
    This half is free: it rounds every cylinder, cone and sphere on the island -
    silos, tanks, columns, stacks, wheels, pipes - while leaving box edges as
    crisp as they were, because nothing on a box meets at less than 32 degrees.
    Faceted cylinders are the second loudest toy signal after hard edges, and
    they cost the same either way.
    """
    _smooth_by_angle(names, angle)
    n = sum(1 for _ in _meshes(names))
    return n


def _smooth_by_angle(names, angle):
    view = bpy.context.view_layer
    prev = view.objects.active
    sel = [ob for ob in bpy.context.selected_objects]
    for ob in bpy.context.selected_objects:
        ob.select_set(False)

    batch = [ob for ob in _meshes(names)]
    for ob in batch:
        for p in ob.data.polygons:
            p.use_smooth = True
        ob.select_set(True)
    if batch:
        view.objects.active = batch[0]
        try:
            bpy.ops.object.shade_auto_smooth(angle=radians(angle))
        except Exception:
            # Older builds: fall back to flat shading rather than smearing the
            # normals of every box on the island into one another.
            for ob in batch:
                for p in ob.data.polygons:
                    p.use_smooth = False

    for ob in batch:
        ob.select_set(False)
    for ob in sel:
        try:
            ob.select_set(True)
        except Exception:
            pass
    view.objects.active = prev


def faces(names=BUILT):
    """Evaluated face count - what the modifier stack actually costs."""
    dg = bpy.context.evaluated_depsgraph_get()
    base = total = 0
    for ob in _meshes(names):
        base += len(ob.data.polygons)
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        total += len(me.polygons)
        ev.to_mesh_clear()
    return base, total
