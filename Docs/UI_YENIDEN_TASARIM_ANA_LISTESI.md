# UI Yeniden Tasarım Ana Listesi

**Proje:** Kayseri / Ore Empire  
**Amaç:** Oyundaki bütün UI ekranlarını ve UI destek parçalarını tek bir görsel sistem altında yeniden tasarlamak.  
**Denetim tarihi:** 2026-09-11  
**Ana hedef:** Dikey Android oyun deneyimi; portrait-first.

Bu dosya yalnızca tasarım ve üretim listesidir. Oyun mekaniğini, ekonomi kurallarını veya ekran akışını değiştirmez.

---

## 1. MCP ile doğrulanan mevcut durum

- Unity sürümü: `6000.4.9f1`.
- Platform hedefi: Android.
- Aktif sahne: `Assets/Scenes/Main.unity`.
- MCP Game görünümü: `486 x 1000` — dikey/portrait.
- Ana UI CanvasScaler referansı: `1080 x 2340`.
- `Assets/Scripts/UI/` altında 77 C# dosyası var.
- `Assets/Prefabs/UI/` altında 12 UI prefabı var.
- Main sahnesinde 18 root nesne var.
- Main sahnesinde 10 ana UI canvası var: `UI_HUD`, `UI_Ayarlar`, `UI_Magaza`, `UI_GunlukOdul`, `UI_HosGeldin`, `UI_Kontrat`, `UI_Reklam`, `UI_Harita`, `UI_IstasyonEkrani`, `UI_Teklif`.
- `UI_Sistemler` üzerinde 12 runtime UI sistemi var: foreman, goals, chapter, captain, live events, crafting, foundry festival, cannon production, mining gear, card collection, pet roster ve ilişkili sistemler.
- Unity MCP UI Toolkit taramasında UXML/USS asseti bulunmadı. Mevcut yapı Unity Canvas/uGUI ve kodla oluşturulan runtime UI ağırlıklı.
- Mevcut ekranlarda iki farklı dil görülüyor: eski beyaz/mavi/gri panel ailesi ve deniz ekranının koyu lacivert/turkuaz/turuncu ailesi. Yeniden tasarımın ana amacı bu ayrılığı kaldırmak.

### Main sahnesindeki UI sahiplik haritası

| Root | Mevcut sorumluluk |
|---|---|
| `UI_HUD` | Ana ada HUD’ı, güvenli alan, tutorial host, kaynaklar ve kalıcı butonlar |
| `UI_Ayarlar` | Ayarlar ve rating prompt |
| `UI_Magaza` | Premium mağaza ve satın alma feedback’i |
| `UI_GunlukOdul` | Günlük ödül takvimi |
| `UI_HosGeldin` | Offline kazanç / welcome-back |
| `UI_Kontrat` | Liman kontratları ve teklifleri |
| `UI_Reklam` | Ödüllü reklam ödülleri |
| `UI_Harita` | Ada/world map ve bildirim deep-link’i |
| `UI_IstasyonEkrani` | İstasyon upgrade ekranı ve model preview |
| `UI_Teklif` | Ada bazlı teklif popup’ı |
| `UI_Sistemler` | Runtime üretilen progression, roster, event ve crafting ekranları |

---

## 2. Önce hazırlanacak ortak görsel sistem

Her ekran aşağıdaki ortak kit üzerinden kurulacak. Önce bu kit tamamlanmadan tek tek ekran illüstrasyonuna geçilmemeli.

### 2.1 Ortak stil yönü

- Stil: premium, renkli, stilize denizcilik + endüstriyel yönetim oyunu.
- Ana iç yüzey: koyu okyanus laciverti.
- Ana çerçeve: parlak cyan/turkuaz.
- Birincil aksiyon: mercan/turuncu.
- İkincil aksiyon: mavi/turkuaz.
- Metin ve ikon vurgusu: sıcak krem.
- Para ve değerli ödül: kontrollü altın.
- Tehlike: kırmızı; tamamlandı/hazır: yeşil; kilitli/devre dışı: muted steel.
- Kalın lacivert dış kontur, yumuşak gölge, hafif bevel ve kontrollü parlama kullanılacak.
- Deniz, dalga, halat, pusula, çapa, rota, borda ve porthole detayları yalnızca anlamı desteklediğinde kullanılacak.
- Düz kurumsal mavi dikdörtgenlerden, gereksiz parıltıdan ve casino benzeri animasyondan kaçınılacak.

### 2.2 Tasarlanacak temel assetler

