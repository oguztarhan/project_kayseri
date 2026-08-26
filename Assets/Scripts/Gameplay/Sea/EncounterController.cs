using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The sea adventure's state machine, one press wide at every stop: SEARCH pays the energy and
    /// sweeps → the find slides in → the DETAILS CARD holds it at gunpoint (name, signature, power
    /// against ours, the fight-or-pass decision) → the exchange, shot for shot with its procs →
    /// sunk with a drop on the LOOT CARD, or driven off with nothing — back to idle. The maths is
    /// all <see cref="SeaCombat"/>; the picture is all <see cref="Game.UI.SeaFightUI"/>.
    ///
    /// THE DETAILS CARD IS NEW to this shape and it changes what the energy buys: a search buys the
    /// SIGHTING. Declining the fight sails the find away and refunds nothing — the player paid to
    /// know what was out there, and the card is what they paid for.
    ///
    /// EVERYTHING THE FIGHT DOES IS AN EVENT. Crits, dodges, burns, mends, stuns, plunder — each
    /// lands in a fixed ring buffer the UI drains for its floating numbers. The controller owns
    /// WHEN (damage on the ball's impact frame, afflictions at turn starts); Core owns WHAT.
    ///
    /// OTOMATİK is the reference game's Auto: search, confirm, fight, and settle each drop the safe
    /// way (strictly better is worn, the rest is scrapped), until the pool runs dry. It presses the
    /// same public buttons a thumb would — no private fast path, so it can never do anything the
    /// player could not.
    /// </summary>
    public sealed class EncounterController : MonoBehaviour
    {
        public enum Phase { Idle, Searching, Approach, Found, Fight, Sunk, Driven, Loot }
        public enum Step { OurAim, OurBall, TheirAim, TheirBall }

        /// <summary>One thing the fight did, for the theater's floating numbers. Kind is Ev*.</summary>
        public struct FightEvent
        {
            public int Kind;
            public double Amount;
            public bool OnUs;
        }

        public const int EvHit = 0, EvCrit = 1, EvBraced = 2, EvDodge = 3, EvBurnTick = 4,
                         EvMend = 5, EvStunProc = 6, EvBurnProc = 7, EvPlunder = 8, EvSalvo = 9,
                         EvHeld = 10;

        private const int EventRing = 16;

        [Tooltip("Batma/püskürtülme sahnesinin süresi. Bitince ganimet (veya boş eller) gelir.")]
        [SerializeField, Min(0.2f)] private float resolveSeconds = 1.4f;

        [Tooltip("OTOMATİK'in nabzı: boşta yeni arama, kartta savaş onayı, ganimette karar. " +
                 "İnsan temposunda — oto mod da aynı düğmelere basar, hızlı bir arka kapıya değil.")]
        [SerializeField, Min(0.2f)] private float autoSearchSeconds = 0.8f;
        [SerializeField, Min(0.2f)] private float autoConfirmSeconds = 0.9f;
        [SerializeField, Min(0.5f)] private float autoLootSeconds = 1.6f;

        private ExpeditionService _sea;
        private VoyageService _voyages;

        private Phase _phase = Phase.Idle;
        private Step _step = Step.OurAim;
        private float _phaseTime, _stepTime;
        private bool _stepOpened;
        private int _ourSalvoShots, _theirSalvoShots;
        private SeaCombat.Fight _fight;
        private int _spawnKind;
        private bool _wasActive, _auto;
        private long _plunder;

        // The sighting, priced for the details card the moment it is made.
        private SeaCombat.Stats _previewTheirs, _previewOurs;
        private double _previewTheirPower, _previewOurPower;

        // The pending drop, while the player decides.
        private SeaCombat.Item _drop;
        private bool _hasDrop;

        // The event ring. Fixed storage; the UI drains by count.
        private readonly FightEvent[] _events = new FightEvent[EventRing];

        /// <summary>Bumped when a fight ends, so the UI can catch the result without an event pair.</summary>
        public int Stamp { get; private set; }
        public bool LastWon { get; private set; }
        public int LastCharts { get; private set; }
        public int LastSalvage { get; private set; }

        /// <summary>Total fight events ever emitted; read the tail with <see cref="EventAt"/>.</summary>
        public int EventCount { get; private set; }
        public FightEvent EventAt(int index) => _events[index & (EventRing - 1)];

        /// <summary>Bumped when a ball step begins (a salvo re-enters the same step) — the UI's
        /// launch cue.</summary>
        public int BallSerial { get; private set; }

        public Phase State => _phase;
        public Step TurnStep => _step;
        public float PhaseTime => _phaseTime;
        public float StepTime => _stepTime;
        public float ResolveSeconds => resolveSeconds;
        public SeaCombat.Fight Current => _fight;
        public int ThreatKind => _phase == Phase.Fight ? _fight.Kind : _spawnKind;
        public SeaCombat.Tuning Combat => _sea != null ? _sea.Combat : SeaCombat.Tuning.Default;
        public bool Auto => _auto;

        public SeaCombat.Item DropItem => _drop;
        public bool HasDrop => _hasDrop;

        /// <summary>The details card's numbers — theirs against ours, priced at sighting time.</summary>
        public SeaCombat.Stats ThreatSheet => _phase == Phase.Fight ? _fight.Them.S : _previewTheirs;
        public double ThreatPower => _previewTheirPower;
        public double OurPower => _previewOurPower;
        public int MenaceLevel => SeaCombat.Menace(_previewOurPower, _previewTheirPower, Combat);

        public void Init()
        {
            _sea = ServiceLocator.Get<ExpeditionService>();
            _voyages = ServiceLocator.Get<VoyageService>();
        }

        // ------------------------------------------------------------------ orders
        /// <summary>The SEARCH button. Pays one energy and starts the sweep; refused mid-anything.</summary>
        public bool TrySearch()
        {
            if (_phase != Phase.Idle || _sea == null || !_sea.Active) return false;
            if (!_sea.TrySpendEnergy()) return false;
            _spawnKind = SeaCombat.KindFor(_sea.Voyage.sailedUnix, _sea.Finds);
            _sea.CountFind();
            Enter(Phase.Searching);
            return true;
        }

        /// <summary>The details card's SAVAŞ! — the fight starts from the sighting's numbers.</summary>
        public bool Confirm()
        {
            if (_phase != Phase.Found || _sea == null || !_sea.Active) return false;
            _fight = SeaCombat.Begin(_sea.Tier, _spawnKind, _previewOurs, _sea.Combat);
            _plunder = 0L;
            Enter(Phase.Fight);
            EnterStep(Step.OurAim);
            return true;
        }

        /// <summary>The details card's VAZGEÇ — the find sails away; the search's energy bought
        /// the look and is not refunded.</summary>
        public bool Decline()
        {
            if (_phase != Phase.Found) return false;
            Enter(Phase.Idle);
            return true;
        }

        /// <summary>OTOMATİK. Turns itself off when the pool runs dry.</summary>
        public void SetAuto(bool on) => _auto = on;

        public bool TryBroadside()
            => _phase == Phase.Fight && _sea != null && SeaCombat.TryBroadside(ref _fight, _sea.Combat);

        public bool TryBrace()
            => _phase == Phase.Fight && _sea != null && SeaCombat.TryBrace(ref _fight, _sea.Combat);

        public bool TryGrapple()
            => _phase == Phase.Fight && _sea != null && SeaCombat.TryGrapple(ref _fight, _sea.Combat);

        /// <summary>Wear the drop. Whatever the slot held is scrapped into salvage on the way out.</summary>
        public bool EquipDrop()
        {
            if (_phase != Phase.Loot || _sea == null || !_hasDrop) return false;
            _sea.Equip(_drop);
            _hasDrop = false;
            Enter(Phase.Idle);
            return true;
        }

        /// <summary>Refuse the drop for its salvage.</summary>
        public bool ScrapDrop()
        {
            if (_phase != Phase.Loot || _sea == null || !_hasDrop) return false;
            _sea.Scrap(_drop.Grade);
            _hasDrop = false;
            Enter(Phase.Idle);
            return true;
        }

        // ------------------------------------------------------------------ drive
        private void Update()
        {
            if (_sea == null) return;

            if (!_sea.Active)
            {
                if (_wasActive) SettlePendingDrop();
                _phase = Phase.Idle;
                _wasActive = false;
                _auto = false;
                return;
            }
            _wasActive = true;

            float dt = Time.deltaTime;
            _phaseTime += dt;
            switch (_phase)
            {
                case Phase.Idle:
                    if (_auto && _phaseTime >= autoSearchSeconds && !TrySearch() && _sea.Energy <= 0)
                        _auto = false;
                    break;
                case Phase.Searching:
                    if (_phaseTime >= (float)_sea.Combat.SearchSeconds) Enter(Phase.Approach);
                    break;
                case Phase.Approach:
                    if (_phaseTime >= (float)_sea.Combat.ApproachSeconds) Sighted();
                    break;
                case Phase.Found:
                    if (_auto && _phaseTime >= autoConfirmSeconds) Confirm();
                    break;
                case Phase.Fight:
                    TickExchange(dt);
                    break;
                case Phase.Sunk:
                case Phase.Driven:
                    if (_phaseTime >= resolveSeconds) AfterResolve();
                    break;
                case Phase.Loot:
                    if (_auto && _phaseTime >= autoLootSeconds) SettleDropNow();
                    break;
            }
        }

        private void Enter(Phase phase)
        {
            _phase = phase;
            _phaseTime = 0f;
        }

        private void EnterStep(Step step)
        {
            _step = step;
            _stepTime = 0f;
            _stepOpened = false;
            if (step == Step.OurBall || step == Step.TheirBall) BallSerial++;
        }

        /// <summary>The find is alongside: price both sheets ONCE and hold for the decision. The
        /// card shows exactly the numbers the fight would start from.</summary>
        private void Sighted()
        {
            VoyageState v = _sea.Voyage;
            if (v == null) { Enter(Phase.Idle); return; }
            _previewTheirs = SeaCombat.ThreatStats(v.tier, _spawnKind, _sea.Combat);
            _previewOurs = _sea.ShipStats();
            _previewTheirPower = SeaCombat.PowerFor(_previewTheirs, _sea.Combat);
            _previewOurPower = SeaCombat.PowerFor(_previewOurs, _sea.Combat);
            Enter(Phase.Found);
        }

        /// <summary>
        /// The exchange: our turn opens (burn bites, mend patches, a stun steals it), our ball
        /// lands, then theirs by the same rules. Damage lands at each ball's arrival; the ring
        /// buffer narrates every roll for the floating numbers.
        /// </summary>
        private void TickExchange(float dt)
        {
            _stepTime += dt;
            SeaCombat.Tuning t = _sea.Combat;

            switch (_step)
            {
                case Step.OurAim:
                    if (!_stepOpened)
                    {
                        _stepOpened = true;
                        _ourSalvoShots = 0;
                        if (OpenTurn(true, t)) break;
                        if (_fight.Us.Stunned)
                        {
                            SeaCombat.TurnSkipped(ref _fight, true);
                            Emit(EvHeld, 0d, true);
                            EnterStep(Step.TheirAim);
                            break;
                        }
                    }
                    if (_stepTime >= (float)t.TurnAimSeconds) EnterStep(Step.OurBall);
                    break;

                case Step.OurBall:
                    if (_stepTime < (float)t.TurnFlightSeconds) break;
                    SeaCombat.ShotReport ours = SeaCombat.ShotLands(ref _fight, true, Dice(), t);
                    Narrate(ours, true);
                    if (_fight.Over) { Resolve(); break; }
                    if (ours.SalvoProc && _ourSalvoShots < 1)
                    {
                        _ourSalvoShots++;
                        Emit(EvSalvo, 0d, false);
                        EnterStep(Step.OurBall);
                    }
                    else EnterStep(Step.TheirAim);
                    break;

                case Step.TheirAim:
                    if (!_stepOpened)
                    {
                        _stepOpened = true;
                        _theirSalvoShots = 0;
                        if (OpenTurn(false, t)) break;
                        if (!SeaCombat.EnemyWillFire(_fight))
                        {
                            bool held = _fight.Them.Stunned;
                            SeaCombat.TurnSkipped(ref _fight, false);
                            // The derelict's silence is not worth narrating every turn; a HELD
                            // turn — hook or stun — is.
                            if (held) Emit(EvHeld, 0d, false);
                            EnterStep(Step.OurAim);
                            break;
                        }
                    }
                    if (_stepTime >= (float)t.TurnAimSeconds) EnterStep(Step.TheirBall);
                    break;

                case Step.TheirBall:
                    if (_stepTime < (float)t.TurnFlightSeconds) break;
                    SeaCombat.ShotReport theirs = SeaCombat.ShotLands(ref _fight, false, Dice(), t);
                    Narrate(theirs, false);
                    if (_fight.Over) { Resolve(); break; }
                    if (theirs.SalvoProc && _theirSalvoShots < 1)
                    {
                        _theirSalvoShots++;
                        EnterStep(Step.TheirBall);
                    }
                    else EnterStep(Step.OurAim);
                    break;
            }
        }

        /// <summary>A turn's opening ledger. True when the burn just ended the fight.</summary>
        private bool OpenTurn(bool ours, in SeaCombat.Tuning t)
        {
            SeaCombat.TurnReport report = SeaCombat.TurnStart(ref _fight, ours, t);
            if (report.BurnDamage > 0d) Emit(EvBurnTick, report.BurnDamage, ours);
            if (report.Mended > 0d) Emit(EvMend, report.Mended, ours);
            if (!_fight.Over) return false;
            Resolve();
            return true;
        }

        private SeaCombat.ShotRolls Dice() => new SeaCombat.ShotRolls
        {
            Dodge = Random.value,
            Crit = Random.value,
            Stun = Random.value,
            Burn = Random.value,
            Plunder = Random.value,
            Salvo = Random.value,
        };

        /// <summary>One landed (or dodged) ball into events, in the order the eye wants them.</summary>
        private void Narrate(in SeaCombat.ShotReport report, bool ours)
        {
            bool victimUs = !ours;
            if (report.Dodged) { Emit(EvDodge, 0d, victimUs); return; }
            Emit(report.Crit ? EvCrit : report.Braced ? EvBraced : EvHit, report.Damage, victimUs);
            if (report.StunProc) Emit(EvStunProc, 0d, victimUs);
            if (report.BurnProc) Emit(EvBurnProc, 0d, victimUs);
            if (report.Plundered > 0L)
            {
                _plunder += report.Plundered;
                Emit(EvPlunder, report.Plundered, false);
            }
        }

        private void Emit(int kind, double amount, bool onUs)
        {
            _events[EventCount & (EventRing - 1)] =
                new FightEvent { Kind = kind, Amount = amount, OnUs = onUs };
            EventCount++;
        }

        private void Resolve()
        {
            LastWon = _fight.Won;
            LastCharts = 0;
            LastSalvage = 0;
            _hasDrop = false;

            if (_sea != null)
            {
                if (_fight.Won && _voyages != null)
                {
                    Voyages.Tuning vt = _voyages.Tuning;
                    int charts = SeaCombat.ChartsFor(_fight.Tier, _fight.Kind, vt, _sea.Combat);
                    int salvage = SeaCombat.SalvageFor(_fight.Tier, _fight.Kind, vt, _sea.Combat);
                    if (_sea.RegisterKill(charts, salvage + (int)_plunder))
                    {
                        LastCharts = charts;
                        LastSalvage = salvage;
                    }
                    _drop = _sea.RollDrop(_fight.Tier);
                    _hasDrop = true;
                }
                else if (_plunder > 0L)
                {
                    // What YAĞMA grabbed mid-fight was grabbed — a loss costs the energy, never
                    // claws back what the fight already paid.
                    _sea.RegisterKill(0, (int)_plunder);
                }
            }
            _plunder = 0L;

            Stamp++;
            Enter(_fight.Won ? Phase.Sunk : Phase.Driven);
        }

        private void AfterResolve()
        {
            if (LastWon && _hasDrop) Enter(Phase.Loot);
            else Enter(Phase.Idle);
        }

        /// <summary>OTOMATİK's loot decision — the same safe rule the exit uses.</summary>
        private void SettleDropNow()
        {
            if (_phase != Phase.Loot || !_hasDrop) return;
            if (SeaCombat.ItemScore(_drop, _sea.Combat) > _sea.GearScore(_drop.Slot)) EquipDrop();
            else ScrapDrop();
        }

        /// <summary>
        /// The safe answer for a drop left undecided on the way out: strictly better is worn,
        /// anything else is scrapped. Deterministic, and nothing earned is destroyed.
        /// </summary>
        private void SettlePendingDrop()
        {
            if (_phase != Phase.Loot || _sea == null || !_hasDrop) return;
            if (SeaCombat.ItemScore(_drop, _sea.Combat) > _sea.GearScore(_drop.Slot))
                _sea.Equip(_drop);
            else _sea.Scrap(_drop.Grade);
            _hasDrop = false;
        }

        private void OnDestroy() => SettlePendingDrop();
    }
}
