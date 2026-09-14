using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The pickaxe business drawn on Main: bench, rack, carrier, shelf and customers.
    ///
    /// Everything shown is read from <see cref="MarketService.MiningShop"/>'s snapshot every frame, and the
    /// only event it listens to is the sale receipt, to send the served customer away. It never advances the
    /// business, moves goods or pays: bodies follow the numbers, the numbers never wait for bodies.
    ///
    /// Anchors are this object's empty children, found by name the way CoalOperation finds its stations.
    /// People come from CoalOperation's people pack; props from Resources/Market.
    /// </summary>
    public sealed class MiningShopView : MonoBehaviour
    {
        [SerializeField] private float personHeight = 55f;
        [SerializeField] private float pickaxeLength = 18f;
        [Tooltip("Longest side of each prop, world units. The source models have unrelated scales and pivots.")]
        [SerializeField] private float benchSize = 45f;
        [SerializeField] private float rackSize = 20f;
        [SerializeField] private float shelfSize = 60f;
        [Tooltip("Customers' walking pace, world units per second. View only; sales timing is the simulation's.")]
        [SerializeField] private float customerSpeed = 45f;
        [SerializeField, Min(2)] private int customerPool = 10;
        [Tooltip("Most pickaxes drawn on a rack, shelf or carrier. The business may hold more; only the drawing stops.")]
        [SerializeField, Min(1)] private int stackShown = 4;
        [SerializeField] private string routePath = "MiningShop_Routes/Carry_Pickaxe_to_Market";
        [Tooltip("Where a new customer appears, relative to the last queue slot.")]
        [SerializeField] private Vector3 customerEntryOffset = new Vector3(0f, 0f, 60f);
        [Tooltip("Where a served customer walks off to, relative to the serving slot.")]
        [SerializeField] private Vector3 customerExitOffset = new Vector3(100f, 0f, 0f);
        [SerializeField] private Color handleColor = new Color(0.45f, 0.28f, 0.14f);
        [SerializeField] private Color headColor = new Color(0.62f, 0.66f, 0.7f);

        private sealed class Walker
        {
            public Transform body;
            public PersonAnimator anim;
            public Transform held;
            public Vector3 target;
            public Vector3 face;
            public bool leaving;
        }

        private MarketService _market;
        private MiningShopService _shop;
        private Transform _work, _worker, _output, _shelf;
        private Transform[] _queue;
        private Vector3[] _route;
        private float _routeLength;
        private Material _handleMat, _headMat;
        private Transform _craftItem;
        private Transform[] _rackItems, _shelfItems, _cargoItems;
        private Walker _carrier;
        private Walker[] _customers;
        private int[] _waiting;
        private int _waitingCount;
        private int _serving = -1;

        /// <summary>The bench's tap target. Null until the shop is built.</summary>
        public Collider TableCollider { get; private set; }

        /// <summary>Middle of the shop, for the camera.</summary>
        public Vector3 Focus { get; private set; }

        private void Awake()
        {
            _work = transform.Find("Shop_Pickaxe_Work");
            _worker = transform.Find("Shop_Pickaxe_Worker");
            _output = transform.Find("Shop_Pickaxe_Output");
            _shelf = transform.Find("Shop_Market_Shelf");
            int slots = 0;
            while (transform.Find("Shop_Customer_Queue_0" + (slots + 1)) != null) slots++;
            _queue = new Transform[slots];
            for (int i = 0; i < slots; i++) _queue[i] = transform.Find("Shop_Customer_Queue_0" + (i + 1));

            GameObject route = GameObject.Find(routePath);
            if (route != null && route.transform.childCount >= 2)
            {
                _route = new Vector3[route.transform.childCount];
                for (int i = 0; i < _route.Length; i++)
                {
                    _route[i] = route.transform.GetChild(i).position;
                    if (i > 0) _routeLength += Vector3.Distance(_route[i - 1], _route[i]);
                }
            }
        }

        private void Start()
        {
            _market = ServiceLocator.Get<MarketService>();
            _shop = _market != null ? _market.MiningShop : null;
            var op = FindAnyObjectByType<CoalOperation>();
            GameObject[] people = op != null ? op.WorkerPrefabs : null;
            // Shop not opened this launch, or the scene is missing its anchors: draw nothing.
            if (_shop == null || _work == null || _worker == null || _output == null || _shelf == null ||
                _queue.Length == 0 || _route == null || people == null || people.Length == 0)
            {
                enabled = false;
                return;
            }

            GameObject bench = Place(Resources.Load<GameObject>("Market/Props/bench"), _work, benchSize);
            Renderer benchRenderer = bench != null ? bench.GetComponentInChildren<Renderer>() : null;
            if (benchRenderer == null) { enabled = false; return; }
            TableCollider = TapTarget(bench);
            GameObject rack = Place(Resources.Load<GameObject>("Market/Props/crate"), _output, rackSize);
            GameObject shelf = Place(Resources.Load<GameObject>("Market/Models/SM_Market_Shelf"), _shelf, shelfSize);

            // ponytail: primitive pickaxe in two shared materials; swap for a modelled mesh when one is chosen.
            _handleMat = Tinted(benchRenderer.sharedMaterial, handleColor);
            _headMat = Tinted(benchRenderer.sharedMaterial, headColor);

            _craftItem = Pickaxe(_work, new Vector3(0f, TopAbove(bench, _work), 0f));
            _rackItems = Stack(_output, TopAbove(rack, _output), false);
            _shelfItems = Stack(_shelf, TopAbove(shelf, _shelf), true);

            Walker worker = Person(people, 0, _worker.position);
            worker.body.rotation = _worker.rotation;
            _carrier = Person(people, 1, _route[0]);
            _cargoItems = new Transform[stackShown];
            for (int i = 0; i < stackShown; i++)
                _cargoItems[i] = Pickaxe(_carrier.body, Carried(i));

            _customers = new Walker[customerPool];
            _waiting = new int[customerPool];
            for (int i = 0; i < customerPool; i++)
            {
                Walker w = Person(people, 2 + i, Slot(0));
                w.held = Pickaxe(w.body, Carried(0));
                w.body.gameObject.SetActive(false);
                _customers[i] = w;
            }

            Focus = Vector3.Lerp(_work.position, _queue[0].position, 0.5f);
            _market.MiningShopSold += OnSold;
        }

        private void OnDestroy()
        {
            if (_market != null) _market.MiningShopSold -= OnSold;
            if (_handleMat != null) Destroy(_handleMat);
            if (_headMat != null) Destroy(_headMat);
        }

        private void Update()
        {
            MiningShopSimulation.Snapshot v = _shop.View;

            _craftItem.gameObject.SetActive(v.Crafting);
            float craft = v.Crafting ? 1f - Frac(v.CraftRemaining, v.CraftDuration) : 0f;
            _craftItem.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, craft);

            // PickupReserved is still physically on the rack until loading completes.
            Show(_rackItems, v.OutputStock + v.PickupReserved);
            Show(_shelfItems, v.ShelfStock);
            DrawCarrier(v);
            Reconcile(v);
            WalkCustomers(Time.deltaTime);
        }

        private void DrawCarrier(in MiningShopSimulation.Snapshot v)
        {
            float t = 0f;
            bool walking = false;
            switch (v.Carrier)
            {
                case MiningShopSimulation.CarrierPhase.ToMarket:
                    t = 1f - Frac(v.CarrierRemaining, v.CarrierDuration); walking = true; break;
                case MiningShopSimulation.CarrierPhase.Returning:
                    t = Frac(v.CarrierRemaining, v.CarrierDuration); walking = true; break;
                case MiningShopSimulation.CarrierPhase.Unloading:
                case MiningShopSimulation.CarrierPhase.WaitingForSpace:
                    t = 1f; break;
            }
            Vector3 pos = OnRoute(t * _routeLength, out Vector3 dir);
            if (v.Carrier == MiningShopSimulation.CarrierPhase.Returning) dir = -dir;
            _carrier.body.position = pos;
            if (walking && dir.sqrMagnitude > 1e-4f) _carrier.body.rotation = Quaternion.LookRotation(dir);
            if (_carrier.anim != null)
            {
                _carrier.anim.SetMoving(walking);
                // Feet under the body: stride rate follows the pace the simulation's clock sets.
                float pace = walking ? _routeLength / Mathf.Max(0.1f, (float)v.CarrierDuration) : 0f;
                _carrier.anim.SetSpeed(walking ? pace / (personHeight * 0.8f) : 1f);
            }
            Show(_cargoItems, v.Cargo);
        }

        /// <summary>
        /// Keeps one body per waiting customer and one for the customer being served. The sale receipt
        /// (OnSold) is what sends the served one away; everything else follows the counts.
        /// </summary>
        private void Reconcile(in MiningShopSimulation.Snapshot v)
        {
            if (v.Serving && _serving < 0)
            {
                _serving = _waitingCount > 0 ? PopFront() : Spawn();
                if (_serving >= 0)
                {
                    Walker w = _customers[_serving];
                    w.target = Slot(0);
                    w.face = _shelf.position - Slot(0);
                    w.held.gameObject.SetActive(true);
                }
            }
            else if (!v.Serving && _serving >= 0)
            {
                Hide(_customers[_serving]);
                _serving = -1;
            }

            while (_waitingCount < v.WaitingCustomers)
            {
                int c = Spawn();
                if (c < 0) break;
                _waiting[_waitingCount++] = c;
            }
            while (_waitingCount > v.WaitingCustomers) Hide(_customers[_waiting[--_waitingCount]]);

            for (int i = 0; i < _waitingCount; i++)
            {
                Walker w = _customers[_waiting[i]];
                w.target = Slot(i + 1);
                w.face = Slot(i) - Slot(i + 1);
            }
        }

        private void OnSold(MiningShopSimulation.Sale sale)
        {
            if (_serving < 0) return;
            Walker w = _customers[_serving];
            w.leaving = true;
            w.target = Slot(0) + customerExitOffset;
            _serving = -1;
        }

        private void WalkCustomers(float dt)
        {
            for (int i = 0; i < _customers.Length; i++)
            {
                Walker w = _customers[i];
                if (!w.body.gameObject.activeSelf) continue;
                Vector3 to = w.target - w.body.position;
                to.y = 0f;
                bool moving = to.sqrMagnitude > 0.25f;
                if (moving)
                {
                    w.body.position = Vector3.MoveTowards(w.body.position, w.target, customerSpeed * dt);
                    w.body.rotation = Quaternion.LookRotation(to);
                }
                else if (w.leaving)
                {
                    Hide(w);
                    continue;
                }
                else if (w.face.sqrMagnitude > 1e-4f)
                {
                    w.face.y = 0f;
                    w.body.rotation = Quaternion.LookRotation(w.face);
                }
                w.anim?.SetMoving(moving);
            }
        }

        private int Spawn()
        {
            for (int i = 0; i < _customers.Length; i++)
            {
                Walker w = _customers[i];
                if (w.body.gameObject.activeSelf) continue;
                w.leaving = false;
                w.held.gameObject.SetActive(false);
                w.body.position = _queue[_queue.Length - 1].position + customerEntryOffset;
                w.target = w.body.position;
                w.body.gameObject.SetActive(true);
                return i;
            }
            return -1;
        }

        private int PopFront()
        {
            int front = _waiting[0];
            for (int i = 1; i < _waitingCount; i++) _waiting[i - 1] = _waiting[i];
            _waitingCount--;
            return front;
        }

        private static void Hide(Walker w)
        {
            w.leaving = false;
            w.held.gameObject.SetActive(false);
            w.body.gameObject.SetActive(false);
        }

        /// <summary>Queue position <paramref name="i"/>; 0 is the serving spot. Past the authored slots the
        /// line carries on in the direction of the last two.</summary>
        private Vector3 Slot(int i)
        {
            int n = _queue.Length;
            if (i < n) return _queue[i].position;
            if (n < 2) return _queue[0].position;
            Vector3 last = _queue[n - 1].position;
            return last + (last - _queue[n - 2].position) * (i - n + 1);
        }

        private Vector3 OnRoute(float distance, out Vector3 dir)
        {
            for (int i = 1; i < _route.Length; i++)
            {
                Vector3 seg = _route[i] - _route[i - 1];
                float len = seg.magnitude;
                dir = len > 1e-4f ? seg / len : Vector3.forward;
                if (distance <= len || i == _route.Length - 1)
                    return _route[i - 1] + dir * Mathf.Clamp(distance, 0f, len);
                distance -= len;
            }
            dir = Vector3.forward;
            return _route[0];
        }

        private static float Frac(double remaining, double duration)
            => duration > 0d ? Mathf.Clamp01((float)(remaining / duration)) : 0f;

        private void Show(Transform[] items, int count)
        {
            for (int i = 0; i < items.Length; i++) items[i].gameObject.SetActive(i < count);
        }

        private Vector3 Carried(int i)
            => new Vector3(0f, personHeight * 0.55f + i * pickaxeLength * 0.16f, personHeight * 0.22f);

        private Transform[] Stack(Transform at, float height, bool sideBySide)
        {
            var items = new Transform[stackShown];
            for (int i = 0; i < stackShown; i++)
            {
                Vector3 local = sideBySide
                    ? new Vector3((i - (stackShown - 1) * 0.5f) * pickaxeLength * 0.45f, height, 0f)
                    : new Vector3(0f, height + i * pickaxeLength * 0.16f, 0f);
                items[i] = Pickaxe(at, local);
            }
            return items;
        }

        /// <summary>A pickaxe lying flat, hidden until something shows it.</summary>
        private Transform Pickaxe(Transform parent, Vector3 localPosition)
        {
            var root = new GameObject("Pickaxe").transform;
            root.SetParent(parent, false);
            root.localPosition = localPosition;
            root.localRotation = Quaternion.Euler(0f, 0f, 90f);
            float l = pickaxeLength;
            Part(PrimitiveType.Cylinder, root, _handleMat, new Vector3(0f, l * 0.5f, 0f), new Vector3(l * 0.14f, l * 0.5f, l * 0.14f));
            Part(PrimitiveType.Cube, root, _headMat, new Vector3(0f, l, 0f), new Vector3(l * 0.8f, l * 0.18f, l * 0.2f));
            root.gameObject.SetActive(false);
            return root;
        }

        private static void Part(PrimitiveType type, Transform parent, Material material, Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Material Tinted(Material source, Color color)
        {
            var m = new Material(source);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        /// <summary>
        /// A person standing at <paramref name="at"/>, sized to personHeight. Measured on the PREFAB: a skinned
        /// instance reports bounds that move with its pose (see CoalOperation.WearPorter).
        /// </summary>
        private Walker Person(GameObject[] people, int pick, Vector3 at)
        {
            var w = new Walker { body = new GameObject("Walker").transform };
            w.body.SetParent(transform, false);
            w.body.position = at;
            w.target = at;
            GameObject src = people[pick % people.Length];
            if (src == null) return w;
            Transform p = Instantiate(src, w.body).transform;
            p.localPosition = Vector3.zero;
            p.localRotation = Quaternion.identity;
            p.localScale = Vector3.one;
            if (WorldBox(src.transform, out Bounds b) && b.size.y > 1e-4f)
            {
                float s = personHeight / b.size.y;
                p.localScale = Vector3.one * s;
                p.localPosition = Vector3.up * (-b.min.y * s);
            }
            w.anim = new PersonAnimator(p);
            return w;
        }

        /// <summary>
        /// A prop scaled so its longest side is <paramref name="size"/>, standing centred on the anchor. Centred by
        /// its drawn bounds, not its pivot: the market shelf's pivot sits well off its mesh.
        /// </summary>
        private static GameObject Place(GameObject src, Transform at, float size)
        {
            if (src == null) return null;
            GameObject go = Instantiate(src, at.position, at.rotation);
            if (WorldBox(go.transform, out Bounds b))
            {
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (longest > 1e-4f) go.transform.localScale *= size / longest;
                WorldBox(go.transform, out b);
                go.transform.position += at.position - new Vector3(b.center.x, b.min.y, b.center.z);
            }
            go.transform.SetParent(at, true);
            return go;
        }

        private static float TopAbove(GameObject prop, Transform at)
            => prop != null && WorldBox(prop.transform, out Bounds b) ? b.max.y - at.position.y : 0f;

        private static Collider TapTarget(GameObject prop)
        {
            WorldBox(prop.transform, out Bounds b);
            var box = prop.AddComponent<BoxCollider>();
            Vector3 scale = prop.transform.lossyScale;
            box.center = prop.transform.InverseTransformPoint(b.center);
            // Generous: a bench is a small thing to hit with a thumb.
            box.size = new Vector3(b.size.x / scale.x, b.size.y / scale.y, b.size.z / scale.z) * 1.6f;
            return box;
        }

        private static bool WorldBox(Transform t, out Bounds box)
        {
            box = new Bounds();
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return false;
            box = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) box.Encapsulate(rs[i].bounds);
            return true;
        }
    }
}
