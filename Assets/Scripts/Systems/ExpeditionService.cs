using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// The player's own fighting ship: whether she is out from the port, and everything the fights
    /// run on. The maths is in <see cref="Expedition"/> and <see cref="SeaCombat"/>; this holds the
    /// adventure's persistent state — the energy pool and the four worn items — and answers
    /// questions about it.
    ///
    /// SHE SAILS FROM THE ISLAND'S HARBOUR, NOT WITH A VOYAGE. The dock's cargo ships and their
    /// clocks are <see cref="VoyageService"/>'s business and this service never touches them —
    /// tapping the ship at the pier is the whole ticket. The one thing still read from the dock is
    /// <see cref="Tier"/>: the furthest route the fleet has opened is which waters the fights are
    /// priced for, so combat climbs the same ladder the voyages do.
    ///
    /// NOTHING SESSION-SHAPED IS SAVED, deliberately. Standing on a deck is not progress — what a
    /// fight banked is, and charts, salvage and gear are all written the moment they are won.
    ///
    /// ENERGY IS THE GOVERNOR, AND IT IS SAVED. One search costs one energy; the pool refills on
    /// the wall clock (SeaCombat.EnergyAt), so it comes back while the app is shut and cannot be
    /// farmed by staying aboard.
    ///
    /// GEAR IS SAVED FIELD BY FIELD — grade, hull, shot, secondary — because an item is now a stat
    /// block, not one number. The cached score (seaGearPower) is display sugar and is recomputed,
    /// never trusted. An item from before stats existed (grade set, stats all zero) is grown in
    /// place by <see cref="Normalise"/> so nobody's drop evaporates in an update.
    ///
    /// THE DEPO LIVES HERE TOO, and not in a service of its own. Everything a kept item needs is
    /// already on this object — the worn slots it swaps with, the salvage a scrap pays, the bench a
    /// scrap teaches — so a second owner would be a second authority over the same three fields.
    /// The arithmetic is in <see cref="GearStash"/>; what this half adds is identity, capacity and
    /// the disk. See <see cref="Stow"/>, <see cref="EquipFromStash"/> and <see cref="ScrapAllStash"/>.
    ///
    /// A DEPO MOVE REACHES THE DISK BEFORE THE SCREEN SAYS IT HAPPENED. Every mutation below ends
    /// in one <see cref="Commit"/>, for the reason Docs/PORT_BOARD.md §3 gives: the item leaving,
    /// the salvage arriving and the slot changing hands all live in one <see cref="SaveData"/>, so
    /// one write means the file holds every part of the move or none of it. "Kill the app to get it
    /// back" is the kind of trick players share.
    /// </summary>
    public sealed class ExpeditionService
    {
        /// <summary>
        /// One out-and-back lap past the harbour, in seconds — what the scene's crossing runs on
        /// now that no voyage clock is aboard. Ten minutes matches the pace the shortest real
        /// voyage gave the scene, which is what the lane and the hop-per-second clock were tuned
        /// against; when the lap ends she turns at the home port and puts out again.
        /// </summary>
        private const double PatrolSeconds = 600d;

        private readonly VoyageService _voyages;
        private readonly TimeService _time;
        private readonly SaveData _data;
        private readonly CaptainService _captains;
        private readonly SeaCombat.Tuning _combat;
        private readonly SaveService _save;
        private readonly IAnalytics _analytics;

        /// <summary>Scratch for <see cref="GearStash.ScrapTotal"/> — the depo's grades, refilled on
        /// demand. One buffer rather than an array per call: the label asks on every refresh.</summary>
        private int[] _grades = new int[GearStash.DefaultCapacity];

        /// <summary>The drop dice. Here and not in Core — SeaCombat takes rolls and stays testable.</summary>
        private readonly Random _random = new Random();

        /// <summary>The worn items, rebuilt from the save on demand. One fixed array, zero churn.</summary>
        private readonly SeaCombat.Item[] _loadout = new SeaCombat.Item[SeaCombat.SlotCount];

        /// <summary>Finds made since she put out — only the seed index for KindFor.</summary>
        private int _finds;

        /// <summary>Whether the player is out from the port, and since when. The stamp seeds the
        /// finds and drives the patrol lap; ashore it means nothing.</summary>
        private bool _atSea;
        private long _sailedUnix;

        /// <summary>The island whose port she put out from — what the home port is a picture of.</summary>
        private string _islandKey = string.Empty;

        /// <summary>Raised when the player puts out or comes ashore, or the gear or pool changes.</summary>
        public event Action Changed;

        /// <summary>Set by the bootstrap. Every scrap teaches the workshop bench, and a won
        /// encounter can drop a craft point — both ride through here when it is wired, and the
        /// sea works exactly as before when it is not.</summary>
        public CraftingService Crafting { get; set; }

        public ExpeditionService(VoyageService voyages, TimeService time,
                                 SaveData data = null, CaptainService captains = null,
                                 SeaCombat.Tuning? combat = null, SaveService save = null,
                                 IAnalytics analytics = null)
        {
            _voyages = voyages;
            _time = time;
            _data = data;
            _captains = captains;
            _combat = combat ?? SeaCombat.Tuning.Default;
            _save = save;
            _analytics = analytics;
            Normalise();
        }

        /// <summary>
        /// Pads every gear array, gives a pre-feature save its full pool, and grows older items in
        /// place: a pre-STAT item (grade set, stats zero — the first build baked one power number)
        /// gets its old power spread by the slot's nature, and a pre-DEF/SPD item (hull or shot
        /// set, both new stats zero) gets its slot's Common defence and speed scaled by its grade.
        /// The wearer keeps their item — the padding-on-load contract every other block keeps.
        /// </summary>
        private void Normalise()
        {
            if (_data == null) return;
            _data.seaGearGrade = Fit(_data.seaGearGrade, SeaCombat.SlotCount);
            _data.seaGearPower = Fit(_data.seaGearPower, SeaCombat.SlotCount);
            _data.seaGearSec = Fit(_data.seaGearSec, SeaCombat.SlotCount);
            _data.seaGearHull = Fit(_data.seaGearHull, SeaCombat.SlotCount);
            _data.seaGearShot = Fit(_data.seaGearShot, SeaCombat.SlotCount);
            _data.seaGearSecAmt = Fit(_data.seaGearSecAmt, SeaCombat.SlotCount);
            _data.seaGearDef = Fit(_data.seaGearDef, SeaCombat.SlotCount);
            _data.seaGearSpd = Fit(_data.seaGearSpd, SeaCombat.SlotCount);

            for (int slot = 0; slot < SeaCombat.SlotCount; slot++)
            {
                if (_data.seaGearGrade[slot] <= SeaCombat.GearEmpty) continue;
                if (_data.seaGearHull[slot] > 0d || _data.seaGearShot[slot] > 0d) continue;
                double power = _data.seaGearPower[slot];
                if (power <= 0d) power = 1d;
                switch (slot)
                {
                    case SeaCombat.SlotCannon:   _data.seaGearShot[slot] = power; break;
                    case SeaCombat.SlotPlating:  _data.seaGearHull[slot] = power * 3d; break;
                    case SeaCombat.SlotSpyglass: _data.seaGearHull[slot] = power * 1.5d;
                                                 _data.seaGearShot[slot] = power * 0.5d; break;
                    default:                     _data.seaGearHull[slot] = power; break;
                }
                _data.seaGearSec[slot] = SeaCombat.SecNone;
                _data.seaGearSecAmt[slot] = 0d;
            }

            for (int slot = 0; slot < SeaCombat.SlotCount; slot++)
            {
                if (_data.seaGearGrade[slot] <= SeaCombat.GearEmpty) continue;
                if (_data.seaGearDef[slot] > 0d || _data.seaGearSpd[slot] > 0d) continue;
                int grade = Math.Min(_data.seaGearGrade[slot] - 1, SeaCombat.GradeMult.Length - 1);
                double mult = SeaCombat.GradeMult[grade];
                _data.seaGearDef[slot] = Math.Round(SeaCombat.SlotDef[slot][0] * mult * 10d) / 10d;
                _data.seaGearSpd[slot] = Math.Round(SeaCombat.SlotSpd[slot][0] * mult * 10d) / 10d;
                _data.seaGearPower[slot] = SeaCombat.ItemScore(GearItem(slot), _combat);
            }

            if (_data.seaEnergy < 0)
            {
                _data.seaEnergy = _combat.EnergyMax;
                _data.seaEnergyStampUnix = NowUnix();
            }

            // Only the TABLE's range is enforced here — a save written under a longer route ladder
            // must not index past its end. Whether the stored tier is still UNLOCKED is the getter's
            // business, because the dock's count can move under a session and a clamp written at
            // construction would go stale the moment it did.
            if (_data.seaTier >= Voyages.TierCount) _data.seaTier = Voyages.TierCount - 1;
            if (_data.seaTier < -1) _data.seaTier = -1;

            NormaliseStash();
        }

        /// <summary>
        /// The depo, repaired: unreadable rows dropped, and every survivor holding an id of its own.
        ///
        /// A ROW THAT CANNOT BE READ IS DROPPED, not defaulted. Membership of the list is what makes
        /// an item exist, so a row with no slot or no grade would otherwise draw as a phantom Common
        /// the player never earned. That is the same call <see cref="CraftingService"/>'s Normalise
        /// makes about a broken bench cell, for the same reason.
        ///
        /// AN OVER-FULL DEPO IS LEFT ALONE. Capacity is tuning and may be lowered; throwing the
        /// overflow away would delete earned items because a number in the Inspector moved. The depo
        /// refuses new items instead, until it drains back under the line.
        ///
        /// TWO CARDS A TAP CANNOT TELL APART is the one state this must never leave behind, so the
        /// sequence is first pulled past every stored id and only then are missing and duplicate ids
        /// re-stamped — a fresh id has to be past all of them, not past the ones that came first.
        /// </summary>
        private void NormaliseStash()
        {
            if (_data.gearStash == null) _data.gearStash = new List<GearStashItem>();
            if (_data.gearStashLastId < 0L) _data.gearStashLastId = 0L;

            for (int i = _data.gearStash.Count - 1; i >= 0; i--)
            {
                GearStashItem row = _data.gearStash[i];
                if (row == null
                    || row.grade <= SeaCombat.GearEmpty || row.grade > Captains.GradeCount
                    || row.slot < 0 || row.slot >= SeaCombat.SlotCount)
                {
                    _data.gearStash.RemoveAt(i);
                    continue;
                }
                if (row.id > _data.gearStashLastId) _data.gearStashLastId = row.id;
            }

            for (int i = 0; i < _data.gearStash.Count; i++)
            {
                GearStashItem row = _data.gearStash[i];
                if (row.id > GearStash.NoId && !IdRepeatsBefore(i)) continue;
                row.id = NextStashId();
            }
        }

        private bool IdRepeatsBefore(int index)
        {
            long id = _data.gearStash[index].id;
            for (int i = 0; i < index; i++)
                if (_data.gearStash[i].id == id) return true;
            return false;
        }

        private long NextStashId()
        {
            _data.gearStashLastId = GearStash.NextId(_data.gearStashLastId);
            return _data.gearStashLastId;
        }

        /// <summary>One write per depo move — see the class header.</summary>
        private void Commit() => _save?.Save(_data);

        private static int[] Fit(int[] src, int len)
        {
            if (src != null && src.Length == len) return src;
            var fitted = new int[len];
            if (src != null)
            {
                int n = src.Length < len ? src.Length : len;
                for (int i = 0; i < n; i++) fitted[i] = src[i];
            }
            return fitted;
        }

        private static double[] Fit(double[] src, int len)
        {
            if (src != null && src.Length == len) return src;
            var fitted = new double[len];
            if (src != null)
            {
                int n = src.Length < len ? src.Length : len;
                for (int i = 0; i < n; i++) fitted[i] = src[i];
            }
            return fitted;
        }

        /// <summary>The fight numbers the scene runs on.</summary>
        public SeaCombat.Tuning Combat => _combat;

        private long NowUnix() => _time != null
            ? _time.NowUnix()
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ------------------------------------------------------------------ state
        public bool Active => _atSea;

        /// <summary>
        /// Put out from this island's port. Always allowed — the ship is the player's own and is
        /// never away on anything else; asking again while already out just answers yes.
        /// </summary>
        public bool SetSail(string islandKey)
        {
            if (_atSea) return true;
            _atSea = true;
            _islandKey = islandKey ?? string.Empty;
            _sailedUnix = NowUnix();
            _finds = 0;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Come ashore. Always allowed — nothing out there is abandoned by leaving.</summary>
        public void Ashore()
        {
            if (!_atSea) return;
            _atSea = false;
            Changed?.Invoke();
        }

        /// <summary>When she put out, for seeding the finds. 0 ashore.</summary>
        public long SailedUnix => _atSea ? _sailedUnix : 0L;

        // ------------------------------------------------------------------- read
        /// <summary>How far through the current patrol lap she is, 0..1.</summary>
        public double Progress
        {
            get
            {
                if (!_atSea) return 0d;
                double t = (NowUnix() - _sailedUnix) % PatrolSeconds / PatrolSeconds;
                return t < 0d ? 0d : t;
            }
        }

        /// <summary>Where along the lane, 0 at home and 1 at the lap's far turn.</summary>
        public double LanePosition => Expedition.LanePosition(Progress);

        public bool Outbound => Expedition.Outbound(Progress);

        /// <summary>Seconds until she passes the home port again — the HUD's arrival clock.</summary>
        public double SecondsLeft => _atSea ? PatrolSeconds * (1d - Progress) : 0d;

        // ------------------------------------------------------------------ route
        /// <summary>
        /// The waters the fights are priced for. The dock opens the ladder and the PLAYER picks a
        /// rung on it: a fleet that has reached the far reach may still hunt the coast, which is
        /// the only answer a player who is outgunned has short of not playing.
        ///
        /// An unpicked save (-1) and a pick the fleet can no longer reach both read as the furthest
        /// open route — the ladder is monotonic, so everything at or below <see cref="MaxTier"/> is
        /// unlocked and nothing above it is.
        /// </summary>
        public int Tier
        {
            get
            {
                int max = MaxTier;
                if (_data == null) return max;
                int chosen = _data.seaTier;
                return chosen < 0 || chosen > max ? max : chosen;
            }
        }

        /// <summary>The furthest route the fleet has opened — the ceiling on <see cref="Tier"/>.</summary>
        public int MaxTier => _voyages != null ? _voyages.MaxTier() : 0;

        /// <summary>True when the dock has sailed enough voyages to open this route.</summary>
        public bool TierUnlocked(int tier)
            => tier >= 0 && tier < Voyages.TierCount
               && (_voyages != null ? _voyages.TierUnlocked(tier) : tier == 0);

        /// <summary>Voyages still to sail before <paramref name="tier"/> opens — what a locked
        /// route pill says instead of a name. 0 once it has opened.</summary>
        public int VoyagesToUnlock(int tier)
            => _voyages != null ? _voyages.VoyagesToUnlock(tier)
                                : (TierUnlocked(tier) ? 0 : Voyages.TierVoyagesRequired[
                                       tier < 0 ? 0 : (tier >= Voyages.TierCount ? Voyages.TierCount - 1 : tier)]);

        /// <summary>
        /// Pick the waters. Refused for a route the fleet has not opened — the strip may SHOW a
        /// locked route, because knowing what is ahead is half the reason to sail, but it can never
        /// be entered from the panel. Picking the one already picked answers yes and writes nothing.
        /// </summary>
        public bool TrySetTier(int tier)
        {
            if (_data == null) return false;
            if (!TierUnlocked(tier)) return false;
            if (_data.seaTier == tier) return true;
            _data.seaTier = tier;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Which island's port she put out from — what the home port is a picture of.</summary>
        public string IslandKey => _atSea ? _islandKey : string.Empty;

        // ----------------------------------------------------------------- energy
        public int EnergyMax => _combat.EnergyMax;

        /// <summary>The pool right now, refilled off the wall clock on read.</summary>
        public int Energy
            => _data == null ? 0
             : SeaCombat.EnergyAt(_data.seaEnergy, _data.seaEnergyStampUnix, NowUnix(), _combat);

        /// <summary>Seconds until the next point, for the pill's countdown. 0 at the cap.</summary>
        public double SecondsToNextEnergy
            => _data == null ? 0d
             : SeaCombat.SecondsToNextEnergy(_data.seaEnergy, _data.seaEnergyStampUnix, NowUnix(), _combat);

        /// <summary>
        /// Pay for one search. The refill earned so far is banked into the stored pair first, so a
        /// spend can never eat a fraction of the next point.
        /// </summary>
        public bool TrySpendEnergy()
        {
            if (_data == null) return false;
            long now = NowUnix();
            int have = SeaCombat.EnergyAt(_data.seaEnergy, _data.seaEnergyStampUnix, now, _combat);
            if (have <= 0) return false;
            Settle(now, have, have - 1);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Pour energy into the pool — what a rewarded ad buys. Capped at the pool's size, and it
        /// returns WHAT ACTUALLY LANDED so a caller can refuse to burn a daily charge on a grant
        /// that had nowhere to go.
        /// </summary>
        public int GrantEnergy(int amount)
        {
            if (_data == null || amount <= 0) return 0;
            long now = NowUnix();
            int have = SeaCombat.EnergyAt(_data.seaEnergy, _data.seaEnergyStampUnix, now, _combat);
            int room = _combat.EnergyMax - have;
            if (room <= 0) return 0;
            int given = amount < room ? amount : room;
            Settle(now, have, have + given);
            Changed?.Invoke();
            return given;
        }

        /// <summary>
        /// Write the pool as a SETTLED pair: this many points, true as of <paramref name="now"/>.
        ///
        /// WHY THE STAMP CANNOT SIMPLY BE LEFT ALONE. The pool is stored as (value, stamp) and read
        /// as value + whole periods since the stamp, so a value written without moving the stamp is
        /// immediately handed those periods back — a spend made one second after a point landed
        /// would refund itself, and the pool would never go down. Everything that writes the pool
        /// goes through here for that reason.
        ///
        /// The stamp is rewound to the moment the last point landed rather than set to now, so the
        /// part-earned point in progress survives a spend or a grant. A pool that is FULL at either
        /// end of the write has no refill running through it — nothing was in progress and nothing
        /// is owed — so its countdown starts here instead. A clock that has gone BACKWARDS keeps
        /// the old stamp: no points accrue on the way back, and none may be paid twice on the way
        /// forward.
        /// </summary>
        private void Settle(long now, int have, int value)
        {
            if (value < 0) value = 0;
            if (value > _combat.EnergyMax) value = _combat.EnergyMax;

            long stamp = _data.seaEnergyStampUnix;
            double regen = _combat.EnergyRegenSeconds;
            if (have >= _combat.EnergyMax || value >= _combat.EnergyMax) _data.seaEnergyStampUnix = now;
            else if (regen > 0d && now > stamp)
                _data.seaEnergyStampUnix = now - (long)((now - stamp) % regen);

            _data.seaEnergy = value;
        }

        /// <summary>How many finds since she put out — the seed index for the next one's kind.
        /// Reset by <see cref="SetSail"/>, so every trip deals its own deck.</summary>
        public int Finds => _atSea ? _finds : 0;

        public void CountFind()
        {
            if (_atSea) _finds++;
        }

        // ------------------------------------------------------------------- gear
        /// <summary>The worn item's grade in Captains.Grade terms, or -1 for an empty slot.</summary>
        public int GearGrade(int slot)
            => _data != null && slot >= 0 && slot < SeaCombat.SlotCount
             ? _data.seaGearGrade[slot] - 1 : -1;

        /// <summary>The worn item's cached score, 0 for an empty slot — the compare number.</summary>
        public int GearScore(int slot)
            => _data != null && slot >= 0 && slot < SeaCombat.SlotCount
               && _data.seaGearGrade[slot] > SeaCombat.GearEmpty
             ? _data.seaGearPower[slot] : 0;

        /// <summary>The worn item, whole. Grade is -1 when the slot is empty.</summary>
        public SeaCombat.Item GearItem(int slot)
        {
            if (_data == null || slot < 0 || slot >= SeaCombat.SlotCount
                || _data.seaGearGrade[slot] <= SeaCombat.GearEmpty)
                return new SeaCombat.Item { Slot = slot, Grade = -1 };
            return new SeaCombat.Item
            {
                Slot = slot,
                Grade = _data.seaGearGrade[slot] - 1,
                Sec = _data.seaGearSec[slot],
                Hull = _data.seaGearHull[slot],
                Shot = _data.seaGearShot[slot],
                Def = _data.seaGearDef[slot],
                Spd = _data.seaGearSpd[slot],
                SecAmt = _data.seaGearSecAmt[slot],
            };
        }

        /// <summary>
        /// The four worn items by slot, for <see cref="SeaCombat.OurStats"/>. The array is owned
        /// here and refreshed in place — treat it as a read.
        /// </summary>
        public SeaCombat.Item[] Loadout()
        {
            for (int i = 0; i < SeaCombat.SlotCount; i++) _loadout[i] = GearItem(i);
            return _loadout;
        }

        /// <summary>
        /// Who is on the bridge: the best-levelled captain the player owns, first of the roster on
        /// a tie, -1 with nobody pulled. Chosen here rather than by the player because the ship at
        /// the pier has no assignment screen — the strongest officer simply takes her out, and a
        /// new pull is felt at sea the moment it happens.
        /// </summary>
        public int CaptainAboard
        {
            get
            {
                if (_captains == null) return -1;
                int best = -1, bestLevel = 0;
                for (int c = 0; c < Captains.Count; c++)
                {
                    int level = _captains.Level(c);
                    if (level > bestLevel) { best = c; bestLevel = level; }
                }
                return best;
            }
        }

        /// <summary>
        /// The ship's whole sheet right now: crew track, the best owned captain, and the worn
        /// gear. This is what the panel prints and what a fight is born from — derived on every
        /// call, stored nowhere.
        /// </summary>
        public SeaCombat.Stats ShipStats()
        {
            int captain = CaptainAboard;
            int level = _captains != null && captain >= 0 ? _captains.Level(captain) : 0;
            int crew = _data != null && _data.shipLevels != null ? _data.shipLevels[Voyages.Crew] : 0;
            Captains.Tuning ct = _captains != null ? _captains.Tuning : Captains.Tuning.Default;
            return SeaCombat.OurStats(captain, level, crew, Loadout(), ct, _combat);
        }

        /// <summary>The panel's headline for <see cref="ShipStats"/>.</summary>
        public double ShipPower() => SeaCombat.PowerFor(ShipStats(), _combat);

        /// <summary>
        /// Wear a drop. Whatever was in the slot is scrapped into salvage on the way out — one
        /// decision per drop, no inventory to manage, and nothing earned is ever destroyed.
        /// Returns the salvage the old item paid (0 for an empty slot).
        /// </summary>
        public long Equip(in SeaCombat.Item item)
        {
            if (_data == null || item.Slot < 0 || item.Slot >= SeaCombat.SlotCount || item.Grade < 0)
                return 0L;
            int displaced = _data.seaGearGrade[item.Slot] - 1;
            long scrap = displaced >= 0 ? SeaCombat.ScrapFor(displaced) : 0L;
            if (scrap > 0L) _data.salvage += scrap;
            if (displaced >= 0) Crafting?.GrantScrapXp(displaced);
            WearCells(item);
            Changed?.Invoke();
            return scrap;
        }

        /// <summary>
        /// Write an item into its worn slot. Whatever was there is overwritten and NOT paid for —
        /// every caller settles the displaced item first, either by scrapping it
        /// (<see cref="Equip"/>) or by putting it on the shelf (<see cref="EquipFromStash"/>).
        /// </summary>
        private void WearCells(in SeaCombat.Item item)
        {
            _data.seaGearGrade[item.Slot] = item.Grade + 1;
            _data.seaGearSec[item.Slot] = item.Sec;
            _data.seaGearHull[item.Slot] = item.Hull;
            _data.seaGearShot[item.Slot] = item.Shot;
            _data.seaGearDef[item.Slot] = item.Def;
            _data.seaGearSpd[item.Slot] = item.Spd;
            _data.seaGearSecAmt[item.Slot] = item.SecAmt;
            _data.seaGearPower[item.Slot] = SeaCombat.ItemScore(item, _combat);
        }

        /// <summary>Leave a worn slot empty. The item is gone by the time this runs — scrapped by
        /// <see cref="ScrapWorn"/> or shelved by <see cref="StowWorn"/>.</summary>
        private void ClearWornCells(int slot)
        {
            _data.seaGearGrade[slot] = SeaCombat.GearEmpty;
            _data.seaGearSec[slot] = SeaCombat.SecNone;
            _data.seaGearHull[slot] = 0d;
            _data.seaGearShot[slot] = 0d;
            _data.seaGearDef[slot] = 0d;
            _data.seaGearSpd[slot] = 0d;
            _data.seaGearSecAmt[slot] = 0d;
            _data.seaGearPower[slot] = 0;
        }

        /// <summary>Refuse a drop for its salvage instead.</summary>
        public void Scrap(int grade)
        {
            if (_data == null) return;
            _data.salvage += SeaCombat.ScrapFor(grade);
            Crafting?.GrantScrapXp(grade);
            Changed?.Invoke();
        }

        /// <summary>Strip a WORN item off for its salvage — the gear popup's SÖK. The slot is left
        /// empty; returns what the item paid, 0 when there was nothing to strip.</summary>
        public long ScrapWorn(int slot)
        {
            if (_data == null || slot < 0 || slot >= SeaCombat.SlotCount
                || _data.seaGearGrade[slot] <= SeaCombat.GearEmpty) return 0L;
            long scrap = SeaCombat.ScrapFor(_data.seaGearGrade[slot] - 1);
            _data.salvage += scrap;
            Crafting?.GrantScrapXp(_data.seaGearGrade[slot] - 1);
            ClearWornCells(slot);
            Changed?.Invoke();
            return scrap;
        }

        // ------------------------------------------------------------------- depo
        /// <summary>How many items the shelf holds. Tuning; never negative.</summary>
        public int StashCapacity => _combat.StashCapacity > 0 ? _combat.StashCapacity : 0;

        /// <summary>What is on the shelf right now.</summary>
        public int StashCount => _data != null && _data.gearStash != null ? _data.gearStash.Count : 0;

        /// <summary>Whether one more item fits. False also means <see cref="Stow"/> will refuse.</summary>
        public bool StashHasRoom => _data != null && GearStash.HasRoom(StashCount, StashCapacity);

        /// <summary>The shelf's nth item, whole. Grade is -1 off either end.</summary>
        public SeaCombat.Item StashItemAt(int index)
        {
            if (_data == null || _data.gearStash == null
                || index < 0 || index >= _data.gearStash.Count)
                return new SeaCombat.Item { Slot = 0, Grade = -1 };
            return ItemFromRow(_data.gearStash[index]);
        }

        /// <summary>The nth item's id — the handle every depo action takes. 0 off either end.</summary>
        public long StashIdAt(int index)
            => _data != null && _data.gearStash != null && index >= 0 && index < _data.gearStash.Count
             ? _data.gearStash[index].id : GearStash.NoId;

        /// <summary>
        /// What emptying the shelf would pay, without emptying it — the hurda in the return and the
        /// bench's lesson in <paramref name="xp"/>. This is the number the button prints, and it is
        /// the same sum <see cref="ScrapAllStash"/> pays, because both go through
        /// <see cref="GearStash.ScrapTotal"/> over the same grades.
        /// </summary>
        public long ScrapAllValue(out long xp)
        {
            int n = FillGrades();
            return GearStash.ScrapTotal(_grades, n, out xp);
        }

        /// <summary>
        /// Put an item on the shelf — a fresh craft being kept, or gear coming off a slot. Refused
        /// when the shelf is full or the item is not one: a refusal costs nothing, which is what
        /// lets the caller offer the choice without having to check first.
        /// </summary>
        public bool Stow(in SeaCombat.Item item)
        {
            if (_data == null || item.Grade < 0 || item.Grade >= Captains.GradeCount
                || item.Slot < 0 || item.Slot >= SeaCombat.SlotCount)
                return false;
            if (!GearStash.HasRoom(StashCount, StashCapacity)) return false;

            _data.gearStash.Add(RowFromItem(item, NextStashId()));
            Commit();
            _analytics?.Log("gear_stow", "grade", item.Grade);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Take a worn item off and KEEP it. The one gear move in the game that pays nothing: the
        /// player is not refusing the item, they are parking it, so no hurda and no lesson — those
        /// are what <see cref="ScrapWorn"/> is for. Refused with the slot empty or the shelf full,
        /// and in both cases the item stays exactly where it was.
        /// </summary>
        public bool StowWorn(int slot)
        {
            if (_data == null || slot < 0 || slot >= SeaCombat.SlotCount
                || _data.seaGearGrade[slot] <= SeaCombat.GearEmpty) return false;
            if (!GearStash.HasRoom(StashCount, StashCapacity)) return false;

            _data.gearStash.Add(RowFromItem(GearItem(slot), NextStashId()));
            ClearWornCells(slot);
            Commit();
            _analytics?.Log("gear_unequip", "slot", slot);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Wear the shelved item with this id, and put whatever it displaces back on the shelf in
        /// its place. A SWAP, not a purchase: nothing is scrapped, nothing is paid, and the shelf's
        /// count does not move — which is what makes the depo the one place a player can try an
        /// item without spending the item that was already good.
        ///
        /// The displaced item takes a NEW id on the way in. That is the double-tap guard: the id
        /// that was just worn is gone for good, so a second press of the same card lands on nothing
        /// and is refused rather than swapping the pair straight back.
        ///
        /// False when the id is not on the shelf — a card that moved under the finger, which costs
        /// nothing and changes nothing.
        /// </summary>
        public bool EquipFromStash(long id)
        {
            if (_data == null || id <= GearStash.NoId) return false;
            int index = IndexOfStashId(id);
            if (index < 0) return false;

            SeaCombat.Item taken = ItemFromRow(_data.gearStash[index]);
            if (taken.Grade < 0) return false;
            SeaCombat.Item displaced = GearItem(taken.Slot);

            WearCells(taken);
            if (displaced.Grade >= 0) _data.gearStash[index] = RowFromItem(displaced, NextStashId());
            else _data.gearStash.RemoveAt(index);

            Commit();
            _analytics?.Log("gear_equip_stash", "grade", taken.Grade);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Feed one shelved item back: its hurda, and its lesson to the bench. Returns the hurda
        /// paid and reports the XP in <paramref name="xp"/>.
        ///
        /// IDEMPOTENT BY ID, which is the whole reason ids exist here. A second press of a card that
        /// has already gone — a double tap, a stale screen — finds nothing and pays 0/0 rather than
        /// paying for the item that slid into the row behind it.
        ///
        /// <paramref name="xp"/> is the ladder's value for that grade, which is what the bench
        /// learns when one is wired; with no bench it is still what the item was worth teaching.
        /// </summary>
        public long ScrapFromStash(long id, out long xp)
        {
            xp = 0L;
            if (_data == null || id <= GearStash.NoId) return 0L;
            int index = IndexOfStashId(id);
            if (index < 0) return 0L;

            int grade = _data.gearStash[index].grade - 1;
            long scrap = SeaCombat.ScrapFor(grade);
            xp = Game.Core.Crafting.SalvageXpFor(grade);
            _data.salvage += scrap;
            _data.gearStash.RemoveAt(index);
            Crafting?.GrantScrapXp(grade);
            Commit();
            _analytics?.Log("gear_scrap_stash", "grade", grade);
            Changed?.Invoke();
            return scrap;
        }

        /// <summary>
        /// Empty the shelf — the depo's PARÇALA button. Pays every item's hurda and teaches the
        /// bench once per item, because the lesson is per item wherever the scrap happened.
        /// 0/0 on an empty shelf, and nothing is written.
        /// </summary>
        public long ScrapAllStash(out long xp)
        {
            xp = 0L;
            if (_data == null || StashCount == 0) return 0L;

            int n = FillGrades();
            long scrap = GearStash.ScrapTotal(_grades, n, out xp);
            _data.salvage += scrap;
            _data.gearStash.Clear();
            for (int i = 0; i < n; i++) Crafting?.GrantScrapXp(_grades[i]);
            Commit();
            _analytics?.Log("gear_scrap_all", "count", n);
            Changed?.Invoke();
            return scrap;
        }

        /// <summary>Where the item with this id sits, or -1. The list is 20 long at most.</summary>
        private int IndexOfStashId(long id)
        {
            if (_data.gearStash == null) return -1;
            for (int i = 0; i < _data.gearStash.Count; i++)
                if (_data.gearStash[i].id == id) return i;
            return -1;
        }

        /// <summary>Copies the shelf's grades into <see cref="_grades"/>; returns how many.</summary>
        private int FillGrades()
        {
            int n = StashCount;
            if (n == 0) return 0;
            if (_grades.Length < n) _grades = new int[n];
            for (int i = 0; i < n; i++) _grades[i] = _data.gearStash[i].grade - 1;
            return n;
        }

        private static GearStashItem RowFromItem(in SeaCombat.Item item, long id) => new GearStashItem
        {
            id = id,
            slot = item.Slot,
            grade = item.Grade + 1,
            sec = item.Sec,
            hull = item.Hull,
            shot = item.Shot,
            def = item.Def,
            spd = item.Spd,
            secAmt = item.SecAmt,
        };

        private static SeaCombat.Item ItemFromRow(GearStashItem row) => new SeaCombat.Item
        {
            Slot = row.slot,
            Grade = row.grade - 1,
            Sec = row.sec,
            Hull = row.hull,
            Shot = row.shot,
            Def = row.def,
            Spd = row.spd,
            SecAmt = row.secAmt,
        };

        /// <summary>Roll a win's drop: which slot, how rare (the worn spyglass's grade leans on the
        /// odds), and the item's whole stat block. The only dice in the feature.</summary>
        public SeaCombat.Item RollDrop(int tier)
        {
            int slot = SeaCombat.RollSlot(_random.NextDouble());
            int grade = SeaCombat.RollGrade(_random.NextDouble(), tier,
                                            SeaCombat.SpyglassLuck(GearGrade(SeaCombat.SlotSpyglass)),
                                            _combat);
            return SeaCombat.ItemFor(slot, tier, grade, _random.NextDouble(), _combat);
        }

        /// <summary>
        /// Bank a win's trickle: charts to the captain roster, salvage to the shipyard — the two
        /// closed loops the sea pays into. Refused ashore; bounded upstream by the energy a search
        /// cost. YAĞMA procs ride through here too, as salvage.
        /// </summary>
        public bool RegisterKill(int charts, int salvage)
        {
            if (!_atSea) return false;
            if (charts > 0 && _captains != null) _captains.AddCharts(charts);
            if (salvage > 0 && _data != null) _data.salvage += salvage;
            // The workshop's point drop rides the same win, on the same dice-in-the-service rule.
            Crafting?.TryDropPoint(_random.NextDouble());
            Changed?.Invoke();
            return true;
        }
    }
}
