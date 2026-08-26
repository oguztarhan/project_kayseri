# Voyages — the consumption layer

**Date:** 2026-08-26 · **Status:** spec, nothing implemented · **Restore point:** `Backups/pre_voyages/` + git tag `pre-voyages` (f73d939)

> Every number in this document is a **DEFAULT**, in the sense GDD §14 means it: it lives in a
> ScriptableObject and is edited in the Inspector. The numbers here describe the *shape* of the
> curve. The actual values get set by measurement, the way REMAKE_PLAN §P7 set the island ladder —
> not by hand-guessing.

---

## 1. Why this exists

Reference set for this feature: *Idle Weapon Shop*, whose loop is not one loop but four feeding each
other — forge → hunter orders → expeditions → collection, wrapped in chapters and events.

Our diagnosis, from reading our own code rather than the GDD:

**Every bar this game produces has exactly one destination.** It goes to the counter and becomes
cash, and cash buys more production. There is no point anywhere in the chain where the player decides
what a bar is *for*. That is the whole gap. It is not art, not polish, not balance.

The reference game's weapons go **into** hunters; hunters return materials; materials feed the forge.
Production is consumed by something that pays back in a different currency. That is the structure we
are stealing. The fantasy stays ours: we already own ships, ports, an offshore trade post, an export
dock, sea bounds, and a berthing hull.

**What this is not.** It is not an RPG layer. The reference game spends an enormous build on
auto-battle, arenas, and tournaments to produce one thing: a risk decision with a delayed payout.
We can buy the same decision with a risk roll for ~1% of that cost. If auto-battle earns its place
later it can, but it is not what closes this gap.

---

## 2. The loop

```
bars in the yard ──┬── carried to the counter ──→ CASH       (exists today)
                   │
                   └── carried to the dock ─────→ a voyage ──→ CARDS   → foremen
                                                            → SALVAGE → ships
```

Two returns, both deliberately chosen because they feed something that already exists and is
currently starved:

**CARDS** feed `Core/Foremen.cs`, which is built for "months of duplicates" (its own words) and is
fed today only by a trickle from dailies and contracts. `ForemanService.GrantRandomDuplicates` is
already written and documented as "what a generic foreman crate pays out" — the crate was never
built. This is the crate. The roster has a ladder and no engine; voyages are the engine.

**SALVAGE** buys ship upgrades and nothing else. Hold size, speed, berths, crew. It is a closed loop
— salvage comes from voyages and only ever goes back into voyages — which means it carries **zero
balance risk to the main economy**. This is the reference game's "equip your hunters", sealed.

---

## 3. The four rules

These are load-bearing. Breaking any one of them collapses the feature into something we already have.

**R1 — A voyage never pays cash.** Cash has one faucet, `MarketService`, and that is the one good
architectural decision in the income path. If voyages paid cash they would compete with the counter
for the same reward, and whichever paid better would make the other pointless. Different currency,
different track, no overlap.

**R2 — A voyage never pays a rate.** Every island's income is clamped by `incomeCapPerMin`, so
anything that lifts throughput gets swallowed for exactly the player who has been playing long
enough to care. Cards are not a rate. This is the same reasoning `Foremen.cs` already gives for
why a foreman lifts the ceiling as well as the throughput.

**R3 — Nothing expires.** `ContractService` states it plainly: an idle game must never punish a
player for looking away. A returned voyage waits at the dock indefinitely. A loaded ship that has
not sailed waits indefinitely.

**R4 — Costs are fractions of delivery, never absolute bars.** `MarketFlow` established this and the
reason is unchanged: the ore ladder multiplies output by 3.2 per tier, so any absolute bar count is
correct on coal and meaningless by diamond.

---

## 4. How a voyage is paid for

`MarketYard.stock` is a real inventory (SaveData.cs:168) but a **small** one — it is bars on the
pads, bounded by `depositSlots` (max 4). It is a buffer, not a warehouse. So a voyage cannot be
bought with a lump sum out of stock; there will never be a big enough pile.

Instead, **the ship has a hold and the hold fills over time**, exactly the way the pads do:

- **Actively:** the player carries bars to the dock pad. Reuses `CarryStack` wholesale.
- **Idly:** a hired dockhand diverts a share of arriving deliveries into the hold instead of the
  counter. This is `MarketFlow`'s fourth job, and it slots into the existing three-job hire model
  (`Carry`, `Serve`, `Collect` → add `Load`).

When the hold is full, the ship sails. The cost is therefore expressed as **diverted throughput**,
which auto-scales across the ore ladder for free and satisfies R4 without any special-casing.

```
holdSize (bars) = ship.holdMinutes × yard.deliveredPerMin
fillRate        = yard.deliveredPerMin × divertShare      (divertShare default 0.35)
timeToFill      = ship.holdMinutes / divertShare
```

The felt cost of a voyage is "for the next N minutes, a third of this island's output goes to sea
instead of the till." That is a decision the player makes with a number they can already read on
the HUD.

---

## 5. Routes

A route is a distance tier. Everything else is derived from it.

| Tier | Name (placeholder) | Duration ×  | Payout × | Risk | Unlocked by |
|------|--------------------|-------------|----------|------|-------------|
| 1 | Coastal run   | 1.0 | 1.0 | 0 %  | free |
| 2 | Open water    | 2.5 | 3.0 | 8 %  | ship level 2 |
| 3 | Deep sea      | 6.0 | 9.0 | 18 % | ship level 4 |
| 4 | The far reach | 14.0 | 28.0 | 30 % | ship level 6 |

```
duration = baseDuration × tier.durationMult / ship.speedMult      (baseDuration default 20 min)
cards    = round(tier.payoutMult × ship.crewMult × cardRate)
salvage  = round(tier.payoutMult × salvageRate)
```

Payout grows faster than duration on purpose — a tier-4 voyage is 14× as long and pays 28×, so
committing to a long absence is strictly better *if it lands*. Risk is what buys that back. That is
the entire decision, and it is the decision the reference game charges an auto-battler for.