- [ ] Büyük panel: koyu iç yüzey + cyan çerçeve.
- [ ] Küçük panel/kart ve yatay liste satırı.
- [ ] Başlık plakası: mercan/turuncu başlık + çapa/arma yuvası.
- [ ] Birincil, ikincil, pasif, kilitli ve reklam aksiyon butonları.
- [ ] Kapat, geri, ileri ve bilgi butonları.
- [ ] Kaynak pill’i: altın, elmas, enerji, chart, mining point, salvage, pet essence.
- [ ] Progress bar, level bar, timer chip ve countdown pill’i.
- [ ] Route tab: seçili, açık, kilitli ve tamamlandı halleri.
- [ ] Equipment slot: boş, dolu, seçili, kilitli, disabled ve upgrade-ready halleri.
- [ ] Stat kartı, rarity frame, yıldız, duplicate progress ve badge.
- [ ] Notification badge ve tekil signal-flag/bell bildirimi.
- [ ] Reward chest, pack, card, pet ve equipment reveal çerçeveleri.
- [ ] Dimmer/karartma ve modal arka planı.
- [ ] Empty, loading, insufficient resource, expired, maxed, claimed ve error durumları.

### 2.3 Renk hedefleri

| Token | Kullanım | Hedef |
|---|---|---|
| `OceanDeep` | Ana panel içi | `#071C36` – `#123F66` |
| `OceanMid` | İkincil yüzey/seçili trim | `#176788` – `#168BA3` |
| `AquaTrim` | Çerçeve/parlama | `#16D4D8` – `#67F4EA` |
| `CoralAction` | Ana aksiyon/dikkat | `#FF5A3D` – `#FF8A43` |
| `WarmCream` | Kenar/ikon/metin vurgusu | `#FFF0C7` – `#FFF9E6` |
| `NauticalGold` | Para/premium ödül | `#F6C84A` – `#FFDF76` |
| `AlertRed` | Tehlike/kayıp | `#F34243` |
| `SuccessGreen` | Hazır/tamam/uygun | `#7BE58B` |
| `MutedSteel` | Kilitli/pasif | `#70839B` |
| `NavyOutline` | Kontur/gölge/metin gölgesi | `#03112C` |

### 2.4 Dikey layout kuralları

- Referans kompozisyon: `1080 x 2340` portrait.
- Notch, yuvarlak köşe ve gesture bar için her ekranda safe-area wrapper kullanılacak.
- Ana oyun dünyası mümkün olduğunca görünür kalacak; modal ekranlarda bile gereksiz alan kapatılmayacak.
- Dikey scroll olan ekranlarda üst wallet ve başlık sabit, içerik kaydırılabilir olacak.
- Minimum dokunma alanı küçük ikonlarda bile rahat okunacak şekilde hazırlanacak.
- Çok sayıda kalıcı buton yerine `More`/ikincil menü kullanılacak; ana akışın odağı korunacak.
- Landscape varyantı bu çalışmanın hedefi değil; yalnızca mevcut ekran bunu açıkça destekliyorsa ayrıca üretilecek.
- Metinler artın içine gömülmeyecek; bütün başlık, fiyat, sayaç ve açıklamalar runtime localization socket’i olarak kalacak.

---

## 3. Grup A — giriş, HUD, navigasyon ve dünya üstü UI

Bu grup oyuncunun en sık gördüğü alan olduğu için ilk görsel uyum burada sağlanacak.

### A01 — Açılış / loading ekranı

**Kaynak:** `Bootstrap.unity`, `SeaSceneBoot`, `SceneCurtain`, `LetterboxRoot`, açılış artları.

- [ ] Dikey koyu okyanus arka planı ve sakin dalga/ışık katmanı.
- [ ] Logo için merkezde güçlü ama nefes alan alan.
- [ ] Cyan çerçeveli progress bar ve küçük altın ilerleme vurgusu.
- [ ] Loading, transition ve hata durumlarını aynı görsel ailede üret.
- [ ] Kontrol butonu ekleme; ekranın görevi güven vermek ve geçişi anlatmak.

### A02 — Ana ada HUD’ı

**Kaynak:** `UI_HUD.prefab`, `HudUI`, `HudJuice`, `CurrencyText`, `ObjectiveBannerUI`.

- [ ] Üstte altın, elmas ve gelir rate için ortak kaynak pill’leri.
- [ ] Güvenli alan içinde dikey kenar/alt navigasyon düzeni.
- [ ] Store, offer, daily, map, settings, contract, event, roster ve upgrade girişlerini tek dilde üret.
- [ ] Upgrade için en belirgin mercan/turuncu ana aksiyon.
- [ ] Objective banner: hedef ikonu, kısa başlık, progress bar ve hazır durumu.
- [ ] Normal, pressed, disabled, ready ve notification badge durumlarını tasarla.
- [ ] Ada görüntüsünü kapatmayacak boşluk ve katman sırasını koru.

### A03 — Portrait shipyard HUD

**Kaynak:** `Shipyard.unity`, `WalletUI`, `ShipyardSafeArea`, `ShipyardMapView`, `PortraitShipyardCamera`.

