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

# ---- the derived islands' ores ------------------------------------------------
# Four maps carry eight islands: silver re-exports the copper map, ruby the
# iron, emerald the coal, diamond the gold (see isle_silver.py). The land is
# identical, so THIS is what has to carry the difference - every stockpile,
# wagon load, truck load and theme pile on those islands asks the island for
# ORE and gets one of these.
#
# All four keep the coal/ore voronoi at scale 0.34 (and 0.9 for the shiny
# variant), because the heap geometry is shared and a different grain size on
# the same cone reads as a modelling mistake rather than a different mineral.

# Silver: argentite and galena. Lead-grey metal in a pale gangue, with the
# bright flash silver is actually recognised by at the top of the ramp. Against
# the copper island's rust-stained country rock this is the coolest thing on
# the map, which is the whole job.
T("ore_ag", [(0.10, (0.062, 0.066, 0.078)), (0.42, (0.140, 0.148, 0.168)),
             (0.70, (0.285, 0.298, 0.325)), (0.90, (0.480, 0.495, 0.520))],
  rough=0.62, rough_hi=0.40, kind="voronoi", scale=0.34, bump=0.32,
  bump_dist=0.22, spec=0.55, metal=0.20)
T("ore_ag_shiny", [(0.12, (0.128, 0.134, 0.152)), (0.46, (0.275, 0.288, 0.318)),
                   (0.74, (0.510, 0.528, 0.560)), (0.92, (0.780, 0.800, 0.835))],
  rough=0.32, rough_hi=0.22, kind="voronoi", scale=0.9, bump=0.16,
  bump_dist=0.07, spec=0.65, metal=0.55)

# Ruby: corundum. Crimson crystal faces in a dark host, cut with the pink of
# the marble it grows in. Deliberately DEEPER and more saturated than iron's
# haematite, which is a rusty orange-brown - ruby #5 follows iron #2 on the
# ladder and they share a map, so the two reds have to be told apart.
T("ore_rb", [(0.10, (0.075, 0.020, 0.030)), (0.42, (0.185, 0.032, 0.052)),
             (0.70, (0.360, 0.055, 0.082)), (0.90, (0.245, 0.175, 0.190))],
  rough=0.60, rough_hi=0.40, kind="voronoi", scale=0.34, bump=0.32,
  bump_dist=0.22, spec=0.55)
T("ore_rb_shiny", [(0.12, (0.130, 0.028, 0.045)), (0.46, (0.300, 0.045, 0.070)),
                   (0.74, (0.545, 0.075, 0.110)), (0.92, (0.760, 0.240, 0.290))],
  rough=0.30, rough_hi=0.20, kind="voronoi", scale=0.9, bump=0.16,
  bump_dist=0.07, spec=0.7)

# Emerald: beryl in schist. Deep green crystal IN a near-black host rock -
# which is what keeps it off the copper island's malachite, an ore that is
# green all the way through. The dark bottom of the ramp is doing that work,
# so do not lift it.
T("ore_em", [(0.10, (0.020, 0.038, 0.030)), (0.42, (0.028, 0.098, 0.062)),
             (0.70, (0.040, 0.215, 0.115)), (0.90, (0.130, 0.145, 0.135))],
  rough=0.64, rough_hi=0.44, kind="voronoi", scale=0.34, bump=0.32,
  bump_dist=0.22, spec=0.50)
T("ore_em_shiny", [(0.12, (0.030, 0.075, 0.052)), (0.46, (0.045, 0.170, 0.100)),
                   (0.74, (0.070, 0.330, 0.175)), (0.92, (0.230, 0.560, 0.340))],
  rough=0.30, rough_hi=0.20, kind="voronoi", scale=0.9, bump=0.16,
  bump_dist=0.07, spec=0.7)

# Diamond: kimberlite. Blue-grey host rock with the icy flash of the stones in
# it. Raw diamond ore is rock the same way gold ore is quartz - the sparkle is
# the accent at the top of the ramp, not the body of the heap.
T("ore_dm", [(0.10, (0.072, 0.082, 0.098)), (0.42, (0.150, 0.172, 0.200)),
             (0.70, (0.265, 0.300, 0.340)), (0.90, (0.420, 0.490, 0.545))],
  rough=0.66, rough_hi=0.46, kind="voronoi", scale=0.34, bump=0.32,
  bump_dist=0.22, spec=0.55)
