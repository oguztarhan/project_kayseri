# Updated Portrait Shipyard Worklist — Claude + Codex Handoff

**Updated:** 2026-09-05  
**Project:** Island Mining Tycoon / Project Kayseri  
**Target:** Unity 6.4.9f1, URP, Android, portrait  
**Purpose:** This is the current handoff and execution order for finishing the portrait shipyard. It reconciles the original five ship-equipment workshop plan with the later decision to expose only five major upgrade stations.

Before changing the project, read `CLAUDE.md`. In particular: do not hand-edit Unity scenes, prefabs, assets, or meta files; do not add packages; compile and test after C# changes; and keep Claude's Blender work separate from Codex's Unity/gameplay work.

---

## 1. Product decisions now treated as locked

### Five major upgrade stations

Only these five stations appear as major upgrade tabs and map labels:

1. **Mine**
2. **Deposit**
3. **Refinery**
4. **Market**
5. **Port**

The current presentation maps them onto the existing save-safe economy indices:

| Player-facing station | Existing save index | Current constant |
|---|---:|---|
| Mine | 0 | `IslandEconomy.Mine` |
| Deposit | 2 | `IslandEconomy.Storage` |
| Refinery | 4 | `IslandEconomy.Smelter` |
| Market | 6 | `IslandEconomy.Market` |
| Port | 7 | `IslandEconomy.Power` |

Do not reorder or delete the original eight economy save slots. Train and truck upgrades may continue to exist internally for old saves and simulation math, but they must not reappear as separate player-facing upgrade tabs.

### Five ship-item machine families

The Idle Weapon Shop-style sequence still exists, but these are **recipe-machine families inside the production world**, not five additional major upgrade tabs:

1. **Cannon**
2. **Hull / Plating**
3. **Rigging**
4. **Navigation / Spyglass**
5. **Figurehead / Charm**

Only Cannon and its first recipe are visible and usable on a fresh save. The other machines appear one at a time as quiet construction pads, then built machines. Future recipe cards and recipe models remain absent until unlocked.

The intended equipment-slot mapping is append-only:

| Machine family | Combat slot | Save rule |
|---|---|---|
| Cannon | current Cannon slot `0` | unchanged |
| Hull | current Plating slot `1` | unchanged |
| Navigation | current Spyglass slot `2` | unchanged |
| Figurehead | current Charm slot `3` | unchanged |
| Rigging | new Rigging slot `4` | append; never insert or reorder |

The map should therefore read:

`Mine → Deposit → Refinery/workshop machines → Market/customer orders → Port/equip/Set Sail`

The exact map may contain several factory buildings, but only the five infrastructure stations are upgrade hubs. Recipe machines are contextual production points.

### Scene and navigation decisions

- Keep `Assets/Scenes/Main.unity` as the production gameplay scene because the approved industrial map is already integrated there.
- Keep `Assets/Scenes/Shipyard.unity` as an isolated art/anchor preview and test scene.
- Keep the legacy scenes and save fields. Do not delete them.
- Add one reversible `UsePortraitShipyard` feature switch before release.
- With the switch enabled, Main uses `Island_Shipyard`, normal ship-item customers remain in Main, and Sea Combat opens through **Set Sail**.
- With the switch disabled, the legacy presentation remains available for rollback and comparison.
- Camera movement is vertical only. A narrow strip of water stays visible on the left and right. Do not allow a far zoom that turns the island into a small diagram.

---

## 2. Current verified state

### Blender and map import

- Blender source exists at `Tools/blender/IndustrialReference/ada_haritasi.blend`.
- The reference map has been imported into Unity as modular prefabs.
- Main prefab: `Assets/Prefabs/Island/IndustrialReference/IndustrialReference_Map.prefab`.
- Visual/reference scene: `Assets/Scenes/KayseriIsland_IndustrialReference.unity`.
- The import report records 277 prefabs, 1,866 editable parts, 1,203 Unity meshes, and no coordinate difference in its mesh comparison.
- Claude's manifest currently supplies 45 anchors, 17 routes, and five recipe-machine pads.
- `Station_Figurehead` has a valid pad and anchors but still has no finished building art.
- Collision proxies and LODs are not finished.

### Unity presentation