- [ ] İnce üst wallet şeridi.
- [ ] Dikey kaydırma sırasında production path’i kapatmayan bağlamsal buton ray’i.
- [ ] Map, boost, contract ve upgrade için porthole ikon butonları.
- [ ] İnşaat/hazır/bottleneck işaretlerini signal flag veya küçük badge olarak göster.
- [ ] Notch, gesture bar ve kısa telefonlarda test edilecek portrait layout.
- [ ] Permanent button sayısını azalt; ikincil girişleri gerekirse More menüsüne al.

### A04 — Sea / embarkment / combat HUD

**Kaynak:** `SeaFightUI`, `SeaHudUI`, `SeaKit`, `SeaSceneBoot`.

- [ ] Bu ekranın koyu panel + aqua trim + mercan aksiyon dili ana görsel referans olacak.
- [ ] Rota sekmeleri, sefer ilerlemesi, geri dön ve enerji pill’i.
- [ ] Kaptan portresi, 5 equipment slot’u ve 3 pet slot’u.
- [ ] Oyuncu gemisi, düşman gemisi/yaratığı ve can barları.
- [ ] Search/Fight ana butonu, Auto ikincil butonu ve enerji ekleme.
- [ ] Boş, dolu, kilitli, seçili, warning, victory, loss, loot ve repair durumları.
- [ ] Asset katmanlarını panel, slot, ikon, text socket, state overlay şeklinde ayır.

### A05 — World map / island map

**Kaynak:** `UI_Harita.prefab`, `IslandMapUI`, `MapArchipelago`, `NotificationNavigationUI`.

- [ ] Liste yerine tek adayı odaklayan dikey map sunumu.
- [ ] Ada modeli, ada adı, cevher rengi, emblem ve seçili aura.
- [ ] İleri/geri navigation ve route göstergesi.
- [ ] Ziyaret/satın al/locked aksiyonları.
- [ ] Kilitli ada için sakin navy silhouette + krem kilit + muted route.
- [ ] Harita bildirimini tek badge ile göster; çoklu kırmızı nokta kullanma.

### A06 — Ada, bina ve bağlamsal marker’lar

**Kaynak:** `BuildingSigns`, `IslandBillboard`, `UpgradeReadyMarkers`, `RepairMarkers`, `PortShipMarker`, `PortContractMarker`.

- [ ] Station name board ve island identity billboard.
- [ ] Upgrade-ready, repair-needed, player-ship boarding ve contract-ready marker’ları.
- [ ] Her marker için anlamı metinsiz de anlaşılabilir farklı silhouette.
- [ ] Idle/bobbing, pulse, completed, disabled ve notification durumları.
- [ ] Binaları ve üretim hattını kapatmayacak konumlandırma.
- [ ] Gece/gündüz ışığında okunabilirlik ve koyu arka plan kontrastı.

---

## 4. Grup B — shipyard, istasyon, workshop, ekipman ve roster ekranları

Bu grup oyuncunun karar verdiği ekranlardır. Okunabilirlik ve karşılaştırma, dekorasyondan önce gelir.

### B01 — İstasyon upgrade ekranı

**Kaynak:** `UI_IstasyonEkrani.prefab`, `StationScreenUI`, `StationPreviewStage`.

- [ ] Üstte istasyon/makine modelini gösteren porthole benzeri preview alanı.
- [ ] Altta dikey okunabilir upgrade satırları.
- [ ] İstasyon tab’ları: seçili, açık ve kilitli.
- [ ] Level bar, cost, upgrade button ve expansion state.
- [ ] Model preview’i metin ve butonlarla boğma.

### B02 — Cannon production contextual card

**Kaynak:** `CannonProductionUI`, `CannonProductionWorldBridge`.

- [ ] Shipyard dünyasını kapatmayan küçük navy + aqua card.
- [ ] Cannon ikon/modeli, input kaynak slot’ları, progress, output ve CRAFT/COLLECT.
- [ ] Empty, crafting, ready, blocked ve maxed durumları.
- [ ] Hazır durumda yeşil, maliyette altın, unavailable durumda muted steel.

### B03 — Workshop crafting ekranı

**Kaynak:** `CraftingUI`, `OddsSheetUI`, `RosterInspectPanel`, `RewardRevealUI`.

- [ ] Craft bench: ingredient slot’ları, output preview, cost, duration ve CRAFT.
- [ ] Odds sheet’i kaptan günlüğü/ship manifest görselinde tasarla.
- [ ] Rarity satırları ve yüzdeleri büyük/okunabilir yap.
- [ ] Pending item için açık `Equip / Store / Scrap` seçimleri.
- [ ] Common, rare, epic, legendary, locked, crafting, result ve insufficient state’leri.

### B04 — Inventory / depot / catalogue

