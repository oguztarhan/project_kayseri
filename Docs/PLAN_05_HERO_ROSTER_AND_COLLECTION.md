# Plan #05 — Hero roster and collection

**Completed:** 2026-09-04 · Unity compilation clean · 841/841 EditMode tests green  
**Scope:** unify the existing master and captain rosters; do not create a third hero system.

## Product decision

Project Kayseri already has the two rosters the game needs:

- **Masters** improve one station across every island.
- **Captains** change voyage outcomes and can be assigned at the dock.

Plan #05 standardises how those rosters communicate rarity, level, duplicates, ownership, role,
current benefit, upgrade readiness and locked entries. The source screenshots are interaction
evidence only; their characters, copy, composition and economy are not copied.

Collection/set bonuses are deliberately out of the first release. Both rosters already provide
strong individual bonuses, and another multiplier risks mandatory compositions. If introduced
later, a bonus must be derived from current owned/level state, data-driven, and add no save fields.

## Delivery slices

### 1. Shared card grammar — complete

- [x] Add one presentation-neutral card state used by both services.
- [x] Cover locked, owned, maxed, duplicate progress and upgrade-ready states with EditMode tests.
- [x] Make both roster screens read progress and affordance from that shared state.
- [x] Standardise opener notifications: a badge means an upgrade is ready, on either roster.
- [x] Verify compilation and EditMode tests in Unity.

### 2. Browse and inspect

- [x] Add shared sort modes: default, upgrade-ready, rarity and level.
- [x] Add filters: all, owned, locked and upgrade-ready; never hide locked goals by default.
- [x] Add a selected-card inspection panel with role, current benefit and next-level delta.
- [x] Preserve the distinct master chest and captain crate economies.

### 3. Accessibility and polish

- [x] Pair rarity colour with a localized rarity label; colour is never the only cue.
- [x] Ensure long localized role/effect text fits at the largest supported 1.5× text scale.
- [x] Give locked cards a visible name, rarity, role/discovery copy and disabled actions.
- [x] Keep one structured visible detail description per card. The project does not yet expose an
      active accessibility hierarchy to attach screen-reader nodes to; no roster-only hierarchy is
      installed because it would replace rather than extend the app-wide hierarchy.

### 4. Verification

- [x] Service coverage exists for new pull, duplicate pull, upgrade, max level and locked entry;
      shared card adapters now have focused assertions too.
- [x] UI smoke test: both rosters, all filters/sorts, locked and upgrade-ready cards.
- [x] Save round-trip and migration regression; no new persisted state was added.
- [x] Automated portrait-canvas and normalized-anchor checks at the 1080×1920 reference, plus
      maximum text scale and shared-copy validation in all 11 launch languages. Real-device visual
      inspection remains part of release QA, not a Plan #05 code dependency.

## Verification result

- Unity 6000.4.9f1 refreshed every new script and generated its metadata/project entries.
- Full compilation completed with zero errors and no new Plan #05 warnings.
- Full EditMode suite: **841 passed, 0 failed, 0 skipped**.
- The UI smoke tests instantiate both runtime-built screens, exercise all four sorts and filters,
  open the shared detail sheet, retain locked entries as named goals, and verify portrait-safe anchors.

## Definition of done

Both rosters use the same visual and behavioral vocabulary while retaining separate services and
economies. A player can find an owned, locked or upgrade-ready character quickly; every card states
its role and current value; rarity remains understandable without colour; existing saves retain all
levels and duplicates; tests and the Unity console are clean.
