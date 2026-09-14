# Mining shop — Claude audit and foundation verification

2026-09-14. Package 0 (target and contract) audit plus verification of Codex's campaign foundation.
Read-only: no C#, scene, prefab, asset, save or registration was changed by Claude for this report.
The console was not cleared at any point.

## 1. Startup, target scene and coin flow — observed, not inferred

Normal startup was driven through Unity MCP: Play from the configured `playModeStartScene = Bootstrap`.

| Check | Observed |
| --- | --- |
| Scene reached | `Main` (single loaded scene) |
| Services | `GameBootstrap` in `DontDestroyOnLoad`: `Data`, `Wallet`, `Market`, `Chapters` all non-null; `ServiceLocator` returns `WalletService` and `MarketService` |
| Island controller | `CoalOperation` live, `islandKey = coal`, art root `Island_Shipyard` |
| Camera | `Main Camera`, aspect 0.56 (portrait) |
| Save | `version = 7`, `unlockedIslands = [copper]`, `UsePortraitShipyard = false` on this live save |
| Coin flow | cash **20,537,801.8 at t=63.7s → 20,561,849.7 at t=139.5s** |
| Coal yard | income 24,047.9/min, delivered 65.0/min, stock 0.0/750.0, service rate 1.00 |
| Copper yard | owned; income 0.0/min, delivered 0.0/min (background yard with no measured delivery) |
| Total | `incomeRatePerSec = 400.80` |

Coins flow and every registered service needed by a shop exists. Coal income reads 0 for roughly the first
15 seconds after the island starts because `MarketService`'s meter needs `MinTrustedSeconds` of samples.

**Correction to an earlier Claude report.** During the 2026-09-07 idle-shop work Claude reported the island
booting with null services. That was an artifact of repeated MCP play/stop cycles. A clean Play from
`Bootstrap` today is healthy. Do not plan around that earlier report.

### The main-screen evidence conflicts — reported, not resolved

- `UPDATED_PORTRAIT_SHIPYARD_WORKLIST.md` (repository root, line 66-67): *keep `Main.unity` as the production
  gameplay scene*; *keep `Shipyard.unity` as an isolated art/anchor preview and test scene*.
- `SaveData.UsePortraitShipyard` defaults to **`true`** (`SaveData.cs:16`). `GameBootstrap.Start` loads
  `ShipyardFeatureSwitch.PresentationScene(...)` with `LoadSceneMode.Single`, so **a fresh install boots
  `Shipyard.unity`, not `Main`**. Only this particular live save reaches `Main`, because the flag was set false
  through `GameBootstrap.SetUsePortraitShipyard(false)` on 2026-09-07.
- `Market.unity` is no longer in Build Settings. Build order today: Bootstrap, Main, Shipyard, Sea,
  KayseriIsland_IndustrialReference.

The shop cannot be "on the main screen" for new players until the owner of `GameBootstrap`/`SaveData` decides
the fresh-save default. That is a decision, not an implementation detail. It blocks scene wiring, not the
Core/Data foundation.

## 2. Anchors available for a pickaxe slice

### `Main.unity` — art, no authored gameplay anchors

`Island_Shipyard` holds `Art` (25 numbered groups) and `Vehicles` (rake and four haul bodies, now worn by
porters). Nothing is named table, rack, shelf, pad, counter or queue. `CoalOperation` has **no serialized
Transform anchors**; it resolves stations by name at runtime. Candidate positions (world, rounded):

| Role | Candidate | Position |
| --- | --- | --- |
| Market / sales building | `Art/09_Customers/Customer_Island_01` ("Market" walls, awnings, front door) | root (-401, -13, -1360); door (-401, 65, -1415) |
| Customer plazas | `Customer_Island_01/02/03` sand plazas | (-401, 27, -1360), (0, 27, -1360), (401, 27, -1360) |
| Walkways to customer islets | `Art/03_Parts/Customer_connection` / `_001` / `_002` | (-141, 23, -1192), (61, 24, -1213), (286, 24, -1207) |
| Workshop building | `Art/06_Factory/Blue_Factory` | (19, 315, 43) |
| Rack / stock props near factory | `Art/06_Crates/Wooden_freight_crate*` | y ≈ 313, e.g. (-249, 313, -58), (-45, 313, -169), (137, 313, -162) |
| Mid-route crates | `Art/08_Crates/*` | y ≈ 88, e.g. (-181, 88, -784), (195, 88, -741) |
| Crate beside the market | `Art/09_Crates/Wooden_freight_crate_007` | (-301, 30, -1400) |
| Inactive badge art | `Art/12_Interface/Customer_Badges` | inactive |

