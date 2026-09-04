# Mağaza, IAP, sandık ve para birimleri (Store) — the audit, and what is actually missing

**Date:** 2026-09-05 · **Status:** audit complete; **F1 (odds disclosure) is built — see §8.**
Everything else below is still plan only. The pack's own brief for system 14 says "Plan only", and
pricing and SKU choices are yours, not mine.
**Method:** read the shipped store end to end (`PremiumStoreUI`, `OfferPopupUI`,
`GooglePlayIAPService`, `BoostService`, `FreeRewardService`, `ForemanService`, `CaptainService`,
`SeasonalIndustryPassService`, `IapTransactionJournal`) and the nine reference frames.
**Headline:** the store is in better shape than the brief assumes. The gaps that matter are **not
missing products** — they are three compliance/observability holes, and one of them blocks a store
submission.

---

## 1. What the reference sells

Five tabs across the bottom of its store, plus one pop-up:

| Tab (frame) | What it sells |
|---|---|
| **Ayrıcalık Paketi** (0006) | "aristokratik hakkı" subscription, a ₺219,99 luxury bundle, and permanent ×2 / ×4 income cards |
| **Kombinasyon hediye paketi** (0009) | free stamina, a beginner bundle on a 23h countdown, a cosmetic+currency bundle, an arena bundle — all with "500% değer" badges |
| **Takvim** (0010) | daily calendar |
| **Hazine göğüsleri** (0011) | one free chest, then four gem-priced chests: 50 / 100 / 300 / 600 |
| **Para ve bilet** (0012, 0013) | free daily gem, seven consumables sold for gems, six gem packs sold for money, six consumables sold for money |
| **Toplam gelir pop-up** (0039) | the two permanent income cards + a rewarded ad, sold from inside a stats screen |

**Two things to notice, because they decide most of what follows.** First, the reference has
**seven** soft consumables (altın, fiziksel güç, rün anahtarı, evcil hayvan zilleri, et parçaları,
mavi kristal, kutsal parça) and sells every one of them for both gems *and* money — that is what a
store looks like when the game has seven currencies. We have three, on purpose. Second, its chest
tab prices randomised pulls in a currency you can buy with cash, and shows an ⓘ per chest — which is
the odds sheet, and is the one part of that tab we are legally obliged to copy.

---

## 2. What we already ship

The store is not a stub. Fourteen live SKUs and four separate purchase surfaces:

| Surface | Where | Sells |
|---|---|---|
| Store, offer cards | `PremiumStoreUI.offers` | `offer_hazine`, `offer_gecevardiyasi`, `offer_madenpatronu` — permanent perks, non-consumable, restorable |
| Store, ELMAS ızgarası | `PremiumStoreUI.items` (`GemPackIAP`) | `gems_80 … gems_12000`, six money→gem packs |
| Store, ALTIN ızgarası | `PremiumStoreUI.items` (`GoldPackGems`) | gems→cash, priced in **minutes of the player's own income** |
| Store, ELMAS İLE AL | `PremiumStoreUI.gemOffers` | gem-priced boosts/shields, cloned from the same template |
| Island pop-up | `OfferPopupUI` | `teklif_kucuk/orta/buyuk`, per-island, priced in income-hours |
| Starter | `offer_baslangic` | once per island, `StarterOfferState` |
| Season pass | `SeasonalIndustryPassService` | `industry_pass_2026_09`, its own restore + unfinished-purchase handler |
| Master chest | `ForemanService.TryOpenChest` | **gem-priced randomised pulls**, 1× and bulk |
| Captain crate | `CaptainService` | charts-priced randomised pulls — charts are earned by sailing and **cannot be bought** |
| Rewarded ads | `FreeRewardService` + `BoostService` | free ×2/5dk, daily charges, UTC-day reset |
| Free master chest | `ForemanService.FreeChestReady` | free, banks at most one |

And the money-handling discipline the brief asks for is already there and is genuinely good:

- **Idempotent by journal.** `IapTransactionJournal` is one shared list in the save; every grant path
  checks it before granting and records it in the same `SaveData` object as the reward.
- **Saved before acknowledgement.** `Grant` → `SavePurchase` → *then* `_store.ConfirmPurchase`. The
  ordering is deliberate and commented; the reward reaches disk before the platform order is closed.
- **Refund-safe on failure.** `FlushUnfinishedPurchases` catches a failed grant, leaves the order
  unconfirmed, and retries on the next boot rather than eating it.
