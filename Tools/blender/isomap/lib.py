"""Shared helpers for the isometric industrial map build."""
import bpy, bmesh, math, random
from math import radians, sin, cos, pi, atan2, hypot, exp
from mathutils import Vector, Matrix, Euler

RNG = random.Random(20260801)

try:
    from mathutils import noise as _mnoise
except Exception:                                    # pragma: no cover
    _mnoise = None


def nz(x, y, s, seed=0.0):
    """Perlin noise in world units - s is 1 / feature size."""
    if _mnoise is None:
        return 0.0
    return _mnoise.noise(Vector((x * s, y * s, seed)))


def nz1(x, y, s, seed=0.0):
    """Same, scaled to roughly [-1, 1].

    Raw mathutils noise has a standard deviation of 0.25, so an amplitude
    written as 0.1 actually lands as 0.025 - which is how a terrain built on
    three octaves of it comes out looking like one flat colour.
    """
    return max(-1.0, min(1.0, nz(x, y, s, seed) * 2.9))

# ---------------------------------------------------------------- collections
def coll(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c

# ------------------------------------------------------------------ materials
def set_in(node, name, val):
    s = node.inputs.get(name)
    if s is None:
        return
    try:
        s.default_value = val
    except Exception:
        pass

def mat(name, color=(0.5, 0.5, 0.5), rough=0.75, metal=0.0,
        emis=None, emis_str=0.0, alpha=1.0, spec=0.5):
    """Fetch-or-create a simple principled material."""
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    if b:
        set_in(b, "Base Color", (color[0], color[1], color[2], 1.0))
        set_in(b, "Roughness", rough)
        set_in(b, "Metallic", metal)
        set_in(b, "Specular IOR Level", spec)
        if emis is not None:
            set_in(b, "Emission Color", (emis[0], emis[1], emis[2], 1.0))
            set_in(b, "Emission Strength", emis_str)
        if alpha < 1.0:
            set_in(b, "Alpha", alpha)
    if alpha < 1.0:
        for attr, val in (("blend_method", 'BLEND'),
                          ("surface_render_method", 'BLENDED')):
            if hasattr(m, attr):
                try:
                    setattr(m, attr, val)
                except Exception:
                    pass
    return m

# --------------------------------------------------------------------- geometry
def _mx(loc, rot, scale):
    return Matrix.LocRotScale(Vector(loc), Euler(rot, 'XYZ'), Vector(scale))

class B:
    """Accumulates primitives into a single mesh.

    All primitives are centred on their own origin unless noted; `loc` places
    them in the object's local space.  Call `.use("matname")` to switch the
    material that subsequent primitives are tagged with, so one object can be
    multi-coloured (a truck, a locomotive) without splitting into many objects.
    """

    def __init__(self):
        self.bm = bmesh.new()
        self.mats = []
        self.cur = 0

    def use(self, m):
        name = m if isinstance(m, str) else m.name
        if name not in self.mats:
            self.mats.append(name)
        self.cur = self.mats.index(name)
        return self

    def _tagv(self, verts):
        """Tag every face touching these freshly-created verts.

        Deriving the faces from the new verts is exact.  (Do NOT assume new
        faces land at the end of bm.faces - bmesh reuses pool slots, so an
        index-range approach silently mis-assigns materials.)
        """
        if not self.mats or self.cur == 0:
            return
        seen = set()
        for v in verts:
            for f in v.link_faces:
                if f not in seen:
                    seen.add(f)
                    f.material_index = self.cur

    def _place(self, verts, loc, rot, scale):
        bmesh.ops.transform(self.bm, matrix=_mx(loc, rot, scale), verts=verts)
        self._tagv(verts)

    def box(self, size=(1, 1, 1), loc=(0, 0, 0), rot=(0, 0, 0)):
        """Cube centred on loc. size = full extents."""
        r = bmesh.ops.create_cube(self.bm, size=1.0)
        self._place(r['verts'], loc, rot, size)
        return self

    def boxz(self, size=(1, 1, 1), loc=(0, 0, 0), rot=(0, 0, 0)):
        """Box sitting ON loc (loc is the base centre)."""
        return self.box(size, (loc[0], loc[1], loc[2] + size[2] * 0.5), rot)

    def cyl(self, r=1.0, h=1.0, loc=(0, 0, 0), rot=(0, 0, 0), seg=16, cap=True,
            scale=(1, 1, 1)):
        res = bmesh.ops.create_cone(self.bm, cap_ends=cap, cap_tris=False,
                                    segments=seg, radius1=r, radius2=r, depth=h)
        self._place(res['verts'], loc, rot, scale)
        return self

    def cylz(self, r=1.0, h=1.0, loc=(0, 0, 0), rot=(0, 0, 0), seg=16, cap=True):
        return self.cyl(r, h, (loc[0], loc[1], loc[2] + h * 0.5), rot, seg, cap)

    def cone(self, r1=1.0, r2=0.0, h=1.0, loc=(0, 0, 0), rot=(0, 0, 0), seg=16):
        res = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False,
                                    segments=seg, radius1=r1, radius2=r2, depth=h)
        self._place(res['verts'], loc, rot, (1, 1, 1))
        return self

    def conez(self, r1=1.0, r2=0.0, h=1.0, loc=(0, 0, 0), rot=(0, 0, 0), seg=16):
        return self.cone(r1, r2, h, (loc[0], loc[1], loc[2] + h * 0.5), rot, seg)

    def sphere(self, r=1.0, loc=(0, 0, 0), subd=2, scale=(1, 1, 1)):
        res = bmesh.ops.create_icosphere(self.bm, subdivisions=subd, radius=r)
        self._place(res['verts'], loc, (0, 0, 0), scale)
        return self

    def uvsphere(self, r=1.0, loc=(0, 0, 0), seg=16, rings=8, scale=(1, 1, 1)):
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=seg, v_segments=rings,
                                        radius=r)
        self._place(res['verts'], loc, (0, 0, 0), scale)
        return self

    def plane(self, size=(1, 1), loc=(0, 0, 0), rot=(0, 0, 0)):
        res = bmesh.ops.create_grid(self.bm, x_segments=1, y_segments=1, size=0.5)
        self._place(res['verts'], loc, rot, (size[0], size[1], 1))
        return self

    def grid(self, size=(1, 1), loc=(0, 0, 0), segs=10):
        res = bmesh.ops.create_grid(self.bm, x_segments=segs, y_segments=segs,
                                    size=0.5)
        self._place(res['verts'], loc, (0, 0, 0), (size[0], size[1], 1))
        return self

    def tube(self, r=0.2, pts=None, seg=8):
        """Connected pipe run through a list of 3D points."""
        if not pts or len(pts) < 2:
            return self
        for i in range(len(pts) - 1):
            a, b = Vector(pts[i]), Vector(pts[i + 1])
            d = b - a
            L = d.length
            if L < 1e-5:
                continue
            rot = (0.0, math.acos(max(-1, min(1, d.z / L))), atan2(d.y, d.x))
            self.cyl(r, L, tuple((a + b) * 0.5), (0, rot[1], rot[2]), seg)
        for p in pts[1:-1]:
            self.sphere(r, tuple(p), 1)
        return self

    def wedge(self, size=(1, 1, 1), loc=(0, 0, 0), rot=(0, 0, 0)):
        """Right-triangular prism, rises along +X, base at loc."""
        bm = self.bm
        sx, sy, sz = size
        vs = [bm.verts.new(v) for v in [
            (-sx / 2, -sy / 2, 0), (sx / 2, -sy / 2, 0), (sx / 2, -sy / 2, sz),
            (-sx / 2, sy / 2, 0), (sx / 2, sy / 2, 0), (sx / 2, sy / 2, sz)]]
        bm.faces.new([vs[0], vs[1], vs[2]])
        bm.faces.new([vs[5], vs[4], vs[3]])
        bm.faces.new([vs[0], vs[3], vs[4], vs[1]])
        bm.faces.new([vs[1], vs[4], vs[5], vs[2]])
        bm.faces.new([vs[2], vs[5], vs[3], vs[0]])
        self._place(vs, loc, rot, (1, 1, 1))
        return self

    def roof(self, size=(1, 1, 1), loc=(0, 0, 0), rot=(0, 0, 0)):
        """Gable roof prism; ridge runs along Y. Base at loc."""
        bm = self.bm
        sx, sy, sz = size
        vs = [bm.verts.new(v) for v in [
            (-sx / 2, -sy / 2, 0), (sx / 2, -sy / 2, 0), (0, -sy / 2, sz),
            (-sx / 2, sy / 2, 0), (sx / 2, sy / 2, 0), (0, sy / 2, sz)]]
        bm.faces.new([vs[0], vs[1], vs[2]])
        bm.faces.new([vs[5], vs[4], vs[3]])
        bm.faces.new([vs[0], vs[3], vs[4], vs[1]])
        bm.faces.new([vs[1], vs[4], vs[5], vs[2]])
        bm.faces.new([vs[2], vs[5], vs[3], vs[0]])
        self._place(vs, loc, rot, (1, 1, 1))
        return self

    def truss(self, a, b, r=0.12, rungs=True, step=1.6):
        """Two parallel rails from a to b with cross rungs - conveyor/gantry look."""
        a, b = Vector(a), Vector(b)
        d = b - a
        L = d.length
        if L < 1e-4:
            return self
        side = Vector((-d.y, d.x, 0))
        if side.length < 1e-5:
            side = Vector((1, 0, 0))
        side = side.normalized() * (r * 5)
        for s in (side, -side):
            self.tube(r, [tuple(a + s), tuple(b + s)], 6)
        if rungs:
            n = max(2, int(L / step))
            for i in range(n + 1):
                p = a + d * (i / n)
                self.tube(r * 0.7, [tuple(p + side), tuple(p - side)], 5)
        return self

    def merge(self, dist=0.0005):
        bmesh.ops.remove_doubles(self.bm, verts=list(self.bm.verts), dist=dist)
        return self

    def shade_smooth(self, angle=40):
        for f in self.bm.faces:
            f.smooth = True
        self._smooth_angle = angle
        return self

    def make(self, name, material=None, collection=None,
             loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1), smooth=False):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        self.bm = None
        if smooth:
            for p in me.polygons:
                p.use_smooth = True
        ob = bpy.data.objects.new(name, me)
        ob.location = loc
        ob.rotation_euler = Euler(rot, 'XYZ')
        ob.scale = scale
        if self.mats:
            for mn in self.mats:
                me.materials.append(mat(mn) if isinstance(mn, str) else mn)
        elif material is not None:
            me.materials.append(mat(material) if isinstance(material, str)
                                else material)
        (collection or bpy.context.scene.collection).objects.link(ob)
        return ob

