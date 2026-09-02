using System;
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
                                 SeaCombat.Tuning? combat = null)
        {
            _voyages = voyages;
            _time = time;
            _data = data;
            _captains = captains;
            _combat = combat ?? SeaCombat.Tuning.Default;
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
        }

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

        /// <summary>
        /// The waters the fights are priced for: the furthest route the fleet has opened. The one
        /// read this service keeps from the dock — combat climbs the ladder the voyages climb.
        /// </summary>
        public int Tier => _voyages != null ? _voyages.MaxTier() : 0;

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
        /// spend can never eat a fraction of the next point — the stamp only moves when the value does.
        /// </summary>
        public bool TrySpendEnergy()
        {
            if (_data == null) return false;
            long now = NowUnix();
            int have = SeaCombat.EnergyAt(_data.seaEnergy, _data.seaEnergyStampUnix, now, _combat);
            if (have <= 0) return false;
            bool wasFull = have >= _combat.EnergyMax;
            _data.seaEnergy = have - 1;
            // A full pool has no running refill; the countdown starts at the moment it stops being
            // full. A partial pool keeps its stamp so the point in progress is not thrown away.
            if (wasFull) _data.seaEnergyStampUnix = now;
            Changed?.Invoke();
            return true;
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
            _data.seaGearGrade[item.Slot] = item.Grade + 1;
            _data.seaGearSec[item.Slot] = item.Sec;
            _data.seaGearHull[item.Slot] = item.Hull;
            _data.seaGearShot[item.Slot] = item.Shot;
            _data.seaGearDef[item.Slot] = item.Def;
            _data.seaGearSpd[item.Slot] = item.Spd;
            _data.seaGearSecAmt[item.Slot] = item.SecAmt;
            _data.seaGearPower[item.Slot] = SeaCombat.ItemScore(item, _combat);
            Changed?.Invoke();
            return scrap;
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
            _data.seaGearGrade[slot] = SeaCombat.GearEmpty;
            _data.seaGearSec[slot] = SeaCombat.SecNone;
            _data.seaGearHull[slot] = 0d;
            _data.seaGearShot[slot] = 0d;
            _data.seaGearDef[slot] = 0d;
            _data.seaGearSpd[slot] = 0d;
            _data.seaGearSecAmt[slot] = 0d;
            _data.seaGearPower[slot] = 0;
            Changed?.Invoke();
            return scrap;
        }

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
