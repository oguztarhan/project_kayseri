# Pet System Completion Plan

Status: planning only. No gameplay implementation is authorized by this document until the current phase is approved.

Owner: Claude Code / Codex implementation workflow  
Feature: Deniz Dostları (Pet Companions)  
Target: Unity 6.4.9f1, Android, existing sea-combat and collection architecture

## 1. Locked decisions

The following decisions are approved and must not be changed implicitly during implementation:

1. **Pearl economy:** use tier-scaled pearl income and a one-time 100-pearl bootstrap grant when the pet panel is first opened.
2. **Chest odds:** retain the Captain Chest-shaped odds and pity values:
   - Common 60%
   - Rare 26%
   - Epic 10.5%
   - Legendary 3%
   - Mythic 0.5%
   - Epic pity at 10 chests
   - Legendary pity at 70 chests
   - Legendary soft pity starts at chest 45 and adds 0.010 weight per chest
3. **Fusion:** retain the three-identical-copies rule, with a controlled Pet Essence escape valve for high-end progression.
4. **Maxed duplicates:** a Mythic 5-star duplicate becomes Pet Essence instead of becoming inert.
5. **Free acquisition:** add the bootstrap grant plus small daily, weekly, sea-milestone, and achievement grants. Do not add a free chest every day.
6. **Slots and roster:** six launch species; three slots; gates at 0, 10, and 25 sea-fight wins.
7. **Visual workflow:** use safe placeholder art first, then replace it with final ocean/pirate art before release.
8. **Localization:** ship all 11 existing languages.
9. **Verification:** fix the four pre-existing Copper RenderingSafety failures so the final completion gate has no known test failures.

The work is intentionally phased. Each phase ends with a compile/test/checkpoint report. The next phase must not start until the checkpoint is green and the user approves continuation.

## 2. Current repository baseline

The backend already exists in `Assets/Scripts/Core/Pets.cs`, `PetChest.cs`, `Data/PetConfig.cs`, `Systems/PetService.cs`, `ExpeditionService.cs`, `GameBootstrap.cs`, and `SaveData.cs`.

The existing backend currently has:

- six species, one combat stat per species;
- three slots and five rarities;
- one-to-five stars;
- three-copy fusion;
- pearl chest costs of 100 / 900;
- save normalization and persistence;
- combat bonus application in `ExpeditionService.ShipStats()`;
- 70 passing pet EditMode tests.

It does not yet have a `PetConfig.asset`, pet UI, pet art, pet localization, milestone reward integration, or Play Mode verification. The current full EditMode run completes 1259 tests and reports four Copper rendering failures; those failures are part of Phase 0 and must be resolved before final sign-off.

## 3. Balance contract

### 3.1 Pearl income

Pearls remain a closed currency. They may only be earned by approved pet reward sources and spent on pet chests. They may not buy cash, gems, charts, salvage, craft points, mining points, or gear.

The primary sea-fight formula is:

```text
PearlsForWin(tier, enemyKind) = max(
    1,
    round(4.0 * Voyages.PayoutMult[tier] * 0.06 * SeaCombat.KindLoot[enemyKind])
)
```

This reuses the existing sea progression instead of inventing a parallel tier curve. With the current `PayoutMult` values of 1, 3, 9, and 24, the expected result is approximately 1 pearl in early waters, 2–3 in mid waters, and 5–7 in the far reach, depending on enemy kind.

The implementation must pass the actual `tier` and `enemyKind` from `EncounterController` to the one authoritative payout method. A win must pay exactly once, only after a successful fight, and never for a mid-fight plunder event or a win registered while ashore.

### 3.2 Bootstrap and recurring rewards

Add `bootstrapGranted` to the pet save block. The first successful opening of the pet panel grants exactly 100 pearls, sets the flag, saves immediately, and cannot grant again after reload.

Starting values for the additional sources are:

- Daily reward: 20 pearls once per UTC day.
- Weekly milestone: 100 pearls at the existing 100-point weekly milestone.
- Sea-fight milestones: 25 pearls at 10 wins, 50 at 25 wins, and 100 at 50 wins; each is claimable once.
- Achievement rewards: 25 pearls on the first pet-related achievement tier, then 50 and 100 on later tiers; use stable IDs and idempotent claim records.

These values are intentionally small relative to the existing 30-energy pool and the 100-pearl chest price. The daily reward is roughly ten early fights, while the weekly reward is one single chest. All values must be Inspector-configurable through `PetConfig` or the owning existing reward config, with these values as defaults.

### 3.3 Chest pacing

Keep the approved chest prices:

- Single: 100 pearls.
- Bulk 10: 900 pearls.

Bulk opening remains all-or-nothing. The short pity guarantee must still be reached within a default ten-chest batch. If an Inspector edit makes that invariant false, the config validation/test must report it rather than silently promising a guarantee the table cannot provide.