# ------------------------------------------------------------------- instancing
def dup(src, loc=(0, 0, 0), rot=(0, 0, 0), scale=None, collection=None, name=None):
    """Linked duplicate - shares mesh data, cheap for forests/fleets."""
    ob = bpy.data.objects.new(name or (src.name + ".i"), src.data)
    ob.location = loc
    ob.rotation_euler = Euler(rot, 'XYZ')
    ob.scale = scale or src.scale
    (collection or bpy.context.scene.collection).objects.link(ob)
    return ob

def displace(ob, strength=1.0, size=1.0, seed=0, mid=0.5):
    """Add a cloud-noise displace modifier - for rocks, coal piles, terrain."""
    tex = bpy.data.textures.new(ob.name + "_disp", type='CLOUDS')
    tex.noise_scale = size
    try:
        tex.noise_depth = 3
    except Exception:
        pass
    m = ob.modifiers.new("disp", 'DISPLACE')
    m.texture = tex
    m.strength = strength
    m.mid_level = mid
    m.texture_coords = 'LOCAL'
    return m

def rough_verts(ob, amount=0.3, scale=1.0, seed=0.0):
    """Bake noise displacement straight into the mesh so linked duplicates
    inherit it (object modifiers would not)."""
    try:
        from mathutils import noise as mnoise
    except Exception:
        return ob
    me = ob.data
    for v in me.vertices:
        n = mnoise.noise(v.co * scale + Vector((seed, seed * 0.7, seed * 1.3)))
        v.co = v.co + v.normal * (n * amount)
    return ob


