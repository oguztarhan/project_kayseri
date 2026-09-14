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
5. **Correction (2026-09-14):** an earlier note in this session called `Captures/` safe to delete. That was wrong.
   `Captures/` is an existing tracked folder of ≈220 project images that are not disposable. Only the untracked
   `round*`, `mining_shop_*` and `legacy_factory_area` PNGs were written by these checks; if anything is cleaned up,
   it is those files alone.
6. Earlier in this session the scene save also carried pre-existing UI prefab-override values and TMP atlas glyphs
   (section 8), still untouched.

Next owner: Codex to review package 2. No push.

## 12. Package 2 correction pass (Codex review, 2026-09-14)

### Changes

| File | Change |
| --- | --- |
| `Gameplay/CoalOperation.cs` | `_haulageOff` = shop open. After unlocks are applied, `HideHaulage()` switches off every rake (engine + wagons, train porters included) and every truck body (road porters included); `Tick` skips train/truck ticks, so no `Deliver` call and no refused load; `ApplyFleetStates` returns early. Island art, stations, crew, heaps, contract ship and all saved levels untouched |
| `Gameplay/MiningShop/MiningShopView.cs` | `ShopBounds` (bench, worker head, rack, shelf, queue, customer entry) replaces the single focus point |
| `UI/MiningShopUpgradeUI.cs` | Camera fits `ShopBounds` into the width between the HUD rails (`hudLeftFraction 0.15`, `hudRightFraction 0.12`, `shopFitMargin 1.15`, `shopScreenLift 0.08`) at the island camera's own pitch; buttons use `UiSkin.ButtonGreen/ButtonGrey` (with a skin wired, `UiBuild.Btn` leaves a flat white image and the white label disappeared) |
| `Scenes/Main.unity` (Unity tools) | Loop moved off the far side of the market onto the open plaza front so the roof no longer hides it: Work (-282,-1400), Worker (-258,-1400, facing the bench), Output (-282,-1445), Upgrade (-258,-1445), Shelf (-362,-1472), Queue_01..06 (-395,-1472) → (-533,-1422), route P00 (-282,-1445) → P01 (-282,-1474) → P02 (-335,-1474) = **82.0 units**. `customerExitOffset` (-5,0,54): served customers walk into the market door. Panel rect x 0.16–0.97, y 0.05–0.28 |

No MarketService, SaveData, simulation, HUD, tutorial or save-version change. The shop remains the only clock and payer.

### Verification

- Compile after each edit: 0 errors. Console in Play: only the Unity Remote/adb error and URP memoryless-depth info.
- `run_tests` EditMode job `4b9ab21e32174bfc80db8baa94b76172`: **118/118 passed** (MiningShop ×3, MarketService,
  SaveService, SaveMigration, ShipyardFoundation, LocalizationTable).
- Save isolation: the live save had been rewritten at 18:48–18:49 by an Editor Play session outside these tests
  (it held the shop at 2 sold, cash 21.77M). That state was backed up with new SHA-256s, used only as copies, and
  restored afterwards; `shasum -c` OK.

| Scenario | Observed |
| --- | --- |
| Fresh save | `Main`, shop open, `_haulageOff` true: rake engine inactive, wagons 0/1, trucks 0/2, active `_Porter` objects 0. Sold 2, cash 0 → 40 |
| Copy of the live save | v7, `tutorialStep` 100, shop resumed (sold 3 → 11), haulage off, 0 porters, legacy yards coal/copper stock 0 unchanged |
| No second payer | cash +100 while earned +100 (60 → 160); across Stop/Play cash +60 while earned +60 (160 → 220); legacy rate 0 |
| Five stages on screen (portrait 0.563, projected) | bench 0.77,0.60 · worker 0.83,0.60 · rack 0.78,0.55 · shelf 0.60,0.52 · queue head 0.52,0.52 · queue tail 0.20,0.57 — all between the rails and above the panel |
| Real Game-view capture (`ScreenCapture`, 1080×1920, HUD + open panel) | `Captures/round4_existing_hud_panel.png`: panel clear of the left rail, right rail and top bar; labels `SPEED LV. 1 / $40`, `VALUE LV. 1 / $40`, `10.0S · $20 · 11 SOLD` readable; loop bench → rack → carrier → shelf → customers visible together |