- The industrial map is integrated into `Assets/Scenes/Main.unity` as `Island_Shipyard`.
- Project and Bootstrap force portrait orientation.
- The Main camera is framed close to the island width with a small amount of side water.
- Horizontal drag is locked; vertical scrolling remains.
- Fog is disabled.
- The five map labels are visible and separated: Mine, Deposit, Refinery, Market, Port.
- The Upgrade screen contains exactly the same five entries.
- Port uses `Export Value` and `Loading Speed` as its two upgrade axes.
- Sail Combat is available again through the fixed ship button and the contextual Set Sail action.
- Decorative vehicle duplicates are disabled. The live simulation is visually capped to one representative vehicle per route, and the train has one visible wagon.
- Normal Play starts through `Assets/Scenes/Bootstrap.unity`.

### Save and progression foundations

- `SaveData.shipyard` exists as an additive payload.
- `ShipyardProgression` currently saves a basic unlocked-ID list and completed-order count.
- The old prototype IDs are still `Station_Cannon`, `Station_Hull`, `Station_Rigging`, `Station_Navigation`, and `Station_Figurehead`.
- The isolated Shipyard preview can display Cannon built and later machines as pads.
- This is only an unlock/display skeleton. It does not yet save production queues, materials, recipes, workers, output racks, or customer orders.

### Tests most recently verified

- Focused Edit Mode regression group: **72/72 passed** across Shipyard foundation, Sea Combat, and save migration coverage.
- A previous integration pass recorded **103/103** focused tests across Shipyard, Sea Combat, Crafting, Save Migration, and Save Service.
- Unity Remote still reports the pre-existing Android `adb forward` environment error. Physical-device touch testing has not been completed.

### Important inconsistency to remove

The project currently contains two definitions of “station”:

- `IslandEconomy.PlayerStations`: Mine, Deposit, Refinery, Market, Port.
- `ShipyardProgression.StationIds`: Cannon, Hull, Rigging, Navigation, Figurehead.

Do not continue building both as equal upgrade systems. From this document onward:

- call the first group **major stations** or **hubs**;
- call the second group **ship-item machines** or **machine families**;
- only major stations appear in the global Upgrade tab;
- ship-item machines own recipes, queues, construction pads, and completed-item racks.

---

## 3. Frozen shared contracts

Claude and Codex must agree on these names before either changes anchors or runtime bindings.

### Major-station IDs

Use stable logical IDs for new data and analytics:

```text
Hub_Mine
Hub_Deposit
Hub_Refinery
Hub_Market
Hub_Port
```

These logical IDs do not replace the existing economy indices. They are a readable, append-only layer above them.

### Ship-item machine IDs and unlock order

Keep the existing anchor-compatible IDs:

```text
Station_Cannon
Station_Hull
Station_Rigging
Station_Navigation
Station_Figurehead
```

These IDs are append-only and must not be renamed after production saves begin.

### Required map anchors

Keep the existing anchor contract unless both owners approve a versioned manifest change:

```text
Mine_Output
Train_Load
Train_Unload
Storage_Input
Storage_Output
Refinery_Input
Refinery_Output

Station_Cannon_Input
Station_Cannon_Work
Station_Cannon_Output
Station_Cannon_Upgrade
Station_Cannon_Worker

Station_Hull_Input / Work / Output / Upgrade / Worker
Station_Rigging_Input / Work / Output / Upgrade / Worker
Station_Navigation_Input / Work / Output / Upgrade / Worker
Station_Figurehead_Input / Work / Output / Upgrade / Worker

Customer_Berth_01..03
Player_Outfitting
Set_Sail
Camera_Stop_01..07
Camera_Bounds
```

### Route contract

- Delivery routes describe visible spatial movement only. They do not force Cannon to be an ingredient of Hull or any other recipe-family dependency.
- Each machine has one worker loop.
- The existing rail route remains Mine to Deposit.
- Sea routes run from Set Sail to the customer berths.
- One representative vehicle per logistics route is the visual target.
- Route points must already be on valid ground or water at Unity scale. Runtime code should not guess paths from arbitrary Blender object names.

### Portrait reference contract

Claude supplies consistent comparison renders at **720 × 1560**:

1. fresh save: Cannon machine only;
2. mid progression: Cannon, Hull, and Rigging;
3. fully built: all five machines;
4. gameplay frame with the narrow side-water margin.

