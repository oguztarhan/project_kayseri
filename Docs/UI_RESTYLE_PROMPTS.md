# UI Restyle Inventory & English Design Prompts

**Project:** Kayseri / Ore Empire  
**Purpose:** Restyle every existing and planned UI surface so it belongs to the visual family of the sea / embarkment screen.  
**Audit date:** 2026-09-11  
**Scope:** old and new UI prefabs, runtime-built screens, HUDs, map markers, reward surfaces, tutorials, system screens, and shared UI effects.

This is a design handoff document. It does not change Unity scenes, prefabs, scripts, or gameplay behaviour.

---

## 1. What exists in the project

The repository currently contains:

- **77 C# files** under `Assets/Scripts/UI/`.
- **12 UI prefabs** under `Assets/Prefabs/UI/`.
- **8 Unity scenes** under `Assets/Scenes/`.
- **25 dedicated sea-screen design references** under `Assets/UI DESİGNS/Deniz ekranı tasarım/`.
- Several additional art families under `Assets/Art/UI/` and `Assets/Resources/UI/`.
- Both authorable Inspector-driven screens and screens generated at runtime by C#.

### Important implementation distinction

- **Authored / legacy-compatible:** visual hierarchy lives in a prefab and references are wired in the Inspector. Main examples: `UI_HUD`, `UI_Ayarlar`, `UI_Magaza`, `UI_Kontrat`, `UI_IstasyonEkrani`.
- **Runtime / code-built:** the screen is assembled from sprites and layout code when opened. Main examples: `SeaFightUI`, `SeaHudUI`, `GoalsUI`, `CaptainRosterUI`, `PetRosterUI`, `WalletUI`.
- **World-space / contextual:** the UI is attached to buildings, ships, piers, or the island rather than being a full-screen modal. Main examples: `UpgradeReadyMarkers`, `RepairMarkers`, `PortShipMarker`, `PortContractMarker`, `BuildingSigns`.
- **Infrastructure / FX:** these are not standalone screens, but they must use the same visual language when they appear: safe areas, transitions, card-open motion, button bounce, currency fly-outs, sparkles, confetti, and countdowns.

---

## 2. Sea-screen visual DNA — the global style block

Use this block as the shared beginning of every prompt below.

### Global style guide

- Stylized premium mobile game UI, cheerful nautical adventure mixed with polished industrial management.
- Bright cyan and turquoise glossy trim; deep ocean navy inner surfaces; coral-orange primary action areas; warm cream highlights; restrained gold for currency and rewards.
- Thick dark navy outer contour, soft navy drop shadow, beveled cartoon volume, clean rounded corners, controlled specular highlights, subtle bubbles and wave motifs.
- Use an anchor, compass, rope, wave crest, porthole, or ship-plank detail as a structural accent only when it helps the screen's meaning. Avoid decorative clutter.
- UI should feel painted and dimensional, not flat corporate SaaS and not photorealistic.
- Keep the silhouette readable at small Android sizes. Large shapes, strong contrast, short labels, generous touch targets.
- All panels, buttons, tabs, chips, bars, slots, badges, and icons must be delivered as clean transparent PNG/SVG-style game UI assets with no baked-in text unless explicitly requested.
- Preserve separate layers wherever possible: frame, inner fill, highlight, shadow, icon socket, text socket, disabled overlay, selected state, pressed state.
- Design for a 1080 x 2340 portrait reference canvas, with a safe-area-aware variant for tall phones, short phones, tablets, and landscape where the existing screen supports it.
- Use nine-slice-friendly straight edges on stretchable panels and bars; keep ornamental corners and centre emblems outside the stretch zone.
- Do not use a generic blue rectangle. The visual hierarchy should come from material, silhouette, trim, depth, and semantic colour.
- Do not redesign gameplay or invent controls. Keep the existing information architecture and states, but make the presentation feel like one coherent nautical world.

### Sea reference assets to show the designer

The strongest source of truth is:

`Assets/UI DESİGNS/Deniz ekranı tasarım/01_top.png` through `25_kilitli_rota.png`.

The production/runtime equivalents are in:

`Assets/Art/UI/DenizKiti/` and `Assets/Resources/UI/Sea/`.

The most important reference pieces are:

| Reference | Role | Visual lesson |
|---|---|---|
| `12_bilgi_paneli.png` | Large information panel | cyan rounded frame, deep blue readable well, wave crest ornament |
| `13_baslik_plakasi.png` | Title plate | orange/coral plate, cream anchor medallion, glossy cyan rim |
| `06_ana_buton.png` | Primary action | large coral-orange pill, cream inner rim, compass icon, navy outline |
| `07_oto_buton.png` | Secondary/auto action | same family with lower emphasis and clear state separation |
| `10_rota_sekmesi.png` | Route tab | compact nautical tab with selected/open/locked readability |
| `11_enerji_pili.png` | Resource pill | short horizontal pill, icon socket, strong count legibility |
| `14_ekipman_slotu.png` | Equipment slot | framed socket with readable empty/filled/locked states |
| `15_stat_karti.png` | Stat cell | compact dark inset, high-contrast label/value structure |
| `18_deniz_arka_plani.png` | Stage background | saturated turquoise sea, bright sky, cream clouds, island silhouettes |
| `21_can_bari.png` | Health bar | clear full/empty state and readable red/blue semantic fill |
| `24_tehlike_ikonu.png` | Warning | small, high-contrast danger symbol that does not overpower content |
| `25_kilitli_rota.png` | Locked state | lock treatment that preserves the route silhouette and hierarchy |

### Suggested shared design tokens

These are design targets, not a request to hard-code them into C#:

| Token | Direction |
|---|---|
| `OceanDeep` | #071C36 to #123F66, primary inner panel well |
| `OceanMid` | #176788 to #168BA3, secondary panel / selected trim |
| `AquaTrim` | #16D4D8 to #67F4EA, glossy cyan outer frame |
| `CoralAction` | #FF5A3D to #FF8A43, primary action and attention |
| `WarmCream` | #FFF0C7 to #FFF9E6, borders, icons, highlights |
| `NauticalGold` | #F6C84A to #FFDF76, currency and premium reward emphasis |
| `AlertRed` | #F34243, danger / loss / hostile state |
| `SuccessGreen` | #7BE58B, completed / affordable / ready |
| `MutedSteel` | #70839B, inactive, locked, or unavailable state |
| `NavyOutline` | #03112C, outer contour and text shadow |

