"""Step 21: settle the whole island - off the rails, out of each other, on the ground.

Runs after every district has been built and after 17_tidy has sorted out each
one internally, because the faults this catches are the ones that span two
collections. See settle.py.
"""
import importlib
import layout
importlib.reload(layout)
import survey
importlib.reload(survey)      # settle reads its helpers; reload it first or the
import settle                 # cached copy from an earlier run is what gets used
importlib.reload(settle)
L = layout

# Run until it stops moving anything, up to three passes. One pass is not
# always enough from a raw build: resolving a knot re-seats objects that were
# not overlapping before, and the box snapshot the solver works from is taken
# once at entry, so the second pass sees the new arrangement.
passes = []
for _p in range(3):
    r = settle.settle(L)
    passes.append(r["moved"])
    if r["moved"] == 0:
        break

print("settle ok %d solids, %d movable: %d off the rails, %d lifted out of the "
      "ground, moved per pass %s, phase %d"
      % (r["solids"], r["movable"], r["off_rail"], r["lifted"], passes, PHASE))
