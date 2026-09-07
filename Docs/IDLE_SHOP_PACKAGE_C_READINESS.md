# Package C readiness and first implementation slice

**Package-order update:** [C0 yard-upgrade panel](IDLE_SHOP_C0_YARD_UPGRADES.md) now precedes final B teardown. Codex owns the six-track island purchase UI; Claude retains an upgrade-only market until C0 is verified, then removes its entry points. Multi-product C1 follows. This supersedes earlier statements that all B work must finish before any C work.


2026-09-07 — Codex. Independent source/data audit while Claude fixes foremen and completes package B. This is implementation preparation, not a claim that C or B's remaining acceptance checks are complete. No shared runtime files or Unity assets were changed by this audit.

## What was checked

Read the current StageDefinition/StageService, live haul delivery path, presentation switch, product/recipe asset GUID references, and package-B log. Queried the Unity Console read-only. There were no `error CS` entries returned by the targeted query, but runtime exceptions were present; no fresh compilation or test run was triggered while Claude was working.

SeaFightUI.BuildPanel at line 422 indexed four icons with the five-slot SeaCombat loop. Subsequent DriveSheet/RefreshSheet null references followed incomplete construction. While Codex was preparing a scoped fix, another workstream changed that exact code to five icons and a count-based layout. Codex's patch did not apply and changed nothing. The current diff addresses both identified causes; runtime verification remains with the owner of that fix. Do not count the observed old exceptions as proof the new fix fails or claim it passes without retesting.

## Actual authored recipe catalogue

Resolved input/output asset GUIDs directly. These are authored recipes, not runtime production currently operating on the island. Values below are asset tuning, not observed throughput or approved replacement sale prices.

| Recipe | Inputs per batch | Output | Seconds per batch |
| --- | --- | --- | --- |
| CokeRecipe | 1 Coal | Coke | 1 |
| CopperBarRecipe | 1 Copper | CopperBar | 1 |
| SteelRecipe | 1 Iron + 1 Coal | SteelBeam | 1.5 |
| SilverBarRecipe | 1 Silver | SilverBar | 1 |
| GoldBarRecipe | 1 Gold | GoldBar | 1 |
| CutRubyRecipe | 1 Ruby | CutRuby | 1.2 |
| CutEmeraldRecipe | 1 Emerald | CutEmerald | 1.2 |
| PolishedDiamondRecipe | 1 Diamond | PolishedDiamond | 1.5 |
| RubyRingRecipe | 1 GoldBar + 1 CutRuby | RubyRing | 2 |
| DiamondCrownRecipe | 1 GoldBar + 1 PolishedDiamond | DiamondCrown | 2.5 |

Existing first eight product save IDs match the Output column exactly. RubyRing and DiamondCrown are additional authored products; registering them in a runtime catalogue is still new work, not an automatic unlock.

**Coal-only input reaches Coke and no other authored product.** The 1-2/1-3/1-4 targets of 2/3/4 distinct craftable products therefore need a deliberate content decision. StageDefinition.extracted=true is not a supply implementation: marking Iron or Copper extracted without adding real production would falsely pass reachability validation while starving the runtime.

Recommended content direction: keep chapter one's coal identity and introduce three additional coal-derived game products with distinct uses/visuals, rather than granting later-island ores for free. Candidate concepts are construction carbon blocks, filter cartridges and carbon rods. These are proposed content concepts, not approved asset IDs, recipes, resource costs, final names or chemistry claims. Author them with Unity tools after the second-product runtime proof. Alternatively choose new local ore sources with explicit upgrades and progression consequences; do not silently assume either solution exists.

## First C delivery: one new recipe operating alongside Coke

Build and verify the smallest complete multi-product slice before promising the full stage table:

1. Register the actual Coke binding and one explicitly chosen second product, with validated stable IDs, available raw inputs, workstation anchor and recipe data. Preserve Coke's legacy quantity and sale-price semantics.
2. Extend/extract B's single production authority into a focused recipe-processing component. Two workstations share finite inputs/output space; recipe work has an authoritative timer and owns its consumed inputs. NPCs reflect those jobs. No second tick loop may also produce scalar bars for the same workstation.
3. Transfer product-tagged loads into the existing MarketService. Keep unaccepted quantities with the job/source, including retries and cancellation. Never add a second whole-island capacity budget for the second product.
4. Process distinct customer demand/sales through a shared service budget; each paid sale identifies the correct product, consumed quantity and cash amount. NPCs don't mint money independently.
5. Save/load and switch away/back during refining, carrying and sales. Existing single-product migrated saves must retain Coke stock, investments and chapter claims. Load the completed save before MarketService registration.
6. Show both products crafting and selling on Main, with an upgrade affecting the intended station and no manual-player actions. Only then add the third/fourth products and wider stage UI.

Definition-of-done evidence: timed runtime observation, per-product before/after inventory accounting, sale attribution, reload conservation, one shared budget, and targeted Unity tests. Configuration validation alone is insufficient.

## Runtime boundaries to settle at B → C handoff

- **Production ownership:** name the extracted update owner and save checkpoint. Reuse the existing haul-state mechanism; do not add the full new system to CoalOperation.
- **Stock ownership:** raw inputs, in-process consumed ingredients, finished workstation output, in-transit cargo, market stock and voyage escrow are distinct quantities. A unit must appear in exactly one place; partial delivery returns the actual accepted amount.
- **Simulation clock:** state transitions consume elapsed time without dependence on rendered crowd size or animation callbacks. Large offline intervals and small foreground ticks must obey the same conservation rules.
- **Stage progression:** StageService is still a query/validation adapter over Chapters. It neither loads assets nor starts recipes. Its current fixtures use a shared recipe across labels; that proves numbering/claim behavior, not distinct product counts.
- **Recipe validation:** registration needs real supplies and stable identifiers, plus validation of duplicate ingredients/output rules and cumulative stage sets. A recipe asset reference alone is not a stable recipe save ID; decide and serialize explicit recipe IDs before saving partial batches.
- **Economy scope:** the shared sales budget is island-wide. Keep legacy pricing intact for migrated Coke; additional product values are new tuning. Do not read Product.baseValue into every existing sale as an incidental refactor.
- **Save lifecycle:** never construct/register a yard against placeholder SaveData and hydrate legacy rows later. Require save readiness in the composition path; never “repair” the resulting empty row by recopying spent legacy stock.
- **Startup:** current SaveData still defaults UsePortraitShipyard=true and ShipyardFeatureSwitch routes that to Shipyard. The reported live-save override does not establish correct fresh-install startup. Coordinate with the bootstrap owner; don't edit their file concurrently.

## B checks still visible in this checkout

The reviewed CoalOperation delivery branch still ignores the accepted amount, records the offered amount in _deliveredFlow, then clears cargo. Retrying this unchanged would either lose stock or inflate delivery rates. Complete the conservation contract before calling the porter delivery line finished.

MarketDoorMarker still has a Market scene entry; ShipyardFeatureSwitch.AllowsLegacyMarket still returns true for Main's flag. Thus switching the presentation to Main alone does not remove manual-market access. These remain within Claude's stated teardown scope.

The last recorded B progress remains a previously reported 961-test run with four missing-prefab failures. No new runtime sign-off is inferred here. Phase-art removal, automatic customer operation and ten-minute no-manual-interaction acceptance must be recorded by the current owner before C changes shared services.

## Independent work completed / ownership

This audit and recipe dependency table are complete and available for Claude. No C# or serialized asset edits were made, no Play session started, no save loaded/written, and no tests run concurrently with Claude. Continue using IDLE_SHOP_COORDINATION_2026_09_07.md for file ownership. Codex can start the first C implementation slice once B hands over the production/market/save interfaces and the current UI fixes are verified.