---

## 4. Claude / Blender worklist

Claude owns Blender world art and exported map data. Claude must not edit Unity gameplay services, save code, UI scripts, Bootstrap, or Unity scenes.

### C0 — confirm the current map contract

- [ ] Re-run `Tools/blender/IndustrialReference/build_anchors.py`.
- [ ] Confirm all 45 required anchors still pass ground and uniqueness checks.
- [ ] Re-run the route validator and record any deliberate route exceptions.
- [ ] Verify the Blender root remains scale `1,1,1` and uses the documented Unity-space conversion.
- [ ] Produce a manifest diff. If no anchor changed, say explicitly that Codex can keep the current Unity manifest.

**Exit:** Codex can bind the map without guessing or rescaling.

### C1 — machine-state art

For Cannon, Hull, Rigging, Navigation, and Figurehead, supply separate, toggleable collections for:

- [ ] locked pad;
- [ ] construction state;
- [ ] built level 1;
- [ ] upgraded level 2;
- [ ] upgraded level 3;
- [ ] input-bin prop;
- [ ] active-work prop/socket;
- [ ] completed-item rack/socket;
- [ ] worker idle/work socket;
- [ ] upgrade VFX socket.

Do not reveal the finished machine or future recipe models while locked. Locked areas should be quiet and readable, not bright calls to action.

**Exit:** Unity can switch construction and upgrade states without modifying mesh hierarchies at runtime.

### C2 — Figurehead Atelier

- [ ] Create the missing Figurehead building for the existing 1.2 × 0.7 m waterfront pad.
- [ ] Keep `Station_Figurehead_*` anchors fixed unless the current footprint is proven unusable.
- [ ] Ensure the building does not obstruct Set Sail, the port ship, or Navigation-to-Figurehead delivery.
- [ ] Add matching locked, construction, built, and upgraded states.

**Exit:** `needs_art` becomes false and a purchase can never consume currency for an invisible building.

### C3 — production props and readable outputs

- [ ] Create simple silhouettes for Cannon, Hull, Rigging, Navigation, and Figurehead products.
- [ ] Create low-cost input material stacks and output racks that visibly change empty/full state.
- [ ] Keep each product readable at the current portrait camera distance.
- [ ] Avoid tiny props that require zooming in to understand the production state.
- [ ] Keep decoration lower contrast and lower motion than active production.

**Exit:** a player can identify what a machine is producing without opening a menu.

### C4 — collision and route clearance

- [ ] Add simple collision proxies for island cliffs, station interaction areas, docks, and any path blockers that matter.
- [ ] Inspect every delivery and worker route in Blender.
- [ ] Verify the train, one truck per route, workers, and customer/port ships do not pass through buildings, trees, cliffs, or each other.
- [ ] Fix the route or art placement at the source; do not rely on large runtime avoidance radii to hide bad paths.
- [ ] Record minimum clearances and vehicle footprint assumptions in `ANCHORS.md`.

**Exit:** all routes can be played in Unity without visible clipping.

### C5 — mobile art optimisation

- [ ] Add LODs where they materially reduce cost at the portrait camera distance.
- [ ] Combine or instance repeated scenery where safe.
- [ ] Keep URP-compatible shared materials and atlases.
- [ ] Remove unseen faces only when it does not break construction-state swaps.
- [ ] Provide collider-only and visual-only hierarchy guidance.
- [ ] Deliver an updated import report with mesh, triangle, material, and texture counts.

**Exit:** Codex can profile a representative Android build without first restructuring the art.

### Claude delivery checklist

- [ ] Updated `.blend` file.
- [ ] Updated `anchors.json` and generator scripts.
- [ ] Updated `ANCHORS.md` with any approved contract changes.
- [ ] Reference renders at the frozen resolution.
- [ ] Export/import report.
- [ ] Short list of changed collections and files.
- [ ] No Unity scene, prefab, save, or gameplay-code edits.

---

## 5. Codex / Unity and gameplay worklist

Codex owns Unity integration, gameplay, saving, UI, tests, and release validation. Codex must not remodel Claude's Blender collections.

### G0 — reconcile the two station systems

