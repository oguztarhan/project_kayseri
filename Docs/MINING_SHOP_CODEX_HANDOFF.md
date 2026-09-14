# Mining shop — Codex foundation handoff

2026-09-14. User approved starting coordinated work after clarifying variable chapter lengths. Current milestone: campaign contract and Unity integration audit. This is not a playable-business completion report.

## Files owned and added by Codex

- `Assets/Scripts/Core/MiningShopCampaign.cs`: immutable catalogue with stable business/chapter IDs, one-based display coordinates and zero-based overall CampaignIndex. Provides IslandAt, TryGet and TryGetNext. No saves, rewards, money, scene changes or legacy ChapterService writes.
- `Assets/Scripts/Data/MiningShopCampaignConfig.cs`: Inspector-editable chapter entries containing explicit stable island IDs. Default example lists contain 7, 6 and 9 islands. CreateCampaign validates and copies them at registration. No .asset was created or edited.
- `Assets/Scripts/Tests/EditMode/MiningShopCampaignTests.cs`: 14 NUnit cases including both chapter transitions, arbitrary chapter lengths/IDs, end-of-content, invalid content, defensive copying, cumulative product availability and distinct captain equipment ordering.
- `Docs/MAIN_SCREEN_MINING_SHOP_PLAN.md`: latest user correction incorporated.

No existing gameplay/shared C# files were edited. Unity owns any generated metadata. There is no runtime registration yet, deliberately: scene choice, service ownership, and migration must be agreed first.

## Contract for Claude review

- Example default IDs are `mining-shop.chapter-01` and `mining-shop.island-01-01` through the authored list. IDs are opaque; display coordinates come from catalogue order. Keep existing IDs unchanged when appending content.
- Overall CampaignIndex continues 6→7 at 1-7→2-1 and 12→13 at 2-6→3-1. Future economy tuning must use overall progression rather than restarting at IslandNumber 1. This index is not a tested difficulty curve or final balance.
- AvailableProductCount rises 1,2,3,4 and stays four through later chapters. StartingTableCount is always one; this is a definition, not a built-table save record. Runtime must initialize a new business once and preserve purchased tables on return.
- ProductIdAt unlock order: `mining-shop.pickaxe`, `mining-shop.helmet`, `mining-shop.lantern`, `mining-shop.bag`. These are new merchandise identifiers, not MiningGear saved slot indices.
- Unknown or final IDs return false from TryGetNext; no wrap, implicit new chapter or destructive fallback.
- Construction costs, income curves, progression transactions, chapter rewards and save migration remain outstanding. No legacy ore or ship-equipment IDs have been remapped.

## Verification request

Claude: after the source files have imported, wait for Unity recompile, inspect the console, and run `Game.Tests.MiningShopCampaignTests` through Unity MCP `run_tests` in EditMode. Report exact result count and any errors/new warnings in `Docs/MINING_SHOP_CLAUDE_HANDOFF.md`. Do not clear the baseline console. No external/manual test runner was used by Codex.

Source review by Codex confirms Data references Core and tests reference both. Claude's recorded Unity verification now reports all three types imported, no new-file console diagnostics, and 14/14 EditMode cases passed (0 failed/skipped; 1.25 s; job 14ac25b57b934c57a2066de91c75ec96). Codex read the result in MINING_SHOP_CLAUDE_HANDOFF.md. Existing project warnings and the baseline Android Remote issue are disclosed there; this is not an all-clear for the project or a gameplay verification.

Claude agrees with the campaign API. IslandAt requires a valid index in [0, IslandCount); use TryGet for external/saved IDs. Inspector-authored config becomes the content source of truth once an asset exists; changing code defaults will not migrate that asset. Runtime difficulty curves, build purchases and all scene behavior remain unimplemented.

Initial coordination response (superseded on target screen by the checkpoint below): preserving legacy rows untouched is already consistent with the approved plan, so no new approval is needed to maintain that boundary. A short market-side carrier route is a reasonable proposed implementation choice, to be visually reviewed during scene authoring.

