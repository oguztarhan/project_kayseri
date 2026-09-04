# Sıralama ve turnuvalar (Leaderboards) — the decision, and the seam it hangs on

**Date:** 2026-09-04 · **Status:** **decisions answered and the league is built** — option A, three-day
seasons. See §17 for what shipped. The decision record below is kept as written.
**Verified:** all six assemblies compile clean; 16 new EditMode tests green plus the 44 that were
already here, and the full suite passes outside the Editor — see §17.
**Was blocked on:** seven product-owner decisions, §14. D1, D4, D5 and D7 are answered (§17); D3 and
D6 took the defaults this document proposed.

This is integration-pack system **12**, and it is the one system in the pack whose own brief says
*plan only*. It is also the third and last of the three gaps `FIVE_LAYERS.md` §1 named — "Chapters,
events, leaderboards." Chapters shipped. Events shipped as `LiveEvents` + `LiveEventService`. This is
the third, and it is the one that cannot ship the same way, because the other two are arithmetic over
the player's own save and a leaderboard is a claim about **other people**.

---

## 1. What the reference actually shows

Two frames, and it is worth being precise about which half of them is evidence.

| | Frame 0027 — a weekly ladder | Frame 0050 — an event ladder |
|---|---|---|
| Window | one countdown, ~1 gün 9 saat left | 4 gün 4 saat, plus "tur 1/5" |
| Cohort | a numbered list; the player sits 8th | a list, the player is **"Listede Değil"** |
| Score | one currency-like number per row | "kümülatif puanlar", plus a per-row power stat |
| Reward | a chest per row, three graded podium chests | a reward-preview strip, a shop and a rewards button |
| Action | none — it is a standings screen | a **Challenge 4/4** button and a ticket purchase |

**Observed:** timed windows, a ranked cohort of ~10 visible rows, a highlighted self row, per-rank
reward previews, graded podium tiers, and an event ladder that reuses the same list structure.

**Inferred, and not evidenced by anything in the frames:** that the other rows are real people. Every
name in 0027 is a first-name-plus-surname handle except the player's own `player11371029`; every row
in 0050 carries a level and a power number that no live ranking would need to transmit. It is at
least as consistent with a generated cohort as with a real one. **We must not copy a mechanic whose
central claim we cannot see.** What we copy is the shape of the screen; what it is filled with is
decision D4.

---

## 2. Why this cannot be built the way the rest of the game is

Everything else in this project is settled on the device: the wallet, the contract board, the
festival. That works because a save only ever lies to the player who edited it. A leaderboard pays
one player **out of** a pool that other players are in, which makes a client-authored score an
instruction to take rewards from other people. On an Android build with no server, editing that score
is a rooted phone and a memory editor — not an exploit, a hobby.

So the rule for this system, and it is not negotiable by tuning:

> **A score that a client authored may never determine a reward.** Either a server validates it, or
> the ladder pays nothing, or the ladder is openly a solo exercise against generated opponents.

Those three sentences are the three options in §3.

---

## 3. The three options

| | **A. Local league** (generated cohort) | **B. Platform leaderboards** (Play Games / Game Center) | **C. Managed backend** (UGS or equivalent) |
|---|---|---|---|
| Authentication | none — nothing leaves the device | platform account, already on the device | anonymous sign-in, an account this game now owns |
| Anti-cheat | **not applicable** — nothing is contested | **none.** The client submits, the platform stores | real, **but only if scores are written server-side** (Cloud Code or equivalent); a client-submitted score to a hosted table is option B with a bill |
| Ongoing cost | zero | zero | usage-metered — free tier, then per-request. Verify current pricing before committing |
| Ops burden | zero | small: two dashboards, two ids | real: an environment, a deploy path, a scoring function, an incident owner |
| Offline handling | perfect — there is no online | scores queue in the platform SDK | our own outbox (built — §7) |
| iOS/Android parity | identical | **two different SDKs**; Play Games has not supported iOS since 2017, so iOS needs Game Center separately | identical |
| Server-validated rewards | n/a | **impossible** | possible, and is the only reason to pick it |
| New packages | **none** | Play Games plugin + a Game Center path | UGS packages (Core, Authentication, Leaderboards, Cloud Code) |
| GDPR / store surface | nothing new | platform's own; still a data-safety declaration change | a processor, a DPA, a deletion path, privacy-policy and data-safety updates — §10 |
| Time to a shippable slice | days | ~a week, and it buys a vanity list only | weeks, most of it not Unity work |

