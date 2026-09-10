# Pet System Handoff — Plan 15 (Deniz Dostları)

**Written by:** Claude, for a handoff to another assistant (GPT) continuing this work.
**Repo state this describes:** commit `d6a8017` ("ss"), working tree clean at that commit.
**Status in one line:** the whole non-Unity backend (maths, save, service, combat hook, 4 test
files) is written and committed. Nothing Unity-side exists yet — no config asset, no UI, no scene
wiring, no art, no localization, and **nothing has been compiled or test-run by me**, because
UnityMCP was disconnected for this entire working session. Treat everything below as "should work
by inspection," not "verified."

The full original design document is [`Docs/PLAN_15_PET_COMPANIONS.md`](PLAN_15_PET_COMPANIONS.md)
— read it for the complete reasoning. This handoff summarizes it **and** calls out every place the
shipped code diverged from that plan.

---

## 1. The original plan

`Docs/PLAN_15_PET_COMPANIONS.md` proposed a fourth collectible roster, combat-only, reusing as much
of the existing captain/card machinery as possible rather than inventing a fifth gacha shape. Key
proposed pieces, most of which shipped as written:

- Pets are a small third combat lever beside gear and the captain — never touching `Shot`, never
  touching economy multipliers, so there is no shared surface with `CardCollection`/`MiningGear` to
  inflate.
- Three equip slots, unlocked at `seaFightsWon` 0/10/25 (reusing the same numbers `Voyages`'s sea
  tiers already gate on).
- Six species, each owning exactly one `SeaCombat` stat for life; rarity/star is per-owned-copy
  state, not per-species.
- Rarity reuses `RosterCardState.Rarity` (5 rungs); stars go 1–5; **fusion**, not leveling, is the
  new mechanic — 3 copies of the same (species, rarity, star) → 1 copy one rung up, crossing into
  the next rarity at ★1 only when fusing from ★5.
- Ownership as a flat count grid (`counts[species, rarity, star]`), not discrete instanced items —
  deliberately sidesteps an inventory cap entirely (there is nothing to "hold too many of").
- A new currency, **Pearls**, earned at sea and spent only on pet chests — a fourth closed loop
  beside charts/salvage/craft points.
- One pet chest type, odds/pity **copied verbatim from `CaptainCrate.Tuning.Default`** (same
  weights, same pity thresholds) rather than authoring new numbers, on the stated reasoning that the
  captain crate's shape was already measured and shipped.
- `SeaCombat.cs` is left **completely untouched**; the pet bonus is added one layer up, in
  `ExpeditionService.ShipStats()`, after gear+captain are already folded and clamped.

## 2. Decisions the user made before implementation started

- **Currency name: Pearls** (İnci in Turkish internally via `papagan`/`maymun`/etc.-style ids, not
  yet localized — see §12).
- **The six-species roster is approved as proposed** in the plan's §3 (no swaps requested).
- **Pet slots unlock through progression, not all at once** — confirming the plan's §2 proposal
  (0/10/25 `seaFightsWon` gates) rather than an alternative currency-gated scheme.
- User then said "proceed with the plan using those decisions" — authorizing full implementation.

## 3. What has actually been implemented

All of the following is **committed** (part of commit `d6a8017`, alongside an unrelated, separately
authored Card Collection feature — see §14 for why that matters).