- **No invented prices.** `PriceUnknown = "—"`, and an unpriced card is made non-interactable rather
  than left tappable. The only currency string drawn is one the platform handed us.
- **Restore applies perks only.** `RestoreEntitlements` calls `GrantPermanent`, never the cash/gem
  half — a restore cannot re-mint consumables.

That list is why my recommendation below is mostly "don't build products".

---

## 3. The findings, worst first

### F1 — The master chest is a paid randomised mechanic with no disclosed odds. **This blocks a submission.**

`ForemanService.TryOpenChest` spends **gems**, and gems are sold for real money (`gems_80 …
gems_12000`). That makes the chest a paid loot box under both platforms' rules, and both require the
odds be shown to the player **before** they buy. Nothing in `ForemanRosterUI` or `CaptainRosterUI`
displays a probability — I grepped; the only `%` in either file is an income-effect readout.

The odds themselves already exist as data (`MasterChest.Tuning`: `CardsPerChest`, `DirectedPerChest`,
flat over eight slots; `CaptainCrate.Tuning`: five grade weights plus two pity counters), so this is a
disclosure screen, not a mechanic change. The captain crate is *not* in scope for the requirement —
charts cannot be bought, and `CaptainService`'s own comment says that separation is why charts exist —
but the pity rules are exactly the kind of thing players assume is rigged when it is hidden, so I
would disclose both and get the goodwill for free.

**Scope:** one shared ⓘ panel, fed from the two `Tuning` structs so it cannot drift from the maths.
This is the one item I would build before any new SKU.

### F2 — Zero purchase analytics.

We log 30-odd event names (`ladder_claim`, `contract_accept`, `pass_premium_owned`, …) and **not one
of them is a purchase.** `PremiumStoreUI` and `OfferPopupUI` call `IAnalytics` nowhere;
`GooglePlayIAPService` only writes `Debug.Log`. So today we cannot answer: which card converts, which
card is opened and abandoned, whether the pop-up's escalating pacing is annoying people into
quitting, or what the refund rate is per SKU. The brief names analytics explicitly and it is the
cheapest item here — the interface and the service registration already exist.

**Proposed events** (names only, no PII, no receipt bodies): `store_open`, `store_card_tap` (+sku),
`iap_start` (+sku), `iap_success` (+sku), `iap_fail` (+sku, +reason), `iap_restore`,
`iap_unfinished_retry`, `gem_spend` (+sink), `chest_open` (+count). Prices and currency stay out —
the platform reports revenue and we would only be reporting it wrong.

### F3 — No remote kill switch on any product.

`IRemoteConfig` exists (`LocalRemoteConfigService`, returns the fallback), `GameBootstrap` registers
it, and the store never asks it a single question. A SKU that turns out to be mispriced, broken, or
region-illegal today needs a **full app update** to withdraw — days on iOS.

**Scope:** one `bool` per selling surface, read at `RefreshOffers` time, defaulting to *enabled* so a
config outage cannot shut the store. Note this is only half a kill switch until a real remote-config
backend is chosen — which is a package decision, and therefore yours.

### F4 — A withdrawn SKU produces a permanently stuck order.

`CompleteUnfinishedPurchase` throws `InvalidOperationException` when a returned sku is in neither
`offers`, `items`, nor the pop-up's runtime bindings. `FlushUnfinishedPurchases` catches it correctly
and retries next boot — which is right for a *transient* failure, but for a product we have removed
from the catalogue it means the order is retried forever and never confirmed. On Android an
unconfirmed purchase auto-refunds after three days; on iOS it re-prompts on every launch and never
resolves. Low severity today (nothing has been withdrawn yet), but it is the failure mode F3's kill
switch would create the moment it is used, so the two belong in the same change.

### F5 — Gems have thin sinks for a currency we sell six packs of.

Gem sinks today: the ALTIN cards, the ELMAS İLE AL boosts, master chests, two ship berths, and a
repair skip. That is a reasonable spread, but if you ever want the ₺-heavy gem packs to feel worth
buying, the honest lever is *more things worth spending gems on*, not more gem packs. Flagging it as
an economy observation, not proposing anything.

---

## 4. Products I propose — and the ones I refuse

The brief says "propose only missing, **player-respectful** products". Against the reference's tab
list, here is every candidate and my verdict.

