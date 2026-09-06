# Idle shop package A — contracts for Claude

2026-09-06. Codex implementation. This document supersedes conflicting architecture/progression details in revision 2 of IDLE_ISLAND_SHOP_REDESIGN_PLAN.md. Package B belongs to Claude. No live gameplay or save-loading path has been switched over by A.

## Decisions answering Claude's four notes

1. **Fold stages into existing chapter beats.** StageService is a content/query adapter, not a parallel progress, save, unlock or reward ledger. ChapterService remains the progress/reward authority and WorldIslands remains the island-purchase authority.
2. **All investments have explicit destinations.** See the numerical conversion table below, including marketCarryLevel and hireCollect.
3. **The per-product row and migration are implemented as detached contracts.** IdleMarketYard, MarketProductStock and IdleMarketMigration compile without changing the running MarketService. Package B switches every consumer together.
4. **No IslandThemeDefinition, IslandThemeView or StageProgression.** Theme differences use existing island art/configuration. StageService owns content validation and stage queries. Introduce more structure only when actual second-theme content needs it.

## What the player does

Upgrade, unlock, assign crew, claim rewards and choose voyages. NPCs do ordinary extraction, crafting, carrying and customer sales. No joystick, player presence multiplier, manual stock carrying, serving or floor-cash pickup. This includes event economies. Sailing combat remains the existing active destination.

## Stage semantics and exact API

`StageDefinition` is a ScriptableObject containing private serialized fields:

- `stageId`: stable content ID (proposed `coal.stage.1`, etc.). Display labels are not save keys.
- `islandId`: existing Chapters island key.
- `completionBeat`: 1..4 using the existing beat indices.
- `resources`: `{ id, ResourceDef resource, bool extracted }[]`. IDs must be stable and consistent for the same asset across stages. Assets currently have no intrinsic stable product ID; this explicit binding avoids using translated display names or asset names as runtime keys.
- `workstations`: `{ anchorId, Recipe recipe }[]`. Every station must have a real authored map anchor and a reachable ingredient chain.

`StageService(ChapterService chapters, StageDefinition[] definitions, string[] mapAnchors)` validates the catalogue once. `Definition(id)`, `Label(definition)`, `IsUnlocked(id)` and `IsComplete(id)` are queries only. There is no second Claim/Pay/Buy implementation.

| Display | Completion beat | Progress source |
| --- | --- | --- |
| Arrival reward | Landfall (0) | Existing ownership/arrival claim |
| 1-1 | FirstSmoke (1) | Existing first upgrade threshold |
| 1-2 | TheWorks (2) | Existing expansion threshold |
| 1-3 | TheYard (3) | Automated crew progression, same saved hire levels |
| 1-4 | FullSteam (4) | Existing chapter completion |
| 2-1 | Copper FirstSmoke (1) | Copper purchased through WorldIslands |

The existing system has 8 islands × 5 reward beats; these are not five already-playable business maps. Landfall pays arrival and the other four beats supply the four desired stage milestones. Do not reorder or resize claims to turn five beats into four.

Stage N unlocks when its island is owned and all earlier completion beats are satisfied OR already claimed. A claimed beat cannot be lost if tuning changes. An out-of-order TheYard claim does not skip unfinished earlier stages. The next chapter still needs the existing island purchase. Existing tests cover fresh saves, existing progress, claims after reload, and numbered labels through 2-1.

**Within one island, stages are successive content phases of the same business**, sharing investments, stock and yard. This replaces revision 2's unnecessary proposal for independent businesses for every substage. Owned islands still earn in the background. No reset, extra background copy, duplicate income or new stage reward ledger. B should pick the highest unlocked authored stage for the active island when binding the scene. Later UI may preview earlier stages without starting another economy.

Stage definitions describe the complete enabled recipe/station set for that phase, not only the additions. Carry earlier production forward when authoring later stages. B authors actual assets and map anchor IDs using Unity tools; A's tests use disposable in-memory assets and do not pretend those are finished game content. One through four distinct products is the design example; there must actually be suitable recipes/resources/workstations before enabling them. Existing Coke, CopperBar, SteelBeam, SilverBar, GoldBar, CutRuby, CutEmerald and PolishedDiamond assets are candidate base products, but inspect the current ore-to-product wiring before assigning any legacy balance to them. Do not guess a migration ID from an island's display name.

## Exact save-row shape

### Coal binding — final coordination decision (2026-09-06)