**Option B deserves its own sentence, because it is the tempting one.** Play Games and Game Center
leaderboards are cheap to add and they are *display surfaces*: the client tells the platform its
score and the platform believes it. They are fine for a vanity list with no payout and no entry fee.
They cannot back a rewarded tournament, they do not solve the parity problem, and they would mean two
integrations plus an unavoidable dependency on the very account systems §10 is about. They are also
the option that most looks like it has solved the problem while having solved none of it.

**Recommendation: A now, behind the seam; C when a rewarded ladder is actually wanted; B never as a
ranking source.** A is honest, costs nothing, adds no package, needs no account and no privacy
change, and it lets the screen be designed and the reward brackets tuned while the real decision is
still open. C is the only option that can ever pay a contested reward, and it is not a Unity task —
it is picking a processor, signing a DPA, writing a scoring function and owning an on-call rota.
Deciding C **later** costs nothing extra precisely because of the seam below.

---

## 4. What was built (and what deliberately was not)

Four files. No package, no network call, no `SaveData` field, no migration, no scene or prefab touched.

| File | What it is |
|---|---|
| `Assets/Scripts/Core/Leaderboards.cs` | the arithmetic: seasons, the total order, ties, brackets, bands, the merge rule |
| `Assets/Scripts/Systems/ILeaderboardService.cs` | the seam, plus `StubLeaderboardService` — the null object a build registers today |
| `Assets/Scripts/Systems/LocalLeaderboardService.cs` | the offline double: a whole ladder with no server |
| `Assets/Scripts/Tests/EditMode/LeaderboardsTests.cs` | 44 tests |

**Not built, on purpose:** any UI, any `GameBootstrap` registration, any save field, any reward grant.
A screen would need a prefab (which is mine to edit only in the Editor, per `CLAUDE.md`), and a save
schema for an unapproved feature is a schema that has to be supported forever.

The seam is **callback-shaped** (`SubmitScore(score, onDone)`), matching `IIAPService.Purchase`. Every
real implementation of this is a round trip; an interface whose methods return values can only be
implemented by one that is not, and getting that wrong is a rewrite of every call site on the exact
day a backend is chosen — which is the day the seam exists to survive.

**The seam grants nothing.** Not one method touches a wallet, a save or an inbox. A settlement is an
*answer* — "you finished 4th, that is bracket 3" — and the granting belongs to whichever service owns
the reward, behind its own claim flag, exactly as `LiveEventService.MarkClaimed` gates an event
payout. A leaderboard that pays out is a leaderboard that pays out twice the first time a response is
delivered twice.

---

## 5. Seasons

A season is `[start, start + cadence)` — **half-open**, the same rule `LiveEvents` keeps, so the
second that closes one season is the first second of the next and no score can land in two of them.

- Anchor: **Monday 2026-01-05 00:00 UTC** (`Leaderboards.SeasonEpochUnix`). UTC, because a
  local-time schedule opens the same season eleven hours apart for two players.
- Cadence: **weekly**, 604 800s — a parameter everywhere, so a daily ladder needs no new arithmetic.
- Id: `"lig-" + index`, built with **InvariantCulture**. Not decoration: this team develops on Turkish
  Windows and has already shipped one culture-formatting bug (`ForemanRosterUI`). An id that reads
  `lig-1.234` on one machine and `lig-1,234` on another is two different rows, and the player who
  crosses that boundary loses a season's reward. `SeasonIdIsTheSameStringInEveryCulture` pins it.
- Ids are **immutable and never reused**. A re-themed season is a new index, not an edited one.
- Before the epoch, everything is season 0 — a device whose clock says 1974 gets a real id rather than
  a negative index nothing has heard of.

**Event ladders (frame 0050) do not get their own scheduler.** A tournament inside a live event takes
its *window* from the existing `LiveEvents.Definition` and its *ranking* from this file, with the
season id derived from the event id. That is the reuse the pack asks for and it is why `Leaderboards`
takes epoch and cadence as arguments instead of reading its own constants.

---

## 6. Identity

