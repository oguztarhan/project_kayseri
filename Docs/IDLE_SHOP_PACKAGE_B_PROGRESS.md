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


## No land vehicles at all — train + road (COMPILES, NOT RUNTIME-VERIFIED)

User instruction: the train was the last vehicle left, and there are to be no vehicles in the game; the
haul road becomes something to walk on.

### Train -> porter

`TrainAgent` gains a `porter`, and `BuildTrain` wears the same costume on the ENGINE that the road legs
already wear, then blanks every wagon's children. The engine keeps the agent because every distance,
cover stretch and shed-door position along the line is measured against it — as with the lorries, only
the costume changes. Walk/idle comes off the rake's own state: `TR.Haul` and `TR.Return` walk,
`TR.LoadMountain` and `TR.Deposit` stand still. Toggle: `portersInsteadOfTrains`.

The rail PATH is deliberately kept. That is where the island says the mine route runs; only the thing
moving along it changed.

### Road -> footpath

The authored haul road is **three stacked slabs** under `Art/03_Parts/`, not one object:

| Layer | Size | Fate |
| --- | --- | --- |
| `Main_road_ochre_foundation` | 940 x 524 x 2125 | **kept** — reads as a worn earth path |
| `Main_road_bright_center` | 887 x 524 x 2104 | hidden |
| `Main_road_luminous_golden_ribbon` | 914 x 524 x 2115 | hidden — this was the "neon" |
| `Factory_circular_service_road` | 897 x 3 x 503 | hidden |

`StripRoadToFootpath` hides the top layers by name from a serialized `roadLayersToHide` list, leaving the
foundation. **No new geometry is built and no asset is edited** — the path is the road's own base course.
Toggle: `footpathInsteadOfRoad`.

### Status: VERIFIED in Play (see below)

Originally logged as unverified because the MCP bridge died. Re-verified once it came back.

Unity recompiled this change with **0 console errors and 0 `error CS`**. Then the MCP bridge died: the
Python server on 8080 still issues sessions, but the Editor-side plugin detached, so every `execute_code`,
`manage_editor` and `run_tests` call returns `success:false`. The Editor.log tail is full of exceptions
inside the MCP package's own socket/WebSocket code (`McpLog.cs`), and Unity itself is idle at 0.7% CPU
with no game exceptions.

So, explicitly: **the full test suite has not been re-run, and nobody has seen the train porters or the
stripped road on screen.** Reconnect via Window > MCP For Unity > Connection (toggle the HTTP server off
and on), and this needs re-verifying before it is called done.


## The presentation switch — why none of this was visible

The island work was invisible in Play for a reason that had nothing to do with the code.

`GameBootstrap.Start` loads a presentation scene with `LoadSceneMode.Single`, chosen by
`ShipyardFeatureSwitch` from a save flag. The live save read `UsePortraitShipyard = True`, so the game
booted `Shipyard.unity` and **never loaded `Main.unity`** — which is where `CoalController`, the island
art, the lorries, the rake and the roads all live. Every change in package B targets `Main`.

The user confirmed the island is the intended main view, so the flag was flipped through the supported
setter, `GameBootstrap.SetUsePortraitShipyard(false)`, which persists it via `SaveService`. Presentation
is now `Main`. **Anyone confused by "my island change did nothing" should check this flag first.**

### Step 2 migration, proven against the real save

Read out of the live save in Play, not a fixture:

```
idleMarketYards rows=1  schemaVersion=1  legacy marketYards=8
  row id=coal ver=1 carry=5 serve=5 dispatch=5 | Coke=0.00
```

Hires 5/5/5 preserved with `hireCollect` landing in `dispatchLevel`, product id exactly `Coke`, all 8
legacy rows untouched as migration input.

### Train and road, verified

- Road stripped: `Main_road_bright_center`, `Main_road_luminous_golden_ribbon` and
  `Factory_circular_service_road` hidden; `Main_road_ochre_foundation` visible as the path.
- Rake: `wagon` visible renderers **0**, engine's `model` hidden, `_Porter` present with `porterBody`
  wired. The engine's GameObject reads `activeSelf=False` between runs because `SetTrainVisible` hides
  the rake inside the mountain — expected, not a fault.