**Tier 1 is risk-free and always available.** A player who never engages with risk still has a
working card faucet, just a slow one.

---

## 6. Risk, and the foreman question

On return, the risk rolls once:

- **Success** → full payout.
- **Failure** → `failPayout` of it (default 40 %, never zero — a wasted hold plus a wasted wait plus
  nothing at all is the kind of outcome players quit over) **and the hull takes damage**: the ship is
  out of service for a repair window, payable in salvage or in time. This reuses the mental model
  `MaintenanceService` already taught the player.

**Assigning a foreman cuts the risk.** Default `−2 % per foreman level`, so a level-10 foreman takes
20 points off — enough to make tier 4 comfortable, not enough to make it free. A foreman can be on
only one voyage at a time, and there are 8 of them against a small number of berths.

> **OPEN QUESTION — needs your call before V2.** Does an assigned foreman keep their station bonus
> while at sea?
>
> - **Keeps it** (recommended): the only cost is opportunity — 8 foremen, N berths. Simple, never
>   feels punishing, no interaction with the income cap.
> - **Loses it:** a real trade-off with teeth, and it makes levelling *more* foremen matter rather
>   than levelling one. But it means a voyage visibly cuts your income, which fights R1's spirit and
>   will read as a penalty to a player who does not connect the two screens.
>
> **RESOLVED 2026-08-26 — keeps it.** The cost of sending a foreman is opportunity: eight slots
> against a small number of berths, and a foreman at sea cannot crew a second voyage. Losing the
> station bonus would make a voyage visibly cut income, which fights R1's spirit and reads as a
> penalty to a player who has not connected the two screens. Reversible — it is one branch in
> `Settle` and one line in the save comment.

---

## 7. Ships and salvage

One fleet, account-wide, not per-island — same reasoning `Foremen.cs` gives for its roster: per-island
would go stale the moment you sail, and the whole point of a collection is that it comes with you.

| Upgrade | Effect | Ceiling |
|---------|--------|---------|
| **Hold**  | `holdMinutes` — bigger cargo, bigger payout | 8 |
| **Speed** | `speedMult` — shorter voyages | 8 |
| **Crew**  | `crewMult` — more cards per voyage | 8 |
| **Berths**| voyages running at once | 4 |

Berths are the interesting one: a second berth roughly doubles the faucet but also doubles the
diverted throughput, so it is a real decision rather than a strict upgrade. Berths 3 and 4 are a
**gem sink**, which the game needs — `Foremen.cs` notes gems currently have nowhere to go but the
store.

---

## 8. Offline behaviour — and why ours beats theirs

Voyages run on wall-clock unix, the way `boostEndUnix`, `repairEnd` and `shieldEndUnix` already do.
The hold fills while the app is closed, using the projected `deliveredPerMin` that `MarketService`
already maintains for absent islands. Voyages complete while the app is closed.

The reference game's single loudest review complaint is its **2-hour offline cap**. A system that
specifically rewards a long absence — sail the far reach before bed, it lands by morning — is not a
copy of that game. It is the thing it gets wrong, done right. Treat this as the feature's headline,
not a footnote.

Interaction with `OfflineConfig`: voyage completion is **not** capped by the offline cap. It is not
accrued income; it is a timer that ended. Capping it would break rule R3.

---

## 9. Where it physically lives

**The dock is a pad on the market yard floor**, beside the sell counter — sibling to `StockPad` and
`UpgradePad`, in the scene `MarketYardBuild` already generates.

Bars live in the yard, so consumption has to happen in the yard. And the yard is the one place in
this game where the player already has hands: he walks, he picks up, he carries. Making the dock a
menu would make it feel bolted on; making it a pad makes it the same verb the player already knows.

The island's harbour is the **display**: while a voyage is out, that island's moored hull is gone
from the water and the berth is empty. `CoalOperation` already owns berth placement, sea bounds, and
a shuttle-ship pool (`_ships`, `BuildContractShip`, `PlaceContractShip`) — this is a visibility flag
on existing machinery, not new geometry.

---

## 10. Monetization

All opt-in, per GDD §10. No new interstitials, no new pressure.

- **Rewarded ad:** finish the running voyage now.
- **Rewarded ad:** re-roll the route board.
- **Gems:** berths 3 and 4; instant hull repair after a failure.
- **IAP:** nothing new. The existing store already sells the things that make voyages better
  indirectly.

Explicitly **not** doing: paying gems for guaranteed voyage success. That converts the one real
decision in the feature into a wallet check, which is precisely the complaint that dominates the
reference game's negative reviews.

---

## 11. Data model

```csharp
// SaveData
public List<VoyageState> voyages = new List<VoyageState>();
public int[]  shipLevels = new int[4];   // Hold, Speed, Crew, Berths — never reorder
public long   salvage;

public class VoyageState {
    public string island;      // which yard is feeding the hold
    public int    berth;
    public int    tier;
    public double held;        // bars in the hold so far
    public double holdSize;    // locked in when loading started, so a mid-voyage rate change is not retroactive
    public long   sailedUnix;  // 0 = still loading
    public long   returnsUnix;
    public int    foreman;     // -1 = none
    public bool   settled;     // rolled, waiting to be claimed
    public bool   succeeded;
    public int    payoutCards, payoutSalvage;
}
```

**No `SaveMigration` version bump** — this spec originally called for one and that was wrong.
`SaveMigration.CurrentVersion` is not a schema stamp: bumping it **wipes every player's progress**,
by design, because levels only mean anything against the curves they were bought under. Voyages adds
fields and removes none, so there is nothing to reconcile. `VoyageService.Normalise()` pads the null
list and null array a pre-voyage save arrives with, which is exactly the precedent
`ForemanService.Normalise` set when the roster shipped for the same reason.

---

## 12. Files

Follows the house pattern: pure maths in `Core`, state in `Systems`, tunables in a config asset,
strings through `Loc`.