| Reference feature | Verdict | Why |
|---|---|---|
| Odds sheet on chests (ⓘ) | **Build (F1)** | Required. Not a product. |
| Free daily gem / free chest, shown **in the store** (0009, 0012) | **Build** | We already own both — `FreeRewardService` charges and `ForemanService.FreeChestReady`. They are just not surfaced where the player is looking at prices. No new SKU, no new currency, no new save field. This is the player-respectful half of the reference and it is nearly free. |
| Stamina / energy pack (0009) | **Propose, your call** | `seaEnergy` exists and regenerates on a wall clock with **no paid refill at all**. A gem-priced refill is the reference's most defensible product. But it is the first thing we would add that makes waiting worse rather than progress better, so it is a design decision, not an obvious win. |
| Subscription / "aristokratik hakkı" (0006) | **Skip** | Auto-renewing subscriptions need new SDK surface, their own restore and cancellation semantics, a separate App Review pass, and a support burden. Our three non-consumable perk cards (`offer_hazine`, `offer_gecevardiyasi`, `offer_madenpatronu`) already cover what that card actually grants, one-time and restorable. Revisit only if you want recurring revenue as a deliberate business decision. |
| Selling soft consumables for money (0013) | **Skip** | The reference does this because it has seven currencies. Our third currency, charts, is deliberately unbuyable — `CaptainService` says in as many words that charts exist to keep the two rosters out of one wallet. Selling charts would undo the reason they exist. |
| A new currency (bilet / rün / kristal) | **Skip** | The brief forbids it without an economy justification and I have none. Three currencies with clear roles beats five with blurred ones. |
| "500% değer" badges | **Skip** | An unverifiable claim about our own pricing, printed next to a price. Both stores treat that as a compliance surface and I would not want to defend the arithmetic. |
| Escalating countdown pressure on every card | **Skip** | We already have paced, capped pop-ups (`offerShownToday`, `offerShownThisWeek`, `offerDeclineStreak`). The reference's density is the thing the brief tells us not to copy. |
| ×2 / ×4 forever income cards (0006, 0039) | **Already shipped** | `permanentStationSpeedMultiplier`, and it correctly takes `Math.Max` rather than stacking. |
| Bundle with a countdown (0009) | **Already shipped** | Starter offer, per island, with `StarterOfferState`. |
| Selling from inside a stats screen (0039) | **Skip** | `PremiumStoreUI.BuyOffer` is already the one till and `OfferPopupUI` sells through it. Adding a third entry point multiplies the surfaces without adding a product. |

So: **two builds (F1 odds sheet, free-slots surfacing), three fixes (F2 analytics, F3 kill switch,
F4 stuck order), one product for you to rule on (energy refill), everything else skipped.**

---

## 5. The bar any new SKU has to clear

Not new rules — this is the discipline the shipped code already enforces, written down so a new
product is checked against it rather than reasoned about fresh:

1. **Maps to an existing entitlement path.** A grant must be expressible in `OfferBinding`'s fields or
   an existing service call. A product that needs a new grant kind needs a new plan.
2. **Journalled and saved before acknowledgement.** Through `IapTransactionJournal` and `SavePurchase`,
   in that order, before `ConfirmPurchase`.
3. **Restore-correct.** Permanent → non-consumable, listed in `GooglePlayIAPService.NonConsumables`,
   granted through `GrantPermanent` only. Consumable → never re-granted on restore.
4. **Idempotent under redelivery.** Both `CompleteUnfinishedPurchase` and `RestoreEntitlements` must
   resolve the sku, or F4 applies.
5. **Priced by the platform.** Never a hardcoded currency string; `PriceFor` is the only authority.
6. **Non-interactable until priced.** Existing `Sellable` / `Grey` behaviour.
7. **Kill-switchable** once F3 lands.
8. **Instrumented** once F2 lands.
9. **Localized** — a row in `metinler.txt` across all 11 languages.
10. **No new currency, no new package, no new SDK** without your explicit approval.

---

## 6. Decisions I need from you

Nothing below is a judgement call I should make alone.

- **D1 — Energy refill: yes or no?** A gem-priced `seaEnergy` refill is the reference's most
  defensible missing product, and also the first one that profits from making waiting worse. My
  lean: **no**, until expeditions are a bottleneck players actually complain about.
- **D2 — Odds sheet scope.** Master chest only (the legal minimum), or master chest + captain crate
  including the pity rules (my lean: **both** — it costs one extra panel and buys trust).
- **D3 — Prices and SKU ids** for anything you approve. Yours entirely; I will not invent a price
  point, and the code will not print one the platform did not supply.
- **D4 — Remote config backend.** F3 is only half a kill switch without one, and picking it is a
  package install, which needs your approval. Firebase Remote Config and Unity Remote Config are the
  two obvious candidates; I am not adding either unprompted.
