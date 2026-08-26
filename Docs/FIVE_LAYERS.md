# Five Layers — the chapter spine, the sea, the workshop, the crew, the season

**Started:** 2026-08-26 · **Reference set:** *Idle Weapon Shop* (HOT GAMES CO.)

> Every number in this document is a **DEFAULT**, in the sense GDD §14 means it: it lives in a
> ScriptableObject and is edited in the Inspector. The numbers here describe the *shape* of the
> curve; the values get set by measurement, the way REMAKE_PLAN §P7 set the island ladder.

---

## 1. Why this exists

[VOYAGES.md](VOYAGES.md) §1 diagnosed the gap and closed half of it: every bar had exactly one
destination, and the dock made a second. §16 then named what was left, in its own words:

> **Chapters, events, leaderboards.** The other two gaps from the analysis. Separate specs.

This is those specs. The reference game runs five playable layers wrapped in chapters and events;
we own two of the five outright, a third in menu form, and none of the wrapper.

| | Layer | Where | State |
|---|---|---|---|
| 1 | **The Island** — chain, 18 axes, 10 unlocks, bottleneck readout | `Gameplay/CoalOperation.cs`, `Core/IslandEconomy.cs` | shipped |
| 2 | **The Market Yard** — walk, carry, sell, hire | `Gameplay/Market/*`, `Core/MarketFlow.cs` | shipped |
| 3 | **The Sea** — voyages as a menu and a dock pad | `Core/Voyages.cs`, `Systems/VoyageService.cs` | V1–V4 · becomes a scene |
| 4 | **The Workshop** — craft, rarity, equip-or-display | — | planned |
| 5 | **The Roster** — 8 foremen, rarity, duplicates | `Core/Foremen.cs` | **+ 10 captains, gacha, pity — shipped** |

**One rule was overruled deliberately.** §16 excluded combat — *"the risk roll buys the same decision
for ~1% of the cost."* That call was reversed on 2026-08-26: the sea becomes a real scene with ships
and characters. The rule that keeps it from breaking the idle game is in §4 below.

---

## 2. The rules every layer obeys

Four are [VOYAGES.md](VOYAGES.md) §3, unchanged. The fifth comes from `MarketFlow`.

| | Rule | Why |
|---|---|---|
| **R1** | Nothing new pays **cash**. | `MarketService.Earn` is the only faucet. Two faucets compete, and whichever pays less becomes pointless. |
| **R2** | Nothing new pays a **rate**. Rewards are items, cards or **ceiling lifts**. | Every island is clamped by `incomeCapPerMin`, so a rate bonus does nothing for exactly the player who earned it. `Foremen.cs` already solves this by lifting the ceiling too. |
| **R3** | Nothing **expires**. | `ContractService`: an idle game must never punish a player for looking away. |
| **R4** | Costs are **fractions of delivery**, never absolute bars. | The ore ladder multiplies output ×3.2 per tier. |
| **R5** | Never add a link to the yard's **selling chain**. | `MarketFlow.ServiceRate` is the **minimum** of Carry/Serve/Collect. A fourth job would silently drop every existing yard to the 0.15 idle trickle — §20 already cut the `Load` hire for this. Parallel systems only. |

**And never bump `SaveMigration.CurrentVersion`** — it wipes the run. Every stage adds fields that
default-initialise plus its own `Normalise()`, the precedent `ForemanService` and `VoyageService` set.

---

## 3. Build stages

| | Stage | Depends on |
|---|---|---|
| 0 | **Instrumentation** — real Analytics + RemoteConfig | — |
| 1 | **Chapters & the story spine** | — |
| 2 | **The Captains** — roster, rarity, gacha, pity · **shipped 2026-08-26** | — |
| 3 | **The Sea** — `Sea.unity`, encounters, combat · **S1+S2 shipped 2026-08-26** | 2 |
| 4 | **The Workshop** — craft, rarity grades, equip-or-display | 3 |
| 5 | **Chapter bosses** | 1, 3, 4 |
| 6 | **Seasons & events** | all |

---

## 4. The one rule for the sea *(Stage 3, not yet built)*

> **Active sailing can only improve a voyage's outcome, never worsen it.**

Sail idly and you get today's behaviour exactly. Sail actively and you fight encounters for loot
**on top**; lose every fight and you still get the idle result. This is the relationship §20 already
established for hand-carrying — *"the automatic share is what the dock manages on its own, and the
player is the pair of hands on top"* — and it is what keeps R3 intact when combat lands.

**No stamina.** The reference game gates exploration on it; berths (max 4) and hold-fill time already
limit us, at no new currency and no new nag. Deliberate difference, recorded rather than discovered.

---

## 5. Stage 1 as built — 2026-08-26

Shipped and verified against the Editor. **Not yet played** — see "How to test" below.

Eight chapters, one per island, five beats each. Every beat is a thing the player was going to do
anyway, now named and paid for.

| # | Island | # | Island |
|---|---|---|---|
| 1 | coal | 5 | gold |
| 2 | copper | 6 | ruby |
| 3 | iron | 7 | emerald |
| 4 | silver | 8 | diamond |

| Beat | Asks for | Default |
|---|---|---|
| 0 **LANDFALL** | the island is owned | — |
| 1 **FIRST SMOKE** | axis levels bought here | 10 |
| 2 **THE WORKS** | ghost buildings raised here | 3 |
| 3 **THE YARD** | every yard job hired and levelled out | `MarketFlow.IsMaxed` |
| 4 **FULL STEAM** | levels **and** buildings | 200 · 8 |

Rewards are gems and foreman cards, `Base + Step × chapter`, with FULL STEAM worth ×3 gems and ×2
cards. **No cash** — R1.

### Files

| File | |
|---|---|
| `Assets/Scripts/Core/Chapters.cs` | new — the rules, the ladder, the reward table |
| `Assets/Scripts/Systems/ChapterService.cs` | new — reads the save, pays out, `Normalise()` |
| `Assets/Scripts/Data/ChapterConfig.cs` + `Assets/Data/ChapterConfig.asset` | new — the tunables |
| `Assets/Scripts/UI/ChapterUI.cs` | new — eight islands left, five beats right, code-built |
| `Assets/Scripts/Systems/Save/SaveData.cs` | `List<ChapterState> chapters` + the class. **No version bump.** |
| `Assets/Scripts/Systems/GameBootstrap.cs` | `chapterConfig` slot, `Chapters` property, registration |
| `Assets/Resources/Diller/metinler.txt` | 24 `bolum.*` keys × 11 languages |
| `Assets/Scripts/Tests/EditMode/ChaptersTests.cs` | new — 18 tests |
| `Assets/Scripts/Tests/EditMode/ChapterServiceTests.cs` | new — 18 tests |
| `Assets/Scenes/Main.unity` | `ChapterUI` added to `UI_Sistemler`, beside `GoalsUI` and `ForemanRosterUI` |

