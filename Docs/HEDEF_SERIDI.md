# Hedef şeridi (Core shop tycoon loop) — the game finally says what to do next

**Date:** 2026-09-04 · **Status:** code complete, all six assemblies compile clean, 135 tests green
(run outside the Editor — see §5).
**Outstanding:** nothing blocking. Nothing on screen has been looked at; the Editor was closed for
this work (§6).

Section #01 of the integration pack asked for five things and four of them already shipped. What was
genuinely missing was the one the reference game puts at the very top of its screen: a line that says
what to do next. Kayseri had eleven buttons and no goal. It has one now, and under it a chip that
names the stage of the production chain that is throttling the island.

No economy formula changed. No save field was added. **The save version is still 7**, and this
section adds no persisted state at all.

---

## 1. What the reference does, and what Kayseri already had

| Reference behaviour | Kayseri before this | |
|---|---|---|
| Red upgrade markers over affordable buildings | `UI/UpgradeReadyMarkers.cs` — affordability-driven, pops in, opens the panel | **already shipped** |
| Floating income numbers off the building that earned them | `UI/SaleFx.cs` and `UI/HudJuice.cs` — pooled, batched, one draw call | **already shipped** |
| A stage that visibly grows as you spend | `Gameplay/IslandPhaseController.cs` — per district, with a burst and a camera shake | **already shipped** |
| Queues you can see filling up | `Gameplay/PileStack.cs` — the ore is a real heap that grows on the pad | **already shipped** |
| A persistent objective bar guiding the next milestone | nothing. `Core/Chapters.cs` had five named beats per island, with thresholds, notes and progress in eleven languages, reachable only inside a modal | **built here** |
| Which stage is the bottleneck | `Core/ProductionBottleneck.cs`, read by one screen: the CHAIN page inside the station modal | **built here** |

**The queue was visible; the verdict was not.** Ore genuinely piles up on the pad, so the player can
see that *something* is backed up. What they could not see is which pile matters — and reading a
chain backwards from the market is not something a player does by eye. That is what the chip is for.

---

## 2. What changed for the player

| | before | after |
|---|---|---|
| What to do next | eleven buttons, no goal | a **card under the currency bar**, always up |
| Where the task is done | find the panel yourself | the card **is the button** — it opens that panel |
| A beat you have earned | a badge on an opener you had to notice | the card turns gold and says **CLAIM** |
| An island you have finished | you find out by running out of things to buy | the card says **CHAPTER COMPLETE · Next island** and opens the map |
| A chain that is throttled | a pile of ore and no explanation | **BOTTLENECK · ORE TRUCKS**, tapping to the CHAIN page |
| A chain that is healthy | — | **nothing**. The chip is dark, deliberately (§4) |

The card reads as three lines: `Chapter 1 · FIRST SMOKE` above `Buy 10 upgrade levels` above a bar
reading `6 / 10`. The name comes from the chapter spine, the instruction from that beat's own
localised note, and the numbers from the tuning — so none of the three is a second copy of anything.

---

## 3. Code map

| File | What it holds |
|---|---|
| `Assets/Scripts/UI/ObjectiveBannerUI.cs` | **new** — the strip. Both rows, one refresh, one timer. |
| `Assets/Scripts/UI/HudUI.cs` | `AttachTopStrip` — the seam a code-built strip hangs from — and it hosts the banner. |
| `Assets/Scripts/Core/Chapters.cs` | `NextBeat`, `BeatCounts`. Pure, no new state. |
| `Assets/Scripts/Core/ProductionBottleneck.cs` | `Blocked` (the back-pressure half of `Find`), `StationOf` (rows are not stations). |

**No new service, no new save field, no new text key.** The two words the claim state needs —
`ortak.hazir` and `gorev.al` — were already in the table in eleven languages, and `gorev.al` is the
same CLAIM the goals screen prints on its own claim button, so the two read alike instead of being
two translations of one word. Nothing was added to `metinler.txt`.

**Analytics, where the gear loop's instrumentation ends.** `objective_changed` fires when the target
moves, not when the card is drawn, and carries `island.beat` — so the funnel says how long players
sit on each beat, which is the retention telemetry #14 is gated behind. `bottleneck_changed` carries
the station name and says which stage actually strangles players, per island. `objective_tap` and
`bottleneck_tap` say whether anybody uses the routing.