- [ ] Keep `IslandEconomy.PlayerStations` as the exact five global Upgrade entries.
- [ ] Change documentation and code terminology so `ShipyardProgression` represents ship-item machines, not global stations.
- [ ] Preserve existing `unlockedStations` data written by the prototype, or perform an explicit append-only normalization into a new machine-state collection.
- [ ] Add constants for `Hub_Mine`, `Hub_Deposit`, `Hub_Refinery`, `Hub_Market`, and `Hub_Port` where readable IDs are needed.
- [ ] Add automated tests proving the global Upgrade tab stays `[0,2,4,6,7]`.
- [ ] Mark the old plan and `Docs/SHIPYARD_INTEGRATION_STATUS.md` as historical/outdated after this document is accepted.

**Exit:** there is one unambiguous major-station list and one unambiguous machine-family list.

### G1 — data-driven recipe definitions

Create designer-editable definitions for each recipe. Each recipe needs:

- [ ] stable recipe ID;
- [ ] machine-family ID;
- [ ] display name and localization key;
- [ ] ingredient IDs and quantities;
- [ ] production duration;
- [ ] output equipment slot;
- [ ] base stat/value table;
- [ ] rarity rules;
- [ ] material/island unlock conditions;
- [ ] order/reputation unlock conditions;
- [ ] model/icon references;
- [ ] input, work, output, and VFX socket names.

Only the first Cannon recipe is available on a fresh save. Future recipes must not be instantiated into the main-screen UI before their unlock conditions are met.

**Exit:** designers can add or tune recipes without changing production code.

### G2 — persistent machine and inventory state

Add append-only save data for:

- [ ] material inventory by stable resource ID;
- [ ] one state record per unlocked machine family;
- [ ] active recipe ID;
- [ ] queue start and finish timestamps;
- [ ] worker/queue capacity if used;
- [ ] finished output awaiting collection;
- [ ] completed order and reputation counters;
- [ ] discovered recipe IDs;
- [ ] construction/unlock state.

Rules:

- [ ] Normalize missing/null/short collections without wiping old saves.
- [ ] Never reorder the four existing Sea Combat slots.
- [ ] Append Rigging as slot `4` only after migration tests are written.
- [ ] Save ingredient consumption and the resulting queued job atomically.
- [ ] Save the completed item before showing the decision screen.
- [ ] A restart at any point must not duplicate ingredients, items, cash, or salvage.

**Exit:** a queued or completed item survives app termination exactly once.

### G3 — Cannon vertical slice

Implement only Cannon before cloning the system to the other four machine families.

- [ ] Bind the existing production manifest into Main, not only the isolated Shipyard preview.
- [ ] Start a fresh save with Cannon built and one Cannon recipe visible.
- [ ] Keep Hull as one quiet construction pad at the lower discovery edge.
- [ ] Feed real Mine/Deposit/Refinery materials into the Cannon input bin.
- [ ] Show one worker or delivery action that corresponds to real inventory transfer.
- [ ] Run a timed Cannon queue.
- [ ] Show the finished cannon on its output rack.
- [ ] Allow exactly four outcomes: Sell, Equip, Store, Salvage.
- [ ] Connect Equip and Store to the existing Sea Combat and gear-stash services.
- [ ] Connect Sell to the correct wallet transaction.
- [ ] Connect Salvage to the existing salvage economy.
- [ ] Complete one real customer order at the Market/port end of the map.
- [ ] Save/load and offline-complete the queue.
- [ ] Add a short tutorial: start recipe, collect item, choose outcome.

**Exit:** a new player can complete one Cannon order entirely from the portrait world without opening a generic crafting screen.

Do not implement Hull, Rigging, Navigation, or Figurehead gameplay before this exit is met and reviewed for clarity/fun.

### G4 — machine unlocking and focus ladder

- [ ] Unlock in this fixed order: Cannon → Hull → Rigging → Navigation → Figurehead.
- [ ] Require completed orders/reputation plus a construction price, not cash alone.
- [ ] Check art readiness before spending currency.
- [ ] Play construction, then camera-glide to the new machine.
- [ ] Teach only one new action after each unlock.
- [ ] Keep earlier machines operating while off-screen.
- [ ] Show at most one urgent world marker at a time.
- [ ] Return from background to the highest-priority stalled machine unless the player recently selected another view.
- [ ] Prove with tests that every unlock is reachable from a fresh save and cannot be skipped.