**Kaynak:** `InventoryUI`, `GearStash`, `Catalogue`, `Docs/DEPO.md`.

- [ ] İki ana sekme: Donanım ve Katalog.
- [ ] Donanımda 4 worn slot, ekipman grid’i, rarity frame ve POWER özeti.
- [ ] Equip, compare, salvage ve selected durumları.
- [ ] Katalogda cevherden ürüne üretim zinciri, recipe connector ve lock condition.
- [ ] Portrait grid’de küçük spreadsheet metninden kaçın.

### B05 — Mining gear ekranı

**Kaynak:** `MiningGearUI`, `MiningGearService`.

- [ ] 4 mining equipment slot’u; yatay veya 2x2 dikey düzen.
- [ ] Kaptan portrait’i, grade star’ları, bonus ve toplam income bonus’u.
- [ ] CRAFT + mining point maliyeti.
- [ ] Empty, filled, locked, affordable ve cooldown durumları.

### B06 — Island yard upgrade sheet

**Kaynak:** `IslandYardUpgradeUI`, `MarketService`, `YardUpgrade`.

- [ ] Altı upgrade track’i tek bir dockmaster control board olarak düzenle.
- [ ] Her satırda ikon, level, progress/tier, gold cost ve BUY/UPGRADE.
- [ ] Affordable, insufficient, maxed, unavailable/loading ve success state’leri.
- [ ] Portrait modda dünya görünürlüğünü koru.

### B07 — Foreman / master roster

**Kaynak:** `ForemanRosterUI`, `UI_UstaKarti.prefab`, `StationForemen`.

- [ ] Chest shelf ve 15 foreman kartı.
- [ ] Kartta portrait, station identity, rarity, 5 star, bonus, duplicate count ve action.
- [ ] Station gruplarını renk/arma ile ayır; yeni görsel dil oluşturma.
- [ ] Chest-ready, cooldown, reveal, duplicate, maxed ve locked halleri.

### B08 — Captain roster

**Kaynak:** `CaptainRosterUI`, `UI_KaptanKarti.prefab`, `Captains`.

- [ ] Captain crate + chart balance hero alanı.
- [ ] 10 captain için portrait, role, rarity, star, duplicate progress ve sea bonus.
- [ ] Common steel, rare blue, epic violet, legendary gold, mythic coral-magenta rarity hiyerarşisi.
- [ ] Unowned, owned, upgrade-ready, maxed, selected, crate-open ve duplicate state’leri.

### B09 — Pet companion collection

**Kaynak:** `PetRosterUI`, `SeaFightUI` pet slot’ları, `Pets`, `PetChest`.

- [ ] Pet chest ve pearl/essence balance.
- [ ] 6 tür için portrait, species emblem, rarity, fusion progress ve effect.
- [ ] Equip slot dilini Sea combat ekranıyla aynı tut.
- [ ] Locked slot, equipped, fusion-ready, insufficient essence, chest-open ve maxed halleri.

### B10 — Card collection

**Kaynak:** `CardCollectionUI`, `CardCollection`, `CardCollectionCatalogue`.

- [ ] Üstte pack status veya unopened pack hero kartı.
- [ ] Üç set tab’ı ve her sette 8 kartlık 2 kolonlu portrait grid.
- [ ] Card art, rarity frame, star, duplicate progress, locked silhouette ve kalıcı bonus.
- [ ] Unopened, selected, set-complete, duplicate, max-level ve locked state’leri.

### B11 — Roster inspect / detail panel

**Kaynak:** `RosterInspectPanel`.

- [ ] Captain, foreman, pet ve card için tek reusable detail card.
- [ ] Porthole portrait, title strip, emblem, rarity, star progress, stat/effect ve action.
- [ ] Selected için kontrollü coral/gold glow; locked için muted steel + lock.
- [ ] İnsan, hayvan ve kart görselini aynı çerçeve içinde destekle.

---

## 5. Grup C — hedefler, chapter, kontrat, event, league ve pass

Bu grup uzun vadeli ilerlemeyi anlatır. Ortak progress bar, reward ve status dili korunacak.

### C01 — Goals / daily / weekly tasks

**Kaynak:** `GoalsUI`, `GoalService`, `ObjectiveBannerUI`.

- [ ] Daily, weekly ve achievement tab’ları.
- [ ] Her görevde ikon, kısa başlık, progress, reward preview ve CLAIM.
- [ ] Ready reward özeti ve claimable/claimed ayrımı.
- [ ] Empty, in-progress, claimable, claimed ve locked state’leri.

### C02 — Chapter log / island progression

**Kaynak:** `ChapterUI`, `Chapters`, `ChapterService`.

- [ ] 8 ada chapter’ı ve seçili chapter’ın 5 progression beat’i.
- [ ] Ada emblem’leriyle bağlanan rota çizgisi.
- [ ] Story intro, milestone rows, progress bars ve reward icons.
- [ ] Completed için ship-log stamp, current için coral signal, locked için silhouette + lock.

