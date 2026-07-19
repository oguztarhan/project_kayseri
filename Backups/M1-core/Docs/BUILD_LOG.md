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

## M1 — Core spine (in progress)
Goal: playable loop — one mountain → tap-to-mine → train → storage → market → cash + basic upgrades (greybox visuals).

**Done so far (verified):** Economy layer — `Core/Pool.cs`, `Systems/Economy/WalletService.cs`, `Systems/Economy/EconomyService.cs`, `Data/OreTier.cs` + EditMode tests (PoolTests, WalletServiceTests, EconomyServiceTests). **34/34 tests green.**
**Remaining M1:** StationConfig SO; MineStation/StorageYard/Market; RailLine+Train (pooled); HUD (currency bar, upgrade buttons, floating text); greybox prefabs + Main scene wiring (GameBootstrap builds services from EconomyConfig, loads Main); save integration + PlayMode verification. No `Backups/M1/` snapshot yet — taken when M1 is complete.
