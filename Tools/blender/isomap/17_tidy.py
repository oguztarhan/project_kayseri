"""Step 17: settle each district's property.

The district scripts lay out a working site that grows over three phases and
stands on four differently-turned islands - twelve combinations, and a placement
that reads well in nine of them can still put a tank through a column in the
other three.  Rather than hand-solving all twelve, this pushes overlapping props
apart and clears anything left standing in the way in.  Fixed plant, ground and
connective geometry are pinned; see yard.PINNED.
"""
import importlib
import layout
importlib.reload(layout)
import yard
importlib.reload(yard)
L = layout

DISTRICTS = [("mine", "Mine", L.MINE), ("depot", "Depot", L.DEPOT),
             ("refinery", "Refinery", L.REFINERY), ("market", "Market", L.MARKET)]

moved = []
for key, cname, centre in DISTRICTS:
    col = bpy.data.collections.get(cname)
    if col is None:
        continue
    n = yard.separate(list(col.objects), centre, key)
    if n:
        moved.append("%s %d" % (cname, n))

print("tidy ok", ("moved: " + ", ".join(moved)) if moved else "nothing to move",
      "phase", PHASE)
