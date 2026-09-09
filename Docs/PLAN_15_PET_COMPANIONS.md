# Plan 15 — Pet companions (Deniz Dostları)

**Status:** draft, awaiting approval · nothing implemented yet
**Scope:** a fourth collectible roster beside masters, captains and cards — small, combat-only,
equippable, with its own fusion ladder and its own currency. Reference: *Idle Weapon Shop*'s
weapon-fusion loop, adapted rather than copied.

This document is the plan only. Nothing in `Assets/` is touched until it is approved.

---

## 0. What already exists, and what this reuses

Four rosters already live in this project, each with a settled shape:

| Roster | Rarity rungs | Progression | Currency | Where it lives |
|---|---|---|---|---|
| Foremen (`Foremen.cs`) | 3 (own enum) | stars, cards from island production | foreman cards (free) | island economy |
| Captains (`Captains.cs`) | 5 (own enum) | levels via duplicates | **charts** (closed loop) | sea |
| Cards (`CardCollection.cs`) | 5 (`RosterCardState.Rarity`) | levels via duplicates, always-active | free packs | everywhere (small passive %s) |
| **Pets (this plan)** | 5 (`RosterCardState.Rarity`, reused) | **fusion**, equipped | **pearls (new, closed loop)** | sea combat only |

None of the three existing rosters fuses copies into a higher rarity — they all spend duplicates
against a **level** counter on a fixed-rarity card. Fusion (3 same-tier copies → 1 copy one rung up,
eventually crossing rarity) is a genuinely new mechanic here, so it gets a genuinely new state shape
(§5), not a rider on `CardCollectionProgress` or `Captains.DuplicatesToLevel`.

