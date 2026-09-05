# Portrait Shipyard — Focus Ladder Production Plan

> **Historical / superseded.** The current source of truth is [UPDATED_PORTRAIT_SHIPYARD_WORKLIST.md](../UPDATED_PORTRAIT_SHIPYARD_WORKLIST.md). Keep this document for historical context only.

**Status:** proposal for approval  
**Goal:** turn the current island game into one vertically scrollable portrait shipyard where the player can watch the complete production flow and unlock ship-equipment stations one at a time.

This plan keeps the existing mine, train, storage, refinery, transport, market, islands, sea combat, gear rarity, offline progress, and save data. It changes how those systems are arranged and revealed. The existing maps are made inactive behind a feature flag; they are not deleted.

---

## 1. The target experience

The main screen is one continuous portrait island. The player drags only up and down.

At the beginning:

- only the first ship-equipment station is operational;
- only its first recipe is shown;
- the mine, train, storage, refinery, workers, customers, and that station visibly operate;
- future station areas appear only as quiet construction pads or silhouettes;
- future recipes remain hidden.

As the player progresses, each new station is physically constructed farther down the island. The camera smoothly moves to the newly unlocked area, teaches one action, and then gives control back to the player. Older stations continue working above it.

The player should understand the economy without opening a menu:

`Mine → Train → Storage → Refinery → Material bins → Equipment station → Finished rack → Customer ship / Player ship`

The map is a focus ladder, not a screen full of equal-priority buttons. At any moment, only one of these should demand attention:

1. the current objective;
2. the most important bottleneck;
3. the next affordable upgrade;
4. a newly unlocked station.

---

## 2. Five ship-equipment station families

Station unlock order is separate from material/recipe progression.

| Unlock | Station | Ship role | Existing system it uses |
|---|---|---|---|
| 1 | **Cannon Foundry** | attack | `SeaCombat` cannon slot |
| 2 | **Hull Forge** | health and defence | plating slot |
| 3 | **Rigging Loft** | speed and handling | new rigging slot appended to the current slot list |
| 4 | **Navigation Works** | aim, scouting, loot | spyglass slot |
| 5 | **Figurehead Atelier** | special bonuses | charm slot, presented as a ship relic/figurehead |

Do not reorder the four existing save-array slots. Append Rigging as the fifth slot and give stations stable string IDs independent of their array order.

### Recipes and the existing islands

Coal, copper, iron, silver, gold, ruby, emerald, and diamond remain valuable. They unlock recipe tiers and combinations across the five stations; they do not create eight copies of the map.

Examples:

- coal fuels workshops and advanced production cycles;
- copper makes fittings, mechanisms, and early navigation equipment;
- iron and steel make cannons and hull armour;
- silver and gold make precise or premium equipment;
- ruby, emerald, and diamond make rare late-game variants.

Each station begins with one recipe. Later recipes appear inside that station only after their own unlock condition is met. Locked recipes must not fill the main screen. A station may show one small “next discovery” hint, but never its complete future catalog.

The recipe list should be data-driven so names, ingredients, duration, rarity, model, value, and unlock conditions can be tuned without changing code.

---

## 3. New portrait island map

Create a new map rather than bending the eight existing scenes into the new shape.

**Working scene name:** `Shipyard.unity`  
**Working Blender source:** `Shipyard_Island.blend`

### Vertical composition

```text
TOP / source

Mountain mine + train entrance
          ↓
Storage yard + refinery
          ↓
Cannon Foundry                 ← first default camera focus
          ↓
Hull Forge construction pad
          ↓
Rigging Loft construction pad
          ↓
Navigation Works construction pad
          ↓
Figurehead Atelier construction pad
          ↓
Outfitting dock + customer ships + Set Sail

BOTTOM / demand
```

The stations should alternate slightly left and right around one readable central logistics path. This creates the Focus Ladder rhythm while preserving enough empty space around each station for workers, materials, upgrade badges, construction effects, and recipe variants.

### What is visible at the start

- mine/train, storage/refinery, Cannon Foundry, and first customer berth;
- a short portion of the next construction pad at the lower edge, inviting one downward swipe;
- no models, labels, or recipe grids for the later finished stations;
- ocean around the island, with customer ships entering at the bottom.

### Map art rules