def subsurf(ob, levels=1, simple=False):
    m = ob.modifiers.new("subsurf", 'SUBSURF')
    m.levels = levels
    m.render_levels = levels
    if simple:
        m.subdivision_type = 'SIMPLE'
    return m

def bevel(ob, width=0.03, segments=2, angle=50):
    m = ob.modifiers.new("bevel", 'BEVEL')
    m.width = width
    m.segments = segments
    m.limit_method = 'ANGLE'
    m.angle_limit = radians(angle)
    m.harden_normals = False
    return m

# ------------------------------------------------------------------------ paths
def bez(points, name="path", collection=None, closed=False, res=24):
    """Smooth bezier curve through points (auto handles)."""
    cu = bpy.data.curves.new(name, 'CURVE')
    cu.dimensions = '3D'
    cu.resolution_u = 12
    sp = cu.splines.new('BEZIER')
    sp.bezier_points.add(len(points) - 1)
    for bp, p in zip(sp.bezier_points, points):
        bp.co = Vector(p)
        bp.handle_left_type = bp.handle_right_type = 'AUTO'
    sp.use_cyclic_u = closed
    ob = bpy.data.objects.new(name, cu)
    (collection or bpy.context.scene.collection).objects.link(ob)
    return ob

def sample_bez(points, n, closed=False):
    """Evaluate a Catmull-Rom-ish path -> [(pos, yaw)] without needing a curve object."""
    P = [Vector(p) for p in points]
    if closed:
        P = P + [P[0]]

    def cr(p0, p1, p2, p3, t):
        t2, t3 = t * t, t * t * t
        return 0.5 * ((2 * p1) + (-p0 + p2) * t +
                      (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                      (-p0 + 3 * p1 - 3 * p2 + p3) * t3)

    ext = [P[0] - (P[1] - P[0])] + P + [P[-1] + (P[-1] - P[-2])]
    segs = len(P) - 1
    out = []
    for i in range(n):
        u = (i / max(1, n - 1)) * segs
        s = min(int(u), segs - 1)
        t = u - s
        pos = cr(ext[s], ext[s + 1], ext[s + 2], ext[s + 3], t)
        nxt = cr(ext[s], ext[s + 1], ext[s + 2], ext[s + 3], min(1.0, t + 0.01))
        d = nxt - pos
        out.append((pos, atan2(d.y, d.x) if d.length > 1e-6 else 0.0))
    return out

def strip(pts, width, z=0.0, name="strip", material=None, collection=None,
          thickness=0.0, zfun=None, cols=None):
    """Build a ribbon mesh following pts - roads, rivers, rail beds.

    zfun(x, y) adds a ground height per vertex. Passing a function rather than
    baking heights into pts matters: the XY sampling below must stay untouched,
    because the loop road is a Catmull-Rom through four corners that bulges ~18
    units past them, and densifying its control points to carry heights would
    straighten it and move the road off the exported centreline.

    cols is the lateral seam positions as fractions of the width, ordered +0.5
    down to -0.5 (the default two are the edges). More of them make a ribbon
    that can be COLOURED across its width - wheel ruts down a dirt road need
    vertices to hang the colour on, and a two-vertex ribbon has none.
    Vertices are laid out row-major, so vertex index // len(cols) is the sample
    along the path and index % len(cols) the seam across it.
    """
    bm = bmesh.new()
    samples = sample_bez(pts, max(8, len(pts) * 10))
    fr = tuple(cols) if cols else (0.5, -0.5)
    rows = []
    for pos, yaw in samples:
        nx, ny = -sin(yaw), cos(yaw)
        row = []
        for f in fr:
            ax, ay = pos.x + nx * width * f, pos.y + ny * width * f
            # Sampled at each edge vertex, not once at the centreline: on a
            # side-slope a ribbon held level across its width buries one edge in
            # the hill and floats the other. Per-vertex lets the road bank into it.
            row.append(bm.verts.new((ax, ay, pos.z + z +
                                     (zfun(ax, ay) if zfun else 0.0))))
        rows.append(row)
    for i in range(len(rows) - 1):
        for k in range(len(fr) - 1):
            try:
                # Wound so the face normal points +Z. The reverse order leaves the
                # normal pointing down, which Blender hides (it draws double-sided)
                # but Unity backface-culls, making every flat strip invisible.
                bm.faces.new([rows[i][k + 1], rows[i + 1][k + 1],
                              rows[i + 1][k], rows[i][k]])
            except ValueError:
                pass
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    if material is not None:
        me.materials.append(material)
    (collection or bpy.context.scene.collection).objects.link(ob)
    if thickness:
        sm = ob.modifiers.new("solid", 'SOLIDIFY')
        sm.thickness = thickness
        # Flipped alongside the winding above so solidified strips (the chrome
        # rails) still extrude the same way and keep their original position.
        sm.offset = 1
    return ob

def mix(a, b, t):
    t = max(0.0, min(1.0, t))
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t,
            a[2] + (b[2] - a[2]) * t)