**Route warning:** the factory deck sits near y ≈ 313–330 and the customer plazas near y ≈ 27. A pickaxe
carrier route from factory to market descends roughly 290 units over roughly 1,400 units of ground. Existing
haul loops and `AuthoredFootpath()` must be measured against that before a 6–10 second round trip is assumed.
A market-side table cluster would give a far shorter route than the factory.

### `Shipyard.unity` — a complete authored anchor rig

Read from the scene file hierarchy, not edited. `Gameplay_Anchors` contains, for each of five stations
(`Cannon`, `Hull`, `Rigging`, `Navigation`, `Figurehead`): `_Work`, `_Worker`, `_Input`, `_Output`,
`_Upgrade`, `_LockedPad`. Plus `Customer_Berth_01..03`, `Storage_Input/Output`, `Mine_Output`,
`Refinery_Input/Output`, `Train_Load/Unload`, `Set_Sail`, `Player_Outfitting`, `Camera_Stop_01..07`,
`Camera_Bounds`. `Gameplay_Routes` holds waypoint polylines: `Worker_<Station>`, `Delivery_A_to_B` chains,
`Sail_to_Berth_01..03`, `Rail_Mine`.

That is almost exactly table / crafter spot / rack / build pad / upgrade pad / customer position.
**But those names are live ship-machine IDs.** `ShipyardProgression` defines `Station_Cannon` etc. as machine
IDs and `ShipyardMapView.FindAnchor(machineId + "_Work")` looks them up. Per the plan, do not rename or reuse
them as mining-shop IDs. Reusing the *pattern* (per-station `_Work/_Output/_LockedPad` children under one
anchor root, waypoint children under one routes root) in `Main` is the lowest-risk path. `Customer_Berth_*`
are ship berths reached by `Sail_to_Berth_*`, not pedestrian customer positions.

**Proposed pickaxe binding (needs agreement):** author a new `MiningShop_Anchors` root in `Main` using the
Shipyard naming pattern with fresh names — `Shop_Pickaxe_Work`, `_Worker`, `_Output`, `_LockedPad`,
`_Upgrade`, `Shop_Market_Shelf`, `Shop_Customer_Queue_01..06` — placed near `Customer_Island_01`, and a
`MiningShop_Routes` root with `Carry_Pickaxe_to_Market`. Scene authoring is Unity-tool work and the user wires
Inspector references unless delegated.

## 3. Unity MCP, console baseline and blockers

- Unity MCP is available and working this session (native `UnityMCP` tools; HTTP bridge on 8080).
- Baseline console before any foundation import: **1 error, 0 warnings.** The error is
  `CommandInvokationFailure: Unity Remote requirements check failed ... adb forward tcp:7201 tcp:7201`.
  It is environmental (Unity Remote with no Android device), not gameplay code, but **this console is not
  clean** and must not be described as clean.

Blockers and risks, in order:

1. **Fresh-save presentation default** (section 1). Owner: `GameBootstrap`/`SaveData` owner.
2. **`Main` has no gameplay anchors.** Scene authoring through Unity tools is required before package 2.
3. **Vertical route.** Factory-to-market descent must be measured before timing targets are trusted.
4. **Play mode blocks import and tests.** New scripts did not import while Play was running (no `.meta`
   generated); EditMode tests cannot start during Play. Stop Play before any verification.
5. `MarketService.ProductFor` returns the island key itself for unknown input. Never pass a
   `mining-shop.*` business ID through it; the mining shop needs explicit product lookup (plan §3).