- All porters `upright = 1.000`, feet within 0.65 units of the driving line.

### Two bugs found and fixed during that verification

1. **The rake's walker leaned 21 degrees.** Train cars use the same `VehicleFacing` as lorries, but the
   rail has a GRADIENT and the cars' heading follows it in three dimensions, so `LookRotation` tilted the
   man with the track. Lorries never showed it because their heading is flattened. `TrainTick` now
   flattens his own world forward and looks again — same yaw, upright body. A walker on a hill is
   vertical; only his path is inclined.
2. **Everyone was half height.** The shared porter size was taken from the first vehicle costumed, and
   **the rake is built before the road fleet**, so the whole crew was sized against a locomotive.
   `PorterWorldHeight` now measures a `truck_road*` body specifically, whatever the build order.
   `_porterHeightWorld` went 36.6 -> 64.19, and both road porters measure exactly 64.2.

A measuring note that cost time: probing a porter's height with `GetComponentsInChildren` includes the
crate riding on his shoulder, which made two identical porters read 47 and 36. Exclude the carried load.

Final suite: **961 tests, 957 passed** (job `df692f08ff934282a3c9b7c528a939cb`), same 4 pre-existing
prefab failures. Editor returned to edit mode.


## Manual play removed — the yard now pays itself

The old market yard had a second, competing economy: the counter MINTED cash by hand, the note landed on
the floor, and it was worth nothing until somebody walked over it. `SetSimulatedYard` switched the
ledger's own selling OFF for whichever island the player was standing in, so the scene could be the
seller. That is the "third job" the redesign exists to delete.

| Was | Now |
| --- | --- |
| `MarketService.SellByHand` mints, does not bank | **deleted** |
| `MarketService.Collect` banks floor cash | **deleted** |
| `MarketSceneBoot` calls `SetSimulatedYard(_yardKey)` | passes `null` — the ledger never stops selling |
| `SellCounter.Serve` returns `SellByHand(...)` | returns 0; it hands over goods and never touches the wallet |
| `CustomerQueue` drops one note per bar | no notes; the serve still looks like a serve |
| `CashFloor.Bank` credits the wallet | clears a stale note only; credits nothing |
| `StockPad` loads bars onto the player's back | gone — the pile you look at, not the pile you work |
| `YardWorker` calls `TakeFromStock` | calls `Stock` — **observes**, never consumes |

That last row matters more than it looks. With the ledger selling on every tick, anything the scene took
off the pads would be removed from stock and then never sold — income lost silently to an animation. The
hires now carry a copy of a bar that the ledger is selling independently, which is codex's own rule:
customer visuals observe committed sales, they never mint.

`SetSimulatedYard` is kept but is only ever passed null. `SettleYard` still honours it, so a future live
yard view could take the selling back; the market scene no longer does.

### Deliberately NOT done: the joystick and the player body

The player character still exists, and this is a sequencing decision rather than an oversight.
**`UpgradePad` is the only place in the game a `YardUpgrade` can be bought** — deposit slots, queue slots,
all three hires, carry capacity — and it detects the player by his `CarryStack`. Deleting the body today
would strand the entire yard upgrade track, which is exactly what "hire to 5/5/5 and the yard runs itself"
depends on.

Making the pads tap-driven instead would mean reworking `MarketSceneBoot`, `MarketHudUI`,
`MarketYardScene`, `MarketCamera` and three pads — in a scene package C deletes wholesale. That is
throwaway work. The joystick dies with the scene, in one commit, once C's station panel can sell
`YardUpgrade` on the island.

So today: **no manual carrying, no manual serving, no floor cash, and no way to earn by hand.** The
remaining walk is a menu, not a job.

### Verification

**981 tests, 977 passed** (job `0350c5ef59c048cab024c978d4cc82fa`), 0 compile errors. Only the 4
pre-existing missing-prefab `RenderingSafetyTests` remain. New test
`SalesBankThemselvesAndThereIsNoHandSalePath` pins both halves: a staffed yard banks on the tick with
nobody in it, and `SellByHand`/`Collect` must not come back — asserted by reflection so the API cannot be
quietly restored.


