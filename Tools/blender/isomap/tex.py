"""Procedural material factory.

Every surface gets node-based texture: colour variation from a noise/voronoi/
wave field, plus a matching bump so the stylised flat-shaded forms pick up
some grain without needing any image assets.
"""
import bpy


def _in(node, name, val):
    s = node.inputs.get(name)
    if s is None:
        return
    try:
        s.default_value = val
    except Exception:
        pass


def _fresh(name):
    """Create (or reset) a material with just an output + principled node."""
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (620, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (300, 0)
    nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    return m, nt, bsdf


def _ramp(nt, stops):
    """ColorRamp from [(pos, (r,g,b)), ...]."""
    r = nt.nodes.new("ShaderNodeValToRGB")
    r.location = (-120, 60)
    el = r.color_ramp.elements
    while len(el) > 1:
        el.remove(el[-1])
    el[0].position = stops[0][0]
    el[0].color = (*stops[0][1], 1.0)
    for pos, col in stops[1:]:
        e = el.new(pos)
        e.color = (*col, 1.0)
    return r


def ptex(name, stops, rough=0.8, metal=0.0, kind="noise", scale=0.35,
         detail=5.0, distortion=0.0, bump=0.22, bump_dist=0.06,
         rough_hi=None, coords="Object", alpha=1.0, emis=None, emis_str=0.0,
         wave_dir="X", wave_type="BANDS", spec=0.5, randomness=1.0,
         vor_feature="F1", interp=None):
    """Build a textured material.  `stops` is the ColorRamp colour spread."""
    m, nt, bsdf = _fresh(name)

    tc = nt.nodes.new("ShaderNodeTexCoord")
    tc.location = (-820, 0)
    src = tc.outputs.get(coords) or tc.outputs["Object"]

    if kind == "voronoi":
        t = nt.nodes.new("ShaderNodeTexVoronoi")
        _in(t, "Randomness", randomness)
        try:
            t.feature = vor_feature
        except Exception:
            pass
    elif kind == "wave":
        t = nt.nodes.new("ShaderNodeTexWave")
        try:
            t.wave_type = wave_type
            t.bands_direction = wave_dir
        except Exception:
            pass
        _in(t, "Distortion", distortion)
    elif kind == "checker":
        t = nt.nodes.new("ShaderNodeTexChecker")
    else:
        t = nt.nodes.new("ShaderNodeTexNoise")
        _in(t, "Distortion", distortion)
    t.location = (-540, 0)
    _in(t, "Scale", scale)
    _in(t, "Detail", detail)
    nt.links.new(src, t.inputs["Vector"])

    fac = t.outputs.get("Factor") or t.outputs.get("Fac") or t.outputs["Color"]

    ramp = _ramp(nt, stops)
    if interp:
        try:
            ramp.color_ramp.interpolation = interp
        except Exception:
            pass
    nt.links.new(fac, ramp.inputs["Factor"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])

    if bump > 0.0:
        bp = nt.nodes.new("ShaderNodeBump")
        bp.location = (60, -260)
        _in(bp, "Strength", bump)
        _in(bp, "Distance", bump_dist)
        nt.links.new(fac, bp.inputs["Height"])
        nt.links.new(bp.outputs["Normal"], bsdf.inputs["Normal"])

    _in(bsdf, "Metallic", metal)
    _in(bsdf, "Specular IOR Level", spec)
    if rough_hi is None:
        _in(bsdf, "Roughness", rough)
    else:
        rr = _ramp(nt, [(0.0, (rough, rough, rough)),
                        (1.0, (rough_hi, rough_hi, rough_hi))])
        rr.location = (-120, -220)
        nt.links.new(fac, rr.inputs["Factor"])
        nt.links.new(rr.outputs["Color"], bsdf.inputs["Roughness"])

    if emis is not None:
        _in(bsdf, "Emission Color", (*emis, 1.0))
        _in(bsdf, "Emission Strength", emis_str)
    if alpha < 1.0:
        _in(bsdf, "Alpha", alpha)
        for attr, val in (("blend_method", 'BLEND'),
                          ("surface_render_method", 'BLENDED')):
            if hasattr(m, attr):
                try:
                    setattr(m, attr, val)
                except Exception:
                    pass
    return m


def flat(name, col, rough=0.55, metal=0.0, spec=0.5, emis=None, emis_str=0.0,
         alpha=1.0, grain=0.05):
    """Solid colour with a whisper of grain - for vehicles and small props."""
    lo = tuple(max(0.0, c * (1.0 - grain)) for c in col)
    hi = tuple(min(1.0, c * (1.0 + grain)) for c in col)
    return ptex(name, [(0.25, lo), (0.75, hi)], rough=rough, metal=metal,
                kind="noise", scale=1.6, detail=2.0, bump=0.0, spec=spec,
                emis=emis, emis_str=emis_str, alpha=alpha)