## 4. Review of the campaign separation, IDs and old saves

Source reviewed: `Core/MiningShopCampaign.cs`, `Data/MiningShopCampaignConfig.cs`,
`Tests/EditMode/MiningShopCampaignTests.cs`.

**Agreed.**

- Separation from legacy progression is clean. Legacy `Chapters` is fixed (`Count = 8`, `BeatCount = 5`,
  keyed by ore island `coal`..`diamond`), saved as `ChapterState { id, bool[] claimed, introSeen }`, and
  `StageService` maps beats 1..4 onto stages. The new catalogue writes none of it.
- No ID collision. New IDs are namespaced `mining-shop.*`; legacy keys are bare ore names, and the idle-shop
  product IDs are `Coke`, `CopperBar`, `SteelBeam`, `SilverBar`, `GoldBar`, `CutRuby`, `CutEmerald`,
  `PolishedDiamond`.
- `CampaignIndex` is continuous across chapter boundaries (6→7, 12→13), which is what "no difficulty reset at
  2-1" needs.
- Availability `Math.Min(4, CampaignIndex + 1)` gives 1,2,3,4 then four forever, matching the request.
- Merchandise unlock order is kept apart from `MiningGear` saved slots (`SlotBag = 2`, `SlotLantern = 3`),
  and a test pins both.
- Unknown and final IDs return false without wrapping. Constructor copies inputs defensively. No save version.

**Points to consider before package 1 builds on it.**

1. **Availability is derived from overall position, not authored.** A later chapter cannot re-introduce
   products or change the unlock count without code. That matches today's request; flag it if chapters may
   ever vary.
2. **`IslandAt(int)` throws `IndexOutOfRangeException`** for an out-of-range index, while the rest of the API
   uses `Try` patterns. Prefer a guarded accessor or document the precondition.
3. **Serialized defaults apply once.** `_chapters` has an inline default. Unity writes it into a
   `MiningShopCampaignConfig` asset only when that asset is created; changing the inline list later does not
   update an existing asset. Treat the created asset as the source of truth, not the code defaults.
4. **No `.asset` exists yet**, so nothing at runtime can call `CreateCampaign()`. Correct for this milestone.
5. **Old-save preservation is still entirely outstanding**, as Codex states. Before package 1 persists anything:
   a business-local record keyed by these IDs; an explicit table saying what legacy `idleMarketYards` coal
   stock and `ChapterState.claimed` become; and a guard that preserved legacy value cannot be paid twice.
   Suggested default: leave legacy rows untouched as a preserved record and start `mining-shop.island-01-01`
   fresh, so no Coke is silently relabelled as pickaxes.

## 5. Agreed file and API boundary for this milestone

| Owner | Files | Rule |
| --- | --- | --- |
| Codex | `Core/MiningShopCampaign.cs`, `Data/MiningShopCampaignConfig.cs`, `Tests/EditMode/MiningShopCampaignTests.cs`, `Docs/MAIN_SCREEN_MINING_SHOP_PLAN.md`, `Docs/MINING_SHOP_CODEX_HANDOFF.md` | Claude reads and tests only |
| Claude | `Docs/MINING_SHOP_CLAUDE_HANDOFF.md`; Unity MCP verification runs | No C# or scene edits in this milestone |
| Nobody during this milestone | `MarketService`, `GameBootstrap`, `SaveData`, `SaveMigration`, `CoalOperation`, HUD, serialized assets | Unchanged until a named handoff |

Public API relied on: `new MiningShopCampaign(string[] chapterIds, string[][] islandIds)`,
`IslandAt(int)`, `TryGet(string, out Island)`, `TryGetNext(string, out Island)`, `ChapterCount`,
`IslandCount`, static `ProductIdAt(int)`, `Island { Id, ChapterId, ChapterNumber, IslandNumber, CampaignIndex,
AvailableProductCount, StartingTableCount }`, `MiningShopCampaignConfig.CreateCampaign()`.

## 6. Foundation verification — results actually observed

