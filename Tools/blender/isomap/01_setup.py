"""Step 1: clean slate, isometric camera, lighting, world, material palette."""
import importlib as _il
import layout
_il.reload(layout)
L = layout

clear_scene()

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 2400
scene.render.resolution_y = 1600
scene.render.resolution_percentage = 100
scene.render.film_transparent = False

# ------------------------------------------------------------------ eevee quality
ev = scene.eevee
for attr, val in (("taa_render_samples", 128), ("taa_samples", 16),
                  ("use_shadows", True), ("use_raytracing", True),
                  ("use_volumetric_lights", True), ("shadow_ray_count", 2),
                  ("shadow_step_count", 6), ("use_gtao", True),
                  ("gtao_distance", 2.0), ("use_bloom", False),
                  ("use_volumetric_lights", False)):
    if hasattr(ev, attr):
        try:
            setattr(ev, attr, val)
        except Exception:
            pass

# ------------------------------------------------------------ colour management
vs = scene.view_settings
try:
    vs.view_transform = 'AgX'
except Exception:
    pass
for look in ("AgX - Punchy", "Punchy", "AgX - Medium High Contrast", "None"):
    try:
        vs.look = look
        break
    except Exception:
        continue
vs.exposure = 0.30
vs.gamma = 1.0

# --------------------------------------------------------------------- camera
cam_data = bpy.data.cameras.new("IsoCam")
cam_data.type = 'ORTHO'
cam_data.ortho_scale = L.ORTHO
cam_data.clip_start = 1.0
cam_data.clip_end = 3000.0
cam = bpy.data.objects.new("IsoCam", cam_data)
# elevation 48 deg above the horizon, yaw 45 deg -> classic game isometric
ELEV = 48.0
cam.rotation_euler = Euler((radians(90.0 - ELEV), 0.0, radians(45.0)), 'XYZ')
D = 700.0
vd = Vector((-0.5, 0.5, 0.0)).normalized() * cos(radians(ELEV))
vd.z = -sin(radians(ELEV))
cam.location = -vd * D
scene.collection.objects.link(cam)
scene.camera = cam

# ---------------------------------------------------------------------- lights
sun_d = bpy.data.lights.new("Sun", 'SUN')
sun_d.energy = 3.5
sun_d.color = (1.0, 0.945, 0.845)
sun_d.angle = radians(2.5)
sun = bpy.data.objects.new("Sun", sun_d)
# key light from screen upper-left, shadows fall down-right like the reference
sun.rotation_euler = Euler((radians(46), 0.0, radians(232)), 'XYZ')
sun.location = (0, 0, 200)
scene.collection.objects.link(sun)

fill_d = bpy.data.lights.new("Fill", 'SUN')
fill_d.energy = 0.38
fill_d.color = (0.80, 0.87, 1.0)
fill_d.angle = radians(30)
fill = bpy.data.objects.new("Fill", fill_d)
fill.rotation_euler = Euler((radians(58), 0.0, radians(40)), 'XYZ')
fill.location = (0, 0, 200)
scene.collection.objects.link(fill)

# ----------------------------------------------------------------------- world
world = bpy.data.worlds.get("World") or bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
nt = world.node_tree
nt.nodes.clear()
out = nt.nodes.new("ShaderNodeOutputWorld")
bg = nt.nodes.new("ShaderNodeBackground")
sky = nt.nodes.new("ShaderNodeTexSky")
try:
    sky.sky_type = 'NISHITA'
    sky.sun_elevation = radians(46)
    sky.sun_rotation = radians(232)
    sky.altitude = 0
    sky.air_density = 1.0
    sky.dust_density = 0.6
except Exception:
    pass
bg.inputs["Strength"].default_value = 0.42
nt.links.new(sky.outputs[0], bg.inputs["Color"])
nt.links.new(bg.outputs[0], out.inputs["Surface"])

# ------------------------------------------------------------- material palette
# Procedurally textured, saturated tycoon palette (see tex.py).
import importlib
import tex
importlib.reload(tex)
T, F = tex.ptex, tex.flat

# ---- terrain -----------------------------------------------------------------
T("grass", [(0.20, (0.075, 0.230, 0.062)), (0.48, (0.150, 0.380, 0.090)),
            (0.78, (0.250, 0.470, 0.115))],
  rough=0.92, scale=0.055, detail=7.0, bump=0.16, bump_dist=0.5)