### Four decisions

**1. A chapter never gates the next island.** §16 considered making a won resource a requirement for
an island and dropped it, because a new resource in front of the ladder can stall a player behind a
system they have not engaged with. Islands stay bought with cash; a chapter gates its own rewards and
nothing else. `EveryBeatIsReachable_NoChapterCanDeadlock` is the test that holds this.

**2. Nothing reports into it.** `GoalService` needed a `Record()` call in six files. This needed none:
every beat is derived from state the save already carries, so the island sim, the yards and the dock
go on knowing nothing about chapters, and a beat cannot be missed because somebody forgot to call in.
It is also why an existing player's islands light up everything they have already earned the first
time they open the screen.

**3. Rows are keyed by island and carry their own beat array.** A single flat `Count × BeatCount`
array would have been smaller and would have re-labelled every chapter after the first the moment a
sixth beat was appended. `AShortBeatArrayIsPaddedAndKeepsItsClaims` pins the growth path — Stage 5's
boss beat costs one entry in `Chapters` and no migration.

**4. Every threshold is a COUNT, identical on all eight islands.** This is `Goals.cs`'s reasoning one
layer up: cash and bars inflate 3.2× per ore tier, so a threshold in money is a wall on coal and a
formality on diamond. Levels and buildings mean the same thing everywhere, forever — which is why the
chapter index is the only thing the rewards scale by.

### Deliberately not in Stage 1

- **The boss beat.** It needs the sea scene and a crew to fight it with. The save shape is already
  sized for it (see decision 3).
- **Per-chapter beat copy.** Each chapter gets its own opening line; the five beat names are shared.
  48 × 11 translated strings for one line of flavour each was the wrong trade.
- **A chapter-complete celebration.** `ConfettiBurst` and `WelcomeBackFx` exist and this should use
  one; it is presentation, and Stage 1 is the spine.

### Verification

- **332 EditMode tests pass** (was 296; +36). `Game.Tests.EditMode`, run via Unity MCP.
- **0 compile errors. 0 new warnings** — the console's warnings are all pre-existing `CS0618`
  obsolete-API and `CS0414` unused-field notices in other files; nothing matching `Chapter`.
- **All four types load in the Editor** (`Chapters`, `ChapterService`, `ChapterState`,
  `ChapterConfig`), and the rules answer correctly against a live probe: a maxed island satisfies
  5/5 and reads complete; a freshly bought one satisfies 1/5.
- **All 35 loc keys resolve in all 11 languages**, format substitution included
  (`200 seviye ve 8 tesis` / `200 levels and 8 buildings` / `200 уровней и 8 зданий`).
- **Not played.** Every claim about how it *feels* is still a guess — VOYAGES §22's warning stands.

### How to test in game

1. **Open the chapter screen.** New button in the HUD's bottom row, third from the left, beside
   GÖREVLER and USTABAŞI. It should carry a counter chip showing how many beats are waiting.
2. **It should open on the island you are actually on**, not on coal — buy copper, then open it.
3. **On a fresh save**, coal shows LANDFALL claimable and the other four with empty bars. The other
   seven islands read locked, and their story lines stay hidden.
4. **Claim LANDFALL** — gems land in the HUD pill, the button greys to ALINDI, the chip counts down.
5. **Buy ten upgrades on coal** and reopen: FIRST SMOKE should be full and claimable. The bar must
   never read full while the button is still dead.
6. **HEPSİNİ AL** takes everything owed in that chapter in one press.
7. **Switch language** (Ayarlar) with the screen open, then reopen — every line should follow.
8. **An existing save should already owe you beats** for islands you have built out. That is the
   design, not a bug.

### Still to do

- **The opener icon.** `Assets/Resources/UI/Buttons/bolum.png` does not exist, so the button falls
  back to the plain blue pill. Needs a piece in the house style — see `Tools/ui/hud_butonlar.py`,
  which generated `gorev.png` and `ustabasi.png`.
- **`ChapterConfig.asset` is not wired.** It exists at `Assets/Data/ChapterConfig.asset`; drop it on
  `GameBootstrap`'s `chapterConfig` slot in `Bootstrap.unity`. Until then the game runs on
  `Chapters.Tuning.Default`, which carries the same numbers — nothing is broken, but they cannot be
  tuned without a rebuild.
- **`ChapterUI`'s sprite slots are empty.** It renders through `UiSkin` fallbacks. Wiring them the
  way `GoalsUI` is wired (`MaviSet/panel_beyaz`, `serit_mavi`, `btn_hap_kalin`, `btn_kapat_yeni`,
  `slider_yatak`, `bar_dolgu`, `gosterge_grafit`, `ikon_elmas`) is an Inspector job.
- **The thresholds are guesses.** 10 / 3 / 200 / 8 have not been measured against
  `EconomyCurve.SamplePerMin`. FULL STEAM in particular should be checked against how long an island
  actually takes — `EconomySimTests` is the instrument.

---

## 6. Stage 2 as built — 2026-08-26

Shipped and verified against the Editor. **Not yet played.**

Ten captains, four roles, five grades, pulled from a crate bought with a currency the sea pays.

### The naming change, first

The plan called these **crew**. The word was already taken twice over: `Voyages.Crew` is ship
upgrade track 2, and `sefer.murettebat` is the shipyard tab that buys it. Those are the anonymous
hands that make a hull carry more. A **captain** is a named person who commands one voyage. Renaming
after shipping would have cost a save migration, so it was done before.

### The roster

| | Captain | Role | Grade |
|---|---|---|---|
| 0 | Kemal the Scales | Quartermaster | Common |
| 1 | Selim Powderhorn | Gunner | Common |
| 2 | Musa Ropehand | Bosun | Common |
| 3 | Derya Quill | Purser | Common |
| 4 | Zehra of the Hold | Quartermaster | Rare |
| 5 | Baran Ledger | Purser | Rare |
| 6 | Iron Orhan | Bosun | Epic |
| 7 | Nihal Compass | Quartermaster | Epic |
| 8 | Hüsrev Roundshot | Gunner | Legendary |
| 9 | Ateş Northwester | Bosun | Mythic |

Every role is drawable at Common, so whatever a new player pulls first does something they can point
at; every role appears at two grades or more, so no role is a trap you can only draw badly.

### What a captain does — and what none of them touch

| Role | Aboard a voyage |
|---|---|
| **Quartermaster** | +charts |
| **Gunner** | +salvage |
| **Bosun** | −risk points, and a shorter repair window after a failure |
| **Purser** | a share of returned cards goes to the foreman furthest behind instead of at random |

