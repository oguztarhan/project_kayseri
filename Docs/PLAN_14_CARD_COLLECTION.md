# Plan 14 — Card collection

**Status:** approved 2026-09-09 · implementation in progress
**Scope:** a third collectible domain beside masters and captains, always active, never equipped.

## Product decisions

- **Cards are always active.** There is no deck, loadout, assignment or "best card" choice. The first
  copy unlocks a card at level 1; further copies are duplicates spent on that card's own levels.
- **Cards arrive from gameplay only** — one free pack per UTC day, plus packs on selected weekly
  milestones and achievement tiers. No paid packs, no gem packs, no rewarded-ad packs in v1.
- **Set completion has two independent outcomes.** The permanent set bonus activates the moment the
  last card is owned and is derived from completion forever after; the one-time reward is claimed
  separately. A player who never taps "Claim" must not lose the bonus.
- **Nothing existing is replaced or reset.** Masters, captains, mining gear, crafting and the sea
  ladder keep every number they own.

### Reversing Plan #05

`PLAN_05_HERO_ROSTER_AND_COLLECTION.md` shipped on 2026-09-04 with the scope line "do not create a
third hero system" and the decision that collection/set bonuses were "deliberately out of the first
release… another multiplier risks mandatory compositions. If introduced later, a bonus must be
derived from current owned/level state, data-driven, and add no save fields."

That decision is **deliberately reversed here**, and the reasoning matters more than the reversal:

- The **mandatory-composition worry is answered by the design, not waived.** Plan #05's fear was a
  bonus you had to assemble — the wrong five cards costing you a multiplier. Cards here are always
  active and can never be selected, so there is no composition to get right or wrong. Owning more is
  monotonically better and owning less is never a mistake.
- **Derived from owned/level state:** met. Every effect is `perLevelValue × level` summed over owned
  cards, plus a set bonus derived from completion. Nothing is stored that could disagree with it.
- **Data-driven:** met. The whole catalogue, curve and weight table live in `CardCollectionConfig`.
- **Adds no save fields:** **not met.** Ownership, duplicates and pity counters cannot be derived
  from anything already saved. This is the one condition the feature genuinely breaks, and it is
  accepted knowingly rather than overlooked. The addition is one nested object, on the same
  no-version-bump precedent as every other block appended to `SaveData`.

Anyone reading Plan #05 later should find this section; anyone reading this section should not have
to guess whether Plan #05 was considered.

### Effects cut before implementation

`OfflineIncomeMultiplier` was in the drafted effect surface and is **not implemented**.
`GameBootstrap.GrantOffline` builds efficiency as `OfflineConfig.Efficiency (0.50) +
SaveData.offlineEfficiencyBonus (bought in the store) + ForemanService.OfflineBonus`, then clamps the
sum to `1.0`. A maxed master roster contributes exactly `+1.00` on its own, so a late-game player is
already over the ceiling before a collection says anything. A collection offline bonus would be
invisible to precisely the players who own a collection, and would silently cancel the *purchased*
offline perk for anyone near the cap. The v1 surface is therefore five effects, not six.

## Balance

All values below are Inspector-editable in `CardCollectionConfig`; the numbers here are the launch
defaults and the reasoning behind them, not constants in code.

### Pack odds and pity

One card per pack. The daily pack and milestone packs share one weight table — a second table would
double the balance surface and the odds sheet for no gameplay difference.

| Rarity | Weight | Effective at launch | Cards | Per named card |
|---|---:|---:|---:|---:|
| Common | 0.520 | 52.3% | 9 | 5.81% |
| Rare | 0.280 | 28.1% | 6 | 4.69% |
| Epic | 0.140 | 14.1% | 6 | 2.34% |
| Legendary | 0.055 | 5.5% | 3 | 1.84% |
| Mythic | 0.005 | — | 0 | — |

Weights are relative and normalised over rarities that actually carry cards, so Mythic's weight is
inert until a Mythic card is authored — the same rule `CaptainCrate.WeightOf` already applies to a
grade nobody in the roster carries. The odds sheet must not print a 0% row: a rate being hidden and
a rank that does not exist look identical to a player.