T("grass_dry", [(0.22, (0.230, 0.360, 0.100)), (0.60, (0.370, 0.440, 0.140)),
                (0.85, (0.470, 0.500, 0.180))],
  rough=0.93, scale=0.075, detail=6.0, bump=0.16, bump_dist=0.5)
T("dirt", [(0.20, (0.155, 0.098, 0.052)), (0.55, (0.245, 0.160, 0.086)),
           (0.85, (0.330, 0.230, 0.130))],
  rough=0.95, scale=0.16, detail=6.0, bump=0.28, bump_dist=0.25)
T("rock", [(0.18, (0.155, 0.152, 0.150)), (0.50, (0.265, 0.258, 0.242)),
           (0.82, (0.390, 0.378, 0.352))],
  rough=0.88, scale=0.30, detail=7.0, bump=0.45, bump_dist=0.30)
T("rock_dark", [(0.20, (0.088, 0.088, 0.092)), (0.55, (0.150, 0.148, 0.146)),
                (0.85, (0.225, 0.220, 0.212))],
  rough=0.90, scale=0.32, detail=7.0, bump=0.45, bump_dist=0.30)
T("cliff", [(0.18, (0.235, 0.222, 0.198)), (0.52, (0.345, 0.328, 0.292)),
            (0.85, (0.470, 0.450, 0.405))],
  rough=0.90, scale=0.26, detail=7.0, bump=0.42, bump_dist=0.30)
T("gravel", [(0.15, (0.115, 0.108, 0.098)), (0.55, (0.190, 0.180, 0.163)),
             (0.88, (0.275, 0.262, 0.238))],
  rough=0.95, kind="voronoi", scale=1.5, bump=0.35, bump_dist=0.09)
T("sand", [(0.22, (0.330, 0.268, 0.180)), (0.60, (0.440, 0.372, 0.258)),
           (0.88, (0.540, 0.470, 0.340))],
  rough=0.95, scale=0.24, detail=5.0, bump=0.22, bump_dist=0.2)

# ---- paved surfaces ----------------------------------------------------------
T("asphalt", [(0.25, (0.036, 0.037, 0.041)), (0.62, (0.058, 0.060, 0.066)),
              (0.88, (0.082, 0.084, 0.090))],
  rough=0.80, rough_hi=0.92, scale=1.1, detail=6.0, bump=0.14, bump_dist=0.05)
T("asphalt_lt", [(0.25, (0.062, 0.064, 0.070)), (0.65, (0.092, 0.094, 0.101)),
                 (0.90, (0.125, 0.128, 0.136))],
  rough=0.78, rough_hi=0.90, scale=1.1, detail=6.0, bump=0.14, bump_dist=0.05)
T("concrete", [(0.22, (0.290, 0.286, 0.276)), (0.60, (0.375, 0.370, 0.356)),
               (0.88, (0.455, 0.448, 0.430))],
  rough=0.86, scale=0.65, detail=5.0, bump=0.15, bump_dist=0.08)
T("concrete_dk", [(0.22, (0.150, 0.149, 0.145)), (0.62, (0.205, 0.203, 0.197)),
                  (0.90, (0.262, 0.258, 0.250))],
  rough=0.88, scale=0.65, detail=5.0, bump=0.16, bump_dist=0.08)
F("kerb", (0.46, 0.455, 0.44), rough=0.8)

# ---- coal & industry ---------------------------------------------------------
T("coal", [(0.10, (0.0055, 0.0055, 0.0070)), (0.55, (0.0110, 0.0110, 0.0135)),
           (0.90, (0.0195, 0.0195, 0.0235))],
  rough=0.60, rough_hi=0.42, kind="voronoi", scale=0.34, bump=0.30,
  bump_dist=0.22, spec=0.35)
T("coal_shiny", [(0.15, (0.0085, 0.0085, 0.0105)), (0.60, (0.0155, 0.0155, 0.0185)),
                 (0.90, (0.0265, 0.0265, 0.0315))],
  rough=0.52, rough_hi=0.36, kind="voronoi", scale=0.9, bump=0.16,
  bump_dist=0.07, spec=0.35)