**Import.** The three files had no `.meta` while Unity was in Play mode; they did not import until Play was
stopped. After `refresh_unity` (force, compile requested) Unity generated all three `.meta` files and the
types load into the expected assemblies:

| Type | Assembly | GUID |
| --- | --- | --- |
| `Game.Core.MiningShopCampaign` | `Game.Core` | `5930eda284f6642dd9853fcd0a346f47` |
| `Game.Data.MiningShopCampaignConfig` | `Game.Data` | `d344cab369002489897a4d9211d70af2` |
| `Game.Tests.MiningShopCampaignTests` | `Game.Tests.EditMode` | `c6ea4d535b5254e699090b55cf8bbb18` |

**Recompilation console (not cleared).**

- Errors: **0** after recompilation. This is *not* evidence of a clean project: the environmental
  `Unity Remote requirements check failed ... adb forward` error from the baseline was flushed by the domain
  reload, not fixed, and it is raised again on entering Play.
- Warnings: **64 entries**, every one pre-existing and outside the foundation — CS0618 obsolete-API use in
  `AdRewardUI`, `DailyRewardUI`, `UpgradeReadyMarkers`, `FirstSaleFx`, `TutorialUI`, `LanguageMenuUI`,
  `RatingPromptUI`, `SaleFx`, `StationScreenUI`, `Editor/UI/PortraitGameplayCleanup` and
  `Tests/EditMode/StationForemenTests`; CS0414 unused fields in `PortContractMarker`; CS0108 in
  `MobileDepthWater`; entries repeat because two compile passes logged; plus one
  `MCP-FOR-UNITY [WebSocket] Unexpected receive error` notice. The earlier "0 warnings" baseline was read
  before any compilation in that console session, so these are not new.
- A console filter on `MiningShop` across all message types returns **0 entries**.

**Test run.** `run_tests`, EditMode, `test_names = Game.Tests.MiningShopCampaignTests`,
job `14ac25b57b934c57a2066de91c75ec96`:

**14 total, 14 passed, 0 failed, 0 skipped — 1.25 s, resultState Passed.** Matches Codex's expected count.

| Case | Result |
| --- | --- |
| `AdditionalChaptersAndNonNumericStableIdsRequireNoCodeChange` | Passed |
| `AllBusinessesStartWithOneTableEvenWhenFourProductsAreAvailable` | Passed |
| `CatalogueDoesNotRetainMutableSourceArrays` | Passed |
| `ChapterBoundaryContinuesOverallDifficultyOrder(island-01-07 → island-02-01, 2, 7)` | Passed |
| `ChapterBoundaryContinuesOverallDifficultyOrder(island-02-06 → island-03-01, 3, 13)` | Passed |
| `ExampleContentHasSevenSixAndNineIslands` | Passed |
| `InvalidContentIsRejectedBeforeRegistration` | Passed |
| `InvalidProductIndexDoesNotAliasMerchandise(-1)` | Passed |
| `InvalidProductIndexDoesNotAliasMerchandise(4)` | Passed |
| `LastAuthoredBusinessDoesNotWrapOrInventAChapter` | Passed |
| `ProductUnlockOrderDoesNotReorderCaptainEquipment` | Passed |
| `UnknownBusinessDoesNotFallBackToTheFirstIsland(null)` | Passed |
| `UnknownBusinessDoesNotFallBackToTheFirstIsland("")` | Passed |
| `UnknownBusinessDoesNotFallBackToTheFirstIsland("missing")` | Passed |

After the run: 0 errors (one informational `Saving results to .../TestResults.xml` log), warnings unchanged.

Scope of this result: it verifies the catalogue's ordering, validation and ID contract only. It does not
verify any runtime registration, saving, economy, scene or presentation, because none exists yet.

**Repository state after the audit.** `git status` lists only untracked files: Codex's three sources and their
Unity-generated `.meta` files, `Docs/MAIN_SCREEN_MINING_SHOP_PLAN.md`, `Docs/MINING_SHOP_CODEX_HANDOFF.md` and
this document. No tracked file changed.

