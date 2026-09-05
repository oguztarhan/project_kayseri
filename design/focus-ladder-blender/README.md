# Focus Ladder — Blender review scene

Open **Focus_Ladder_Island.blend** in Blender. The saved portrait camera shows the complete map. **Factory_Detail_Camera** provides a closer view of the main blue factory.

The scene reconstructs the supplied reference as editable 3D geometry: five rocky terraces, mountain mine, coal works, blue factory, yellow refinery, warehouse, connected railway, vehicles, harbor crane, container ship, and three customer islands. This is a stylized reconstruction for visual review, not a pixel-identical reproduction of the illustration.

Use the Outliner to hide **08_Concept_Glow_Toggle** or **09_Reference_Markers_Toggle** when reviewing the environment without highlights or floating badges. Terrain, industry, railway, vehicles, harbor, and nature each have their own collection. The source reference is packed into the Blender file.

- `01-portrait-preview.png`: full map render.
- `02-factory-detail.png`: closer render of the blue factory.
- `scene-report.json`: saved scene inventory and file-integrity checks.

No Unity scene, map, or gameplay setting was changed. Materials and lighting are currently authored for Blender; matching them in Unity is a separate import/material-conversion step after visual approval.

The reproducible generator is `Tools/blender/reference_map/build.py`. It uses the project's existing mesh helpers and vehicle builders and runs in a separate Blender process. `review.py` generates the detail image and checks the saved file.
