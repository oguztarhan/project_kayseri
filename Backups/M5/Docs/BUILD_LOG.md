# Build Log — Ore Empire *(working title)*

Autonomous build per [PLAN.md](PLAN.md) while the user is AFK.

**Backups / rewind:** git-write was denied during this session, so each completed milestone is snapshotted to **`Backups/M<n>/`** (a full copy of `Assets/Scripts`, `Assets/Data`, `Assets/Scenes`, `Docs`). To rewind to a milestone, restore its `Backups/M<n>/` folder over the working tree (or `git checkout` the files listed below once you re-enable git). When you're back, committing + tagging each milestone in git is recommended.

---

## M0 — Foundation ✅ (2026-07-18)

**Status:** complete & verified. **26/26 EditMode tests green.** Bootstrap scene runs (`session_start` logged), clean play/stop, console clean.

**What it delivers:** the package-free bedrock — big-number math, formatting, fixed-Hz sim clock, encrypted save with tamper detection, a service registry, dev-stub services (facade-first), the economy config asset, and a working bootstrap.

**Files added:**
- `Assets/Scripts/Core/` — `BigDouble.cs`, `NumberFormatter.cs`, `GameClock.cs`, `ServiceLocator.cs`, `Game.Core.asmdef`
- `Assets/Scripts/Systems/` — `GameBootstrap.cs`, `Game.Systems.asmdef`, `Save/SaveData.cs`, `Save/SaveService.cs`, `Services/{Analytics,RemoteConfig,Consent}.cs`
- `Assets/Scripts/Data/` — `EconomyConfig.cs`, `Game.Data.asmdef`
- `Assets/Scripts/Tests/EditMode/` — `Game.Tests.EditMode.asmdef` + `BigDoubleTests`, `NumberFormatterTests`, `GameClockTests`, `SaveServiceTests`, `ServiceLocatorTests`
- `Assets/Data/EconomyConfig.asset`
- `Assets/Scenes/Bootstrap.unity` (Main Camera + `Bootstrap`→GameBootstrap), `Assets/Scenes/Main.unity` (Camera + Directional Light)

**Notable decisions/fixes:**
- `NumberFormatter` forced to `CultureInfo.InvariantCulture` (Turkish-locale would otherwise print `1,5K`).
- Save = AES-256-CBC + HMAC-SHA256; key is client-side (obfuscation + tamper detection; server-trusted time is M3).
- Sim runs on `GameClock` at 8 Hz, decoupled from frame rate (GDD §14.5).

**Backup:** `Backups/M0/`

---

## M1 — Core spine ✅ (2026-07-18)
Goal: playable loop — one mountain → tap-to-mine → train → storage → market → cash + basic upgrades (greybox visuals).

**Core loop VERIFIED & running** (snapshot `Backups/M1-core/`):
- Economy: `Core/Pool.cs`, `Core/ResourceBuffer.cs`, `Systems/Economy/WalletService.cs`, `Systems/Economy/EconomyService.cs` + tests → **37/37 EditMode tests green.**
- Data: `Data/OreTier.cs`, `Data/StationConfig.cs`; assets `Data/Ore/Coal.asset`, `Data/Stations/{Mine,Storage,Market,Train}Config.asset`.
- Gameplay: `Gameplay/{IUpgradable,MineStation,StorageYard,Market,Train}.cs` (Game.Gameplay asmdef).
- `GameBootstrap` now builds WalletService + EconomyService from EconomyConfig, registers GameClock/Wallet/Economy, loads Main. Bootstrap+Main in Build Settings.
- Main scene greybox built via script (mine→train→storage→market, iso camera), all references wired.
- **PlayMode proof:** mine produces → train shuttles → market sells → cash flows (observed tick=108, cash=15.4, train moving). Console clean (only `session_start`).
- Note: Unity pauses an unfocused Game view unless `runInBackground`; that's an editor-test artifact, not a game issue (device backgrounding is handled by offline earnings). Stations are built in-scene (not prefabs yet — prefab-ization happens with real art in M6).