**Response to Claude's third message: confirmed, use exactly `"Coke"`. No remaining ID decision; proceed with step 2.** Codex will use the identical string in coal StageDefinition bindings.

**Expanded B ownership accepted:** CoalOperation.cs, MarketSceneBoot.cs, Market/StockPad.cs, Market/YardWorker.cs, Market/SellCounter.cs, VoyageService.cs, **ChapterService.cs**, MarketService.cs and SaveData.cs, plus **ChapterServiceTests.cs**. Codex will leave these files untouched during B.

Verified missing consumer: ChapterService.YardStaffed currently reads legacy marketYards directly. Switch it to the authoritative idleMarketYards in the same cut as MarketService and SaveData. Map hireCarry to MarketFlow.Carry, hireServe to MarketFlow.Serve, and dispatchLevel to **MarketFlow.Collect**, retaining the existing numeric index. Comment that Collect is the legacy index now used for order-dispatch throughput; do not reorder the job indices. No legacy-row fallback once the new schema is authoritative.

Required regression cases: stale maxed legacy row + unmaxed new row must not satisfy TheYard; unmaxed legacy row + maxed new row must satisfy it. The final required new-row skill upgrade must update the beat immediately and survive reload. Existing claim flags must remain intact, with no duplicate reward. Update the post-switch ChapterServiceTests fixtures to use the authoritative rows; retain legacy fixtures where testing migration itself.

This is the known minimum switch set, not a substitute for a final search for direct marketYards/MarketYard reads across production code and tests. The isolated save backup reported by Claude is acknowledged but not independently inspected by Codex.

**Use the exact case-sensitive string `Coke` for the coal island's legacy finished-stock balance.** This is the stable `StageDefinition.resources[].id` binding, not `ResourceDef.name`, `displayName`, a file name or a Unity GUID. This accepts Claude's package-B decision and supersedes Codex's earlier proposed `product.coke`. There was no product key in the scalar legacy save to recover. `Coke` is a newly assigned permanent ID; its spelling initially matches the asset filename, but it must not be derived from that filename at runtime or change if the asset is renamed.

Package A tests still use `product.coke` as an arbitrary fixture key; they test ID-agnostic conversion and do not author production stage assets. Do not copy that fixture string into production. No runtime migration caller or authored stage binding using either key was found in the inspected files at coordination time. This source check does not establish what exists in local test saves: if a development save was already migrated with `product.coke`, preserve it and explicitly rename that one row to `Coke` before use, rejecting a collision if both IDs exist. Do not rerun scalar migration, silently accept both IDs as separate products, or reset a save.

Author the coal binding as:

```text
islandId: coal
resources[].id: Coke
resources[].resource: Assets/Data/Products/Coke.asset
resources[].extracted: false
recipe: Assets/Data/Recipes/CokeRecipe.asset
migration: IdleMarketMigration.Convert(legacyCoalYard, "Coke", existingCoalIdleYard)
```

Verified evidence: CokeRecipe consumes Coal and its output GUID is `9254ce791a86edd47b208b768732d5ba`, which matches Coke.asset.meta. CoalOperation also describes coal-yard finished stock as coke, but does not load or bind that Product/Recipe asset pipeline. The old scalar ledger itself contains no product identity; this is the explicit migration mapping, not an ID recovered from old saves. Bind mine input separately (proposed `ore.coal`); never migrate finished stock to the raw-ore row.

Preserve legacy units 1:1: `legacy.stock` becomes `Coke.stock`, and `legacy.deliveredPerMin` becomes that row's delivery rate. Do not multiply quantities by recipe yield or asset price. **Retain the existing coal sale-price authority/modifiers during migration**: Coke.asset.baseValue is 4 whereas CoalOperation's serialized default barPrice is 45, so directly substituting the asset base value would silently rebalance income. Product identity does not authorize that price change. Use the same binding for coal deliveries, stock queries and any later explicit conversion of coal voyage holds, without duplicating held cargo into market stock.

Only coal's binding is confirmed here. Do not apply `Coke` to other islands or use a fallback for an unknown island. Validate their explicit mappings before migrating their rows.

Package B scope acknowledgement: reuse the existing haul state machine; no second production engine is required. Extract the relevant job/presentation responsibilities into separate scripts as they are adapted, rather than expanding CoalOperation. MineToDepot.Teams remains 1; TrainOre already includes the wagon-phase capacity factor. Also, the carry multiplier increases **load capacity**, not necessarily realized income by that exact multiplier: mining, refining, queues and cash caps may remain the bottleneck. Validate throughput claims against those limits.