---

# 3. Group A — entry, HUD, navigation, and persistent world UI

These surfaces are seen most often. They define the player's first impression and should receive the strongest unification pass.

## A01 — Opening / loading screen

**Current source:** `Bootstrap.unity`, `SeaSceneBoot`, `SceneCurtain`, `LetterboxRoot`.  
**Status:** authored opening scene plus transition infrastructure.  
**Must show:** game logo, loading/transition state, reassuring progress, no distracting controls.

**English design prompt**

> Design a premium mobile game loading screen for a stylized nautical industrial adventure. Use a deep ocean-navy background with a subtle turquoise glow, soft wave lines, and a warm cream cloud or compass accent. Place the game logo in a strong central position with a compact glossy cyan-and-coral frame, leaving generous breathing room. Add a thin nautical progress bar with a dark navy trough, bright aqua fill, and a small warm-gold highlight. The composition must feel calm, polished, and optimistic, not like a generic sci-fi loading screen. Deliver clean transparent UI elements and a full-screen composition for 1080x2340 portrait, with no baked-in progress percentage or language-specific text.

## A02 — Main island HUD

**Current source:** `UI_HUD.prefab`, `HudUI`, `HudJuice`, `CurrencyText`, `ObjectiveBannerUI`.  
**Status:** current authored HUD, replacing older code-built top bars.  
**Must show:** gold, gems, income rate, settings, store/offer/daily/map and other navigation entry points, upgrade action, current objective.

**English design prompt**

> Restyle a portrait mobile idle-game island HUD in the established nautical style. Keep the gameplay island visible and unobstructed. Create compact top resource pills for gold, gems, and income using deep navy wells, glossy cyan trim, warm-gold currency accents, and very high-contrast white text sockets. Create a clean side or lower navigation rail using small porthole-like buttons with distinct icon sockets for store, offers, daily reward, map, settings, contracts, events, roster, and upgrades. Make the primary UPGRADE action a larger coral-orange compass/anchor button, while secondary actions use blue or turquoise states. Add one compact objective banner below the resource area: dark ocean panel, cyan edge, cream title, small progress bar, and one restrained goal icon. Everything must remain legible on a busy low-poly island, with clear selected, ready, disabled, pressed, and notification-badge states. Do not bake text into the art.

## A03 — Portrait shipyard HUD

**Current source:** `Shipyard.unity`, `WalletUI`, `ShipyardSafeArea`, `ShipyardMapView`, `PortraitShipyardCamera`.  
**Status:** current portrait shipyard presentation.  
**Must show:** compact wallet overview, shipyard context, vertical-scroll-friendly controls, safe-area support.

**English design prompt**

> Design a compact portrait shipyard HUD for a mobile nautical workshop game. The world is a vertical shipyard with stations, workers, cargo flow, and construction pads, so the UI must stay light and never cover the main production path. Use a slim top wallet strip with navy inset pills, cyan trim, gold and gem accents, and small shipyard utility buttons. Create a left or edge-mounted contextual rail made of compact porthole buttons for map, boost, contracts, and upgrades, with one clear coral active state and quiet muted-blue inactive states. Add subtle construction-ready and bottleneck indicators that look like nautical badges or signal flags, not exclamation spam. The design must work with vertical dragging, device notches, and the safe area. Deliver portrait-first components, no baked text, large touch targets, and a coherent relationship to the sea screen's panel and button family.

## A04 — Sea / embarkment HUD

**Current source:** `SeaHudUI`, `SeaFightUI`, `SeaKit`, `SeaSceneBoot`.  
**Status:** current runtime-built reference screen; the visual source of truth for this restyle.  
**Must show:** route, voyage progress, return-to-shore, captain, equipment, energy, search, auto, combat stage, loot and result cards.

**English design prompt**

> Create the master nautical UI kit for the sea embarkment and combat screen. The top stage is a bright stylized ocean with turquoise waves, cream clouds, island silhouettes, the player's ship, hostile pirate ship or sea creature, and clean health bars. The lower sheet is a large deep-navy information panel with a glossy aqua rim. Add an orange anchor title plate, five equipment slots for cannon, plating, spyglass, charm, and rigging, a captain portrait area, three pet slots, a yellow energy pill, route tabs, a coral SEARCH/FIGHT primary action, and a cool-blue AUTO/secondary action. Include filled, empty, locked, selected, disabled, warning, victory, loss, loot, and repair states. Preserve a friendly premium cartoon finish: thick navy contour, rounded bevels, controlled highlights, bubbles, rope and wave accents. Design every element as reusable transparent layered assets without text baked in.

## A05 — World map / island map

**Current source:** `UI_Harita.prefab`, `IslandMapUI`, `MapArchipelago`, `NotificationNavigationUI`.  
**Status:** authored full-screen map with runtime island selection and notification deep links.  
**Must show:** one island at a time, island identity, ore colour, emblem, route/selection, buy/visit/locked states.

**English design prompt**

> Redesign the world map as a nautical archipelago showcase rather than a plain list. Show one large stylized island at a time inside a dark ocean-blue presentation window, with a turquoise glossy frame, a strong island emblem, its ore colour, a compact name plate, and a clear selected-state aura. Add left and right navigation arrows shaped like rope-wrapped compass controls, a small island progress/status card, and a coral purchase or travel action. Locked islands should use a calm navy silhouette, a cream lock icon, and a muted turquoise route rather than a grey UI rectangle. Include subtle map rays, bubbles, wave rings, and a small notification badge, but preserve a clear focal hierarchy. Deliver portrait and landscape-safe layouts, transparent components, and no baked-in text.

## A06 — Island / building labels and contextual markers

**Current source:** `BuildingSigns`, `IslandBillboard`, `UpgradeReadyMarkers`, `RepairMarkers`, `PortShipMarker`, `PortContractMarker`.  
**Status:** world-space and screen-space contextual UI.  
**Must show:** station names, island name, upgrade-ready, repair-needed, player ship boarding, contract-ready ship.

**English design prompt**

> Design a family of small world-space nautical markers for a colourful low-poly industrial island. Create: a readable station name board, a larger island identity billboard, an affordable-upgrade badge, a repair-needed wrench badge, a player-ship boarding marker, and a contract-ready harbour marker. All markers should share the sea screen's thick navy contour, aqua or turquoise rim, cream icon highlight, and coral or gold attention accent. Use distinct silhouettes so players can understand each marker before reading text. The badges must remain readable over grass, buildings, water, and night lighting; include idle, bobbing, pulse, completed, disabled, and notification variants. Avoid floating generic red exclamation marks and avoid covering the buildings themselves. Supply transparent assets with icon and text sockets separated.