**Completed & verified:**
- UI: `UI/HudDebug.cs` (Game.UI asmdef) — IMGUI greybox HUD: cash/gems readout (NumberFormatter), Mine tap button, per-station upgrade buttons via IUpgradable. *(Themed uGUI canvas + pooled floating text deferred to M6 polish — greybox-now.)*
- `Gameplay/GameWorld.cs`: finds stations, applies saved levels on load, routes upgrades so spend→level-up→save-write stay in sync.
- `SaveData` gained `StationLevels`; `GameBootstrap` registers `SaveData` in the locator.
- Main scene: `GameManager` object (GameWorld + HudDebug).
- **PlayMode proof:** upgrade spends cash + raises level + persists (Mine Lv0→1, cost 10). **Save round-trip** across stop→replay: cash 296 saved → loaded, Mine reloaded at Lv.1. Console clean. **37/37 EditMode tests green.**

**M1 snapshot:** `Backups/M1/`. Human-playable in the Unity Game view (tap MINE, buy upgrades, watch cash grow).

---

## M2 — Full chain ✅ (2026-07-18)
Goal: ore trucks + refinery + recipes (incl. Steel = Iron + Coal) + cargo trucks — the full ore→product→cash pipeline.

**Completed & verified** (snapshot `Backups/M2/`):
- Resource model: `Data/ResourceDef` (base) + `Data/Product`, `Data/Recipe`; `Core/Inventory<TKey>` (multi-resource), `Core/Inventories.Transfer`, `Core/Refining.Process`. **46/46 EditMode tests green** (added Inventory, Refining, Inventories).
- Chain restructure: ore-typed `MineStation`, inventory `StorageYard`, `Refinery` (recipe-driven), `Hauler` base + `OreTruck`/`CargoTruck`, product-selling `Market`, save-by-id `GameWorld`, scrollable HUD.
- Data assets: Iron ore, Coke + SteelBeam products, Coke & Steel(Iron+Coal) recipes, Refinery/OreTruck/CargoTruck configs.
- **Real art integrated** (your Blender `.fbx` kit — imported at correct 1u=1m scale, materials render in colour): Coal + Iron mountains, storage/refinery/market buildings, train + ore-truck + cargo-truck. Greybox retired for the built stations.
- **PlayMode proof:** full chain flows — mines → trains → storage → ore truck → refinery → cargo truck → market. **Both recipes produce** (refOut coke 4.5 + steel 4.5), products reach market, **cash rose 296 → 469** selling products. Console clean.

**M2 snapshot:** `Backups/M2/`. Playable, real-art, ore→product→cash.

---

## M3 — Idle layer ✅ (2026-07-18)
**Completed & verified** (snapshot `Backups/M3/`, 49/49 EditMode tests green):
- Offline earnings: `Core/OfflineEarnings.Compute` (rate×time×eff, capped, rollback-safe) + tests, `Data/OfflineConfig`, `Systems/TimeService` (clamps clock rollback), `Gameplay/IncomeMeter` (cash/sec → save), grant on load in `GameBootstrap`, welcome-back popup in HUD. **PlayMode: reload paid 47.9 offline (50/s × ~1.9s × 0.5).**
- Managers: `IProducer` rate multiplier on Mine/Refinery/Market, hire via `GameWorld` (cost from EconomyConfig, persists in `hiredManagers`), HUD hire buttons. **PlayMode: hire → rate ×2.**
- Anti clock-cheat (TimeService clamp), `ICloudSave` + local stub facade (real backend M5). Save schema v3 (+ incomeRate, lifetimeCash BigDouble, hiredManagers).

---

## M4 — Meta / prestige ✅ (2026-07-18)
**Completed & verified** (snapshot `Backups/M4/`, 53/53 EditMode tests green):
- `Core/Prestige` (Investors = k·√lifetimeCash via BigDouble.Pow(0.5); income multiplier) + tests, `Data/PrestigeConfig`, `Systems/PrestigeService` (investors, multiplier, DoPrestige reset), Market revenue × prestige multiplier, `GameWorld.DoPrestige` (resets in-scene levels/managers), HUD prestige button + investor/multiplier readout.
- **PlayMode: seed 10k lifetime → prestige → +100 investors, income ×3.0, cash + levels + managers reset.**

---

## M5 — Engagement & money (next)
Goal: contracts/orders, daily reward, achievements, + **stubbed** monetization facades (ads/IAP/notifications — no package installs, real SDKs need approval).
