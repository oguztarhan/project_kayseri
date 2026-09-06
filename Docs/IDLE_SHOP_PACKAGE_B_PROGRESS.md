# Package B progress — Claude

Date: 2026-09-06. Owner: Claude. For Codex: this is B's running log; read before touching anything it
claims. A's files remain yours.

## Step 1 — COMPLETE: transport economics routed through IdleTransportRules

**Changed: `Assets/Scripts/Gameplay/CoalOperation.cs` only.** One block, ~12 lines. No other file touched.
No vehicles removed, no scene/prefab edits, no save schema change, no `SaveMigration.CurrentVersion` bump.

The extraction turned out to be far smaller than the plan assumed, and Codex should know why before
scoping anything else:

**CoalOperation's haul loop is already the porter job model.** Lines ~3300-3390 run a single agent state
machine — `ToLoad → Loading → ToDrop → Dropping → ToIdle → Idle` — with an `ore` bool selecting the leg
(depot→refinery, deposits to `_refOre`; or refinery→market, calls `Deliver`). That is exactly the
`idle → reserve → travel → pick up → travel → deposit → repeat` cycle the plan specifies. **The trucks and
train are only the costume.** There is no second production engine to write; there is a rendering and
route-geometry change, plus the ledger work in step 2.

Better still, every transport number was already funnelled through ~9 private properties in one block
(~line 1205), by earlier design. So step 1 was rewriting those properties, nothing else:

| Property | Now reads |
| --- | --- |
| `EffTrainOre` | `IdleTransportRules.MineToDepot(Ec, CarryLevel).LoadPerTeam` |
| `EffTrainSpeed` | `MineToDepot(...).Speed` |
| `OreTruckCount` / `EffOreSpeed` / `EffOreCap` | `DepotToRefinery(...)` `.Teams` / `.Speed` / `.LoadPerTeam` |
| `CargoTruckCount` / `EffCargoSpeed` / `EffCargoCap` | `RefineryToCounter(...)` `.Teams` / `.Speed` / `.LoadPerTeam` |

New `CarryLevel` property sources the global upgrade via
`_marketService.Level(islandKey, YardUpgrade.CarryCapacity)`, null-guarded to 0.

### Two things verified before editing, both confirming A's design

1. **The train leg does not double-count the rake.** `IslandEconomy.TrainOre` already folds in
   `(ActiveWagons / BaseWagons)`, and the sim sets `a.carry = EffTrainOre` for ONE agent.
   `ActiveWagons` / `VisibleWagons` are used only at lines 2024, 2089 and 2347 — all rendering.
   So `MineToDepot`'s `Teams = 1` is correct, and I deliberately do **not** consume `.Teams` on that leg.
   Left a comment at the call site saying so, because it looks like an oversight and is not.
2. **No cache.** Each property recomputes its budget, so a `CrewBudget` is built up to 3x per read. It is
   a struct (no heap allocation) and costs ~40 extra float multiplies per frame across ~20 agents.
   Caching it would need refreshing in both `Tick` and `ApplyFleetStates` and would risk a one-frame
   stale truck count on upgrade. Not worth a staleness bug; revisit only if profiling says so.

### Intended behaviour change

Income is **identical** for any save with `marketCarryLevel == 0`, and higher by exactly
`PorterLoadMultiplier` (max 1.8x after your clamp fix) for players who bought carry levels. That is the
conversion working as designed — the purchase now benefits NPC loads instead of a manual stack.

### Verification

Unity recompiled via the MCP HTTP bridge. Console: **0 errors.**
EditMode suite: **938 tests, 934 passed, 4 failed** (job `d807e0c0c2ea4aba9d723d23b7d46d1f`).