---

# 4. Group B — shipyard, station, workshop, equipment, and roster screens

These are decision-heavy screens. Their job is to make upgrades, crafting, comparison, and crew choices feel part of the same shipboard interface.

## B01 — Station upgrade screen

**Current source:** `UI_IstasyonEkrani.prefab`, `StationScreenUI`, `StationPreviewStage`.  
**Status:** authored station page with model preview, station tabs, upgrade rows, level bars, and expansion page.

**English design prompt**

> Design a full-screen station upgrade page for a stylized nautical shipyard management game. The upper half is a calm presentation stage for a rotating station or machine model, framed like a large porthole or ship window with a soft ocean-to-navy gradient. The lower half is a readable upgrade keyboard: compact dark-blue rows, bright aqua progress bars, warm-gold costs, coral primary upgrade buttons, blue disabled buttons, and small nautical station icons. At the top, use a broad cyan title plate with a small anchor or station emblem. Add a horizontal station-family tab strip with selected, available, and locked states. Include a separate expansion state where the ground/building result is the hero rather than a recipe list. Preserve clear model visibility, safe-area margins, and no text baked into the art.

## B02 — Cannon production card

**Current source:** `CannonProductionUI`, `CannonProductionWorldBridge`.  
**Status:** current runtime contextual card in the portrait shipyard world.

**English design prompt**

> Design a compact contextual cannon-production card that can sit over a portrait shipyard without hiding the world. Use a deep navy inset with a glossy aqua frame and a small orange anchor title plate. Show the cannon icon or model socket, ingredient/resource slots, craft progress, output status, and one clear coral CRAFT or COLLECT action. Use warm gold for cost, bright green for ready, muted steel for unavailable, and a small smoke or ember accent to connect the card to a working foundry. The card should look like a miniature shipboard control panel, not a generic modal. Include empty, crafting, ready, blocked, and maxed states, no baked-in text, and transparent stretchable panel components.

## B03 — Workshop crafting screen

**Current source:** `CraftingUI`, `OddsSheetUI`, `RosterInspectPanel`, `RewardRevealUI`.  
**Status:** current runtime-built workshop screen with bench, odds, and pending-item decision card.

**English design prompt**

> Restyle the workshop crafting screen as a premium nautical workbench. Use a large ocean-navy panel with a cyan rounded frame and a raised anchor title plate. The left side is the crafting bench: ingredient sockets, a recipe/output preview, a clear coral CRAFT button, cost and timing chips. The right side is a compact odds sheet styled like a captain's log or ship manifest, with rarity colour bands, clean percentages, and a small information icon. When a craft is waiting, show a top decision card with the item preview and three explicit actions: EQUIP, STORE, or SCRAP. Use porthole sockets, rope dividers, brass/gold reward accents, and thick navy outlines. Provide common, rare, epic, legendary, locked, crafting, result, and insufficient-resource states. Do not use spreadsheet-like grey rows and do not bake words into the artwork.

## B04 — Inventory / depot and catalogue

**Current source:** `InventoryUI`, `GearStash`, `Catalogue`; referenced in `Docs/DEPO.md`.  
**Status:** current runtime-built two-tab shell: gear depot and material/product catalogue.

**English design prompt**

> Design a two-tab depot screen for a nautical industrial adventure. The shell uses a dark ocean-navy body, a glossy turquoise frame, and a raised cyan title plate. Tab one is DONANIM / Gear Depot: a five-by-four shelf of framed equipment cards, four worn-equipment slots at the top, clear rarity trim, upgrade comparison arrows, a POWER summary, and a coral or cool-blue EQUIP action. Tab two is KATALOG / Catalogue: a readable material-to-product chain with ore icons, recipe connectors, lock conditions, and a compact detail panel. Use separate visual grammar for gear versus resources while keeping the same chrome: porthole sockets, rope dividers, cream text, gold currencies, and navy insets. Include full, empty, locked, better-than-equipped, selected, and salvage states. Keep the grid readable on portrait Android and avoid tiny spreadsheet text.

## B05 — Mining gear screen

**Current source:** `MiningGearUI`, `MiningGearService`.  
**Status:** current runtime-built four-slot captain/mining loadout screen.

**English design prompt**

> Design a compact mining loadout screen in the sea-screen visual language, adapted for an industrial island captain. Show four equipment slots in a horizontal or two-by-two arrangement, each as a nautical framed socket with a distinct mining icon, grade stars, and a readable bonus. Place the captain portrait and total income bonus in a strong header area with an orange-coral title plate and cyan rim. Add a single clear CRAFT action with mining-point cost, plus empty, filled, locked, affordable, and cooldown states. Mix shipboard brass details with ore, hammer, drill, and crystal motifs without changing the global palette. Use deep navy panel wells, aqua highlights, cream labels, and restrained gold reward accents. Deliver reusable slot, star, stat, and button assets with separate text sockets and no baked-in wording.

## B06 — Island yard upgrade sheet

**Current source:** `IslandYardUpgradeUI`, `MarketService`, `YardUpgrade`.  
**Status:** current runtime island-side six-track purchase sheet.

**English design prompt**

> Design a six-row island yard upgrade sheet for an automatic nautical production island. Use a compact deep-navy panel with a glossy aqua border and an orange anchor title medallion. Each row represents one yard capability: rate, capacity, transport, automation, or equivalent production track. Give each row a large nautical-industrial icon, current level, small aqua progress or tier indicator, gold cost, and a strong coral BUY/UPGRADE action. Show affordability, insufficient funds, maxed, unavailable/loading, and successful purchase states. The sheet should feel like the player's dockmaster's control board: dimensional, friendly, readable, and connected to the sea screen's panel chrome. Keep the world visible around the sheet when used in portrait mode. No baked text.

## B07 — Foreman / master roster

**Current source:** `ForemanRosterUI`, `UI_UstaKarti.prefab`, `StationForemen`.  
**Status:** current runtime roster, with legacy card prefab available.

**English design prompt**