Earlier captures for comparison: `Captures/round2_hud_panel.png` (before: panel under the left rail, blank buttons,
legacy tutorial overlay), `Captures/round3_loop_moment.png` (camera render after the move).

### Still open (outside my files)

1. **Legacy onboarding on fresh saves.** `TutorialUI` starts whenever `tutorialStep < StepDone`; its dimmer and
   Foreman Max card cover the lower screen including the bench panel, and it narrates the old ore chain. Needs a
   decision (skip, or rewrite for the shop) by whoever owns TutorialUI/SaveData.
2. **HUD reads the legacy economy.** The rate pill shows `$0/min` (legacy meter) and the cash/gem pill text is dark on
   dark. HUD is not package 2's.
3. **Scale.** People are ~5% of screen height at this fit; enlarging needs a narrower loop or a closer camera that
   lets the queue tail leave the screen.
4. The fresh-save framing after the rail fit was checked by the same code path, not re-captured (the fresh-save
   capture is covered by the tutorial overlay).

## 13. Package 2 legibility pass (Codex blocking finding, 2026-09-14)

Finding: in `round4` the roof and the yellow props hid the loop; projected viewport coordinates were not proof of
visibility. Kept: HUD-safe panel and legacy-haulage suppression (still `_Porter` count 0 in this pass).

### Changes

| File | Change |
| --- | --- |
| `Gameplay/MiningShop/MiningShopView.cs` | Work table is now a plain dark-wood primitive table (`Table()`): every yellow market prop tried (bench, anvil) read as part of the building. Rack is `Resources/Market/Props/pallet` so goods sit visibly on top. Carried pickaxes over the shoulder: upright, tilted 30°, `carryScale 0.75` (flat they stuck out like a pole; full-size upright read as a pillar). Pickaxe primitives thicker; iron head colour |
| `UI/MiningShopUpgradeUI.cs` | Shop-specific camera `shopPitch 72` (island shot is 58); three small world-space signs **Craft / Stock / Sell** on one static canvas facing that camera, `signScale 0.22`, outlined, placed so moving goods and HUD buttons do not clip them |
| `Resources/Diller/metinler.txt` | `maden_dukkani.uret`, `.stok`, `.sat` in all 11 languages |
| `Scenes/Main.unity` (Unity tools) | Work/Worker (-282/-258, -1418); Output (-282,-1447); Shelf (-395,-1474); Queue_01..06 (-440,-1474) → (-535,-1378) along the front and up the open west strip; route P00 (-282,-1447) → P01 (-300,-1470) → P02 (-355,-1474) = **84.4 units**. View: `benchSize 44`, `rackSize 28`, `shelfSize 70`, `pickaxeLength 32`, head colour (0.30,0.32,0.36), `customerExitOffset` (35,0,58). UI: `shopFitMargin 1.0`, `hudRightFraction 0.2` |

### Evidence

Final real Game-view capture: **`Captures/round11_loop_hud_panel.png`** (1080×1920, `ScreenCapture`, HUD + open panel),
taken by a frame callback only when the simulation reported the carrier `ToMarket` at 0.55 of the leg with
cargo 1 (state at capture: crafting, rack 0, shelf 0, 6 waiting, sold 53). In the frame: CRAFT over the brown table
with the worker beside it → STOCK beside the pallet → the carrier between rack and shelf with a pickaxe on his
shoulder → SELL over the shelf → the queue along the front and west side. Panel, left rail, right buttons and top bar
do not overlap the loop or each other.

Honest limits: with one crafter feeding one carrier, the rack and shelf are empty almost all the time (the drawn
counts follow the simulation exactly, so no display stock is faked); the goods visible in the frame are the one being
carried and the one being crafted. People and pickaxes are still small (about 5% of screen height). Intermediate
attempts `round5`–`round10` in `Captures/` show the prop, sign and carry iterations.

Compile after every edit: 0 errors; Play console: only the environmental Unity Remote/adb error and URP
memoryless-depth info.

Tests after the pass: `run_tests` EditMode job `9df7e9e83c594fcea8bac2a0b26a9110` — **118/118 passed**, including
`LocalizationTableTests` over the three new rows. Play checks ran on a copy of the 18:49 live save; the live
`save.dat`/`.bak`/`.yedek` were restored afterwards and `shasum -c` reported OK for all three. No MarketService,
SaveData, simulation, HUD, tutorial or save-version change. Package 3 not started.

