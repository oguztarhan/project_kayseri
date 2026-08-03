"""Show what Unity will actually draw, inside Blender.

The island shader takes its albedo from VERTEX COLOUR and ignores the material's
base colour entirely. Blender shows the opposite: its own procedural node trees,
and none of the baked colour. So a Blender render of the island is not a preview
of the game - it is a preview of something the player never sees, which is why
every colour judgement used to need a full round trip through FBX, Unity import,
prefab build and play mode. Three minutes to see one change.

    import preview; preview.on()      # bake everything, wire Col to Base Color
    shot.zoom("grass", (60, -60, 8))  # now this is the game's colours
    preview.off()                     # back to the authoring materials

on() is destructive to nothing: it stores each material's original Base Color
link and off() puts it back.
"""
import bpy
import importlib
import bake
importlib.reload(bake)

_SAVED = {}          # material name -> (from_node_name, from_socket_name) or None
_ATTR = "Col"


def _bsdf(m):
    if not m or not m.use_nodes:
        return None
    for n in m.node_tree.nodes:
        if n.bl_idname == "ShaderNodeBsdfPrincipled":
            return n
    return None


def on(bake_first=True):
    """Bake every mesh, then drive every material's Base Color from the bake."""
    n_baked = bake.bake_all() if bake_first else 0
    n_mat = 0
    for m in bpy.data.materials:
        b = _bsdf(m)
        if b is None or m.name in _SAVED:
            continue
        sock = b.inputs["Base Color"]
        link = sock.links[0] if sock.links else None
        _SAVED[m.name] = (link.from_node.name, link.from_socket.name) if link else None

        nt = m.node_tree
        attr = nt.nodes.get("PreviewCol")
        if attr is None:
            attr = nt.nodes.new("ShaderNodeVertexColor")
            attr.name = "PreviewCol"
            attr.location = (b.location.x - 260, b.location.y - 320)
        attr.layer_name = _ATTR
        if link:
            nt.links.remove(link)
        nt.links.new(attr.outputs["Color"], sock)
        n_mat += 1
    return "preview on: %d meshes baked, %d materials rewired" % (n_baked, n_mat)


def off():
    """Put every rewired material back on its own procedural texture.

    Driven by the PreviewCol node being present rather than by _SAVED, because a
    module reload empties _SAVED and would otherwise strand every material in
    preview wiring with no way back.
    """
    n = 0
    for m in bpy.data.materials:
        if not m.use_nodes:
            continue
        nt = m.node_tree
        attr = nt.nodes.get("PreviewCol")
        b = _bsdf(m)
        if attr is None or b is None:
            continue
        saved = _SAVED.pop(m.name, None)
        src = nt.nodes.get(saved[0]) if saved else None
        socket = saved[1] if saved else None
        if src is None or socket not in src.outputs:
            # tex.ptex always drives Base Color from the ramp, so that is the
            # answer whenever the note has been lost.
            src, socket = None, "Color"
            for node in nt.nodes:
                if node.bl_idname == "ShaderNodeValToRGB":
                    src = node
                    break
        sock = b.inputs["Base Color"]
        for l in list(sock.links):
            nt.links.remove(l)
        if src is not None:
            nt.links.new(src.outputs[socket], sock)
        nt.nodes.remove(attr)
        n += 1
    return "preview off: %d materials restored" % n