**Not decided — D2.** What is decided is the shape: the ranker sees an `EntrantId` that is opaque,
stable, comparable and carries no personal data. It is not a display name, and display names are not
comparable — the tie-break reads the id, and nothing else may.

Under option A the id is the constant `"oyuncu"` and never leaves the device. Under option C it is an
account-scoped identifier issued by the provider. Under no option is it the player's e-mail, their
advertising id, or anything derived from either.

**And it is not the support id.** `Game.Core.PlayerId` / `Game.Systems.PlayerIdentity` mint a short
label so a mail from a stranger can be matched to the save that sent it. Its own file says it is not
an account, and that is exactly why it cannot be an entrant id: it is minted on the device, so it is
forgeable at will, and it changes on a reinstall — two properties that are harmless for a support
desk and fatal for a contested ranking. Reusing it would look like it had solved D2 while having
solved nothing.

---

## 7. Submission, idempotency and failure recovery

**The one decision everything else rests on: a submission carries the season's best, absolute, never a
delta.** The record keeps `max(held, sent)`. That makes the write idempotent *by construction* — a
retry after a lost acknowledgement, a duplicate delivery, a resume from a stale save all re-send a
number that is already there, and `max(n, n) = n`. A ladder built on "+37 points" needs a
de-duplication journal for every packet and pays twice the first time one slips through.

The rest follows from it:

- **The outbox is one slot per season, not a queue.** A newer submission *supersedes* the pending one
  (`Leaderboards.Supersedes`). A player earning points for two hours on a plane comes back owing one
  number, not four hundred — and a queue that grows while offline is a queue that outlives the season
  it belongs to.
- **An offline submission is a promise, not a failure.** `LeaderboardStatus.Offline` + `Pending`; the
  player's own row reads the outbox's best, so the number they can see never goes backwards.
- **A stranded score is dropped, never re-aimed.** If the season closes while the outbox is full, the
  flush discards it. Carrying it into the running season would hand a player who was offline over a
  Sunday night a head start in a season they had not played. `AScoreStrandedInAClosedSeasonIsDropped…`
- **An acknowledgement is merged, not adopted.** `AdoptAck = max(local, remote)`: adopting the remote
  number blindly drops a score earned while the ack was in flight; ignoring it hides a score submitted
  from a second device.
- **The achievement stamp only moves when the score does.** Resubmitting the same number must not push
  the player down the tie-break for having tapped refresh.
- **Malformed submissions die at the client**, not as a server error nobody reads.
- **Settlement is a separate call from the board**, because it must work long after a season ended and
  while the next one runs. A player who was away a fortnight has two settlements owed, and nothing in
  this game expires for being looked at late (`FIVE_LAYERS.md` R3).
- **A season that has not finished has nothing to settle** — refused, rather than answered with a
  provisional rank, which is the thing a player screenshots and then argues about.

**Deferred to the backend slice:** the outbox and the per-season best are in memory today. When a
backend is approved they need `SaveData` fields — sketch in §11 — because a submission that survives
an app kill is the whole point of an outbox, and this double is not a record of anything.

---

## 8. Ties, cohorts and brackets

**The order is total, in three levels: score descending, then earliest achievement, then entrant id.**
Three, because any two distinct entrants must have a definite winner on every device and in every
rebuild. A comparator that stops at the score leaves ties to the sort's internal order — the same
board renders two ways on two phones and the screenshot arguments start.

Earliest-first rather than splitting the reward: whoever reached the number first held it longest, a
server can settle it from its own write log, and it needs no special payout arithmetic for the
four-way tie at zero points that every season's tail contains.

**Cohort: 30, fixed, and a constant rather than a config.** A cohort that can be resized is one that
can be resized *mid-season*, and moving somebody from a board of 30 into a board of 50 halfway
through invalidates every rank they had earned. Thirty is a real contest that still fits a phone in a
few flicks.

**Matching is by progression band** — two islands per band, four bands, the same eligibility axis
`LiveEvents.Eligible` uses. Output inflates roughly ×3.2 per ore tier, so an unbanded board puts a
diamond island and a coal one on the same list and the coal player never sees a rank above 30th.
Banding is a *matching* input, never a score adjustment: normalising scores across islands would mean
the number on the board is not the number the player earned.