# Copper ore, for the copper island. Malachite green with azurite in the
# shadows and iron oxide on the weathered faces - the same shapes as the coal
# stockpiles, so a heap reads instantly as "not coal" without new geometry.
T("ore_cu", [(0.10, (0.018, 0.052, 0.048)), (0.40, (0.038, 0.135, 0.108)),
             (0.68, (0.085, 0.235, 0.165)), (0.90, (0.185, 0.105, 0.042))],
  rough=0.72, rough_hi=0.50, kind="voronoi", scale=0.34, bump=0.32,
  bump_dist=0.22, spec=0.40)
# Haematite, for the iron island. Dark red-brown with a rust bloom on the
# weathered faces and a steely glint in the fresh ore - the same shapes as the
# other two islands' stockpiles, reading as neither coal nor malachite.
T("ore_fe", [(0.10, (0.055, 0.022, 0.016)), (0.42, (0.135, 0.048, 0.030)),
             (0.70, (0.245, 0.088, 0.048)), (0.90, (0.180, 0.150, 0.140))],
  rough=0.74, rough_hi=0.52, kind="voronoi", scale=0.34, bump=0.32,
  bump_dist=0.22, spec=0.40)
T("ore_fe_shiny", [(0.12, (0.080, 0.032, 0.024)), (0.46, (0.185, 0.068, 0.042)),
                   (0.74, (0.320, 0.120, 0.062)), (0.92, (0.245, 0.212, 0.196))],
  rough=0.58, rough_hi=0.40, kind="voronoi", scale=0.9, bump=0.18,
  bump_dist=0.07, spec=0.45)

T("ore_cu_shiny", [(0.12, (0.030, 0.082, 0.072)), (0.46, (0.060, 0.180, 0.140)),
                   (0.74, (0.120, 0.290, 0.200)), (0.92, (0.235, 0.140, 0.058))],
  rough=0.58, rough_hi=0.40, kind="voronoi", scale=0.9, bump=0.18,
  bump_dist=0.07, spec=0.45)

# Gold-bearing quartz, for the gold island. Mostly pale vein quartz - raw gold
# ore is rock with colour in it, not bullion - with warm ochre staining and a
# metallic fleck in the sun. The same heap shapes as every other ore, reading
# as "pay dirt" next to the island's straw ground.
T("ore_au", [(0.10, (0.155, 0.145, 0.125)), (0.42, (0.300, 0.272, 0.220)),
             (0.70, (0.475, 0.400, 0.240)), (0.90, (0.620, 0.465, 0.145))],
  rough=0.66, rough_hi=0.46, kind="voronoi", scale=0.34, bump=0.32,
  bump_dist=0.22, spec=0.55)
T("ore_au_shiny", [(0.12, (0.240, 0.185, 0.065)), (0.46, (0.430, 0.310, 0.095)),
                   (0.74, (0.650, 0.470, 0.135)), (0.92, (0.830, 0.620, 0.200))],
  rough=0.42, rough_hi=0.30, kind="voronoi", scale=0.9, bump=0.16,
  bump_dist=0.07, spec=0.6, metal=0.35)
# Gold island signature pieces: poured bullion for the strong-room stack, and
# weathered sluice timber for the river workings - greyer than "wood", which
# reads as fresh-sawn.
T("bullion", [(0.25, (0.520, 0.360, 0.075)), (0.62, (0.720, 0.520, 0.130)),
              (0.90, (0.870, 0.680, 0.220))],
  rough=0.26, rough_hi=0.38, metal=0.75, scale=1.3, bump=0.06, bump_dist=0.03)
T("sluice_wood", [(0.22, (0.150, 0.128, 0.100)), (0.60, (0.235, 0.205, 0.162)),
                  (0.88, (0.320, 0.285, 0.230))],
  rough=0.9, scale=0.8, detail=5.0, bump=0.2, bump_dist=0.08)

# ---- island signature ---------------------------------------------------------
# Materials that exist only to make one island read as ITSELF - see 16_theme.py.
# They are in the shared palette rather than per-island because 13_export.py
# merges the palette across islands anyway, and a third island will want to pick
# from the same shelf.
#
# Coal: fired brick for the coke ovens, and the sooty wash that gathers wherever
# the stuff is handled.
T("brick", [(0.20, (0.150, 0.062, 0.040)), (0.55, (0.245, 0.098, 0.058)),
            (0.88, (0.320, 0.150, 0.100))],
  rough=0.86, rough_hi=0.70, scale=0.55, bump=0.26, bump_dist=0.10)
