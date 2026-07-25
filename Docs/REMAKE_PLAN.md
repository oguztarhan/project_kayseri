# Remake Plan — "Look and Feel Like a Real Idle Tycoon"

**Date:** 2026-07-25 · **Status:** P1–P7 + P9 done, P8 (world map) outstanding

> **Progress.** Done: portrait, camera fit, render look, layout compaction, station badges, HUD reskin,
> juice, the balance rework, and **P9 site legibility** (§7). Outstanding: **P8 world map**, and the
> per-island mechanical differentiation. Measured balance results are recorded in §2 P7 below.
>
> Key measured numbers (coal, cap $50K/min):
> | | before | after |
> |---|---|---|
> | cost growth / level cap | 1.6 / 10 | 1.13 / 50 |
> | full-max output vs cap | 14.6× (overflow destroyed) | 1.38× |
> | max-out cost vs next unlock | 0.20 → 0.02 across ladder | ~0.50 flat |
> | upgrade curve | `1080·(1+0.335L)^2.89` | same shape, `axisEffectScale = 0.085` |
**Goal:** Rebuild the presentation layer and the balance curve so the game reads as a shipped
idle-tycoon title (reference set: *Idle Miner Tycoon*, *Idle Factory Tycoon*, *Idle Theme Park
Tycoon*), and so a player has ~30 days of meaningful progression across the 8 ore islands.

The simulation (`CoalOperation`) is fundamentally sound and is **not** being rewritten. What
changes is everything the player sees, plus the numbers.

---

## 1. Diagnosis — what's wrong today

From the current build screenshots (`Assets/Screenshots/screenshot-20260724-105144.png`):

| # | Problem | Genre standard |
|---|---------|----------------|
| 1 | **Camera 3–4× too far.** ~55 % of a portrait screen is empty grey ground. | Playfield fills the frame edge-to-edge. |
| 2 | **No island.** An infinite flat grey-green plane, despite `Island_*.fbx` sitting unused in `Assets/Art/Islands/`. | Island silhouette + beach + water, sky visible at the horizon. |
| 3 | **Sprawling horizontal layout** — built for landscape, played in portrait. | Composition runs *vertically* down the screen. |
| 4 | **UI is two flat orange rectangles**, while a full GUI kit sits unused in `Assets/2D Game GUI/`. | Chunky top capsules, bottom nav bar, 9-slice panels. |
| 5 | **No diegetic upgrade buttons.** All upgrading is hidden behind one menu. | Floating cost/level badge over *every* building — the single strongest genre signal. |
| 6 | **Zero feedback.** Money is a number that ticks. | Coins fly to the counter, `+$1.2K` pops, buildings punch on upgrade. |
| 7 | **Flat, desaturated lighting.** | Warm sun, saturated palette, soft shadows, gentle bloom. |
| 8 | **Level cap of 10/axis** → an island caps in under an hour, then the player just waits. | Hundreds of levels; the *income cap* is the gate, not the level cap. |

Problem 8 is the one that actually kills a 30-day arc, and it's invisible in a screenshot.

---

## 2. Phases

Ordered so the biggest visible win lands first.

### P1 — Camera & framing  ⟵ *the explicit complaint*
New `IdleCamera.cs`, replacing the framing math in `OperationCameraBoot.cs`.

**Root cause of the bad framing — three compounding problems:**

1. **Portrait is disabled at the project level.** `ProjectSettings.asset` has
   `allowedAutorotateToPortrait: 0` and a 1024×768 landscape Game-view default. Any fit math solved
   for 9:19.5 won't match what the Game view actually shows until this is fixed. **This goes first.**