Everything else — the weighted-chest-with-dual-pity shape, the Inspector/code split, the closed-loop
currency rule, "roll is an argument," "derived not stored," no version bump on save — is reused
**as-is**, because reusing a proven shape is exactly what this project already does every time it adds
a roster (`CaptainCrate` copied `MasterChest`'s pity reasoning; `CardCollectionPack` copied
`CaptainCrate`'s). A fifth different-shaped gacha system would be a fifth thing to learn.

**Naming lesson already paid for once, applied here.** `FIVE_LAYERS.md` §6 records that "crew" collided
with `Voyages.Crew` and `sefer.murettebat`, and renaming after shipping would have cost a save
migration. The word **"mürettebat"/"tayfa"** must not be reused for pets. This plan uses **dost**
(companion) — "gemi dostu" (ship's companion) — which collides with nothing already in the loc table
or the codebase. **Action before implementation: grep `metinler.txt` and every `.cs` file for `dost`
and for the chosen currency name to confirm no collision**, the same check this plan is not able to
run without opening the whole localization table, and the one a human should not skip either.

---

## 1. Role in ship combat

Pets are a **third, small, combat-only lever** — parallel to gear and the captain, never a
replacement for either. A pet does exactly one thing: it adds a small amount to one stat on the
derived `SeaCombat.Stats` sheet, the same sheet gear and the captain's role secondary already feed.

**What pets deliberately do NOT touch**, and why:

- **Never `Shot` (TOP).** Raw damage is the single most inflationary stat in the formula (`SeaCombat.OurStats`
  multiplies it by crew, captain worth, and every equipped item's `Shot`). No pet species owns it.
- **Never income, gems, cash, craft points, or any economy multiplier.** Pets are sea-combat-only,
  full stop — they cannot compound with `MiningGear.IncomeMultiplier` or `CardCollection.EffectKind`
  the way a fifth economy lever would. This is the cleanest way to satisfy "prevent power inflation
  and overlap": there is no shared surface to inflate.
- **Never a stat no other system owns without reason.** Where a pet's stat already has an owner
  (gear rolls all nine secondaries; the captain's role adds a signature one — Gunner→Crit,
  Quartermaster→Dodge, Bosun→Mend, Purser→Plunder), the pet's contribution is sized as a **small
  third increment inside the stat's existing hard cap** (`SeaCombat.CritCap`, `DodgeCap`, …) — never a
  new ceiling. This is exactly `CardCollection`'s own reasoning for its four multiplier effects,
  applied to hard-capped chance stats instead of soft-capped multipliers (§7).

Pets are equipped from the sea-combat sheet (`SeaFightUI`'s persistent panel), fought with
automatically (no buttons, matching the fact that gear and the captain are also passive), and never
enter a fight as an action — same "no ability buttons, the sheet is the fight" rule `SeaCombat.cs`'s
header states for everything else on the ship.

---

## 2. Pet slots

**Three**, unlocked progressively and gated on fights won rather than a currency — reusing
`Voyages.TierFightsRequired = { 0, 3, 10, 25 }`, the exact thresholds that already gate sea-combat
tiers 1–3, instead of inventing a second gating currency for the same kind of milestone:

| Slot | Unlocked at |
|---|---|
| 1 | from the first time the player opens the pet panel (free) |
| 2 | `seaFightsWon ≥ 10` (same gate as sea Tier 2) |
| 3 | `seaFightsWon ≥ 25` (same gate as sea Tier 3, the far reach) |

Three, not five like gear: pets are the smallest lever on the sheet, and three slots is enough for a
real "which stat do I want" choice without turning the panel into a second gear grid. It also means a
player who has not engaged with pets at all changes nothing about how sea combat already reads —
`SeaFightUI`'s sheet gains three small buttons, not a redesign.

---

## 3. Initial roster and archetypes

Six species at launch, each fixed to exactly one stat for its whole life — the species is the
identity, not the rarity; rarity and stars are per-owned-copy state (§4). Every species is drawable
at every rarity (§6), so pulling a "Rare Parrot" and later fusing it into an "Epic Parrot" is the same
animal, just further along.

| Species (TR / EN) | Archetype | Stat | Why this pairing | Existing owner of the same stat |
|---|---|---|---|---|
| **Papağan** / Parrot | Gözcü (Lookout) | `SecDodge` (MANEVRA) | a lookout on the shoulder calls the shot early | gear (plating/spyglass/rigging pools), captain Quartermaster |
| **Maymun** / Monkey | Çevik Eller (Quick Hands) | `SecSalvo` (extra shot) | nimble hands reload the swivel gun faster | gear (cannon/charm/rigging pools) |
| **Ahtapot yavrusu** / Baby Octopus | Sıkı Sarılma (Tight Grip) | `SecStun` (SERSEMLETME) | tentacles foul the enemy's rigging | gear (plating/charm pools) |
| **Gemi Faresi** / Ship's Rat | Yağmacı İçgüdü (Scavenger Instinct) | `SecSteal` (CAN ÇALMA) | opportunist, first to the wreck | **nobody** — the one secondary no captain role owns |
| **Yengeç** / Crab | Zırhlı Kabuk (Armored Shell) | `Def` (core stat, SAVUNMA) | a shell is armor, not a chance | gear (plating-heavy), no captain source |
| **Kaplumbağa** / Turtle | Dayanıklı Gövde (Sturdy Hull) | `Hull` (core stat, CESARET) | slow, patient endurance | gear (plating-heavy), no captain source |

Two stats are deliberately left unclaimed at launch — `SecBurn` (YANGIN) and `SecPoison` (ZEHİR) —
the same way `CardCollectionCatalogue` reserved Mythic cells for cards that do not exist yet. A
seventh and eighth species (a **flying fish** for Burn, a **kraken pup** for Poison) are the obvious
next content drop and cost nothing to leave room for now.

**Design note, stated once so it does not need re-deriving:** pets are the only lever in the whole sea
sheet that can raise `Def`/`Hull` (the crab/turtle) or `Steal` (the rat) **without owning a gear slot
or a captain role** — every other route to those three needs a slot spent on something else. That is
the actual reason pets are worth having alongside gear and the captain rather than reading as a
weaker copy of one: two of six species fill a real gap, and the rest are a controlled second source
inside an existing cap.

---

## 4. Rarity and star ladder

Rarity reuses `RosterCardState.Rarity` (Common/Rare/Epic/Legendary/Mythic) — no sixth five-rung enum,
the exact rule `CardCollection.cs`'s header states and `Captains`/`Foremen` already follow.

**Max stars per rarity: 5.** Matches `Foremen.MaxStars`, `Captains.MaxLevel` and `CardCollection.MaxLevel`
— all 5 — for the same stated reason every one of those files gives: one number to learn across every
roster in the game, not a fourth different ceiling.

A pull always lands at **star 1** of whatever rarity it rolled (§6). Stars climb only by fusion.

---

## 5. Fusion rules

**3 copies of the same species, at the same rarity and the same star, fuse into 1 copy one rung up:**

- Star `S < 5`, same rarity: 3 copies at star `S` → 1 copy at star `S + 1`, same rarity.
- Star `5` (max), rarity `R < Mythic`: 3 copies at star 5 → 1 copy at **star 1 of rarity `R + 1`**.
- Star `5`, **Mythic**: no fusion. Mythic 5★ is the ceiling, same trophy shape `Captains.Grade.Mythic`
  and `RosterCardState.Rarity.Mythic` already carry everywhere else in the project.

Worked exactly as the brief's own examples, continued:

```
Rare★1 + Rare★1 + Rare★1        → Rare★2
Rare★2 + Rare★2 + Rare★2        → Rare★3
Rare★3 + Rare★3 + Rare★3        → Rare★4
Rare★4 + Rare★4 + Rare★4        → Rare★5
Rare★5 + Rare★5 + Rare★5        → Epic★1        ← rarity crosses only at max star
```

**Why ownership is a count grid, not discrete instanced items (and why this sidesteps an inventory
cap entirely).** A first draft modeled a pet the way `GearStash` models an item — a row with an id,
because a fused pet's star/rarity is state that has to live somewhere. That reintroduces the exact
problem `GearStash` exists to solve (a capacity, a full-stash refusal, a UI for browsing rows) for a
roster that does not need it: every copy at the same `(species, rarity, star)` is completely fungible,
so there is nothing to distinguish two Rare★2 Parrots from each other. The actual state is therefore
one integer per `(species, rarity, star)` cell — a pull increments one cell, a fusion decrements three
cells in one and increments one cell in another — and **there is no inventory cap, because there is no
inventory of objects, only counts.** This is `Captains.captainDuplicates`'s own shape, generalised
from one dimension (duplicates toward a level) to two (rarity × star), and it is why the "full
inventories" edge case in §12 resolves to "does not apply" rather than to a capacity rule.

**Equip references a species, not a specific fused copy** (§8) — resolved the same way
`ExpeditionService.CaptainAboard` already resolves "which captain is on the bridge": automatically, to
the best one owned. A pet slot holding "Parrot" always fights with the player's single best-owned
`(rarity, star)` of Parrot, computed on read. Fusing away the copy that used to be "best" simply
changes what "best" resolves to next frame — there is no stale reference to clear, unlike a
`GearStash` id that can be tapped out from under the player.

---

## 6. Duplicate handling

There are no "duplicates" as a separate concept from ownership — every pull, whatever rarity it
lands on, **is** a new copy in that cell, immediately fusable once two more like it exist. This is
simpler than both `Captains` (duplicates spent one-way against a level counter) and `CardCollection`
(duplicates spent one-way, with an overflow-to-gems rule for a maxed card) — pets need neither a
"maxed, so this is waste" rule nor an overflow currency, because **every copy of a maxed species is
still fusion fuel for the next species you want to raise**, or, once a specific `(species, rarity,
star)` cell is itself maxed for that species (i.e. Mythic★5), a copy simply sits in the count with
nothing left to do — which is fine and matches `CardCollection.OverflowGems`'s own justification
("leaving it visible and inert is the one option that silently discards value") **except pets do not
even need the gem conversion**, because a Mythic★5 pet is already the single best thing that species
can be, and a player who has one is not looking for it to become something else.

---

## 7. Pet bonuses and integration with sea combat

### 7.1 The formula

One shared curve **shape**, reused across all six stats and scaled per stat to its own ceiling — the
same method `CardCollection.DefaultEffectPerLevel` uses (one small table, effect-major, rarity-
ascending, rather than six hand-tuned constants per rarity per star that are six chances to drift).

```
bonus(species, rarity, star) = PerRarity[EffectKind(species), rarity] × star
```

`PerRarity` follows the same **1 : 2 : 3 : 5 : 8** Common→Mythic shape `CardCollection`'s own table
uses (its `IncomeMultiplier` row is `.002 .004 .006 .010 .016` — almost exactly that ratio), scaled so
five stars of Mythic lands at each stat's target ceiling (§7.2). Reusing an already-shipped, already
player-legible curve shape across a fifth system is deliberate: the player who has learned "rarer
means more, and the jump to Legendary and Mythic is the big one" from cards does not have to relearn
it for pets.

### 7.2 Sizing against the stat's existing cap

Every chance stat pets can touch already has a hard ceiling in `SeaCombat.cs` (`CritCap 0.60`,
`DodgeCap 0.50`, `StunCap 0.40`, `StealCap 0.40`, …) that gear and the captain can already push close
to on their own. A pet cannot raise that ceiling — it can only make it easier to reach. Sized at
roughly **8–10% of the stat's absolute cap at Mythic★5**:

| Species | Stat | Cap (`SeaCombat`) | Pet ceiling (Mythic★5) | Existing max from gear+captain (approx.) |
|---|---|---:|---:|---:|
| Papağan | Dodge | 0.50 | **+0.045** | gear ≈0.17/item × up to 3 pools + captain Quartermaster ≈0.216 |
| Maymun | Salvo | 0.50 | **+0.045** | gear ≈0.17/item × up to 2 pools |
| Ahtapot | Stun | 0.40 | **+0.035** | gear ≈0.17/item × up to 2 pools |
| Gemi Faresi | Steal | 0.40 | **+0.035** | gear ≈0.17/item, no captain source |
| Yengeç | Def (flat) | — (no hard cap; scales with gear) | **≈ one Rare plating's worth at Mythic★5** | gear plating alone can be 3–15 per grade |
| Kaplumbağa | Hull (flat) | — (no hard cap) | **≈ one Rare plating's worth at Mythic★5** | gear plating alone can be 26–120 per grade |

Common★1 (the very first pull, weakest possible) is sized at roughly 1/40th of the Mythic★5 ceiling —
present but genuinely small, matching `CardCollection`'s own "income is the smallest because it
already competes" reasoning turned into "a fresh pet should be felt, not decisive."

Exact per-rarity table values are Inspector defaults, finalised the way every table in this project is
— worked by hand against these targets, then a deterministic test pins the shape (§13). Nothing above
is final balance; it is the target the config asset's defaults are built to hit.

### 7.3 Where the addition happens — and why `SeaCombat.cs` is not touched

`SeaCombat.OurStats` already folds gear and the captain's role secondary together and clamps every
chance stat to its cap **before returning**. The clean seam is therefore *after* that call, not inside
it:

```csharp
// ExpeditionService.ShipStats() — sketch, not final code
SeaCombat.Stats baseStats = SeaCombat.OurStats(captain, level, crew, Loadout(), ct, _combat);
SeaCombat.Stats withPets = Pets.ApplyCombatBonus(baseStats, _petService.EquippedBonuses(), _combat);
return withPets;
```

`Pets.ApplyCombatBonus` (new, in `Core/Pets.cs`) adds each equipped species' bonus to the matching
field and **re-clamps against the exact same `SeaCombat` cap constants** (`CritCap`, `DodgeCap`, …,
already `public const`). This keeps `SeaCombat.cs` — the most heavily tested, longest-standing file in
the feature, 42+ tests pinned against it — completely untouched, and keeps `PetService` an optional
collaborator exactly the way `CaptainService` already is to `SeaCombat`/`Voyages` ("a captain moves
five knobs, none of them arguments to the card payout" — pets move six knobs, none of them arguments
to anything inside `SeaCombat.OurStats`). If a future maintainer prefers a first-class parameter on
`OurStats` instead, that is a one-line, backward-compatible addition (an optional trailing parameter)
and can be revisited without anything above being wrong in the meantime — noted as an option, not the
recommendation.

---

## 8. Equip flow

A pet slot stores a **species index** (or `-1`, empty) — not a specific fused copy. Combat always uses
that species' current best-owned `(rarity, star)`, computed on read (§5). Equipping/unequipping a
species is instant, free, and reversible, matching the officer-chip pattern `VoyageUI`'s captain/foreman
pickers already use ("press to walk nobody → each available officer → nobody again").

The panel shows, per slot: species icon, its current best rarity (tint) and star (pips), and its
live bonus number — the same information density `SeaFightUI`'s four gear-slot buttons already carry
("grade-tinted frames, star pips, tap → item popup").

---

## 9. New currency: **İnci (Pearls)**

*(Naming to be reconfirmed against the loc-collision check in §0 before implementation; "İnci" does
not currently appear in `metinler.txt` search results run during this research pass, but the check
belongs to implementation, not to this plan.)*

| | |
|---|---|
| **Earned from** | won sea fights only (a small per-kill trickle, tier-scaled) + small grants folded into the *existing* Daily Reward, Weekly Milestone and Achievement tracks — no new standalone timer/faucet |
| **Stored** | `PetSaveData.pearls` (nested, not `WalletData` — see §11) |
| **Spent on** | pet chests, and pet chests only |
| **Cannot buy** | cash, gems, salvage, charts, craft points, mining points, or anything outside this feature — a closed loop, the same guarantee `salvage` and `charts` already carry |

**Per-fight payout**, shaped exactly like `SeaCombat.ChartsFor`/`SalvageFor` (`LootFor`), but with a
smaller share so pearls do not out-earn the two systems that already fund the sea's two existing
loops:

```
PearlsFor(tier, kind) = round(PearlRate × Voyages.PayoutMult[tier] × PearlShare × KindLoot[kind]), floor 1
PearlRate  = 4      (same as Voyages.ChartRate)
PearlShare = 0.06   (half of EncounterChartShare/EncounterSalvageShare's 0.12)
```

Worked: tier 0 → 1 pearl/kill (floor). Tier 3 (far reach) → ≈7 pearls/kill. A chest costs 100 pearls
(§10), so a tier-3 grinder nets roughly one chest per ~15 kills, well inside a single energy pool.

**Why deliberately slower than charts.** `PLAN_14`'s "Known risk, accepted" section already flags that
too many systems are racing to consume the same handful of milestone metrics. Pearls are new income,
not a re-skin of an existing one, but they are sized to pace **behind** charts (captains) specifically
so pets read as the sea's newer, slower side project rather than a second currency competing for the
same attention charts already have. This is a pacing choice, stated once so nobody re-derives it while
tuning.

**One-time bootstrap.** The first time the pet panel is opened, grant exactly 100 pearls (one chest) —
mirrors `MasterChest`'s "a fresh save opens the game with a chest waiting" and the sea's own "a
pre-feature save starts with a FULL energy pool": the first thing a new system shows a player must
not be an empty wallet.

---

## 10. Pet chests

One chest type at launch — no premium/rare chest split, matching how `CaptainCrate` and `MasterChest`
both ship with exactly one tier and let pity/weights do the work rather than a second SKU.

| | Value | Reasoning |
|---|---:|---|
| Chest cost | 100 pearls | identical to `CaptainCrate.ChartCost` — reuse the proven number, not a new guess |
| Bulk (10) | 900 pearls | identical to `CaptainCrate.BulkChartCost` — same 10% discount shape |
| Common weight | 0.600 | **identical to `CaptainCrate.Default`** |
| Rare weight | 0.260 | ″ |
| Epic weight | 0.105 | ″ |
| Legendary weight | 0.030 | ″ |
| Mythic weight | 0.005 | ″ |
| Epic pity | 10 pulls | ″ |
| Legendary pity | 70 pulls | ″ |
| Soft pity start / step | 45 / +0.010 | ″ |

**Why copy `CaptainCrate`'s table verbatim rather than author a new one.** Two reasons, both already
stated in this project's own docs. First, `FIVE_LAYERS.md` explicitly measured this exact shape (57.66
/ 24.46 / 12.99 / 4.24 / 0.66% realised over 20,000 real pulls, Mythic reachable in ~67 days on a
maxed fleet) and it produced a roster the project shipped and is happy with — reusing it is reusing a
measurement, not skipping one. Second, `Captains.cs`'s own header states the cost of *not* reusing a
shape: "two rosters with two different ladders is two things to learn." A pet chest that looks and
paces like the captain crate the player already understands is a feature that needs no separate
tutorial.

**Species roll**, once the rarity is decided: **flat among all six species**, independent of rarity —
unlike `CaptainCrate.RollCaptain` (which must look up "who carries this grade" because captains are
grade-locked 1:1), every pet species is reachable at every rarity, so the second roll is a plain
`nth = (int)(roll × SpeciesCount)`. Simpler than the precedent it borrows from, for a real structural
reason (§3/§6), not by accident.

**Pacing estimate**, reusing the measured captain numbers directly since the tuning is intentionally
identical: median pulls to first Epic-or-better ≈ 4, to first Legendary-or-better ≈ 13–16. At the
pearl income in §9 (~1–7/kill, chest = 100 pearls), an engaged tier-2/3 player should see their first
Legendary pet inside the first 1–2 weeks, with a maxed single species being a multi-month tail — the
same order of magnitude `CardCollection`'s own per-card L5 numbers land in (500–780 packs), which is
the right neighbourhood for "a collection that finishes is a collection you stop opening."

**This is an estimate carried over from a different (if identically-tuned) system, not a measurement
of pets specifically — a `PetChestTests`-driven simulation (mirroring `CardCollectionPackTests`'s
2,000–20,000-run sweep) belongs in the implementation phase before these numbers are called final.**

---

## 11. Save design

One nested `PetSaveData` object on `SaveData`, following `CardCollectionSaveData`'s newer convention
(one block, not scattered root fields) rather than `Captains`'/`Voyages`' older root-field convention
— `PLAN_14`'s own reasoning for the split applies unchanged: this is a self-contained feature and a
future maintainer should be able to read its whole shape in one place.

```csharp
public class PetSaveData
{
    // counts[species, rarity, star] flattened: species-major, then rarity, then star (1..5 → index 0..4)
    // size = Pets.SpeciesCount * RosterCardState.RarityCount * Pets.MaxStars
    public int[] counts = new int[Game.Core.Pets.SpeciesCount * 5 * Game.Core.Pets.MaxStars];

    public int[] equippedSpecies = new int[Game.Core.Pets.SlotCount]; // -1 = empty
    public int slotsUnlocked = 1;               // 1..3, derived-checkable but stored for a quick read

    public long pearls;
    public bool bootstrapGranted;                // the one-time 100-pearl gift, §9

    public int chestsSinceEpic;
    public int chestsSinceLegendary;
    public int chestsOpened;                      // lifetime, for the panel's own readout
}
```

`SaveData` gains one field: `public PetSaveData pets = new PetSaveData();` — **no version bump**, the
same precedent every block in this file already follows (`FIVE_LAYERS.md` §2: "never bump
`SaveMigration.CurrentVersion` — it wipes the run").

**Backward compatibility.** An old save simply has no `pets` node; deserialization default-constructs
`PetSaveData` with an empty `counts` array, `equippedSpecies` all `-1`, `slotsUnlocked = 1`,
`bootstrapGranted = false` — reads as a player who has never touched the feature, which is exactly
correct, and the one-time bootstrap fires the first time they open the panel. `PetService.Normalise()`
pads `counts`/`equippedSpecies` to the current expected length the same way
`ExpeditionService.Normalise`'s `Fit()` pads every `seaGear*` array — **appending a seventh species or
a fourth slot later is array growth, not a migration.**

**Reading an out-of-range `equippedSpecies` entry** (a species removed in a future build, or a corrupt
save) reads as empty, the same way `Captains.Exists`/`Foremen` treat an unknown index — never a crash,
never a phantom pet.

---

## 12. Edge cases

| Case | Resolution |
|---|---|
| Max rarity (Mythic) reached | fusion of Mythic★5 is refused (no rung above it) — the button simply does not offer it, same as `CardCollection.CanLevel` refusing past `MaxLevel` |
| Max star (★5) within a lower rarity | the *next* fusion of 3 copies crosses rarity to the next tier's ★1 (§5) — never refused, always has somewhere to go, up to the Mythic★5 ceiling above |
| Insufficient fusion materials (< 3 in the target cell) | fusion action refused, costs nothing, same "a refusal costs nothing" contract `GearStash`/`ContractBoard` already use |
| Duplicate pets | not a distinct case — every pull is immediately fusion fuel, see §6 |
| "Full inventories" | **does not apply.** Ownership is a count grid (§5), not discrete rows — there is no capacity to exceed. This is a structural answer, not a rule that had to be authored. |
| Equipped species' best copy gets fused away mid-panel | resolves automatically on next read — equip stores a species, not a copy (§5/§8); nothing needs to be cleared |
| A species removed/renamed in a future build | an `equippedSpecies` entry pointing at it reads as empty (§11); `counts` cells for it are inert but harmless — never deleted outright, so a later re-add (unlikely, but see `CardCollectionCatalogue`'s "unknown ids retained" rule) does not silently discard progress |
| Pearls spent past what is owned | refused by the same `TrySpend`-style guard `WalletService.TrySpendGems` uses — no negative balance, ever |
| Bulk chest open (10) with fewer than 900 pearls | refused as a whole; no partial bulk-open, matching `CaptainCrate.Cost`'s all-or-nothing bulk price |

---

## 13. Services, data/config assets, enums, and core classes

New files, named and shaped to match the precedent each mirrors:

| File | Mirrors | Contents |
|---|---|---|
| `Core/Pets.cs` | `Captains.cs` + `CardCollection.cs` | species roster (`Card` struct: id, `EffectKind`), `Tuning` struct, fusion math (`CanFuse`, `Fuse`), bonus formula (`PerRarity`, `bonus(species,rarity,star)`), `ApplyCombatBonus` |
| `Core/PetChest.cs` | `CaptainCrate.cs` | weights, dual pity, `RollGrade`/`RollSpecies`/`Advance`/`Cost` — "the roll is an argument, not a call" |
| `Data/PetConfig.cs` | `CaptainConfig.cs` | one `ScriptableObject`, both `Tuning` structs, rarity tints (reuse `CardCollectionConfig`'s palette convention), species art bound by id |
| `Systems/PetService.cs` | `CaptainService.cs` + parts of `ExpeditionService`'s gear handling | owns `PetSaveData`, RNG, `TryOpenChest`, `TryFuse`, `Equip`/`Unequip`, `EquippedBonuses()`, `Normalise()` |
| `Systems/Save/SaveData.cs` | — | `+PetSaveData pets`, no version bump |
| `Systems/ExpeditionService.cs` | — | one call site added in `ShipStats()`: `Pets.ApplyCombatBonus(...)` — optional collaborator, ctor gains an optional `PetService pets = null` parameter, same pattern `CaptainService captains = null` already uses |
| `Systems/GameBootstrap.cs` | — | `petConfig` slot, `Pets` property, constructed after `CaptainService`/before `ExpeditionService` so the optional collaborator is available at construction |
| `UI/PetRosterUI.cs` | `CaptainRosterUI.cs` | chest + pearl balance + pity readout on one side, six species rows (best owned rarity/star, fuse button) on the other |
| `UI/SeaFightUI.cs` | (existing file, extended) | three pet-slot buttons added to the persistent sheet panel, same visual language as the four gear-slot buttons |
| `Assets/Resources/Diller/metinler.txt` | — | new `dost.*` loc keys (species names, archetype descriptions, chest/currency strings) × 11 languages |
| `Assets/Data/PetConfig.asset` | — | the Inspector-tunable instance, wired on `GameBootstrap` |
| Six species icons + 5 rarity frame tints (reuse `CardCollectionConfig.rarityTint`) + 1 chest icon + 1 currency icon | — | art (§16) |

**Reused as-is, nothing new needed:**

- `RosterCardState.Rarity` — the rarity enum.
- `SeaCombat`'s cap constants (`CritCap`, `DodgeCap`, `StunCap`, `StealCap`, …) — pets clamp against
  these, never redefine them.
- `SeaCombat.SlotCount`'s neighbourly pattern for `Pets.SlotCount = 3` and `Pets.SpeciesCount = 6`.
- `CardCollectionConfig`'s art-binding-by-id pattern (`CardArt`/`SetArt` structs) for pet/species art.
- `WalletService`'s `TrySpend`/`Add` shape, copied for pearls inside `PetService` (pearls do not need
  their own service — `charts`/`salvage`/`craftPoints`/`miningPoints` all skip a dedicated wallet
  class too, and `PetService` is the pearls' one and only writer, same as `CaptainService` is charts').

---

## 14. HUD and pet collection UI

Opener button lives in the **"Daha Fazla" (More) sheet**, per the shipped HUD convention — content
under the `Karartma`/`Guvenli` safe-area sheet, openers at sort order **103** (the slot the More-menu
audit already settled on for newly added panel openers). This keeps the bottom HUD row from growing an
eighth/ninth button for a system smaller than chapters or captains.

`PetRosterUI` layout mirrors `CaptainRosterUI` exactly (chest/currency column left, roster column
right) — same opaque `Zemin` sheet-behind-cards fix `FIVE_LAYERS.md` §7 already had to apply once to
two other screens, applied correctly from the start here rather than rediscovered.

`SeaFightUI`'s persistent sheet panel gains three small slot buttons alongside the existing four gear
slots and the captain line — visually a fifth "row" under GÜÇ, not a new panel, so equipping a pet
before a fight is exactly as many taps as equipping gear already is.

---

## 15. Flows

- **Detail / inspect.** Tapping a species row in `PetRosterUI` opens a small card: icon, archetype
  name and stat, current best rarity/star with pips, live bonus number, and counts per rarity/star
  cell (so the player can see what is fusable at a glance) — same information shape
  `RosterInspectPanel` already prints for foremen.
- **Upgrade / fusion.** From the same card: any cell with `count ≥ 3` shows a **FUSE** button; pressing
  it consumes 3, grants 1 one rung up, and the reveal is a small flourish (reuse `ConfettiBurst` on a
  rarity crossing, nothing on a same-rarity star-up — matching the weight `CardCollection`'s reveal
  overlay already gives a Legendary versus a Common).
- **Equip.** Tap a pet slot on `SeaFightUI` → species picker (the same cycling-chip pattern
  `VoyageUI`'s officer pickers use) → instant, free, reversible.
- **Chest-opening.** `PetRosterUI`'s left column: single-open and bulk-10 buttons, cost shown before
  the press (mirrors `MasterChest`/`CaptainCrate`'s panels), pity counters printed as "N until
  guaranteed Epic+" the way the captain screen already does.
- **Reward flow.** A won sea fight silently credits pearls (no popup — same as salvage/charts today,
  a HUD pill number ticking) plus, on a rarity/star milestone from the Daily/Weekly/Achievement tracks
  (§9), a small toast consistent with those systems' existing reward toasts.

---

## 16. Inspector-configurable tuning

Everything a designer might move lives in `PetConfig` (mirrors `CaptainConfig`'s "one asset, two
tuning structs" shape):

- Chest weights (5), pity thresholds/ramp (4), chest cost + bulk cost (2).
- Per-`EffectKind`-per-rarity bonus table (6 kinds × 5 rarities = 30 doubles, flattened, effect-major
  — same shape `CardCollectionConfig.effectPerLevel` already uses).
- Pearl earn rate + share (2), matching `Voyages.Tuning.ChartRate`/`EncounterChartShare`'s shape.
- Rarity tints (reuse `CardCollectionConfig`'s array, or point at the same asset — to decide during
  implementation; duplicating five colours a second time is the kind of thing that drifts).
- Species art bound by id (`SpeciesArt[] { speciesId, icon }`), chest icon, currency icon.
- The game runs on code defaults with no asset wired, exactly like every other config in this project.

---

## 17. Localization, art, animation, sound

**Localization.** New `dost.*` key family: 6 species names + 6 archetype/flavour lines + rarity
words (reuse `kaptan.derece.*`, already 11-language, the same reuse `CardCollectionCatalogue` took)
+ chest/currency/panel strings (~15–20 rows) + fuse/equip confirmation strings. Rough total: ~35–40
new rows × 11 languages ≈ 400–440 translated strings — smaller than `CardCollection`'s ~980, because
there is no per-card description text (six archetypes, not twenty-four cards).

**Art.** Six species icons (parrot/monkey/octopus/rat/crab/turtle, in the project's existing PIL
house style — `Tools/ui/deniz_savas_seti.py`'s "thick navy outline, vertical gradient, top sheen"
language, the same kit that drew the sea-combat sprite set), one chest icon, one currency icon, and
rarity frame/tint reuse from `CardCollectionConfig`. No portraits needed — species read as icons, not
character art, matching gear's own presentation rather than the captain roster's.

**Animation.** None beyond what already exists: a fuse action can reuse `ConfettiBurst` on a rarity
crossing; the equipped-pet slot buttons are static icons with a tint, like the four gear-slot buttons
already are. No new VFX system.

**Sound.** Reuse existing UI taps/panel-open stingers (`UiPanelSound`); a distinct "fuse" chime is the
only genuinely new cue, sized the same as the existing level-up/upgrade stinger already used elsewhere.

---

## 18. Deterministic tests

Mirrors the existing four-file split per roster (`Captains`/`CaptainCrate`/`CaptainService`/save), one
extra file for the combat-integration seam:

| File | Coverage |
|---|---|
| `PetsTests.cs` | fusion math (`CanFuse` at every star/rarity boundary, the Mythic★5 ceiling refuses), bonus formula shape and monotonicity, `Pets.SpeciesCount`/`EffectKind` invariants |
| `PetChestTests.cs` | weight table sums correctly per grade, both pity counters fire at the exact pull, soft-pity ramp shape, species roll is flat and independent of rarity, `Advance` clears both counters on a Legendary — copy `CaptainCrateTests`' own test shapes since the tuning is copied |
| `PetServiceTests.cs` | `TryOpenChest` spends pearls exactly once and never over-spends, `TryFuse` consumes exactly 3 and grants exactly 1 in the correct target cell, `Equip`/`Unequip` on a species with zero owned copies is refused, an equipped species with its best copy fused away still resolves to *something* (or empty) without throwing, slot-unlock gating against `seaFightsWon` |
| `PetSaveTests.cs` | `Normalise()` pads a short/missing `counts`/`equippedSpecies` array without loss, an old save with no `pets` node loads as untouched-feature state, the one-time bootstrap fires exactly once, an out-of-range `equippedSpecies` index reads as empty rather than throwing |
| `PetCombatIntegrationTests.cs` (or folded into `SeaCombatTests.cs`/`ExpeditionServiceTests.cs`) | **the bonus is applied exactly once** per fight (no double-count if `ShipStats()` is called twice in a frame), the stat re-clamp after adding pets never exceeds `SeaCombat`'s existing cap constants even with three max-tier pets stacked on the same stat, `ExpeditionServiceTests`' existing "a dock/service with no roster behaves exactly as it did before" pattern repeated for "no `PetService` wired" |

Target: 30–40 new tests, in the range `CaptainsTests`(20)+`CaptainCrateTests`(19)+`CaptainServiceTests`(23)
sit in individually, smaller than that combined total because pets are a smaller surface (one fixed
stat per species vs. four full role effects).

---

## 19. Implementation order

1. **Core pure maths** — `Core/Pets.cs`, `Core/PetChest.cs`. No service, no save, no UI. Tests from
   §18's first two rows.
2. **Config asset** — `Data/PetConfig.cs`. Game still runs unchanged (nothing calls it yet).
3. **Save model** — `PetSaveData` on `SaveData`, no version bump. `PetServiceTests`' `Normalise()`
   cases.
4. **`PetService`** — chest opening, fusion, equip, `EquippedBonuses()`. Not yet wired into combat.
5. **Combat integration** — the one call site in `ExpeditionService.ShipStats()`, optional-collaborator
   constructor change, `PetCombatIntegrationTests`.
6. **Bootstrap plumbing** — `GameBootstrap` registration, construction order, one-time bootstrap grant,
   pearl payout wired into `ExpeditionService.RegisterKill` (mirrors how `charts`/`salvage` are paid
   there today), pearl grants added to Daily Reward / Weekly Milestone / Achievement tracks.
7. **UI** — `PetRosterUI`, the three slot buttons on `SeaFightUI`, the "Daha Fazla" opener at order 103.
8. **Localization and art** — through Unity tooling, `dost.*` keys, the PIL sprite set.

Each step should compile and pass tests before the next begins — the same incremental discipline
`PLAN_14`'s own delivery slices already followed.

---

## 20. Final Unity MCP verification (not done until all of this passes)

1. Unity recompiles with **0 new errors, 0 new warnings**.
2. Unity Test Runner (`run_tests`): every new pet test **and** the full existing suite — currently
   1045/1049 green with 4 pre-existing `RenderingSafetyTests` failures (`editmode-suite-baseline`
   memory); the pet work must not move that baseline.
3. Launch the game in Play mode from `Main.unity`:
   - Open the pet panel from "Daha Fazla" — confirm the one-time 100-pearl bootstrap grant.
   - Open a single chest, then a bulk-10 — confirm pearl spend is exact, confirm the bulk-10 always
     contains an Epic-or-better (Epic pity = 10).
   - Fuse 3 same-tier copies of one species; confirm the star/rarity result matches §5 exactly,
     including one deliberate rarity-crossing fusion.
   - Equip a pet in each of the three slots (unlocking slots 2/3 via `seaFightsWon`, or a debug grant
     if faster); confirm the sheet's stat line moves by exactly the pet's bonus.
   - Enter sea combat and fight: confirm the equipped pets' bonus is reflected in the fight's actual
     rolls (a Dodge pet should visibly reduce incoming hits over enough samples), and **confirm it is
     applied exactly once** — call `ShipStats()` twice in the same frame via a debug probe and confirm
     the number does not move.
   - Fuse away an equipped species' only copy mid-session; confirm the slot resolves to empty (or the
     next-best copy) without an exception and without the UI showing a stale pet.
4. Confirm the Unity Console is clean through the whole flow above — no new warnings, no exceptions.

---

## Open questions for approval before implementation

1. **Currency name** — "İnci" (Pearl) is this plan's proposal; confirm no collision once
   `metinler.txt` is searched directly (§0), and confirm the pirate/ocean fit reads right to you.
2. **Species roster** — six archetypes in §3 are a proposal grounded in "fill the two gaps gear/captain
   leave (Def, Hull, Steal) plus three controlled seconds (Dodge, Salvo, Stun)." Happy to swap any
   individual species/animal for a different one without changing the underlying mechanic.
3. **Slot count and gates** — 3 slots at `seaFightsWon` 0/10/25 reuses existing tier thresholds; open
   to a currency-gated alternative if you'd rather pets not be entirely free to unlock.
4. **`SeaCombat.cs` untouched vs. a first-class parameter** — §7.3 recommends keeping `SeaCombat.cs`
   untouched and folding pets in one layer up in `ExpeditionService`. Flagging in case you'd prefer
   the alternative (an optional trailing parameter on `OurStats`) for a tighter single-source-of-truth
   at the cost of touching the project's most heavily-tested file.
