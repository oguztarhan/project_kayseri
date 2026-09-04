# Plan 07 — Daily/Weekly Goals and Rewards

## Product decisions

- Calendar boundaries use UTC: daily at 00:00 and weekly on Monday at 00:00.
- Missing a login-reward day resets the streak. There is no recovery/spend mechanic in this slice.
- Weekly progress is derived from deltas of the existing lifetime metrics; gameplay systems do not
  report into a second counter.
- Weekly milestones have immutable string IDs so presentation order can change without relabelling
  saved claims.
- Claim-all takes every ready daily, weekly milestone, and achievement reward in one operation.
- Services own validation, grants, and persistence. UI animation starts only after a successful claim
  has been saved and the service raises its change event.

## Delivery slices

1. **Foundation (complete):** weekly definitions, UTC rollover, migration defaults, idempotent
   claims, claim-all, persistence wiring, and edit-mode coverage.
2. **Unified screen (complete):** refactor the existing Goals UI into Daily, Weekly, and Achievements tabs; add
   distinct locked, ready, and claimed states plus one claim-all action.
3. **Navigation and presentation (complete):** add goal deep-link targets to notification navigation and a
   presentation-only reward reveal driven by successful claim results.
4. **Hardening (complete):** verify login streak behavior, UTC/timezone changes, missed days, repeated taps,
   offline weekly rollover, partial weekly progress, localization, and Unity console cleanliness.

## Acceptance criteria

- A metric event advances daily, weekly, and lifetime progress without duplicate instrumentation.
- Repeating any claim cannot grant currency twice, including after reload.
- A pre-Plan-07 save starts the current week at zero progress and keeps lifetime achievements.
- Weekly rollover clears partial weekly progress and weekly claims without touching lifetime totals.
- UI never grants rewards and never shows a reveal before the corresponding save completes.