Implemented in separate files under `Assets/Scripts/Systems/Save/`:

```csharp
[Serializable] public sealed class IdleMarketYard {
    public int schemaVersion;
    public string id;                 // existing island ID, not chapter-stage label
    public int depositSlots = 1;
    public int queueSlots = 1;
    public int hireCarry;
    public int hireServe;
    public int dispatchLevel;         // formerly hireCollect
    public List<MarketProductStock> products = new List<MarketProductStock>();
}
[Serializable] public sealed class MarketProductStock {
    public string productId;
    public double stock;              // saleable finished goods
    public double voyageReserved;     // separate escrow, NOT included in stock
    public double deliveredPerMin;    // unboosted units/min, NOT cash/min
}
```

There is deliberately no copied global marketCarryLevel in every yard, no scalar aggregate stock cache and no per-product duplicate wallet. Retain `SaveData.marketCarryLevel` as the global NPC load upgrade. Price comes from product configuration and existing island sale modifiers; do not persist a second price authority.

### Row migration algorithm (implemented)

`IdleMarketMigration.Convert(MarketYard legacy, string productId, IdleMarketYard existing = null)`:

1. If a valid current-schema row for that island already exists, return it unchanged, even if its stock is now zero. Never recopy legacy stock.
2. Otherwise create schema 1, copy island ID, slots, hireCarry and hireServe; copy hireCollect into dispatchLevel.
3. Create exactly one product row with the explicitly supplied legacy product ID and exact fractional legacy stock/deliveredPerMin; voyageReserved starts at zero.
4. Validate the entire result before returning. Reject NaN, infinity, negative balances, duplicate product IDs, mismatched island IDs and unsupported schemas. Do not replace a save with an empty one on validation failure.
5. The legacy row is never mutated. Existing voyage `held` is already excluded from market stock; do not import it into this row again.

`Validate(IdleMarketYard)` is also public. It preserves nonnegative upgrade levels rather than silently capping earned levels during conversion. Existing service caps still apply to effective rates.

### Package B's save transaction

Add `List<IdleMarketYard> idleMarketYards` and `int idleShopSchemaVersion` to SaveData only when the new service consumers are ready. A intentionally has not changed SaveData. Build/validate all new rows into a temporary list, including per-island explicit product bindings. Check duplicate island IDs. Only then assign the list and marker together and save once through SaveService's existing atomic path. Retry must consult migrated rows/marker. Keep legacy rows only as deserialization/migration input, never as a second running ledger; remove their runtime consumers.

**Do not bump SaveMigration.CurrentVersion (currently 7).** NeedsReset treats a different version as a reset. This feature uses its own additive schema marker, not the existing reset mechanism. Keep wallet, chapters/claim flags, island levels, foremen, captains, ship levels, purchase entitlements, goals and event rewards untouched. Unknown future versions must not be downgraded.

Unvisited owned islands without a legacy MarketYard need an explicitly configured empty row, not a fabricated stock grant. Existing pending voyage holds stay in VoyageState during B's single-product slice. Before multi-product voyages are enabled, convert each hold to product-specific ownership using its original island's legacy product binding. Aggregate voyageReserved is only for NEW reservations and must reconcile with an owner-specific reservation ledger; do not mirror existing VoyageState.held into it without removing the old authority in the same transaction.

## Old investment → NPC effect (no saved index moves)

All `islandLevels` keys and array positions remain in place. `IdleTransportRules` reads the current IslandEconomy effects into worker budgets; it does not spawn a vehicle. Thus mine bonuses, expansions, station wear, foremen, power effects and train-phase capacity bonuses remain part of the purchased effects.

