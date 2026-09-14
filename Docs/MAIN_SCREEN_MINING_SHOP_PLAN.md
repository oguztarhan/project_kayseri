# Playable main screen — mining-gear island business

Date: 2026-09-14. Status: user authorized coordinated implementation after correcting chapter lengths. No gameplay implementation or Unity verification is claimed by this document.

Latest coordination checkpoint: package 0's catalogue/audit and package 1's pickaxe logic are verified. Claude ran 110 focused Unity tests covering the new logic and affected legacy systems; all passed. Package 2 scene integration is next for Claude. Empty Main anchors and a flat carrier route are prepared; the business is not yet registered in normal gameplay. Claude retrieved prior user instructions confirming **Main as the gameplay screen**, with Shipyard retained as preview. This supersedes the unresolved-screen statements below: the default startup flag is an implementation mismatch, not a new approval question. See `MINING_SHOP_CODEX_HANDOFF.md` for exact APIs, ownership, test evidence and limitations.

This plan implements the latest request: watch pickaxes, helmets, lanterns and bags being crafted, carried and sold on the main island screen; spend earnings on visible improvements; collect optional bonus coins; expand through numbered islands. The user authorized coordinated work with Claude after the chapter-length correction. This is the task-specific source of truth for this feature. Older documents remain historical references wherever they prescribe Coke, ship equipment, five machine families, or different stage semantics. Project safety rules still apply.

## 1. Intended player experience

The island itself is the place the player plays. They watch workers crafting, see finished products stack beside tables, follow carriers taking recognizable goods to a market, watch customers buy them, and immediately see the coin balance rise. Tapping a table opens a compact upgrade panel while the island continues working.

The repeating decision is: “What should I improve next: crafting, carrying, selling, or opening another product line?” The answer must be visible in the world. Full output racks mean carriers need help. Full market shelves mean sales need help. Empty shelves with idle carriers mean production needs help.

Ordinary sales are automatic. Optional tips and bonuses are collectible. The player can leave the game running for ten minutes without having to start each craft, move a character, or tap each payment.

## 2. Reference research and its limits

