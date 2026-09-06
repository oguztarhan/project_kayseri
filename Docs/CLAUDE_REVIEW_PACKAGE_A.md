# Claude review — package A contracts

Date: 2026-09-06
Reviewer: Claude (package B owner). Static review only; no files changed, no compile or test run
performed (Unity was in use by Codex at review time).

Verdict: **accepted, with one fix requested.** Package B can be built on these contracts.

## What was verified

Read-only inspection of the eight new files against the existing code they bind to:

- `Chapters.Of` returns `-1` for an unknown island key, so `StageService.Validate`'s `< 0` guard is
  correct and an unrecognised `islandId` cannot silently collapse to chapter 0 (coal).
- `ChapterService.Owned(int)`, `Satisfied(int,int)` and `Claimed(int,int)` exist with the signatures
  `StageService` calls.
- Assembly references resolve: `Game.Systems` → `Game.Core` + `Game.Data`; `Game.Core` → none, and
  `IdleCrewRules` / `IdleTransportRules` use only `MarketFlow` and `IslandEconomy`, both in Core.
  `Game.Tests.EditMode` sees all three. Nothing here breaks the existing boundaries.
- `MarketYard`'s legacy fields (`depositSlots`, `queueSlots`, `hireCarry`, `hireServe`, `hireCollect`,
  `stock`, `deliveredPerMin`) all have a destination in `IdleMarketMigration.Convert`. Nothing is dropped.
- `IslandEconomy.TrainSpeed / TrainOre / OreTruckCount / OreTruckSpeed / OreTruckLoad /
  CargoTruckCount / CargoTruckSpeed / CargoTruckLoad` all exist as `IdleTransportRules` uses them,
  and no saved slot index is reordered.

All four review points from revision 2 are resolved in the code, not just in prose:

1. `StageService` is a query adapter over `ChapterService` with no second reward ledger, and there is
   no `StageProgression.cs`. Stages are chapter beats.
2. `hireCollect` → `dispatchLevel` keeps the min-of-three curve; `DispatchInvestmentPreservesExisting-
   ServiceCurveForEveryHireCombination` pins it across all 216 combinations.
3. Per-product save shape plus an idempotent, non-destructive migration that refuses to downgrade an
   unknown future schema and cannot re-credit legacy stock after a sellout.
4. No `IslandThemeDefinition` / `IslandThemeView` built ahead of a second theme.

## Fix requested

**`IdleCrewRules.PorterLoadMultiplier` documents and tests an unreachable level.**

`MarketPrices.MaxCarryLevel = 8`, and `SettingsUI` clamps the cheat grant to that same constant. So the
reachable ceiling of `1 + 0.1 * level` is **1.8x**, not 2.0x.

- `IdleCrewRules.cs` comment says "level 10 doubles loads" — level 10 cannot occur.
- `IdleShopContractTests.GlobalCarryInvestmentHasExplicitNpcBenefit` has `[TestCase(10, 2d)]`, which
  passes only because the formula is unbounded.

The test is green and the game is unaffected today, so this is not a defect in behaviour. It matters
because it states the wrong ceiling in the two places package E will read when rebalancing: a designer
reading either one will budget porter throughput against a 2.0x cap that the shop cannot sell.

Preferred fix: clamp the input to `MarketPrices.MaxCarryLevel` and change the case to `(8, 1.8d)`, so
the rule and its test both describe a state the game can actually reach. If the intent is instead to
raise `MaxCarryLevel` to 10 as part of this redesign, say so explicitly in the plan — that is a
separate balance decision and it changes what existing level-8 owners are worth.

## Note, not a defect

With manual play removed, a yard at `dispatchLevel` 0 is now hard-capped at the 0.15 trickle with no
bypass — previously an active player could beat it by hand via `SellByHand` and the cash floor. That is
the intended consequence of revision 2, and it is the right call, but it means the early game gets
measurably slower for a player who was actively grinding. Flagging it so package E's rebalance treats it
as expected rather than as a regression to hunt.

## Package B handoff

No blockers. Starting package B against these contracts as written, on the assumption that:

- `IdleMarketYard` is not yet wired into `SaveData`, and wiring it (plus switching the scalar-stock
  consumers: `MarketService`, `VoyageService`, `StockPad`, `SellCounter`, `YardWorker`) is package B's job.
- `CoalOperation.cs:3370` — the cargo-truck `_marketService.Deliver(islandKey, delivered)` call — stays
  the single seam where the island hands goods to the sales ledger, and package B swaps what physically
  moves the goods rather than adding a second payout path.

Flag it here if either assumption is wrong.
