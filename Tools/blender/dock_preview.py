"""Render the dock set from the game's own camera angle (45 degree pitch, 45 degree yaw)."""
import bpy, os, sys, math
from mathutils import Vector

OUT = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else "/tmp"

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)

# import the three exports back in, laid out the way MarketYardScene places them
for name, loc in (("SM_Harbor_Jetty",   (0, 0, 0)),
                  ("SM_Harbor_Bollard", (0, 0, 0.6)),
                  ("SM_Harbor_Launch",  (5.2, 0, -0.18))):
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"))
    ob = bpy.context.selected_objects[0]
    ob.location = loc

# ground
bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, -0.01))
g = bpy.context.object
m = bpy.data.materials.new("Ground")
m.use_nodes = True
m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.14, 0.15, 0.18, 1)
g.data.materials.append(m)

# water on the +X side, so the boat reads as moored rather than beached
bpy.ops.mesh.primitive_plane_add(size=40, location=(24, 0, 0.34))
w = bpy.context.object
wm = bpy.data.materials.new("Water")
wm.use_nodes = True
wm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.10, 0.34, 0.46, 1)
w.data.materials.append(wm)

# warm key light + soft fill, matching the "bright, friendly" brief
bpy.ops.object.light_add(type="SUN", location=(6, -8, 12))
sun = bpy.context.object
sun.data.energy = 4.0
sun.data.angle = math.radians(12)
sun.rotation_euler = (math.radians(50), 0, math.radians(35))
bpy.context.scene.world.use_nodes = True
bpy.context.scene.world.node_tree.nodes["Background"].inputs[0].default_value = (0.45, 0.55, 0.68, 1)
bpy.context.scene.world.node_tree.nodes["Background"].inputs[1].default_value = 0.7

# camera: the game's isometric-ish framing
target = Vector((1.6, 0, 0.7))
dist = 27.0
pitch = math.radians(45)
yaw = math.radians(45)
cam_loc = target + Vector((
    dist * math.cos(pitch) * math.sin(yaw),
    -dist * math.cos(pitch) * math.cos(yaw),
    dist * math.sin(pitch)))
bpy.ops.object.camera_add(location=cam_loc)
cam = bpy.context.object
direction = target - cam.location
cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
cam.data.lens = 55
bpy.context.scene.camera = cam

sc = bpy.context.scene
sc.render.engine = "BLENDER_EEVEE"
sc.render.resolution_x = 1100
sc.render.resolution_y = 700
sc.render.film_transparent = False
sc.render.filepath = os.path.join(OUT, "preview.png")
bpy.ops.render.render(write_still=True)
print("rendered ->", sc.render.filepath)
