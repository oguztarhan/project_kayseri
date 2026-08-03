"""Vertex-colour bake: a material's procedural texture, sampled onto its mesh.

The island shader in Unity reads VERTEX COLOUR and ignores the material's base
colour, so this is how the texture detail authored in tex.py survives FBX. It is
an approximation of the node tree - the ramp evaluated at the texture node's
factor - which means it can only vary a colour with position. Anything that has
to vary with slope, height or wear paints itself instead (see lib.paint), and
those meshes are skipped here.

Lives in its own module rather than inside 13_export so the preview can use it:
preview.py bakes the whole scene and shows the result in Blender's viewport,
which is the only way to see what the game will draw without a round trip
through Unity.
"""
import bpy
from math import sin, floor
from mathutils import Vector

try:
    from mathutils import noise as mnoise
except Exception:                                    # pragma: no cover
    mnoise = None


def ramp_of(m):
    if not m or not m.use_nodes:
        return None
    for n in m.node_tree.nodes:
        if n.bl_idname == "ShaderNodeValToRGB":
            return n.color_ramp
    return None


def texnode_of(m):
    if not m or not m.use_nodes:
        return None
    for n in m.node_tree.nodes:
        if n.bl_idname in ("ShaderNodeTexNoise", "ShaderNodeTexVoronoi",
                           "ShaderNodeTexWave", "ShaderNodeTexChecker"):
            return n
    return None


_CACHE = {}


def mat_info(m):
    """(kind, scale, detail, wave_dir, ramp, base) - cached per material."""
    if m is None:
        return None
    key = m.name
    if key in _CACHE:
        return _CACHE[key]
    ramp = ramp_of(m)
    t = texnode_of(m)
    if ramp is None or t is None:
        bsdf = m.node_tree.nodes.get("Principled BSDF") if m.use_nodes else None
        base = tuple(bsdf.inputs["Base Color"].default_value)[:3] if bsdf else (0.5,) * 3
        info = ("flat", 1.0, 1.0, "X", None, base)
    else:
        kind = {"ShaderNodeTexNoise": "noise", "ShaderNodeTexVoronoi": "voronoi",
                "ShaderNodeTexWave": "wave",
                "ShaderNodeTexChecker": "checker"}[t.bl_idname]
        sc = t.inputs["Scale"].default_value if "Scale" in t.inputs else 1.0
        det = t.inputs["Detail"].default_value if "Detail" in t.inputs else 2.0
        wd = getattr(t, "bands_direction", "X")
        info = (kind, sc, det, wd, ramp, None)
    _CACHE[key] = info
    return info


def fac_at(info, co):
    """Approximate the texture node's Factor output at a local-space point."""
    kind, sc, det, wd, ramp, base = info
    x, y, z = co.x * sc, co.y * sc, co.z * sc
    if kind == "noise":
        v = mnoise.noise(Vector((x, y, z))) if mnoise else 0.0
        amp, f = 0.5, 2.0
        for _ in range(int(min(4, max(0, det - 1)))):
            if mnoise:
                v += amp * mnoise.noise(Vector((x * f, y * f, z * f)))
            amp *= 0.5
            f *= 2.0
        return max(0.0, min(1.0, 0.5 + 0.42 * v))
    if kind == "voronoi":
        if mnoise:
            d, _p = mnoise.voronoi(Vector((x, y, z)), distance_metric='DISTANCE')
            return max(0.0, min(1.0, d[0] * 0.9))
        return 0.5
    if kind == "wave":
        a = {"X": x, "Y": y, "Z": z, "DIAGONAL": (x + y)}.get(wd, x)
        return 0.5 + 0.5 * sin(a * 6.2831853)
    if kind == "checker":
        return float((int(floor(x)) + int(floor(y)) + int(floor(z))) % 2)
    return 0.5


def color_at(info, co):
    kind, sc, det, wd, ramp, base = info
    if ramp is None:
        return base
    c = ramp.evaluate(fac_at(info, co))
    return (c[0], c[1], c[2])


def bake_mesh(me):
    if not me.polygons:
        return False
    # Already painted by the step that built it - see lib.paint. This bake can
    # only vary a colour with position; the ground and the dirt roads vary with
    # slope, height and wear, so they carry their own.
    if me.get("painted"):
        return False
    ca = None
    for a in me.color_attributes:
        if a.name == "Col":
            ca = a
            break
    if ca is None:
        try:
            ca = me.color_attributes.new(name="Col", type='BYTE_COLOR',
                                         domain='CORNER')
        except Exception:
            return False
    infos = [mat_info(m) for m in me.materials] or [mat_info(None)]
    verts = me.vertices
    loops = me.loops
    data = ca.data
    for poly in me.polygons:
        info = infos[poly.material_index] if poly.material_index < len(infos) \
            else infos[0]
        if info is None:
            continue
        for li in poly.loop_indices:
            r, g, b = color_at(info, verts[loops[li].vertex_index].co)
            data[li].color = (r, g, b, 1.0)
    return True


def bake_objects(objs):
    """Bake every distinct mesh among objs. Returns how many were baked."""
    done, n = set(), 0
    for ob in objs:
        if ob.type != 'MESH' or ob.data is None or ob.data.name in done:
            continue
        done.add(ob.data.name)
        if bake_mesh(ob.data):
            n += 1
    return n


def bake_all():
    return bake_objects(list(bpy.data.objects))