- **D5 — Order to build in.** My lean: **F1 → F2 → F3+F4 → free-slot surfacing**, with F1 first
  because it is the only one that blocks a store submission.

---

## 7. What I did not verify

I read the code; I did not run the store. Unity MCP has been unreachable all session
(ConnectionRefused), so nothing here was checked against a running build or a real Play/StoreKit
sandbox. Specifically unverified: that all fourteen SKUs are actually live in Play Console and App
Store Connect with matching ids, and that `industry_pass_2026_09`'s season id still matches the
current pass. Both are worth checking before the next submission and neither is something I can see
from here.

---

## 8. As built — F1, the odds sheet

**Date:** 2026-09-05. Decision taken: **disclose both crates**, per §6 D2.

### What the player sees

An ⓘ badge in the top-right corner of the chest shelf (masters) and the crate card (captains). It
opens a sheet over the screen, dismissed by the scrim or by KAPAT.

| Master chest | Captain crate |
|---|---|
| Sandık başına kart — **3** | SIRADAN — **60%** |
| En geride kalana — **1** | NADİR — **26%** |
| Rastgele çekilen — **2** | DESTANSI — **10.5%** |
| Her usta için — **12.5%** | EFSANEVİ — **3%** |
| | MİTİK — **0.5%** |

Under the table, in words: for the chest, that the directed card is not chance and always goes to the
master with the fewest cards; for the crate, both guarantees (Epic within 10 pulls, Legendary within
70) and the soft-pity ramp (from pull 45, +1% Legendary per pull). Both end with "every pull is
independent".

### The one design decision worth knowing

**The directed card is listed apart from the rolled ones and never folded into a percentage.** A
master chest holds three cards, one of which is aimed at whoever is furthest behind — it is not
chance at all. Averaging it into a per-master figure would have printed a single tidy number that
overstates the randomness by a third and understates the floor. Two rolls at 12.5% plus one aimed
card is what actually happens, so it is what the sheet says.

### Why it cannot go stale

Every number is derived at draw time from the same `Tuning` struct the roll reads, through
`CaptainCrate.WeightOf`'s own normalisation — nothing is typed in. A grade with nobody in the roster
is dropped from the table rather than printed at 0%, because `WeightOf` already returns zero for it
and the roll can never produce it.

`OddsTests` pins this the only way that means anything: it samples the **real** `RollGrade` and
`RollSlot` 200,000 times each (stratified, so the frequency is exact to one step) and asserts the
printed figure equals the measured one — with an owed guarantee, and with the soft-pity ramp warm, as
well as cold. If a balance pass moves a weight and this file is not updated, those tests go red
rather than the game quietly stating a false probability.

### Files

- `Assets/Scripts/Core/Odds.cs` — new. Derivation only; no UI, no randomness.
- `Assets/Scripts/UI/OddsSheetUI.cs` — new. Shared sheet, a plain class like `RosterInspectPanel`.
- `Assets/Scripts/UI/ForemanRosterUI.cs` — ⓘ on the chest shelf; title narrowed to make room.
- `Assets/Scripts/UI/CaptainRosterUI.cs` — ⓘ on the crate card; same.
- `Tools/ui/oran_bilgi.py` → `Assets/Resources/UI/Buttons/bilgi.png` — the badge. Imports as a Sprite
  via the existing `UiSpriteImporter`, which is why it does not need Inspector wiring.
- `Assets/Resources/Diller/metinler.txt` — nine `oran.*` rows × 11 languages. Reuses `kaptan.derece.*`
  for the grade names and `lig.kapat` for the close button.
- `Assets/Scripts/Tests/EditMode/OddsTests.cs` — 7 tests, all green.
- `Assets/Scripts/Tests/EditMode/OddsSheetUiSmokeTests.cs` — 3 tests, **not runnable outside the
  Editor**; see below.

### Verified, and not

All six assemblies compile: 0 errors, 26 Game.UI warnings — the same 26 that were there before, none
of them mine. Suite: **823 passing** (was 816; +7 from `OddsTests`), 32 failing — the same known set,
now 26 engine-native `ECall` failures instead of 23. The three new ones are the sheet's smoke tests:
**no UI smoke test in this project can execute outside the Unity Editor**, which is a pre-existing
limitation of the offline runner, not a fault in the tests. They will run in the Test Runner.

**Not verified: I have not seen the sheet rendered.** Unity MCP has been unreachable all session, so
the layout is reasoned from the two roster screens' own rules rather than looked at.