**Exit:** the complete machine ladder unlocks deterministically and persists.

### G5 — remaining machine families

After Cannon approval, add one family at a time:

- [ ] Hull / Plating.
- [ ] Rigging, including appended Sea Combat slot `4` and save-array normalization.
- [ ] Navigation / Spyglass.
- [ ] Figurehead / Charm after Claude marks its art ready.

For each machine:

- [ ] at least one starter recipe;
- [ ] hidden future recipes;
- [ ] input/output props;
- [ ] queue and offline completion;
- [ ] sell/equip/store/salvage choices;
- [ ] customer-order demand;
- [ ] unit and integration tests;
- [ ] world readability review at default zoom.

**Exit:** all five families work through the same tested production contract.

### G6 — customer demand and recipe tiers

- [ ] Create equipment orders instead of paying only for raw-market throughput.
- [ ] Keep older machine families relevant after later ones unlock.
- [ ] Map Coal, Copper, Iron, Silver, Gold, Ruby, Emerald, and Diamond to meaningful recipe tiers/ingredients.
- [ ] Never request a hidden recipe or an unavailable resource.
- [ ] Decide how many orders can wait without filling the screen.
- [ ] Make customer ships/berths communicate waiting, ready, and fulfilled states.
- [ ] Keep Sea Combat rewards and tycoon production economically separate except through intentionally defined materials/currencies.

**Exit:** all unlocked machines have useful demand and no progression dead end exists.

### G7 — feature switch and legacy-map treatment

- [ ] Add a reversible `UsePortraitShipyard` setting in a single authoritative configuration location.
- [ ] Enable `Island_Shipyard` and the portrait production services when true.
- [ ] Keep legacy scene roots and old progression data untouched but inactive when true.
- [ ] Restore the old presentation when false.
- [ ] Prevent normal equipment orders from opening the separate Market scene while the portrait feature is active.
- [ ] Keep legacy Market available when the switch is off.
- [ ] Keep Sea Combat reachable through Set Sail in both modes.
- [ ] Add fresh-save and legacy-save tests for switching on, off, and on again.

**Exit:** rollback is one setting change and does not lose player data.

### G8 — portrait UI and attention polish

- [ ] Keep the five-entry global Upgrade tab.
- [ ] Build a contextual machine panel that shows only unlocked recipes for the selected machine.
- [ ] Do not attach a permanent recipe carousel to every machine.
- [ ] Keep cash, premium currency, and income readable in the top safe area.
- [ ] Review whether Map, Boost, Contract, Upgrade, Store, Daily, Events, and Offers should remain permanent. Move secondary entries behind a compact More menu if the screen becomes crowded.
- [ ] Keep the Sail Combat button easy to find.
- [ ] Ensure station labels, market interaction, Set Sail, upgrade markers, and customer orders do not overlap during vertical scrolling.
- [ ] Test at least one tall phone, one shorter 16:9 phone, one notched phone, and one tablet aspect.
- [ ] Localize every new machine, recipe, order, and decision string.

**Exit:** no important world action is hidden by the HUD or device cutout.

### G9 — animation, pooling, and audio/VFX hooks

- [ ] Bind workers and item transfers to Claude's stable sockets/routes.
- [ ] Pool repeated items, delivery props, construction effects, sale effects, and customer ships.
- [ ] Avoid allocations and component searches in per-frame paths.
- [ ] Use visual bottleneck states: empty input, active work, full output, waiting customer, stopped vehicle.
- [ ] Add hooks for construction, machine cycle, item completion, equip, sale, and Set Sail sounds.
- [ ] Keep completed but non-selected machines animated at lower emphasis.

**Exit:** every visible transfer represents real simulation state and stays mobile-safe.

### G10 — verification and release gate

- [ ] Run the complete Unity Edit Mode and Play Mode test suites, not only focused groups.
- [ ] Test fresh, early, mid-game, maxed, and pre-feature saves.
- [ ] Test app termination during ingredient spending, active production, completion, and reward choice.
- [ ] Test offline durations below and above the cap.
- [ ] Test enabling/disabling the feature switch repeatedly.
- [ ] Smoke-test portrait input on a physical Android device.
- [ ] Profile CPU, GPU, draw calls, overdraw, memory, loading time, and GC on a representative mid-range Android device.
- [ ] Agree performance budgets before declaring the art final.
- [ ] Add analytics for scroll discovery, machine unlocks, recipe starts/completions, queue stalls, item decisions, orders, and Set Sail conversion.
- [ ] Tune time-to-first-Cannon, time-to-each-machine, queue capacity, order pacing, and offline limits from play data.