F("soot", (0.048, 0.045, 0.044), rough=0.92)
# Copper: the green liquor in the leach ponds, its crusted rim, and cathode
# plate - the one place on the island the metal itself is on show.
F("leach", (0.040, 0.250, 0.165), rough=0.20, spec=0.75)
F("crust", (0.480, 0.510, 0.420), rough=0.95)
# Blue kept near zero, like "orange" and "red" beside it. The first pass at this
# sat around (0.68, 0.33, 0.15), which is a perfectly good copper on paper and
# comes out of AgX as pale peach - the plate stacks read as cardboard.
T("copper_plate", [(0.22, (0.300, 0.085, 0.018)), (0.60, (0.560, 0.190, 0.038)),
                   (0.90, (0.760, 0.330, 0.075))],
  rough=0.38, rough_hi=0.52, metal=0.45, scale=1.2, bump=0.08, bump_dist=0.04)
F("verdigris", (0.055, 0.310, 0.235), rough=0.72)

T("steel", [(0.25, (0.300, 0.316, 0.340)), (0.65, (0.395, 0.412, 0.436)),
            (0.90, (0.480, 0.498, 0.522))],
  rough=0.42, rough_hi=0.55, metal=0.55, scale=0.9, bump=0.10, bump_dist=0.05)
T("steel_dk", [(0.25, (0.115, 0.121, 0.132)), (0.65, (0.162, 0.169, 0.182)),
               (0.90, (0.212, 0.220, 0.236))],
  rough=0.50, rough_hi=0.62, metal=0.5, scale=0.9, bump=0.10, bump_dist=0.05)
T("steel_lt", [(0.25, (0.510, 0.528, 0.552)), (0.65, (0.615, 0.632, 0.656)),
               (0.90, (0.700, 0.716, 0.738))],
  rough=0.35, rough_hi=0.48, metal=0.45, scale=0.9, bump=0.09, bump_dist=0.05)
T("rust", [(0.20, (0.215, 0.092, 0.036)), (0.58, (0.330, 0.148, 0.056)),
           (0.88, (0.430, 0.215, 0.095))],
  rough=0.85, scale=0.75, detail=6.0, bump=0.30, bump_dist=0.08)
T("wood", [(0.20, (0.135, 0.075, 0.036)), (0.60, (0.195, 0.112, 0.055)),
           (0.90, (0.255, 0.155, 0.082))],
  rough=0.90, kind="wave", scale=2.2, distortion=3.0, bump=0.22, bump_dist=0.05)
T("wood_lt", [(0.20, (0.290, 0.180, 0.090)), (0.60, (0.390, 0.255, 0.132)),
              (0.90, (0.480, 0.330, 0.185))],
  rough=0.88, kind="wave", scale=2.0, distortion=3.0, bump=0.22, bump_dist=0.05)

# ---- corrugated roofs & clad walls -------------------------------------------
for rn, c0, c1 in (("roof_red", (0.395, 0.062, 0.042), (0.560, 0.115, 0.075)),
                   ("roof_grey", (0.115, 0.122, 0.136), (0.190, 0.200, 0.218)),
                   ("roof_blue", (0.048, 0.135, 0.310), (0.095, 0.215, 0.430)),
                   ("roof_teal", (0.035, 0.190, 0.200), (0.075, 0.290, 0.300)),
                   ("roof_orange", (0.560, 0.190, 0.030), (0.720, 0.300, 0.060)),
                   ("roof_green", (0.075, 0.230, 0.105), (0.130, 0.330, 0.160))):
    T(rn, [(0.12, c0), (0.88, c1)], rough=0.66, rough_hi=0.78, kind="wave",
      scale=1.35, wave_dir="X", bump=0.30, bump_dist=0.05, metal=0.25)
T("clad", [(0.12, (0.400, 0.410, 0.425)), (0.88, (0.545, 0.556, 0.572))],
  rough=0.60, kind="wave", scale=1.6, wave_dir="Z", bump=0.26, bump_dist=0.05,
  metal=0.3)