## 14. Package 3 four-product core — Unity verification (2026-09-14)

Scope: source-test milestone only. Edit Mode throughout (no Play), no scene or view change, no live save files
touched, no Codex source edited.

- `refresh_unity` force + compile request; Editor returned idle, not compiling, not playing.
- Import check by reflection: `Game.Core.MiningShopBusinessSimulation`, `MiningShopBusinessState`,
  `MiningShopProductLineState`, `MiningShopState` (Game.Core), `Game.Data.MiningShopConfig` (Game.Data),
  `Game.Tests.MiningShopBusinessSimulationTests` (Game.Tests.EditMode) all load.
- Console, read without clearing: **0 errors**. Warnings: 84 entries — 83 compiler warning lines that repeat across
  the Editor's recent compiles (CS0618/CS0414/CS0108 in AdRewardUI, DailyRewardUI, FirstSaleFx, UpgradeReadyMarkers,
  TutorialUI, LanguageMenuUI, RatingPromptUI, SaleFx, StationScreenUI, PortContractMarker, MobileDepthWater,
  StationForemenTests, Editor/PortraitGameplayCleanup) plus one MCP-for-Unity WebSocket notice. A MiningShop filter
  returns **0** errors or warnings. After the run: 0 errors (only the "Saving results to TestResults.xml" log).

`run_tests` EditMode, job `ea0615d7466c4753955fbdaeabc7a69a`: **100 total, 100 passed, 0 failed, 0 skipped, 2.88 s.**

| Class | Expected | Passed |
| --- | --- | --- |
| `Game.Tests.MiningShopBusinessSimulationTests` | 10 | 10 |
| `Game.Tests.MiningShopSimulationTests` | 15 | 15 |
| `Game.Tests.MiningShopServiceTests` | 13 | 13 |
| `Game.Tests.MiningShopCampaignTests` | 14 | 14 |
| `Game.Tests.MarketServiceTests` | — | 27 |
| `Game.Tests.SaveMigrationTests` | — | 11 |
| `Game.Tests.SaveServiceTests` | — | 2 |
| `Game.Tests.EditMode.LocalizationTableTests` | — | 8 |

New cases observed passing: four built lines share one carrier and one seller without starving the bag; Inspector
defaults match the four-product contract; legacy pickaxe record becomes line 0 without changing old fields; product
order/times/prices/table costs; reload keeps built tables, cargo and receipt identity without replaying sales; shared
seller capacity does not multiply with tables; tables unlock only in order and only when the island allows; two
products keep their own goods and prices through one receipt stream; unavailable/corrupt product records rejected
without clearing the save; per-product upgrade keeps its craft fraction and leaves other products unchanged.

Not claimed: no Play-mode, visual, startup-registration or device check — the live Main loop still runs the package 1
pickaxe simulation, as the Codex handoff states. `ShipyardFoundationTests` was not in this requested set. Package 4
scene work not started; waiting for Codex review.

## 15. Package 3 final service addition — Unity verification (2026-09-14): 3 FAILURES

Scope unchanged: Edit Mode only, no Play, no scene/view edits, no live save files, no Codex source edited.

- `refresh_unity` force + compile; Editor idle, not playing. Reflection: `Game.Systems.MiningShopBusinessService`,
  `Game.Core.MiningShopBusinessSimulation`, `Game.Tests.MiningShopBusinessServiceTests`,
  `Game.Tests.MiningShopBusinessSimulationTests` load; `MarketService.OpenMiningShopBusiness` exists.
- Console (not cleared): **0 errors**; MiningShop filter **0** errors/warnings. Warnings list: 66 entries — repeated
  pre-existing CS0618/CS0414/CS0108 lines outside MiningShop, one MCP WebSocket notice, two PerformanceTesting
  prebuild/cleanup lines from the test run.

`run_tests` EditMode job `d3fee3b8d8bb4251a8cb9e5d3e4a4a54` (MiningShopBusinessSimulation, MiningShopBusinessService,
MiningShopSimulation, MiningShopService, MiningShopCampaign, MarketService, SaveService, SaveMigration,
LocalizationTable): **104 total, 101 passed, 3 failed.** Re-run of `MiningShopBusinessServiceTests` alone (job
`da2258c058f941589a2cc0a2187af69c`): **4 total, 1 passed, 3 failed** — same three. The other 100 are the set that
passed 100/100 in section 14 (the tool returns no per-class breakdown for a failed job).

