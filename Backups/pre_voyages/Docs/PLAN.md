# Ore Empire *(working title)* — Implementation Plan

Companion to [GDD.md](GDD.md). This is the **engineering roadmap**: how we turn the design into sequenced, buildable, testable work. Section refs like *(GDD §5)* point back to the design doc.

---

## 0. Guiding principles for the build

1. **Data-driven from line one.** Every tunable is a ScriptableObject / `[SerializeField]` field (GDD §14, §16). No magic numbers in code.
2. **Performance is a gate, not a pass.** The 16.6 ms budget and zero-alloc rules (GDD §14.5) apply from M0. Sim runs on a fixed low-Hz tick, decoupled from render.
3. **Facade-first, SDK-late.** We build our own service *interfaces* early with local/stub implementations. Heavy external SDKs (Firebase, ads, IAP, Play Games) are installed **only at the milestone they go live** — so M0–M4 need **zero external dependencies**.
4. **Greybox now, Blender art later.** Every visual is a swappable prefab/mesh reference. Custom low-poly meshes are authored in **Blender (via Blender MCP)** per [ASSETS.md](ASSETS.md) and dropped into the reference slot with **no code change** — so art can be produced **in parallel** any time from M2 onward, independent of the code milestones.
5. **One task at a time; each milestone ends playable + tunable + console-clean + 60 fps.**
6. **How things get created:** C# scripts → written directly (`.cs` is allowed). ScriptableObject assets, prefabs, scenes → created via Unity MCP tools or by you in the editor (never hand-edited `.asset`/`.prefab`/`.meta`, per CLAUDE.md).
7. **Verify, don't assume.** After each C# change: recompile, read the Unity console, run the relevant Test Runner tests before calling anything done.

---

## 1. Project scaffolding (one-time, start of M0)

**Folders** (GDD §14):
```
Assets/
  Scripts/  Core/ · Data/ · Systems/ · Gameplay/ · UI/ · Tests/
  Data/     (SO assets: EconomyConfig, OreTiers/, Recipes/, …)
  Prefabs/  Stations/ · Vehicles/ · Ore/ · VFX/ · UI/
  Scenes/   Bootstrap · Main
  Art/ · Audio/ · Settings/
```

**Assembly definitions** (compile speed + enforced dependencies):
- `Game.Core` (no deps) → `Game.Data` → `Game.Systems` → `Game.Gameplay` → `Game.UI`
- `Game.Tests` (EditMode + PlayMode) references all.

**Scenes:** `Bootstrap` (initializes services, `DontDestroyOnLoad`) → loads `Main` (the map).

---

## 2. Core class map (target architecture)

**Core** — `BigDouble` (mantissa+exponent), `NumberFormatter`, `GameClock` (fixed-Hz tick event), `ServiceLocator`, `ObjectPool<T>`.

**Data (ScriptableObjects)** — `EconomyConfig`, `OreTier`, `Recipe`, `MountainDefinition`, `StationConfig`/`UpgradeCurve`, `ManagerConfig`, `OfflineConfig`, `PrestigeConfig`, `ContractDefinition` + `ContractConfig`, `MonetizationConfig`, `AudioLibrary` + `AudioConfig`, `VFXLibrary`, `UITheme`, `QualityConfig`, `AccessibilityConfig`, `JuiceConfig` + `HapticConfig`, `NotificationConfig`, `AnalyticsConfig`, `LocalizationConfig`, `ReviewConfig`, `SocialConfig`, `DailyRewardConfig`, `AchievementDefinition`.

**Systems (services, behind interfaces)** — `SaveService`, `CloudSaveService`, `WalletService` (currencies), `EconomyService` (costs/income), `AudioService`, `VFXService`, `AdService`, `IAPService`, `AnalyticsService`, `RemoteConfigService`, `ConsentService`, `NotificationService`, `TimeService` (trusted time), `QualityService`.

**Gameplay** — `MineStation`, `RailLine` + `Train`, `StorageYard`, `OreTruckFleet`, `Refinery`, `CargoTruckFleet`, `Market`, `Vehicle` (pooled mover), `OreVisual` (pooled), `MountainManager`, `PrestigeManager`, `ContractManager`, `ManagerSystem` (automation). *(A shared `StationBase` is extracted in M2 only once M1 proves the duplication — no premature abstraction, per CLAUDE.md.)*

**UI** — `HUDController`, `CurrencyBar`, `UpgradePanel`, `ContractPanel`, `FloatingTextPool`, `SettingsPanel`, `OfflineWelcomePanel`.

**Save model** — `SaveData` POCO (version, timestamp, wallet, per-station state, unlocks, prestige, settings) → JSON → AES-encrypt + checksum.

---

## 3. Milestones

Each milestone: **Goal → Tasks → New configs → Packages (approval) → Definition of Done.** Sizes are relative effort (S/M/L/XL), not time.