# ---- flat brand colours (vehicles, trim, plastics) ---------------------------
for fn, c, r in (("yellow", (0.700, 0.440, 0.030), 0.48),
                 ("yellow_lt", (0.860, 0.610, 0.060), 0.44),
                 ("orange", (0.720, 0.215, 0.028), 0.44),
                 ("red", (0.520, 0.048, 0.040), 0.48),
                 ("white", (0.800, 0.800, 0.790), 0.52),
                 ("offwhite", (0.640, 0.632, 0.605), 0.62),
                 ("cream", (0.720, 0.650, 0.500), 0.60),
                 ("blue", (0.055, 0.150, 0.360), 0.48),
                 ("blue_lt", (0.130, 0.310, 0.520), 0.48),
                 ("teal", (0.040, 0.270, 0.290), 0.48),
                 ("green_ind", (0.060, 0.230, 0.120), 0.55),
                 ("purple", (0.180, 0.090, 0.250), 0.50),
                 ("tarp", (0.360, 0.335, 0.285), 0.90)):
    F(fn, c, rough=r)

F("metal_gal", (0.58, 0.60, 0.63), rough=0.30, metal=1.0)
F("metal_dark", (0.21, 0.22, 0.24), rough=0.42, metal=1.0)
F("chrome", (0.74, 0.76, 0.80), rough=0.18, metal=1.0)
F("copper", (0.50, 0.22, 0.10), rough=0.35, metal=1.0)
F("glass", (0.28, 0.48, 0.60), rough=0.08, spec=0.95, alpha=0.40)
F("foam", (0.90, 0.94, 0.97), rough=0.55)
F("linepaint", (0.80, 0.78, 0.70), rough=0.60)
F("linepaint_y", (0.78, 0.58, 0.10), rough=0.60)
F("headlight", (1.0, 0.94, 0.75), emis=(1.0, 0.92, 0.70), emis_str=9.0)
F("taillight", (0.8, 0.05, 0.03), emis=(1.0, 0.10, 0.05), emis_str=7.0)
F("winlight", (1.0, 0.86, 0.58), emis=(1.0, 0.82, 0.50), emis_str=3.6)
F("lamp_glow", (1.0, 0.93, 0.72), emis=(1.0, 0.90, 0.66), emis_str=6.0)
F("smoke", (0.86, 0.87, 0.89), rough=1.0, alpha=0.24)
# locked-plot ghost preview + hazard striping for buildable sites
F("ghost", (0.20, 0.62, 0.85), rough=0.3, alpha=0.30,
  emis=(0.25, 0.70, 0.95), emis_str=1.4)
T("hazard", [(0.42, (0.780, 0.560, 0.045)), (0.58, (0.045, 0.045, 0.050))],
  rough=0.62, kind="wave", scale=0.75, wave_dir="DIAGONAL", bump=0.05,
  interp='CONSTANT')

# ---- water -------------------------------------------------------------------
T("water", [(0.25, (0.014, 0.082, 0.152)), (0.70, (0.026, 0.130, 0.222)),
            (0.92, (0.044, 0.180, 0.285))],
  rough=0.16, rough_hi=0.08, scale=0.30, detail=3.0, distortion=0.5,
  bump=0.11, bump_dist=0.05, spec=0.85)
# Ocean: shallow turquoise grading to deep blue, gentle swell in the normal.
T("sea", [(0.20, (0.010, 0.078, 0.150)), (0.55, (0.022, 0.150, 0.230)),
          (0.80, (0.045, 0.245, 0.310)), (0.95, (0.090, 0.330, 0.375))],
  rough=0.10, rough_hi=0.04, scale=0.055, detail=4.0, distortion=0.8,
  bump=0.09, bump_dist=0.30, spec=1.0)
T("seabed", [(0.20, (0.115, 0.135, 0.115)), (0.60, (0.185, 0.200, 0.160)),
             (0.90, (0.260, 0.265, 0.205))],
  rough=0.94, scale=0.14, detail=5.0, bump=0.22, bump_dist=0.25)

# The inside of a tunnel. Near-black and fully rough, so it takes no highlight
# and reads as an opening rather than as a dark surface - see bore() in 04_rail.
mat("tunnel_void", (0.006, 0.006, 0.008), rough=1.0, spec=0.0)

