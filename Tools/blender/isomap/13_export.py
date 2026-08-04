"""Step 13: bake vertex colours and export the island to the Unity project.

- strips road vehicles and rolling stock (ships and yard machinery stay)
- bakes each procedural material's colour into a FACE_CORNER colour attribute
  so the texture detail survives FBX (Unity gets it as vertex colour)
- exports one FBX per district, per phase, with Blender converting Z-up -> Y-up

World transforms stay baked in, so every FBX dropped at the Unity origin
reassembles the map exactly.

ONLY narrows the run to named groups:

    run("13_export", 1, ONLY=("Terrain", "Roads"))

A terrain tweak then writes one file instead of fifteen and Unity re-imports
one instead of 1500 objects, which is the difference between a 30-second
iteration and a three-minute one. "Vehicles" is a group name here too.
"""
import os
import importlib
import layout
importlib.reload(layout)
L = layout
import bake
importlib.reload(bake)

ONLY = set(globals().get("ONLY") or ())


def wanted(group):
    return not ONLY or group in ONLY

UNITY = "/Users/macbookair/Documents/GitHub/project_kayseri"
# One tree per island. Every group file is named the same on both maps, so
# without the island in the path a copper export silently overwrites coal.
OUT = UNITY + "/Assets/Art/KayseriIsland/Models/%s/Phase%d" % (L.NAME.capitalize(), PHASE)
os.makedirs(OUT, exist_ok=True)

# --------------------------------------------------------------- strip vehicles
# Ships, cranes, loaders, excavators and forklifts stay - they read as scenery.
DROP = ("Truck", "Van", "Train.", "PortTrain", "Loco.", "Wagon", "V.ore",
        "V.cargo", "V.tank", "V.van", "Market.T")
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

# ------------------------------------------------ bake, only what is exported
# Baking every mesh in the scene costs a second or two of pure waste when the
# run is narrowed to one group, and the ground alone is a quarter of a million
# corners. Which objects are wanted is settled below, before anything is baked.
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


# Visible only. collect_vehicles() sweeps the whole Vehicles collection, which
# includes the hidden source objects every dup() is made from - and those sit at
# the origin. They stayed out of the FBX purely because Blender cannot select a
# hidden object, which is not something to rely on.
DRIVEN = [o for o in VEHICLES if not (o.hide_render or o.hide_viewport)]

loco = [o for o in DRIVEN if "Train.loco" in o.name or "Loco." in o.name]
wagons = sorted([o for o in DRIVEN if "Train.wagon" in o.name], key=lambda o: o.name)
# Tagged by the body they were modelled as, not just numbered. CoalOperation
# picks its ore fleet and its cargo fleet by this tag; numbering them all
# "truck_roadN" left it guessing from whichever loop a truck was parked nearest,
# so cargo flatbeds ended up hauling ore and tippers hauling bars.
trucks = sorted([o for o in DRIVEN if o.name.startswith("V.ore")],
                key=lambda o: o.name)
cargo_trucks = sorted([o for o in DRIVEN if o.name.startswith("V.cargo")],
                      key=lambda o: o.name)

veh = []
if loco:
    loco[0].name = "train"
    veh.append(loco[0])
for i, w in enumerate(wagons):
    w.name = "wagon" if i == 0 else "wagon.%03d" % i
    veh.append(w)
for i, t in enumerate(trucks):
    t.name = "truck_road_ore%d" % i
    veh.append(t)
for i, t in enumerate(cargo_trucks):
    t.name = "truck_road_cargo%d" % i
    veh.append(t)

if veh and wanted("Vehicles"):
    bake.bake_objects(veh)
    vpath = "%s/Vehicles_P%d.fbx" % (OUT, PHASE)
    export_selection(vpath, veh)
    print("   vehicles  %4d objs  %6d KB   (%s)"
          % (len(veh), os.path.getsize(vpath) // 1024,
             ", ".join(o.name for o in veh[:6])))
elif not veh:
    print("   WARNING: no vehicle meshes found to export")

removed = strip_vehicles()

# --------------------------------------------------------------------- export
GROUPS = ("Terrain", "Roads", "Rail", "Mine", "Depot", "Refinery", "Market",
          "Port", "Sites", "Props", "Foliage",
          "Power", "Haul", "Fleet", "Civic")