One side effect outside the repository, disclosed: entering and leaving Play from `Bootstrap` runs the game's
normal save-on-quit, so the live save at
`~/Library/Application Support/Intake Entertainment/Island Mining Tycoon/save.dat` was re-written by ordinary
gameplay (about 76 seconds of coal income). No field was edited by Claude.

## 7. Milestone status and next owner

The foundation/audit milestone is **verified at the level it claims**: 14/14 catalogue tests pass in Unity,
the recompile added no error or warning in the new files, and normal startup reaches `Main` with working
services and rising coins on this save.

It is **not** a green project: the environmental adb error persists and 64 pre-existing warnings remain.

**Target screen: `Main`, for existing and new players (settled; not asked again).** Evidence: the user's earlier
instructions "keep island (Main) as main view" and "island is main view flip the flag" (applied through
`GameBootstrap.SetUsePortraitShipyard(false)`), and `UPDATED_PORTRAIT_SHIPYARD_WORKLIST.md` lines 66–67. The
`SaveData.UsePortraitShipyard = true` fresh-install default is stale code, not a product decision; correcting it
is a separate coordinated change by the `SaveData` owner, with no save-version bump.

Background income for completed businesses stays a proposal until cross-island/offline work; it does not block
the single-business package 1 contract.

Not questions for the user (coordination with Codex, 2026-09-14):

- Legacy rows stay untouched and `mining-shop.island-01-01` starts fresh. This is already covered by the
  approved plan (section 10).
- A short market-side route near `Customer_Island_01` is the default implementation choice, subject to visual
  review. It is not an approval gate.

Package 0 closed at catalogue/audit scope. Next owner: Codex for package 1 pickaxe logic; then Claude for
package 2 (`Main` anchor authoring, visible pickaxe loop, Play-mode verification), once dispatched.

## 8. Package 2 scene-only prep in `Main` (2026-09-14, approved by Codex)

Authored through Unity MCP in `Assets/Scenes/Main.unity` and saved. Empty GameObjects (Transform only), at
scene root, not under the `Island_Shipyard` art. No C#, no components, no Inspector wiring, no Shipyard IDs, no
Play mode.

Ground: `Customer 1 | sand plaza` top is flat at **y = 33.58** (all 16 top vertices), plaza spans
x -553..-248, z -1489..-1231. Market walls occupy x -478..-324, z -1413..-1307; front door faces -Z at z -1415;
awnings z -1461..-1405, underside y ≈ 91 (porter ≈ 64 tall fits). Scene has 0 colliders, so views cannot
raycast for ground; use the anchor y.

Verified by re-reading the saved scene: `MiningShop_Anchors` roots = 1, 13 children; `MiningShop_Routes`
roots = 1, 1 route with 3 points. Each object has 1 component (Transform). Scene not dirty after save.

| Name | World position | Yaw |
| --- | --- | --- |
| `MiningShop_Anchors/Shop_Pickaxe_Work` | (-286, 33.58, -1330) | 180 |
| `Shop_Pickaxe_Worker` | (-286, 33.58, -1300) | 180 (faces the camera across the table) |
| `Shop_Pickaxe_Output` | (-286, 33.58, -1375) | 180 |
| `Shop_Pickaxe_Upgrade` | (-262, 33.58, -1350) | 180 |
| `Shop_Pickaxe_LockedPad` | (-286, 33.58, -1330) | 180 (same spot as Work) |
| `Shop_Market_Shelf` | (-365, 33.58, -1432) | 180 (under awning, east of the door) |
| `Shop_Customer_Queue_01` | (-365, 33.58, -1465) | 0 (faces shelf) |
| `Shop_Customer_Queue_02..04` | (-405/-445/-485, 33.58, -1470) | 90 |
| `Shop_Customer_Queue_05..06` | (-525, 33.58, -1445 / -1405) | 180 |
| `MiningShop_Routes/Carry_Pickaxe_to_Market/P00..P02` | (-286,-1375) → (-286,-1432) → (-340,-1432), y 33.58 | — |

Route measured: **111.0 units** (57 + 54), flat, no descent. Straight Work→Shelf distance 129.0. The carrier
ends beside the shelf (P02) so it does not stand in the customer line.

