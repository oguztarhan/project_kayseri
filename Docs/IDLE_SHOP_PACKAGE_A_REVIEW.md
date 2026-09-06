# Package A review — Claude

Date: 2026-09-06
Reviewer: Claude, package B owner.
Scope: static review of the eight new files plus `IDLE_SHOP_PACKAGE_A_HANDOFF.md`. No files changed.
Codex's own run reported 130 EditMode tests passing (job `4b59ab9323a342e0848c8616413e4773`); I did not
re-run them, because Unity was in use and a domain reload mid-session risks a stuck test job.

**Verdict: accepted. One concrete issue, one note. Package B starts against these contracts as written.**

## Verified against the existing code

Every binding these contracts depend on was checked, not assumed:

- `Chapters.Of` returns `-1` for an unknown island key, so `StageService.Validate`'s `< 0` guard is real —
  a bad `islandId` cannot silently collapse into chapter 0 (coal).
- `ChapterService.Owned(int)`, `Satisfied(int,int)`, `Claimed(int,int)` exist with the called signatures.
- Assembly graph resolves: `Game.Systems` → `Game.Core` + `Game.Data`; `Game.Data` → `Game.Core`;
  `Game.Core` → none, and `IdleCrewRules` / `IdleTransportRules` touch only `MarketFlow` and
  `IslandEconomy`, both in Core. No existing boundary is crossed or widened.
- Every legacy `MarketYard` field has a destination in `IdleMarketMigration.Convert`. Nothing is dropped.
- All eight `IslandEconomy` transport properties used by `IdleTransportRules` exist, and no saved slot
  index is reordered — `Mine=0, Train=1, Storage=2, OreTrucks=3, Smelter=4, CargoTrucks=5, Market=6,
  Power=7` are untouched.

All four of my revision-2 notes are resolved in code, not only in prose: stages are chapter beats with
no second ledger; `hireCollect` → `dispatchLevel` keeps the min-of-three curve, pinned across all 216
combinations by test; the per-product row migrates idempotently and refuses to downgrade an unknown
schema; and no theme/progression classes were built ahead of content that needs them.

## Issue 1 — an unreachable level is documented as the design ceiling

`MarketPrices.MaxCarryLevel = 8`. `SettingsUI:650` clamps its grant to that same constant. So the
reachable ceiling of `1 + 0.10 * level` is **1.8x**, not 2.0x. Level 10 cannot occur in a real save.

It is stated as 2x in three places:

- `IdleCrewRules.cs` — comment "level 10 doubles loads".
- `IdleShopContractTests.GlobalCarryInvestmentHasExplicitNpcBenefit` — `[TestCase(10, 2d)]`, green only
  because the formula is unbounded.
- `IDLE_SHOP_PACKAGE_A_HANDOFF.md`, conversion table — "level 0 = 1×, 5 = 1.5×, 10 = 2×".

Behaviour today is correct; nothing is broken. It matters because these are exactly the lines package E
reads when rebalancing porter throughput, and all three describe a cap the shop cannot reach. A designer
budgeting against 2.0x will overshoot by 11%.

Requested fix (codex's call, it is A's file): clamp the input to `MarketPrices.MaxCarryLevel` and change
the case to `(8, 1.8d)`, so rule, test and table all describe a reachable state. If the intent is instead
to raise `MaxCarryLevel` to 10 as part of this redesign, that is a separate balance decision that changes
what existing level-8 owners are worth — say so explicitly rather than leaving the formula open-ended.

I have not edited it; A's files are codex's under the ownership split.

## Note — expected consequence, not a regression

With manual play removed, a yard at `dispatchLevel` 0 is hard-capped at the 0.15 trickle with no bypass.
Previously an active player could beat that by hand through `SellByHand` and the cash floor. This is the
intended effect of revision 2 and I agree with it, but it makes the early game measurably slower for a
player who was actively grinding. Recording it so package E treats it as expected rather than hunting it
as a bug.

## Confirmed for package B

Both assumptions I flagged are confirmed by the handoff:

- `IdleMarketYard` is not wired into `SaveData`; wiring it and switching every scalar-stock consumer in
  one transaction is B's job, with its own additive schema marker and **no bump to
  `SaveMigration.CurrentVersion` (7)**, since `NeedsReset` treats a version change as a reset.
- `CoalOperation.cs:3370` stays the single seam where the island hands goods to the sales ledger. It
  becomes `double Deliver(string islandKey, string productId, double offered)` returning accepted units,
  with all existing void callers switched together and unaccepted stock returned to source.

Housekeeping: an earlier draft of this review was written to `Docs/CLAUDE_REVIEW_PACKAGE_A.md` before the
agreed filename was known. Its content is superseded by this file and it can be deleted.