**Nothing here multiplies `Voyages.Cards`.** [VOYAGES.md](VOYAGES.md) §21 records that the first
voyage defaults were wrong by ~2.5×, and names the cause: *"a multiplicative stack — tier payout ×
hold × crew — where each factor was defensible alone and the product was not."* Those numbers were
then re-solved against four constraints at once. A captain moves the two closed-loop currencies, the
risk, the repair window and where cards land — five knobs, none of them in that solve.
`CaptainsTests.NothingCaptainsOwnIsAnArgumentToTheCardPayout` asserts the *signature*, not a value,
because a value test cannot tell "no captain factor exists" from "the factor happened to be 1 here".

### Charts — a second closed loop

Charts are paid by every voyage, shaped exactly like salvage (same tier and hold multipliers, same
reduced share on a failure), and spent only on crates. They are **not gems** on purpose: the foreman
roster already eats gems, and pricing both rosters in one currency puts them in competition for one
wallet — the trap R1 names for cash faucets, which reads the same way for sinks. Neither end of the
chart loop can reach the cash economy however badly it is tuned.

### Two pities, and why not one

A single counter gives a bad shape: short and legendaries stop being rare, long and a new player can
pull thirty commons in a row and conclude the crate is broken.

| | Window | Effect |
|---|---|---|
| **Short** | 10 pulls | guarantees an Epic+ — which is why a 10-pull always contains one |
| **Long** | 70 pulls | guarantees a Legendary+, with a weight ramp from pull 45 so the guarantee is usually beaten by a real roll |

Both counters live in the save, so a pity that reset on launch would be a pity the player could farm
by closing the app. `ThePityCountersSurviveInTheSave` holds that.

### Two things measurement changed

**1. A rarer captain needs FEWER copies.** The opposite of the obvious answer, and the only one that
works. At one flat curve of 90 duplicates the roster paced like this: own all ten in 4 days, max a
Common in 15 — and max the single Mythic in **370 days**, because 0.66% of pulls carry them and
ninety of those is fourteen thousand crates. VOYAGES.md §21 sizes one foreman at "four to six weeks
of ordinary play"; a year for one captain is not a long tail but an unreachable one, and an
unreachable ceiling makes the ladder beneath it read as pointless. Scaling the curve by grade lands
it here, measured on a maxed fleet at ~39 pulls a day:

| Grade | Duplicates to max | Pulls | Days |
|---|---|---|---|
| Common | 90 | 570 | 15 |
| Rare | 80 | 609 | 16 |
| Epic | 55 | 765 | 20 |
| Legendary | 35 | 840 | 22 |
| Mythic | 16 | 2,596 | 67 |
| *own all ten* | — | *166* | *4.3* |

**2. A purser always places at least one card.** Found by a failing test, not by reading. A tier-0
voyage pays one card and a Common purser's share of it is 0.4, which rounds to nothing — so the role
did exactly nothing on the only route a new player has open. A role that is inert precisely where it
is first met reads as broken. The floor costs the balance nothing, because the count is not what is
moving.

### Files

| File | |
|---|---|
| `Assets/Scripts/Core/Captains.cs` | new — roster, grades, roles, levels, the five effects |
| `Assets/Scripts/Core/CaptainCrate.cs` | new — weights, both pities, the soft ramp, price |
| `Assets/Scripts/Systems/CaptainService.cs` | new — state, crate opening, levelling, `Normalise()` |
| `Assets/Scripts/Data/CaptainConfig.cs` + `Assets/Data/CaptainConfig.asset` | new — both tuning structs in one asset |
| `Assets/Scripts/UI/CaptainRosterUI.cs` | new — crate left, ten captains in two columns right |
| `Assets/Scripts/Core/Voyages.cs` | `+ChartRate`, `+Charts()`, `+RiskFor` overload. **Every existing signature untouched.** |
| `Assets/Scripts/Data/VoyageConfig.cs` | `+chartRate` |
| `Assets/Scripts/Systems/VoyageService.cs` | captain slot, charts payout, the four role effects |
| `Assets/Scripts/Systems/ForemanService.cs` | `+GrantDirectedDuplicates` — what a purser buys |
| `Assets/Scripts/Systems/Save/SaveData.cs` | charts, two arrays, two pity counters, `VoyageState.captain`/`payoutCharts`. **No version bump.** |
| `Assets/Scripts/Systems/GameBootstrap.cs` | `captainConfig` slot, registration before the dock |
| `Assets/Resources/Diller/metinler.txt` | 34 `kaptan.*` keys × 11 languages |
| `Assets/Scripts/Tests/EditMode/CaptainsTests.cs` | new — 20 tests |
| `Assets/Scripts/Tests/EditMode/CaptainCrateTests.cs` | new — 19 tests |
| `Assets/Scripts/Tests/EditMode/CaptainServiceTests.cs` | new — 23 tests |
| `Assets/Scenes/Main.unity` | `CaptainRosterUI` on `UI_Sistemler` |

### Three decisions

**1. The captain sits ALONGSIDE the foreman, not instead of them.** Two slots on one voyage: the
foreman cuts risk, the captain does their own job. A single officer slot would have made the two
rosters compete for it, and whichever paid better would make the other pointless.

**2. A bosun's nerve STACKS with the foreman's.** Taking the better of the two would make one officer
pointless the moment the other was levelled. What keeps that safe is the size of the numbers, not a
special case: a maxed Mythic bosun beside a maxed foreman takes 26 points off the far reach's 30, and
`TheBestPairOfOfficersStillLeavesTheFarReachARisk` is what holds the remaining 4 there. §10 refuses
to *sell* guaranteed success; this refuses to let it be collected either.

**3. Levels cost duplicates and nothing else.** The foremen charge gems on top of their cards. Charts
already paid for the crate, and charging twice would put the two rosters back in one wallet.

### Verification

- **394 EditMode tests pass** (was 332; +62). 0 compile errors, 0 new warnings.
- **The distribution was measured, not assumed** — 20,000 real pulls through the service:
  57.66 / 24.46 / 12.99 / 4.24 / 0.66%. Epic and Legendary run above their base weights (10.5% and
  3%) because pity lifts them, which is what pity is for.
- **Both pity windows hold under the worst luck available**: longest Epic dry run 9 of 10, longest
  Legendary dry run 67 of 70, across 20,000 pulls.
- **All 35 loc keys resolve in all 11 languages**, format substitution included.
- **The 50 voyage tests still pass unchanged** — `CaptainService` is an optional collaborator, and
  `ADockWithNoRosterBehavesExactlyAsItDidBefore` pins that.
- **Not played.** Every claim about how it *feels* is a guess.

### How to test in game

1. **Sail a voyage and claim it.** The dock should now pay charts as well as cards and salvage.
2. **Open the captain screen** — fourth button in the HUD's bottom row. The crate shows the charts
   you have, both pity counters in pulls, and how much of the roster you own.
3. **Open one crate.** The card should name what came out, coloured by grade.
4. **Open ten.** There must always be an Epic or better in a ten — that is the whole reason the
   button exists.