### C03 — Port contracts / orders

**Kaynak:** `UI_Kontrat.prefab`, `ContractUI`, `PortContractMarker`.

- [ ] Harbour notice board görünümü.
- [ ] Üç offer kartında target, quantity, timer, reward, accept/skip.
- [ ] Aktif kontratta büyük progress, reward row ve CLAIM.
- [ ] Horizon countdown, ready, expired ve claimed state’leri.

### C04 — Live events board

**Kaynak:** `LiveEventsUI`, `LiveEventService`, `UI_Sistemler`.

- [ ] Running, upcoming ve completed event bölümleri.
- [ ] Event emblem, status chip, kalan süre, kısa hedef, progress/reward ve OPEN/CLAIM.
- [ ] Foundry, harbour, production ve seasonal temaları aynı chrome ile üret.
- [ ] Empty schedule durumunu kasıtlı ve anlaşılır göster.

### C05 — Foundry Festival

**Kaynak:** `FoundryFestivalUI`, `FoundryFestivalService`.

- [ ] 7 günlük strip, seçili gün ve 3 günlük görev kartı.
- [ ] Alt bölümde reward chest/milestone sırası.
- [ ] Forge/ember/brass vurgusu; ana deniz stilinden kopma.
- [ ] Active, completed, claimable, expired ve locked state’leri.

### C06 — Harbor Festival

**Kaynak:** `HarborFestivalUI`, `HarborFestivalService`.

- [ ] Tasks, Rewards ve Catalogue sekmeleri.
- [ ] Görev progress/claim; katalogda item art, cost, owned ve purchase.
- [ ] Harbor crest, rope, flag ve ship bell detaylarını kontrollü kullan.
- [ ] Portrait telefon için kompakt scroll yapı.

### C07 — Production Sprint

**Kaynak:** `ProductionSprintUI`, `ProductionSprintService`.

- [ ] Stopwatch + wave + cargo/gear sprint badge.
- [ ] Task actions ve personal milestones bölümleri.
- [ ] Current/target değerleri, progress, reward ve CLAIM.
- [ ] Active, completed, claimable, cooldown ve event-ended state’leri.

### C08 — Seasonal Industry Pass

**Kaynak:** `SeasonalIndustryPassUI`, `SeasonalIndustryPassService`.

- [ ] Season crest ve scrollable reward rail.
- [ ] Free/premium lane, tier number, current tier, progress marker ve reward icon.
- [ ] Premium lane’i gold/coral ile zenginleştir ama farklı UI ailesi yapma.
- [ ] Current, claimed, claimable, locked, maxed ve season-ended state’leri.

### C09 — League / leaderboard ladder

**Kaynak:** `LadderUI`, `LadderService`, `Docs/LEADERBOARDS.md`.

- [ ] 1-2-3 podium ve aşağıda okunabilir ranking rows.
- [ ] Player badge, rank, score, reward chest ve countdown.
- [ ] Player row emphasis, synthetic data, empty state ve chest-open detail.
- [ ] Gold/silver/bronze/turquoise/navy ile kontrollü sıralama hiyerarşisi.

---

## 6. Grup D — mağaza, teklifler, reklam, ödül ve wallet

Bu ekranlar oyuncuda güven ve cömertlik hissi oluşturmalı; baskıcı satış dili kullanılmamalı.

### D01 — Premium store

**Kaynak:** `UI_Magaza.prefab`, `PremiumStoreUI`, `StoreCardFx`, `StoreHeroFx`, `StorePurchaseFx`, `StoreNotice`, `OfferCountdown`.

- [ ] Harbour market / captain supply deck ana kompozisyonu.
- [ ] Hero offer, offer cards, cash packs, gem packs ve boost bölümleri.
- [ ] Her kartta art, reward/value, price, owned/purchased ve value badge.
- [ ] Portrait scroll, kısa telefon ve safe-area kontrolü.
- [ ] Countdown’ı sakin ve güvenilir tut; casino benzeri flashing kullanma.

### D02 — Ada contextual offer popup

**Kaynak:** `UI_Teklif.prefab`, `OfferPopupUI`, `StoreHeroFx`, `OfferCountdown`.

- [ ] Koyu scrim + kompakt navy/aqua modal.
- [ ] Ada adı/tier, reward tray, price strip, BUY ve close.
- [ ] Ada temasına göre cargo/ore/wave detayları değişebilir; chrome değişmez.
- [ ] Purchased, claimed, expired ve unavailable state’leri.

### D03 — Rewarded-ad rewards panel

**Kaynak:** `UI_Reklam.prefab`, `AdRewardUI`, `FreeRewardService`.