T("ore_dm_shiny", [(0.12, (0.150, 0.180, 0.215)), (0.46, (0.300, 0.355, 0.410)),
                   (0.74, (0.540, 0.620, 0.690)), (0.92, (0.850, 0.920, 0.980))],
  rough=0.22, rough_hi=0.14, kind="voronoi", scale=0.9, bump=0.16,
  bump_dist=0.07, spec=0.85)

# The silver works, for the copper map's theme props (16_theme.py). Cathode
# plate and pond liquor are the two pieces of that theme that are about the ore
# rather than the land, so without these the silver island stacks COPPER
# cathode beside a malachite-green pond - on the one landmark whose whole job
# is saying which ore is mined here.
T("plate_ag", [(0.25, (0.480, 0.492, 0.512)), (0.62, (0.660, 0.678, 0.702)),
               (0.90, (0.840, 0.860, 0.888))],
  rough=0.30, rough_hi=0.40, metal=0.70, scale=1.3, bump=0.06, bump_dist=0.03)
# Cyanide liquor: silver really is leached this way, so the ponds stay - they
# just stop being malachite green and go the pale blue-grey of the real thing.
#
# F() and not T(), matching "leach" exactly. A ramped roughness (rough != 
# rough_hi) LINKS the Principled roughness socket, and 13_export cannot read a
# linked socket - it writes the 0.5 fallback into palette.json, so the pond
# came out matte where copper's is wet. Same construction, hue only.
F("leach_ag", (0.135, 0.200, 0.215), rough=0.20, spec=0.75)

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
if L.DESIGN == "copper":
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

if L.DESIGN == "iron":
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

if L.DESIGN == "gold":
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

# ------------------------------------------- derived-island ground tints
# These run AFTER the four design blocks above and redefine the same names, so
# each derived island gets its base map's country rock and then a shift off it.
# Ordering is load-bearing: T() reuses the datablock by name (tex._fresh), so
# last one wins, and moving these above the design blocks would silently undo
# them.
#
# The ore alone was not enough. A derived island IS the base island - same
# land, same roads, same buildings - so with only the heaps recoloured the two
# read as one map at the play camera's distance, where the ground is most of
# what you can see. These blocks are small on purpose: enough to separate the
# pair, not so much that the island stops looking like the one it re-exports.
# Every one of them keeps its base block's rough/scale/bump; only colour moves.

if L.ISLAND == "silver":
    # Copper's country rock is iron-stained warm brown. Silver country is the
    # cold end of the same range: grey granite with a blue cast, the way a
    # galena district reads against a copper one.
    T("rock", [(0.18, (0.135, 0.142, 0.158)), (0.50, (0.238, 0.248, 0.272)),
               (0.82, (0.352, 0.365, 0.395))],
      rough=0.88, scale=0.30, detail=7.0, bump=0.45, bump_dist=0.30)
    T("rock_dark", [(0.20, (0.076, 0.080, 0.094)), (0.55, (0.128, 0.135, 0.155)),
                    (0.85, (0.192, 0.202, 0.228))],
      rough=0.90, scale=0.32, detail=7.0, bump=0.45, bump_dist=0.30)
    T("cliff", [(0.18, (0.196, 0.205, 0.228)), (0.52, (0.308, 0.320, 0.350)),
                (0.85, (0.428, 0.442, 0.478))],
      rough=0.90, scale=0.26, detail=7.0, bump=0.42, bump_dist=0.30)
    # Beaches below a grey range are grey sand, not the red the copper island
    # gets from its iron-stained hills.
    T("sand", [(0.22, (0.298, 0.302, 0.312)), (0.60, (0.408, 0.412, 0.428)),
               (0.88, (0.518, 0.525, 0.545))],
      rough=0.95, scale=0.24, detail=5.0, bump=0.22, bump_dist=0.2)