**Pity.**

- **Epic hard pity 15.** `pullsSinceEpic` advances on Common and Rare; resets on Epic or better.
- **Legendary hard pity 50.** `pullsSinceLegendary` advances on Common, Rare and Epic; resets on
  Legendary or better.
- **Legendary soft pity from 35, +0.015 weight per pull.** By pull 49 the Legendary weight has
  climbed 0.055 → 0.265, so most dry runs end before the hard guarantee fires.
- **Overlap.** Both counters can come due on one pull. `Floor()` tests Legendary first and returns
  the higher floor; a Legendary also satisfies the Epic guarantee, so `Advance()` clears both. A
  player owed two guarantees is paid once — this is `CaptainCrate.Advance` copied deliberately.

**Simulated, 2,000 runs per horizon** (`Docs` records the result; `CardCollectionPackTests` asserts
the same distribution deterministically by sweeping the unit interval):

| Opens | Common | Rare | Epic | Legendary |
|---:|---:|---:|---:|---:|
| 100 | 51.64% | 27.91% | 14.53% | 5.92% |
| 1,000 | 51.64% | 27.72% | 14.49% | 6.14% |
| 10,000 | 51.60% | 27.74% | 14.49% | 6.18% |

Pity lifts realised Legendary from the 5.53% base to 6.18% — a 12% uplift, which is the intended
size. A guarantee that visibly moves the headline rate is a guarantee being sold twice.

| Packs to first | Mean | Median | p95 | Worst (guaranteed) |
|---|---:|---:|---:|---:|
| Epic or better | 4.9 | 4 | 14 | 15 |
| Legendary or better | 16.2 | 13 | 43 | 50 |

### Pacing

Two profiles, 20,000 simulated players each, 180 days. Engaged = daily pack + full weekly milestone
track + achievements; casual = daily pack + half the weekly track.

| Milestone | Engaged median | p90 | Casual median | p90 |
|---|---:|---:|---:|---:|
| First card | day 1 | day 1 | day 1 | day 1 |
| First set complete | day 18 | day 29 | day 28 | day 45 |
| All three sets | day 49 | day 91 | day 74 | day 126 |
| Every card at level 5 | ≈ day 410 | — | ≈ day 560 | — |

These sit between the anchors already in the project: the full master roster is documented at 3–5
weeks (`MASTERS.md`) and a maxed Mythic captain at ~58 days (`Captains.cs`). The level-5 tail beyond
a year is deliberate and matches the "months of duplicates, not a weekend" standard `Captains.cs`
sets for its own ladder — a collection that finishes is a collection you stop opening.

### Duplicate curves

Max level 5, matching both existing rosters. `Captains.cs` is explicit that two rosters with two
different ladders is two things for a player to learn; three would be three.

| Rarity | L1→2 | 2→3 | 3→4 | 4→5 | Total | Draw rate | Packs to L5 |
|---|---:|---:|---:|---:|---:|---:|---:|
| Common | 2 | 4 | 8 | 14 | 28 | 5.81% | 506 |
| Rare | 2 | 4 | 7 | 12 | 25 | 4.69% | 562 |
| Epic | 1 | 3 | 5 | 9 | 18 | 2.34% | 782 |
| Legendary | 1 | 2 | 4 | 7 | 14 | 1.84% | 732 |
| Mythic | 1 | 2 | 3 | 5 | 11 | — | — |

**A rarer card needs fewer copies** — the same inversion `Captains.cs` chose, for the same measured
reason. On a flat curve the three Legendaries would take roughly four times as long to max as the
nine Commons purely because they drop a third as often, and an unreachable ceiling makes the whole
ladder beneath it read as pointless. Scaling cost down by rarity lands the four launch rarities
within ~55% of each other while keeping Commons the fastest thing on the board.

Measured duplicate supply for one named card, first copy excluded (3,000 runs per row):