The official [Google Play listing](https://play.google.com/store/apps/details?id=com.hg.idleweaponshoptycoon.android&hl=en) describes a business built around crafting equipment, selling to customers, upgrades, and expansion. The official [App Store listing](https://apps.apple.com/us/app/idle-weapon-shop/id6739552291) also describes automated crafting, offline progression and expansion to other locations. These support the overall management direction.

A [January 2025 beginner guide](https://www.mrguider.org/guide/idle-weapon-shop-tycoon-guide-tips-for-beginners/) reports spending sale coins on workbenches, customers, workers and income upgrades. It is an older secondary description, useful for identifying management choices, not current numerical balance.

Adapt the relationship between workshop investment, visible activity and customer income. The timings, prices, carrier behavior, stage reset policy and bonus rules below are our proposed design, not verified rules copied from the reference. This research reviewed public descriptions; it did not include a hands-on session or frame-by-frame gameplay analysis. Combat, pets and monetization in the reference are outside the first delivery.

## 3. What the current project actually contains

Source inspection found Unity 6000.4.9f1. The following are code findings, not proof of Play-mode behavior:

| Existing area | Finding | Consequence |
| --- | --- | --- |
| `Core/MiningGear.cs`, `Systems/MiningGearService.cs` | Four captain equipment slots, random grade crafting, points/scrap and an income multiplier | Reuse suitable names/art. Create sale stock separately; never turn worn equipment into expendable shop inventory. |
| `Systems/MarketService.cs` | Existing wallet settlement, delivery, upgrades, foreground/background and offline paths; runtime selects `products[0]` | Extend one sales authority to handle distinct products. The list-shaped save alone is not multi-product gameplay. |
| `MarketService.Deliver` | Accepts a product ID but currently selects the existing single product row | Implement real product lookup and reject unknown IDs before a second product can enter the ledger. |
| `Gameplay/CoalOperation.cs` | Existing production, porter presentation, routes and material hooks in a large controller | Reuse relevant routes and visuals; keep new workshop/job responsibilities in focused components. |
| `Data/Product.cs`, `Data/Recipe.cs` | Reusable product/ingredient/output/timer definitions | Introduce stable production IDs and explicit workshop supply rules; current stage validation requires recipe inputs. |
| `Data/StageDefinition.cs`, `Systems/StageService.cs` | Content/validation adapter over chapter beats | Does not yet represent independently developing businesses for every numbered island. |
| `Systems/Save/IdleMarketYard.cs` | One island row with stock list and crew upgrades | Existing island keys cannot silently stand for several new independently saved businesses. |
| `Systems/CannonProductionService.cs` | Persistent ship-equipment queues and manual item decisions | Useful patterns, but ship equipment and mining merchandise have different ownership and sales rules. Do not relabel these saved items. |
| `GameBootstrap`, `ShipyardFeatureSwitch`, `SaveData` | Default switch chooses `Shipyard`; older worklist says production belongs in `Main` | Confirm the actual target screen and verify normal startup before wiring gameplay. Source and older documentation disagree. |
| `BalloonRewardService`, `FreeRewardService`, `GoalService`, wallet/UI services | Existing reward, cooldown and presentation infrastructure | Extend suitable boundaries; distinguish free tips from existing rewarded-ad offers. |
| `SaveMigration.cs` | Increasing the global save version triggers a reset | Use an additive, idempotent feature migration. A version bump is not routine schema housekeeping here. |

Existing equipment indices are pickaxe 0, helmet 1, bag 2, lantern 3. The requested BUSINESS unlock order is pickaxe, helmet, lantern, bag. Keep those separate; never reorder saved equipment slots.

Relevant earlier references: `Docs/IDLE_ISLAND_SHOP_REDESIGN_PLAN.md`, `Docs/IDLE_SHOP_COORDINATION_2026_09_07.md`, `Docs/IDLE_SHOP_PACKAGE_C_READINESS.md`, and `UPDATED_PORTRAIT_SHIPYARD_WORKLIST.md`. Their historical test counts are not current verification.

## 4. Numbered islands and product availability

Interpretation of the latest request: each numbered island is a business with its own table unlocks. Arriving at 1-4 does not grant four operating tables. It grants the opportunity to build all four, starting with pickaxes.

| Island | Active on arrival | Tables purchasable in order | Unavailable here |
| --- | --- | --- | --- |
| 1-1 | Pickaxe | None beyond pickaxe | Helmet, lantern, bag |
| 1-2 | Pickaxe | Helmet | Lantern, bag |
| 1-3 | Pickaxe | Helmet → Lantern | Bag |
| 1-4 | Pickaxe | Helmet → Lantern → Bag | None of these four |

Each line keeps operating when a later line opens. There is exactly one crafting table per product in the first release. Add throughput by upgrading that table and its crew; do not spawn duplicate tables to disguise missing product logic.

A table has four meaningful states: unavailable on this island, eligible but locked, affordable to build, and operating. Locked future tables display the next product, cost and unmet requirement. Unavailable products never show a misleading purchase button.

Proposed arrival rules: free level-1 pickaxe table, one crafter, one carrier, a usable sales shelf and one seller. Automation works at the beginning. Building a new table includes its starter crafter. No island can strand the player with zero production and no money to start it.

Proposed persistence: completed businesses retain local table levels, stock and crew and earn background income. Only the selected business renders people. Global wallet and permanent collections remain global. New business tables start at level 1; returning to an existing business restores its own progress. This persistence choice is awaiting the user's response.

Keep the existing global coin wallet initially. Require local sales milestones as well as coins to build later tables, so a wealthy older island cannot skip the entire new business sequence. Do not add a second spendable “bonus coin” currency. If playtests show inherited wealth erases progression, review the economy deliberately before introducing local wallets.

User correction: progression continues 1-1 through 1-7, then 2-1 through 2-6, then 3-1 through 3-9, and onward. These are example chapter lengths, not a repeating pattern or a global limit. Each chapter has its own configured island list. The first four islands introduce product availability; they do not cap the game at four islands. From 1-4 onward all four products are available, with every newly entered business starting with pickaxes and purchasing helmet, lantern and bag sequentially.

Difficulty increases with the overall position in the campaign, including chapter boundaries. Later construction/upgrade costs and local sales objectives grow alongside product rewards and logistics demand. Do not restart difficulty at 2-1 or 3-1 or simply stretch every craft timer. Author and validate the 7/6/9 example progression as a foundation; prove the first playable pickaxe business before expanding playable content. Do not invent new merchandise for 2-1.

## 5. Production, stock and carriers

Visible route:

`Crafting table → finished-item rack → carrier → product shelf at market → customer → coins → upgrades`

Proposed first-release supply rule: routine materials are included in workshop operation; time and output space determine crafting. There is no ingredient purchase or consumable-energy requirement in the first playable slice. This is a proposed simplification requiring plan approval, not a claim about existing recipes. Existing mine/refinery systems must not accidentally add a second payout to the new business. A physical raw-material chain can be scoped separately if wanted.

If existing Recipe/StageDefinition cannot represent an explicit automatic-supply workshop cleanly, add a small workshop configuration referencing Product; do not mark nonexistent materials as “extracted” just to pass validation. No new package is necessary.

Crafting begins automatically when the table is built and has room for one output. Each cycle produces one identifiable unit. Reserve output capacity when a cycle starts. Pause with “Output full” when another cycle cannot start; never delete overflow or keep running an invisible timer that generates unbounded stock. Show progress, remaining seconds, current level and output count. Upgrades preserve the completed fraction of an active cycle.

Every product moves through distinct ownership states: in progress, table output, carrier cargo, market stock, sold. A unit belongs to exactly one state. Finished goods count as whole items; intermediate timing may use fractional seconds. Frame rate and visual population do not determine item counts.

Carrier behavior: choose a valid job → reserve source quantity and destination space → walk to rack → pick up → walk to market → deposit accepted quantity → release reservation → repeat. Begin with one-product loads for readability. Show the actual pickaxe/helmet/lantern/bag in the carrier's hands or load.

Choose jobs using round-robin fairness with a higher priority for a nearly full rack. Do not always choose the fastest product and starve bags. Limit waiting for a fuller load: a finished bag must not wait for several more bags before dispatch. A full destination leaves cargo owned by the carrier or safely returns it to its source; it never disappears. Cancellation and reload release or restore reservations once.

Reuse authored walking paths and stable endpoints. Prefer simple waypoint movement on this fixed island. Validate slopes, obstacles and crossing paths in Unity before deciding any navigation package is needed.

## 6. Customers and one authoritative sale

Customers arrive at the market, show a wanted-product icon, join an appropriate waiting position, receive the product, pay, and leave carrying it. Starter customers buy one unit. Later basket sizes are outside the initial balance.

Generate demand only for operating product lines. Favor available stock and use bounded pending demand for items being made; do not fill the only service position with a bag request while pickaxes could be sold. Permit waiting requests to step aside so another stocked order can be served. Queue limits prevent an ever-growing crowd.

One market service budget is shared across all product types. Adding four products must not multiply the same seller's capacity by four. Track sold quantity, product ID, unit value, business ID and a transaction/receipt identity for each committed sale.

MarketService alone commits stock removal and coin credit. A customer view requests or observes a service job through that authority. The sale completes at the modeled service deadline, aligned with the handoff animation. Animation callbacks and coin effects never award currency. Low-quality mode, camera movement or despawn cannot cancel an already committed payment or pay again.

In the first slice, show every sale. At high throughput, cap rendered actors and represent multiple modeled customers with a clear grouped receipt if necessary. The world must still show the correct product flow; spawning extra bodies must never increase revenue.

## 7. Initial balance to test

All values are proposed defaults in Inspector-editable configuration, not validated economy results. Coins below exclude permanent multipliers and tips. Workshop construction is a one-time purchase, not a per-item cost.

| Product | Base craft duration | Base sale price | Ideal output/min | Ideal coins/min | Proposed table cost |
| --- | ---: | ---: | ---: | ---: | ---: |
| Pickaxe | 10 sec | 20 | 6 | 120 | Free on arrival |
| Helmet | 20 sec | 60 | 3 | 180 | 300 |
| Lantern | 40 sec | 150 | 1.5 | 225 | 1,400 |
| Bag | 60 sec | 360 | 1 | 360 | 5,000 |

These rates assume continuous crafting, enough carriers, sufficient demand and no shelf blockage. Actual earnings are constrained by the slowest part of the route. At base levels the four lines together produce 11.5 items/minute and at most 885 coins/minute. A slow bag must be sufficiently valuable to justify opening it.

Starting tuning candidates: four output spaces per table, four market spaces per active product within one explicitly allocated market capacity, one carrier carrying two units, a roughly 6–10 second round trip on the chosen map, two seconds of seller service per customer, and at most six visible waiting customers. Measure route time; do not assume the existing map fits that target. Stagger initial craft phases once several lines are active to avoid artificial synchronized traffic bursts.

Table panel exposes two purchases: Work Speed and Product Value. Example candidate curves: speed multiplier `min(3, 1 + 0.10 × (level - 1))`; value multiplier `1 + 0.15 × (level - 1)`; each track has its own configured starting cost and geometric cost growth. Pickaxe value upgrade starts at 40 coins, with 1.18 cost growth as an initial test. Show exact before/after seconds or coin value. Round displayed and charged prices consistently. Reassess curves after measuring payback; do not present theoretical production as guaranteed income.

Shared improvements: carrier load, walking speed, an additional carrier at clear milestones, seller speed and shelf capacity. Introduce these gradually. A capacity upgrade should visibly enlarge a rack; a hire should add a worker where the actor cap permits; speed upgrades should visibly shorten the relevant task.

For meaningful bottleneck guidance, compare measured production, delivered units, stocked units and completed sales over a short window. Show “Rack full — improve carrying” or “Shelves full — improve selling” only when sustained. Avoid flickering recommendations from a single empty second.

## 8. Main-screen layout and collection

Keep the existing approved island art and camera character. Anchor four workshops so their loading areas connect to a readable common route and market. Use the closest suitable existing pads; exact mapping is decided after the target scene is confirmed. Do not rename existing ship-machine save IDs to mining-product IDs.

| Screen area | Contents |
| --- | --- |
| Top safe area | Coins, compact observed income, island label; existing settings access |
| Main play area | Working tables, progress, finished racks, carriers, stocked market, customers |
| Next build site | Product silhouette, construction cost, concise requirement |
| Compact objective | One current goal and a claim action when earned |
| Contextual bottom panel | Selected table or crew upgrade; before/after effect and price |
| Optional reward location | Small market tip pile or existing balloon offer, with clearly distinct treatment |

Keep most of the island visible when upgrading. One compact panel at a time. Hide unrelated activity prompts during onboarding. Preserve access to existing secondary features through their established navigation; do not expand their scope in this task. Camera focus on a selected table is brief and user-controlled. In portrait, use short vertical movement if the whole island cannot remain readable at once.

Base sale coins fly toward the wallet automatically. “Collect” applies to optional tips, goal rewards and welcome-back rewards. Proposed free tip: a small fraction of completed-sale value accrues into one capped, tappable pile at the market. It remains claimable until collected or the cap is reached; missed tips never stop production. Persist the unclaimed balance and claim atomically. Target optional active rewards at roughly 10–20% additional session earnings, then test; avoid balancing basic progress around perfect tapping.

Reuse the existing balloon offer where appropriate, but label ad-backed bonuses clearly and keep their reward path separate from the free tip pile. No ad integration work is needed to prove the core loop. Stage and goal rewards pay exactly once and exclude their own payout from sale-based progress.

Dynamic counters/progress use separate canvases from static HUD. Update text on value changes or a modest cadence; avoid rebuilding every label every frame. Include safe-area, narrow-phone, localization and large-number checks.

## 9. First-session and stage goals

Targets for a fresh save, without ads or premium boosts:

| Time or milestone | Intended experience |
| --- | --- |
| First 5 seconds | See a working pickaxe table and an obvious sales destination |
| About 10 seconds | First pickaxe completes and becomes visible stock |
| Within 20–30 seconds | Carrier delivery, customer purchase and first coin credit |
| Within 30–60 seconds | First useful affordable upgrade; effect visibly changes the business |
| Next 2–4 minutes | Several decisions, an optional tip, and progress toward island completion |
| Roughly 3–5 minutes | 1-1 can be completed, subject to measured balance |

Candidate 1-1 completion: sell 20 pickaxes and buy four table upgrades. Candidate helmet gate: sell 10 pickaxes locally and afford its table. Lantern gate: build helmet and sell five helmets locally. Bag gate: build lantern and sell five lanterns locally. These are tunable starting conditions, not hardcoded global rules.

Later island completion should require every supported table built and several sales of its newest product. Suggested tests start at ten helmets for 1-2, eight lanterns for 1-3, five bags for 1-4, plus an appropriate local upgrade goal. Target about 8–12, 12–18 and 18–25 minutes respectively for a fresh-business playtest; revise after simulation and play. Never force advancement automatically. Show “Island complete” and let the player collect the reward and choose when to continue.

First-sale onboarding uses a small pointer or world highlight. It teaches one upgrade, one bonus claim and one build pad through actual transactions. It never awards imaginary demonstration money from UI-only code.

## 10. Runtime and save responsibilities

Agree exact APIs and file ownership before implementation. Suggested focused additions, only where existing boundaries cannot cover the job:

| Responsibility | Proposed location / existing integration |
| --- | --- |
| Product IDs, times, price and table tuning | Data workshop definitions referencing existing Product |
| Craft progress and bounded output | Core workshop rules + Systems production service |
| Reservations and cargo ownership | Focused Core carrier job state; service owns elapsed-time advancement |
| Distinct stock, demand, service and receipts | Extend existing MarketService and market save rows |
| Numbered-business unlocks and completion | Extend StageService deliberately; preserve ChapterService reward ownership where applicable |
| Scene bindings and views | Small Gameplay island binder, workshop view, carrier view, customer view |
| Table/crew upgrades, build pads, goals | Existing HUD boundaries plus focused UI components |
| Tip balance and claims | Focused reward service using existing WalletService and save infrastructure |

No second wallet, competing market payout, broad rewrite of CoalOperation, or dual production tick for the same goods. Core stays independent of Unity views. Systems do not depend on Gameplay. Expose tunables as `[SerializeField] private`; bind references in the Inspector as the user requires. New MonoBehaviours follow one class per file.

Store stable business, station, product and recipe IDs independently of displayed labels. Persist built tables, levels, progress, output counts, market stock, in-transit cargo, active service/reservations, local sale counts, stage completion/claims, tip balance and simulation timestamp. Do not use array order or translated names as identity.

The latest request changes old “four beats of one business” semantics. Add business-local records keyed by stable IDs for arbitrary chapter/island lists rather than resetting the existing coal row repeatedly or indexing through the legacy fixed chapter beats. Define an explicit migration table for old island progress, stock, upgrades, shipyard items and claimed chapter rewards. Preserve previous economic value; do not silently relabel Coke as pickaxes or delete unfinished ship equipment. Unknown old stock can remain in a preserved legacy record pending an approved conversion. Any conversion must prevent the preserved value being paid twice.

Foreground, background business and offline modes use the same craft/transport/sales constraints. Offline progression should process elapsed-time events or bounded aggregated equivalents; never loop once per rendered frame over hours. Persist partial cycles and cargo, reconstruct views on return, and consume each elapsed interval once. Model boost expiry at its actual time; do not apply a recently active boost to the entire absence.

Choose one payout path for the new businesses. Existing global offline-rate grants must exclude income already settled by the new simulation. Apply the existing configured offline cap/efficiency intentionally once. Keep legacy income outside the new-business ledger isolated until its migration is explicit. Saving during pickup, handoff, sale, reward claim and island switch must conserve both items and money.

## 11. Sequential implementation packages for Codex and Claude

Assignments below are proposed roles for the user's two agents, not dispatched tasks. Work one package at a time and confirm its result before beginning the next. One writer owns shared files at any moment. Scene/prefab/asset changes use Unity tools; the user wires serialized references unless they explicitly delegate that work.

| Package | Owner | Deliverable | Acceptance gate |
| --- | --- | --- | --- |
| 0 — Target and contract | Codex drafts; Claude reviews | Confirm actual main scene, four-product IDs, numbered-business semantics, migration mapping, API/file list and scene anchor checklist | Approved design; normal startup observed on the selected screen; baseline console recorded |
| 1 — Pickaxe business logic | Codex | Auto 10-second craft, finite rack, carrier cargo transaction, one-product customer sale, wallet receipt and save state | Focused Unity tests show exact stock/payment conservation and no duplicate payout |
| 2 — Pickaxe visible and playable | Claude | Reuse current map/people, bind table/rack/routes/market, add observable crafting/carrying/customer states and one upgrade panel | Ten-minute Play-mode run; first sale within target; upgrade visibly helps; works without repeated manual actions |
| 3 — Four-product business | Codex | Real product lookup, 20/40/60-second lines, shared carrier/seller budgets, distinct prices, sequential table purchases and saves | Two-product proof first, then all four; no starvation, incorrect goods or multiplied shared capacity |
| 4 — Four-product presentation | Claude | Four table/load/shelf visuals, locked pads, matching customer requests and readable portrait layout | Can follow each product from table to customer; one table per product; second table never starts before purchase |
| 5 — Islands and active rewards | Codex | Variable chapter-length progression (7/6/9 example), increasing campaign difficulty, local milestones, persistence, free tips and goal rewards; adapt compact HUD | 1-7→2-1 and 2-6→3-1 work without a difficulty reset; every island starts with pickaxes; reopening retains purchased tables; rewards pay once; no softlock |
| 6 — Offline and integration | Codex implements; Claude runs Unity scenarios | Exact mode ownership, elapsed-time settlement, old-save conversion and existing modifier/secondary-screen checks | Reload/switch/background/offline scenarios pass without duplicate currency or lost stock |
| 7 — Balance and Android verification | Claude records play/device evidence; Codex reviews math and defects | Fresh-save timing runs, 1-4 traffic stress, UI checks, performance profile and measured tuning revision | Unity console has no errors/new warnings; target Android device measured; final user playtest accepted |

Packages 1 and 2 are the first milestone: one complete pickaxe business visible on the actual main screen. Do not spend the first milestone on four polished inactive tables. Later packages begin only after that loop earns money correctly.

Handoff after every package: approved scope, files changed, exact public API and IDs, scene/Inspector work still needed, migration impact, tests actually run, observed results, remaining issues, and next owner. Record the current commit when available. Both agents read this document and project instructions before touching a transferred file. Never edit GameBootstrap, MarketService, SaveData, migration code or the HUD concurrently. No automatic pushes.

Codex supports repository instructions through AGENTS.md; see [official instruction guidance](https://learn.chatgpt.com/docs/agent-configuration/agents-md). Keep task detail here and keep the short project safety rules in their existing instruction files. No model choice or tool access is assumed for Claude; Unity verification is assigned only to the environment that actually has it.

## 12. Verification checklist

Run tests through Unity Test Runner / Unity MCP `run_tests`, per project instructions. After C# edits, wait for recompilation and inspect the console. This session has no callable Unity MCP tools, so implementation verification will require a connected Unity environment or user-run checks. Do not mark a package complete while those checks are outstanding.

Meaningful automated scenarios:

- At 9.9 seconds there is no completed pickaxe; at 10 seconds exactly one exists, with identical results for equivalent tick partitions.
- A full output rack blocks new work without losses; opening space resumes correctly. An upgrade preserves progress without duplicating completion.
- Two carriers cannot reserve the same item. A refused or partial delivery retains the remainder. Cargo survives a save/load or is settled by an explicitly tested conservation rule.
- Each customer consumes the correct product and pays its correct value once. Bags never use pickaxe pricing. No animation, tip claim or reload repeats a sale receipt.
- Four products share one seller capacity and finite destination budget. Bag and lantern output cannot starve indefinitely behind pickaxes.
- Tables obey island availability, prerequisite, affordability and one-time construction rules. Repeated purchase input cannot spend twice.
- 1-4 initially has only pickaxes; after all purchases all four lines work concurrently. Visiting 1-2 cannot erase 1-4 progress.
- Campaign supports differing chapter lengths, traverses 1-7→2-1 and 2-6→3-1 correctly, ends the authored list safely at 3-9, and can append further chapters without modifying progression code. Difficulty increases across chapter boundaries. Later chapters retain four available products.
- New-business save round trips preserve goods, levels and claims. Existing captain gear slots, paid benefits, legacy stock and chapter rewards retain their meaning.
- Equivalent foreground/background/offline intervals match before deliberate cap/efficiency policies. A boost expiring mid-interval applies only to the eligible interval. Welcome-back credit cannot pay the same income again.
- Rapid claim/save/restart cannot duplicate tips, stage rewards or balloon rewards.

Manual Play-mode scenarios: fresh startup; follow ten individual pickaxes; upgrade mid-craft; fill a rack; fill market shelves; temporarily starve a product; unlock each table; switch island with loaded carriers; open and close secondary UI; background at pickup, sale and claim; reload at those points; advance and revisit; watch the fully developed 1-4 for at least ten minutes.

Art/UI checks: correct product silhouettes in craft/load/shelf/customer views; feet follow paths; no unreachable racks; readable progress; no permanently blocked customer; dynamic text does not obscure the island; safe areas and localization remain usable.

Performance: pool carriers, customers, item views and effects; cache components in Awake; avoid LINQ and per-frame allocation in the new steady-state simulation; cap visible populations independently of economic capacity; use URP-compatible shared materials; split static/dynamic canvases. Measure frame time and GC on a named mid-range Android device. 60 fps is a 16.7 ms frame budget, not an achievement inferred from desktop Play mode.

## 13. Approval decisions and scope boundary

Two clarifications were requested while preparing this plan: the desired main-screen scene, and whether completed numbered islands keep earning. They do not block this written proposal, but their answers must be incorporated into package 0 before scene wiring or persistence changes.

Other proposed defaults included in plan approval: automatic basic material supply for the first loop; 10/20/40/60-second craft durations; automatic ordinary income; optional free tips using the same coins; global wallet plus local sales gates; one table per product; preserving captain equipment and existing paid benefits.

No additional combat, collection, event, package, monetization or map replacement work is included. Existing integration behavior that this feature touches must still be checked. Product models and animation can reuse existing assets where suitable; missing art is listed for the user/Claude rather than silently purchased or installed.

The user authorized starting coordinated implementation. Package 0 is in progress: Codex owns the configurable campaign foundation and Claude owns the Unity/runtime audit, recorded in MINING_SHOP_CODEX_HANDOFF.md and MINING_SHOP_CLAUDE_HANDOFF.md. Complete and confirm that milestone before broadening into the pickaxe integration. The two earlier screen/persistence clarifications remain recorded as unresolved; the authorization to start does not invent answers to them.