Next handoff: record target scene/startup evidence and blockers, agree this API and proposed product-to-map bindings, then finish/confirm this foundation milestone before implementing the pickaxe simulation and scene integration. User wires Inspector references unless explicitly delegated.

## Latest status synchronization with Claude

The user requested that Codex contact Claude to establish where the plan was left. Claude confirmed there is no newer implementation or unrecorded work. Package 0 remains the completed catalogue/audit milestone, with 14/14 previously recorded Unity tests; gameplay is not implemented or registered.

Claude retrieved earlier user direction from its session: “keep island (Main) as main view” and “island is main view flip the flag.” This agrees with the repository worklist naming Main as production and Shipyard as preview. On that evidence, Main is the target; the repeated screen question was unnecessary. SaveData's default-on portrait flag is an implementation mismatch to address under explicit file ownership, without resetting saves. No startup setting was changed in this status synchronization.

Next unfinished package: Codex owns package 1, the 10-second pickaxe production/stock/sale contract and tests. Claude owns package 2, new mining-shop anchors/routes and visible craft/carry/customer integration in Main after that contract is delivered. Preserve existing legacy records and use separate business IDs. Existing warnings and the Android Remote baseline issue remain disclosed.

Whether completed numbered businesses continue earning remains a proposed design default rather than an explicit user answer. It need not block the first single-business production contract; resolve its cross-island/offline behavior before implementing those transitions. Do not turn that into another prerequisite for unrelated scene or single-business work.

## Package 1 — pickaxe logic verified and handed to scene integration

User/Claude requested continuation and proposed bounded parallel prep. Codex approved Claude authoring ONLY empty anchors/routes in Main via Unity MCP while Codex implements this package. No Inspector wiring or other C# ownership was delegated to Claude. Package 2 visual implementation follows package 1 verification.

### Changed C# and ownership

New: `Core/MiningShopState.cs`, `Core/MiningShopSimulation.cs`, `Data/MiningShopConfig.cs`, `Systems/MiningShopService.cs`, `Tests/EditMode/MiningShopSimulationTests.cs`, `Tests/EditMode/MiningShopServiceTests.cs`.

Codex-owned existing edits: `SaveData.cs` gains only `activeMiningShopBusinessId` and `List<MiningShopState> miningShopBusinesses`; `MarketService.cs` gains registration, the shop-only tick branch, dedicated sale receipts, and explicit legacy delivery/offline suppression while the new business is active. No global save version, startup flag, GameBootstrap, HUD or scene was changed by Codex. No .asset or .meta was hand-edited.

### Exact scene-facing API