All 4 failures are `RenderingSafetyTests` and are **pre-existing and environmental, not caused by this
change**: commit `fb1de03` deleted the generated island phase prefabs from the repo
(`Assets/Prefabs/Island/Coal|Copper/Island_Phase*.prefab`). `Assets/Prefabs/Island/` now holds only
`IndustrialReference`, and `git status Assets/Prefabs` is empty — I modified nothing there. These tests
pass only on a checkout where `BuildPhasePrefabs` has been run. I have not run it: it regenerates assets
and the user has `Main.unity` open and dirty.

Every economy test passed, including `IdleShopContractTests`, `MarketServiceTests`, `MarketFlowTests`,
`ChaptersTests` and `SaveMigrationTests`.

Note your run reported 130 tests and mine 938 — different scope, not a discrepancy. Mine was the whole
EditMode assembly including the scene-dependent rendering tests.

## Files I claim for steps 2-3 — do not edit

`CoalOperation.cs`, `MarketService.cs`, `SaveData.cs`, `VoyageService.cs`, `MarketSceneBoot.cs`,
`Market/StockPad.cs`, `Market/YardWorker.cs`, `Market/SellCounter.cs`.

## Step 2 — COMPLETE (productId decided as "Coke" on the user's instruction; Codex was offline)

### Finding: the Product/Recipe catalogue is orphaned

Nothing in live code loads `Assets/Data/Products` or `Assets/Data/Recipes`. `Coke.asset` is referenced
only by `CokeRecipe.asset`, and nothing references that. The only code hits for `Product`/`Recipe` are
Codex's new files, `GooglePlayIAPService` (unrelated IAP `Product`), and `MarketSurfaces.cs` — whose
`Recipe` is a private struct for material tiling, a name collision. `ResourceDef` has no ID field.

So the live economy is entirely scalar bars, and the product catalogue has **zero runtime consumers**.
Two consequences: there is no legacy product key in any save to corrupt (a `MarketYard` holds one
`double stock`), and stages 1-2/1-3/1-4 with 2-4 craftable products are NEW system work, not content
authoring onto an existing pipeline. Package C scoping should reflect that.

### Switch-together set: nine consumers, not six

`ChapterService.YardStaffed` (ChapterService.cs:152-163) was on nobody's list. It reads
`_data.marketYards` directly, maps `hireCarry/hireServe/hireCollect` through `MarketFlow.IsMaxed`, and
**gates a chapter beat**. If MarketService moves to `IdleMarketYard` and this keeps reading the legacy
list, the "TheYard" beat freezes or reads stale hire levels forever. It must switch in the same
transaction. It also uses the `MarketFlow.Collect` index for what is now `dispatchLevel`.

| Consumer | Call |
| --- | --- |
| `CoalOperation.cs:3370` | `Deliver` — the island seam |
| `MarketSceneBoot.cs:302` | `Deliver` |
| `Market/StockPad.cs:123,125` | `TakeFromStock`, `Deliver` |
| `Market/YardWorker.cs:153` | `TakeFromStock` |
| `Market/SellCounter.cs:113` | `SellByHand` |
| `VoyageService.cs:601` | `TakeFromStock` |
| `ChapterService.cs:154-157` | `YardStaffed` — beat gate |
| `MarketService.cs` | 35 `.save.*` sites |
| `SaveData.cs` | new rows + schema marker |

Plus `ChapterServiceTests.cs:37,137`, which construct `MarketYard` rows directly.

### Safety done

The live save is encrypted binary, 9408 bytes, at
`~/Library/Application Support/Intake Entertainment/Island Mining Tycoon/save.dat`. Backed up to an
isolated copy before any migration work, per the handoff.

### The product ids — DECIDED, and Codex must match them

The user instructed me to proceed without waiting. The mapping was not guessed from display names: it
was read off the recipe assets, which pin each product to the ore its island mines (`CokeRecipe` consumes
Coal, `CopperBarRecipe` consumes Copper, `SteelRecipe` consumes Iron, and so on). It lives in one place,
`MarketService.ProductFor`, and a test asserts all eight are present and distinct.

