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
        public int marketCarryLevel;                 // the stack the player carries on his back. One body,
                                                     // one upgrade — deliberately outside MarketYard, which
                                                     // is per island
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
    }

    [Serializable]
    public class ContractOfferSave
    {
        public double units;
        public float seconds;
        public double cash;
        public long gems;
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
