# Depo (Equipment, crafting and inventory) — keeping things is a third answer

**Date:** 2026-09-04 · **Status:** code complete, all six assemblies compile clean, 95 EditMode
tests green (run outside the Editor — see §4).
**Outstanding:** nothing blocking. Nothing on screen has been looked at; the Editor was closed for
this work (§5).

Gear used to have exactly two answers and nowhere to put anything. A craft or a sea drop was worn
on the spot or fed back for hurda, which is a fine rule inside a fight — the card is in the way of
the next fight — but it made a Legendary charm rolled before the charm slot is worth filling into a
coin toss. There is now a **depo**: a shelf of twenty, and the production chain it sits beside is
readable for the first time.

C# names are unchanged where saves address them. The save version is **still 7**; two fields were
added without a bump, see §3.

---

## 1. What changed for the player

| | before | after |
|---|---|---|
| A finished craft | wear it or scrap it | wear it, scrap it, or **keep it** |
| Gear you are not wearing | did not exist | a **20-slot depo**, five across by four down |
| Taking a worn item off | scrapped it for hurda | **or park it**, and it costs nothing |
| Trying a second cannon | scrap the one you had | **swap** — both survive, nothing is paid |
| Clearing the shelf | — | **PARÇALA**, with the hurda and the XP on the button |
| What the ship is wearing | four cards inside the sea screen | a row of four, with the POWER above it |
| What the islands make | nothing said so anywhere | a **catalogue**: 8 ores, 10 refined goods, and what each one needs |
| Which shelved item is worth wearing | — | a ▲ on every card that beats what is worn |

**The two tabs.** DONANIM is the four worn slots, the depo grid and the actions; KATALOG is the
chain. They are not merged, and that is deliberate: ore and refined goods flow through `Refining`
in tonnes and are sold, while ship gear is four stat blocks and a shelf of kept ones. One grid
holding both would have to lie about at least one of them.

**Where the depo lives.** The workshop's header — a `DEPO n/20` pill beside the title. It is not a
seventh opener in the HUD's bottom row: that row re-centres itself on every attach, so a seventh
button starts pushing the ends of it off a narrow screen. The workshop is also simply where a shelf
of half-decided gear belongs.

**The catalogue's locks are transitive.** A ruby ring is a gold bar and a cut ruby, so it is locked
on Ruby Island alone, and the row names **gold** — the lowest rung still missing, not the last step.
Telling somebody standing on silver to go and buy Diamond Island is not directions.

---

## 2. Code map

| File | What it holds |
|---|---|
| `Assets/Scripts/Core/GearStash.cs` | **new** — the depo's arithmetic. Ids, capacity, the upgrade compare, the PARÇALA total. No clock, no wallet, no dice, no save. |
| `Assets/Scripts/Core/Catalogue.cs` | **new** — 8 ores, 10 recipes, and the transitive lock. Pure, so every lock is a test rather than a play-through. |
| `Assets/Scripts/Systems/ExpeditionService.cs` | the shelf's authority: `Stow`, `StowWorn`, `EquipFromStash`, `ScrapFromStash`, `ScrapAllStash`, `NormaliseStash`, `Commit`. |
| `Assets/Scripts/Systems/CraftingService.cs` | `StowPending` — the third answer on the bench. |
| `Assets/Scripts/UI/InventoryUI.cs` | **new** — the two-tab shell. Built on first open, not in `Awake`. |
| `Assets/Scripts/UI/CraftingUI.cs` | the `DEPO n/20` pill, the DEPOYA pill on the decision card, and the object that hosts the depo. |
| `Assets/Scripts/Core/SeaCombat.cs` · `Data/SeaCombatConfig.cs` | `StashCapacity` (20), Inspector-tunable. |
| `Assets/Scripts/Systems/Save/SaveData.cs` | `gearStash` + `gearStashLastId`, and `GearStashItem`. |

**No new service, and no second authority.** The shelf holds gear, so it belongs to the service
that already owns the worn slots, the salvage a scrap pays and the bench a scrap teaches. A depo
service of its own would be a second owner of the same three fields.

