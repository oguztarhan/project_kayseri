# Island import — handoff

Continue from the **project_kayseri** Claude Code session (it has both `blender`
and `UnityMCP`; the session this was built in only had `blender`).

## Open problem

The map builds but reads as untextured. Root cause not yet confirmed — it is one
of two things, and the diagnostic below distinguishes them.

**First thing to run:** `Kayseri → Island → Diagnose Vertex Colours`

It logs `X/Y meshes carry colours`.

- **X = 0** → Unity is not importing the baked vertex colours. The FBX definitely
  contain them (`LayerElementColor`, `ByPolygonVertex`, verified with `strings`),
  so the fault is on the import side. The menu item auto-falls back to flat
  colours so the map at least reads correctly. Investigate `ModelImporter`
  settings in `IslandBuilder.ConfigureModelImports` — suspect
  `optimizeMeshVertices` / `weldVertices`, or re-export with
  `colors_type='LINEAR'` from `13_export.py`.
- **X > 0** → colours import fine, so the shader is not sampling them. Check the
  renderer's material really is `Kayseri/IslandVertexLit` and that
  `_VertexColorAmount` is 1.

There is also `Kayseri → Island → Toggle Flat Colours` to flip between baked
detail and flat colour on all 63 materials.

## Already fixed (verified: 0 C# errors, 0 shader errors, both shaders ok=1)

1. **Shader squared every colour.** The bake writes the material's *full* colour
   into vertex colours, but the shader did `_BaseColor × vertexColor`. Changed to
   `lerp(_BaseColor, vertexColor, _VertexColorAmount)`.
2. **`BuildAll()` ran importers before creating materials**, so the remap lookup
   was empty and nothing got remapped on a clean run. Order is now materials →
   imports. (This is why the first Build All produced grey meshes.)
3. **sRGB written raw into a Linear project** (`m_ActiveColorSpace: 1`).
   `ToColor` now converts via `.linear`.
4. **Empty mesh exported** (`Port.Containers` has 0 containers at phase 1) caused
   `Can't calculate tangents`. Guarded at source and in the exporter.

## State

- 31 FBX in `Assets/Art/KayseriIsland/Models/Phase1|2|3/` — 11 district groups
- Trucks, vans, rolling stock stripped (verified: 0 occurrences of `Truck`,
  `.Van`, `Train.`, `Wagon`, `Loco` across all files). Ships, cranes, loaders,
  excavators, forklifts kept.
- 63 URP materials, 60 opaque + 3 transparent (glass, smoke, ghost)
- `Island_Phase1|2|3.prefab`, `Assets/Scenes/KayseriIsland.unity`
- Blender generator preserved at `Tools/blender/isomap/` (see its README) — it
  was previously only in a temp session directory
- Phase geometry is exact: world transforms baked into the FBX, so each group
  drops at the origin and reassembles the map with no manual placement

## Cleanup when the texture issue is resolved

Delete `Assets/Editor/KayseriIsland/IslandDiagnosticsDump.cs` — it's a temporary
`[InitializeOnLoad]` probe that writes `Tools/blender/island_diag.txt` on every
domain reload.

## Not yet done

- Mesh/draw-call optimisation (deliberately deferred — exported as-is so
  individual buildings stay addressable). ~230k tris / ~1,650 objects at phase 3.
  Foliage and rocks share meshes, so GPU instancing is the first easy win.
- Camera `orthographicSize` is 126.7, which assumes a 3:2 viewport. Retune for
  phone aspect.