- [ ] Cash, gems, speed ve energy için kısa reward rows.
- [ ] Reward icon, kalan günlük hak, cooldown ve WATCH.
- [ ] Disabled/exhausted durumunu anlaşılır ve muted steel göster.
- [ ] Remove-ads satışını bu ekrana karıştırma.

### D04 — Daily reward calendar

**Kaynak:** `UI_GunlukOdul.prefab`, `DailyRewardUI`, `DailyRewardService`.

- [ ] 6 günlük tile + 7. gün büyük chest card.
- [ ] Day number, reward icon, amount, collected, today ve future lock.
- [ ] Bugünün ödülünde coral/gold odak.
- [ ] Tek net CLAIM aksiyonu ve portrait grid.

### D05 — Welcome-back / offline earnings

**Kaynak:** `UI_HosGeldin.prefab`, `WelcomeBackUI`, `WelcomeBackFx`, `OfflineReport`.

- [ ] Büyük coin/cargo medallion hero.
- [ ] Away time, earned amount, cap note.
- [ ] `Collect` ve `Watch to double` iki farklı önem seviyesinde.
- [ ] Empty/no earnings, capped, collected ve doubled state’leri.

### D06 — Wallet overview

**Kaynak:** `WalletUI`, `CurrencyRegistry`.

- [ ] Tüm currency’leri ikon + ad + miktar + kullanım/kaynak açıklamasıyla göster.
- [ ] Cash, gem, chart, energy, mining point, salvage ve pet essence ayrımı.
- [ ] Regenerating currency için timer/progress.
- [ ] Wallet’i mağaza gibi değil, okunabilir genel bakış gibi tasarla.

### D07 — Reward reveal / result toast

**Kaynak:** `RewardRevealUI`.

- [ ] Tek ödül, çoklu ödül ve chest/pack reveal kartları.
- [ ] Cash, gem, chart, salvage, energy, card, equipment, pet ve bundle varyantları.
- [ ] Reveal burst kısa ve düşük maliyetli olsun.
- [ ] En büyük görsel öncelik ödül miktarında; metin ayrı socket’te.

---

## 7. Grup E — ayarlar, dil, destek, tutorial ve rating

### E01 — Settings

**Kaynak:** `UI_Ayarlar.prefab`, `SettingsUI`, `UiPanelSound`.

- [ ] Her satırı shipboard control strip olarak tasarla.
- [ ] Music/sound slider, haptics, language, rate, privacy, restore, support ve build info.
- [ ] Büyük ikon, okunabilir label, mevcut durum ve interaction affordance.
- [ ] Tüm supported language’larda taşma kontrolü.

### E02 — Language picker

**Kaynak:** `LanguageMenuUI`, `SettingsUI`.

- [ ] Settings ile aynı panel, title plate, close konumu ve satır yapısı.
- [ ] Her dilde native name, selected check ve selected highlight.
- [ ] Uzun liste için portrait scroll; debug menu hissi oluşturma.

### E03 — Support / player identity

**Kaynak:** `SupportMenuUI`, `PlayerIdentity`, `SupportTicket`.

- [ ] Captain logbook görünümü.
- [ ] Contact us, community link, Player ID + copy affordance, version/build.
- [ ] Copy success, unavailable link ve empty contact state’leri.

### E04 — Tutorial / onboarding tour

**Kaynak:** `TutorialUI`, `UI_HUD.prefab`, `Assets/Art/UI/Tutorial/`.

- [ ] Tutorial card, rehber karakter, pointer hand, attention arrow, step pip ve skip.
- [ ] Mine, train, storage, refinery, harbour, ship ve upgrade için callout varyantları.
- [ ] Gerçek HUD butonlarını kapatmadan işaretle.
- [ ] First visit, warning, encouragement, final step ve skipped state’leri.

### E05 — Rating prompt

**Kaynak:** `RatingPromptUI`, `RatingPromptService`.

- [ ] Küçük, non-blocking navy/aqua kart.
- [ ] Pozitif an sonrası göster; gameplay’i kapatma.
- [ ] Rate, Later ve close.
- [ ] Neutral, pressed, submitted ve dismissed state’leri.

---

## 8. Grup F — ortak layout, feedback, animasyon ve geçişler

Bunlar tek başına ekran değil, tüm ekranların aynı ürün gibi hissetmesini sağlayan altyapı parçalarıdır.