5. **Level a captain** once duplicates reach the quoted number. The button reads `have/need`.
6. **Put a captain aboard** from the dock panel and check the risk line moves for a bosun.
7. **Switch language** with the screen open, then reopen — names, grades and roles should all follow.

### Still to do

- **`CaptainConfig.asset` is not wired.** Drop it on `GameBootstrap`'s `captainConfig` slot. Until
  then the game runs on the same numbers via the code defaults.
- **No captain portraits.** The rows are a grade stripe and two lines of text. `Docs/ASSETS.md` §J is
  where ten portraits would be specified.
- **`ChartRate = 4` has not been played against.** The pacing above is simulated on a maxed fleet;
  §14's warning stands — a simulation is a better guess, not a measurement of the real thing.

---

## 7. The dock picker and the UI pass — 2026-08-26

Three things, after the screens were first looked at in the game.

### The captain picker

`VoyageUI` now carries two officer chips on one row: the foreman on the left, the captain on the
right. Both are the same cycling chip — press to walk nobody → each available officer → nobody again
— which is the pattern the foreman already used, and for the reason it gives: most of a run has two
or three of them, so a picker would be a screen opened in order to press its only button.

Three details that mattered:

- **The tier buttons now quote the real risk.** They called `RiskFor(tier, foreman)`; a bosun aboard
  changes that number, so the panel was understating the odds on exactly the decision it exists to
  offer. They call the three-argument overload now.
- **A stale captain is cleared on open**, the same way a stale foreman already was: they may have
  sailed on another berth since the panel was last closed, and a chip naming somebody who cannot go
  is a button that does nothing.
- **The chip hides entirely until a captain has been pulled**, mirroring the foreman chip, which
  hides until anyone is hired. A button naming a system the player has never met is a question they
  cannot answer.

### The bug that made both new screens unreadable

`ChapterUI` and `CaptainRosterUI` each carried this line, copied from `GoalsUI`:

```csharp
if (cardPanel == null) c.GetComponent<Image>().color = card;   // card = dark navy
```

`GoalsUI` has all eight of its sprite slots wired, so the line never fires there. The two new screens
had **none** wired — so `Art()` fell through to `UiSkin.Panel`, which is real art, and then this line
tinted it dark navy. Every card was a dark panel with the dark `Ink` palette painted on it. The
screens were not confusing so much as invisible.

Both lines are gone. A card is now light whether or not a sprite is wired, which is what the Ink
palette assumes.

### A sheet to sit on

The screens were loose cards floating on a translucent scrim with the island still moving between
them — legible in a mock-up, unreadable in motion, because the eye has nothing to anchor on and
every gap is a moving picture. Both now build an opaque **`Zemin`** sheet first, so sibling order
puts it behind every card, and it eats its own taps. This is what `VoyageUI`'s `SeferPaneli` already
did; the difference is between a window and a heads-up display.

The scrim also dismisses on a tap now, like every other panel in the game.

### Wired, not left for later

Both screens' eight sprite slots were filled from `GoalsUI`'s — `panel_beyaz`, `serit_mavi`,
`btn_hap_kalin`, `btn_kapat_yeni`, `slider_yatak`, `bar_dolgu`, `gosterge_grafit`,
`diamond_128x128`. Those are the exact pieces the screens' own tooltips name, so this was not a
design choice being made on the user's behalf; leaving them empty was the bug.

### The two missing opener icons

The bottom row is eight buttons now. Six carried their own icon; the chapter and captain openers had
no art and fell back to `UiSkin.ButtonBlue`/`ButtonYellow` — two blank coloured plates in a row of
six drawn icons, which is the first thing the eye lands on.

`Tools/ui/hud_bolum_kaptan.py` draws them: **an open book** for chapters (a chapter is a story, and
a book collides with nothing — the trophy is quests, the helmet is foremen) and **a captain's cap**
for captains, white-crowned with a gold anchor, because the foreman's helmet is yellow and two
people-shaped icons only stay apart at 150px by colour.

The plate is **not drawn, it is lifted**. `gorev.png`'s plate is copied and the trophy erased: the
body is flat horizontally and a soft gradient vertically, so every row is a band of its own colour
sampled from one symbol-free column. A plate drawn from scratch would never have matched the
outline, corner radius and gloss exactly, and would have looked like a different family sitting next
to the others.

The existing `Tools/ui/hud_butonlar.py` could not be reused: it rasterises SVG through headless
Chrome at a hardcoded Windows path. The new script draws with PIL and runs anywhere.

### Verification, run in the Editor

Both screens and the dock panel were opened in play mode and captured. The player's save was backed
up first and restored byte-identical afterwards — play mode fires `OnApplicationPause`/`Quit`, which
save for real, so a verification run *does* overwrite the file.

- 394 EditMode tests pass. 0 errors, 0 new warnings.
- The chapter screen now reads as a ladder: all eight island names down the left, the locked ones
  greyed with the lock in the subtitle.
- The captain screen: labelled chart chip, unambiguous crate prices, readable pity lines, and a
  level button that says the action.
- The dock shows both officer chips side by side.
- The two new openers now carry real icons.

### Still to do

- **`CaptainConfig.asset` is not wired.** Drop it on `GameBootstrap`'s `captainConfig` slot.
- **No captain portraits.** The rows are a grade stripe and two lines of text; `Docs/ASSETS.md` §J
  is where ten portraits would be specified.
- **`ChartRate = 4` has not been played against.** The pacing in §6 is simulated on a maxed fleet.
- **The dock panel still wears `UiSkin.Panel`**, which is a grey button plate rather than the
  `panel_beyaz` sheet the chapter and captain screens use. That is how it has always looked and is
  not something this pass changed, but it is why the dock reads as a different game from the two new
  screens.

---

## 8. Stage 3 · S1 as built — 2026-08-26

The scene, the lane, the ship, the camera and the handoff. **No combat** — that is S2, and everything
below exists to be the ground it stands on.

Run in the Editor: a voyage was sailed, boarded from the dock panel, and watched from the deck.

### What is there

| | |
|---|---|
| `Assets/Scenes/Sea.unity` | one `SeaBoot` object, nothing else — the shape `Market.unity` uses |
| `Assets/Scripts/Core/Expedition.cs` | new — the clock→position maths and the lane's shape |
| `Assets/Scripts/Systems/ExpeditionService.cs` | new — who is aboard which berth. **Saves nothing.** |
| `Assets/Scripts/Gameplay/Sea/SeaLane.cs` | the route in world space |
| `Assets/Scripts/Gameplay/Sea/PlayerShip.cs` | the hull, placed from the clock |
| `Assets/Scripts/Gameplay/Sea/SeaCamera.cs` | fixed-angle follow, like `MarketCamera` |
| `Assets/Scripts/Gameplay/Sea/SeaScene.cs` | water, both ports, the buoys |
| `Assets/Scripts/UI/SeaSceneBoot.cs` | assembles it, owns the frame, owns the exit |
| `Assets/Scripts/UI/SeaHudUI.cs` | route · leg · ETA · progress · go ashore |
| `Assets/Scripts/UI/VoyageUI.cs` | **SAIL WITH HER** button, shown only for a ship at sea |
| `Assets/Scripts/UI/SceneCurtain.cs` | `Cover` gained an optional `parkCurrent` |
| `Assets/Scripts/Systems/GameBootstrap.cs` | registers `ExpeditionService` after the dock |
| tests | `ExpeditionTests` (17), `ExpeditionServiceTests` (9) |