2. **The framing bounds are inflated by locked ghost buildings.** `Frame()`
   (OperationCameraBoot.cs:48-59) encapsulates every renderer under the island root, skipping only
   ground/water/dressing by name. Three *unpurchasable* expansion ghosts define the entire box:
   `ghostx_power` at x=4762 and `ghostx_shaft` at x=4992 set the whole 240-unit X span, and
   `ghostx_mine4` at z=−4285 sets the Z span. The active operation the player actually watches —
   mine (x 4985) → storage (4922) → refinery (4848) → market (4801) — is much tighter. So the
   camera is composed around content that stays locked for hours.
3. **The fit ignores aspect ratio.** `zoom = span × viewPad × distanceFactor` (0.22 × 2.6 in the
   scene) is a hand-tuned constant unrelated to FOV or screen shape. At `fieldOfView = 38` the
   horizontal FOV on a 9:19.5 portrait is only ~20°, and nothing accounts for it.

> Correction: an earlier draft of this doc blamed the rail network for (2). That was wrong — the
> rail/road tiles are **not** children of the island root (coal's sit at scene root, clones' under
> `Tiles_<Ore>`), so `Frame()` never sees them. The ghost buildings are the actual cause.

Measured geometry to fit against — playfield centre **(4872, 6.12, −4213)**, all-pivot span
**240 (X) × 143.6 (Z)**, tiles-only bbox 235.6 × 123.1, ground plane y = 6.12. The chain runs
strongly **east→west**, which is the wrong axis for a portrait screen (→ P3).

Fix: frame on **active station positions** (what the player can act on), not all renderers, and use
real fit math:

- Perspective, FOV ~28°, pitch 45°, yaw 45°.
- Project the island's bounds into camera space, solve the required distance against **both** the
  vertical FOV and the horizontal FOV (`2·atan(tan(fov/2)·aspect)`), take the larger, add a 6 % margin.
- Default zoom = "the operation fills the frame". Zoom range 0.55×–1.7× of that.
- One-finger drag pan, two-finger pinch, momentum, soft rubber-band clamp to island bounds.
- `FocusOn(Transform)` for double-tap-to-centre a station and for map→island dives.

`CameraController.cs` is a usable base — Input System already, perspective dolly-zoom already — but
it needs four specific fixes rather than a rewrite:
- **No smoothing or inertia anywhere.** Every input writes `transform.position` the same frame. This
  alone is a large share of why the game feels unfinished versus the genre.
- **Pan speed is calibrated to a dead world map**: `scale = CurrentZoom / 200f`, where 200 was tuned
  for the old ortho-330 map. At an idle-tycoon dolly (~40–60) panning will feel glacial.
- **Zoom doesn't re-clamp position**, so dollying walks the camera outside its own pan bounds.
- **`FrameTo`'s `size` argument is ignored in perspective** (it only touches `orthographicSize`) —
  distance must be pre-baked into `pos`. Easy to trip over; worth making explicit.

Also note the serialized `boundsX/boundsZ` of ±250 on the scene camera are meaningless — the island
sits at x≈4857, z≈−4195, and the bounds are overwritten at boot.

### P2 — Set dressing & lighting
**The bright/juicy look is mostly three switches that are currently off.** The URP asset and
`SampleSceneProfile` already have Bloom (0.25), Neutral tonemapping and Vignette (0.2) authored —
they just never execute:

1. `Main Camera` → `m_RenderPostProcessing: 0` (Main.unity:24877). The whole post stack is bypassed.
2. **No global Volume exists in `Main.unity` at all** (0 hits for `sharedProfile`/`isGlobal`).
3. The scene's only directional light has `m_Shadows.m_Type: 0` — **nothing casts shadows**,
   despite 2048px shadowmaps / 4 cascades / High soft shadows being configured in the URP asset.

Plus `m_AmbientMode: 3` (Flat) with a cool grey-blue `(0.6, 0.64, 0.7)` — that flat cool fill is
precisely why the screenshot reads dull and washed.

Work:
- Flip the three switches; add a global Volume (Bloom + ColorAdjustments saturation/post-exposure +
  soft Vignette).
