using Game.Core;
using Game.Data;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The deposit pad: the heap of bars the island's lorries tipped here, and the place the player
    /// picks them up.
    ///
    /// The heap is the ledger's stock made visible — it is not a separate count, it is
    /// <see cref="MarketService.Stock"/> drawn as a pile. Keeping one number and one picture of it is
    /// the reason a yard the player has been away from for an hour looks exactly as buried as the
    /// welcome-back screen says it is.
    ///
    /// Pickup is on a cadence rather than instant. Emptying the pad in one frame would make the walk
    /// over to it pointless, and the rhythm — bar, bar, bar — is most of what the loop feels like.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class StockPad : MonoBehaviour
    {
        [Tooltip("İki külçe alışı arasındaki süre. Küçültürsen yığın daha hızlı sırta biner.")]
        [SerializeField, Min(0.02f)] private float pickupSeconds = 0.14f;

        [Tooltip("Yığındaki bir parçanın kaç külçeyi temsil ettiği. Küçültürsen tepe daha çok, " +
                 "daha ufak parçadan kurulur.")]
        [SerializeField, Min(0.05f)] private float barsPerChunk = 0.3f;

        [Tooltip("Bir parçanın boyu. Adadaki yığınlara göre küçük — burası kaya değil külçe yığını.")]
        [SerializeField, Min(0.05f)] private float chunkScale = 0.42f;

        [Tooltip("Piramidin en geniş tabanı. Büyüdükçe yığın daha çok parça alabilir.")]
        [SerializeField, Min(2)] private int maxGrid = 9;

        /// <summary>
        /// How stale a touch may be before the pad decides nobody is standing on it. Comfortably longer
        /// than a physics step so a fast frame rate cannot blink the contact off, comfortably shorter
        /// than a walk to the counter.
        /// </summary>
        private const float ContactGrace = 0.15f;

        private MarketService _market;
        private string _yardKey;
        private CarryStack _carry;
        private AudioService _audio;
        private HapticService _haptics;
        private PileStack _heap;
        private float _timer;
        private float _lastTouch = float.NegativeInfinity;

        /// <summary>
        /// Hooks the pad up. <paramref name="padSurface"/> is the slab the heap is drawn on top of;
        /// it needs a renderer, because the pile measures the footprint it may cover from its bounds.
        /// </summary>
        public void Configure(MarketService market, string yardKey, Transform padSurface, Material oreMaterial)
        {
            _market = market;
            _yardKey = yardKey;
            _audio = ServiceLocator.Get<AudioService>();
            _haptics = ServiceLocator.Get<HapticService>();
            _heap = new PileStack(padSurface, oreMaterial, barsPerChunk, "StokYigini",
                                  chunkMesh: null, cellScale: chunkScale, maxGrid: maxGrid);
            GetComponent<BoxCollider>().isTrigger = true;
        }

        /// <summary>
        /// Who is picking up here, re-asserted every physics step.
        ///
        /// Stay rather than Enter/Exit on purpose. The obvious version latches a bool when the player
        /// arrives and clears it when they leave — and a CharacterController does not reliably raise the
        /// leaving half. Teleport one, or disable it for a frame, and the pad goes on loading bars onto
        /// a player standing on the far side of the yard. A contact that has to be renewed cannot get
        /// stuck on; the worst a missed event can do is cost a fifteenth of a second.
        /// </summary>
        private void OnTriggerStay(Collider other)
        {
            CarryStack stack = other.GetComponentInChildren<CarryStack>();
            if (stack == null) return;
            // Stepping on starts the cadence over, so the first bar comes straight away.
            if (Time.time - _lastTouch > ContactGrace)
            {
                _timer = 0f;
                // Once per arrival, not once per bar: the pad loads seven a second and the sound is
                // meant to say "you are picking up", not to keep time with it.
                _audio?.Play(SoundId.Tick);
                _haptics?.Light();
            }
            _carry = stack;
            _lastTouch = Time.time;
        }

        private void Update()
        {
            if (_market != null && _heap != null)
            {
                double stock = _market.Stock(_yardKey);
                double capacity = _market.StockCapacity(_yardKey);
                // A yard whose delivery meter has not filled yet reports no capacity. Drawing the heap
                // against the stock itself keeps it visible instead of blanking the pad on a fresh save.
                _heap.Set(stock, capacity > 0d ? capacity : stock);
            }

            if (_carry == null || _market == null) return;
            if (Time.time - _lastTouch > ContactGrace) { _carry = null; return; }   // they walked off

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = pickupSeconds;

            if (_carry.IsFull) return;
            // Taken from the ledger FIRST. If the take comes back short the bar was never there, and
            // putting one on the player's back anyway would mint stock out of nothing.
            double taken = _market.TakeFromStock(_yardKey, 1d);
            if (taken <= 0d) return;
            if (!_carry.TryAdd()) _market.Deliver(_yardKey, taken);   // lost the race for the last slot; put it back
        }
    }
}