**Analytics.** `gear_stow`, `gear_unequip`, `gear_equip_stash` and `gear_scrap_stash` carry `grade`;
`gear_scrap_all` carries `count`. The gear loop had no instrumentation at all before this.

**Localisation.** 13 `depo.*` keys and 10 `urun.*` product names, **all eleven languages**. The
product names follow the ore names already in the table (`cevher.*`) and the PARÇALA button follows
`atolye.sok`, down to each language's own XP abbreviation — EP, EXP, PD, ОП — so a button reads like
the button beside it. Product names are looked up with `Loc.T` rather than `Loc.Id`: `Loc.Id`
answers from the active language alone and prints the raw id when a cell is blank, while `Loc.T`
falls back to English, and the worst a future gap can do is print GOLD BAR in German.
A native check on the two full sentences (`depo.bos`, `depo.sec`) is still worth having.

**The catalogue is a transcription.** `Assets/Data/Ore`, `Products` and `Recipes` hold the chain as
ScriptableObjects and nothing at runtime reads them — the game runs the eight-rung island ladder in
`WorldIslands`. So `Catalogue` is a table in Core (inputs and refining times included, taken from
those ten recipe assets) rather than a walk over assets a test cannot see. **If a recipe asset is
ever retouched, that table is the other half of the change.** `CatalogueTests` pins every row.

---

## 3. The rules that are easy to get wrong

Each is pinned by a test, and twelve of them were mutation-checked: the fix removed, the named test
confirmed to fail, the fix restored and the suite re-run green.

**A card is tapped by id, never by cell.** The shelf re-orders itself whenever something leaves the
middle of it, so a cell index is where an item happened to be drawn last frame. Every action takes
an id and a miss is refused for nothing — the same lesson `Docs/PORT_BOARD.md` §3 learned on the
contract board.

**The displaced item takes a NEW id.** `EquipFromStash` is a swap: the worn item goes back into the
cell the shelved one came from. If it kept the old id, a second press of the same card would swap
the pair straight back — a double tap would undo a deliberate choice. With a fresh id the second
press lands on nothing.

**Scrapping is idempotent by id.** A stale screen or a double tap on a card that has gone pays 0
and 0. Matching by position instead would pay for whatever slid into the row behind it.

**A swap pays nothing, and parking pays nothing.** `Equip` from a drop still scraps what it
displaces — the sea's one-decision-per-drop rule is untouched. But nothing on the shelf is ever
destroyed to make room for something else, because that is the whole point of having one. `StowWorn`
is the only gear move in the game that pays no hurda and teaches the bench nothing: the player is
not refusing the item, they are parking it.

**The button prints what the press will pay.** `ScrapAllValue` and `ScrapAllStash` both go through
`GearStash.ScrapTotal` over the same grades, so the label cannot quote a number the press does not
honour. `ScrapTotal` also counts only the first `count` entries of the service's reusable buffer —
paying for the stale grades past the end of the shelf would pay for items that are not there.

**A kept item is never in two places.** `StowPending` clears the bench cell **in memory first**, so
the single write inside `Stow` holds both halves of the move. Writing the shelf first and clearing
after would put a save on disk with two copies of one item in it. Every depo move ends in exactly
one `Commit`, and a refusal writes nothing.

**Two fields were added without bumping the save version.** `SaveMigration.NeedsReset` is an
equality test, so a bump deletes every live save on every device. A save from before the depo
arrives with an empty list, which is a player who has kept nothing.

**A shelf row that cannot be read is dropped, not defaulted.** Membership of the list is what makes
an item exist, so grade is stored as `grade + 1` and a 0 is a broken row rather than a Common. A row
with no slot, no grade or no id at all would otherwise draw as a phantom Common nobody earned.

**Missing and duplicate ids are re-stamped, and the sequence is pulled past every stored id first.**
A fresh id has to be past all of them, not past the ones that happened to come first. Two cards a
tap cannot tell apart is the one state the depo must never be left in.