1. Obtain existing `MarketService` and `SaveService` AFTER Bootstrap has registered the loaded save/wallet. Do not create another wallet or MarketService in a scene.
2. Author/assign `MiningShopCampaignConfig` and `MiningShopConfig` through Unity tools / the user's Inspector wiring. `campaignConfig.CreateCampaign()` creates the immutable catalogue. `shopConfig.ToTuning()` returns validated `MiningShopSimulation.Tuning` (defaults: 10s craft, 20 coins, output/shelf 4 each, carrier load 2, one-way travel 4s, handling 0.5s at either end, customer arrival 3s, service 2s, queue 6).
3. Call `market.OpenMiningShop(campaign, "mining-shop.island-01-01", tuning, saveService)` once on explicit feature entry. Returns `MiningShopService`. Rebinding the SAME ID returns the SAME instance, preserving progress; changing to another business while bound is explicitly rejected until the progression package. Unknown IDs and duplicate/corrupt saved business records are rejected without resetting them. The integration owner must expose only the unlocked first business in this slice.
4. **Do not tick from scene code.** Existing `GameBootstrap.Update → Market.Tick` advances the simulation exactly once per frame. No animation callback supplies goods, deposits, services a customer, pays currency or calls a production timer. Disabling a view does not cancel its business or lose cargo.
5. Read `shop.View` (a value snapshot): BusinessId; Crafting/CraftRemaining/CraftDuration; OutputStock/PickupReserved/Cargo/ShelfStock; Carrier/CarrierRemaining/CarrierDuration; WaitingCustomers; Serving/ServiceRemaining/ServiceDuration; Produced/Sold/Earned; ElapsedSeconds/PendingSeconds; SpeedLevel/ValueLevel. No allocations are required to read it.
6. Carrier phases: Idle → Loading → ToMarket → Unloading → Returning → Idle. WaitingForSpace retains cargo on partial delivery (including a smaller shelf after retuning/reload). Idle/Loading are at the workshop endpoint; ToMarket interpolates forward by `1 - CarrierRemaining / CarrierDuration`; Unloading/WaitingForSpace are at the shelf endpoint; Returning interpolates back. Use the prepared route's points and measured distance; choose/adjust authored travel duration deliberately. Do not multiply economics by the number of rendered people.
7. Display `OutputStock + PickupReserved` on the rack until Loading completes; PickupReserved is still physically at the table. Display Cargo in the carrier's load and ShelfStock on the shelf. Serving reserves a separate ONE item with its price fixed at service start; it is no longer shelf stock. Crafting reserves one output space at start. These are disjoint ownership counts; Produced excludes unfinished crafts.
8. Listen to `market.MiningShopSold` for `MiningShopSimulation.Sale { BusinessId, ProductId, Sequence, Cash }`. Each receipt is one unit. ProductId is `mining-shop.pickaxe`; Sequence equals the persistent business Sold count. Wallet and sold state are already updated before the event. Use receipts only for customer departure/coin effects. Do not reuse legacy `MarketService.Sold` or create a second payment. MarketService is the only wallet settlement owner.
9. Upgrade UI reads `shop.CraftSeconds`, `shop.UnitPrice`, `shop.UpgradeCost(speed)`; pass true for speed or false for value. Zero upgrade cost means capped, NOT a free purchase. `shop.TryBuyUpgrade(speed)` returns success; it spends the existing wallet, preserves current craft fraction, saves if SaveService was supplied, and raises `shop.Changed`. Failed/capped/reentrant purchases spend nothing. Current customer price is unchanged by a value upgrade. Changed is an upgrade event, not a per-frame text redraw signal.

### Save and integration behavior to keep explicit

- `MiningShopState` persists craft/carrier/service remaining times, reservations, cargo, shelf/output stock, queued customers, upgrade levels, produced/sold/earned totals and unprocessed elapsed time. SaveData and WalletData are serialized together. Resume reconstructs jobs without replaying receipts or wall-clock time. Legacy ore rows, claims, captain equipment indices and paid entitlement fields are untouched.
- Opening the new business explicitly selects a new economy: Market.Tick advances only this shop, legacy Deliver refuses loads (returns 0), legacy SettleOffline leaves old stock unchanged, and legacy `incomeRatePerSec` is zeroed. It stays zero after restart until the business is rebound, so the new shop does not inherit an unrelated ore offline payout. No legacy row is relabelled or erased. Suppression covers legacy market income; package 2 still needs to prevent legacy production/ship-item UI from confusing the visible business.
- The feature remains **unregistered in normal startup** until package 2 integration. This package does not flip startup defaults or automatically activate a business on the live save. On entry pass SaveService to persist the selection with its zeroed offline rate. The live Market HUD's legacy rate APIs are not the new shop's income display.
- This is foreground operation only. There is no offline reward, completed-business income, cross-business transition, stage reward, additional table, modifier/paid-perk integration or tip feature yet. Those belong to later packages; no production release or offline parity is claimed.
- Advance is bounded to 256 event boundaries per call. A long foreground hitch leaves PendingSeconds in the save; following Market.Tick calls drain it. Purchases are refused until that older time is processed, avoiding retroactive upgrades. This is not permission for views to feed wall-clock/offline intervals into Tick.
- One-way path defaults to 4s; measured scene geometry may require different tuning. Expected first base sale is 17s (10 craft + 0.5 load + 4 travel + 0.5 unload + 2 service), before any art or queue approach delays. New service customers must use this authoritative timing.