**Brackets, by last rank covered: 1 · 2 · 3 · 4–10 · 11–20 · 21–30.** A podium worth chasing and a
tail that still pays, so 27th place is a reason to come back rather than a reason to stop. What each
bracket *pays* is not in this layer at all — the same split `LiveEvents` keeps from its modules.

---

## 9. The reward inbox

The ladder never grants. A settled season produces an **inbox row keyed by the season id**, and the
grant is gated on a claim flag exactly as `ChapterService.Claim` and `IapTransactionJournal` gate
theirs: **flag written and saved first, reward second.** The season id is the idempotency key, so a
settlement delivered twice — by a retry, a redelivery, or two devices — pays once.

The inbox is *not* a new service. It is a list on `SaveData` plus a claim method on whichever service
already owns the currency, for the same reason `LiveEventService` has no module registry: there are
not three use cases yet, and `CLAUDE.md` says abstract at three.

**Nothing expires.** A season's reward waits indefinitely, like an unclaimed contract at the port. An
inbox that swept itself would be the first thing in this game that punished absence.

---

## 10. Privacy, stores and the thing that is easy to miss

Option A changes nothing: no data leaves the device, no policy text changes, no data-safety
declaration changes.

Option C changes a lot, and most of it is not code. Storing a per-player identifier and a score is
processing personal data even when the identifier is pseudonymous — so: a processor agreement with
whoever hosts it, a privacy-policy update in **both** language sections (the policy already carries
`#tr`/`#en` anchors), updated Google Play Data Safety and App Store privacy answers, a deletion path,
and a retention rule for closed seasons. **Verify before committing:** if the ladder involves anything
Apple reads as account creation, App Store review requires in-app account *deletion* too — that is a
feature, not a checkbox, and it lands in the same slice.

Two more that are easy to miss:

- **A paid entry fee changes the legal character of the thing.** Frame 0050 shows a ticket purchase
  next to a ranked payout. A contest with a paid entry and a prize is regulated differently in several
  of the eleven territories this build is localised for. If D5 is "yes", it needs a look from someone
  who is not an engineer.
- **A generated cohort must be labelled.** Which is D4, and the next paragraph.

---

## 11. If the opponents are generated, the game says so

`LeaderboardBoard.Synthetic` rides on every board rather than being something a screen must remember
to ask about. A generated cohort presented as real people is a plain deception of the player; with an
entry fee or a reward attached it stops being a design opinion and becomes a consumer-protection
problem. The double sets it true, always, and `TheDoubleNeverPretendsToBeReal` pins it.

The double also **does not chase the player**: opponent scores are drawn from the season id and the
band and from nothing else. A ladder that quietly rescales so the player always finishes eighth is a
slot machine with a rank painted on it — and it would make every ranking test meaningless besides.
`TheCohortDoesNotChaseThePlayer` pins that too.

---

## 12. The save, when it lands

Not written. This is the sketch the backend slice implements, appended without a version bump on the
precedent `ForemanService`, `VoyageService` and `LiveEventService` set — a bump wipes every player's
run, and this only appends:

```
public LeaderboardSaveData ladder = new LeaderboardSaveData();

class LeaderboardSaveData
    string  pendingSeasonId;      // the outbox: one slot, never a queue (§7)
    long    pendingScore;
    long    pendingAchievedUnix;
    long    pendingSequence;
    string  currentSeasonId;      // the season bestScore belongs to
    long    bestScore;
    long    bestAchievedUnix;
    List<string> settledSeasons;  // idempotency keys for granted rewards (§9)
    List<LeaderboardInboxRow> inbox;   // seasonId + rewardTier + claimed
```

Defaults must read as "a player who has never entered a ladder", which is every save that exists
today. `SaveMigrationTests` gets a case for a save with a null `ladder`.

---

## 13. Analytics, localisation, UI

**Analytics** (through the existing `IAnalytics`, no new facade):
`ladder_open` · `ladder_submit` (season, score, status) · `ladder_flush` (status, seconds pending) ·
`ladder_settle` (season, rank, tier) · `ladder_claim` (season, tier). Status travels as the enum's
*number*, which is why `LeaderboardStatus` is append-only.

