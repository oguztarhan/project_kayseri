# Ayarlar — destek, topluluk, künye (integration pack §15)

**Date:** 2026-09-04 · **Status:** code complete, compiles clean, **797 EditMode tests green**, seen
on screen in portrait and landscape.
**Outstanding:** two Inspector fields to fill in (§6), and one product decision that was deliberately
left unmade (§4).

The settings screen already had the whole of §15's first half — music, effects, vibration, the
language picker, rate, the privacy policy, the GDPR ad-preferences row and restore-purchases. What it
had none of was the half a support desk needs: a way to be reached, a place the players gather, a
number that names one save, and a build number to quote. That is what this adds.

No save version bump. One field was appended to the save; see §3.

---

## 1. What changed for the player

| | before | after |
|---|---|---|
| Which build am I on | nowhere in the game | a **künye strip** under the card: `v1.4.2 · 8f3c1a02` |
| Reaching us | nothing | **BİZE ULAŞIN**, opening a mail already filled in with the diagnostics |
| Community | nothing | one row per **approved** link, hidden until one is authored |
| Who am I | nothing | **OYUNCU NUMARASI** — twelve symbols, one tap to the clipboard |
| Redeeming a code | nothing | still nothing, on purpose — §4 |

**Where it lives.** The artist's card is 976×1782 in a 1080×2340 design space and its last row ends 98
units from the bottom edge; there are 128 units under the whole card and nothing else. A seventh row
does not fit and the card cannot grow without leaving the screen. So the new controls are a **second
page**, exactly the way the language picker is a second page, reached from a strip that fits in the
band under the card. The reference game puts its version and its id in the same place.

**The strip is the whole button.** 112 units is about 40 dp — small for a tap target on its own, which
is why the version text and the DESTEK caption sit *inside* one 976-unit-wide button rather than
beside a small one.

**The page inherits the settings screen's art.** It takes the same `LanguageMenuUI.Skin` the language
picker takes, handed over by `SettingsUI`, so it needed no new prefab wiring at all and the two pages
cannot drift apart when a sprite is swapped. Its panel is 1782 tall — the settings card's own height,
not the language picker's 1520, which no longer covers the card it opens over.

**The support mail arrives filled in:**

```
-----
id: K7QM-9F3D-XB2R
app: 1.4.2 (8f3c1a02)
platform: Android SM-G991B ...
device: Pixel 7a
lang: tr
save: v7
-----

Sorununuzu buraya yazın:
```

The block is English whatever the game's language is, because the person reading it works here. Only
the last line is translated, and it is last because mail apps open with the cursor at the end of the
body — the player is already typing where they were asked to.

---

## 2. Code map

| File | What it holds |
|---|---|
| `Assets/Scripts/Core/PlayerId.cs` | **new** — the id's alphabet, shape and validity. Pure; the entropy is a `Guid` argument. |
| `Assets/Scripts/Core/SupportTicket.cs` | **new** — the diagnostics body, the subject, and the `mailto:` with its address check. Pure. |
| `Assets/Scripts/Systems/PlayerIdentity.cs` | **new** — mint-and-persist, the version line, the clipboard. A static helper on the `StorePage` precedent, not a service. |
| `Assets/Scripts/UI/SupportMenuUI.cs` | **new** — the page: contact row, link rows, id row, version line. |
| `Assets/Scripts/UI/SettingsUI.cs` | the künye strip, `supportEmail`, `communityLinks`, and closing the page with the window. |
| `Assets/Scripts/Systems/Save/SaveData.cs` | `playerId`, appended. |
| `Assets/Scripts/Systems/Save/SaveMigration.cs` | keeps it across a wipe. |
| `Assets/Resources/Diller/metinler.txt` | nine new keys, and fifteen old rows finished — §3. |

**Analytics.** `settings_support_opened`, `settings_contact_opened`, `settings_community_opened`
(carrying `link`), `settings_id_copied`. None of this surface was instrumented before.

**Localisation.** New: `ayarlar.destek`, `ayarlar.bize_ulasin`, `ayarlar.git`, `ayarlar.katil`,
`ayarlar.oyuncu_no`, `ayarlar.kopyala`, `ayarlar.kopyalandi`, `ayarlar.destek_konu`,
`ayarlar.destek_mesaj` — all twelve columns.

**One dev-only move.** The Editor/Development-Build test strips are anchored to the bottom of the
window, which is where the künye strip now is. They were shifted up 130 units. Nothing about them
otherwise changed, and none of it compiles into a release build.

---

## 3. The rules that are easy to get wrong

**The id reaches the disk before it reaches the screen.** `PlayerIdentity.Ensure` mints and calls
`SaveService.Save` in the same breath, and only then returns the string the row is about to show and
the clipboard is about to hold. An id that got into a support inbox but never got into the save file
would come back as a different one on the next launch, and the ticket would be unmatchable. This is
the rule the contract claim already runs under, applied to the one other thing the player takes out of
the game with them.

**Minted on demand, not at boot.** Nothing needs the id until a screen shows it, and minting at boot
would mean writing a save file for a player who has not yet tapped anything.

**The id survives `SaveMigration.Reset`.** A wipe throws away progress; identity is neither progress
nor a purchase, and a mail sent the day before a reset still has to name the same device the day
after. It is the only field in the keep-list that was not bought with money.

**The alphabet has no look-alikes.** No 0/O, no 1/I/L, no U. Thirty symbols, twelve of them, in three
groups of four. A support desk that has to ask "was that a one or an ell?" has already lost the
exchange, and the id exists to save exactly that exchange.

**An id we did not mint is replaced, not repaired.** `IsValid` is strict about the separators as well
as the symbols. A player who has never quoted their id loses nothing by getting a new one; a player
who has quoted it was shown a valid one, and a valid one still passes.

