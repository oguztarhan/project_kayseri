using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// The art the market yard spawns: bodies, bars and banknotes. Wired in the Inspector on the
    /// market scene's boot object, so swapping the miner for a real model is a drag rather than an edit.
    ///
    /// Every field may be left empty. An empty field falls back to the primitive the greybox has been
    /// using all along, which is what keeps the yard runnable while the art is still being made — and
    /// it means a half-wired scene degrades to grey capsules instead of throwing.
    ///
    /// The one rule for a replacement: it should stand at its own origin with its feet on y=0 and face
    /// +Z. Everything here positions bodies by their base and turns them with LookRotation, so a model
    /// authored around its middle will sink and a model facing -Z will moonwalk.
    /// </summary>
    [System.Serializable]
    public sealed class MarketPrefabs
    {
        [Tooltip("Oyuncunun gövdesi. Sıradaki hiç kimseyle ve elemanlarla aynı olmamalı — oyuncunun " +
                 "kalabalıkta bir bakışta bulunabilmesi gerek. Boşsa gri kapsül.")]
        [SerializeField] private GameObject player;

        [Tooltip("Sıradaki müşteriler. Birden fazla koy: her müşteri sırayla birini alır, " +
                 "böylece kuyruk aynı kişinin kopyalarından oluşmaz. Boşsa mavi kapsül.")]
        [SerializeField] private GameObject[] customers;

        [Tooltip("Tuttuğun elemanlar. Bunlar da müşterilerden ayrı bir tip olmalı — kimin çalıştığı " +
                 "kimin sırada beklediği karışmasın. Boşsa yeşil kapsül.")]
        [SerializeField] private GameObject worker;

        [Tooltip("Tek külçe: sırtta, tezgâhta ve elemanın omzunda aynısı. Boşsa küçük kutu.")]
        [SerializeField] private GameObject bar;

        [Tooltip("Yerdeki para destesi. Boşsa yeşil kutu.")]
        [SerializeField] private GameObject cash;

        [Tooltip("Bütün insanlara uygulanan boy çarpanı. Paketteki modeller gerçek insan boyunda ve " +
                 "avlu onlara göre çok geniş — bir telefonda kim olduklarını görebilmek için büyütülüyorlar.\n\n" +
                 "Oyuncunun çarpışma kapsülü de bununla ölçekleniyor, yoksa gövde büyür ama duvara " +
                 "çarptığı yer eskisi gibi kalır.")]
        [SerializeField, Min(0.2f)] private float personScale = 1.75f;

        public GameObject Player => player;
        public GameObject Worker => worker;
        public float PersonScale => personScale;

        /// <summary>
        /// One of the customer bodies, chosen by a counter rather than at random.
        ///
        /// Deterministic on purpose: a queue built from <c>Random</c> occasionally deals four of the
        /// same person in a row, which reads as a bug even though it is not, and it makes a layout
        /// problem impossible to reproduce twice. Walking the list gives an even spread every run.
        /// </summary>
        public GameObject CustomerAt(int index)
        {
            if (customers == null || customers.Length == 0) return null;
            GameObject pick = customers[((index % customers.Length) + customers.Length) % customers.Length];
            return pick;
        }
        public GameObject Bar => bar;
        public GameObject Cash => cash;

        /// <summary>
        /// Instantiates <paramref name="prefab"/> under <paramref name="parent"/>, or builds a primitive
        /// of <paramref name="fallback"/> shape at <paramref name="fallbackScale"/> when none is wired.
        ///
        /// The collider always goes. Every body in this yard is moved by hand — the player has the only
        /// CharacterController, and customers and staff walk waypoints — so an authored prefab arriving
        /// with a collider would start shoving things around the moment it spawned.
        /// </summary>
        public static Transform Spawn(GameObject prefab, Transform parent, string name,
                                      PrimitiveType fallback, Vector3 fallbackScale, Material fallbackMaterial)
        {
            GameObject go;
            if (prefab != null)
            {
                go = Object.Instantiate(prefab, parent, false);
            }
            else
            {
                go = GameObject.CreatePrimitive(fallback);
                go.transform.SetParent(parent, false);
                go.transform.localScale = fallbackScale;
                if (fallbackMaterial != null)
                {
                    var renderer = go.GetComponent<MeshRenderer>();
                    if (renderer != null) renderer.sharedMaterial = fallbackMaterial;
                }
            }
            go.name = name;
            StripColliders(go);
            return go.transform;
        }

        /// <summary>
        /// Spawns a person and sizes them. The scale goes on the instance rather than the prefab so the
        /// pack's own assets are never touched — the yard is the thing with an opinion about how big a
        /// person should look, not the art.
        /// </summary>
        public Transform SpawnPerson(GameObject prefab, Transform parent, string name,
                                     Vector3 fallbackScale, Material fallbackMaterial)
        {
            Transform body = Spawn(prefab, parent, name, PrimitiveType.Capsule,
                                   fallbackScale * personScale, fallbackMaterial);
            if (prefab != null) body.localScale = body.localScale * personScale;
            return body;
        }

        /// <summary>
        /// Spawns a piece of cargo — a bar, or a bundle of notes — and paints it whatever this yard is
        /// selling.
        ///
        /// One line different from <see cref="Spawn"/>, and that line is the reason it exists: the
        /// material goes on EVEN WHEN a prefab is wired. People keep their own skins, deliberately — a
        /// customer arrives dressed and should stay dressed. Cargo cannot: one bar model serves eight
        /// islands and the whole point of it is to be the colour of the ore on the player's back. Wired
        /// through <see cref="Spawn"/> it kept its authored grey, which is a worse bug than the plain
        /// box it replaced, because a grey bar in a copper yard looks like it belongs there.
        ///
        /// The renderer is found in the CHILDREN, not on the root. An imported FBX arrives as a root
        /// with the mesh hung under it, so a look on the root alone finds nothing and silently does not
        /// paint — the exact failure this method is here to stop.
        /// </summary>
        public static Transform SpawnCargo(GameObject prefab, Transform parent, string name,
                                           Vector3 fallbackScale, Material material)
        {
            Transform body = Spawn(prefab, parent, name, PrimitiveType.Cube, fallbackScale, material);
            Skin(body, material);
            return body;
        }

        /// <summary>Puts one material on every renderer under a transform. See <see cref="SpawnCargo"/>.</summary>
        public static void Skin(Transform body, Material material)
        {
            if (body == null || material == null) return;
            var parts = body.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < parts.Length; i++) parts[i].sharedMaterial = material;
        }

        private static void StripColliders(GameObject go)
        {
            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) Object.Destroy(colliders[i]);
        }
    }
}
