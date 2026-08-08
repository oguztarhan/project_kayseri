"""Step 18: shade the built geometry by angle.

The bevel half of detail.py is deliberately NOT run - see detail.shading. This
is the part that costs no faces: round the curved surfaces, leave the flat ones
alone.
"""
import importlib
import detail
importlib.reload(detail)

n = detail.shading()
print("shading ok %d meshes smoothed by angle, phase %d" % (n, PHASE))