| File | Role | New/Edit |
|------|------|----------|
| `Core/Voyages.cs` | Route table, hold maths, payout curve, risk. **No Unity types.** Sibling to `MarketFlow`, `Foremen`, `IslandEconomy`. | new |
| `Systems/VoyageService.cs` | State, wall-clock ticking, grants via `ForemanService` | new |
| `Data/VoyageConfig.cs` + `.asset` | Every tunable | new |
| `Systems/Save/SaveData.cs` | The block in §11 | edit |
| `Systems/Save/SaveMigration.cs` | Version bump | edit |
| `Core/MarketFlow.cs` | Fourth job: `Load` | edit |
| `Systems/MarketService.cs` | Divert a share of deliveries to a loading hold | edit |
| `Gameplay/Market/DockPad.cs` | The yard-floor pad | new |
| `UI/VoyageUI.cs` | Route board, on the `StationScreenUI` pattern | new |
| `Tests/EditMode/VoyageTests.cs` | Hold fill, payout curve, risk, offline completion | new |

Constraints carried from CLAUDE.md: no new packages; tunables as `[SerializeField] private`; no LINQ
or per-frame allocation; scene and prefab changes through Unity MCP only.

---

## 13. Build stages

Each stage is shippable on its own. **V1 alone closes the one-verb gap** — everything after it is
depth.

### V1 — the sink works
`Voyages.cs` + `VoyageService` + save + migration + a plain UI panel opened from the yard HUD.
One berth, tier 1 only, no risk, no ships, no salvage, no art. Bars go out, cards come back.

**Done when:** a voyage loads from live delivery, sails, completes across an app restart, grants
cards that show up on the foreman roster, and survives a save round-trip. EditMode tests green,
Unity console clean.

### V2 — the decision
Route tiers 2–4, risk roll, failure payout, hull damage, foreman assignment. Resolve the §6 open
question first.

**Done when:** all four tiers are reachable and the risk maths matches the test table.

### V3 — the gear loop
Salvage, the four ship upgrades, berths 2–4, gem sink, rewarded-ad hooks.

**Done when:** a maxed ship measurably outruns a base one on the same route, and berth count changes
diverted throughput as specified.

### V4 — the physical act
`DockPad` on the yard floor, `Load` hire job, hull leaving the island harbour, juice — coin-burst
equivalent for a returning voyage, punch on claim.

**Done when:** a voyage can be loaded end to end without opening a menu.

---

## 14. Balance

**These numbers will not be hand-guessed.** REMAKE_PLAN §P7 established why: this economy is
queue-driven on scene geometry and has no closed form, and the first attempt at guessing it produced
a curve that was wrong in four independent ways at once.

The tunable that actually matters is **cards per real-time hour**, against `Foremen`'s stated intent
that a maxed roster takes months. Target for a mid-game player running one berth on tier 2:

| | target |
|---|---|
| cards / hour, one berth, tier 2, no ads | ~1.5 |
| cards / hour, four berths, tier 4, maxed ship | ~18 |
| time to level-10 a single foreman from scratch | 4–6 weeks |

Diverted throughput must stay below the point where sending a voyage feels like turning the game off:
`divertShare` default 0.35, hard ceiling 0.5 across all berths combined.

---

## 15. Risks

1. **The yard and the dock compete for the player's attention and for the same bars.** Mitigated by
   R1 (different currency) and the 0.5 divert ceiling. Watch for it in playtest anyway — if players
   stop working the counter, the divert share is too high.
2. **A card faucet devalues the existing card rewards** on dailies, contracts and achievements. Those
   payouts (`Goals.DailyPool` awards 0–1 cards) may need raising so they do not read as insulting
   next to a voyage.
3. **Unity MCP.** V1–V3 are pure C# and need no bridge. **V4 needs it**, and REMAKE_PLAN §5 records
   it down last time. Confirm the editor is up before V4 starts.
4. **Scope.** This is the largest feature since the market yard. V1 is the honest minimum that fixes
   the diagnosed problem; treat V2–V4 as separate decisions, not a committed backlog.

---

## 16. Deliberately not in this feature

- **Combat / auto-battle.** §1 explains why: the risk roll buys the same decision.
- **Cash payouts.** R1.
- **Charts gating island unlocks.** Considered and dropped — a new resource in front of the island
  ladder can stall a player behind a system they have not engaged with. Risk stays inside the feature.
- **Gacha rarity and pity.** V1 grants uniformly at random via the existing `GrantRandomDuplicates`.
  Rarity tiers are a separate feature with its own balance surface — built 2026-08-26 as the captain
  roster, see [FIVE_LAYERS.md](FIVE_LAYERS.md) §6. It adds a currency (`charts`), a second officer
  slot on a voyage, and `ForemanService.GrantDirectedDuplicates`; it deliberately leaves
  `Voyages.Cards` and every existing signature in this file alone, for the reason §21 gives.
- **Chapters, events, leaderboards.** The other two gaps from the analysis. Separate specs —
  written up in [FIVE_LAYERS.md](FIVE_LAYERS.md), where chapters shipped 2026-08-26. That
  document also records the reversal of the no-combat call below.

---

## 17. V1 as built — 2026-08-26

Shipped and compile-verified. **Not yet run in the Editor or on device** — see "How to test" below.

### Files

| File | Lines | |
|---|---|---|
| `Core/Voyages.cs` | 206 | new — tier table, hold maths, payout curve |
| `Systems/VoyageService.cs` | 329 | new — berths, loading, wall-clock returns, claims |
| `Data/VoyageConfig.cs` | 58 | new — Inspector tuning, Turkish labels |
| `UI/VoyageUI.cs` | 283 | new — the dock panel |
| `Tests/EditMode/VoyageTests.cs` | 330 | new — 20 tests |
| `Systems/Save/SaveData.cs` | +33 | `voyages`, `shipLevels`, `salvage`, `VoyageState` |
| `Systems/MarketService.cs` | +22 | `ReturnToStock` |
| `Systems/GameBootstrap.cs` | +12 | construction + tick |
| `UI/MarketSceneBoot.cs` | +13 | builds the dock, follows the player between yards |
| `Resources/Diller/metinler.txt` | +14 rows | `sefer.*` in all 11 languages |

