# Liman (Port board) — a board of jobs, not a job

**Date:** 2026-09-03 · **Status:** code complete, compiles clean, 635 EditMode tests green.
**Outstanding:** nothing blocking. `ContractConfig.asset` has been re-serialised (§5).

The port was one job at a time on a rolling cash goal. It is now a board: a ship docks, tables
three jobs at three difficulties, and waits — indefinitely — while the player picks one, swaps one
they do not like, runs it, and claims it. Nothing on the screen expires on its own except the
running clock, because an idle game must never punish someone for looking away.

C# names are unchanged where saves address them — `ContractService`, `contract`, `rerollsUsed` —
and the save version is **still 7**. Nine fields were added to the save without a bump; see §3.

---

## 1. What changed for the player

| | before | after |
|---|---|---|
| What is on offer | one job | **three**, one per difficulty, all visible at once |
| What a job asks | a rolling cash goal | ore **processed**, which upgrades visibly move |
| Don't like a job | nothing to do | **swap one card per visit** — same tier, different length |
| Foreman cards | 2, whatever the job | **1 / 2 / 3 by tier** — the reason to pick hard |
| Card count on screen | never shown | on every card, before the choice is made |
| Falling behind | found out when the ship left | a strip under the card, offering the ×2 that fixes it |
| The claim | cash + gems | plus an optional **×2 cash** for a rewarded ad, twice a day |
| Board gone stale while away | tiny numbers, still on the table | re-cut once the empire outgrows it |
| HUD chip while docked | `READY` | `3 JOBS` — a reward and a choice are different asks |

**The three tiers** (multiples of NORMAL, all Inspector-tunable):

| | rate | window | pay | gems | cards |
|---|---|---|---|---|---|
| KOLAY | ×0.6 | 15 min | ×0.5 | 1 | **1** |
| NORMAL | ×1.0 | 10 min | ×1.0 | 2 | **2** |
| ZOR | ×1.6 | 7 min | ×2.2 | 4 | **3** |

EASY asks for less than passive play already delivers over a long window; HARD asks for more than
it can over a short one. Cash separates them too, but cash is the resource the game inflates — by
the third island the gap between the easy pay and the hard pay is a rounding error against passive
income, and the choice collapses. Cards do not inflate and cannot be bought, so they are what keeps
"which job?" a real question for the whole game.

**Sizing.** Targets are not authored. Each job is `measured throughput × window × tier rate ×
streak`, so one contract is roughly one focused window whether the islands smelt 200 units a minute
or 200 trillion. `floorUnits` (50) and `rewardCash` (500) only carry the opening minutes, before the
meter has anything in it.

**Streak.** Each claim multiplies the next board by 1.08, capped at 3.0. A miss resets the streak
counter to 0 and divides that multiplier back one step, floored at 1.0 — the ship leaving is
punishment enough. The windows here are 7–15 minutes, so a streak compounds far more slowly in
wall-clock terms than the old 90-second cash goal it replaced, hence the gentler step. Every 5
claims adds +1 card to every tier.

**Swap.** One per ship visit. It keeps the tier — rate, gems, pay-per-minute — and moves only the
**window**, to ×0.7, ×0.85, ×1.15 or ×1.3 of the authored length, with units and cash following it.
None of those is 1.0: a swap that could hand back the card it replaced reads as a button that did
nothing. The budget refills when a **new ship docks** and at no other time.

**Rewarded ×2.** On the claim screen, twice a day, its own `kontrat` slot. **Cash only** — gems and
foreman cards are not multiplied, because cards are the one reward that cannot be bought and an ad
that doubled them would make watching ads the fastest way to build the roster. The store's
`freeRewardBonusCharges` perk deliberately does **not** apply to this slot: contract cash scales with
the empire, so a bought charge here would be worth more the deeper the player is — an unbounded
purchase, which is not what that perk was priced as.

---

## 2. Code map

