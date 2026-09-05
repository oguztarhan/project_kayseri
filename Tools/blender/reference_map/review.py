"""Render an additional detail camera and verify the saved review asset."""
import bpy, json, math, sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'design'/'focus-ladder-blender'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Focus_Ladder_Island.blend'))
sc=bpy.context.scene
overview=sc.camera
detail=bpy.data.objects.get('Factory_Detail_Camera')
if detail is None:
    detail=overview.copy(); detail.data=overview.data.copy(); detail.name='Factory_Detail_Camera'
    bpy.data.collections['90_Camera_and_Lighting'].objects.link(detail)
K=math.sqrt(.5)
target=Vector((56*1.3459*K,-56*1.3459*K,20))
detail.location=target+detail.rotation_euler.to_matrix()@Vector((0,0,340)); detail.data.ortho_scale=69
sc.camera=detail; sc.render.resolution_x=1100; sc.render.resolution_y=1200; sc.render.resolution_percentage=100
sc.cycles.samples=48
bpy.data.collections['09_Reference_Markers_Toggle'].hide_render=True
sc.render.filepath=str(OUT/'02-factory-detail.png')
if '--verify-only' not in sys.argv: bpy.ops.render.render(write_still=True)
bpy.data.collections['09_Reference_Markers_Toggle'].hide_render=False
sc.camera=overview; sc.render.resolution_x=1080; sc.render.resolution_y=2276
sc.render.filepath=str(OUT/'01-portrait-preview.png')
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active; space.region_3d.view_perspective='CAMERA'; space.clip_end=2000
            space.shading.type='MATERIAL'; space.shading.use_scene_world=False; space.shading.studiolight_rotate_z=.4
            space.overlay.show_overlays=False
            space.region_3d.view_camera_zoom=0
reference=Path('/var/folders/bx/z71j3z61067107ssjhks3w4r0000gn/T/codex-clipboard-a4b35e39-a86e-4cc8-bc03-f6166d322706.png')
if reference.exists():
    im=bpy.data.images.load(str(reference),check_existing=True); im.name='REFERENCE — Focus Ladder'; im.use_fake_user=True; im.pack()
stats={'objects':len(sc.objects),'mesh_objects':sum(o.type=='MESH' for o in sc.objects),
 'mesh_faces':sum(len(o.data.polygons) for o in sc.objects if o.type=='MESH'),
 'materials':len(bpy.data.materials),'collections':[c.name for c in sc.collection.children],
 'missing_external_images':[im.filepath for im in bpy.data.images if im.source=='FILE' and not im.packed_file and not Path(bpy.path.abspath(im.filepath)).exists()],
 'nonfinite_transforms':[o.name for o in sc.objects if not all(math.isfinite(v) for row in o.matrix_world for v in row)],
 'unity_imported':False,'reference_image_packed':any(im.packed_file for im in bpy.data.images if im.name.startswith('REFERENCE'))}
assert not stats['missing_external_images']
assert not stats['nonfinite_transforms']
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Focus_Ladder_Island.blend'))
(OUT/'scene-report.json').write_text(json.dumps(stats,indent=2))
print('VERIFIED',json.dumps(stats))