| Packs | Common | Rare | Epic | Legendary |
|---:|---:|---:|---:|---:|
| 60 | 2.5 | 1.9 | 0.7 | 0.5 |
| 120 | 5.9 | 4.5 | 2.0 | 1.5 |
| 240 | 12.8 | 10.1 | 4.8 | 3.9 |
| 400 | 21.9 | 17.5 | 8.7 | 7.2 |
| 700 | 39.2 | 31.4 | 15.9 | 13.3 |
| 1200 | 67.7 | 54.6 | 28.1 | 23.6 |

### Copies drawn after max level

A copy of a level-5 card **converts to gems at draw time**, at a rarity-scaled rate, reported in the
reveal overlay as the outcome. Leaving them visible and inert is the one option that silently
discards value; removing maxed cards from the roll pool is refused outright, because it would make
the published odds untrue for exactly the players who read them.

| Rarity | Gems | Why |
|---|---:|---|
| Common | 5 | A twelfth of a master chest — a nudge, not a payout. |
| Rare | 10 | Six overflow Rares buy one 60-gem master chest. |
| Epic | 20 | Comparable to one achievement tier's gem pay. |
| Legendary | 40 | Two thirds of a chest; rare enough to feel like a find. |
| Mythic | 75 | Reserved; sized off the Legendary rung. |

Overflow starts only once a card is at level 5 — around pack 500 for the first Commons — and yields
on the order of 500 gems across the following three months. Against the ~14,400 gems an all-bought
master set costs, that is a rounding error by design.

### The three launch sets

Three sets × eight cards, one gameplay loop each. Every set carries the same rarity mix — 3 Common,
2 Rare, 2 Epic, 1 Legendary — so completion difficulty is uniform and which set a player finishes
first is decided by luck rather than by which tab they opened.

| Set | Loop | Permanent bonus | Value | One-time reward | Effect owner |
|---|---|---|---:|---:|---|
| `coal_industry` (Kömür Sanayi) | island income | `IncomeMultiplier` | +8% | 400 gems | `MarketService` tick |
| `workshop_guild` (Tersane Atölyesi) | the bench | `CraftPointDropChance` | +0.06 | 40 craft points | `CraftingService.TryDropPoint` |
| `deep_waters` (Derin Sular) | sea salvage | `SeaSalvageMultiplier` | +15% | 250 charts | `ExpeditionService.RegisterKill` |

Rewards are sized off real anchors: 400 gems ≈ 2.7 full weekly milestone tracks; 40 craft points ≈
three heavy sea days at the current 20% drop chance; 250 charts = 2.5 captain crates.

### Card effects

Each card carries exactly one effect. Values are **per level** — a card is worth five times its
listed value at level 5. Effects of the same kind add, and a completed set's bonus enters the same
sum.

| Effect | Cards | C | R | E | L | All L5 | + set | Cap |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| `IncomeMultiplier` | 7 | .002 | .004 | .006 | .010 | +16.0% | +24.0% | +30% |
| `CraftXpMultiplier` | 6 | .010 | .020 | .030 | — | +50.0% | +50.0% | +75% |
| `CraftPointDropChance` | 3 | — | .004 | .006 | .010 | +0.100 | +0.160 | +0.20, final ≤ 0.40 |
| `SeaSalvageMultiplier` | 4 | .006 | .010 | .016 | — | +19.0% | +34.0% | +50% |
| `SeaChartMultiplier` | 4 | .006 | .010 | .016 | .024 | +28.0% | +28.0% | +40% |

The caps stop a later content set compounding a launch value into something nobody re-solved.
`CraftPointDropChance` carries two — the collection may contribute at most +0.20, and the final
chance is clamped at 0.40 whatever else is in play — because it is a probability, not a multiplier,
and a probability that can reach 1.0 stops being a drop rate.