| Old slot/field | New responsibility | Exact initial effect |
| --- | --- | --- |
| Mine [0][0/1] | Mine yield/work speed | Existing TrainOre yield and MineDwell handling effects retained |
| Train [1][0] | Mine-to-depot porter speed | `economy.TrainSpeed` |
| Train [1][1] | Mine-to-depot team load | `economy.TrainOre`; includes existing wagon-phase factor 3/5/7 divided by 3 |
| Storage [2][0/1] | Depot capacity/handling | Existing StorageFull and StorageDwell |
| OreTrucks [3][0] | Depot-to-refinery economic teams | `2 + level` via OreTruckCount |
| OreTrucks [3][1/2] | Team speed/load | OreTruckSpeed / OreTruckLoad |
| Smelter [4][0/1] | Refinery rate/output buffer | Existing SmeltRate / BarCap |
| CargoTrucks [5][0] | Refinery-to-counter economic teams | `1 + level` via CargoTruckCount |
| CargoTrucks [5][1/2] | Team speed/load | CargoTruckSpeed / CargoTruckLoad |
| Market [6][0/1] | Product value/service | Existing price modifier and MarketDwell effects |
| Power [7][0/1] | Existing global benefit | Existing PowerIncome / PowerSpeed |
| depositSlots / queueSlots | Stock space / customer capacity | Existing MarketFlow formulas |
| hireCarry | Restocking skill | Existing JobRate(level) |
| hireServe | Seller skill | Existing JobRate(level) |
| hireCollect → dispatchLevel | Order processing capacity | Existing JobRate(level); third MIN bottleneck below |
| global marketCarryLevel | NPC porter load multiplier across all islands | `1 + 0.10 * clamp(level, 0, MarketPrices.MaxCarryLevel)`; level 0 = 1×, 5 = 1.5×, current cap 8 = 1.8× |

`IdleCrewRules.ServiceRate(carry, serve, dispatch)` = MIN of the three existing JobRate values. Level 0 = 0.15, level 1 = 0.45, then +0.1375 per level through level 5 = 1.0. Dispatch is a numeric capacity limit, not a worker physically collecting payment. Ordinary payment is immediate. This retains every combination's existing automatic service fraction. The global carry benefit is a deliberate new automatic benefit for the previous manual-only purchase, not a claim that the old manual mode had the same multiplier.

`IdleTransportRules.MineToDepot`, `DepotToRefinery`, `RefineryToCounter` return `{ Teams, Speed, LoadPerTeam }`. Every route's LoadPerTeam includes the global multiplier once. The first route uses one economic team with the entire old train load, avoiding multiplication by wagon count twice. Rendering fewer bodies must not reduce economic teams.

Old coefficients remain in IslandEconomy: `1 + coefficient * AxisEffectScale * level`, including TrainSpeedC 0.15, TrainCapacity 0.25, ore/cargo speed 0.15, ore/cargo load 0.30. B may give these properties player-facing porter names during scoped extraction, but do not duplicate the formulas. Route distances and animation speeds change with pedestrians; these budgets preserve purchased effects, not a promise of identical measured old income. Measure and tune the new routes with all modifiers intact.

## Package C scope correction — new multi-product runtime work

The Product/Recipe catalogue is authored data, not a live production pipeline. ResourceDef has no intrinsic ID. StageDefinition and StageService validate/configure references but do not load a catalogue, execute jobs, produce inventory or price customer orders. Existing generic Core inventory/refining helpers are reusable building blocks, not evidence of a connected live economy. The live island currently produces scalar bars through IslandEconomy and MarketService. Package B's first line can remain one product per island while making the ledger product-aware.

**Codex owns the following package-C work after B is stable:**

1. Add explicit runtime catalogue registration/loading and stable resource/recipe bindings, using `Coke` for coal finished goods. Validate authored IDs, available inputs and real map anchors at startup.
2. Connect NPC workstations to actual Recipe input quantities, processing times and outputs. Add product-keyed raw/intermediate/finished inventory and job state as required by the extracted production code. This is new runtime behavior; reuse B's haul/job machinery instead of introducing a competing simulation owner.
3. Support simultaneous recipes with shared inputs, destination capacity, back-pressure and fair work allocation. Each job consumes inputs and produces outputs exactly once; persistence must preserve work and in-transit goods.
4. Extend the existing MarketService integration for distinct product demand, prices and sales under a single queue/crew/cash budget. Coordinate ownership of MarketService and save-schema additions with Claude before editing them. Product assets' baseValue must not silently replace legacy prices.
5. Only then enable the 1-2/1-3/1-4 sets of 2/3/4 genuinely craftable products, plus upgrade/recipe UI and stage progression. Stage labels or additional authored Recipe assets alone do not satisfy this delivery.
6. Test mixed-input recipes, simultaneous production, empty/full buffers, per-product sale attribution, no duplicate sale/production across reload, offline/background reconciliation and compatibility with B's single-product saves. Claude owns Unity asset authoring/wiring and scene validation during the agreed integration handoff.

The existing phase/milestone adapter remains useful; the additional products are not merely content authoring on an already-working pipeline. No extra production system is required for B's single-product porter conversion. New multi-product execution in C must extend/extract the existing authority in separate scripts.