| Island | productId | Island | productId |
| --- | --- | --- | --- |
| coal | `Coke` | gold | `GoldBar` |
| copper | `CopperBar` | ruby | `CutRuby` |
| iron | `SteelBeam` | emerald | `CutEmerald` |
| silver | `SilverBar` | diamond | `PolishedDiamond` |

**These are save keys.** `StageDefinition.resources[].id` must use these exact strings or migrated rows
read as zero stock.

### What step 2 actually changed

- `SaveData`: added `idleMarketYards` + `idleShopSchemaVersion`. Additive; the legacy `marketYards` list
  stays on disk as migration input only and has no runtime consumer left. **`SaveMigration.CurrentVersion`
  is still 7** — bumping it would make `NeedsReset` wipe every player.
- `MarketService`: `Yard.save` is now `IdleMarketYard`; all 35 `.save.*` sites moved to the product row via
  a `ProductRow(Yard)` helper. `MigrateOrNewRow` converts a legacy row on first touch through
  `IdleMarketMigration.Convert`, which is idempotent — after the yard sells out, reload does not re-credit.
- `Deliver` is now `double Deliver(string islandKey, string productId, double bars)` returning accepted
  units. Every caller still drops the remainder exactly as before, so behaviour is unchanged; handing
  overflow back to source belongs with step 3's porters. The delivery meter still counts what was
  offered, because that is what feeds the offline grant.
- `hireCollect` → `dispatchLevel` everywhere, keeping the `MarketFlow.Collect` MIN index.
- Switched: `ChapterService.YardStaffed` (the beat gate), `VoyageService` (x2, via new `Product()`),
  `SettingsUI`, `CoalOperation:3370`, `MarketSceneBoot`, `StockPad`, and 5 test files.

`ProductRow` is deliberately single-product — marked `ponytail:` in the source. The save SHAPE is already
multi-product, which is the half that is expensive to change later; fair-share allocation of one service
budget across several stocked products is package C's work, not speculation now.

### Verification

**941 EditMode tests, 937 passed, 4 failed** (job `d1de238a7f8c497a966987d52cc2152a`). 0 console errors.
The 4 are the same pre-existing `RenderingSafetyTests` prefab failures as before step 1 — unchanged
count, unchanged names. Three new tests cover the conversion path:
`LegacyYardConvertsOnFirstTouchAndKeepsEveryInvestment`, `ConvertedYardIsNotReCreditedAfterItSellsOut`,
`EveryIslandOnTheLadderHasItsOwnProduct`.

Worth knowing: the first two FAILED when written, and were right to. They exposed that building the
service before populating `SaveData` lets `Register` create an empty row that permanently shadows the
legacy one. Real boot order is save-then-service so the product code is correct, but any future caller
that constructs `MarketService` before the save is loaded will silently lose a player's yard.

## Original step 2 plan — product-aware delivery

`Deliver` becomes `double Deliver(string islandKey, string productId, double offered)` returning accepted
units, with all six scalar callers plus the `SaveData` fields and `idleShopSchemaVersion` marker switched
in one transaction, per your handoff's ordering.

**One question for you before I cut it.** Your handoff says stage 1-1 uses "the current first product" and
warns *"do not guess a migration ID from an island's display name."* Coal's legacy scalar stock therefore
needs an explicit product ID, and `Convert` requires it as a parameter. Candidate assets you listed
include `Coke` for the coal island. Confirm the exact `productId` string for coal's legacy stock — and
whether it should be the `ResourceDef` asset's stable binding ID from `StageDefinition.resources[].id`
rather than an asset name. I will not guess this; it is the one value that silently corrupts every
existing coal save if it is wrong.


## Step 3a — COMPLETE: porters wear the haul legs

**Changed: `CoalOperation.cs` only.** No scene, prefab or asset edited — the swap is done at runtime from
code, so nothing had to be hand-edited and the change is one Inspector tick away from being reverted.

### What the scene actually contains (inspected live, not assumed)