| Parça | Kaynak | Yapılacak iş |
|---|---|---|
| Safe area | `SafeArea`, `ShipyardSafeArea` | Notch, kısa telefon, tablet ve gesture bar sınırları; bütün interaktifleri güvenli bölgede tut |
| Portrait root | `LetterboxRoot` | 1080x2340 dikey ölçekleme, ornamental köşeleri bozmadan responsive yerleşim |
| Panel open | `PanelOpenFx` | %96’dan 100’e hafif yükselme, cyan highlight ve kısa settle |
| Button press | `TapBounce` | %94–96 press scale, hızlı shadow compression, okunabilir state |
| Panel sound | `UiPanelSound` | Open, close, primary, secondary, reward, warning ve disabled feedback eşleştirmesi |
| Responsive pill | `PillFit` | Uzun localized sayı/adlar için otomatik sığan kaynak ve status pill’i |
| Scroll layout | `ScrollColumnFit` | Dikey kart/ödül listelerinde tutarlı boşluk, büyük touch target ve tipografi |
| Currency fly-out | `HudJuice`, `StorePurchaseFx` | Gold/gem/chart/salvage ikonunu doğru wallet pill’ine hafif uçuşla taşı |
| Sale feedback | `SaleFx`, `FirstSaleFx` | +cash, coin arc, ilk satış banner’ı ve ölçülü confetti |
| Welcome-back motion | `WelcomeBackFx` | Medallion giriş, reward rows stagger, ana miktarı sürekli okunabilir tut |
| Store card entrance | `StoreCardFx`, `StoreHeroFx` | Hafif stagger ve float; flashing/shake yok |
| Offer countdown | `OfferCountdown` | Navy pill, cream zaman, yalnızca gerçek aciliyette coral pulse |
| Card sparkle | `CardSparkle` | Düşük sayıda compass star/sea glint; yalnızca reveal’da yoğunlaşır |
| Confetti | `ConfettiBurst` | Flag, bubble, star, gold/coral/aqua parçalar; kısa ve mobil dostu |
| Scene curtain | `SceneCurtain` | Lacivert fade + cyan wave sweep + kısa compass/anchor glint |
| Objective banner | `ObjectiveBannerUI` | Wallet altında kompakt hedef, ikon, progress ve ready state |
| Notification | `NotificationNavigationUI` | Signal flag/harbour bell ile tekil badge ve deep-link |
| Runtime skin | `UiSkin`, `UiBuild` | Runtime ekranların ortak panel/button/slot/typography style’ına bağlanması |
| Localization | `LocalizedText` | Text-free art, uzun dil metni, sayı formatı ve overflow kontrolü |

---

## 9. Eksiksiz kaynak envanteri

### 9.1 Ekran/surface sahipleri

`AdRewardUI.cs`, `BuildingSigns.cs`, `CannonProductionUI.cs`, `CaptainRosterUI.cs`, `CardCollectionUI.cs`, `ChapterUI.cs`, `ContractUI.cs`, `CraftingUI.cs`, `DailyRewardUI.cs`, `ForemanRosterUI.cs`, `FoundryFestivalUI.cs`, `GoalsUI.cs`, `HarborFestivalUI.cs`, `HudUI.cs`, `InventoryUI.cs`, `IslandBillboard.cs`, `IslandMapUI.cs`, `IslandYardUpgradeUI.cs`, `LadderUI.cs`, `LanguageMenuUI.cs`, `LiveEventsUI.cs`, `MiningGearUI.cs`, `NotificationNavigationUI.cs`, `ObjectiveBannerUI.cs`, `OddsSheetUI.cs`, `OfferPopupUI.cs`, `PetRosterUI.cs`, `PortContractMarker.cs`, `PortShipMarker.cs`, `PremiumStoreUI.cs`, `ProductionSprintUI.cs`, `RatingPromptUI.cs`, `RepairMarkers.cs`, `RewardRevealUI.cs`, `RosterInspectPanel.cs`, `SeaFightUI.cs`, `SeaHudUI.cs`, `SeasonalIndustryPassUI.cs`, `SettingsUI.cs`, `StationScreenUI.cs`, `SupportMenuUI.cs`, `TutorialUI.cs`, `UpgradeReadyMarkers.cs`, `WalletUI.cs`, `WelcomeBackUI.cs`.

### 9.2 Ortak/support/runtime dosyaları

`CameraController.cs`, `CardSparkle.cs`, `ConfettiBurst.cs`, `CurrencyText.cs`, `FirstSaleFx.cs`, `HudJuice.cs`, `LetterboxRoot.cs`, `LocalizedText.cs`, `MapArchipelago.cs`, `OfferCountdown.cs`, `OperationCameraBoot.cs`, `PanelOpenFx.cs`, `PillFit.cs`, `PortraitShipyardCamera.cs`, `SafeArea.cs`, `SaleFx.cs`, `SceneCurtain.cs`, `ScrollColumnFit.cs`, `SeaKit.cs`, `SeaSceneBoot.cs`, `ShipyardMapView.cs`, `ShipyardSafeArea.cs`, `StationPreviewStage.cs`, `StoreCardFx.cs`, `StoreHeroFx.cs`, `StoreNotice.cs`, `StorePurchaseFx.cs`, `TapBounce.cs`, `UiBuild.cs`, `UiPanelSound.cs`, `UiSkin.cs`, `WelcomeBackFx.cs`.