| File | Status | Purpose |
|---|---|---|
| `Assets/Scripts/Core/Pets.cs` | new | Pure maths: roster, effect table, `Bonus`, `TryFuse`, counts-grid helpers (`CellIndex`/`CountAt`/`TryBestOwned`), `ApplyCombatBonus` |
| `Assets/Scripts/Core/PetChest.cs` | new | Pure maths: chest rarity odds, dual pity + soft ramp, flat species roll, bulk pricing |
| `Assets/Scripts/Data/PetConfig.cs` | new | Inspector-editable `ScriptableObject` — effect table, chest odds/pity, pearl costs, pearls-per-win, slot-unlock gates, art bindings. **No `.asset` instance exists yet** |
| `Assets/Scripts/Systems/Save/SaveData.cs` | changed | Added `public long pearls;` (root field) and `public PetSaveData pets = new PetSaveData();` plus the nested `PetSaveData` class with `Normalise()` |
| `Assets/Scripts/Systems/PetService.cs` | new | Equip/unequip, fuse, open chests (single+bulk), slot-gate logic, combat-bonus aggregation, **and disk persistence** (see §9) |
| `Assets/Scripts/Systems/ExpeditionService.cs` | changed | Added `public PetService Pets { get; set; }` and `public long PearlsPerWin { get; set; }`; `ShipStats()` applies the pet bonus once; `RegisterWin()` pays pearls once |
| `Assets/Scripts/Systems/GameBootstrap.cs` | changed | Added `petConfig` Inspector field, `Pets` property, constructed `PetService` after `Expeditions`, wired `Expeditions.Pets`/`PearlsPerWin` |
| `Assets/Scripts/Tests/EditMode/PetsTests.cs` | new | Core maths tests |
| `Assets/Scripts/Tests/EditMode/PetChestTests.cs` | new | Chest odds/pity tests |
| `Assets/Scripts/Tests/EditMode/PetServiceTests.cs` | new | Save-contract, chest, fusion, equip, combat-bonus, **and persistence** tests |
| `Assets/Scripts/Tests/EditMode/PetCombatIntegrationTests.cs` | new | The `ExpeditionService` ↔ pet seam |

Every one of the above has a real Unity `.meta` file in the commit, alongside a large, separately
authored Card Collection UI/scene/asset/localization drop — see §14 for what that implies and
doesn't imply about verification.

## 4. The six approved species and their bonuses