- Swap flat ambient for warm gradient/skybox ambient; assign the sun to `RenderSettings.sun`.
- Instance the real `Island_<Ore>.fbx` under each island root → actual silhouette and shoreline.
- Large water plane + gradient sky so the horizon isn't void.
- Re-tint ground/grass to a bright saturated palette. All `Assets/Art/` materials are already
  URP/Lit flat-color clones, so re-tints land directly.
- Note: `PC_RPAsset` is the active quality level in-editor; `Mobile_RPAsset` must get the same
  treatment or the phone build won't match what I screenshot.

### P3 — Compact the layout
Shrink station spacing so the whole chain fits one portrait screen at default zoom, arranged as a
vertical S-curve:

```
Mountain (top) → Rail → Storage → Ore trucks → Refinery → Cargo trucks → Market (bottom)
```

This also serves GDD pillar 1: bottlenecks must be *visible*, which requires every stage on-screen
at once. Today the chain runs east→west across 184 units (mine x4985 → market x4801) — the long
axis of the operation is the *short* axis of a portrait screen.

**Two traps this phase has to handle:**
- **Coal's landmarks live inside `Island_Coal.fbx`; the 7 clones' are loose scene GameObjects.**
  Coal is an unopened FBX prefab instance, so `mine_Coal`, `storage`, `refinery`, `market`, `train`
  etc. exist there only as prefab overrides, while the clones have real GameObjects. Any repositioning
  script must handle both paths.
- **`CoalOperation` hard-disables itself if a landmark name is missed** (:316-321), and it finds
  everything by exact name — so a rename or a reparent silently kills the island. Positions may move;
  names and parentage must not.

Layout is not authored in code at all — `CoalOperation` reads named scene objects and never moves
them. The measured grid: roads on an exact 2.0-unit step, rails ~3.78, deck plane y = 6.12, 342 tiles
per island, islands laid out on a 700×700 grid (4 columns × 2 rows).

### P4 — Diegetic upgrade badges  ⟵ *the genre signature*
`StationBadge` — a world-space card floating over each building: icon, `LVL n`, cost.
Green when affordable, grey when not, pulse when it *becomes* affordable.

- Tap badge → buy one level. Tap building → open that station's full upgrade panel.
- One world-space canvas per island (not per badge), billboarded, pooled, culled off-screen —
  keeps draw calls and the SRP batcher happy per CLAUDE.md.

### P5 — HUD reskin using the existing GUI kit
Two facts make this far cheaper than it looks:

- **`MakeFlat()` is the entire HUD skin.** Both `CoalHud.cs:323` and `IslandMapUI.cs:263` generate a
  4×4 white texture and use it for every panel, row and button. Replacing that one sprite source
  with real kit sprites reskins the whole HUD at once.
- **TextMeshPro is fully imported and 100 % unused** — every label is legacy `Text` + Arial.
  Migrating to TMP is mechanical, needs no package work, and unlocks the fat outlined text that is
  the biggest single visual delta versus *Idle Miner*.

One prerequisite: **all 142 kit sprites have `spriteBorder: {0,0,0,0}`** — none are set up for
9-slicing, so `Image.Type.Sliced` silently degrades to Simple and buttons distort at non-native
sizes. Borders must be authored on the `Windows/*` and `Buttons/Button_Square_*` sets first.
I'll also add a SpriteAtlas — 142 loose sprites is 142 potential batches on mobile.

Work:
- Top: cash capsule (coin icon + *rolling* counter), gems capsule, $/min ticker.
- Bottom nav bar: Upgrades · Managers · Boost · Map · Shop.
- Slide-up panels from `2D Game GUI/Windows/*.png` (9-slice), rows from `Buttons/Button_Square_*.png`.
- Rows currently render as one concatenated string (`"TRUCKS  Lv 3  $1.2K"`) with **no icons** —
  split into icon / name / cost columns.
