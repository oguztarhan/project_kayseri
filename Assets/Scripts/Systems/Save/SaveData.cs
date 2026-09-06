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
        // The foreman roster, one entry per station index (Game.Core.Foremen). Levels are 0 for a slot
        // nobody has hired; duplicates are the spare cards waiting to be spent on a level. Both are
        // fixed-length arrays rather than keyed lists because the roster is exactly the station list
        // and cannot grow. A save written before the roster existed simply arrives short and is padded
        // on load, which is why there is no version bump for this.
        public int[] foremanLevels = new int[Game.Core.Foremen.Count];
        public int[] foremanDuplicates = new int[Game.Core.Foremen.Count];
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

        // ---- voyages (VoyageService) -----------------------------------------------------------
        // Added WITHOUT a save-version bump, on the precedent ForemanService.Normalise set: a bump
        // wipes every player's progress (see SaveMigration), and this feature only adds fields. A save
        // written before it arrives here with a null list and a null array, which VoyageService pads.
        public List<VoyageState> voyages = new List<VoyageState>();
        public int[] shipLevels = new int[Game.Core.Voyages.ShipTrackCount];  // Hold, Speed, Crew, Berths
        public long salvage;                         // ship-upgrade currency. Unused until V3 — voyages
                                                     // do not pay it yet — but carried so V3 needs no bump
        public int voyagesCompleted;                 // every voyage that came home, won or lost. This is
                                                     // what opens the further routes — see
                                                     // Voyages.TierVoyagesRequired
        public long[] hullReadyUnix = new long[Game.Core.Voyages.MaxBerths];  // 0 = seaworthy. A failed
                                                     // voyage puts its berth out of use until this
                                                     // passes; wall-clock, so the yard mends itself
                                                     // while the game is shut, like every other repair

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
        // precedent as every block above. The roster itself needs no new field: stars ARE
        // foremanLevels, so a save written before the masters rework arrives with its foremen already
        // at the right stars and its banked cards already counted against the same curve.
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

    /// <summary>
    /// One ship, from the moment a berth is claimed to the moment its cards are taken off the dock.
    ///
    /// <see cref="holdSize"/> is LOCKED IN when loading starts rather than read live. The island's
    /// delivery rate moves whenever an upgrade lands, and a hold that silently grew mid-load would
    /// mean the progress bar the player is watching goes backwards after they buy something — the
    /// same trap PileStack fell into when it keyed the ore heap off fill fraction (REMAKE_PLAN §P9).
    /// </summary>
    [Serializable]
    public class VoyageState
    {
        public string island;             // island key whose yard is feeding the hold
        public int berth;                 // which berth this occupies, 0..Voyages.MaxBerths-1
        public int tier;                  // route tier, 0..Voyages.TierCount-1
        public double held;               // bars in the hold so far
        public double holdSize;           // what a full hold is, fixed when loading started
        public long sailedUnix;           // 0 = still loading at the dock
        public long returnsUnix;          // wall clock, so a voyage lands while the app is shut
        public int foreman;               // -1 = nobody aboard. A station index into Foremen; whoever is
                                          // aboard takes ForemanRiskPerLevel off the roll per level.
                                          // They keep their station bonus while at sea — the cost of
                                          // sending one is opportunity, not a visible cut to income
                                          // (Docs/VOYAGES.md §6)
        public bool settled;              // home and rolled, waiting for the player to take it
        public bool succeeded;            // rolled once, on arrival. Always true on tier 0, which has no risk
        public int captain = -1;          // -1 = nobody. An index into Game.Core.Captains.Roster.
                                          // INITIALISED to -1 rather than left at 0, because
                                          // JsonUtility runs field initialisers and only then writes
                                          // the fields the JSON actually carries — so a voyage saved
                                          // before captains existed comes back with nobody aboard
                                          // instead of with captain 0 press-ganged into the job.
                                          // Sits alongside the foreman rather than replacing them:
                                          // the foreman cuts risk, the captain does their own job,
                                          // and the two never move the same number.
        public int payoutCards;
        public int payoutSalvage;         // V3
        public int payoutCharts;          // what the crate is bought with
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
