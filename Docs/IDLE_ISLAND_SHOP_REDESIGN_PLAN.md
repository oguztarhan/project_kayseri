# Island mining tycoon — worker and customer redesign

Date: 2026-09-06
Status: revision 3 — package A contracts implemented; live-game integration belongs to Claude.

**Implementation authority:** [Package A handoff](IDLE_SHOP_PACKAGE_A_HANDOFF.md) supersedes the earlier stage architecture, independent-substage businesses, script proposals and migration sequence below. Stages now fold into existing chapter beats; no theme classes or parallel reward ledger are introduced.

This revision supersedes the original delivery sequence and handoff. Work is shared between Codex and Claude as specified below.

## Direction

Keep the existing island, terrain, ocean, visual identity, expansion fantasy, sailing and sea combat. Make the main island a continuously operating mining business with the upgrade frequency, visible activity and layered progression the user likes in Idle Weapon Shop. Remove land vehicles from the main island and replace their economic work with people. Sailing ships remain part of the theme.

The player manages the business by tapping buildings, improving staff and equipment, collecting optional rewards and choosing expansion or fleet investments. Ordinary production, carrying, serving and payment must run automatically. Remove the character-controlled market mode from the redesigned game: no joystick, manual carrying, manual serving, floor-cash collection or player-presence income bonus. The player only manages upgrades, unlocks, assignments and optional rewards. NPCs perform all ordinary work automatically, from the first sale onward. This applies to every stage and themed event, not only the starting island.

## Reference evidence and limits

Assumed reference: **Idle Weapon Shop**, HOT GAMES CO., LIMITED, Android package `com.hg.idleweaponshoptycoon.android`; not the similarly named Steam game.

