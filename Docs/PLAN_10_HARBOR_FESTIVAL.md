# Plan #10 — Harbor Festival

## Product decision

Adapt the reference Summer Carnival as **Harbor Festival**, using its broad interaction pattern but
none of its branding, art, text, layout, prices, or balance. The event is a scheduled module on the
existing `LiveEventService`; it is not a second live-ops system.

The first release is deterministic. Event tasks earn Harbor Tokens, tokens unlock a free milestone
lane and can be spent once in a disclosed reward catalogue. There is no random draw and no direct
paid-currency wagering. A premium lane is represented in tuning and reads an entitlement supplied by
the existing store, but its SKU must remain empty until product/legal approve the product, rewards,
price, and store configuration.

## Vertical slices

1. **Domain and persistence (implemented).** Add the slot map, validation, default balance,
   goal-metric accrual, token accounting, free/premium tier claims, deterministic redemptions, expiry
   conversion, idempotent claim flags, analytics, bootstrap registration, background sync, and EditMode
   tests. Reuse `LiveEventState`; no save-version bump or migration is required.
2. **Authoring and UI (implemented).** Add a `HarborFestivalConfig` authoring surface and a code-built festival
   screen with Tasks, Rewards, and Catalogue tabs. Add the event-hub route, badge, deep links, reward
   reveal, safe-area behavior, Turkish/English localization keys, and accessibility labels. No scene,
   prefab, asset, or meta file will be hand-edited.
3. **Premium entitlement (decision gate).** After SKU/reward/price approval, configure a one-time
   entitlement through `PremiumStoreUI`/`IIAPService`. Restore and interrupted-purchase paths must
   persist entitlement and transaction journal before platform acknowledgement. Premium tiers become
   retroactively claimable from already-earned tokens.
4. **Live tuning and telemetry (schedule and events implemented; dashboard pending).** Author a real schedule row (`kind = 2`, `slots >= 33`), remote-disable
   policy, event/version identifiers, funnels, economy dashboards, and notification copy. Balance from
   Project Kayseri telemetry rather than reference values.

## Runtime model

- Six event tasks consume existing `GoalService` lifetime metrics. Per-metric cursor slots turn those
  totals into event-only deltas; no gameplay call site is added.
- Tokens are derived from completed tasks. Spending is derived from claimed catalogue slots. There is
  no mutable second wallet and therefore no balance that can drift or be duplicated.
- Free and premium milestones use the same token-earned total; catalogue spending never moves the
  milestone track backwards.
- Catalogue rewards are deterministic, fully disclosed, and one-time per event instance.
- The event window is half-open and wall-clock based. Progress stops at `EndUnix`; earned rewards stay
  claimable. Unspent tokens convert to gems once after expiry at the configured integer ratio.
- Config-version changes clear progress/cursors but preserve claimed flags, matching the existing
  live-event idempotency rule.

## Slot map and save behavior

| Slots | Meaning |
|---|---|
| 0–5 | Task progress and task reward claim flags |
| 6–13 | Free milestone claims |
| 14–21 | Premium milestone claims |
| 22–25 | Deterministic catalogue redemption flags |
| 26–31 | Goal lifetime cursors |
| 32 | Expired-token conversion claim |

Existing `SaveData.liveEvents` stores all counters and flags. Older saves already normalize missing
rows and short arrays, so migration is a default-empty event row and `SaveMigration.CurrentVersion`
does not change.

## Failure modes

- Missing/malformed tuning falls back to shipped defaults and warns.
- Missing or undersized schedule rows make the module unavailable instead of partially running.
- Before start, after expiry, insufficient tokens, duplicate taps, missing premium entitlement, and
  invalid indices return false without grants.
- A closed event can still pay earned task/tier rewards and its one expiry conversion, but cannot earn
  progress or buy catalogue items.
- Clock rollback cannot create negative progress; future trusted time can replace `TimeService`
  without changing the module contract.

## Analytics

Emit `harbor_task_claim`, `harbor_tier_claim`, `harbor_redeem`, and
`harbor_expiry_conversion`, keyed by immutable event ID plus slot/index. The lifecycle already emits
generic live-event claims.

## Acceptance criteria

- Existing goal actions accrue only while an eligible Harbor Festival is active.
- Completed tasks increase earned tokens once, without requiring a separate wallet field.
- Catalogue purchases cannot overspend and cannot pay twice.
- Free rewards remain claimable after expiry; premium rewards require entitlement and are retroactive.
- Expiry conversion pays only the unspent balance, exactly once.
- All grants mutate the authoritative services and are saved with their claim flags before UI
  acknowledgement.
- EditMode coverage includes malformed tuning, baseline seeding, accrual, spending, duplicate claims,
  premium gating/restoration behavior, expiry, and config-version safety.

## Explicit approvals still required

- Premium entitlement SKU, real-money price, reward values, and store presentation.
- Whether Harbor Festival tokens expire through conversion or instead carry into a named rerun. The
  implementation defaults to conversion because event IDs are immutable and tokens are event-local.
- Final event dates, eligibility, remote-disable behavior, and player-facing localization/art.
- Any future random draw. It is excluded by default and requires legal/platform review, exact published
  probabilities, pity/guarantee rules, and a prohibition on direct paid-currency wagering unless
  separately approved.