> Design a master foreman roster screen for a nautical industrial empire. Place a free or gem chest shelf on the left, styled as a carved ship cargo chest with cyan edge light and warm-gold hardware. Place fifteen compact foreman cards on the right in a readable station-grouped grid. Each card needs a portrait socket, station identity, rarity frame, five-star progression, current bonus, duplicate/card count, locked or owned state, and one clear action. Use colour-coded station emblems, but keep every card inside the same navy-and-aqua frame family. The selected card may use a coral glow; legendary and mythic cards may use gold and magenta accents sparingly. Include chest-ready, chest-cooldown, reveal, duplicate, maxed, and locked states. Preserve a premium playful mobile-game finish without making the screen visually noisy.

## B08 — Captain roster

**Current source:** `CaptainRosterUI`, `UI_KaptanKarti.prefab`, `Captains`.  
**Status:** current runtime two-column roster with crate and captain cards.

**English design prompt**

> Design a captain roster screen for a stylized sea-adventure game. Put a captain crate and chart balance in a strong left-side or top hero area, using a warm wooden chest, brass fittings, an anchor emblem, and a glossy cyan frame. Show ten captains in two columns, each with a large portrait socket, role icon, rarity band, five-star level, duplicate progress, and readable sea bonus. Use five clearly separated rarity treatments: common steel, rare ocean blue, epic violet, legendary gold, and mythic coral-magenta, all grounded by the same navy outline and aqua chrome. Include unowned, owned, upgrade-ready, maxed, selected, crate-opening, and duplicate states. Keep the portrait the focal point and make the chart currency easy to understand without adding a new visual language.

## B09 — Pet companion collection

**Current source:** `PetRosterUI`, `SeaFightUI` pet slots, `Pets`, `PetChest`.  
**Status:** current runtime collection screen plus three equip slots on the sea sheet.

**English design prompt**

> Design a pet companion collection screen for a friendly nautical combat game. Use a dark ocean panel with a cyan glossy frame and a coral anchor title plate. Show a pet chest and pearl or essence balance in the header, then six species cards for parrot, monkey, baby octopus, ship's rat, crab, and turtle. Each card needs a charming portrait socket, species emblem, rarity border, star/fusion progress, effect label socket, equipped indicator, locked slot treatment, and one clear FUSE or EQUIP action. Mirror the three pet equip slots used in the sea combat sheet so the player recognises the same objects in both screens. Use the same five rarity colours as the captain system, with playful creature silhouettes and restrained sparkle effects. Include chest-open, insufficient essence, locked slot, equipped, fusion-ready, and maxed states. No baked text.

## B10 — Card collection

**Current source:** `CardCollectionUI`, `CardCollection`, `CardCollectionCatalogue`.  
**Status:** current runtime pack card, set tabs, and two-column card grid.

**English design prompt**

> Design a collectible card collection screen for a nautical industrial adventure. Place the unopened pack or pack status in a prominent upper card with a small sparkle or wave-glint effect. Below it, use three set tabs styled like painted ship plaques or route tabs. Each set contains an eight-card two-column grid with portrait art sockets, rarity frames, level stars, duplicate progress, locked silhouettes, and a readable permanent-bonus summary. Use deep navy card wells, glossy aqua edges, warm cream text sockets, and rarity accents that match the captain and pet rosters. The active set tab may use coral-orange; inactive tabs use cool ocean blue. Include unopened pack, selected card, set-complete, duplicate, max-level, and locked states. Keep the information dense but friendly and avoid casino-like visual noise.

## B11 — Roster inspect / detail panel

**Current source:** `RosterInspectPanel`.  
**Status:** runtime detail component reused by roster surfaces.

**English design prompt**

> Design a reusable roster detail card for captains, foremen, pets, and collectible cards. Use a compact porthole-like portrait frame, a cyan title strip with a small emblem, a deep navy information well, a rarity badge, star progression, one primary stat or effect, duplicate progress, and a clear action area. The selected state should use a controlled coral or gold glow, while locked and unavailable states use muted steel and a readable lock icon. The component must be able to switch between human portraits, pet portraits, and card art without losing its shared nautical identity. Make all sockets separable and keep the frame nine-slice-friendly where it stretches.

---

# 5. Group C — goals, chapters, contracts, events, leagues, and passes

These surfaces explain long-term progression. Their layouts can differ, but they must share the same ocean panel, cyan chrome, title-plate, chip, progress-bar, and reward language.

## C01 — Goals / daily and weekly tasks

**Current source:** `GoalsUI`, `GoalService`, `ObjectiveBannerUI`.  
**Status:** current runtime tabbed task presentation.

**English design prompt**

> Design a goals screen for a mobile nautical idle-management game. Use a large deep ocean panel with a glossy aqua frame and an anchor-shaped title plate. Organise daily, weekly, and achievement tasks into compact readable cards with a clear icon, task title socket, progress bar, reward preview, and CLAIM action. Completed tasks should receive a warm-gold or coral highlight, while incomplete tasks stay in calm navy and turquoise. Add a small top summary chip for ready rewards without covering the content. Use rope-like dividers, porthole icons, and wave-shaped progress accents, but keep the screen practical and easy to scan. Include empty, in-progress, claimable, claimed, and locked states. No baked text.

## C02 — Chapter log / island progression

**Current source:** `ChapterUI`, `Chapters`, `ChapterService`.  
**Status:** current runtime chapter log with eight island entries and five beats.

**English design prompt**

> Design a nautical chapter log showing eight island chapters and the selected chapter's five progression beats. Use the left side for island emblems or compact island medallions, connected by a subtle turquoise route line. Use the right side for a large chapter title plate, a short story-intro text socket, five milestone rows, progress bars, and reward icons. Completed beats should feel like stamped ship-log entries with gold or cream seals; current beats use a coral signal glow; locked chapters use a navy silhouette and a small cream lock. The background should be deep ocean blue with faint map rays and bubbles. Preserve a readable portrait layout and a calm storybook feeling without turning it into a parchment fantasy UI.

## C03 — Port contracts / orders

**Current source:** `UI_Kontrat.prefab`, `ContractUI`, `PortContractMarker`.  
**Status:** authored running-contract card plus runtime offer cards and countdown.

**English design prompt**

> Design a port contract board for a nautical industrial game. The panel should feel like a polished harbour notice board: deep navy body, glossy cyan outer frame, rope or rivet dividers, and a raised orange anchor title plate. Show a horizon countdown state, then three contract offer cards with target icons, quantities, timers, rewards, and accept/skip actions. Once a contract is active, show one larger running card with a strong progress bar, reward row, and coral CLAIM action. Use gold for cash rewards, charts or salvage icons for sea rewards, and clear ready/expired/claimed states. The player's ship and harbour should feel implied by the visual language, but the board must stay concise, readable, and touch-friendly. No baked text.