| Failing test | Unity message |
| --- | --- |
| `BusinessAndPickaxeOnlyServicesCannotRunTogether` | `Expected: <System.InvalidOperationException>  But was: null` |
| `MarketCreditsEachProductReceiptOnceThroughTheSharedBusinessPayer` | `Expected: 840.0d  But was: 839.99999999999989d` |
| `TablesSpendOnceInOrderAndPersistInTheBusinessRecord` | `Expected: 8300.0d  But was: 8299.9999999999982d` |

Passing: `EncryptedSaveRestoresBuiltLinesWithoutReplayingReceipts`.

Read-only observations for Codex (not verified by a fix, not edited):
1. Mutual exclusion: `OpenMiningShopBusiness` refuses when `_miningShop != null` (MarketService line ~146), but
   `OpenMiningShop` (line ~99) checks only `_miningShop`, not an already-bound business — consistent with the first
   `Assert.Throws` (open business, then pickaxe-only) receiving no exception. The stack trace was not returned, so
   which of the two `Assert.Throws` failed is inferred, not observed.
2. The two money failures are exact `Is.EqualTo` on `WalletService.Cash.ToDouble()` after `BigDouble` arithmetic
   (10000 − 300 − 1400; receipts summing to 840). The results differ in the last binary digits only, so either the
   assertions need a tolerance or the wallet path needs exact-integer handling — Codex's call.

Package 3 is not verified. No package 4 work started.

## 16. Package 3 correction — Unity re-verification (2026-09-14): PASSED

Codex correction (read on disk, not edited): `MarketService.OpenMiningShop` now throws when a four-product business
is already bound; the two wallet assertions in `MiningShopBusinessServiceTests` use `.Within(1e-6)`. Edit Mode only,
no Play, no code/scene/save edits by Claude.

- `refresh_unity` force + compile; Editor idle, not compiling, not playing; `MiningShopBusinessService` loads.
- Console (not cleared): **0 errors**; MiningShop filter **0** errors/warnings. After both runs: 0 errors (only the
  "Saving results to TestResults.xml" log). Pre-existing warnings outside MiningShop unchanged from section 15.

`run_tests` EditMode, full requested set, job `76f58437b8c94eef858bef1169020258`: **104 total, 104 passed, 0 failed,
0 skipped, 2.88 s.**

| Class | Expected | Passed |
| --- | --- | --- |
| `Game.Tests.MiningShopBusinessSimulationTests` | 10 | 10 |
| `Game.Tests.MiningShopBusinessServiceTests` | 4 | 4 |
| `Game.Tests.MiningShopSimulationTests` | 15 | 15 |
| `Game.Tests.MiningShopServiceTests` | 13 | 13 |
| `Game.Tests.MiningShopCampaignTests` | 14 | 14 |
| `Game.Tests.MarketServiceTests` | — | 27 |
| `Game.Tests.SaveMigrationTests` | — | 11 |
| `Game.Tests.SaveServiceTests` | — | 2 |
| `Game.Tests.EditMode.LocalizationTableTests` | — | 8 |

Separate re-run of `MiningShopBusinessServiceTests` alone, job `8beed394ef5a4edd9d9007f183008e04`: **4/4 passed**,
including `BusinessAndPickaxeOnlyServicesCannotRunTogether`,
`MarketCreditsEachProductReceiptOnceThroughTheSharedBusinessPayer`,
`TablesSpendOnceInOrderAndPersistInTheBusinessRecord` and `EncryptedSaveRestoresBuiltLinesWithoutReplayingReceipts`.

Scope of the claim: EditMode logic and service settlement only. Main still runs the package 1/2 pickaxe loop;
no Play-mode, visual, startup or device check of the four-product business. Package 4 not started; waiting for
Codex review and handoff.

## 17. Package 4 — Main on the four-product business (2026-09-14): BLOCKED on old-save migration

### Changes (Claude-owned files only)