### 3.4 Pet Essence

Add a non-spendable progression resource named `Pet Essence`.

- It is stored in `PetSaveData`.
- It cannot open chests and cannot be converted to pearls.
- A Mythic 5-star duplicate converts to one Essence.
- Essence can replace one missing copy in a fusion group, never more than one Essence per fusion.
- The normal rule remains three identical copies; Essence only prevents a maxed duplicate from becoming dead inventory.
- Every Essence grant and spend must be saved atomically with the source action.

The exact fusion UI must display whether the third input is a real copy or Essence.

## 4. Phase-by-phase implementation order

### Phase 0 — Baseline and Copper test repair

Before editing pet gameplay:

1. Refresh Unity and confirm the project compiles.
2. Re-run the full EditMode suite and capture the four Copper failures.
3. Inspect the missing Copper scene/prefab references and repair only the smallest existing rendering/test contract needed to make those tests pass.
4. Re-run the full suite and read the Console.

Checkpoint: zero compile errors, zero warnings introduced by the repair, and zero EditMode failures. If the Copper repair requires a broader art/scene change, stop and request approval before proceeding.

### Phase 1 — Pet save and economy contract

Update the existing pet backend without creating UI:

1. Extend `PetSaveData` with `petEssence`, `bootstrapGranted`, daily reward stamp, and stable claimed milestone IDs as needed by the existing goal/reward architecture.
2. Keep backward compatibility: missing fields become zero/false/empty, arrays are padded, negative values are clamped, and old saves are never reset.
3. Add a single authoritative pearl grant API in `PetService` or a dedicated `PetRewardService`; do not write `data.pearls` from multiple unrelated callers.
4. Replace flat `PearlsPerWin` behavior with the tier/kind formula.
5. Add idempotent bootstrap, daily, weekly, sea-milestone, and achievement reward paths.
6. Add `PetConfig` tuning fields for all reward amounts and validate non-negative values.
7. Preserve atomic save semantics: spend/grant, pity update, inventory update, and claim flag must be persisted in the same operation.

Tests to add/update:

- tier/kind pearl formula and floor behavior;
- win pays once, loss/plunder does not pay;
- ashore win pays nothing;
- bootstrap pays once across reload;
- daily and weekly claims are UTC-based and idempotent;
- milestone and achievement claims cannot be repeated;
- old saves normalize correctly;
- Essence grants and spends persist atomically.

Checkpoint: pet tests plus the full EditMode suite pass; no UI work starts before this checkpoint.

### Phase 2 — Fusion and duplicate progression

1. Add Essence-aware fusion APIs while preserving the existing exact three-copy path.
2. Reject invalid species, rarity, star, insufficient real copies, insufficient Essence, and Mythic 5-star non-duplicate fusion.
3. Convert Mythic 5-star chest duplicates to Essence during the chest transaction.
4. Ensure an equipped pet resolves to the best remaining owned copy immediately after fusion.
5. Add a clear result object for the UI: consumed copies, Essence spent, result rarity/star, and whether the result crossed rarity.

Tests to add/update:

- every star transition;
- every rarity transition;
- Essence substitution consumes exactly one Essence;
- invalid mixed inputs fail without mutation;
- Mythic 5-star duplicates become Essence;
- equipped pets never retain stale stats;
- maxed pets cannot generate an infinite chest/refund loop.

Checkpoint: deterministic fusion and duplicate tests pass, including a complete ladder walk and persistence tests.

### Phase 3 — Config asset and Bootstrap wiring

1. Create `Assets/Data/PetConfig.asset` using the existing ScriptableObject menu.
2. Populate it with the approved chest odds, costs, reward amounts, slot gates, effect table, rarity colors, and placeholder sprites.
3. Assign it to `GameBootstrap.petConfig` in `Bootstrap.unity`.
4. Add OnValidate checks for array lengths, positive chest weights, ascending slot gates, valid pity windows, bulk discount, and the ten-chest Epic guarantee.
5. Confirm the runtime uses the asset values rather than silently falling back to code defaults.

Checkpoint: asset reload, Bootstrap construction, and config-validation tests pass; inspect the scene in Unity and confirm no missing serialized references.

### Phase 4 — Pet collection UI

Create `Assets/Scripts/UI/PetRosterUI.cs`, following the existing `CaptainRosterUI` and `CardCollectionUI` patterns.

The panel must show:

- pearl balance;
- Pet Essence balance;
- single and bulk chest buttons with exact costs;
- Epic and Legendary pity counters;
- six species cards;
- owned count by rarity/star;
- current best rarity/star;
- live combat bonus;
- fusion button and a precise fusion preview;
- locked/unlocked slot information;
- last chest result and rarity color.

All UI actions must call service methods and never mutate save data directly. Refresh through `PetService.Changed` and localization changes. Use dynamic labels only on a separate canvas when appropriate, following existing mobile canvas conventions.