## C04 — Live events board

**Current source:** `LiveEventsUI`, `LiveEventService`, `UI_Sistemler`.  
**Status:** current runtime board listing running, upcoming, and completed events.

**English design prompt**

> Design a live-events board for a mobile nautical empire game. Use one large ocean-navy frame with a glossy aqua border and a coral or gold event crest. List running, upcoming, and finished-but-claimable events as clear horizontal cards. Each card needs an event emblem, phase/status chip, time remaining, short objective preview, progress or reward cue, and a clear OPEN or CLAIM action. Event cards should support foundry, harbour, production, and seasonal themes while retaining the same shared chrome. Use coral for active attention, turquoise for scheduled, gold for rewards, and muted steel for closed. Include an empty schedule state that feels intentional rather than broken. Do not use a wall of tiny text or bake event names into the art.

## C05 — Foundry Festival

**Current source:** `FoundryFestivalUI`, `FoundryFestivalService`.  
**Status:** event-specific runtime screen: week strip, three daily tasks, reward chests.

**English design prompt**

> Design a limited-time Foundry Festival screen inside the shared nautical UI system. Use a large celebratory title plate with a glowing forge, anchor, or molten-metal emblem, but keep the base panel deep navy with aqua trim. Show a seven-day strip across the top, one selected day with coral emphasis, three daily task cards in the middle, and a row of reward chests or milestones at the bottom. Mix warm orange, ember red, brass gold, and cyan highlights to suggest a foundry without leaving the ocean world. Include active, completed, claimable, expired, and locked task states, with readable progress bars and large reward icons. The page should feel festive but still belong to the same game.

## C06 — Harbor Festival

**Current source:** `HarborFestivalUI`, `HarborFestivalService`.  
**Status:** current runtime event screen for tasks, rewards, and catalogue.

**English design prompt**

> Design a Harbor Festival event screen for a cheerful nautical mobile game. Use a bright harbour crest, rope, flags, and ship-bell details as restrained ornaments around a glossy aqua-and-navy panel. Provide three clear tabs or sections for TASKS, REWARDS, and CATALOGUE. Task cards need icons, progress bars, timer/status chips, and CLAIM actions; catalogue cards need item art sockets, cost, owned state, and purchase action. Use coral for the festival's primary action, warm gold for rewards, sea blue for navigation, and muted steel for unavailable content. Keep the layout compact enough for portrait phones, with a strong title plate and no baked-in language-specific labels.

## C07 — Production Sprint

**Current source:** `ProductionSprintUI`, `ProductionSprintService`.  
**Status:** current runtime action and personal-milestone screen.

**English design prompt**

> Design a Production Sprint screen for a nautical industrial idle game. The hero should be a dynamic but readable sprint badge combining a stopwatch, wave trail, and cargo or gear symbol. Use a deep navy panel with cyan rim, a coral selected tab, and two clear sections: task actions and personal milestones. Show short task rows with a strong progress bar, current value, target value, reward icons, and a large CLAIM button. Milestones should feel like ship-log checkpoints with stamped seals and reward chests. Include active, completed, claimable, cooldown, and event-ended states. Keep the visual energy high through subtle motion-ready accents, not through clutter or excessive glow.

## C08 — Seasonal Industry Pass

**Current source:** `SeasonalIndustryPassUI`, `SeasonalIndustryPassService`.  
**Status:** current runtime scrollable free/premium reward track.

**English design prompt**

> Design a seasonal industry pass reward track in the master nautical style. Use a dark ocean-navy scroll panel with glossy cyan frame and a strong season crest on a raised title plate. Create a horizontal or vertically readable reward rail with numbered tiers, free and premium lanes, large reward icons, progress markers, current-tier highlight, locked tier treatment, and a clear CLAIM or UPGRADE action. Make the premium lane feel richer through brass/gold trim and coral accents, not through a separate unrelated style. Include current, claimed, claimable, locked, maxed, and season-ended states. The track must remain readable on portrait phones with large touch targets and no tiny spreadsheet-like labels.

## C09 — League / leaderboard ladder

**Current source:** `LadderUI`, `LadderService`, `LEADERBOARDS.md`.  
**Status:** current runtime podium plus ranked rows and reward chests.

**English design prompt**

> Design a three-day nautical league ladder. Use a prominent podium for ranks one, two, and three, shaped like painted ship crates or harbour platforms, with first place centred and highlighted in warm gold. Below it, show readable ranked rows with player badge, rank, score, and reward chest socket. The whole board sits inside a deep navy ocean panel with a glossy aqua rim and a compact countdown title plate. Use clear player-row emphasis, synthetic/placeholder data treatment when needed, claimable reward strip, empty-state explanation, and chest-open detail state. Keep rank colours celebratory but controlled: gold, silver, bronze, turquoise, and navy. No fantasy tournament clutter and no baked text.

---

# 6. Group D — store, offers, ads, rewards, and wallet

These screens contain monetisation or reward moments, so they must feel generous, trustworthy, and consistent rather than aggressive.

## D01 — Premium store

**Current source:** `UI_Magaza.prefab`, `PremiumStoreUI`, `StoreCardFx`, `StoreHeroFx`, `StorePurchaseFx`, `StoreNotice`, `OfferCountdown`.  
**Status:** authored scrollable store with offer cards and currency grids.

**English design prompt**

> Design the premium store for a stylized nautical industrial adventure. The store should feel like a beautiful harbour market or captain's supply deck, not a casino. Use a rich but readable deep ocean background with a glossy cyan frame, a broad title plate, and one hero offer area. Organise offer cards, gold packs, gem packs, and gem-priced boosts into clean sections with strong visual grouping. Each card needs a large art socket, value or reward area, price strip, owned/purchased state, and subtle value badge. Use coral for the primary buy action, gold for currency and premium trim, turquoise for section headers, and navy wells for readability. Add restrained floating sparkle or purchase-flight effects. Avoid pressure-heavy countdown treatment, fake percentage claims, and baked text. Provide portrait scroll, short-phone, and landscape-safe component variants.

## D02 — Island contextual offer popup