**Every cap sits above what the launch catalogue can reach**, and deliberately so: a cap that clips a
shipped value has quietly become the thing doing the balancing, and this table would stop describing
the game. `CardCollectionTests.EveryCapLeavesTheLaunchCatalogueRoomToBreathe` asserts the headroom on
all five kinds, so a later edit cannot close it silently. The drop-chance cap was 0.15 in the first
draft of this table, which would have clipped the +0.160 the completed launch collection reaches —
that test is what caught it.

Why these sizes are safe next to what exists:

- **Income (+24% completed).** The existing ceiling is `foreman 3.0× × mining gear 1.8× = 5.4×`
  before the legacy investor multiplier and boosts. Adding 1.24× moves that product by 4.6%. It is
  deliberately the smallest bonus on the board: income is the number every other system competes to
  raise.
- **Craft XP (+50%).** Generous on purpose and safe because XP is not the bench's real brake — the
  retooling gates at levels 10, 20 and 30 are wall-clock cooldowns no multiplier can shorten.
- **Craft points (+0.16, clamped at 0.40).** Points are the bench's actual bottleneck: 1 point per
  craft against a ~280-craft ladder, dropping at 20% per won fight. 36% is roughly +80% point supply
  — real, bounded, and the reason this one needs a hard clamp.
- **Salvage (+34%) and charts (+28%).** Both are closed loops by design: salvage comes from sailing
  and goes back into sailing, charts come from sailing and go into crates. Neither touches the main
  economy, which is why they carry the largest percentages. The chart bonus takes a maxed Mythic
  captain from ~58 days to ~45.

## Integration

### The income multiplication order

From `MarketService.SettleYard` and `AdvanceIncomeMeter`, with the new factor marked:

```
sale = bars · BarPriceRaw · legacyIncomeMult ÷ speed · permanentSpeed
                          · foremanMult · miningGearMult · [collectionMult]

cap  = IncomeCapPerMinuteRaw · legacyIncomeMult · permanentSpeed
                             · foremanMult · miningGearMult · [collectionMult]

paid = min(sale, cap − earnedThisMinute) · boostMult
```

The new factor goes in **both** the sale and the cap. That is the rule `Foremen.cs` states and the
reason it exists: every island's income is capped, so a bonus applied to the sale alone would do
nothing at all for the players who have been playing long enough to own a collection. It must also
reach `BarPrice()` so the yard's price board agrees with the till.

The snapshot is recomputed only when collection state changes — a pack opens, a card levels, a set
completes — never per frame and never per sale. `MarketService` latches it once a second alongside
the other multipliers, exactly as it latches `_foremanMult`, so the per-frame path allocates nothing.

### Scope boundaries that must be tested, not assumed

- `SeaSalvageMultiplier` applies **only** to fight loot settled in `ExpeditionService.RegisterKill`.
  Salvage also enters the save from five scrap paths (`Equip`, `Refuse`, strip, `ScrapFromStash`,
  `ScrapAllStash`) and those are a different loop; multiplying them too would silently double the
  bonus for a player who scraps everything.
- `CraftXpMultiplier` has exactly two write sites — `SalvagePending` and `GrantScrapXp`. Both need
  it, once each. Both now route through one private `CraftingService.ScrapXp`, so the bonus cannot
  come to depend on which screen the player was standing on when they scrapped.
- `SeaChartMultiplier` applies at `RegisterKill` only, not to any other path into `SaveData.charts`.

All five boundaries are held by tests in `CardCollectionIntegrationTests`, and each of those runs the
same consumer twice — with a maxed collection and without — asserting the exact ratio rather than a
balance number, so retuning the cards cannot make them stop checking the wiring.

### One rounding rule for every counted reward

`CardCollection.Scale(long, double)` is the only place a whole-number reward meets a collection
multiplier. Three consumers use it (craft XP, fight salvage, fight charts) and the alternative was
each picking its own rounding, which is how a player ends up finding that the same +6% is worth
something on charts and nothing on salvage. It rounds away from zero, matching `Foremen.CardsFor`
and `SeaCombat.SalvageFor`, and a multiplier at or below 1 returns the amount untouched — a bonus
must never be able to subtract.