| File | What it holds |
|---|---|
| `Assets/Scripts/Core/ContractBoard.cs` | **new** — all of the board's arithmetic, none of its state. `Cut`, `IsStale`, `WindowScale`, `RoundNice`. No clock, no wallet, no RNG. |
| `Assets/Scripts/Systems/ContractService.cs` | the visit state machine, the frozen meter, `Swap`, `Claim(multiplier)`, `Pace`, durable `Commit`. |
| `Assets/Scripts/Data/ContractConfig.cs` | every number above, with the reasoning in the tooltips. |
| `Assets/Scripts/UI/ContractUI.cs` | three code-built offer cards, the SWAP pill, the ×2 CASH pill, the pace strip. |
| `Assets/Scripts/UI/HudUI.cs` | the port chip — `3 JOBS` / `READY` / a clock. |
| `Assets/Scripts/Systems/NotificationService.cs` | "{0} new offers are waiting", fed from `TierCount`. |
| `Assets/Scripts/Systems/Save/SaveData.cs` | `ContractSaveData` + `ContractOfferSave`, nine new fields. |

**States.** `Away → Arriving → Offering → Active → Reward → Departing → Away`. `Away`, `Arriving`
and `Departing` run on the wall clock and are reconciled on return; `Active` runs on **play time**,
so a deadline cannot expire while the app is closed. Offline earnings credit no contract units, and
that pairing is what makes an away window neither punish the player nor advance the job for free.

**Analytics.** `contract_accept`, `contract_claim`, `contract_claim_doubled`, `contract_missed`,
`contract_swap` — all carrying `tier`. The loop was entirely uninstrumented before this.

**Localisation.** Added `kontrat.degistir`, `kontrat.odul_iki_kat`, `kontrat.is_sayisi`,
`kontrat.geride`, `ustabasi.kart_tekil`. `bildirim.kontrat_geldi` was reworded from "Three new
offers" to `{0}` form in all 12 columns. Polish and Russian inflect after a numeral: the forms used
are correct for 3 and 4 — the only range `TierCount` will realistically take — but would read wrong
at 1 or 5. Worth a native check before launch.

---

## 3. The rules that are easy to get wrong

Each of these is pinned by a test, and each test has been mutation-checked: the fix removed, the
named test confirmed to fail, the fix restored and the suite re-run green. One mutation — a claim
recomputing the foreman-card count instead of paying what the card promised — **survived** when it
was first tried, because every tier paid the same number then. It began failing the moment tiers
paid 1 / 2 / 3, which is the case it was always guarding.

**The board freezes the meter it was cut against.** Read once in `RollOffers`, persisted, and used
by every slot filled afterwards. The live meter keeps moving while the ship waits, and three cards
priced at three different instants are not a choice.

**Both meters are normalised for a running boost.** On the island being played a boost is spent on
*time*, not on price — see `MarketService.IslandTimeScale`. The whole chain runs at ×2, so during a
boost the cash meter reads double **and** the furnace reports double. Normalising only one tilts the
board rather than levelling it. This was originally implemented for cash alone, on the mistaken
belief that a boost left throughput untouched; the resulting board asked twice the ore for the same
money until it was corrected.

**A board that has been outgrown is re-cut, on a ratio, not an age.** `IsStale` fires when live
throughput reaches `boardRefreshFactor` × the frozen reading. A board goes stale because the player
got stronger, not because time passed — and measuring growth puts no device clock in the decision,
so there is nothing to gain by moving one. It never fires on a job already signed, and it never
refills the swap budget: that would make growing the empire the cheapest way to buy re-rolls.

**Every card carries an id, and a tap is matched against it.** The slot alone is not enough — a
board that can replace one card leaves the player's finger over a job that is no longer there, and a
tier-only accept would sign whatever took its place. A mismatched id is refused, and the refusal
costs nothing.

