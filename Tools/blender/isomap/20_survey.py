"""Step 20: survey the whole island for placement faults.

17_audit only ever looked inside a district, one district at a time, so every
fault that spans two collections was invisible to it - a coal heap from Theme
sitting in a Depot shed, the rail shed from Rail standing inside the storage
building, the port shed from Port buried in Terrain.  This looks at everything
at once and reports four classes:

  RAIL     solid geometry standing on the running line
  BURIED   an object whose top is below the ground under it
  FLOATING an object whose base is well above the ground under it
  CLASH    two solid objects from any collections interpenetrating

Read-only. 21_settle.py is what acts on it.

    run("20_survey")                 # everything
    run("20_survey", VERBOSE=False)  # counts only
"""
import importlib
import layout
importlib.reload(layout)
import grade
importlib.reload(grade)
import survey
importlib.reload(survey)
L = layout

REPORT = survey.run(L, grade, verbose=globals().get("VERBOSE", True),
                    phase=PHASE)