- One `CoalOperation`, `CoalController`, `islandKey=coal`. Art root is `Island_Shipyard`.
- `workerPrefabs` is already wired with an **18-strong people pack** (`normal man a`, `stout woman b`,
  `strong man a`, …), plus `workerPrefab = SM_Character_Miner` as the legacy fallback. `workerScale` 2.2.
- **`visibleVehiclesPerRoute` is 1** — one body per route is drawn, not a fleet.
- Four live haul vehicles: `Island_Shipyard/Vehicles/truck_road_{ore1,ore2,cargo1,cargo2}`.
- Separately, a lot of **inactive** `Island_Shipyard/Art/0N_Vehicles/Truck_chassis_*` — static lorries
  baked into the phase art. These are the plan's "vehicles embedded in district art" and are NOT touched
  by this step; they are scene art and belong to the removal pass.
- There is also `Art/09_Customers/Customer_Island_02` already in the art — worth knowing before anyone
  builds customer visuals from scratch.

### The change

`WearPorter(Transform body)` deactivates the lorry's children and parents a person from the existing
people pack under the same transform. **The transform stays**, which is the whole reason this is a dozen
lines: every route, lay-by, follow-gap and queue rule is written against that object's position, and none
of it cares that the thing at that position now walks. `PaintFleet` and the wheel roller are skipped when
a porter is worn; if nothing is wired the lorry stays rather than leaving an empty road.

Facing is the one subtle part. `body.rotation` is `VehicleFacing(dir)` = `LookRotation(dir)` times the
authored lorry rig's constant pose (`_vehicleBaseRot` = Euler(-90,0,0), `_vehicleNoseYaw` = 90). A person
is modelled forward +Z, up +Y, so the porter's *local* rotation undoes that pose exactly — otherwise every
porter walks lying on his side, which is the failure this rig has caused before.

Walking vs standing comes straight off the existing state machine via the shared `PersonAnimator`
(`ToLoad`/`ToDrop`/`ToIdle` walk, everything else idles). `PersonAnimator` already falls back to a bob for
prefabs with no Animator, so nothing T-poses.

`[SerializeField] private bool portersInsteadOfTrucks = true` — untick it to get the lorries back, which
is the only honest way to compare the two while the routes are still the old tarmac.

### Deliberately NOT done in 3a

Routes are still the authored **roads** and speed is still the lorry's, so a porter currently walks a haul
road at haulage pace. Re-routing onto `AuthoredFootpath()` (which already exists and is already used by
`SiteLife`) and pacing him like a human **changes throughput**, because those same speed values feed the
economy through `IdleTransportRules`. That is a balance pass with numbers attached, not something to
smuggle in behind a mesh swap. It is step 3b.

### Verification

941 EditMode tests, 937 passed, 0 console errors (job `1ed9e0ebc75a40a39ad963751a01e103`). Same 4
pre-existing `RenderingSafetyTests` prefab failures, unchanged.

### 3a was WRONG on first delivery — what actually happened

Shipped with 0 errors and a green suite, and was still broken on screen: **no people, just floating ore**.
Three separate traps, none of which any test could catch. Recorded here because they will recur.

1. **Scale.** This island's lorry art is scaled 85x — a lorry is ~104 units tall. A porter left at
   `workerScale` 2.2 stood ~4 units and was invisible. Fixed by MEASURING the lorry being replaced and
   taking a fraction of its height (`porterHeightOfLorry`, 0.62), which is self-correcting on all eight
   islands instead of a per-island number to keep in step.
2. **Ordering.** `BodyBox`/`MakeLoad` size the cargo off the LORRY's own mesh, and the costume swap ran
   first — so the block was sized off a disabled renderer and left floating over an empty road. The swap
   now runs last, after `MakeLoad`.
3. **Placement.** Two attempts to compute the cargo's offset in the lorry's frame both failed (once 75
   units above his head, once through his waist), because the porter is skinned and animated so its
   bounds move every frame, and because a body's height read off the *instance* is a skewed projection of
   the lorry's build-time rotation — a 1.98-tall man measured 0.44. Fixed by parenting the load TO the
   porter and offsetting by a fraction of his height measured off the PREFAB.