- Canvas split: static bar and dynamic counters on separate canvases (CLAUDE.md UI rule).
- Follow the `UI_Store.prefab` pattern (editor-authored uGUI on the kit, template-clone driver) —
  it's the most recent work and clearly the intended direction.

### P6 — Juice
Starting point: **there is currently none.** No tweening library, no floating text, no coin fly, no
button press feedback, no screen shake, and zero particle systems running at runtime. The only
animation in the whole live game is the 1.3 s loading bar lerping `anchorMax`.

Useful salvage: `Core/Pool.cs` is a working, unit-tested generic pool used by *nothing* in
production — free infrastructure for coins and floating text. `SaleBurst.cs` / `DustTrail.cs` /
`ChimneySmoke.cs` are dead but are decent code-built `ParticleSystem` references to adapt.
`JuiceConfig.asset` already exposes `ScreenShake` and `NumberPunchScale` with zero consumers.

Work:
- Coin burst Market → cash counter on each sale (pooled, ≤8 coins).
- Floating `+$1.2K` on sale.
- Punch-scale + dust puff on upgrade; sparkle on level-up.
- Counter rolls up instead of snapping.
- Confetti + small camera nudge on island unlock.
- Small hand-rolled tween helper (no new packages, per CLAUDE.md) driving all of the above.

### P7 — Balance for ~30 days
**This is the phase that decides whether the game retains, and the current numbers are badly
broken in four independent ways.** Measured, not guessed:

**(a) It's a 10-level curve wearing an idle game's clothes.**
`upgradeCostGrowth = 1.6` with `axisLevelCap = 10` (CoalOperation.cs:43,57). `1.6^10 = 110`.
The GDD §5 specifies `1.09`. An idle curve is 1.09–1.16 over *hundreds* of levels; 1.6 over ten
levels is a different genre. Fix: growth → ~1.13, cap → 60–100.

**(b) Maxing an island is 5–48× cheaper than the next island.**
Fully maxing every axis and unlock costs `$1,365,613 × costMultiplier` — that's **27 minutes** of
capped income, on *every* island. Then the next unlock costs 5× (copper) to 48× (diamond) that.
So each island is a ~30-minute upgrade burst followed by **hours of pure waiting with nothing to
buy.** This is the single biggest structural problem and no amount of art fixes it.
Fix: bring max-out cost and unlock cost into proportion (~1:2.5), so upgrading stays live right up
until you can sail.

**(c) The income cap is a wall, not a ceiling.**
A fully-maxed coal island produces **$729,000/min against a $50,000/min cap — 14.6× over**, and
overflow is *destroyed* at point of sale (CoalOperation.cs:823-831). The top ~half of every axis
track is money thrown away. Fix: tune so max output lands ~1.05× the cap, approached
asymptotically — the cap becomes a felt conclusion instead of an invisible shredder.

**(d) The total arc is several months, not one.**
At ideal continuous at-cap accrual the ladder takes **146 hours**; with real-world friction
(below) it's 2–4× that. Fix: retune `DefaultLadder` unlock costs — the dominant lever, since
axis costs barely move the needle.

**Three outright bugs found while measuring, all of which quietly break the fantasy:**
- A newly-bought island earns **$0/min in the background forever** until you travel there and stay
  30 s (WorldIslands.cs:53-56 + CoalOperation.cs:1004). The passive-empire promise silently
  doesn't fire.
- **Prestige and Boost multipliers are inert.** They're only applied in `Island.cs:82` /
  `Market.cs:44`, both dead code absent from the live scene; `WalletService.AddCash` applies no
  multiplier. Two entire meta-systems currently do nothing.
- Background rates are stored as **`int`** (`SaveData.cs:36-40`) — truncated to whole dollars and
  hard-capped at 2.1 B/min. Latent wall.

With those fixed, the structural change: **raise the per-axis level cap from 10 to 60–100 and drop
cost growth to ~1.13**, letting the *income cap* be the real ceiling. The player then always has
something to buy, income approaches the island cap asymptotically, and "this island is done" is a
felt conclusion rather than an abrupt wall.