### M0 — Foundation · **L** · no external packages
**Goal:** testable bedrock, no gameplay yet.
- [ ] Scaffolding: folders, asmdefs, Bootstrap + Main scenes.
- [ ] `BigDouble` struct: normalize, +−×÷, compare, `FromDouble`/`ToDouble`. **EditMode tests.**
- [ ] `NumberFormatter`: `1.5K / 2.3M / 4.1aa` notation, config-driven. **EditMode tests.**
- [ ] `GameClock`: fixed-Hz tick (default 8 Hz) decoupled from frame; pause/resume.
- [ ] `SaveData` + `SaveService`: JSON, AES-encrypt, checksum, versioned, `persistentDataPath`, autosave + on-pause. **EditMode tests (round-trip + tamper rejection).**
- [ ] `ServiceLocator` + `GameBootstrap` (init order, DontDestroyOnLoad).
- [ ] Stub services: `AnalyticsService` (no-op log), `RemoteConfigService` (returns local SO defaults), `ConsentService` (assume granted in dev).
- [ ] `EconomyConfig` SO (currencies, `growth`, tier multiplier defaults).
- **DoD:** all EditMode tests green; Bootstrap runs; console clean.

### M1 — Core spine · **XL** · no external packages
**Goal:** playable loop — one mountain → tap-to-mine → train → storage → market → cash + basic upgrades *(trucks/refinery deferred to M2, per GDD §15)*.
- [ ] `OreTier` SO (Coal). Greybox prefabs saved (Mine, Storage, Market, Train, OreVisual) from the demo primitives.
- [ ] `MineStation`: tap to produce ore into buffer (auto later); rate/capacity from config.
- [ ] `RailLine` + `Train`: waypoint mover, capacity, load at mine → unload at storage → return; pooled visuals; **interpolated between sim ticks.**
- [ ] `StorageYard`: raw-ore buffer with capacity.
- [ ] `Market`: consume from storage → cash at price, over time.
- [ ] `WalletService` + `EconomyService`: cash (BigDouble), upgrade cost curve (GDD §5).
- [ ] `HUDController` + `CurrencyBar` + `UpgradePanel` (mine speed, train capacity, market price).
- [ ] `FloatingTextPool` (pooled cash popups).
- [ ] Wire M1 state into `SaveData`.
- **DoD:** tap→ore→cash flows; upgrades raise throughput; save persists; 60 fps; console clean; a **bottleneck is visible** (ore stacks when a stage lags).

### M2 — Full chain · **L**
**Goal:** complete the 7-station chain with refining.
- [ ] `Product` + `Recipe` SOs; first **combine** recipe (Steel = Iron + Coal, GDD §4).
- [ ] `OreTruckFleet` (Storage→Refinery) and `CargoTruckFleet` (Refinery→Market) — pooled vehicles.
- [ ] `Refinery`: consume ore per recipe → produce product; process rate + output buffer.
- [ ] Add Copper/Iron ore tiers; extend upgrades to all 7 stations.
- [ ] **Extract `StationBase`** if duplication is now real (buffer/level/upgrade).
- [ ] Bottleneck visuals across all stages.
- **DoD:** ore→product→cash end-to-end; combine recipe forces two-stream balancing; 60 fps with full pipeline; console clean.

### M3 — Idle layer · **L**
**Goal:** the game plays itself and rewards absence.
- [ ] `ManagerSystem` + `ManagerConfig`: automate each station + bonus (GDD §6).
- [ ] Offline earnings: `OfflineConfig`, timestamp accrual, cap, welcome-back popup, 2×/collect via (stubbed) ad hook (GDD §7).
- [ ] `TimeService` (trusted time) + save integrity/anti clock-cheat; `CloudSaveService` interface (local impl now, Play Games later).
- **DoD:** managers automate; offline grant correct within cap; clock-rollback denied; 60 fps.

### M4 — Meta progression · **L**
**Goal:** long-term depth.
- [ ] `PrestigeManager` + `PrestigeConfig`: reset, Investors = `k·√lifetimeCash`, global multiplier, prestige upgrade tree (GDD §8).
- [ ] `MountainManager` + `MountainDefinition`: unlock a 2nd mountain/biome on the shared map; more ore tiers + recipes (incl. a premium combine).
- **DoD:** first prestige reachable + satisfying; 2nd mountain integrates into the shared network; economy re-balanced.