Add the pet opener to the existing “Daha Fazla” HUD sheet and preserve the existing sort/order convention.

Add UI smoke tests for hierarchy, disabled states, pity text, fusion preview, insufficient pearls, and no-owned-pet states.

Checkpoint: Main scene opens the pet panel, all buttons have correct interactability, and no Console errors occur.

### Phase 5 — SeaFightUI equipment flow

Extend `SeaFightUI` with three pet slots beside the existing captain and gear information.

1. Show each slot’s pet portrait, rarity tint, star count, and bonus.
2. Show fight-count requirements for locked slots.
3. Add a species picker that cycles through unowned/owned species and prevents the same species in two slots.
4. Support unequip without losing ownership.
5. Refresh immediately after chest open, fusion, equip, unequip, and sea-fight victory.
6. Display the same derived value used by `ExpeditionService.ShipStats()` so the sheet cannot disagree with the fight.

Add a Play Mode or integration smoke test proving:

- no pet means the old stats are unchanged;
- one equipped pet adds exactly one bonus;
- repeated `ShipStats()` reads do not stack;
- a capped base stat remains capped;
- fusing the equipped species updates the next read;
- removing the last owned copy clears the effective bonus safely.

Checkpoint: live equip and combat-sheet values match deterministic service calculations.

### Phase 6 — Chest, fusion, and reward presentation

1. Build single-chest and bulk-10 reveal flows.
2. Persist before reveal animation finishes.
3. Show rarity, species, star, Essence conversion, remaining pearls, and pity counters.
4. Reuse existing reward/confetti/haptic/audio systems where safe.
5. Add an explicit confirm step for fusion and a clear “not enough materials” message.
6. Ensure closing/reopening the panel never grants or spends a second time.

Checkpoint: manual Play Mode sequence works from a clean save and from a save loaded after force-close simulation.

### Phase 7 — Localization and art pass

1. Add stable `dost.*` localization keys for all six species, archetypes, stats, rarities, chest actions, Essence, pity, slot gates, fusion results, errors, and reward sources.
2. Fill all 11 languages using the existing localization file format.
3. Start with safe placeholder sprites during Phases 4–6.
4. Replace placeholders with final pirate/ocean art before release:
   - parrot, monkey, octopus, ship rat, crab, turtle;
   - pearl icon;
   - pet chest icon;
   - rarity frames and star pips.
5. Add or reuse suitable chest-open, fusion, success, and equip sounds.
6. Verify portrait aspect ratios, compression, atlas usage, and Android memory impact.

Checkpoint: all 11 languages render without missing keys; final art is wired in the `PetConfig` asset; no placeholder is present in the release scene.

### Phase 8 — Full verification and release gate

Run the following in order:

1. Unity refresh and compile.
2. Full EditMode suite.
3. Pet-specific deterministic suite.
4. Play Mode from a clean save:
   - open the pet panel and receive the bootstrap grant;
   - open single and bulk chests;
   - verify pearl spend and pity progression;
   - fuse within a rarity;
   - perform a 5★ rarity-crossing fusion;
   - spend Essence in a fusion;
   - equip all three slots after unlocking them;
   - enter sea combat;
   - verify each bonus affects the intended stat once;
   - verify repeated reads do not stack;
   - fuse away an equipped pet and confirm safe fallback/empty state;
   - claim daily, weekly, sea-milestone, and achievement rewards once each;
   - reload and confirm all balances, pity, claims, pets, equipment, and Essence persist.
5. Read the Unity Console after every major flow and at the end.
6. Confirm zero compile errors, zero new warnings, zero test failures, and no missing localization or serialized-reference errors.

Only after this phase is complete should the feature be reported as done.

## 5. Required implementation rules

- Do not hand-edit `.unity`, `.prefab`, `.asset`, or `.meta` files; use Unity MCP/editor tooling.
- Keep every designer-facing value in `[SerializeField] private` fields.
- Do not add a second currency wallet; pearls and Essence remain pet-owned state.
- Keep `SeaCombat.cs` unchanged unless a verified integration failure requires a narrowly scoped change.
- Keep one authoritative writer for pearls and one authoritative writer for pet inventory mutations.
- Save before acknowledging chest, fusion, equip, or reward success to the UI.
- Do not use per-frame allocations for UI refresh or combat paths.
- Do not begin the next phase after a failed checkpoint; report the failure and wait for approval.

## 6. Definition of done

The pet system is complete only when:

- all approved economy and fusion decisions are implemented;
- the system is visible and usable from the live game HUD;
- all six pets can be collected, fused, equipped, and used in combat;
- free rewards and milestone rewards are idempotent;
- old saves load safely;
- all 11 languages and final art are present;
- deterministic tests cover fusion, odds, pity, economy, save/load, rewards, Essence, and combat;
- the full Unity MCP verification passes with a clean Console and zero known test failures.