**Localisation keys** — Turkish first, then the other ten, through `LocalizationService`:
`lig_baslik` · `lig_sure` · `lig_siralamam` · `lig_listede_degil` · `lig_odul_onizleme` ·
`lig_gonderiliyor` · `lig_cevrimdisi` · `lig_sezon_bitti` · `lig_kullanilamiyor` · `lig_temsili`
(the synthetic-cohort label, §11) · `lig_odul_bekliyor`.

**UI wiring**, when D1 and D4 are answered: a panel on its own canvas (dynamic rows must not share a
canvas with static ones), a pooled row prefab, `RequestBoard` on open and on `Changed` — **never in
`Update`** — with the countdown recomputed locally from `SecondsLeftInSeason`. `Available == false`
means the entry point is not drawn at all: no ladder, not an empty one.

---

## 14. Decisions I need from you

| | Decision | Default if you say nothing |
|---|---|---|
| **D1** | Ladder at all? And on which option — A, B or C? | nothing ships; `StubLeaderboardService` stays registered |
| **D2** | If C: which provider, and who owns the account/DPA/on-call? | — |
| **D3** | What is the score? Bars processed in the week is the honest candidate — it is already metered by `Goals` and needs no new hook | bars processed, banded |
| **D4** | If A: are generated opponents acceptable **when labelled as such**? If not, A is off the table and there is no ladder until C | labelled, or nothing |
| **D5** | Paid entry / ticket purchase (frame 0050)? See §10 | no |
| **D6** | What the six brackets pay — gems, master cards, charts? Must come from the existing economy, not the reference game's numbers | — |
| **D7** | Weekly, or tied to live-event windows, or both? | weekly |

D1 and D4 block everything. D3 and D6 are tuning and can follow.

---

## 15. What is verified, and how

In Unity, after a forced reimport and recompile: **0 errors, 0 new warnings**, and the whole EditMode
suite green through the Test Runner — **694 tests, 694 passed, 0 failed, 14.5s**. The suite stood at
635 before this; these files add 44, and the support-id and stash work landing alongside brings the
rest.

The suite was written before that, and run before Unity was available, against Roslyn (`dotnet build`,
net10, 0 warnings) with a reflection runner over the `[Test]` methods — same 44, same result. Two
independent compilers agreeing is worth the ten minutes it cost.

What is pinned: every contract in §5–§8. The half-open season boundary to the second, ids that are
byte-identical under `tr-TR`, the three-level total order, brackets at every edge including rank 0,
the merge rule under replay and under a corrupt negative, the offline outbox and its collapse, the
stranded-season drop, settlement refusing an unfinished season and answering the same twice, and a
cohort that is deterministic and does not chase the player.

What is **not** verified, because it does not exist: any of it running in a scene. There is no UI, no
bootstrap registration and no persistence — see §4.

---

## 16. A note on the order

The pack recommends folder 13 (battle pass) before 12. That order is right for *implementation* — a
pass gives a ladder something to pay into — but 12's deliverable is a decision, and the decision
gates weeks of other people's work (a processor, a DPA, a privacy review). Producing it now costs
nothing and unblocks the calendar. Nothing in §4 depends on 13; the reward brackets in D6 will.

---

## 17. As built — 2026-09-04 · the three-day league

Integration-pack system **12**, shipped on option **A**. Compile-verified across all six assemblies
and test-verified outside the Editor; **not played** — the in-game pass is the user's.

### The decisions, answered

| | Decision | Answer |
|---|---|---|
| **D1** | Ladder, and which option | **A — local league.** No package, no account, no network, no privacy change. The seam is untouched, so C remains a swap rather than a rewrite |
| **D3** | What the score is | **Bars sold in the season**, the default §14 proposed — already metered by `Goals`, so no new hook anywhere |
| **D4** | Generated opponents | **Yes, labelled.** `lig.temsili` is written from the board's own `Synthetic` flag on every refresh |
| **D5** | Paid entry | **No.** Free entry; no ticket SKU exists to be enabled by accident |
| **D6** | What the brackets pay | 150 / 100 / 75 / 40 / 20 / 10 gems, with 3 / 2 / 2 / 1 / – / – master cards |
| **D7** | Cadence | **Three days**, not the weekly default this document assumed |