- portrait composition first; do not build a landscape map and crop it;
- large, simple silhouettes and saturated station colours;
- one main route with visible material movement;
- no decorative object may compete with a working station;
- props become denser only near unlocked/active areas;
- separate station collections so construction levels can swap cleanly;
- provide simple collision proxies, camera bounds, worker paths, vehicle paths, item sockets, VFX sockets, and customer berth anchors;
- use URP-compatible materials, shared atlases, LODs where useful, and mobile-friendly object counts.

### Required anchor contract

Claude supplies a manifest with stable names and transforms. Codex binds gameplay to these anchors in Unity.

```text
Mine_Output
Train_Load / Train_Unload
Storage_Input / Storage_Output
Refinery_Input / Refinery_Output
Station_Cannon_Input / Work / Output / Upgrade / Worker
Station_Hull_Input / Work / Output / Upgrade / Worker
Station_Rigging_Input / Work / Output / Upgrade / Worker
Station_Navigation_Input / Work / Output / Upgrade / Worker
Station_Figurehead_Input / Work / Output / Upgrade / Worker
Customer_Berth_01..N
Player_Outfitting
Set_Sail
Camera_Stop_01..N
Camera_Bounds
```

Anchor names are frozen before art integration. Gameplay code must never search arbitrary Blender object names every frame.

---

## 4. What the current game is missing

The repository already has most of the ingredients, but not the layer that joins them into this experience.

### Missing gameplay structure

- five separately placed equipment stations;
- a saved unlock state and level for every station;
- a saved active recipe, queue, progress, worker count, and finished-item rack per station;
- recipe discovery conditions and hidden/visible states;
- material requests from each station to storage/refinery;
- visible worker delivery and finished-item pickup;
- customer orders for equipment, not only raw-market income;
- a choice to sell, equip, store, or salvage a completed item;
- predictable progression that unlocks stations in the intended order;
- offline catch-up for every unlocked station.

### Missing presentation structure

- one unified portrait scene instead of separate Main/Market presentation;
- a camera locked to vertical travel with reliable bounds;
- camera stops and auto-focus for station unlocks;
- construction pads that preserve future space without exposing future recipes;
- one contextual station panel instead of a generic full crafting screen;
- a reduced portrait HUD;
- tutorial beats that introduce one station and one decision at a time;
- visual bottleneck language: empty input bin, full output rack, waiting worker, stopped vehicle.

### Existing systems that need adaptation

- `Crafting` currently behaves as one generic random bench; it needs to become a coordinator for distinct station families.
- `SeaCombat` has four equipment slots; Rigging should be appended as slot five without changing existing indices.
- `SaveData` has one pending crafted item, not multiple persistent stations and queues.
- `Market` is presented as a separate scene; equipment customers need to appear at the bottom dock of the unified map.
- `CameraController` already pans and zooms, but needs vertical-only movement, portrait bounds, focus stops, and controlled zoom.
- `GameBootstrap` and project settings currently force landscape and must be switched to portrait.
- the current HUD and several panels contain landscape-specific layouts and too many permanent buttons.
- `WorldIslands` assumes one of eight legacy islands is the active visual world; the new shipyard needs to become the active presentation while retaining the old progression data.

---

## 5. Player-attention rules

### Main screen hierarchy

1. **World action:** workers and materials moving through the currently important station.
2. **Current goal:** one compact task near the top, such as “Craft the first cannon.”
3. **Context action:** one upgrade badge over the selected station.
4. **Economy:** cash, premium currency, and income rate in a compact top bar.
5. **Navigation:** a restrained bottom bar; shop, events, and secondary offers live behind one More button.

### Camera behaviour

- one-finger vertical drag;
- horizontal position locked;
- no accidental diagonal wandering;
- default zoom shows the active station plus part of the stations above and below;
- optional small, limited pinch zoom; never allow zooming far enough out to turn the island into a diagram;
- tap a station badge to centre it;
- unlocking a station triggers a short camera glide to its construction pad;
- returning to the game focuses the highest-priority stalled station, unless the player was recently viewing another station.

### Screen restraint

- no recipe carousel permanently attached to every station;
- no more than one urgent exclamation mark on the world at once;
- offers and ad boosts do not cover the production chain;
- completed stations continue animating but use lower visual emphasis when they are not the current objective;
- use colour and motion for actionable state, not for every decoration.

---

## 6. Progression shape

