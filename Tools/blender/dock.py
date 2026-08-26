"""
Harbour dock set for the market yard — the art for the Voyages dock (Docs/VOYAGES.md §20),
which shipped as three primitive cubes.

Built to Docs/ASSETS.md's Global Style Guide: low-poly, flat colours from the shared palette,
chunky readable shapes, no textures, no micro-bevels, must read from a 45 degree camera.
1 Blender unit = 1 metre = 1 Unity unit. Z-up. Front faces -Y. Transforms applied.
Pivots at base centre (props). Exported triangulated.

Sizes are taken from what MarketYardScene.BuildDock already lays out, so the meshes drop
straight into the volumes the pads and triggers were authored around:
    jetty   7.0 x 7.0
    bollard 0.6 wide, 3.0 tall, standing at the slab centre
    launch  2.2 x 6.4 x 1.4, moored 4.6 to the +X side
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector

OUT = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else "/tmp"

# ---- shared palette (Docs/ASSETS.md) -----------------------------------------------------
PALETTE = {
    "Wood":   (0x8A, 0x5A, 0x3C),
    "WoodLt": (0xD8, 0xB8, 0x88),
    "Steel":  (0x7A, 0x87, 0x9F),
    "Accent": (0xF2, 0xC1, 0x4E),   # warm gold — the lantern, the one thing that catches the eye
    "Rope":   (0xC6, 0x92, 0x2E),
    "Dark":   (0x56, 0x5E, 0x6B),
}


def srgb_to_linear(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def mat(name):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    r, g, b = PALETTE[name]
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (
        srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), 1.0)
    bsdf.inputs["Roughness"].default_value = 0.85
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.1
    m.diffuse_color = (srgb_to_linear(r), srgb_to_linear(g), srgb_to_linear(b), 1.0)
    return m


def wipe():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.objects):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def box(name, size, loc, material):
    """An axis-aligned box. size/loc are (x, y, z); loc is the box centre."""
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    ob = bpy.context.object
    ob.name = name
    ob.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    ob.data.materials.append(mat(material))
    return ob


def join(objs, name):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.object
    ob.name = name
    return ob


def set_origin(ob, point):
    """Move the origin to a world point without moving the geometry."""
    offset = Vector(point) - ob.location
    for v in ob.data.vertices:
        v.co -= offset
    ob.location = Vector(point)


def flat_shade(ob):
    for p in ob.data.polygons:
        p.use_smooth = False


def triangulate(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(ob.data)
    bm.free()


def export(ob, filename):
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    path = os.path.join(OUT, filename)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        apply_scale_options="FBX_SCALE_NONE",
        axis_forward="-Z", axis_up="Y",       # Blender Z-up -> Unity Y-up
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        bake_space_transform=True,
        add_leaf_bones=False,
        path_mode="COPY",
    )
    return path


# ==========================================================================================
# 1. The jetty — a plank deck. Planks run along Y so the grain points at the water.
# ==========================================================================================
def build_jetty():
    wipe()
    parts = []
    W, D, T = 7.0, 7.0, 0.16

    # A dark base slab, then boards laid over it with gaps. The gaps show the dark through and
    # THAT is what reads as planking. The first pass alternated two wood tones across nine wide
    # boards, which at this scale came out as deck-chair stripes rather than a deck.
    parts.append(box("Base", (W, D, 0.10), (0, 0, 0.05), "Dark"))

    planks, gap = 16, 0.045
    pw = (W - gap * (planks - 1)) / planks
    for i in range(planks):
        x = -W / 2 + pw / 2 + i * (pw + gap)
        parts.append(box(f"Plank{i}", (pw, D, T), (x, 0, 0.10 + T / 2), "Wood"))

    # two boards run crossways at the ends, the way a real deck is trimmed
    for y in (-D / 2 + pw, D / 2 - pw):
        parts.append(box(f"Trim{y}", (W, pw * 1.6, T + 0.02), (0, y, 0.10 + T / 2), "WoodLt"))

    parts.append(box("Kerb", (0.20, D, 0.24), (W / 2 - 0.10, 0, 0.38), "Steel"))

    ob = join(parts, "SM_Harbor_Jetty")
    set_origin(ob, (0, 0, 0))          # base centre, sits on the floor at Y=0 in Unity
    flat_shade(ob); triangulate(ob)
    return ob, len(ob.data.polygons)


# ==========================================================================================
# 2. The bollard — the mooring post that bobs when a ship is home, with a lantern on top.
#    The lantern is the accent colour: it is the one thing the player reads from across
#    the yard, so it gets the only bright material in the set.
# ==========================================================================================
def build_bollard():
    wipe()
    parts = [
        box("Base",    (0.62, 0.62, 0.22), (0, 0, 0.11), "Dark"),
        box("Post",    (0.36, 0.36, 2.10), (0, 0, 1.17), "Wood"),
        box("Collar",  (0.50, 0.50, 0.16), (0, 0, 1.05), "Steel"),   # the rope ring
        box("Cap",     (0.46, 0.46, 0.14), (0, 0, 2.29), "Steel"),
        box("Lantern", (0.34, 0.34, 0.40), (0, 0, 2.56), "Accent"),
        box("Finial",  (0.14, 0.14, 0.16), (0, 0, 2.84), "Steel"),
    ]
    ob = join(parts, "SM_Harbor_Bollard")
    set_origin(ob, (0, 0, 0))
    flat_shade(ob); triangulate(ob)
    return ob, len(ob.data.polygons)


# ==========================================================================================
# 3. The launch — a chunky cargo boat. Bow points -Y (the style guide's "front").
#    The cargo well is left OPEN, the same way ASSETS.md leaves the hopper wagon open, so
#    a bar stack can be dropped into it later without touching this mesh.
# ==========================================================================================
def build_launch():
    """
    A lofted hull, not a pile of scaled cubes.

    The first two passes tapered a primitive cube by shoving its verts around in bmesh. Both
    collapsed: at the bow the taper drove opposite corners through each other, so from the game
    camera the boat read as a few detached flat panels floating over the water. This builds the
    hull the way a hull is actually shaped — one plan outline, lofted down to a keel and in to a
    gunwale — so every face is well-formed by construction and the hold is genuinely open.
    """
    wipe()

    # Plan outline, counter-clockwise from the bow. Bow points -Y, per the style guide.
    half = [(0.00, -3.20),
            (0.58, -2.35),
            (1.10, -0.80),
            (1.10,  2.30),
            (0.86,  3.20)]
    outline = [(-x, y) for x, y in reversed(half[1:])] + half        # port side, then bow, then starboard
    n = len(outline)

    DECK, HOLD = 1.15, 0.42      # gunwale height, and the floor of the hold
    KEEL_X, KEEL_Y = 0.60, 0.94  # how far the hull draws in at the waterline
    RIM = 0.74                   # how far the gunwale sits inboard of the sheer

    bm = bmesh.new()
    keel  = [bm.verts.new((x * KEEL_X, y * KEEL_Y, 0.0))   for x, y in outline]
    sheer = [bm.verts.new((x, y, DECK))                    for x, y in outline]
    rim   = [bm.verts.new((x * RIM, y * RIM, DECK))        for x, y in outline]
    hold  = [bm.verts.new((x * RIM, y * RIM, HOLD))        for x, y in outline]
    bm.verts.ensure_lookup_table()

    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((keel[i],  keel[j],  sheer[j], sheer[i]))   # hull side
        bm.faces.new((sheer[i], sheer[j], rim[j],   rim[i]))     # gunwale cap
        bm.faces.new((rim[j],   rim[i],   hold[i],  hold[j]))    # inside of the hold
    bm.faces.new(list(reversed(keel)))                            # bottom
    bm.faces.new(hold)                                            # floor of the hold

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new("HullMesh")
    bm.to_mesh(me); bm.free()
    hull = bpy.data.objects.new("Hull", me)
    bpy.context.collection.objects.link(hull)
    hull.data.materials.append(mat("Wood"))
    parts = [hull]

    # Everything above the deck is a box, and every one of them sits ON the gunwale.
    parts.append(box("Deck",   (1.70, 1.30, 0.12), (0, 2.30, DECK + 0.06), "WoodLt"))
    parts.append(box("House",  (1.34, 1.02, 0.78), (0, 2.30, DECK + 0.51), "WoodLt"))
    parts.append(box("Window", (1.40, 0.10, 0.26), (0, 1.82, DECK + 0.64), "Dark"))
    parts.append(box("Roof",   (1.56, 1.20, 0.12), (0, 2.30, DECK + 0.96), "Steel"))
    parts.append(box("Funnel", (0.26, 0.26, 0.52), (0, 2.72, DECK + 1.28), "Dark"))
    parts.append(box("Stem",   (0.16, 0.55, 0.55), (0, -3.05, DECK + 0.20), "Steel"))
    parts.append(box("Mast",   (0.12, 0.12, 1.30), (0, -1.35, DECK + 0.65), "Wood"))
    parts.append(box("Lamp",   (0.24, 0.24, 0.26), (0, -1.35, DECK + 1.42), "Accent"))

    ob = join(parts, "SM_Harbor_Launch")
    set_origin(ob, (0, 0, 0))          # centre-bottom, per the vehicle convention
    flat_shade(ob); triangulate(ob)
    return ob, len(ob.data.polygons)


# ==========================================================================================
built = []
for fn, filename in ((build_jetty,   "SM_Harbor_Jetty.fbx"),
                     (build_bollard, "SM_Harbor_Bollard.fbx"),
                     (build_launch,  "SM_Harbor_Launch.fbx")):
    ob, tris = fn()
    dims = tuple(round(d, 2) for d in ob.dimensions)
    path = export(ob, filename)
    built.append((ob.name, tris, dims, len(ob.data.materials), path))

print("\n=== BUILT ===")
for name, tris, dims, slots, path in built:
    print(f"{name:<22} {tris:>5} tris  dims={dims}  materials={slots}")
    print(f"{'':<22} -> {path}")