### The sea's scrap buttons now report what the bench actually received

`ScrapFromStash` and `ScrapAllStash` used to compute their `out xp` from `Crafting.SalvageXpFor`
directly while `CraftingService.GrantScrapXp` did the real crediting. Once the collection can lift
that figure the two diverge, so `GrantScrapXp` returns what it granted and the sea reports that.
Every UI caller currently discards the value with `out _`, which is exactly why it was worth fixing
now rather than leaving a trap for whoever first wires it to a label.

### Pack supply

| Source | Packs | Cadence | Mechanism |
|---|---:|---|---|
| Daily pack | 1 | every UTC day | Owned by `CardCollectionService`; `dailyPackDay` vs `Goals.DayNumber`. Claiming banks an unopened pack — it never auto-opens. |
| `weekly_50` | 1 | weekly | New `Packs` field on `Goals.WeeklyMilestone`, carried through `GoalService.ClaimReceipt`. |
| `weekly_75` | 1 | weekly | as above |
| `weekly_100` | 3 | weekly | as above |
| Achievement tiers 3+ | 2 | per tier, once | New `PacksPerTier` on `Goals.Achievement`; early tiers already pay gems and foreman cards. |

`Goals.DailyPool` is **untouched**. Its length must stay prime for `DailyIndex`'s distinctness
invariant; adding a field to the struct is safe, adding an entry to the pool is not.

The daily reset is a UTC day number, not a countdown — matching Plan #07's calendar rule and
`MasterChest`'s deadline shape. It ticks while the app is shut, banks at most one, and a clock rolled
backwards only ever delays it. It is deliberately not the rewarded-ad cooldown: this is non-ad
progression.

### Known risk, accepted

Packs on goal milestones make an **eighth consumer** of five already-shared metrics. `Upgrades`,
`Contracts`, `Repairs`, `ForemanLevels` and `BarsSold` already feed Daily Goals, Weekly Milestones,
Achievements, Production Sprint, Harbor Festival, Foundry Festival and the Seasonal Pass, and no
single tuning file can see its own multiplier. The pack counts above are deliberately small next to
what those tiers already pay, and they are the first number to revisit whenever the deferred
cross-system reward review is picked up.

## Save design

One nested `CardCollectionSaveData` on `SaveData`, not fields scattered across the root:

- `List<CardCollectionProgress>` — `cardId`, `level`, `duplicates`, `seen`
- `List<string> claimedSetRewardIds`
- `int unopenedPacks`, `int dailyPackDay`, `int packsOpened`
- `int pullsSinceEpic`, `int pullsSinceLegendary`

Rules: missing collection data in an old save is an empty, valid collection. Unknown progress IDs are
retained rather than dropped, and never crash load. Config cards missing from a save start at level
0. Duplicates alone can never unlock a card — only a pack draw creates level 1.
**`SaveMigration.CurrentVersion` is not bumped**, on the same precedent as every block already
appended to `SaveData`. Save immediately after every meaningful mutation.

### Identity

Stable string IDs, which are save keys and must never be renamed after release:

- Set: `coal_industry`
- Card: `coal_industry/foreman_ledger`

Display names, descriptions, sprites, rarity and every balance value may change freely without
invalidating a save.

## Delivery slices

1. **Core types and pure pack maths — complete.** `Core/CardCollection.cs` and
   `Core/CardCollectionPack.cs`; rarity reuses `RosterCardState.Rarity` rather than declaring a
   fourth five-rung enum. 60 tests.
2. **Catalogue and config asset class — complete.** `Core/CardCollectionCatalogue.cs` carries the 24
   cards and 3 sets; `Data/CardCollectionConfig.cs` carries the tuning scalars and the art. 30 tests.
3. **Save model and normalisation — complete.** `CardCollectionSaveData` nested on `SaveData`, no
   version bump, unknown card ids retained. 19 tests.
4. **`CardCollectionService` — complete.** Atomic `TryOpenPack`, duplicate upgrades, set completion,
   one-time reward claims, cached effect snapshot. `Core/CardCollectionEffects.cs` and
   `Core/CollectionSetState.cs` carry the read models. 40 tests.