| File | Change |
| --- | --- |
| `Systems/GameBootstrap.cs` | `OpenMiningShop()` now calls `Market.OpenMiningShopBusiness(campaign, miningShopBusinessId, config.ToBusinessTuning(), Save)`; default business still `mining-shop.island-01-01` (1 product available). Refusal still logged, record untouched |
| `Gameplay/CoalOperation.cs` | `_haulageOff` true when either `MiningShop` or `MiningShopBusiness` is open |
| `Gameplay/MiningShop/MiningShopView.cs` | Rewritten over `MiningShopBusinessService.View`: one `Line` per product (table, pallet rack, craft item, rack/shelf/cargo items, locked pad, route, optional worker). A line not offered draws nothing; offered + unbuilt shows only its pad; built shows table/rack/goods. One shared carrier walks the route of `CarrierProductIndex` and shows `CarrierCount` of that product; one shared queue from `WaitingCustomerCount`; the served customer holds the `ServiceProductIndex` item; `MiningShopBusinessSold` sends them away. Shelf split into four product columns (`shelfItemScale 0.55`). Primitive helmet/lantern/bag items. No stock or payout invented |
| `UI/MiningShopUpgradeUI.cs` | Tap any built bench → that product's panel (title, `CraftSeconds/UnitPrice/Sold`, speed/value via `TryBuyUpgrade(index, speed)`); tap a locked pad → one `BUILD BENCH $cost` button via `TryBuildTable(index)`, enabled only for the next bench in order when affordable and no pending time |
| `Resources/Diller/metinler.txt` | `maden_dukkani.kask_tezgahi`, `fener_tezgahi`, `canta_tezgahi`, `tezgah_kur` in all 11 languages |
| `Scenes/Main.unity` (Unity tools) | New anchors `Shop_Helmet/Lantern/Bag_Work`, `_Output`, `_LockedPad`: helmet (-292,-1386)/(-268,-1386), lantern (-292,-1356)/(-268,-1356) in the east strip; bag (-524,-1356)/(-500,-1356) in the west strip. Routes `Carry_Helmet_to_Market` 158.2, `Carry_Lantern_to_Market` 190.7, `Carry_Bag_to_Market` 174.2 units. Queue re-laid inside the octagonal plaza (Q01 (-440,-1474) … Q06 (-534,-1387)); `customerEntryOffset` (-10,0,8). Pickaxe line, signs, panel rect and camera unchanged |

### API/view assumptions recorded

- `ProductSnapshot` is a live view over the line record, not a copy: values read before a mutation change after it
  (a "before" read of `SpeedLevel` showed 2 after the purchase). Views read it fresh each frame, which is fine; any
  before/after comparison must copy the numbers first.
- When the carrier returns to `Idle`, `CarrierProductIndex` becomes −1; the view keeps it where its last route
  started, so the next pickup at a different rack starts with a short jump.
- All four routes share `TravelSeconds`, so the carrier walks the longer helmet/lantern/bag routes faster (stride
  rate follows).
- `extraBenchScale 0.7`: the islet is an octagon (top vertices (-509,-1451) (-401,-1489) (-293,-1451) (-249,-1360)
  (-293,-1269) (-401,-1231) (-509,-1269) (-554,-1360)) mostly covered by the market roof; the three extra lines are
  squeezed into the two side strips. They have no worker (no room).

### Verification

Compile after edits: 0 errors. Tests: `run_tests` EditMode job `c54b3d39934a4efda2684b5e4e723dc5` — **132/132 passed**
(MiningShopBusinessSimulation 10, MiningShopBusinessService 4, MiningShopSimulation 15, MiningShopService 13,
MiningShopCampaign 14, MarketService 27, SaveService 2, SaveMigration 11, ShipyardFoundation 28, LocalizationTable 8).
Post-run console: 0 errors.

Save isolation: the live save still matched the 18:49 backup (`shasum -c` OK) before testing; every Play used a
moved-aside fresh save or a copy; the live `save.dat`/`.bak`/`.yedek` were restored afterwards and `shasum -c`
reported OK for all three.