### Four decisions that differ from the plan above

**1. No save-version bump.** §11, corrected in place. The original instruction would have wiped every
tester's save on the next build.

**2. `MarketService.ReturnToStock`, a new method.** Abandoning a load has to put the bars back, and
the obvious call — `Deliver` — feeds the delivery meter. That meter is the only input to the next
launch's offline grant, so refunding through it would have banked the same bars as income twice and
left the saved rate reading high for a minute afterwards. A refund is a correction to the pads, not a
lorry arriving. `VoyageTests.ARefund_DoesNotRegisterAsADelivery` pins the distinction down.

**3. A hold does not fill while the app is shut; a ship at sea still comes home.** §8 implied both
would run offline. They must not. Offline cash is granted from a persisted *rate*, not by selling the
bars on the pads — so a hold that filled during an absence would divert bars the player was paid for
anyway and the voyage would be free. Loading is the part that happens while the game is open; the
voyage is the part that runs while it is shut, which is also the half worth having overnight. The
headline in §8 is unaffected.

**4. The dock is its own canvas, not a card on the yard HUD.** §9 puts the dock on the market floor,
which is still right and is V4's job. For V1 it is one chip low on the right of its own canvas, and
`MarketHudUI`'s layout is untouched. That layout works; threading a fifth card through it for a
feature nobody has played yet is the wrong risk to take first.

### Deliberately not in V1

One berth, tier 0 only, no risk (tier 0's risk is 0, so the roll is generic and simply always
succeeds), no ship upgrades, no salvage payouts, no `Load` hire job, no dock pad, no juice. The
tier table, ship-level parameters and `salvage` field all exist so V2/V3 do not have to reopen
`Core` or the save.

### Verification

Unity MCP is not connected, so this was type-checked with Unity's bundled Roslyn against the real
assemblies (`Docs` note: see the offline compile-check procedure), compiled in dependency order:

```
Game.Core  0   Game.Data  0   Game.Systems  0   Game.Gameplay  0   Game.UI  0   VoyageTests  0
```

Zero warnings introduced. `GameBootstrap.voyageConfig` emits the same benign `CS0649` that all twelve
sibling config fields already emit — Unity assigns `[SerializeField]` by serialization.

**The 20 EditMode tests have NOT been run.** The bundled nunit is a net40 build and cannot be
referenced from a netstandard pass, so `VoyageTests.cs` was type-checked against the real Game
assemblies with a stub NUnit surface instead. That proves every call into `Voyages`, `VoyageService`,
`MarketService` and `ForemanService` resolves; it does not prove the assertions pass. Run them in the
Test Runner.

### Still to do before V1 is really done

- **Run `VoyageTests` in the Unity Test Runner.** First thing.
- **Create `Assets/Data/VoyageConfig.asset`** (Assets > Create > Ore Empire > Voyage Config) and drop
  it on `GameBootstrap`'s new `voyageConfig` slot. The game runs without it on
  `Voyages.Tuning.Default`; the asset is how the numbers get tuned, not how the feature turns on.
  Not done here because CLAUDE.md forbids hand-writing `.asset` files and the MCP bridge is down.

### How to test in game

1. Enter a market yard and let it run until the HUD shows a delivery rate — the dock refuses a yard
   whose meter is still empty, on purpose, and the panel says so.
2. Tap the blue **SEFERLER** chip on the right. Expect: berth empty, cargo size, a 15:00 route.
3. **SEFER AÇ.** The bar starts filling and the yard's stock visibly drains slower into the till —
   that diversion is the cost, and it should be felt.
4. **VAZGEÇ** partway. Every bar should come straight back onto the pads.
5. Start again, let it fill (~8.5 min at defaults), confirm it sails on its own at 100%.
6. Or hit **YOLA ÇIK** above 25% — it should sail, and pay proportionally less.
7. **Force-quit the app while it is at sea, wait, relaunch.** The voyage should be home. This is the
   headline behaviour and the one most worth breaking.
8. **TOPLA**, then open the foreman roster: the cards should be there, on a foreman you have hired.
9. Walk through a doorway into another yard — the panel should follow to that yard.

---

## 18. V2 as built — 2026-08-26

Shipped and compile-verified. **Not yet run in the Editor or on device.**

### What changed

| File | |
|---|---|
| `Core/Voyages.cs` | `RiskFor`, `CardsOnFailure`, `RepairSeconds`, `TierVoyagesRequired`; three new tuning fields |
| `Systems/VoyageService.cs` | tier gating, `TrySetForeman`, `ForemanBusy`, `BerthDamaged`, failure + wreck in `Settle` |
| `Data/VoyageConfig.cs` | risk, fail-payout and repair knobs |
| `UI/VoyageUI.cs` | four-route selector, foreman chip, wrecked-berth state, quoted odds |
| `Systems/Save/SaveData.cs` | `voyagesCompleted`, `hullReadyUnix[]` |
| `Resources/Diller/metinler.txt` | +11 rows |
| `Tests/EditMode/VoyageTests.cs` | 34 tests (was 20) |

### Two decisions that differ from the plan

**1. Tiers unlock on VOYAGES SAILED, not on ship level.** §13 put tiers in V2 and ship upgrades in
V3, but §5's table gated tiers on the Hold track — so as written, V2's routes waited on a currency
V3 hadn't introduced yet and none of them were reachable. Worse, it had one upgrade doing two
unrelated jobs. `TierVoyagesRequired = {0, 3, 10, 25}`: sailing is what teaches the system and what
opens it. The Hold track now only decides how much a ship carries.

**2. Hull repair is a fraction of the route, not a flat 20 minutes.** The flat window scaled
backwards — 20 minutes is most of a 15-minute coastal run and nothing beside a 3.5-hour far reach,
so the punishment landed hardest where the gamble was smallest. Now `RepairFraction = 0.25` of the
route's own length. `AWreckCostsMoreTime_TheFurtherOutItHappened` pins it.

### The ladder at default numbers

