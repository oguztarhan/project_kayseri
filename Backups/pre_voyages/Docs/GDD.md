# Ore Empire *(working title)* — Game Design Document

**Studio:** Intake Entertainment · **Engine:** Unity 6.4.9f1 · **Pipeline:** URP · **Target:** Android (Google Play), 60 fps on mid-range · **View:** 2.5D isometric, low-poly, bright/cartoony

> **Status: living document.** Every number in here is a **DEFAULT**. All tunable values live in ScriptableObjects / `[SerializeField]` fields and are edited in the Unity Inspector. **You never open a script to change a value.** See §14 (Data-Driven Architecture) and §16 (Config Catalog).

---

## 1. Vision & Design Pillars

**One-liner:** Build and optimize a mining empire where ore flows from mountains, through trains, refining, and trucks, into cash — then reinvest, automate, and prestige your way to a bigger operation.

**Pillars (every feature must serve at least one):**
1. **The bottleneck loop is the fun.** Players watch throughput, spot the clogged stage, upgrade it, and feel the money jump. Bottlenecks must be *visible* (ore visibly stacks up when a stage can't keep up).
2. **Everything is upgradeable.** 7 stations × multiple upgrade axes + managers + prestige = a constant "what do I improve next?" decision.
3. **Designer-configurable.** Meshes, audio, VFX, economy, UI — all editable in the Inspector without touching code. (Your explicit requirement.)
4. **Idle-friendly.** Offline earnings, automation via managers, respect the player's time.
5. **Generous, opt-in monetization.** Player-chosen rewarded ads + high-value IAP, minimal nagging → higher long-term spend.
6. **Runs everywhere.** 60+ fps across the broadest possible Android range, with adaptive quality for low-end devices. Draw calls and GC allocations are the enemy. This is a hard requirement, not a "nice to have" — see §14.5.

---

## 2. Core Loop

**The chain (one shared map, mountains as nodes):**

```
MOUNTAIN → TRAIN → STORAGE → ORE TRUCK → REFINERY → CARGO TRUCK → MARKET → 💰
 mine     haul     raw-ore    haul raw    ore →       haul          sell
 raw ore  ore      buffer     ore         PRODUCT     goods         goods → cash → reinvest
```

- **Moment-to-moment (seconds):** tap the mine (early game) / watch automated flow; collect tappable bonuses.
- **Session (minutes):** spot bottleneck → buy upgrades → unlock next ore tier / recipe → complete a contract.
- **Meta (days):** unlock new mountains, hire managers, prestige for permanent multipliers, climb rarity tiers.

---

## 3. Station Systems

Seven upgradeable stations. Each has a **rate** (throughput) and a **capacity** (buffer), a **Manager** (automation + bonus), and a **swappable visual prefab**.

| # | Station | Function | Upgrade axes | Bottleneck signal |
|---|---------|----------|--------------|-------------------|
| 1 | **Mine** | Extract raw ore from a mountain | mine speed · ore-per-cycle · unlock richer tier | ore piles at mountain head |
| 2 | **Rail / Train** | Haul ore mountain → storage | train count · wagon capacity · train speed · new rail lines | full wagons wait / mountain overflows |
| 3 | **Storage Yard** | Raw-ore buffer | max capacity · load/unload speed | yard full → trains can't unload |
| 4 | **Ore Trucks** | Haul raw ore storage → refinery | fleet size · truck capacity · speed | refinery starves / storage overflows |
| 5 | **Refinery** | Turn ore → **product** (recipes) | process speed · recipe slots · output capacity · unlock recipes | input backs up / output full |
| 6 | **Cargo Trucks** | Haul products refinery → market | fleet size · truck capacity · speed | products back up at refinery |
| 7 | **Market** | Sell products for cash | sell speed · price multiplier · bulk/demand deals | products pile at market |

**Design rule:** tune curves so the bottleneck *keeps moving* — fix the train, trucks become the limit; fix trucks, the mine becomes the limit. That churn is the retention engine.

---

## 4. Ore & Products

**8 raw ore tiers (rarity ladder):** Coal → Copper → Iron → Silver → Gold → Ruby → Emerald → Diamond.
Each tier: higher base value, gated behind mountain unlocks. *(Defined as `OreTier` ScriptableObjects — value, color, icon, mesh.)*

**Refining = "light recipes"** (mostly 1:1, a few premium combines). *(Each is a `Recipe` ScriptableObject: inputs[], output, refine time, value.)*

| Recipe | Inputs | Stage | Role |
|--------|--------|-------|------|
| Coke | Coal | Early | Teaches basic 1:1 refining |
| Copper Bar | Copper | Early | 1:1 |
| **Steel Beam** | **Iron + Coal** | Early-mid | First *combine* — balance two ore streams |
| Silver / Gold Bar | Silver / Gold | Mid | 1:1 high value |
| Cut Ruby / Emerald | Ruby / Emerald | Mid | 1:1 gem |
| Polished Diamond | Diamond | Late | 1:1 premium |
| **Ruby Ring** | **Gold Bar + Cut Ruby** | Mid-late | Premium combine, big money |
| **Diamond Crown** | **Gold Bar + Polished Diamond** | Late | Endgame money-maker |

Product value ≫ raw ore value (that's *why* the Refinery is worth upgrading heavily).

---

## 5. Economy & Progression Math

**Currencies:**
- **Cash** — soft currency; earned by selling; spent on upgrades. Uses the big-number system (§14).
- **Gems** — premium currency; managers, instant upgrades, time-skips. Earned slowly (achievements/daily) or bought.
- **Investors** — prestige currency (§8); permanent global multiplier.
- *(Optional later)* **Research Points** — global upgrade tree.

**Default curves (all params in `EconomyConfig` SO — tunable):**
- Upgrade cost: `cost(level) = baseCost × growth^level`, default `growth = 1.09`.
- Station output scales with level; income roughly tracks cost growth so progression feels steady but slows enough to make boosts/IAP attractive.
- Ore tier value: geometric, default `×3.2` per tier.
- Milestone upgrades (every 10/25/50 levels) give a step multiplier + a visual change to the station.

**Balancing philosophy:** never let one stage dominate for long; the "next upgrade" should almost always be a different station than the last.

---

## 6. Managers & Automation

Early game = light tapping (tap mine to mine). Then hire a **Manager** per station → it automates that station and grants a bonus. *(Each a `ManagerConfig` SO: target station, automation on, bonus type/amount, cost, portrait, hire SFX.)*

- Manager bonuses: e.g., +% speed, +% capacity, −% upgrade cost, "ships even at partial load."
- Managers are a primary **Gem sink** and a satisfying "set it and forget it" beat.

---

## 7. Offline Earnings & Idle Math

*(All in `OfflineConfig` SO.)*
- On quit, timestamp the world state. On return, compute elapsed time and award accumulated production/cash at a configurable **offline efficiency** (default 50%).
- **Cap** offline accrual (default 2h free). Extend cap via IAP; **2× / instant-collect via rewarded ad** on return (big engagement + revenue beat).
- Welcome-back popup shows the pile of cash earned while away.

---

## 8. Prestige & Meta-Progression *(designed in from day one)*

*(All in `PrestigeConfig` SO.)*
- "Sell the operation" → reset stations/cash, keep a permanent boost. Awards **Investors** based on lifetime earnings this run: default `Investors = k × sqrt(lifetimeCash)`.
- Investors grant a **global income multiplier** and unlock a **prestige upgrade tree** (permanent perks: faster trains everywhere, cheaper managers, higher offline cap, etc.).
- Economy is authored so the *first* prestige is reachable in a satisfying early window, then each subsequent one is deeper.

---

## 9. Contracts / Orders *(your added feature)*

*(A pool of `ContractDefinition` SOs + `ContractConfig` for spawn rules.)*
- Timed goals: "Deliver **500 Steel Beams** in **10:00** → **big cash + gems**."
- Gives idle players a concrete goal and a reason to *rebalance* production on demand.
- 2–3 active slots; reroll/skip via ad or gems. Difficulty/reward scale with progress.
- *(Market price fluctuation is parked for a later version, per your call.)*

---

## 10. Monetization *(generous & opt-in)*

*(All in `MonetizationConfig` SO — every price, boost %, and cooldown editable.)*
- **Rewarded ads (player-initiated):** 2× income (timed), collect-offline-now, speed-boost-all-stations (30s), free gems, contract reroll.
- **IAP:** Remove Ads · Gem packs (tiered) · **one-time Starter Pack** (cheap, high-value — best conversion) · **permanent 2× income** · rotating flash offers · (optional) season pass.
- **Almost no forced interstitials.** Respecting the player raises LTV more than nagging.

---

## 11. Retention

- **Daily reward / login streak**, **achievements & milestones** (Gem payouts), **tappable bonuses** (gold nugget pops up → tap for bonus cash / a short boost). *(Configs: `DailyRewardConfig`, `AchievementDefinition` SOs.)*
- **In-app review prompt** at a high-satisfaction moment (e.g., just after a prestige or a big contract payout) via Google Play In-App Review — rate-limited, never mid-action. *(`ReviewConfig`.)*
- **Light social / leaderboards** — Google Play Games leaderboards ("richest empires," total prestige) + platform achievements; optional friend compare. *(`SocialConfig`.)*
- **Limited-time events** (seasonal mountains) — post-launch.

---

## 12. UX / FTUE / UI

- **FTUE:** first minute you tap the mine and watch the first ore reach the market and turn into cash; then the first upgrade; then the first manager (automation). Teach the loop by doing.
- **HUD:** cash + gems top bar, station upgrade buttons, contracts panel, prestige button, settings.
- **Onboarding arc:** tap → upgrade → automate → unlock next tier → prestige.
- **Canvas split:** static UI (frame, currency bar) on one canvas; dynamic UI (floating cash numbers, progress bars) on another — avoids rebuilding the whole canvas each frame.
- **UI is data-driven:** a `UITheme` SO (colors, fonts, spacing, button styles) + prefab screens; text through a strings table (localization-ready). Change the whole look from one SO.

---

## 13. Art & Audio Direction

- **Visual:** 2.5D isometric, low-poly, flat/vertex-colored, bright saturated palette, soft ambient light. **Few base meshes, recolor everything** (1 mountain tinted per biome, 1 ore chunk tinted per tier, 1 train/truck recolored, modular rail kit, 3 building shells).
- **Pipeline:** **greybox primitives for prototyping → swap to custom low-poly meshes authored in Blender (via Blender MCP)**, dropped into the prefab's mesh reference in the Inspector — no code change. The full asset list, per-asset Blender prompts, and Blender→Unity import conventions live in [ASSETS.md](ASSETS.md).
- **Audio:** `AudioLibrary` SO — background music tracks (per screen/biome) + SFX (event → clip + volume + pitch range). Master/music/SFX volumes in `AudioConfig` SO.
- **VFX:** `VFXLibrary` SO — event → pooled particle prefab (ore burst, cash pop, upgrade sparkle, prestige flash).

---

## 13.5 Accessibility, Localization & Game Feel

**Accessibility** *(`AccessibilityConfig` SO — all toggles)*
- **Colorblind-safe ore/products:** never rely on hue alone. Each ore/product reads by **color + a unique icon/shape + label**, with colorblind palette presets (deuteranopia/protanopia/tritanopia).
- Text-size scaling, high-contrast HUD, **reduce-motion** (dampens shake/particles), larger tap targets, left-handed layout option.

**Localization** *(Unity Localization string tables + `LocalizationConfig`)*
- All player-facing text via string tables — **no hardcoded strings**. Locale-aware number/currency formatting (works with the big-number formatter).
- **Launch languages (default set, editable):** English, Turkish, Spanish, Portuguese (BR), German, French, Russian, Japanese, Korean, Simplified Chinese. Font atlas must cover Latin-ext + Cyrillic + CJK. RTL-ready hooks for future Arabic.

**Game feel / "juice"** *(`JuiceConfig` + `HapticConfig` SOs)*
- Satisfying feedback on every meaningful action: number **punch/scale** on cash gain, coin-burst on sale, sparkle on upgrade, camera **micro-shake** on milestone, eased UI transitions.
- **Haptics:** light vibration on upgrade/sale/reward, fully toggleable, respects reduce-motion and OS settings.
- All juice is **quality- and accessibility-gated** (auto-reduced on Low tier / reduce-motion) so it never costs the frame budget (§14.5).

---

## 14. Technical Architecture (Unity)

**Data-Driven pillar (the core of "everything editable"):** every system reads its numbers/assets from a ScriptableObject asset in `Assets/Data/…`. Designers duplicate/edit SO assets in the Project window; runtime code only *reads* them. Prefab & mesh & clip references are `[SerializeField]` fields wired in the Inspector.

**Foundational systems:**
- **Big-number type** (`BigDouble`: mantissa + exponent) with formatting (`1.5K / 2.3M / 4.1aa`) — idle numbers exceed `double`/`long`.
- **Save system:** JSON, timestamped, for offline earnings; versioned for migration; autosave + on-pause.
- **Object pooling:** ore chunks, train/wagon cars, trucks, floating text, VFX — anything spawned repeatedly. No `Instantiate`/`Destroy` in hot paths.
- **Performance:** SRP Batcher friendly (shared materials, no per-renderer MPBs), no LINQ/allocations in `Update`, cache component refs in `Awake`.
- **Config-first services:** `EconomyService`, `AudioService`, `VFXService`, `SaveService`, `AdService`, `IAPService` — each initialized from its SO config.

**Folder layout:**
```
Assets/
  Scripts/  Gameplay/ · UI/ · Systems/ · Data/ (SO class defs)
  Data/     EconomyConfig, OreTiers/, Recipes/, Mountains/, Managers/, Contracts/, Audio/, VFX/, UITheme, Monetization  (SO assets)
  Prefabs/  Stations/ · Vehicles/ · Ore/ · VFX/ · UI/
  Art/ · Audio/ · Scenes/ · Settings/ (URP)
```

---

## 14.5 Performance Budget & Optimization Mandate *(mobile-first — hard requirement)*

**Target: 60+ fps on the widest possible Android range**, with graceful degradation on very low-end hardware. Frame budget = **16.6 ms**. Draw calls and GC allocations are the primary enemies (per CLAUDE.md). Every system below is written to this budget from day one — performance is not a post-launch cleanup pass.

**Non-negotiable coding rules**
- **Zero per-frame heap allocations** in gameplay/UI hot paths — no LINQ, no closures/lambdas that capture, no boxing, no `string` concatenation in `Update`. Cache, reuse, pool.
- **Object-pool everything** spawned repeatedly: ore chunks, wagons, trucks, floating cash text, VFX, UI list rows. No `Instantiate`/`Destroy` in hot paths.
- Cache all component refs in `Awake`; never `GetComponent` / `Find` / `Camera.main` in `Update`.
- **Decouple simulation from rendering:** run the economy/production sim on a **fixed low-frequency tick** (5–10 Hz), not per-frame; visuals interpolate between ticks. A stuffed factory must not cost more CPU per frame than an empty one.

**Rendering**
- One shared URP Lit/Unlit **material family**; keep the **SRP Batcher** happy (no per-renderer `MaterialPropertyBlock` unless justified).
- **GPU instancing / static batching** for repeated meshes (ore chunks, rail segments, identical vehicles).
- Texture atlas; flat/vertex-colored low-poly → minimal texture memory, no LODs needed.
- **Baked lighting**, no realtime shadows (or one cheap blob shadow), no realtime GI.
- Post-processing minimal and **quality-gated** (bloom only on higher tiers).
- **Canvas splitting** (static vs dynamic UI); avoid per-frame layout rebuilds.

**Adaptive quality tiers** *(`QualityConfig` SO)*
- On first launch, detect device tier from `SystemInfo` (RAM, GPU, resolution) → **Low / Mid / High**.
- Scale per tier: particle density, resolution scale, post-processing, shadows, `targetFrameRate`, max simultaneously animated vehicles.
- Use the **Adaptive Performance** package for thermal/battery throttling where supported.
- Player override in Settings (quality preset + fps cap).

**Idle / background behavior**
- Cap `Application.targetFrameRate`; drop to 30 (or lower) when the scene is visually static to save battery — idle games run for long sessions.
- Reduce sim frequency and halt non-essential animation when backgrounded.

**Memory**
- Addressables for on-demand content (biomes/mountains load as they unlock; unload when away).
- Budget atlas/mesh/audio memory against 2–3 GB-RAM devices.

**Measurement & gate**
- Profile on a **real low-end device** (not just the editor) every milestone.
- Automated performance tests (frame-time budget per system) in the Test Runner.
- A milestone is **not "done"** if it regresses the frame budget on the reference low-end device.

---

## 14.6 Live Services, Platform & Player Data

*(Backend/SDK-integration systems. Each provider is wrapped behind our own service interface so providers are swappable and everything degrades gracefully offline.)*

**Analytics & funnel tracking** *(`AnalyticsService` + `AnalyticsConfig` event taxonomy)*
- One `AnalyticsService.Log(event, params)` facade over Firebase/Unity Analytics.
- **Core events:** session_start, ftue_step_x, first_ore_sold, station_upgraded (id/level/cost), mountain_unlocked, recipe_unlocked, manager_hired, prestige (run#/investors), contract_completed, currency_earned/spent **tagged by source/sink**, ad_shown (placement/result), iap_purchase (sku/price), offline_collected.
- Every cash **source and sink** is tagged so the economy can be balanced from real data.

**Remote config / live-ops** *(`RemoteConfigService`, falls back to local SO defaults)*
- Economy numbers, event toggles, offer schedules, and monetization params are **remotely tunable without an app update** — the live-ops extension of the "everything configurable" pillar.
- **A/B testing** hooks; staged rollout; safe local-default fallback if fetch fails.

**Cloud save + save integrity / anti-cheat** *(`SaveService` + `CloudSaveService`)*
- Local save **AES-encrypted** and **checksummed**; tampered saves rejected.
- **Anti clock-cheat:** offline earnings validated against **trusted server time**, not just the device clock; suspicious time jumps and date rollbacks capped/denied.
- **Cloud sync** via Google Play Games Services (or custom backend) → no progress loss, cross-device continuity, conflict resolution.

**Local push notifications** *(`NotificationService` + `NotificationConfig`)*
- Triggers: **storage full**, **offline earnings capped**, **contract expiring**, **daily reward ready**, win-back after N idle days.
- Scheduled locally (Unity Mobile Notifications), localized copy, **opt-in** and frequency-capped.

**Privacy, consent & ad mediation** *(`ConsentService` + `AdService`)*
- **GDPR/CCPA consent** via Google UMP; **COPPA age-gate** on first launch → non-personalized ads for minors.
- Google Play **Data Safety** compliance; consent **gates** analytics and ads (nothing fires before consent).
- **Ad mediation** (ironSource LevelPlay / AdMob) behind `AdService`; placements defined in `MonetizationConfig` (§10).

---

## 15. Build Order (MVP → v1)

- **M0 — Foundation:** BigDouble + formatting, SaveService skeleton, EconomyConfig SO, folder/asmdef setup, greybox prefabs.
- **M1 — Core spine:** one mountain → tap-to-mine → train → storage → market → cash. Upgrades for these. (No refinery/trucks yet.)
- **M2 — Full chain:** ore trucks + refinery + recipes (incl. first combine) + cargo trucks.
- **M3 — Idle layer:** managers/automation, offline earnings, welcome-back.
- **M4 — Meta:** prestige + prestige tree; unlock a 2nd mountain.
- **M5 — Engagement/money:** contracts, rewarded ads, IAP hooks, daily reward, achievements.
- **M6 — Polish:** art pass (swap greybox → real low-poly), audio, VFX, juice, performance profiling.

Each milestone ends **playable and tunable in the Inspector**.

**Cross-cutting systems slot in as:** analytics + consent + encrypted save + remote-config facade at **M0** (architectural — cheap early, painful to retrofit); cloud save + anti-cheat with the offline layer at **M3**; push notifications, ad mediation, in-app review, and leaderboards at **M5**; accessibility, localization, and haptics/juice at **M6** — with the **colorblind palette + string-table discipline observed from M1 onward** (both are painful to retrofit).

---

## 16. Config Catalog — "Everything You Can Edit Without Code"

| You want to change… | Where (SO / field) |
|---------------------|--------------------|
| Any money value, cost curve, income, tier multiplier | `EconomyConfig` |
| Ore tiers (value, color, icon, **mesh**) | `OreTier` assets |
| Refining recipes (inputs, output, time, value) | `Recipe` assets |
| Mountains / biomes (ore mix, unlock cost, **mesh/material**) | `MountainDefinition` assets |
| Station upgrade axes & curves | per-station `UpgradeCurve` / `StationConfig` |
| Managers (bonus, cost, portrait) | `ManagerConfig` assets |
| Offline cap & efficiency | `OfflineConfig` |
| Prestige formula & perk tree | `PrestigeConfig` |
| Contracts (goals, timers, rewards) | `ContractDefinition` + `ContractConfig` |
| IAP prices, ad rewards, boosts | `MonetizationConfig` |
| Daily rewards, achievements | `DailyRewardConfig`, `AchievementDefinition` |
| **Music & SFX** (clips, volumes, pitch) | `AudioLibrary` + `AudioConfig` |
| **Visual effects** (which particle per event) | `VFXLibrary` |
| Device quality tiers, fps caps, scaling | `QualityConfig` |
| Analytics events tracked | `AnalyticsConfig` (event taxonomy) |
| Remotely-tuned economy / events / offers | Remote Config (server) → local SO fallback |
| Push notification triggers & copy | `NotificationConfig` |
| Consent, age-gate, ad mediation | `ConsentService` + `MonetizationConfig` |
| Accessibility (colorblind, text size, reduce-motion) | `AccessibilityConfig` |
| Haptics & juice (shake, pops, vibration) | `JuiceConfig` + `HapticConfig` |
| Review-prompt timing | `ReviewConfig` |
| Leaderboards / achievements | `SocialConfig` |
| **UI look** (colors, fonts, spacing) | `UITheme` |
| **Any mesh/model** (station, vehicle, ore, prop) | prefab's `[SerializeField]` mesh/prefab ref |
| Text / language | strings table |

> If it's a value or an asset you'd ever want to tweak, it lives here — not in code.
