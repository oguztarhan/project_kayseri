using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The counter: where the player puts bars down, and where customers pick them up.
    ///
    /// The shelf between those two is the point. Without it, serving would mean the player handing a
    /// bar directly to a customer, and the queue would only ever move as fast as he ran. With it, he
    /// can stock ahead, walk away, and come back to find the line has been working through what he
    /// left — which is the difference between a chore and a shop.
    ///
    /// The shelf is scene state, not save state, and deliberately so. It holds a few seconds of work;
    /// persisting it would mean a yard that remembers half a bar on a plank across a reinstall.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class SellCounter : MonoBehaviour
    {
        [Tooltip("Tezgâhta aynı anda durabilen külçe sayısı.")]
        [SerializeField, Min(1)] private int shelfCapacity = 12;

        [Tooltip("Sırttan tezgâha bir külçenin inme süresi.")]
        [SerializeField, Min(0.02f)] private float unloadSeconds = 0.11f;

        [Tooltip("Tezgâhın üstünde külçelerin dizildiği alanın eni ve boyu.")]
        [SerializeField] private Vector2 shelfArea = new Vector2(9.5f, 2.2f);

        [Tooltip("Külçelerin tezgâh tablasından yüksekliği.")]
        [SerializeField] private float shelfHeight = 1.75f;

        private readonly System.Collections.Generic.List<Transform> _onShelf =
            new System.Collections.Generic.List<Transform>();
        private readonly System.Collections.Generic.Stack<Transform> _spare =
            new System.Collections.Generic.Stack<Transform>();

        /// <summary>See <see cref="StockPad"/> — a contact that has to be renewed cannot get stuck on.</summary>
        private const float ContactGrace = 0.15f;

        private MarketService _market;
        private string _yardKey;
        private Material _oreMaterial;
        private MarketPrefabs _prefabs;
        private CarryStack _carry;
        private float _timer;
        private float _lastTouch = float.NegativeInfinity;
        private Transform _settling;      // the bar that landed most recently, mid-pop
        private float _settle;
        private YardWorker _cashier;      // null until the serve pad is paid for

        /// <summary>How many bars are sitting out. The queue will not move without at least one.</summary>
        public int Stocked => _onShelf.Count;

        /// <summary>
        /// Puts one bar on the shelf on somebody else's behalf — a hired carrier setting down what it
        /// lifted off the pad. False when there is no room, which is the carrier's cue to stand and
        /// wait rather than lose the load.
        ///
        /// The player's own unloading does not come through here: theirs comes off a
        /// <see cref="CarryStack"/> in <see cref="Update"/>, one bar per contact tick. Both end at the
        /// same <see cref="Place"/>, which is what keeps a staffed shelf and a hand-stocked one
        /// indistinguishable.
        /// </summary>
        public bool TryStock()
        {
            if (_onShelf.Count >= shelfCapacity) return false;
            Place();
            return true;
        }

        /// <summary>Whether anyone is behind the counter — the player, or the cashier they hired.</summary>
        public bool Served => Time.time - _lastTouch <= ContactGrace || _cashier != null;

        /// <summary>The hired cashier, once there is one. Null means the counter is only ever staffed by you.</summary>
        /// <summary>How many bars the plank holds. The display rack behind it scales off this.</summary>
        public int ShelfCapacity => shelfCapacity;

        public void SetCashier(YardWorker cashier) => _cashier = cashier;

        /// <summary>The hired cashier, for the queue to point at whoever it is serving.</summary>
        public YardWorker Cashier => _cashier;

        public void Configure(MarketService market, string yardKey, Material oreMaterial, MarketPrefabs prefabs)
        {
            _market = market;
            _yardKey = yardKey;
            _oreMaterial = oreMaterial;
            _prefabs = prefabs;
            GetComponent<BoxCollider>().isTrigger = true;
        }

        /// <summary>
        /// A customer taking one. Returns what the sale was worth so the caller can drop it on the
        /// floor — the money is made here, but it does not reach the wallet until somebody picks it up.
        /// </summary>
        public double TakeUpTo(int wanted, out int taken)
        {
            taken = 0;
            if (_market == null || wanted <= 0) return 0d;

            // Whatever is left, if they asked for more than the shelf holds. A customer who wanted four
            // and can only have two takes the two and goes — holding the line until a shelf can fill a
            // whole order would stall the counter behind one greedy shopper.
            while (taken < wanted && _onShelf.Count > 0)
            {
                int last = _onShelf.Count - 1;
                Transform bar = _onShelf[last];
                _onShelf.RemoveAt(last);
                bar.gameObject.SetActive(false);
                _spare.Push(bar);
                taken++;
            }
            return taken > 0 ? _market.SellByHand(_yardKey, taken) : 0d;
        }

        private void OnTriggerStay(Collider other)
        {
            CarryStack stack = other.GetComponentInChildren<CarryStack>();
            if (stack == null) return;
            if (Time.time - _lastTouch > ContactGrace) _timer = 0f;
            _carry = stack;
            _lastTouch = Time.time;
        }

        private void Update()
        {
            if (_carry == null) return;
            if (Time.time - _lastTouch > ContactGrace) { _carry = null; return; }   // they walked off
            if (_carry.IsEmpty || _onShelf.Count >= shelfCapacity) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = unloadSeconds;

            // Off the back before onto the plank: if the removal fails there was nothing to put down.
            if (!_carry.TryRemove()) return;
            Place();
        }

        /// <summary>
        /// Squashes and springs whatever landed most recently. One line of state and no coroutine: the
        /// bar the player just put down is the only one that should be moving, and the moment they stop
        /// unloading it settles on its own.
        /// </summary>
        private void LateUpdate()
        {
            if (_settling == null) return;
            _settle += Time.deltaTime * 7f;
            if (_settle >= 1f)
            {
                _settling.localScale = Vector3.one;
                _settling = null;
                return;
            }
            // Overshoots once and comes back, which reads as weight landing rather than a shape appearing.
            float pop = 1f + Mathf.Sin(_settle * Mathf.PI) * 0.28f;
            _settling.localScale = new Vector3(1f / pop, pop, 1f / pop);
        }

        private void Place()
        {
            Transform bar = _spare.Count > 0 ? _spare.Pop() : NewBar();
            bar.gameObject.SetActive(true);

            // Laid out in rows across the plank so a full shelf reads as a full shelf rather than a
            // tower. Four to a row is what fits the greybox counter; the authored one will bring its own.
            const int perRow = 4;
            int index = _onShelf.Count;
            int row = index / perRow, column = index % perRow;
            float x = (column - (perRow - 1) * 0.5f) * (shelfArea.x / perRow);
            float z = (row - 1) * (shelfArea.y * 0.5f);
            bar.localPosition = new Vector3(x, shelfHeight, z);
            _onShelf.Add(bar);

            // The holder takes the squash; the mesh inside keeps its bar shape.
            _settling = bar;
            _settle = 0f;
        }

        /// <summary>
        /// A holder with the bar's mesh inside it. Two transforms rather than one so the landing pop
        /// has a scale of its own to play with — squashing the cube directly would fight the
        /// dimensions that make it bar-shaped.
        /// </summary>
        private Transform NewBar()
        {
            var holder = new GameObject("TezgahKulcesi");
            holder.transform.SetParent(transform, false);
            MarketPrefabs.SpawnCargo(_prefabs != null ? _prefabs.Bar : null, holder.transform, "Mesh",
                                     new Vector3(1.7f, 0.4f, 0.85f), _oreMaterial);
            return holder.transform;
        }
    }
}