### The rule, and how it is held

> Active sailing may only improve a voyage's outcome, never worsen it.

Held by the **shape of the code**, not by a check:

- Position is a pure function of `sailedUnix`, `returnsUnix` and now. There is no path from the scene
  to the voyage. `GoingToSeaWithHerChangesNothingAboutTheVoyage` compares every field of the
  `VoyageState` across a board / read / ashore cycle, and
  `ExpeditionExposesNoWayToWrite` asserts `Expedition` stays a static class with no fields — so if
  somebody adds state there, that is the conversation it has to have first.
- Nothing is saved. Standing on a deck is not progress, so a player killed mid-crossing loses only
  the view.
- Leaving is always allowed and needs no confirmation, because nothing is being abandoned.

### Three things measurement changed

**1. Seventeen degrees.** The camera's FOV is 40, so it sees from `pitch−20` to `pitch+20`. At the
first pass's 44 the top ray still pointed at the water: no horizon in frame, and the sea rendered as
a flat blue field with no sky, no scale and no sense of a world. Anything above 20 has that problem.

**2. An empty scene has no lighting at all.** No skybox, no ambient. Every face turned away from the
one directional light rendered black — the far port and the buoys were silhouettes. Setting ambient
is not a preference here, it is required. Overdoing it is the opposite failure: a bright trilight
ambient washed every material out to a single cyan.

**3. The buoys are load-bearing.** A hull on an empty plane has no parallax — it can be moving at any
speed or none and the eye cannot tell. Twenty-three markers down the route are the cheapest thing in
the scene and the only reason the crossing reads as one.

### Scene flow

Market → Sea is a plain single load. Sea → Market passes `parkCurrent: false`, so the market is
rebuilt rather than parked — it is small and code-built, and parking the sea under it would leave a
whole scene resident behind a screen the player has walked away from. `SceneCurtain`'s existing
Main↔Market parking is untouched.

### What S2 inherits

- **`SeaLane.Beside(u, distance)`** — a point off the route, the same distance from it wherever the
  lane is bending. `BesideTheLaneIsAlwaysTheSameDistanceFromIt` pins that. This is where an encounter
  goes.
- **`SeaSceneBoot.Update`** is the single place the clock becomes a position — the one hook needed to
  hold the ship still for a fight.
- **`PlayerShip.Hull`** is the transform to hang muzzle flashes, damage and crew off.

### The decision S2 has to make first

**Captains have no combat stats.** All four roles — Quartermaster, Gunner, Bosun, Purser — are
defined as *voyage* effects (charts, salvage, risk, where cards land). There is no Attack or Guard
anywhere in `Captains.cs`. S2 must either add a combat stat block or derive fighting power from role
+ grade + level, and that changes whether `CaptainConfig` grows a second half. Worth settling before
code.

### Verification

- **420 EditMode tests pass** (was 394; +26). 0 errors, 0 new warnings.
- **Run in the Editor.** A tier-0 voyage was sailed, boarded, and watched at the halfway turn: ship
  on the lane at u=0.973 homeward, camera following, 34 renderers, HUD reading
  *COASTAL RUN · HOMEWARD · Arrives in 12:38*. The save was backed up first and restored
  byte-identical.
- **Not played as a player would.** The crossing has been looked at, not lived through.

### Still to do

- **The ship and the ports are primitives** — the V4 dock precedent (§20/§23 of VOYAGES.md): shape
  first, models once it has been played. `SM_Harbor_Launch.fbx` is already in the project and is the
  obvious hull; wiring it needs a serialized slot and an Inspector pass.
- **No wake, no water motion.** The sea is a flat colour. A scrolling normal or a simple vertex
  displacement is the cheapest large improvement available.
- **No ambience.** `AudioService` has a market bed and an island bed; the sea has neither.
- **The lane is the same length for every tier.** A far reach and a coastal run look identical from
  the deck. `ExpeditionConfig` does not exist yet — the lane's numbers are `[SerializeField]` on
  `SeaLane` and should move into one when S2 gives them company.

---

## 9. Stage 3 · S2 as built — 2026-08-26 · combat

Encounters, the auto-battle, and the three abilities. Compile-verified, 448 EditMode tests pass.
**Not run in the Editor and not played — by request: the in-game pass is the user's.**

### The decision §8 left open, decided

**Fighting power is DERIVED, not stored.** The captains carry no combat stats and never will by this
design: the ship's own Crew track sets the base, and the captain aboard multiplies it by the same
per-grade worth the roster already pays (`Captains.PerLevel`), doubled for a Gunner because fighting
is their whole name. Collecting harder therefore fights harder, and no new number was created — §21's
lesson held structurally, with `FightingPowerIsDerivedNotStored` asserting the captain card keeps
exactly its three fields.

### The fight

A threat comes alongside and stays for a **window** (28s). Your firepower drains its hull; its
menace drains your **nerve**. Three endings: hull empty first — **sunk**, loot banks; window ends —
it **got away**; nerve empty — it got away. Two of the three pay nothing, and none of them touch the
voyage: §4 is held because the voyage is not an argument to anything in `SeaCombat`.

| Ability | Does | Default |
|---|---|---|
| **BORDA** | nine seconds of gunnery landed at once | CD 12s |
| **SİPER** | incoming fire cut to 35% for ten seconds | CD 16s |
| **KANCA** | the window grows 12s — **the threat is held, never the ship** | CD 20s |

The shape, worked by hand and then pinned by tests: tier 0 falls to pure watching; tier 1 wants one
brace; **from tier 2 up the buttons are load-bearing** — no roster alone clears it, because the nerve
clock beats the hull clock until Brace bends it. Two defaults moved during that working: burst 7→9
and brace 8→10, because at the old values the fight the buttons exist for was unwinnable *with* the
buttons — `TheAbilitiesTurnTier2FromEscapeIntoKill` is the regression wall.

### Threats come to the watcher, not to the clock

The first design put encounters at fixed voyage times; a player boarding a 35-minute crossing would
have stared at empty water for most of it. Threats spawn **on presence** instead — first at ~6s,
then every ~18s — so a session aboard is fights back to back, and the idle player still loses
nothing because an unseen encounter never existed. Three kinds off one stat curve: the **raider**
(baseline), the **beast** (tougher, gentler), the **derelict** (an unmanned prize that cannot fight
back). Deterministic per voyage, like `Goals.DailyIndex`.

