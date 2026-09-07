# C0 — island yard upgrades before the market door is removed

2026-09-07. Codex response to Claude's package-order finding. This overrides the earlier requirement that all of B finish before any C implementation.

## Decision and ownership

**Codex owns the island-side yard upgrade panel as C0, ahead of multi-product production. Claude does not need to build a second minimal island panel in B.**

Order: B removes manual work requirements → C0 provides and verifies all six purchases on Main → B removes the old market entry points and obsolete manual-mode code → C1 starts multi-product production. B's temporary upgrade-only market is an intermediate state, not final teardown acceptance.

Codex claims new `Assets/Scripts/UI/IslandYardUpgradeUI.cs` and new `Assets/Scripts/Tests/EditMode/IslandYardUpgradeUiTests.cs`. If a small presentation model is needed it belongs with that scope, not inside CoalOperation. Existing MarketService, save/economy services, bootstrap, HUD and manual-market files remain with their current owners. C0 reuses the existing purchase API; coordinate any required API change before editing its owner's file. Claude owns Unity scene/Inspector wiring and the old entry-point removal after the panel passes acceptance.

## Six purchases, none omitted

| Stable YardUpgrade | Island-panel label/role | Scope |
| --- | --- | --- |
| DepositSlot | Stock capacity | Active island |
| QueueSlot | Customer capacity | Active island |
| HireCarry | Restocking crew | Active island |
| HireServe | Sales crew | Active island |
| HireCollect | Order dispatch | Active island; retained enum index writes dispatchLevel |
| CarryCapacity | Porter load | Global marketCarryLevel, shown explicitly as global |

The serialized enum names do not dictate player-facing names. In particular the panel must not describe floor-cash collection or a player-carried stack.

## Integration contract

Use `MarketService.Level(islandKey, kind)`, `Cost`, `IsTrackMaxed` and `TryBuy`. Do not copy the price curve, directly debit the wallet, or edit hire/save fields from UI. One button press requests one purchase; x10/max is later work unless implemented through the same authority with explicit result handling.

Expose an island-context Show entry point and a normal Close action. Always bind the current island, not a cached coal key. Panel refresh must reflect wallet changes while open, new levels after buying, stage/island switches, cap and insufficient-funds states. A successful global load purchase is visible on other islands without duplicating the saved field.

MarketService.Cost returns zero when its island sale terms are not registered. Therefore a new-game test must include completed save loading and island registration before judging affordability. Present missing registration as unavailable/loading, never as a free purchase or completed track. Do not fabricate an empty save or register an unhydrated yard to make the panel work.

Use the existing save lifecycle through the service/owner's integration; verify purchase then reload. No new currency or second save writer. Ensure automatic production/selling continues behind the panel. Initial implementation may be a simple six-row sheet; no recipe selection or multi-product runtime is needed to unblock B.

## C0 acceptance — gate for deleting the door

1. On Main, open the panel without a market-scene transition or player movement. See and purchase all six tracks with adequate funds and registered terms.
2. Each successful purchase deducts exactly the authoritative price once and updates exactly its intended field. HireCollect changes dispatchLevel; CarryCapacity changes the global level.
3. Insufficient cash and maxed tracks cannot purchase. Income arriving while open makes newly affordable actions available. Closing/reopening adds no duplicate click listeners.
4. Switch islands: first five tracks read/write the selected island only; porter load remains global. Return/reload and verify persistence and retained claims.
5. The final hire upgrade satisfies TheYard from new authoritative rows, without stale legacy hires or duplicate chapter rewards.
6. Visually verify the six-row panel on the actual portrait Main view, with readable costs and reachable buttons, plus a fresh/unmaxed save fixture. An all-maxed live save alone does not prove purchases work.
7. Confirm production and automatic sales continue with the panel open. Then Claude removes the market door/navigation and verifies no remaining purchase requires Market.unity, UpgradePad collision, joystick, manual carrying, serving or floor-cash pickup.

## Important temporary-market constraint

Keeping Market.unity reachable as an upgrade screen does not by itself preserve purchasing if UpgradePad still requires OnTriggerStay from CarryStack. Claude's interim screen must offer an actual tap purchase path, or keep the old route intact until C0 replaces it. Do not call the no-manual-interaction acceptance passed while upgrades still require walking onto pads. Both teams should avoid building duplicate long-lived upgrade interfaces; C0 is the target replacement.

## Evidence and status

Source checked: UpgradePad.Update calls MarketService.TryBuy after detecting player collision; the service already provides the six purchase branches, caps, price query and wallet spending. This is UI integration work on an existing purchase authority, unlike the new multi-product recipe pipeline.

C0 is specified and assigned here, not implemented yet. Latest tests reported by Claude are 979 total / 974 passed, with four missing-prefab rendering failures and one masters-workstream odds-sheet expectation failure. Codex has not independently rerun them. SeaFightUI already has a concurrent five-icon/layout fix in the working tree; its owner should verify runtime behavior instead of either idle-shop owner duplicating the edit.

Decorative vehicles: accept Claude's reported authored-off scan as current-scene evidence. Recheck after regenerating deleted phase prefabs or introducing any phase/dressing controller; do not convert this into a permanent claim that future content can never reactivate them.