**Nothing is pooled, because nothing is spawned.** The strip is two rows built once. What it does
instead is refuse to write a label unless the inputs behind it moved: state, chapter, beat, have,
need, the wall, and a language switch. A settled banner allocates nothing.

**Subscriptions.** `ChapterService.Changed` for a claim, `LocalizationService.Changed` for a language
switch, and a one-second poll for everything else. The poll is not laziness: chapter beats are
OBSERVED out of the save rather than reported, so nothing fires when a level is bought. It runs once
a second rather than at the HUD's own quarter second because every tick walks the save's level list.

---

## 4. The rules that are easy to get wrong

Each is pinned by a test, and eleven of them were mutation-checked: the fix removed, the named test
confirmed to fail, the fix restored and the suite re-run green.

**The chip shows back-pressure only, never `Find`'s verdict.** This is the whole design of
`Blocked`. `Find` always names a stage, because a report asked "what is the wall?" needs an answer
even for a chain that is running perfectly — there it says the mine, meaning supply-limited. That is
right for a report and wrong for a badge: it would read BOTTLENECK · MINE on every healthy island
forever, which is how a warning stops being read. This is the same lesson `UpgradeReadyMarkers` was
built on, where a badge tied to "an upgrade exists" would have sat over all five buildings for the
whole run. A pile at its ceiling is different in kind: throughput the player has **already paid
for** is going to waste.

**Report rows are not station indices.** The CHAIN page groups the mine with its railway and gives
the storage shed a line of its own, so row 2 is ORE TRUCKS while station 2 is STORAGE. Anything that
names a row out loud goes through `StationOf`, and the test pins the mapping by NAME so a re-cut
station list fails there rather than mislabelling the chip.

**`Find` has one copy of the thresholds, not two.** Its back-pressure half *is* `Blocked` now. A test
walks a grid of clock readings and asserts the two agree wherever `Blocked` answers, so a future
re-inlining of those four checks fails rather than drifting.

**The next objective is the LOWEST unsatisfied beat, not the one after the last satisfied.** A player
can staff the yard before raising three buildings, so the beats do not fall in order. Walking forward
from the last satisfied beat would name a target they had already met and never send them back for
the one they skipped.

**FULL STEAM's bar reports whichever half is further behind.** It asks for levels and buildings at
once, and `BeatCounts` compares them as fractions rather than raw counts — eight of eight buildings
is further along than 150 of 200 levels even though 150 is the bigger number. Reporting levels while
the buildings were missing is how a bar ends up sitting at 100% refusing to pay. Pinned twice: once
against `BeatProgress`, which had already made this decision, and once directly.

**A claim is judged from the same snapshot as the next beat.** `ChapterService.CanClaim` re-derives
progress per call by walking the save's level list, so asking it five times a second from the one
screen that is always up is five walks — and worse, two separate reads of the save can name one beat
as both earned and outstanding. The refresh takes one `Progress`, then asks the service only whether
each beat was collected, which is a flag rather than a walk.

**The strip's position is solved from the HUD, not authored against it.** `AttachTopStrip` takes the
lowest edge of every authored part of the top area — both currency pills, the rate pill, the settings
button and both indicator chips — and hangs the strip under all of them, so it keeps clearing that
bar if the bar is ever re-laid out. The indicators are measured whether they are showing or not: a
strip that rose when the boost chip expired would be a banner that jumps while it is being read.

**Anchored inside the sheet, never to a fraction of the screen.** `HudUI.AttachBottomButton` learnt
this the hard way — the HUD is a portrait sheet scaled as one piece, so in landscape a fraction of
the screen is not a fraction of the sheet, and two code-built openers once landed on top of the ads
button. Positions are measured through world corners rather than read off `anchoredPosition`, because
the parts of that bar are anchored every which way and subtracting one anchored position from another
is arithmetic across two different origins.

**Nothing inside the card may eat its tap.** The card is one button, so the bar's track and its fill
both have `raycastTarget` off. A tap that landed on the bar would be swallowed instead of opening
the panel.