**The cap is what keeps presence from becoming a farm**: a crossing pays for `2 + tier` kills and
then goes quiet, keyed on the `VoyageState` object itself. `AClearedCrossingStaysABonusNotASecondFaucet`
asserts the summed loot of a fully fought crossing stays **under** what the hold itself brings home
— the pair of hands on top, never a second faucet. Loot is a *share* of the voyage's own tier tables
(R4 one layer up), banked through `ExpeditionService.RegisterKill` — the layer's single write, aimed
away from the voyage into the two closed loops (charts, salvage). **Never cash** (R1).

### Files

| | |
|---|---|
| `Assets/Scripts/Core/SeaCombat.cs` | new — threats, derivation, the fight struct, abilities, loot |
| `Assets/Scripts/Data/SeaCombatConfig.cs` + `Assets/Data/SeaCombatConfig.asset` | new — every number above |
| `Assets/Scripts/Systems/ExpeditionService.cs` | `RegisterKill`, `KillsRemaining`, per-voyage cap; ctor gains optional data/captains/tuning |
| `Assets/Scripts/Gameplay/Sea/EncounterController.cs` | new — spawn → approach → fight → sink/flee (state machine only, no visuals) |
| `Assets/Scripts/UI/SeaFightUI.cs` | new — the 2D battle stage (see §10) |
| `Assets/Scripts/UI/SeaSceneBoot.cs` | wires both in; `SeaHudUI` never learns a fight exists |
| `Assets/Scripts/Systems/GameBootstrap.cs` | `seaCombatConfig` slot |
| `Assets/Resources/Diller/metinler.txt` | 10 `deniz.*` keys × 11 languages |
| `Assets/Scripts/Tests/EditMode/SeaCombatTests.cs` | new — 28 tests |

### What to test in game

1. **Start a voyage, let it sail, press GEMİYLE ÇIK.** Within ~6 seconds a threat should slide
   alongside — its name plate top right, hull bar full.
2. **Watch a tier-0 fight without touching anything.** It must end BATTI with a `+N harita · +M
   hurda` line, and the charts must show up on the KAPTANLAR screen afterwards.
3. **Tap the three buttons.** BORDA visibly bites the hull bar; SİPER slows the blue CESARET drain;
   KANCA is felt as the fight simply lasting longer. Each greys under a draining veil.
4. **Let one escape** (a beast on a higher tier): KAÇTI, no loot, and — the point — the voyage
   itself completely unbothered.
5. **Count the fights.** A tier-0 crossing pays for 2, then the sea stays quiet. Leave the scene and
   come back: still quiet — the cap belongs to the voyage.
6. **Go ashore mid-fight.** Nothing breaks, nothing is owed; the crossing arrives exactly on its
   own clock, cards as normal.
7. **Near the end of a crossing** no new threat should start (45s guard).
8. **Switch language** — threat names, ability labels and the result lines should all follow.

### Still to do

- **`SeaCombatConfig.asset` is not wired** — drop it on `GameBootstrap`'s `seaCombatConfig` slot;
  identical defaults run until then.
- **Threats are primitives**, same precedent as the ship and the dock.
- **No hit feedback** — no flash, shake, sound or numbers; that is S4 (juice) territory.
- **Every number above is worked, not played.** The tier ladder shape especially
  (watch / one tap / buttons load-bearing) is the thing the in-game pass should judge.

---

## 10. Stage 3 · S2 restaged in 2D — 2026-08-26

The fight is no longer primitive hulls bordalaşan in the 3D world — it is its own **2D side-view
battle card**, the way the reference game stages its fights: sky, scrolling sea, our ship on the
left, the threat sliding in from the right, bars over the hulls they belong to, abilities along the
bottom. Simple to read in half a second; detailed where detail pays.

### The split

- **`EncounterController` is now the state machine only** — phases, timing, firepower derivation,
  the banking call. Everything visual left it. What stayed is exactly what the tests can hold, and
  all 448 still pass untouched: nothing about the fight's truth moved.
- **`SeaFightUI` is the whole picture.** The theater is not the truth: damage is still
  `SeaCombat.Tick`'s continuous maths; cannonballs, flashes and hull-wobbles are a picture OF it,
  timed to feel causal, feeding nothing back. The one deliberate exception: an ability tap draws its
  volley only if the controller said yes.

### The sprite set — `Tools/ui/deniz_savas_seti.py`

Ten pieces into `Assets/Resources/UI/Sea/`, drawn with PIL in the kit's own language (thick navy
outline, vertical gradient, top sheen): **gemi** (our ship — planked red hull, portholes, cabin,
sail, deck gun), **korsan** (ragged red sail, oar slits, black pennant), **canavar** (two humps,
back spines, yellow slit eye, open jaw), **enkaz** (listing hull, broken mast, hanging sail scrap,
two breaches), plus **gulle · patlama · kalkan · kanca · dalga · bulut**. All vessels are authored
facing right; the threat is mirrored at runtime — one drawing per vessel, not two. `dalga.png` is
horizontally tileable (64px period) and scrolls via `RawImage.uvRect` — its importer wraps Repeat.

### What the stage does

- Two wave strips scroll opposite ways (horizon slow, foreground fast); the foreground strip is
  built **after** the ships, so a sinking hull slips behind it — the cheapest "under the waves"
  there is. Clouds drift; both hulls bob and heel on their own periods.
- Our gun fires a ball ~every second (the picture of continuous damage); BORDA draws a five-ball
  volley; the raider answers every 1.7s, the beast lobs high every 2.6s, **the derelict never fires
  — its menace is zero and the picture must not claim otherwise**.
- Impacts flash and kick the struck hull; a braced hit lands on the wooden **kalkan** at our bow and
  the hull stays still. KANCA flies the hook across on a successful grapple.
- Sunk: down by the bow, rolling, gone behind the foam. Fled: sheers off right and fades. The result
  banner (BATTI! + loot / KAÇTI) holds ~2.4s.
- Cannonballs and flashes are **pooled** — fixed arrays, zero allocation after Build. When the ball
  pool is exhausted the extra ball is dropped silently, which is the correct failure: the loudest
  moment on screen is exactly when one missing ball is invisible.

### Files

| | |
|---|---|
| `Tools/ui/deniz_savas_seti.py` | new — the sprite generator |
| `Assets/Resources/UI/Sea/*.png` | ten sprites |
| `Assets/Scripts/Gameplay/Sea/EncounterController.cs` | rewritten logic-only; `Init()` replaces `Bind(lane, ship)` |
| `Assets/Scripts/UI/SeaFightUI.cs` | rewritten as the stage |
| `Assets/Scripts/UI/SeaSceneBoot.cs` | wiring updated |