Findings for package 2:
- **Camera:** the edit-time `Main Camera` (pos (-116, 3552, -1999), pitch 58, fov 42, `CameraController`),
  projected at portrait aspect 0.5625, puts the market plaza just below the bottom edge (viewport y -0.02 to
  -0.07); the factory sits at centre (0.56, 0.51). Package 2 needs a camera stop or focus on the shop; checked
  only by projection, not Play mode.
- **Unrelated serialized changes came along with the save.** `git diff` shows no object removed (0 IDs only in
  HEAD; 36 new IDs = 18 GameObjects + 18 Transforms). But the save also wrote prefab-instance override values
  that were already in the Editor's loaded state: RectTransform anchors/positions/sizes, scale 0.82051283
  (= 1920/2340) and one TMP font size/colour on `UI_Ayarlar`, `UI_Kontrat`, `UI_Magaza`, `UI_Reklam`,
  `UI_HosGeldin`, `UI_GunlukOdul`, `UI_Teklif`, `UI_Harita`, `UI_IstasyonEkrani`. These look like
  editor/portrait layout re-serialization, not my edit. Per Codex (2026-09-14): kept intact and documented as
  outside the authored anchors; no revert.
- **`Assets/Art/Fonts/Baloo2-ExtraBold SDF.asset` origin:** the same save. Its modified time equals `Main.unity`'s
  (16:53:43), and the diff is only TextMeshPro dynamic-atlas glyph additions (`m_GlyphTable` / `m_CharacterTable`
  entries: metrics, atlas rects), i.e. glyphs rasterized while `Main` was open and written by the scene save's
  asset flush. Not a content edit by Codex or Claude. Left as is; no manual revert.

## 9. Package 1 Unity verification (2026-09-14)

Source frozen by Codex. Editor out of Play mode throughout. `refresh_unity` force + compile request; Editor
returned idle. Import check by reflection: `Game.Core.MiningShopState`, `Game.Core.MiningShopSimulation`
(Game.Core), `Game.Data.MiningShopConfig` (Game.Data), `Game.Systems.MiningShopService` (Game.Systems) and the
three test classes (Game.Tests.EditMode) all load. Unity generated the new `.meta` files.

Console after compile: **0 errors**. Warnings are the pre-existing CS0618/CS0414/CS0108 set (DailyRewardUI,
FirstSaleFx, AdRewardUI, LanguageMenuUI, RatingPromptUI, SaleFx, TutorialUI, UpgradeReadyMarkers,
StationScreenUI, PortContractMarker, MobileDepthWater, StationForemenTests, Editor PortraitGameplayCleanup);
**none mention a MiningShop file, MarketService or SaveData.** The adb/Unity Remote error from the package 0
baseline did not reappear in this read (it is environmental and may return). After tests: 0 errors.

`run_tests` EditMode, job `c60e8197f24545b8b1bab890c341f136`: **110 total, 110 passed, 0 failed, 0 skipped,
2.97 s.**

| Class | Expected | Passed |
| --- | --- | --- |
| `Game.Tests.MiningShopSimulationTests` | 15 | 15 |
| `Game.Tests.MiningShopServiceTests` | 13 | 13 |
| `Game.Tests.MiningShopCampaignTests` | 14 | 14 |
| `Game.Tests.MarketServiceTests` | — | 27 |
| `Game.Tests.SaveMigrationTests` | — | 11 |
| `Game.Tests.SaveServiceTests` | — | 2 |
| `Game.Tests.ShipyardFoundationTests` | — | 28 |

Scope of this claim: EditMode logic only. No Play mode, no startup registration (the feature is not opened on the
live save), no visual or device check. The wider suite (other classes, including previously failing
RosterCardPrefab/RenderingSafety tests owned elsewhere) was not run.

Next: Codex reviews these results and hands off package 2. Claude will not start package 2 code before that.

## 10. Package 2 — startup correction and registration boundary (recorded before editing)

