using Game.Core;
using Game.Systems;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The mining-gear business drawn on Main: one table and rack per product line (pickaxe, helmet, lantern, bag),
    /// one shared carrier, one shared shelf and one customer queue.
    ///
    /// Everything shown is read from <see cref="MarketService.MiningShopBusiness"/>'s snapshot every frame, and the
    /// only event it listens to is the sale receipt, to send the served customer away. It never advances the
    /// business, moves goods or pays: bodies follow the numbers, the numbers never wait for bodies. A line the island
    /// does not offer draws nothing; an offered line not yet built shows only its locked pad.
    ///
    /// An accepted contract adds one more body: the contract customer, who waits beside the counter rather than in
    /// the queue, holds each bundle as it is handed over, fills his crate, and leaves when the contract ends. A
    /// contract receipt bounces his crate instead of sending anyone away.
    ///
    /// Anchors are this object's empty children, found by name the way CoalOperation finds its stations.
    /// People come from CoalOperation's people pack; props from Resources/Market.
    /// </summary>
    public sealed class MiningShopView : MonoBehaviour
    {
        [SerializeField] private float personHeight = 55f;
        [SerializeField] private float pickaxeLength = 18f;
        [Tooltip("Size of a carried item relative to one on a bench or rack.")]
        [SerializeField, Range(0.3f, 1f)] private float carryScale = 0.75f;
        [Tooltip("Size of an item on the shared shelf, where four products share the width.")]
        [SerializeField, Range(0.3f, 1f)] private float shelfItemScale = 0.55f;
        [Tooltip("Longest side of each prop, world units. The source models have unrelated scales and pivots.")]
        [SerializeField] private float benchSize = 45f;
        [SerializeField] private float rackSize = 20f;
        [SerializeField] private float shelfSize = 60f;
        [Tooltip("Helmet, lantern and bag tables share the islet's two narrow strips, so they are smaller than the pickaxe's.")]
        [SerializeField] private float extraBenchScale = 0.7f;
        [Tooltip("Customers' walking pace, world units per second. View only; sales timing is the simulation's.")]
        [SerializeField] private float customerSpeed = 45f;
        [SerializeField, Min(2)] private int customerPool = 10;
        [Tooltip("Most items drawn on a rack, shelf slot or carrier. The business may hold more; only the drawing stops.")]
        [SerializeField, Min(1)] private int stackShown = 4;
        [Tooltip("Where a new customer appears, relative to the last queue slot.")]
        [SerializeField] private Vector3 customerEntryOffset = new Vector3(0f, 0f, 60f);
        [Tooltip("Where a served customer walks off to, relative to the serving slot.")]
        [SerializeField] private Vector3 customerExitOffset = new Vector3(100f, 0f, 0f);
        [SerializeField] private Color handleColor = new Color(0.45f, 0.28f, 0.14f);
        [SerializeField] private Color headColor = new Color(0.62f, 0.66f, 0.7f);
        [SerializeField] private Color helmetColor = new Color(0.98f, 0.76f, 0.12f);
        [SerializeField] private Color lanternColor = new Color(1f, 0.92f, 0.55f);
        [SerializeField] private Color bagColor = new Color(0.36f, 0.22f, 0.12f);
        [SerializeField] private Color padColor = new Color(0.85f, 0.85f, 0.8f);
        [Tooltip("Bir tezgâh yıldız kazanınca ne kadar süre zıplar (sn).")]
        [SerializeField, Min(0.05f)] private float starPulseSeconds = 0.6f;
        [Tooltip("Yıldız zıplamasında tezgâhın en fazla büyüme payı.")]
        [SerializeField, Range(0f, 0.5f)] private float starPulseScale = 0.18f;
        [Tooltip("Tezgâhtaki ustanın altındaki nadirlik kaidesinin yarıçapı (dünya birimi).")]
        [SerializeField, Min(0f)] private float workerPlinthRadius = 11f;
        [Tooltip("Her nadirlik basamağında tezgâh ustasının ne kadar büyüdüğü; istasyon ustalarındakiyle aynı.")]
        [SerializeField, Range(0f, 0.3f)] private float workerRankScaleStep = 0.10f;

        [Header("Kontrat müşterisi")]
        [Tooltip("Kontrat müşterisinin durduğu yer, servis noktasına göre. Sahnede 'Shop_Customer_Contract' çapası " +
                 "varsa bu değil o kullanılır.")]
        [SerializeField] private Vector3 contractSpotOffset = new Vector3(8f, 0f, -30f);
        [Tooltip("Kontrat müşterisi normal müşterilerden bu kadar büyük görünür.")]
        [SerializeField, Range(1f, 1.5f)] private float contractScale = 1.12f;
        [Tooltip("Kontrat müşterisinin ayağının altındaki disk ve elindeki sandık.")]
        [SerializeField] private Color contractBadgeColor = new Color(1f, 0.78f, 0.2f);
        [SerializeField] private Color contractCrateColor = new Color(0.55f, 0.36f, 0.18f);
        [Tooltip("Her teslimde sandığın ne kadar süre zıplayacağı (sn).")]
        [SerializeField, Min(0.05f)] private float contractReceiveSeconds = 0.35f;

        /// <summary>Anchor and route names per product, in MiningShopCampaign.ProductIdAt order.</summary>
        private static readonly string[] Names = { "Pickaxe", "Helmet", "Lantern", "Bag" };

        private sealed class Walker
        {
            public Transform body;
            public PersonAnimator anim;
            public Transform[] held;
            public int heldProduct = -1;
            public Vector3 target;
            public Vector3 face;
            public bool leaving;
        }

        private sealed class Line
        {
            public Transform work, output;
            public GameObject table, rack, pad;
            public Walker worker;
            public Transform workerAnchor;
            // Which master the worker body is: -1 the apprentice, -2 not built yet.
            public int workerWho = -2;
            public Transform craft;
            public Transform[] rackItems, shelfItems, cargo;
            public Vector3[] route;
            public float routeLength;
            public Collider tableCollider, padCollider;
            public Vector3 workScale;
            public float pulse;
        }

        private MarketService _market;
        private MiningShopBusinessService _shop;
        private Transform _shelf;
        private Transform[] _queue;
        private readonly Line[] _lines = new Line[MiningShopCampaign.ProductCount];
        private Material _handleMat, _headMat, _helmetMat, _lanternMat, _bagMat, _padMat;
        private Material[] _plinthMats;
        private GameObject[] _people;
        private ForemanService _foremen;
        private Walker _carrier;
        private int _carrierLine;
        private Walker[] _customers;
        private int[] _waiting;
        private int _waitingCount;
        private int _serving = -1;

        // The contract customer: a body of his own beside the counter, never one of the queue's.
        private Transform _contractAnchor;
        private Walker _contractor;
        private Transform _crate;
        private Transform[][] _crateItems;
        private Material _crateMat, _badgeMat;
        private bool _contractorHere;
        private bool _contractorSnap = true;
        private int _contractorProduct = -1;
        private int _crateShown;
        private bool _crateFull;
        private float _receivePulse;

        /// <summary>The pickaxe bench's tap target, which the camera waits for. Null until the shop is built.</summary>
        public Collider TableCollider => _lines[0] != null ? _lines[0].tableCollider : null;

        /// <summary>World position used by the first-time guide to point at the pickaxe bench.</summary>
        public bool TryGetTutorialBench(out Vector3 position)
        {
            Collider target = TableCollider;
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                position = Vector3.zero;
                return false;
            }

            position = target.bounds.center;
            return true;
        }

        /// <summary>The carrier, for the first-time guide to follow. Null until the shop is built.</summary>
        public Transform TutorialCarrier => _carrier != null ? _carrier.body : null;

        /// <summary>The shelf the customers buy from, for the first-time guide.</summary>
        public Transform TutorialShelf => _shelf;

        /// <summary>An offered bench's empty pad, where the player taps to build it. Null when there is none.</summary>
        public Collider TutorialPad(int product)
            => product >= 0 && product < _lines.Length && _lines[product] != null ? _lines[product].padCollider : null;

        /// <summary>Over the head of the customer being served, where a sale's moment is shown.</summary>
        public Vector3 SalePoint => _queue.Length > 0 ? Slot(0) + Vector3.up * personHeight * 1.2f : transform.position;

        /// <summary>Everything the offered lines use — benches to the customers' entry — for the camera to fit.</summary>
        public Bounds ShopBounds { get; private set; }

        /// <summary>
        /// Over the contract customer's head, for the progress marker. False while there is none, and while he walks
        /// out: a finished or cancelled contract has nothing left to count.
        /// </summary>
        public bool TryGetContractCustomer(out Vector3 head)
        {
            if (!_contractorHere || _contractor == null || !_contractor.body.gameObject.activeSelf)
            {
                head = Vector3.zero;
                return false;
            }
            head = _contractor.body.position + Vector3.up * personHeight * contractScale * 1.25f;
            return true;
        }

        /// <summary>
        /// A short bounce of the bench and the item on it, for the moment it earns a star. It scales the bench's work
        /// anchor, so the two move together about the bench's footprint and nothing else on the line is touched.
        /// </summary>
        public void PulseBench(int product)
        {
            if (product >= 0 && product < _lines.Length && _lines[product] != null) _lines[product].pulse = starPulseSeconds;
        }

        /// <summary>Which product a tapped collider belongs to, and whether it was that line's locked pad.</summary>
        public bool TryGetProduct(Collider hit, out int product, out bool pad)
        {
            for (int i = 0; i < _lines.Length; i++)
            {
                Line line = _lines[i];
                if (line == null || hit == null) continue;
                if (hit == line.tableCollider || hit == line.padCollider)
                {
                    product = i;
                    pad = hit == line.padCollider;
                    return true;
                }
            }
            product = -1;
            pad = false;
            return false;
        }

        private void Awake()
        {
            _shelf = transform.Find("Shop_Market_Shelf");
            int slots = 0;
            while (transform.Find("Shop_Customer_Queue_0" + (slots + 1)) != null) slots++;
            _queue = new Transform[slots];
            for (int i = 0; i < slots; i++) _queue[i] = transform.Find("Shop_Customer_Queue_0" + (i + 1));
            _contractAnchor = transform.Find("Shop_Customer_Contract");
        }

        private void Start()
        {
            _market = ServiceLocator.Get<MarketService>();
            _shop = _market != null ? _market.MiningShopBusiness : null;
            var op = FindAnyObjectByType<CoalOperation>();
            GameObject[] people = op != null ? op.WorkerPrefabs : null;
            // Shop not opened this launch, or the scene is missing its shared anchors: draw nothing.
            if (_shop == null || _shelf == null || _queue.Length == 0 || people == null || people.Length == 0 ||
                transform.Find("Shop_Pickaxe_Work") == null || GameObject.Find(RoutePath(0)) == null)
            {
                enabled = false;
                return;
            }

            GameObject shelf = Place(Resources.Load<GameObject>("Market/Models/SM_Market_Shelf"), _shelf, shelfSize);
            GameObject pallet = Resources.Load<GameObject>("Market/Props/pallet");
            Renderer source = shelf != null ? shelf.GetComponentInChildren<Renderer>() : null;
            if (source == null || pallet == null) { enabled = false; return; }

            // ponytail: primitive goods and tables in shared tinted materials; swap for modelled meshes when chosen.
            _handleMat = Tinted(source.sharedMaterial, handleColor);
            _headMat = Tinted(source.sharedMaterial, headColor);
            _helmetMat = Tinted(source.sharedMaterial, helmetColor);
            _lanternMat = Tinted(source.sharedMaterial, lanternColor);
            _bagMat = Tinted(source.sharedMaterial, bagColor);
            _padMat = Tinted(source.sharedMaterial, padColor);
            // One plinth colour per rarity, the roster's own, so the bench master's disc matches his card.
            _foremen = ServiceLocator.Get<ForemanService>();
            if (_foremen != null)
            {
                _plinthMats = new Material[Foremen.RarityCount];
                for (int r = 0; r < _plinthMats.Length; r++)
                    _plinthMats[r] = Tinted(source.sharedMaterial, _foremen.RarityTint((Foremen.Rarity)r));
            }
            _people = people;

            _carrier = Person(people, 1, Vector3.zero);
            float shelfTop = TopAbove(shelf, _shelf);
            MiningShopBusinessSimulation.Snapshot v = _shop.View;
            var bounds = new Bounds(_shelf.position, Vector3.zero);
            for (int p = 0; p < _lines.Length; p++)
            {
                Line line = BuildLine(p, people, pallet, shelfTop);
                _lines[p] = line;
                if (line == null || p >= v.AvailableProductCount) continue;
                bounds.Encapsulate(line.work.position + Vector3.up * personHeight);
                bounds.Encapsulate(line.output.position);
            }
            if (_lines[0] == null) { enabled = false; return; }
            _carrier.body.position = _lines[0].route[0];

            _customers = new Walker[customerPool];
            _waiting = new int[customerPool];
            for (int i = 0; i < customerPool; i++)
            {
                Walker w = Person(people, 2 + i, Slot(0));
                w.held = new Transform[MiningShopCampaign.ProductCount];
                for (int p = 0; p < w.held.Length; p++) w.held[p] = Carried(p, w.body, 0);
                w.body.gameObject.SetActive(false);
                _customers[i] = w;
            }
            BuildContractor(people, source.sharedMaterial);
            bounds.Encapsulate(ContractSpot() + Vector3.up * personHeight);

            for (int i = 0; i < _queue.Length; i++) bounds.Encapsulate(_queue[i].position);
            bounds.Encapsulate(_queue[_queue.Length - 1].position + customerEntryOffset);
            ShopBounds = bounds;
            RefreshWorkers();
            _market.MiningShopBusinessSold += OnSold;
            _shop.WorkersChanged += RefreshWorkers;
        }

        /// <summary>One product's table, rack, locked pad, goods and route. Null when its anchors are not authored.</summary>
        private Line BuildLine(int p, GameObject[] people, GameObject pallet, float shelfTop)
        {
            Transform work = transform.Find("Shop_" + Names[p] + "_Work");
            Transform output = transform.Find("Shop_" + Names[p] + "_Output");
            GameObject routeRoot = GameObject.Find(RoutePath(p));
            if (work == null || output == null || routeRoot == null || routeRoot.transform.childCount < 2) return null;

            var line = new Line { work = work, output = output, workScale = work.localScale };
            float size = p == 0 ? 1f : extraBenchScale;
            line.table = Table(work, benchSize * size);
            line.tableCollider = TapTarget(line.table);
            line.rack = Place(pallet, output, rackSize * size);
            line.craft = Item(p, work, new Vector3(0f, TopAbove(line.table, work), 0f));
            float rackTop = TopAbove(line.rack, output);
            line.rackItems = new Transform[stackShown];
            line.shelfItems = new Transform[stackShown];
            line.cargo = new Transform[stackShown];
            // Four products share the shelf's width: each gets a quarter, its items stacked upwards.
            float column = (p - (MiningShopCampaign.ProductCount - 1) * 0.5f) * shelfSize / MiningShopCampaign.ProductCount;
            for (int i = 0; i < stackShown; i++)
            {
                line.rackItems[i] = Item(p, output, new Vector3(0f, rackTop + i * ItemHeight(p), 0f));
                line.shelfItems[i] = Item(p, _shelf, new Vector3(column, shelfTop + i * ItemHeight(p) * shelfItemScale, 0f));
                line.shelfItems[i].localScale = Vector3.one * shelfItemScale;
                line.cargo[i] = Carried(p, _carrier.body, i);
            }

            Transform padAnchor = transform.Find("Shop_" + Names[p] + "_LockedPad");
            if (padAnchor != null && p > 0)
            {
                line.pad = new GameObject("LockedPad");
                line.pad.transform.SetParent(padAnchor, false);
                Part(PrimitiveType.Cube, line.pad.transform, _padMat, new Vector3(0f, 1f, 0f),
                     new Vector3(benchSize * size, 2f, benchSize * size * 0.6f));
                line.padCollider = TapTarget(line.pad);
            }

            // The body itself follows whoever works the bench; see RefreshWorkers.
            line.workerAnchor = transform.Find("Shop_" + Names[p] + "_Worker");

            Transform r = routeRoot.transform;
            line.route = new Vector3[r.childCount];
            for (int i = 0; i < line.route.Length; i++)
            {
                line.route[i] = r.GetChild(i).position;
                if (i > 0) line.routeLength += Vector3.Distance(line.route[i - 1], line.route[i]);
            }
            return line;
        }

        private static string RoutePath(int p) => "MiningShop_Routes/Carry_" + Names[p] + "_to_Market";

        private void OnDestroy()
        {
            if (_market != null) _market.MiningShopBusinessSold -= OnSold;
            if (_shop != null) _shop.WorkersChanged -= RefreshWorkers;
            DestroyMaterial(_handleMat); DestroyMaterial(_headMat); DestroyMaterial(_helmetMat);
            DestroyMaterial(_lanternMat); DestroyMaterial(_bagMat); DestroyMaterial(_padMat);
            DestroyMaterial(_crateMat); DestroyMaterial(_badgeMat);
            if (_plinthMats != null) for (int r = 0; r < _plinthMats.Length; r++) DestroyMaterial(_plinthMats[r]);
        }

        /// <summary>
        /// Makes each bench's body the worker posted there: the apprentice is the plain body with no plinth, a master
        /// is his own body out of the people pack (picked the way <see cref="StationForemen"/> picks, so he is the same
        /// man on the island), a little bigger per rarity and standing on his rarity's disc. Runs on a posting change,
        /// never per frame; a body is only rebuilt when the man changes.
        /// </summary>
        private void RefreshWorkers()
        {
            for (int p = 0; p < _lines.Length; p++)
            {
                Line line = _lines[p];
                if (line == null || line.workerAnchor == null) continue;
                int who = _shop.WorkerAt(p);
                if (who == line.workerWho) continue;
                if (line.worker != null)
                {
                    // Off before it goes: Destroy waits for the end of the frame.
                    line.worker.body.gameObject.SetActive(false);
                    Destroy(line.worker.body.gameObject);
                }
                line.workerWho = who;
                line.worker = Person(_people, who < 0 ? 0 : who * 5, line.workerAnchor.position);
                line.worker.body.rotation = line.workerAnchor.rotation;
                if (who < 0 || _plinthMats == null) continue;

                int rank = (int)Foremen.RankOf(who);
                line.worker.body.localScale = Vector3.one * (1f + workerRankScaleStep * rank);
                var plinth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(plinth.GetComponent<Collider>());
                plinth.name = "Kaide";
                plinth.transform.SetParent(line.worker.body, false);
                // Just clear of the ground, so the disc does not z-fight the deck it stands on.
                plinth.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                plinth.transform.localScale = new Vector3(workerPlinthRadius * 2f, 0.5f, workerPlinthRadius * 2f);
                var mr = plinth.GetComponent<MeshRenderer>();
                mr.sharedMaterial = _plinthMats[Mathf.Clamp(rank, 0, _plinthMats.Length - 1)];
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }
        }

        private static void DestroyMaterial(Material m)
        {
            if (m != null) Destroy(m);
        }

        private void Update()
        {
            MiningShopBusinessSimulation.Snapshot v = _shop.View;
            for (int p = 0; p < _lines.Length; p++)
            {
                Line line = _lines[p];
                if (line == null) continue;
                MiningShopBusinessSimulation.ProductSnapshot product = v.ProductAt(p);
                bool offered = p < v.AvailableProductCount;
                bool built = offered && product.TableBuilt;
                line.table.SetActive(built);
                line.rack.SetActive(built);
                if (line.worker != null) line.worker.body.gameObject.SetActive(built);
                if (line.pad != null) line.pad.SetActive(offered && !built);

                if (line.pulse > 0f)
                {
                    line.pulse = Mathf.Max(0f, line.pulse - Time.deltaTime);
                    // One swell and settle: sin over half a turn, largest a third of the way in.
                    float t = 1f - line.pulse / starPulseSeconds;
                    float swell = Mathf.Sin(Mathf.Pow(t, 0.6f) * Mathf.PI) * starPulseScale;
                    line.work.localScale = line.workScale * (1f + swell);
                }

                line.craft.gameObject.SetActive(built && product.Crafting);
                float craft = product.Crafting ? 1f - Frac(product.CraftRemaining, product.CraftDuration) : 0f;
                line.craft.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, craft);
                // PickupReserved is still physically on the rack until loading completes.
                Show(line.rackItems, built ? product.OutputStock + product.PickupReserved : 0);
                Show(line.shelfItems, built ? product.ShelfStock : 0);
                Show(line.cargo, p == v.CarrierProductIndex ? v.CarrierCount : 0);
            }
            DrawCarrier(v);
            Reconcile(v);
            DrawContractor(v);
            WalkCustomers(Time.deltaTime);
        }

        /// <summary>
        /// The contract customer: a bigger body on a gold disc, holding a crate that fills as the goods arrive. Built
        /// once and hidden; <see cref="DrawContractor"/> walks him in and out.
        /// </summary>
        private void BuildContractor(GameObject[] people, Material source)
        {
            _crateMat = Tinted(source, contractCrateColor);
            _badgeMat = Tinted(source, contractBadgeColor);
            // The last of the pack: never one of the queue's faces, which start at the front of it.
            _contractor = Person(people, people.Length - 1, ContractSpot());
            _contractor.body.localScale = Vector3.one * contractScale;
            _contractor.held = new Transform[MiningShopCampaign.ProductCount];
            for (int p = 0; p < _contractor.held.Length; p++) _contractor.held[p] = Carried(p, _contractor.body, 0);

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(disc.GetComponent<Collider>());
            disc.name = "KontratDiski";
            disc.transform.SetParent(_contractor.body, false);
            disc.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            disc.transform.localScale = new Vector3(workerPlinthRadius * 2f, 0.5f, workerPlinthRadius * 2f);
            var mr = disc.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _badgeMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // Carried on the other side from the item being handed over, so the two never overlap.
            float crate = pickaxeLength * 0.7f;
            _crate = new GameObject("KontratSandigi").transform;
            _crate.SetParent(_contractor.body, false);
            _crate.localPosition = new Vector3(-personHeight * 0.26f, personHeight * 0.22f, personHeight * 0.08f);
            Part(PrimitiveType.Cube, _crate, _crateMat, new Vector3(0f, crate * 0.25f, 0f), new Vector3(crate, crate * 0.5f, crate * 0.7f));
            _crateItems = new Transform[MiningShopCampaign.ProductCount][];
            for (int p = 0; p < _crateItems.Length; p++)
            {
                _crateItems[p] = new Transform[stackShown];
                for (int i = 0; i < stackShown; i++)
                {
                    _crateItems[p][i] = Item(p, _crate, new Vector3(0f, crate * 0.5f + i * ItemHeight(p) * shelfItemScale, 0f));
                    _crateItems[p][i].localScale = Vector3.one * shelfItemScale;
                }
            }
            _contractor.body.gameObject.SetActive(false);
        }

        private Vector3 ContractSpot() => _contractAnchor != null ? _contractAnchor.position : Slot(0) + contractSpotOffset;

        /// <summary>
        /// Walks the contract customer in when a contract starts and out when it ends, finished or cancelled. On a
        /// launch that opens mid-contract he is simply standing there. While a bundle is being handed to him he holds
        /// it, and the crate shows how far the order has come.
        /// </summary>
        private void DrawContractor(in MiningShopBusinessSimulation.Snapshot v)
        {
            if (_contractor == null) return;
            bool active = v.ContractActive;
            if (active && !_contractorHere)
            {
                _contractorHere = true;
                _crateFull = false;
                _contractorProduct = v.ContractProductIndex;
                Walker w = _contractor;
                w.leaving = false;
                w.body.position = _contractorSnap ? ContractSpot() : _queue[_queue.Length - 1].position + customerEntryOffset;
                w.target = ContractSpot();
                w.face = _shelf.position - ContractSpot();
                w.body.gameObject.SetActive(true);
            }
            else if (!active && _contractorHere)
            {
                _contractorHere = false;
                _contractor.leaving = true;
                _contractor.target = ContractSpot() + customerExitOffset;
                if (_crateFull) _crateShown = stackShown;
            }
            _contractorSnap = false;

            int holding = active && v.ServingContract ? v.ContractProductIndex : -1;
            if (holding != _contractor.heldProduct) Hold(_contractor, holding);

            if (active && v.ContractQuantity > 0)
                _crateShown = Mathf.CeilToInt(stackShown * (float)v.ContractDelivered / v.ContractQuantity);
            ShowCrate(_contractorProduct, _crateShown);

            if (_receivePulse > 0f)
            {
                _receivePulse = Mathf.Max(0f, _receivePulse - Time.deltaTime);
                float t = 1f - _receivePulse / contractReceiveSeconds;
                _crate.localScale = Vector3.one * (1f + Mathf.Sin(t * Mathf.PI) * 0.25f);
            }
        }

        private void ShowCrate(int product, int count)
        {
            for (int p = 0; p < _crateItems.Length; p++)
                for (int i = 0; i < _crateItems[p].Length; i++)
                {
                    bool on = p == product && i < count;
                    if (_crateItems[p][i].gameObject.activeSelf != on) _crateItems[p][i].gameObject.SetActive(on);
                }
        }

        private void DrawCarrier(in MiningShopBusinessSimulation.Snapshot v)
        {
            // One carrier walks the route of whichever line it is serving; between jobs it waits where it last was.
            if (v.CarrierProductIndex >= 0 && v.CarrierProductIndex < _lines.Length && _lines[v.CarrierProductIndex] != null)
                _carrierLine = v.CarrierProductIndex;
            Line line = _lines[_carrierLine];
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
            Vector3 pos = OnRoute(line, t * line.routeLength, out Vector3 dir);
            if (v.Carrier == MiningShopSimulation.CarrierPhase.Returning) dir = -dir;
            _carrier.body.position = pos;
            if (walking && dir.sqrMagnitude > 1e-4f) _carrier.body.rotation = Quaternion.LookRotation(dir);
            if (_carrier.anim != null)
            {
                _carrier.anim.SetMoving(walking);
                // Feet under the body: stride rate follows the pace the simulation's clock sets.
                float pace = walking ? line.routeLength / Mathf.Max(0.1f, (float)v.CarrierDuration) : 0f;
                _carrier.anim.SetSpeed(walking ? pace / (personHeight * 0.8f) : 1f);
            }
        }

        /// <summary>
        /// Keeps one body per waiting customer (all lines together, one queue) and one for the customer being
        /// served, holding the product actually being sold. The sale receipt (OnSold) sends the served one away.
        /// </summary>
        private void Reconcile(in MiningShopBusinessSimulation.Snapshot v)
        {
            // A hand-over to the contract customer is his, not the queue's: nobody steps up to the counter for it.
            bool sale = v.Serving && !v.ServingContract;
            if (sale && _serving < 0)
            {
                _serving = _waitingCount > 0 ? PopFront() : Spawn();
                if (_serving >= 0)
                {
                    Walker w = _customers[_serving];
                    w.target = Slot(0);
                    w.face = _shelf.position - Slot(0);
                    Hold(w, v.ServiceProductIndex);
                }
            }
            else if (!sale && _serving >= 0)
            {
                Hide(_customers[_serving]);
                _serving = -1;
            }

            int waiting = v.WaitingCustomerCount;
            while (_waitingCount < waiting)
            {
                int c = Spawn();
                if (c < 0) break;
                _waiting[_waitingCount++] = c;
            }
            while (_waitingCount > waiting) Hide(_customers[_waiting[--_waitingCount]]);

            for (int i = 0; i < _waitingCount; i++)
            {
                Walker w = _customers[_waiting[i]];
                w.target = Slot(i + 1);
                w.face = Slot(i) - Slot(i + 1);
            }
        }

        private void OnSold(MiningShopBusinessSimulation.Sale sale)
        {
            if (sale.Contract)
            {
                // The contract customer keeps his place until the order is complete; the crate bounces instead.
                _receivePulse = contractReceiveSeconds;
                if (sale.CompletesContract) _crateFull = true;
                return;
            }
            if (_serving < 0) return;
            Walker w = _customers[_serving];
            w.leaving = true;
            w.target = Slot(0) + customerExitOffset;
            _serving = -1;
        }

        private void WalkCustomers(float dt)
        {
            for (int i = 0; i < _customers.Length; i++) Walk(_customers[i], dt);
            if (_contractor != null) Walk(_contractor, dt);
        }

        private void Walk(Walker w, float dt)
        {
            if (!w.body.gameObject.activeSelf) return;
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
                return;
            }
            else if (w.face.sqrMagnitude > 1e-4f)
            {
                w.face.y = 0f;
                w.body.rotation = Quaternion.LookRotation(w.face);
            }
            w.anim?.SetMoving(moving);
        }

        private int Spawn()
        {
            for (int i = 0; i < _customers.Length; i++)
            {
                Walker w = _customers[i];
                if (w.body.gameObject.activeSelf) continue;
                w.leaving = false;
                Hold(w, -1);
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

        private static void Hold(Walker w, int product)
        {
            if (w.heldProduct >= 0) w.held[w.heldProduct].gameObject.SetActive(false);
            w.heldProduct = product >= 0 && product < w.held.Length ? product : -1;
            if (w.heldProduct >= 0) w.held[w.heldProduct].gameObject.SetActive(true);
        }

        private static void Hide(Walker w)
        {
            w.leaving = false;
            Hold(w, -1);
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

        private static Vector3 OnRoute(Line line, float distance, out Vector3 dir)
        {
            Vector3[] route = line.route;
            for (int i = 1; i < route.Length; i++)
            {
                Vector3 seg = route[i] - route[i - 1];
                float len = seg.magnitude;
                dir = len > 1e-4f ? seg / len : Vector3.forward;
                if (distance <= len || i == route.Length - 1)
                    return route[i - 1] + dir * Mathf.Clamp(distance, 0f, len);
                distance -= len;
            }
            dir = Vector3.forward;
            return route[0];
        }

        private static float Frac(double remaining, double duration)
            => duration > 0d ? Mathf.Clamp01((float)(remaining / duration)) : 0f;

        private static void Show(Transform[] items, int count)
        {
            for (int i = 0; i < items.Length; i++) items[i].gameObject.SetActive(i < count);
        }

        private float ItemHeight(int p) => p == 0 ? pickaxeLength * 0.16f : pickaxeLength * 0.5f;

        /// <summary>
        /// An item carried at the walker's side. The pickaxe goes over the shoulder, head up and tilted out (lying flat
        /// it stuck out towards the steep shop camera like a pole); the others are held upright.
        /// </summary>
        private Transform Carried(int p, Transform walker, int i)
        {
            Transform item = Item(p, walker, new Vector3(personHeight * 0.22f + i * pickaxeLength * 0.2f, personHeight * 0.3f, 0f));
            item.localRotation = p == 0 ? Quaternion.Euler(0f, 0f, -30f) : Quaternion.identity;
            item.localScale = Vector3.one * carryScale;
            return item;
        }

        /// <summary>One product item built from primitives, hidden until something shows it.</summary>
        private Transform Item(int p, Transform parent, Vector3 localPosition)
        {
            var root = new GameObject(Names[p]).transform;
            root.SetParent(parent, false);
            root.localPosition = localPosition;
            float l = pickaxeLength;
            switch (p)
            {
                case 0:
                    // Lying flat on benches and racks; Carried stands it up.
                    root.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    Part(PrimitiveType.Cylinder, root, _handleMat, new Vector3(0f, l * 0.5f, 0f), new Vector3(l * 0.18f, l * 0.5f, l * 0.18f));
                    Part(PrimitiveType.Cube, root, _headMat, new Vector3(0f, l, 0f), new Vector3(l * 0.9f, l * 0.24f, l * 0.26f));
                    break;
                case 1:
                    Part(PrimitiveType.Sphere, root, _helmetMat, new Vector3(0f, l * 0.2f, 0f), new Vector3(l * 0.6f, l * 0.4f, l * 0.6f));
                    Part(PrimitiveType.Cylinder, root, _helmetMat, new Vector3(0f, l * 0.05f, 0f), new Vector3(l * 0.8f, l * 0.03f, l * 0.8f));
                    break;
                case 2:
                    Part(PrimitiveType.Cylinder, root, _lanternMat, new Vector3(0f, l * 0.22f, 0f), new Vector3(l * 0.3f, l * 0.22f, l * 0.3f));
                    Part(PrimitiveType.Cube, root, _headMat, new Vector3(0f, l * 0.48f, 0f), new Vector3(l * 0.38f, l * 0.08f, l * 0.38f));
                    break;
                default:
                    Part(PrimitiveType.Cube, root, _bagMat, new Vector3(0f, l * 0.22f, 0f), new Vector3(l * 0.55f, l * 0.44f, l * 0.3f));
                    Part(PrimitiveType.Cube, root, _handleMat, new Vector3(0f, l * 0.5f, 0f), new Vector3(l * 0.4f, l * 0.06f, l * 0.08f));
                    break;
            }
            root.gameObject.SetActive(false);
            return root;
        }

        /// <summary>A plain work table, <paramref name="size"/> wide, standing on the anchor.</summary>
        private GameObject Table(Transform at, float size)
        {
            var root = new GameObject("Table");
            root.transform.SetParent(at, false);
            float w = size, d = size * 0.6f, h = size * 0.45f, top = size * 0.1f, leg = size * 0.1f;
            Part(PrimitiveType.Cube, root.transform, _handleMat, new Vector3(0f, h - top * 0.5f, 0f), new Vector3(w, top, d));
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Part(PrimitiveType.Cube, root.transform, _handleMat,
                         new Vector3(x * (w - leg) * 0.5f, (h - top) * 0.5f, z * (d - leg) * 0.5f),
                         new Vector3(leg, h - top, leg));
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
            box.size = new Vector3(b.size.x / scale.x, Mathf.Max(b.size.y, 10f) / scale.y, b.size.z / scale.z) * 1.6f;
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