## BUG FIXED: the crew-size upgrades were dead money

Found while answering Codex's C0/acceptance point about `visibleVehiclesPerRoute`. It was not a
theoretical risk — it was live, in the user's own save.

`BuildTruckAgents` clamps a route's agent list to `visibleVehiclesPerRoute`, which is **1**. The economic
team count is not clamped. Measured in Play before the fix:

```
ECONOMIC teams claimed: OreTruckCount=5  CargoTruckCount=5
actual agents:          route Ore: 1     route Market: 1
```

So the player had bought levels on BOTH truck crew-size tracks and received nothing for them: one body
worked each leg regardless. Every level on those two axes was dead money.

`TeamShare(teams)` now makes the drawn workers carry the whole crew's load — `teams / drawn`, which is
exactly 1 when the crew is already all on screen, so it cannot double-count if the visible cap is raised
later. Measured after, on the same save:

| ore crew-size levels | teams | throughput budget |
| --- | --- | --- |
| 3 (as purchased) | 5 | 7648.6 |
| 0 | 2 | 3059.4 |

2.5x, where before the two were identical. This satisfies Codex's requirement that paid team-count
upgrades keep a benefit even when only one body renders. Spawning the real crew instead is the honest
fix and a visibly busier island, but it is more agents, longer queues and a profiling job — Codex agrees
that is a separately scoped C change.

Suite: **987 tests, 983 passed** (job `bbdddab0c8374e2c9cf25c056e8edbbd`), 0 compile errors, same 4
pre-existing prefab failures.

## Accepted from Codex's review — still open in B

1. **The pacing claim was a BUDGET check, not measured throughput.** Codex is right. With handling time
   H, throughput is `load / (D/speed + H)`; scaling speed by p and load by 1/p gives
   `load / (D/speed + pH)`, which differs whenever H is nonzero — and this state machine has real
   loading/dropping dwell. The speed x load equality stands as a budget identity only. Timed
   delivery/sale sampling at both paces is still owed.
2. **Delivery conservation is unfinished.** `Deliver` returns accepted units but every caller still drops
   the remainder, exactly as the lorries did. Callers must keep/retry or return `offered - accepted`,
   without letting retries inflate the measured rate. Voyage cancellation returns need the same audit.
3. **"Inactive now" is not proof for the decorative vehicles.** Re-check after a phase change, expansion,
   reload and island switch.
4. **Ten minutes on a FRESH, unmaxed crew**, not only the maxed live save, plus real customer
   presentation on the island — an existing `Customer_Island_02` prop is not a working queue.


## Delivery conservation — DONE (Codex review item 2)

Goods now have exactly one owner at a time. Nothing is destroyed by being refused.

| Path | Was | Now |
| --- | --- | --- |
| Porter arriving at a full yard | overflow destroyed, `a.carry = 0` unconditionally | keeps `offered - accepted` and waits at the pad to offer again |
| `Deliver` meter | counted what was OFFERED | counts what was **accepted** |
| `ReturnToStock` | `void`; overflow silently dropped | returns accepted units |
| `TryAbandon` a voyage | cargo vanished if the pads were full | hold keeps the remainder; the berth stays until there is room |

The meter change is the subtle half and it is why the two must land together. A carrier that keeps its
remainder brings the same goods back next dwell, so metering the OFFER would count them again on every
attempt: a jammed yard would report its highest ever delivery rate at precisely the moment it is
delivering nothing — and `deliveredPerMin` is what the next launch's offline grant is paid from.

There is no strangulation risk from metering acceptance. If measured supply ever fell far enough that
capacity reached zero, `Deliver`'s `capacity <= 0` branch accepts everything uncapped, so the loop fails
open rather than closed. Sales also drain stock every tick, which is what lets a jam clear itself.

The porter that still holds cargo re-dwells at the drop instead of driving off to fetch more of what
there is already nowhere to put — the queue behind him is the visible bottleneck the plan asks for.

`MarketSceneBoot`'s inter-yard drop is now a dead path: nothing fills `CarryStack` since `StockPad`
stopped loading the player, so it cannot run. It dies with the scene.

### Verification

