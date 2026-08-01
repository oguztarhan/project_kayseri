"""Preview / final render helper."""
import bpy

OUT = "/Users/macbookair/Documents/GitHub/project_kayseri/Tools/blender/isomap/out"


def zoom(name, target, scale=90.0, pct=40, samples=12):
    """Render a close-up on a world point, then restore the wide framing."""
    from mathutils import Vector
    import math
    sc = bpy.context.scene
    cam = sc.camera
    old_loc = tuple(cam.location)
    old_sc = cam.data.ortho_scale
    elev = math.radians(48.0)
    vd = Vector((-0.5, 0.5, 0.0)).normalized() * math.cos(elev)
    vd.z = -math.sin(elev)
    cam.location = Vector((target[0], target[1], target[2] if len(target) > 2
                           else 0.0)) - vd * 700.0
    cam.data.ortho_scale = scale
    out = shot(name, pct, samples)
    cam.location = old_loc
    cam.data.ortho_scale = old_sc
    return out


def shot(name="preview", pct=32, samples=12):
    sc = bpy.context.scene
    old_pct = sc.render.resolution_percentage
    old_s = getattr(sc.eevee, "taa_render_samples", None)
    sc.render.resolution_percentage = pct
    if old_s is not None:
        sc.eevee.taa_render_samples = samples
    sc.render.image_settings.file_format = 'PNG'
    sc.render.filepath = "%s/%s.png" % (OUT, name)
    bpy.ops.render.render(write_still=True)
    sc.render.resolution_percentage = old_pct
    if old_s is not None:
        sc.eevee.taa_render_samples = old_s
    return sc.render.filepath + " @%d%%" % pct