**Current source:** `UI_Teklif.prefab`, `OfferPopupUI`, `StoreHeroFx`, `OfferCountdown`.  
**Status:** per-island offer popup with themed packs and countdown.

**English design prompt**

> Design a contextual island offer popup that feels like a dockside captain's bargain. Use a dark ocean scrim behind a compact deep-navy card with glossy cyan frame and an orange anchor title plate. Show the island name and tier as text sockets, a large themed reward tray, a clear price strip, a coral BUY action, and a quiet close button. Include small wave, cargo, ore, or ship details that can change per island while the chrome stays shared. The popup must have readable value hierarchy, a friendly non-aggressive countdown chip, purchased/claimed/expired states, and a clear unavailable state. No flashing red sale banners, no baked language-specific copy, and no obstruction of the full screen beyond the intended modal focus.

## D03 — Rewarded-ad rewards panel

**Current source:** `UI_Reklam.prefab`, `AdRewardUI`, `FreeRewardService`.  
**Status:** authored list of opt-in ad reward slots.

**English design prompt**

> Design a trustworthy rewarded-ad rewards panel for a nautical mobile game. Use a calm deep-navy card with a glossy aqua edge and a warm cream or gold reward header. Show a short list of opt-in reward rows such as cash, gems, speed, or energy. Each row needs a large reward icon, remaining daily charges, cooldown indicator, and a clear coral WATCH action with a small play icon. Disabled and exhausted states should use muted steel and remain understandable. The screen should feel like a helpful harbour notice, never like a forced advertisement wall. Keep the remove-ads purchase out of this reward-giveaway screen. Include no baked text and provide transparent row, pill, charge pip, and button states.

## D04 — Daily reward calendar

**Current source:** `UI_GunlukOdul.prefab`, `DailyRewardUI`, `DailyRewardService`.  
**Status:** authored seven-day login reward screen: six grid tiles plus day-seven chest.

**English design prompt**

> Design a seven-day daily reward calendar in the nautical style. Use a large ocean-navy panel with glossy turquoise frame and a raised title plate featuring a small anchor, compass, or ship's wheel. Show six compact daily reward tiles in a two-by-three grid and a larger day-seven chest card below or beside them. Each tile needs a day-number socket, reward icon, amount socket, collected badge, today highlight, and locked future state. Make the current reward feel inviting with coral and warm-gold emphasis, while collected rewards use a cream stamp or turquoise check. Add one clear CLAIM action and keep the calendar readable at portrait mobile size. No baked text.

## D05 — Welcome-back / offline earnings

**Current source:** `UI_HosGeldin.prefab`, `WelcomeBackUI`, `WelcomeBackFx`, `OfflineReport`.  
**Status:** authored return-from-idle reward popup with collect and double-by-ad actions.

**English design prompt**

> Design a welcoming offline-earnings screen for a nautical idle game. Use a warm but controlled deep-navy modal with aqua frame, soft light rays, and a large gold coin or cargo medallion as the hero. Show time away, earned amount, a short cap note, and two clear actions: COLLECT in cool blue or turquoise, and WATCH TO DOUBLE in coral with a play icon. Add a subtle wave or rising-coin visual effect, but keep the earned amount as the focus. Include empty/no-earnings, capped, collected, and doubled states. The result should feel rewarding and honest, like returning to a well-run harbour, not like a sales interruption. No baked text.

## D06 — Wallet overview

**Current source:** `WalletUI`, `CurrencyRegistry`.  
**Status:** current runtime-built overview of all wallet currencies.

**English design prompt**

> Design a wallet overview screen for every player currency in a nautical management game. Use a deep ocean panel with glossy aqua frame and a compact anchor title plate. Show one clean row per currency with a large icon socket, currency name socket, current amount, source or purpose hint, and a subtle category accent. Separate ordinary cash, premium gems, charts, energy, mining points, salvage, and pet essence using meaningful icons and restrained colour, while keeping one shared card family. Regenerating currencies need a small progress or timer indicator; unavailable or empty states should remain readable. Avoid turning the screen into a shop: this is an overview, not a purchase flow. Deliver portrait-friendly rows with large icons and no baked text.

## D07 — Reward reveal / result toast

**Current source:** `RewardRevealUI`, used by multiple claim and chest flows.  
**Status:** presentation-only runtime reward surface.

**English design prompt**

> Design a reusable nautical reward-reveal card and toast system. The base card should use a dark ocean well, glossy aqua outline, warm cream title area, and a central reward icon or chest socket. Add separate variants for cash, gems, charts, salvage, energy, cards, equipment, pets, and bundles. The reveal sequence may use a short coral-to-gold burst, wave sparkle, or compass spin, but it must remain lightweight and readable. Include single reward, multi-reward row, unopened pack granted, duplicate conversion, and claim-complete states. Make the reward amount the clearest element, with separate text sockets and no baked words.

---

# 7. Group E — settings, language, support, tutorial, and review

These are utility surfaces, but they should still feel like pages from the same captain's interface.

## E01 — Settings

**Current source:** `UI_Ayarlar.prefab`, `SettingsUI`, `UiPanelSound`.  
**Status:** authored settings page with audio sliders, haptics, language, rating, privacy, restore, and support entry.

**English design prompt**

> Design a settings page for a polished nautical mobile game. Use the same deep navy panel, glossy cyan frame, and raised anchor title plate as the sea screen. Each settings row should look like a shipboard control strip: icon socket, readable label socket, current state, and a clear interaction affordance. Include music and sound sliders with rope or wave-shaped tracks, a turquoise/coral haptic switch, language, rate game, privacy, restore purchases, support, and build-information rows. Use cream text, gold or aqua accents, muted steel disabled states, and a simple close button. The page must be calm, accessible, safe-area aware, and readable in every supported language. No baked text and no tiny controls.

## E02 — Language picker

**Current source:** `LanguageMenuUI`, opened from `SettingsUI`.  
**Status:** current runtime-built second page using the settings skin.

**English design prompt**

> Design a language picker as a second page of the nautical settings screen. Reuse the exact same panel chrome, cyan trim, title plate, close-button placement, and row structure as settings. Display each language as a large, highly readable row with its native name, selection check, and a calm selected-state highlight. Include a clear scroll or two-column portrait layout if needed, but never let the list feel like a debug menu. Use deep navy wells, cream text, aqua selected trim, and a single coral focus accent. Keep the design culturally neutral, accessible, and free of baked labels so localization can replace all text.