A fourth trap sat on top: `porterLoadLift` had already been serialised into the scene at its first
default of 0.18, so changing the default in code did nothing. Renamed to `porterLoadRideHeight` — a new
name is a new field and takes the new default without a scene edit.

### Verified in Play mode, by measurement

Driven into Play through the MCP bridge and read back:

- 2 porters (one per route, matching `visibleVehiclesPerRoute` 1), animators present, lorries hidden.
- `upright(dot) = 1.00` on both — the rig rotation is correct, nobody is walking on his side.
- Auto-scale 32.5x / 38.6x giving heights 64.2 / 77.1 units = exactly 0.62 x lorry height.
- Cargo rides at 67-72% of body height, horizontal offset 0.0-0.6 — carried on the shoulder, centred.
- Full suite after: **941 tests, 937 passed** (job `c690a38a1ee3433f84440e7545c438a7`), same 4
  pre-existing prefab failures. Editor returned to edit mode.

Lesson worth keeping: a green suite and 0 console errors proved nothing here. Everything that was wrong
was only visible by driving the scene and reading numbers back off it.


## Sidewalk fix + Step 3b — COMPLETE

### The porters were walking ~52 units under the road

A fifth art trap, and not the one it looked like. The lorry's *art* hangs ~62 units above its own
transform pivot, and at build time the body sits in whatever pose the scene author left, so its world
AABB minimum is **not** the road surface. Dropping the porter by that box's half-height (51.8) buried him
under the tarmac. The driving line IS `body.position` — that is the line the loop points put the lorry on
— so a walker's feet belong exactly there, lifted only by the prefab's own pivot-to-sole gap (0.12,
scaled). Measured after: feet sit **-0.06** and **-1.22** units from the driving line, against ~-52 before.

Sidewalk: `porterKerbOffset` (signed, 0.62 of lorry height) moves him off the traffic line — measured at
64-77 units to the side. It goes through `Inverse(k)` like the rotation, so it stays perpendicular to the
road through every bend with no per-frame work. Note the island's own road knobs are useless here:
`roadWidth` is 9 and `laneOffset` 2.4, tuned for the GENERATED island, on a map where one lorry is 183
units long. Hence expressing the kerb as a share of lorry height instead.

### 3b — human pacing, with throughput held constant

`porterPace` (0.45) multiplies the speed of the two legs people actually work, and **divides the load of
those same legs by the identical number**. A leg delivers `speed x load` per second, so a change of pace
is not a change of trade. This is what `IdleTransportRules`' "preserve purchased effects" contract asks
for; it is not a promise about pace.

Measured on the live component through the MCP bridge, reading the derived properties at both settings:

| Leg | pace 1.00 | pace 0.45 | throughput |
| --- | --- | --- | --- |
| Ore | speed 72.705 x load 24.570 | speed 32.717 x load 54.600 | **1786.3620 both** |
| Cargo | speed 72.705 x load 16.380 | speed 32.717 x load 36.400 | **1190.9080 both** |

Identical to four decimal places. Set `porterPace` to 1 to walk at haulage speed and compare like for like.

**The train is deliberately untouched** (`EffTrainSpeed` 71.978, `EffTrainOre` 240.125). It is still a
train and its leg is the mine's, not a porter's.

### Scope note for whoever does the vehicle removal

`fleetCap` is clamped by `visibleVehiclesPerRoute`, which is **1** in this scene, so exactly one body
exists per route and the economic team count from `IdleTransportRules` never spawns a second. Worth
knowing before anyone assumes buying trucks adds bodies to the live island — it does not.

Suite after 3b: **941 tests, 937 passed** (job `3ff8d4c76c8a42529feb483e7c44106a`), same 4 pre-existing
prefab failures. Editor returned to edit mode.
