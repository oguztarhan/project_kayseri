using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// A square on the floor you stand on to buy something. No menu, no confirm — the yard's whole
    /// interface for spending money is walking onto it and staying there.
    ///
    /// It buys REPEATEDLY while the player stands on it, on a cadence that quickens the longer they
    /// hold. That is the shape these games all use and it is worth being precise about why: the first
    /// purchase has to feel deliberate, so the first tick is slow; the twentieth has to not be a chore,
    /// so by then it is pouring. A single purchase per touch would make an eight-step track eight
    /// separate walks.
    ///
    /// The pad's colour is its whole state readout in the greybox — affordable, too expensive, or
    /// finished. Per-pad price labels arrive with the authored art; the HUD names whatever the player
    /// is standing on in the meantime.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class UpgradePad : MonoBehaviour
    {
        /// <summary>See <see cref="StockPad"/> — a contact that must be renewed cannot get stuck on.</summary>
        private const float ContactGrace = 0.15f;

        [Tooltip("Pedin üstünde durunca ilk alışa kadar geçen süre.")]
        [SerializeField, Min(0.05f)] private float firstBuySeconds = 0.45f;

        [Tooltip("Basılı tutuldukça alışlar bu süreye kadar hızlanır.")]
        [SerializeField, Min(0.02f)] private float fastestBuySeconds = 0.08f;

        [Tooltip("En hızlı tempoya ulaşma süresi.")]
        [SerializeField, Min(0.1f)] private float rampSeconds = 1.6f;

        private MarketService _market;
        private string _yardKey;
        private YardUpgrade _kind;
        private MeshRenderer _face;
        private Material _affordable, _tooDear, _finished;

        private AudioService _audio;
        private HapticService _haptics;
        private float _lastTouch = float.NegativeInfinity;
        private float _heldFor;
        private float _timer;
        private bool _wasBought;
        private bool _refused;      // already said "no" for this arrival

        /// <summary>Raised after a purchase lands, so the yard can react — a new slot, a new worker.</summary>
        public event System.Action<YardUpgrade> Bought;

        public void Configure(MarketService market, string yardKey, YardUpgrade kind, MeshRenderer face)
        {
            _market = market;
            _yardKey = yardKey;
            _kind = kind;
            _face = face;
            _audio = ServiceLocator.Get<AudioService>();
            _haptics = ServiceLocator.Get<HapticService>();
            GetComponent<BoxCollider>().isTrigger = true;

            _affordable = MarketYardBuild.Mat(new Color(0.20f, 0.52f, 0.33f));
            _tooDear = MarketYardBuild.Mat(new Color(0.24f, 0.26f, 0.33f));
            _finished = MarketYardBuild.Mat(new Color(0.62f, 0.50f, 0.16f));
            Repaint();
        }

        /// <summary>Which track this pad sells, for the HUD to name.</summary>
        public YardUpgrade Kind => _kind;

        /// <summary>Whose yard it stands in — a label over a pad has to price it against its own island.</summary>
        public string YardKey => _yardKey;

        /// <summary>True while the player is on it — what the HUD watches to decide whose price to show.</summary>
        public bool Occupied => Time.time - _lastTouch <= ContactGrace;

        private void OnTriggerStay(Collider other)
        {
            // The carry stack is what identifies the player: customers and hires have no such thing,
            // and a pad that took their money would be spending the wallet on its own.
            if (other.GetComponentInChildren<CarryStack>() == null) return;
            if (Time.time - _lastTouch > ContactGrace) { _heldFor = 0f; _timer = firstBuySeconds; _refused = false; }
            _lastTouch = Time.time;
        }

        private void Update()
        {
            if (_market == null) return;

            if (!Occupied)
            {
                // Repaint once on the way out rather than every frame: the wallet may have moved while
                // the player was buying, and the pad has to stop claiming to be affordable.
                if (_wasBought) { Repaint(); _wasBought = false; }
                return;
            }

            float dt = Time.deltaTime;
            _heldFor += dt;
            _timer -= dt;
            if (_timer > 0f) return;

            // Eases from the deliberate first tap to a pour, so a long track is one hold, not twenty.
            float t = rampSeconds > 0f ? Mathf.Clamp01(_heldFor / rampSeconds) : 1f;
            _timer = Mathf.Lerp(firstBuySeconds, fastestBuySeconds, t * t);

            bool wasAuto = _market.IsMaxed(_yardKey);

            if (!_market.TryBuy(_yardKey, _kind))
            {
                // Said once per arrival, not once per attempt: a player standing on a pad they cannot
                // afford would otherwise be told off eight times a second.
                if (!_refused)
                {
                    _refused = true;
                    if (!_market.IsTrackMaxed(_yardKey, _kind)) _audio?.Play(SoundId.Denied);
                }
                Repaint();
                return;
            }

            _refused = false;
            _wasBought = true;
            Repaint();
            _audio?.Play(SoundId.Upgrade);
            _haptics?.Light();

            // The yard just became somebody else's job, permanently. It is the biggest thing that ever
            // happens in a market, and it gets the sound a rebuilt district gets.
            if (!wasAuto && _market.IsMaxed(_yardKey))
            {
                _audio?.Play(SoundId.PhaseUp);
                _haptics?.Heavy();
            }

            if (Bought != null) Bought(_kind);
        }

        private void Repaint()
        {
            if (_face == null || _market == null) return;
            Material want = _market.IsTrackMaxed(_yardKey, _kind) ? _finished
                          : Affordable() ? _affordable
                          : _tooDear;
            if (_face.sharedMaterial != want) _face.sharedMaterial = want;
        }

        private bool Affordable()
        {
            var wallet = ServiceLocator.Get<WalletService>();
            double cost = _market.Cost(_yardKey, _kind);
            return wallet != null && cost > 0d && wallet.CanAfford(new BigDouble(cost));
        }
    }
}