**1009 tests, 1005 passed** (job `c51712ca8d7c41018dc51145581834f7`), 0 compile errors, same 4
pre-existing prefab failures. Three new tests:

- `DeliverReportsWhatThePadsTookAndNeverSwallowsTheRest`
- `RefusedRetriesDoNotInflateTheDeliveryMeter` — twenty refused offers leave the meter untouched
- `AbandoningAVoyageIntoAFullYardKeepsTheCargoRatherThanDestroyingIt`

Operational note for anyone driving Unity over MCP: a test job that reports `status: failed` with
`0 / None` usually means **the editor is still in Play mode**, not that the suite broke. EditMode tests
cannot start while playing. Stop play and re-run before investigating anything else.


## Pacing measured properly — Codex review item 1

Codex was right, and the algebra does not merely weaken the old claim, it reverses its sign. With
`speed' = speed*p` and `load' = load/p`:

```
throughput = (load/p) / (D/(speed*p) + H)  =  load / (D/speed + p*H)
```

`p < 1` shrinks the denominator, so slowing porters and enlarging their loads **increases** throughput
whenever dwell H is nonzero — the fixed handling cost is paid on fewer trips.

Measured from live values in Play (loop lengths off the built agents, dwell off the economy):

| Leg | loop | pace 1.00 | pace 0.45 | change |
| --- | --- | --- | --- | --- |
| Ore (depot→refinery) | 3574.8 | cycle 58.47s, load 122.9 → **126.1/min** | cycle 128.65s, load 273.0 → **127.3/min** | **+0.95%** |
| Market (refinery→pads) | 3769.1 | cycle 61.31s, load 81.9 → **80.1/min** | cycle 134.96s, load 182.0 → **80.9/min** | **+1.0%** |

`dwellSeconds` 0.70, `StorageDwell` 0.35, `MarketDwell` 0.35 — so H is about 1.05s against a ~58s cycle,
1.8% of the trip. That is why the effect is real but small.

**The honest statement is therefore: pacing is not throughput-neutral, it is +1%.** The earlier
"identical to four decimals" was a budget identity and is retained only as that.

### Still owed: the same measurement against income

Whether that +1% reaches the wallet depends on the chain's bottleneck, and I could not sample it. The
market leg (80.9/min) is the tightest transport stage against a smelter doing 180/min, but every attempt
to read `deliveredPerMin` and `RatePerMin` in Play found the island running with **null services**
(`SaveData`, `MarketService`, `CoalOperation._marketService`, `_agents`, `_train1` all null), so no
delivery or sale was being recorded at all. See below — that needs settling before the soak test.

## Found and fixed: an unguarded tick that hid the real fault

`Tick` called `TrainTick(_train1, dt)` with no null check, while `_train2`..`_train4` were all guarded,
and iterated `_agents` unguarded. On an island whose build did not complete, that threw a
`NullReferenceException` **every frame** — drowning the console in a storm that hides whatever actually
went wrong upstream. Both are guarded now. Pre-existing: `switch (a.state)` would have thrown on the same
null before any of my changes.

## Open, not mine, needs an owner

- **The island came up with no services in every Play session I drove.** `GameBootstrap` survives into
  `DontDestroyOnLoad` with `Data=True` but `Wallet=False` and `Market=False`, and the locator returns
  null for all three, yet the log shows `GameBootstrap.Awake` reaching line 414 — past both service
  constructions. I could not reconcile that, and I did not edit `GameBootstrap`: it belongs to the
  masters workstream. It may be an artifact of repeated MCP play/stop cycles rather than a regression,
  but if it is real the island has no economy at all and nothing else matters.
- **`SeaFightUI` throws every frame** in `DriveSheet:1170` / `RefreshSheet:1226`.
- **Three new failures in `RosterCardPrefabTests`** (`StateBadgesShipHiddenAndWritable`,
  `TheCaptainCardCarriesEveryPieceTheScreenBinds`, `TheMasterCardCarriesEveryPieceTheScreenBinds`) —
  masters workstream, appeared during this session.

Suite: **1010 tests, 1003 passed** (job `8ee1ea5bdfad41d78d8f5bf9fa7795b7`) — the 4 pre-existing prefab
failures plus those 3 roster ones.
