# Plan 02 — Station upgrades and island development

**Date:** 2026-09-04 · **Status:** code complete, all six assemblies compile without errors, 821 EditMode tests green

## Product decisions

- Station axes, prices, effects, caps, wallet spending and persistence remain owned by
  `IslandEconomy` and `CoalOperation`.
- Island development is derived, never saved: one axis level is one point, a built expansion is five
  points, and each 25 points advances the visible development level. Existing saves therefore need no
  migration and cannot disagree with the underlying station state.
- The upgrade screen gains an island-wide recommendation page. It shows four actionable axes,
  affordable first, then favors the least-developed station, then the cheaper upgrade. Selecting a
  recommendation opens that station; the established station card remains the only purchase path.
- The next island remains visible as a preview, including its identity and income ceiling. Purchase
  requires the previous island's chapter objectives and the existing cash price. `WorldIslands`
  enforces this rule as the authority; the map only presents it.
- A maxed island shows a completed development card and no stale recommendations. Insufficient cash
  greys the recommendation price but does not disable navigation to the station.

## Verification

- EditMode tests pin derived levels, legacy-level clamping, recommendation order, stable ties and the
  next-island objective gate.
- Unity compilation has no errors or new warnings, and all 821 EditMode tests pass. A live Main-scene smoke test opened the
  recommendation page at development level 5/35, populated ranked station rows, and confirmed that
  Copper stayed locked while Coal's chapter objectives were incomplete. The runtime console was clean.

## Migration

No persisted field was added. Development progress and the gate are calculated from existing
`islandLevels`, expansion flags and chapter progress, so `SaveMigration.CurrentVersion` stays at 7.