### 9.3 UI prefabları

- `Assets/Prefabs/UI/UI_Ayarlar.prefab`
- `Assets/Prefabs/UI/UI_GunlukOdul.prefab`
- `Assets/Prefabs/UI/UI_Harita.prefab`
- `Assets/Prefabs/UI/UI_HosGeldin.prefab`
- `Assets/Prefabs/UI/UI_HUD.prefab`
- `Assets/Prefabs/UI/UI_IstasyonEkrani.prefab`
- `Assets/Prefabs/UI/UI_KaptanKarti.prefab`
- `Assets/Prefabs/UI/UI_Kontrat.prefab`
- `Assets/Prefabs/UI/UI_Magaza.prefab`
- `Assets/Prefabs/UI/UI_Reklam.prefab`
- `Assets/Prefabs/UI/UI_Teklif.prefab`
- `Assets/Prefabs/UI/UI_UstaKarti.prefab`

### 9.4 Ana referans asset ailesi

Öncelikli referans: `Assets/UI DESİGNS/Deniz ekranı tasarım/` içindeki deniz kitleri.

- Top, zırh, dürbün, tılsım, rigging ikonları.
- Ana ve auto buton.
- Geri ve enerji ekle butonları.
- Rota sekmesi ve enerji pill’i.
- Bilgi paneli, başlık plakası, ekipman slotu ve stat kartı.
- Rütbe yıldızı, kaptan portresi ve gemi görselleri.
- Can barı, ganimet, tehlike ve kilitli rota durumları.

---

## 10. Uygulama sırası

1. [ ] Ortak panel/button/pill/slot/tab/state kitini hazırla.
2. [ ] `A02` Ana ada HUD’ı ve `A04` Sea HUD’ını birlikte tasarla.
3. [ ] `A03` Portrait shipyard HUD’ı ve safe-area davranışını tamamla.
4. [ ] `A05` World map ve `A06` world-space marker ailesini bağla.
5. [ ] `B01` istasyon, `B03` crafting, `B04` depot ekranlarını üret.
6. [ ] `B07`, `B08`, `B09`, `B10` roster/collection ekranlarını aynı card grammar ile üret.
7. [ ] `B02`, `B05`, `B06` contextual production ve upgrade kartlarını tamamla.
8. [ ] `C` grubundaki progression/event ekranlarını ortak progress/reward diliyle üret.
9. [ ] `D` grubundaki mağaza ve ödül ekranlarını güvenilir monetization hiyerarşisiyle üret.
10. [ ] `E` grubundaki settings, language, support, tutorial ve rating ekranlarını tamamla.
11. [ ] `F` grubundaki motion, sound, transition, localization ve notification parçalarını tüm ekranlara uygula.
12. [ ] Dikey telefonlarda görsel QA ve düşük cihaz performans kontrolü yap.

### 10.1 Ek UI içerik başlıkları

- [ ] Kaptanlar — 5 kaptan
- [ ] Ustalar — 15 usta
- [ ] Petler — 6 pet
- [ ] Kartlar — 24 kart
- [ ] Deniz ekipmanı — 5 ekipman slotu
- [ ] Madencilik ekipmanı — 4 ekipman slotu
- [ ] Deniz düşmanları — 5 düşman
- [ ] Para birimleri — 10 para birimi

---

## 11. Her ekran için teslim kriteri

- [ ] Portrait `1080x2340` ana kompozisyon.
- [ ] Safe-area notu ve gerçek dikey Game görünümünde kontrol.
- [ ] Panel, frame, highlight, shadow, icon socket ve text socket ayrı katmanlar.
- [ ] Stretch gereken yerlerde 9-slice rehberi.
- [ ] Normal, selected, pressed, disabled, locked, ready, completed ve error durumları.
- [ ] Runtime metinleri art içine gömülmemiş.
- [ ] Uzun dil metinleri, büyük sayılar ve farklı para formatları test edilmiş.
- [ ] Renk körlüğü için yalnızca renge bağlı olmayan ikon/şekil ayrımı.
- [ ] Koyu arka plan ve yoğun oyun dünyası üzerinde okunabilirlik kontrolü.
- [ ] Açılış/kapanış/tap/reward animasyonları ortak motion dilinde.
- [ ] UI dünyanın kritik üretim noktalarını veya ana hedefi kapatmıyor.
- [ ] Gereksiz particle, glow ve overdraw yok; Android’de 60 FPS hedefi korunuyor.

## Son hedef

Her ekran farklı bir oda, güverte, harita veya kaptan günlüğü gibi hissedebilir; fakat hepsi aynı parlak, katmanlı ve dikey denizcilik dünyasının parçası olarak okunmalıdır.