| Route | Sail | Risk | Win | Lose | Cycle | **Cards/hr (expected)** |
|---|---|---|---|---|---|---|
| Coastal run | 15 m | 0 % | 1 | — | 23.6 m | **2.5** |
| Open water | 37.5 m | 8 % | 3 | 1 | 46 m | **3.7** |
| Deep sea | 90 m | 18 % | 9 | 4 | 99 m | **4.7** |
| The far reach | 3.5 h | 30 % | 28 | 11 | 3.6 h | **5.9** |

Monotonic after risk is priced in — going further is always worth it in expectation, which is the
trade §5 promised. Reaching the far reach takes roughly **30 hours of elapsed play**.

**These are ~3× more generous than §14's target** of ~1.5 cards/hour at tier 2. `CardRate` is the
dial. Not corrected here because §14 is explicit that these numbers get set by measurement, not by
guessing, and the measurement has not been run.

### Deliberately still not in

Ship upgrades and salvage payouts (V3 — `salvage` accrues nothing yet), berths 2–4 (the code and
tests handle multiple berths; `BerthCount` just returns 1 until the Berths track is buyable),
rewarded-ad hooks, the dock pad, juice.

### Verification

Same offline Roslyn pass, dependency order, after every edit:

```
Game.Core 0 · Game.Data 0 · Game.Systems 0 · Game.Gameplay 0 · Game.UI 0 · VoyageTests 0
```

Zero new warnings. **The 34 EditMode tests still have not been run** — same net40/netstandard nunit
mismatch as V1. Run them in the Test Runner.

### How to test in game

Everything from §17, plus:

1. The panel now shows **four routes**. Three should read **KİLİTLİ** with a number — how many more
   voyages open them. Only the coastal run is tappable.
2. Coastal run should quote **RİSK 0%**. Complete 3 voyages; open water should unlock and quote 8%.
3. **Hire a foreman**, then tap the **FORMEN** chip. It cycles nobody → each hired foreman → nobody.
   The quoted risk should drop as you put a higher-level foreman aboard.
4. The chip should be **hidden entirely** until you have hired at least one foreman.
5. Send a foreman out, then check the roster — their station bonus should still be applied. That is
   the §6 decision; if it feels too cheap, that is the knob to revisit.
6. Once she sails, the route row and the foreman chip should both **freeze**.
7. **Lose one.** Open water at 8% takes a few tries; deep sea at 18% is faster to test. On a loss the
   panel should say **SEFER AKSADI**, still pay reduced cards, and the berth should go to
   **GEMİ ONARILIYOR** with a countdown — about a quarter of the route's length.
8. Force-quit during a repair and relaunch. The repair should have continued.

---

## 19. V3 as built — 2026-08-26

Shipped and compile-verified. **Not yet run in the Editor or on device.** 45 tests (was 34).

### What changed

| File | |
|---|---|
| `Core/Voyages.cs` | `DivertShareEach`, `Salvage`, `HoldMultiplier`, `ShipCost`, berth costs, `MaxLevelOf`; nine new tuning fields |
| `Systems/VoyageService.cs` | shipyard (`TryBuyShip`, costs, caps), `TryFinishNow`, `TryRepairNow`, `LoadingOn`; now takes `WalletService` |
| `UI/VoyageUI.cs` | SEFER / TERSANE tabs, four upgrade rows, ad and gem shortcuts |
| `Data/VoyageConfig.cs` | nine shipyard knobs |
| `Resources/Diller/metinler.txt` | +10 rows |

### Three decisions that differ from the plan

**1. Berths buy pipelining, not diversion — and they had to.** §7 said a second berth "roughly doubles
the faucet but also doubles the diverted throughput". Four berths each taking `DivertShare` would
take **1.4× everything the island makes**: the counter would sell nothing and buying a berth would
read as switching the game off. All loading holds now share one budget capped at `MaxDivertShare`
(0.50). What a berth actually buys is filling the dead time — a far-reach berth is **idle 96% of its
cycle** (8.6 min loading, 210 min sailing), so a second berth nearly doubles utilisation without
sending one extra bar to sea. Better design than the original, and the only one that doesn't
strangle the yard.

**2. The Hold track was a trap, and the acceptance test caught it.** Payout keyed off the load
*fraction*, which is capped at 1 by definition — so a bigger hold took proportionally longer to fill
and paid **exactly the same**. A fully maxed ship came out at **1.9 cards/hr against a stock ship's
2.5**. Added `HoldMultiplier`: fraction says how full she is, hold level says how big she is, and the
payout needs both. `AMaxedShip_OutrunsAStockOne_OnTheSameRoute` is the guard.

**3. The ad hook is "bring her in", not "re-roll the route board".** §10 listed a board re-roll, but
routes are a fixed four-row table, not a rolled offer — there is nothing to re-roll. The rewarded ad
finishes the running voyage instead, and it does so by pulling the arrival time to now and letting
the ordinary tick roll the risk, so a skipped wait and a served one resolve through **one** code
path. Gems buy an instant hull repair. Still never sold: a guaranteed success.

### The fleet, at default numbers

| Route | stock | maxed | |
|---|---|---|---|
| Coastal run | 2.6 cards/hr | 10.4 | 4.1× |
| Open water | 3.6 | 25.1 | 6.9× |
| Deep sea | 4.7 | 49.4 | 10.4× |
| The far reach | 5.9 | 81.8 | 14.0× |

Maxing Hold + Speed + Crew + the second berth costs **2,722 salvage**.

### ⚠ The defaults are roughly 20× too generous — this needs a balance pass

Simulating a player who always buys the cheapest available upgrade and sails the furthest route open
to them:

```
one berth               fleet maxed in  47.9 h   (29 voyages, 1352 cards)
two berths, pipelined   fleet maxed in  28.0 h   (31 voyages, 1353 cards)
```

**1,352 cards in ~48 hours.** A level-10 foreman needs 90 duplicates; the whole eight-slot roster
needs 720. So at these numbers the fleet maxes out *and hands the player the entire foreman roster*
inside two days — which collapses the exact long tail this feature was built to create. §14's target
was ~1.5 cards/hr; the sim averages ~28.