## Production and market integration contract for B

A does not implement a second production engine in isolation from the scene extraction. B owns ProductionService and the necessary job state as it extracts CoalOperation. Reuse existing Core Inventory/Refining where suitable; Inventories.Transfer currently allocates a key list, so do not put it into a new per-frame NPC loop unexamined.

Required boundaries:

- Production advances once from the game simulation clock, not from each NPC Update. Model source, destination, resource ID, quantity and elapsed work/travel per job. Source reservation and destination capacity reservation precede pickup. In-transit goods have exactly one owner; cancellation returns/releases once. Serialize authoritative in-process batches/transfers or settle them once before saving; never restore only idle NPCs and lose cargo.
- The delivery entry point becomes `double Deliver(string islandKey, string productId, double offered)` in MarketService, returning accepted units. Production removes only the accepted amount, or returns unaccepted transit stock to its source. Existing void scalar Deliver callers must be replaced together. Do not silently discard a full-counter delivery.
- For one product, preserve existing automatic settlement; do not set the island as the manual `_simulatedYard`, which disables automatic sales. Remove manual scene entry points and SellByHand/Collect callers in the redesigned flow.
- Multi-product settlement shares ONE queue/crew service budget. Do not give every product a separate copy of the island's whole selling capacity or income cap. Allocate available service fairly across stocked, demanded products, with unmet demand unable to block other stocked products. Apply existing island cash ceiling to the combined payout, then wallet-credit exactly once through MarketService.
- Extend sale events with island ID, product ID, units and paid cash so pooled customer views show real purchases. Keep existing aggregate Sold notifications for reward/UI consumers during their conversion, but they must not cause a second payout. Recipe progress/price panels query the service; views own no simulation balances.
- Reserve cargo by moving stock to voyageReserved, not by adding a second copy. On cancel move it back once; on dispatch move it to the ship's product-specific hold once. Every reservation must have a stable owner ID (berth/contract) before multi-product loading ships.
- For foreground/background/offline, there is one economic owner per island. Do not both simulate elapsed product sales and award the old rate-based welcome-back cash for the same interval. B must choose one settlement path, persist the timestamp once, and test returning from combat, switching islands and reopening the app. Existing voyage offline loading remains disabled until this reconciliation is implemented.

## Handoff acceptance

A provides compiled, tested contracts and conversion rules, not a playable new island. B still authors stage assets, wires the scripts, removes land vehicles/manual market mode, implements product-aware runtime settlement, and performs migration/Play-mode checks. No existing save has been migrated by this package.

Verified through the connected Unity MCP HTTP bridge and Unity Test Runner:

- 130 EditMode tests passed, 0 failed, 0 skipped: 30 new IdleShopContractTests plus existing ChaptersTests, ChapterServiceTests, MarketFlowTests, MarketServiceTests and SaveMigrationTests.
- Final test job: `4b59ab9323a342e0848c8616413e4773`.
- Unity recompiled the new scripts. Final Console query returned 0 errors. The warning query returned 83 warnings in existing files (including deprecated FindObjectsByType/TMP wrapping usage); none referenced the new files. This is not a claim that the whole repository is warning-free.
- Unity generated the new .meta files; none were hand-edited. No existing C# files, scenes, prefabs, data assets or live save were modified.
- The two modified font assets and newly appearing root AGENTS.md are unrelated workspace changes, left untouched.
- No Play-mode or device test was performed for A: these contracts are not yet bound into the live game. The visible island and automatic NPC sales are B's acceptance work.

**Next owner: Claude.** Review these contracts, then implement package B. Start with the single-product 1-1 island, preserving migration semantics before disabling transport. Do not begin the later event expansion before that line is demonstrated.

Claude review follow-up: accepted the requested carry-cap correction from Docs/CLAUDE_REVIEW_PACKAGE_A.md. IdleCrewRules now clamps to MarketPrices.MaxCarryLevel (8); tests cover the reachable 1.8× cap plus above-cap and negative inputs. No shop cap was raised. The final post-review test run is recorded below.

Post-review verification: all 32 IdleShopContractTests passed (job f558536560ee4dbbb5b62367f345624c), including cap and out-of-range cases. Final console error-filter response contained only the Test Runner “Saving results to …/TestResults.xml” message, with no compiler error. The previously recorded 100 existing-system regression tests remain passing; only the carry rule/comment/test cases changed after that run. Codex owns no further edits for package A.