def ramp(cols, t):
    """Piecewise-linear colour ramp through evenly spaced stops."""
    t = max(0.0, min(0.99999, t)) * (len(cols) - 1)
    i = int(t)
    return mix(cols[i], cols[i + 1], t - i)


def paint(ob, fn):
    """Write a per-corner colour attribute from fn(x, y, z, vi, fi) -> (r, g, b).

    The island shader reads VERTEX COLOUR and ignores the material's base colour
    entirely, so this attribute is the whole of what a surface looks like in the
    game. 13_export normally bakes it from an approximation of the material's
    procedural texture, which can only vary with position - it cannot know a
    slope, a height above the valley floor or a wheel rut. Surfaces that need
    those paint themselves here, and the exporter leaves a painted mesh alone.

    Colours are LINEAR, like Blender's ramps; the FBX export converts. Call this
    before any lift_collection - v.co is in the object's own space.
    """
    me = ob.data
    ca = None
    for a in me.color_attributes:
        if a.name == "Col":
            ca = a
            break
    if ca is None:
        ca = me.color_attributes.new(name="Col", type='BYTE_COLOR', domain='CORNER')
    verts, loops, data = me.vertices, me.loops, ca.data
    for poly in me.polygons:
        for li in poly.loop_indices:
            vi = loops[li].vertex_index
            co = verts[vi].co
            r, g, b = fn(co.x, co.y, co.z, vi, poly.index)
            data[li].color = (r, g, b, 1.0)
    me["painted"] = True
    return ob