### Verification request / current status

Source edits complete. Claude imported/recompiled in Unity and ran the requested tests; Codex read and reviewed the recorded results in Claude's handoff section 9. No external/manual test runner was used by Codex.

Unity MCP `run_tests` EditMode job `c60e8197f24545b8b1bab890c341f136`: **110 total, 110 passed, 0 failed, 0 skipped in 2.97 seconds**. Breakdown: MiningShopSimulationTests 15, MiningShopServiceTests 13, MiningShopCampaignTests 14, MarketServiceTests 27, SaveServiceTests 2, SaveMigrationTests 11, ShipyardFoundationTests 28. Console after compile/tests: 0 errors; pre-existing warnings outside the changed files remain, and the Android Remote baseline issue is not fixed. No new diagnostics mentioned MiningShop, MarketService or SaveData. This verifies logic and serialized transaction behavior, not visual gameplay, startup integration or Android performance.

New cases cover timed first sale, equivalent tick partitions, finite racks/shelves, partial delivery after retuning, six reload phases, mid-craft upgrade, locked customer pricing, bounded time debt, invalid state/time, one clock/payment, reentrant notifications, atomic purchase/sale snapshots, encrypted save restoration, preserved legacy state, explicit legacy suppression, and unchanged legacy behavior when the feature is not active.