## E03 — Support and player identity page

**Current source:** `SupportMenuUI`, `PlayerIdentity`, `SupportTicket`, opened from settings.  
**Status:** current runtime-built settings subpage.

**English design prompt**

> Design a support and player-identity page that feels like a captain's logbook page inside the nautical settings system. Reuse the same deep navy panel, glossy aqua frame, warm cream text sockets, and anchor title plate. Provide a large CONTACT US row with mail icon, approved community-link rows, a PLAYER ID row with copy affordance, and a compact version/build strip. Make the player ID feel trustworthy and easy to copy without looking like a developer debug value. Include copy success, unavailable link, and empty contact configuration states. Keep the page sparse, readable, and safe-area friendly. No baked text.

## E04 — Tutorial / onboarding tour

**Current source:** `TutorialUI`, `UI_HUD.prefab`, tutorial art under `Assets/Art/UI/Tutorial/`.  
**Status:** authored tutorial with tour, controls, and coach character.

**English design prompt**

> Design a friendly nautical onboarding system for a mobile industrial island game. Create a large tutorial card with glossy cyan frame, deep navy inner well, cream readable text area, and a coral DEVAM / CONTINUE action. Include a warm workshop captain or foreman guide character, pointer-hand and attention-arrow assets, step pips, skip control, and contextual callout variants for mine, train, storage, refinery, harbour, sea ship, and upgrade actions. The first tour should feel like a calm camera-guided shipyard story; the second controls phase should point to real HUD controls without covering the world. Use wave, rope, compass, and cargo motifs, but keep the instruction hierarchy extremely clear. Provide first-visit, warning, encouragement, final-step, and skipped states. No baked instructional text.

## E05 — Rating prompt

**Current source:** `RatingPromptUI`, `RatingPromptService`.  
**Status:** compact non-blocking positive-moment prompt.

**English design prompt**

> Design a compact, non-blocking rating prompt for a cheerful nautical mobile game. Use a small deep-navy card with glossy aqua border, warm cream title area, and a friendly ship wheel, anchor, or smiling captain emblem. Provide a simple positive question text socket, a coral RATE action, a quiet LATER action, and a close affordance. The card should appear as a gentle celebration after a positive moment, not as an interruption or a dark overlay. Include neutral, hover/pressed, submitted, and dismissed states. Keep it small enough not to hide important gameplay and use no baked text.

---

# 8. Group F — shared components, feedback, animation, and transitions

These are not standalone screens, but their restyle is essential for a coherent result.

| Component | Source | English design prompt |
|---|---|---|
| Safe-area wrapper | `SafeArea`, `ShipyardSafeArea` | “Create a reusable safe-area layout guide for a portrait mobile nautical UI. Show notch, rounded-corner, gesture-bar, short-phone, tablet, and landscape-safe boundaries while preserving the sea screen's navy, aqua, coral, and cream tokens. Keep all interactive controls inside the safe zone.” |
| Letterbox / responsive root | `LetterboxRoot` | “Create a responsive 1080x2340 nautical UI frame with portrait-first scaling, a controlled landscape variant, stable title and button anchors, and no stretching of ornamental cyan corners or anchor medallions.” |
| Panel-open motion | `PanelOpenFx` | “Create a subtle panel-open animation language: deep navy card rises from 96% scale to full size, cyan rim catches a short highlight, title plate settles with a soft nautical bounce, and no excessive overshoot.” |
| Button press / bounce | `TapBounce` | “Create pressed and released states for nautical mobile buttons: 94–96% press scale, brief navy shadow compression, coral or aqua highlight response, fast and satisfying, never elastic enough to affect readability.” |
| Panel sound feedback | `UiPanelSound` | “Define a sound-feedback visual reference sheet for panel open, panel close, primary tap, secondary tap, reward, warning, and disabled states, matching the tactile feeling of a polished shipboard control panel.” |
| Pill fit / responsive chip | `PillFit` | “Create responsive resource and status pills with deep navy inset, glossy aqua or semantic trim, large icon socket, auto-fitting number socket, and enough padding for long localized values.” |
| Scroll-column fit | `ScrollColumnFit` | “Create a portrait mobile scroll layout system for nautical cards and reward rows, preserving large touch targets, consistent rope-like spacing, and readable typography at every phone height.” |
| Currency fly-out | `HudJuice`, `StorePurchaseFx` | “Create lightweight reward-flight effects where gold, gems, charts, or salvage icons arc toward the correct wallet pill with a short aqua or gold trail, small scale pop, and no heavy particle overdraw.” |
| Sale feedback | `SaleFx`, `FirstSaleFx` | “Create sale feedback for an idle harbour: compact floating +cash labels, a brief gold coin arc, a first-sale coral banner with cream lettering, and restrained confetti only for the first meaningful milestone.” |
| Welcome-back motion | `WelcomeBackFx` | “Create a warm return-to-harbour reveal: medallion scales in, reward rows stagger softly, a subtle aqua glow rotates behind the coin pile, and the main amount remains readable throughout.” |
| Store card entrance | `StoreCardFx`, `StoreHeroFx` | “Create a staggered premium-store card entrance with soft 88% scale-up, gentle float for hero art, controlled cyan highlight, and no casino-like flashing or aggressive shake.” |
| Offer countdown | `OfferCountdown` | “Create a trustworthy nautical countdown chip: compact navy pill, cream time text, coral only when genuinely urgent, subtle pulse at the threshold, and no full-screen warning treatment.” |
| Card sparkle | `CardSparkle` | “Create sparse collectible-card sparkles shaped like tiny compass stars or sea glints, warm cream and gold, short lifetime, low particle count, and stronger density only for a real reveal.” |
| Confetti | `ConfettiBurst` | “Create celebratory nautical confetti using tiny paper flags, stars, bubbles, and gold/coral/aqua pieces, with a short readable burst and mobile-friendly low overdraw.” |
| Scene curtain | `SceneCurtain` | “Create a scene transition curtain using a deep ocean navy fade, a thin cyan wave sweep, and a brief compass or anchor glint. It must feel smooth and premium, not like a hard black cut.” |
| Floating objective banner | `ObjectiveBannerUI` | “Create a compact persistent objective strip below the top wallet area: deep navy card, aqua edge, cream goal title, one icon, one progress bar, and restrained coral ready state.” |
| Notification navigation | `NotificationNavigationUI` | “Create a small notification/deep-link treatment based on nautical signal flags or a glowing harbour bell, with one clear badge and no stack of competing red dots.” |