# Settle what this run writes before baking anything: with ONLY narrowing the
# run there is no reason to sample a quarter of a million corners of ground that
# is not going out.
sets = []
for gname in GROUPS:
    col = bpy.data.collections.get(gname)
    if col is None or not wanted(gname):
        continue
    # Empty meshes export as normal-less nodes and make Unity's importer warn
    # ("can't calculate tangents") - skip them.
    objs = [ob for ob in col.objects
            if ob.type == 'MESH' and ob.data and len(ob.data.polygons)]
    if objs:
        sets.append((gname, objs))

baked = bake.bake_objects([ob for _g, objs in sets for ob in objs])

written = []
for gname, objs in sets:
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objs:
        ob.select_set(True)
        view.objects.active = ob
    path = "%s/%s_P%d.fbx" % (OUT, gname, PHASE)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'},
        apply_scale_options='FBX_SCALE_NONE', global_scale=1.0,
        axis_forward='-Z', axis_up='Y', bake_space_transform=False,
        use_mesh_modifiers=True, mesh_smooth_type='FACE', use_triangles=True,
        colors_type='SRGB', prioritize_active_color=True,
        path_mode='STRIP', use_custom_props=False,
        use_active_collection=False)
    written.append((gname, len(objs), os.path.getsize(path) // 1024))

# ------------------------------------------------------------------- palette
# IslandBuilder.CreateMaterials builds one URP material per palette entry, and
# the FBX importer remaps by NAME onto those. A material with no entry gets no
# Unity material, so the mesh lands on the default grey Lit one - which does not
# read vertex colour at all, so it stays grey. The copper island brings ore_cu
# and ore_cu_shiny with it, and a third island will bring its own.
#
# Merge, never rewrite: an entry that already exists is left exactly as it is.
# The colour here is only the flat fallback (Toggle Flat Colours) - the shader
# runs on the baked vertex colour, so an entry that exists is already right.
PAL = UNITY + "/Assets/Art/KayseriIsland/palette.json"


def srgb(c):
    c = max(0.0, min(1.0, float(c)))
    return round(c * 12.92 if c <= 0.0031308
                 else 1.055 * c ** (1.0 / 2.4) - 0.055, 5)


def socket(bsdf, name, fallback):
    s = bsdf.inputs.get(name)
    if s is None or s.is_linked:
        return fallback
    v = s.default_value
    return list(v)[:3] if hasattr(v, "__len__") else float(v)


def entry(m):
    bsdf = None
    for n in m.node_tree.nodes:
        if n.bl_idname == "ShaderNodeBsdfPrincipled":
            bsdf = n
            break
    if bsdf is None:
        return None
    ramp = bake.ramp_of(m)
    col = list(ramp.evaluate(0.5))[:3] if ramp else socket(bsdf, "Base Color", [0.5] * 3)
    return {"name": m.name,
            "color": [srgb(c) for c in col],
            "metallic": round(socket(bsdf, "Metallic", 0.0), 4),
            "smoothness": round(1.0 - socket(bsdf, "Roughness", 0.5), 4),
            "alpha": round(socket(bsdf, "Alpha", 1.0), 4),
            "emission": round(socket(bsdf, "Emission Strength", 0.0), 4),
            "emissionColor": [round(c, 5) for c in socket(bsdf, "Emission Color", [1.0] * 3)]}


import json

with open(PAL) as f:
    pal = json.load(f)
have = {e["name"] for e in pal["materials"]}
added = []
for m in bpy.data.materials:
    if m.name in have or not m.use_nodes:
        continue
    e = entry(m)
    if e is not None:
        pal["materials"].append(e)
        have.add(m.name)
        added.append(m.name)
if added:
    pal["materials"].sort(key=lambda e: e["name"])
    with open(PAL, "w") as f:
        json.dump(pal, f, indent=1)

print("phase %d: removed %d vehicle/source objects, baked %d meshes%s"
      % (PHASE, removed, baked, "  ONLY " + ", ".join(sorted(ONLY)) if ONLY else ""))
for g, n, kb in written:
    print("   %-9s %4d objs  %6d KB" % (g, n, kb))
print("export dir:", OUT)
print("palette: %d entries%s" % (len(pal["materials"]),
                                 ", added " + ", ".join(added) if added else ""))