**An over-full depo is left alone.** Capacity is a number in the Inspector and may be lowered.
Trimming the overflow would delete earned items because tuning moved, so the shelf refuses new
items instead and can be drained one card at a time. The grid brings its own count along when that
happens, so the overflow is visible up to thirty cells.

**The catalogue never throws at a screen that asks too early.** A null or short ownership array
reads as "nothing owned" and draws locks. Every product's inputs also sit at a lower entry index
than the product itself, which is what makes the lock recursion terminate; that invariant is
authored data, so it is pinned rather than trusted.

---

## 4. Verified

- **All six assemblies compile clean: 0 errors, 0 new warnings.** Built with `dotnet build` against
  the project's own csproj files (Unity was closed). Every warning that remains is pre-existing
  CS0618/CS0414/CS0649 in files this work never touched — 34 in `Game.UI`, 8 in `Game.Gameplay`,
  2 apiece in `Game.Data`, `Game.Systems` and `Game.Tests.EditMode`, and none in `Game.Core`.
- **95 EditMode tests run and green, 0 failed.** The suites were compiled and executed outside the
  Editor, in a scratch harness that copies the sources byte-for-byte (`diff` confirms) and stands in
  for the three Unity APIs `Game.Systems` actually touches — `Color`, `Debug`, `Application`,
  `JsonUtility`. 25 in `GearStashTests` + `CatalogueTests`, and all 70 in `ExpeditionServiceTests` +
  `CraftingTests`, which is 22 new depo cases beside every case those two suites already had.
- **Twelve faults injected one at a time, twelve caught** by the test named for each: a full shelf
  taking one more item, a stale id scrapping row 0, the displaced item keeping its id, parking an
  item also paying its hurda, a broken row surviving `NormaliseStash`, the id sequence not being
  pulled past the stored ids, PARÇALA emptying the shelf without paying, the bench cell never being
  cleared, the catalogue's lock stopping at its own island, a locked row naming the last step
  instead of the next, free space going negative, and the ▲ comparing grades instead of scores.
- **A depo move really does reach the disk in one write** — asserted through a real `SaveService`
  round-trip rather than a mock: after a stow the file already holds the item, and after a scrap the
  same file holds both the item's absence and the hurda it paid.
- **Not verified: anything on screen.** The Editor was closed for this work, so the depo, its grid,
  the tab switch, the workshop's new pill and the three-pill decision card have not been looked at
  and no run through the Unity Test Runner has happened. §5 is that list.

---

## 5. Needs the Unity Editor

1. **Run the Test Runner.** The three suites pass outside the Editor; the in-engine run is what
   makes that official. `ADepoMoveReachesTheDiskBeforeItIsAcknowledged` writes `depo-test.dat` under
   `Application.persistentDataPath` — it is the only test here that touches a disk, and it never
   goes near the real `save.dat`.
2. **Look at the depo.** Open the workshop, press `DEPO`, and check: the four worn cards and the
   POWER line, the grid's four rows, a tap selecting a card, GİYDİR swapping, SÖK paying,
   PARÇALA emptying, the tabs switching, and the KATALOG rows for a save that owns two islands
   (most rows should read `needs …`). The longest German and Russian words on the tab pills and the
   action strip are the clipping risk — every label is shrink-to-fit, but that has not been seen.
3. **No scene edit is needed, deliberately.** `CraftingUI` finds an authored `InventoryUI` if the
   scene ever gets one and otherwise makes the object itself, handing over its own sprites. If you
   would rather author it, add the component anywhere in `Main.unity` and wire the five sprites;
   the code will find it and leave its art alone.
4. `SeaCombatConfig.asset` picks up `stashCapacity` automatically on load — worth one look in the
   Inspector to confirm the new Depo block reads **20**.
5. **Optional, cosmetic.** The depo pill and the tabs borrow the workshop's own button art. If a
   dedicated depo icon is ever authored, `CraftingUI.BuildDepo`'s pill is where to point at it.