| Scenario | Observed |
| --- | --- |
| Fresh 1-1 (no save) | `MiningShopBusiness` open, `MiningShop` null; `mining-shop.island-01-01`, available 1, built 1; helmet/lantern/bag tables and pads 0 active; `_haulageOff` true, 0 porters. Sales 20 each (sold 3 → earned 60). Panel speed button `SPEED LV. 1 / $40`: cash 60 → 20 (one spend), craft 10.000 → 9.091 s, craft fraction 0.7884 preserved; later cash 40 = earned 80 − 40. Capture `Captures/p4_fresh_1-1_hud_panel.png` shows the pickaxe loop only (plus the known legacy tutorial overlay) |
| **Existing pickaxe save (copy of 18:49 live save)** | **FAILS.** Console `[MiningShop] not opened: Mining-shop business save has invalid shared jobs; refusing to reset it.` `MiningShopBusiness` null, view disabled, `_haulageOff` false → 3 legacy porters visible, no shop income. Flat record untouched (sold 2, earned 40), cash 21.77M. Cause (read-only): `SaveService`/JsonUtility deserializes `MiningShopState.Business` (a `[Serializable]` class field, `MiningShopState.cs` line 37) as a non-null empty object — Lines 0, available 1 — for a record saved before package 3. `MiningShopBusinessSimulation.EnsureBusinessState` returns early on `owner.Business != null` (line 499), so the one-time pickaxe copy never runs and `ValidateState` throws. Codex-owned; reported, not edited |
| 1-4 test path (fresh isolated save; Bootstrap `miningShopBusinessId` temporarily `mining-shop.island-01-04`) | Available 4, built 1; helmet/lantern/bag pads visible, tables hidden (`p4_1-4_pads_unbuilt.png`). Isolated test cash +6,700. Panel: lantern before helmet → button disabled, wallet unchanged; helmet 6740 → 6440; lantern → 5040; bag → 40; built 4. At `Time.timeScale` 6 (reset to 1 afterwards) the one carrier carried helmet, pickaxe, lantern, bag in turn (`CarrierCount` 1) and the one seller served each (`p4_1-4_carry_0..3.png`, `p4_1-4_serve_0..3.png`); cash equalled the summed line earnings at every capture (550, 610, 760, 1,120) |

Bootstrap revert: `miningShopBusinessId` set back to `mining-shop.island-01-01` and saved. The file is **not**
byte-identical to before: the save serialized the package-2 fields for the first time (`miningShopOnMain: 1`,
`miningShopCampaign/Config: {fileID: 0}`, `miningShopBusinessId: mining-shop.island-01-01`) — the same values the
code defaults already supplied, so startup behaviour is unchanged. Left as is (no hand edit).

### Honest limits / open

1. **Blocker:** old-save migration above. Until fixed, shipping this switches every existing player who already had
   the pickaxe shop to "shop not opened". Do not ship package 4 before Codex's fix and a re-run of the old-save check.
2. 1-4 legibility is weak: the extra lines are small and crowded beside the pickaxe line and the queue; the pickaxe
   Craft/Stock signs sit over the helmet line; the fresh-save captures are dimmed by the legacy tutorial.
3. Helmet/lantern/bag have no worker; items are primitives.
4. No new package-4 EditMode test was added; verification is the Play scenarios above.

Next: Codex fixes migration → Claude re-runs the old-save copy check. No package 5 work started. No push.

## 18. Package 4 re-verification after Codex's migration fix (2026-09-14): PASSED

Codex fix read on disk (not edited): `EnsureBusinessState` returns an existing business only when
`Business != null && Lines != null && Lines.Count > 0`; otherwise it copies the flat pickaxe record once. New test
`MiningShopBusinessServiceTests.PreBusinessFlatPickaxeRecordMigratesAfterSaveServiceRoundTrip`.

- `refresh_unity` force + compile: **0 errors**, MiningShop filter 0 errors/warnings.
- `run_tests` EditMode job `c88c82f609374b69a72e87d8a3fe3d04` (MiningShopBusinessSimulation, MiningShopBusinessService,
  MiningShopSimulation, MiningShopService, MiningShopCampaign, MarketService, SaveService, SaveMigration,
  ShipyardFoundation, LocalizationTable): **133 total, 133 passed, 0 failed, 0 skipped, 3.27 s** (section 17's 132 plus
  the new migration test).

Process note: the first Play attempt after the fix ran with neither `Bootstrap` nor `Main` loaded (no GameBootstrap,
no services) because the Bootstrap scene I had opened additively for the 1-4 check was still listed, unloaded, in
the hierarchy. That run proved nothing and wrote nothing (save copy hashes unchanged). The entry was removed
(`close_scene remove_scene`), hierarchy confirmed to be `Main` only, and the checks below were run again.