5. **Gameplay integration — complete.** `MarketService` (sale, both ceilings, price board),
   `CraftingService` (both XP write sites, the point-drop window), `ExpeditionService.RegisterKill`
   (salvage and charts). 20 tests, including one per scope boundary below.
6. **Bootstrap and goal plumbing — complete.** `GameBootstrap` builds the
   service after `ForemanService` and before `GoalService`, hands it to `GoalService` and
   `MarketService` by constructor, and wires `Expeditions.Cards`, `Crafting.Cards` and the two
   set-reward payers (`Captains`, `Crafting`) once those exist. `Goals.WeeklyMilestone.Packs` and
   `Goals.Achievement.PacksPerTier` (paid from `Goals.PackFirstTier` = 3) ride through all three
   claim paths and `GoalService.ClaimReceipt.Packs`; `ClaimAll` tags weekly and achievement packs
   with their own `PackSource`. Tiers already claimed on an existing save are not back-paid. 14
   tests in `CardCollectionGoalTests`. Verified 2026-09-10: clean recompile, 183/183 collection
   tests, and every reference confirmed live on the single instance in Play mode from `Main.unity`.
7. **UI — complete.** `CardCollectionUI` (on `UI_Sistemler` in `Main.unity`, sort order 116) shows
   the pack card (unopened count, free daily pack with an h:mm:ss countdown on its own sub-canvas,
   both pity lines, the last pull's outcome), one tab per set with its bonus and one-time reward,
   and that set's cards in a 2×4 grid browsed through `RosterCardQuery`; a tile opens the shared
   `RosterInspectPanel` and clears NEW. `BtnKartKoleksiyonu` (order 8) lands in the More sheet with a
   chip counting packs + today's pack + upgrades + claimable rewards. `OddsSheetUI.ShowCardPack`
   omits rarities no card carries. `RewardRevealUI` and the weekly track now print
   `ClaimReceipt.Packs`. Card art, banners and tints are read from the service's
   `CardCollectionConfig` (`CardCollectionService.Config`), so the asset is wired in one place; the
   component's own sprites are the shared chrome and are left for the Inspector. Card and set names
   fall back to the id ("Pit Charter") until slice 8's rows exist. 26 `koleksiyon.*` rows in all 11
   languages; everything else reuses `kaptan.*`, `kadro.*`, `oran.*` and `gorev.*`. 13 tests
   (`CardCollectionUiSmokeTests` + one in `OddsSheetUiSmokeTests`); the full Play-mode flow in
   Verification §3 was run through MCP on 2026-09-10.
8. **Localization — complete. Art — hooks complete, assets outstanding.** 54 rows in all 11
   languages: every card's and set's `.ad` and `.aciklama`, names approved 2026-09-10. Descriptions
   carry no numbers (values live on the effect line, so a retune never strands the prose); a card's
   shows on the details sheet's status line, a set's as the first line of the set panel. Art hooks:
   `CardCollectionConfig.rarityFrame` (5, `FrameOf`) drawn over each tile, `PackIcon` on the pack
   card, a set's banner as its tab — each switched off rather than stretched when unwired.
   `Assets/Data/CardCollectionConfig.asset` exists with tuning identical to the code defaults and is
   wired on `GameBootstrap` in **`Bootstrap.unity`** (not `Main.unity` — the bootstrap lives there and
   survives the scene load). Still owed: the art itself — 24 faces, 3 banners, 5 frames, the pack
   icon and the menu icon — into `Assets/Art/UI/Koleksiyon/`, then assigned on the config asset. 4 tests.

### Where the catalogue lives, and why it is not in the config

The draft plan put card definitions in the ScriptableObject. They are in code instead, in
`CardCollectionCatalogue`, for two reasons. Card ids are **save keys** — an Inspector field nobody
meant to touch is an orphaned collection on every device. And every other roster in this project
(`Foremen.Roster`, `Captains.Roster`) works with no asset present at all, so the tests exercise the
real launch content rather than a fixture. What stayed in the config is what genuinely cannot be a
literal: the art, and the numbers a designer is meant to move.

Two further consequences of that split:

- **A card declares its set; a set does not list its cards.** The draft had both, which is two things
  that can disagree. A set's contents are every card naming it, in authored order, and a card id must
  begin with its own set id — asserted, so identity and membership cannot drift.
- **No per-card balance constants.** A card declares an effect *kind* and a *rarity*;
  `CardCollection.PerLevel` turns that pair into a value off the table above. The value was never
  per-card — it was always a function of rarity, which is what makes a Legendary worth finding.

### The card read model is `RosterCardState`, not a new type

The draft named a `CollectionCardState`. The collection returns `RosterCardState` instead — the
presentation-neutral grammar Plan #05 built and both other rosters already speak. That is not only
one fewer type: `RosterCardQuery` does allocation-free filtering (All / Owned / Locked / Upgrade
Ready) and sorting (Default / Upgrade Ready / Rarity / Level) over `RosterCardState[]`, which is
exactly the four-and-four the collection screen needs in slice 7, already written and already tested.

Two fields are reused rather than left dead: `Role` carries the card's `EffectKind` — that field
exists so a roster can print what a card *does*, which is what it is being used for — and `Busy` is
always false, since no card is ever away at sea.

Sets had no equivalent grammar, so `CollectionSetState` is new.

### How a set reward gets paid

`CardCollectionService` takes `SaveData`, `SaveService`, `TimeService`, `WalletService`, the config
and an injectable `System.Random`. Gems are a constructor dependency because overflow copies pay them
on the hot pack-open path. The other payers — `CaptainService` (charts), `CraftingService` (craft
points), `ForemanService` (foreman cards) — are **settable properties wired in bootstrap**, the same
late-binding `GameBootstrap` already uses for `Expeditions.Crafting` and `Maintenance.Goals`, because
a set reward is claimed perhaps three times in a player's life.

A reward with no payer wired is recorded as claimed but reported `Paid = false` on the receipt, so a
screen cannot celebrate a payment that did not land. The claim is written to `claimedSetRewardIds`
**before** paying, so a payer that throws cannot leave a reward that can be taken twice.

## Costs to expect

- **Localization is the largest non-code item.** 24 card names + 24 descriptions + 3 set titles + 3
  set descriptions + ~35 UI strings ≈ 89 new rows in `metinler.txt` across 11 languages, roughly 980
  translated strings. Rarity words reuse `kaptan.derece.*` and save 5 rows; nothing else is reusable.
- **Art is 33 assets** — 24 card faces, 3 set banners, 5 rarity frames, 1 pack icon, 1 HUD icon.
  Existing UI sprites can shell it temporarily, but a collection screen's whole job is showing
  pictures, so placeholder art will misrepresent how the feature reads.

## Verification

Not done until all of these pass:

1. Unity recompiles with no new errors and no new warnings.
2. Unity Test Runner: the new collection tests and every existing affected test.
3. In Play mode from `Main.unity` — open the collection from the HUD, claim the daily pack, open it,
   confirm first-copy unlock, level a card from a deterministic duplicate grant, complete a test set,
   confirm the passive bonus is live **before** the one-time reward is claimed, claim it once and
   confirm a second attempt is refused, and confirm the relevant income/craft/sea number moves
   exactly once.
4. Unity console clean after the whole flow.

## Noted separately, not part of this work

`MarketService.BarPrice()` (line 314) applies `legacyIncomeMult · permanentSpeed · foremanMult ·
boostMult` but omits `miningGearMult`, which every other income path applies — so the yard's price
board under-quotes a bar by up to 1.8× for a player in full mining gear. It is a pre-existing display
bug, unrelated to this feature, and is **not** fixed here. When `collectionMult` is added to
`BarPrice()` in slice 5, the omission must be left exactly as found unless it blocks verification.
