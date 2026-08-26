using System;
using Game.Core;

namespace Game.Systems
{
    /// <summary>
    /// Which voyage the player has gone out with, and where she is. The maths is in
    /// <see cref="Expedition"/> and <see cref="SeaCombat"/>; this holds the adventure's persistent
    /// state — the energy pool and the four worn items — and answers questions about it.
    ///
    /// NOTHING SESSION-SHAPED IS SAVED, deliberately. Standing on a deck is not progress — the
    /// voyage is, and <see cref="VoyageService"/> already persists all of it on the wall clock.
    ///
    /// IT CANNOT CHANGE A VOYAGE. Every write below aims AWAY from the voyage: kills and plunder
    /// bank charts and salvage into their own closed loops, gear changes only these fights.
    /// Docs/FIVE_LAYERS.md §4 is the reason: active sailing may only ever ADD to an outcome, and
    /// the safest way to hold a rule like that is to build the window with no handles on it.
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
        private readonly VoyageService _voyages;
        private readonly TimeService _time;
        private readonly SaveData _data;
        private readonly CaptainService _captains;
        private readonly SeaCombat.Tuning _combat;

        /// <summary>The drop dice. Here and not in Core — SeaCombat takes rolls and stays testable.</summary>
        private readonly Random _random = new Random();

        /// <summary>The worn items, rebuilt from the save on demand. One fixed array, zero churn.</summary>
        private readonly SeaCombat.Item[] _loadout = new SeaCombat.Item[SeaCombat.SlotCount];

        /// <summary>Finds made this session, per voyage — only the seed index for KindFor.</summary>
        private VoyageState _findVoyage;
        private int _finds;

        /// <summary>Which berth the player went out with. -1 = nobody is at sea with anyone.</summary>
        private int _berth = -1;

        /// <summary>Raised when the player boards or comes ashore, or the gear or pool changes.</summary>
        public event Action Changed;

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
        /// Pads every gear array, gives a pre-feature save its full pool, and grows a pre-STAT item
        /// (grade set, stats zero — the first build baked one power number) into today's shape: the
        /// old power becomes the slot's nature (a cannon's was shot, a plating's was protection),
        /// no secondary, score recomputed. The wearer keeps their item — the padding-on-load
        /// contract every other block keeps.
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
        public int Berth => _berth;

        /// <summary>The voyage the player is out with, or null.</summary>
        public VoyageState Voyage
        {
            get
            {
                if (_berth < 0 || _voyages == null) return null;
                VoyageState v = _voyages.At(_berth);
                // She may have come home and been claimed while the scene was open, and a berth can be
                // re-let to a different voyage entirely. Either way the ship the player boarded is gone.
                return v != null && v.sailedUnix > 0L ? v : null;
            }
        }

        public bool Active => Voyage != null;

        /// <summary>
        /// True when this berth can be sailed with: there is a ship in it and she is actually at sea.
        /// A hold still filling at the dock has nowhere to take anybody.
        /// </summary>
        public bool CanBoard(int berth)
        {
            if (_voyages == null) return false;
            VoyageState v = _voyages.At(berth);
            return v != null && v.sailedUnix > 0L && !v.settled;
        }

        /// <summary>Go out with the ship in this berth. Refused when there is nothing sailing there.</summary>
        public bool Board(int berth)
        {
            if (!CanBoard(berth)) return false;
            _berth = berth;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Come ashore. Always allowed — the voyage carries on without the player.</summary>
        public void Ashore()
        {
            if (_berth < 0) return;
            _berth = -1;
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------- read
        /// <summary>How far through her crossing she is, 0..1.</summary>
        public double Progress
        {
            get
            {
                VoyageState v = Voyage;
                return v == null ? 0d : Expedition.Progress(v.sailedUnix, v.returnsUnix, NowUnix());
            }
        }

        /// <summary>Where along the lane, 0 at home and 1 at the far port.</summary>
        public double LanePosition => Expedition.LanePosition(Progress);

        public bool Outbound => Expedition.Outbound(Progress);

        public double SecondsLeft
        {
            get
            {
                VoyageState v = Voyage;
                return v == null ? 0d : Expedition.SecondsLeft(v.returnsUnix, NowUnix());
            }
        }

        /// <summary>The route she is on, for the scene's own dressing and for the HUD's caption.</summary>
        public int Tier
        {
            get { VoyageState v = Voyage; return v != null ? v.tier : 0; }
        }

        /// <summary>Which island's yard she sailed from — what the home port is a picture of.</summary>
        public string IslandKey
        {
            get { VoyageState v = Voyage; return v != null ? v.island : string.Empty; }
        }

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

        /// <summary>How many finds this voyage has made — the seed index for the next one's kind.</summary>
        public int Finds
        {
            get { VoyageState v = Voyage; if (v == null) return 0; Sync(v); return _finds; }
        }

        public void CountFind()
        {
            VoyageState v = Voyage;
            if (v == null) return;
            Sync(v);
            _finds++;
        }

        private void Sync(VoyageState v)
        {
            if (ReferenceEquals(v, _findVoyage)) return;
            _findVoyage = v;
            _finds = 0;
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
        /// The ship's whole sheet right now: crew track, the boarded voyage's captain, and the worn
        /// gear. This is what the panel prints and what a fight is born from — derived on every
        /// call, stored nowhere.
        /// </summary>
        public SeaCombat.Stats ShipStats()
        {
            VoyageState v = Voyage;
            int captain = v != null ? v.captain : -1;
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
            long scrap = _data.seaGearGrade[item.Slot] > SeaCombat.GearEmpty
                       ? SeaCombat.ScrapFor(_data.seaGearGrade[item.Slot] - 1) : 0L;
            if (scrap > 0L) _data.salvage += scrap;
            _data.seaGearGrade[item.Slot] = item.Grade + 1;
            _data.seaGearSec[item.Slot] = item.Sec;
            _data.seaGearHull[item.Slot] = item.Hull;
            _data.seaGearShot[item.Slot] = item.Shot;
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
            _data.seaGearGrade[slot] = SeaCombat.GearEmpty;
            _data.seaGearSec[slot] = SeaCombat.SecNone;
            _data.seaGearHull[slot] = 0d;
            _data.seaGearShot[slot] = 0d;
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
        /// Bank a win's trickle: charts to the captain roster, salvage to the shipyard — the same
        /// two closed loops the voyage itself pays, at a fraction, on top. Refused ashore; bounded
        /// upstream by the energy a search cost. YAĞMA procs ride through here too, as salvage.
        /// </summary>
        public bool RegisterKill(int charts, int salvage)
        {
            if (Voyage == null) return false;
            if (charts > 0 && _captains != null) _captains.AddCharts(charts);
            if (salvage > 0 && _data != null) _data.salvage += salvage;
            Changed?.Invoke();
            return true;
        }
    }
}