**What the cadence does to the calendar,** recorded because it looks like a bug the first time it is
seen: the epoch is a Monday and three does not divide seven, so season boundaries walk through the
week — Monday, Thursday, Sunday, Wednesday. Every window is measured from the epoch in seconds
rather than from a weekday, so nothing else cares.

**Sizing for D6.** A whole Production Sprint's milestone ladder pays 210 gems and 6 cards, and the
master set costs ~14,400 gems. Winning every three-day season for a month is ~1,500 gems, a tenth of
the set — beside the sprint rather than ahead of it. The tail still pays, so 27th is a reason to come
back rather than a reason to stop.

### Files

| File | |
|---|---|
| `Assets/Scripts/Core/Ladder.cs` | new — the payout table only. Separate from `Leaderboards` for the reason that file keeps from `LiveEvents`: a ranking is arithmetic, a payout is content |
| `Assets/Scripts/Core/Leaderboards.cs` | `+ThreeDayCadenceSeconds`. Nothing else — cadence was already a parameter everywhere |
| `Assets/Scripts/Systems/LadderService.cs` | new — the score cursor, the settlement sweep, the claim |
| `Assets/Scripts/Systems/LocalLeaderboardService.cs` | `+Restore` — see below |
| `Assets/Scripts/Systems/Save/SaveData.cs` | `LadderState ladder` + `LadderInboxRow`. **No version bump** |
| `Assets/Scripts/Systems/GameBootstrap.cs` | registers the double as `ILeaderboardService`, then the service |
| `Assets/Scripts/UI/LadderUI.cs` | new — ten rows, a pinned "you" row, the claim strip, opener order 6 |
| `Assets/Scripts/UI/HudUI.cs` | `BuildLadder` — makes the screen at runtime, the route `ObjectiveBannerUI` already took |
| `Assets/Resources/Diller/metinler.txt` | 6 `lig.*` keys × 11 languages, appended |
| `Assets/Scripts/Tests/EditMode/LadderTests.cs` | new — 16 tests |
| `Assets/Scripts/Tests/EditMode/LadderUiSmokeTests.cs` | new — the screen builds with no service and no scene |

**No scene was touched, and none needs to be.** `HudUI` makes the screen when the scene has none, so
this needs no prefab wiring to appear — which also means there is nothing here waiting on the Editor.

### Four things worth keeping

**1. The score is derived, not reported.** Bars sold minus a baseline snapshotted when the season
opened — the same shape `GoalService`'s day and week baselines use. Nothing reports into the league,
which is why the market, the yards and the dock needed no edit, and why an existing player's first
season starts at zero instead of counting their whole career.

**2. The league follows the counter, not the screen.** The first build synced only on read. That is
enough for a screen and wrong for a score: a player who sold all season and never opened the ladder
would have had nothing recorded when it closed, and would have settled as if they had not played —
so the reward would have gone to whoever opened a screen. It subscribes to `GoalService.Changed` now.
That path does the least it can: it updates the season's best in the save and nothing else — no
submission, no save, no allocation — and hands over to the full sync only when the season has ended,
which it detects with one comparison against a cached end-second rather than by building a season id.

**3. `Restore`, and why the double needed it.** `LocalLeaderboardService` persists nothing by design.
A season that closed while the app was shut would therefore settle on the zero a fresh dictionary
reports — paying the tail bracket to a player who may have led the board. The owner keeps the score
in the save and replays it on the launch that discovers the rollover. `SubmitScore` cannot do that
job: it refuses a closed season, which is right for a score being *earned* and wrong for one being
*restored*. It merges rather than overwrites, so a replayed stale snapshot can never lower a score.

**4. A season nobody played is not settled.** Rolling one over with a zero score would hand the
player the tail bracket for having been asleep, and a reward for absence makes the bottom of the
board worth as much as playing.

### What the sprint's ranking does now

Nothing, and correctly. `ProductionSprintService` asks the locator for a ladder and only ranks when
the ladder's season id equals its own event id; a `lig-N` season never will. It now receives the real
service instead of a stub and still stays dark — which is what PLAN_11 specified.

### Verification

- **All six assemblies compile: 0 errors, 0 new warnings.** Run against Unity's bundled Roslyn with
  the Editor closed (the MCP bridge was refused all session — something else holds port 8080).