**Exit:** the portrait shipyard can replace the legacy presentation without save loss, progression traps, input problems, or unacceptable device performance.

---

## 6. Recommended execution order

Do not attempt all remaining work at once.

### Phase A — contract cleanup

1. Accept this document as the current source of truth.
2. Freeze major-station IDs, machine IDs, slot mapping, anchor names, and scene ownership.
3. Codex completes G0.
4. Claude completes C0 in parallel, using a separate branch/worktree.

### Phase B — one playable Cannon loop

1. Codex implements G1 and G2 for Cannon only.
2. Claude supplies Cannon state variants and production props from C1/C3.
3. Codex integrates them into Main and completes G3.
4. Run a fresh-save playtest before expanding scope.

### Phase C — progressive machines

1. Codex completes G4.
2. Add Hull, then Rigging, then Navigation.
3. Claude finishes Figurehead art while the first four are integrated.
4. Add Figurehead only after its art-readiness gate passes.

### Phase D — full economy and UI

1. Complete G6 customer demand and material tiers.
2. Complete G7 feature switch and reversible legacy presentation.
3. Complete G8/G9 portrait polish, animation, pooling, audio, and VFX bindings.

### Phase E — optimisation and release

1. Claude completes C4/C5.
2. Codex completes G10.
3. Balance from real play data and physical-device measurements.

---

## 7. Branch and file ownership

Use separate branches/worktrees. Never have both assistants edit the same Unity serialized asset or scene concurrently.

### Claude branch

Suggested branch: `claude/portrait-shipyard-art-next`

Claude-owned paths:

```text
Tools/blender/IndustrialReference/
design/shipyard-refkit/
design/shipyard-blockout/
```

Claude may deliver exports to an agreed staging folder, but Codex performs the Unity import/integration.

### Codex branch

Suggested branch: `codex/portrait-shipyard-systems-next`

Codex-owned areas:

```text
Assets/Scripts/Data/
Assets/Scripts/Gameplay/
Assets/Scripts/Systems/
Assets/Scripts/UI/
Assets/Scripts/Tests/
Assets/Editor/IndustrialReference/
Assets/Editor/UI/
Assets/Scenes/Main.unity        (Unity tools only)
Assets/Scenes/Shipyard.unity    (Unity tools only)
Assets/Prefabs/                 (Unity tools only)
ProjectSettings/                (Unity tools/settings only)
```

### Merge rule

1. Claude commits Blender source, manifest, renders, and reports.
2. Codex reviews the manifest diff first.
3. Codex imports art through the existing Unity importer/builder.
4. Codex runs compile, scene audit, visual smoke test, and automated tests.
5. Only then merge the art and systems branches.

---

## 8. Required automated tests

At minimum, add coverage for:

- [ ] The global Upgrade catalog is exactly Mine, Deposit, Refinery, Market, Port.
- [ ] Transport slots never appear as global Upgrade entries.
- [ ] Fresh save unlocks only Cannon and its first recipe.
- [ ] Future recipes are absent from the visible UI.
- [ ] Machines cannot unlock out of order.
- [ ] A machine with missing art cannot spend currency.
- [ ] Ingredients are consumed once when a queue begins.
- [ ] Active queues survive encrypted save round trips.
- [ ] Offline completion produces exactly one pending item.
- [ ] Claiming twice cannot duplicate an item, cash, or salvage.
- [ ] Sell, Equip, Store, and Salvage each commit the correct state exactly once.
- [ ] Rigging is appended as slot `4`; slots `0..3` retain their old items and meanings.
- [ ] Old saves normalize without losing islands, currency, gear, upgrades, or voyages.
- [ ] Feature switch on/off/on preserves both progress models.
- [ ] Every machine unlock is reachable from a fresh save.
- [ ] Orders never require hidden recipes or unavailable resources.
- [ ] Camera horizontal movement remains locked at all supported aspect ratios.
- [ ] Every required anchor and route exists in the imported manifest.