def scatter_along(pts, spacing, jitter=0.0, offset=0.0, both=False):
    """Positions along a path at fixed spacing, offset sideways - fences, lamps."""
    samples = sample_bez(pts, 400)
    total = 0.0
    out = []
    last = samples[0][0]
    acc = 0.0
    for pos, yaw in samples[1:]:
        d = (pos - last).length
        total += d
        acc += d
        last = pos
        if acc >= spacing:
            acc = 0.0
            offs = [offset, -offset] if both else [offset]
            for o in offs:
                nx, ny = -sin(yaw), cos(yaw)
                j = (RNG.random() - 0.5) * jitter
                out.append((Vector((pos.x + nx * o + j, pos.y + ny * o + j, pos.z)),
                            yaw))
    return out

# ------------------------------------------------------- map frame (screen space)
# The camera is a true isometric ortho cam at yaw 45 deg, so the world X/Y axes
# read as screen diagonals.  Authoring composition in (u, v) - where u is screen
# right and v is screen up - makes it possible to lay the map out exactly like a
# reference image, while buildings/roads stay aligned to the world axes and so
# keep the isometric-grid look.
S2 = 0.7071067811865476

def M(u, v, z=0.0):
    """Map/screen frame -> world.  u = screen right, v = screen up."""
    return (S2 * (u - v), S2 * (u + v), z)