- **16 new EditMode tests pass**, plus the 44 `LeaderboardsTests` that were already here.
- **The full suite runs 815 passing.** The 28 failures are all the offline runner's own limits —
  engine natives (`ECall`), the `Resources`-loading `EconomySimTests`, and one `[TestCase]` the naive
  runner cannot invoke. None are assertion failures and none are in this work.
- **Not played, and not seen.** Every claim about how the screen looks is a guess: no Editor was
  available, so `LadderUiSmokeTests` is the only thing that has exercised `Build()`.

### What to test in game

1. **The opener.** Seventh button in the HUD's bottom row, after the events board. It should carry a
   counter chip only when a closed season owes you something.
2. **Open it.** Ten rows, your row tinted green wherever it sits, the countdown to the season's end,
   and **"Rakipler temsilidir"** under it — that line is the condition the generated cohort pays
   rewards under, and it must never be missing.
3. **Sell some bars, reopen.** Your score should have moved. Rank moves with it — the cohort does not
   chase you, so a big enough day genuinely climbs.
4. **The pinned row.** Outside the top ten, your own row sits at the bottom instead. Fresh save with
   no bars sold: it should read "Listede değil".
5. **Roll a season.** Three days is a long wait, so the honest check is a device-clock jump forward.
   The closed season should file one reward row, the board should reset to zero, and the strip at the
   bottom should say a reward is waiting.
6. **Claim it.** Gems land in the HUD pill, master cards go to the roster, the chip counts down, and
   a second tap pays nothing.
7. **Kill the app mid-season, jump the clock past the end, relaunch.** The settlement must rank you
   on what you actually earned — this is the `Restore` path, and the failure mode it exists to
   prevent is silently finishing last.
8. **A season you never played** should file no row at all.
9. **Switch language** with the screen open, then reopen — the title, the temsili line, "Listede
   değil" and the claim strip should all follow.

### Still to do

- **No opener icon.** `Assets/Resources/UI/Buttons/lig.png` does not exist, so the button falls back
  to the plain blue pill — the same gap the chapter and captain openers had before
  `Tools/ui/hud_bolum_kaptan.py` drew theirs. A trophy collides with the goals opener; a pennant or a
  podium is the obvious piece.
- **`LadderUI`'s sprite slots are empty.** It renders through `UiSkin` fallbacks. Wiring them the way
  `GoalsUI` is wired is an Inspector job.
- **No `LadderConfig` asset.** The payout table runs from `Ladder.Tuning.Default`. An SO would need a
  `.asset` created in the Editor, which was not available; the numbers cannot be tuned without a
  rebuild until one exists — the same state `ChapterConfig` and `CaptainConfig` shipped in.
- **The bracket payouts are worked, not played.** D6's numbers are sized against the sprint and the
  master set, not measured against how a real player's three days actually go.

### The first in-game pass — 2026-09-05

The screen did not open; it drew as one pile of overlapping text. Two causes, both mine.

**1. The canvas was nested inside the HUD's.** `HudUI` made the screen and parented it to itself, the
way it parents `ObjectiveBannerUI`. But the banner has no canvas of its own — it is fitted into the
HUD's rect — whereas this screen builds a `ScreenSpaceOverlay` canvas. **A Canvas inside another
Canvas has its render mode ignored** and becomes a sub-canvas laid out inside the parent's
RectTransform, which here was a zero-sized host: every anchored child collapsed onto one point.

It is made on `UI_Sistemler` now, the plain root object the other code-built screens live on
(verified: root, no Canvas component). `LadderUI.Detach` also leaves any canvas it finds itself
under, so the mistake cannot be made again from a new call site, and
`ScreenLeavesAnyCanvasItWasCreatedInside` pins it.

**2. Ten full-width rows stacked down a landscape screen.** `ChapterUI` already recorded this rule —
*"thirteen full-width rows stacked down a landscape screen come out as a letterbox with bars in it"* —
and this screen was built without applying it. The board is two columns of five now: ranks 1-5 down
the left, 6-10 down the right, the shape the chapter log and the goals screen use.

**Also corrected while there:** the board was being re-requested once a second from `Update`, which
`ILeaderboardService.RequestBoard`'s own contract says not to do — it allocates a board and thirty
entries per call. The per-second tick now only redraws the countdown and syncs the service; the
standings are re-requested on open and on `Changed`.