---

## 9. Manual acceptance checklist

### Fresh save

- [ ] Player sees the portrait island with narrow side water.
- [ ] Only vertical movement is possible.
- [ ] Five major station names are readable.
- [ ] The global Upgrade screen contains only five major stations.
- [ ] Cannon is the only built ship-item machine.
- [ ] Only Cannon recipe 1 is visible.
- [ ] Hull appears only as a quiet construction pad.
- [ ] One real material flow reaches Cannon.
- [ ] One completed item can be sold, equipped, stored, or salvaged.
- [ ] One customer order can be completed.
- [ ] Sail Combat is reachable from the ship button and Set Sail.

### Progressed save

- [ ] Machines unlock in the correct order.
- [ ] Camera focuses a new construction once, then returns control.
- [ ] Earlier machines keep operating off-screen.
- [ ] Only one world action is urgent at a time.
- [ ] Recipe lists contain only discovered recipes.
- [ ] Input/output bottlenecks are visually understandable.
- [ ] Vehicles and workers do not clip or form traffic noise.

### Returning/offline player

- [ ] Queues advance only within the allowed offline rules.
- [ ] Full output racks stop production correctly.
- [ ] No reward is granted twice.
- [ ] The camera returns to a useful stalled/current station.

### Device and rollback

- [ ] No important control is behind a notch or tablet edge.
- [ ] Vertical drag and taps work on a physical Android device.
- [ ] Frame-time and memory stay within the agreed device budget.
- [ ] Turning `UsePortraitShipyard` off restores the legacy presentation.
- [ ] Turning it back on restores portrait progress unchanged.

---

## 10. Things neither assistant should do

- Do not delete legacy scenes, prefabs, save fields, or island progress.
- Do not reorder economy station indices or the four existing equipment slots.
- Do not make Train, Ore Trucks, or Cargo Trucks separate global Upgrade pages again.
- Do not show all recipes from the beginning.
- Do not create five more major Upgrade tabs for the ship-item machines.
- Do not make one equipment family consume the previous family's finished item unless design explicitly changes the recipe rules.
- Do not spend construction currency before checking milestone, order, and art readiness.
- Do not duplicate visible vehicles to express an economic level increase.
- Do not search arbitrary Blender object names every frame.
- Do not hand-edit `.unity`, `.prefab`, `.asset`, or `.meta` files.
- Do not let Claude and Codex edit Main, Shipyard, Bootstrap, save code, or imported prefabs concurrently.
- Do not report “done” from compilation alone; verify the live portrait flow and saved state.

---

## 11. Immediate next assignment

The next implementation task is **Cannon Vertical Slice**, not additional map polishing.

### Claude starts with

1. Validate the current anchor/route manifest.
2. Deliver Cannon locked/construction/built/upgraded groups plus input bin, output rack, and product silhouette.
3. Continue Figurehead art separately; it does not block Cannon.

### Codex starts with

1. Reconcile major-station versus machine terminology and save contracts.
2. Create data-driven recipe definitions and persistent machine state for Cannon only.
3. Bind `Station_Cannon_*` anchors into Main.
4. Implement one real Cannon queue and the Sell/Equip/Store/Salvage decision.
5. Verify save/load, offline completion, and one customer order.

### Review checkpoint

Do not begin Hull gameplay until a fresh-save player can complete the entire Cannon loop in Main and the team approves its readability, pacing, and screen attention.

---

## 12. Definition of complete

The updated plan is complete only when:

- the five major station upgrades remain Mine, Deposit, Refinery, Market, and Port;
- Cannon, Hull, Rigging, Navigation, and Figurehead unlock as hidden-to-visible recipe machines;
- every visible material/item transfer represents real saved simulation state;
- finished items can be sold, equipped, stored, or salvaged;
- customer equipment orders and Sea Combat are both reachable from the portrait world;
- all unlocked machines continue producing while off-screen and during allowed offline time;
- old saves retain their economy, currency, islands, voyages, and gear;
- the legacy presentation can be restored with one feature switch;
- the complete test suite and physical Android portrait smoke test pass;
- the map meets the agreed mobile performance budget.

Until then, the current build should be described as a **portrait map/UI foundation with a saved unlock skeleton**, not a finished Idle Weapon Shop-style production game.