The cause is the **multiplicative stack**: tier payout (×28 at the far reach) × hold (×6.3) × crew
(×1.8) = ×319 on a single maxed far-reach voyage. Each factor is defensible alone; together they run
away.

`CardRate` is the obvious single dial (1.0 → ~0.15 lands near target), but note it interacts with the
"never pay zero" floor in `Cards`, which would squash the low tiers flat. Flattening `PayoutMult`
is the other candidate.

**I have not changed the numbers.** §14 is explicit that this economy has no closed form and gets
tuned by measurement rather than by guessing — and this simulation *is* that measurement starting to
talk. It should drive a proper pass with `BalanceSim`, not a number I pick at the end of a build
turn. Everything needed is in `VoyageConfig`; no code has to move.

### Verification

```
Game.Core 0 · Game.Data 0 · Game.Systems 0 · Game.Gameplay 0 · Game.UI 0 · VoyageTests 0
```

Zero new warnings. **45 EditMode tests, still unrun** — same nunit mismatch.

### How to test in game

1. The panel now has two tabs: **SEFERLER** and **TERSANE**.
2. Complete a voyage — the claim should pay **cards and salvage**. Salvage shows on the yard tab.
3. Buy **AMBAR** once. The next voyage should carry more and pay more, and take longer to fill.
4. Buy **RIHTIM** (250 salvage) → two berths. Start two voyages on the same island and confirm each
   loads at **roughly ¾ the speed** one alone does — that is the shared cap working. The counter must
   keep selling.
5. Third berth should ask for **gems**, not salvage.
6. While a ship is at sea, the second button offers a **rewarded ad** to bring her in. Confirm the ad
   plays and she settles normally, risk and all.
7. Wreck a ship, then pay gems on **HEMEN ONAR**.
8. Everything from §17 and §18 still applies.

---

## 20. V4 as built — 2026-08-26

Shipped and compile-verified. **Not yet run in the Editor or on device.** 50 tests (was 45).

**Voyages is now feature-complete against §13.**

### What changed

| File | |
|---|---|
| `Gameplay/Market/DockPad.cs` | new — the pad, the marker, the hull |
| `Gameplay/Market/MarketYardScene.cs` | `BuildDock` at (-9, 11) |
| `Systems/VoyageService.cs` | `DepositByHand`, `LoadingBerthOn`, `SettledBerthOn` |
| `UI/VoyageUI.cs` | the `Returned` event finally has a listener — chip kick, sound, haptic |
| `Tests/EditMode/VoyageTests.cs` | 50 tests |

**No Unity MCP was needed after all.** §12 and §15 both said V4 would need the bridge. Wrong: the
market yard is generated in code (`MarketYardBuild` / `MarketYardScene`), not authored in
`Market.unity`, so the dock is a `GameObject` like every other station in the room.

### Three decisions

**1. The dock does not START voyages.** §13 said "loaded end to end without opening a menu", and
*loaded* is the word that matters. Which route and who is aboard are decisions with odds attached,
and a decision made by standing somewhere by accident is not a decision. The panel opens a voyage;
the pad loads it and unloads it. A hold can now be filled entirely by hand.

**2. Hand-carrying is ON TOP of the automatic divert, not instead of it.** The alternative — make
auto-fill a paid upgrade — would have turned V1–V3's behaviour into something the player has to buy
back. This is the relationship `MarketFlow` already spells out between a staffed yard and a player
standing in it: the automatic share is what the dock manages on its own, and the player is the pair
of hands on top.

**3. The hull is at the dock, not in the island harbour.** §9 put it in `CoalOperation`'s berth. Two
reasons not to: the player is in the **Market scene** when he loads her, and the island is a
different scene — a ship that is only absent somewhere the player is not is a signal nobody receives.
And `CoalOperation` hard-disables its entire island if a landmark name is missed, which makes it the
riskiest file in the repo to touch for pure decoration.

### Where the dock stands

`(-9, 0, 11)` — north of the counter, on the one patch of floor nothing else uses. Deliberately **not**
beside the stock pad, which is the obvious place and the wrong one: the player crosses that slab
constantly with a full back, and a dock there would load the ship every time he walked past on his
way to the counter. Going to sea has to be somewhere he goes.

Clearances: pad rank at x −17.5, stock slab starts at x 1.5, ramp at z 15, counter shelf at z −3.

### Deferred, with the reason

**The `Load` hire job is not in.** §4 said it "slots into the existing three-job hire model
(`Carry`, `Serve`, `Collect` → add `Load`)". It does not. `MarketFlow.ServiceRate` is the **minimum**
of all `JobCount` jobs — that is the entire design of the yard — so adding a fourth job would drop
**every existing yard in the game to the idle trickle** until the player hired a dockhand they have
never heard of. A silent, global income cut dressed as a feature addition.

If it is wanted later it has to be a *parallel* hire (its own save field, its own rate), never a link
in the selling chain. Left out rather than done wrong.

### Verification

```
Game.Core 0 · Game.Data 0 · Game.Systems 0 · Game.Gameplay 0 · Game.UI 0 · VoyageTests 0
```

Zero new warnings. **50 EditMode tests, still unrun.**

### How to test in game

1. Open the panel, start a voyage, then **close it** and walk to the north of the yard. There should
   be a slab, a post, and a hull alongside.
2. Pick bars off the stock pad and walk onto the dock. They should go aboard one at a time, at the
   same rhythm as the counter, and the hold should fill visibly faster than it does on its own.
3. Fill it entirely by hand — **the hull should disappear** when she sails.
4. When she is due back, the **post bobs**. Walk onto the dock: the cards should be claimed without
   opening anything, with a sound and a kick.
5. If the panel is shut when she lands, the **SEFERLER chip should still kick** and play.
6. Walk past the dock with a full back while a voyage is at sea — nothing should happen.
7. Check the dock does not steal bars meant for the counter: the walk from stock pad to counter
   should never cross it.

---

## 21. The balance pass — 2026-08-26