if L.ISLAND == "ruby":
    # The one pair that genuinely needed this. Iron is red ore on red ground;
    # so is ruby if nothing moves, and they share the iron map. Corundum forms
    # in MARBLE, so the ground goes pale violet-grey and the ore stays deep
    # red - which puts the contrast the other way round from iron, where the
    # ore is the lighter of the two.
    T("rock", [(0.18, (0.238, 0.222, 0.238)), (0.50, (0.352, 0.332, 0.352)),
               (0.82, (0.472, 0.452, 0.478))],
      rough=0.90, scale=0.30, detail=7.0, bump=0.46, bump_dist=0.30)
    T("cliff", [(0.18, (0.318, 0.292, 0.312)), (0.52, (0.442, 0.412, 0.438)),
                (0.85, (0.575, 0.548, 0.575))],
      rough=0.92, scale=0.26, detail=7.0, bump=0.44, bump_dist=0.30)
    # Dry upland over marble: dusty rose rather than iron's rust laterite.
    T("grass", [(0.20, (0.132, 0.098, 0.098)), (0.48, (0.238, 0.180, 0.180)),
                (0.80, (0.352, 0.278, 0.282))],
      rough=0.92, scale=0.26, detail=6.0, bump=0.30, bump_dist=0.25)
    T("grass_dry", [(0.22, (0.288, 0.232, 0.222)), (0.60, (0.402, 0.338, 0.328)),
                    (0.88, (0.508, 0.442, 0.436))],
      rough=0.94, scale=0.24, detail=5.0, bump=0.26, bump_dist=0.22)
    T("sand", [(0.22, (0.408, 0.360, 0.348)), (0.60, (0.512, 0.462, 0.452)),
               (0.88, (0.610, 0.562, 0.556))],
      rough=0.95, scale=0.24, detail=5.0, bump=0.22, bump_dist=0.2)

if L.ISLAND == "emerald":
    # The coal map has no design block at all - it is the default green coast,
    # and grey granite. Emerald country is the same wet coast over SCHIST, so
    # the rock goes green-black and the hillsides deepen to match. Nothing else
    # moves: the coke ovens and the pine coast are what say "this is the coal
    # map", and they should keep saying it.
    T("rock", [(0.18, (0.092, 0.118, 0.098)), (0.50, (0.170, 0.208, 0.176)),
               (0.82, (0.262, 0.310, 0.268))],
      rough=0.88, scale=0.30, detail=7.0, bump=0.45, bump_dist=0.30)
    T("rock_dark", [(0.20, (0.048, 0.065, 0.054)), (0.55, (0.086, 0.112, 0.092)),
                    (0.85, (0.135, 0.170, 0.142))],
      rough=0.90, scale=0.32, detail=7.0, bump=0.45, bump_dist=0.30)
    T("cliff", [(0.18, (0.148, 0.182, 0.152)), (0.52, (0.238, 0.282, 0.242)),
                (0.85, (0.342, 0.392, 0.345))],
      rough=0.90, scale=0.26, detail=7.0, bump=0.42, bump_dist=0.30)
    T("grass", [(0.20, (0.048, 0.096, 0.046)), (0.48, (0.088, 0.170, 0.082)),
                (0.80, (0.148, 0.262, 0.132))],
      rough=0.92, scale=0.26, detail=6.0, bump=0.30, bump_dist=0.25)

if L.ISLAND == "diamond":
    # Gold's arid straw over pale quartz, cooled onto a kimberlite pipe:
    # blue-grey rock, grey-tan sand, and the dry grass left alone apart from
    # the warmth taken out of it. The eucalypt scrub stays exactly as the gold
    # island has it - the vegetation is the map's, not the ore's.
    T("rock", [(0.18, (0.198, 0.212, 0.232)), (0.50, (0.302, 0.320, 0.345)),
               (0.82, (0.412, 0.432, 0.462))],
      rough=0.90, scale=0.30, detail=7.0, bump=0.46, bump_dist=0.30)
    T("cliff", [(0.18, (0.252, 0.268, 0.292)), (0.52, (0.362, 0.382, 0.412)),
                (0.85, (0.478, 0.500, 0.535))],
      rough=0.92, scale=0.26, detail=7.0, bump=0.44, bump_dist=0.30)
    T("sand", [(0.22, (0.392, 0.372, 0.350)), (0.60, (0.495, 0.478, 0.458)),
               (0.88, (0.592, 0.578, 0.562))],
      rough=0.95, scale=0.24, detail=5.0, bump=0.22, bump_dist=0.2)
    T("grass_dry", [(0.22, (0.272, 0.248, 0.185)), (0.60, (0.392, 0.362, 0.278)),
                    (0.88, (0.498, 0.468, 0.372))],
      rough=0.94, scale=0.24, detail=5.0, bump=0.26, bump_dist=0.22)

# ------------------------------------------------------------------ collections
for c in ("Terrain", "Roads", "Rail", "Mine", "Depot", "Refinery", "Market",
          "Port", "Vehicles", "Props", "Foliage", "Sites"):
    coll(c)

print("setup ok", stats(), "cam", tuple(round(v, 1) for v in cam.location))
