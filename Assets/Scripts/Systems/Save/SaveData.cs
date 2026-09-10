using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Root serializable save payload (Unity JsonUtility). Station levels are a list keyed by station
    /// id so any number persist without a schema change.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        // Rollout switch. It is intentionally default-on so a save written before this field existed
        // enters the portrait presentation without changing any legacy progression collection.
        public bool UsePortraitShipyard = true;
        public ShipyardProgression shipyard = new ShipyardProgression();
        public int version = SaveMigration.CurrentVersion;  // stamped on write; a mismatch on load
                                                            // wipes the run — see SaveMigration
        // One-time, non-destructive retirement of prestige. Existing investors become this frozen
        // permanent income bonus; new players remain at x1.
        public bool prestigeRetired;
        public double legacyIncomeMultiplier = 1d;
        public long savedUnixSeconds;
        public long lastDailyClaimUnix;               // daily reward (GDD §11)
        public int dailyStreak;                       // consecutive daily claims; drives the 7-day ladder
        public double incomeRatePerSec;               // offline earnings (GDD §7)
        public WalletData wallet = new WalletData();
        public List<StationLevel> stationLevels = new List<StationLevel>();
        // The master roster, one entry per index into Game.Core.Foremen.Roster — fifteen masters,
        // three at each of the five stations. Stars are 0 for a card nobody has found; cards are the
        // spares waiting to be spent on a star. Fixed-length arrays rather than keyed lists because
        // the roster is a published table and cannot grow at runtime; a short or missing array is
        // padded on load, which is why there is no version bump for this.
        //
        // NEW NAMES ON PURPOSE. These replace foremanLevels/foremanDuplicates, which were eight
        // entries indexed by STATION and carried a star count that also decided rarity. Fifteen
        // entries indexed by master is a different meaning at every position, so reusing the old field
        // names would have quietly reinterpreted every existing save — a mine master's stars read as a
        // Common mine master's, a market master's read as a Rare deposit master's. Renaming makes an
        // old save arrive empty instead of arriving wrong.
        public int[] masterStars = new int[Game.Core.Foremen.Count];
        public int[] masterCards = new int[Game.Core.Foremen.Count];

        // Who is actually working at each of the five stations: an index into Foremen.Roster, or -1
        // for nobody. You own as many as the chests give you and put ONE of a station's three to work.
        // Validated on read rather than trusted — see Foremen.ActiveAt.
        public int[] masterActive = Game.Core.Foremen.NewActive();
        public GoalSaveData goals = new GoalSaveData();
        public List<string> unlockedMountains = new List<string>();  // mountain ids the player has bought (GDD §4/§8)
        public List<string> unlockedIslands = new List<string>();    // island ids the player has bought (archipelago progression)
        public List<StationLevel> islandLevels = new List<StationLevel>();  // per-island upgrade level (archipelago progression)
        public List<IslandRate> islandRates = new List<IslandRate>();       // what each idle island pays while you are away
        public int freeRewardDay;                    // UTC day number the free-reward charges were last reset on
        public List<FreeRewardState> freeRewards = new List<FreeRewardState>();  // rewarded-ad slots (GDD §10)
        public bool adsRemoved;                      // the remove-ads purchase, so it survives a restart
        public List<string> purchasedOffers = new List<string>();  // one-time offer skus already owned
        // StoreKit/Play can redeliver an unconfirmed order after an app kill or network failure. The
        // reward and this id are written in the same save before the order is confirmed, making that
        // redelivery harmless instead of paying the same transaction twice.
        public List<string> processedIapTransactions = new List<string>();
        public double stationSpeedMultiplier = 1d;  // Maden Patronu: permanent station clock multiplier
        public double offlineEfficiencyBonus;        // permanent offline perks bought from the store, added
        public long offlineCapBonusSeconds;          // on top of OfflineConfig's base efficiency and cap
        public long starterOfferSeenUnix;            // legacy account-wide timestamp; migrated once below
        // Legacy field above is migrated once into these per-island windows. A newly entered island gets
        // its own 48-hour starter window; buying one island's pack must not close the next island's.
        public bool starterOffersMigrated;
        public List<StarterOfferWindow> starterOfferWindows = new List<StarterOfferWindow>();
        public string pendingStarterIsland = "";      // island captured before StoreKit/Play opens;
                                                     // survives an app kill between payment and grant
        public double dailyRewardBonusMult;          // permanent daily-reward multiplier bought from the store;
                                                     // the effective multiplier is 1 + this, so 1 means doubled
        public int freeRewardBonusCharges;           // extra rewarded-ad charges per slot per day, bought
        public long dailyGemStipend;                 // flat gems added to every daily-reward claim, bought;
                                                     // deliberately outside dailyRewardBonusMult, see the card
        public double boostMultiplier;               // running income boost and when it expires, as wall-clock
        public long boostEndUnix;                    // unix — the store sells boosts measured in hours, and an
                                                     // idle player spends most of those hours with the app shut
        public int tutorialStep;                     // 0 = the opening has never been played, 100 = it has
        public List<string> tutorialTipsSeen = new List<string>();  // one-shot hints already fired, by id
        public bool firstSaleSeen;                   // the one-off celebration when the chain first pays out

        // ---- liman kontratı ------------------------------------------------------------------
        // Offers/reward survive a restart. Active jobs keep their remaining play-time while the game
        // is closed; ship travel/cooldown uses the wall clock so a waiting ship can arrive while away.
        public ContractSaveData contract = new ContractSaveData();

        // ---- pop-up teklifler (OfferPopupUI) --------------------------------------------------
        // The IAP skus are consumable and shared by all eight islands, so purchasedOffers cannot
        // gate these: buying the small pack on coal would lock it on copper too. The pop-up keeps
        // its own "island:tier" receipts instead, and leaves the shared sku out of that list.
        public List<string> islandOffersBought = new List<string>();
        public long offerShownUnix;                  // when the last pop-up opened; paces the next one
        public int offerDayNumber;                   // UTC day the daily counter belongs to
        public int offerShownToday;
        public int offerWeekNumber;                  // UTC week the weekly counter belongs to
        public int offerShownThisWeek;
        public int offerDeclineStreak;               // pop-ups closed without buying, in a row; a sale
                                                     // resets it and each step widens the gap
        public string offerLiveKey = "";             // the offer the HUD button opens ("" = none). Armed
        public long offerLiveStartUnix;              // silently; the clock only starts (and this is only
                                                     // stamped) once the pop-up has actually interrupted
        public string offerPoppedKey = "";           // the offer already shown as a pop-up. Separate from
                                                     // offerLiveKey so the button can stay lit for an offer
                                                     // the player has already been asked about once

        // ---- bakım (MaintenanceService) --------------------------------------------------------
        // One row per island the player has actually stood on, made on demand. An island with no row
        // is a perfect island, which is what a save written before this feature existed describes.
        public List<IslandCondition> conditions = new List<IslandCondition>();
        public long conditionStampUnix;              // when the empire's wear was last worked out. The
                                                     // whole archipelago decays off this one clock: it
                                                     // is the player who was away, not each island
                                                     // separately (0 = never evaluated, so the first
                                                     // launch after the update wears nothing)
        public long shieldEndUnix;                   // the maintenance shield bought in the store: while
                                                     // this is in the future no island wears at all. A
                                                     // wall-clock deadline rather than a countdown, for
                                                     // the same reason a boost is one — the whole point
                                                     // of the product is that it runs while the game is
                                                     // shut. Empire-wide, like the stamp above it

        // ---- market yards (MarketService) ------------------------------------------------------
        public List<MarketYard> marketYards = new List<MarketYard>();  // one row per island, made on demand

        // The idle-shop rows that replace marketYards. Additive on purpose: the legacy list above stays
        // on disk as MIGRATION INPUT ONLY and has no runtime consumer any more, so a save written by an
        // older build still converts, and a save written by this build still opens in one that has not
        // shipped yet. Deliberately NOT guarded by SaveMigration.CurrentVersion — NeedsReset treats a
        // version change as a wipe, and this feature is not worth anyone's empire.
        public List<IdleMarketYard> idleMarketYards = new List<IdleMarketYard>();
        public int idleShopSchemaVersion;
        public int marketCarryLevel;                 // the stack the player carries on his back. One body,
                                                     // one upgrade — deliberately outside MarketYard, which
                                                     // is per island

        // ---- ship tracks -------------------------------------------------------------------------
        // What is left of the dock after the voyage feature was removed. The tracks are KEPT rather
        // than dropped because Crew still feeds sea-combat power and the levels are progress players
        // paid for; dropping them would quietly rebalance every existing save downward. They have no
        // shop of their own at the moment — the yard tab that sold them went with the dock — so they
        // sit at whatever level they reached until an upgrade screen is rehomed.
        //
        // Fields REMOVED here (voyages, voyagesCompleted, hullReadyUnix) are simply absent from new
        // saves and ignored in old ones. No save-version bump: a bump wipes every player's progress
        // (see SaveMigration), and JSON tolerates keys it no longer has a field for.
        public int[] shipLevels = new int[Game.Core.Voyages.ShipTrackCount];  // Hold, Speed, Crew, Berths
        public long salvage;                         // ship-upgrade currency, paid by won fights

        // ---- captains (CaptainService) ---------------------------------------------------------
        // The sea roster, and the crate that fills it. Added WITHOUT a save-version bump, like the two
        // blocks above: a save written before captains existed arrives with a null array and a zero
        // chart balance, which is a player who has never opened a crate.
        //
        // Charts are a SECOND CLOSED LOOP beside salvage — earned only by sailing, spent only on
        // crates. They are not gems on purpose: the foreman roster already eats gems, and pricing both
        // rosters in one currency would put them in competition for the same wallet, which is the trap
        // Docs/VOYAGES.md R1 names for cash faucets and which reads the same way for sinks.
        public long charts;
        public int[] captainLevels = new int[Game.Core.Captains.Count];      // 0 = never pulled
        public int[] captainDuplicates = new int[Game.Core.Captains.Count];  // spare cards toward a level
        public int crateSinceEpic;         // pulls since an Epic-or-better; drives the short pity
        public int crateSinceLegendary;    // pulls since a Legendary-or-better; the long pity and the ramp
        public int cratesOpened;           // lifetime, for the crate screen's own readout

        // ---- deniz macerasi (ExpeditionService) ------------------------------------------------
        // The sea adventure's two persistent things: the energy pool that paces the grind, and the
        // gear the grind pays. Added WITHOUT a save-version bump like every block above.
        //
        // Energy is stored as (value, stamp) and refilled on READ off the wall clock — the same
        // shape as boosts and repairs, so it regenerates while the app is shut and a device clock
        // is the only authority. -1 means "never initialised": a save from before the feature
        // starts with a FULL pool rather than an empty one, because the first thing the feature
        // shows a returning player should not be a wait.
        public int seaEnergy = -1;
        public long seaEnergyStampUnix;
        // Won fights, lifetime. This is what opens the further routes — see
        // Voyages.TierFightsRequired and ExpeditionService.MaxTier. It replaces voyagesCompleted,
        // which only the dock could ever move; a save from before this field starts at zero, so a
        // returning player re-earns the ladder by fighting rather than by sailing.
        public int seaFightsWon;
        // Which waters the fights are priced for. -1 means "follow the fleet": a player who never
        // touches the route strip always fights in the furthest water the dock has opened, which is
        // what every save before this field did and what a new one should do. Once they PICK, the
        // pick stands — including a shallower route than they could sail, which is the whole point
        // of the choice. A stored tier above what is unlocked (a reset fleet) reads as the furthest
        // open one; ExpeditionService.Normalise keeps the stored value inside the table's range.
        public int seaTier = -1;
        // One item per slot (Game.Core.SeaCombat.Slot*). Grade is Captains.Grade + 1 so 0 = empty;
        // stats are baked at drop time so a tuning change never silently re-arms old items. Power
        // is the item's cached SCORE (recomputable; kept for display). The stat arrays arrived
        // when items grew whole stat blocks — a grade with all-zero stats is a pre-stat item and
        // ExpeditionService.Normalise grows it in place, wearer keeps the item.
        public int[] seaGearGrade = new int[Game.Core.SeaCombat.SlotCount];
        public int[] seaGearPower = new int[Game.Core.SeaCombat.SlotCount];
        public double[] seaGearHull = new double[Game.Core.SeaCombat.SlotCount];
        public double[] seaGearShot = new double[Game.Core.SeaCombat.SlotCount];
        public int[] seaGearSec = new int[Game.Core.SeaCombat.SlotCount];
        public double[] seaGearSecAmt = new double[Game.Core.SeaCombat.SlotCount];
        // Defence and speed arrived when items grew the full core-stat block. An older item
        // (grade set, both zero) is grown in place by ExpeditionService.Normalise from its
        // slot's Common table, so nobody's drop gets slower or softer than a fresh Common.
        public double[] seaGearDef = new double[Game.Core.SeaCombat.SlotCount];
        public double[] seaGearSpd = new double[Game.Core.SeaCombat.SlotCount];

        // ---- atölye (CraftingService) ----------------------------------------------------------
        // The workshop bench: points are its closed currency (earned at sea and on the dock, spent
        // only here), XP is LIFETIME salvage learning — the level is always recomputed from it, so
        // the pair cannot drift. Added WITHOUT a save-version bump like every block above; a save
        // from before the bench arrives all-zero, which is a player who has never crafted.
        public long craftPoints;
        public long craftXp;
        public int craftGatesCleared;                // retooling stops passed (levels 10/20/30)
        public long craftGateEndUnix;                // the running stop's wall-clock deadline; 0 = none
        // The crafted-but-undecided item, one cell in the seaGear shape (grade+1, 0 = empty). The
        // point is spent and THIS is what it bought, saved in the same breath — an app killed
        // between crafting and choosing must find the item on the bench, not an empty slot and a
        // missing point.
        public int craftPendingGrade;
        public int craftPendingSlot;
        public int craftPendingSec;
        public double craftPendingHull;
        public double craftPendingShot;
        public double craftPendingDef;
        public double craftPendingSpd;
        public double craftPendingSecAmt;
        // Rewarded auto-craft is a wall-clock window, just like the income boost. Points arriving
        // while this is in the future are spent and resolved immediately by CraftingService.
        public long autoCraftEndUnix;

        // ---- depo (ExpeditionService, Game.Core.GearStash) --------------------------------------
        // The workshop's shelf: gear that has been crafted or taken off and is being kept rather
        // than worn or scrapped. Added WITHOUT a save-version bump on the precedent every block
        // above set — SaveMigration.NeedsReset is an equality test, so a bump deletes every live
        // save on every device. A save from before the depo arrives with an empty list, which is a
        // player who has kept nothing, and that is exactly right.
        //
        // A LIST, NOT A FIXED ARRAY, because unlike the four worn slots the depo's size is tuning
        // (SeaCombat.Tuning.StashCapacity) and may move in either direction. Shrinking it never
        // deletes anything: ExpeditionService.Normalise leaves an over-full depo alone and simply
        // refuses new items until it drains.
        //
        // gearStashLastId only ever goes up. An id is how a tap finds its item (the depo re-orders
        // itself whenever something leaves the middle of it), and re-using one would let a
        // double-tap scrap whatever slid into the row — see Docs/PORT_BOARD.md §3 for the contract
        // board that learned this first.
        public List<GearStashItem> gearStash = new List<GearStashItem>();
        public long gearStashLastId;

        // ---- madencilik teçhizatı (MiningGearService) -------------------------------------------
        // The captain's mining loadout: four worn slots (pickaxe, helmet, bag, lantern) that feed
        // the island's income multiplier, and the Mining Points that pay for a craft. Added WITHOUT
        // a save-version bump, on the same precedent as every block above — a save from before this
        // feature arrives all-zero, which is an empty loadout and an empty purse, exactly right for
        // a player who has never been assigned a captain to duty.
        //
        // Points accrue off the wall clock like seaEnergy — (value, stamp), refilled on read — so
        // the pool keeps filling while the app is shut. miningGearGrade follows the seaGear
        // convention: Grade+1 per slot, 0 = empty.
        public long miningPoints;
        public long miningPointsStampUnix;
        public int[] miningGearGrade = new int[Game.Core.MiningGear.SlotCount];
        public long miningScrap;   // this loadout's own small refund currency, paid when a craft loses the compare

        // ---- chapters (ChapterService) ---------------------------------------------------------
        // Added WITHOUT a save-version bump, on the same precedent as the voyages block above. One
        // row per chapter, made on demand, keyed on the island's own save key and carrying its OWN
        // beat array — a single flat array would have re-labelled every chapter after the first the
        // moment a beat was appended. A save written before chapters existed arrives with an empty
        // list, which is a player who has claimed nothing; because beats are OBSERVED rather than
        // reported (see ChapterService), their existing islands light up whatever they already earned.
        public List<ChapterState> chapters = new List<ChapterState>();

        // ---- usta sandigi (ForemanService) ------------------------------------------------------
        // The master chest's two pieces of state. Added WITHOUT a save-version bump, on the same
        // precedent as every block above — a chest count and a claim stamp mean the same thing before
        // and after the roster rework, so unlike masterStars these keep their names.
        //
        // The free chest is stored as WHEN THE LAST ONE WAS TAKEN rather than as a countdown, so it
        // ticks while the app is shut and cannot be farmed by leaving it open — the same shape as
        // boosts, repairs and sea energy. 0 means never claimed, which reads as one waiting.
        public long masterFreeChestClaimUnix;
        public int masterChestsOpened;               // lifetime, for the chest shelf's own readout

        // ---- canlı etkinlikler (LiveEventService) ------------------------------------------------
        // One row per event the player has actually touched; an event nobody has opened has no row at
        // all, so a config full of future events costs an untouched save nothing. Added WITHOUT a
        // save-version bump, on the precedent every block above set: a null list is a player who has
        // seen no event, which is exactly what every existing save is.
        public List<LiveEventState> liveEvents = new List<LiveEventState>();

        // ---- destek numarasi (PlayerIdentity) ---------------------------------------------------
        // The id a player quotes to support. Minted the first time a screen asks for it and never
        // again — see Game.Core.PlayerId for the shape and PlayerIdentity for when it is written.
        //
        // Added WITHOUT a save-version bump, on the precedent every block above set: an empty string is
        // a player who has not opened the support sheet yet, which is every existing save.
        //
        // It is the one field in here that survives SaveMigration.Reset. A wipe throws away progress,
        // not identity: a ticket filed the day before an economy reset still has to name the same
        // device the day after, or the desk is answering a stranger.
        public string playerId = "";

        // ---- lig (LadderService) -----------------------------------------------------------------
        // The three-day league. Added WITHOUT a save-version bump, on the precedent every block above
        // set: a default-constructed row is a player who has never entered a season, which is exactly
        // what every existing save is.
        public LadderState ladder = new LadderState();

        // ---- kart koleksiyonu (CardCollectionService) ---------------------------------------------
        // The card collection. ONE NESTED OBJECT rather than nine fields scattered across this root,
        // because nine loose fields is nine chances for the next feature to sit between two of them
        // and make the block impossible to read.
        //
        // Added WITHOUT a save-version bump, on the precedent every block above set: an empty
        // collection is a player who has opened no packs, which is exactly what every existing save
        // is. Docs/PLAN_14 records that this is the one Plan #05 condition the feature knowingly
        // breaks — ownership and pity counters cannot be derived from anything already saved.
        public CardCollectionSaveData cardCollection = new CardCollectionSaveData();

        // ---- evcil hayvanlar (PetService) -------------------------------------------------------
        // Pearls are the pet chest's own closed loop, the same shape charts and salvage already
        // keep: earned only at sea (a small per-win trickle — see ExpeditionService.RegisterWin) and
        // spent only on pet chests. Kept at the root, beside charts and salvage, rather than inside
        // WalletData — those two are the shared cash/gem wallet every system can reach into, and a
        // third closed loop living there would read as spendable on everything the other two are.
        //
        // Added WITHOUT a save-version bump, on the precedent every block above set: a save written
        // before pets existed arrives with zero pearls and an empty grid, which is a player who has
        // never opened a pet chest.
        public long pearls;
        public PetSaveData pets = new PetSaveData();
    }

    /// <summary>
    /// The league's whole persisted state — deliberately small, because most of what a ladder appears
    /// to hold is DERIVED rather than stored.
    ///
    /// The running season's score is <c>lifetime[BarsSold] - baseline</c>, both of which are already
    /// in the save, so the score survives an app kill without being written twice and can never drift
    /// from the counter it is measured off. What genuinely has to be kept is here:
    ///
    /// <see cref="bestScore"/> exists for one reason — a season that closes while the app is shut. The
    /// delta would then include bars sold AFTER the window ended, and would settle a closed season
    /// with a score that was never earned inside it. So the number is snapshotted while the season is
    /// open and settled from the snapshot.
    ///
    /// <see cref="settledSeasons"/> is the idempotency key list, the same shape
    /// <c>processedIapTransactions</c> keeps: a settlement delivered twice must pay once.
    /// </summary>
    [Serializable]
    public class LadderState
    {
        public string seasonId = "";      // the season baseline/bestScore belong to; "" = never entered
        public long baseline;             // lifetime[BarsSold] when this season opened
        public long bestScore;            // the best seen WHILE the season was open (see the class note)
        public long bestAchievedUnix;     // when bestScore was first reached — the ranking tie-break
        public List<string> settledSeasons = new List<string>();
        public List<LadderInboxRow> inbox = new List<LadderInboxRow>();
    }

    /// <summary>
    /// One card's progress. Addressed by ID rather than by index, unlike
    /// <see cref="SaveData.masterStars"/> and <see cref="SaveData.captainLevels"/>, and deliberately:
    /// those two are fixed-length arrays over rosters that are frozen in code, while the card
    /// catalogue is expected to GROW. A positional array would tie every future content set to the
    /// order the launch cards happen to sit in.
    ///
    /// A row exists only for a card the player has actually met. A card in the catalogue with no row
    /// is at level 0, which is also what every pre-collection save says about all twenty-four.
    /// </summary>
    [Serializable]
    public class CardCollectionProgress
    {
        public string cardId = "";        // CardCollectionCatalogue.Card.Id — a save key, never renamed
        public int level;                 // 0 = never drawn; only a pack draw can make this 1
        public int duplicates;            // spare copies banked toward the next level
        public bool seen;                 // cleared the "NEW" badge — presentation only
    }

    /// <summary>
    /// The card collection's whole persisted state.
    ///
    /// THE PITY COUNTERS ARE SAVED, and they have to be: a guarantee the player is forty packs into
    /// is a thing they have earned, and losing it on an app kill would make the published worst case
    /// a lie. They are the reason this feature cannot meet Plan #05's "add no save fields" condition
    /// — see Docs/PLAN_14.
    ///
    /// THE DAILY PACK IS A UTC DAY NUMBER, not a countdown, the same shape
    /// <see cref="GoalSaveData.day"/> keeps: it ticks while the app is shut, cannot be farmed by
    /// leaving the app open, and a clock rolled backwards only ever delays it. It banks at most one.
    ///
    /// <see cref="claimedSetRewardIds"/> is the idempotency key list, the same shape
    /// <see cref="LadderState.settledSeasons"/> and <c>processedIapTransactions</c> keep: a set
    /// completed twice must pay once. It is deliberately NOT what decides whether the permanent set
    /// bonus is live — that is derived from the cards owned, so a player who never taps Claim keeps
    /// the bonus they earned.
    /// </summary>
    [Serializable]
    public class CardCollectionSaveData
    {
        public List<CardCollectionProgress> progress = new List<CardCollectionProgress>();
        public List<string> claimedSetRewardIds = new List<string>();

        public int unopenedPacks;         // claimed but not yet opened; the reveal is the player's moment
        public int dailyPackDay = int.MinValue;   // UTC day the last free pack was claimed for
        public int packsOpened;           // lifetime, for the collection screen's own readout

        public int pullsSinceEpic;
        public int pullsSinceLegendary;

        /// <summary>
        /// Makes a loaded block safe to read, whatever wrote it. Called once on load, before any
        /// screen or service reads a card.
        ///
        /// UNKNOWN CARD IDS ARE KEPT, NOT DROPPED. A row naming a card this build does not carry is
        /// not corruption — it is a card from a build the player has since moved off, or one a
        /// staged rollout has not given them yet. Dropping it would silently destroy a collection on
        /// a downgrade, so it rides along untouched and is simply never shown.
        ///
        /// Returns true when something actually had to be changed, so a load that repaired a save
        /// can write the repair back rather than leaving it to be redone on every launch.
        /// </summary>
        public bool Normalise()
        {
            bool changed = false;

            if (progress == null) { progress = new List<CardCollectionProgress>(); changed = true; }
            if (claimedSetRewardIds == null) { claimedSetRewardIds = new List<string>(); changed = true; }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = progress.Count - 1; i >= 0; i--)
            {
                CardCollectionProgress row = progress[i];

                // A null row or one naming nothing addresses no card and can never be shown or
                // repaired — it is the one case where dropping is the only honest answer.
                if (row == null || string.IsNullOrEmpty(row.cardId))
                {
                    progress.RemoveAt(i);
                    changed = true;
                    continue;
                }

                // Two rows for one card would let one shadow the other's duplicates depending on
                // which the lookup happened to reach first. The walk is backwards, so the row kept
                // is the LAST written — the one a later append would have added, and therefore the
                // likelier of the two to be current.
                if (!seenIds.Add(row.cardId))
                {
                    progress.RemoveAt(i);
                    changed = true;
                    continue;
                }

                int level = Game.Core.CardCollection.ClampLevel(row.level);
                if (level != row.level) { row.level = level; changed = true; }

                if (row.duplicates < 0) { row.duplicates = 0; changed = true; }

                // Duplicates on an unowned card are LEFT ALONE. They cannot unlock it — only a pack
                // draw creates level 1, and CardCollection.CanLevel refuses a level-0 card whatever
                // it is holding — so there is nothing to defend against, and zeroing them would
                // throw away real progress if a future build ever hands out copies directly.
            }

            for (int i = claimedSetRewardIds.Count - 1; i >= 0; i--)
            {
                string id = claimedSetRewardIds[i];
                if (string.IsNullOrEmpty(id) || claimedSetRewardIds.IndexOf(id) != i)
                {
                    claimedSetRewardIds.RemoveAt(i);
                    changed = true;
                }
            }

            if (unopenedPacks < 0) { unopenedPacks = 0; changed = true; }
            if (packsOpened < 0) { packsOpened = 0; changed = true; }
            if (pullsSinceEpic < 0) { pullsSinceEpic = 0; changed = true; }
            if (pullsSinceLegendary < 0) { pullsSinceLegendary = 0; changed = true; }

            return changed;
        }

        /// <summary>The row for a card id, or null for one the player has never met. Linear because
        /// the catalogue is two dozen cards and this is asked on load and on a pack open, never in a
        /// frame.</summary>
        public CardCollectionProgress Find(string cardId)
        {
            if (string.IsNullOrEmpty(cardId) || progress == null) return null;
            for (int i = 0; i < progress.Count; i++)
                if (progress[i] != null && string.Equals(progress[i].cardId, cardId, StringComparison.Ordinal))
                    return progress[i];
            return null;
        }

        /// <summary>The row for a card id, created at level 0 if the player has not met it yet.</summary>
        public CardCollectionProgress FindOrAdd(string cardId)
        {
            CardCollectionProgress row = Find(cardId);
            if (row != null) return row;

            if (progress == null) progress = new List<CardCollectionProgress>();
            row = new CardCollectionProgress { cardId = cardId };
            progress.Add(row);
            return row;
        }

        public bool HasClaimedSetReward(string setId)
        {
            if (string.IsNullOrEmpty(setId) || claimedSetRewardIds == null) return false;
            for (int i = 0; i < claimedSetRewardIds.Count; i++)
                if (string.Equals(claimedSetRewardIds[i], setId, StringComparison.Ordinal)) return true;
            return false;
        }
    }

    /// <summary>
    /// The pet system's whole persisted state.
    ///
    /// COUNTS IS ONE FLAT GRID, species-major, the shape <see cref="SaveData.masterStars"/> and
    /// <see cref="SaveData.captainLevels"/> already use for a roster that is frozen in code. A fixed
    /// six species times five rarities times five stars is 150 cells, and a fused pet DECREASES a
    /// cell and INCREASES another rather than raising one card's level in place — see
    /// <see cref="Game.Core.Pets.TryFuse"/> — which is why this is a count grid and not a level array.
    ///
    /// EQUIPPED IS FIXED AT THE SYSTEM'S WHOLE SLOT COUNT, not at how many are unlocked right now.
    /// Slot unlocks are progression (<c>PetService.SlotsUnlocked</c>) and can only ever widen; storing
    /// the unlocked count here too would be a second source of truth that a changed gate could
    /// disagree with. -1 is empty, the same convention <see cref="SaveData.masterActive"/> keeps.
    /// </summary>
    [Serializable]
    public class PetSaveData
    {
        public int[] counts = new int[Game.Core.Pets.CountsLength];
        public int[] equippedSpecies = NewEquipped();
        public int chestSinceEpic;
        public int chestSinceLegendary;
        public int chestsOpened;

        private static int[] NewEquipped()
        {
            var a = new int[Game.Core.Pets.SlotCount];
            for (int i = 0; i < a.Length; i++) a[i] = -1;
            return a;
        }

        /// <summary>Makes a loaded block safe to read, whatever wrote it. Returns true when something
        /// actually had to be changed, the same contract <see cref="CardCollectionSaveData.Normalise"/>
        /// keeps.</summary>
        public bool Normalise()
        {
            bool changed = false;

            if (counts == null || counts.Length != Game.Core.Pets.CountsLength)
            {
                var fitted = new int[Game.Core.Pets.CountsLength];
                if (counts != null)
                {
                    int n = counts.Length < fitted.Length ? counts.Length : fitted.Length;
                    for (int i = 0; i < n; i++) fitted[i] = counts[i];
                }
                counts = fitted;
                changed = true;
            }
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] < 0) { counts[i] = 0; changed = true; }

            if (equippedSpecies == null || equippedSpecies.Length != Game.Core.Pets.SlotCount)
            {
                var fitted = NewEquipped();
                if (equippedSpecies != null)
                {
                    int n = equippedSpecies.Length < fitted.Length ? equippedSpecies.Length : fitted.Length;
                    for (int i = 0; i < n; i++) fitted[i] = equippedSpecies[i];
                }
                equippedSpecies = fitted;
                changed = true;
            }
            for (int i = 0; i < equippedSpecies.Length; i++)
                if (!Game.Core.Pets.Exists(equippedSpecies[i]) && equippedSpecies[i] != -1)
                {
                    equippedSpecies[i] = -1;
                    changed = true;
                }

            if (chestSinceEpic < 0) { chestSinceEpic = 0; changed = true; }
            if (chestSinceLegendary < 0) { chestSinceLegendary = 0; changed = true; }
            if (chestsOpened < 0) { chestsOpened = 0; changed = true; }

            return changed;
        }
    }

    /// <summary>
    /// One closed season's result, waiting to be collected. Keyed by the season id, which is what
    /// makes the grant idempotent: the flag is written and saved BEFORE the reward is handed over,
    /// exactly as <c>ChapterService.Claim</c> and <c>IapTransactionJournal</c> gate theirs.
    ///
    /// NOTHING HERE EXPIRES. A row waits indefinitely, like an unclaimed contract at the port — an
    /// inbox that swept itself would be the first thing in this game that punished absence
    /// (Docs/FIVE_LAYERS.md R3).
    /// </summary>
    [Serializable]
    public class LadderInboxRow
    {
        public string seasonId;
        public int rank;                  // final 1-based rank; 0 when the player did not take part
        public int tier;                  // bracket index, or -1 for no payout
        public bool claimed;
    }

    /// <summary>
    /// One event's local state. Keyed by the event's own immutable id rather than an index, the shape
    /// <see cref="ChapterState"/> uses and for the same reason: adding an event to the config must not
    /// re-label the rows already in a save.
    ///
    /// TWO ARRAYS, AND ONLY ONE OF THEM IS EVER DROPPED. <see cref="progress"/> is re-tunable content
    /// and is cleared when <see cref="configVersion"/> falls behind the definition — see
    /// <c>LiveEvents.ProgressSurvives</c>. <see cref="claimed"/> is NOT: a reward already handed over
    /// must never be handed over twice, so the flags survive every version bump. That asymmetry is the
    /// whole idempotency story, and it is why the two are separate arrays instead of one struct.
    /// </summary>
    [Serializable]
    public class LiveEventState
    {
        public string id;                 // the config's immutable event id
        public int configVersion;         // the version progress below was earned under
        public long[] progress;           // one counter per slot; padded on load
        public bool[] claimed;            // one flag per slot; padded on load, never cleared
    }

    /// <summary>
    /// One chapter's collected beats. <see cref="claimed"/> is padded on load rather than sized here,
    /// so appending a beat to <see cref="Game.Core.Chapters"/> costs no migration.
    /// </summary>
    [Serializable]
    public class ChapterState
    {
        public string id;                 // island key: "coal", "copper", …
        public bool[] claimed = new bool[Game.Core.Chapters.BeatCount];
        public bool introSeen;            // the chapter's opening card has been shown once
    }

    [Serializable]
    public class StarterOfferWindow
    {
        public string island;
        public long startedUnix;
    }

    [Serializable]
    public class ContractSaveData
    {
        public bool initialized;
        public int state;
        public int lastResult;
        public int streak;
        public double difficulty = 1d;
        public double target;
        public double done;
        public double rewardCash;
        public long rewardGems;
        public float secondsLeft;
        public float stateSpan;
        public long stateEndUnix;
        public string unitWord = "COAL";
        public double processingPerMinute;
        public double cashPerMinute;
        public List<ContractOfferSave> offers = new List<ContractOfferSave>();

        // Offer identity. nextOfferId is the sequence the board stamps cards from; activeOfferId is
        // which card the running job was signed off, and doubles as the "this save knows about ids"
        // flag — a save written before them restores it as 0, which is how activeCards is told apart
        // from a legitimate zero.
        public int nextOfferId;
        public int activeOfferId;
        public int activeCards;

        // The meter the board on the table was cut against, frozen for its life. Persisted rather than
        // recomputed because it is what makes the board reproducible and what says whether the empire
        // has since outgrown it — a restored board has to be able to answer both.
        public double boardProcPerMinute;
        public double boardCashPerMinute;

        // Swaps spent on the ship currently at the pier. Reset when a NEW ship docks and by nothing
        // else — persisted, and written to disk the moment one is spent, so killing the app cannot
        // refund it.
        public int rerollsUsed;
    }

    [Serializable]
    public class ContractOfferSave
    {
        public double units;
        public float seconds;
        public double cash;
        public long gems;

        // id is unique for the life of the save and is what a tap is matched against, so a card that
        // was replaced between being drawn and being pressed cannot be accepted in the new one's
        // place. cards is the foreman payout promised on the card, frozen here so the claim pays what
        // the player was shown rather than recomputing it later.
        public int id;
        public int tier;
        public int cards;
    }

    /// <summary>
    /// One kept item on the workshop's shelf — the same stat block the four worn slots hold, plus
    /// the id that makes it addressable while the shelf re-orders around it.
    ///
    /// GRADE IS grade + 1, the shape every gear cell in this file uses: 0 is not a Common, it is a
    /// broken row, and <c>ExpeditionService.Normalise</c> drops it. Being in the list is what makes
    /// an item exist, so a row that cannot be read is worth nothing and pretending otherwise would
    /// put a phantom Common in the grid.
    /// </summary>
    [Serializable]
    public class GearStashItem
    {
        public long id;
        public int slot;
        public int grade;        // Captains.Grade + 1; 0 = a broken row, dropped on load
        public int sec;
        public double hull;
        public double shot;
        public double def;
        public double spd;
        public double secAmt;
    }

    /// <summary>
    /// One island's market yard: what has been bought in it, and what is sitting in it right now.
    ///
    /// Hires are stored as a LEVEL rather than a flag plus a level — 0 means nobody does that job, 1..5
    /// is a worker. One number can't drift out of step with itself, and it is the same number
    /// <see cref="Game.Core.MarketFlow.JobRate"/> takes.
    /// </summary>
    [Serializable]
    public class MarketYard
    {
        public string id;                 // island key: "coal", "copper", …
        public int depositSlots = 1;      // pads on the floor — how much the yard can hold
        public int queueSlots = 1;        // places in the line — how fast it can sell
        public int hireCarry;             // 0 = the job is yours. 1..MarketFlow.MaxHireLevel = a hire
        public int hireServe;
        public int hireCollect;
        public double stock;              // bars on the pads, waiting to be sold
        public double deliveredPerMin;    // measured delivery rate, kept so the yard keeps filling while
                                          // nobody is simulating the island that feeds it
    }

    /// <summary>
    /// One island's state of repair: how worn each of its eight stations is, and whatever repair is
    /// running on it right now.
    ///
    /// <see cref="station"/> is handed straight to that island's <see cref="Game.Core.IslandEconomy"/>
    /// and read from the simulation every frame, so it is SHARED rather than copied — the same trick
    /// the level arrays already use, and for the same reason: two copies of what the player owns is
    /// two things that can disagree.
    /// </summary>
    /// <summary>
    /// The goal system's state. Lifetime totals are what the achievement ladder reads; the day
    /// baseline is what makes a daily task a DELTA rather than another counter to keep — the day's
    /// progress is simply lifetime minus what it was when the day rolled.
    /// </summary>
    [Serializable]
    public class GoalSaveData
    {
        public int day = -1;                                              // UTC day the dailies belong to
        public long[] lifetime = new long[Game.Core.Goals.MetricCount];
        public long[] dayBaseline = new long[Game.Core.Goals.MetricCount];
        public bool[] dailyClaimed = new bool[Game.Core.Goals.DailySlots];
        public int[] tiersClaimed = new int[Game.Core.Goals.Ladder.Length];
        public int week = int.MinValue;                                   // Monday-based UTC week
        public long[] weekBaseline = new long[Game.Core.Goals.MetricCount];
        public string[] weeklyMilestonesClaimed = new string[0];          // immutable tier IDs
    }

    [Serializable]
    public class IslandCondition
    {
        public string id;                 // island key: "coal", "copper", …
        public float[] station;           // 0..1 per IslandEconomy station index; 1 = as new

        // ---- the repairs in flight ----
        // A repair is a wall-clock deadline rather than a countdown, for the same reason a boost is:
        // the player will start one and immediately put the phone down, and a timer measured against
        // session uptime would still be waiting for them when they got back.
        //
        // PER STATION, because several crews can be out at once. It used to be one deadline for the
        // whole row, which meant tapping a second building did nothing until the first was finished —
        // and on an island that has been left for a fortnight, every building wants seeing to.
        public float[] repairFrom;        // condition each station started its repair at, so the bar
                                          // can climb from where it actually was
        public long[] repairEnd;          // per station; 0 = nobody is on that one
        public int[] repairSecs;          // per station, what each repair was quoted at, for the bar
        public long bonusEndUnix;         // the maintenance bonus won by putting the whole island right
    }

    /// <summary>
    /// An idle island's measured $/min, kept while the player is standing somewhere else. It is a
    /// double rather than a <see cref="StationLevel"/> because prestige lifts the income ceiling: the
    /// top islands cap at 110M/min, which overflows an int once the multiplier passes about 19×.
    /// </summary>
    [Serializable]
    public class IslandRate
    {
        public string id;
        public double perMin;
    }

    /// <summary>One rewarded-ad slot's daily state: how many of today's charges are spent, and when.</summary>
    [Serializable]
    public class FreeRewardState
    {
        public string id;
        public int used;
        public long lastWatchUnix;
    }

    [Serializable]
    public class WalletData
    {
        public BigDouble cash;
        public long gems;
        // Kept for save compatibility after prestige was retired. Runtime systems no longer write or
        // read these fields; removing them would make old JsonUtility payloads harder to migrate safely.
        public double investors;
        public BigDouble lifetimeCash;
    }

    [Serializable]
    public class StationLevel
    {
        public string id;
        public int level;
    }
}
