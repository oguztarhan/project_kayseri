# Plan #13 — Seasonal Industry Pass

## Product decision

Adapt the reference battle-pass pattern as the **Seasonal Industry Pass**. A scheduled live-event
season converts existing goal activity into deterministic points and unlocks a vertical tier track
with free and premium lanes. The pass reuses `LiveEventService`, `GoalService`, `IIAPService`,
`IapTransactionJournal`, reward services, analytics, and the encrypted save.

The implementation does not copy the reference title, art, text, layout, price, or economy values.
Event IDs are immutable season IDs. Premium ownership is keyed by the season's non-consumable SKU,
so a pass never leaks into another season.

## Vertical slices

1. **Domain, persistence, and commerce safety (implemented).** Add point-source rules, fifteen
   deterministic tiers, free/premium claim lanes, event-local goal cursors, idempotent claims,
   season-scoped entitlement, interrupted-purchase recovery, restoration, analytics, and EditMode
   coverage. The existing live-event row stores all progress and claim flags; no save migration is
   required.
2. **Runtime integration (implemented).** The service and config reference are registered by
   `GameBootstrap`, pass progress syncs before pause/quit, the season SKU is in the device store
   catalogue, and the live-events hub shows, badges, retains, and opens the pass.
3. **Player screen (implemented).** Build the two-lane tier track, locked/upcoming states, progress header, localized
   purchase/restore feedback, claim-all affordance, HUD badge, reward reveal, safe-area behavior, and
   accessibility labels. Keep dynamic rows on their own canvas.
4. **Live authoring and release checks (project work implemented).** The tuning asset, `kind = 4`
   September schedule row, and Bootstrap reference were authored through Unity. Store-console product
   approval and device sandbox purchase/restore checks remain release operations outside the project.

## Runtime and save model

- Four point sources read deltas from existing lifetime goal metrics: upgrades, contracts, repairs,
  and foreman levels. Pre-season lifetime totals are captured as cursors and never award points.
- Points are derived from persisted source progress and integer weights; they are never a currency and
  cannot drift from their sources.
- Fifteen ascending tier thresholds and both reward lanes are deterministic configuration. Invalid
  authored tuning falls back to the shipped table.
- `LiveEventState.progress[0..3]` stores earned source deltas and `[4..7]` stores encoded lifetime
  cursors. Free claims use `claimed[0..14]`; premium claims use `claimed[15..29]`.
- A purchased/restored premium SKU is persisted in `purchasedOffers`. Platform transaction IDs are
  journaled in `processedIapTransactions` before the purchase callback returns. Re-delivery is safe.
- Premium ownership is retroactive: any earned premium tier can be claimed after entitlement arrives,
  in any order. Claims remain available after the event closes; progress and new purchases do not.
- Season rollover is isolated by immutable live-event ID. A new ID receives a new event row and fresh
  cursors, while old claimed flags remain available for audit and idempotency.

## Acceptance criteria

- Pre-season totals award zero points; eligible goal deltas accrue only inside `[StartUnix, EndUnix)`.
- Tier thresholds are ascending, both lanes have exactly fifteen rewards, and all rewards are valid.
- Free and premium tiers can be claimed in any order and pay at most once.
- Premium claims are blocked before ownership and become available retroactively after purchase or
  restore, including after an app interruption.
- A restored entitlement is saved locally; repeated restoration and repeated transaction delivery do
  not duplicate rewards or ownership.
- A new season ID starts from zero and cannot inherit progress, claims, or premium access from the old
  season.
- Earned rewards remain claimable after close, but no new points accrue and premium cannot be bought.

## Deferred decisions

- Store product approval and device sandbox validation in Play Console/App Store Connect.
- Bespoke UI art, animation, notification copy, and analytics backend schema.
- Trusted server time. Device UTC remains the current project-wide authority until a server clock is
  approved.

## Verification

- Unity asset refresh and domain reload completed without compilation errors or Plan #13 warnings.
- The tuning asset, September schedule row, and Bootstrap reference were serialized by Unity.
- UI smoke coverage builds all fifteen two-lane tiers without scene references; hub smoke coverage
  seats the active pass card and opens its track.
- The complete `Game.Tests.EditMode` assembly passes: 869 passed, 0 failed, 0 skipped.
- A runtime Game-view capture verifies the scrollable track, localized copy, disabled/locked states,
  free/premium columns, purchase/restore controls, and mobile landscape fit.
