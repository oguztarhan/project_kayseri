"""Step 13: bake vertex colours and export the island to the Unity project.

- strips road vehicles and rolling stock (ships and yard machinery stay)
- bakes each procedural material's colour into a FACE_CORNER colour attribute
  so the texture detail survives FBX (Unity gets it as vertex colour)
- exports one FBX per district, per phase, with Blender converting Z-up -> Y-up

World transforms stay baked in, so every FBX dropped at the Unity origin
reassembles the map exactly.
"""
import os
import importlib
import layout
importlib.reload(layout)
L = layout

try:
    from mathutils import noise as mnoise
except Exception:
    mnoise = None

UNITY = "/Users/macbookair/Documents/GitHub/project_kayseri"
OUT = UNITY + "/Assets/Art/KayseriIsland/Models/Phase%d" % PHASE
os.makedirs(OUT, exist_ok=True)

# --------------------------------------------------------------- strip vehicles
# Ships, cranes, loaders, excavators and forklifts stay - they read as scenery.
DROP = ("Truck", "Van", "Train.", "PortTrain", "Loco.", "Wagon", "V.ore",
        "V.cargo", "V.empty", "V.tank", "V.van", "Market.T")
KEEP = ("Ship", "Boat", "Tug", "Crane", "Gantry", "Loader", "Excav", "Fork")


def is_vehicle(name):
    if any(k in name for k in KEEP):
        return False
    return any(k in name for k in DROP)


def collect_vehicles():
    """Vehicle meshes, gathered before the strip below deletes them."""
    out = []
    for ob in bpy.data.objects:
        if ob.type != 'MESH' or ob.data is None or not len(ob.data.polygons):
            continue
        if ob.hide_render or ob.hide_viewport:
            continue
        if is_vehicle(ob.name):
            out.append(ob)
    vc = bpy.data.collections.get("Vehicles")
    if vc:
        for ob in vc.objects:
            if ob.type == 'MESH' and ob.data and len(ob.data.polygons) \
                    and ob not in out:
                out.append(ob)
    return out


VEHICLES = collect_vehicles()


def strip_vehicles():
    n = 0
    # By identity, not by name: the export above renames these to train/wagon/truck_roadN
    # for Unity, after which is_vehicle() no longer recognises them and a second copy of
    # the whole train was surviving into the Rail district as scenery.
    for ob in list(VEHICLES):
        try:
            bpy.data.objects.remove(ob, do_unlink=True)
            n += 1
        except ReferenceError:
            pass

    for ob in list(bpy.data.objects):
        if ob.type != 'MESH':
            continue
        # hidden source/master objects sit at the origin and must never export
        if ob.hide_render or ob.hide_viewport or is_vehicle(ob.name):
            bpy.data.objects.remove(ob, do_unlink=True)
            n += 1
    vc = bpy.data.collections.get("Vehicles")
    if vc:
        for ob in list(vc.objects):
            bpy.data.objects.remove(ob, do_unlink=True)
            n += 1
    return n

# ------------------------------------------------------- material colour model
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
    """(kind, scale, detail, wave_dir, ramp) - cached per material."""
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


# --------------------------------------------------------------- bake to mesh
def bake_vertex_colours(me):
    if not me.polygons:
        return
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
            return
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


done = set()
for ob in bpy.data.objects:
    if ob.type == 'MESH' and ob.data and ob.data.name not in done:
        done.add(ob.data.name)
        bake_vertex_colours(ob.data)

# ------------------------------------------------------------------ vehicles
# Exported before the strip below removes them. The gameplay layer drives these,
# so they are renamed to the names CoalOperation resolves under the island root:
# one "train", "wagon"/"wagon.NNN" for the rake, and "truck_roadN" for the fleet.
# Positions are kept - the operation assigns each truck to the route it is
# parked nearest.
view = bpy.context.view_layer


def export_selection(path, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objs:
        ob.select_set(True)
        view.objects.active = ob
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'},
        apply_scale_options='FBX_SCALE_NONE', global_scale=1.0,
        axis_forward='-Z', axis_up='Y', bake_space_transform=False,
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True,
        colors_type='SRGB', prioritize_active_color=True,
        path_mode='STRIP', use_custom_props=False, use_active_collection=False)


loco = [o for o in VEHICLES if "Train.loco" in o.name or "Loco." in o.name]
wagons = sorted([o for o in VEHICLES if "Train.wagon" in o.name], key=lambda o: o.name)
trucks = sorted([o for o in VEHICLES if o.name.startswith(("V.ore", "V.cargo"))],
                key=lambda o: o.name)

veh = []
if loco:
    loco[0].name = "train"
    veh.append(loco[0])
for i, w in enumerate(wagons):
    w.name = "wagon" if i == 0 else "wagon.%03d" % i
    veh.append(w)
for i, t in enumerate(trucks):
    t.name = "truck_road%d" % i
    veh.append(t)

if veh:
    vpath = "%s/Vehicles_P%d.fbx" % (OUT, PHASE)
    export_selection(vpath, veh)
    print("   vehicles  %4d objs  %6d KB   (%s)"
          % (len(veh), os.path.getsize(vpath) // 1024,
             ", ".join(o.name for o in veh[:6])))
else:
    print("   WARNING: no vehicle meshes found to export")

removed = strip_vehicles()

# --------------------------------------------------------------------- export
GROUPS = ("Terrain", "Roads", "Rail", "Mine", "Depot", "Refinery", "Market",
          "Port", "Sites", "Props", "Foliage")

written = []
for gname in GROUPS:
    col = bpy.data.collections.get(gname)
    if col is None or not len(col.objects):
        continue
    bpy.ops.object.select_all(action='DESELECT')
    n = 0
    for ob in col.objects:
        # Empty meshes export as normal-less nodes and make Unity's importer
        # warn ("can't calculate tangents") - skip them.
        if ob.type != 'MESH' or ob.data is None or not len(ob.data.polygons):
            continue
        ob.select_set(True)
        view.objects.active = ob
        n += 1
    if n == 0:
        continue
    path = "%s/%s_P%d.fbx" % (OUT, gname, PHASE)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'},
        apply_scale_options='FBX_SCALE_NONE', global_scale=1.0,
        axis_forward='-Z', axis_up='Y', bake_space_transform=False,
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True,
        colors_type='SRGB', prioritize_active_color=True,
        path_mode='STRIP', use_custom_props=False,
        use_active_collection=False)
    written.append((gname, n, os.path.getsize(path) // 1024))

print("phase %d: removed %d vehicle/source objects" % (PHASE, removed))
for g, n, kb in written:
    print("   %-9s %4d objs  %6d KB" % (g, n, kb))
print("export dir:", OUT)
