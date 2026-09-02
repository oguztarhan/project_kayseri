# Ustalar (Masters) — chests, stars, tiers

**Date:** 2026-09-02 · **Status:** code complete, compiles clean, 524 EditMode tests green.
**Outstanding:** one scene value needs the Unity Editor (§5).

The per-station foreman roster became a full collectible layer in the *Idle Weapon Shop* mould:
one master per building, rarity earned rather than assigned, cards from chests, and the master
standing on the island in the colour of his tier.

C# names are unchanged on purpose — `ForemanService`, `foremanLevels`, `Foremen` — because live
saves and the scene wiring address them. Only the product copy says "usta / master".

---

## 1. What changed for the player

| | before | after |
|---|---|---|
| Getting a master | 150–900 gems to hire, then cards | first card unlocks him at 1★, and is still banked |
| Rarity | fixed per slot (mine was always Epic) | earned: stars 1-10 promote through 5 tiers |
| Star-up cost | cards **and** gems | cards only |
| Top boost | +45% to a station | **+300%** at Legendary, +500% at Mythic |
| Card source | goals / contracts / chapters / voyages | those, **plus a gem chest and a free chest every 8h** |
| On the island | one strongman clone at each station | a different body per station, sized and plinth-tinted by tier |

**Tiers.** Two stars apiece: 1-2 Sıradan, 3-4 Nadir, 5-6 Destansı, 7-8 Efsanevi, 9-10 Mitik.

**Boost per star** (added to 1.0, on that station's throughput):

| ★ | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| | .10 | .20 | .45 | .70 | 1.10 | 1.60 | 2.30 | **3.00** | 4.00 | 5.00 |

The curve accelerates so a promotion is always felt: the second star of a tier is worth more than
the first, and the first star of the next tier more again. Pinned by `ForemenTests`.

**Empire income** = `1 + Σ boost × 0.10`. Eight Legendary masters land at **3.4×** — exactly where
the old maxed roster landed, which is where the ladder was solved. Mythic stretches the tail to
5.0× rather than moving the floor. The dual effect is deliberate and unchanged: on an island already
at its income cap, throughput alone pays nothing, and the income share is the half that lifts the
ceiling.

**Chest.** 3 cards for 60 gems, ×10 for 540. One card per chest is **aimed** at the owned master
furthest behind; the rest roll flat over all eight. No rarity pity, because a chest rolls a *slot* —
there is no rarity to be starved of. Free chest: 2 cards every 8 h, banks at most one, never expires.

**Pacing.** 90 cards per master, 720 for the set ≈ 2–3 months of mixed play; one master maxed in
3–5 weeks. All-bought ≈ 14 400 gems, which is where the gem sink moved to now that hiring is gone.

---

## 2. Code map

| File | What it holds |
|---|---|
| `Assets/Scripts/Core/MasterChest.cs` | **new** — chest maths. Pure functions, the roll is an argument. |
| `Assets/Scripts/Core/Foremen.cs` | `Tier`, `TierOf`, `Boost`; rarity table and hire pricing deleted. |
| `Assets/Scripts/Systems/ForemanService.cs` | chest transaction, free-chest clock, auto-unlock, tier palette. |
| `Assets/Scripts/Data/ForemanConfig.cs` | every number above, Inspector-editable, with the reasoning in the tooltips. |
| `Assets/Scripts/UI/ForemanRosterUI.cs` | chest shelf + 8 tier-framed cards + the card-reveal ceremony. |
| `Assets/Scripts/Gameplay/StationForemen.cs` | distinct body per station, per-tier scale, tinted plinth. |
| `Assets/Scripts/UI/BuildingSigns.cs` | name plates fade out when the camera comes in. |
| `Assets/Scripts/UI/CameraController.cs` | `ZoomT` — where the camera sits in its own zoom band. |

**Save.** `SaveMigration.CurrentVersion` is **still 7** and must stay there. Stars *are*
`foremanLevels`, so a live save needs no reinterpretation; the only new fields are two appended
ones (`masterFreeChestClaimUnix`, `masterChestsOpened`) plus `Normalise()`.

**One migration does run.** A pre-rework save can hold cards for a master who was never hired —
every reward path paid cards from the first hour while the first hire cost gems. Hiring is gone, and
the aimed card skips unowned slots, so those cards would have been invisible and unspendable.
`Normalise()` stands up any slot that has cards but no stars, keeping the cards banked.

**The tier palette lives in one place** — `ForemanConfig.tierTint`, read through
`ForemanService.TierTint()` by both the card frames (Game.UI) and the plinths (Game.Gameplay).
A Legendary that was gold on the card and purple on the ground would be worse than no colour.

---

## 3. Camera and declutter

`defaultZoomFraction` 0.21 → **0.14**, which puts a station near ninety pixels tall on a 1284-high
screen instead of sixty. Nothing is taken away: `zoomOutFactor` is untouched, so the old survey view
is one pinch away.

Building name plates now fade in only as the camera pulls back (`ZoomT` 0.30 → 0.50). Measured: the
opening shot sits at ZoomT ≈ 0.12 at the new zoom (≈ 0.26 at the old), so the plates are off at the
default framing and the master standing beside the building carries the identification instead.
The wrench and the upgrade badge are untouched — both are actionable and already gated.

---

## 4. Verified

- All six assemblies compile clean; no new warnings.
- 524 EditMode tests, 0 real failures (10 known-false offline: engine natives in
  SaveService/RenderingSafety, `Resources` loads in EconomySim, runner isolation in ServiceLocator).
- New: `MasterChestTests` (13), `ForemanServiceTests` (15). `ForemenTests` rewritten for tiers.
- Three-lens adversarial review over the diff; five confirmed findings fixed (confetti drawing under
  the reveal sheet, the confetti pool binding to a hidden screen, an un-localised label, the legacy
  save above, the fade band).
- **Not verified:** anything on screen. The Editor was closed for this work.

---

## 5. Needs the Unity Editor

1. **The camera zoom.** Every field on `OperationCameraBoot` is serialised into `Main.unity`, and the
   scene wins — the C# default alone does nothing. On `CoalController` → `OperationCameraBoot`, set
   `defaultZoomFraction` to **0.14** and save the scene.
2. **The chest icon** (cosmetic). `ForemanRosterUI.chestIcon` → `Art/UI/Ikonlar/ikon_sandik`.
   Unwired it falls back to the card panel, so the shelf works either way.
3. `ForemanConfig.asset` picks up its new fields automatically on load — worth one look in the
   Inspector to confirm the chest block reads 3 / 60 / 10 / 540 / 1 / 28800 / 2.
