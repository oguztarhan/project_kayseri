# Plan #11 — Production Sprint

## Product decision

Adapt the reference sprint pattern as **Production Sprint**: a short event that awards points for
explicitly configured Project Kayseri actions. It reuses the existing `LiveEventService`, goal
metrics, reward services, analytics, and save row. It does not copy the source event's name, art,
text, layout, prices, or balance.

The first release is personal and fully offline. Ranking is an optional adapter and remains disabled
unless an approved service reports the exact immutable event/season ID. Gift packs and ranking
rewards are excluded from this release.

## Vertical slices

1. **Domain and persistence (implemented).** Add four capped scoring rules, five personal milestones,
   event-local goal cursors, derived score, idempotent claims, default balance, analytics, bootstrap
   registration, background sync, an Inspector authoring surface, and EditMode tests.
2. **Player screen (implemented).** Add a code-built Tasks/Milestones screen, event-hub route, HUD badge, reward
   reveal, accessibility labels, safe-area behavior, and Turkish/English localization. Ranking and
   Packs tabs should be hidden, not shown disabled, in the personal build.
3. **Live schedule and tuning (implemented).** The first `kind = 3` event runs from
   **2026-09-12 00:00 UTC** through **2026-09-15 00:00 UTC**, requires Chapter 1 completion, and uses
   five increasingly engaged personal targets. The schedule asset is authored through Unity.
4. **Ranking decision gate.** Only after backend, anti-cheat, privacy, tie, settlement, and reward
   approval, connect an `ILeaderboardService` whose `CurrentSeasonId` equals the live-event ID.

## Runtime and save model

- Four rules map existing lifetime goal metrics to points per action. Each rule is capped; total event
  score is derived from capped progress and is never a mutable currency.
- The shipped rules score upgrades, contracts, repairs, and foreman levels. Bars sold are excluded
  because production scales sharply by ore tier; islands are excluded because they are not repeatable
  within a short sprint.
- Cursor slots turn lifetime metrics into deltas earned during the event. The constructor seeds active
  baselines, so earlier lifetime progress cannot be imported into a new sprint.
- All writes pass through `LiveEventService.Record`, so the event's half-open `[start, end)` window and
  island eligibility remain authoritative.
- Claims are marked before rewards are granted and saved before UI acknowledgement. Closed events can
  still pay earned personal milestones forever; this is the explicit no-expiry claim deadline.
- The five targets are 40, 100, 190, 300, and 400 points. Rewards are respectively: 10 gems plus
  5 minutes of current cash income; 20 gems plus 15 minutes of current cash income; 30 gems plus one
  Foreman card; 50 gems plus two Foreman cards; and 100 gems plus three Foreman cards.
- Sprint score has no paid source: there are no purchasable points, paid multipliers, or gift packs.
- No save-version bump is needed. Older saves normalize a missing event row to empty progress.

| Slots | Meaning |
|---|---|
| 0–3 | Capped action progress for four scoring rules |
| 4–8 | Personal milestone claim flags |
| 9–14 | Lifetime cursors for the six existing goal metrics |

## Timing, offline, and integrity rules

- Event IDs are immutable season IDs. Reusing an ID for different content is forbidden; balance edits
  use `ConfigVersion` and clear unclaimed progress while preserving paid claim flags.
- Goal actions performed without network access count normally. Work performed while the app is not
  simulating counts only if the underlying authoritative goal metric records it.
- Late score submissions are not sent after `EndUnix`; personal progress is already local and frozen.
- The current `TimeService` uses device UTC. Production Sprint keeps a session high-water mark so
  rolling the clock backward during one run cannot reopen it, but restart-proof clock tamper remains
  an accepted limitation until trusted server time exists.
- If ranking is later enabled, equal scores follow the existing leaderboard rule: higher score first,
  earliest achievement time next, then stable entrant ID. The sprint grants no ranking reward itself.

## Acceptance criteria

- Pre-event lifetime totals never produce sprint score.
- Eligible actions accrue only inside the active window and stop at their configured caps.
- Score is deterministic, derived, and isolated from permanent economy balance.
- Every earned personal milestone remains claimable after close and pays at most once.
- Missing or malformed tuning falls back to shipped defaults; undersized schedule rows stay unavailable.
- Ranking remains unavailable with the shipping stub or a mismatched season ID.
- EditMode coverage verifies tuning, baseline seeding, weighting, caps, post-close claims, idempotency,
  disabled ranking, malformed tuning fallback, and schedule slot safety.

## Deferred decisions

- Any ranking backend, public leaderboard presentation, ranking reward brackets, or gift pack offer.
- A trusted-time source and server-side score validation before competitive rewards are enabled.
- Bespoke event art and notification copy; neither blocks the personal milestone release.

## Verification

- Unity 6.4.9f1 asset refresh and domain reload completed; all new `.meta` files were generated by Unity.
- Unity console contains no compilation errors and no Plan #11 warnings.
- Focused Sprint, event lifecycle, schedule, eligibility, and UI run: 37 passed, 0 failed, 0 skipped.
- Full `Game.Tests.EditMode` run: 860 passed, 0 failed, 0 skipped.
- The runtime-built UI smoke test confirms both tabs and all milestone claim rows build without scene
  or prefab references.