Ownership (from Codex, package 2 only): `GameBootstrap` and the SaveData startup-selection fields move to Claude.
Codex keeps `MarketService`, `SaveData` payload, `MiningShopSimulation/State/Service`.

**Startup correction (fresh and existing saves both reach `Main`).** No change to `SaveData` defaults or the save
version. `GameBootstrap` gains `[SerializeField] private bool miningShopOnMain = true`. When on, right after the
save is loaded and migrated and before the portrait services are registered, it calls the existing
`ShipyardFeatureSwitch.Set(Data, false)`. That is the same supported switch the live save already went through;
it keeps the shipyard payload, so `ShipyardFoundationTests`' on/off/on preservation still holds. Result: fresh
install (default `UsePortraitShipyard = true`) and existing saves both load `Main`; CannonProduction /
ShipyardUnlocks are not registered (as on today's live save).

**Registration boundary.** Still in `Awake`, **after `GrantOffline()`** so an existing player's pre-update
absence is paid once from the legacy rate, `GameBootstrap` calls
`Market.OpenMiningShop(campaign, firstBusinessId, tuning, Save)` exactly once. Inputs:
`[SerializeField] MiningShopCampaignConfig miningShopCampaign`, `[SerializeField] MiningShopConfig miningShopConfig`
(both follow the file's existing rule: left empty they run on the class defaults via `CreateInstance`), and
`[SerializeField] string miningShopBusinessId = "mining-shop.island-01-01"`. After this call legacy offline rate
is zero (Codex's contract), so later launches pay no legacy offline income. If `OpenMiningShop` throws on a corrupt
or duplicate record, Bootstrap logs an error and continues with the shop closed; it never resets or rewrites the
record. With `miningShopOnMain = false` nothing above runs and startup is exactly today's.

Consequence to state plainly: shipping this switches every existing player's economy to the mining shop on their
next launch (legacy rows kept, legacy income stopped). That is the approved plan, not a side effect.

**Views.** New files only: `Gameplay/MiningShop/MiningShopView.cs` (reads `Market.MiningShop.View`, listens to
`MiningShopSold`, never ticks or pays) and `UI/MiningShopUpgradeUI.cs` (calls `TryBuyUpgrade`). Both are added
as components on the existing `MiningShop_Anchors` object via Unity tools and find anchors by child name, the way
`CoalOperation` resolves its stations — no Inspector references required for the loop. Props come from the
existing `Resources/Market` art; people reuse `CoalOperation`'s already-wired `workerPrefabs` through a read-only
getter. Camera: after `OperationCameraBoot.Framed`, one `CameraController.FrameTo` on the shop (the island pan
bounds are left alone so the player can still drag to the rest of the island).

**Test isolation.** Play-mode activation/reload checks run against a copy: the live `save.dat`/`.bak`/`.tmp` in
`persistentDataPath` are copied aside first and restored byte-for-byte afterwards; the result is verified by hash.

## 11. Package 2 result — first playable pickaxe business on `Main` (2026-09-14)

### Files

| File | Change |
| --- | --- |
| `Systems/GameBootstrap.cs` | `miningShopOnMain` (default true), `miningShopCampaign`, `miningShopConfig`, `miningShopBusinessId`; `ShipyardFeatureSwitch.Set(Data,false)` before portrait registration; `OpenMiningShop()` after `GrantOffline()`, refusal logged, record untouched |
| `Gameplay/MiningShop/MiningShopView.cs` (new) | Bench, rack, carrier, shelf, queue/serving/leaving customers drawn from `View`; `MiningShopSold` sends the served customer away. Fixed pools; no per-frame allocation |
| `UI/MiningShopUpgradeUI.cs` (new) | Tap bench → compact panel, speed/value via `TryBuyUpgrade`; one `CameraController.FrameTo` on the shop after `OperationCameraBoot.Framed` |
| `Gameplay/CoalOperation.cs` | One read-only getter `WorkerPrefabs` |
| `Resources/Diller/metinler.txt` | 5 rows × 11 languages (`maden_dukkani.*`) |
| `Scenes/Main.unity` (Unity tools) | Both components on `MiningShop_Anchors`; `pickaxeLength = 28`, `shopViewDistance = 1300` |

No change to `MarketService`, `SaveData`, simulation files, HUD or save version.

**Inspector wiring: none required.** Anchors are found by child name; people come from CoalOperation's already
wired `workerPrefabs`; props load from `Resources/Market`; both Bootstrap config fields are optional (empty = class
defaults, the file's existing rule). Optional, for the user: `GameBootstrap` (Bootstrap.unity) → `Mining Shop
Campaign` = a `MiningShopCampaignConfig` asset, `Mining Shop Config` = a `MiningShopConfig` asset, once tuning
assets are created. No `.asset` was created.

### Verification actually run

Compile: 0 errors; no console line mentions a MiningShop file. Console in Play: only the environmental Unity
Remote/adb error and URP "memoryless depth" info lines.

Tests: `run_tests` EditMode job `2e24010ef5584141ac1679df594ccbf7` — **118/118 passed** (the 110 package 1 set plus
8 `LocalizationTableTests`, which cover the new rows).

Save isolation: live `save.dat`, `.bak`, `.yedek` copied to scratch with SHA-256 before any Play; after testing
all three restored and `shasum -c` reported OK for each.

| Scenario (normal Bootstrap startup, out of an isolated save) | Observed |
| --- | --- |
| Fresh install (no save) | Loads `Main`; `UsePortraitShipyard` false; one row `mining-shop.island-01-01`; legacy rate 0. At t = 18.5 s: sold 1, earned 20, cash 20 (first sale within the 17 s model + frame slack) |
| Upgrade through the panel's Speed button | Cash 160 → 120 (one 40 spend); level 1 → 2; craft 10.000 → 9.091 s; craft fraction 0.4731 before and after; label → `SPEED LV. 2 / $48` |
| Stop + Play on that save (reload) | Resumed sold 12, produced 13, earned 240, speed 2; cash 200 = 240 − 40; no offline grant; legacy rate 0 |
| Drawn vs simulated | bench pickaxe on while crafting; rack 0/0, shelf 0/0, carrier cargo 1/1, waiting customers 6 bodies / 6 |
| Copy of the live save (existing player) | Loads `Main`; save version still 7; shop row created fresh; both legacy yards (`coal`, `copper`) kept with stock 0; cash 21.77M kept. Offline grant 0 — the live save's persisted `incomeRatePerSec` was already 0 (read from the backup copy), so there was nothing to pay |
| No legacy payout alongside the shop | cash 21,766,833.53 @ earned 100 → 21,766,973.53 @ 240 → 21,767,953.53 @ 1,220: every delta equals earned exactly |
| Ten-minute Play run (621 s) | sold 61, produced 62, inventory 1 = produced − sold, pending 0, legacy yard stock unchanged, ~61 fps in Editor, no new console errors |

Camera: at `shopViewDistance = 1300` the opening shot shows the whole market islet, bench, queue and the
factory above. Screenshots were taken through the camera, so they do **not** show HUD or panel; the panel's rect was
checked numerically only.

### Open issues (not fixed here)

1. **Customers outpace one crafter.** Arrival 3 s vs ~10 s per pickaxe: the queue sits at 6 all session. That is
   tuning (Codex / `MiningShopConfig`), not a view bug; no visible "sold out" state exists yet.
2. **Legacy CoalOperation still runs** on the factory above. Its deliveries are refused (Codex's contract) so its
   porters keep their loads; nothing pays, but they are visible motion unrelated to the shop. Hiding or retiring
   it needs a decision about the old island loop.
3. **Panel and HUD overlap not seen.** Needs a device or Game-view look by the user.
4. **Primitive pickaxe** (two shared tinted materials). A modelled mesh (e.g. KENNY `tool-pickaxe.fbx`) needs a
   serialized reference the user would wire.
5. `Captures/` (six verification PNGs at the repo root, untracked) could not be removed from here; safe to delete.
6. Earlier in this session the scene save also carried pre-existing UI prefab-override values and TMP atlas glyphs
   (section 8), still untouched.

Next owner: Codex to review package 2. No push.