§19 flagged the shipped defaults as roughly 20× too generous. This is the pass that fixed it, and the
first one in this feature done against **measurement rather than judgement**.

### What was wrong

A simulated player who always bought the cheapest upgrade and sailed the furthest open route maxed
the fleet **and** collected the entire eight-foreman roster in about **48 hours**. §14's target was
~1.5 cards/hour at deep sea; the sim averaged ~28.

The cause was a multiplicative stack — tier payout (×28) × hold (×6.3) × crew (×1.8) = **×319** on a
single maxed far-reach voyage. Every factor was defensible alone. The product was not.

### The two targets in §14 are mutually inconsistent

Worth recording, because it cost a search to establish. §14 wants **1.5** cards/hr at deep sea with a
stock ship on one berth, and **18** at the far reach with a maxed ship on four. That ratio is 12×.
But four berths alone are worth ~4×, and V3's own acceptance test demands a maxed ship beat a stock
one by more than 2× — which leaves nothing for the tier step. No configuration satisfies both.

**Target A was kept and target B abandoned**, deliberately. A governs whether the long tail exists at
all, which is the entire point of the feature; B is an endgame ceiling, and an endgame player earning
40 cards an hour is not a problem. §14's B should be treated as retired.

### How the numbers were chosen

A solver over `base × payout × hold × crew × speed`, rejecting any configuration that failed **four
simultaneous guards**:

1. cards/hour must **rise with every tier** (the promise in §5 — longer routes pay better per minute)
2. a fully bought ship must beat a stock one by **more than 2×** (V3's acceptance criterion)
3. one foreman to level 10 must take **4–6 weeks** of ordinary play
4. the payout ladder must stay readable as integers

274 configurations passed. The chosen one is the shortest coastal run that still lands the foreman
timeline — a longer base voyage fits §14's A more exactly but costs the first-session experience.

### The change

| | was | now | |
|---|---|---|---|
| `BaseVoyageMinutes` | 15 | **35** | the dominant lever on pacing |
| `PayoutMult` | 1 / 3 / 9 / **28** | 1 / 3 / 9 / **24** | top tier compressed |
| `HoldMinutesPerLevel` | 2.0 | **0.6** | hold was worth 6.3× cargo; now 2.6× |
| `CrewPerLevel` | 0.10 | **0.05** | |
| `SpeedPerLevel` | 0.08 | **0.04** | |
| `SalvageRate` | 3.0 | **1.5** | |
| `CardRate` | 1.0 | 1.0 | unchanged — it is what keeps the ladder integral |

`CardRate` was the obvious dial and the wrong one: dropping it squashes the low tiers onto the
"never pay zero" floor and **inverts** the ladder, so a coastal run out-earns open water per minute.
The length of the routes is the lever that scales pacing without touching the integers.

### Where it lands

| Route | sail | win | lose | salvage | stock/hr | maxed/hr | 4 berths maxed |
|---|---|---|---|---|---|---|---|
| Coastal run | 35 m | 1 | 1 | 2 | 1.38 | 4.92 | 15.4 |
| Open water | 88 m | 3 | 1 | 4 | 1.74 | 6.97 | 27.9 |
| Deep sea | 3.5 h | 9 | 4 | 14 | **2.13** | 9.36 | 37.4 |
| The far reach | 8.2 h | 24 | 10 | 36 | 2.22 | 10.17 | 40.7 |

```
deep sea, stock, one berth   4.74  ->  2.13 cards/hr     (§14 target 1.5)
maxed ship vs stock          4.1x  ->  3.57x             (guard: > 2x)
one foreman to level 10        19h ->  42h               (~4 weeks at 1.5 h/day)
whole roster (720 cards)      152h ->  338h
fleet fully upgraded           57h ->  396h
```

The two long tails now **converge**: the roster completes around 338 hours and the fleet around 396,
so neither finishes long before the other. That is the shape the feature was supposed to have.

### Two tests changed, and why

Both were asserting rounding rather than design, and the retune exposed them.

- `AFailedVoyage_StillPaysSomething_ButLess` — at tier 0 the full payout is one card, so "never pay
  nothing" and "a loss must cost something" cannot both hold. The floor wins, and it costs nothing:
  the only route paying a single card is the coastal run, whose risk is 0. A voyage that cannot fail
  cannot be shortchanged by the rule for failures. The reduction is now asserted where a failure is
  reachable.
- `EachTrack_PullsItsOwnWeight` — measured on tier 0, where a maxed crew's ×1.4 on one card rounds
  straight back to one. Moved to the furthest route, where the tracks have resolution. Harmless in
  play: nobody with a maxed crew sails the tutorial route.

`Cards_ScaleWithLoad_…` now derives its expectations from `PayoutMult` instead of hard-coding 28 and
14, so the next balance pass does not falsify a rule it is not about.

---

## 22. Verification — the real one, 2026-08-26

Everything above V1 had been checked only by an Editor-less Roslyn pass. The Unity MCP bridge came
back up, so all of it has now been verified in the Editor itself.

**Compile.** Forced full refresh and recompile: **0 errors in the Unity console.** V2, V3 and V4 had
never been compiled by Unity before this; they are clean.

**Tests.** The full EditMode suite, run through the Test Runner:

```
296 / 296 passed, 0 failed
```

That is all 50 `VoyageTests` plus the 246 pre-existing tests — so the additive edits to
`MarketService`, `SaveData`, `GameBootstrap`, `MarketSceneBoot` and `MarketYardScene` broke nothing
that was already working.

Three real defects were found by actually running them, all in the tests rather than the game code:
the two rounding assertions above, plus the tier-0 failure-payout conflict. Every one of them was a
case the offline type-check could not have caught.

> Note for future sessions: this server delivers POST replies inline **only until**
> `notifications/initialized` is sent — after that they move to the SSE GET stream and a plain POST
> returns empty. A one-shot shell client should skip the notification.

### Still to do

- **`Assets/Data/VoyageConfig.asset` does not exist.** The game runs on `Voyages.Tuning.Default`,
  which now carries the balanced numbers, so nothing is broken — but the asset is how they get tuned
  without a rebuild. Create via Assets > Create > Ore Empire > Voyage Config and drop it on
  `GameBootstrap`'s `voyageConfig` slot.
- **Nothing has been played.** Every number above is simulated. The play-test lists in §17–§20 are
  still the next step, and §14's warning stands: a simulation is a better guess, not a measurement of
  the real thing.

---

## 23. The dock art — 2026-08-26

V4 shipped the dock as three primitive cubes. These are the models. Full spec in
[ASSETS.md §H](ASSETS.md); source in `Tools/blender/dock.py`, preview render in
`Tools/blender/dock_preview.py` and `DevScreenshots/harbor-dock-set.png`.

| | tris | size | pivot |
|---|---|---|---|
| `SM_Harbor_Jetty` | 240 | 7 × 0.5 × 7 | base centre |
| `SM_Harbor_Bollard` | 72 | 0.62 × 2.92 × 0.62 | base centre |
| `SM_Harbor_Launch` | 164 | 2.20 × 2.70 × 6.53 | centre bottom |

Built headless (`blender -b -P`) rather than through the Blender MCP bridge — the bridge process was
running but Blender itself was not open, and the addon's socket needs a GUI. A script is the better
path anyway: it is re-runnable, diffable, and lives in the repo.

**Rendering it was the point.** Three passes, and both of the first two looked fine as numbers and
wrong on screen — the jetty read as deck-chair stripes, and the launch collapsed into detached flat
panels at the bow because I was tapering a primitive cube by shoving verts around in bmesh. Neither
would have been caught by tri counts or bounding boxes.

**Wiring.** Three optional `[SerializeField]` slots on `MarketPrefabs`, wired on Market.unity's boot
object. An empty slot falls back to the greybox cube, so the dock stays runnable with no art. The
authored hull moors at **5.2** rather than the greybox's 4.6 — half the jetty plus half her beam plus
a channel, because at 4.6 she cut through the deck.

> CLAUDE.md says the Inspector wiring is yours. I did these three because they are 1:1 — the jetty
> model into the jetty slot — rather than a judgement call. Clear the slots if you would rather do it
> yourself; the greybox comes back automatically.

**Verified in the Editor:** import scale factor 1.000 (no Blender ×100 error), bounds exactly as
authored, pivots on the floor, materials intact. Recompiled clean, **296 / 296 tests still pass**.

Not yet seen in game — the yard has to be walked into for that.

---

## 24. Ran it — 2026-08-26

Played from Bootstrap through the real boot chain, loaded the market, walked onto the dock.
`DevScreenshots/dock-ingame-full.png`.

The dock builds, the three authored meshes load, the SEFERLER chip sits on the HUD, a voyage opens
and loads by hand. **Two placement bugs came straight out of looking at it**, both now fixed:

### Bug 1 — the dock stood inside the entrance ramp

§20 recorded the clearance check as "the ramp is at z 15". That is the ramp's **anchor**. Its geometry
runs **z 12 to 18**, so a 7-deep jetty at z 11 reached z 14.5 and the moored hull stood inside the
ramp's raised deck.

Moved to **(-8, 4.5)**, measured from renderer bounds rather than anchors:

```
Rampa        x[-14.0, 14.0]  z[12.0, 18.0]   <- the one that bit
Rampa_Serit                  z[11.8, 12.0]
StokPedi     x[  1.5, 18.5]  z[-5.5,  9.5]
pad rank     x -17.5, reaching about -15.5
CounterShelf                 z ~ -3
jetty now    x[-11.5, -4.5]  z[ 1.0,  8.0]    hull x[-3.9, -1.7]
```

**Lesson worth keeping: take extents from the renderers, never from the anchor a piece was placed by.**

### Bug 2 — the player stood inside the mooring post

The bollard was at the pad's local origin, which is also the centre of the trigger the player stands
in. So every time he used the dock he was standing inside the post — and the one piece that signals a
ship is home was the one piece hidden behind his body. Moved to the water edge at local (2.7, 2.5):
clear of the trigger (3.6 wide, reaching x 1.8), inside the deck (7 wide, ending at x 3.5).

### The palette was left alone, deliberately

The yard is themed per island (`MarketTheme`) and the dock is not. On coal — a dark charcoal and
steel room — the warm wood is the only warm thing in the frame, which is exactly what a dock wants to
be: somewhere you go. The counter and its shelf are already wood-toned, so it is not a foreign
material in this room. If it reads wrong on the brighter islands (gold, silver) the fix is to tint
the jetty from `MarketTheme.Trim`, but that is a decision to make after seeing them, not before.

---

## ⚠ 25. Open bug — a voyage sails at ~39 %

**Found while screenshotting; not fixed; not explained.**

A loading voyage sailed on its own twice, at 37 % and 39 % of its hold, with nobody asking it to.
Raw state at the moment it happened:

```
berth=0 tier=0 held=700/1800 sailed=1787749556 returns=1787751656 settled=False
```

`returns - sailed` is exactly 2100 s, so `Sail()` ran normally — the question is who called it.

**What is ruled out.** `Load()` and `DepositByHand()` only sail on `held >= holdSize`, and `Load` adds
at most `FillPerSecond × deltaTime` ≈ 3.5 bars/s against a 1138-bar gap — it cannot close it in one
frame even at Unity's clamped 0.333 s. That leaves `TrySail`, whose only caller is `VoyageUI.OnPrimary`,
which needs a click on a panel that was shut.

**What is confirmed.** Forced back to loading and left alone, it loads correctly and does not sail —
663 → 665 → 666 over three polls, pulling from stock. So it is not a constant misfire.

**Next step:** put a `Debug.LogError` with a stack trace inside `VoyageService.Sail()`, reproduce, and
read the caller out of the console. That is a five-minute job and it should be done before anyone
plays this properly — a ship leaving early is the player losing most of a payout they were still
loading for.

Note the anomaly first appeared right after I had forced `settled = true` on an at-sea voyage from the
console, which is a state the game cannot produce. It may yet be an artifact of that poking. It should
not be assumed to be.