- [Official Google Play listing](https://play.google.com/store/apps/details?hl=en_US&id=com.hg.idleweaponshoptycoon.android)
- [Official Apple listing and release history](https://apps.apple.com/us/app/idle-weapon-shop/id6739552291)

The listings describe automated crafting and sales, upgrades and regional expansion, adventuring for resources, companions and customization. Release notes describe themed events, collection events, tournaments, a pass and an event schedule. These establish the broad reference direction. No hands-on reference session was performed; exact screen layout, timer values, drop rates and event rules are not verified. All numbers and themed mechanics below are design proposals, not claims about the reference game. Before visual implementation, compare reference gameplay footage/screens with the current Unity Game view to tune density and interaction rhythm.

## Target main-screen experience

The existing island remains the main view. Building positions and silhouettes stay recognizable. Adapt former transport spaces into pedestrian routes, work yards and visible stock areas with minimal landscape changes.

Production chain: **mine → ore porter → refinery → goods porter → stocked sales counter → customer purchase → coins → upgrades**.

- Miners visibly work the rock face and fill ore piles.
- Porters pick up real batches, carry baskets/crates and deposit them at the refinery.
- Refinery workers process ore; progress and finished goods are visible.
- Goods porters restock island counters.
- Customers enter from harbor/town paths, approach an available counter, queue, buy available refined products, then leave carrying purchases.
- Sellers serve automatically. Ordinary sale coins enter the wallet automatically; tips and treasure are separate optional collectible rewards.
- Characters use distinct outfits, tools and readable work/carry/idle animations. Activity reflects actual inventory and jobs.
- Empty shelves, full piles and waiting customers communicate bottlenecks. Tapping the affected building explains the limiting stage and its useful upgrade.

HUD: coins and actual income/min at the top, one current objective below, a compact event/reward entry at the edge, and bottom navigation for Island, Crew, Fleet and Events. Tapping a station opens a compact bottom panel while the business stays visible. Show current level, exact benefit, cost, next milestone and x1/x10/max purchase options. Avoid surrounding every building with permanent reward icons.

## Upgrades and progression

| Area | Purchases | Visible result |
| --- | --- | --- |
| Mine | Yield, work speed, additional work positions | More ore and more occupied mining positions |
| Porter crew | Carry capacity, handling speed, crew capacity | Larger loads and more productive trips |
| Refinery | Processing speed, batch capacity, product research | Faster batches, new equipment and valuable goods |
| Storage | Ore and finished-stock capacity | Larger organized stock areas; fewer stoppages |
| Sales counters | Service speed, counter capacity, sale value | Faster service and additional staffed counters |
| Harbor | Customer arrival capacity, trade contracts, berths | More visitors and stronger trade opportunities |
| Foremen | Existing cards, levels and assignments | Station specialization and clearly stated bonuses |
| Fleet | Existing ship/captain/combat progression | Stronger voyages and sea encounters |

Give every required job a visible baseline automated worker. For the first conversion, preserve the current level-zero 0.15 service fraction as the starter crew rate, and preserve existing hire rates (0.45 through 1.0). This is the full rate of that crew level in every view, not a penalty for absence; remove the manual-play income bonus. Automatic payment is immediate, while any retained collection-level investment becomes back-office throughput. Rebalance upgrade costs against this baseline after measurement rather than silently multiplying existing income. Hiring improves throughput; it does not rescue a deliberately nonfunctional starting chain. Limit rendered population independently from economic levels so late-game upgrades do not spawn hundreds of characters.

Suggested initial pacing targets, to validate in playtests: first sale within 30 seconds, first useful upgrade within 60 seconds, and an affordable meaningful decision roughly every 20–45 seconds during the first ten minutes. A new capability should arrive roughly every 3–5 minutes during onboarding. Use equipment/crew milestones at selected levels such as 10/25/50 only after checking current caps and cost curves. Longer-term progression can slow, with short objectives still providing intermediate wins.

Upgrade recommendations must use the actual bottleneck and expected income improvement. More extraction should not be advertised as an income increase when sales capacity is already saturated. Show storage headroom benefits honestly.

## Rewards and reasons to keep playing

- Short objectives: sell a batch, upgrade a refinery, improve a porter, serve a visiting merchant. Reuse existing goal/reward infrastructure.
- Bonus points: use existing goal/event/pass progression where practical. Award for completed objectives and real production/sale milestones; show progress toward a chest. Do not create another spendable currency without a concrete need.
- Optional island collectibles: customer tips and washed-ashore treasure, with a capped number visible and predictable reward rules. Core income does not require tapping these.
- Temporary opportunities: merchant rush increases demand; foreman rally temporarily boosts one stage; VIP trade orders consume actual available goods for a premium reward.
- Daily and offline rewards: show what was earned and the next useful purchase. Preserve current caps initially, then rebalance against the new throughput.
- Unlock additional refined products using the existing resource/recipe catalogue. Begin the prototype with the first island's current product; preserve its naming and progression.

## Integrate sailing and combat

Island profits fund fleet improvement. Voyages and sea combat return useful progression rewards: existing foreman/captain rewards first, with rare refining recipes/materials as later extensions. Harbor orders give production a purpose beyond raw coin growth.

Goods allocated to a voyage or contract cannot also be sold to customers. Expose reservation amounts and keep a default sales reserve so loading a ship does not unexpectedly stop everyday income. Combat remains an optional active destination; the island continues operating while the player sails. Retain current combat controls and encounters during the first delivery.

## Events translated into our world

| Event | Island activity | Reward direction | Existing foundation |
| --- | --- | --- | --- |
| Harbor Festival | Serve merchant visitors and complete trade goals | Crew rewards and harbor decorations | HarborFestivalService |
| Foundry Festival | Refine special batches and hit milestones | Refinery progression and themed cosmetics | FoundryFestivalService |
| Production Sprint | Improve a constrained chain and reach output goals | Milestone chests | ProductionSprintService |
| Convoy Expedition | Prepare cargo and complete sail encounters | Captain/ship rewards | VoyageService and sea combat; new integration |
| Island Collection | Complete themed tasks and collect a set | Island decoration or companion cosmetic | New content on existing event infrastructure |

Reuse LiveEventService and SeasonalIndustryPassService. Surface current and upcoming events through one entry, unlocking after the basic loop is understood. Begin with one working event. New event state must specify start/end, scoring, claim grace period, repeat-instance identity and save behavior. Preserve earned unclaimed rewards. Events complement permanent island growth.

## Repository findings and implementation boundaries

- `Assets/Scripts/Gameplay/CoalOperation.cs` owns substantial route-based production, trucks, trains, stock and island presentation. Removing vehicle meshes alone would leave vehicle-dependent economics behind.
- `Assets/Scripts/Core/IslandEconomy.cs` stores upgrade slots by numeric index. Train/ore-truck/cargo-truck slots remain saved even though the current player station list hides transport. Never reorder saved indices.
- `Assets/Scripts/Gameplay/StationCrew.cs` and `SiteLife.cs` already provide people and work-state visuals. They are a starting point; walking scenery alone does not fulfill the new production loop.
- `Assets/Scripts/Gameplay/Market/` contains CustomerQueue, YardWorker, SellCounter, CounterShelf, CarryStack and PersonAnimator. Reuse/adapt behavior and art without importing the separate yard's player-control requirement.
- `Assets/Scripts/Systems/MarketService.cs` owns the market ledger and sale events, and distinguishes live/simulated yards from background operation. Extend this authority rather than adding a second island sales payout.
- `Assets/Scripts/Core/MarketFlow.cs` currently models carry/serve/collect jobs, including a reduced unattended rate. Retain the numerical starter/hire curve for the first conversion, reinterpret it as crew progression, and remove manual-mode branches. Automatic payment must not wait for floor-cash collection.
- Existing goals, foremen, chapters, rewards, voyages, festivals and pass systems should feed the new main screen. Presence in the repository is not proof that every feature is currently functional; verify integrations in Unity.
- `VoyageService` currently documents offline loading restrictions to avoid selling the same cargo twice. Preserve that restriction until the new offline accounting explicitly supports reservations.

Use a single economic authority for each inventory transfer and sale. Worker job states are idle → reserve source/destination → travel → pick up → travel → deposit → repeat. Reservations prevent two workers taking the same batch. Cancelled jobs must return/release inventory exactly once. Customer visuals observe committed sales or request them through the same authority; animation callbacks must never independently mint coins.

Foreground, background-island and offline calculations must share rate/capacity rules. Include in-transit cargo and finite buffers in reconciliation. Decide explicitly how unfinished jobs settle on save/load; do not allow goods to disappear or duplicate. Scene changes and switching to combat must transfer simulation ownership once.

## Numbered stages and reusable map

Progression is **1-1 → 1-2 → 1-3 → 1-4 → 2-1 → …**. The first number identifies a themed chapter; the second is a business stage. Four stages in chapter one are the initial content example, not a hardcoded global limit.

Use the ready island layout as a reusable scene with stable station, path, queue and dock anchors. Stage data chooses its theme, enabled work positions, recipes, number of craftable products, NPC staffing, prices, costs and completion objectives. Do not create a new controller or copy the entire scene for each stage. Additional product lines must fit the map's authored slots.

Illustrative content progression (not final product balance):

| Stage | Theme | NPC-crafted product variety | New management decision |
| --- | --- | --- | --- |
| 1-1 | Starter mining harbor | 1 recipe | Upgrade production versus service |
| 1-2 | Same chapter, busier operation | 2 recipes | Improve a second workstation |
| 1-3 | Expanded trading settlement | 3 recipes | Balance shared inputs and shelf capacity |
| 1-4 | Chapter's developed harbor | 4 recipes | Complete a production and sales milestone |
| 2-1 | New chapter palette, props and goods | Configured starter set | Develop the next themed business |

The number of recipes means distinct items NPCs can actually craft and customers can buy, not just bigger output batches or cosmetic variants of one shared bar count. Reuse Product and Recipe definitions. Keep stock, in-process batches, prices and sales identifiable by product ID. Multi-product inventory needs an intentional MarketService extension; the current scalar bar ledger cannot simply be relabeled.

Completion uses upgrade, production and sales goals. Show a claim/advance action and the next stage's preview. Combat and event wins do not block the basic stage ladder.

Proposed persistence default: stages are permanent businesses, not forced resets. Unlock the next stage while keeping completed-stage investment and background income; retain global wallet, crew collection, captains, fleet and permanent rewards. Initialize new stage-local levels, stock and jobs from its own definition. This preserves the existing empire philosophy and avoids introducing an unrequested reset economy. Treat this as our design default, not a verified rule of the reference game.

Store a stable stage ID separately from the displayed chapter-stage label. Save stage-local upgrades, product inventories, active jobs/reservations, completion and reward claims. Theme art is configuration, not saved gameplay state. Only the selected stage renders NPCs; completed businesses use the shared background simulation.

## Script boundaries — required

CoalOperation.cs is currently 6,637 lines. Splitting the functionality involved in this redesign is explicitly in scope. Do not add the new feature set to it, or move its contents into another giant file or partial-class collection. Leave unrelated systems alone.

Proposed files/responsibilities (agree exact signatures before implementation):

| Layer / script | Responsibility |
| --- | --- |
| Data/StageDefinition.cs | Stable ID, chapter/stage label, theme reference, enabled recipe/station IDs, objectives and starting values |
| Data/IslandThemeDefinition.cs | Existing-map materials, props and presentation selections |
| Core/StageProgression.cs | Pure unlock/completion rules |
| Systems/StageService.cs | Active stage, progression transactions, background-stage registration |
| Core/ProductionSimulation.cs | Timed extraction/refining and bounded input/output buffers |
| Core/WorkerJobs.cs | Job reservations, transfers, capacity and cancellation rules |
| Systems/ProductionService.cs | Advance production/jobs and deliver actual finished goods to MarketService |
| Gameplay/Island/IslandSceneBinder.cs | Connect existing map anchors to services and views; no economy formulas |
| Gameplay/Island/IslandWorkerView.cs | Pooled NPC work/carry animations driven by job state |
| Gameplay/Island/IslandCustomerView.cs | Pooled customer arrival, queue and departure visuals driven by authoritative sales |
| Gameplay/Island/IslandThemeView.cs | Apply a stage's visual theme and enabled stations |
| UI/StageProgressUI.cs | Stage label, completion and next-stage preview |
| UI/StationUpgradeUI.cs | Present upgrades and request purchases through existing economy authority |

Reuse MarketService, WalletService, Product, Recipe, reward/event services and suitable animation helpers. Extend their existing authority rather than creating alternative wallets or sale ledgers. Core remains independent of Unity presentation. Systems cannot reference Gameplay; use plain data/events consistent with the current assembly boundaries. UI and NPC views never credit coins or advance production themselves.

## Claude review — resolved

1. Automatic selling already exists. Step one adapts delivery and adds island presentation; it does not rebuild the sales authority. In the first single-product slice, leave the existing automatic MarketService settlement active. Do not call SetSimulatedYard for the island and accidentally disable that settlement. Customer visuals consume its committed sale events; prevent visual sales from paying again.
2. Keep 0.15 as the initial crew rate during conversion, with existing hire progression; it applies identically while watching, away and offline. Manual advantage is removed. Cost/pacing changes follow measured results.
3. Write and test the old-upgrade conversion before switching transport off. This includes train, ore-truck, cargo-truck and collect-job investment. No shipped intermediate build may orphan them.
4. Splitting CoalOperation is explicitly authorized within this redesign. New production, NPC, stage and UI responsibilities go in separate scripts.

## Shared work and sequencing

These are proposed ownership assignments for the existing Claude session and this Codex task. They do not mean either agent has already implemented or accepted work. Use this shared document for handoff. One owner per file at a time; do not have both agents edit bootstrap, saves, MarketService or CoalOperation concurrently.

### Package A — Codex: stage and economy contracts, before scene implementation

Own the new stage data/rules and pure simulation/job rules, their relevant tests, and a written old-slot conversion table. Define per-product stock and service input/output contracts with Claude before dependent implementations. Define how former islands map to numbered stages and preserve already-earned chapter rewards. Validate missing/duplicate IDs, unavailable recipe inputs, stage order and map-slot limits. No destructive save conversion is inferred from numbering alone.

Handoff: exact file/API list, upgrade mapping, stage persistence schema and acceptance fixtures. Initial fixture content covers 1-1 through 1-4 and 2-1. Stage 1-1 uses the current first product; additional recipes remain authored content, not invented silent replacements.

### Package B — Claude: integrate the first automatic island

Own CoalOperation extraction/removal, ProductionService integration, existing MarketService adaptations, GameBootstrap wiring, SaveData/SaveMigration changes using package A's mapping, and Unity scene/prefab/Inspector changes. Own worker/customer views and map anchors. Preserve the ready island and sailing ships; remove land vehicles and the main-screen market-character route.

Integrate the stage core and one complete 1-1 line: mine → NPC transfer → refinery → counter → automatic sales. Use an isolated copy of an existing save for migration verification. Report the first playable result before broadening scope.

Acceptance: ten minutes without joystick/manual jobs/cash pickup, an observable useful upgrade, consistent inventory, existing investments preserved, and no double payout. Explicitly verify the old market scene and its UI entry points are absent from the player flow, not merely optional.

### Package C — Codex, after B is stable: multi-product runtime, management UI and content progression

Own the NEW multi-product runtime work described in IDLE_SHOP_PACKAGE_A_HANDOFF.md: catalogue loading/stable IDs, NPC recipe execution, per-product inventories and in-progress jobs, shared-input scheduling, demand/pricing/sales integration, and persistence/offline reconciliation. Existing Product/Recipe assets are not wired into the live economy; do not scope stages with 2/3/4 craftable products as content-only work. Reuse/extract B's existing haul machinery and keep a single simulation authority. Coordinate ownership transfers for MarketService and save changes before editing Claude's files.

Also own StageProgressUI and the new station upgrade presentation, stage objectives and recipe unlock rules. Build the 1-1 → 1-2 → 1-3 → 1-4 → 2-1 flow, x1/x10/max purchase presentation, meaningful bottleneck recommendations and bonus/event entry presentation. Claude wires serialized references and theme assets in Unity. Coordinate before editing existing shared HUD code.

Acceptance: each stage changes the configured product/station set; NPCs automatically craft every unlocked product with available inputs; customers purchase the appropriate stock; reload preserves the active stage and earned rewards; progression is not just a label change.

### Package D — Claude: voyages, existing events and full integration

Own existing voyage/combat/event service adaptations, multi-product cargo reservations and themed event content wiring. Keep sailing combat's existing play style; the idle-shop requirement applies to the island business. Add Harbor Festival first, then Foundry Festival/Production Sprint and later convoy/collection content. All production and selling within events remains NPC-operated.

Acceptance: cargo cannot be sold twice, island income continues while sailing, claims pay once across reload/expiration, and event currencies never gate basic chapter-stage progress.

### Package E — Codex review; Claude Unity/device verification

Codex reviews economy, migration, stage transitions and reference-style management behavior. Claude runs Unity compilation, relevant Test Runner checks and device profiling, and fixes owned integration issues. Compare foreground/background/offline results, render population caps and all building/theme phases. Rebalance toward first sale within 30 seconds and useful early upgrades every 20–45 seconds; these are proposed playtest targets.

Report observed results, including existing errors. Do not claim 60fps or working gameplay based solely on source inspection.

## Current review evidence

On this revision, Codex inspected Chapters, ChapterConfig, WorldIslands, IslandDefinition, Product, Recipe, MarketService and MarketFlow, and read Claude's actual review in the existing “Idle Island shop redesign plan” session. Current chapters are eight ore islands with five beats, not numbered business substages. The sale ledger is currently scalar stock, so multiple craftable items require more than content renaming.

Unity is open on Assets/Scenes/Main.unity. The Scene view shows the ready elongated island and harbor. The Game view is covered by the welcome-back panel. Console indicators show one existing error and zero warnings; its cause was not inspected. No Play-mode interaction or compilation/test run was performed. Claude previously reported a Unity MCP connection failure; the open editor alone does not establish that this connection is fixed.

The review above describes the pre-implementation snapshot. Package A has since added isolated C# contracts and tests; consult IDLE_SHOP_PACKAGE_A_HANDOFF.md for the final implementation and verification. Claude reviews that handoff before package B. Follow CLAUDE.md for Unity asset editing and verification.
