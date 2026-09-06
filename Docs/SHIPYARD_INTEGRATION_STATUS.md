# Portrait shipyard — first integration pass

> **Historical snapshot / superseded.** The current source of truth is [UPDATED_PORTRAIT_SHIPYARD_WORKLIST.md](../UPDATED_PORTRAIT_SHIPYARD_WORKLIST.md). This document records the earlier foundation pass and must not be used as the active implementation plan.

2026-09-05. This is a **layout/runtime foundation**, not a complete production-loop release.

## Claude handoff verdict

The revised `Tools/blender/IndustrialReference/anchors.json` is usable for this pass.
Its supplied geometry validator passes: 45 anchor names, measured ground heights,
17 route polylines, five independent station pads. Original map root scale is 1.
The Figurehead Atelier still has no building. Keep it locked; never spend currency
on that missing building. The 1.2 × 0.7 m waterfront footprint needs art review.

This does **not** certify collision-free driving: the route generator deliberately
ignores some trees/pebbles and overlapping road obstacles. Validate vehicle clearance
and slopes in play before shipping. Its serial delivery route is a spatial route,
not a recipe dependency between equipment families.

## Implemented

- Dedicated scene: `Assets/Scenes/Shipyard.unity`, using the existing approved map
  prefab without changing its mesh assets or reference scene.
- All 45 anchors bound as named transforms; all 17 routes bound under
  `Shipyard_Runtime/Gameplay_Routes`. Runtime waypoint data also lives in the manifest.
- Cannon building visible initially. Hull, Rigging, Navigation and Figurehead show
  their pads. Logistics tank, storage containers, mine and harbour remain visible.
- Orthographic portrait camera: vertical-only drag/scroll, clamped camera stops,
  no sideways movement, rotation or pinch. UI consumes pointer starts; safe-area HUD.
- No preview/debug HUD or fake crafting/revenue buttons remain on the clean map.
- Additive `SaveData.shipyard` payload, sequential unlock commit boundary, explicit
  milestone/art gates and encrypted-save round-trip coverage. No save version bump,
  old equipment slot changes, wallet changes, or island-progress migration.
- Isolated Play Preview menu bypasses Bootstrap and does not read/write player saves.
  Its temporary start-scene override is restored when Play mode ends.
- Portrait cleanup pass: all floating building-name boards and the chapter/objective
  banner are removed from the main screen. The ad/offer duplicate shortcuts and all
  legacy code-built bottom openers are suppressed; Map, Boost, Contract and Upgrade
  form one four-button rail on the left edge. Store, Daily, currency and Settings keep
  their authored positions.
- Main camera opens at a 58-degree overhead angle, fits the island width with a 2%
  framing margin, and locks input to the island's vertical lane. Runtime fog and the
  DayNight fog option are disabled. Day/night lighting itself remains available.
- The clean map preview opens on the measured map centre, shows the full coast and all
  three customer islets with a tight water margin at both sides, and has no debug canvas or
  buttons.

## Stage 8 functional pass (2026-09-06)

- The runtime production card now acts as one contextual machine panel rather than a permanent
  Cannon-only recipe carousel. It creates tabs only for built machine families and shows only
  discovered recipes for the selected family.
- Production actions (start, sell, equip, store, salvage, and fulfil) route through the generic
  machine-aware service methods, so the panel does not fork gameplay rules per station.
- New machine, recipe, material, status, and action copy is localized in
  `Assets/Resources/Diller/metinler.txt` for every shipped language column.
- The runtime panel is constrained to `Screen.safeArea` and reflows when resolution or orientation
  changes. The panel height was increased to accommodate the contextual row without clipping.
- The authored HUD now clamps cash, premium currency, income, settings, boost/shield indicators, and
  the compact rail to safe-area bounds; the Set Sail opener remains the one allowed primary runtime
  opener in compact mode.
- Functional verification: `Game.Tests.ShipyardFoundationTests` passes 28/28, ordinary Bootstrap
  startup reaches Shipyard with zero console errors, and a direct Main-scene smoke exercises the
  HUD plus runtime production canvas with zero console errors. Normal Play start remains Bootstrap.
- Art polish, worker/material-transfer effects, output rack modelling, and device-specific visual QA
  remain intentionally deferred per the current project direction.

## Open / play

Unity menu: **Kayseri → Portrait Shipyard → Open Preview Scene**.
Then **Kayseri → Portrait Shipyard → Play Preview (No Player Save)**.
Use this menu, not the ordinary Play button, which normally starts Bootstrap/Main.
Drag vertically to inspect the map. Select a portrait Game view resolution.
The scene builder refuses to overwrite an existing preview.

`Assets/Resources/Shipyard/Map.json` is an array-shaped copy of Claude's dictionary
manifest for Unity JsonUtility. Regenerate/revalidate this copy when anchors change;
do not rescale it to match the legacy Main scene's 85× art wrapper.

## Verification

- Source geometry validator passed.
- Eight `Game.Tests.ShipyardFoundationTests` passed in Unity Edit Mode.
- Combined regression run passed **103/103** tests after the portrait cleanup across ShipyardFoundation,
  SeaCombat, Crafting, SaveMigration and SaveService (not the entire project suite).
- Scene audit: 45 anchor transforms, 17 route groups, root scale (1,1,1), initial
  building visibility [true,false,false,false,false]. Hull unlock hides its pad.
- Navigation locking leaves the tank and storage containers active.
- Isolated Play mode opens Shipyard, has no GameBootstrap, retains Cannon-only
  visibility, and has an active Input System UI action asset.
- Exiting the isolated preview restores `Assets/Scenes/Bootstrap.unity` as the normal
  Play start scene.
- No Shipyard compile errors/warnings were reported. The pre-existing Unity Remote
  Android `adb forward` requirements error still appears during Play; physical-device
  streaming/touch testing is not verified. Existing unrelated deprecation warnings remain.
- Portrait render inspected at 720 × 1560:
  `design/shipyard-integration/cannon-first.png`.
- Clean overhead framing inspected at 720 × 1560:
  `design/shipyard-integration/clean-overhead.png`.
- A direct Main runtime audit (Bootstrap/save deliberately bypassed) found no Mine,
  Depot, Smelter, Chapter or Next labels; no building-sign canvas; fog false; camera
  pitch 58; and exactly Map/Boost/Contract/Upgrade in the edge rail.

## Next implementation slice

1. Connect actual mine/refinery/storage inventory to a timed Cannon queue. Define
   material costs and order milestones in data; do not make Hull consume Cannon items.
2. Add the first Cannon recipe card and real completion/collect/equip-or-sell decisions,
   reusing the existing wallet/combat services. Later recipes remain absent from UI.
3. Persist queue timestamps and inventory; test restart/offline completion and prevent
   double rewards. Only then wire paid station unlocks to the guarded commit method.
4. Expand to Hull, Rigging and Navigation; append the Rigging equipment slot without
   reindexing legacy gear. Figurehead awaits Claude's art.
5. Add a reversible Bootstrap feature switch only once the Cannon loop is playable.
   Keep the old game launch available until that smoke test passes.
6. Collision proxies, vehicle clearance, LODs, draw-call profiling and device touch QA.

The existing map is a low-poly interpretation of the picture, not a pixel-identical
reconstruction. This pass adds behavior and layout bindings, not a visual-quality overhaul.

## Workspace safety

Existing Main/Bootstrap/camera edits and removal of many legacy island assets were
already present or arrived concurrently. This integration does not revert, restore,
or extend those removals. It does not disable the existing launch path.