Exact numbers must be tuned from play data, but the order is fixed.

| Chapter | Player learns | Unlock result |
|---|---|---|
| Tutorial | collect, upgrade, fulfil one order | Cannon Foundry recipe 1 |
| Early 1 | manage input/output bottlenecks | Hull Forge |
| Early 2 | equip an item and compare stats | Rigging Loft |
| Mid 1 | unlock better materials and recipes | Navigation Works |
| Mid 2 | choose sell versus equip versus salvage | Figurehead Atelier |
| Late | optimise all stations and customer demand | rare recipes and station variants |

Each station unlock requires a short achievement chain, not only a cash price. A safe pattern is:

1. complete a number of orders with the current station;
2. reach a workshop reputation level;
3. pay the construction price;
4. watch construction;
5. receive one free starter recipe for the new station.

Never require an unrevealed recipe or a resource the player cannot currently produce. Automated tests must prove that every unlock is reachable from a fresh save.

---

## 7. Safe treatment of the existing maps

Do not delete scenes, prefabs, art, or legacy save fields.

Add one reversible feature switch: `UsePortraitShipyard`.

When enabled:

- startup opens the new Shipyard presentation;
- legacy island roots and the island-map opener remain inactive;
- the separate Market presentation is not opened for normal equipment orders;
- legacy island ownership and resource progression continue to feed the new shipyard;
- Sea remains available through the bottom outfitting dock / Set Sail action.

When disabled, the existing flow still opens for comparison and rollback.

No old save is wiped. New save collections initialise with safe defaults and run their own normalisation. Existing enum indices and persisted lists are not reordered.

---

## 8. GPT/Codex and Claude division

The two assistants may work in parallel only after the shared contract is approved. They must use different branches/worktrees and must not edit the same Unity scene, save file, or bootstrap file at the same time.

### Claude + Blender — owns world art

**Branch:** `claude/portrait-shipyard-art`

Claude owns:

- the portrait island blockout and final Blender map;
- reuse and adaptation of the existing mine, rail, storage, refinery, road, harbor, ship, worker-route, and prop assets;
- five visually distinct ship-equipment station sets;
- locked pad, construction, built, and upgraded visual states;
- the central logistics route and secondary worker paths;
- customer berths and outfitting dock;
- anchor manifest, portrait reference renders, collision proxies, LODs, and exports;
- updates contained under `Tools/blender/isomap/` and an agreed new art export folder.

Claude does **not** edit `SaveData.cs`, `GameBootstrap.cs`, gameplay services, UI scripts, or Unity scenes. It delivers modular FBX files plus the anchor manifest.

Use the existing scripted Blender pipeline as the starting point. Add a new shipyard generator instead of overwriting the current island generator.

### GPT/Codex — owns game integration

**Branch:** `codex/portrait-shipyard-systems`

Codex owns:

- ship-station and recipe data definitions;
- progression, production, customer-order, offline, and persistence rules;
- append-only save normalisation and the fifth equipment slot;
- feature switch and reversible legacy-map deactivation;
- new `Shipyard.unity` assembly through Unity tools;
- portrait project settings and bootstrap orientation;
- vertical camera, camera stops, focus transitions, and safe-area handling;
- contextual station UI and reduced HUD;
- binding exported art anchors to gameplay;
- automated tests, Unity compilation checks, scene smoke tests, and device-layout verification.

Codex does **not** remodel the island or make aesthetic changes inside Claude's Blender collections.

### Shared contract — freeze first

Before either implementation begins, approve:

- the five stable station IDs;
- the five equipment slot mappings;
- map dimensions and Unity scale;
- anchor names and required sockets;
- export folder and file naming;
- worker/vehicle route format;
- construction-state collection names;
- portrait reference resolution;
- the initial unlock order and first recipe for every station.

Only Codex integrates the final Unity scene. Only Claude publishes final Blender exports. This prevents scene and asset conflicts.

---

## 9. Build milestones

### Milestone 0 — approve the contract

- lock station families, map order, unlock order, and ownership boundaries;
- create a fresh-save backup and record the current save schema;
- add no gameplay or art yet.

**Exit:** both assistants can build against the same IDs and measurements without guessing.

### Milestone 1 — greybox + pure rules in parallel

Claude:

- produces the portrait island as simple coloured blocks;
- proves all five stations, routes, docks, and camera stops fit vertically;
- supplies two portrait renders: fully locked start and fully built end state.