**A claim reaches the disk before the screen says it paid.** `Commit` runs at the end of `Accept`
and `Claim` and nowhere else — never from `Tick`, which would be an AES pass and a whole-file write
per frame. The paid cash, the paid gems and the state flip that stops it being claimable again all
live in one `SaveData`, so one write means the file holds every part of the claim or none. The
×2 multiplier is applied *inside* the claim for the same reason. The spent swap and the spent ad
charge are both written the same way — "kill the app to get it back" is the kind of trick players
share.

**A swap cannot raise pay-per-minute.** Only the window moves; units and cash follow it, *and so
does the cash floor*. That last clause was missed at first and found on a real device: a floor-priced
board kept the whole $500 for 15% less time.

**Nine save fields were added without bumping the version.** `SaveMigration.NeedsReset` is an
equality test, so a bump deletes every live save on every device. `Normalise()` re-stamps a board
restored without identity instead — the slot *is* the tier, ids come from the sequence, card counts
follow from the restored streak. A job signed before tiers had their own card counts falls back to
NORMAL, whose count *is* the old flat `cardsPerContract`, so it pays exactly what it was signed for.

---

## 4. Verified

- All assemblies compile clean. **0 errors, 0 new warnings** in the seven files touched. (The 28
  pre-existing CS0618/CS0414/CS0108 warnings elsewhere in `Game.UI` are untouched and unrelated.)
- **635 EditMode tests, 635 passed, 0 failed**, run through the Unity Test Runner. `ContractPersistenceTests`
  grew from 3 to 50 cases.
- **Every guarantee in §3 mutation-checked** — twenty-two faults injected one at a time, each caught
  by the test named for it (the one exception is in §3): boost normalisation for cash and for units
  separately, staleness, the frozen meter, re-cutting a signed job, the swap budget on refresh and
  across a restart, a swap raising pay-per-minute, the cash floor, a refused swap costing the budget,
  duplicate payout, the ad doubling gems and cards, the multiplier being dropped, pace ignoring the
  boost, pace warning from a cold meter, `OfferCount`, flat card counts, and the claim recomputing
  cards instead of paying the promise.
- **On screen**, in play mode against a real save: the three cards and their `+1 / +2 / +3 card(s)`
  row; the SWAP pill and a real swap (`10:00 / 81 coal → 8:30 / 42 coal`, all pills then gone); the
  ×2 CASH pill and a real press (`$500` reward → wallet `+1000` exactly, one daily charge spent,
  ship departed, not re-claimable); the pace strip appearing and then hiding once a boost ran; the
  HUD chip reading `3 JOBS`. Longest card words in German, Spanish and Russian checked for clipping.
- Every new localisation key confirmed to resolve at runtime — `metinler.txt` is positional and
  silently drops a short row, so a missing one would ship as its own key on the button.
- The save file was backed up before every play-mode session and restored byte-for-byte after,
  verified by SHA-256.

---

## 5. Needs the Unity Editor

1. ~~`ContractConfig.asset` is stale~~ — **done.** Re-serialised via `AssetDatabase.ForceReserializeAssets`;
   it now carries all 23 fields, including `easyCards` (1), `hardCards` (3), `boardRefreshFactor` (2),
   `swapsPerVisit` (1) and `paceWarnBelow` (0.95). The two dead fields it still had from the old cash
   contract, `targetUnits` and `timeLimitSeconds`, are gone. Every value written matches the C#
   default it was already running on, so nothing shifted — `rewardCash` (500) and `rewardGems` (2),
   the only two the file actually held, are unchanged.
2. **Nothing else is blocking.** Every control this work added is built in code: the authored card
   has no free slot for them, and the three offer cards already set the precedent for this screen
   drawing its own. `UI_Kontrat.prefab`'s empty `cardRoot` slot should stay empty — `ContractUI.Body()`
   depends on it.
3. **Optional, cosmetic.** The pace strip and the ×2 pill both borrow the amber accept-pill art from
   `acceptButtons[1]`. If a dedicated ad-button sprite is ever authored, those are the two places to
   point at it.