### What to test in game (replaces §9's list, points 1–4)

1. Board a sailing ship. Within ~6s the battle card fades in and a vessel slides in from the right —
   correct silhouette per kind (red-sailed raider / green beast / brown listing derelict).
2. Watch a tier-0 fight hands-off: balls arc both ways, hits flash and rock the hulls, the threat
   sinks by the bow behind the foreground waves, BATTI! + loot banner, charts on KAPTANLAR after.
3. BORDA → a five-ball volley and a visible bite out of the hull bar. SİPER → the wooden shield
   appears at our bow and incoming hits land on it, hull steady. KANCA → the hook flies across.
4. A derelict must never shoot back.
5. Points 5–8 of §9 stand unchanged (cap, ashore mid-fight, end-of-crossing guard, language).

Every number and every animation timing is worked, not felt — the in-game pass judges the theater.

---

## 11. Stage 3 · S2 rebuilt as the adventure — 2026-08-26

The combat became the reference game's exploration mode, by request: **energy → search → a
turn-based exchange → gear drops you wear**. 449 EditMode tests pass; not run in the Editor and not
played — the in-game pass is the user's.

### The reversal, recorded

§8 recorded "no stamina — berths and hold-fill time already limit us" as a deliberate difference
from the reference game. **Overruled by the user.** Energy is now the governor: one search, one
point, a pool of 10 refilling one per 5 minutes on the wall clock (so it comes back while the app
is shut, like every timer here). It replaced the per-crossing kill cap — the cap paced spawns
nobody asked for; energy paces the button the player presses. A pre-feature save starts with a FULL
pool: the first thing the feature shows a returning player must not be a wait.

### The loop

The 2D card is on from the moment the player boards — the sea IS the adventure screen. **DÜŞMAN
ARA (1 ⚡)** sweeps for ~a second, the find slides in, and the exchange begins: **our shot, then
theirs, one ball at a time**, each side's damage landing at the moment its ball lands
(`EncounterController.TurnStep` — the controller applies `SeaCombat.PlayerShot` /
`EnemyShotLands` at ball-arrival, so the bars and the picture agree to the frame). Enemy hull
empty: **sunk**, loot. Our nerve empty: **driven off** — the energy is gone and nothing else is.

Abilities re-shaped for turns: **BORDA** arms the next shot ×2.2 (drawn as a tight trio of balls),
**SİPER** softens the next incoming to 35% (the shield takes the flash), **KANCA** makes the
enemy's next turn simply not happen. Cooldowns count TURNS — a turn fight has no honest seconds.

### The gear

Every win drops **one item** and parks on a loot card: grade (the captain screen's five, same
tints), slot, power, what it beats — **GİYDİR** or **SÖK (+salvage)**. Wearing over an old item
scraps it automatically: one decision per drop, no inventory, nothing earned destroyed. Leaving
with the card open resolves the safe way — strictly better is worn, else scrapped.

| Slot | On | Does |
|---|---|---|
| **TOP** (cannon) | ship | flat damage per shot |
| **ZIRH** (plating) | ship | % off every incoming hit, capped at 60% |
| **DÜRBÜN** (spyglass) | captain | leans the DROP odds toward rarer |
| **TILSIM** (charm) | captain | flat nerve |

Power is baked at drop time (a tuning change never silently re-arms old items), scales with the
route's tier and the grade, and is a **closed loop**: an item's only effects are inside the fights
— it cannot lift income, shorten a route, or touch a card payout.

### The ladder, worked and pinned

**Tier 0 falls to watching · tier 1 falls to the buttons · tier 3 falls only to gear** — with
buttons alone the far reach is still lost, which is the grind the drops exist to feed, and
tier-3 Mythic gear does clear it. All four statements are tests.

### Files

`Core/SeaCombat.cs` rewritten (energy, turn exchange, gear, drops) · `ExpeditionService` gains
energy/gear/drop-roll, loses the kill cap · `SaveData` +`seaEnergy`, stamp, `seaGearGrade/Power[4]`
(no version bump) · `EncounterController` rewritten search-driven · `SeaFightUI` gains the energy
pill, SEARCH, turn-synced balls, the loot card · `SeaCombatConfig` rebuilt · 12 new `deniz.*` keys
× 11 languages · `SeaCombatTests` rewritten, 29 tests.

### What to test in game

1. **Board a sailing ship** — the 2D card is there at once: your ship waiting, the energy pill
   (⚡ 10/10), and DÜŞMAN ARA.
2. **Press search** — one energy leaves, the pill starts a +1 countdown, a find slides in.
3. **Watch the exchange** — strictly one ball at a time, alternating, bars moving only when a ball
   lands. Tier 0 should be a comfortable win.
4. **Win** → BATTI + trickle, then the LOOT CARD: grade-coloured title, NEW vs CURRENT, GİYDİR /
   SÖK. Wear a cannon and the next fight's shots should visibly bite deeper.
5. **Lose on purpose** (search on a tier-2+ crossing bare) → PÜSKÜRTÜLDÜK, no loot, energy spent,
   voyage untouched.
6. **The buttons**: BORDA fires a trio and bites double; SİPER's shield eats the next hit; KANCA
   makes their turn not happen (hook flies, no ball comes).
7. **A derelict never shoots** — the exchange is just your shots landing.
8. **Drain the pool** — the 11th search is refused; wait five minutes, one comes back. Close the
   app over it: the refill continues.
9. **Language switch** — search, slots, card lines, results all follow.

### Still to do

- **No gear screen.** Worn items are visible only on the loot card's CURRENT line. A four-slot
  panel (dock or sea) is the natural next piece.