| Scenario (isolated save) | Observed |
| --- | --- |
| Existing pickaxe-only save (copy of the 18:49 live save: flat sold 2, produced 3, earned 40, carrier ToMarket cargo 1, cash 21.77M, v7) | No console error. `MiningShopBusiness` open, `MiningShop` null, 1 business row, available 1, built 1; helmet/lantern/bag unbuilt. Line 0 continued from the copied state: at t = 10 s receipts 3, sold 3, produced 4, earned 60. Flat record unchanged (sold 2 / produced 3 / earned 40 / ToMarket / cargo 1). `_haulageOff` true, 0 porters, view enabled. Offline grant none (rate 0) |
| No duplicate payment after migration | cash 21,766,793.53 @ earned 60 → 21,766,913.53 @ earned 180: **+120 cash for +120 earned**; receipts 9 = sold 9; flat record still sold 2 |
| Resumed old-save capture | `Captures/p4_oldsave_resumed_hud.png` (1080×1920, HUD): pickaxe loop only, served customer leaving with a pickaxe, no tutorial (step already done) |
| Fresh 1-1 (no save) | Business open, available 1, helmet/lantern/bag tables and pads 0 active, haulage off, 0 porters. First sale at t ≈ 23 s: sold 1, earned 20, cash 20, receipts 1. Capture `Captures/p4b_fresh_1-1_hud.png`: pickaxe loop with the carrier mid-route; legacy tutorial overlay present (tutorialStep 0) |

Live save restored from the 18:49 backup; `shasum -c` OK for `save.dat`, `.bak`, `.yedek`. Editor left in Edit Mode,
`Time.timeScale` 1, only `Main` loaded.

### Package 4 visual limitations (for choosing the next focused package)

1. **1-4 crowding.** With all four lines built the three extra benches (scale 0.7, no workers) squeeze into the two
   narrow strips beside the market roof; the helmet line sits under the pickaxe's CRAFT/STOCK signs; the queue and
   bag route share the west strip. Readable as "four lines exist", not as four clear loops.
2. **Carrier routes differ in length (82–191 units) with one `TravelSeconds`,** so the carrier visibly speeds up on
   the helmet/lantern/bag routes; after `Returning` it waits at the last line's start, so switching lines begins with
   a short jump.
3. **Signs are pickaxe-only** (Craft/Stock/Sell); no product labels on the other lines.
4. **Primitive goods and tables** for all four products; no workers for helmet/lantern/bag.
5. **Legacy tutorial** dims and covers the bench panel on every fresh save.
6. **HUD still shows legacy values** (`$0/min`, dark-on-dark cash text).
7. **People and goods are small** (~5% of screen height) at the current fit.
8. `Bootstrap.unity` carries the four serialized package-2 fields at default values (section 17).

Candidate next focused packages: (a) a larger or relocated stage for 1-2…1-4 so each line gets its own readable
table→rack→route; (b) tutorial/HUD alignment with the shop economy; (c) modelled product meshes and per-line workers.
No package 5 work started. No push.

## 19. Package 4.1 — fresh-player readability (2026-09-14)

Ownership used: tutorial presentation, HUD, localization. No change to Core, MiningShopBusinessService, MarketService,
campaign/progression, save/migration, GameBootstrap, scene layout, tuning or art.

### Changes

| File | Change |
| --- | --- |
| `UI/TutorialUI.cs` | Caches `MarketService` in `Start`; `Update` returns early while `MiningShop` or `MiningShopBusiness` is open (`ShopActive`). The ore tour never starts and its one-shot tips (`TipTick`) never fire while the shop owns Main. `SaveData.tutorialStep` is never written here, so the tour resumes where it was if the shop is ever switched off |
| `UI/HudUI.cs` | `[SerializeField] counterTextColor` (white) applied in `Start` to the cash, gem and rate labels (dark-on-dark before). While `MiningShopBusiness` is open the rate pill shows `maden_dukkani.hud_durum` = "{sold} SOLD · ${recent}/min": sold = Σ `View.ProductAt(p).Sold` over available lines; recent = sum of `MiningShopBusinessSold` receipt `Cash` in the last `shopRateWindow` (60 s, `Time.time`) × 60 / window. Receipts are only recorded (fixed 64-entry ring, no allocation per sale); nothing ticks or pays. The label auto-sizes (max = authored 36, min = 18) only in shop mode. Legacy ore `$ /min` path unchanged when the shop is off. Unsubscribes in `OnDestroy` |
| `Resources/Diller/metinler.txt` | `maden_dukkani.hud_durum` in all 11 languages |