Unexpected working-tree changes observed during parallel work: Main.unity (expected from Claude's prep) and `Assets/Art/Fonts/Baloo2-ExtraBold SDF.asset` (not edited by Codex). Claude should identify the font change's source and handle any cleanup only through Unity tools; Codex will not hand-edit/revert it.

Claude identified the font change as TMP dynamic glyph additions flushed with the scene save. Main also serialized pre-existing Editor UI layout overrides. These were retained and disclosed rather than manually reverted; see Claude handoff section 8. The new authored route is flat, 111 units, and has three points. Default camera projection places the market just below the portrait viewport, so package 2 must explicitly verify/fix camera focus.

Package 1 is now handed to Claude for package 2: focused new Gameplay/UI views and binder, visible pickaxe table/rack/load/shelf/customer flow, compact useful upgrades, and Main camera framing. Existing approved project restrictions on Inspector wiring remain. **Explicit ownership transfer sent to Claude:** GameBootstrap and SaveData startup-selection fields may be edited by Claude for the necessary fresh/existing-save Main correction and registration in package 2; record that boundary before editing, preserve the new shop payload and legacy records, and do not bump the global save version. Codex retains MarketService, MiningShopService and the core simulation; send concrete API issues back instead of editing those files concurrently. Use isolated test saves for activation/reload checks. The next verification milestone is observed pickaxe gameplay and reload through normal startup, not another catalogue-only test run.

## Package 3 — four-product core and service, Unity verified

Package 2 is accepted after Claude's final legibility pass (`round11_loop_hud_panel.png`, 118/118 EditMode tests, save hash restoration). Package 3 is also accepted: Unity compiled cleanly with no mining-shop console errors, and the targeted EditMode suite passed 104/104. This covers ten four-product simulation tests, four business-service tests, legacy shop coverage, save migration/reload, payment mutual exclusion, and localization. Package 4 has now switched Main to that verified business service and passed 133/133 targeted EditMode tests. Its old-save regression enters from JSON that predates the `Business` field, crosses the encrypted SaveService boundary, migrates the flat pickaxe/carrier/receipt state once, and reloads without replaying payments. An isolated copy of the live pickaxe save also resumed visually with legacy haulage hidden; the live save was restored byte-for-byte.

Package 4 is accepted. Claude owned the Main startup/view/UI/scene migration and four-product presentation; Codex retained the core business simulation, business service, and MarketService. The default remains campaign 1-1, so it shows and runs only pickaxes while the helmet, lantern, and bag visuals remain dormant for later islands. An isolated 1-4 path proved ordered table purchases, shared carrier/seller use and per-product payments.

### Package 4.1 — first-session shop clarity (Claude)

Before broader island progression, fix the visible entry friction on Main. While the four-product shop is active, the legacy Foreman Max tutorial must not cover the shop panel or make the player act through ore instructions. The active HUD must present high-contrast cash and a shop-relevant status rather than a stale `$0/min` ore rate. Preserve wallet ownership, save records, tutorial progression state and the business simulation; this is a UI/presentation package only. Verify with isolated fresh and returning saves through Bootstrap, a visible first sale and upgrade, a panel screenshot without a blocking legacy tutorial, Unity console review and the impacted EditMode suite. No islands, rewards, offline income, economy changes or art pass in this package.

New Core state is additive: `MiningShopState.Business` holds `MiningShopBusinessState`, which owns four
`MiningShopProductLineState` records plus the one shared carrier, shared seller, demand cursors and global
receipt sequence. A first open copies the existing flat pickaxe record into line zero once without clearing or
reinterpreting the predecessor fields. It writes no save-version and does not modify legacy ore rows, chapters or
equipment.

`MiningShopBusinessSimulation` is an independent deterministic model, constructed with
`(MiningShopState owner, int availableProductCount, Tuning tuning, Action<Sale> settle)`. Product order is fixed to
the campaign's merchandise IDs: pickaxe (10 s/$20/free), helmet (20 s/$60/$300), lantern (40 s/$150/$1,400), bag
(60 s/$360/$5,000). `BuildTable(index)` permits only the next unbuilt available line; its caller must validate and
spend `TableCost(index)` atomically. `Upgrade(index, speed)`, `CraftSeconds(index)`, `UnitPrice(index)` and
`UpgradeCost(index, speed)` are product-local. `Advance(seconds)` has the same bounded foreground-only contract as
the pickaxe simulation. `View.ProductAt(index)` exposes each line; `View.CarrierProductIndex` and
`View.ServiceProductIndex` identify the actual carried/sold good. A receipt contains the business ID, correct
product ID, one global monotonic sequence and the price locked when service began.

`MiningShopBusinessService` is the wallet/save facade: `TryBuildTable(index)` and
`TryBuyUpgrade(index, speed)` spend once through `WalletService`, persist through `SaveService`, and refuse pending
time debt. `MarketService.OpenMiningShopBusiness(campaign, businessId, tuning, save)` resolves the campaign's
available-product count, selects one saved record, zeroes the legacy offline rate and becomes the only clock/payer
for the multi-line model. It is mutually exclusive with the package-2 `OpenMiningShop` slice in one runtime
instance. `GameBootstrap` still calls the verified pickaxe-only entry point, so Main remains unchanged until the
following scene/service handoff selects a multi-product business. `MiningShopConfig.ToBusinessTuning()` adds
serialized helmet/lantern/bag duration, price and build-cost fields plus a single-item shared carrier load; existing
pickaxe configuration stays the source for line zero.

New focused tests: `Game.Tests.MiningShopBusinessSimulationTests` (10 expected) cover definitions/configuration,
additive pickaxe transfer, island availability and sequential builds, two-product pricing/receipts, four-line
fairness including bags, one shared seller, per-line upgrades, reload and corrupt/unavailable saved lines.
`Game.Tests.MiningShopBusinessServiceTests` (4 expected) adds atomic builds, receipt wallet settlement,
mutual-exclusion with the old service and encrypted reload. Claude: refresh/import, read the console without
clearing it, then run these classes plus affected existing MiningShop, MarketService, SaveService and localization
EditMode tests through Unity MCP. Report exact totals and diagnostics before any package-4 scene work. Codex owns
these Core/Data/test files during this verification.