Every species owns exactly one `SeaCombat.Stats` field for its whole life, and no two species share
a stat (`Pets.cs`'s `EffectKind` enum has exactly 6 values, one per species).

| Species id (`Pets.Roster`) | TR/EN name | Stat (`Pets.EffectKind`) | `SeaCombat.Stats` field |
|---|---|---|---|
| `papagan` | Parrot | `Dodge` | `s.Dodge` |
| `maymun` | Monkey | `Salvo` | `s.Salvo` |
| `ahtapot` | Octopus | `Stun` | `s.Stun` |
| `gemi_faresi` | Ship's Rat | `Steal` | `s.Steal` |
| `yengec` | Crab | `Def` | `s.Def` |
| `kaplumbaga` | Turtle | `Hull` | `s.Hull` |

Matches the plan's §3 table exactly — no species/stat swaps were made.

## 5. Rarity and star progression

- Rarity reuses `RosterCardState.Rarity` (`Common=0 … Mythic=4`) — **no new rarity enum**, per the
  plan and per `CardCollection.cs`'s own precedent.
- `Pets.MaxStars = 5`. A pull always lands at star 1. Stars climb only by fusion (§6) — there is no
  leveling/XP mechanic for pets.
- `Pets.RarityCount = 5`, `Pets.SpeciesCount = 6`, `Pets.SlotCount = 3`, `Pets.FuseGroupSize = 3`.

### The value table (`Pets.DefaultEffectPerRarity`)

One shared curve shape (1:2:3:5:8, Common→Mythic), per stat:

| Stat | Common | Rare | Epic | Legendary | Mythic | Mythic★5 ceiling |
|---|---:|---:|---:|---:|---:|---:|
| Dodge / Salvo | 0.0011 | 0.0023 | 0.0034 | 0.0056 | 0.0090 | **+0.045** |
| Stun / Steal | 0.0009 | 0.0018 | 0.0026 | 0.0044 | 0.0070 | **+0.035** |
| Def (flat) | 0.20 | 0.40 | 0.60 | 1.00 | 1.60 | **+8.0** |
| Hull (flat) | 1.0 | 2.0 | 3.0 | 5.0 | 8.0 | **+40.0** |

`bonus(species, rarity, star) = PerRarity[EffectKind(species), rarity] × star` — linear in star,
exactly the plan's §7.1 formula. Dodge/Salvo/Stun/Steal ceilings sit at ~8–10% of `SeaCombat`'s own
cap for that stat (`DodgeCap`/`SalvoCap` 0.50, `StunCap`/`StealCap` 0.40); Def/Hull ceilings were
sized to "about one Rare-grade gear roll's worth," per the plan's §7.2 table.

## 6. Exact fusion rules (`Pets.TryFuse`)

```
3 copies at (species, rarity, star), star < 5   →  1 copy at (species, rarity, star + 1)
3 copies at (species, rarity, 5), rarity < Mythic →  1 copy at (species, rarity + 1, 1)
3 copies at (species, Mythic, 5)                →  refused — this is the ceiling, no fusion possible
```

Worked ladder example (exactly the brief's own example, continued):
```
Rare★1+Rare★1+Rare★1 → Rare★2 → Rare★3 → Rare★4 → Rare★5 → Epic★1 → … → Mythic★5 (terminal)
```
Climbing the whole ladder from Common★1 to Mythic★5 is exactly `RarityCount × MaxStars − 1 = 24`
fusion steps (asserted in `PetsTests.TheWholeLadderClimbsWithoutEverSkippingOrRepeatingARung`).

Ownership is a **flat `int[150]` count grid** (`6 species × 5 rarities × 5 stars`), not discrete
instanced items — every copy of the same `(species, rarity, star)` is fungible. A fusion decrements
3 from one cell and increments 1 in another, in the same service call. There is no duplicate
concept separate from ownership (every pull is immediately fusion fuel) and no overflow-to-gems rule
(unlike `CardCollection`) — a Mythic★5 copy simply has nothing left to do with it, which is fine.

## 7. Pearls — earning and spending

- **New root field:** `SaveData.pearls` (a `long`), living beside `charts`/`salvage` at the save
  root rather than inside `WalletData` — deliberately, so it reads as its own closed loop rather
  than as spendable on everything `WalletData`'s cash/gems already buy into.
- **Earned:** only via `ExpeditionService.RegisterWin()` — `_data.pearls += PearlsPerWin` once per
  won fight, guarded by the same `_atSea` check charts/salvage/`seaFightsWon` already use (a win
  registered ashore pays nothing).
- **`PearlsPerWin`** is a flat, non-tier-scaled number set by the bootstrap from
  `PetConfig.pearlsPerWin` (Inspector default `2`), falling back to `PetService.DefaultPearlsPerWin
  = 2` when no config asset is wired.
- **Spent:** only on pet chests, via `PetService.TryOpenChests`. There is no other spend path
  implemented — pearls cannot buy cash, gems, salvage, charts, craft points, or mining points.

> **⚠️ Deviation from the plan, flagged for GPT:** §9 of the plan proposed a **tier-scaled per-kill
> formula** (`PearlsFor(tier, kind) = round(PearlRate × PayoutMult[tier] × PearlShare ×
> KindLoot[kind])`, ~1 pearl/kill at tier 0 rising to ~7/kill at tier 3) **plus** small grants folded
> into the Daily Reward / Weekly Milestone / Achievement tracks, **plus** a one-time 100-pearl
> bootstrap grant the first time the pet panel is opened. **None of that shipped.** What shipped
> instead is the much simpler flat-per-win number above, and the Daily/Weekly/Achievement/bootstrap
> grants were never implemented. This was a scope call made during implementation (documented at
> the time as deliberately deferred), not an oversight — but it means the pearl economy currently
> paces very differently from what the plan's pacing estimates in §9/§10 assumed, and a player who
> has never won a sea fight has **zero** pearls and no chest, unlike the plan's "the first thing a
> new system shows a player must not be an empty wallet" intent.

## 8. Pet chest odds, pity, bulk (`PetChest.Tuning.Default`)

Copied verbatim from `CaptainCrate.Tuning.Default`, per the plan's explicit recommendation (reuse a
measured shape rather than author a new one):

| | Value |
|---|---:|
| Common / Rare / Epic / Legendary / Mythic weight | 0.600 / 0.260 / 0.105 / 0.030 / 0.005 |
| Epic pity | 10 chests |
| Legendary pity | 70 chests |
| Soft-pity start / step | 45 chests / +0.010 per chest past start |
| Single chest cost | 100 pearls |
| Bulk (10) cost | 900 pearls (10% discount vs. 10× single) |

**Structural difference from `CaptainCrate`/`CardCollectionPack`:** every rarity always carries all
six species (a pet's rarity is how far it's fused, not which species it is), so there is no "nobody
carries this rarity, zero its weight" case either of those two reference chests has to handle.
Species roll is a flat, independent `RollSpecies(roll) = floor(roll × 6)`, clamped to `[0,5]`.

Bulk opening is all-or-nothing: pearls are taken **before** the first roll, the whole batch rolls in
one call, and a short-of-cost bulk attempt spends nothing and returns `null`.

## 9. Pet slot progression / unlock rules

`PetService.SlotsUnlocked` reads `SaveData.seaFightsWon` against gates supplied by the bootstrap
(`PetConfig.SlotUnlockFightsWon(i)`, default `{0, 10, 25}` — same numbers the plan proposed, reused
from the sea-tier thresholds, but stored as **independent** numbers in `PetConfig`/`PetService`
rather than a literal reference to `Voyages.TierFightsRequired`. If `Voyages`'s gates ever change,
pet slot gates will **not** follow automatically — flagged because the plan's intent was a shared
reference, and what shipped is a one-time copy of the same numbers.

Gates are assumed ascending; `SlotsUnlocked` walks from slot 0 and stops at the first gate not yet
met. `Equip`/`Unequip`/`EquippedAt` all refuse a slot index `>= SlotsUnlocked`.

> **Missing from the plan:** `PetSaveData` does **not** store a `slotsUnlocked` field (the plan's
> §11 proposed storing it "derived-checkable but stored for a quick read"). The shipped version
> computes it live from `seaFightsWon` every time — arguably cleaner (no drift risk), but different
> from what was drafted. There is also no `bootstrapGranted` flag (see §7's pearl deviation).

## 10. How pet bonuses integrate with ship combat

`ExpeditionService.ShipStats()` (the single method that builds a fight's stat sheet) now reads:

```csharp
SeaCombat.Stats s = SeaCombat.OurStats(captain, level, crew, Loadout(), ct, _combat);
return Pets != null ? Game.Core.Pets.ApplyCombatBonus(s, Pets.CombatBonus()) : s;
```

- `PetService.CombatBonus()` sums, for every **unlocked, equipped** slot, `Pets.Bonus(kind,
  bestOwnedRarity, bestOwnedStar)` for that species — resolved live from the counts grid each call
  (a fusion made between two fights is worth more on the very next read, no caching to invalidate).
- `Pets.ApplyCombatBonus` adds each kind's total onto the matching `SeaCombat.Stats` field, then
  **re-clamps `Dodge`/`Salvo`/`Stun`/`Steal` to the exact same `SeaCombat` cap constants** gear and
  the captain already respect (`DodgeCap`, `SalvoCap`, `StunCap`, `StealCap`). `Def`/`Hull` are flat
  additions with no `SeaCombat`-side cap (their ceiling is the authored table itself).
- `SeaCombat.cs` was **never touched** — exactly the plan's §7.3 recommendation (option taken over
  the "first-class parameter on `OurStats`" alternative it also offered).
- `Pets` is an optional collaborator (nullable property, like `Cards`/`Crafting` already are on
  `ExpeditionService`) — unwired, `ShipStats()` behaves exactly as before pets existed.

**Exactly-once guarantee:** `ShipStats()` is a pure, derived read that stores nothing — calling it
repeatedly in the same frame returns the identical value every time. This is asserted directly by
`PetCombatIntegrationTests.CallingShipStatsRepeatedlyNeverStacksThePetBonus`.

## 11. Avoiding power inflation / overlap with other systems

- **No shared stat with captains/cards/economy.** Pets never touch `Shot` (the most inflationary
  stat in `SeaCombat.OurStats`), never touch cash/gems/income/craft points, and never touch any
  `CardCollection.EffectKind`. There is no surface for pets to compound with those systems at all.
- **1:1 species→stat mapping removes the need for an aggregate cap.** Because no two of the six
  species share an `EffectKind`, at most 3 equipped pets can ever be active and each contributes to
  a *different* stat — there is structurally no way for two pets to double up on the same number,
  so `Pets.cs` needed no `CardCollection.CapOf`/`Capped`-style aggregate-cap table of its own.
- **The shared `SeaCombat` caps are still the real backstop.** For the four percentage stats, a
  pet's contribution is added and clamped *after* gear+captain are already clamped, to the exact
  same constants — pets can only use up headroom already capped by `SeaCombat.cs`, never raise the
  ceiling.
- **Def/Hull ceilings were deliberately sized small** ("about one Rare-grade gear roll's worth" at
  Mythic★5), so a fully maxed pet roster reads as a second small gear slot, not a dominant new power
  source.
- **Pearls are a sealed currency** — earned only at sea, spent only on pet chests, convertible to
  and from nothing else. This is the same "closed loop" guarantee `charts`/`salvage` already carry.
- **Different axis from Card Collection.** Cards' sea-relevant effects (`SeaSalvageMultiplier`,
  `SeaChartMultiplier`) are *reward* multipliers applied in `RegisterKill`; pets are *combat stat*
  contributions applied in `ShipStats`. Different methods, different numbers, no way to double-count
  between them.

## 12. Save-data structure and persistence

```csharp
// SaveData.cs (root)
public long pearls;
public PetSaveData pets = new PetSaveData();

public class PetSaveData
{
    public int[] counts = new int[Pets.CountsLength];       // 150 = 6 species × 5 rarities × 5 stars
    public int[] equippedSpecies = { -1, -1, -1 };           // Pets.SlotCount, -1 = empty
    public int chestSinceEpic;
    public int chestSinceLegendary;
    public int chestsOpened;
    public bool Normalise();   // pads/repairs counts + equippedSpecies, drops unknown species, clamps negatives
}
```

- Flattened index: `Pets.CellIndex(species, rarity, star) = ((species*5)+(int)rarity)*5+(star-1)`.
- **Added without a save-version bump**, following every other block in `SaveData.cs` — an old save
  arrives with `pearls = 0` and an empty `pets` grid, read as "never touched the feature."
- `PetSaveData.Normalise()` is called from `PetService`'s constructor: pads a short/null `counts`
  array (keeping what it had), pads/repads `equippedSpecies` to `Pets.SlotCount`, drops any
  equipped-species entry that no longer exists in the roster (sets it to `-1`), clamps negative
  counts/pity counters to zero. Covered by `PetServiceTests`.

### Persistence fix already made (beyond the original draft)

`PetService` now takes an optional `SaveService save = null` (wired from `GameBootstrap` as `Save`,
the same instance every other service shares) and calls `_save?.Save(_data)` at the end of `Equip`,
`Fuse`, and `TryOpenChests`. This mirrors the project's "a move reaches the disk before the screen
says it happened" rule (the same one `GearStash`/`ExpeditionService` already follow) — a pet chest
open, fusion, or equip change now writes to disk in the **same call** as its pearl spend / grid
mutation, so a force-close right after the action cannot refund pearls or re-roll a pull. This was
**not** in my first draft of `PetService` — it was added afterward and is verified by four
persistence tests in `PetServiceTests.cs` that load the file back via `SaveService.TryLoad` and
check the written values.

## 13. Tests added (all in `Assets/Scripts/Tests/EditMode/`)

All four files are deterministic — no live RNG sampling without a fixed seed or a roll swept
exactly across `[0,1)`.

**`PetsTests.cs`** — roster integrity (6 species, unique lowercase ids, no two share a stat), table
shape (`EffectKindCount × RarityCount` cells, all positive, monotonically increasing per row),
`Bonus` scaling linearly with star and clamping above `MaxStars`, fallback to the shipped table when
a config array is null, out-of-range kind/rarity safety, the full 24-step fusion ladder walk with no
skip/repeat, the Mythic★5 terminal case, invalid-cell fusion refusal, counts-grid index uniqueness,
`TryBestOwned`'s rarest-then-highest-star tie-break, and `ApplyCombatBonus` (adds once, clamps to the
real `SeaCombat` caps, null-safe, ignores negative/out-of-range cells).

**`PetChestTests.cs`** — rarity distribution matches configured weights (200,000-point exact sweep,
not sampling), roll clamping on bad input (NaN/negative/≥1), every rarity always carries weight
(the one structural difference from the two reference chests), short-pity window never exceeded,
a full bulk-10 open always contains an Epic-or-better, long-pity window never exceeded, soft-pity
ramp only climbs after its start, floor/guarantee resolution when both pities are due at once, pity
can be switched off, pity-counter advance rules (Legendary clears both, Epic clears the short one,
anything lesser lengthens both), species roll is flat/even across all six, bulk pricing is strictly
cheaper than N singles.

**`PetServiceTests.cs`** — save contract (pre-pets save with `data.pets = null`, short/corrupt
`counts` array padded in place, an equipped species no longer in the roster dropped to `-1` on load,
a fully-null `SaveData` survivable throughout); chest opening (insufficient pearls is a true no-op,
a successful open spends pearls/grants a star-1 copy/advances pity, a bulk open is one all-or-
nothing spend); fusion (too few copies refused, a successful 3-in-1-out mutation, overflow copies
beyond 3 left untouched, Mythic★5 cannot fuse even with 9 copies on hand); the slot gate (unlocks
exactly at 0/10/25 wins); equip rules (a locked slot refuses, an unowned species refuses, the same
species cannot ride two slots at once, unequip always clears an unlocked slot); combat-bonus
aggregation (zero with nothing equipped, reflects the equipped pet's current rarity/star, rises
immediately after a fusion with no extra step, drops to zero on unequip); and **persistence** — a
chest open, a bulk open, a fusion, and an equip/unequip each reach an actual on-disk save file in
the same call, verified by reloading via `SaveService.TryLoad`.

**`PetCombatIntegrationTests.cs`** — an unwired `PetService` leaves `ShipStats()` exactly as before;
an equipped pet's bonus reaches `ShipStats()` exactly once; repeated `ShipStats()` calls never stack
the bonus; a pet bonus stacked on top of gear that has *already* hit `SeaCombat.DodgeCap` on its own
still respects that one shared cap after combining; `RegisterWin()` pays the configured pearls
exactly once per call (two wins pay twice, not double the first); zero `PearlsPerWin` pays nothing
without throwing; a win registered while ashore pays no pearl.

Total: roughly 60 new test methods across the four files (exact count not recounted here — run the
suite to get it).

## 14. What has and hasn't been verified in Unity/MCP

- **UnityMCP was disconnected for this entire working session** (repeated `ConnectionRefused`). I
  personally never ran `refresh_unity`, `run_tests`, or `read_console` at any point while writing
  this code. I cannot personally confirm it compiles, that any test passes, or that the Console is
  clean.
- **Circumstantial evidence it compiles:** every pet file now carries a real Unity-generated `.meta`
  file, and they are part of commit `d6a8017`, a large batch (44 files) that *also* finished a
  separately-authored Card Collection feature (its own UI, scene wiring, config asset, and
  localization). That batch's scale strongly suggests a live Unity Editor session imported and at
  least attempted to compile everything in it around the time of the commit. **This is an inference
  from repo state, not a verification I performed** — the very first thing GPT should do is
  `refresh_unity` (request compile) and `read_console`, then `run_tests` on the EditMode suite.
- **Zero Play Mode verification exists.** There is no UI to click through yet, so none of the plan's
  §20 Play-mode checklist (open panel, open chests, fuse, equip, fight, confirm bonus applied once
  in a live fight, confirm Console stays clean) has been attempted.
- The EditMode baseline recorded before this work began was **1045/1049 green, 4 pre-existing
  `RenderingSafetyTests` failures** (unrelated to pets). GPT should confirm the pet work doesn't move
  that baseline down, and should expect the total green count to rise by roughly the new pet test
  count once they pass.

## 15. Known bugs, risks, unfinished integration points, design caveats

- **Pearl economy is simpler than planned** — see the deviation box in §7. No tier scaling, no
  Daily/Weekly/Achievement grants, no one-time bootstrap grant. If the original pacing intent
  matters, this needs real design/implementation work, not just wiring.
- **No `PetConfig.asset` instance exists.** `GameBootstrap.petConfig` is an unassigned `[SerializeField]`
  reference — the whole system currently runs on code-level defaults
  (`Pets.DefaultEffectPerRarity`, `PetChest.Tuning.Default`, `PetService.DefaultPearlsPerWin = 2`,
  `PetService.DefaultSlotUnlockFightsWon = {0,10,25}`). This is safe (matches the project's "runs
  without a config" convention) but means there is currently no Inspector tuning surface a designer
  can actually touch without first creating the asset and wiring it into the `Bootstrap` scene.
- **Zero UI exists.** No pet roster/collection screen, no fusion screen, no chest-opening/reveal
  screen, no equip picker, no HUD "Daha Fazla" opener entry, no `SeaFightUI` pet-slot buttons.
  Nothing a player can currently see or interact with.
- **Zero localization.** Species ids (`papagan`, `maymun`, `ahtapot`, `gemi_faresi`, `yengec`,
  `kaplumbaga`) are loc-key-shaped per project convention, but no `dost.*` rows were added to
  `Assets/Resources/Diller/metinler.txt` in any of the 11 languages. The plan's §0 also explicitly
  calls for a collision check (grep `metinler.txt` for `dost` and for the chosen currency word)
  before any loc keys are authored — **this check was never run**.
- **Zero art/sound.** `PetConfig.SpeciesArt`/`ChestIcon`/`PearlIcon` fields exist and are empty; no
  species icons, chest icon, currency icon, or fuse/chest-open SFX exist yet.
- **Design judgment call, not a bug:** `Equip()` refuses the same species in two slots
  simultaneously. Documented in the code's own XML comment, but worth GPT knowing it was a
  deliberate choice rather than rediscovering it.
- **Design judgment call, not a bug:** combat always resolves an equipped species to its single
  best-owned `(rarity, star)` — a player cannot choose to field a lesser copy. Confirm this matches
  the intended UX before building the equip picker (the picker need only ever offer "equip this
  species," never "equip this specific copy").
- **No inventory cap exists for pets, by design** (§6/§12 of the plan) — this is not a missing
  feature, it's the structural answer to the "full inventories" edge case from the original brief.
  Don't go looking for a missing capacity rule.
- **Slot gates are a numeric copy, not a shared reference**, of `Voyages`'s tier thresholds (see
  §9's deviation note) — if those ever move, pet gates need a manual update to match.
- **Constructor signature drift risk:** `PetService`'s constructor has grown optional trailing
  parameters twice already (`slotUnlockFightsWon`, then `random`, then `rarityTint`, then `save`).
  Any future change should keep new parameters trailing/optional the way the existing ones are, or
  every test-file call site (`PetServiceTests.cs`, `PetCombatIntegrationTests.cs`) needs updating.

## 16. Concurrent Card Collection work touching shared files

A separate, parallel effort (not mine, not coordinated with me in real time) implemented and
finished the **Card Collection** feature (`Docs/PLAN_14_CARD_COLLECTION.md`) during the same working
window, including its UI, scene wiring, and a `CardCollectionConfig.asset`. That work landed in the
same commit (`d6a8017`) as the pet backend, purely because of commit timing — the two features are
functionally unrelated (no shared code paths), but they **do share edited regions of three files**:

- **`GameBootstrap.cs`** — Card Collection added: a `cardCollectionConfig` field, a `CardCollection`
  property, its construction (right after `Foremen`), and its cross-wiring into `Goals`, `Market`,
  `Expeditions.Cards`, and `Crafting.Cards`. I confirmed (by re-reading the file before each of my
  own edits) that my pet wiring landed cleanly with **no duplicate fields, properties, or
  construction calls** — but GPT should re-read the current file rather than trust any older diff,
  since it changed under me mid-session at least once.
- **`ExpeditionService.cs`** — Card Collection added the `Cards` property and its use inside
  `RegisterKill` (chart/salvage multipliers). Pets added the `Pets`/`PearlsPerWin` properties and
  their use inside `ShipStats`/`RegisterWin`. These are two independent optional collaborators on
  the same class; they do not interact.
- **`Goals.cs` / `GoalService.cs`** — changed for Card Collection's goal/metric hooks. **Pets do not
  touch either file at all currently** — this is exactly the gap described in §7/§15 (no
  Daily/Weekly/Achievement pearl grants exist). If GPT wants to add that later, re-read both files'
  *current* state first — they've changed since the original Plan 15 doc was written and don't
  necessarily look like what that plan assumed.
- `SaveData.cs` also gained Card Collection's own save block in the same commit; it does not overlap
  with the `pearls`/`pets` fields pets added.

Bottom line for GPT: nothing here needs to be reconciled or merged — both features are already
committed together and compile-compatible by construction (confirmed by inspection, not by a build).
Just be aware that re-reading these three files before editing them is worth the cost, since they
carry more than one feature's worth of recent history now.

---

# Remaining work (ordered)

1. **Reconnect Unity / first compile check.** `refresh_unity` (request compile), `read_console` —
   confirm 0 new errors/warnings before touching anything else.
2. **Run the EditMode suite** (`run_tests`, `EditMode`). Confirm the new pet tests pass and the
   existing suite doesn't regress below its 1045/1049 (4 pre-existing, unrelated) baseline. Fix
   anything red before proceeding.
3. **Create `Assets/Data/PetConfig.asset`** (via Unity, `Assets > Create > Ore Empire > Pet Config`)
   and assign it to `GameBootstrap`'s `petConfig` field in the `Bootstrap` scene.
4. **Decide on the Pearl-economy deviation** (§7/§15): ship the simpler flat-per-win number as-is,
   or implement the plan's original tier-scaled formula plus Daily/Weekly/Achievement/bootstrap
   grants. This is a design decision to make before building UI copy that might reference it.
5. **Localization collision check** (plan §0, never run): grep `Assets/Resources/Diller/metinler.txt`
   and every `.cs` file for `dost` and for the currency word before authoring any loc keys.
6. **Localization:** author `dost.*` key family (species names, archetype/flavour lines, reuse
   `kaptan.derece.*` for rarity words, chest/currency/panel strings, fuse/equip confirmation
   strings) across all 11 languages.
7. **Art:** six species icons (parrot/monkey/octopus/rat/crab/turtle) in the project's existing PIL
   house style, one chest icon, one currency (Pearl) icon. Reuse `CardCollectionConfig`'s rarity tint
   palette rather than authoring a second one.
8. **Sound:** reuse existing UI tap/panel-open stingers; author one new "fuse" chime sized like the
   existing level-up/upgrade stinger.
9. **`UI/PetRosterUI.cs`** — new screen mirroring `CaptainRosterUI.cs`'s layout (chest/currency/pity
   column + six-species roster column), with species detail (icon, archetype, current best
   rarity/star, live bonus number, per-cell counts) and a fuse button on any cell with ≥3 copies.
10. **`SeaFightUI.cs` extension** — three pet-slot buttons added to the existing persistent sheet
    panel alongside the four gear slots and the captain line, same visual language (grade-tinted
    frame, star pips, tap → species picker).
11. **HUD opener** — add the pet panel's opener to the "Daha Fazla" (More) sheet at sort order 103,
    per the shipped HUD convention (see `hud-more-menu-and-safe-area-audit` memory).
12. **Equip flow wiring** — species picker on `SeaFightUI`'s slot buttons, cycling-chip pattern
    (matches `VoyageUI`'s officer pickers): tap → walk nobody → each owned species → nobody again.
13. **Chest-opening flow wiring** — single/bulk-10 buttons on `PetRosterUI`, cost shown before the
    press, pity counters printed as "N until guaranteed Epic+," reveal flourish (reuse
    `ConfettiBurst` on a rarity-crossing pull only).
14. **Fusion flow wiring** — fuse button inside the species detail card; confirm the star/rarity
    result shown matches `Pets.TryFuse` exactly, including at least one rarity-crossing fusion in
    manual testing.
15. **Scene wiring** — add `PetRosterUI` (and any new prefabs) to `Main.unity`, wire all Inspector
    references, confirm `GameBootstrap.petConfig` and every new `[SerializeField]` is actually
    assigned (not left null) in the scene asset.
16. **New/updated tests as UI lands** — at minimum, confirm no regression in the four existing pet
    test files; consider a `PetCombatIntegrationTests`-style smoke test for the new UI if the
    project's UI-smoke-test convention (see `CardCollectionUiSmokeTests.cs`, added in the same
    commit) should be followed for consistency.
17. **Play Mode verification**, end to end, per the plan's §20:
    - Launch `Main.unity`, open the pet panel from "Daha Fazla."
    - Open a single chest, then a bulk-10; confirm pearl spend is exact and the bulk-10 always
      contains an Epic-or-better.
    - Fuse three same-tier copies of one species; confirm the result matches §6 exactly, including
      one deliberate rarity-crossing fusion.
    - Equip a pet in each of the three slots (unlock slots 2/3 via `seaFightsWon`, or a debug grant
      if faster); confirm the sheet's stat line moves by exactly the pet's bonus.
    - Enter sea combat and fight; confirm the equipped pets' bonus is reflected in actual combat
      rolls, and confirm it is applied exactly once (a debug probe calling `ShipStats()` twice in a
      frame should show no movement).
    - Fuse away an equipped species' only copy mid-session; confirm the slot resolves to empty (or
      the next-best copy) without an exception or a stale UI pet.
18. **Final Console check** — confirm the Unity Console is clean (no new warnings, no exceptions)
    through the entire flow above, then re-run the full EditMode suite one more time before calling
    the feature done.