Codex:

- implements station definitions and deterministic unlock rules;
- appends Rigging safely;
- adds save normalisation and reachability tests;
- creates the feature switch without deactivating legacy content yet.

**Exit:** the complete spatial ladder is readable in a render and the complete progression is testable without a scene.

### Milestone 2 — one-station vertical slice

- assemble the new scene through Unity tools;
- switch to portrait;
- add vertical camera bounds;
- connect mine → refinery → Cannon Foundry → one customer ship;
- support craft, sell/equip/store/salvage, save/load, and offline catch-up for Cannon only;
- hide all later recipes and show only one quiet Hull construction pad.

**Exit:** a new player can complete the first equipment order entirely from the portrait world.

Do not build stations 2–5 until this slice is fun and readable.

### Milestone 3 — progressive station construction

- add Hull, Rigging, Navigation, and Figurehead one at a time;
- add unlock camera glides and one-step tutorials;
- add per-station workers, queues, input bins, and output racks;
- verify that old stations keep operating while viewing a new one.

**Exit:** fresh-save unlock order is deterministic, saved, and visible in the world.

### Milestone 4 — island materials and recipe tiers

- map existing island resources into equipment ingredients;
- reveal new recipes only when their material and station conditions are met;
- reuse existing rarity, comparison, equip, stash, and salvage behaviour;
- balance order demand so every unlocked station stays relevant.

**Exit:** existing island progression improves the workshop instead of being discarded.

### Milestone 5 — portrait UI and attention polish

- replace permanent side rails with a compact More menu;
- author portrait versions of crafting, order, inventory, map, contract, and offer panels;
- add safe-area support and test common tall phones and tablets;
- use motion/colour only for the current objective or bottleneck.

**Exit:** no important world action is covered by HUD or device cutouts.

### Milestone 6 — final art and optimisation

- Claude replaces greybox collections with finished art without moving frozen anchors;
- add construction sequences, material props, workers, ships, VFX, and sound hooks;
- pool repeated effects and objects;
- profile draw calls, overdraw, memory, and garbage collection on a mid-range Android device.

**Exit:** final map preserves the tested gameplay geometry and meets mobile performance targets.

### Milestone 7 — migration, balancing, and release gate

- verify fresh, mid-game, and late-game legacy saves;
- test enabling and disabling the feature switch;
- tune time-to-first-order, time-to-each-station, idle storage capacity, and offline caps;
- collect analytics for stalls, scroll discovery, recipe use, order completion, and equipment choices;
- run full Unity tests and a real-device portrait smoke test.

**Exit:** the new screen can ship without deleting the legacy maps or trapping an existing player.

---

## 10. Acceptance checklist

- Fresh save shows Cannon Foundry as the only operational equipment station.
- Only the first cannon recipe is initially visible.
- Future station space exists, but its recipes and full models are hidden.
- Every production transfer visible on-screen corresponds to real simulation state.
- The camera travels vertically and cannot drift sideways.
- Every unlocked station can be reached by one continuous vertical drag.
- A new unlock automatically focuses its construction and teaches one action.
- Each station panel shows only recipes available for that station and player state.
- All five stations continue producing when off-screen.
- Offline progress handles all unlocked queues and respects storage/output limits.
- Finished equipment can be sold, equipped, stored, or salvaged.
- Existing island resources unlock meaningful recipe improvements.
- Customer demand does not make older stations obsolete.
- Existing saves load without losing islands, gear, currency, or progression.
- The legacy flow can be restored by turning off one feature switch.
- Portrait layouts pass tall-phone, short-phone, and tablet safe-area tests.
- The build holds the project's mobile frame-rate and allocation targets.

---

## 11. First approval checkpoint

Approve or revise only these items before implementation:

1. station order: Cannon → Hull → Rigging → Navigation → Figurehead;
2. one continuous island ordered source-at-top and customers-at-bottom;
3. existing materials become recipe tiers across stations;
4. Rigging is appended as the fifth saved equipment slot;
5. old maps stay intact behind `UsePortraitShipyard`;
6. Claude owns Blender/map exports and Codex owns Unity/game systems;
7. Milestone 2 builds Cannon only before the other four stations.

Once these seven decisions are accepted, both assistants can begin Milestone 1 safely.
