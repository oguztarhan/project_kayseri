# NEWPLAN38 — Mining shop on Main: where we are and what comes next

Date: 2026-09-14. Replaces the status parts of `MAIN_SCREEN_MINING_SHOP_PLAN.md` (that file stays the design reference
for player experience, balance candidates and save rules). Evidence lives in `MINING_SHOP_CLAUDE_HANDOFF.md`
(sections noted as §) and `MINING_SHOP_CODEX_HANDOFF.md`. Project rules in `CLAUDE.md` still apply.

## 1. Where we left off

| Package | What it delivered | Owner | Status | Evidence |
| --- | --- | --- | --- | --- |
| 0 — Target and contract | Main confirmed as the gameplay screen; campaign catalogue (7/6/9 islands, stable IDs) | Codex / Claude audit | Done | §1–7, 14/14 tests |
| 1 — Pickaxe logic | Craft → rack → carrier → shelf → customer → one payout, save/reload | Codex | Done | §9, 110/110 |
| 2 — Pickaxe on screen | Main anchors/route, views, upgrade panel, camera, legacy haulage hidden, fresh/existing startup | Claude | Accepted | §10–13, round11 capture |
| 3 — Four-product core + service | Helmet/lantern/bag lines, shared carrier/seller, ordered table builds, receipts | Codex | Done | §14–16, 104/104 |
| 4 — Main on the business service | Bootstrap opens `OpenMiningShopBusiness`; per-product views, locked pads, build button; 1-4 test path | Claude (+ Codex migration fix) | Accepted | §17–18, 133/133 |
| 4.1 — First-session clarity | Old ore tutorial paused while the shop is open; white HUD numbers; "N SOLD · $/min" shop status | Claude | Delivered, awaiting Codex review | §19, 133/133 + 14/14 |

What a player sees today on a fresh install: Main opens on island 1-1, a pickaxe bench with worker, a pallet rack,
one carrier, a shared shelf and a customer queue; first sale in ~20 s; tap the bench to buy speed/value; HUD shows
cash and live shop status; no old tutorial, no ore porters. Existing players' pickaxe saves migrate once with no
double payment.

Not committed yet: all package 1–4.1 code, scene and docs changes are in the working tree (`git status`). No push.

## 2. Known open issues carried forward

1. **1-4 crowding.** Helmet/lantern/bag benches (scale 0.7, no workers) squeeze into the narrow strips beside the
   market roof on a small octagonal islet; pickaxe Craft/Stock signs overlap the helmet line.
2. **Carrier routes** differ in length (82–191 units) but share one travel time; the carrier jumps between line starts.
3. **Customers outpace one crafter** on 1-1 (queue sits full) — tuning, Codex's call.
4. **No shop onboarding.** The ore tour is paused, not replaced.
5. **Legacy leftovers in UI:** rate-pill tap opens the ore breakdown; contract offers are sized from the ore meter (0).
6. **Placeholder art:** primitive goods and tables; only the pickaxe line has a worker and signs.
7. **Small people/goods** (~5% of screen height).
8. `Bootstrap.unity` now serializes the package-2 startup fields at their default values (4 lines, harmless).
9. Test screenshots in `Captures/` (untracked `round*`, `mining_shop_*`, `p4*`, `p41_*`) — delete only those; the folder
   itself is tracked project content.

## 3. Decisions needed from the user (only these)

1. **Completed islands:** keep earning in the background, or pause when the player moves on? (Blocks package 6.)
2. **Island stage:** keep all four lines on the current market islet (accept crowding), or give 1-2…1-4 a larger
   stage/extra space on Main? (Blocks package 4.2 scope.)
3. **Commit point:** commit packages 1–4.1 now as one checkpoint before package 5? (No push without asking.)

## 4. Next packages (in order)

### 4.2 — Readable multi-product shop (Claude; Codex reviews)

Goal: 1-2…1-4 read as clear loops, and a new player is taught the shop, not the ore chain.
- Stage per decision 3.2: either re-lay the islet with per-line workers and signs, or add a larger stage.
- Even out carrier movement (route lengths or view-side pacing only; no economy change).
- Minimal shop onboarding: first sale pointer, first upgrade, first build pad — real transactions only, localized.
- Replace remaining ore leftovers on the HUD (rate-pill tap destination) with shop information.
- Verify: isolated fresh 1-1 and 1-4 test path screenshots at 1080×1920, no rail/panel overlap, tests green.
- Out of scope: islands, rewards, offline, tuning, new art assets.

### 5 — Islands and goals (Codex core; Claude screen)

- Codex: island completion rules (e.g. 1-1 sell 20 pickaxes + 4 upgrades; later islands: all tables built + newest
  product sales), per-island records, travel/advance without difficulty reset across 1-7→2-1 and 2-6→3-1, rewards pay
  once, contract/offer sizing from shop income instead of the ore meter.
- Claude: island label and goal card on HUD, "Island complete" + claim, travel flow on Main, every new island starts
  with pickaxes and shows only its available pads.
- Verify: 1-1→1-2→1-4 progression on isolated saves, reopening an island keeps its tables, no double reward.

### 6 — Offline, background and integration (Codex implements; Claude runs Unity scenarios)

- Depends on decision 3.1. Elapsed-time settlement through the same business rules, bounded, consumed once; welcome-back
  report; existing offline cap/efficiency applied once; legacy offline grant excluded.
- Verify: background/foreground/reload/offline scenarios, no duplicate currency, no lost stock.

### 7 — Balance and device (Claude measures; Codex tunes)

- Fresh-save timing runs against the first-session targets (first sale ≤30 s, first upgrade ≤60 s, 1-1 in ~3–5 min).
- Fix the full-queue pacing, check 1-4 traffic, profile on a mid-range Android device (60 fps budget, GC), then iOS.
- Final user playtest.

### Later (not scheduled)

Optional tips/bonus pile, modelled product meshes and per-line crafter animations, grouped receipts at high
throughput, bottleneck hints ("Rack full — improve carrying").

## 5. Working rules for both agents

- One owner per shared file per package; Codex: Core, MiningShop services, MarketService, save/migration, campaign.
  Claude: scene, views, HUD/tutorial/UI, localization, Unity verification. GameBootstrap goes to whoever the package names.
- Never hand-edit `.unity/.prefab/.asset/.meta`; scene work through Unity tools.
- Every Play check uses an isolated save (copy + SHA-256, restore + `shasum -c`); Play always starts from Bootstrap with
  only Main loaded.
- After C# edits: recompile, read the console, run the affected EditMode tests; report exact counts.
- No save-version bump, no packages, no push without asking.