- **Energy is not yet sellable/watchable** — no ad refill, no gem top-up. Deliberate: pacing first,
  monetization after it is felt (GDD §10's opt-in rule applies when it comes).
- **Every number is worked, not played** — the 5-shot tier-0 exchange, the 5-minute refill, the
  drop weights. The in-game pass judges the feel.

## 12. Stage 3 · S2 grown to the reference's full frame — 2026-08-26

The screenshots settled what "the same combat adventure system" means: not just the exchange, but
the FRAME around it — a persistent character sheet under the fight, a Monster Details card before
every commitment, item drops compared stat by stat against what is worn, and a stat system rich
enough (crit/dodge/stun/regen/steal/poison/combo) that gear is a build, not a number. §11's rebuild
had the loop; this pass gives it the reference's body, mapped onto ships.

### The nine stats

The reference sheet's nine, renamed for a deck and all DERIVED (crew track + captain + gear, baked
per fight, stored nowhere — §21's rule holds structurally, pinned by the Card-has-3-fields test):

| Reference | Ours | Does |
|---|---|---|
| HP | **CESARET** | our staying power — losing still only drives us off (§4: a loss costs energy, nothing else) |
| ATK | **TOP** | damage per ball |
| CRIT | **KRİTİK** | ×2 ball |
| DODGE | **MANEVRA** | ball misses |
| COMBO | **SALVO** | immediate extra ball (one chain per turn) |
| STUN | **SERSEMLETME** | target's next turn does not happen — the same flag KANCA raises |
| REGEN | **ONARIM** | heals a fraction at own turn start |
| STEAL | **YAĞMA** | a landed hit grabs salvage on the spot (closed loop; kept even on a loss) |
| POISON | **YANGIN** | 3-turn burn, 6%/turn of max hull |

Chance stats are capped (Core consts) so no stack turns the far reach into a coin with one face.
Captain ROLES carry their signature to sea: Topçu→KRİTİK, Levazımcı→MANEVRA, Lostromo→ONARIM,
Yazman→YAĞMA, each = roster worth × RoleSecFactor. The Gunner keeps the doubled shot worth on top.

### Items are stat blocks now

Every drop rolls HULL + SHOT split by the slot's nature (cannon shot-heavy, plating hull-heavy) ×
grade, and — RARE AND UP ONLY — one secondary from the slot's pool (cannon: crit/salvo/burn;
plating: dodge/stun/mend; spyglass: crit/plunder/dodge; charm: any), sized by grade. A Common has
none: rarity is a KIND, not just a size. The spyglass's find-luck now rides its grade
(`SpyglassLuck`); its stats fight like everyone else's. `ItemScore` prices an item in the same
weights the panel's GÜÇ uses, so the compare card's +12 is the sheet's +12.

**Save:** four new arrays (`seaGearHull/Shot/Sec/SecAmt`) beside the old pair, no version bump.
A §11-era item (grade set, stats zero) is grown in place by `Normalise` — its one power number
becomes its slot's nature (a cannon's was shot, a plating's protection), no secondary, score
re-derived. Pinned by APreStatItemGrowsStatsWithoutLosingItsGrade.

### Five kinds, each with a face and a warning

KORSAN crits (0.22) · CANAVAR stuns (0.25) · ALEV GEMİSİ burns (0.40) · HAYALET GEMİ dodges
(0.30) and self-mends · ENKAZ still cannot answer. Two new PIL sprites (alev, hayalet) in the
kit's language.

### The flow grew two cards

**DETAILS CARD (Found phase).** Search pays 1⚡ → the find slides alongside → the card holds it:
name, signature chip with its %, GÜÇ theirs·ours, TEHLİKELİ (red, >1.15×) or KOLAY (<0.70×), hull
and shot, the reward row — then SAVAŞ! or VAZGEÇ. **A search buys the SIGHTING**: declining sails
it away, energy spent. OTOMATİK (the reference's Auto) presses the same public buttons a thumb
would — search, confirm, settle each drop strictly-better-wears — until the pool runs dry.

**COMPARE CARD (Loot).** MEVCUT beside YENİ, three rows each (CESARET/TOP/secondary), the NEW side
tinted green/red per row against the worn item, GÜÇ delta on top. GİYDİR auto-scraps the old;
SÖK pays grade salvage.

**THE SHEET.** Landscape split: stage left (x<0.655), panel right — GÜÇ headline, the nine stats,
four slot buttons (grade-tinted frames, star pips, tap → item popup with SÖK to strip for
salvage), the captain line, energy pill, ARA + OTO. Refreshes only when its own GÜÇ probe moves.
Every roll floats as text on the stage: KRİTİK! big and gold, ISKA grey, burn ticks orange, mends
green, SERSEMLEDİ! purple, +hurda teal — the controller narrates through a fixed event ring, the
UI just drains it. Energy pool 10 → 30 (a real session), refill still 5 min/point on the wall
clock.

### The ladder, re-worked and re-pinned (proc-free rolls)

- Tier 0 falls to watching, all five kinds.
- Tier 1 falls to the buttons (win on our 7th shot, 63 nerve left).
- **Tier 2 resists the buttons alone** (266/300 hull down when we break) — the new middle rung;
  without it the drops are decoration.
- Tier 3 falls to a Mythic tier-3 loadout in 4 shots, no buttons.

Live fights sit on both sides of the skeleton: enemy signatures push down, our secondaries up.

### Files

Core/SeaCombat.cs (stats, items, procs, rolls-as-arguments engine) · Systems/ExpeditionService.cs
(item ledger, ShipStats, migration, ScrapWorn) · Save/SaveData.cs (+4 arrays) ·
Gameplay/Sea/EncounterController.cs (Found/Confirm/Decline, event ring, salvo/stun/burn steps,
OTOMATİK) · UI/SeaFightUI.cs (sheet panel, two cards, floats) · UI/SeaHudUI.cs (bar cleared off
the panel) · Data/SeaCombatConfig.cs + asset recreated · Tools/ui/deniz_savas_seti.py (+7 sprites)
· 28 loc rows × 11 languages (verified resolving tr+en) · SeaCombatTests.cs rewritten, 42 tests.
**468/468 EditMode green, console clean.**

### What to test in-game

1. Board → sheet panel on the right immediately: GÜÇ, nine stats, four empty slots, KAPTANSIZ or
   your officer, ⚡30/30.
2. DÜŞMAN ARA → energy drops, enemy slides in, DETAILS CARD: name, signature chip, GÜÇ line.
   VAZGEÇ once — it leaves, energy stays spent.
3. SAVAŞ on a tier 0 → alternating balls, floating damage numbers on each landing.
4. Meet all five: korsan crits (gold KRİTİK! against you), canavar stuns you out of a shot,
   alev sets you burning (orange ticks at your turn), hayalet dodges (ISKA), enkaz never fires.
5. Win → compare card: rows tinted against the worn item, GÜÇ delta. Wear a cannon → sheet's TOP
   and GÜÇ jump; next fight bites visibly deeper.
6. Tap a worn slot on the sheet → item popup; SÖK strips it for salvage, slot goes empty.
7. Wear a RARE+ item with a secondary → the matching % on the sheet moves; a Common moves none.
8. OTO → searches, confirms, fights, settles drops on its own; stops by itself at ⚡0.
9. Lose on tier 2 bare → PÜSKÜRTÜLDÜK; any YAĞMA salvage grabbed mid-fight is kept.
10. Language switch → cards, chips, floats, stat labels all follow.

### Still to do

- **OTO pacing and the whole balance surface are worked, not played** — proc rates, burn bite,
  the 30-pool, the tier-2 wall. The in-game pass judges the feel.
- **Energy refill monetization still deferred** (ad/gem top-up) — pacing first.
- **The details card's reward row is generic** (TEÇHİZAT · HARİTA · HURDA) — per-kind loot
  preview art (the reference's chest icons) is a polish pass.
- Enemy sprites for alev/hayalet came from the PIL kit sight-unseen in motion — judge them on
  the water.