# ---- foliage -----------------------------------------------------------------
T("pine", [(0.20, (0.038, 0.115, 0.048)), (0.60, (0.070, 0.185, 0.072)),
           (0.90, (0.110, 0.245, 0.098))],
  rough=0.90, scale=1.1, detail=4.0, bump=0.18, bump_dist=0.08)
T("pine_lt", [(0.20, (0.072, 0.185, 0.070)), (0.60, (0.118, 0.265, 0.098)),
              (0.90, (0.175, 0.335, 0.130))],
  rough=0.88, scale=1.1, detail=4.0, bump=0.18, bump_dist=0.08)
T("bush", [(0.20, (0.085, 0.170, 0.058)), (0.65, (0.140, 0.255, 0.088)),
           (0.92, (0.200, 0.320, 0.120))],
  rough=0.90, scale=1.4, detail=4.0, bump=0.20, bump_dist=0.08)
T("trunk", [(0.20, (0.072, 0.045, 0.026)), (0.70, (0.115, 0.072, 0.042))],
  rough=0.95, kind="wave", scale=3.0, distortion=2.0, bump=0.20, bump_dist=0.04)

# ------------------------------------------------------- per-island overrides
# Redefining a material by name re-uses the same datablock (tex._fresh), so an
# override here changes every mesh already asking for "rock" without touching a
# single district script.
if L.ISLAND == "copper":
    # Copper country rock is iron-stained rather than grey, and the weathered
    # outcrops carry the same green bloom as the ore they sit on. This is what
    # keeps the two islands telling apart at a glance from the terrain alone.
    T("rock", [(0.18, (0.190, 0.140, 0.104)), (0.50, (0.310, 0.232, 0.168)),
               (0.82, (0.430, 0.330, 0.244))],
      rough=0.88, scale=0.30, detail=7.0, bump=0.45, bump_dist=0.30)
    T("rock_dark", [(0.20, (0.104, 0.080, 0.066)), (0.55, (0.168, 0.130, 0.106)),
                    (0.85, (0.240, 0.190, 0.152))],
      rough=0.90, scale=0.32, detail=7.0, bump=0.45, bump_dist=0.30)
    T("cliff", [(0.18, (0.262, 0.190, 0.128)), (0.52, (0.380, 0.284, 0.192)),
                (0.85, (0.505, 0.395, 0.278))],
      rough=0.90, scale=0.26, detail=7.0, bump=0.42, bump_dist=0.30)
    # Beaches below an iron-stained range come out redder too.
    T("sand", [(0.22, (0.352, 0.258, 0.160)), (0.60, (0.462, 0.360, 0.232)),
               (0.88, (0.565, 0.462, 0.312))],
      rough=0.95, scale=0.24, detail=5.0, bump=0.22, bump_dist=0.2)

if L.ISLAND == "iron":
    # THIS ISLAND IS NOT GREEN. isle_iron sets GROUND_RAMP, which paints the
    # terrain's vertex colours red - but those only show once 13_export has
    # baked them, so in Blender the map still came up green and read as the
    # same island in a different arrangement. These are the same colours as
    # materials, so what is on screen matches what ships.
    #
    # Ferruginous country: laterite and ore-stained ground, rust through ochre.
    T("grass", [(0.20, (0.105, 0.048, 0.028)), (0.48, (0.215, 0.104, 0.052)),
                (0.80, (0.345, 0.192, 0.092))],
      rough=0.92, scale=0.26, detail=6.0, bump=0.30, bump_dist=0.25)
    T("grass_dry", [(0.22, (0.268, 0.150, 0.070)), (0.60, (0.395, 0.245, 0.116)),
                    (0.88, (0.505, 0.352, 0.180))],
      rough=0.94, scale=0.24, detail=5.0, bump=0.26, bump_dist=0.22)
    T("rock", [(0.18, (0.215, 0.150, 0.122)), (0.50, (0.330, 0.245, 0.198)),
               (0.82, (0.452, 0.352, 0.286))],
      rough=0.90, scale=0.30, detail=7.0, bump=0.46, bump_dist=0.30)
    T("cliff", [(0.18, (0.300, 0.180, 0.118)), (0.52, (0.418, 0.272, 0.176)),
                (0.85, (0.540, 0.392, 0.264))],
      rough=0.92, scale=0.26, detail=7.0, bump=0.44, bump_dist=0.30)
    T("sand", [(0.22, (0.408, 0.278, 0.162)), (0.60, (0.512, 0.375, 0.228)),
               (0.88, (0.610, 0.482, 0.318))],
      rough=0.95, scale=0.24, detail=5.0, bump=0.22, bump_dist=0.2)
    # The trees go with it. Dry scrub-country foliage - olive and dust rather
    # than the wet dark green of a conifer coast.
    T("pine", [(0.20, (0.088, 0.098, 0.046)), (0.60, (0.148, 0.162, 0.070)),
               (0.88, (0.215, 0.225, 0.102))],
      rough=0.92, scale=0.5, detail=4.0, bump=0.25, bump_dist=0.2)
    T("pine_lt", [(0.20, (0.152, 0.155, 0.070)), (0.60, (0.232, 0.228, 0.104)),
                  (0.88, (0.318, 0.300, 0.146))],
      rough=0.92, scale=0.5, detail=4.0, bump=0.25, bump_dist=0.2)