def MV(u, v, z=0.0):
    return Vector(M(u, v, z))

def unM(x, y):
    """World -> map frame."""
    return (S2 * (x + y), S2 * (y - x))

def MY(deg):
    """Map-frame heading in degrees -> world yaw in radians."""
    return radians(deg + 45.0)

# ------------------------------------------------------------------------- misc
def clear_scene():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for blocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials,
                   bpy.data.lights, bpy.data.cameras, bpy.data.textures,
                   bpy.data.images, bpy.data.collections):
        for b in list(blocks):
            if getattr(b, "users", 0) == 0:
                try:
                    blocks.remove(b)
                except Exception:
                    pass

def lift_collection(name, dz):
    """Raise a finished district onto its graded pad.

    The district scripts hardcode ~150 z literals against a flat z=0 ground.
    Translating the built objects is far less error-prone than editing every
    one of them, and it keeps each script readable in local terms.

    Parented children are skipped - they move with their parent already.
    """
    if not dz:
        return 0
    n = 0
    for ob in coll(name).all_objects:
        if ob.parent is not None:
            continue
        ob.location.z += dz
        n += 1
    return n


def lift_by_pad(name, padfn):
    """Raise each object in a collection by padfn(x, y) at its own position.

    For collections whose contents sit on DIFFERENT pads - the three unlockable
    sites are 60-140 units apart at three different heights, so a single offset
    cannot serve them.

    Position is taken from the world-space bounding-box centre rather than
    ob.location, because builders here mix two conventions: parts.* return
    objects positioned by location, while B().make() bakes world coordinates
    into the mesh and leaves location at the origin.
    """
    # matrix_world is CACHED and only recomputed when the dependency graph runs.
    # A script that sets ob.location and reads ob.matrix_world in the same pass
    # gets the matrix from when the object was created - identity - so every
    # parts.* object here was measured at (0, 0) and lifted by the height of the
    # island's centre instead of its own pad. That is why the quarry's shed,
    # hopper and truck all stood 2.9 into the ground and the store's floated 0.9.
    # B().make() objects came out right only because identity IS their matrix.
    bpy.context.view_layer.update()
    n = 0
    for ob in coll(name).all_objects:
        if ob.parent is not None:
            continue
        if ob.type == 'MESH' and len(ob.data.vertices):
            c = Vector((0.0, 0.0, 0.0))
            for v in ob.bound_box:
                c += Vector(v)
            w = ob.matrix_world @ (c / 8.0)
        else:
            w = ob.matrix_world.translation
        ob.location.z += padfn(w.x, w.y)
        n += 1
    return n


def purge_collection(name):
    c = bpy.data.collections.get(name)
    if not c:
        return
    for ob in list(c.objects):
        bpy.data.objects.remove(ob, do_unlink=True)

def stats():
    tri = sum(len(o.data.polygons) for o in bpy.data.objects
              if o.type == 'MESH' and o.data)
    return {"objects": len(bpy.data.objects), "faces": tri,
            "meshes": len(bpy.data.meshes), "materials": len(bpy.data.materials)}