**A `mailto:` is refused rather than half-built.** `SupportTicket.Mailto` escapes the subject and the
body but *checks* the address, because escaping the address would break the `@` and unescaping it back
would put us in the business of ruling on characters. One plain address or nothing: no display name,
no second recipient, and above all no newline — a newline inside an address is how a `mailto` is made
to carry headers nobody typed. The field is authored in the Inspector today, but a refusal costs
nothing and the field will outlive the reason it is safe.

**An unconfigured row is not built at all.** No support address, no contact row. No URL on a link, no
link row. A button that does nothing is worse than an absent one — the same rule the ad-preferences
row already follows outside the GDPR region.

**One field is appended to the save and the version stays at 7.** `SaveMigration.NeedsReset` is an
equality test, so a bump deletes every live save on every device. An empty `playerId` is a player who
has not opened the page yet, which is every save that exists.

**The string table is positional and fails silently.** A row one column short does not raise
anything — the languages past the gap never see that key, and the screen shows the raw key, on a
device, in a language nobody here reads. Fifteen rows were in exactly that state: `etkinlik.*` and
`senlik.*`, Turkish and English only, nine languages showing `senlik.hepsini_al` on a button. They are
filled in, and `LocalizationTableTests` now asserts the shape of every row rather than the shape of
the rows somebody remembered.

---

## 4. Decisions the product owner still owns

1. **Redemption codes are not implemented, and should not be implemented client-side.** The reference
   game has a code entry point; a client that reads a code and grants a reward is a client that grants
   any reward to anyone who reads the binary. It needs a server that owns the code list, a per-account
   single-use record, replay and rate limiting, and a signed response the client verifies — none of
   which exists here, and none of which should be faked. There is no half-version worth shipping: the
   brief itself rules out an arbitrary reward field, and this follows it.
2. **The support address and the community links are empty until you fill them in.** They are
   Inspector fields precisely so a link nobody approved cannot reach a build.
3. **The reference pays gems for following a community link.** Not copied: a reward for leaving the
   app is a reward that can be farmed by tapping and coming back, and there is nothing on the device
   that can tell whether the player actually joined anything.

**Also found, not fixed:** `AccessibilityConfig` — colorblind mode, text scale, reduce-motion — is
authored, registered in `GameBootstrap`, and read by **nothing**. `TextScale` and `ReduceMotion` have
no consumer anywhere in the project. That is a real §15 gap, but it is a pass over every screen in the
game rather than a settings-page change, so it is named here rather than half-done.

---

## 5. Verified

- **Compiles clean.** All six assemblies rebuilt from scratch: 0 errors. The Editor console carries no
  error and exactly the warnings it carried before — none of them in a file this work created or
  touched, checked by name.
- **797 EditMode tests, 797 passed, 0 failed**, through the Unity Test Runner. 55 of them are new.
- **On screen, in play mode, against the real save**, at 1080×2340 portrait *and* in the landscape
  Game view: the künye strip under the card reading `v1.0 · SUPPORT`; the page with CONTACT US /
  Discord / Facebook / PLAYER ID and its id; COPY pressed for real — the clipboard held
  `W5JA-E4H4-6FGZ` and the pill read COPIED (the tester's own clipboard was saved and put back).
- **The id survived a restart.** First session minted it and `save.dat` changed on disk; the second
  session loaded the same id back. `SaveService.Suspended` was false, so the write was a real one.
- **The mail was built but not sent.** `mailto:destek@example.com?subject=Support%20request%20%5BW5JA…`
  with the whole diagnostics block escaped, and `Destek <destek@example.com>` refused to the empty
  string, both read off a live build.
- **In landscape the strip folds with the card.** `LetterboxRoot` moved it from -2218 to -1490 along
  with the card it belongs to, which is what parenting it to the letterboxed sheet was for. In
  portrait it stays exactly where it was authored.
- **The save was backed up before play and restored byte-for-byte after**, SHA-256 checked, and the
  temporary portrait entry added to the Game view size list was removed again.

**Two defects the screen found that nothing else would have:**

1. **The id was invisible.** Right text, right colour, enabled, on screen — and not drawn. TMP with
   `Ellipsis` overflow does not clip a line one unit too tall for its box, it drops the line entirely,
   and 40pt in this font needs more than the 56 units it had. Now 36pt in 64.
2. **The version read `v1.0 · 00000000`.** `Application.buildGUID` returns thirty-two zeros rather
   than an empty string when there is no build to name. A build id nobody can look up invites a
   support desk to quote it back; an all-zero guid now reads as no build id at all, and
   `PlayerIdentityTests.AnUnbuiltRunNamesNoBuild` holds that.

Both were fixed and the suite re-run green.

**One thing to know about the string table.** Another session writing `metinler.txt` at the same time
as this work dropped all nine new rows once, silently — the localisation tests were what caught it.
They were put back, and the 23 rows that session had added Turkish-and-English-only (`depo.*`,
`urun.*`, from §03) were filled in the other nine languages at the same time, because
`EveryRowFillsEveryColumn` does not care whose row is short.

## 6. Needs the Unity Editor

1. **Two fields on `UI_Ayarlar` → `SettingsUI`.** `supportEmail` — one plain address; anything else is
   refused and the row hides. `communityLinks` — a name and a URL per row (the name is not translated;
   Discord is Discord in every language). Both empty is a valid, shipping state: the page still shows
   the id and the build.
2. **Nothing else.** Every control is built in code, the suite is green and both orientations have
   been looked at. The one check left for a real handset is the longest captions — German
   `KONTAKTIERE UNS` on the contact row, Vietnamese `ĐÃ SAO CHÉP` on the copy pill — though both
   labels auto-size down to 26 before they can clip.