Target arc (time to cap each island, assuming ~5 sessions/day plus idle):

| Island | Coal | Copper | Iron | Silver | Gold | Ruby | Emerald | Diamond |
|---|---|---|---|---|---|---|---|---|
| Days | 0.5 | 1 | 1.5 | 2.5 | 3.5 | 5 | 7 | 9+ |

≈ 30 days, with Diamond as the open-ended tail into prestige.

Curve shape (per genre convention: production outpaces cost early, then converges):
- Upgrade cost growth: **1.15^n** on early islands → **1.30^n** on late ones.
- Income per level: ~1.13^n, clamped by the island's `incomeCapPerMin`.
- Next island's unlock price ≈ 5 h of current *total* capped income → affordable around 60–70 %
  through the current island, so sailing early is possible but finishing is rewarded.
- Background income from owned islands is what funds the next unlock — this is the "come back
  tomorrow" engine.

**I will not hand-guess these numbers.** Note the pipeline is *queue-driven on scene geometry*
(throughput depends on rail path length and road-loop point counts from `BuildRoadLoops`
:745-781), so $/min has no closed form. So the measurement is two-stage:

1. **Ground truth by sampling.** An editor tool drives a real `CoalOperation` at
   `Time.timeScale = 50–100`, sets axis levels to a grid of configurations, and records the
   settled `CashPerMinute` for each. That yields a real throughput curve instead of a guess.
2. **Ladder arithmetic on top.** With that curve, `BalanceSim` simulates a realistic player —
   5 sessions/day × 4 min, spends everything, offline cap applied — and prints days-to-unlock per
   island. Tune `DefaultLadder` + growth constants until the day table above lands.

Retention systems to switch on for the Habit phase (scripts already exist — need to confirm they're
live, not dead code): `DailyRewardService`, `BoostService` (2× via rewarded ad), `ContractManager`
(quests), `OfflineEarnings` (2 h base / 4 h with ad), managers for automation, `Prestige` for the tail.

### P8 — World map screen
Replace the flat card grid + fake loading bar with the real thing: `OreEmpire_WorldMap.fbx` as a
3D sea map, islands as pins with lock/price plates, camera pulls out to the map and dives into the
chosen island. The "loading screen" becomes a camera move, which is what the genre actually does.

---

## 3. Constraints honoured

- Scene/prefab/asset changes go through Unity MCP only — never hand-edited (CLAUDE.md hard rule).
- No new packages.
- Tunables exposed as `[SerializeField] private`.
- No LINQ or per-frame allocation in gameplay paths; badges and coins are pooled.
- Existing art (`Assets/Art/**`) used as-is; anything I generate is placeholder and swappable.

## 4. Code map — what's actually live

Verified by resolving every script's `.meta` GUID and grepping the scenes and prefabs. This matters
because roughly **4,700 lines of the repo are dead** and must not be built on.

**Live (all of it):** `CoalOperation` (×8 components), `WorldIslands`, `CoalHud`, `IslandMapUI`,
`OperationCameraBoot`, `CameraController`, `PremiumStoreUI` + `UI_Store.prefab`, `Core/Pool.cs`,
the `Assets/Data/*Config` assets, `Assets/Settings/PC_*`.

Everything runtime hangs off one GameObject, `CoalController`, plus `UI_Store`, `EventSystem` and
`Main Camera`. `Main.unity` contains **zero authored Canvases and zero authored ParticleSystems** —
all three canvases are built in code at runtime.

**Dead — do not touch or extend:** `HudUGUI`, `WorldMapUI`, `HudDebug` (~2,600 lines of superseded
UI) and 21 gameplay scripts including `Island`, `IslandManager`, `GameWorld`, `Market`, `Refinery`,
`MineStation`, `TruckFleet`, `Train`, `ContractManager`, `SaleBurst`, `DustTrail`, `ChimneySmoke`
(~2,100 lines). Note `OperationCameraBoot.cs:92-108` still calls `FindAnyObjectByType<HudUGUI>()`
and `<IslandManager>()` — a vestigial branch that always no-ops. It should go.