### M5 — Engagement & monetization · **XL** · installs external SDKs (approval)
**Goal:** the live-service and money layer.
- [ ] `ContractManager` + `ContractDefinition`/`ContractConfig` (GDD §9).
- [ ] Daily rewards, achievements/milestones (GDD §11).
- [ ] **Ads** live: mediation (LevelPlay/AdMob) behind `AdService`; rewarded placements from `MonetizationConfig`.
- [ ] **IAP** live: `IAPService` (Unity Purchasing); Starter Pack, gem packs, remove-ads, permanent 2×.
- [ ] **Analytics** live (Firebase) + **Remote Config** live + **Consent** (UMP) + COPPA age-gate.
- [ ] **Push notifications** live (`NotificationService`).
- [ ] In-app review prompt; leaderboards/achievements (Play Games).
- **DoD:** contracts loop; a rewarded ad and a test IAP complete; analytics events fire post-consent; notifications schedule; nothing fires before consent.

### M6 — Polish & ship · **XL** · ProBuilder + art/localization packages (approval)
**Goal:** ship-quality.
- [ ] **Art pass:** swap greybox → **Blender-authored low-poly meshes per [ASSETS.md](ASSETS.md)**, recolor kit (GDD §13). No code change — reference swaps only. *(Assets may already be done from the parallel art track — this milestone just finalizes the swap + polish.)*
- [ ] Audio (`AudioLibrary`), VFX (`VFXLibrary`), juice + haptics (`JuiceConfig`/`HapticConfig`).
- [ ] Accessibility (colorblind palette + icons, text scale, reduce-motion) — palette already enforced from M1.
- [ ] Localization: 10 launch languages incl. **Turkish** (string tables filled).
- [ ] Adaptive quality tiers (`QualityService`/`QualityConfig`); **profile on a real low-end device.**
- [ ] Store assets, closed → open testing, release.
- **DoD:** 60 fps verified on reference low-end device; passes Play Data Safety; localized; store-ready.

---

## 4. Dependency / sequencing summary

```
M0 Foundation ─► M1 Core spine ─► M2 Full chain ─► M3 Idle ─► M4 Meta ─► M5 Engage/Money ─► M6 Polish/Ship
   (bedrock)       (playable)       (refining)      (offline)   (prestige)   (SDKs, live)       (art, launch)
```
- M0–M4: **no external packages** — pure C# + Unity built-ins.
- Facades built early (M0/M3) so M5 swaps stub → real SDK with no gameplay rewrite.
- Colorblind palette + localization string discipline observed from **M1** (cheap early, painful to retrofit).

**Parallel art track (Blender MCP):** because every visual is a reference swap, the asset catalog in [ASSETS.md](ASSETS.md) can be produced **independently and in parallel** with the code milestones. Suggested order = ASSETS.md §L priority: first-playable set (mountain, buildings, vehicles, rail, ore, ground) → products & gems → biomes → dressing. Greybox stands in until each asset lands.

---

## 5. Packages requiring your approval (and when)

| Package / SDK | Milestone | Purpose |
|---|---|---|
| *(none)* | M0–M4 | built entirely on Unity built-ins |
| Unity In-App Purchasing | M5 | IAP |
| Ad mediation (LevelPlay or Google Mobile Ads) | M5 | rewarded/interstitial ads |
| Firebase (Analytics + Remote Config) | M5 | analytics + live-ops |
| Unity Mobile Notifications | M5 | local push |
| Google Play Games plugin | M5 | cloud save, leaderboards |
| *(Blender — external DCC via Blender MCP, **not** a Unity package)* | M2+ | authors ALL low-poly meshes; import as `.glb`. See [ASSETS.md](ASSETS.md) |
| Unity Localization | M6 | string tables |
| Addressables | M6 (or M4) | on-demand biome loading |
| Adaptive Performance (Android provider) | M6 | thermal/battery scaling |
| *(optional)* Newtonsoft JSON / BreakInfinity source | M0 | nicer save / proven big-number — only if you'd rather not roll our own |

I will **always ask before adding any of these.**

---

## 6. Open decisions (don't block M0–M4, but needed before M5–M6)

1. **Game name** (still `[GAME_NAME]`).
2. **Big-number:** roll our own `BigDouble` (plan default, zero dep) **or** drop in BreakInfinity source?
3. **Save JSON:** Unity `JsonUtility` (default, zero dep) **or** Newtonsoft (nicer, package)?
4. **Backend for cloud save + trusted time + anti-cheat:** Google Play Games Services vs Firebase vs custom. (Affects M3/M5.)
5. **Ad network / mediation** choice; **analytics provider** (Firebase vs Unity).
6. **Min Android API level** + reference low-end test device.
7. **Monetization pricing** (gem packs, Starter Pack price, permanent 2× price).

---

## 7. Immediate next action

Start **M0**. First concrete sub-tasks: scaffolding (folders + asmdefs + scenes), then `BigDouble` + tests, then `SaveService` + tests, then `GameClock`, then `EconomyConfig` + Bootstrap. I'll create granular tracked tasks when we begin, and verify each against the Test Runner + Unity console before moving on.

> **Prerequisite:** the Unity MCP connection dropped — I'll need it reconnected before writing scripts into the project so I can verify compilation and run tests. (Reopening/refocusing the Unity editor usually restores it.)
