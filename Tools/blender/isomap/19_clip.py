"""Step 19: make the pavement stop at the kerb and the paint stay on the tarmac.

Face-exact, against the carriageways as they are actually drawn for this island
at this phase - see roadmask.py for why gapping the source polylines is not
enough on its own.
"""
import importlib
import layout
importlib.reload(layout)
import roadmask
importlib.reload(roadmask)
L = layout

MAIN_W = PK(L.ROAD_W * 0.62, L.ROAD_W * 0.86, L.ROAD_W)
LOOP_W = PK(8.0, 10.0, 12.0)
SPUR_W = PK(7.0, 9.0, 10.5)
PORT_W = PK(8.0, 10.0, 12.0)

mask = roadmask.build(L, PHASE, MAIN_W, LOOP_W, SPUR_W, PORT_W)

CR = bpy.data.collections.get("Roads")
raised, paint = [], []
if CR is not None:
    for ob in CR.objects:
        if ob.type != 'MESH':
            continue
        n = ob.name
        # Crosswalks are paint that BELONGS on the road; everything else called
        # Walk or Kerb is a raised strip that does not.
        if n.startswith("Walk.Crossings"):
            paint.append(ob)
        elif n.startswith("Walk") or n.startswith("Kerb"):
            raised.append(ob)
        elif n.startswith("Mark"):
            paint.append(ob)

# The pavement keeps a little clearance past the tarmac edge so the kerb face
# reads as a kerb rather than butting the road exactly.
cut_a, _ = roadmask.clip_out(raised, mask, margin=0.35)
# Paint is trimmed the other way, and tightly: a dash that ends 5 cm past the
# tarmac is a dash floating on grass.
cut_b, _ = roadmask.clip_in(paint, mask, margin=-0.10)
gone = roadmask.purge_empty(CR) if CR is not None else []

print("clip ok %d pavement faces off the road, %d paint faces off the grass, "
      "%d emptied objects removed, phase %d" % (cut_a, cut_b, len(gone), PHASE))