### API / UI assumptions

- `MarketService.MiningShopBusiness` is non-null from Bootstrap `Awake`, before Main's `Start`, so `TutorialUI` and
  `HudUI` read a settled value once; neither handles the shop opening mid-session (it does not today).
- `MiningShopBusinessSold` fires after the wallet was credited (Codex contract), so the HUD cash roll and the rate
  pill agree on the same receipt.
- The rate window uses scaled `Time.time`, matching `Market.Tick`'s scaled `deltaTime`; it starts empty after a load
  (receipts before this session are not replayed), so a returning save shows its lifetime sold count with a
  this-session rate.
- `ProductSnapshot` is read live each refresh (4 Hz); the pill allocates one string per refresh, as the legacy pill did.
- Tapping the rate pill still opens the legacy ore income breakdown (`OnRate`); not changed in this package.
- Contract offers are still sized from `IncomePerMinute()` (ore meter, now 0); not changed.

### Verification (isolated saves; Play started from Bootstrap)

Compile after each edit: 0 errors. Tests: `run_tests` EditMode job `4314f9307da541a7b8c4df1dea0a7583` — **133/133**
(MiningShop ×5, MarketService, SaveService, SaveMigration, ShipyardFoundation incl. `CompactHudMovesLegacyExtraOpenersIntoMoreSheet`,
LocalizationTable); job `c5c46a8c12f74542acf7b42ca36e67a5` — `LadderUiSmokeTests` + `PetRosterUiSmokeTests` (the other
tests touching HudUI) **14/14**.

| Scenario | Observed |
| --- | --- |
| Fresh 1-1, start | Tutorial `IsShowing` false, `_running` false, `tutorialStep` 0 (unchanged). HUD cash '0', gems '0', rate '0 SOLD · $0/min', all white |
| First sale | t = 22.3 s: sold 1, earned 20, cash 20; HUD gold '20', rate '1 SOLD · $20/min'; tutorial still hidden, step 0. Capture `Captures/p41_fresh_first_sale.png` |
| Speed upgrade via panel | Button 'SPEED LV. 1 / $40' interactable; cash 40 → 0 (one spend); speed 1 → 2; craft 10.000 → 9.091 s; craft fraction 0.7887 → 0.7887; HUD '2 SOLD · $40/min'. Capture `Captures/p41_fresh_panel_after_upgrade.png` (panel clear of both rails, no tutorial) |
| Pill overflow | Before auto-size the status text measured 287 px in a 222 px label and spilled past the pill (visible in the two captures above). After: auto-size on, font 29.2, text width 222 = label width |
| **Final fresh Main** | `Captures/p41_final_fresh_main.png`: sold 2, cash 40, gold '40', rate '2 SOLD · $40/min' inside its pill, no tutorial, Craft/Stock/Sell loop and carrier mid-route clear of the rails |
| Returning migrated pickaxe save (copy of 18:49 save) | Business open, sold 3 / earned 60 / receipts 3 (flat record still 2 / 40); `tutorialStep` 100, tutorial and tips not showing; HUD gold '21.77M' white, rate '3 SOLD · $20/min'; `_haulageOff` true, 0 porters |
| Returning save, later | t = 39.3 s: sold 4 → 6, earned 80 → 120 (+40), **cash +40.00** (no duplicate), receipts 6; HUD '6 SOLD · $80/min'; tutorial hidden, step 100; 0 porters; flat record sold 2. Capture `Captures/p41_returning_hud.png`: cash '21.77M' and gems '80' legible, status inside its pill, pickaxe loop clear of the rails |

Live save restored from the 18:49 backup after all runs; `shasum -c` OK for `save.dat`, `.bak`, `.yedek`. Editor left in
Edit Mode with only `Main` loaded.

### Remaining UI limits (not in this package)

1. Rate pill tap still opens the legacy ore breakdown.
2. Contract offers are sized from the ore meter (0 while the shop is active).
3. The ore tutorial is paused, not replaced: a fresh player gets no shop onboarding yet.
4. The status pill's font auto-shrinks as the numbers grow (29 px at "2 SOLD · $40/min"); very large counts will get
   small before the pill needs a wider art slot.
5. Package 4 visual limits (section 18) otherwise unchanged.

No package 5 work. No push.