---

# 9. Exhaustive source inventory

This checklist contains every file currently under the UI script folder. Files marked **surface** have a dedicated prompt above; files marked **support** are covered by the shared-component prompts or are non-visual runtime helpers.

## UI scripts — surface owners

`AdRewardUI.cs`, `BuildingSigns.cs`, `CannonProductionUI.cs`, `CaptainRosterUI.cs`, `CardCollectionUI.cs`, `ChapterUI.cs`, `ContractUI.cs`, `CraftingUI.cs`, `DailyRewardUI.cs`, `ForemanRosterUI.cs`, `FoundryFestivalUI.cs`, `GoalsUI.cs`, `HarborFestivalUI.cs`, `HudUI.cs`, `InventoryUI.cs`, `IslandBillboard.cs`, `IslandMapUI.cs`, `IslandYardUpgradeUI.cs`, `LadderUI.cs`, `LanguageMenuUI.cs`, `LiveEventsUI.cs`, `MiningGearUI.cs`, `NotificationNavigationUI.cs`, `ObjectiveBannerUI.cs`, `OddsSheetUI.cs`, `OfferPopupUI.cs`, `PetRosterUI.cs`, `PortContractMarker.cs`, `PortShipMarker.cs`, `PremiumStoreUI.cs`, `ProductionSprintUI.cs`, `RatingPromptUI.cs`, `RepairMarkers.cs`, `RewardRevealUI.cs`, `RosterInspectPanel.cs`, `SeaFightUI.cs`, `SeaHudUI.cs`, `SeasonalIndustryPassUI.cs`, `SettingsUI.cs`, `StationScreenUI.cs`, `SupportMenuUI.cs`, `TutorialUI.cs`, `UpgradeReadyMarkers.cs`, `WalletUI.cs`, `WelcomeBackUI.cs`.

## UI scripts — support, layout, motion, or runtime infrastructure

`CameraController.cs`, `CardSparkle.cs`, `ConfettiBurst.cs`, `CurrencyText.cs`, `FirstSaleFx.cs`, `HudJuice.cs`, `LetterboxRoot.cs`, `LocalizedText.cs`, `MapArchipelago.cs`, `OfferCountdown.cs`, `OperationCameraBoot.cs`, `PanelOpenFx.cs`, `PillFit.cs`, `PortraitShipyardCamera.cs`, `SafeArea.cs`, `SaleFx.cs`, `SceneCurtain.cs`, `ScrollColumnFit.cs`, `SeaKit.cs`, `SeaSceneBoot.cs`, `ShipyardMapView.cs`, `ShipyardSafeArea.cs`, `StationPreviewStage.cs`, `StoreCardFx.cs`, `StoreHeroFx.cs`, `StoreNotice.cs`, `StorePurchaseFx.cs`, `TapBounce.cs`, `UiBuild.cs`, `UiPanelSound.cs`, `UiSkin.cs`, `WelcomeBackFx.cs`.

## Legacy and authored UI prefab inventory

These must remain in the restyle audit even if a newer runtime screen has replaced or supplemented part of them:

| Prefab | Main surface | Restyle target |
|---|---|---|
| `UI_Ayarlar.prefab` | settings | E01 |
| `UI_GunlukOdul.prefab` | daily reward | D04 |
| `UI_Harita.prefab` | island/world map | A05 |
| `UI_HosGeldin.prefab` | welcome-back earnings | D05 |
| `UI_HUD.prefab` | main island HUD and tutorial host | A02, E04 |
| `UI_IstasyonEkrani.prefab` | station upgrade page | B01 |
| `UI_KaptanKarti.prefab` | captain card template | B08, B11 |
| `UI_Kontrat.prefab` | port contracts | C03 |
| `UI_Magaza.prefab` | premium store | D01 |
| `UI_Reklam.prefab` | rewarded-ad rewards | D03 |
| `UI_Teklif.prefab` | island offer popup | D02 |
| `UI_UstaKarti.prefab` | foreman card template | B07, B11 |

## Dedicated sea reference asset inventory

`01_top`, `02_zirh`, `03_durbun`, `04_tilsim`, `05_riging`, `06_ana_buton`, `07_oto_buton`, `08_geri_butonu`, `09_enerji_ekle_butonu`, `10_rota_sekmesi`, `11_enerji_pili`, `12_bilgi_paneli`, `13_baslik_plakasi`, `14_ekipman_slotu`, `15_stat_karti`, `16_rutbe_yildizi`, `17_kaptan_portresi`, `18_deniz_arka_plani`, `19_oyuncu_gemisi`, `20_dusman_korsan_gemisi`, `21_can_bari`, `22_harita_ganimet_ikonu`, `23_kurtarma_ganimeti_ikonu`, `24_tehlike_ikonu`, `25_kilitli_rota`.

---

# 10. Restyle order for the designer

1. Produce the global chrome kit first: panel, title plate, primary/secondary buttons, route tab, pill, stat cell, slot, stars, close/back buttons, disabled states, and notification badge.
2. Restyle A02 Main HUD and A04 Sea / embarkment HUD together so the visual relationship is explicit.
3. Restyle A05 map and A06 world-space markers so navigation and contextual feedback agree with the HUD.
4. Restyle B01 station, B03 crafting, B04 depot, B07/B08/B09 rosters, then B02/B05/B06 contextual cards.
5. Restyle C progression/event surfaces using the same chrome and reward language.
6. Restyle D store/reward surfaces with a trustworthy, non-aggressive hierarchy.
7. Finish E system/tutorial surfaces and all F shared motion/feedback states.
8. Export portrait-first assets, then create landscape variants only where the current screen supports landscape.

## Delivery requirements for every surface

- Full composition mockup.
- Transparent component exports.
- Normal, selected, pressed, disabled, locked, ready, completed, and error states where applicable.
- Text-free art layers wherever runtime localization is used.
- 9-slice guidance for stretchable panels, bars, and pills.
- Icon socket and text socket positions.
- Portrait 1080x2340 reference plus safe-area notes.
- Explicit reuse mapping to the sea kit assets.
- One dark/low-contrast accessibility check and one colourblind-safe semantic-state check.

## Final art direction sentence

**Every screen should feel like a different room, deck, map, or logbook inside the same bright nautical world — not like a collection of unrelated mobile UI packs.**