The dead `Assets/Data/Islands/*.asset` ladder is also **mis-ordered** (Iron $30 k is cheaper than
Copper $150 k; Emerald $20 M cheaper than Ruby $100 M). If it's ever revived it needs rebuilding.
I'd rather delete it than leave a trap — will confirm before removing anything.

## 5. Blocker (needs you)

Unity's MCP bridge is not accepting connections right now — every call returns `no_unity_session`,
and `mcpforunity://instances` reports zero instances. The editor process **is** running on this
project, but its log shows it loaded `Temp/__Backupscenes/0.backup` and there are
`UnityCrashHandler64` processes alive, i.e. **the editor crashed and came back on a recovered
scene** — most likely sitting on a modal recovery dialog, which would block the bridge.

I can write and compile C# without it, but every scene, material, lighting and prefab change (P2,
P3, P4, P8) needs the bridge. Please check the editor window and dismiss/resolve the recovery
prompt, then I'll re-verify the connection.

## 6. Verification

- After each phase: wait for recompile, read the Unity console, confirm zero new errors/warnings.
- Screenshot the Game view in portrait after P1–P3 and compare against the reference framing.
- Play-mode smoke test per phase; `BalanceSim` output checked against the day table for P7.
- Nothing is claimed done on "it compiles" alone.

---

## 7. P9 — Site legibility and felt upgrades

**Problem.** The islands ship with painted road and rail, but it was authored against a layout the sim
no longer drives. Trucks crossed bare ground beside track that led somewhere else, and the two ore-yard
pads sat ~24 units off the working chain, so the piles read as unrelated props on the grass. Nothing a
player bought changed anything on screen except a number in the HUD.

**What changed.**

| Area | Before | After |
|---|---|---|
| Roads | painted tiles, unrelated to routes | one generated slab per route, dashed centre line, overrun ends for the turnaround |
| Rails | painted tiles + `edge` kerb strips | generated ballast + sleepers + rails, railheads on the engine's wheel line |
| Yards | pads 24 units off-chain, near-white | pulled beside their building, gravel-toned, linked by an apron road |
| Piles | one stretched cube scaled by *fill fraction* | pyramid of chunks driven by *absolute amount*; footprint widens with Capacity |
| Mine | train blinked into existence at a pivot | `SM_Mountain_MineEntrance` on the mine's face, engine spawns inside the building and drives out |
| Map edge | terrain trailing off | `SM_Mountain_<Ore>` ridge, auto-pushed clear of every mine head |
| Buildings | fixed size | scale with accumulated axis levels (cap ×1.22), punch on purchase; dark site pads under each |

**Why the pile drive changed.** Keying the heap off the fill *fraction* meant buying a Capacity upgrade
instantly shrank the pile — the one purchase whose entire point is a bigger yard was the one that made
the yard look emptier. Absolute amount + a grid that widens with capacity inverts that.

**New files.** `BoxMeshBuilder` (mesh accumulation, reused between rebuilds so a growing pile stops
allocating), `RouteMesh` (road/rail/pad builders), `PileStack` (the chunk heap).

**Verified.** All 8 islands travelled in one play session: 4 roads, 2–3 rails, 1 active portal + locked
ones, 4 ridge rocks, 4 site pads each; every authored track object hidden (`stillOn=0`); zero console
errors or warnings. Upgrade feedback measured on coal — 427 levels bought: storage `2.600 → 3.172`,
mine `1.506 → 1.830`, ore yard `4.85 → 9.97` wide and `1.62 → 3.35` tall.

**Not verified:** on-device performance. Each island now adds ~18 renderers / ~30 draw calls of
generated geometry.