**A strip that cannot be built is reported, not retried.** If the HUD is somehow not on a
RectTransform there is no sheet to hang anything in. It warns once and disables itself rather than
attempting the build every frame forever, which would hide the misconfiguration.

---

## 5. Verified

- **All six assemblies compile clean: 0 errors, and 0 warnings in any file this section touched.**
  Built with `dotnet build -t:Rebuild` against the project's own csproj files (Unity was closed).
  Every warning that remains is pre-existing CS0618/CS0414/CS0649 in files this work never went near:
  34 in `Game.UI`, 8 in `Game.Gameplay`, and 2 apiece in `Game.Data`, `Game.Systems` and
  `Game.Tests.EditMode`, with none in `Game.Core` — the same counts as before this section.
- **135 EditMode tests run and green, 0 failed.** Compiled and executed outside the Editor in a
  scratch harness that copies the sources byte-for-byte (`diff` confirms). 65 in the Core harness,
  which is `ChaptersTests` and `ProductionBottleneckTests` entire — 14 new cases beside every case
  those two suites already had — plus §03's `GearStashTests` and `CatalogueTests`; and all 70 in the
  Systems harness, run as regression insurance since `Game.Systems` links against the two Core files
  that changed.
- **The first `ObjectiveBannerUI` build added two CS0618 deprecation warnings and they were fixed,
  not accepted.** It had copied `UpgradeReadyMarkers`'s `FindObjectsByType<T>(FindObjectsSortMode)`,
  which Unity 6.4 deprecates.
- **Eleven faults injected one at a time, eleven caught** by the test named for each: the banner
  skipping an earlier beat because a later one landed, a finished chapter still naming a beat, FULL
  STEAM always counting levels, FULL STEAM reporting the half that is further ALONG, FIRST SMOKE
  quoting the wrong threshold, the chain read forwards so a full yard outranks the market that caused
  it, the chip lighting on every healthy island, a pile that is briefly full counting as a wall, a
  report row read as a station index, a non-station row answering MINE, and `Find` re-inlining its
  own copy of the thresholds.
- **`Find`'s behaviour is unchanged by the extraction.** Its nine original tests pass untouched, and
  the new agreement test walks 256 combinations of the four clocks.
- **Not verified: anything on screen.** The Editor was closed for this work, so the card, the chip,
  the bar and the routing have not been looked at, and no run through the Unity Test Runner has
  happened. §6 is that list.

---

## 6. Needs the Unity Editor

1. **Run the Test Runner.** The two suites pass outside the Editor; the in-engine run is what makes
   that official. Nothing here touches a disk.
2. **Look at the strip.** Open the game on coal and check: the card sits clear of the currency bar
   and of the boost and shield chips, it reads `Chapter 1 · FIRST SMOKE` over `Buy 10 upgrade
   levels` over `n / 10`, the bar fills as levels are bought, and tapping it opens the upgrade panel.
   Then in landscape, where the sheet is widest.
3. **Watch the states change.** Cross ten levels and the card should go gold and say CLAIM; claim it
   in the log and the card should move to THE WORKS within a second. On a finished island it should
   read CHAPTER COMPLETE and open the map.
4. **Make the chip appear.** Let a yard back up — buy mine Richness and leave the ore fleet alone —
   and `BOTTLENECK · ORE TRUCKS` should show within a minute of the pile sitting at its ceiling, and
   go away when the fleet is upgraded. The clock needs about six seconds at the ceiling inside a
   trailing minute, so it will not appear instantly.
5. **The long words are the clipping risk.** German (`AUSRÜSTUNG`-length compounds) and Russian on
   the instruction line and the chip. Every label is shrink-to-fit down to 12px, but that has not
   been seen.
6. **No scene edit is needed, deliberately.** `HudUI` finds an authored `ObjectiveBannerUI` if the
   scene ever gets one and otherwise makes the object itself. If you would rather author it — to wire
   a dedicated icon and the card and chip sprites instead of borrowing the chapter opener's icon and
   `UiSkin`'s panel — add the component anywhere in `Main.unity` and fill the three sprite slots; the
   HUD will find it and leave its art alone.
7. **Tunables are all on the component**, not in a config asset: the strip's width fraction, the card
   and chip heights, both gaps, the five colours and the refresh interval.