**Still not verified by me:** how any of it looks. There is no Editor in this session, so the layout
above is reasoning from the other screens' rules rather than a screenshot.

### The second in-game pass — 2026-09-05

Reported: empty buttons, and the board not working at all. One root cause behind the look, plus the
missing icon.

**`Art()` decided the 9-slice from the argument instead of the sprite it assigned.** The helper —
copied verbatim from `LiveEventsUI`, which is where it is correct — reads:

```csharp
img.sprite = sprite != null ? sprite : UiSkin.Panel;
img.type = sprite != null && sprite.border.sqrMagnitude > 0f ? Sliced : Simple;
img.preserveAspect = img.type == Image.Type.Simple;
```

With a null argument it falls back to `UiSkin.Panel` — which **is** 9-sliceable (`Button_Square_Disable`,
border 26) — and then types it Simple with `preserveAspect` on anyway. Every panel, row and strip
therefore rendered as an aspect-locked square that would not fill its rect.

The screens that carry this helper never see it, because their sprite slots are wired in the
Inspector. **A runtime-built screen has no Inspector, so every slot is null by definition** — which
is the whole reason this one was hit. `ScreenBuildsItsBoardAndClaimStripWithoutSceneReferences` now
asserts a row is not `preserveAspect`.

**Theme.** `UiSkin`'s own rule is that the kit art is pre-coloured and state is picked by swapping the
sprite, never by tinting a neutral plate — the tints here were doing exactly what that rule forbids.
The header band takes `UiSkin.Pill`, a normal row `UiSkin.Panel`, the player's row and the claim
button `UiSkin.ButtonGreen`, and the reward strip `UiSkin.ButtonYellow`. The two tint fields are gone.

**The opener icon.** `Tools/ui/hud_lig.py` draws `lig.png`: a podium, gold first step in the middle.
A trophy would have been the natural symbol for a ranking but it is already the goals opener, and a
podium collides with nothing else in the row. It lifts the plate from `gorev.png` exactly as
`hud_bolum_kaptan.py` does, so it sits in the same family.

**And why that icon would still have imported blank.** The project is in 3D behaviour mode, so a new
PNG imports as a plain texture and `Resources.Load<Sprite>` answers null for it — a blank coloured
plate, indistinguishable from a missing file. Every icon under `Assets/Resources/UI/` is generated by
a script and loaded that way, so `Assets/Editor/UI/UiSpriteImporter.cs` now sets the sprite defaults
on **first** import only (`importSettingsMissing`), leaving every setting anybody has since chosen —
the sea set's Repeat wrap among them — untouched.

### The third in-game pass — 2026-09-05 · the frozen score, and the podium

**The score sat at 53 and never moved. That was a real bug, and mine.** The "league follows the
counter" fix has `OnGoalsChanged` move the season's best in the save silently, on the hot path. But
`Track` — the thing that actually submits — then asked *"is the score higher than the save's best?"*,
which by that point is never true. So after the first submission it returned early forever: nothing
new ever reached the board, `Changed` never fired, and the number froze at whatever was last sent
while the player went on selling.

`Track` measures against **what was submitted** now, not against the save.
`TheBoardFollowsTheScoreInsteadOfFreezingAtTheFirstSubmission` pins it, at 53.

**The screen is laid out like the reference now.** Frame 0027's structure, not its art:

| | |
|---|---|
| Podium | ranks 1-3 as cards, **first in the middle and tallest** — the arrangement is what says "podium" before a number is read |
| Rows | ranks 4-9, two columns of three (landscape, per `ChapterUI`'s rule) |
| Chests | **on every position**, gold / silver / bronze / plain by bracket |
| Tap a chest | opens what it holds: which ranks share that bracket, and the gems and cards it pays |
| Player | their card or row takes the green plate; pinned underneath when outside the nine |

**The chest is the reward table, not decoration.** Before this there was nowhere in the game a player
could find out what finishing 7th was worth — the brackets existed only in this document. That is
why the reference puts a chest on every row, and why tapping one had to open.

**New art.** `Tools/ui/lig_sandik.py` draws four chests into `Assets/Resources/UI/Lig/`, in the same
outline-plus-gradient language as the rest of the kit.