if L.ISLAND == "gold":
    # DRY COUNTRY, not green and not iron's red: sun-cured straw over pale
    # quartz-bearing rock. Same reasoning as the iron block above - the vertex
    # bake only shows in Unity, so the Blender materials have to say "arid"
    # too or the preview reads as the green island rearranged.
    T("grass", [(0.20, (0.130, 0.100, 0.045)), (0.48, (0.240, 0.190, 0.085)),
                (0.80, (0.370, 0.300, 0.140))],
      rough=0.92, scale=0.26, detail=6.0, bump=0.30, bump_dist=0.25)
    T("grass_dry", [(0.22, (0.300, 0.235, 0.105)), (0.60, (0.425, 0.345, 0.165)),
                    (0.88, (0.530, 0.445, 0.235))],
      rough=0.94, scale=0.24, detail=5.0, bump=0.26, bump_dist=0.22)
    T("rock", [(0.18, (0.250, 0.230, 0.200)), (0.50, (0.360, 0.335, 0.295)),
               (0.82, (0.470, 0.445, 0.400))],
      rough=0.90, scale=0.30, detail=7.0, bump=0.46, bump_dist=0.30)
    T("cliff", [(0.18, (0.310, 0.265, 0.205)), (0.52, (0.425, 0.370, 0.290)),
                (0.85, (0.545, 0.490, 0.395))],
      rough=0.92, scale=0.26, detail=7.0, bump=0.44, bump_dist=0.30)
    T("sand", [(0.22, (0.430, 0.345, 0.210)), (0.60, (0.530, 0.440, 0.285)),
               (0.88, (0.625, 0.535, 0.370))],
      rough=0.95, scale=0.24, detail=5.0, bump=0.22, bump_dist=0.2)
    # Dry-summer eucalypt scrub: olive-gold canopies, dusty understorey.
    T("pine", [(0.20, (0.105, 0.108, 0.042)), (0.60, (0.175, 0.170, 0.068)),
               (0.88, (0.250, 0.235, 0.100))],
      rough=0.92, scale=0.5, detail=4.0, bump=0.25, bump_dist=0.2)
    T("pine_lt", [(0.20, (0.170, 0.160, 0.065)), (0.60, (0.255, 0.235, 0.100)),
                  (0.88, (0.345, 0.315, 0.145))],
      rough=0.92, scale=0.5, detail=4.0, bump=0.25, bump_dist=0.2)
    T("bush", [(0.20, (0.140, 0.125, 0.050)), (0.65, (0.215, 0.195, 0.082)),
               (0.92, (0.300, 0.270, 0.120))],
      rough=0.90, scale=1.4, detail=4.0, bump=0.20, bump_dist=0.08)

# ------------------------------------------------------------------ collections
for c in ("Terrain", "Roads", "Rail", "Mine", "Depot", "Refinery", "Market",
          "Port", "Vehicles", "Props", "Foliage", "Sites"):
    coll(c)

print("setup ok", stats(), "cam", tuple(round(v, 1) for v in cam.location))
